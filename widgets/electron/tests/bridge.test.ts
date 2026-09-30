import {beforeEach, describe, expect, test} from "bun:test";
import {NevaBridgeApiError} from "@nevabridge/sdk";
import {createWebChatBridge, type WebChatBridge} from "../src/bridge.js";
import type {PickedFile} from "../src/conversations.js";
import {
  CONVERSATION_ID,
  FakeConversations,
  REPORT_ID,
} from "./support/fake-conversations.js";

const PRODUCT_ID = "product-00000000-0000-4000-8000-000000000001";
const PAGE_URL = "https://webchat.nevabridge.example/index.html";

interface Reply {
  readonly id: number;
  readonly ok: boolean;
  readonly result?: Record<string, unknown> | null;
  readonly error?: {
    name: string;
    message: string;
    status: number | null;
    retryable: boolean;
  };
}

let nevaBridge: FakeConversations;
let nextPickedFile: PickedFile | null;
let pickerFailure: unknown;

function createBridge(canPickFiles = true): WebChatBridge {
  return createWebChatBridge({
    conversations: nevaBridge,
    productId: PRODUCT_ID,
    reporter: {id: "jdoe@corp.example", displayName: "Jane Doe"},
    applicationContext: {screen: "Invoices"},
    pageUrl: PAGE_URL,
    pickFile: canPickFiles
      ? async () => {
          if (pickerFailure !== null) {
            throw pickerFailure;
          }
          return nextPickedFile;
        }
      : undefined,
  });
}

async function send(
  bridge: WebChatBridge,
  message: unknown,
  source = PAGE_URL,
): Promise<Reply | null> {
  const response = await bridge.handle(
    source,
    typeof message === "string" ? message : JSON.stringify(message),
  );
  return response === null ? null : (JSON.parse(response) as Reply);
}

beforeEach(() => {
  nevaBridge = new FakeConversations();
  nextPickedFile = null;
  pickerFailure = null;
});

