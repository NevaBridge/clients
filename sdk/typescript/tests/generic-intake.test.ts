import {describe, expect, it} from "bun:test";
import {once} from "node:events";
import {createServer} from "node:http";
import {
  intakeTurnRunningId,
  NevaBridgeApiError,
  NevaBridgeClient,
} from "../src/nevabridge-client";
import type {IntakeTurnEvent} from "../src/intake-turn-events";

const conversationId = "conversation-11111111-1111-4111-8111-111111111111";
const turnId = "intaketurn-22222222-2222-4222-8222-222222222222";
const timestamp = "2026-11-03T12:00:00.000Z";

interface CapturedRequest {
  readonly method: string;
  readonly path: string;
  readonly query: URLSearchParams;
  readonly body: string;
  readonly headers: Headers;
}

interface FakeApi {
  readonly client: NevaBridgeClient;
  readonly requests: CapturedRequest[];
  stop(): Promise<void>;
}

function fakeApi(answer: (request: CapturedRequest) => Response): FakeApi {
  const requests: CapturedRequest[] = [];
  const server = Bun.serve({
    port: 0,
    async fetch(request): Promise<Response> {
      const url = new URL(request.url);
      // Local tooling may probe a freshly opened port; such a request must not
      // consume a scripted answer.
      if (!url.pathname.startsWith("/v1/")) {
        return new Response(null, {status: 404});
      }
      const captured: CapturedRequest = {
        method: request.method,
        path: url.pathname,
        query: url.searchParams,
        body: await request.text(),
        headers: request.headers,
      };
      requests.push(captured);
      return answer(captured);
    },
  });
  return {
    client: new NevaBridgeClient({
      baseUrl: server.url.toString(),
      tokenProvider: () => "token",
    }),
    requests,
    async stop(): Promise<void> {
      await server.stop(true);
    },
  };
}

function eventsAnswers(
  pages: readonly (Record<string, unknown> | Response)[],
): (request: CapturedRequest) => Response {
  let next = 0;
  return () => {
    const page = pages[next];
    next += 1;
    if (page === undefined) {
      return Response.json(
        {error: "Unexpected", message: "No page left."},
        {status: 500},
      );
    }
    return page instanceof Response ? page : Response.json(page);
  };
}

function apiError(value: unknown): NevaBridgeApiError {
  if (!(value instanceof NevaBridgeApiError)) {
    throw new Error(`Expected a NevaBridgeApiError, got ${String(value)}.`);
  }
  return value;
}

function edgeFailure(status: 502 | 504): Response {
  return Response.json({message: "Endpoint request timed out"}, {status});
}

