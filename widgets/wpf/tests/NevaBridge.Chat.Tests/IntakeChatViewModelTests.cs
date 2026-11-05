using System.Net.Http;
using NevaBridge.Chat.Tests.Support;
using static NevaBridge.Chat.Tests.Support.FakeIntakeConversations;

namespace NevaBridge.Chat.Tests;

public sealed class IntakeChatViewModelTests
{
    private static readonly NevaBridgeReporter Reporter = new("jdoe", "Jane Doe", ["customer"]);

    private static IntakeChatViewModel Chat(FakeIntakeConversations nevaBridge, IntakeChatOptions? options = null) =>
        new(nevaBridge, "product-1", Reporter, (options ?? new IntakeChatOptions()) with { Delay = (_, _) => Task.CompletedTask });

    private static void Send(IntakeChatViewModel chat, string text)
    {
        chat.Draft = text;
        chat.SendCommand.Execute(null);
    }

    private static readonly IReadOnlyList<Attachment> NoFiles = [];

    /// <summary>A conversation whose first turn was saved at revision 1.</summary>
    private static FakeIntakeConversations Started(string firstMessage = "It crashes.") =>
        new FakeIntakeConversations()
            .Answer("startIntakeConversation", new IntakeConversationStarted(ConversationId, TurnId))
            .Answer("getIntakeTurnEvents", Events(Done(1)))
            .Answer("getConversation", Conversation(1, [("user", firstMessage), ("assistant", "When does it crash?")]));

