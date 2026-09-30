import {
  NevaBridgeApiError,
  NevaBridgeClient,
  type Attachment,
  type ConversationTurn,
  type SubmitReportResult,
} from "@nevabridge/sdk";
import type {NevaBridgeSettings} from "./settings.js";

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
  readonly category?: string;
  /** The application's own context, stored with the report and shown in triage. */
  readonly applicationContext?: Readonly<Record<string, string>>;
}

/** A file the user chose to attach, read only when it is uploaded. */
export interface PickedFile {
  readonly name: string;
  readonly sizeBytes: number;
  read(): Promise<Blob>;
}

/**
 * The NevaBridge operations the web chat needs. `createSdkConversations` calls NevaBridge directly
 * with the API key; an implementation that calls your own backend can replace it.
 */
export interface NevaBridgeConversations {
  startConversation(
    productId: string,
    reporter: Reporter,
    input: StartConversationInput,
  ): Promise<ConversationTurn>;
  appendMessage(
    conversationId: string,
    text: string,
  ): Promise<ConversationTurn>;
  /**
   * Finalizes a report. Call it when the user says they are done; do not add an inactivity timer,
   * NevaBridge finalizes an abandoned conversation itself.
   */
  submitReport(
    conversationId: string,
    reportId: string,
  ): Promise<SubmitReportResult>;
  /** The tenant's effective features, such as chatAttachments. Features not reported are off. */
  getFeatures(): Promise<Record<string, boolean>>;
  /** Requests an upload address, uploads the bytes there, and confirms the upload. */
  attachFile(
    conversationId: string,
    uploaderId: string,
    file: PickedFile,
  ): Promise<Attachment>;
}

/**
 * Calls NevaBridge directly with the API key through `@nevabridge/sdk`. Whoever runs this code can
 * read the key; see the package guide before using it in an application whose users must not.
 */
export function createSdkConversations(
  settings: NevaBridgeSettings,
  options: {readonly fetch?: typeof fetch} = {},
): NevaBridgeConversations {
  const baseUrl = settings.apiBaseUrl.replace(/\/+$/, "");
  const apiKey = settings.apiKey;
  const fetchFn = options.fetch ?? fetch;
  const client = new NevaBridgeClient({baseUrl, tokenProvider: () => apiKey});

  return {
    startConversation: (productId, reporter, input) =>
      client.startConversation({
        productId,
        actorId: reporter.id,
        actorName: reporter.displayName,
        actorRoles: reporter.roles?.length ? [...reporter.roles] : undefined,
        request: {
          userMessage: {content: input.text},
          category: input.category as never,
          applicationContext: input.applicationContext
            ? {...input.applicationContext}
            : undefined,
        },
      }),

    appendMessage: (conversationId, text) =>
      client.appendMessage({conversationId, request: {content: text}}),

    submitReport: (conversationId, reportId) =>
      client.submitReport({conversationId, reportId}),

    async getFeatures() {
      // Not in the SDK yet; replaced by its getFeatures once the contract publishes /v1/features.
      const response = await fetchFn(`${baseUrl}/v1/features`, {
        headers: {
          authorization: `Bearer ${apiKey}`,
          accept: "application/json",
        },
      });
      if (response.ok) {
        return (await response.json()) as Record<string, boolean>;
      }
      const detail = (await response.json().catch(() => null)) as {
        error?: string;
      } | null;
      if (response.status === 404 && detail?.error === "RouteNotFound") {
        // A NevaBridge that predates the features operation offers no gated features.
        return {};
      }
      throw new NevaBridgeApiError(response.status, response.headers, detail);
    },

    async attachFile(conversationId, uploaderId, file) {
      // 1. Admit the file: NevaBridge checks its type and size and returns a one-time upload address.
      const ticket = await client.requestAttachmentUpload({
        conversationId,
        actorId: uploaderId,
        request: {fileName: file.name, sizeBytes: file.sizeBytes},
      });

      // 2. Upload the bytes straight to storage. The address is presigned, so it gets no API key.
      const upload = await fetchFn(ticket.uploadUrl, {
        method: "PUT",
        headers: {"if-none-match": "*"},
        body: await file.read(),
      });
      if (!upload.ok) {
        throw new NevaBridgeApiError(upload.status, upload.headers, {
          error: "UploadFailed",
          message: `The file could not be uploaded (HTTP ${upload.status}).`,
        });
      }

      // 3. Confirm, which attaches the file to the conversation.
      return await client.confirmAttachmentUpload({
        conversationId,
        attachmentId: ticket.attachmentId,
      });
    },
  };
}
