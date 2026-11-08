import type {JsonValue} from "@nevabridge/sdk";
import type {
  NevaBridgeConversations,
  PickedFile,
  Reporter,
  StartConversationInput,
  StartIntakeConversationInput,
  StartIntakeTurnInput,
} from "../../src/conversations.js";

export const CONVERSATION_ID =
  "conversation-00000000-0000-4000-8000-000000000002";
export const REPORT_ID = "report-00000000-0000-4000-8000-000000000003";
export const TURN_ID = "intaketurn-00000000-0000-4000-8000-000000000004";
export const NEXT_TURN_ID = "intaketurn-00000000-0000-4000-8000-000000000005";

export interface RecordedCall {
  readonly operation: string;
  readonly args: Record<string, unknown>;
}

/** An in-memory NevaBridge that records every call, so bridge tests see exactly what reaches it. */
export class FakeConversations implements NevaBridgeConversations {
  public readonly calls: RecordedCall[] = [];
  public features: {[feature: string]: JsonValue} = {};
  /** The turn a getConversation result names as running, if any. */
  public activeTurnId: string | null = null;
  public failWith: unknown = null;

  public async startConversation(
    productId: string,
    reporter: Reporter,
    input: StartConversationInput,
  ): Promise<JsonValue> {
    this.record("start", {productId, reporter, input});
    return turn(input.text);
  }

  public async appendMessage(
    conversationId: string,
    text: string,
  ): Promise<JsonValue> {
    this.record("append", {conversationId, text});
    return turn(text);
  }

  public async submitReport(
    conversationId: string,
    reportId: string,
  ): Promise<JsonValue> {
    this.record("submit", {conversationId, reportId});
    return {
      report: {...report(), status: "submitted"},
      connectors: {
        github: {
          status: "success",
          ticket: "https://github.com/acme/app/issues/9",
        },
      },
    };
  }

  public async getFeatures(): Promise<JsonValue> {
    this.record("features", {});
    return this.features;
  }

  public async attachFile(
    conversationId: string,
    uploaderId: string,
    file: PickedFile,
  ): Promise<JsonValue> {
    const bytes = new Uint8Array(await (await file.read()).arrayBuffer());
    this.record("attach", {
      conversationId,
      uploaderId,
      fileName: file.name,
      bytes,
    });
    return {
      id: "attachment-1",
      fileName: file.name,
      kind: "text",
      sizeBytes: file.sizeBytes,
      uploadedBy: `api:${uploaderId}`,
      uploadedAt: "2026-09-30T10:02:00.000Z",
    };
  }

  public async startIntakeConversation(
    productId: string,
    reporter: Reporter,
    input: StartIntakeConversationInput,
  ): Promise<JsonValue> {
    this.record("startIntake", {productId, reporter, input});
    return {conversationId: CONVERSATION_ID, turnId: TURN_ID};
  }

  public async startIntakeTurn(
    conversationId: string,
    input: StartIntakeTurnInput,
  ): Promise<JsonValue> {
    this.record("startTurn", {conversationId, input});
    return {turnId: NEXT_TURN_ID};
  }

  public async getIntakeTurnEvents(
    turnId: string,
    after?: string,
  ): Promise<JsonValue> {
    this.record("events", {turnId, after});
    return {events: [{type: "done", revision: 1}], cursor: "1", finished: true};
  }

  public async getConversation(conversationId: string): Promise<JsonValue> {
    this.record("read", {conversationId});
    return {
      id: CONVERSATION_ID,
      revision: 1,
      ...(this.activeTurnId === null ? {} : {activeTurnId: this.activeTurnId}),
    };
  }

  public async deleteAttachment(
    conversationId: string,
    attachmentId: string,
  ): Promise<void> {
    this.record("deleteAttachment", {conversationId, attachmentId});
  }

  private record(operation: string, args: Record<string, unknown>): void {
    if (this.failWith !== null) {
      throw this.failWith;
    }
    this.calls.push({operation, args});
  }
}

function report(): {[field: string]: JsonValue} {
  return {
    id: REPORT_ID,
    conversationId: CONVERSATION_ID,
    templateId: "t",
    templateVersion: "1",
    status: "reporting_in_progress",
    structured: {},
    createdAt: "2026-09-30T10:00:02.000Z",
    updatedAt: "2026-09-30T10:00:02.000Z",
  };
}

function turn(userText: string): JsonValue {
  return {
    userMessage: {
      id: "m-1",
      conversationId: CONVERSATION_ID,
      role: "user",
      content: userText,
      createdAt: "2026-09-30T10:00:00.000Z",
    },
    assistantMessage: {
      id: "m-2",
      conversationId: CONVERSATION_ID,
      role: "assistant",
      content: "When does it crash?",
      createdAt: "2026-09-30T10:00:02.000Z",
    },
    reports: [report()],
  };
}
