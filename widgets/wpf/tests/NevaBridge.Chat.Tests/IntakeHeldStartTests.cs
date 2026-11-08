using NevaBridge.Chat.Tests.Support;
using static NevaBridge.Chat.Tests.Support.FakeIntakeConversations;

namespace NevaBridge.Chat.Tests;

/// <summary>Files attached before the first message, which start the conversation without one.</summary>
public sealed class IntakeHeldStartTests
{
    private static readonly NevaBridgeReporter Reporter = new("jdoe", "Jane Doe", ["customer"]);

    private static readonly PickedFile Log = new("crash.log", 12, () => new MemoryStream(new byte[12]));

    private static readonly PickedFile Screenshot = new("screen.png", 34, () => new MemoryStream(new byte[34]));

    private static readonly Attachment LogAttached = new("attachment-1", "crash.log", "text", 12, "api:jdoe", DateTimeOffset.UnixEpoch);

    private static readonly Attachment ScreenshotAttached = new("attachment-2", "screen.png", "image", 34, "api:jdoe", DateTimeOffset.UnixEpoch);

    private static IntakeChatViewModel Chat(FakeIntakeConversations nevaBridge, params PickedFile[] picks)
    {
        Queue<PickedFile> queue = new(picks);
        return new IntakeChatViewModel(
            nevaBridge,
            "product-1",
            Reporter,
            new IntakeChatOptions
            {
                PickFile = () => Task.FromResult<PickedFile?>(queue.Dequeue()),
                ApplicationContext = new Dictionary<string, string> { ["screen"] = "Invoices" },
                ReporterContact = new ReporterContact { Email = "jane@example.com" },
                Delay = (_, _) => Task.CompletedTask,
            });
    }

    private static void Send(IntakeChatViewModel chat, string text)
    {
        chat.Draft = text;
        chat.SendCommand.Execute(null);
    }

    /// <summary>NevaBridge answers the first message of a held conversation and lists its files.</summary>
    private static FakeIntakeConversations AnswersFirstMessage(FakeIntakeConversations nevaBridge, params Attachment[] held) =>
        nevaBridge
            .Answer("startIntakeTurn", new IntakeTurnStarted(TurnId))
            .Answer("getIntakeTurnEvents", Events(Status("attachments"), Done(1)))
            .Answer("getConversation", Conversation(1, [("user", "It crashes."), ("assistant", "I see the log.")]))
            .Answer("listAttachments", (IReadOnlyList<Attachment>)held);

    [Fact]
    public void Attaching_is_offered_before_the_first_message()
    {
        // Given
        IntakeChatViewModel chat = Chat(new FakeIntakeConversations(), Log);

        // When nothing was sent yet

        // Then
        Assert.True(chat.CanAttach);
        Assert.True(chat.AttachCommand.CanExecute(null));
    }

    [Fact]
    public void The_first_picked_file_starts_the_conversation_without_a_message_and_uploads_at_once()
    {
        // Given
        FakeIntakeConversations nevaBridge = new FakeIntakeConversations()
            .Answer("startHeldIntakeConversation", Held("crash.log"))
            .Answer("uploadAttachment", LogAttached);
        IntakeChatViewModel chat = Chat(nevaBridge, Log);

        // When
        chat.AttachCommand.Execute(null);

        // Then
        Assert.Equal(["startHeldIntakeConversation", "uploadAttachment"], nevaBridge.Operations);
        RecordedCall start = nevaBridge.Calls[0];
        Assert.Equal("product-1", start.Arguments["productId"]);
        Assert.Equal(Reporter, start.Arguments["reporter"]);
        StartHeldIntakeConversationRequest request = Assert.IsType<StartHeldIntakeConversationRequest>(start.Arguments["request"]);
        Assert.Equal([new AttachmentUploadRequest("crash.log", 12)], request.Attachments);
        Assert.Equal("jane@example.com", request.ReporterContact?.Email);
        Assert.Equal("Invoices", request.ApplicationContext?["screen"]);
        RecordedCall upload = nevaBridge.Calls[1];
        Assert.Equal(ConversationId, upload.Arguments["conversationId"]);
        Assert.Equal("attachment-1", Assert.IsType<AttachmentUpload>(upload.Arguments["upload"]).AttachmentId);
        Assert.Same(Log, upload.Arguments["file"]);
        AttachmentItem item = Assert.Single(chat.Attachments);
        Assert.Equal(AttachmentState.Uploaded, item.State);
        Assert.Equal(LogAttached, item.Attachment);
        Assert.True(item.RemoveCommand.CanExecute(null));
    }

