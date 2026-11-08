using System.Text.Json;
using System.Text.Json.Nodes;

namespace NevaBridge.Chat.WebChat;

/// <summary>
/// Answers requests from the NevaBridge web chat page hosted in a WebView, following version 1 of
/// the webview chat bridge protocol (pinned in the clients repository under
/// <c>contracts/webview-chat-protocol/</c>). The page only renders; every API call runs here, so
/// the API key never reaches the page.
/// </summary>
/// <remarks>
/// The page may be loaded from a server outside the host application's control, so it is
/// treated as untrusted:
/// <list type="bullet">
/// <item>The page must come over https, or plain http on this machine only.</item>
/// <item>Messages from any origin other than the page's are ignored.</item>
/// <item>Only the chat operations are available, never arbitrary requests.</item>
/// <item>The product, reporter and application context come from the host, never the page.</item>
/// <item>Conversations and Generic Intake turns are reachable only when this bridge returned them
/// to the page, so the page cannot reach other reporters' conversations.</item>
/// </list>
/// Results are the API's JSON for each operation, unchanged. String request IDs from hosted pages
/// and numeric IDs from the bundled prototype are echoed unchanged; a message without an ID is
/// ignored.
/// </remarks>
public sealed class WebChatBridge
{
    private const int MaxMessageLength = 32768;
    private const int MaxRequestIdLength = 64;
    private const int MaxCursorLength = 1024;

    private static readonly HashSet<string> RetryableModelFailures = new(StringComparer.Ordinal)
    {
        "throttled", "transient-unavailable", "timed-out", "output-unparseable", "unknown",
    };

    private readonly INevaBridgeWebChatApi _api;
    private readonly string _productId;
    private readonly NevaBridgeReporter _reporter;
    private readonly IReadOnlyDictionary<string, string>? _applicationContext;
    private readonly string _allowedOrigin;
    private readonly Func<CancellationToken, Task<PickedFile?>>? _pickFile;

    // Messages can be handled concurrently, so both sets are guarded by one lock.
    private readonly HashSet<string> _startedConversations = new(StringComparer.Ordinal);
    private readonly HashSet<string> _startedTurns = new(StringComparer.Ordinal);
    private readonly object _sessionLock = new();

