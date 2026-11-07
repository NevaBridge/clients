using System.Globalization;
using NevaBridge.Chat.Tests.Support;
using static NevaBridge.Chat.Tests.Support.FakeIntakeConversations;

namespace NevaBridge.Chat.Tests;

public sealed class IntakeVoiceTests
{
    private static readonly NevaBridgeReporter Reporter = new("jdoe", "Jane Doe", ["customer"]);
    private static readonly TimeSpan Pause = TimeSpan.FromSeconds(1.2);
    private static readonly TimeSpan Drain = TimeSpan.FromSeconds(5);

    private readonly FakeVoice _voice = new();
    private readonly FakeMicrophone _microphone = new();
    private readonly FakeSpeechPlayer _player = new();
    private readonly ManualDelays _delays = new();
    private readonly List<FakeTranscribeSocket> _sockets = [];
    private int _resultIds;

    public IntakeVoiceTests()
    {
        _player.MicrophoneToCheck = () => _microphone;
    }

    private FakeTranscribeSocket Transcribe => _sockets[^1];

    /// <summary>A first turn whose reply streams as two text events, then is saved with one more sentence.</summary>
    private static FakeIntakeConversations RepliesTo(string spoken, string saved = "Thanks. When does it crash? Since when?") =>
        new FakeIntakeConversations()
            .Answer("startIntakeConversation", new IntakeConversationStarted(ConversationId, TurnId))
            .Answer("getIntakeTurnEvents", new IntakeTurnEvents([Text("Thanks. "), Text("When does it crash? Since")], "c1", Finished: false))
            .Answer("getIntakeTurnEvents", Events(Done(1)))
            .Answer("getConversation", Conversation(1, [("user", spoken), ("assistant", saved)]));

    private IntakeChatViewModel Chat(
        FakeIntakeConversations nevaBridge,
        bool dictation = true,
        bool handsFree = true,
        VoiceLanguage? language = null) =>
        new(nevaBridge, "product-1", Reporter, new IntakeChatOptions
        {
            Voice = new IntakeChatVoice(_voice, _microphone, _player)
            {
                Dictation = dictation,
                HandsFree = handsFree,
                CreateSocket = () =>
                {
                    FakeTranscribeSocket socket = new();
                    _sockets.Add(socket);
                    return socket;
                },
            },
            VoiceLanguage = language ?? VoiceLanguage.English,
            Delay = _delays.Delay,
        });

    private static IntakeVoiceViewModel VoiceOf(IntakeChatViewModel chat) => chat.Voice ?? throw new InvalidOperationException("No voice.");

    /// <summary>The reporter says a phrase that Transcribe finishes.</summary>
    private void Say(string text) => Transcribe.Hear(($"r{++_resultIds}", text, false));

    /// <summary>The reporter says a phrase and falls silent long enough for it to be sent.</summary>
    private void SayAndPause(string text)
    {
        Say(text);
        _delays.Elapse(Pause);
    }

    [Fact]
    public void Without_voice_from_the_host_the_chat_offers_none()
    {
        // When
        IntakeChatViewModel chat = new(new FakeIntakeConversations(), "product-1", Reporter);

        // Then
        Assert.Null(chat.Voice);
    }

    [Fact]
    public void Each_voice_feature_is_offered_only_when_the_host_allows_it()
    {
        // When
        IntakeVoiceViewModel dictationOnly = VoiceOf(Chat(new FakeIntakeConversations(), dictation: true, handsFree: false));

        // Then
        Assert.True(dictationOnly.OffersDictation);
        Assert.False(dictationOnly.OffersHandsFree);
        Assert.False(dictationOnly.TalkCommand.CanExecute(null));
    }

