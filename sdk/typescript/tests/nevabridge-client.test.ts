import {describe, expect, it} from "bun:test";
import {createServer} from "node:http";
import {once} from "node:events";
import {NevaBridgeApiError, NevaBridgeClient} from "../src/nevabridge-client";

const timestamp = "2026-08-23T12:00:00.000Z";

function message(role: "user" | "assistant"): Record<string, unknown> {
  return {
    id: `message-${role}`,
    conversationId: "conversation-1",
    role,
    content: role,
    createdAt: timestamp,
  };
}

function report(status = "reporting_in_progress"): Record<string, unknown> {
  return {
    id: "report-1",
    conversationId: "conversation-1",
    templateId: "template-1",
    templateVersion: "1",
    status,
    structured: {},
    createdAt: timestamp,
    updatedAt: timestamp,
  };
}

function responseFor(request: Request): Response {
  const url = new URL(request.url);
  if (url.pathname.endsWith("/submit")) {
    return Response.json({
      report: report("reported"),
      connectors: {
        github: {status: "success", ticket: "https://tracker.example/123"},
        slack: {status: "failed", error: "Webhook rejected the request."},
      },
    });
  }
  if (url.pathname.endsWith("/messages") || request.method === "POST") {
    return Response.json({
      userMessage: message("user"),
      assistantMessage: message("assistant"),
      reports: [report()],
      connectors: {
        github: {status: "success", ticket: "https://tracker.example/123"},
        slack: {status: "failed", error: "Webhook rejected the request."},
      },
    });
  }
  if (url.pathname === "/v1/products/product-1/conversations") {
    return Response.json([
      {
        id: "conversation-1",
        tenantId: "tenant-1",
        productId: "product-1",
        reporterId: "reporter-1",
        createdAt: timestamp,
        updatedAt: timestamp,
        status: "in_progress",
      },
    ]);
  }
  return Response.json({
    id: "conversation-1",
    tenantId: "tenant-1",
    productId: "product-1",
    reporterId: "reporter-1",
    createdAt: timestamp,
    updatedAt: timestamp,
    status: "in_progress",
    reports: [report()],
    messages: [message("user")],
  });
}

