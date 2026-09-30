using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace NevaBridge.Chat.WebChat;

/// <summary>
/// Answers requests from the NevaBridge web chat page hosted in a WebView. The page only
/// renders; every API call runs here, so the API key never reaches the page.
/// </summary>
/// <remarks>
/// The page may be loaded from a server outside the host application's control, so it is
/// treated as untrusted:
/// <list type="bullet">
/// <item>The page must come over https, or plain http on this machine only.</item>
/// <item>Messages from any origin other than the page's are ignored.</item>
/// <item>Only the chat operations are available, never arbitrary requests.</item>
/// <item>The product, reporter and application context come from the host, never the page.</item>
/// <item>Follow-ups and submissions are accepted only for conversations the page started through
/// this bridge, so the page cannot reach other reporters' conversations.</item>
/// </list>
/// String request IDs from hosted pages and numeric IDs from the bundled prototype are echoed unchanged.
/// Protocol version 1 requests:
/// <c>{"id":1,"type":"startConversation","text":"...","category":"bug_report"}</c>
/// (category optional; see <see cref="ReportCategories"/>),
/// <c>{"id":2,"type":"appendMessage","conversationId":"...","text":"..."}</c>,
/// <c>{"id":3,"type":"submitReport","conversationId":"...","reportId":"..."}</c>,
/// <c>{"id":4,"type":"getFeatures"}</c> (result: feature name to on/off), and
/// <c>{"id":5,"type":"attachFile","conversationId":"..."}</c> (the host shows its file picker and
/// uploads the file itself; result: the attachment, or null when the user picked nothing).
/// Response: <c>{"id":1,"ok":true,"result":{...}}</c> with the API's own JSON shape, or
/// <c>{"id":1,"ok":false,"error":{"name":"...","message":"...","status":429,"retryable":true}}</c>.
/// </remarks>
public sealed class WebChatBridge
{
    private const int MaxMessageLength = 32768;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly INevaBridgeConversations _conversations;
    private readonly string _productId;
    private readonly NevaBridgeReporter _reporter;
    private readonly IReadOnlyDictionary<string, string>? _applicationContext;
    private readonly string _allowedOrigin;
    private readonly Func<CancellationToken, Task<PickedFile?>>? _pickFile;
    // Messages can be handled concurrently, so the set is guarded.
    private readonly HashSet<string> _startedConversations = new(StringComparer.Ordinal);
    private readonly object _startedConversationsLock = new();

    public WebChatBridge(
        INevaBridgeConversations conversations,
        string productId,
        NevaBridgeReporter reporter,
        IReadOnlyDictionary<string, string>? applicationContext,
        Uri pageOrigin,
        Func<CancellationToken, Task<PickedFile?>>? pickFile = null)
    {
        _conversations = conversations;
        _productId = productId;
        _reporter = reporter;
        _applicationContext = applicationContext;

        // A page fetched over plain http can be replaced in transit while keeping its origin.
        if (!SecureAddress.IsAllowed(pageOrigin))
        {
            throw new ArgumentException(
                $"The web chat page must be served over https (plain http only on this machine), not '{pageOrigin}'.",
                nameof(pageOrigin));
        }

        _allowedOrigin = OriginOf(pageOrigin);
        _pickFile = pickFile;
    }