    [Fact]
    public void Dictation_appends_what_is_heard_to_the_typed_text_and_holds_the_message_box_until_it_stops()
    {
        // Given
        IntakeChatViewModel chat = Chat(new FakeIntakeConversations());
        IntakeVoiceViewModel voice = VoiceOf(chat);
        int focusRequests = 0;
        voice.DictationEnded += (_, _) => focusRequests++;
        chat.Draft = "Invoices:  ";

        // When
        voice.ToggleDictationCommand.Execute(null);
        Transcribe.Hear(("a", "the export", true));

        // Then the unsettled words are shown apart
        Assert.True(voice.IsDictating);
        Assert.Equal("Invoices:  ", chat.Draft);
        Assert.Equal("the export", voice.Interim);
        Assert.False(chat.SendCommand.CanExecute(null));
        Assert.False(voice.CanChooseLanguage);

        // When the words settle
        Transcribe.Hear(("a", "the export fails.", false), ("b", "since", true));

        // Then
        Assert.Equal("Invoices: the export fails.", chat.Draft);
        Assert.Equal("since", voice.Interim);

        // When dictation stops
        voice.ToggleDictationCommand.Execute(null);

        // Then every word heard is kept
        Assert.False(voice.IsDictating);
        Assert.Equal("Invoices: the export fails. since", chat.Draft);
        Assert.Equal(string.Empty, voice.Interim);
        Assert.False(_microphone.IsOpen);
        Assert.Equal(1, focusRequests);
        Assert.True(chat.SendCommand.CanExecute(null));
        Assert.Equal([VoiceLanguage.English], _voice.SessionRequests);
    }

    [Fact]
    public void A_typed_message_returned_to_the_message_box_during_dictation_is_not_overwritten()
    {
        // Given a typed message is on its way and fails while the reporter dictates
        TaskCompletionSource<IntakeConversationStarted> start = new();
        IntakeChatViewModel chat = Chat(new FakeIntakeConversations().Answer("startIntakeConversation", start.Task));
        IntakeVoiceViewModel voice = VoiceOf(chat);
        chat.Draft = "It crashes.";
        chat.SendCommand.Execute(null);
        voice.ToggleDictationCommand.Execute(null);
        Say("Also");
        start.SetException(ApiError(500, "InternalError"));

        // When
        Say("the export");

        // Then
        Assert.False(voice.IsDictating);
        Assert.Equal("It crashes." + Environment.NewLine + "Also", chat.Draft);
    }

    [Fact]
    public void The_hands_free_loop_sends_after_a_pause_reads_the_reply_sentence_by_sentence_and_listens_again()
    {
        // Given
        IntakeChatViewModel chat = Chat(RepliesTo("It crashes when I save."));
        IntakeVoiceViewModel voice = VoiceOf(chat);
        chat.Draft = "typed earlier";

        // When the reporter talks
        voice.TalkCommand.Execute(null);

        // Then
        Assert.Equal(VoicePhase.Listening, voice.Phase);
        Assert.True(_microphone.IsOpen);
        Assert.False(chat.SendCommand.CanExecute(null));
        Assert.False(chat.FinishCommand.CanExecute(null));

        // When a phrase ends, but the pause has not run out
        Say("It crashes");
        Say("when I save.");

        // Then nothing is sent yet
        Assert.Empty(chat.Messages);
        Assert.Equal("It crashes when I save.", voice.Heard);
        Assert.Equal(1, _delays.Pending.Count(p => p == Pause));

        // When the reporter stays silent
        _delays.Elapse(Pause);

        // Then the words go as the first message and the reply is read as the live text completes sentences
        Assert.Equal("It crashes when I save.", chat.Messages[0].Text);
        Assert.Equal("typed earlier", chat.Draft);
        Assert.Equal(VoicePhase.Speaking, voice.Phase);
        Assert.False(_microphone.IsOpen);
        Assert.Equal(["Thanks."], _player.Started);
        Assert.Equal(["Thanks.", "When does it crash?"], _voice.SpeechRequests.Select(r => r.Text));

        // When each sentence plays to its end
        _player.Finish();
        Assert.Equal(["Thanks.", "When does it crash?", "Since when?"], _voice.SpeechRequests.Select(r => r.Text));
        _player.Finish();
        _player.Finish();

        // Then the saved reply's addition was read, and voice mode listens again on a new session
        Assert.Equal(["Thanks.", "When does it crash?", "Since when?"], _player.Started);
        Assert.Equal(VoicePhase.Listening, voice.Phase);
        Assert.True(_microphone.IsOpen);
        Assert.Equal(2, _voice.SessionRequests.Count);
        Assert.Equal(string.Empty, voice.Heard);
        Assert.False(_player.HeardTheMicrophoneOpen);
    }

