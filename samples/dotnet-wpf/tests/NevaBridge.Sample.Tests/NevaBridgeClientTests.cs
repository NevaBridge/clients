using System.Text;
using System.Text.Json;
using NevaBridge.Chat;
using NevaBridge.Chat.Settings;
using NevaBridge.Sample.Client;
using NevaBridge.Sample.Tests.Support;

namespace NevaBridge.Sample.Tests;

public sealed class NevaBridgeClientTests : IAsyncLifetime
{
    private const string ApiKey = "sk_test_not_a_real_key";
    private const string ProductId = "product-00000000-0000-4000-8000-000000000001";
    private const string ConversationId = "conversation-00000000-0000-4000-8000-000000000002";
    private const string ReportId = "report-00000000-0000-4000-8000-000000000003";

    private const string TurnJson = $$"""
        {
          "userMessage": {
            "id": "m-1", "conversationId": "{{ConversationId}}", "role": "user",
            "content": "Saving an invoice freezes the app.", "createdAt": "2026-09-30T10:00:00Z"
          },
          "assistantMessage": {
            "id": "m-2", "conversationId": "{{ConversationId}}", "role": "assistant",
            "content": "Which screen were you on?", "createdAt": "2026-09-30T10:00:03Z"
          },
          "reports": [
            {
              "id": "{{ReportId}}", "conversationId": "{{ConversationId}}",
              "templateId": "bug-default", "templateVersion": "3",
              "category": "bug_report", "displayTitle": "Invoice save freezes",
              "status": "reporting_in_progress",
              "structured": { "summary": "Saving an invoice freezes the app." },
              "createdAt": "2026-09-30T10:00:03Z", "updatedAt": "2026-09-30T10:00:03Z"
            }
          ],
          "displayTitle": "Invoice save freezes"
        }
        """;

    private FakeNevaBridgeApi _api = null!;
    private HttpClient _http = null!;
    private NevaBridgeClient _client = null!;

    public async Task InitializeAsync()
    {
        _api = await FakeNevaBridgeApi.StartAsync();
        _http = new HttpClient();
        _client = new NevaBridgeClient(_http, new NevaBridgeSettings(_api.BaseUrl, ProductId, ApiKey, null));
    }

    public async Task DisposeAsync()
    {
        _http.Dispose();
        await _api.DisposeAsync();
    }

    [Fact]
    public async Task StartConversation_sends_the_key_the_reporter_and_the_first_message()
    {
        // Given
        _api.Respond(201, TurnJson);
        NevaBridgeReporter reporter = new("CORP\\jdoe", "Jürgen Doe", ["tenant", "customer"]);
        StartConversationRequest request = new(new UserMessage("Saving an invoice freezes the app."))
        {
            ApplicationContext = new Dictionary<string, string> { ["screen"] = "Invoices" },
        };

        // When
        ConversationTurn turn = await _client.StartConversationAsync(ProductId, reporter, request);

        // Then
        RecordedRequest sent = Assert.Single(_api.Requests);
        Assert.Equal("POST", sent.Method);
        Assert.Equal($"/v1/products/{ProductId}/conversations", sent.PathAndQuery);
        Assert.Equal($"Bearer {ApiKey}", sent.Headers["Authorization"]);
        Assert.Equal("CORP\\jdoe", sent.Headers["X-Actor-Id"]);
        Assert.Equal("J%C3%BCrgen%20Doe", sent.Headers["X-Actor-Name"]);
        Assert.Equal("tenant,customer", sent.Headers["X-Actor-Roles"]);
        using JsonDocument body = JsonDocument.Parse(sent.Body);
        Assert.Equal(
            "Saving an invoice freezes the app.",
            body.RootElement.GetProperty("userMessage").GetProperty("content").GetString());
        Assert.Equal(
            "Invoices",
            body.RootElement.GetProperty("applicationContext").GetProperty("screen").GetString());
        Assert.False(body.RootElement.TryGetProperty("templateId", out _), "unset options are omitted");

        Assert.Equal("Which screen were you on?", turn.AssistantMessage.Content);
        Report report = Assert.Single(turn.Reports);
        Assert.Equal(ReportId, report.Id);
        Assert.Equal("reporting_in_progress", report.Status);
        Assert.Equal("Saving an invoice freezes the app.", report.Structured["summary"]);
    }

    [Fact]
    public async Task StartConversation_omits_optional_reporter_headers_when_not_given()
    {
        // Given
        _api.Respond(201, TurnJson);

        // When
        await _client.StartConversationAsync(
            ProductId,
            new NevaBridgeReporter("user-1"),
            new StartConversationRequest(new UserMessage("Hello")));

        // Then
        RecordedRequest sent = Assert.Single(_api.Requests);
        Assert.False(sent.Headers.ContainsKey("X-Actor-Name"));
        Assert.False(sent.Headers.ContainsKey("X-Actor-Roles"));
    }

