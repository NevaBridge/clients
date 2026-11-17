using NevaBridge.Chat.Tests.Support;
using static NevaBridge.Chat.Tests.Support.FakeIntakeConversations;

namespace NevaBridge.Chat.Tests;

public sealed class IntakeReplyRatingsTests
{
    private const string ReplyId = "message-1";

    private static readonly NevaBridgeReporter Reporter = new("jdoe", "Jane Doe", ["customer"]);

    /// <summary>
    /// A conversation whose first reply is saved; NevaBridge sends <paramref name="rating"/> on it, as
    /// it does for a tenant with replyRatings, and none when it is null.
    /// </summary>
    private static FakeIntakeConversations Started(MessageRating? rating) =>
        new FakeIntakeConversations()
            .Answer("startIntakeConversation", new IntakeConversationStarted(ConversationId, TurnId))
            .Answer("getIntakeTurnEvents", Events(Done(1)))
            .Answer("getConversation", RatedConversation(rating));

    private static ConversationDetail RatedConversation(MessageRating? rating) =>
        Conversation(1, [("user", "It crashes."), ("assistant", "When does it crash?")]) is var detail
            ? detail with
            {
                Messages = [detail.Messages[0], detail.Messages[1] with { Rating = rating }],
            }
            : throw new InvalidOperationException();

    private static IntakeChatViewModel Chat(FakeIntakeConversations nevaBridge, bool offerRatings = true)
    {
        IntakeChatViewModel chat = new(
            nevaBridge,
            "product-1",
            Reporter,
            new IntakeChatOptions
            {
                ReplyRatings = offerRatings ? nevaBridge : null,
                Delay = (_, _) => Task.CompletedTask,
            });
        chat.Draft = "It crashes.";
        chat.SendCommand.Execute(null);
        return chat;
    }

    private static ReplyRatingItem Reply(IntakeChatViewModel chat) =>
        Assert.IsType<ReplyRatingItem>(chat.Messages.Single(message => !message.IsFromReporter).Rating);

    private static IEnumerable<RecordedCall> RatingCalls(FakeIntakeConversations nevaBridge) =>
        nevaBridge.Calls.Where(call => call.Operation is "setReplyRating" or "clearReplyRating");

    [Fact]
    public void Each_assistant_reply_offers_thumbs_and_no_user_message_does()
    {
        // When
        IntakeChatViewModel chat = Chat(Started(new MessageRating(CanChange: false)));

        // Then
        Assert.Null(chat.Messages.Single(message => message.IsFromReporter).Rating);
        ReplyRatingItem reply = Reply(chat);
        Assert.False(reply.IsUp);
        Assert.False(reply.IsDown);
        Assert.Equal(IntakeChatText.GoodReply, reply.UpHint);
    }

