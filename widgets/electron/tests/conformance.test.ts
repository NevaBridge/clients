import {describe, expect, test} from "bun:test";
import {readdirSync, readFileSync} from "node:fs";
import {join} from "node:path";
import {createWebChatBridge} from "../src/bridge.js";
import {createSdkConversations, type PickedFile} from "../src/conversations.js";
import {createSettings} from "../src/settings.js";

// Runs every conformance case of the pinned webview chat bridge protocol against the shipped
// bridge and SDK-backed conversations. Only the network is replaced: the case's API answers are
// replayed through the fetch the conversations are given.

const CONFORMANCE_DIRECTORY = join(
  import.meta.dir,
  "..",
  "..",
  "..",
  "contracts",
  "webview-chat-protocol",
  "v1",
  "conformance",
);
const API_BASE_URL = "https://api.nevabridge.test";
const API_KEY = "sk_test_not_a_real_key";
const PAGE_URL = "https://chat.nevabridge.test/v1/";
const PRIMING_CONVERSATION_ID =
  "conversation-00000000-0000-4000-8000-0000000000ff";
// The protocol fixes these names; the message they carry is free text and not compared.
const BRIDGE_ERROR_NAMES = new Set([
  "EdgeFailure",
  "NetworkFailure",
  "UploadFailed",
  "Cancelled",
  "UnknownConversation",
  "UnknownTurn",
  "UnsupportedRequest",
]);
// Headers a case lists exactly; a call carries no other header of these kinds.
const CHECKED_HEADER = /^(x-actor-.*|if-none-match)$/i;

interface CaseHost {
  readonly productId: string;
  readonly actorId: string;
  readonly actorName?: string;
  readonly actorRoles?: ReadonlyArray<"anonymous" | "customer" | "tenant">;
  readonly applicationContext?: Readonly<Record<string, string>>;
}

type CaseApiResponse =
  | {readonly status: number; readonly body?: unknown}
  | {readonly networkFailure: true};

interface CaseApiCall {
  readonly operationId: string | null;
  readonly method: "GET" | "POST" | "PUT";
  readonly path?: string;
  readonly url?: string;
  readonly headers: Readonly<Record<string, string>>;
  readonly body?: unknown;
  readonly bodySizeBytes?: number;
  readonly response: CaseApiResponse;
}

interface ConformanceCase {
  readonly name: string;
  readonly description: string;
  readonly host: CaseHost;
  readonly startedConversations?: readonly string[];
  readonly startedTurns?: readonly string[];
  readonly userFile?: {
    readonly fileName: string;
    readonly sizeBytes: number;
  } | null;
  readonly request: Readonly<Record<string, unknown>>;
  readonly apiCalls: readonly CaseApiCall[];
  readonly response: Readonly<Record<string, unknown>>;
}

interface RecordedCall {
  readonly method: string;
  readonly url: string;
  readonly checkedHeaders: Record<string, string>;
  readonly authorization: string | null;
  readonly body: unknown;
  readonly bodySizeBytes: number;
}

function loadCases(): ConformanceCase[] {
  return readdirSync(CONFORMANCE_DIRECTORY)
    .filter((file) => file.endsWith(".json"))
    .sort()
    .map(
      (file) =>
        JSON.parse(
          readFileSync(join(CONFORMANCE_DIRECTORY, file), "utf8"),
        ) as ConformanceCase,
    );
}

function jsonResponse(status: number, body: unknown): Response {
  return new Response(body === undefined ? null : JSON.stringify(body), {
    status,
    headers: {"content-type": "application/json"},
  });
}

async function record(request: Request): Promise<RecordedCall> {
  const checkedHeaders: Record<string, string> = {};
  request.headers.forEach((value, name) => {
    if (CHECKED_HEADER.test(name)) {
      checkedHeaders[name.toLowerCase()] = value;
    }
  });
  const bytes = new Uint8Array(await request.arrayBuffer());
  let body: unknown = undefined;
  if (bytes.length > 0 && request.method !== "PUT") {
    body = JSON.parse(new TextDecoder().decode(bytes)) as unknown;
  }
  return {
    method: request.method,
    url: request.url,
    checkedHeaders,
    authorization: request.headers.get("authorization"),
    body,
    bodySizeBytes: bytes.length,
  };
}

/**
 * Answers the bridge's calls. While priming, every call gets the next queued answer unchecked;
 * afterwards each call is recorded and answered with the case's next API answer.
 */
class ReplayNetwork {
  public readonly recorded: RecordedCall[] = [];
  private primingAnswers: unknown[] = [];
  private caseAnswers: CaseApiResponse[] = [];
  private priming = true;

  public prime(answer: unknown): void {
    this.primingAnswers.push(answer);
  }

  public replay(calls: readonly CaseApiCall[]): void {
    this.priming = false;
    this.caseAnswers = calls.map((call) => call.response);
  }

