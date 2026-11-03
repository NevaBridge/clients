import {AttachmentsApi} from "./generated/apis/AttachmentsApi.js";
import {ConversationsApi} from "./generated/apis/ConversationsApi.js";
import {FeaturesApi} from "./generated/apis/FeaturesApi.js";
import {ReportsApi} from "./generated/apis/ReportsApi.js";
import type {AppendMessageRequest} from "./generated/models/AppendMessageRequest.js";
import type {ActorRole} from "./generated/models/ActorRole.js";
import type {Attachment} from "./generated/models/Attachment.js";
import type {AttachmentDownloadUrl} from "./generated/models/AttachmentDownloadUrl.js";
import type {AttachmentList} from "./generated/models/AttachmentList.js";
import type {ConversationDetail} from "./generated/models/ConversationDetail.js";
import type {ConversationSummary} from "./generated/models/ConversationSummary.js";
import type {ConversationTurn} from "./generated/models/ConversationTurn.js";
import type {Features} from "./generated/models/Features.js";
import type {IntakeConversationList} from "./generated/models/IntakeConversationList.js";
import type {IntakeConversationStarted} from "./generated/models/IntakeConversationStarted.js";
import type {IntakeTurnStarted} from "./generated/models/IntakeTurnStarted.js";
import type {RequestAttachmentUploadRequest} from "./generated/models/RequestAttachmentUploadRequest.js";
import type {RequestAttachmentUploadResponse} from "./generated/models/RequestAttachmentUploadResponse.js";
import type {StartConversationRequest} from "./generated/models/StartConversationRequest.js";
import type {StartIntakeConversationRequest} from "./generated/models/StartIntakeConversationRequest.js";
import type {StartIntakeTurnRequest} from "./generated/models/StartIntakeTurnRequest.js";
import type {SubmitReportResult} from "./generated/models/SubmitReportResult.js";
import {Configuration, FetchError, ResponseError} from "./generated/runtime.js";
import {
  intakeTurnEventPage,
  type IntakeTurnEvent,
  type IntakeTurnEventPage,
} from "./intake-turn-events.js";

/** Retries of a failed poll; reading events again is safe because the cursor is unchanged. */
const MAXIMUM_POLL_RETRIES = 3;
const POLL_RETRY_PAUSE_MILLISECONDS = 500;

export interface NevaBridgeClientConfiguration {
  readonly baseUrl?: string;
  readonly tokenProvider: () => string | Promise<string>;
}

export interface StartConversationOptions {
  readonly productId: string;
  readonly actorId: string;
  readonly actorName?: string;
  readonly actorRoles?: ActorRole[];
  readonly request: StartConversationRequest;
  readonly signal?: AbortSignal;
}

export interface ListConversationsOptions {
  readonly productId: string;
  readonly reporterId?: string;
  readonly signal?: AbortSignal;
}

export interface GetConversationOptions {
  readonly conversationId: string;
  readonly signal?: AbortSignal;
}

export interface AppendMessageOptions {
  readonly conversationId: string;
  readonly request: AppendMessageRequest;
  readonly signal?: AbortSignal;
}

export interface SubmitReportOptions {
  readonly conversationId: string;
  readonly reportId: string;
  readonly signal?: AbortSignal;
}

export interface ListAttachmentsOptions {
  readonly conversationId: string;
  readonly signal?: AbortSignal;
}

export interface RequestAttachmentUploadOptions {
  readonly conversationId: string;
  readonly actorId: string;
  readonly request: RequestAttachmentUploadRequest;
  readonly signal?: AbortSignal;
}

export interface AttachmentOptions {
  readonly conversationId: string;
  readonly attachmentId: string;
  readonly signal?: AbortSignal;
}

export interface StartIntakeConversationOptions {
  readonly productId: string;
  readonly actorId: string;
  readonly actorName?: string;
  readonly actorRoles?: ActorRole[];
  readonly request: StartIntakeConversationRequest;
  readonly signal?: AbortSignal;
}

export interface StartIntakeTurnOptions {
  readonly conversationId: string;
  readonly request: StartIntakeTurnRequest;
  readonly signal?: AbortSignal;
}

