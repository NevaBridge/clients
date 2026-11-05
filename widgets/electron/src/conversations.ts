import {
  NevaBridgeApiError,
  NevaBridgeClient,
  type FetchFunction,
  type JsonValue,
} from "@nevabridge/sdk";
import type {NevaBridgeSettings} from "./settings.js";

/** Seconds a turn-events read waits for a first new event; well inside the page's limit. */
const POLL_WAIT_SECONDS = 20;

/**
 * Who is reporting. `id` becomes the conversation's reporterId. `roles` (anonymous, customer,
 * tenant) decide which knowledge-base documents the assistant may draw on.
 */
export interface Reporter {
  readonly id: string;
  readonly displayName?: string;
  readonly roles?: ReadonlyArray<"anonymous" | "customer" | "tenant">;
}

export interface StartConversationInput {
  readonly text: string;
  /** bug_report, feature_request or support_request. Omitted means support_request. */
  readonly category?: "bug_report" | "feature_request" | "support_request";
  /** The application's own context, stored with the report and shown in triage. */
  readonly applicationContext?: Readonly<Record<string, string>>;
}

export interface StartIntakeConversationInput {
  readonly text: string;
  /** The application's own context, stored with the conversation and shown in triage. */
  readonly applicationContext?: Readonly<Record<string, string>>;
}

export interface StartIntakeTurnInput {
  readonly text: string;
  /** The conversation's revision the page last read; a stale one is refused. */
  readonly expectedRevision: number;
  /** Ends the conversation with this message. */
  readonly finish?: boolean;
}

/** A file the user chose to attach, read only when it is uploaded. */
export interface PickedFile {
  readonly name: string;
  readonly sizeBytes: number;
  read(): Promise<Blob>;
}

/**
 * The upload to the presigned address failed, so the attachment was not confirmed. `status` is
 * the upload's HTTP status, or null when no response arrived.
 */
export class UploadFailedError extends Error {
  public constructor(
    public readonly status: number | null,
    options?: {cause?: unknown},
  ) {
    super(
      status === null
        ? "The file could not be uploaded."
        : `The file could not be uploaded (HTTP ${status}).`,
      options,
    );
    this.name = "UploadFailedError";
  }
}

/**
 * The NevaBridge operations the web chat needs. Each resolves to the API's JSON for the operation,
 * unchanged, because the bridge passes it to the page as it is. Fail with the SDK's
 * `NevaBridgeApiError` when NevaBridge answers with an error, and with `UploadFailedError` when
 * the upload of an attachment fails; any other failure reaches the page as NevaBridge being
 * unreachable.
 *
 * `createSdkConversations` calls NevaBridge directly with the API key; an implementation that
 * calls your own backend can replace it.
 */
export interface NevaBridgeConversations {
  startConversation(
    productId: string,
    reporter: Reporter,
    input: StartConversationInput,
  ): Promise<JsonValue>;
  appendMessage(conversationId: string, text: string): Promise<JsonValue>;
  /**
   * Finalizes a report. Call it when the user says they are done; do not add an inactivity timer,
   * NevaBridge finalizes an abandoned conversation itself.
   */
  submitReport(conversationId: string, reportId: string): Promise<JsonValue>;
  /** The tenant's effective features, such as chatAttachments. Features not reported are off. */
  getFeatures(): Promise<JsonValue>;
  /** Requests an upload address, uploads the bytes there, and confirms the upload. */
  attachFile(
    conversationId: string,
    uploaderId: string,
    file: PickedFile,
  ): Promise<JsonValue>;
  /** Starts a Generic Intake conversation; its first turn runs in the background. */
  startIntakeConversation(
    productId: string,
    reporter: Reporter,
    input: StartIntakeConversationInput,
  ): Promise<JsonValue>;
  /** Starts the next turn of a Generic Intake conversation. */
  startIntakeTurn(
    conversationId: string,
    input: StartIntakeTurnInput,
  ): Promise<JsonValue>;
  /** Reads a turn's events after the cursor `after`, or from its first event. */
  getIntakeTurnEvents(turnId: string, after?: string): Promise<JsonValue>;
  getConversation(conversationId: string): Promise<JsonValue>;
}

