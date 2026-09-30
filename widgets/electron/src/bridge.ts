import {NevaBridgeApiError} from "@nevabridge/sdk";
import type {
  NevaBridgeConversations,
  PickedFile,
  Reporter,
} from "./conversations.js";
import {isSecureAddress, originOf} from "./secure-address.js";

const MAX_MESSAGE_LENGTH = 32768;
const REPORT_CATEGORIES = new Set([
  "bug_report",
  "feature_request",
  "support_request",
]);
const RETRYABLE_STATUSES = new Set([408, 424, 429, 502, 504]);
const FILE_ERROR_CODES = new Set([
  "ENOENT",
  "EACCES",
  "EPERM",
  "EISDIR",
  "EBUSY",
]);

export interface WebChatBridgeOptions {
  readonly conversations: NevaBridgeConversations;
  readonly productId: string;
  readonly reporter: Reporter;
  readonly applicationContext?: Readonly<Record<string, string>>;
  /** Where the page is loaded from. Only messages from this origin are answered. */
  readonly pageUrl: string;
  /** Shows the operating system's file picker. Without it, the page cannot attach files. */
  readonly pickFile?: () => Promise<PickedFile | null>;
}

export interface WebChatBridge {
  /**
   * Handles one message from the page. Resolves to the response to post back, or null when the
   * message did not come from the page's origin and must be dropped.
   */
  handle(sourceUrl: string, message: string): Promise<string | null>;
}

type RequestId = string | number;

interface ProtocolError {
  readonly name: string;
  readonly message: string;
  readonly status: number | null;
  readonly retryable: boolean;
}

/**
 * Answers the NevaBridge web chat page (protocol version 1). The page only renders; every API call
 * runs here, so the API key never reaches it. The page may come from a server outside the host's
 * control, so it is treated as untrusted:
 *
 * - it must be served over https (plain http only on this machine);
 * - messages from any origin but the page's are ignored;
 * - only the chat operations exist, never arbitrary requests;
 * - the product, reporter and application context come from the host, never the page;
 * - follow-ups, submissions and attachments are accepted only for conversations the page started.
 */
