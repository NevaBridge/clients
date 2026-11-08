namespace NevaBridge.Chat.Tests.Support;

/// <summary>
/// An in-memory NevaBridge for the Generic Intake chat. Each operation answers with the next
/// scripted answer for it, either a result or an exception, and every call is recorded.
/// </summary>
public sealed class FakeIntakeConversations : INevaBridgeIntakeConversations
{
    public const string ConversationId = "conversation-00000000-0000-4000-8000-000000000002";
    public const string TurnId = "intaketurn-00000000-0000-4000-8000-000000000004";
    public const string NextTurnId = "intaketurn-00000000-0000-4000-8000-000000000005";

    private readonly Dictionary<string, Queue<object>> _answers = [];
    private readonly List<RecordedCall> _calls = [];

    public IReadOnlyList<RecordedCall> Calls => _calls;

    public IEnumerable<string> Operations => _calls.Select(call => call.Operation);

    /// <summary>
    /// Queues the next answer of <paramref name="operation"/>: a result, an exception to throw, or a
    /// task the test completes later.
    /// </summary>
    public FakeIntakeConversations Answer(string operation, object answer)
    {
        if (!_answers.TryGetValue(operation, out Queue<object>? queue))
        {
            queue = new Queue<object>();
            _answers[operation] = queue;
        }

        queue.Enqueue(answer);
        return this;
    }

    public static IntakeTurnEvents Events(params IntakeTurnEvent[] events) =>
        new(events, Cursor: $"cursor-{Guid.NewGuid():N}", Finished: events.Any(e => e.Type is "done" or "error"));

    public static IntakeTurnEvent Status(string phase, string? tool = null) => new("status") { Phase = phase, Tool = tool };

    public static IntakeTurnEvent Text(string delta) => new("text") { Delta = delta };

    public static IntakeTurnEvent Done(int revision) => new("done") { Revision = revision };

    public static IntakeTurnEvent Error(string name, string message, int statusCode) =>
        new("error") { Error = new IntakeTurnFailure(name, message, statusCode) };

    public static ConversationDetail Conversation(
        int revision,
        IEnumerable<(string Role, string Content)> messages,
        string status = "in_progress",
        string? activeTurnId = null,
        IReadOnlyList<Report>? reports = null) =>
        new(
            ConversationId,
            "product-1",
            "jdoe",
            status,
            DateTimeOffset.UnixEpoch,
            DateTimeOffset.UnixEpoch,
            messages.Select((message, index) => new Message(
                $"message-{index}",
                ConversationId,
                message.Role,
                message.Content,
                DateTimeOffset.UnixEpoch)).ToList(),
            reports ?? [])
        {
            Revision = revision,
            ActiveTurnId = activeTurnId,
        };

    public static Report Filed(string category, string status, string title, string? reason = null) =>
        new(
            $"report-{Guid.NewGuid():N}",
            ConversationId,
            "system:template",
            "1",
            status,
            new Dictionary<string, string>(),
            DateTimeOffset.UnixEpoch,
            DateTimeOffset.UnixEpoch)
        {
            Category = category,
            DisplayTitle = title,
            StatusReason = reason,
        };

    public static NevaBridgeApiException ApiError(int status, string name, string? turnId = null) =>
        new(status, name, null, name) { TurnId = turnId };

    /// <summary>A conversation started without a message, holding one file.</summary>
    /// <summary>An admission for a file of a conversation that already exists.</summary>
    public static AttachmentUpload Admission(string attachmentId = "attachment-1") =>
        new(attachmentId, "crash.log", new Uri("https://uploads.example.com/" + attachmentId), DateTimeOffset.MaxValue);

    /// <remarks>The upload address has expired unless <paramref name="live"/> is set.</remarks>
    public static HeldIntakeConversation Held(string fileName, string attachmentId = "attachment-1", bool live = false) =>
        new(ConversationId, [new AttachmentUpload(
            attachmentId,
            fileName,
            new Uri("https://uploads.example.com/" + attachmentId),
            live ? DateTimeOffset.MaxValue : DateTimeOffset.UnixEpoch)]);