    [Fact]
    public void Send_now_sends_without_waiting_for_the_pause()
    {
        // Given
        IntakeChatViewModel chat = Chat(RepliesTo("It crashes."));
        IntakeVoiceViewModel voice = VoiceOf(chat);
        voice.TalkCommand.Execute(null);
        Assert.False(voice.SendNowCommand.CanExecute(null));
        Transcribe.Hear(("a", "It crashes.", true));

        // When
        voice.SendNowCommand.Execute(null);

        // Then
        Assert.Equal("It crashes.", chat.Messages[0].Text);
        Assert.Equal(VoicePhase.Speaking, voice.Phase);
    }

    [Fact]
    public void A_saved_reply_that_does_not_continue_what_was_read_adds_nothing()
    {
        // Given
        IntakeChatViewModel chat = Chat(RepliesTo("It crashes.", saved: "Sorry to hear that. Which screen?"));
        IntakeVoiceViewModel voice = VoiceOf(chat);
        voice.TalkCommand.Execute(null);

        // When
        SayAndPause("It crashes.");
        _player.Finish();
        _player.Finish();

        // Then
        Assert.Equal(["Thanks.", "When does it crash?"], _player.Started);
        Assert.Equal(VoicePhase.Listening, voice.Phase);
    }

    [Fact]
    public void Stop_reading_drops_the_rest_of_the_reply_and_listens_again_once_the_turn_is_done()
    {
        // Given the reply is being read while its turn still runs
        TaskCompletionSource<IntakeTurnEvents> lastEvents = new();
        FakeIntakeConversations nevaBridge = new FakeIntakeConversations()
            .Answer("startIntakeConversation", new IntakeConversationStarted(ConversationId, TurnId))
            .Answer("getIntakeTurnEvents", new IntakeTurnEvents([Text("Thanks. When does it crash? ")], "c1", Finished: false))
            .Answer("getIntakeTurnEvents", lastEvents.Task)
            .Answer("getConversation", Conversation(1, [("user", "It crashes."), ("assistant", "Thanks. When does it crash? And since when?")]));
        IntakeChatViewModel chat = Chat(nevaBridge);
        IntakeVoiceViewModel voice = VoiceOf(chat);
        voice.TalkCommand.Execute(null);
        SayAndPause("It crashes.");
        Assert.Equal("Thanks.", _player.Playing);

        // When
        voice.StopReadingCommand.Execute(null);

        // Then the sentence stops, voice mode stays on, and the turn is still awaited
        Assert.Equal(["Thanks."], _player.Stopped);
        Assert.Equal(VoicePhase.Speaking, voice.Phase);
        Assert.False(_microphone.IsOpen);

        // When the turn ends
        lastEvents.SetResult(new IntakeTurnEvents([Text("And since when?"), Done(1)], "c2", Finished: true));

        // Then nothing more of the reply is read, the saved addition included
        Assert.Equal(["Thanks."], _player.Started);
        Assert.DoesNotContain(_voice.SpeechRequests, r => r.Text == "And since when?");
        Assert.Equal(VoicePhase.Listening, voice.Phase);
        Assert.True(_microphone.IsOpen);
    }

    [Fact]
    public void Ending_voice_mode_puts_words_heard_but_not_sent_on_their_own_line_after_the_typed_text()
    {
        // Given
        IntakeChatViewModel chat = Chat(new FakeIntakeConversations());
        IntakeVoiceViewModel voice = VoiceOf(chat);
        chat.Draft = "Typed first.";
        voice.TalkCommand.Execute(null);
        Say("The export");
        Transcribe.Hear(("x", "fails", true));

        // When
        voice.EndVoiceModeCommand.Execute(null);

        // Then
        Assert.Equal(VoicePhase.Off, voice.Phase);
        Assert.Equal("Typed first." + Environment.NewLine + "The export fails", chat.Draft);
        Assert.False(_microphone.IsOpen);
        Assert.DoesNotContain(Pause, _delays.Pending);
        Assert.True(chat.SendCommand.CanExecute(null));
    }

    [Fact]
    public void A_spoken_message_that_could_not_be_sent_goes_after_the_typed_text_and_ends_voice_mode()
    {
        // Given
        IntakeChatViewModel chat = Chat(new FakeIntakeConversations().Answer("startIntakeConversation", ApiError(500, "InternalError")));
        IntakeVoiceViewModel voice = VoiceOf(chat);
        chat.Draft = "Typed first.";
        voice.TalkCommand.Execute(null);

        // When
        SayAndPause("It crashes.");

        // Then
        Assert.Equal("Typed first." + Environment.NewLine + "It crashes.", chat.Draft);
        Assert.Equal(VoicePhase.Off, voice.Phase);
        Assert.Equal(IntakeChatText.SpokenNotSent, voice.Notice);
        Assert.NotNull(chat.ErrorText);
        Assert.Empty(chat.Messages);
    }

