using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using NevaBridge.Chat;
using NevaBridge.Chat.Settings;
using NevaBridge.Chat.WebChat;

namespace NevaBridge.Sample.Client;

/// <summary>
/// Calls the NevaBridge Tenant Integration API directly with the tenant API key. It stands in for
/// the NevaBridge .NET SDK until that is published; copy it into your application, or replace it
/// with a client that calls your own backend.
/// </summary>
/// <remarks>
/// Whoever runs this code can read the key. Use it in a desktop application only when every
/// user of that application may see all of the tenant's conversations and act under any
/// reporter identity and role. Otherwise, move these calls into a service the users cannot
/// inspect, and give the chat controls an <see cref="INevaBridgeConversations"/>, an
/// <see cref="INevaBridgeIntakeConversations"/>, an <see cref="INevaBridgeVoice"/> and an
/// <see cref="INevaBridgeWebChatApi"/> that call it.
/// </remarks>
public sealed class NevaBridgeClient :
    INevaBridgeConversations,
    INevaBridgeIntakeConversations,
    INevaBridgeVoice,
    INevaBridgeWebChatApi
{
    /// <summary>Seconds a turn-events read waits for a first new event.</summary>
    private const int PollWaitSeconds = 20;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly HttpClient _http;
    private readonly Uri _baseUrl;
    private readonly string _apiKey;

    /// <param name="http">Share one instance for the lifetime of the application.</param>
    /// <param name="settings">Where NevaBridge is and the API key to call it with.</param>
    public NevaBridgeClient(HttpClient http, NevaBridgeSettings settings)
    {
        _http = http;

        // A base address without a trailing slash would drop its last path segment when relative
        // request paths are resolved against it.
        _baseUrl = settings.ApiBaseUrl.AbsoluteUri.EndsWith('/')
            ? settings.ApiBaseUrl
            : new Uri(settings.ApiBaseUrl.AbsoluteUri + "/");
        _apiKey = settings.ApiKey;
    }

    public Task<ConversationTurn> StartConversationAsync(
        string productId,
        NevaBridgeReporter reporter,
        StartConversationRequest request,
        CancellationToken cancellationToken = default) =>
        SendAsync<ConversationTurn>(StartConversationRequestMessage(productId, reporter, request), cancellationToken);

    public Task<ConversationTurn> AppendMessageAsync(
        string conversationId,
        AppendMessageRequest request,
        CancellationToken cancellationToken = default) =>
        SendAsync<ConversationTurn>(AppendMessageRequestMessage(conversationId, request), cancellationToken);

    public Task<ConversationDetail> GetConversationAsync(
        string conversationId,
        CancellationToken cancellationToken = default) =>
        SendAsync<ConversationDetail>(GetConversationRequestMessage(conversationId), cancellationToken);

    public Task<SubmitReportResult> SubmitReportAsync(
        string conversationId,
        string reportId,
        CancellationToken cancellationToken = default) =>
        SendAsync<SubmitReportResult>(SubmitReportRequestMessage(conversationId, reportId), cancellationToken);

    public async Task<IReadOnlyDictionary<string, bool>> GetFeaturesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await SendAsync<Dictionary<string, bool>>(
                CreateRequest(HttpMethod.Get, "v1/features", null),
                cancellationToken).ConfigureAwait(false);
        }
        catch (NevaBridgeApiException error) when (IsMissingFeaturesOperation(error))
        {
            return new Dictionary<string, bool>();
        }
    }

    public async Task<Attachment> AttachFileAsync(
        string conversationId,
        string uploaderId,
        PickedFile file,
        CancellationToken cancellationToken = default) =>
        await SendAsync<Attachment>(
            await UploadAsync(conversationId, uploaderId, file, cancellationToken).ConfigureAwait(false),
            cancellationToken).ConfigureAwait(false);

    public Task<IntakeConversationStarted> StartIntakeConversationAsync(
        string productId,
        NevaBridgeReporter reporter,
        StartIntakeConversationRequest request,
        CancellationToken cancellationToken = default) =>
        SendAsync<IntakeConversationStarted>(
            StartIntakeConversationRequestMessage(productId, reporter, request),
            cancellationToken);

    public Task<HeldIntakeConversation> StartHeldIntakeConversationAsync(
        string productId,
        NevaBridgeReporter reporter,
        StartHeldIntakeConversationRequest request,
        CancellationToken cancellationToken = default)
    {
        HttpRequestMessage message = CreateRequest(
            HttpMethod.Post,
            $"v1/products/{Uri.EscapeDataString(productId)}/intake-conversations",
            request);
        AddActorHeaders(message, reporter);
        return SendAsync<HeldIntakeConversation>(message, cancellationToken);
    }

    public async Task<AttachmentUpload> RequestAttachmentUploadAsync(
        string conversationId,
        string uploaderId,
        AttachmentUploadRequest file,
        CancellationToken cancellationToken = default)
    {
        UploadTicket ticket = await SendAsync<UploadTicket>(AdmitRequest(conversationId, uploaderId, file), cancellationToken)
            .ConfigureAwait(false);
        return new AttachmentUpload(ticket.AttachmentId, file.FileName, ticket.UploadUrl, ticket.ExpiresAt);
    }

    public async Task<Attachment> UploadAttachmentAsync(
        string conversationId,
        AttachmentUpload upload,
        PickedFile file,
        CancellationToken cancellationToken = default)
    {
        NevaBridgeUploadException? failed = null;
        try
        {
            await PutAsync(upload.UploadUrl, file, cancellationToken).ConfigureAwait(false);
        }
        catch (NevaBridgeUploadException error)
        {
            // The bytes may still have arrived, from this attempt or an earlier one; confirming tells.
            failed = error;
        }

        try
        {
            return await SendAsync<Attachment>(ConfirmRequest(conversationId, upload.AttachmentId), cancellationToken).ConfigureAwait(false);
        }
        catch (NevaBridgeApiException error) when (failed is not null && error.ErrorName == "AttachmentNotUploaded")
        {
            throw failed;
        }
    }

    public Task<IntakeTurnStarted> StartIntakeTurnAsync(
        string conversationId,
        StartIntakeTurnRequest request,
        CancellationToken cancellationToken = default) =>
        SendAsync<IntakeTurnStarted>(StartIntakeTurnRequestMessage(conversationId, request), cancellationToken);

    public Task<IntakeTurnEvents> GetIntakeTurnEventsAsync(
        string turnId,
        string? after,
        CancellationToken cancellationToken = default) =>
        SendAsync<IntakeTurnEvents>(IntakeTurnEventsRequestMessage(turnId, after), cancellationToken);

    public async Task<IReadOnlyList<Attachment>> ListAttachmentsAsync(
        string conversationId,
        CancellationToken cancellationToken = default) =>
        (await SendAsync<AttachmentList>(
            CreateRequest(HttpMethod.Get, $"v1/conversations/{Uri.EscapeDataString(conversationId)}/attachments", null),
            cancellationToken).ConfigureAwait(false)).Attachments;

    public async Task DeleteAttachmentAsync(
        string conversationId,
        string attachmentId,
        CancellationToken cancellationToken = default)
    {
        using HttpRequestMessage message = CreateRequest(
            HttpMethod.Post,
            $"v1/conversations/{Uri.EscapeDataString(conversationId)}/actions/delete-attachment",
            new AttachmentCommand(attachmentId));
        using HttpResponseMessage response = await _http.SendAsync(message, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            string body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            throw ApiErrors.FromResponse((int)response.StatusCode, body);
        }
    }

    /// <remarks>The session's address is a credential; this client never logs it.</remarks>
    public Task<TranscriptionSession> CreateTranscriptionSessionAsync(
        VoiceLanguage language,
        CancellationToken cancellationToken = default) =>
        SendAsync<TranscriptionSession>(
            CreateRequest(HttpMethod.Post, "v1/voice/transcription-sessions", new TranscriptionSessionRequest(language.Code)),
            cancellationToken);

    public Task<SpeechSynthesis> SynthesizeSpeechAsync(
        string text,
        VoiceLanguage language,
        CancellationToken cancellationToken = default) =>
        SendAsync<SpeechSynthesis>(
            CreateRequest(HttpMethod.Post, "v1/voice/speech", new SpeechRequest(text, language.Code)),
            cancellationToken);

    Task<JsonElement> INevaBridgeWebChatApi.StartConversationAsync(
        string productId,
        NevaBridgeReporter reporter,
        StartConversationRequest request,
        CancellationToken cancellationToken) =>
        SendJsonAsync(StartConversationRequestMessage(productId, reporter, request), cancellationToken);

    Task<JsonElement> INevaBridgeWebChatApi.AppendMessageAsync(
        string conversationId,
        AppendMessageRequest request,
        CancellationToken cancellationToken) =>
        SendJsonAsync(AppendMessageRequestMessage(conversationId, request), cancellationToken);

    Task<JsonElement> INevaBridgeWebChatApi.SubmitReportAsync(
        string conversationId,
        string reportId,
        CancellationToken cancellationToken) =>
        SendJsonAsync(SubmitReportRequestMessage(conversationId, reportId), cancellationToken);

    async Task<JsonElement> INevaBridgeWebChatApi.GetFeaturesAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await SendJsonAsync(CreateRequest(HttpMethod.Get, "v1/features", null), cancellationToken)
                .ConfigureAwait(false);
        }
        catch (NevaBridgeApiException error) when (IsMissingFeaturesOperation(error))
        {
            using JsonDocument none = JsonDocument.Parse("{}");
            return none.RootElement.Clone();
        }
    }

    async Task<JsonElement> INevaBridgeWebChatApi.AttachFileAsync(
        string conversationId,
        string uploaderId,
        PickedFile file,
        CancellationToken cancellationToken) =>
        await SendJsonAsync(
            await UploadAsync(conversationId, uploaderId, file, cancellationToken).ConfigureAwait(false),
            cancellationToken).ConfigureAwait(false);

    Task<JsonElement> INevaBridgeWebChatApi.StartIntakeConversationAsync(
        string productId,
        NevaBridgeReporter reporter,
        StartIntakeConversationRequest request,
        CancellationToken cancellationToken) =>
        SendJsonAsync(StartIntakeConversationRequestMessage(productId, reporter, request), cancellationToken);

    Task<JsonElement> INevaBridgeWebChatApi.StartIntakeTurnAsync(
        string conversationId,
        StartIntakeTurnRequest request,
        CancellationToken cancellationToken) =>
        SendJsonAsync(StartIntakeTurnRequestMessage(conversationId, request), cancellationToken);

    Task<JsonElement> INevaBridgeWebChatApi.GetIntakeTurnEventsAsync(
        string turnId,
        string? after,
        CancellationToken cancellationToken) =>
        SendJsonAsync(IntakeTurnEventsRequestMessage(turnId, after), cancellationToken);

    Task<JsonElement> INevaBridgeWebChatApi.GetConversationAsync(
        string conversationId,
        CancellationToken cancellationToken) =>
        SendJsonAsync(GetConversationRequestMessage(conversationId), cancellationToken);

    /// <summary>
    /// Header values are ASCII, and NevaBridge percent-decodes this one. A name of printable ASCII
    /// without <c>%</c> reads the same either way, so it is sent as it is.
    /// </summary>
    private static string ActorNameHeader(string displayName) =>
        displayName.All(c => c is >= ' ' and <= '~' and not '%')
            ? displayName
            : Uri.EscapeDataString(displayName);

    /// <summary>A NevaBridge that predates the features operation offers no gated features.</summary>
    private static bool IsMissingFeaturesOperation(NevaBridgeApiException error) =>
        error.StatusCode == 404 && error.ErrorName == "RouteNotFound";

    private static void AddActorHeaders(HttpRequestMessage message, NevaBridgeReporter reporter)
    {
        message.Headers.Add("X-Actor-Id", reporter.Id);
        if (!string.IsNullOrWhiteSpace(reporter.DisplayName))
        {
            message.Headers.Add("X-Actor-Name", ActorNameHeader(reporter.DisplayName));
        }

        if (reporter.Roles is { Count: > 0 } roles)
        {
            message.Headers.Add("X-Actor-Roles", string.Join(',', roles));
        }
    }

    private HttpRequestMessage StartConversationRequestMessage(
        string productId,
        NevaBridgeReporter reporter,
        StartConversationRequest request)
    {
        HttpRequestMessage message = CreateRequest(
            HttpMethod.Post,
            $"v1/products/{Uri.EscapeDataString(productId)}/conversations",
            request);
        AddActorHeaders(message, reporter);
        return message;
    }

    private HttpRequestMessage StartIntakeConversationRequestMessage(
        string productId,
        NevaBridgeReporter reporter,
        StartIntakeConversationRequest request)
    {
        HttpRequestMessage message = CreateRequest(
            HttpMethod.Post,
            $"v1/products/{Uri.EscapeDataString(productId)}/intake-conversations",
            request);
        AddActorHeaders(message, reporter);
        return message;
    }

    private HttpRequestMessage StartIntakeTurnRequestMessage(string conversationId, StartIntakeTurnRequest request) =>
        CreateRequest(HttpMethod.Post, $"v1/intake-conversations/{Uri.EscapeDataString(conversationId)}/turns", request);

    private HttpRequestMessage IntakeTurnEventsRequestMessage(string turnId, string? after)
    {
        string query = (after is null ? string.Empty : $"after={Uri.EscapeDataString(after)}&") + $"wait={PollWaitSeconds}";
        return CreateRequest(HttpMethod.Get, $"v1/intake-turns/{Uri.EscapeDataString(turnId)}/events?{query}", null);
    }

    private HttpRequestMessage AppendMessageRequestMessage(string conversationId, AppendMessageRequest request) =>
        CreateRequest(HttpMethod.Post, $"v1/conversations/{Uri.EscapeDataString(conversationId)}/messages", request);

    private HttpRequestMessage GetConversationRequestMessage(string conversationId) =>
        CreateRequest(HttpMethod.Get, $"v1/conversations/{Uri.EscapeDataString(conversationId)}", null);

    private HttpRequestMessage SubmitReportRequestMessage(string conversationId, string reportId) =>
        CreateRequest(
            HttpMethod.Post,
            $"v1/conversations/{Uri.EscapeDataString(conversationId)}/reports/{Uri.EscapeDataString(reportId)}/submit",
            null);

    /// <summary>
    /// Admits the file, uploads its bytes, and returns the request that confirms the upload.
    /// </summary>
    private async Task<HttpRequestMessage> UploadAsync(
        string conversationId,
        string uploaderId,
        PickedFile file,
        CancellationToken cancellationToken)
    {
        // 1. Admit the file: NevaBridge checks its type and size and returns a one-time upload address.
        UploadTicket ticket = await SendAsync<UploadTicket>(
            AdmitRequest(conversationId, uploaderId, new AttachmentUploadRequest(file.Name, file.SizeBytes)),
            cancellationToken).ConfigureAwait(false);

        // 2. Upload the bytes straight to storage.
        await PutAsync(ticket.UploadUrl, file, cancellationToken).ConfigureAwait(false);

        // 3. Confirm, which attaches the file to the conversation.
        return ConfirmRequest(conversationId, ticket.AttachmentId);
    }

    private HttpRequestMessage AdmitRequest(string conversationId, string uploaderId, AttachmentUploadRequest file)
    {
        HttpRequestMessage admit = CreateRequest(
            HttpMethod.Post,
            $"v1/conversations/{Uri.EscapeDataString(conversationId)}/actions/request-attachment-upload",
            file);
        admit.Headers.Add("X-Actor-Id", uploaderId);
        return admit;
    }

    /// <summary>Uploads the bytes to a presigned address, which gets no API key.</summary>
    private async Task PutAsync(Uri uploadUrl, PickedFile file, CancellationToken cancellationToken)
    {
        await using Stream content = file.OpenRead();
        using HttpRequestMessage upload = new(HttpMethod.Put, uploadUrl);
        upload.Headers.IfNoneMatch.Add(EntityTagHeaderValue.Any);
        upload.Content = new StreamContent(content);
        upload.Content.Headers.ContentLength = file.SizeBytes;
        HttpResponseMessage uploaded;
        try
        {
            uploaded = await _http.SendAsync(upload, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException error)
        {
            throw new NevaBridgeUploadException(null, error);
        }

        using (uploaded)
        {
            if (!uploaded.IsSuccessStatusCode)
            {
                throw new NevaBridgeUploadException((int)uploaded.StatusCode);
            }
        }
    }

    private HttpRequestMessage ConfirmRequest(string conversationId, string attachmentId) =>
        CreateRequest(
            HttpMethod.Post,
            $"v1/conversations/{Uri.EscapeDataString(conversationId)}/actions/confirm-attachment-upload",
            new AttachmentCommand(attachmentId));

    private HttpRequestMessage CreateRequest(HttpMethod method, string path, object? body)
    {
        HttpRequestMessage message = new(method, new Uri(_baseUrl, path));
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
        message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (body is not null)
        {
            message.Content = JsonContent.Create(body, body.GetType(), options: JsonOptions);
        }

        return message;
    }

    private async Task<JsonElement> SendJsonAsync(HttpRequestMessage message, CancellationToken cancellationToken)
    {
        using (message)
        using (HttpResponseMessage response = await _http.SendAsync(message, cancellationToken).ConfigureAwait(false))
        {
            string body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                throw ApiErrors.FromResponse((int)response.StatusCode, body);
            }

            using JsonDocument document = JsonDocument.Parse(body);
            return document.RootElement.Clone();
        }
    }

    private async Task<T> SendAsync<T>(HttpRequestMessage message, CancellationToken cancellationToken)
    {
        using (message)
        using (HttpResponseMessage response = await _http.SendAsync(message, cancellationToken).ConfigureAwait(false))
        {
            string body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                throw ApiErrors.FromResponse((int)response.StatusCode, body);
            }

            return JsonSerializer.Deserialize<T>(body, JsonOptions)
                ?? throw new JsonException($"NevaBridge answered HTTP {(int)response.StatusCode} with an empty body.");
        }
    }

    private sealed record UploadTicket(string AttachmentId, Uri UploadUrl, DateTimeOffset ExpiresAt);

    private sealed record AttachmentCommand(string AttachmentId);

    private sealed record AttachmentList(IReadOnlyList<Attachment> Attachments);

    private sealed record TranscriptionSessionRequest(string LanguageCode);

    private sealed record SpeechRequest(string Text, string LanguageCode);
}
