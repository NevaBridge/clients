namespace NevaBridge.Chat.Tests.Support;

public sealed record RecordedCall(string Operation, IReadOnlyDictionary<string, object?> Arguments);

/// <summary>
/// An in-memory NevaBridge that records every call and answers with configurable results, so bridge
/// tests can check exactly what reaches NevaBridge.
/// </summary>
public sealed class FakeConversations : INevaBridgeConversations
{
    public const string ConversationId = "conversation-00000000-0000-4000-8000-000000000002";
    public const string ReportId = "report-00000000-0000-4000-8000-000000000003";

    private readonly List<RecordedCall> _calls = [];

    public IReadOnlyList<RecordedCall> Calls => _calls;

    public IReadOnlyDictionary<string, bool> Features { get; set; } = new Dictionary<string, bool>();

    public Exception? FailWith { get; set; }

    public Task<ConversationTurn> StartConversationAsync(
        string productId,
        NevaBridgeReporter reporter,
        StartConversationRequest request,
        CancellationToken cancellationToken = default)
    {
        Record("start", ("productId", productId), ("reporter", reporter), ("request", request));
        return Task.FromResult(Turn(request.UserMessage.Content));
    }

    public Task<ConversationTurn> AppendMessageAsync(
        string conversationId,
        AppendMessageRequest request,
        CancellationToken cancellationToken = default)
    {
        Record("append", ("conversationId", conversationId), ("request", request));
        return Task.FromResult(Turn(request.Content));
    }

    public Task<ConversationDetail> GetConversationAsync(string conversationId, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("The bridge never reads a whole conversation.");

    public Task<SubmitReportResult> SubmitReportAsync(
        string conversationId,
        string reportId,
        CancellationToken cancellationToken = default)
    {
        Record("submit", ("conversationId", conversationId), ("reportId", reportId));
        return Task.FromResult(new SubmitReportResult(Report("submitted"))
        {
            Connectors = new Dictionary<string, ConnectorDeliveryResult>
            {
                ["github"] = new("success") { Ticket = "https://github.com/acme/app/issues/9" },
            },
        });
    }

    public Task<IReadOnlyDictionary<string, bool>> GetFeaturesAsync(CancellationToken cancellationToken = default)
    {
        Record("features");
        return Task.FromResult(Features);
    }

    public async Task<Attachment> AttachFileAsync(
        string conversationId,
        string uploaderId,
        PickedFile file,
        CancellationToken cancellationToken = default)
    {
        using Stream content = file.OpenRead();
        using MemoryStream copy = new();
        await content.CopyToAsync(copy, cancellationToken);
        Record("attach", ("conversationId", conversationId), ("uploaderId", uploaderId), ("fileName", file.Name), ("bytes", copy.ToArray()));
        return new Attachment("attachment-1", file.Name, "text", file.SizeBytes, "api:" + uploaderId, DateTimeOffset.Parse("2026-09-30T10:02:00Z"));
    }

    private void Record(string operation, params (string Name, object? Value)[] arguments)
    {
        if (FailWith is not null)
        {
            throw FailWith;
        }

        _calls.Add(new RecordedCall(operation, arguments.ToDictionary(a => a.Name, a => a.Value)));
    }

    private static ConversationTurn Turn(string userText) =>
        new(
            new Message("m-1", ConversationId, "user", userText, DateTimeOffset.Parse("2026-09-30T10:00:00Z")),
            new Message("m-2", ConversationId, "assistant", "When does it crash?", DateTimeOffset.Parse("2026-09-30T10:00:02Z")),
            [Report("reporting_in_progress")]);

    private static Report Report(string status) =>
        new(
            ReportId,
            ConversationId,
            "t",
            "1",
            status,
            new Dictionary<string, string>(),
            DateTimeOffset.Parse("2026-09-30T10:00:02Z"),
            DateTimeOffset.Parse("2026-09-30T10:00:02Z"));
}