    [Fact]
    public void A_spoken_message_refused_for_a_stale_revision_is_kept_and_not_sent_twice()
    {
        // Given a started conversation whose next turn is refused because another client moved it on
        FakeIntakeConversations nevaBridge = RepliesTo("It crashes.")
            .Answer("startIntakeTurn", ApiError(409, "RevisionConflict"))
            .Answer("getConversation", Conversation(2, [("user", "It crashes."), ("assistant", "Thanks."), ("user", "Other tab"), ("assistant", "Ok.")]));
        IntakeChatViewModel chat = Chat(nevaBridge);
        IntakeVoiceViewModel voice = VoiceOf(chat);
        voice.TalkCommand.Execute(null);
        SayAndPause("It crashes.");
        _player.Finish();
        _player.Finish();
        _player.Finish();

        // When
        SayAndPause("Since Monday.");

        // Then
        Assert.Single(nevaBridge.Calls, c => c.Operation == "startIntakeTurn");
        Assert.Equal("Since Monday.", chat.Draft);
        Assert.Equal(VoicePhase.Off, voice.Phase);
    }

    [Fact]
    public void A_403_for_the_session_switches_voice_off_for_this_chat()
    {
        // Given
        _voice.AnswerSession(new NevaBridgeApiException(403, "Forbidden", null, "Feature \"voiceDictation\" is not available."));
        IntakeChatViewModel chat = Chat(new FakeIntakeConversations());
        IntakeVoiceViewModel voice = VoiceOf(chat);

        // When
        voice.ToggleDictationCommand.Execute(null);

        // Then
        Assert.False(voice.IsDictating);
        Assert.False(voice.OffersDictation);
        Assert.False(voice.OffersHandsFree);
        Assert.Equal(IntakeChatText.VoiceOff, voice.Notice);
        Assert.False(voice.ToggleDictationCommand.CanExecute(null));
        Assert.False(voice.TalkCommand.CanExecute(null));
        Assert.False(_microphone.IsOpen);
    }

    [Fact]
    public void A_403_for_speech_while_a_reply_is_read_switches_voice_off_and_makes_no_further_voice_call()
    {
        // Given
        _voice.AnswerSpeech(FakeVoice.Speech("Thanks."))
            .AnswerSpeech(new NevaBridgeApiException(403, "Forbidden", null, "Feature \"voiceConversation\" is not available."));
        IntakeChatViewModel chat = Chat(RepliesTo("It crashes."));
        IntakeVoiceViewModel voice = VoiceOf(chat);
        voice.TalkCommand.Execute(null);

        // When the refusal for the second sentence arrives while the first one plays
        SayAndPause("It crashes.");

        // Then
        Assert.Equal(["Thanks."], _player.Stopped);
        Assert.Equal(VoicePhase.Off, voice.Phase);
        Assert.False(voice.OffersHandsFree);
        Assert.Equal(IntakeChatText.VoiceOff, voice.Notice);
        Assert.Equal(2, _voice.SpeechRequests.Count);
        Assert.Single(_voice.SessionRequests);
        Assert.False(_microphone.IsOpen);
    }

    [Theory]
    [InlineData(403)]
    [InlineData(402)]
    public void A_refusal_for_the_next_sentence_counts_as_soon_as_it_arrives_while_the_current_one_plays(int status)
    {
        // Given the next sentence is being synthesized while the first one plays
        TaskCompletionSource<SpeechSynthesis> next = new();
        _voice.AnswerSpeech(FakeVoice.Speech("Thanks.")).AnswerSpeech(next.Task);
        IntakeChatViewModel chat = Chat(RepliesTo("It crashes."));
        IntakeVoiceViewModel voice = VoiceOf(chat);
        voice.TalkCommand.Execute(null);
        SayAndPause("It crashes.");
        Assert.Equal("Thanks.", _player.Playing);

        // When NevaBridge refuses it
        next.SetException(new NevaBridgeApiException(status, "Refused", null, "No credits left."));
        voice.StopReadingCommand.Execute(null);

        // Then the refusal ends voice mode at once, and no further voice call follows
        Assert.Equal(VoicePhase.Off, voice.Phase);
        Assert.Equal(["Thanks."], _player.Stopped);
        Assert.Equal(status == 403 ? IntakeChatText.VoiceOff : "No credits left.", voice.Notice);
        Assert.Equal(status != 403, voice.OffersHandsFree);
        Assert.Single(_voice.SessionRequests);
        Assert.Equal(2, _voice.SpeechRequests.Count);
        Assert.False(_microphone.IsOpen);
    }

