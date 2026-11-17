using System.Text.Json;

namespace NevaBridge.Chat.WebChat;

/// <summary>
/// The NevaBridge operations the web chat page reaches through <see cref="WebChatBridge"/>. Each
/// returns the API's JSON response body for the operation, unchanged, because the bridge passes it
/// to the page as it is. Implement it with a client that calls NevaBridge directly with the API
/// key, or with one that calls your own backend instead.
/// </summary>
/// <remarks>
/// Throw <see cref="NevaBridgeApiException"/> when NevaBridge or its gateway answers with an error
/// status, with <see cref="NevaBridgeApiException.TurnId"/> set for a 409 IntakeTurnRunning;
/// <see cref="NevaBridgeUploadException"/> when the upload of an attachment fails; and
/// <see cref="HttpRequestException"/> when no answer arrives.
/// </remarks>
public interface INevaBridgeWebChatApi
{
    Task<JsonElement> StartConversationAsync(
        string productId,
        NevaBridgeReporter reporter,
        StartConversationRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends the next message as <paramref name="actorId"/>, who goes into <c>X-Actor-Id</c>. With
    /// <c>replyRatings</c>, NevaBridge marks a reply as ratable only for the conversation's participant.
    /// </summary>
    Task<JsonElement> AppendMessageAsync(
        string conversationId,
        string actorId,
        AppendMessageRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Finalizes a report. The page calls it when the user says they are done; NevaBridge
    /// finalizes an abandoned conversation itself.
    /// </summary>
    Task<JsonElement> SubmitReportAsync(
        string conversationId,
        string reportId,
        CancellationToken cancellationToken = default);

    /// <summary>The tenant's effective features, such as <c>chatAttachments</c>.</summary>
    Task<JsonElement> GetFeaturesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Attaches one file: requests an upload address, uploads the bytes there, and confirms the
    /// upload. Returns the confirmed attachment.
    /// </summary>
    Task<JsonElement> AttachFileAsync(
        string conversationId,
        string uploaderId,
        PickedFile file,
        CancellationToken cancellationToken = default);

    /// <summary>Starts a Generic Intake conversation; its first turn runs in the background.</summary>
    Task<JsonElement> StartIntakeConversationAsync(
        string productId,
        NevaBridgeReporter reporter,
        StartIntakeConversationRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Starts the next turn of a Generic Intake conversation.</summary>
    Task<JsonElement> StartIntakeTurnAsync(
        string conversationId,
        StartIntakeTurnRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads a turn's events after the cursor <paramref name="after"/>, or from its first event.
    /// Wait up to 20 seconds for a first new event, which keeps a read well inside the page's limit.
    /// </summary>
    Task<JsonElement> GetIntakeTurnEventsAsync(
        string turnId,
        string? after,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes one file from an open conversation. NevaBridge answers 204 without a body, also for
    /// a file that is already gone.
    /// </summary>
    Task DeleteAttachmentAsync(
        string conversationId,
        string attachmentId,
        CancellationToken cancellationToken = default);

    /// <summary>Reads a conversation as <paramref name="actorId"/>; see <see cref="AppendMessageAsync"/>.</summary>
    Task<JsonElement> GetConversationAsync(
        string conversationId,
        string actorId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Rates an assistant reply as <paramref name="actorId"/>, who must be the conversation's
    /// participant, and returns the stored rating. Requires <c>replyRatings</c>.
    /// </summary>
    Task<JsonElement> SetReplyRatingAsync(
        string conversationId,
        string messageId,
        string actorId,
        ReplyRatingRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes the rating of an assistant reply as <paramref name="actorId"/>. NevaBridge answers 204
    /// without a body, also for a reply that has no rating.
    /// </summary>
    Task ClearReplyRatingAsync(
        string conversationId,
        string messageId,
        string actorId,
        CancellationToken cancellationToken = default);
}
