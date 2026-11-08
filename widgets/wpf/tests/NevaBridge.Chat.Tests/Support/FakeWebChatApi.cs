using System.Text.Json;
using NevaBridge.Chat.WebChat;

namespace NevaBridge.Chat.Tests.Support;

/// <summary>
/// An in-memory NevaBridge for the web chat bridge that records every call and answers with the
/// API's JSON, so bridge tests can check exactly what reaches NevaBridge and what the page gets.
/// </summary>
public sealed class FakeWebChatApi : INevaBridgeWebChatApi
{
    public const string ConversationId = "conversation-00000000-0000-4000-8000-000000000002";
    public const string ReportId = "report-00000000-0000-4000-8000-000000000003";
    public const string TurnId = "intaketurn-00000000-0000-4000-8000-000000000004";
    public const string NextTurnId = "intaketurn-00000000-0000-4000-8000-000000000005";

    private readonly List<RecordedCall> _calls = [];

    public IReadOnlyList<RecordedCall> Calls => _calls;

    public string FeaturesJson { get; set; } = "{}";

    /// <summary>The turn a conversation read names as running, if any.</summary>
    public string? ActiveTurnId { get; set; }

    public Exception? FailWith { get; set; }

    public Task<JsonElement> StartConversationAsync(
        string productId,
        NevaBridgeReporter reporter,
        StartConversationRequest request,
        CancellationToken cancellationToken = default)
    {
        Record("start", ("productId", productId), ("reporter", reporter), ("request", request));
        return Json(Turn(request.UserMessage.Content));
    }

    public Task<JsonElement> AppendMessageAsync(
        string conversationId,
        AppendMessageRequest request,
        CancellationToken cancellationToken = default)
    {
        Record("append", ("conversationId", conversationId), ("request", request));
        return Json(Turn(request.Content));
    }

    public Task<JsonElement> SubmitReportAsync(
        string conversationId,
        string reportId,
        CancellationToken cancellationToken = default)
    {
        Record("submit", ("conversationId", conversationId), ("reportId", reportId));
        return Json($$$"""
            {
              "report": {"id": "{{{ReportId}}}", "conversationId": "{{{ConversationId}}}", "status": "submitted"},
              "connectors": {"github": {"status": "success", "ticket": "https://github.com/acme/app/issues/9"}}
            }
            """);
    }

    public Task<JsonElement> GetFeaturesAsync(CancellationToken cancellationToken = default)
    {
        Record("features");
        return Json(FeaturesJson);
    }

    public async Task<JsonElement> AttachFileAsync(
        string conversationId,
        string uploaderId,
        PickedFile file,
        CancellationToken cancellationToken = default)
    {
        using Stream content = file.OpenRead();
        using MemoryStream copy = new();
        await content.CopyToAsync(copy, cancellationToken);
        Record("attach", ("conversationId", conversationId), ("uploaderId", uploaderId), ("fileName", file.Name), ("bytes", copy.ToArray()));
        return await Json($$"""
            {
              "id": "attachment-1", "fileName": {{JsonSerializer.Serialize(file.Name)}}, "kind": "text",
              "sizeBytes": {{file.SizeBytes}}, "uploadedBy": {{JsonSerializer.Serialize("api:" + uploaderId)}},
              "uploadedAt": "2026-09-30T10:02:00.000Z"
            }
            """);
    }

    public Task<JsonElement> StartIntakeConversationAsync(
        string productId,
        NevaBridgeReporter reporter,
        StartIntakeConversationRequest request,
        CancellationToken cancellationToken = default)
    {
        Record("startIntake", ("productId", productId), ("reporter", reporter), ("request", request));
        return Json($$"""{"conversationId": "{{ConversationId}}", "turnId": "{{TurnId}}"}""");
    }

    public Task<JsonElement> StartIntakeTurnAsync(
        string conversationId,
        StartIntakeTurnRequest request,
        CancellationToken cancellationToken = default)
    {
        Record("startTurn", ("conversationId", conversationId), ("request", request));
        return Json($$"""{"turnId": "{{NextTurnId}}"}""");
    }

    public Task<JsonElement> GetIntakeTurnEventsAsync(
        string turnId,
        string? after,
        CancellationToken cancellationToken = default)
    {
        Record("events", ("turnId", turnId), ("after", after));
        return Json("""{"events": [{"type": "done", "revision": 1}], "cursor": "1", "finished": true}""");
    }

    public Task DeleteAttachmentAsync(
        string conversationId,
        string attachmentId,
        CancellationToken cancellationToken = default)
    {
        Record("deleteAttachment", ("conversationId", conversationId), ("attachmentId", attachmentId));
        return Task.CompletedTask;
    }

    public Task<JsonElement> GetConversationAsync(
        string conversationId,
        CancellationToken cancellationToken = default)
    {
        Record("read", ("conversationId", conversationId));
        string activeTurn = ActiveTurnId is null ? string.Empty : $$""", "activeTurnId": "{{ActiveTurnId}}" """;
        return Json($$"""{"id": "{{ConversationId}}", "revision": 1{{activeTurn}}}""");
    }

    private void Record(string operation, params (string Name, object? Value)[] arguments)
    {
        if (FailWith is not null)
        {
            throw FailWith;
        }

        _calls.Add(new RecordedCall(operation, arguments.ToDictionary(a => a.Name, a => a.Value)));
    }

    private static Task<JsonElement> Json(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        return Task.FromResult(document.RootElement.Clone());
    }

    private static string Turn(string userText) =>
        $$"""
        {
          "userMessage": {
            "id": "m-1", "conversationId": "{{ConversationId}}", "role": "user",
            "content": {{JsonSerializer.Serialize(userText)}}, "createdAt": "2026-09-30T10:00:00.000Z"
          },
          "assistantMessage": {
            "id": "m-2", "conversationId": "{{ConversationId}}", "role": "assistant",
            "content": "When does it crash?", "createdAt": "2026-09-30T10:00:02.000Z"
          },
          "reports": [{"id": "{{ReportId}}", "conversationId": "{{ConversationId}}", "status": "reporting_in_progress"}],
          "fieldFromALaterRevision": true
        }
        """;
}