describe("web chat bridge", () => {
  test.each([
    {id: "r17", type: "getFeatures"},
    {id: "r17", type: "startConversation", text: "It crashes."},
    {id: "r17", type: "unsupported"},
  ])("preserves the hosted page request id in a reply: %j", async (request) => {
    // Given
    const bridge = createBridge();

    // When
    const response = await bridge.handle(PAGE_URL, JSON.stringify(request));

    // Then
    expect(response).not.toBeNull();
    const reply: unknown = JSON.parse(response ?? "null");
    expect(reply).toMatchObject({
      id: "r17",
      ok: request.type !== "unsupported",
    });
  });

  test("starts a conversation with the host's product, reporter and context", async () => {
    // When
    const reply = await send(createBridge(), {
      id: 1,
      type: "startConversation",
      text: "It crashes.",
      productId: "product-evil",
      reporterId: "admin",
    });

    // Then
    expect(nevaBridge.calls).toHaveLength(1);
    const call = nevaBridge.calls[0]!;
    expect(call.operation).toBe("start");
    expect(call.args["productId"]).toBe(PRODUCT_ID);
    expect(call.args["reporter"]).toEqual({
      id: "jdoe@corp.example",
      displayName: "Jane Doe",
    });
    expect(call.args["input"]).toEqual({
      text: "It crashes.",
      category: undefined,
      applicationContext: {screen: "Invoices"},
    });
    expect(reply?.id).toBe(1);
    expect(reply?.ok).toBe(true);
    expect(
      (reply?.result?.["assistantMessage"] as {content: string}).content,
    ).toBe("When does it crash?");
  });

  test.each(["bug_report", "feature_request", "support_request"])(
    "starts the report type chosen on the page: %s",
    async (category) => {
      // When
      await send(createBridge(), {
        id: 1,
        type: "startConversation",
        text: "It crashes.",
        category,
      });

      // Then
      expect(
        (nevaBridge.calls[0]!.args["input"] as {category: string}).category,
      ).toBe(category);
    },
  );

  test("refuses a report type the API does not offer", async () => {
    // When
    const reply = await send(createBridge(), {
      id: 6,
      type: "startConversation",
      text: "It crashes.",
      category: "generic_intake",
    });

    // Then
    expect(nevaBridge.calls).toHaveLength(0);
    expect(reply?.error?.name).toBe("InvalidRequest");
  });

  test("gives no bridge to a page served without https from another machine", () => {
    // When
    const create = (): unknown =>
      createWebChatBridge({
        conversations: nevaBridge,
        productId: PRODUCT_ID,
        reporter: {id: "jdoe"},
        pageUrl: "http://chat.example.com/index.html",
      });

    // Then
    expect(create).toThrow("https");
  });

  test("ignores a message from another origin without calling NevaBridge", async () => {
    // When
    const reply = await send(
      createBridge(),
      {id: 1, type: "startConversation", text: "hi"},
      "https://attacker.example/index.html",
    );

    // Then
    expect(reply).toBeNull();
    expect(nevaBridge.calls).toHaveLength(0);
  });

  test("continues a conversation this page started", async () => {
    // Given
    const bridge = createBridge();
    await send(bridge, {id: 1, type: "startConversation", text: "It crashes."});

    // When
    const reply = await send(bridge, {
      id: 2,
      type: "appendMessage",
      conversationId: CONVERSATION_ID,
      text: "On save.",
    });

    // Then
    expect(nevaBridge.calls[1]).toEqual({
      operation: "append",
      args: {conversationId: CONVERSATION_ID, text: "On save."},
    });
    expect(reply?.ok).toBe(true);
  });

  test.each([
    {id: 7, type: "appendMessage", conversationId: CONVERSATION_ID, text: "hi"},
    {
      id: 7,
      type: "submitReport",
      conversationId: CONVERSATION_ID,
      reportId: REPORT_ID,
    },
    {id: 7, type: "attachFile", conversationId: CONVERSATION_ID},
  ])("refuses a conversation this page did not start: %o", async (message) => {
    // When
    const reply = await send(createBridge(), message);

    // Then
    expect(nevaBridge.calls).toHaveLength(0);
    expect(reply?.error?.name).toBe("UnknownConversation");
  });

  test("submits a report of a started conversation and returns the outcome", async () => {
    // Given
    const bridge = createBridge();
    await send(bridge, {id: 1, type: "startConversation", text: "It crashes."});

    // When
    const reply = await send(bridge, {
      id: 2,
      type: "submitReport",
      conversationId: CONVERSATION_ID,
      reportId: REPORT_ID,
    });

    // Then
    expect(nevaBridge.calls[1]?.args["reportId"]).toBe(REPORT_ID);
    const result = reply?.result as {
      report: {status: string};
      connectors: {github: {ticket: string}};
    };
    expect(result.report.status).toBe("submitted");
    expect(result.connectors.github.ticket).toBe(
      "https://github.com/acme/app/issues/9",
    );
  });

  test("tells the page the tenant's features", async () => {
    // Given
    nevaBridge.features = {chatAttachments: true, knowledgebase: false};

    // When
    const reply = await send(createBridge(), {id: 3, type: "getFeatures"});

    // Then
    expect(reply?.result).toEqual({
      chatAttachments: true,
      knowledgebase: false,
    });
  });

  test("attaches the file the user picked, uploaded as the reporter", async () => {
    // Given
    const bridge = createBridge();
    await send(bridge, {id: 1, type: "startConversation", text: "It crashes."});
    const bytes = new TextEncoder().encode("error at line 7");
    nextPickedFile = {
      name: "app.log",
      sizeBytes: bytes.length,
      read: async () => new Blob([bytes]),
    };

    // When
    const reply = await send(bridge, {
      id: 4,
      type: "attachFile",
      conversationId: CONVERSATION_ID,
    });

    // Then
    const call = nevaBridge.calls[1]!;
    expect(call.operation).toBe("attach");
    expect(call.args["uploaderId"]).toBe("jdoe@corp.example");
    expect(call.args["bytes"]).toEqual(bytes);
    expect(reply?.result?.["fileName"]).toBe("app.log");
  });

  test("attaches nothing when the user closes the file picker", async () => {
    // Given
    const bridge = createBridge();
    await send(bridge, {id: 1, type: "startConversation", text: "It crashes."});

    // When
    const reply = await send(bridge, {
      id: 4,
      type: "attachFile",
      conversationId: CONVERSATION_ID,
    });

    // Then
    expect(nevaBridge.calls).toHaveLength(1);
    expect(reply?.ok).toBe(true);
    expect(reply?.result).toBeNull();
  });

  test("does not offer attachments when the host has no file picker", async () => {
    // Given
    const bridge = createBridge(false);
    await send(bridge, {id: 1, type: "startConversation", text: "It crashes."});

    // When
    const reply = await send(bridge, {
      id: 4,
      type: "attachFile",
      conversationId: CONVERSATION_ID,
    });

    // Then
    expect(reply?.error?.name).toBe("UnsupportedOperation");
  });

  test("reports attachments as off when the host has no file picker", async () => {
    // Given
    nevaBridge.features = {chatAttachments: true, knowledgebase: true};

    // When
    const reply = await send(createBridge(false), {id: 3, type: "getFeatures"});

    // Then
    expect(reply?.result).toEqual({
      chatAttachments: false,
      knowledgebase: true,
    });
  });

  test("turns a picked file that cannot be read into a chat error", async () => {
    // Given
    const bridge = createBridge();
    await send(bridge, {id: 1, type: "startConversation", text: "It crashes."});
    pickerFailure = Object.assign(new Error("ENOENT: no such file"), {
      code: "ENOENT",
    });

    // When
    const reply = await send(bridge, {
      id: 4,
      type: "attachFile",
      conversationId: CONVERSATION_ID,
    });

    // Then
    expect(reply?.error?.name).toBe("FileUnreadable");
  });

  test("turns an answer from NevaBridge that cannot be read into a chat error", async () => {
    // Given
    nevaBridge.failWith = new SyntaxError("Unexpected end of JSON input");

    // When
    const reply = await send(createBridge(), {
      id: 3,
      type: "startConversation",
      text: "hi",
    });

    // Then
    expect(reply?.error?.name).toBe("InvalidResponse");
  });

  test("passes an API error to the page as a structured error", async () => {
    // Given
    nevaBridge.failWith = new NevaBridgeApiError(429, new Headers(), {
      error: "ModelInvocationFailed",
      message: "The writer model is throttled.",
      category: "throttled",
    });

    // When
    const reply = await send(createBridge(), {
      id: 3,
      type: "startConversation",
      text: "hi",
    });

    // Then
    expect(reply?.ok).toBe(false);
    expect(reply?.error).toEqual({
      name: "ModelInvocationFailed",
      message: "The writer model is throttled.",
      status: 429,
      retryable: true,
    });
  });

  test.each([
    ['{"id":4,"type":"listConversations"}', 4, "UnsupportedOperation"],
    ['{"id":5,"type":"startConversation","text":"   "}', 5, "InvalidRequest"],
    ["not json", 0, "InvalidRequest"],
  ])(
    "refuses requests outside the protocol: %s",
    async (message, expectedId, expectedError) => {
      // When
      const reply = await send(createBridge(), message);

      // Then
      expect(nevaBridge.calls).toHaveLength(0);
      expect(reply?.id).toBe(expectedId);
      expect(reply?.error?.name).toBe(expectedError);
    },
  );
});