    /// <summary>
    /// Handles one message from the page. Returns the response to post back, or null when the
    /// message did not come from the page's origin and must be dropped.
    /// </summary>
    public async Task<string?> HandleAsync(Uri source, string message, CancellationToken cancellationToken = default)
    {
        if (!string.Equals(OriginOf(source), _allowedOrigin, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        JsonObject? request;
        try
        {
            request = JsonNode.Parse(message) as JsonObject;
        }
        catch (JsonException)
        {
            request = null;
        }

        JsonValue? id = ReadRequestId(request);
        if (request is null)
        {
            return Failure(id, "InvalidRequest", "The message is not a JSON object.");
        }

        try
        {
            return ReadString(request, "type") switch
            {
                "startConversation" => await StartAsync(id, request, cancellationToken).ConfigureAwait(false),
                "appendMessage" => await AppendAsync(id, request, cancellationToken).ConfigureAwait(false),
                "submitReport" => await SubmitAsync(id, request, cancellationToken).ConfigureAwait(false),
                "getFeatures" => Success(id, await GetFeaturesAsync(cancellationToken).ConfigureAwait(false)),
                "attachFile" => await AttachFileAsync(id, request, cancellationToken).ConfigureAwait(false),
                _ => Failure(id, "UnsupportedOperation", "The chat page asked for an operation the host does not offer."),
            };
        }
        catch (NevaBridgeApiException error)
        {
            return Failure(id, error.ErrorName ?? "GatewayError", error.Message, error.StatusCode, error.IsRetryable);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Failure(id, "Timeout", "NevaBridge did not answer in time.", retryable: true);
        }
        catch (HttpRequestException)
        {
            return Failure(id, "NetworkError", "NevaBridge could not be reached.", retryable: true);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return Failure(id, "FileUnreadable", "The chosen file could not be read.");
        }
        catch (JsonException)
        {
            return Failure(id, "InvalidResponse", "NevaBridge sent an answer this version of the chat cannot read.");
        }
        catch (Exception error) when (error is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            // Adapter failures must not escape the UI event handler or expose private host details.
            return Failure(id, "HostError", "The application could not complete the chat request.");
        }
    }

    private async Task<string> StartAsync(JsonValue? id, JsonObject request, CancellationToken cancellationToken)
    {
        if (ReadText(request) is not { } text)
        {
            return InvalidText(id);
        }

        string? category = ReadString(request, "category");
        if (category is not null && !ReportCategories.All.Contains(category))
        {
            return Failure(id, "InvalidRequest", "category must be one of: " + string.Join(", ", ReportCategories.All) + ".");
        }

        ConversationTurn turn = await _conversations.StartConversationAsync(
            _productId,
            _reporter,
            new StartConversationRequest(new UserMessage(text))
            {
                Category = category,
                ApplicationContext = _applicationContext,
            },
            cancellationToken).ConfigureAwait(false);
        lock (_startedConversationsLock)
        {
            _startedConversations.Add(turn.AssistantMessage.ConversationId);
        }

        return Success(id, turn);
    }

    private async Task<string> AppendAsync(JsonValue? id, JsonObject request, CancellationToken cancellationToken)
    {
        if (ReadStartedConversation(request) is not { } conversationId)
        {
            return UnknownConversation(id);
        }

        if (ReadText(request) is not { } text)
        {
            return InvalidText(id);
        }

        ConversationTurn turn = await _conversations.AppendMessageAsync(
            conversationId,
            new AppendMessageRequest(text),
            cancellationToken).ConfigureAwait(false);
        return Success(id, turn);
    }

    private async Task<string> SubmitAsync(JsonValue? id, JsonObject request, CancellationToken cancellationToken)
    {
        if (ReadStartedConversation(request) is not { } conversationId)
        {
            return UnknownConversation(id);
        }

        if (ReadString(request, "reportId") is not { Length: > 0 } reportId)
        {
            return Failure(id, "InvalidRequest", "reportId is required.");
        }

        SubmitReportResult result = await _conversations.SubmitReportAsync(
            conversationId,
            reportId,
            cancellationToken).ConfigureAwait(false);
        return Success(id, result);
    }

    private async Task<IReadOnlyDictionary<string, bool>> GetFeaturesAsync(CancellationToken cancellationToken)
    {
        IReadOnlyDictionary<string, bool> features = await _conversations.GetFeaturesAsync(cancellationToken).ConfigureAwait(false);
        if (_pickFile is not null)
        {
            return features;
        }

        // Without a file picker this host cannot attach files, whatever the tenant may do.
        Dictionary<string, bool> effective = new(features) { ["chatAttachments"] = false };
        return effective;
    }

    private async Task<string> AttachFileAsync(JsonValue? id, JsonObject request, CancellationToken cancellationToken)
    {
        if (ReadStartedConversation(request) is not { } conversationId)
        {
            return UnknownConversation(id);
        }

        if (_pickFile is null)
        {
            return Failure(id, "UnsupportedOperation", "This application does not offer file attachments.");
        }

        PickedFile? file = await _pickFile(cancellationToken).ConfigureAwait(false);
        if (file is null)
        {
            return Success<Attachment?>(id, null);
        }

        Attachment attachment = await _conversations.AttachFileAsync(
            conversationId,
            _reporter.Id,
            file,
            cancellationToken).ConfigureAwait(false);
        return Success(id, attachment);
    }

    private string? ReadStartedConversation(JsonObject request)
    {
        if (ReadString(request, "conversationId") is not { } conversationId)
        {
            return null;
        }

        lock (_startedConversationsLock)
        {
            return _startedConversations.Contains(conversationId) ? conversationId : null;
        }
    }

    private static string? ReadText(JsonObject request) =>
        ReadString(request, "text")?.Trim() is { Length: > 0 and <= MaxMessageLength } text ? text : null;

    private static string InvalidText(JsonValue? id) =>
        Failure(id, "InvalidRequest", $"text must contain 1 to {MaxMessageLength} characters.");

    private static string UnknownConversation(JsonValue? id) =>
        Failure(id, "UnknownConversation", "This chat did not start that conversation.");

    private static string Success<T>(JsonValue? id, T result) =>
        new JsonObject
        {
            ["id"] = id?.DeepClone(),
            ["ok"] = true,
            ["result"] = JsonSerializer.SerializeToNode(result, JsonOptions),
        }.ToJsonString();

    private static string Failure(JsonValue? id, string name, string message, int? status = null, bool retryable = false) =>
        new JsonObject
        {
            ["id"] = id?.DeepClone(),
            ["ok"] = false,
            ["error"] = new JsonObject
            {
                ["name"] = name,
                ["message"] = message,
                ["status"] = status,
                ["retryable"] = retryable,
            },
        }.ToJsonString();

    private static string? ReadString(JsonObject request, string name) =>
        request[name] is JsonValue value && value.TryGetValue(out string? text) ? text : null;

    private static JsonValue? ReadRequestId(JsonObject? request) =>
        request?["id"] is JsonValue value &&
        (value.TryGetValue(out string? _) || value.TryGetValue(out int _))
            ? value
            : JsonValue.Create(0);

    private static string OriginOf(Uri uri) => uri.GetLeftPart(UriPartial.Authority);
}