/**
 * Calls NevaBridge directly with the API key through `@nevabridge/sdk`. Whoever runs this code can
 * read the key; see the package guide before using it in an application whose users must not.
 */
export function createSdkConversations(
  settings: NevaBridgeSettings,
  options: {readonly fetch?: FetchFunction} = {},
): NevaBridgeConversations {
  const fetchFn = options.fetch ?? fetch;
  const apiKey = settings.apiKey;
  const client = new NevaBridgeClient({
    baseUrl: settings.apiBaseUrl,
    tokenProvider: () => apiKey,
    fetch: fetchFn,
  });
  const actor = (
    reporter: Reporter,
  ): {
    actorId: string;
    actorName?: string;
    actorRoles?: Array<"anonymous" | "customer" | "tenant">;
  } => ({
    actorId: reporter.id,
    actorName: reporter.displayName,
    actorRoles: reporter.roles?.length ? [...reporter.roles] : undefined,
  });

  return {
    startConversation: (productId, reporter, input) =>
      client.json.startConversation({
        productId,
        ...actor(reporter),
        request: {
          userMessage: {content: input.text},
          category: input.category,
          applicationContext: input.applicationContext
            ? {...input.applicationContext}
            : undefined,
        },
      }),

    appendMessage: (conversationId, text) =>
      client.json.appendMessage({conversationId, request: {content: text}}),

    submitReport: (conversationId, reportId) =>
      client.json.submitReport({conversationId, reportId}),

    async getFeatures() {
      try {
        return await client.json.getFeatures();
      } catch (error: unknown) {
        if (
          error instanceof NevaBridgeApiError &&
          error.status === 404 &&
          typeof error.detail === "object" &&
          error.detail !== null &&
          "error" in error.detail &&
          error.detail.error === "RouteNotFound"
        ) {
          // A NevaBridge that predates the features operation offers no gated features.
          return {};
        }
        throw error;
      }
    },

    async attachFile(conversationId, uploaderId, file) {
      // 1. Admit the file: NevaBridge checks its type and size and returns a one-time upload address.
      const ticket = await client.requestAttachmentUpload({
        conversationId,
        actorId: uploaderId,
        request: {fileName: file.name, sizeBytes: file.sizeBytes},
      });

      // 2. Upload the bytes straight to storage. The address is presigned, so it gets no API key.
      const bytes = await file.read();
      let upload: Response;
      try {
        upload = await fetchFn(ticket.uploadUrl, {
          method: "PUT",
          headers: {"if-none-match": "*"},
          body: bytes,
        });
      } catch (error: unknown) {
        throw new UploadFailedError(null, {cause: error});
      }
      if (!upload.ok) {
        throw new UploadFailedError(upload.status);
      }

      // 3. Confirm, which attaches the file to the conversation.
      return await client.json.confirmAttachmentUpload({
        conversationId,
        attachmentId: ticket.attachmentId,
      });
    },

    startIntakeConversation: (productId, reporter, input) =>
      client.json.startIntakeConversation({
        productId,
        ...actor(reporter),
        request: {
          userMessage: {content: input.text},
          applicationContext: input.applicationContext
            ? {...input.applicationContext}
            : undefined,
        },
      }),

    startIntakeTurn: (conversationId, input) =>
      client.json.startIntakeTurn({
        conversationId,
        request: {
          content: input.text,
          expectedRevision: input.expectedRevision,
          finish: input.finish,
        },
      }),

    getIntakeTurnEvents: (turnId, after) =>
      client.json.getIntakeTurnEvents({turnId, after, wait: POLL_WAIT_SECONDS}),

    getConversation: (conversationId) =>
      client.json.getConversation({conversationId}),
  };
}