    [Fact]
    public void The_next_sentence_is_not_synthesized_before_the_current_one_has_its_audio()
    {
        // Given the first sentence's synthesis is still on its way
        TaskCompletionSource<SpeechSynthesis> first = new();
        _voice.AnswerSpeech(first.Task);
        IntakeChatViewModel chat = Chat(RepliesTo("It crashes."));
        IntakeVoiceViewModel voice = VoiceOf(chat);
        voice.TalkCommand.Execute(null);

        // When the whole reply has arrived
        SayAndPause("It crashes.");

        // Then only the first sentence is being synthesized
        Assert.Equal(["Thanks."], _voice.SpeechRequests.Select(r => r.Text));

        // When it fails
        first.SetException(new NevaBridgeApiException(424, "FailedDependency", null, "Polly failed."));

        // Then the rest is read past without another paid synthesis
        Assert.Single(_voice.SpeechRequests);
        Assert.Equal(IntakeChatText.SpeechFailed, voice.Notice);
        Assert.Equal(VoicePhase.Listening, voice.Phase);
    }

    [Fact]
    public void Dictation_stops_when_a_typed_turn_closes_the_conversation()
    {
        // Given the reporter dictates while a typed Finish is on its way
        TaskCompletionSource<IntakeTurnEvents> finishing = new();
        FakeIntakeConversations nevaBridge = RepliesTo("It crashes.", saved: "Thanks.")
            .Answer("startIntakeTurn", new IntakeTurnStarted(NextTurnId))
            .Answer("getIntakeTurnEvents", finishing.Task)
            .Answer("getConversation", Conversation(2, [("user", "It crashes."), ("assistant", "Thanks."), ("user", IntakeChatText.FinishMessage), ("assistant", "Passed on.")], status: "closed"));
        IntakeChatViewModel chat = Chat(nevaBridge);
        IntakeVoiceViewModel voice = VoiceOf(chat);
        chat.Draft = "It crashes.";
        chat.SendCommand.Execute(null);
        chat.FinishCommand.Execute(null);
        voice.ToggleDictationCommand.Execute(null);
        Say("Also the export");
        Transcribe.Hear(("x", "fails", true));

        // When the conversation closes
        finishing.SetResult(Events(Done(2)));

        // Then dictation has stopped, keeping every word heard
        Assert.True(chat.IsEnded);
        Assert.False(voice.IsDictating);
        Assert.False(_microphone.IsOpen);
        Assert.Equal("Also the export fails", chat.Draft);
    }

    [Fact]
    public void Dictation_stops_as_soon_as_a_typed_message_comes_back_into_the_message_box()
    {
        // Given a typed message is on its way while the reporter dictates
        TaskCompletionSource<IntakeConversationStarted> start = new();
        IntakeChatViewModel chat = Chat(new FakeIntakeConversations().Answer("startIntakeConversation", start.Task));
        IntakeVoiceViewModel voice = VoiceOf(chat);
        chat.Draft = "It crashes.";
        chat.SendCommand.Execute(null);
        voice.ToggleDictationCommand.Execute(null);
        Say("Also");

        // When the send fails and nothing more is heard
        start.SetException(ApiError(500, "InternalError"));

        // Then
        Assert.False(voice.IsDictating);
        Assert.False(_microphone.IsOpen);
        Assert.Equal("It crashes." + Environment.NewLine + "Also", chat.Draft);
        Assert.True(chat.SendCommand.CanExecute(null));
    }

