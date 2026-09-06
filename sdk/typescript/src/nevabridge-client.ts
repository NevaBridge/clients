import {ConversationsApi} from "./generated/apis/ConversationsApi.js";
import {ReportsApi} from "./generated/apis/ReportsApi.js";
import type {AppendMessageRequest} from "./generated/models/AppendMessageRequest.js";
import type {ActorRole} from "./generated/models/ActorRole.js";
import type {ConversationDetail} from "./generated/models/ConversationDetail.js";
import type {ConversationSummary} from "./generated/models/ConversationSummary.js";
import type {ConversationTurn} from "./generated/models/ConversationTurn.js";
import type {StartConversationRequest} from "./generated/models/StartConversationRequest.js";
import type {SubmitReportResult} from "./generated/models/SubmitReportResult.js";
import {Configuration, FetchError, ResponseError} from "./generated/runtime.js";

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

export class NevaBridgeClient {
  private readonly conversationsApi: ConversationsApi;
  private readonly reportsApi: ReportsApi;

  public constructor(configuration: NevaBridgeClientConfiguration) {
    const generatedConfiguration = new Configuration({
      basePath: configuration.baseUrl?.replace(/\/+$/, ""),
      accessToken: configuration.tokenProvider,
    });
    this.conversationsApi = new ConversationsApi(generatedConfiguration);
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
