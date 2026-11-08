using System.Text.Json.Nodes;
using NevaBridge.Chat;
using NevaBridge.Chat.Settings;
using NevaBridge.Sample.Client;
using NevaBridge.Sample.Tests.Support;

namespace NevaBridge.Sample.Tests;

public sealed class IntakeClientTests : IAsyncLifetime
{
    private const string ApiKey = "sk_test_not_a_real_key";
    private const string ProductId = "product-00000000-0000-4000-8000-000000000001";
    private const string ConversationId = "conversation-00000000-0000-4000-8000-000000000002";
    private const string TurnId = "intaketurn-00000000-0000-4000-8000-000000000004";

    private FakeNevaBridgeApi _api = null!;
    private HttpClient _http = null!;
    private INevaBridgeIntakeConversations _client = null!;

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
    public async Task StartIntakeConversation_sends_the_reporter_the_message_and_the_contact()
    {
        // Given
        _api.Respond(202, $$"""{"conversationId": "{{ConversationId}}", "turnId": "{{TurnId}}"}""");
        NevaBridgeReporter reporter = new("CORP\\jdoe", "Jane Doe", ["customer"]);
        StartIntakeConversationRequest request = new(new UserMessage("Saving an invoice freezes the app."))
        {
            ApplicationContext = new Dictionary<string, string> { ["screen"] = "Invoices" },
            ReporterContact = new ReporterContact { Email = "jane@example.com" },
        };

        // When
        IntakeConversationStarted started = await _client.StartIntakeConversationAsync(ProductId, reporter, request);

        // Then
        Assert.Equal(new IntakeConversationStarted(ConversationId, TurnId), started);
        RecordedRequest sent = Assert.Single(_api.Requests);
        Assert.Equal("POST", sent.Method);
        Assert.Equal($"/v1/products/{ProductId}/intake-conversations", sent.PathAndQuery);
        Assert.Equal($"Bearer {ApiKey}", sent.Headers["Authorization"]);
        Assert.Equal("CORP\\jdoe", sent.Headers["X-Actor-Id"]);
        Assert.Equal("Jane Doe", sent.Headers["X-Actor-Name"]);
        Assert.Equal("customer", sent.Headers["X-Actor-Roles"]);
        AssertSameJson(
            """{"userMessage":{"content":"Saving an invoice freezes the app."},"applicationContext":{"screen":"Invoices"},"reporterContact":{"email":"jane@example.com"}}""",
            sent.Body);
    }

    [Fact]
    public async Task StartIntakeConversation_without_contact_sends_none()
    {
        // Given
        _api.Respond(202, $$"""{"conversationId": "{{ConversationId}}", "turnId": "{{TurnId}}"}""");

        // When
        await _client.StartIntakeConversationAsync(ProductId, new NevaBridgeReporter("jdoe"), new StartIntakeConversationRequest(new UserMessage("Hello")));

        // Then
        AssertSameJson("""{"userMessage":{"content":"Hello"}}""", Assert.Single(_api.Requests).Body);
    }

    [Fact]
    public async Task StartHeldIntakeConversation_sends_the_files_and_reads_their_uploads()
    {
        // Given
        _api.Respond(201, $$"""
            {"conversationId":"{{ConversationId}}","attachments":[{"attachmentId":"attachment-1","fileName":"app.log",
             "uploadUrl":"{{_api.BaseUrl}}upload/attachment-1?signature=abc","expiresAt":"2026-09-30T10:15:00Z"}]}
            """);
        StartHeldIntakeConversationRequest request = new([new AttachmentUploadRequest("app.log", 15)])
        {
            ReporterContact = new ReporterContact { Email = "jane@example.com" },
        };

        // When
        HeldIntakeConversation held = await _client.StartHeldIntakeConversationAsync(
            ProductId,
            new NevaBridgeReporter("CORP\\jdoe", "Jane Doe"),
            request);

        // Then
        Assert.Equal(ConversationId, held.ConversationId);
        AttachmentUpload upload = Assert.Single(held.Attachments);
        Assert.Equal("attachment-1", upload.AttachmentId);
        Assert.Equal(new Uri(_api.BaseUrl, "upload/attachment-1?signature=abc"), upload.UploadUrl);
        RecordedRequest sent = Assert.Single(_api.Requests);
        Assert.Equal("POST", sent.Method);
        Assert.Equal($"/v1/products/{ProductId}/intake-conversations", sent.PathAndQuery);
        Assert.Equal("CORP\\jdoe", sent.Headers["X-Actor-Id"]);
        AssertSameJson(
            """{"attachments":[{"fileName":"app.log","sizeBytes":15}],"reporterContact":{"email":"jane@example.com"}}""",
            sent.Body);
    }

