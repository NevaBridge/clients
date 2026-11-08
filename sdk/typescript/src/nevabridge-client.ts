import {AttachmentsApi} from "./generated/apis/AttachmentsApi.js";
import {ConversationsApi} from "./generated/apis/ConversationsApi.js";
import {FeaturesApi} from "./generated/apis/FeaturesApi.js";
import {ReportsApi} from "./generated/apis/ReportsApi.js";
import {VoiceApi} from "./generated/apis/VoiceApi.js";
import type {AppendMessageRequest} from "./generated/models/AppendMessageRequest.js";
import type {ActorRole} from "./generated/models/ActorRole.js";
import type {Attachment} from "./generated/models/Attachment.js";
import type {AttachmentDownloadUrl} from "./generated/models/AttachmentDownloadUrl.js";
import type {AttachmentList} from "./generated/models/AttachmentList.js";
import type {ConversationDetail} from "./generated/models/ConversationDetail.js";
import type {ConversationSummary} from "./generated/models/ConversationSummary.js";
import type {ConversationTurn} from "./generated/models/ConversationTurn.js";
import type {Features} from "./generated/models/Features.js";
import type {IntakeConversationHeld} from "./generated/models/IntakeConversationHeld.js";
import type {IntakeConversationList} from "./generated/models/IntakeConversationList.js";
import {
  IntakeConversationStartedFromJSON,
  type IntakeConversationStarted,
} from "./generated/models/IntakeConversationStarted.js";
import type {IntakeTurnStarted} from "./generated/models/IntakeTurnStarted.js";
import type {RequestAttachmentUploadRequest} from "./generated/models/RequestAttachmentUploadRequest.js";
import type {RequestAttachmentUploadResponse} from "./generated/models/RequestAttachmentUploadResponse.js";
import type {StartConversationRequest} from "./generated/models/StartConversationRequest.js";
import type {StartIntakeConversationRequest} from "./generated/models/StartIntakeConversationRequest.js";
import type {StartIntakeTurnRequest} from "./generated/models/StartIntakeTurnRequest.js";
import type {SpeechSynthesis} from "./generated/models/SpeechSynthesis.js";
import type {SubmitReportResult} from "./generated/models/SubmitReportResult.js";
import type {TranscriptionSession} from "./generated/models/TranscriptionSession.js";
import type {VoiceLanguageCode} from "./generated/models/VoiceLanguageCode.js";
import {
  Configuration,
  FetchError,
  JSONApiResponse,
  ResponseError,
  type ApiResponse,
} from "./generated/runtime.js";
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
  /** Sends every HTTP request. Defaults to the global `fetch`. */
  readonly fetch?: FetchFunction;
}

/** The part of `fetch` the client uses, so a plain function can stand in for it. */
export type FetchFunction = (
  input: string | URL | Request,
  init?: RequestInit,
) => Promise<Response>;

/** A JSON value as the API sent it. */
export type JsonValue =
  null | boolean | number | string | JsonValue[] | {[key: string]: JsonValue};

/**
 * The operations that answer with a body, resolving to that body as the API's
 * JSON instead of an SDK model: dates stay strings and fields this SDK revision
 * does not know are kept. For relaying NevaBridge answers to a client that reads
 * the API's JSON, such as the web chat page. Errors are the same as for the
 * typed operations.
 */
