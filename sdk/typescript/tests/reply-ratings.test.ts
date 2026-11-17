import {describe, expect, it} from "bun:test";
import {NevaBridgeApiError, NevaBridgeClient} from "../src/nevabridge-client";

const timestamp = "2026-11-16T12:00:00.000Z";
const conversationId = "conversation-0f1e2d3c-4b5a-4968-8776-655443322110";
const messageId = "message-1a2b3c4d-5e6f-4a7b-8c9d-0e1f2a3b4c5d";

interface CapturedRequest {
  readonly method: string;
  readonly path: string;
  readonly actorId: string | null;
  readonly body: string;
}

function assistantMessage(): Record<string, unknown> {
  return {
    id: messageId,
    conversationId,
    role: "assistant",
    content: "Restart the export.",
    createdAt: timestamp,
    rating: {canChange: true, value: "up"},
  };
}

async function withServer(
  respond: (request: Request) => Response,
  run: (client: NevaBridgeClient) => Promise<void>,
): Promise<CapturedRequest[]> {
  const requests: CapturedRequest[] = [];
  const server = Bun.serve({
    port: 0,
    async fetch(request): Promise<Response> {
      requests.push({
        method: request.method,
        path: new URL(request.url).pathname,
        actorId: request.headers.get("x-actor-id"),
        body: await request.clone().text(),
      });
      return respond(request);
    },
  });
  try {
    await run(
      new NevaBridgeClient({
        baseUrl: server.url.toString(),
        tokenProvider: () => "token",
      }),
    );
  } finally {
    server.stop(true);
  }
  return requests;
}

describe("NevaBridgeClient reply ratings", () => {
  it("rates a reply as the named user and returns the stored rating", async () => {
    // GIVEN
    let rating: unknown;

    // WHEN
    const requests = await withServer(
      () =>
        Response.json({
          conversationId,
          messageId,
          value: "down",
          comment: "Asked twice.",
          ratedAt: timestamp,
        }),
      async (client) => {
        rating = await client.setReplyRating({
          conversationId,
          messageId,
          actorId: "user-4821",
          request: {rating: "down", comment: "Asked twice."},
        });
      },
    );

    // THEN
    expect(requests).toEqual([
      {
        method: "PUT",
        path: `/v1/conversations/${conversationId}/messages/${messageId}/rating`,
        actorId: "user-4821",
        body: JSON.stringify({rating: "down", comment: "Asked twice."}),
      },
    ]);
    expect(rating).toEqual({
      conversationId,
      messageId,
      value: "down",
      comment: "Asked twice.",
      ratedAt: new Date(timestamp),
    });
  });

  it("returns the stored rating as the API's JSON", async () => {
    // GIVEN
    let rating: unknown;

    // WHEN
    await withServer(
      () =>
        Response.json({
          conversationId,
          messageId,
          value: "up",
          ratedAt: timestamp,
        }),
      async (client) => {
        rating = await client.json.setReplyRating({
          conversationId,
          messageId,
          actorId: "user-4821",
          request: {rating: "up"},
        });
      },
    );

    // THEN
    expect(rating).toEqual({
      conversationId,
      messageId,
      value: "up",
      ratedAt: timestamp,
    });
  });

  it("clears a reply's rating as the named user", async () => {
    // GIVEN
    let cleared: unknown = "not called";

    // WHEN
    const requests = await withServer(
      () => new Response(null, {status: 204}),
      async (client) => {
        cleared = await client.clearReplyRating({
          conversationId,
          messageId,
          actorId: "user-4821",
        });
      },
    );

    // THEN
    expect(requests).toEqual([
      {
        method: "DELETE",
        path: `/v1/conversations/${conversationId}/messages/${messageId}/rating`,
        actorId: "user-4821",
        body: "",
      },
    ]);
    expect(cleared).toBeUndefined();
  });

  it("fails with the API's error when someone else's conversation is rated", async () => {
    // GIVEN
    let failure: unknown;

    // WHEN
    await withServer(
      () =>
        Response.json(
          {error: "Forbidden", message: "Only the participant may rate."},
          {status: 403},
        ),
      async (client) => {
        failure = await client
          .setReplyRating({
            conversationId,
            messageId,
            actorId: "user-9999",
            request: {rating: "up"},
          })
          .catch((error: unknown) => error);
      },
    );

    // THEN
    expect(failure).toBeInstanceOf(NevaBridgeApiError);
    expect((failure as NevaBridgeApiError).status).toBe(403);
  });

  it("names the reader on conversation reads and appends, so their own ratings are changeable", async () => {
    // GIVEN
    let detailRating: unknown;

    // WHEN
    const requests = await withServer(
      (request) =>
        request.method === "GET"
          ? Response.json({
              id: conversationId,
              tenantId: "tenant-1",
              productId: "product-1",
              reporterId: "user-4821",
              createdAt: timestamp,
              updatedAt: timestamp,
              messages: [assistantMessage()],
              reports: [],
            })
          : Response.json({
              userMessage: {
                ...assistantMessage(),
                role: "user",
                rating: undefined,
              },
              assistantMessage: assistantMessage(),
              reports: [],
            }),
      async (client) => {
        const detail = await client.getConversation({
          conversationId,
          actorId: "user-4821",
        });
        detailRating = detail.messages[0]?.rating;
        await client.appendMessage({
          conversationId,
          actorId: "user-4821",
          request: {content: "It still fails."},
        });
        await client.getConversation({conversationId});
      },
    );

    // THEN
    expect(requests.map((request) => request.actorId)).toEqual([
      "user-4821",
      "user-4821",
      null,
    ]);
    expect(detailRating).toEqual({canChange: true, value: "up"});
  });
});