describe("NevaBridgeClient Generic Intake", () => {
  it("starts a Generic Intake conversation with the actor headers and returns its first turn", async () => {
    // Given
    const api = fakeApi(() =>
      Response.json({conversationId, turnId}, {status: 202}),
    );

    try {
      // When
      const started = await api.client.startIntakeConversation({
        productId: "product-1",
        actorId: "reporter-1",
        actorName: "Jörg Example",
        actorRoles: ["customer"],
        request: {
          userMessage: {content: "Saving an invoice shows an error."},
          reporterContact: {email: "reporter@example.com"},
        },
      });

      // Then
      expect(started).toEqual({conversationId, turnId});
      const [request] = api.requests;
      expect(request?.method).toBe("POST");
      expect(request?.path).toBe("/v1/products/product-1/intake-conversations");
      expect(request?.headers.get("x-actor-id")).toBe("reporter-1");
      expect(request?.headers.get("x-actor-name")).toBe(
        encodeURIComponent("Jörg Example"),
      );
      expect(request?.headers.get("x-actor-roles")).toBe("customer");
      expect(JSON.parse(request?.body ?? "")).toEqual({
        userMessage: {content: "Saving an invoice shows an error."},
        reporterContact: {email: "reporter@example.com"},
      });
    } finally {
      await api.stop();
    }
  });

  it("starts the next turn with the expected revision and returns its turn", async () => {
    // Given
    const api = fakeApi(() => Response.json({turnId}, {status: 202}));

    try {
      // When
      const started = await api.client.startIntakeTurn({
        conversationId,
        request: {content: "That is all.", expectedRevision: 3, finish: true},
      });

      // Then
      expect(started).toEqual({turnId});
      const [request] = api.requests;
      expect(request?.method).toBe("POST");
      expect(request?.path).toBe(
        `/v1/intake-conversations/${conversationId}/turns`,
      );
      expect(JSON.parse(request?.body ?? "")).toEqual({
        content: "That is all.",
        expectedRevision: 3,
        finish: true,
      });
    } finally {
      await api.stop();
    }
  });

  it("reads one page of turn events with the cursor and timing, keeping unknown event types apart", async () => {
    // Given
    const api = fakeApi(() =>
      Response.json({
        events: [
          {type: "status", phase: "tool", tool: "searchKnowledge"},
          {type: "sparkle", intensity: 3},
          {type: "text", delta: "Does it happen "},
          {type: "done", revision: 4},
        ],
        cursor: "cursor-2",
        finished: true,
      }),
    );

    try {
      // When
      const page = await api.client.getIntakeTurnEvents({
        turnId,
        after: "cursor-1",
        wait: 5,
        linger: 250,
      });

      // Then
      expect(page).toEqual({
        events: [
          {type: "status", phase: "tool", tool: "searchKnowledge"},
          {
            type: "unknown",
            eventType: "sparkle",
            payload: {type: "sparkle", intensity: 3},
          },
          {type: "text", delta: "Does it happen "},
          {type: "done", revision: 4},
        ],
        cursor: "cursor-2",
        finished: true,
      });
      const [request] = api.requests;
      expect(request?.path).toBe(`/v1/intake-turns/${turnId}/events`);
      expect(request?.query.get("after")).toBe("cursor-1");
      expect(request?.query.get("wait")).toBe("5");
      expect(request?.query.get("linger")).toBe("250");
    } finally {
      await api.stop();
    }
  });

  it("rejects a known event type whose fields break the contract", async () => {
    // Given
    const api = fakeApi(() =>
      Response.json({
        events: [{type: "done", revision: "four"}],
        cursor: "cursor-2",
        finished: true,
      }),
    );

    try {
      // When
      const reading = api.client.getIntakeTurnEvents({turnId});

      // Then
      await expect(reading).rejects.toThrow("done");
    } finally {
      await api.stop();
    }
  });

  it("lists a product's Generic Intake conversations by reporter, one page at a time", async () => {
    // Given
    const api = fakeApi(() =>
      Response.json({
        conversations: [
          {
            id: conversationId,
            tenantId: "tenant-1",
            productId: "product-1",
            reporterId: "reporter-1",
            createdAt: timestamp,
            updatedAt: timestamp,
            status: "in_progress",
          },
        ],
        nextCursor: "page-2",
      }),
    );

    try {
      // When
      const list = await api.client.listIntakeConversations({
        productId: "product-1",
        reporterId: "reporter-1",
        cursor: "page-1",
        limit: 10,
      });

      // Then
      expect(list.conversations.map((summary) => summary.id)).toEqual([
        conversationId,
      ]);
      expect(list.nextCursor).toBe("page-2");
      const [request] = api.requests;
      expect(request?.path).toBe("/v1/products/product-1/intake-conversations");
      expect(request?.query.get("reporterId")).toBe("reporter-1");
      expect(request?.query.get("cursor")).toBe("page-1");
      expect(request?.query.get("limit")).toBe("10");
    } finally {
      await api.stop();
    }
  });

  it("reads the tenant's features", async () => {
    // Given
    const api = fakeApi(() =>
      Response.json({
        chatAttachments: false,
        knowledgebase: true,
        genericIntake: true,
      }),
    );

    try {
      // When
      const features = await api.client.getFeatures();

      // Then
      expect(features).toEqual({
        chatAttachments: false,
        knowledgebase: true,
        genericIntake: true,
      });
      expect(api.requests[0]?.path).toBe("/v1/features");
    } finally {
      await api.stop();
    }
  });

  it("reads the running turn and revision of a Generic Intake conversation", async () => {
    // Given
    const api = fakeApi(() =>
      Response.json({
        id: conversationId,
        tenantId: "tenant-1",
        productId: "product-1",
        reporterId: "reporter-1",
        createdAt: timestamp,
        updatedAt: timestamp,
        status: "in_progress",
        reports: [],
        messages: [],
        revision: 3,
        activeTurnId: turnId,
      }),
    );

    try {
      // When
      const detail = await api.client.getConversation({conversationId});

      // Then
      expect(detail.revision).toBe(3);
      expect(detail.activeTurnId).toBe(turnId);
    } finally {
      await api.stop();
    }
  });

  describe("waitForIntakeTurn", () => {
    it("polls with each cursor, reports every event in order, and resolves with the new revision", async () => {
      // Given
      const api = fakeApi(
        eventsAnswers([
          {events: [], cursor: "cursor-0", finished: false},
          {
            events: [
              {type: "status", phase: "planning"},
              {type: "future-progress", percent: 50},
              {type: "text", delta: "Does it happen "},
            ],
            cursor: "cursor-2",
            finished: false,
          },
          {
            events: [
              {type: "text", delta: "for every invoice?"},
              {type: "done", revision: 1},
            ],
            cursor: "cursor-4",
            finished: true,
          },
        ]),
      );
      const seen: IntakeTurnEvent[] = [];

      try {
        // When
        const completion = await api.client.waitForIntakeTurn({
          turnId,
          wait: 2,
          linger: 100,
          onEvent: (event) => seen.push(event),
        });

        // Then
        expect(completion).toEqual({revision: 1});
        expect(seen).toEqual([
          {type: "status", phase: "planning"},
          {
            type: "unknown",
            eventType: "future-progress",
            payload: {type: "future-progress", percent: 50},
          },
          {type: "text", delta: "Does it happen "},
          {type: "text", delta: "for every invoice?"},
          {type: "done", revision: 1},
        ]);
        expect(
          api.requests.map((request) => request.query.get("after")),
        ).toEqual([null, "cursor-0", "cursor-2"]);
        expect(
          api.requests.map((request) => request.query.get("wait")),
        ).toEqual(["2", "2", "2"]);
        expect(
          api.requests.map((request) => request.query.get("linger")),
        ).toEqual(["100", "100", "100"]);
      } finally {
        await api.stop();
      }
    });

    it("rejects with the turn's failure as an API error with its status code", async () => {
      // Given
      const failure = {
        error: "IntakeTurnTimedOut",
        message: "The turn did not finish in time and changed nothing.",
        statusCode: 408,
      };
      const api = fakeApi(
        eventsAnswers([
          {
            events: [
              {type: "status", phase: "planning"},
              {type: "error", error: failure},
            ],
            cursor: "cursor-2",
            finished: true,
          },
        ]),
      );

      try {
        // When
        const waiting = api.client.waitForIntakeTurn({turnId});

        // Then
        const error: unknown = await waiting.catch((caught: unknown) => caught);
        expect(apiError(error).status).toBe(408);
        expect(apiError(error).detail).toEqual(failure);
      } finally {
        await api.stop();
      }
    });

    it("carries a model failure of the turn with its provider and role", async () => {
      // Given
      const failure = {
        error: "ModelInvocationFailed",
        message: "The writer model is unavailable.",
        category: "access-denied",
        provider: "bedrock",
        role: "writer",
        catalogKey: "amazon-nova-pro",
        statusCode: 424,
      };
      const api = fakeApi(
        eventsAnswers([
          {
            events: [{type: "error", error: failure}],
            cursor: "cursor-1",
            finished: true,
          },
        ]),
      );

      try {
        // When
        const error = await api.client
          .waitForIntakeTurn({turnId})
          .catch((caught: unknown) => caught);

        // Then
        expect(apiError(error).status).toBe(424);
        expect(apiError(error).detail).toEqual(failure);
      } finally {
        await api.stop();
      }
    });

    it("reads the same cursor again after an edge failure and continues", async () => {
      // Given
      const api = fakeApi(
        eventsAnswers([
          {
            events: [{type: "status", phase: "planning"}],
            cursor: "cursor-1",
            finished: false,
          },
          edgeFailure(504),
          {
            events: [{type: "done", revision: 2}],
            cursor: "cursor-2",
            finished: true,
          },
        ]),
      );

      try {
        // When
        const completion = await api.client.waitForIntakeTurn({turnId});

        // Then
        expect(completion).toEqual({revision: 2});
        expect(
          api.requests.map((request) => request.query.get("after")),
        ).toEqual([null, "cursor-1", "cursor-1"]);
      } finally {
        await api.stop();
      }
    });

    it("retries a poll whose response breaks off mid-body", async () => {
      // Given
      let polls = 0;
      const server = createServer((request, response) => {
        if (!request.url?.startsWith("/v1/")) {
          response.writeHead(404).end();
          return;
        }
        polls += 1;
        if (polls === 1) {
          response.writeHead(200, {
            "content-length": "100",
            "content-type": "application/json",
          });
          response.flushHeaders();
          response.write('{"events":');
          setTimeout(() => response.destroy(), 10);
          return;
        }
        response.writeHead(200, {"content-type": "application/json"});
        response.end(
          JSON.stringify({
            events: [{type: "done", revision: 5}],
            cursor: "cursor-1",
            finished: true,
          }),
        );
      });
      server.listen(0, "127.0.0.1");
      await once(server, "listening");
      const address = server.address();
      if (address === null || typeof address === "string") {
        throw new Error("Expected a local TCP server address.");
      }
      const client = new NevaBridgeClient({
        baseUrl: `http://127.0.0.1:${address.port}`,
        tokenProvider: () => "token",
      });

      try {
        // When
        const completion = await client.waitForIntakeTurn({turnId});

        // Then
        expect(completion).toEqual({revision: 5});
        expect(polls).toBe(2);
      } finally {
        server.closeAllConnections();
        server.close();
      }
    });

    it("gives up after three retries of failed polls", async () => {
      // Given
      const api = fakeApi(
        eventsAnswers([
          edgeFailure(502),
          edgeFailure(504),
          edgeFailure(502),
          edgeFailure(504),
          {
            events: [{type: "done", revision: 1}],
            cursor: "cursor-1",
            finished: true,
          },
        ]),
      );

      try {
        // When
        const error = await api.client
          .waitForIntakeTurn({turnId})
          .catch((caught: unknown) => caught);

        // Then
        expect(apiError(error).status).toBe(504);
        expect(api.requests).toHaveLength(4);
      } finally {
        await api.stop();
      }
    });

    it("rejects at once when a poll fails for any other reason", async () => {
      // Given
      const api = fakeApi(
        eventsAnswers([
          Response.json(
            {error: "BadRequest", message: "Unknown turn."},
            {status: 400},
          ),
          {
            events: [{type: "done", revision: 1}],
            cursor: "cursor-1",
            finished: true,
          },
        ]),
      );

      try {
        // When
        const error = await api.client
          .waitForIntakeTurn({turnId})
          .catch((caught: unknown) => caught);

        // Then
        expect(apiError(error).status).toBe(400);
        expect(api.requests).toHaveLength(1);
      } finally {
        await api.stop();
      }
    });

    it("rejects at once when a poll answers with a body that is not JSON", async () => {
      // Given
      const api = fakeApi(
        eventsAnswers([
          new Response("<html>Service</html>", {
            headers: {"content-type": "application/json"},
          }),
          {
            events: [{type: "done", revision: 1}],
            cursor: "cursor-1",
            finished: true,
          },
        ]),
      );

      try {
        // When
        const error: unknown = await api.client
          .waitForIntakeTurn({turnId})
          .catch((caught: unknown) => caught);

        // Then
        expect(error).toBeInstanceOf(SyntaxError);
        expect(api.requests).toHaveLength(1);
      } finally {
        await api.stop();
      }
    });

    it("stops polling when its signal is aborted", async () => {
      // Given
      const controller = new AbortController();
      const api = fakeApi(
        eventsAnswers([
          {
            events: [{type: "status", phase: "planning"}],
            cursor: "cursor-1",
            finished: false,
          },
          {
            events: [{type: "done", revision: 1}],
            cursor: "cursor-2",
            finished: true,
          },
        ]),
      );

      try {
        // When
        const error = await api.client
          .waitForIntakeTurn({
            turnId,
            signal: controller.signal,
            onEvent: () => controller.abort(),
          })
          .catch((caught: unknown) => caught);

        // Then
        expect(error instanceof Error && error.name).toBe("AbortError");
        expect(api.requests).toHaveLength(1);
      } finally {
        await api.stop();
      }
    });

    it("stops waiting when its signal is aborted during the pause before a retry", async () => {
      // Given
      const controller = new AbortController();
      const api = fakeApi(
        eventsAnswers([
          edgeFailure(504),
          {
            events: [{type: "done", revision: 1}],
            cursor: "cursor-1",
            finished: true,
          },
        ]),
      );

      try {
        // When
        const waiting = api.client.waitForIntakeTurn({
          turnId,
          signal: controller.signal,
        });
        setTimeout(() => controller.abort(), 100);
        const error: unknown = await waiting.catch((caught: unknown) => caught);

        // Then
        expect(error instanceof Error && error.name).toBe("AbortError");
        expect(api.requests).toHaveLength(1);
      } finally {
        await api.stop();
      }
    });
  });

  describe("intakeTurnRunningId", () => {
    it("names the running turn when a turn is refused because another one runs", async () => {
      // Given
      const api = fakeApi(() =>
        Response.json(
          {
            error: "IntakeTurnRunning",
            message: "Another turn of this conversation is running.",
            turnId,
          },
          {status: 409},
        ),
      );

      try {
        // When
        const error = await api.client
          .startIntakeTurn({
            conversationId,
            request: {content: "Hello again", expectedRevision: 1},
          })
          .catch((caught: unknown) => caught);

        // Then
        expect(intakeTurnRunningId(error)).toBe(turnId);
      } finally {
        await api.stop();
      }
    });

    it("names no turn for any other refusal", async () => {
      // Given
      const api = fakeApi(() =>
        Response.json(
          {
            error: "RevisionConflict",
            message: "The conversation has moved on.",
          },
          {status: 409},
        ),
      );

      try {
        // When
        const error = await api.client
          .startIntakeTurn({
            conversationId,
            request: {content: "Hello again", expectedRevision: 1},
          })
          .catch((caught: unknown) => caught);

        // Then
        expect(error).toBeInstanceOf(NevaBridgeApiError);
        expect(intakeTurnRunningId(error)).toBeUndefined();
        expect(intakeTurnRunningId(new Error("network"))).toBeUndefined();
      } finally {
        await api.stop();
      }
    });
  });
});
