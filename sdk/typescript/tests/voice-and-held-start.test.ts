import {describe, expect, it} from "bun:test";
import {NevaBridgeApiError, NevaBridgeClient} from "../src/nevabridge-client";

const conversationId = "conversation-11111111-1111-4111-8111-111111111111";

interface CapturedRequest {
  readonly method: string;
  readonly path: string;
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
      if (!url.pathname.startsWith("/v1/")) {
        return new Response(null, {status: 404});
      }
      const captured: CapturedRequest = {
        method: request.method,
        path: url.pathname,
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

const held = {
  conversationId,
  attachments: [
    {
      attachmentId: "attachment-33333333-3333-4333-8333-333333333333",
      fileName: "app.log",
      uploadUrl: "https://uploads.example.com/attachment-3?signature=abc",
      expiresAt: "2026-11-08T12:15:00.000Z",
    },
  ],
};

describe("Generic Intake start with attachments", () => {
  it("sends the files with the actor headers and returns their upload addresses", async () => {
    // Given
    const api = fakeApi(() => Response.json(held, {status: 201}));

    try {
      // When
      const started = await api.client.startHeldIntakeConversation({
        productId: "product-1",
        actorId: "reporter-1",
        actorName: "Jane Doe",
        actorRoles: ["customer"],
        request: {
          attachments: [{fileName: "app.log", sizeBytes: 15}],
          reporterContact: {email: "jane@example.com"},
        },
      });

      // Then
      expect(started.conversationId).toBe(conversationId);
      expect(started.attachments).toHaveLength(1);
      expect(started.attachments[0]?.attachmentId).toBe(
        "attachment-33333333-3333-4333-8333-333333333333",
      );
      expect(started.attachments[0]?.uploadUrl).toBe(
        "https://uploads.example.com/attachment-3?signature=abc",
      );
      expect(started.attachments[0]?.expiresAt).toEqual(
        new Date("2026-11-08T12:15:00.000Z"),
      );
      const [request] = api.requests;
      expect(request?.method).toBe("POST");
      expect(request?.path).toBe("/v1/products/product-1/intake-conversations");
      expect(request?.headers.get("x-actor-id")).toBe("reporter-1");
      expect(request?.headers.get("x-actor-name")).toBe("Jane Doe");
      expect(request?.headers.get("x-actor-roles")).toBe("customer");
      expect(JSON.parse(request?.body ?? "")).toEqual({
        attachments: [{fileName: "app.log", sizeBytes: 15}],
        reporterContact: {email: "jane@example.com"},
      });
    } finally {
      await api.stop();
    }
  });

  it("names the stored conversation when its uploads could not be signed", async () => {
    // Given
    const api = fakeApi(() =>
      Response.json(
        {
          error: "FailedDependency",
          message: "Upload URLs could not be created.",
          conversationId,
        },
        {status: 424},
      ),
    );

    try {
      // When
      const failure = await api.client
        .startHeldIntakeConversation({
          productId: "product-1",
          actorId: "reporter-1",
          request: {attachments: [{fileName: "app.log", sizeBytes: 15}]},
        })
        .catch((error: unknown) => error);

      // Then
      expect(failure).toBeInstanceOf(NevaBridgeApiError);
      expect(failure).toMatchObject({
        status: 424,
        detail: {conversationId},
      });
    } finally {
      await api.stop();
    }
  });

  it("offers the answer as the API's JSON as well", async () => {
    // Given
    const api = fakeApi(() => Response.json(held, {status: 201}));

    try {
      // When
      const started = await api.client.json.startHeldIntakeConversation({
        productId: "product-1",
        actorId: "reporter-1",
        request: {attachments: [{fileName: "app.log", sizeBytes: 15}]},
      });

      // Then
      expect(started).toEqual(held);
    } finally {
      await api.stop();
    }
  });
});

describe("Voice", () => {
  it("creates a transcription session for a language", async () => {
    // Given
    const session = {
      url: "wss://transcribestreaming.example.com/stream?X-Amz-Signature=abc",
      expiresAt: "2026-11-08T12:05:00.000Z",
      languageCode: "de-DE",
      sampleRate: 16000,
      maxDurationSeconds: 60,
    };
    const api = fakeApi(() => Response.json(session, {status: 201}));

    try {
      // When
      const created = await api.client.createTranscriptionSession({
        languageCode: "de-DE",
      });

      // Then
      expect(created.url).toBe(session.url);
      expect(created.sampleRate).toBe(16000);
      expect(created.maxDurationSeconds).toBe(60);
      const [request] = api.requests;
      expect(request?.method).toBe("POST");
      expect(request?.path).toBe("/v1/voice/transcription-sessions");
      expect(JSON.parse(request?.body ?? "")).toEqual({languageCode: "de-DE"});
      expect(request?.headers.get("x-actor-id")).toBeNull();
    } finally {
      await api.stop();
    }
  });

  it("synthesizes speech for a text", async () => {
    // Given
    const speech = {
      audioBase64: "SUQz",
      contentType: "audio/mpeg",
      voiceId: "Vicki",
    };
    const api = fakeApi(() => Response.json(speech));

    try {
      // When
      const synthesized = await api.client.synthesizeSpeech({
        text: "Wann stürzt es ab?",
        languageCode: "de-DE",
      });

      // Then
      expect(synthesized).toEqual(speech);
      const [request] = api.requests;
      expect(request?.method).toBe("POST");
      expect(request?.path).toBe("/v1/voice/speech");
      expect(JSON.parse(request?.body ?? "")).toEqual({
        text: "Wann stürzt es ab?",
        languageCode: "de-DE",
      });
    } finally {
      await api.stop();
    }
  });

  it("reports a voice feature the tenant lacks as an API error", async () => {
    // Given
    const api = fakeApi(() =>
      Response.json(
        {error: "Forbidden", message: "voiceConversation is not enabled."},
        {status: 403},
      ),
    );

    try {
      // When
      const failure = await api.client
        .synthesizeSpeech({text: "Hello", languageCode: "en-US"})
        .catch((error: unknown) => error);

      // Then
      expect(failure).toBeInstanceOf(NevaBridgeApiError);
      expect(failure).toMatchObject({status: 403});
    } finally {
      await api.stop();
    }
  });
});
