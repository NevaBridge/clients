namespace NevaBridge.Chat;

/// <summary>
/// The NevaBridge operations the Generic Intake chat needs. It is separate from
/// <see cref="INevaBridgeConversations"/>, so implementations of that interface keep compiling; a
/// client that implements both shares <see cref="GetConversationAsync"/>,
/// <see cref="GetFeaturesAsync"/> and <see cref="AttachFileAsync"/> between them.
/// </summary>
public interface INevaBridgeIntakeConversations
{
    /// <summary>Starts a conversation with its first message; the first turn runs in the background.</summary>
    Task<IntakeConversationStarted> StartIntakeConversationAsync(
        string productId,
        NevaBridgeReporter reporter,
        StartIntakeConversationRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Starts the next turn. Fails with a 409 <c>RevisionConflict</c> when the revision is stale, and
    /// with a 409 <c>IntakeTurnRunning</c>, naming the running turn in
    /// <see cref="NevaBridgeApiException.TurnId"/>, while another turn runs.
    /// </summary>
    Task<IntakeTurnStarted> StartIntakeTurnAsync(
        string conversationId,
        StartIntakeTurnRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads a turn's events after the cursor <paramref name="after"/>, or from its first event.
    /// Wait up to 20 seconds for a first new event.
    /// </summary>
    Task<IntakeTurnEvents> GetIntakeTurnEventsAsync(
        string turnId,
        string? after,
        CancellationToken cancellationToken = default);

    Task<ConversationDetail> GetConversationAsync(
        string conversationId,
        CancellationToken cancellationToken = default);

    /// <inheritdoc cref="INevaBridgeConversations.GetFeaturesAsync"/>
    Task<IReadOnlyDictionary<string, bool>> GetFeaturesAsync(CancellationToken cancellationToken = default);

    /// <inheritdoc cref="INevaBridgeConversations.AttachFileAsync"/>
    Task<Attachment> AttachFileAsync(
        string conversationId,
        string uploaderId,
        PickedFile file,
        CancellationToken cancellationToken = default);

    /// <summary>The files attached to a conversation, as NevaBridge holds them.</summary>
    Task<IReadOnlyList<Attachment>> ListAttachmentsAsync(
        string conversationId,
        CancellationToken cancellationToken = default);

    /// <summary>Removes one file from an open conversation. Removing a file that is gone succeeds.</summary>
    Task DeleteAttachmentAsync(
        string conversationId,
        string attachmentId,
        CancellationToken cancellationToken = default);
}