    public static NevaBridgeApiException EdgeFailure() => new(504, null, null, "Endpoint request timed out");

    public Task<IntakeConversationStarted> StartIntakeConversationAsync(
        string productId,
        NevaBridgeReporter reporter,
        StartIntakeConversationRequest request,
        CancellationToken cancellationToken = default) =>
        Next<IntakeConversationStarted>("startIntakeConversation", ("productId", productId), ("reporter", reporter), ("request", request));

    public Task<HeldIntakeConversation> StartHeldIntakeConversationAsync(
        string productId,
        NevaBridgeReporter reporter,
        StartHeldIntakeConversationRequest request,
        CancellationToken cancellationToken = default) =>
        Next<HeldIntakeConversation>("startHeldIntakeConversation", ("productId", productId), ("reporter", reporter), ("request", request));

    public Task<AttachmentUpload> RequestAttachmentUploadAsync(
        string conversationId,
        string uploaderId,
        AttachmentUploadRequest file,
        CancellationToken cancellationToken = default) =>
        Next<AttachmentUpload>("requestAttachmentUpload", ("conversationId", conversationId), ("uploaderId", uploaderId), ("file", file));

    public Task<Attachment> UploadAttachmentAsync(
        string conversationId,
        AttachmentUpload upload,
        PickedFile file,
        CancellationToken cancellationToken = default) =>
        Next<Attachment>("uploadAttachment", ("conversationId", conversationId), ("upload", upload), ("file", file));

    public Task<IntakeTurnStarted> StartIntakeTurnAsync(
        string conversationId,
        StartIntakeTurnRequest request,
        CancellationToken cancellationToken = default) =>
        Next<IntakeTurnStarted>("startIntakeTurn", ("conversationId", conversationId), ("request", request));

    public Task<IntakeTurnEvents> GetIntakeTurnEventsAsync(
        string turnId,
        string? after,
        CancellationToken cancellationToken = default) =>
        Next<IntakeTurnEvents>("getIntakeTurnEvents", ("turnId", turnId), ("after", after));

    public Task<ConversationDetail> GetConversationAsync(
        string conversationId,
        CancellationToken cancellationToken = default) =>
        Next<ConversationDetail>("getConversation", ("conversationId", conversationId));

    public Task<IReadOnlyDictionary<string, bool>> GetFeaturesAsync(CancellationToken cancellationToken = default) =>
        Next<IReadOnlyDictionary<string, bool>>("getFeatures");

    public Task<Attachment> AttachFileAsync(
        string conversationId,
        string uploaderId,
        PickedFile file,
        CancellationToken cancellationToken = default) =>
        Next<Attachment>("attachFile", ("conversationId", conversationId), ("uploaderId", uploaderId), ("file", file));

    public Task<IReadOnlyList<Attachment>> ListAttachmentsAsync(
        string conversationId,
        CancellationToken cancellationToken = default) =>
        Next<IReadOnlyList<Attachment>>("listAttachments", ("conversationId", conversationId));

    public async Task DeleteAttachmentAsync(
        string conversationId,
        string attachmentId,
        CancellationToken cancellationToken = default) =>
        await Next<object>("deleteAttachment", ("conversationId", conversationId), ("attachmentId", attachmentId));

    private Task<T> Next<T>(string operation, params (string Name, object? Value)[] arguments)
    {
        _calls.Add(new RecordedCall(operation, arguments.ToDictionary(a => a.Name, a => a.Value)));
        if (!_answers.TryGetValue(operation, out Queue<object>? queue) || queue.Count == 0)
        {
            throw new InvalidOperationException($"The test scripted no answer for {operation}.");
        }

        return queue.Dequeue() switch
        {
            Exception error => Task.FromException<T>(error),
            Task<T> pending => pending,
            T result => Task.FromResult(result),
            object other => throw new InvalidOperationException($"{operation} answered {other.GetType().Name}, not {typeof(T).Name}."),
        };
    }
}