export interface GetIntakeTurnEventsOptions {
  readonly turnId: string;
  /** The `cursor` of the previous read; absent reads from the first event. */
  readonly after?: string;
  /** Seconds to wait for a first new event, 0 to 20. The API defaults to 20. */
  readonly wait?: number;
  /** Milliseconds to keep collecting once an event arrived, 0 to 3000. The API defaults to 500. */
  readonly linger?: number;
  readonly signal?: AbortSignal;
}

export interface WaitForIntakeTurnOptions {
  readonly turnId: string;
  /** Called for every known event in order, the terminal one included. */
  readonly onEvent?: (event: IntakeTurnEvent) => void;
  readonly wait?: number;
  readonly linger?: number;
  readonly signal?: AbortSignal;
}

export interface IntakeTurnCompletion {
  /** The conversation's revision after the turn: the next turn's `expectedRevision`. */
  readonly revision: number;
}

export interface ListIntakeConversationsOptions {
  readonly productId: string;
  readonly reporterId?: string;
  readonly cursor?: string;
  readonly limit?: number;
  readonly signal?: AbortSignal;
}

export interface GetFeaturesOptions {
  readonly signal?: AbortSignal;
}

export class NevaBridgeApiError extends Error {
  public constructor(
    public readonly status: number,
    public readonly headers: Headers,
    public readonly detail: unknown,
  ) {
    super(`NevaBridge API request failed with status ${status}.`);
    this.name = "NevaBridgeApiError";
  }
}

async function errorDetail(response: Response): Promise<unknown> {
  let text: string;
  try {
    text = await response.text();
  } catch {
    return null;
  }
  if (!text) {
    return null;
  }

  const contentType = (
    response.headers.get("content-type") ?? ""
  ).toLowerCase();
  if (contentType.includes("json")) {
    try {
      return JSON.parse(text) as unknown;
    } catch {
      return text;
    }
  }

  return text;
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null && !Array.isArray(value);
}

/**
 * The id of the turn that is still running when a turn start was refused with
 * 409 `IntakeTurnRunning`, so a caller that lost a response resumes polling
 * that turn instead of sending again; otherwise `undefined`.
 */
export function intakeTurnRunningId(error: unknown): string | undefined {
  if (
    error instanceof NevaBridgeApiError &&
    error.status === 409 &&
    isRecord(error.detail) &&
    error.detail.error === "IntakeTurnRunning" &&
    typeof error.detail.turnId === "string"
  ) {
    return error.detail.turnId;
  }
  return undefined;
}

function isRetryablePollFailure(error: unknown): boolean {
  if (error instanceof NevaBridgeApiError) {
    return error.status === 502 || error.status === 504;
  }
  return error instanceof FetchError;
}

async function pause(
  milliseconds: number,
  signal: AbortSignal | undefined,
): Promise<void> {
  signal?.throwIfAborted();
  await new Promise<void>((resolve, reject) => {
    const abort = (): void => {
      clearTimeout(timer);
      reject(signal?.reason);
    };
    const timer = setTimeout(() => {
      signal?.removeEventListener("abort", abort);
      resolve();
    }, milliseconds);
    signal?.addEventListener("abort", abort, {once: true});
  });
}

export class NevaBridgeClient {
  private readonly attachmentsApi: AttachmentsApi;
  private readonly conversationsApi: ConversationsApi;
  private readonly featuresApi: FeaturesApi;
  private readonly reportsApi: ReportsApi;

  public constructor(configuration: NevaBridgeClientConfiguration) {
    const generatedConfiguration = new Configuration({
      basePath: configuration.baseUrl?.replace(/\/+$/, ""),
      accessToken: configuration.tokenProvider,
    });
    this.attachmentsApi = new AttachmentsApi(generatedConfiguration);
    this.conversationsApi = new ConversationsApi(generatedConfiguration);
    this.featuresApi = new FeaturesApi(generatedConfiguration);
    this.reportsApi = new ReportsApi(generatedConfiguration);
  }

