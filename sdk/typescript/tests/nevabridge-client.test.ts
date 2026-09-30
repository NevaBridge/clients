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

  it("sends the response language and writer model on start and append", async () => {
    const bodies: unknown[] = [];
    const server = Bun.serve({
      port: 0,
      async fetch(request): Promise<Response> {
        bodies.push(await request.clone().json());
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
          userMessage: {
            content: "Die Seite bleibt leer.",
            writerModelKey: "claude-sonnet-5",
            responseLanguage: "de-DE",
          },
        },
      });
      await client.appendMessage({
        conversationId: "conversation-1",
        request: {
          content: "Nach dem Klick auf Bezahlen.",
          writerModelKey: "claude-sonnet-5",
          responseLanguage: "de-DE",
        },
      });
      expect(bodies).toEqual([
        {
          userMessage: {
            content: "Die Seite bleibt leer.",
            writerModelKey: "claude-sonnet-5",
            responseLanguage: "de-DE",
          },
        },
        {
          content: "Nach dem Klick auf Bezahlen.",
          writerModelKey: "claude-sonnet-5",
          responseLanguage: "de-DE",
        },
      ]);
    } finally {
      await server.stop(true);
    }
  });

  it("returns the writer model chosen at creation on summaries and details", async () => {
    const conversation = {
      id: "conversation-1",
      tenantId: "tenant-1",
      productId: "product-1",
      reporterId: "reporter-1",
      createdAt: timestamp,
      updatedAt: timestamp,
      status: "in_progress",
      writerModelKey: "openrouter-anthropic-claude-opus-5-5",
    };
    const server = Bun.serve({
      port: 0,
      fetch(request): Response {
        if (new URL(request.url).pathname.startsWith("/v1/products/")) {
          return Response.json([conversation]);
        }
        return Response.json({
          ...conversation,
          reports: [report()],
          messages: [message("user")],
        });
      },
    });
    const client = new NevaBridgeClient({
      baseUrl: server.url.toString(),
      tokenProvider: () => "token",
    });

    try {
      const [summary] = await client.listConversations({
        productId: "product-1",
      });
      const detail = await client.getConversation({
        conversationId: "conversation-1",
      });
      expect(summary?.writerModelKey).toBe(
        "openrouter-anthropic-claude-opus-5-5",
      );
      expect(detail.writerModelKey).toBe(
        "openrouter-anthropic-claude-opus-5-5",
      );
    } finally {
      server.stop(true);
    }
  });

  it("runs the five attachment operations with a fresh token for each", async () => {
    interface CapturedRequest {
      readonly authorization: string | null;
      readonly actorId: string | null;
      readonly body: string;
      readonly method: string;
      readonly path: string;
    }
    const attachmentId = "attachment-0f1e2d3c-4b5a-4968-8776-655443322110";
    const attachment = {
      id: attachmentId,
      fileName: "console.log",
      kind: "text",
      sizeBytes: 48213,
      uploadedBy: "api:user-4821",
      uploadedAt: timestamp,
    };
    const requests: CapturedRequest[] = [];
    const server = Bun.serve({
      port: 0,
      async fetch(request): Promise<Response> {
        const path = new URL(request.url).pathname;
        requests.push({
          authorization: request.headers.get("authorization"),
          actorId: request.headers.get("x-actor-id"),
          body: await request.clone().text(),
          method: request.method,
          path,
        });
        if (path.endsWith("/attachments")) {
          return Response.json({attachments: [attachment]});
        }
        if (path.endsWith("/request-attachment-upload")) {
          return Response.json({
            attachmentId,
            uploadUrl: "https://files.example/upload?signature=1",
            expiresAt: timestamp,
          });
        }
        if (path.endsWith("/confirm-attachment-upload")) {
          return Response.json(attachment);
        }
        if (path.endsWith("/request-attachment-download-url")) {
          return Response.json({
            url: "https://files.example/download?signature=2",
          });
        }
        return new Response(null, {status: 204});
      },
    });
    let tokenNumber = 0;
    const client = new NevaBridgeClient({
      baseUrl: server.url.toString(),
      tokenProvider: () => `token-${++tokenNumber}`,
    });

    try {
      const listed = await client.listAttachments({
        conversationId: "conversation-1",
      });
      const upload = await client.requestAttachmentUpload({
        conversationId: "conversation-1",
        actorId: "user-4821",
        request: {fileName: "console.log", sizeBytes: 48213},
      });
      const confirmed = await client.confirmAttachmentUpload({
        conversationId: "conversation-1",
        attachmentId,
      });
      const download = await client.requestAttachmentDownloadUrl({
        conversationId: "conversation-1",
        attachmentId,
      });
      const deleted = await client.deleteAttachment({
        conversationId: "conversation-1",
        attachmentId,
      });

      expect(listed.attachments.map((file) => file.id)).toEqual([attachmentId]);
      expect(listed.attachments[0]?.uploadedAt).toEqual(new Date(timestamp));
      expect(upload.attachmentId).toBe(attachmentId);
      expect(upload.uploadUrl).toBe("https://files.example/upload?signature=1");
      expect(upload.expiresAt).toEqual(new Date(timestamp));
      expect(confirmed.kind).toBe("text");
      expect(download.url).toBe("https://files.example/download?signature=2");
      expect(deleted).toBeUndefined();
    } finally {
      server.stop(true);
    }

    expect(
      requests.map((request) => `${request.method} ${request.path}`),
    ).toEqual([
      "GET /v1/conversations/conversation-1/attachments",
      "POST /v1/conversations/conversation-1/actions/request-attachment-upload",
      "POST /v1/conversations/conversation-1/actions/confirm-attachment-upload",
      "POST /v1/conversations/conversation-1/actions/request-attachment-download-url",
      "POST /v1/conversations/conversation-1/actions/delete-attachment",
    ]);
    expect(requests.map((request) => request.authorization)).toEqual([
      "Bearer token-1",
      "Bearer token-2",
      "Bearer token-3",
      "Bearer token-4",
      "Bearer token-5",
    ]);
    expect(requests.map((request) => request.actorId)).toEqual([
      null,
      "user-4821",
      null,
      null,
      null,
    ]);
    expect(requests.map((request) => request.body)).toEqual([
      "",
      JSON.stringify({fileName: "console.log", sizeBytes: 48213}),
      JSON.stringify({attachmentId}),
      JSON.stringify({attachmentId}),
      JSON.stringify({attachmentId}),
    ]);
  });

  it("carries the free allowance when an upload exceeds the storage limit", async () => {
    const server = Bun.serve({
      port: 0,
      fetch(): Response {
        return Response.json(
          {
            error: "QuotaExceeded",
            message: "The attachment allowance cannot hold this file.",
            remainingBytes: 1024,
          },
          {status: 402},
        );
      },
    });
    const client = new NevaBridgeClient({
      baseUrl: server.url.toString(),
      tokenProvider: () => "token",
    });

    try {
      await client.requestAttachmentUpload({
        conversationId: "conversation-1",
        actorId: "user-4821",
        request: {fileName: "screen.mp4", sizeBytes: 90000000},
      });
      throw new Error("Expected requestAttachmentUpload to fail.");
    } catch (error: unknown) {
      expect(error).toBeInstanceOf(NevaBridgeApiError);
      if (!(error instanceof NevaBridgeApiError)) {
        throw error;
      }
      expect(error.status).toBe(402);
      expect(error.detail).toEqual({
        error: "QuotaExceeded",
        message: "The attachment allowance cannot hold this file.",
        remainingBytes: 1024,
      });
    } finally {
      server.stop(true);
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
