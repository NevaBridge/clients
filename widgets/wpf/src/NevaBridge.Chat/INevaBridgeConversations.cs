namespace NevaBridge.Chat;

/// <summary>
/// The NevaBridge operations both chat variants need. Implement it with a client that calls
/// NevaBridge directly with the API key, or with one that calls your own backend instead; the
/// chat controls do not change.
/// </summary>
public interface INevaBridgeConversations
{
    Task<ConversationTurn> StartConversationAsync(
        string productId,
        NevaBridgeReporter reporter,
        StartConversationRequest request,
        CancellationToken cancellationToken = default);

    Task<ConversationTurn> AppendMessageAsync(
        string conversationId,
        AppendMessageRequest request,
        CancellationToken cancellationToken = default);

    Task<ConversationDetail> GetConversationAsync(
        string conversationId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Finalizes a report and delivers it to the product's connectors. Call it when the user says
    /// they are done. Do not add an inactivity timer: NevaBridge finalizes an abandoned
    /// conversation itself 60 to 70 minutes after its last turn.
    /// </summary>
    Task<SubmitReportResult> SubmitReportAsync(
        string conversationId,
        string reportId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// The tenant's effective feature entitlements, such as <c>chatAttachments</c>. Features
    /// NevaBridge does not report are off.
    /// </summary>
    Task<IReadOnlyDictionary<string, bool>> GetFeaturesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Attaches one file to a conversation: requests an upload address, uploads the bytes there, and
    /// confirms the upload. Requires the <c>chatAttachments</c> feature.
    /// </summary>
    Task<Attachment> AttachFileAsync(
        string conversationId,
        string uploaderId,
        PickedFile file,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Who is reporting. <paramref name="Id"/> becomes the conversation's reporterId and must be
/// ASCII. <paramref name="Roles"/> (anonymous, customer, tenant) decide which knowledge-base
/// documents the assistant may draw on.
/// </summary>
public sealed record NevaBridgeReporter(
    string Id,
    string? DisplayName = null,
    IReadOnlyList<string>? Roles = null);

/// <summary>A file the user chose to attach, read only when it is uploaded.</summary>
public sealed record PickedFile(string Name, long SizeBytes, Func<Stream> OpenRead);
