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
    /// Starts a conversation without a message, holding the given files until the first message,
    /// which is a turn at revision 0. Upload each file with <see cref="UploadAttachmentAsync"/>.
    /// Requires <c>chatAttachments</c>. When NevaBridge stored the conversation but could not sign the
    /// uploads, it answers 424 and <see cref="NevaBridgeApiException.ConversationId"/> names it.
    /// </summary>
    Task<HeldIntakeConversation> StartHeldIntakeConversationAsync(
        string productId,
        NevaBridgeReporter reporter,
        StartHeldIntakeConversationRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Uploads a file to an address NevaBridge already issued and confirms it. Calling it again for
    /// the same address is safe: storage keeps one copy and confirming twice returns the same file.
    /// When the upload fails, it confirms anyway, since the bytes may have arrived, and fails with
    /// <see cref="NevaBridgeUploadException"/> only when NevaBridge has none
    /// (<c>AttachmentNotUploaded</c>).
    /// </summary>
    Task<Attachment> UploadAttachmentAsync(
        string conversationId,
        AttachmentUpload upload,
        PickedFile file,
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
