using System.Text;
using System.Text.Json;
using NevaBridge.Chat.Tests.Support;
using NevaBridge.Chat.WebChat;

namespace NevaBridge.Chat.Tests;

public sealed class WebChatBridgeTests
{
    private const string ProductId = "product-00000000-0000-4000-8000-000000000001";
    private const string ConversationId = FakeConversations.ConversationId;
    private const string ReportId = FakeConversations.ReportId;

    private static readonly Uri PageOrigin = new("https://webchat.nevabridge.example/");
    private static readonly Uri PageUrl = new("https://webchat.nevabridge.example/index.html");

    private readonly FakeConversations _nevaBridge = new();
    private PickedFile? _nextPickedFile;
    private Exception? _pickerFailure;

    private WebChatBridge CreateBridge(bool canPickFiles = true) =>
        new(
            _nevaBridge,
            ProductId,
            new NevaBridgeReporter("CORP\\jdoe", "Jane Doe"),
            new Dictionary<string, string> { ["screen"] = "Invoices" },
            PageOrigin,
            canPickFiles ? _ => _pickerFailure is null ? Task.FromResult(_nextPickedFile) : Task.FromException<PickedFile?>(_pickerFailure) : null);

    [Theory]
    [InlineData("getFeatures", "")]
    [InlineData("startConversation", ",\"text\":\"It crashes.\"")]
    [InlineData("unsupported", "")]
    public async Task A_reply_preserves_the_hosted_pages_string_request_id(string operation, string fields)
    {
        // Given
        WebChatBridge bridge = CreateBridge();

        // When
        string? response = await bridge.HandleAsync(
            PageUrl,
            $$"""{"id":"r17","type":"{{operation}}"{{fields}}}""");

        // Then
        Assert.NotNull(response);
        using JsonDocument reply = JsonDocument.Parse(response);
        Assert.Equal(JsonValueKind.String, reply.RootElement.GetProperty("id").ValueKind);
        Assert.Equal("r17", reply.RootElement.GetProperty("id").GetString());
        Assert.Equal(operation != "unsupported", reply.RootElement.GetProperty("ok").GetBoolean());
    }

