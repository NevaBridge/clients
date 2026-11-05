import {describe, expect, it} from "bun:test";
import {NevaBridgeApiError, NevaBridgeClient} from "../src/nevabridge-client";

const conversationId = "conversation-11111111-1111-4111-8111-111111111111";
const turnId = "intaketurn-22222222-2222-4222-8222-222222222222";

interface Sent {
  readonly method: string;
  readonly url: string;
  readonly headers: Headers;
  readonly body: string;
}

function clientAnswering(answer: (sent: Sent) => Response): {
  readonly client: NevaBridgeClient;
  readonly sent: Sent[];
} {
  const sent: Sent[] = [];
  const client = new NevaBridgeClient({
    baseUrl: "https://api.nevabridge.test/",
    tokenProvider: () => "token",
    fetch: async (input, init) => {
      const request = new Request(input, init);
      const recorded: Sent = {
        method: request.method,
        url: request.url,
        headers: request.headers,
        body: await request.text(),
      };
      sent.push(recorded);
      return answer(recorded);
    },
  });
  return {client, sent};
}

describe("NevaBridgeClient transport", () => {
  it("sends every request through the fetch it was given", async () => {
    // Given
    const {client, sent} = clientAnswering(() =>
      Response.json({chatAttachments: true}),
    );

    // When
    await client.getFeatures();

    // Then
    expect(sent.map((request) => `${request.method} ${request.url}`)).toEqual([
      "GET https://api.nevabridge.test/v1/features",
    ]);
    expect(sent[0]?.headers.get("authorization")).toBe("Bearer token");
  });

  it.each([
    ["a printable ASCII name unchanged", "Dana Example", "Dana Example"],
    ["a non-ASCII name percent-encoded", "Jörg Example", "J%C3%B6rg%20Example"],
    [
      "a name with a percent sign percent-encoded",
      "100% Dana",
      "100%25%20Dana",
    ],
  ])("sends %s as X-Actor-Name", async (_label, actorName, header) => {
    // Given
    const {client, sent} = clientAnswering(() =>
      Response.json({conversationId, turnId}, {status: 202}),
    );

    // When
    await client.startIntakeConversation({
      productId: "product-1",
      actorId: "reporter-1",
      actorName,
      request: {userMessage: {content: "Hello"}},
    });

    // Then
    expect(sent[0]?.headers.get("x-actor-name")).toBe(header);
  });
});

describe("NevaBridgeClient JSON results", () => {
  it("returns the API's JSON unchanged, with fields the SDK does not model", async () => {
    // Given
    const body = {
      id: conversationId,
      createdAt: "2026-11-03T12:00:00.000Z",
      revision: 2,
      activeTurnId: turnId,
      fieldFromALaterRevision: {nested: [1, null]},
    };
    const {client, sent} = clientAnswering(() => Response.json(body));

    // When
    const result = await client.json.getConversation({conversationId});

    // Then
    expect(result).toEqual(body);
    expect(sent[0]?.url).toBe(
      `https://api.nevabridge.test/v1/conversations/${conversationId}`,
    );
  });

  it("returns a page of turn events as the API sent it", async () => {
    // Given
    const page = {
      events: [{type: "future-progress", percent: 50}],
      cursor: "4",
      finished: false,
    };
    const {client, sent} = clientAnswering(() => Response.json(page));

    // When
    const result = await client.json.getIntakeTurnEvents({
      turnId,
      after: "3",
      wait: 20,
    });

    // Then
    expect(result).toEqual(page);
    expect(sent[0]?.url).toBe(
      `https://api.nevabridge.test/v1/intake-turns/${turnId}/events?after=3&wait=20`,
    );
  });

  it("fails like the typed operation when the API answers with an error", async () => {
    // Given
    const {client} = clientAnswering(() =>
      Response.json(
        {
          error: "IntakeTurnRunning",
          message: "Another turn of this conversation is running.",
          turnId,
        },
        {status: 409},
      ),
    );

    // When
    const failure = await client.json
      .startIntakeTurn({
        conversationId,
        request: {content: "Hello", expectedRevision: 1},
      })
      .catch((error: unknown) => error);

    // Then
    expect(failure).toBeInstanceOf(NevaBridgeApiError);
    expect((failure as NevaBridgeApiError).status).toBe(409);
    expect((failure as NevaBridgeApiError).detail).toEqual({
      error: "IntakeTurnRunning",
      message: "Another turn of this conversation is running.",
      turnId,
    });
  });
});