    [Fact]
    public async Task AppendMessage_posts_the_next_message_without_reporter_headers()
    {
        // Given
        _api.Respond(200, TurnJson);

        // When
        ConversationTurn turn = await _client.AppendMessageAsync(
            ConversationId,
            new AppendMessageRequest("On the invoice list, every time."));

        // Then
        RecordedRequest sent = Assert.Single(_api.Requests);
        Assert.Equal("POST", sent.Method);
        Assert.Equal($"/v1/conversations/{ConversationId}/messages", sent.PathAndQuery);
        Assert.Equal($"Bearer {ApiKey}", sent.Headers["Authorization"]);
        Assert.False(sent.Headers.ContainsKey("X-Actor-Id"));
        using JsonDocument body = JsonDocument.Parse(sent.Body);
        Assert.Equal("On the invoice list, every time.", body.RootElement.GetProperty("content").GetString());
        Assert.Equal("Invoice save freezes", turn.DisplayTitle);
    }

    [Fact]
    public async Task GetConversation_reads_the_transcript_and_reports()
    {
        // Given
        _api.Respond(200, $$"""
            {
              "id": "{{ConversationId}}", "tenantId": "tenant-1", "productId": "{{ProductId}}",
              "reporterId": "CORP\\jdoe", "status": "in_progress",
              "createdAt": "2026-09-30T10:00:00Z", "updatedAt": "2026-09-30T10:00:03Z",
              "messages": [
                { "id": "m-1", "conversationId": "{{ConversationId}}", "role": "user",
                  "content": "Hi", "createdAt": "2026-09-30T10:00:00Z" }
              ],
              "reports": []
            }
            """);

        // When
        ConversationDetail detail = await _client.GetConversationAsync(ConversationId);

        // Then
        Assert.Equal($"/v1/conversations/{ConversationId}", Assert.Single(_api.Requests).PathAndQuery);
        Assert.Equal("GET", _api.Requests[0].Method);
        Assert.Equal("in_progress", detail.Status);
        Assert.Equal("Hi", Assert.Single(detail.Messages).Content);
    }

    [Fact]
    public async Task SubmitReport_returns_the_finalized_report_and_each_connector_outcome()
    {
        // Given
        _api.Respond(200, $$"""
            {
              "report": {
                "id": "{{ReportId}}", "conversationId": "{{ConversationId}}",
                "templateId": "bug-default", "templateVersion": "3", "status": "submitted",
                "structured": {}, "submissionOrigin": "manual",
                "createdAt": "2026-09-30T10:00:03Z", "updatedAt": "2026-09-30T10:05:00Z"
              },
              "connectors": {
                "github": { "status": "success", "ticket": "https://github.com/acme/app/issues/7" },
                "slack": { "status": "failed", "error": "channel_not_found" }
              }
            }
            """);

        // When
        SubmitReportResult result = await _client.SubmitReportAsync(ConversationId, ReportId);

        // Then
        RecordedRequest sent = Assert.Single(_api.Requests);
        Assert.Equal("POST", sent.Method);
        Assert.Equal($"/v1/conversations/{ConversationId}/reports/{ReportId}/submit", sent.PathAndQuery);
        Assert.Equal("submitted", result.Report.Status);
        Assert.NotNull(result.Connectors);
        Assert.Equal("https://github.com/acme/app/issues/7", result.Connectors["github"].Ticket);
        Assert.Equal("failed", result.Connectors["slack"].Status);
        Assert.Equal("channel_not_found", result.Connectors["slack"].Error);
    }

    [Fact]
    public async Task A_NevaBridge_error_surfaces_its_status_name_and_message()
    {
        // Given
        _api.Respond(402, """{"error":"QuotaExceeded","message":"The tenant has no credits left."}""");

        // When
        NevaBridgeApiException error = await Assert.ThrowsAsync<NevaBridgeApiException>(
            () => _client.AppendMessageAsync(ConversationId, new AppendMessageRequest("Hi")));

        // Then
        Assert.Equal(402, error.StatusCode);
        Assert.Equal("QuotaExceeded", error.ErrorName);
        Assert.Equal("The tenant has no credits left.", error.Message);
        Assert.False(error.IsRetryable);
        Assert.False(error.IsEdgeFailure);
    }

    [Fact]
    public async Task A_model_failure_carries_its_category_and_is_retryable_when_throttled()
    {
        // Given
        _api.Respond(429, """
            {"error":"ModelInvocationFailed","message":"The writer model is throttled.",
             "category":"throttled","provider":"bedrock","role":"writer","catalogKey":"claude-sonnet-5"}
            """);

        // When
        NevaBridgeApiException error = await Assert.ThrowsAsync<NevaBridgeApiException>(
            () => _client.AppendMessageAsync(ConversationId, new AppendMessageRequest("Hi")));

        // Then
        Assert.Equal("ModelInvocationFailed", error.ErrorName);
        Assert.Equal("throttled", error.Category);
        Assert.True(error.IsRetryable);
    }

