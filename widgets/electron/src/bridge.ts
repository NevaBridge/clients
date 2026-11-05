import {NevaBridgeApiError, type JsonValue} from "@nevabridge/sdk";
import {
  UploadFailedError,
  type NevaBridgeConversations,
  type PickedFile,
  type Reporter,
  type StartConversationInput,
} from "./conversations.js";
import {isSecureAddress, originOf} from "./secure-address.js";

const MAX_MESSAGE_LENGTH = 32768;
const MAX_REQUEST_ID_LENGTH = 64;
const MAX_CURSOR_LENGTH = 1024;
const REPORT_CATEGORIES: ReadonlySet<string> = new Set([
  "bug_report",
  "feature_request",
  "support_request",
]);
const RETRYABLE_MODEL_FAILURES: ReadonlySet<string> = new Set([
  "throttled",
  "transient-unavailable",
  "timed-out",
  "output-unparseable",
  "unknown",
]);
const FILE_ERROR_CODES: ReadonlySet<string> = new Set([
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
   * message must be dropped: it did not come from the page's origin, or it carries no request id.
   */
  handle(sourceUrl: string, message: string): Promise<string | null>;
}

// The deployed page sends string ids; the prototype page shipped with this package sends numbers.
type RequestId = string | number;
type Request = Readonly<Record<string, unknown>>;

interface ProtocolError {
  readonly name: string;
  readonly message: string;
  readonly status: number | null;
  readonly retryable: boolean;
  readonly turnId?: string;
}

type Outcome =
  | {readonly ok: true; readonly result: JsonValue}
  | {readonly ok: false; readonly error: ProtocolError};

/** The request is not a valid request of protocol major 1. */
class UnsupportedRequest extends Error {}

const success = (result: JsonValue): Outcome => ({ok: true, result});
const failure = (error: ProtocolError): Outcome => ({ok: false, error});
const unknownConversation = (): Outcome =>
  failure({
    name: "UnknownConversation",
    message: "This conversation was not started in this chat.",
    status: null,
    retryable: false,
  });

function isRecord(value: unknown): value is Readonly<Record<string, unknown>> {
  return typeof value === "object" && value !== null && !Array.isArray(value);
}

function stringField(value: unknown, field: string): string | null {
  return isRecord(value) && typeof value[field] === "string"
    ? value[field]
    : null;
}

/**
 * Answers the NevaBridge web chat page (bridge protocol version 1). The page only renders; every
 * API call runs here, so the API key never reaches it. The page may come from a server outside
 * the host's control, so it is treated as untrusted:
 *
 * - it must be served over https (plain http only on this machine);
 * - messages from any origin but the page's are ignored;
 * - only the chat operations exist, never arbitrary requests;
 * - the product, reporter and application context come from the host, never the page;
 * - conversations and Generic Intake turns are reachable only when this bridge returned them to
 *   the page in this session.
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
  const startedTurns = new Set<string>();

  const rememberConversation = (conversationId: string | null): void => {
    if (conversationId !== null) {
      startedConversations.add(conversationId);
    }
  };
  const rememberTurn = (turnId: string | null): void => {
    if (turnId !== null) {
      startedTurns.add(turnId);
    }
  };

  const text = (request: Request): string => {
    const value =
      typeof request["text"] === "string" ? request["text"].trim() : "";
    if (value.length === 0 || value.length > MAX_MESSAGE_LENGTH) {
      throw new UnsupportedRequest(
        `text must contain 1 to ${MAX_MESSAGE_LENGTH} characters.`,
      );
    }
    return value;
  };
  const requiredString = (request: Request, field: string): string => {
    const value = request[field];
    if (typeof value !== "string" || value.length === 0) {
      throw new UnsupportedRequest(`${field} is required.`);
    }
    return value;
  };
  const startedConversation = (request: Request): string | null => {
    const conversationId = requiredString(request, "conversationId");
    return startedConversations.has(conversationId) ? conversationId : null;
  };

  async function answer(request: Request): Promise<Outcome> {
    switch (request["type"]) {
      case "startConversation": {
        const message = text(request);
        const category = request["category"];
        if (
          category !== undefined &&
          (typeof category !== "string" || !REPORT_CATEGORIES.has(category))
        ) {
          throw new UnsupportedRequest(
            `category must be one of: ${[...REPORT_CATEGORIES].join(", ")}.`,
          );
        }
        const turn = await options.conversations.startConversation(
          options.productId,
          options.reporter,
          {
            text: message,
            category: category as StartConversationInput["category"],
            applicationContext: options.applicationContext,
          },
        );
        rememberConversation(
          stringField(
            isRecord(turn) ? turn["assistantMessage"] : null,
            "conversationId",
          ) ??
            stringField(
              isRecord(turn) ? turn["userMessage"] : null,
              "conversationId",
            ),
        );
        return success(turn);
      }
      case "appendMessage": {
        const conversationId = startedConversation(request);
        const message = text(request);
        if (conversationId === null) {
          return unknownConversation();
        }
        return success(
          await options.conversations.appendMessage(conversationId, message),
        );
      }
      case "submitReport": {
        const conversationId = startedConversation(request);
        const reportId = requiredString(request, "reportId");
        if (conversationId === null) {
          return unknownConversation();
        }
        return success(
          await options.conversations.submitReport(conversationId, reportId),
        );
      }
      case "getFeatures": {
        const features = await options.conversations.getFeatures();
        // Without a file picker this host cannot attach files, whatever the tenant may do.
        return success(
          options.pickFile === undefined && isRecord(features)
            ? {
                ...(features as {[key: string]: JsonValue}),
                chatAttachments: false,
              }
            : features,
        );
      }
      case "attachFile": {
        const conversationId = startedConversation(request);
        if (conversationId === null) {
          return unknownConversation();
        }
        if (options.pickFile === undefined) {
          throw new UnsupportedRequest(
            "This application does not offer file attachments.",
          );
        }
        const file = await options.pickFile();
        if (file === null) {
          return failure({
            name: "Cancelled",
            message: "No file was chosen.",
            status: null,
            retryable: false,
          });
        }
        return success(
          await options.conversations.attachFile(
            conversationId,
            options.reporter.id,
            file,
          ),
        );
      }
      case "startIntakeConversation": {
        const started = await options.conversations.startIntakeConversation(
          options.productId,
          options.reporter,
          {text: text(request), applicationContext: options.applicationContext},
        );
        rememberConversation(stringField(started, "conversationId"));
        rememberTurn(stringField(started, "turnId"));
        return success(started);
      }
      case "startIntakeTurn": {
        const conversationId = startedConversation(request);
        const message = text(request);
        const expectedRevision = request["expectedRevision"];
        const finish = request["finish"];
        if (
          typeof expectedRevision !== "number" ||
          !Number.isInteger(expectedRevision) ||
          expectedRevision < 0
        ) {
          throw new UnsupportedRequest(
            "expectedRevision must be a whole number from 0.",
          );
        }
        if (finish !== undefined && typeof finish !== "boolean") {
          throw new UnsupportedRequest("finish must be true or false.");
        }
        if (conversationId === null) {
          return unknownConversation();
        }
        const started = await options.conversations.startIntakeTurn(
          conversationId,
          {text: message, expectedRevision, finish},
        );
        rememberTurn(stringField(started, "turnId"));
        return success(started);
      }
      case "getIntakeTurnEvents": {
        const turnId = requiredString(request, "turnId");
        const after = request["after"];
        if (
          after !== undefined &&
          (typeof after !== "string" ||
            after.length === 0 ||
            after.length > MAX_CURSOR_LENGTH)
        ) {
          throw new UnsupportedRequest(
            `after must contain 1 to ${MAX_CURSOR_LENGTH} characters.`,
          );
        }
        if (!startedTurns.has(turnId)) {
          return failure({
            name: "UnknownTurn",
            message: "This turn was not started in this chat.",
            status: null,
            retryable: false,
          });
        }
        return success(
          await options.conversations.getIntakeTurnEvents(turnId, after),
        );
      }
      case "getConversation": {
        const conversationId = startedConversation(request);
        if (conversationId === null) {
          return unknownConversation();
        }
        const detail =
          await options.conversations.getConversation(conversationId);
        rememberTurn(stringField(detail, "activeTurnId"));
        return success(detail);
      }
      default:
        throw new UnsupportedRequest("This request type is not supported.");
    }
  }

  function toProtocolError(error: unknown): ProtocolError {
    if (error instanceof UnsupportedRequest) {
      return {
        name: "UnsupportedRequest",
        message: error.message,
        status: null,
        retryable: false,
      };
    }
    if (error instanceof UploadFailedError) {
      return {
        name: "UploadFailed",
        message: error.message,
        status: error.status,
        retryable: true,
      };
    }
    if (error instanceof NevaBridgeApiError) {
      const name = stringField(error.detail, "error");
      const message = stringField(error.detail, "message");
      if (name === null) {
        // The gateway in front of NevaBridge answered, typically after its time limit.
        return {
          name: "EdgeFailure",
          message: message ?? `NevaBridge answered HTTP ${error.status}.`,
          status: error.status,
          retryable: true,
        };
      }
      const category = stringField(error.detail, "category");
      const retryable =
        error.status === 408 ||
        error.status === 429 ||
        (name === "ModelInvocationFailed" &&
          category !== null &&
          RETRYABLE_MODEL_FAILURES.has(category));
      const turnId =
        error.status === 409 && name === "IntakeTurnRunning"
          ? stringField(error.detail, "turnId")
          : null;
      if (turnId !== null) {
        rememberTurn(turnId);
      }
      return {
        name,
        message: message ?? name,
        status: error.status,
        retryable,
        ...(turnId === null ? {} : {turnId}),
      };
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
    const code = isRecord(error) ? error["code"] : undefined;
    if (typeof code === "string" && FILE_ERROR_CODES.has(code)) {
      return {
        name: "FileUnreadable",
        message: "The chosen file could not be read.",
        status: null,
        retryable: false,
      };
    }
    return {
      name: "NetworkFailure",
      message: "NevaBridge could not be reached.",
      status: null,
      retryable: true,
    };
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

      let request: unknown;
      try {
        request = JSON.parse(message);
      } catch {
        return null;
      }
      if (!isRecord(request)) {
        return null;
      }
      const id = request["id"];
      if (!(
        (typeof id === "string" &&
          id.length > 0 &&
          id.length <= MAX_REQUEST_ID_LENGTH) ||
        (typeof id === "number" && Number.isInteger(id))
      )) {
        return null;
      }

      let outcome: Outcome;
      try {
        outcome = await answer(request);
      } catch (error: unknown) {
        outcome = failure(toProtocolError(error));
      }
      return JSON.stringify({id, ...outcome});
    },
  };
}