    public WebChatBridge(
        INevaBridgeWebChatApi api,
        string productId,
        NevaBridgeReporter reporter,
        IReadOnlyDictionary<string, string>? applicationContext,
        Uri pageOrigin,
        Func<CancellationToken, Task<PickedFile?>>? pickFile = null)
    {
        _api = api;
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
    /// message must be dropped: it did not come from the page's origin, or it carries no request ID.
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

        if (request is null || ReadRequestId(request) is not { } id)
        {
            return null;
        }

        try
        {
            return await AnswerAsync(id, request, cancellationToken).ConfigureAwait(false);
        }
        catch (UnsupportedRequestException error)
        {
            return Failure(id, "UnsupportedRequest", error.Message);
        }
        catch (NevaBridgeApiException error)
        {
            return ApiFailure(id, error);
        }
        catch (NevaBridgeUploadException error)
        {
            return Failure(id, "UploadFailed", error.Message, error.StatusCode, retryable: true);
        }
        catch (Exception error) when (
            error is HttpRequestException
            || (error is TaskCanceledException && !cancellationToken.IsCancellationRequested))
        {
            return Failure(id, "NetworkFailure", "NevaBridge could not be reached.", retryable: true);
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

    private Task<string> AnswerAsync(JsonValue id, JsonObject request, CancellationToken cancellationToken) =>
        ReadString(request, "type") switch
        {
            "startConversation" => StartAsync(id, request, cancellationToken),
            "appendMessage" => AppendAsync(id, request, cancellationToken),
            "submitReport" => SubmitAsync(id, request, cancellationToken),
            "getFeatures" => GetFeaturesAsync(id, cancellationToken),
            "attachFile" => AttachFileAsync(id, request, cancellationToken),
            "startIntakeConversation" => StartIntakeAsync(id, request, cancellationToken),
            "startIntakeTurn" => StartTurnAsync(id, request, cancellationToken),
            "getIntakeTurnEvents" => GetTurnEventsAsync(id, request, cancellationToken),
            "getConversation" => GetConversationAsync(id, request, cancellationToken),
            "deleteAttachment" => DeleteAttachmentAsync(id, request, cancellationToken),
            _ => throw new UnsupportedRequestException("This request type is not supported."),
        };

    private async Task<string> StartAsync(JsonValue id, JsonObject request, CancellationToken cancellationToken)
    {
        string text = ReadText(request);
        string? category = ReadString(request, "category");
        if (request["category"] is not null && (category is null || !ReportCategories.All.Contains(category)))
        {
            throw new UnsupportedRequestException("category must be one of: " + string.Join(", ", ReportCategories.All) + ".");
        }

        JsonElement turn = await _api.StartConversationAsync(
            _productId,
            _reporter,
            new StartConversationRequest(new UserMessage(text))
            {
                Category = category,
                ApplicationContext = _applicationContext,
            },
            cancellationToken).ConfigureAwait(false);
        Remember(
            _startedConversations,
            ReadString(turn, "assistantMessage", "conversationId") ?? ReadString(turn, "userMessage", "conversationId"));
        return Success(id, turn);
    }

    private async Task<string> AppendAsync(JsonValue id, JsonObject request, CancellationToken cancellationToken)
    {
        string? conversationId = ReadStartedConversation(request);
        string text = ReadText(request);
        if (conversationId is null)
        {
            return UnknownConversation(id);
        }

        return Success(
            id,
            await _api.AppendMessageAsync(conversationId, new AppendMessageRequest(text), cancellationToken).ConfigureAwait(false));
    }

    private async Task<string> SubmitAsync(JsonValue id, JsonObject request, CancellationToken cancellationToken)
    {
        string? conversationId = ReadStartedConversation(request);
        string reportId = ReadRequiredString(request, "reportId");
        if (conversationId is null)
        {
            return UnknownConversation(id);
        }

        return Success(
            id,
            await _api.SubmitReportAsync(conversationId, reportId, cancellationToken).ConfigureAwait(false));
    }

    private async Task<string> GetFeaturesAsync(JsonValue id, CancellationToken cancellationToken)
    {
        JsonElement features = await _api.GetFeaturesAsync(cancellationToken).ConfigureAwait(false);
        if (_pickFile is not null || features.ValueKind != JsonValueKind.Object)
        {
            return Success(id, features);
        }

        // Without a file picker this host cannot attach files, whatever the tenant may do.
        JsonObject effective = JsonNode.Parse(features.GetRawText())!.AsObject();
        effective["chatAttachments"] = false;
        return Success(id, effective);
    }

    private async Task<string> AttachFileAsync(JsonValue id, JsonObject request, CancellationToken cancellationToken)
    {
        if (ReadStartedConversation(request) is not { } conversationId)
        {
            return UnknownConversation(id);
        }

        if (_pickFile is null)
        {
            throw new UnsupportedRequestException("This application does not offer file attachments.");
        }

        PickedFile? file = await _pickFile(cancellationToken).ConfigureAwait(false);
        if (file is null)
        {
            return Failure(id, "Cancelled", "No file was chosen.");
        }

        return Success(
            id,
            await _api.AttachFileAsync(conversationId, _reporter.Id, file, cancellationToken).ConfigureAwait(false));
    }

    private async Task<string> StartIntakeAsync(JsonValue id, JsonObject request, CancellationToken cancellationToken)
    {
        string text = ReadText(request);
        JsonElement started = await _api.StartIntakeConversationAsync(
            _productId,
            _reporter,
            new StartIntakeConversationRequest(new UserMessage(text)) { ApplicationContext = _applicationContext },
            cancellationToken).ConfigureAwait(false);
        Remember(_startedConversations, ReadString(started, "conversationId"));
        Remember(_startedTurns, ReadString(started, "turnId"));
        return Success(id, started);
    }

    private async Task<string> StartTurnAsync(JsonValue id, JsonObject request, CancellationToken cancellationToken)
    {
        string? conversationId = ReadStartedConversation(request);
        string text = ReadText(request);
        if (request["expectedRevision"] is not JsonValue revisionValue
            || revisionValue.GetValueKind() != JsonValueKind.Number
            || !revisionValue.TryGetValue(out int expectedRevision)
            || expectedRevision < 0)
        {
            throw new UnsupportedRequestException("expectedRevision must be a whole number from 0.");
        }

        bool? finish = null;
        if (request["finish"] is { } finishNode)
        {
            if (finishNode is not JsonValue finishValue || !finishValue.TryGetValue(out bool finishFlag))
            {
                throw new UnsupportedRequestException("finish must be true or false.");
            }

            finish = finishFlag;
        }

        if (conversationId is null)
        {
            return UnknownConversation(id);
        }

        JsonElement started = await _api.StartIntakeTurnAsync(
            conversationId,
            new StartIntakeTurnRequest(text, expectedRevision) { Finish = finish },
            cancellationToken).ConfigureAwait(false);
        Remember(_startedTurns, ReadString(started, "turnId"));
        return Success(id, started);
    }

    private async Task<string> GetTurnEventsAsync(JsonValue id, JsonObject request, CancellationToken cancellationToken)
    {
        string turnId = ReadRequiredString(request, "turnId");
        string? after = ReadString(request, "after");
        if (request["after"] is not null && after is not { Length: > 0 and <= MaxCursorLength })
        {
            throw new UnsupportedRequestException($"after must contain 1 to {MaxCursorLength} characters.");
        }

        bool known;
        lock (_sessionLock)
        {
            known = _startedTurns.Contains(turnId);
        }

        if (!known)
        {
            return Failure(id, "UnknownTurn", "This turn was not started in this chat.");
        }

        return Success(
            id,
            await _api.GetIntakeTurnEventsAsync(turnId, after, cancellationToken).ConfigureAwait(false));
    }

    private async Task<string> GetConversationAsync(JsonValue id, JsonObject request, CancellationToken cancellationToken)
    {
        if (ReadStartedConversation(request) is not { } conversationId)
        {
            return UnknownConversation(id);
        }

        JsonElement detail = await _api.GetConversationAsync(conversationId, cancellationToken).ConfigureAwait(false);
        Remember(_startedTurns, ReadString(detail, "activeTurnId"));
        return Success(id, detail);
    }

    private async Task<string> DeleteAttachmentAsync(JsonValue id, JsonObject request, CancellationToken cancellationToken)
    {
        if (ReadStartedConversation(request) is not { } conversationId)
        {
            return UnknownConversation(id);
        }

        await _api.DeleteAttachmentAsync(conversationId, ReadRequiredString(request, "attachmentId"), cancellationToken)
            .ConfigureAwait(false);

        // The API answers 204 without a body; the protocol's result for it is the empty object.
        return Success(id, new JsonObject());
    }

    private string ApiFailure(JsonValue id, NevaBridgeApiException error)
    {
        if (error.ErrorName is null)
        {
            // The gateway in front of NevaBridge answered, typically after its time limit.
            return Failure(id, "EdgeFailure", error.Message, error.StatusCode, retryable: true);
        }

        bool retryable = error.StatusCode is 408 or 429
            || (error.ErrorName == "ModelInvocationFailed"
                && error.Category is not null
                && RetryableModelFailures.Contains(error.Category));
        string? turnId = error is { StatusCode: 409, ErrorName: "IntakeTurnRunning" } ? error.TurnId : null;
        Remember(_startedTurns, turnId);
        return Failure(id, error.ErrorName, error.Message, error.StatusCode, retryable, turnId);
    }

    private void Remember(HashSet<string> started, string? value)
    {
        if (value is null)
        {
            return;
        }

        lock (_sessionLock)
        {
            started.Add(value);
        }
    }

    private string? ReadStartedConversation(JsonObject request)
    {
        string conversationId = ReadRequiredString(request, "conversationId");
        lock (_sessionLock)
        {
            return _startedConversations.Contains(conversationId) ? conversationId : null;
        }
    }

    private static string ReadText(JsonObject request) =>
        ReadString(request, "text")?.Trim() is { Length: > 0 and <= MaxMessageLength } text
            ? text
            : throw new UnsupportedRequestException($"text must contain 1 to {MaxMessageLength} characters.");

    private static string ReadRequiredString(JsonObject request, string name) =>
        ReadString(request, name) is { Length: > 0 } value
            ? value
            : throw new UnsupportedRequestException($"{name} is required.");

    private static string UnknownConversation(JsonValue id) =>
        Failure(id, "UnknownConversation", "This conversation was not started in this chat.");

    private static string Success(JsonValue id, JsonElement result) =>
        Success(id, JsonNode.Parse(result.GetRawText()));

    private static string Success(JsonValue id, JsonNode? result) =>
        new JsonObject
        {
            ["id"] = id.DeepClone(),
            ["ok"] = true,
            ["result"] = result,
        }.ToJsonString();

    private static string Failure(
        JsonValue id,
        string name,
        string message,
        int? status = null,
        bool retryable = false,
        string? turnId = null)
    {
        JsonObject error = new()
        {
            ["name"] = name,
            ["message"] = message,
            ["status"] = status,
            ["retryable"] = retryable,
        };
        if (turnId is not null)
        {
            error["turnId"] = turnId;
        }

        return new JsonObject
        {
            ["id"] = id.DeepClone(),
            ["ok"] = false,
            ["error"] = error,
        }.ToJsonString();
    }

    private static string? ReadString(JsonObject request, string name) =>
        request[name] is JsonValue value && value.TryGetValue(out string? text) ? text : null;

    private static string? ReadString(JsonElement element, params string[] path)
    {
        foreach (string name in path)
        {
            if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(name, out element))
            {
                return null;
            }
        }

        return element.ValueKind == JsonValueKind.String ? element.GetString() : null;
    }

    private static JsonValue? ReadRequestId(JsonObject request) =>
        request["id"] is JsonValue value
        && ((value.TryGetValue(out string? text) && text is { Length: > 0 and <= MaxRequestIdLength })
            || (value.GetValueKind() == JsonValueKind.Number && value.TryGetValue(out long _)))
            ? value
            : null;

    private static string OriginOf(Uri uri) => uri.GetLeftPart(UriPartial.Authority);

    /// <summary>The message is not a valid request of protocol major 1.</summary>
    private sealed class UnsupportedRequestException(string message) : Exception(message);
}
