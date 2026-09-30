import {afterEach, beforeEach, describe, expect, test} from "bun:test";
import type {Server} from "bun";
import {createSdkConversations} from "../src/conversations.js";
import {createSettings} from "../src/settings.js";

const API_KEY = "sk_test_not_a_real_key";
const CONVERSATION_ID = "conversation-00000000-0000-4000-8000-000000000002";

interface Recorded {
  readonly method: string;
  readonly path: string;
  readonly headers: Headers;
  readonly body: string;
}

let server: Server<undefined>;
let requests: Recorded[];
let responses: Array<{status: number; body: string}>;

beforeEach(() => {
  requests = [];
  responses = [];
  server = Bun.serve({
    hostname: "127.0.0.1",
    port: 0,
    async fetch(request) {
      const url = new URL(request.url);
      requests.push({
        method: request.method,
        path: url.pathname + url.search,
        headers: request.headers,
        body: await request.text(),
      });
      const next = responses.shift() ?? {
        status: 500,
        body: '{"error":"NoCannedResponse","message":"none"}',
      };
      return new Response(next.body, {
        status: next.status,
        headers: {"content-type": "application/json"},
      });
    },
  });
});

afterEach(() => {
  server.stop(true);
});

function conversations(): ReturnType<typeof createSdkConversations> {
  return createSdkConversations(
    createSettings({
      apiBaseUrl: `http://127.0.0.1:${server.port}`,
      productId: "product-1",
      apiKey: API_KEY,
    }),
  );
}

describe("SDK-backed conversations", () => {
  test("read the tenant's features", async () => {
    // Given
    responses.push({
      status: 200,
      body: '{"chatAttachments":true,"knowledgebase":false}',
    });

    // When
    const features = await conversations().getFeatures();

    // Then
    expect(requests[0]?.method).toBe("GET");
    expect(requests[0]?.path).toBe("/v1/features");
    expect(requests[0]?.headers.get("authorization")).toBe(`Bearer ${API_KEY}`);
    expect(features).toEqual({chatAttachments: true, knowledgebase: false});
  });

  test("report every feature off when the API has no features operation", async () => {
    // Given
    responses.push({
      status: 404,
      body: '{"error":"RouteNotFound","message":"Route does not exist"}',
    });

    // When
    const features = await conversations().getFeatures();

    // Then
    expect(features).toEqual({});
  });

  test("attach a file: request an upload, upload the bytes without the key, confirm", async () => {
    // Given
    const bytes = new TextEncoder().encode("error at line 7");
    responses.push({
      status: 200,
      body: JSON.stringify({
        attachmentId: "attachment-1",
        uploadUrl: `http://127.0.0.1:${server.port}/upload/attachment-1?signature=abc`,
        expiresAt: "2026-09-30T10:15:00Z",
      }),
    });
    responses.push({status: 200, body: ""});
    responses.push({
      status: 200,
      body: JSON.stringify({
        id: "attachment-1",
        fileName: "app.log",
        kind: "text",
        sizeBytes: bytes.length,
        uploadedBy: "api:jdoe",
        uploadedAt: "2026-09-30T10:02:00Z",
      }),
    });

    // When
    const attachment = await conversations().attachFile(
      CONVERSATION_ID,
      "jdoe",
      {
        name: "app.log",
        sizeBytes: bytes.length,
        read: async () => new Blob([bytes]),
      },
    );

    // Then
    expect(requests).toHaveLength(3);
    expect(requests[0]?.path).toBe(
      `/v1/conversations/${CONVERSATION_ID}/actions/request-attachment-upload`,
    );
    expect(requests[0]?.headers.get("x-actor-id")).toBe("jdoe");
    expect(JSON.parse(requests[0]!.body)).toEqual({
      fileName: "app.log",
      sizeBytes: bytes.length,
    });

    expect(requests[1]?.method).toBe("PUT");
    expect(requests[1]?.path).toBe("/upload/attachment-1?signature=abc");
    expect(requests[1]?.headers.get("if-none-match")).toBe("*");
    expect(requests[1]?.headers.get("authorization")).toBeNull();
    expect(requests[1]?.body).toBe("error at line 7");

    expect(requests[2]?.path).toBe(
      `/v1/conversations/${CONVERSATION_ID}/actions/confirm-attachment-upload`,
    );
    expect(JSON.parse(requests[2]!.body)).toEqual({
      attachmentId: "attachment-1",
    });
    expect(attachment.fileName).toBe("app.log");
  });

  test("start a conversation with the reporter's identity headers", async () => {
    // Given
    responses.push({
      status: 201,
      body: JSON.stringify({
        userMessage: {
          id: "m-1",
          conversationId: CONVERSATION_ID,
          role: "user",
          content: "hi",
          createdAt: "2026-09-30T10:00:00Z",
        },
        assistantMessage: {
          id: "m-2",
          conversationId: CONVERSATION_ID,
          role: "assistant",
          content: "Hello",
          createdAt: "2026-09-30T10:00:01Z",
        },
        reports: [],
      }),
    });

    // When
    const turn = await conversations().startConversation(
      "product-1",
      {id: "jdoe", displayName: "Jürgen Doe", roles: ["tenant"]},
      {
        text: "hi",
        category: "bug_report",
        applicationContext: {screen: "Invoices"},
      },
    );

    // Then
    expect(requests[0]?.path).toBe("/v1/products/product-1/conversations");
    expect(requests[0]?.headers.get("x-actor-id")).toBe("jdoe");
    expect(requests[0]?.headers.get("x-actor-roles")).toBe("tenant");
    const body = JSON.parse(requests[0]!.body) as Record<string, unknown>;
    expect(body["category"]).toBe("bug_report");
    expect(body["applicationContext"]).toEqual({screen: "Invoices"});
    expect(turn.assistantMessage.content).toBe("Hello");
  });
});
