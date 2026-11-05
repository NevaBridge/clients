using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using NevaBridge.Chat;
using NevaBridge.Chat.Settings;
using NevaBridge.Chat.WebChat;
using NevaBridge.Sample.Client;

namespace NevaBridge.Sample.Tests;

/// <summary>
/// Runs every conformance case of the pinned webview chat bridge protocol against
/// <see cref="WebChatBridge"/> over this sample's <see cref="NevaBridgeClient"/>. Only the network
/// is replaced: the case's API answers are replayed through the client's HTTP handler.
/// </summary>
public sealed class WebChatConformanceTests
{
    private const string ApiKey = "sk_test_not_a_real_key";
    private const string PrimingConversationId = "conversation-00000000-0000-4000-8000-0000000000ff";
    private static readonly Uri ApiBaseUrl = new("https://api.nevabridge.test/");
    private static readonly Uri PageUrl = new("https://chat.nevabridge.test/v1/");

    // The protocol fixes these names; the message they carry is free text and not compared.
    private static readonly HashSet<string> BridgeErrorNames = new(StringComparer.Ordinal)
    {
        "EdgeFailure", "NetworkFailure", "UploadFailed", "Cancelled",
        "UnknownConversation", "UnknownTurn", "UnsupportedRequest",
    };

    public static TheoryData<string> Cases()
    {
        TheoryData<string> cases = new();
        foreach (string path in Directory.GetFiles(CaseDirectory(), "*.json").Order(StringComparer.Ordinal))
        {
            cases.Add(Path.GetFileNameWithoutExtension(path));
        }

        return cases;
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task Bridge_passes_the_conformance_case(string caseName)
    {
        // Given the case's host, session and API answers
        JsonObject conformanceCase = JsonNode.Parse(
            await File.ReadAllTextAsync(Path.Combine(CaseDirectory(), caseName + ".json")))!.AsObject();
        JsonObject host = conformanceCase["host"]!.AsObject();
        JsonArray apiCalls = conformanceCase["apiCalls"]!.AsArray();
        ReplayHandler network = new();
        using HttpClient http = new(network);
        NevaBridgeSettings settings = new(ApiBaseUrl, host["productId"]!.GetValue<string>(), ApiKey, null);
        PickedFile? file = conformanceCase["userFile"] is JsonObject userFile
            ? new PickedFile(
                userFile["fileName"]!.GetValue<string>(),
                userFile["sizeBytes"]!.GetValue<long>(),
                () => new MemoryStream(new byte[userFile["sizeBytes"]!.GetValue<long>()]))
            : null;
        WebChatBridge bridge = new(
            new NevaBridgeClient(http, settings),
            settings.ProductId,
            new NevaBridgeReporter(
                host["actorId"]!.GetValue<string>(),
                host["actorName"]?.GetValue<string>(),
                host["actorRoles"]?.AsArray().Select(role => role!.GetValue<string>()).ToList()),
            host["applicationContext"]?.AsObject().ToDictionary(
                entry => entry.Key,
                entry => entry.Value!.GetValue<string>()),
            PageUrl,
            _ => Task.FromResult(file));
        await PrimeAsync(bridge, network, conformanceCase);
        network.Replay(apiCalls);

        // When the page sends the case's request
        string? answer = await bridge.HandleAsync(PageUrl, conformanceCase["request"]!.ToJsonString());

        // Then the bridge makes exactly the listed calls and sends the expected response
        Assert.Equal(
            apiCalls.Select(call => ExpectedCall(call!.AsObject())).ToList(),
            network.Recorded.Select((call, index) =>
                call with { BodySizeBytes = index < apiCalls.Count && apiCalls[index]!["bodySizeBytes"] is not null ? call.BodySizeBytes : null })
                .ToList());
        Assert.NotNull(answer);
        Assert.Equal(
            WithoutFreeText(conformanceCase["response"]!).ToJsonString(),
            WithoutFreeText(JsonNode.Parse(answer)!).ToJsonString());
    }

    private static string CaseDirectory() =>
        Path.Combine(AppContext.BaseDirectory, "webview-chat-protocol", "v1", "conformance");

    /// <summary>Brings the session to the state the case starts from, the way the page would have.</summary>
    private static async Task PrimeAsync(WebChatBridge bridge, ReplayHandler network, JsonObject conformanceCase)
    {
        int primingId = 0;
        async Task SendAsync(JsonObject request)
        {
            primingId++;
            request["id"] = $"priming-{primingId}";
            string? answer = await bridge.HandleAsync(PageUrl, request.ToJsonString());
            Assert.True(JsonNode.Parse(answer!)!["ok"]!.GetValue<bool>(), answer);
        }

        foreach (JsonNode? conversation in conformanceCase["startedConversations"]?.AsArray() ?? new JsonArray())
        {
            string conversationId = conversation!.GetValue<string>();
            network.Prime(new JsonObject
            {
                ["userMessage"] = Message(conversationId, "user"),
                ["assistantMessage"] = Message(conversationId, "assistant"),
                ["reports"] = new JsonArray(),
            });
            await SendAsync(new JsonObject { ["type"] = "startConversation", ["text"] = "Earlier message" });
        }

        foreach (JsonNode? turn in conformanceCase["startedTurns"]?.AsArray() ?? new JsonArray())
        {
            network.Prime(new JsonObject
            {
                ["conversationId"] = PrimingConversationId,
                ["turnId"] = turn!.GetValue<string>(),
            });
            await SendAsync(new JsonObject { ["type"] = "startIntakeConversation", ["text"] = "Earlier message" });
        }
    }

    private static JsonObject Message(string conversationId, string role) => new()
    {
        ["id"] = "message-priming",
        ["conversationId"] = conversationId,
        ["role"] = role,
        ["content"] = "Earlier message",
        ["createdAt"] = "2026-09-30T09:00:00.000Z",
    };

    private static RecordedCall ExpectedCall(JsonObject call)
    {
        bool presigned = call["operationId"] is null;
        string url = presigned
            ? call["url"]!.GetValue<string>()
            : new Uri(ApiBaseUrl, call["path"]!.GetValue<string>().TrimStart('/')).AbsoluteUri;
        return new RecordedCall(
            call["method"]!.GetValue<string>(),
            url,
            Headers(call["headers"]!.AsObject().Select(entry => (entry.Key, entry.Value!.GetValue<string>()))),
            presigned ? null : $"Bearer {ApiKey}",
            presigned || call["body"] is null ? null : Canonical(call["body"]!),
            call["bodySizeBytes"]?.GetValue<long>());
    }

    /// <summary>The headers a case lists exactly, in a stable order and spelling.</summary>
    private static string Headers(IEnumerable<(string Name, string Value)> headers) =>
        string.Join(
            "\n",
            headers
                .Where(header => header.Name.StartsWith("X-Actor-", StringComparison.OrdinalIgnoreCase)
                    || header.Name.Equals("If-None-Match", StringComparison.OrdinalIgnoreCase))
                .Select(header => $"{header.Name.ToLowerInvariant()}: {header.Value}")
                .Order(StringComparer.Ordinal));

    /// <summary>A JSON value with object members sorted, so equal JSON compares equal as text.</summary>
    private static string Canonical(JsonNode node) => Sorted(node)!.ToJsonString();

    private static JsonNode? Sorted(JsonNode? node) => node switch
    {
        JsonObject obj => new JsonObject(obj
            .OrderBy(entry => entry.Key, StringComparer.Ordinal)
            .Select(entry => KeyValuePair.Create(entry.Key, Sorted(entry.Value)))),
        JsonArray array => new JsonArray(array.Select(Sorted).ToArray()),
        null => null,
        _ => JsonNode.Parse(node.ToJsonString()),
    };

    /// <summary>Drops the free-text message of a bridge-named error and sorts members.</summary>
    private static JsonNode WithoutFreeText(JsonNode response)
    {
        JsonNode copy = Sorted(response)!;
        if (copy["error"] is JsonObject error
            && error["name"]?.GetValue<string>() is { } name
            && BridgeErrorNames.Contains(name)
            && error["message"] is JsonValue message
            && message.TryGetValue(out string? _))
        {
            error.Remove("message");
        }

        return copy;
    }

    private sealed record RecordedCall(
        string Method,
        string Url,
        string Headers,
        string? Authorization,
        string? Body,
        long? BodySizeBytes);

    /// <summary>
    /// Answers the client's calls. While priming, every call gets the next queued answer
    /// unchecked; afterwards each call is recorded and answered with the case's next API answer.
    /// </summary>
    private sealed class ReplayHandler : HttpMessageHandler
    {
        private readonly Queue<JsonNode> _primingAnswers = new();
        private readonly Queue<JsonObject> _caseAnswers = new();
        private bool _priming = true;

        public List<RecordedCall> Recorded { get; } = [];

        public void Prime(JsonNode answer) => _primingAnswers.Enqueue(answer);

        public void Replay(JsonArray calls)
        {
            _priming = false;
            foreach (JsonNode? call in calls)
            {
                _caseAnswers.Enqueue(call!["response"]!.AsObject());
            }
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (_priming)
            {
                return Json(HttpStatusCode.Created, _primingAnswers.Dequeue().ToJsonString());
            }

            byte[] bytes = request.Content is null
                ? []
                : await request.Content.ReadAsByteArrayAsync(cancellationToken);
            IEnumerable<(string, string)> headers = request.Headers
                .Concat(request.Content?.Headers ?? Enumerable.Empty<KeyValuePair<string, IEnumerable<string>>>())
                .Select(header => (header.Key, string.Join(",", header.Value)));
            bool put = request.Method == HttpMethod.Put;
            Recorded.Add(new RecordedCall(
                request.Method.Method,
                request.RequestUri!.AbsoluteUri,
                Headers(headers),
                request.Headers.Authorization?.ToString(),
                put || bytes.Length == 0 ? null : Canonical(JsonNode.Parse(bytes)!),
                bytes.Length));

            if (!_caseAnswers.TryDequeue(out JsonObject? answer))
            {
                return Json(
                    HttpStatusCode.InternalServerError,
                    """{"error":"UnexpectedCall","message":"The case lists no further call."}""");
            }

            if (answer["networkFailure"] is not null)
            {
                throw new HttpRequestException("No answer arrived.");
            }

            return Json(
                (HttpStatusCode)answer["status"]!.GetValue<int>(),
                answer["body"]?.ToJsonString() ?? string.Empty);
        }

        private static HttpResponseMessage Json(HttpStatusCode status, string body) => new(status)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
    }
}