    [Fact]
    public async Task UploadAttachment_puts_the_bytes_to_the_issued_address_and_confirms()
    {
        // Given
        byte[] bytes = System.Text.Encoding.UTF8.GetBytes("error at line 7");
        _api.Respond(200, "");
        _api.Respond(200, """
            {"id":"attachment-1","fileName":"app.log","kind":"text","sizeBytes":15,
             "uploadedBy":"api:jdoe","uploadedAt":"2026-09-30T10:02:00Z"}
            """);
        AttachmentUpload upload = new("attachment-1", "app.log", new Uri(_api.BaseUrl, "upload/attachment-1?signature=abc"), DateTimeOffset.UnixEpoch);

        // When
        Attachment attachment = await _client.UploadAttachmentAsync(
            ConversationId,
            upload,
            new PickedFile("app.log", bytes.Length, () => new MemoryStream(bytes)));

        // Then
        Assert.Equal("app.log", attachment.FileName);
        Assert.Equal(2, _api.Requests.Count);
        RecordedRequest put = _api.Requests[0];
        Assert.Equal("PUT", put.Method);
        Assert.Equal("/upload/attachment-1?signature=abc", put.PathAndQuery);
        Assert.False(put.Headers.ContainsKey("Authorization"), "the presigned URL must not receive the API key");
        Assert.Equal("error at line 7", put.Body);
        RecordedRequest confirm = _api.Requests[1];
        Assert.Equal($"/v1/conversations/{ConversationId}/actions/confirm-attachment-upload", confirm.PathAndQuery);
        AssertSameJson("""{"attachmentId":"attachment-1"}""", confirm.Body);
    }

    [Fact]
    public async Task UploadAttachment_whose_upload_failed_reports_it_when_NevaBridge_has_no_bytes()
    {
        // Given
        _api.Respond(503, "");
        _api.Respond(409, """{"error":"AttachmentNotUploaded","message":"The file has not been uploaded."}""");
        AttachmentUpload upload = new("attachment-1", "app.log", new Uri(_api.BaseUrl, "upload/attachment-1"), DateTimeOffset.UnixEpoch);

        // When
        NevaBridgeUploadException error = await Assert.ThrowsAsync<NevaBridgeUploadException>(() =>
            _client.UploadAttachmentAsync(ConversationId, upload, new PickedFile("app.log", 1, () => new MemoryStream(new byte[1]))));

        // Then: confirming tells whether the bytes arrived despite the failed upload
        Assert.Equal(503, error.StatusCode);
        Assert.Equal(2, _api.Requests.Count);
        Assert.Equal($"/v1/conversations/{ConversationId}/actions/confirm-attachment-upload", _api.Requests[1].PathAndQuery);
    }

    [Fact]
    public async Task UploadAttachment_whose_upload_failed_returns_the_file_when_its_bytes_arrived()
    {
        // Given: storage already holds the file from an earlier attempt whose answer was lost
        _api.Respond(412, "");
        _api.Respond(200, """
            {"id":"attachment-1","fileName":"app.log","kind":"text","sizeBytes":1,
             "uploadedBy":"api:jdoe","uploadedAt":"2026-09-30T10:02:00Z"}
            """);
        AttachmentUpload upload = new("attachment-1", "app.log", new Uri(_api.BaseUrl, "upload/attachment-1"), DateTimeOffset.UnixEpoch);

        // When
        Attachment attachment = await _client.UploadAttachmentAsync(ConversationId, upload, new PickedFile("app.log", 1, () => new MemoryStream(new byte[1])));

        // Then
        Assert.Equal("attachment-1", attachment.Id);
    }