    [Fact]
    public void Further_files_before_the_first_message_take_the_ordinary_upload()
    {
        // Given
        FakeIntakeConversations nevaBridge = new FakeIntakeConversations()
            .Answer("startHeldIntakeConversation", Held("crash.log"))
            .Answer("uploadAttachment", LogAttached)
            .Answer("attachFile", ScreenshotAttached);
        IntakeChatViewModel chat = Chat(nevaBridge, Log, Screenshot);
        chat.AttachCommand.Execute(null);

        // When
        chat.AttachCommand.Execute(null);

        // Then
        Assert.Equal(["startHeldIntakeConversation", "uploadAttachment", "attachFile"], nevaBridge.Operations);
        Assert.Equal(ConversationId, nevaBridge.Calls[2].Arguments["conversationId"]);
        Assert.Equal(["crash.log", "screen.png"], chat.Attachments.Select(item => item.FileName));
    }

    [Fact]
    public void The_first_message_after_files_starts_the_first_reply_at_revision_0()
    {
        // Given
        FakeIntakeConversations nevaBridge = AnswersFirstMessage(
            new FakeIntakeConversations()
                .Answer("startHeldIntakeConversation", Held("crash.log"))
                .Answer("uploadAttachment", LogAttached),
            LogAttached);
        IntakeChatViewModel chat = Chat(nevaBridge, Log);
        chat.AttachCommand.Execute(null);
        List<string?> progress = [];
        chat.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(IntakeChatViewModel.ProgressText)) progress.Add(chat.ProgressText);
        };

        // When
        Send(chat, "It crashes.");

        // Then
        Assert.DoesNotContain("startIntakeConversation", nevaBridge.Operations);
        RecordedCall turn = nevaBridge.Calls.Single(c => c.Operation == "startIntakeTurn");
        Assert.Equal(ConversationId, turn.Arguments["conversationId"]);
        Assert.Equal(new StartIntakeTurnRequest("It crashes.", 0), turn.Arguments["request"]);
        Assert.Contains("Looking at the attached images", progress);
        Assert.Equal(
            [new ChatMessageItem("It crashes.", true), new ChatMessageItem("I see the log.", false)],
            chat.Messages);
        Assert.Equal(LogAttached, Assert.Single(chat.Attachments).Attachment);
    }

    [Fact]
    public async Task Send_waits_for_an_upload_still_in_flight()
    {
        // Given
        TaskCompletionSource<Attachment> upload = new();
        FakeIntakeConversations nevaBridge = AnswersFirstMessage(
            new FakeIntakeConversations()
                .Answer("startHeldIntakeConversation", Held("crash.log"))
                .Answer("uploadAttachment", upload.Task),
            LogAttached);
        IntakeChatViewModel chat = Chat(nevaBridge, Log);
        Task attaching = chat.AttachCommand.ExecuteAsync();
        Assert.Equal(AttachmentState.Uploading, Assert.Single(chat.Attachments).State);

        // When
        chat.Draft = "It crashes.";
        Assert.True(chat.SendCommand.CanExecute(null));
        Task sending = chat.SendCommand.ExecuteAsync();

        // Then: the message shows as sent while the file finishes, and the reply waits for it
        Assert.DoesNotContain("startIntakeTurn", nevaBridge.Operations);
        Assert.Equal(IntakeChatText.WaitingForFiles, chat.ProgressText);
        Assert.Equal([new ChatMessageItem("It crashes.", true)], chat.Messages);
        upload.SetResult(LogAttached);
        await attaching;
        await sending;
        Assert.Equal(new StartIntakeTurnRequest("It crashes.", 0), nevaBridge.Calls.Single(c => c.Operation == "startIntakeTurn").Arguments["request"]);
        Assert.Equal(2, chat.Messages.Count);
    }

    [Fact]
    public void A_failed_upload_keeps_its_file_for_retry_and_does_not_block_the_reply()
    {
        // Given
        FakeIntakeConversations nevaBridge = AnswersFirstMessage(
            new FakeIntakeConversations()
                .Answer("startHeldIntakeConversation", Held("crash.log"))
                .Answer("uploadAttachment", new NevaBridgeUploadException(503)));
        IntakeChatViewModel chat = Chat(nevaBridge, Log);
        chat.AttachCommand.Execute(null);
        AttachmentItem failed = Assert.Single(chat.Attachments);
        Assert.Equal(AttachmentState.Failed, failed.State);
        Assert.True(failed.RetryCommand.CanExecute(null));
        Assert.False(failed.RemoveCommand.CanExecute(null));

        // When
        Send(chat, "It crashes.");

        // Then: the reply went without the file, which stays listed for Retry
        Assert.Equal(2, chat.Messages.Count);
        Assert.Equal(AttachmentState.Failed, Assert.Single(chat.Attachments).State);
    }

    [Fact]
    public void Retry_uploads_a_failed_file_again()
    {
        // Given
        FakeIntakeConversations nevaBridge = new FakeIntakeConversations()
            .Answer("startHeldIntakeConversation", Held("crash.log"))
            .Answer("uploadAttachment", new NevaBridgeUploadException(null))
            .Answer("attachFile", LogAttached);
        IntakeChatViewModel chat = Chat(nevaBridge, Log);
        chat.AttachCommand.Execute(null);

        // When
        Assert.Single(chat.Attachments).RetryCommand.Execute(null);

        // Then
        RecordedCall retry = nevaBridge.Calls.Single(c => c.Operation == "attachFile");
        Assert.Equal(ConversationId, retry.Arguments["conversationId"]);
        Assert.Same(Log, retry.Arguments["file"]);
        AttachmentItem item = Assert.Single(chat.Attachments);
        Assert.Equal(AttachmentState.Uploaded, item.State);
        Assert.Equal(LogAttached, item.Attachment);
    }

    [Fact]
    public void Dismiss_drops_a_failed_file()
    {
        // Given
        FakeIntakeConversations nevaBridge = new FakeIntakeConversations()
            .Answer("startHeldIntakeConversation", Held("crash.log"))
            .Answer("uploadAttachment", new NevaBridgeUploadException(403));
        IntakeChatViewModel chat = Chat(nevaBridge, Log);
        chat.AttachCommand.Execute(null);

        // When
        Assert.Single(chat.Attachments).DismissCommand.Execute(null);

        // Then
        Assert.Empty(chat.Attachments);
        Assert.Null(chat.AttachmentNotice);
    }

    [Fact]
    public void A_refused_first_file_creates_nothing_and_the_first_message_starts_the_conversation()
    {
        // Given
        FakeIntakeConversations nevaBridge = new FakeIntakeConversations()
            .Answer("startHeldIntakeConversation", ApiError(400, "AttachmentTypeNotAllowed"))
            .Answer("startIntakeConversation", new IntakeConversationStarted(ConversationId, TurnId))
            .Answer("getIntakeTurnEvents", Events(Done(1)))
            .Answer("getConversation", Conversation(1, [("user", "It crashes."), ("assistant", "When?")]))
            .Answer("listAttachments", (IReadOnlyList<Attachment>)[]);
        IntakeChatViewModel chat = Chat(nevaBridge, Log);
        chat.AttachCommand.Execute(null);
        Assert.Empty(chat.Attachments);
        Assert.NotNull(chat.AttachmentNotice);

        // When
        Send(chat, "It crashes.");

        // Then
        Assert.Contains("startIntakeConversation", nevaBridge.Operations);
        Assert.DoesNotContain("startIntakeTurn", nevaBridge.Operations);
        Assert.Equal(2, chat.Messages.Count);
    }

    [Fact]
    public void A_start_whose_uploads_could_not_be_signed_attaches_the_file_to_the_stored_conversation()
    {
        // Given
        FakeIntakeConversations nevaBridge = new FakeIntakeConversations()
            .Answer("startHeldIntakeConversation", new NevaBridgeApiException(424, "FailedDependency", null, "signing failed") { ConversationId = ConversationId })
            .Answer("attachFile", LogAttached);
        IntakeChatViewModel chat = Chat(nevaBridge, Log);

        // When
        chat.AttachCommand.Execute(null);

        // Then
        Assert.Equal(ConversationId, nevaBridge.Calls.Single(c => c.Operation == "attachFile").Arguments["conversationId"]);
        Assert.Equal(LogAttached, Assert.Single(chat.Attachments).Attachment);
        Assert.Null(chat.AttachmentNotice);
    }

    [Fact]
    public void Finish_is_offered_only_after_the_first_reply()
    {
        // Given
        FakeIntakeConversations nevaBridge = AnswersFirstMessage(
            new FakeIntakeConversations()
                .Answer("startHeldIntakeConversation", Held("crash.log"))
                .Answer("uploadAttachment", LogAttached),
            LogAttached);
        IntakeChatViewModel chat = Chat(nevaBridge, Log);

        // When the conversation holds files but no message yet
        chat.AttachCommand.Execute(null);

        // Then
        Assert.False(chat.CanFinish);
        Assert.False(chat.FinishCommand.CanExecute(null));
        chat.Draft = "It";
        Assert.Null(chat.FinishHint);
        chat.Draft = string.Empty;

        // And after the first reply
        Send(chat, "It crashes.");
        Assert.True(chat.CanFinish);
    }

    [Fact]
    public void A_failed_first_reply_keeps_the_conversation_its_files_and_the_message()
    {
        // Given
        FakeIntakeConversations nevaBridge = new FakeIntakeConversations()
            .Answer("startHeldIntakeConversation", Held("crash.log"))
            .Answer("uploadAttachment", LogAttached)
            .Answer("startIntakeTurn", new IntakeTurnStarted(TurnId))
            .Answer("getIntakeTurnEvents", Events(Error("ModelInvocationFailed", "The assistant is unavailable.", 503)))
            .Answer("startIntakeTurn", new IntakeTurnStarted(NextTurnId))
            .Answer("getIntakeTurnEvents", Events(Done(1)))
            .Answer("getConversation", Conversation(1, [("user", "It crashes."), ("assistant", "I see the log.")]))
            .Answer("listAttachments", (IReadOnlyList<Attachment>)[LogAttached]);
        IntakeChatViewModel chat = Chat(nevaBridge, Log);
        chat.AttachCommand.Execute(null);
        Send(chat, "It crashes.");
        Assert.Equal("It crashes.", chat.Draft);
        Assert.Equal("The assistant is unavailable.", chat.ErrorText);
        Assert.Single(chat.Attachments);
        Assert.True(chat.CanAttach);

        // When
        chat.SendCommand.Execute(null);

        // Then: the same conversation, still at revision 0, takes the message
        Assert.DoesNotContain("startIntakeConversation", nevaBridge.Operations);
        Assert.Equal(
            [new StartIntakeTurnRequest("It crashes.", 0), new StartIntakeTurnRequest("It crashes.", 0)],
            nevaBridge.Calls.Where(c => c.Operation == "startIntakeTurn").Select(c => c.Arguments["request"]));
        Assert.Equal(2, chat.Messages.Count);
    }

    [Fact]
    public async Task Attaching_waits_while_a_first_message_without_files_is_on_its_way()
    {
        // Given
        TaskCompletionSource<IntakeConversationStarted> start = new();
        FakeIntakeConversations nevaBridge = new FakeIntakeConversations().Answer("startIntakeConversation", start.Task);
        IntakeChatViewModel chat = Chat(nevaBridge, Log);

        // When
        chat.Draft = "It crashes.";
        Task sending = chat.SendCommand.ExecuteAsync();

        // Then: that conversation is deleted again if its first turn fails, so its files would go with it
        Assert.False(chat.CanAttach);
        start.SetException(EdgeFailure());
        await sending;
        Assert.True(chat.CanAttach);
    }
}
