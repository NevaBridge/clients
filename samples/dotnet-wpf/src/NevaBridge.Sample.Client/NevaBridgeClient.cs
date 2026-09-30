using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using NevaBridge.Chat;
using NevaBridge.Chat.Settings;

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
/// inspect, and give the chat control an <see cref="INevaBridgeConversations"/> that calls it.
/// </remarks>
public sealed class NevaBridgeClient : INevaBridgeConversations
{
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
        CancellationToken cancellationToken = default)
    {
        HttpRequestMessage message = CreateRequest(
            HttpMethod.Post,
            $"v1/products/{Uri.EscapeDataString(productId)}/conversations",
            request);
        message.Headers.Add("X-Actor-Id", reporter.Id);
        if (!string.IsNullOrWhiteSpace(reporter.DisplayName))
        {
            // Header values are ASCII; NevaBridge percent-decodes this one.
            message.Headers.Add("X-Actor-Name", Uri.EscapeDataString(reporter.DisplayName));
        }

        if (reporter.Roles is { Count: > 0 } roles)
        {
            message.Headers.Add("X-Actor-Roles", string.Join(',', roles));
        }

        return SendAsync<ConversationTurn>(message, cancellationToken);
    }

    public Task<ConversationTurn> AppendMessageAsync(
        string conversationId,
        AppendMessageRequest request,
        CancellationToken cancellationToken = default) =>
        SendAsync<ConversationTurn>(
            CreateRequest(
                HttpMethod.Post,
                $"v1/conversations/{Uri.EscapeDataString(conversationId)}/messages",
                request),
            cancellationToken);

    public Task<ConversationDetail> GetConversationAsync(
        string conversationId,
        CancellationToken cancellationToken = default) =>
        SendAsync<ConversationDetail>(
            CreateRequest(HttpMethod.Get, $"v1/conversations/{Uri.EscapeDataString(conversationId)}", null),
            cancellationToken);

    public Task<SubmitReportResult> SubmitReportAsync(
        string conversationId,
        string reportId,
        CancellationToken cancellationToken = default) =>
        SendAsync<SubmitReportResult>(
            CreateRequest(
                HttpMethod.Post,
                $"v1/conversations/{Uri.EscapeDataString(conversationId)}/reports/{Uri.EscapeDataString(reportId)}/submit",
                null),
            cancellationToken);

    public async Task<IReadOnlyDictionary<string, bool>> GetFeaturesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await SendAsync<Dictionary<string, bool>>(
                CreateRequest(HttpMethod.Get, "v1/features", null),
                cancellationToken).ConfigureAwait(false);
        }
        catch (NevaBridgeApiException error) when (error.StatusCode == 404 && error.ErrorName == "RouteNotFound")
        {
            // A NevaBridge that predates the features operation offers no gated features.
            return new Dictionary<string, bool>();
        }
    }

    public async Task<Attachment> AttachFileAsync(
        string conversationId,
        string uploaderId,
        PickedFile file,
        CancellationToken cancellationToken = default)
    {
        string conversationPath = $"v1/conversations/{Uri.EscapeDataString(conversationId)}";

        // 1. Admit the file: NevaBridge checks its type and size and returns a one-time upload address.
        HttpRequestMessage admit = CreateRequest(
            HttpMethod.Post,
            conversationPath + "/actions/request-attachment-upload",
            new UploadRequest(file.Name, file.SizeBytes));
        admit.Headers.Add("X-Actor-Id", uploaderId);
        UploadTicket ticket = await SendAsync<UploadTicket>(admit, cancellationToken).ConfigureAwait(false);

        // 2. Upload the bytes straight to storage. The address is presigned, so it gets no API key.
        await using (Stream content = file.OpenRead())
        {
            using HttpRequestMessage upload = new(HttpMethod.Put, ticket.UploadUrl);
            upload.Headers.IfNoneMatch.Add(EntityTagHeaderValue.Any);
            upload.Content = new StreamContent(content);
            upload.Content.Headers.ContentLength = file.SizeBytes;
            using HttpResponseMessage uploaded = await _http.SendAsync(upload, cancellationToken).ConfigureAwait(false);
            if (!uploaded.IsSuccessStatusCode)
            {
                throw new NevaBridgeApiException(
                    (int)uploaded.StatusCode,
                    "UploadFailed",
                    null,
                    $"The file could not be uploaded (HTTP {(int)uploaded.StatusCode}).");
            }
        }

        // 3. Confirm, which attaches the file to the conversation.
        return await SendAsync<Attachment>(
            CreateRequest(
                HttpMethod.Post,
                conversationPath + "/actions/confirm-attachment-upload",
                new AttachmentCommand(ticket.AttachmentId)),
            cancellationToken).ConfigureAwait(false);
    }

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

    private sealed record UploadRequest(string FileName, long SizeBytes);

    private sealed record UploadTicket(string AttachmentId, Uri UploadUrl, DateTimeOffset ExpiresAt);

    private sealed record AttachmentCommand(string AttachmentId);
}