    [Fact]
    public void Voice_mode_does_not_send_while_words_of_the_previous_session_are_still_unsettled()
    {
        // Given the session rotated while the reporter spoke
        IntakeChatViewModel chat = Chat(RepliesTo("The export fails since Monday."));
        IntakeVoiceViewModel voice = VoiceOf(chat);
        voice.TalkCommand.Execute(null);
        FakeTranscribeSocket previous = Transcribe;
        previous.Hear(("a", "The export fails", true));
        _delays.Elapse(TimeSpan.FromSeconds(57));
        FakeTranscribeSocket current = Transcribe;
        Assert.NotSame(previous, current);

        // When the new session finishes its phrase before the old one settles
        current.Hear(("b", "since Monday.", false));

        // Then nothing is sent until the old phrase settles, and the words keep their order
        Assert.DoesNotContain(Pause, _delays.Pending);
        Assert.Equal("The export fails since Monday.", voice.Heard);
        previous.Hear(("a", "The export fails", false));
        _delays.Elapse(Pause);
        _delays.Elapse(Drain);
        Assert.Equal("The export fails since Monday.", chat.Messages[0].Text);
    }

    [Fact]
    public void Voice_mode_waits_for_the_previous_session_to_drain_even_before_it_reported_anything()
    {
        // Given the session rotated right after the reporter's first words
        IntakeChatViewModel chat = Chat(RepliesTo("The export fails since Monday."));
        IntakeVoiceViewModel voice = VoiceOf(chat);
        voice.TalkCommand.Execute(null);
        FakeTranscribeSocket previous = Transcribe;
        _delays.Elapse(TimeSpan.FromSeconds(57));
        FakeTranscribeSocket current = Transcribe;

        // When the new session finishes a phrase and the pause runs out before the old session reported
        current.Hear(("b", "since Monday.", false));
        _delays.Elapse(Pause);

        // Then nothing is sent yet
        Assert.Empty(chat.Messages);
        Assert.Equal(VoicePhase.Listening, voice.Phase);

        // When the old session delivers its words and its stream ends
        previous.Hear(("a", "The export fails", false));
        _delays.Elapse(Pause);
        _delays.Elapse(Drain);

        // Then everything is sent, in the order it was spoken
        Assert.Equal("The export fails since Monday.", chat.Messages[0].Text);
    }

    [Fact]
    public void Stopping_dictation_during_a_rotation_keeps_the_order_of_the_words()
    {
        // Given the old session still revises its phrase while the new one finished the next
        IntakeChatViewModel chat = Chat(new FakeIntakeConversations());
        IntakeVoiceViewModel voice = VoiceOf(chat);
        chat.Draft = "Invoices:";
        voice.ToggleDictationCommand.Execute(null);
        FakeTranscribeSocket previous = Transcribe;
        previous.Hear(("a", "the export fails", true));
        _delays.Elapse(TimeSpan.FromSeconds(57));
        Transcribe.Hear(("b", "since Monday.", false));

        // When
        voice.ToggleDictationCommand.Execute(null);

        // Then
        Assert.Equal("Invoices: the export fails since Monday.", chat.Draft);
    }

    [Fact]
    public void A_402_for_speech_ends_voice_mode_with_the_NevaBridge_message_and_keeps_voice_offered()
    {
        // Given
        _voice.AnswerSpeech(new NevaBridgeApiException(402, "QuotaExceeded", null, "Your organization has no credits left."));
        IntakeChatViewModel chat = Chat(RepliesTo("It crashes."));
        IntakeVoiceViewModel voice = VoiceOf(chat);
        voice.TalkCommand.Execute(null);

        // When
        SayAndPause("It crashes.");

        // Then
        Assert.Equal(VoicePhase.Off, voice.Phase);
        Assert.Equal("Your organization has no credits left.", voice.Notice);
        Assert.True(voice.OffersHandsFree);
        Assert.True(voice.TalkCommand.CanExecute(null));
        Assert.Empty(_player.Started);
    }

    [Fact]
    public void A_402_for_the_session_ends_dictation_with_the_NevaBridge_message()
    {
        // Given
        _voice.AnswerSession(new NevaBridgeApiException(402, "QuotaExceeded", null, "Your organization has no credits left."));
        IntakeVoiceViewModel voice = VoiceOf(Chat(new FakeIntakeConversations()));

        // When
        voice.ToggleDictationCommand.Execute(null);

        // Then
        Assert.False(voice.IsDictating);
        Assert.Equal("Your organization has no credits left.", voice.Notice);
        Assert.True(voice.OffersDictation);
    }

