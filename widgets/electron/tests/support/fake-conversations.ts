import type {
  Attachment,
  ConversationTurn,
  SubmitReportResult,
} from "@nevabridge/sdk";
import type {
  NevaBridgeConversations,
  PickedFile,
  Reporter,
  StartConversationInput,
} from "../../src/conversations.js";

export const CONVERSATION_ID =
  "conversation-00000000-0000-4000-8000-000000000002";
export const REPORT_ID = "report-00000000-0000-4000-8000-000000000003";

export interface RecordedCall {
  readonly operation: string;
  readonly args: Record<string, unknown>;
}

/** An in-memory NevaBridge that records every call, so bridge tests see exactly what reaches it. */
export class FakeConversations implements NevaBridgeConversations {
  public readonly calls: RecordedCall[] = [];
  public features: Record<string, boolean> = {};
  public failWith: unknown = null;

  public async startConversation(
    productId: string,
    reporter: Reporter,
    input: StartConversationInput,
  ): Promise<ConversationTurn> {
    this.record("start", {productId, reporter, input});
    return turn(input.text);
  }

  public async appendMessage(
    conversationId: string,
    text: string,
  ): Promise<ConversationTurn> {
    this.record("append", {conversationId, text});
    return turn(text);
  }

  public async submitReport(
    conversationId: string,
    reportId: string,
  ): Promise<SubmitReportResult> {
    this.record("submit", {conversationId, reportId});
    return {
      report: {...report(), status: "submitted"},
      connectors: {
        github: {
          status: "success",
          ticket: "https://github.com/acme/app/issues/9",
        },
      },
    } as unknown as SubmitReportResult;
  }

  public async getFeatures(): Promise<Record<string, boolean>> {
    this.record("features", {});
    return this.features;
  }

  public async attachFile(
    conversationId: string,
    uploaderId: string,
    file: PickedFile,
  ): Promise<Attachment> {
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
      uploadedAt: new Date("2026-09-30T10:02:00Z"),
    } as Attachment;
  }

  private record(operation: string, args: Record<string, unknown>): void {
    if (this.failWith !== null) {
      throw this.failWith;
    }
    this.calls.push({operation, args});
  }
}

function report(): Record<string, unknown> {
  return {
    id: REPORT_ID,
    conversationId: CONVERSATION_ID,
    templateId: "t",
    templateVersion: "1",
    status: "reporting_in_progress",
    structured: {},
    createdAt: new Date("2026-09-30T10:00:02Z"),
    updatedAt: new Date("2026-09-30T10:00:02Z"),
  };
}

function turn(userText: string): ConversationTurn {
  return {
    userMessage: {
      id: "m-1",
      conversationId: CONVERSATION_ID,
      role: "user",
      content: userText,
      createdAt: new Date("2026-09-30T10:00:00Z"),
    },
    assistantMessage: {
      id: "m-2",
      conversationId: CONVERSATION_ID,
      role: "assistant",
      content: "When does it crash?",
      createdAt: new Date("2026-09-30T10:00:02Z"),
    },
    reports: [report()],
  } as unknown as ConversationTurn;
}