  public async startConversation(
    options: StartConversationOptions,
  ): Promise<ConversationTurn> {
    return await this.request(() =>
      this.conversationsApi.startConversation(
        {
          productId: options.productId,
          xActorId: options.actorId,
          xActorName:
            options.actorName === undefined
              ? undefined
              : encodeURIComponent(options.actorName),
          xActorRoles: options.actorRoles,
          startConversationRequest: options.request,
        },
        {signal: options.signal},
      ),
    );
  }

  public async listConversations(
    options: ListConversationsOptions,
  ): Promise<ConversationSummary[]> {
    return await this.request(() =>
      this.conversationsApi.listConversations(
        {
          productId: options.productId,
          reporterId: options.reporterId,
        },
        {signal: options.signal},
      ),
    );
  }

  public async getConversation(
    options: GetConversationOptions,
  ): Promise<ConversationDetail> {
    return await this.request(() =>
      this.conversationsApi.getConversation(
        {conversationId: options.conversationId},
        {signal: options.signal},
      ),
    );
  }

  public async appendMessage(
    options: AppendMessageOptions,
  ): Promise<ConversationTurn> {
    return await this.request(() =>
      this.conversationsApi.appendMessage(
        {
          conversationId: options.conversationId,
          appendMessageRequest: options.request,
        },
        {signal: options.signal},
      ),
    );
  }

  /**
   * Starts a Generic Intake conversation and its first turn, which runs in the
   * background. Follow the turn with waitForIntakeTurn or getIntakeTurnEvents.
   */
  public async startIntakeConversation(
    options: StartIntakeConversationOptions,
  ): Promise<IntakeConversationStarted> {
    return await this.request(() =>
      this.conversationsApi.startIntakeConversation(
        {
          productId: options.productId,
          xActorId: options.actorId,
          xActorName:
            options.actorName === undefined
              ? undefined
              : encodeURIComponent(options.actorName),
          xActorRoles: options.actorRoles,
          startIntakeConversationRequest: options.request,
        },
        {signal: options.signal},
      ),
    );
  }

  /**
   * Starts the next Generic Intake turn, or finishes the conversation with
   * `finish`. A 409 for a turn that is still running names it; see
   * intakeTurnRunningId.
   */
  public async startIntakeTurn(
    options: StartIntakeTurnOptions,
  ): Promise<IntakeTurnStarted> {
    return await this.request(() =>
      this.conversationsApi.startIntakeTurn(
        {
          conversationId: options.conversationId,
          startIntakeTurnRequest: options.request,
        },
        {signal: options.signal},
      ),
    );
  }

  public async getIntakeTurnEvents(
    options: GetIntakeTurnEventsOptions,
  ): Promise<IntakeTurnEventPage> {
    const response = await this.request(() =>
      this.conversationsApi.getIntakeTurnEventsRaw(
        {
          turnId: options.turnId,
          after: options.after,
          wait: options.wait,
          linger: options.linger,
        },
        {signal: options.signal},
      ),
    );
    let body: unknown;
    try {
      body = await response.raw.json();
    } catch (error: unknown) {
      if (error instanceof SyntaxError) {
        throw error;
      }
      // A connection that breaks off mid-body is a network failure like one
      // that fails before the status arrives.
      throw new FetchError(
        error instanceof Error ? error : new Error(String(error)),
        "The response body could not be read.",
      );
    }
    return intakeTurnEventPage(body);
  }

  /**
   * Polls a turn until its terminal event. Resolves on `done`; read the
   * conversation with getConversation afterwards, because the `text` events
   * are only a preview of the reply. Rejects on `error` with a
   * NevaBridgeApiError carrying the failure's status code and body.
   */
  public async waitForIntakeTurn(
    options: WaitForIntakeTurnOptions,
  ): Promise<IntakeTurnCompletion> {
    let after: string | undefined;
    let retries = 0;
    for (;;) {
      let page: IntakeTurnEventPage;
      try {
        page = await this.getIntakeTurnEvents({
          turnId: options.turnId,
          after,
          wait: options.wait,
          linger: options.linger,
          signal: options.signal,
        });
      } catch (error: unknown) {
        if (!isRetryablePollFailure(error) || retries >= MAXIMUM_POLL_RETRIES) {
          throw error;
        }
        retries += 1;
        await pause(POLL_RETRY_PAUSE_MILLISECONDS, options.signal);
        continue;
      }
      retries = 0;

      for (const event of page.events) {
        options.onEvent?.(event);
        if (event.type === "done") {
          return {revision: event.revision};
        }
        if (event.type === "error") {
          throw new NevaBridgeApiError(
            event.error.statusCode,
            new Headers(),
            event.error,
          );
        }
      }
      if (page.finished) {
        throw new Error(
          `NevaBridge ended Generic Intake turn ${options.turnId} without a terminal event.`,
        );
      }
      options.signal?.throwIfAborted();
      after = page.cursor;
    }
  }