    [Fact]
    public void A_sentence_Polly_could_not_read_is_read_past_and_voice_mode_listens_again_after_the_turn()
    {
        // Given
        _voice.AnswerSpeech(new NevaBridgeApiException(424, "FailedDependency", null, "Amazon Polly could not synthesize the text."));
        IntakeChatViewModel chat = Chat(RepliesTo("It crashes."));
        IntakeVoiceViewModel voice = VoiceOf(chat);
        voice.TalkCommand.Execute(null);

        // When
        SayAndPause("It crashes.");

        // Then
        Assert.Equal(IntakeChatText.SpeechFailed, voice.Notice);
        Assert.Empty(_player.Started);
        Assert.Equal(VoicePhase.Listening, voice.Phase);
        Assert.True(_microphone.IsOpen);
        Assert.Equal("Thanks. When does it crash? Since when?", chat.Messages[^1].Text);
    }

    [Fact]
    public void A_playback_failure_is_read_past_too()
    {
        // Given
        IntakeChatViewModel chat = Chat(RepliesTo("It crashes."));
        IntakeVoiceViewModel voice = VoiceOf(chat);
        voice.TalkCommand.Execute(null);
        SayAndPause("It crashes.");

        // When
        _player.FailPlayback();

        // Then
        Assert.Equal(IntakeChatText.SpeechFailed, voice.Notice);
        Assert.Equal(["Thanks."], _player.Started);
        Assert.Equal(VoicePhase.Listening, voice.Phase);
    }

    [Fact]
    public void Recognition_stopping_in_voice_mode_puts_what_was_heard_into_the_message_box_with_a_notice()
    {
        // Given
        IntakeChatViewModel chat = Chat(new FakeIntakeConversations());
        IntakeVoiceViewModel voice = VoiceOf(chat);
        voice.TalkCommand.Execute(null);
        Say("The export fails");

        // When
        Transcribe.CloseFromTranscribe("Your request timed out.");

        // Then
        Assert.Equal(VoicePhase.Off, voice.Phase);
        Assert.Equal("The export fails", chat.Draft);
        Assert.Equal(IntakeChatText.RecognitionStoppedTalk, voice.Notice);
        Assert.True(voice.TalkCommand.CanExecute(null));
    }

    [Fact]
    public void Recognition_stopping_during_dictation_keeps_the_words_with_a_notice()
    {
        // Given
        IntakeChatViewModel chat = Chat(new FakeIntakeConversations());
        IntakeVoiceViewModel voice = VoiceOf(chat);
        voice.ToggleDictationCommand.Execute(null);
        Say("The export");
        Transcribe.Hear(("x", "fails", true));

        // When
        Transcribe.Fail("InternalFailureException", "Something went wrong.");

        // Then
        Assert.False(voice.IsDictating);
        Assert.Equal("The export fails", chat.Draft);
        Assert.Equal(IntakeChatText.RecognitionStoppedDictate, voice.Notice);
    }

    [Fact]
    public void A_missing_microphone_says_so_and_keeps_voice_offered()
    {
        // Given
        _microphone.Refusal = new MicrophoneUnavailableException("Access denied.");
        IntakeVoiceViewModel voice = VoiceOf(Chat(new FakeIntakeConversations()));

        // When
        voice.TalkCommand.Execute(null);

        // Then
        Assert.Equal(VoicePhase.Off, voice.Phase);
        Assert.Equal(IntakeChatText.MicrophoneUnavailable, voice.Notice);
        Assert.True(voice.TalkCommand.CanExecute(null));
        Assert.Empty(_voice.SessionRequests);
    }

    [Fact]
    public void No_notice_names_the_presigned_address_when_the_connection_fails()
    {
        // Given
        IntakeChatViewModel chat = new(new FakeIntakeConversations(), "product-1", Reporter, new IntakeChatOptions
        {
            Voice = new IntakeChatVoice(_voice, _microphone, _player)
            {
                Dictation = true,
                CreateSocket = () => new FakeTranscribeSocket
                {
                    ConnectFailure = new System.Net.WebSockets.WebSocketException($"Unable to connect to {FakeVoice.PresignedUrl}"),
                },
            },
            Delay = _delays.Delay,
        });
        IntakeVoiceViewModel voice = VoiceOf(chat);

        // When
        voice.ToggleDictationCommand.Execute(null);

        // Then
        Assert.NotNull(voice.Notice);
        Assert.DoesNotContain("X-Amz", voice.Notice);
        Assert.DoesNotContain("X-Amz", chat.ErrorText ?? string.Empty);
    }