    [Fact]
    public void The_first_message_starts_a_conversation_and_shows_the_reply_as_it_is_written_then_as_saved()
    {
        // Given
        FakeIntakeConversations nevaBridge = new FakeIntakeConversations()
            .Answer("startIntakeConversation", new IntakeConversationStarted(ConversationId, TurnId))
            .Answer("getIntakeTurnEvents", Events(Status("planning"), Status("tool", "searchKnowledge")))
            .Answer("getIntakeTurnEvents", Events(Text("When does "), Text("it crash?")))
            .Answer("getIntakeTurnEvents", Events(Done(1)))
            .Answer("getConversation", Conversation(1, [("user", "It crashes."), ("assistant", "When does it crash, exactly?")]));
        IntakeChatViewModel chat = Chat(nevaBridge);
        List<string?> progress = [];
        List<string?> replies = [];
        chat.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(IntakeChatViewModel.ProgressText)) progress.Add(chat.ProgressText);
            if (e.PropertyName == nameof(IntakeChatViewModel.LiveReply)) replies.Add(chat.LiveReply);
        };

        // When
        Send(chat, "It crashes.");

        // Then
        Assert.Contains("Working out the next step", progress);
        Assert.Contains("Searching the knowledge base", progress);
        Assert.Contains("When does it crash?", replies);
        Assert.Equal(
            [new ChatMessageItem("It crashes.", true), new ChatMessageItem("When does it crash, exactly?", false)],
            chat.Messages);
        Assert.Null(chat.LiveReply);
        Assert.False(chat.IsBusy);
        Assert.Equal(string.Empty, chat.Draft);
        Assert.Equal(
            [null, "cursor"],
            nevaBridge.Calls.Where(c => c.Operation == "getIntakeTurnEvents").Take(2)
                .Select(c => c.Arguments["after"] is null ? null : "cursor"));
    }

    [Fact]
    public void The_first_message_carries_the_application_context_and_the_reporter_contact()
    {
        // Given
        FakeIntakeConversations nevaBridge = Started();
        ReporterContact contact = new() { Email = "jane@example.com" };
        Dictionary<string, string> context = new() { ["screen"] = "Invoices" };
        IntakeChatViewModel chat = Chat(nevaBridge, new IntakeChatOptions { ReporterContact = contact, ApplicationContext = context });

        // When
        Send(chat, "It crashes.");

        // Then
        RecordedCall start = nevaBridge.Calls[0];
        Assert.Equal("product-1", start.Arguments["productId"]);
        Assert.Equal(Reporter, start.Arguments["reporter"]);
        StartIntakeConversationRequest request = Assert.IsType<StartIntakeConversationRequest>(start.Arguments["request"]);
        Assert.Equal("It crashes.", request.UserMessage.Content);
        Assert.Equal(contact, request.ReporterContact);
        Assert.Equal(context, request.ApplicationContext);
    }

    [Fact]
    public void Without_contact_details_the_first_message_carries_none()
    {
        // Given
        FakeIntakeConversations nevaBridge = Started();
        IntakeChatViewModel chat = Chat(nevaBridge);

        // When
        Send(chat, "It crashes.");

        // Then
        StartIntakeConversationRequest request = Assert.IsType<StartIntakeConversationRequest>(nevaBridge.Calls[0].Arguments["request"]);
        Assert.Null(request.ReporterContact);
    }

    [Fact]
    public void A_later_message_sends_the_conversations_revision()
    {
        // Given
        FakeIntakeConversations nevaBridge = Started()
            .Answer("startIntakeTurn", new IntakeTurnStarted(NextTurnId))
            .Answer("getIntakeTurnEvents", Events(Done(2)))
            .Answer("getConversation", Conversation(2, [("user", "It crashes."), ("assistant", "When?"), ("user", "On save."), ("assistant", "Thanks.")]));
        IntakeChatViewModel chat = Chat(nevaBridge);
        Send(chat, "It crashes.");

        // When
        Send(chat, "On save.");

        // Then
        RecordedCall turn = nevaBridge.Calls.Single(c => c.Operation == "startIntakeTurn");
        Assert.Equal(ConversationId, turn.Arguments["conversationId"]);
        Assert.Equal(new StartIntakeTurnRequest("On save.", 1), turn.Arguments["request"]);
        Assert.Equal(NextTurnId, nevaBridge.Calls.Last(c => c.Operation == "getIntakeTurnEvents").Arguments["turnId"]);
        Assert.Equal(4, chat.Messages.Count);
        Assert.Equal(string.Empty, chat.Draft);
    }

    [Fact]
    public void A_message_refused_as_stale_stays_out_of_the_composer_when_it_was_saved_and_answered()
    {
        // Given: an earlier send of the same message was saved and answered, but its answer was lost
        FakeIntakeConversations nevaBridge = Started()
            .Answer("startIntakeTurn", ApiError(409, "RevisionConflict"))
            .Answer("getConversation", Conversation(2, [("user", "It crashes."), ("assistant", "When?"), ("user", "On save."), ("assistant", "Thanks.")]));
        IntakeChatViewModel chat = Chat(nevaBridge);
        Send(chat, "It crashes.");

        // When
        Send(chat, "On save.");

        // Then
        Assert.Equal(string.Empty, chat.Draft);
        Assert.Null(chat.ErrorText);
        Assert.Equal(new ChatMessageItem("Thanks.", false), chat.Messages[^1]);
    }

    [Fact]
    public void A_message_refused_as_stale_returns_to_the_composer_when_only_an_earlier_message_has_its_text()
    {
        // Given: the reporter sends the same text twice, and the conversation changed in between
        FakeIntakeConversations nevaBridge = Started("Yes.")
            .Answer("startIntakeTurn", ApiError(409, "RevisionConflict"))
            .Answer("getConversation", Conversation(2, [("user", "Yes."), ("assistant", "When?"), ("assistant", "Anything else?")]));
        IntakeChatViewModel chat = Chat(nevaBridge);
        Send(chat, "Yes.");

        // When
        Send(chat, "Yes.");

        // Then
        Assert.Equal("Yes.", chat.Draft);
        Assert.Equal(IntakeChatText.TurnConflict, chat.ErrorText);
    }

    [Fact]
    public void A_message_refused_while_another_turn_runs_follows_that_turn()
    {
        // Given
        FakeIntakeConversations nevaBridge = Started()
            .Answer("startIntakeTurn", ApiError(409, "IntakeTurnRunning", NextTurnId))
            .Answer("getIntakeTurnEvents", Events(Done(2)))
            .Answer("getConversation", Conversation(2, [("user", "It crashes."), ("assistant", "When?"), ("user", "On save."), ("assistant", "Thanks.")]));
        IntakeChatViewModel chat = Chat(nevaBridge);
        Send(chat, "It crashes.");

        // When
        Send(chat, "On save.");

        // Then
        Assert.Equal(NextTurnId, nevaBridge.Calls.Last(c => c.Operation == "getIntakeTurnEvents").Arguments["turnId"]);
        Assert.Equal(string.Empty, chat.Draft);
        Assert.Equal(4, chat.Messages.Count);
    }

    [Fact]
    public void A_turn_that_fails_puts_the_message_back_with_the_reason()
    {
        // Given
        FakeIntakeConversations nevaBridge = Started()
            .Answer("startIntakeTurn", new IntakeTurnStarted(NextTurnId))
            .Answer("getIntakeTurnEvents", Events(Error("QuotaExceeded", "The organization has no credits left.", 402)));
        IntakeChatViewModel chat = Chat(nevaBridge);
        Send(chat, "It crashes.");

        // When
        Send(chat, "On save.");

        // Then
        Assert.Equal("On save.", chat.Draft);
        Assert.Equal("The organization has no credits left.", chat.ErrorText);
        Assert.Equal(2, chat.Messages.Count);
        Assert.True(chat.SendCommand.CanExecute(null));
    }

    [Fact]
    public void A_first_turn_that_fails_leaves_no_conversation_so_the_next_message_starts_a_new_one()
    {
        // Given
        FakeIntakeConversations nevaBridge = new FakeIntakeConversations()
            .Answer("startIntakeConversation", new IntakeConversationStarted(ConversationId, TurnId))
            .Answer("getIntakeTurnEvents", Events(Error("IntakeTurnTimedOut", "The reply took too long.", 408)))
            .Answer("startIntakeConversation", new IntakeConversationStarted(ConversationId, NextTurnId))
            .Answer("getIntakeTurnEvents", Events(Done(1)))
            .Answer("getConversation", Conversation(1, [("user", "It crashes."), ("assistant", "When?")]));
        IntakeChatViewModel chat = Chat(nevaBridge);
        Send(chat, "It crashes.");
        Assert.Equal("It crashes.", chat.Draft);
        Assert.False(chat.CanFinish);

        // When
        chat.SendCommand.Execute(null);

        // Then
        Assert.Equal(2, nevaBridge.Operations.Count(o => o == "startIntakeConversation"));
        Assert.DoesNotContain("startIntakeTurn", nevaBridge.Operations);
        Assert.Equal(2, chat.Messages.Count);
    }

    [Fact]
    public void A_first_message_whose_start_fails_returns_to_the_composer()
    {
        // Given
        FakeIntakeConversations nevaBridge = new FakeIntakeConversations()
            .Answer("startIntakeConversation", new HttpRequestException("No route to host"));
        IntakeChatViewModel chat = Chat(nevaBridge);

        // When
        Send(chat, "It crashes.");

        // Then
        Assert.Equal("It crashes.", chat.Draft);
        Assert.NotNull(chat.ErrorText);
        Assert.Empty(chat.Messages);
        Assert.False(chat.CanAttach);
    }

    [Fact]
    public void A_poll_that_fails_is_retried_three_times_with_the_same_cursor()
    {
        // Given
        IntakeTurnEvents first = Events(Status("planning"));
        FakeIntakeConversations nevaBridge = new FakeIntakeConversations()
            .Answer("startIntakeConversation", new IntakeConversationStarted(ConversationId, TurnId))
            .Answer("getIntakeTurnEvents", first)
            .Answer("getIntakeTurnEvents", EdgeFailure())
            .Answer("getIntakeTurnEvents", new HttpRequestException("Connection reset"))
            .Answer("getIntakeTurnEvents", EdgeFailure())
            .Answer("getIntakeTurnEvents", Events(Done(1)))
            .Answer("getConversation", Conversation(1, [("user", "It crashes."), ("assistant", "When?")]));
        int pauses = 0;
        IntakeChatViewModel chat = new(nevaBridge, "product-1", Reporter, new IntakeChatOptions
        {
            Delay = (pause, _) =>
            {
                Assert.Equal(TimeSpan.FromSeconds(1), pause);
                pauses++;
                return Task.CompletedTask;
            },
        });

        // When
        Send(chat, "It crashes.");

        // Then
        Assert.Equal(3, pauses);
        Assert.All(
            nevaBridge.Calls.Where(c => c.Operation == "getIntakeTurnEvents").Skip(1),
            call => Assert.Equal(first.Cursor, call.Arguments["after"]));
        Assert.Equal(2, chat.Messages.Count);
        Assert.False(chat.CanCheckAgain);
    }

    [Fact]
    public void After_failed_polls_check_again_reads_the_conversation_and_follows_its_running_turn()
    {
        // Given
        FakeIntakeConversations nevaBridge = Started()
            .Answer("startIntakeTurn", new IntakeTurnStarted(NextTurnId));
        for (int attempt = 0; attempt < 4; attempt++)
        {
            nevaBridge.Answer("getIntakeTurnEvents", EdgeFailure());
        }

        IntakeChatViewModel chat = Chat(nevaBridge);
        Send(chat, "It crashes.");
        Send(chat, "On save.");
        Assert.True(chat.CanCheckAgain);
        Assert.Equal(IntakeChatText.Unreadable, chat.ErrorText);
        Assert.False(chat.SendCommand.CanExecute(null));
        Assert.Equal(new ChatMessageItem("On save.", true), chat.Messages[^1]);
        nevaBridge
            .Answer("getConversation", Conversation(1, [("user", "It crashes."), ("assistant", "When?")], activeTurnId: NextTurnId))
            .Answer("getIntakeTurnEvents", Events(Done(2)))
            .Answer("getConversation", Conversation(2, [("user", "It crashes."), ("assistant", "When?"), ("user", "On save."), ("assistant", "Thanks.")]));

        // When
        chat.CheckAgainCommand.Execute(null);

        // Then
        RecordedCall resumed = nevaBridge.Calls.Last(c => c.Operation == "getIntakeTurnEvents");
        Assert.Equal(NextTurnId, resumed.Arguments["turnId"]);
        Assert.Null(resumed.Arguments["after"]);
        Assert.False(chat.CanCheckAgain);
        Assert.Null(chat.ErrorText);
        Assert.Equal(string.Empty, chat.Draft);
        Assert.Equal(4, chat.Messages.Count);
    }

    [Fact]
    public void After_failed_polls_of_a_first_turn_check_again_reads_its_turn_so_a_failure_still_returns_the_message()
    {
        // Given
        IntakeTurnEvents first = Events(Status("planning"));
        FakeIntakeConversations nevaBridge = new FakeIntakeConversations()
            .Answer("startIntakeConversation", new IntakeConversationStarted(ConversationId, TurnId))
            .Answer("getIntakeTurnEvents", first);
        for (int attempt = 0; attempt < 4; attempt++)
        {
            nevaBridge.Answer("getIntakeTurnEvents", new HttpRequestException("Connection reset"));
        }

        IntakeChatViewModel chat = Chat(nevaBridge);
        Send(chat, "It crashes.");
        Assert.True(chat.CanCheckAgain);
        nevaBridge.Answer("getIntakeTurnEvents", Events(Error("IntakeTurnTimedOut", "The reply took too long.", 408)));

        // When
        chat.CheckAgainCommand.Execute(null);

        // Then
        RecordedCall resumed = nevaBridge.Calls.Last();
        Assert.Equal("getIntakeTurnEvents", resumed.Operation);
        Assert.Equal(TurnId, resumed.Arguments["turnId"]);
        Assert.Equal(first.Cursor, resumed.Arguments["after"]);
        Assert.DoesNotContain("getConversation", nevaBridge.Operations);
        Assert.Equal("It crashes.", chat.Draft);
        Assert.Equal("The reply took too long.", chat.ErrorText);
        Assert.Empty(chat.Messages);
        Assert.False(chat.CanFinish);
    }

    [Fact]
    public void Finish_sends_the_closing_message_and_shows_what_was_filed()
    {
        // Given
        FakeIntakeConversations nevaBridge = Started()
            .Answer("startIntakeTurn", new IntakeTurnStarted(NextTurnId))
            .Answer("getIntakeTurnEvents", Events(Done(2)))
            .Answer("getConversation", Conversation(
                2,
                [("user", "It crashes."), ("assistant", "When?"), ("user", IntakeChatText.FinishMessage), ("assistant", "Thank you.")],
                status: "closed",
                reports:
                [
                    Filed("bug_report", "submitted", "Crash when saving an invoice"),
                    Filed("intake_record", "closed", "How to export invoices", reason: "answered"),
                ]));
        IntakeChatViewModel chat = Chat(nevaBridge);
        Send(chat, "It crashes.");

        // When
        chat.FinishCommand.Execute(null);

        // Then
        Assert.Equal(
            new StartIntakeTurnRequest(IntakeChatText.FinishMessage, 1) { Finish = true },
            nevaBridge.Calls.Single(c => c.Operation == "startIntakeTurn").Arguments["request"]);
        Assert.True(chat.IsEnded);
        Assert.Equal(IntakeChatText.Ended, chat.EndedText);
        Assert.Equal(
            [
                new FiledReportItem("Crash when saving an invoice", "Bug report", "Passed on to the team", null),
                new FiledReportItem("How to export invoices", "Note for the team", "Closed", "Answered in the chat"),
            ],
            chat.FiledReports);
        chat.Draft = "One more thing";
        Assert.False(chat.SendCommand.CanExecute(null));
        Assert.False(chat.FinishCommand.CanExecute(null));
    }

    [Fact]
    public void A_chat_that_ended_without_reports_says_nothing_was_passed_on()
    {
        // Given
        FakeIntakeConversations nevaBridge = new FakeIntakeConversations()
            .Answer("startIntakeConversation", new IntakeConversationStarted(ConversationId, TurnId))
            .Answer("getIntakeTurnEvents", Events(Done(1)))
            .Answer("getConversation", Conversation(1, [("user", "Thanks, that was all."), ("assistant", "Glad to help.")], status: "closed"));
        IntakeChatViewModel chat = Chat(nevaBridge);

        // When
        Send(chat, "Thanks, that was all.");

        // Then
        Assert.True(chat.IsEnded);
        Assert.Empty(chat.FiledReports);
        Assert.Equal(IntakeChatText.Ended + " " + IntakeChatText.NothingFiled, chat.EndedText);
    }

    [Fact]
    public void Finish_waits_until_the_composer_is_empty_and_needs_a_conversation()
    {
        // Given
        FakeIntakeConversations nevaBridge = Started();
        IntakeChatViewModel chat = Chat(nevaBridge);
        Assert.False(chat.FinishCommand.CanExecute(null));
        Send(chat, "It crashes.");

        // When
        chat.Draft = "Also, the totals are wrong.";

        // Then
        Assert.False(chat.FinishCommand.CanExecute(null));
        Assert.Equal(IntakeChatText.FinishBlocked, chat.FinishHint);
        chat.Draft = string.Empty;
        Assert.True(chat.FinishCommand.CanExecute(null));
        Assert.Null(chat.FinishHint);
    }

    [Fact]
    public void A_chosen_file_is_attached_listed_and_can_be_removed()
    {
        // Given
        PickedFile file = new("crash.log", 12, () => new MemoryStream(new byte[12]));
        Attachment attachment = new("attachment-1", "crash.log", "text", 12, "api:jdoe", DateTimeOffset.UnixEpoch);
        FakeIntakeConversations nevaBridge = Started().Answer("listAttachments", NoFiles)
            .Answer("attachFile", attachment)
            .Answer("deleteAttachment", new object());
        IntakeChatViewModel chat = Chat(nevaBridge, new IntakeChatOptions { PickFile = () => Task.FromResult<PickedFile?>(file) });
        Assert.True(chat.OffersAttachments);
        Assert.False(chat.CanAttach);
        Send(chat, "It crashes.");

        // When
        chat.AttachCommand.Execute(null);

        // Then
        RecordedCall attach = nevaBridge.Calls.Single(c => c.Operation == "attachFile");
        Assert.Equal(ConversationId, attach.Arguments["conversationId"]);
        Assert.Equal("jdoe", attach.Arguments["uploaderId"]);
        Assert.Same(file, attach.Arguments["file"]);
        AttachmentItem listed = Assert.Single(chat.Attachments);
        Assert.Equal("crash.log", listed.FileName);

        // When
        listed.RemoveCommand.Execute(null);

        // Then
        RecordedCall delete = nevaBridge.Calls.Single(c => c.Operation == "deleteAttachment");
        Assert.Equal("attachment-1", delete.Arguments["attachmentId"]);
        Assert.Empty(chat.Attachments);
    }

    [Fact]
    public void Attachments_switched_off_while_the_chat_is_open_disappear_with_a_notice()
    {
        // Given
        PickedFile file = new("crash.log", 12, () => new MemoryStream(new byte[12]));
        FakeIntakeConversations nevaBridge = Started().Answer("listAttachments", NoFiles)
            .Answer("attachFile", ApiError(403, "Forbidden"));
        IntakeChatViewModel chat = Chat(nevaBridge, new IntakeChatOptions { PickFile = () => Task.FromResult<PickedFile?>(file) });
        Send(chat, "It crashes.");

        // When
        chat.AttachCommand.Execute(null);

        // Then
        Assert.False(chat.OffersAttachments);
        Assert.False(chat.AttachCommand.CanExecute(null));
        Assert.Equal(IntakeChatText.AttachmentsOff, chat.AttachmentNotice);
        Assert.Empty(chat.Attachments);
    }

    [Fact]
    public void A_cancelled_file_choice_attaches_nothing()
    {
        // Given
        FakeIntakeConversations nevaBridge = Started().Answer("listAttachments", NoFiles);
        IntakeChatViewModel chat = Chat(nevaBridge, new IntakeChatOptions { PickFile = () => Task.FromResult<PickedFile?>(null) });
        Send(chat, "It crashes.");

        // When
        chat.AttachCommand.Execute(null);

        // Then
        Assert.DoesNotContain("attachFile", nevaBridge.Operations);
        Assert.Null(chat.AttachmentNotice);
    }

    [Fact]
    public void Without_a_file_picker_the_chat_offers_no_attachments()
    {
        // Given
        FakeIntakeConversations nevaBridge = Started();
        IntakeChatViewModel chat = Chat(nevaBridge);

        // When
        Send(chat, "It crashes.");

        // Then
        Assert.False(chat.OffersAttachments);
        Assert.False(chat.AttachCommand.CanExecute(null));
    }

    [Fact]
    public async Task Closing_the_chat_while_a_file_uploads_ends_the_upload_quietly()
    {
        // Given
        PickedFile file = new("crash.log", 12, () => new MemoryStream(new byte[12]));
        TaskCompletionSource<Attachment> upload = new();
        FakeIntakeConversations nevaBridge = Started().Answer("listAttachments", NoFiles).Answer("attachFile", upload.Task);
        IntakeChatViewModel chat = Chat(nevaBridge, new IntakeChatOptions { PickFile = () => Task.FromResult<PickedFile?>(file) });
        Send(chat, "It crashes.");
        Task attaching = chat.AttachCommand.ExecuteAsync();

        // When
        chat.Dispose();
        upload.SetCanceled();

        // Then
        await attaching;
        Assert.Empty(chat.Attachments);
        Assert.Null(chat.AttachmentNotice);
    }

    [Fact]
    public async Task Closing_the_chat_while_a_file_is_removed_ends_the_removal_quietly()
    {
        // Given
        PickedFile file = new("crash.log", 12, () => new MemoryStream(new byte[12]));
        TaskCompletionSource<object> removal = new();
        FakeIntakeConversations nevaBridge = Started().Answer("listAttachments", NoFiles)
            .Answer("attachFile", new Attachment("attachment-1", "crash.log", "text", 12, "api:jdoe", DateTimeOffset.UnixEpoch))
            .Answer("deleteAttachment", removal.Task);
        IntakeChatViewModel chat = Chat(nevaBridge, new IntakeChatOptions { PickFile = () => Task.FromResult<PickedFile?>(file) });
        Send(chat, "It crashes.");
        await chat.AttachCommand.ExecuteAsync();
        Task removing = Assert.Single(chat.Attachments).RemoveCommand.ExecuteAsync();

        // When
        chat.Dispose();
        removal.SetCanceled();

        // Then
        await removing;
        Assert.Null(chat.AttachmentNotice);
    }

    [Fact]
    public void Files_can_be_attached_only_once_the_first_turn_is_saved()
    {
        // Given: a first turn that is still running
        PickedFile file = new("crash.log", 12, () => new MemoryStream(new byte[12]));
        TaskCompletionSource<IntakeTurnEvents> firstTurn = new();
        FakeIntakeConversations nevaBridge = new FakeIntakeConversations()
            .Answer("startIntakeConversation", new IntakeConversationStarted(ConversationId, TurnId))
            .Answer("getIntakeTurnEvents", firstTurn.Task)
            .Answer("getConversation", Conversation(1, [("user", "It crashes."), ("assistant", "When?")]))
            .Answer("listAttachments", NoFiles);
        IntakeChatViewModel chat = Chat(nevaBridge, new IntakeChatOptions { PickFile = () => Task.FromResult<PickedFile?>(file) });
        Send(chat, "It crashes.");

        // Then: the conversation would be deleted with its files if this turn failed
        Assert.True(chat.IsBusy);
        Assert.False(chat.CanAttach);
        Assert.False(chat.AttachCommand.CanExecute(null));

        // When
        firstTurn.SetResult(Events(Done(1)));

        // Then
        Assert.True(chat.CanAttach);
    }

    [Fact]
    public async Task An_upload_whose_answer_was_lost_shows_the_files_NevaBridge_holds()
    {
        // Given: the confirmation was saved, but its answer never arrived
        PickedFile file = new("crash.log", 12, () => new MemoryStream(new byte[12]));
        Attachment saved = new("attachment-1", "crash.log", "text", 12, "api:jdoe", DateTimeOffset.UnixEpoch);
        FakeIntakeConversations nevaBridge = Started().Answer("listAttachments", NoFiles)
            .Answer("attachFile", new HttpRequestException("Connection reset"))
            .Answer("listAttachments", (IReadOnlyList<Attachment>)[saved]);
        IntakeChatViewModel chat = Chat(nevaBridge, new IntakeChatOptions { PickFile = () => Task.FromResult<PickedFile?>(file) });
        Send(chat, "It crashes.");

        // When
        await chat.AttachCommand.ExecuteAsync();

        // Then
        Assert.Equal(ConversationId, nevaBridge.Calls.Last(c => c.Operation == "listAttachments").Arguments["conversationId"]);
        Assert.Equal(saved, Assert.Single(chat.Attachments).Attachment);
        Assert.NotNull(chat.AttachmentNotice);
    }

    [Fact]
    public async Task A_file_picker_that_fails_is_reported_as_an_attachment_failure()
    {
        // Given
        FakeIntakeConversations nevaBridge = Started().Answer("listAttachments", NoFiles).Answer("listAttachments", NoFiles);
        IntakeChatViewModel chat = Chat(nevaBridge, new IntakeChatOptions
        {
            PickFile = () => Task.FromException<PickedFile?>(new FileNotFoundException("crash.log is gone")),
        });
        Send(chat, "It crashes.");

        // When
        await chat.AttachCommand.ExecuteAsync();

        // Then
        Assert.NotNull(chat.AttachmentNotice);
        Assert.False(chat.IsBusy);
        Assert.True(chat.AttachCommand.CanExecute(null));
    }

    [Fact]
    public async Task Removing_a_file_is_no_longer_offered_once_the_chat_ended()
    {
        // Given
        PickedFile file = new("crash.log", 12, () => new MemoryStream(new byte[12]));
        FakeIntakeConversations nevaBridge = Started().Answer("listAttachments", NoFiles)
            .Answer("attachFile", new Attachment("attachment-1", "crash.log", "text", 12, "api:jdoe", DateTimeOffset.UnixEpoch))
            .Answer("startIntakeTurn", new IntakeTurnStarted(NextTurnId))
            .Answer("getIntakeTurnEvents", Events(Done(2)))
            .Answer("getConversation", Conversation(2, [("user", "It crashes."), ("assistant", "Thank you.")], status: "closed"))
            .Answer("listAttachments", NoFiles);
        IntakeChatViewModel chat = Chat(nevaBridge, new IntakeChatOptions { PickFile = () => Task.FromResult<PickedFile?>(file) });
        Send(chat, "It crashes.");
        await chat.AttachCommand.ExecuteAsync();
        AttachmentItem attached = Assert.Single(chat.Attachments);
        int refreshes = 0;
        attached.RemoveCommand.CanExecuteChanged += (_, _) => refreshes++;

        // When
        await chat.FinishCommand.ExecuteAsync();

        // Then
        Assert.True(chat.IsEnded);
        Assert.True(refreshes > 0);
        Assert.False(attached.RemoveCommand.CanExecute(null));
    }

    [Fact]
    public void Check_again_on_a_first_turn_keeps_the_reply_written_before_the_polls_failed()
    {
        // Given
        FakeIntakeConversations nevaBridge = new FakeIntakeConversations()
            .Answer("startIntakeConversation", new IntakeConversationStarted(ConversationId, TurnId))
            .Answer("getIntakeTurnEvents", Events(Text("When does ")));
        for (int attempt = 0; attempt < 4; attempt++)
        {
            nevaBridge.Answer("getIntakeTurnEvents", EdgeFailure());
        }

        IntakeChatViewModel chat = Chat(nevaBridge);
        Send(chat, "It crashes.");
        nevaBridge
            .Answer("getIntakeTurnEvents", Events(Text("it crash?")))
            .Answer("getIntakeTurnEvents", Events(Done(1)))
            .Answer("getConversation", Conversation(1, [("user", "It crashes."), ("assistant", "When does it crash?")]));
        List<string?> replies = [];
        chat.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(IntakeChatViewModel.LiveReply)) replies.Add(chat.LiveReply);
        };

        // When
        chat.CheckAgainCommand.Execute(null);

        // Then
        Assert.Contains("When does it crash?", replies);
        Assert.DoesNotContain("it crash?", replies);
    }

    [Fact]
    public void Each_conversation_read_shows_the_files_NevaBridge_holds()
    {
        // Given: a file attached earlier, for example before the chat lost track of an upload
        PickedFile file = new("crash.log", 12, () => new MemoryStream(new byte[12]));
        Attachment held = new("attachment-1", "crash.log", "text", 12, "api:jdoe", DateTimeOffset.UnixEpoch);
        FakeIntakeConversations nevaBridge = Started().Answer("listAttachments", (IReadOnlyList<Attachment>)[held]);
        IntakeChatViewModel chat = Chat(nevaBridge, new IntakeChatOptions { PickFile = () => Task.FromResult<PickedFile?>(file) });

        // When
        Send(chat, "It crashes.");

        // Then
        Assert.Equal(held, Assert.Single(chat.Attachments).Attachment);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Closing_the_chat_while_the_files_are_read_after_a_failure_ends_quietly(bool removing)
    {
        // Given: an attachment action failed, and the files NevaBridge holds are being read
        PickedFile file = new("crash.log", 12, () => new MemoryStream(new byte[12]));
        TaskCompletionSource<IReadOnlyList<Attachment>> listing = new();
        FakeIntakeConversations nevaBridge = Started().Answer("listAttachments", NoFiles);
        if (removing)
        {
            nevaBridge
                .Answer("attachFile", new Attachment("attachment-1", "crash.log", "text", 12, "api:jdoe", DateTimeOffset.UnixEpoch))
                .Answer("deleteAttachment", new HttpRequestException("Connection reset"));
        }
        else
        {
            nevaBridge.Answer("attachFile", new HttpRequestException("Connection reset"));
        }

        nevaBridge.Answer("listAttachments", listing.Task);
        IntakeChatViewModel chat = Chat(nevaBridge, new IntakeChatOptions { PickFile = () => Task.FromResult<PickedFile?>(file) });
        Send(chat, "It crashes.");
        Task action;
        if (removing)
        {
            await chat.AttachCommand.ExecuteAsync();
            action = Assert.Single(chat.Attachments).RemoveCommand.ExecuteAsync();
        }
        else
        {
            action = chat.AttachCommand.ExecuteAsync();
        }

        // When
        chat.Dispose();
        listing.SetCanceled();

        // Then
        await action;
        Assert.NotNull(chat.AttachmentNotice);
    }

    [Fact]
    public async Task Files_that_could_not_be_listed_can_be_loaded_again()
    {
        // Given: an upload failed, and listing the files failed as well
        PickedFile file = new("crash.log", 12, () => new MemoryStream(new byte[12]));
        Attachment saved = new("attachment-1", "crash.log", "text", 12, "api:jdoe", DateTimeOffset.UnixEpoch);
        FakeIntakeConversations nevaBridge = Started().Answer("listAttachments", NoFiles)
            .Answer("attachFile", new HttpRequestException("Connection reset"))
            .Answer("listAttachments", new HttpRequestException("Connection reset"))
            .Answer("listAttachments", (IReadOnlyList<Attachment>)[saved]);
        IntakeChatViewModel chat = Chat(nevaBridge, new IntakeChatOptions { PickFile = () => Task.FromResult<PickedFile?>(file) });
        Send(chat, "It crashes.");
        await chat.AttachCommand.ExecuteAsync();
        Assert.True(chat.FilesUnknown);
        Assert.Contains(IntakeChatText.FilesUnknown, chat.AttachmentNotice);
        Assert.True(chat.RefreshFilesCommand.CanExecute(null));

        // When
        await chat.RefreshFilesCommand.ExecuteAsync();

        // Then
        Assert.False(chat.FilesUnknown);
        Assert.Null(chat.AttachmentNotice);
        Assert.Equal(saved, Assert.Single(chat.Attachments).Attachment);
        Assert.False(chat.RefreshFilesCommand.CanExecute(null));
    }

    [Fact]
    public async Task A_conversation_read_lists_the_files_only_after_an_upload_ends()
    {
        // Given: a reply is still being written while a file uploads
        PickedFile file = new("crash.log", 12, () => new MemoryStream(new byte[12]));
        Attachment uploaded = new("attachment-1", "crash.log", "text", 12, "api:jdoe", DateTimeOffset.UnixEpoch);
        TaskCompletionSource<IntakeTurnEvents> secondTurn = new();
        TaskCompletionSource<Attachment> upload = new();
        FakeIntakeConversations nevaBridge = Started().Answer("listAttachments", NoFiles)
            .Answer("startIntakeTurn", new IntakeTurnStarted(NextTurnId))
            .Answer("getIntakeTurnEvents", secondTurn.Task)
            .Answer("getConversation", Conversation(2, [("user", "It crashes."), ("assistant", "When?"), ("user", "On save."), ("assistant", "Thanks.")]))
            .Answer("attachFile", upload.Task)
            .Answer("listAttachments", (IReadOnlyList<Attachment>)[uploaded]);
        IntakeChatViewModel chat = Chat(nevaBridge, new IntakeChatOptions { PickFile = () => Task.FromResult<PickedFile?>(file) });
        Send(chat, "It crashes.");
        Send(chat, "On save.");
        Task attaching = chat.AttachCommand.ExecuteAsync();
        Assert.False(chat.AttachCommand.CanExecute(null));

        // When: the reply ends while the upload still runs, then the upload ends
        secondTurn.SetResult(Events(Done(2)));
        int listingsDuringUpload = nevaBridge.Operations.Count(o => o == "listAttachments");
        upload.SetResult(uploaded);
        await attaching;

        // Then: the read's listing waited for the upload instead of racing it
        Assert.Equal(1, listingsDuringUpload);
        Assert.Equal(2, nevaBridge.Operations.Count(o => o == "listAttachments"));
        Assert.Equal(uploaded, Assert.Single(chat.Attachments).Attachment);
        Assert.True(chat.AttachCommand.CanExecute(null));
    }

    [Fact]
    public async Task While_the_files_are_listed_no_file_can_be_attached_or_removed()
    {
        // Given: listing the files again is under way
        PickedFile file = new("crash.log", 12, () => new MemoryStream(new byte[12]));
        Attachment held = new("attachment-1", "crash.log", "text", 12, "api:jdoe", DateTimeOffset.UnixEpoch);
        TaskCompletionSource<IReadOnlyList<Attachment>> listing = new();
        FakeIntakeConversations nevaBridge = Started().Answer("listAttachments", (IReadOnlyList<Attachment>)[held])
            .Answer("deleteAttachment", new HttpRequestException("Connection reset"))
            .Answer("listAttachments", new HttpRequestException("Connection reset"))
            .Answer("listAttachments", listing.Task);
        IntakeChatViewModel chat = Chat(nevaBridge, new IntakeChatOptions { PickFile = () => Task.FromResult<PickedFile?>(file) });
        Send(chat, "It crashes.");
        await Assert.Single(chat.Attachments).RemoveCommand.ExecuteAsync();
        Task refreshing = chat.RefreshFilesCommand.ExecuteAsync();

        // When
        await chat.AttachCommand.ExecuteAsync();

        // Then
        Assert.False(chat.AttachCommand.CanExecute(null));
        Assert.False(Assert.Single(chat.Attachments).RemoveCommand.CanExecute(null));
        Assert.DoesNotContain("attachFile", nevaBridge.Operations);
        listing.SetResult([held]);
        await refreshing;
        Assert.True(chat.AttachCommand.CanExecute(null));
    }

    [Fact]
    public void Attachments_refused_when_listed_disappear_with_a_notice()
    {
        // Given
        PickedFile file = new("crash.log", 12, () => new MemoryStream(new byte[12]));
        FakeIntakeConversations nevaBridge = Started().Answer("listAttachments", ApiError(403, "Forbidden"));
        IntakeChatViewModel chat = Chat(nevaBridge, new IntakeChatOptions { PickFile = () => Task.FromResult<PickedFile?>(file) });

        // When
        Send(chat, "It crashes.");

        // Then
        Assert.False(chat.OffersAttachments);
        Assert.Equal(IntakeChatText.AttachmentsOff, chat.AttachmentNotice);
        Assert.False(chat.FilesUnknown);
    }

    [Fact]
    public async Task Files_that_could_not_be_listed_stay_marked_as_unknown_while_other_files_are_attached()
    {
        // Given: listing the files failed on a conversation read
        PickedFile file = new("crash.log", 12, () => new MemoryStream(new byte[12]));
        FakeIntakeConversations nevaBridge = Started()
            .Answer("listAttachments", new HttpRequestException("Connection reset"))
            .Answer("attachFile", new Attachment("attachment-2", "crash.log", "text", 12, "api:jdoe", DateTimeOffset.UnixEpoch));
        bool cancelled = true;
        IntakeChatViewModel chat = Chat(nevaBridge, new IntakeChatOptions
        {
            PickFile = () => Task.FromResult<PickedFile?>(cancelled ? null : file),
        });
        Send(chat, "It crashes.");
        Assert.True(chat.FilesUnknown);

        // When: the reporter cancels a file choice, then attaches a file
        await chat.AttachCommand.ExecuteAsync();
        Assert.Equal(IntakeChatText.FilesUnknown, chat.AttachmentNotice);
        cancelled = false;
        await chat.AttachCommand.ExecuteAsync();

        // Then: the notice and its retry stay until a listing succeeds
        Assert.Equal(IntakeChatText.FilesUnknown, chat.AttachmentNotice);
        Assert.True(chat.RefreshFilesCommand.CanExecute(null));
    }
}