  public readonly fetch = async (
    input: string | URL | Request,
    init?: RequestInit,
  ): Promise<Response> => {
    const request = new Request(input, init);
    if (this.priming) {
      return jsonResponse(201, this.primingAnswers.shift());
    }
    this.recorded.push(await record(request));
    const answer = this.caseAnswers.shift();
    if (answer === undefined) {
      return jsonResponse(500, {
        error: "UnexpectedCall",
        message: `The case lists no call ${request.method} ${request.url}.`,
      });
    }
    if ("networkFailure" in answer) {
      throw new TypeError("fetch failed");
    }
    return jsonResponse(answer.status, answer.body);
  };
}

function pickedFile(file: ConformanceCase["userFile"]): PickedFile | null {
  if (file === undefined || file === null) {
    return null;
  }
  return {
    name: file.fileName,
    sizeBytes: file.sizeBytes,
    read: async () => new Blob([new Uint8Array(file.sizeBytes)]),
  };
}

function expectedCall(call: CaseApiCall): RecordedCall {
  const checkedHeaders: Record<string, string> = {};
  for (const [name, value] of Object.entries(call.headers)) {
    checkedHeaders[name.toLowerCase()] = value;
  }
  return {
    method: call.method,
    url: call.url ?? `${API_BASE_URL}${call.path ?? ""}`,
    checkedHeaders,
    authorization: call.operationId === null ? null : `Bearer ${API_KEY}`,
    body: call.method === "PUT" ? undefined : call.body,
    bodySizeBytes: 0,
  };
}

function comparable(call: RecordedCall, sizeChecked: boolean): object {
  const {bodySizeBytes, ...rest} = call;
  return sizeChecked ? {...rest, bodySizeBytes} : rest;
}

/** Drops the free-text message of a bridge-named error, which the protocol does not fix. */
function withoutFreeText(response: unknown): unknown {
  if (typeof response !== "object" || response === null) {
    return response;
  }
  const error = (response as {error?: unknown}).error;
  if (typeof error !== "object" || error === null) {
    return response;
  }
  const {message, ...rest} = error as Record<string, unknown>;
  if (
    typeof rest["name"] === "string" &&
    BRIDGE_ERROR_NAMES.has(rest["name"]) &&
    typeof message === "string"
  ) {
    return {...response, error: rest};
  }
  return response;
}

async function run(
  conformanceCase: ConformanceCase,
): Promise<{response: unknown; calls: RecordedCall[]}> {
  const network = new ReplayNetwork();
  const file = pickedFile(conformanceCase.userFile);
  const bridge = createWebChatBridge({
    conversations: createSdkConversations(
      createSettings({
        apiBaseUrl: API_BASE_URL,
        productId: conformanceCase.host.productId,
        apiKey: API_KEY,
      }),
      {fetch: network.fetch},
    ),
    productId: conformanceCase.host.productId,
    reporter: {
      id: conformanceCase.host.actorId,
      displayName: conformanceCase.host.actorName,
      roles: conformanceCase.host.actorRoles,
    },
    applicationContext: conformanceCase.host.applicationContext,
    pageUrl: PAGE_URL,
    pickFile: async () => file,
  });

  // Bring the session to the state the case starts from, the way the page would have.
  let primingId = 0;
  const primingRequest = async (request: object): Promise<void> => {
    primingId += 1;
    const answer = await bridge.handle(
      PAGE_URL,
      JSON.stringify({id: `priming-${primingId}`, ...request}),
    );
    expect(JSON.parse(answer ?? "null")).toMatchObject({ok: true});
  };
  for (const conversationId of conformanceCase.startedConversations ?? []) {
    network.prime({
      userMessage: {conversationId},
      assistantMessage: {conversationId},
      reports: [],
    });
    await primingRequest({type: "startConversation", text: "Earlier message"});
  }
  for (const turnId of conformanceCase.startedTurns ?? []) {
    network.prime({conversationId: PRIMING_CONVERSATION_ID, turnId});
    await primingRequest({
      type: "startIntakeConversation",
      text: "Earlier message",
    });
  }

  network.replay(conformanceCase.apiCalls);
  const answer = await bridge.handle(
    PAGE_URL,
    JSON.stringify(conformanceCase.request),
  );
  return {
    response: answer === null ? null : (JSON.parse(answer) as unknown),
    calls: network.recorded,
  };
}

describe("webview chat bridge protocol v1 conformance", () => {
  for (const conformanceCase of loadCases()) {
    test(`${conformanceCase.name}: ${conformanceCase.description}`, async () => {
      // Given the case's host, session and API answers
      // When the page sends the case's request
      const {response, calls} = await run(conformanceCase);

      // Then the bridge makes exactly the listed calls and sends the expected response
      expect(
        calls.map((call, index) =>
          comparable(
            call,
            conformanceCase.apiCalls[index]?.bodySizeBytes !== undefined,
          ),
        ),
      ).toEqual(
        conformanceCase.apiCalls.map((call) => {
          const expected = expectedCall(call);
          return comparable(
            {...expected, bodySizeBytes: call.bodySizeBytes ?? 0},
            call.bodySizeBytes !== undefined,
          );
        }),
      );
      expect(withoutFreeText(response)).toEqual(
        withoutFreeText(conformanceCase.response),
      );
    });
  }
});