    [Fact]
    public void A_reply_shows_the_rating_NevaBridge_holds()
    {
        // When
        IntakeChatViewModel chat = Chat(Started(new MessageRating(CanChange: true) { Value = "down", Comment = "Asked twice." }));

        // Then
        ReplyRatingItem reply = Reply(chat);
        Assert.True(reply.IsDown);
        Assert.Equal("Asked twice.", reply.Comment);
        Assert.True(reply.ShowsComment);
        Assert.Equal(IntakeChatText.ClearRatingHint, reply.DownHint);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void Without_the_application_offering_ratings_or_without_the_tenant_feature_no_reply_has_thumbs(
        bool offerRatings, bool tenantHasRatings)
    {
        // When
        IntakeChatViewModel chat = Chat(Started(tenantHasRatings ? new MessageRating(CanChange: false) : null), offerRatings);

        // Then
        Assert.All(chat.Messages, message => Assert.Null(message.Rating));
    }

    [Fact]
    public void Thumbs_up_rates_the_reply_as_the_reporter_and_pressing_it_again_removes_the_rating()
    {
        // Given
        FakeIntakeConversations nevaBridge = Started(new MessageRating(CanChange: false))
            .Answer("setReplyRating", Rated(ReplyId, "up"))
            .Answer("clearReplyRating", new object());
        IntakeChatViewModel chat = Chat(nevaBridge);
        ReplyRatingItem reply = Reply(chat);

        // When
        reply.RateUpCommand.Execute(null);

        // Then
        Assert.True(reply.IsUp);
        Assert.False(reply.IsPromptOpen);
        RecordedCall rate = Assert.Single(RatingCalls(nevaBridge));
        Assert.Equal(ConversationId, rate.Arguments["conversationId"]);
        Assert.Equal(ReplyId, rate.Arguments["messageId"]);
        Assert.Equal("jdoe", rate.Arguments["actorId"]);
        Assert.Equal(new ReplyRatingRequest("up"), rate.Arguments["request"]);

        // When
        reply.RateUpCommand.Execute(null);

        // Then
        Assert.False(reply.IsUp);
        RecordedCall clear = RatingCalls(nevaBridge).Last();
        Assert.Equal("clearReplyRating", clear.Operation);
        Assert.Equal("jdoe", clear.Arguments["actorId"]);
    }

    [Fact]
    public void Thumbs_down_rates_at_once_and_opens_the_comment_prompt_whose_Send_saves_the_comment()
    {
        // Given
        FakeIntakeConversations nevaBridge = Started(new MessageRating(CanChange: false))
            .Answer("setReplyRating", Rated(ReplyId, "down"))
            .Answer("setReplyRating", Rated(ReplyId, "down", "Asked twice."));
        IntakeChatViewModel chat = Chat(nevaBridge);
        ReplyRatingItem reply = Reply(chat);

        // When
        reply.RateDownCommand.Execute(null);

        // Then
        Assert.True(reply.IsDown);
        Assert.True(reply.IsPromptOpen);
        Assert.Equal(new ReplyRatingRequest("down"), Assert.Single(RatingCalls(nevaBridge)).Arguments["request"]);

        // When
        reply.CommentDraft = "  Asked twice.  ";
        reply.SendCommentCommand.Execute(null);

        // Then
        Assert.False(reply.IsPromptOpen);
        Assert.Equal("Asked twice.", reply.Comment);
        Assert.True(reply.ShowsComment);
        Assert.Equal(
            new ReplyRatingRequest("down") { Comment = "Asked twice." },
            RatingCalls(nevaBridge).Last().Arguments["request"]);
    }

    [Fact]
    public void Skip_and_an_empty_Send_keep_thumbs_down_without_a_comment_and_save_nothing_more()
    {
        // Given
        FakeIntakeConversations nevaBridge = Started(new MessageRating(CanChange: false))
            .Answer("setReplyRating", Rated(ReplyId, "down"))
            .Answer("setReplyRating", Rated(ReplyId, "down"));
        IntakeChatViewModel chat = Chat(nevaBridge);
        ReplyRatingItem reply = Reply(chat);
        reply.RateDownCommand.Execute(null);

        // When
        reply.SkipCommentCommand.Execute(null);

        // Then
        Assert.True(reply.IsDown);
        Assert.False(reply.IsPromptOpen);
        Assert.False(reply.HasComment);

        // When
        reply.EditCommentCommand.Execute(null);
        reply.CommentDraft = "   ";
        reply.SendCommentCommand.Execute(null);

        // Then
        Assert.True(reply.IsDown);
        Assert.False(reply.HasComment);
        Assert.Single(RatingCalls(nevaBridge));
    }

    [Fact]
    public void Edit_comment_opens_the_prompt_with_the_sent_comment_and_Send_replaces_it()
    {
        // Given
        FakeIntakeConversations nevaBridge = Started(new MessageRating(CanChange: true) { Value = "down", Comment = "Asked twice." })
            .Answer("setReplyRating", Rated(ReplyId, "down", "Asked three times."));
        IntakeChatViewModel chat = Chat(nevaBridge);
        ReplyRatingItem reply = Reply(chat);

        // When
        reply.EditCommentCommand.Execute(null);

        // Then
        Assert.True(reply.IsPromptOpen);
        Assert.False(reply.ShowsComment);
        Assert.Equal("Asked twice.", reply.CommentDraft);

        // When
        reply.CommentDraft = "Asked three times.";
        reply.SendCommentCommand.Execute(null);

        // Then
        Assert.Equal("Asked three times.", reply.Comment);
        Assert.Equal(
            new ReplyRatingRequest("down") { Comment = "Asked three times." },
            Assert.Single(RatingCalls(nevaBridge)).Arguments["request"]);
    }

    [Fact]
    public void Switching_from_thumbs_down_to_up_drops_the_comment()
    {
        // Given
        FakeIntakeConversations nevaBridge = Started(new MessageRating(CanChange: true) { Value = "down", Comment = "Asked twice." })
            .Answer("setReplyRating", Rated(ReplyId, "up"));
        IntakeChatViewModel chat = Chat(nevaBridge);
        ReplyRatingItem reply = Reply(chat);

        // When
        reply.RateUpCommand.Execute(null);

        // Then
        Assert.True(reply.IsUp);
        Assert.False(reply.IsDown);
        Assert.False(reply.HasComment);
        Assert.Equal(new ReplyRatingRequest("up"), Assert.Single(RatingCalls(nevaBridge)).Arguments["request"]);
    }

    [Fact]
    public void A_rating_that_was_not_saved_shows_the_earlier_rating_again_with_the_reason()
    {
        // Given
        FakeIntakeConversations nevaBridge = Started(new MessageRating(CanChange: true) { Value = "up" })
            .Answer("setReplyRating", ApiError(400, "MessageNotRatable"));
        IntakeChatViewModel chat = Chat(nevaBridge);
        ReplyRatingItem reply = Reply(chat);

        // When
        reply.RateDownCommand.Execute(null);

        // Then
        Assert.True(reply.IsUp);
        Assert.False(reply.IsDown);
        Assert.False(reply.IsPromptOpen);
        Assert.StartsWith("Your rating was not saved. ", chat.RatingNotice);
        Assert.Contains("MessageNotRatable", chat.RatingNotice);

        // When
        chat.DismissRatingNoticeCommand.Execute(null);

        // Then
        Assert.Null(chat.RatingNotice);
    }

    [Fact]
    public void A_first_comment_that_was_not_saved_stays_in_the_open_prompt_and_can_be_sent_again()
    {
        // Given
        FakeIntakeConversations nevaBridge = Started(new MessageRating(CanChange: false))
            .Answer("setReplyRating", Rated(ReplyId, "down"))
            .Answer("setReplyRating", ApiError(409, "ReplyRatingConflict"))
            .Answer("setReplyRating", Rated(ReplyId, "down", "Asked twice."));
        IntakeChatViewModel chat = Chat(nevaBridge);
        ReplyRatingItem reply = Reply(chat);
        reply.RateDownCommand.Execute(null);
        reply.TakeCommentFocus();
        reply.CommentDraft = "Asked twice.";

        // When
        reply.SendCommentCommand.Execute(null);

        // Then
        Assert.True(reply.IsDown);
        Assert.False(reply.HasComment);
        Assert.True(reply.IsPromptOpen);
        Assert.Equal("Asked twice.", reply.CommentDraft);
        Assert.False(reply.TakeCommentFocus());
        Assert.NotNull(chat.RatingNotice);

        // When
        reply.SendCommentCommand.Execute(null);

        // Then
        Assert.Equal("Asked twice.", reply.Comment);
        Assert.False(reply.IsPromptOpen);
    }

    [Fact]
    public void An_edited_comment_that_was_not_saved_stays_in_the_open_prompt_while_the_sent_one_is_kept()
    {
        // Given
        FakeIntakeConversations nevaBridge = Started(new MessageRating(CanChange: true) { Value = "down", Comment = "Asked twice." })
            .Answer("setReplyRating", EdgeFailure());
        IntakeChatViewModel chat = Chat(nevaBridge);
        ReplyRatingItem reply = Reply(chat);
        reply.EditCommentCommand.Execute(null);
        reply.CommentDraft = "Asked three times.";

        // When
        reply.SendCommentCommand.Execute(null);

        // Then
        Assert.Equal("Asked twice.", reply.Comment);
        Assert.True(reply.IsPromptOpen);
        Assert.Equal("Asked three times.", reply.CommentDraft);
    }

    [Fact]
    public void Focus_moves_only_when_the_user_opens_or_closes_the_prompt_not_when_the_transcript_is_redrawn()
    {
        // Given
        FakeIntakeConversations nevaBridge = Started(new MessageRating(CanChange: false))
            .Answer("setReplyRating", Rated(ReplyId, "down"));
        IntakeChatViewModel chat = Chat(nevaBridge);
        ReplyRatingItem reply = Reply(chat);

        // When
        reply.RateDownCommand.Execute(null);

        // Then
        Assert.True(reply.TakeCommentFocus());
        Assert.False(reply.TakeCommentFocus());
        Assert.False(reply.TakeThumbFocus());

        // When
        reply.SkipCommentCommand.Execute(null);

        // Then
        Assert.True(reply.TakeThumbFocus());
        Assert.False(reply.TakeThumbFocus());
        Assert.False(reply.TakeCommentFocus());

        // When
        reply.EditCommentCommand.Execute(null);

        // Then
        Assert.True(reply.TakeCommentFocus());
    }

    [Fact]
    public void Ratings_switched_off_while_the_chat_is_open_disappear_with_a_notice()
    {
        // Given
        FakeIntakeConversations nevaBridge = Started(new MessageRating(CanChange: false))
            .Answer("setReplyRating", ApiError(403, "Forbidden"));
        IntakeChatViewModel chat = Chat(nevaBridge);

        // When
        Reply(chat).RateUpCommand.Execute(null);

        // Then
        Assert.Equal(IntakeChatText.RatingsOff, chat.RatingNotice);
        Assert.All(chat.Messages, message => Assert.Null(message.Rating));
    }

    [Fact]
    public async Task A_second_press_waits_until_the_first_rating_is_saved()
    {
        // Given
        TaskCompletionSource<ReplyRating> saving = new();
        FakeIntakeConversations nevaBridge = Started(new MessageRating(CanChange: false))
            .Answer("setReplyRating", saving.Task);
        IntakeChatViewModel chat = Chat(nevaBridge);
        ReplyRatingItem reply = Reply(chat);

        // When
        Task first = reply.RateUpCommand.ExecuteAsync();

        // Then
        Assert.True(reply.IsUp);
        Assert.False(reply.RateDownCommand.CanExecute(null));
        Assert.False(reply.RateUpCommand.CanExecute(null));

        // When
        saving.SetResult(Rated(ReplyId, "up"));
        await first;

        // Then
        Assert.True(reply.RateDownCommand.CanExecute(null));
        Assert.Single(RatingCalls(nevaBridge));
    }

    [Fact]
    public void An_open_comment_prompt_survives_the_next_reply()
    {
        // Given
        FakeIntakeConversations nevaBridge = Started(new MessageRating(CanChange: false))
            .Answer("setReplyRating", Rated(ReplyId, "down"))
            .Answer("startIntakeTurn", new IntakeTurnStarted(NextTurnId))
            .Answer("getIntakeTurnEvents", Events(Done(2)))
            .Answer("getConversation", Conversation(2, [("user", "It crashes."), ("assistant", "When does it crash?"), ("user", "On save."), ("assistant", "Thanks.")]) is var next
                ? next with
                {
                    Messages = next.Messages.Select(message => message.Role == "assistant"
                        ? message with { Rating = new MessageRating(CanChange: false) { Value = message.Id == ReplyId ? "down" : null } }
                        : message).ToList(),
                }
                : throw new InvalidOperationException());
        IntakeChatViewModel chat = Chat(nevaBridge);
        ReplyRatingItem reply = Reply(chat);
        reply.RateDownCommand.Execute(null);
        reply.TakeCommentFocus();
        reply.CommentDraft = "Half written";

        // When
        chat.Draft = "On save.";
        chat.SendCommand.Execute(null);

        // Then
        ReplyRatingItem same = Assert.IsType<ReplyRatingItem>(chat.Messages.First(message => !message.IsFromReporter).Rating);
        Assert.Same(reply, same);
        Assert.True(same.IsPromptOpen);
        Assert.Equal("Half written", same.CommentDraft);
        Assert.False(same.TakeCommentFocus());
        Assert.Equal(2, chat.Messages.Count(message => message.Rating is not null));
    }
}