describe("NevaBridgeClient", () => {
  it("constructs all five requests and obtains a fresh token for every call", async () => {
    interface CapturedRequest {
      readonly authorization: string | null;
      readonly body: string;
      readonly method: string;
      readonly url: string;
      readonly actorId: string | null;
      readonly actorName: string | null;
      readonly actorRoles: string | null;
    }
    const requests: CapturedRequest[] = [];
    const server = Bun.serve({
      port: 0,
      async fetch(request): Promise<Response> {
        requests.push({
          authorization: request.headers.get("authorization"),
          body: await request.clone().text(),
          method: request.method,
          url: request.url,
          actorId: request.headers.get("x-actor-id"),
          actorName: request.headers.get("x-actor-name"),
          actorRoles: request.headers.get("x-actor-roles"),
        });
        return responseFor(request);
      },
    });
    let tokenNumber = 0;
    const client = new NevaBridgeClient({
      baseUrl: server.url.toString(),
      tokenProvider: async () => `token-${++tokenNumber}`,
    });

    try {
      const started = await client.startConversation({
        productId: "product-1",
        actorId: "reporter-1",
        actorName: "Jörg Example",
        actorRoles: ["anonymous", "customer"],
        request: {userMessage: {content: "First message"}},
      });
      await client.listConversations({
        productId: "product-1",
        reporterId: "reporter-1",
      });
      await client.getConversation({conversationId: "conversation-1"});
      await client.appendMessage({
        conversationId: "conversation-1",
        request: {content: "More detail"},
      });
      const submitted = await client.submitReport({
        conversationId: "conversation-1",
        reportId: "report-1",
      });
      expect(started.connectors).toEqual({
        github: {status: "success", ticket: "https://tracker.example/123"},
        slack: {status: "failed", error: "Webhook rejected the request."},
      });
      expect(submitted.connectors).toEqual({
        github: {status: "success", ticket: "https://tracker.example/123"},
        slack: {status: "failed", error: "Webhook rejected the request."},
      });
    } finally {
      server.stop(true);
    }

    expect(requests).toHaveLength(5);
    expect(
      requests.map(
        (request) => `${request.method} ${new URL(request.url).pathname}`,
      ),
    ).toEqual([
      "POST /v1/products/product-1/conversations",
      "GET /v1/products/product-1/conversations",
      "GET /v1/conversations/conversation-1",
      "POST /v1/conversations/conversation-1/messages",
      "POST /v1/conversations/conversation-1/reports/report-1/submit",
    ]);
    expect(requests.map((request) => request.authorization)).toEqual([
      "Bearer token-1",
      "Bearer token-2",
      "Bearer token-3",
      "Bearer token-4",
      "Bearer token-5",
    ]);
    expect(requests[0]?.actorId).toBe("reporter-1");
    expect(requests[0]?.actorName).toBe("J%C3%B6rg%20Example");
    expect(requests.map((request) => request.actorRoles)).toEqual([
      "anonymous,customer",
      null,
      null,
      null,
      null,
    ]);
    expect(new URL(requests[1]?.url ?? "").searchParams.get("reporterId")).toBe(
      "reporter-1",
    );
    expect(requests[0]?.body).toBe(
      JSON.stringify({userMessage: {content: "First message"}}),
    );
    expect(requests[3]?.body).toBe(JSON.stringify({content: "More detail"}));
    expect(requests[4]?.body).toBe("");
  });

  it("serializes the optional conversation category into the start request", async () => {
    let capturedBody = "";
    const server = Bun.serve({
      port: 0,
      async fetch(request): Promise<Response> {
        capturedBody = await request.clone().text();
        return responseFor(request);
      },
    });
    const client = new NevaBridgeClient({
      baseUrl: server.url.toString(),
      tokenProvider: async () => "token-1",
    });

    try {
      await client.startConversation({
        productId: "product-1",
        actorId: "reporter-1",
        request: {
          userMessage: {content: "How do I export my data?"},
          category: "support_request",
        },
      });
      expect(JSON.parse(capturedBody)).toEqual({
        userMessage: {content: "How do I export my data?"},
        category: "support_request",
      });
    } finally {
      await server.stop(true);
    }
  });

  it("preserves unknown enum wire values without throwing", async () => {
    const server = Bun.serve({
      port: 0,
      fetch(): Response {
        return Response.json({
          id: "conversation-1",
          tenantId: "tenant-1",
          productId: "product-1",
          reporterId: "reporter-1",
          createdAt: timestamp,
          updatedAt: timestamp,
          status: "future_status",
          reports: [report("future_report_status")],
          messages: [message("user")],
        });
      },
    });
    const client = new NevaBridgeClient({
      baseUrl: server.url.toString(),
      tokenProvider: () => "token",
    });

    try {
      const conversation = await client.getConversation({
        conversationId: "conversation-1",
      });
      expect(conversation.status).toBe("future_status");
      expect(conversation.reports[0]?.status).toBe("future_report_status");
    } finally {
      server.stop(true);
    }
  });

  it("throws a structured API error with safe response detail", async () => {
    const server = Bun.serve({
      port: 0,
      fetch(): Response {
        return Response.json(
          {error: "InvalidRequest", message: "The request is invalid."},
          {status: 400, headers: {"x-request-id": "request-1"}},
        );
      },
    });
    const client = new NevaBridgeClient({
      baseUrl: server.url.toString(),
      tokenProvider: () => "token",
    });

    try {
      await client.getConversation({conversationId: "missing"});
      throw new Error("Expected getConversation to fail.");
    } catch (error: unknown) {
      expect(error).toBeInstanceOf(NevaBridgeApiError);
      if (!(error instanceof NevaBridgeApiError)) {
        throw error;
      }
      expect(error.status).toBe(400);
      expect(error.headers.get("x-request-id")).toBe("request-1");
      expect(error.detail).toEqual({
        error: "InvalidRequest",
        message: "The request is invalid.",
      });
    } finally {
      server.stop(true);
    }
  });

  it("preserves parsed, plain-text, and malformed JSON error details", async () => {
    const responses = [
      new Response('{"message":"capitalized media type"}', {
        status: 400,
        headers: {"content-type": "Application/JSON"},
      }),
      new Response("Upstream unavailable", {
        status: 502,
        headers: {"content-type": "text/plain"},
      }),
      new Response('{"message":"truncated"', {
        status: 502,
        headers: {"content-type": "application/json"},
      }),
    ];
    const server = Bun.serve({
      port: 0,
      fetch(): Response {
        const response = responses.shift();
        if (!response) {
          throw new Error("No error response remains.");
        }
        return response;
      },
    });
    const client = new NevaBridgeClient({
      baseUrl: server.url.toString(),
      tokenProvider: () => "token",
    });

    try {
      for (const expectedDetail of [
        {message: "capitalized media type"},
        "Upstream unavailable",
        '{"message":"truncated"',
      ]) {
        try {
          await client.getConversation({conversationId: "missing"});
          throw new Error("Expected getConversation to fail.");
        } catch (error: unknown) {
          expect(error).toBeInstanceOf(NevaBridgeApiError);
          if (!(error instanceof NevaBridgeApiError)) {
            throw error;
          }
          expect(error.detail).toEqual(expectedDetail);
        }
      }
    } finally {
      server.stop(true);
    }
  });

  it("preserves status and headers when an error response body is unreadable", async () => {
    const server = createServer((_request, response) => {
      response.writeHead(502, {
        "content-length": "100",
        "content-type": "application/json",
        "x-request-id": "request-unreadable",
      });
      response.flushHeaders();
      response.write('{"message":');
      setTimeout(() => response.destroy(new Error("Body stream failed.")), 10);
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
      await client.getConversation({conversationId: "missing"});
      throw new Error("Expected getConversation to fail.");
    } catch (error: unknown) {
      expect(error).toBeInstanceOf(NevaBridgeApiError);
      if (!(error instanceof NevaBridgeApiError)) {
        throw error;
      }
      expect(error.status).toBe(502);
      expect(error.headers.get("x-request-id")).toBe("request-unreadable");
      expect(error.detail).toBeNull();
    } finally {
      server.close();
      await once(server, "close");
    }
  });

  it("preserves the platform abort error when a request is cancelled", async () => {
    const server = Bun.serve({
      port: 0,
      fetch(): Response {
        return new Response(null, {status: 204});
      },
    });
    const controller = new AbortController();
    controller.abort();
    const client = new NevaBridgeClient({
      baseUrl: server.url.toString(),
      tokenProvider: () => "token",
    });

    try {
      try {
        await client.getConversation({
          conversationId: "conversation-1",
          signal: controller.signal,
        });
        throw new Error("Expected getConversation to be aborted.");
      } catch (error: unknown) {
        expect(error).toBeInstanceOf(DOMException);
        if (!(error instanceof DOMException)) {
          throw error;
        }
        expect(error.name).toBe("AbortError");
      }
    } finally {
      server.stop(true);
    }
  });
});