    [Fact]
    public async Task A_start_whose_uploads_could_not_be_signed_names_the_stored_conversation()
    {
        // Given
        _api.Respond(424, $$"""{"error":"FailedDependency","message":"Upload URLs could not be signed.","conversationId":"{{ConversationId}}"}""");

        // When
        NevaBridgeApiException error = await Assert.ThrowsAsync<NevaBridgeApiException>(() =>
            _client.StartHeldIntakeConversationAsync(ProductId, new NevaBridgeReporter("jdoe"), new StartHeldIntakeConversationRequest([new AttachmentUploadRequest("app.log", 15)])));

        // Then
        Assert.Equal(424, error.StatusCode);
        Assert.Equal(ConversationId, error.ConversationId);
    }

    [Fact]
    public async Task StartIntakeTurn_sends_the_revision_and_finish()
    {
        // Given
        _api.Respond(202, $$"""{"turnId": "{{TurnId}}"}""");

        // When
        IntakeTurnStarted started = await _client.StartIntakeTurnAsync(
            ConversationId,
            new StartIntakeTurnRequest("I'm done.", 3) { Finish = true });

        // Then
        Assert.Equal(TurnId, started.TurnId);
        RecordedRequest sent = Assert.Single(_api.Requests);
        Assert.Equal($"/v1/intake-conversations/{ConversationId}/turns", sent.PathAndQuery);
        AssertSameJson("""{"content":"I'm done.","expectedRevision":3,"finish":true}""", sent.Body);
    }

    [Fact]
    public async Task StartIntakeTurn_refused_while_a_turn_runs_names_that_turn()
    {
        // Given
        _api.Respond(409, $$"""{"error": "IntakeTurnRunning", "message": "Another turn is running.", "turnId": "{{TurnId}}"}""");

        // When
        NevaBridgeApiException error = await Assert.ThrowsAsync<NevaBridgeApiException>(
            () => _client.StartIntakeTurnAsync(ConversationId, new StartIntakeTurnRequest("Hello", 1)));

        // Then
        Assert.Equal("IntakeTurnRunning", error.ErrorName);
        Assert.Equal(TurnId, error.TurnId);
    }

    [Fact]
    public async Task GetIntakeTurnEvents_waits_twenty_seconds_and_reads_every_event_kind()
    {
        // Given
        _api.Respond(200, """
            {
              "events": [
                {"type": "status", "phase": "tool", "tool": "searchKnowledge"},
                {"type": "text", "delta": "When "},
                {"type": "progress-from-a-later-version", "percent": 50},
                {"type": "error", "error": {"error": "QuotaExceeded", "message": "No credits left.", "statusCode": 402}}
              ],
              "cursor": "4",
              "finished": true
            }
            """);

        // When
        IntakeTurnEvents page = await _client.GetIntakeTurnEventsAsync(TurnId, "3");

        // Then
        Assert.Equal($"/v1/intake-turns/{TurnId}/events?after=3&wait=20", Assert.Single(_api.Requests).PathAndQuery);
        Assert.Equal("4", page.Cursor);
        Assert.True(page.Finished);
        Assert.Equal(
            [
                new IntakeTurnEvent("status") { Phase = "tool", Tool = "searchKnowledge" },
                new IntakeTurnEvent("text") { Delta = "When " },
                new IntakeTurnEvent("progress-from-a-later-version"),
                new IntakeTurnEvent("error") { Error = new IntakeTurnFailure("QuotaExceeded", "No credits left.", 402) },
            ],
            page.Events);
    }