export interface NevaBridgeJsonOperations {
  startConversation(options: StartConversationOptions): Promise<JsonValue>;
  listConversations(options: ListConversationsOptions): Promise<JsonValue>;
  getConversation(options: GetConversationOptions): Promise<JsonValue>;
  appendMessage(options: AppendMessageOptions): Promise<JsonValue>;
  startIntakeConversation(
    options: StartIntakeConversationOptions,
  ): Promise<JsonValue>;
  startHeldIntakeConversation(
    options: StartHeldIntakeConversationOptions,
  ): Promise<JsonValue>;
  startIntakeTurn(options: StartIntakeTurnOptions): Promise<JsonValue>;
  getIntakeTurnEvents(options: GetIntakeTurnEventsOptions): Promise<JsonValue>;
  listIntakeConversations(
    options: ListIntakeConversationsOptions,
  ): Promise<JsonValue>;
  getFeatures(options?: GetFeaturesOptions): Promise<JsonValue>;
  submitReport(options: SubmitReportOptions): Promise<JsonValue>;
  listAttachments(options: ListAttachmentsOptions): Promise<JsonValue>;
  requestAttachmentUpload(
    options: RequestAttachmentUploadOptions,
  ): Promise<JsonValue>;
  confirmAttachmentUpload(options: AttachmentOptions): Promise<JsonValue>;
  requestAttachmentDownloadUrl(options: AttachmentOptions): Promise<JsonValue>;
  createTranscriptionSession(
    options: CreateTranscriptionSessionOptions,
  ): Promise<JsonValue>;
  synthesizeSpeech(options: SynthesizeSpeechOptions): Promise<JsonValue>;
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

/**
 * A Generic Intake start that holds the files the reporter picked before
 * writing. The first message follows as a turn with `expectedRevision` 0, and
 * it carries `writerModelKey` and `responseLanguage`.
 */
export interface StartHeldIntakeConversationRequest {
  /** One to ten files, admitted all or nothing; the answer lists their uploads in this order. */
  readonly attachments: RequestAttachmentUploadRequest[];
  readonly applicationContext?: StartIntakeConversationRequest["applicationContext"];
  readonly reporterContact?: StartIntakeConversationRequest["reporterContact"];
}

export interface StartHeldIntakeConversationOptions {
  readonly productId: string;
  readonly actorId: string;
  readonly actorName?: string;
  readonly actorRoles?: ActorRole[];
  readonly request: StartHeldIntakeConversationRequest;
  readonly signal?: AbortSignal;
}

export interface CreateTranscriptionSessionOptions {
  readonly languageCode: VoiceLanguageCode;
  readonly signal?: AbortSignal;
}

export interface SynthesizeSpeechOptions {
  readonly text: string;
  readonly languageCode: VoiceLanguageCode;
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

/**
 * Header values are ASCII, and NevaBridge percent-decodes this one. A name of
 * printable ASCII without `%` reads the same either way, so it is sent as it is.
 */
function actorNameHeader(actorName: string | undefined): string | undefined {
  if (actorName === undefined) {
    return undefined;
  }
  return /^[\x20-\x24\x26-\x7e]*$/.test(actorName)
    ? actorName
    : encodeURIComponent(actorName);
}

/** Reads a success body; a connection that breaks off mid-body is a network failure. */
async function responseJson(response: Response): Promise<JsonValue> {
  try {
    // Parsed JSON text is a JsonValue by construction.
    return (await response.json()) as JsonValue;
  } catch (error: unknown) {
    if (error instanceof SyntaxError) {
      throw error;
    }
    throw new FetchError(
      error instanceof Error ? error : new Error(String(error)),
      "The response body could not be read.",
    );
  }
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
  /** The same operations, resolving to the API's JSON; see NevaBridgeJsonOperations. */
  public readonly json: NevaBridgeJsonOperations;

  private readonly attachmentsApi: AttachmentsApi;
  private readonly conversationsApi: ConversationsApi;
  private readonly featuresApi: FeaturesApi;
  private readonly reportsApi: ReportsApi;
  private readonly voiceApi: VoiceApi;

  public constructor(configuration: NevaBridgeClientConfiguration) {
    const generatedConfiguration = new Configuration({
      basePath: configuration.baseUrl?.replace(/\/+$/, ""),
      accessToken: configuration.tokenProvider,
      fetchApi: configuration.fetch,
    });
    this.attachmentsApi = new AttachmentsApi(generatedConfiguration);
    this.conversationsApi = new ConversationsApi(generatedConfiguration);
    this.featuresApi = new FeaturesApi(generatedConfiguration);
    this.reportsApi = new ReportsApi(generatedConfiguration);
    this.voiceApi = new VoiceApi(generatedConfiguration);

    const json = <Options>(
      send: (options: Options) => Promise<ApiResponse<unknown>>,
    ): ((options: Options) => Promise<JsonValue>) => {
      return async (options) =>
        await this.request(async () => responseJson((await send(options)).raw));
    };
    this.json = {
      startConversation: json((options) => this.sendStartConversation(options)),
      listConversations: json((options) => this.sendListConversations(options)),
      getConversation: json((options) => this.sendGetConversation(options)),
      appendMessage: json((options) => this.sendAppendMessage(options)),
      startIntakeConversation: json((options) =>
        this.sendStartIntakeConversation(options),
      ),
      startHeldIntakeConversation: json((options) =>
        this.sendStartHeldIntakeConversation(options),
      ),
      startIntakeTurn: json((options) => this.sendStartIntakeTurn(options)),
      getIntakeTurnEvents: json((options) =>
        this.sendGetIntakeTurnEvents(options),
      ),
      listIntakeConversations: json((options) =>
        this.sendListIntakeConversations(options),
      ),
      getFeatures: json((options: GetFeaturesOptions = {}) =>
        this.sendGetFeatures(options),
      ),
      submitReport: json((options) => this.sendSubmitReport(options)),
      listAttachments: json((options) => this.sendListAttachments(options)),
      requestAttachmentUpload: json((options) =>
        this.sendRequestAttachmentUpload(options),
      ),
      confirmAttachmentUpload: json((options) =>
        this.sendConfirmAttachmentUpload(options),
      ),
      requestAttachmentDownloadUrl: json((options) =>
        this.sendRequestAttachmentDownloadUrl(options),
      ),
      createTranscriptionSession: json((options) =>
        this.sendCreateTranscriptionSession(options),
      ),
      synthesizeSpeech: json((options) => this.sendSynthesizeSpeech(options)),
    };
  }

  public async startConversation(
    options: StartConversationOptions,
  ): Promise<ConversationTurn> {
    return await this.typed(() => this.sendStartConversation(options));
  }

  public async listConversations(
    options: ListConversationsOptions,
  ): Promise<ConversationSummary[]> {
    return await this.typed(() => this.sendListConversations(options));
  }

  public async getConversation(
    options: GetConversationOptions,
  ): Promise<ConversationDetail> {
    return await this.typed(() => this.sendGetConversation(options));
  }

  public async appendMessage(
    options: AppendMessageOptions,
  ): Promise<ConversationTurn> {
    return await this.typed(() => this.sendAppendMessage(options));
  }

  /**
   * Starts a Generic Intake conversation with `userMessage` and its first
   * turn, which runs in the background. Follow the turn with
   * waitForIntakeTurn or getIntakeTurnEvents. To attach files before the
   * first message, use startHeldIntakeConversation.
   */
  public async startIntakeConversation(
    options: StartIntakeConversationOptions,
  ): Promise<IntakeConversationStarted> {
    return await this.typed(() => this.sendStartIntakeConversation(options));
  }

  /**
   * Starts a Generic Intake conversation without a message, holding the files
   * the reporter picked before writing, so the first reply already sees them.
   * Resolves with an upload per file in request order: PUT each file to its
   * `uploadUrl` with `If-None-Match: *` and confirm it with
   * confirmAttachmentUpload. Then send the first message with startIntakeTurn
   * and `expectedRevision` 0; finishing before that answers 400
   * `NothingToFinish`. Requires `genericIntake` and `chatAttachments`. A 424
   * whose detail carries a `conversationId` means the conversation exists but
   * its uploads could not be signed: request each upload again for it with
   * requestAttachmentUpload. NevaBridge deletes a conversation that has no
   * reply 24 hours after it was created, with its files.
   */
  public async startHeldIntakeConversation(
    options: StartHeldIntakeConversationOptions,
  ): Promise<IntakeConversationHeld> {
    return await this.typed(() =>
      this.sendStartHeldIntakeConversation(options),
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
    return await this.typed(() => this.sendStartIntakeTurn(options));
  }

  public async getIntakeTurnEvents(
    options: GetIntakeTurnEventsOptions,
  ): Promise<IntakeTurnEventPage> {
    const body = await this.request(async () =>
      responseJson((await this.sendGetIntakeTurnEvents(options)).raw),
    );
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
    return await this.typed(() => this.sendListIntakeConversations(options));
  }

  public async getFeatures(
    options: GetFeaturesOptions = {},
  ): Promise<Features> {
    return await this.typed(() => this.sendGetFeatures(options));
  }

  /**
   * Call submit when the user explicitly finishes. Do not implement an inactivity
   * timer or retry-based auto-submit. NevaBridge owns the 60-minute fallback.
   */
  public async submitReport(
    options: SubmitReportOptions,
  ): Promise<SubmitReportResult> {
    return await this.typed(() => this.sendSubmitReport(options));
  }

  public async listAttachments(
    options: ListAttachmentsOptions,
  ): Promise<AttachmentList> {
    return await this.typed(() => this.sendListAttachments(options));
  }

  /**
   * Admits one file and returns a presigned URL. PUT exactly `sizeBytes` bytes to
   * `uploadUrl` with the header `If-None-Match: *` before `expiresAt`, then call
   * confirmAttachmentUpload. The SDK never sends the file itself.
   */
  public async requestAttachmentUpload(
    options: RequestAttachmentUploadOptions,
  ): Promise<RequestAttachmentUploadResponse> {
    return await this.typed(() => this.sendRequestAttachmentUpload(options));
  }

  public async confirmAttachmentUpload(
    options: AttachmentOptions,
  ): Promise<Attachment> {
    return await this.typed(() => this.sendConfirmAttachmentUpload(options));
  }

  public async requestAttachmentDownloadUrl(
    options: AttachmentOptions,
  ): Promise<AttachmentDownloadUrl> {
    return await this.typed(() =>
      this.sendRequestAttachmentDownloadUrl(options),
    );
  }

  /**
   * Returns a presigned Amazon Transcribe streaming address for one language
   * and one paid session of `maxDurationSeconds`. The address is a credential:
   * keep it out of logs. Requires `voiceDictation` or `voiceConversation`.
   */
  public async createTranscriptionSession(
    options: CreateTranscriptionSessionOptions,
  ): Promise<TranscriptionSession> {
    return await this.typed(() => this.sendCreateTranscriptionSession(options));
  }

  /** Speaks a text as MP3 audio, base64-encoded. Requires `voiceConversation`. */
  public async synthesizeSpeech(
    options: SynthesizeSpeechOptions,
  ): Promise<SpeechSynthesis> {
    return await this.typed(() => this.sendSynthesizeSpeech(options));
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

  private sendStartConversation(
    options: StartConversationOptions,
  ): Promise<ApiResponse<ConversationTurn>> {
    return this.conversationsApi.startConversationRaw(
      {
        productId: options.productId,
        xActorId: options.actorId,
        xActorName: actorNameHeader(options.actorName),
        xActorRoles: options.actorRoles,
        startConversationRequest: options.request,
      },
      {signal: options.signal},
    );
  }

  private sendListConversations(
    options: ListConversationsOptions,
  ): Promise<ApiResponse<ConversationSummary[]>> {
    return this.conversationsApi.listConversationsRaw(
      {productId: options.productId, reporterId: options.reporterId},
      {signal: options.signal},
    );
  }

  private sendGetConversation(
    options: GetConversationOptions,
  ): Promise<ApiResponse<ConversationDetail>> {
    return this.conversationsApi.getConversationRaw(
      {conversationId: options.conversationId},
      {signal: options.signal},
    );
  }

  private sendAppendMessage(
    options: AppendMessageOptions,
  ): Promise<ApiResponse<ConversationTurn>> {
    return this.conversationsApi.appendMessageRaw(
      {
        conversationId: options.conversationId,
        appendMessageRequest: options.request,
      },
      {signal: options.signal},
    );
  }

  private async sendStartIntakeConversation(
    options: StartIntakeConversationOptions,
  ): Promise<ApiResponse<IntakeConversationStarted>> {
    const response = await this.conversationsApi.startIntakeConversationRaw(
      {
        productId: options.productId,
        xActorId: options.actorId,
        xActorName: actorNameHeader(options.actorName),
        xActorRoles: options.actorRoles,
        startIntakeConversationRequest: options.request,
      },
      {signal: options.signal},
    );
    // The generator types only the 201 answer to a start with attachments; a
    // start with userMessage answers 202 with its turn id.
    return new JSONApiResponse(response.raw, IntakeConversationStartedFromJSON);
  }

  private sendStartHeldIntakeConversation(
    options: StartHeldIntakeConversationOptions,
  ): Promise<ApiResponse<IntakeConversationHeld>> {
    // The generator types the 201 answer, which only a start with attachments gives.
    return this.conversationsApi.startIntakeConversationRaw(
      {
        productId: options.productId,
        xActorId: options.actorId,
        xActorName: actorNameHeader(options.actorName),
        xActorRoles: options.actorRoles,
        startIntakeConversationRequest: {
          attachments: options.request.attachments,
          applicationContext: options.request.applicationContext,
          reporterContact: options.request.reporterContact,
        },
      },
      {signal: options.signal},
    );
  }

  private sendCreateTranscriptionSession(
    options: CreateTranscriptionSessionOptions,
  ): Promise<ApiResponse<TranscriptionSession>> {
    return this.voiceApi.createTranscriptionSessionRaw(
      {transcriptionSessionCreateRequest: {languageCode: options.languageCode}},
      {signal: options.signal},
    );
  }

  private sendSynthesizeSpeech(
    options: SynthesizeSpeechOptions,
  ): Promise<ApiResponse<SpeechSynthesis>> {
    return this.voiceApi.synthesizeSpeechRaw(
      {
        speechSynthesisCreateRequest: {
          text: options.text,
          languageCode: options.languageCode,
        },
      },
      {signal: options.signal},
    );
  }

  private sendStartIntakeTurn(
    options: StartIntakeTurnOptions,
  ): Promise<ApiResponse<IntakeTurnStarted>> {
    return this.conversationsApi.startIntakeTurnRaw(
      {
        conversationId: options.conversationId,
        startIntakeTurnRequest: options.request,
      },
      {signal: options.signal},
    );
  }

  private sendGetIntakeTurnEvents(
    options: GetIntakeTurnEventsOptions,
  ): Promise<ApiResponse<unknown>> {
    return this.conversationsApi.getIntakeTurnEventsRaw(
      {
        turnId: options.turnId,
        after: options.after,
        wait: options.wait,
        linger: options.linger,
      },
      {signal: options.signal},
    );
  }

  private sendListIntakeConversations(
    options: ListIntakeConversationsOptions,
  ): Promise<ApiResponse<IntakeConversationList>> {
    return this.conversationsApi.listIntakeConversationsRaw(
      {
        productId: options.productId,
        reporterId: options.reporterId,
        cursor: options.cursor,
        limit: options.limit,
      },
      {signal: options.signal},
    );
  }

  private sendGetFeatures(
    options: GetFeaturesOptions,
  ): Promise<ApiResponse<Features>> {
    return this.featuresApi.getFeaturesRaw({signal: options.signal});
  }

  private sendSubmitReport(
    options: SubmitReportOptions,
  ): Promise<ApiResponse<SubmitReportResult>> {
    return this.reportsApi.submitReportRaw(
      {conversationId: options.conversationId, reportId: options.reportId},
      {signal: options.signal},
    );
  }

  private sendListAttachments(
    options: ListAttachmentsOptions,
  ): Promise<ApiResponse<AttachmentList>> {
    return this.attachmentsApi.listAttachmentsRaw(
      {conversationId: options.conversationId},
      {signal: options.signal},
    );
  }

  private sendRequestAttachmentUpload(
    options: RequestAttachmentUploadOptions,
  ): Promise<ApiResponse<RequestAttachmentUploadResponse>> {
    return this.attachmentsApi.requestAttachmentUploadRaw(
      {
        conversationId: options.conversationId,
        xActorId: options.actorId,
        requestAttachmentUploadRequest: options.request,
      },
      {signal: options.signal},
    );
  }

  private sendConfirmAttachmentUpload(
    options: AttachmentOptions,
  ): Promise<ApiResponse<Attachment>> {
    return this.attachmentsApi.confirmAttachmentUploadRaw(
      {
        conversationId: options.conversationId,
        attachmentCommandRequest: {attachmentId: options.attachmentId},
      },
      {signal: options.signal},
    );
  }

  private sendRequestAttachmentDownloadUrl(
    options: AttachmentOptions,
  ): Promise<ApiResponse<AttachmentDownloadUrl>> {
    return this.attachmentsApi.requestAttachmentDownloadUrlRaw(
      {
        conversationId: options.conversationId,
        attachmentCommandRequest: {attachmentId: options.attachmentId},
      },
      {signal: options.signal},
    );
  }

  private async typed<Result>(
    send: () => Promise<ApiResponse<Result>>,
  ): Promise<Result> {
    return await this.request(async () => await (await send()).value());
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