export function createWebChatBridge(
  options: WebChatBridgeOptions,
): WebChatBridge {
  // A page fetched over plain http can be replaced in transit while keeping its origin.
  if (!isSecureAddress(options.pageUrl)) {
    throw new Error(
      `The web chat page must be served over https (plain http only on this machine), not '${options.pageUrl}'.`,
    );
  }
  const allowedOrigin = originOf(options.pageUrl);
  const startedConversations = new Set<string>();

  const success = (id: RequestId, result: unknown): string =>
    JSON.stringify({id, ok: true, result: result ?? null});
  const failure = (id: RequestId, error: ProtocolError): string =>
    JSON.stringify({id, ok: false, error});
  const invalid = (id: RequestId, message: string): string =>
    failure(id, {
      name: "InvalidRequest",
      message,
      status: null,
      retryable: false,
    });
  const unknownConversation = (id: RequestId): string =>
    failure(id, {
      name: "UnknownConversation",
      message: "This chat did not start that conversation.",
      status: null,
      retryable: false,
    });

  const readText = (request: Record<string, unknown>): string | null => {
    const text =
      typeof request["text"] === "string" ? request["text"].trim() : "";
    return text.length > 0 && text.length <= MAX_MESSAGE_LENGTH ? text : null;
  };
  const readStartedConversation = (
    request: Record<string, unknown>,
  ): string | null => {
    const conversationId = request["conversationId"];
    return typeof conversationId === "string" &&
      startedConversations.has(conversationId)
      ? conversationId
      : null;
  };

  async function answer(
    id: RequestId,
    request: Record<string, unknown>,
  ): Promise<string> {
    switch (request["type"]) {
      case "startConversation": {
        const text = readText(request);
        if (text === null) {
          return invalid(
            id,
            `text must contain 1 to ${MAX_MESSAGE_LENGTH} characters.`,
          );
        }
        const category = request["category"];
        if (
          category !== undefined &&
          (typeof category !== "string" || !REPORT_CATEGORIES.has(category))
        ) {
          return invalid(
            id,
            `category must be one of: ${[...REPORT_CATEGORIES].join(", ")}.`,
          );
        }
        const turn = await options.conversations.startConversation(
          options.productId,
          options.reporter,
          {
            text,
            category,
            applicationContext: options.applicationContext,
          },
        );
        startedConversations.add(turn.assistantMessage.conversationId);
        return success(id, turn);
      }
      case "appendMessage": {
        const conversationId = readStartedConversation(request);
        if (conversationId === null) {
          return unknownConversation(id);
        }
        const text = readText(request);
        if (text === null) {
          return invalid(
            id,
            `text must contain 1 to ${MAX_MESSAGE_LENGTH} characters.`,
          );
        }
        return success(
          id,
          await options.conversations.appendMessage(conversationId, text),
        );
      }
      case "submitReport": {
        const conversationId = readStartedConversation(request);
        if (conversationId === null) {
          return unknownConversation(id);
        }
        const reportId = request["reportId"];
        if (typeof reportId !== "string" || reportId.length === 0) {
          return invalid(id, "reportId is required.");
        }
        return success(
          id,
          await options.conversations.submitReport(conversationId, reportId),
        );
      }
      case "getFeatures": {
        const features = await options.conversations.getFeatures();
        // Without a file picker this host cannot attach files, whatever the tenant may do.
        return success(
          id,
          options.pickFile === undefined
            ? {...features, chatAttachments: false}
            : features,
        );
      }
      case "attachFile": {
        const conversationId = readStartedConversation(request);
        if (conversationId === null) {
          return unknownConversation(id);
        }
        if (options.pickFile === undefined) {
          return failure(id, {
            name: "UnsupportedOperation",
            message: "This application does not offer file attachments.",
            status: null,
            retryable: false,
          });
        }
        const file = await options.pickFile();
        if (file === null) {
          return success(id, null);
        }
        return success(
          id,
          await options.conversations.attachFile(
            conversationId,
            options.reporter.id,
            file,
          ),
        );
      }
      default:
        return failure(id, {
          name: "UnsupportedOperation",
          message:
            "The chat page asked for an operation the host does not offer.",
          status: null,
          retryable: false,
        });
    }
  }

  return {
    async handle(sourceUrl, message) {
      let sourceOrigin: string;
      try {
        sourceOrigin = originOf(sourceUrl);
      } catch {
        return null;
      }
      if (sourceOrigin !== allowedOrigin) {
        return null;
      }

      let request: Record<string, unknown> | null = null;
      try {
        const parsed: unknown = JSON.parse(message);
        request =
          parsed !== null &&
          typeof parsed === "object" &&
          !Array.isArray(parsed)
            ? (parsed as Record<string, unknown>)
            : null;
      } catch {
        request = null;
      }
      const id =
        typeof request?.["id"] === "string" ||
        (typeof request?.["id"] === "number" && Number.isInteger(request["id"]))
          ? request["id"]
          : 0;
      if (request === null) {
        return invalid(id, "The message is not a JSON object.");
      }

      try {
        return await answer(id, request);
      } catch (error: unknown) {
        return failure(id, toProtocolError(error));
      }
    },
  };
}

function toProtocolError(error: unknown): ProtocolError {
  if (error instanceof NevaBridgeApiError) {
    const detail = (error.detail ?? {}) as {error?: unknown; message?: unknown};
    const name =
      typeof detail.error === "string" ? detail.error : "GatewayError";
    const message =
      typeof detail.message === "string"
        ? detail.message
        : `NevaBridge answered HTTP ${error.status}.`;
    const retryable =
      RETRYABLE_STATUSES.has(error.status) ||
      (error.status === 409 && name !== "ModelInvocationFailed");
    return {name, message, status: error.status, retryable};
  }
  if (error instanceof SyntaxError) {
    return {
      name: "InvalidResponse",
      message:
        "NevaBridge sent an answer this version of the chat cannot read.",
      status: null,
      retryable: false,
    };
  }
  if (
    FILE_ERROR_CODES.has((error as {code?: unknown} | null)?.code as string)
  ) {
    return {
      name: "FileUnreadable",
      message: "The chosen file could not be read.",
      status: null,
      retryable: false,
    };
  }
  return {
    name: "NetworkError",
    message: "NevaBridge could not be reached.",
    status: null,
    retryable: true,
  };
}