    [Fact]
    public async Task An_unexpected_adapter_failure_returns_a_safe_host_error()
    {
        // Given
        _nevaBridge.FailWith = new InvalidOperationException("Private adapter details");

        // When
        string? response = await CreateBridge().HandleAsync(PageUrl, """{"id":"r18","type":"getFeatures"}""");

        // Then
        Assert.NotNull(response);
        using JsonDocument reply = JsonDocument.Parse(response);
        Assert.False(reply.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("r18", reply.RootElement.GetProperty("id").GetString());
        Assert.Equal("HostError", reply.RootElement.GetProperty("error").GetProperty("name").GetString());
        Assert.DoesNotContain("Private adapter details", response);
    }

    [Fact]
    public async Task Starting_a_conversation_uses_the_host_product_reporter_and_context()
    {
        // Given
        WebChatBridge bridge = CreateBridge();

        // When
        string? response = await bridge.HandleAsync(
            PageUrl,
            """{"id":1,"type":"startConversation","text":"It crashes.","productId":"product-evil","reporterId":"admin"}""");

        // Then
        RecordedCall call = Assert.Single(_nevaBridge.Calls);
        Assert.Equal("start", call.Operation);
        Assert.Equal(ProductId, call.Arguments["productId"]);
        Assert.Equal("CORP\\jdoe", ((NevaBridgeReporter)call.Arguments["reporter"]!).Id);
        StartConversationRequest request = (StartConversationRequest)call.Arguments["request"]!;
        Assert.Equal("It crashes.", request.UserMessage.Content);
        Assert.Equal("Invoices", request.ApplicationContext!["screen"]);

        using JsonDocument reply = JsonDocument.Parse(response!);
        Assert.Equal(1, reply.RootElement.GetProperty("id").GetInt32());
        Assert.True(reply.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal(
            "When does it crash?",
            reply.RootElement.GetProperty("result").GetProperty("assistantMessage").GetProperty("content").GetString());
    }

    [Theory]
    [InlineData("bug_report")]
    [InlineData("feature_request")]
    [InlineData("support_request")]
    public async Task The_report_type_chosen_on_the_page_starts_the_conversation(string category)
    {
        // When
        await CreateBridge().HandleAsync(
            PageUrl,
            $$"""{"id":1,"type":"startConversation","text":"It crashes.","category":"{{category}}"}""");

        // Then
        StartConversationRequest request = (StartConversationRequest)Assert.Single(_nevaBridge.Calls).Arguments["request"]!;
        Assert.Equal(category, request.Category);
    }

    [Fact]
    public async Task A_report_type_the_API_does_not_offer_is_refused()
    {
        // When
        string? response = await CreateBridge().HandleAsync(
            PageUrl,
            """{"id":6,"type":"startConversation","text":"It crashes.","category":"generic_intake"}""");

        // Then
        Assert.Empty(_nevaBridge.Calls);
        Assert.Equal("InvalidRequest", ErrorName(response));
    }

    [Fact]
    public void A_page_served_without_https_from_another_machine_gets_no_bridge()
    {
        // When
        ArgumentException error = Assert.Throws<ArgumentException>(() => new WebChatBridge(
            _nevaBridge,
            ProductId,
            new NevaBridgeReporter("CORP\\jdoe"),
            null,
            new Uri("http://chat.example.com/index.html")));

        // Then
        Assert.Contains("https", error.Message);
    }

    [Fact]
    public async Task A_message_from_another_origin_is_ignored_without_calling_NevaBridge()
    {
        // When
        string? response = await CreateBridge().HandleAsync(
            new Uri("https://attacker.example/index.html"),
            """{"id":1,"type":"startConversation","text":"hi"}""");

        // Then
        Assert.Null(response);
        Assert.Empty(_nevaBridge.Calls);
    }

    [Fact]
    public async Task A_follow_up_continues_a_conversation_this_page_started()
    {
        // Given
        WebChatBridge bridge = CreateBridge();
        await bridge.HandleAsync(PageUrl, """{"id":1,"type":"startConversation","text":"It crashes."}""");

        // When
        string? response = await bridge.HandleAsync(
            PageUrl,
            $$"""{"id":2,"type":"appendMessage","conversationId":"{{ConversationId}}","text":"On save."}""");

        // Then
        Assert.Equal("append", _nevaBridge.Calls[1].Operation);
        Assert.Equal(ConversationId, _nevaBridge.Calls[1].Arguments["conversationId"]);
        Assert.True(Ok(response));
    }

    [Theory]
    [InlineData($$"""{"id":7,"type":"appendMessage","conversationId":"{{ConversationId}}","text":"hi"}""")]
    [InlineData($$"""{"id":7,"type":"submitReport","conversationId":"{{ConversationId}}","reportId":"{{ReportId}}"}""")]
    [InlineData($$"""{"id":7,"type":"attachFile","conversationId":"{{ConversationId}}"}""")]
    public async Task A_conversation_this_page_did_not_start_is_refused(string message)
    {
        // When
        string? response = await CreateBridge().HandleAsync(PageUrl, message);

        // Then
        Assert.Empty(_nevaBridge.Calls);
        Assert.Equal("UnknownConversation", ErrorName(response));
    }

    [Fact]
    public async Task Submitting_a_report_of_a_started_conversation_returns_the_outcome()
    {
        // Given
        WebChatBridge bridge = CreateBridge();
        await bridge.HandleAsync(PageUrl, """{"id":1,"type":"startConversation","text":"It crashes."}""");

        // When
        string? response = await bridge.HandleAsync(
            PageUrl,
            $$"""{"id":2,"type":"submitReport","conversationId":"{{ConversationId}}","reportId":"{{ReportId}}"}""");

        // Then
        Assert.Equal(ReportId, _nevaBridge.Calls[1].Arguments["reportId"]);
        using JsonDocument reply = JsonDocument.Parse(response!);
        JsonElement result = reply.RootElement.GetProperty("result");
        Assert.Equal("submitted", result.GetProperty("report").GetProperty("status").GetString());
        Assert.Equal(
            "https://github.com/acme/app/issues/9",
            result.GetProperty("connectors").GetProperty("github").GetProperty("ticket").GetString());
    }

    [Fact]
    public async Task The_page_learns_the_tenant_features_from_the_host()
    {
        // Given
        _nevaBridge.Features = new Dictionary<string, bool> { ["chatAttachments"] = true, ["knowledgebase"] = false };

        // When
        string? response = await CreateBridge().HandleAsync(PageUrl, """{"id":3,"type":"getFeatures"}""");

        // Then
        using JsonDocument reply = JsonDocument.Parse(response!);
        JsonElement result = reply.RootElement.GetProperty("result");
        Assert.True(result.GetProperty("chatAttachments").GetBoolean());
        Assert.False(result.GetProperty("knowledgebase").GetBoolean());
    }

    [Fact]
    public async Task Attaching_a_file_uploads_what_the_user_picked_as_the_reporter()
    {
        // Given
        WebChatBridge bridge = CreateBridge();
        await bridge.HandleAsync(PageUrl, """{"id":1,"type":"startConversation","text":"It crashes."}""");
        byte[] bytes = Encoding.UTF8.GetBytes("error at line 7");
        _nextPickedFile = new PickedFile("app.log", bytes.Length, () => new MemoryStream(bytes));

        // When
        string? response = await bridge.HandleAsync(
            PageUrl,
            $$"""{"id":4,"type":"attachFile","conversationId":"{{ConversationId}}"}""");

        // Then
        RecordedCall call = _nevaBridge.Calls[1];
        Assert.Equal("attach", call.Operation);
        Assert.Equal("CORP\\jdoe", call.Arguments["uploaderId"]);
        Assert.Equal(bytes, (byte[])call.Arguments["bytes"]!);
        using JsonDocument reply = JsonDocument.Parse(response!);
        Assert.Equal("app.log", reply.RootElement.GetProperty("result").GetProperty("fileName").GetString());
    }

    [Fact]
    public async Task Closing_the_file_picker_attaches_nothing()
    {
        // Given
        WebChatBridge bridge = CreateBridge();
        await bridge.HandleAsync(PageUrl, """{"id":1,"type":"startConversation","text":"It crashes."}""");
        _nextPickedFile = null;

        // When
        string? response = await bridge.HandleAsync(
            PageUrl,
            $$"""{"id":4,"type":"attachFile","conversationId":"{{ConversationId}}"}""");

        // Then
        Assert.Single(_nevaBridge.Calls);
        using JsonDocument reply = JsonDocument.Parse(response!);
        Assert.True(reply.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal(JsonValueKind.Null, reply.RootElement.GetProperty("result").ValueKind);
    }

    [Fact]
    public async Task A_host_without_a_file_picker_does_not_offer_attachments()
    {
        // Given
        WebChatBridge bridge = CreateBridge(canPickFiles: false);
        await bridge.HandleAsync(PageUrl, """{"id":1,"type":"startConversation","text":"It crashes."}""");

        // When
        string? response = await bridge.HandleAsync(
            PageUrl,
            $$"""{"id":4,"type":"attachFile","conversationId":"{{ConversationId}}"}""");

        // Then
        Assert.Equal("UnsupportedOperation", ErrorName(response));
    }

    [Fact]
    public async Task A_host_without_a_file_picker_reports_attachments_as_off()
    {
        // Given
        _nevaBridge.Features = new Dictionary<string, bool> { ["chatAttachments"] = true, ["knowledgebase"] = true };

        // When
        string? response = await CreateBridge(canPickFiles: false).HandleAsync(PageUrl, """{"id":3,"type":"getFeatures"}""");

        // Then
        using JsonDocument reply = JsonDocument.Parse(response!);
        JsonElement result = reply.RootElement.GetProperty("result");
        Assert.False(result.GetProperty("chatAttachments").GetBoolean());
        Assert.True(result.GetProperty("knowledgebase").GetBoolean());
    }

    [Fact]
    public async Task A_picked_file_that_cannot_be_read_becomes_a_chat_error()
    {
        // Given
        WebChatBridge bridge = CreateBridge();
        await bridge.HandleAsync(PageUrl, """{"id":1,"type":"startConversation","text":"It crashes."}""");
        _pickerFailure = new FileNotFoundException("The file was deleted.");

        // When
        string? response = await bridge.HandleAsync(
            PageUrl,
            $$"""{"id":4,"type":"attachFile","conversationId":"{{ConversationId}}"}""");

        // Then
        Assert.Equal("FileUnreadable", ErrorName(response));
    }

    [Fact]
    public async Task An_answer_from_NevaBridge_that_cannot_be_read_becomes_a_chat_error()
    {
        // Given
        _nevaBridge.FailWith = new JsonException("Unexpected end of data.");

        // When
        string? response = await CreateBridge().HandleAsync(PageUrl, """{"id":3,"type":"startConversation","text":"hi"}""");

        // Then
        Assert.Equal("InvalidResponse", ErrorName(response));
    }

    [Fact]
    public async Task An_API_error_reaches_the_page_as_a_structured_error()
    {
        // Given
        _nevaBridge.FailWith = new NevaBridgeApiException(429, "ModelInvocationFailed", "throttled", "The writer model is throttled.");

        // When
        string? response = await CreateBridge().HandleAsync(PageUrl, """{"id":3,"type":"startConversation","text":"hi"}""");

        // Then
        using JsonDocument reply = JsonDocument.Parse(response!);
        JsonElement error = reply.RootElement.GetProperty("error");
        Assert.False(reply.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("ModelInvocationFailed", error.GetProperty("name").GetString());
        Assert.Equal(429, error.GetProperty("status").GetInt32());
        Assert.True(error.GetProperty("retryable").GetBoolean());
        Assert.Equal("The writer model is throttled.", error.GetProperty("message").GetString());
    }

    [Theory]
    [InlineData("""{"id":4,"type":"listConversations"}""", 4, "UnsupportedOperation")]
    [InlineData("""{"id":5,"type":"startConversation","text":"   "}""", 5, "InvalidRequest")]
    [InlineData("""not json""", 0, "InvalidRequest")]
    public async Task Requests_outside_the_protocol_are_refused(string message, int expectedId, string expectedError)
    {
        // When
        string? response = await CreateBridge().HandleAsync(PageUrl, message);

        // Then
        Assert.Empty(_nevaBridge.Calls);
        using JsonDocument reply = JsonDocument.Parse(response!);
        Assert.Equal(expectedId, reply.RootElement.GetProperty("id").GetInt32());
        Assert.Equal(expectedError, reply.RootElement.GetProperty("error").GetProperty("name").GetString());
    }

    private static bool Ok(string? response)
    {
        using JsonDocument reply = JsonDocument.Parse(response!);
        return reply.RootElement.GetProperty("ok").GetBoolean();
    }

    private static string? ErrorName(string? response)
    {
        using JsonDocument reply = JsonDocument.Parse(response!);
        return reply.RootElement.GetProperty("error").GetProperty("name").GetString();
    }
}