    [Fact]
    public async Task GetIntakeTurnEvents_reads_from_the_first_event_without_a_cursor()
    {
        // Given
        _api.Respond(200, """{"events": [{"type": "done", "revision": 2}], "cursor": "1", "finished": true}""");

        // When
        IntakeTurnEvents page = await _client.GetIntakeTurnEventsAsync(TurnId, null);

        // Then
        Assert.Equal($"/v1/intake-turns/{TurnId}/events?wait=20", Assert.Single(_api.Requests).PathAndQuery);
        Assert.Equal(2, Assert.Single(page.Events).Revision);
    }

    [Fact]
    public async Task GetConversation_reads_the_revision_the_running_turn_and_the_filed_reports()
    {
        // Given
        _api.Respond(200, $$"""
            {
              "id": "{{ConversationId}}", "tenantId": "tenant-1", "productId": "{{ProductId}}", "reporterId": "jdoe",
              "createdAt": "2026-11-05T10:00:00Z", "updatedAt": "2026-11-05T10:01:00Z", "status": "closed",
              "revision": 4, "activeTurnId": "{{TurnId}}",
              "messages": [{"id": "m-1", "conversationId": "{{ConversationId}}", "role": "user", "content": "Hi", "createdAt": "2026-11-05T10:00:00Z"}],
              "reports": [{
                "id": "report-1", "conversationId": "{{ConversationId}}", "templateId": "system:intake-record", "templateVersion": "1",
                "category": "intake_record", "displayTitle": "Export question", "status": "closed", "statusReason": "answered",
                "structured": {}, "createdAt": "2026-11-05T10:01:00Z", "updatedAt": "2026-11-05T10:01:00Z"
              }]
            }
            """);

        // When
        ConversationDetail detail = await _client.GetConversationAsync(ConversationId);

        // Then
        Assert.Equal(4, detail.Revision);
        Assert.Equal(TurnId, detail.ActiveTurnId);
        Assert.True(detail.IsClosed);
        Report report = Assert.Single(detail.Reports);
        Assert.Equal(("intake_record", "Export question", "answered"), (report.Category, report.DisplayTitle, report.StatusReason));
    }

    [Fact]
    public async Task ListAttachments_reads_the_files_of_the_conversation()
    {
        // Given
        _api.Respond(200, """
            {"attachments": [{"id": "attachment-1", "fileName": "crash.log", "kind": "text", "sizeBytes": 12,
              "uploadedBy": "api:jdoe", "uploadedAt": "2026-11-05T10:00:00Z"}]}
            """);

        // When
        IReadOnlyList<Attachment> attachments = await _client.ListAttachmentsAsync(ConversationId);

        // Then
        Assert.Equal($"/v1/conversations/{ConversationId}/attachments", Assert.Single(_api.Requests).PathAndQuery);
        Assert.Equal(("attachment-1", "crash.log", 12L), (attachments[0].Id, attachments[0].FileName, attachments[0].SizeBytes));
    }

    [Fact]
    public async Task DeleteAttachment_names_the_file_and_accepts_an_empty_answer()
    {
        // Given
        _api.Respond(204, string.Empty);

        // When
        await _client.DeleteAttachmentAsync(ConversationId, "attachment-1");

        // Then
        RecordedRequest sent = Assert.Single(_api.Requests);
        Assert.Equal("POST", sent.Method);
        Assert.Equal($"/v1/conversations/{ConversationId}/actions/delete-attachment", sent.PathAndQuery);
        AssertSameJson("""{"attachmentId":"attachment-1"}""", sent.Body);
    }

    [Fact]
    public async Task DeleteAttachment_refused_fails_with_the_error()
    {
        // Given
        _api.Respond(403, """{"error": "Forbidden", "message": "Feature \"chatAttachments\" is not available for this tenant."}""");

        // When
        NevaBridgeApiException error = await Assert.ThrowsAsync<NevaBridgeApiException>(
            () => _client.DeleteAttachmentAsync(ConversationId, "attachment-1"));

        // Then
        Assert.Equal(403, error.StatusCode);
    }

    /// <summary>Equal JSON, whatever the serializer's escaping and spacing.</summary>
    private static void AssertSameJson(string expected, string actual) =>
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(expected), JsonNode.Parse(actual)), $"Expected {expected}, sent {actual}");
}