    [Fact]
    public void A_conversation_that_closes_is_read_to_its_end_and_then_voice_mode_ends()
    {
        // Given
        FakeIntakeConversations nevaBridge = new FakeIntakeConversations()
            .Answer("startIntakeConversation", new IntakeConversationStarted(ConversationId, TurnId))
            .Answer("getIntakeTurnEvents", Events(Text("Passed on. Goodbye."), Done(1)))
            .Answer("getConversation", Conversation(1, [("user", "That's all."), ("assistant", "Passed on. Goodbye.")], status: "closed"));
        IntakeChatViewModel chat = Chat(nevaBridge);
        IntakeVoiceViewModel voice = VoiceOf(chat);
        voice.TalkCommand.Execute(null);
        SayAndPause("That's all.");

        // When
        _player.Finish();
        _player.Finish();

        // Then
        Assert.Equal(["Passed on.", "Goodbye."], _player.Started);
        Assert.Equal(VoicePhase.Off, voice.Phase);
        Assert.Equal(IntakeChatText.VoiceModeClosed, voice.Notice);
        Assert.False(_microphone.IsOpen);
        Assert.Single(_voice.SessionRequests);
    }

    [Fact]
    public void Closing_the_window_while_reading_stops_quietly()
    {
        // Given
        IntakeChatViewModel chat = Chat(RepliesTo("It crashes."));
        IntakeVoiceViewModel voice = VoiceOf(chat);
        chat.Draft = "typed";
        voice.TalkCommand.Execute(null);
        SayAndPause("It crashes.");

        // When
        chat.Dispose();

        // Then
        Assert.Equal(["Thanks."], _player.Stopped);
        Assert.Equal("typed", chat.Draft);
        Assert.Null(voice.Notice);
        Assert.False(_microphone.IsOpen);
    }

    [Fact]
    public void Closing_the_window_while_listening_discards_the_heard_words()
    {
        // Given
        IntakeChatViewModel chat = Chat(new FakeIntakeConversations());
        IntakeVoiceViewModel voice = VoiceOf(chat);
        voice.TalkCommand.Execute(null);
        Say("The export fails");

        // When
        chat.Dispose();

        // Then
        Assert.DoesNotContain(Pause, _delays.Pending);
        Assert.Equal(string.Empty, chat.Draft);
        Assert.False(_microphone.IsOpen);
        Assert.Empty(chat.Messages);
    }

    [Fact]
    public void The_language_follows_the_host_else_the_Windows_UI_language_and_is_locked_while_voice_runs()
    {
        // Given
        CultureInfo previous = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = new CultureInfo("de-AT");
            IntakeChatViewModel byCulture = new(new FakeIntakeConversations(), "product-1", Reporter, new IntakeChatOptions
            {
                Voice = new IntakeChatVoice(_voice, _microphone, _player) { Dictation = true },
            });
            Assert.Equal(VoiceLanguage.German, VoiceOf(byCulture).Language);
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }

        IntakeVoiceViewModel voice = VoiceOf(Chat(new FakeIntakeConversations(), language: VoiceLanguage.English));
        Assert.Equal(VoiceLanguage.English, voice.Language);

        // When
        voice.Language = VoiceLanguage.German;
        voice.ToggleDictationCommand.Execute(null);
        voice.Language = VoiceLanguage.English;

        // Then
        Assert.Equal(VoiceLanguage.German, voice.Language);
        Assert.Equal([VoiceLanguage.German], _voice.SessionRequests);
    }

    [Fact]
    public void Talk_waits_while_a_typed_message_is_on_its_way()
    {
        // Given
        TaskCompletionSource<IntakeConversationStarted> start = new();
        IntakeChatViewModel chat = Chat(new FakeIntakeConversations().Answer("startIntakeConversation", start.Task));
        IntakeVoiceViewModel voice = VoiceOf(chat);
        chat.Draft = "It crashes.";

        // When
        chat.SendCommand.Execute(null);

        // Then
        Assert.False(voice.TalkCommand.CanExecute(null));
        Assert.True(voice.ToggleDictationCommand.CanExecute(null));
        start.SetException(ApiError(500, "InternalError"));
    }
}