  public async listIntakeConversations(
    options: ListIntakeConversationsOptions,
  ): Promise<IntakeConversationList> {
    return await this.request(() =>
      this.conversationsApi.listIntakeConversations(
        {
          productId: options.productId,
          reporterId: options.reporterId,
          cursor: options.cursor,
          limit: options.limit,
        },
        {signal: options.signal},
      ),
    );
  }

  public async getFeatures(
    options: GetFeaturesOptions = {},
  ): Promise<Features> {
    return await this.request(() =>
      this.featuresApi.getFeatures({signal: options.signal}),
    );
  }

  /**
   * Call submit when the user explicitly finishes. Do not implement an inactivity
   * timer or retry-based auto-submit. NevaBridge owns the 60-minute fallback.
   */
  public async submitReport(
    options: SubmitReportOptions,
  ): Promise<SubmitReportResult> {
    return await this.request(() =>
      this.reportsApi.submitReport(
        {
          conversationId: options.conversationId,
          reportId: options.reportId,
        },
        {signal: options.signal},
      ),
    );
  }

  public async listAttachments(
    options: ListAttachmentsOptions,
  ): Promise<AttachmentList> {
    return await this.request(() =>
      this.attachmentsApi.listAttachments(
        {conversationId: options.conversationId},
        {signal: options.signal},
      ),
    );
  }

  /**
   * Admits one file and returns a presigned URL. PUT exactly `sizeBytes` bytes to
   * `uploadUrl` with the header `If-None-Match: *` before `expiresAt`, then call
   * confirmAttachmentUpload. The SDK never sends the file itself.
   */
  public async requestAttachmentUpload(
    options: RequestAttachmentUploadOptions,
  ): Promise<RequestAttachmentUploadResponse> {
    return await this.request(() =>
      this.attachmentsApi.requestAttachmentUpload(
        {
          conversationId: options.conversationId,
          xActorId: options.actorId,
          requestAttachmentUploadRequest: options.request,
        },
        {signal: options.signal},
      ),
    );
  }

  public async confirmAttachmentUpload(
    options: AttachmentOptions,
  ): Promise<Attachment> {
    return await this.request(() =>
      this.attachmentsApi.confirmAttachmentUpload(
        {
          conversationId: options.conversationId,
          attachmentCommandRequest: {attachmentId: options.attachmentId},
        },
        {signal: options.signal},
      ),
    );
  }

  public async requestAttachmentDownloadUrl(
    options: AttachmentOptions,
  ): Promise<AttachmentDownloadUrl> {
    return await this.request(() =>
      this.attachmentsApi.requestAttachmentDownloadUrl(
        {
          conversationId: options.conversationId,
          attachmentCommandRequest: {attachmentId: options.attachmentId},
        },
        {signal: options.signal},
      ),
    );
  }

  public async deleteAttachment(options: AttachmentOptions): Promise<void> {
    await this.request(() =>
      this.attachmentsApi.deleteAttachment(
        {
          conversationId: options.conversationId,
          attachmentCommandRequest: {attachmentId: options.attachmentId},
        },
        {signal: options.signal},
      ),
    );
  }

  private async request<Result>(
    operation: () => Promise<Result>,
  ): Promise<Result> {
    try {
      return await operation();
    } catch (error: unknown) {
      if (error instanceof FetchError && error.cause.name === "AbortError") {
        throw error.cause;
      }
      if (!(error instanceof ResponseError)) {
        throw error;
      }
      throw new NevaBridgeApiError(
        error.response.status,
        error.response.headers,
        await errorDetail(error.response),
      );
    }
  }
}
