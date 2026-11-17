namespace NevaBridge.Chat;

/// <summary>
/// Rating assistant replies in the Generic Intake chat, separate from
/// <see cref="INevaBridgeIntakeConversations"/> so existing implementations keep compiling. Pass an
/// implementation as <see cref="IntakeChatOptions.ReplyRatings"/> only when the tenant has
/// <c>replyRatings</c>.
/// </summary>
public interface INevaBridgeReplyRatings
{
    /// <summary>
    /// Rates an assistant reply as <paramref name="actorId"/>, sent as <c>X-Actor-Id</c>, replacing
    /// an earlier rating: <c>PUT /v1/conversations/{conversationId}/messages/{messageId}/rating</c>.
    /// Only the conversation's participant may rate; anyone else gets 403 <c>Forbidden</c>, as does a
    /// tenant without <c>replyRatings</c>.
    /// </summary>
    Task<ReplyRating> SetReplyRatingAsync(
        string conversationId,
        string messageId,
        string actorId,
        ReplyRatingRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes the rating of an assistant reply as <paramref name="actorId"/>:
    /// <c>DELETE /v1/conversations/{conversationId}/messages/{messageId}/rating</c>. Clearing a reply
    /// that has no rating succeeds.
    /// </summary>
    Task ClearReplyRatingAsync(
        string conversationId,
        string messageId,
        string actorId,
        CancellationToken cancellationToken = default);
}