    [Fact]
    public async Task A_gateway_timeout_is_told_apart_from_a_NevaBridge_error()
    {
        // Given
        _api.Respond(504, """{"message":"Endpoint request timed out"}""");

        // When
        NevaBridgeApiException error = await Assert.ThrowsAsync<NevaBridgeApiException>(
            () => _client.AppendMessageAsync(ConversationId, new AppendMessageRequest("Hi")));

        // Then
        Assert.Equal(504, error.StatusCode);
        Assert.True(error.IsEdgeFailure);
        Assert.Null(error.ErrorName);
        Assert.True(error.IsRetryable);
    }

    [Fact]
    public async Task Features_are_read_from_the_tenant()
    {
        // Given
        _api.Respond(200, """{"chatAttachments":true,"knowledgebase":false,"somethingNew":true}""");

        // When
        IReadOnlyDictionary<string, bool> features = await _client.GetFeaturesAsync();

        // Then
        RecordedRequest sent = Assert.Single(_api.Requests);
        Assert.Equal("GET", sent.Method);
        Assert.Equal("/v1/features", sent.PathAndQuery);
        Assert.Equal($"Bearer {ApiKey}", sent.Headers["Authorization"]);
        Assert.True(features["chatAttachments"]);
        Assert.False(features["knowledgebase"]);
    }

    [Fact]
    public async Task An_API_without_the_features_operation_reports_every_feature_off()
    {
        // Given
        _api.Respond(404, """{"error":"RouteNotFound","message":"Route does not exist"}""");

        // When
        IReadOnlyDictionary<string, bool> features = await _client.GetFeaturesAsync();

        // Then
        Assert.Empty(features);
    }

    [Fact]
    public async Task Attaching_a_file_requests_an_upload_uploads_the_bytes_and_confirms()
    {
        // Given
        byte[] bytes = Encoding.UTF8.GetBytes("error at line 7");
        _api.Respond(200, $$"""
            {"attachmentId":"attachment-1","uploadUrl":"{{_api.BaseUrl}}upload/attachment-1?signature=abc",
             "expiresAt":"2026-09-30T10:15:00Z"}
            """);
        _api.Respond(200, "");
        _api.Respond(200, """
            {"id":"attachment-1","fileName":"app.log","kind":"text","sizeBytes":15,
             "uploadedBy":"api:CORP\\jdoe","uploadedAt":"2026-09-30T10:02:00Z"}
            """);

        // When
        Attachment attachment = await _client.AttachFileAsync(
            ConversationId,
            "CORP\\jdoe",
            new PickedFile("app.log", bytes.Length, () => new MemoryStream(bytes)));

        // Then
        Assert.Equal(3, _api.Requests.Count);
        RecordedRequest request = _api.Requests[0];
        Assert.Equal($"/v1/conversations/{ConversationId}/actions/request-attachment-upload", request.PathAndQuery);
        Assert.Equal("CORP\\jdoe", request.Headers["X-Actor-Id"]);
        using (JsonDocument body = JsonDocument.Parse(request.Body))
        {
            Assert.Equal("app.log", body.RootElement.GetProperty("fileName").GetString());
            Assert.Equal(bytes.Length, body.RootElement.GetProperty("sizeBytes").GetInt32());
        }

        RecordedRequest upload = _api.Requests[1];
        Assert.Equal("PUT", upload.Method);
        Assert.Equal("/upload/attachment-1?signature=abc", upload.PathAndQuery);
        Assert.Equal("*", upload.Headers["If-None-Match"]);
        Assert.False(upload.Headers.ContainsKey("Authorization"), "the presigned URL must not receive the API key");
        Assert.Equal("error at line 7", upload.Body);

        RecordedRequest confirm = _api.Requests[2];
        Assert.Equal($"/v1/conversations/{ConversationId}/actions/confirm-attachment-upload", confirm.PathAndQuery);
        Assert.Contains("attachment-1", confirm.Body);
        Assert.Equal("app.log", attachment.FileName);
    }

    [Fact]
    public async Task Values_added_to_the_API_later_do_not_break_reading_a_turn()
    {
        // Given
        string future = TurnJson
            .Replace("\"reporting_in_progress\"", "\"awaiting_translation\"")
            .Replace("\"displayTitle\": \"Invoice save freezes\"\n}", "\"displayTitle\": \"x\", \"newField\": {\"a\": 1}\n}");
        _api.Respond(200, future);

        // When
        ConversationTurn turn = await _client.AppendMessageAsync(ConversationId, new AppendMessageRequest("Hi"));

        // Then
        Assert.Equal("awaiting_translation", Assert.Single(turn.Reports).Status);
    }
}
