using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace NevaBridge.Chat;

/// <summary>Where the hands-free conversation is.</summary>
public enum VoicePhase
{
    /// <summary>Voice mode is off; the composer is shown.</summary>
    Off,

    /// <summary>The microphone is open; a pause after the last phrase sends what was heard.</summary>
    Listening,

    /// <summary>The spoken message is on its way; the microphone is closed.</summary>
    Waiting,

    /// <summary>The reply is being read aloud; the microphone is closed.</summary>
    Speaking,
}

/// <summary>
/// Dictation and the hands-free conversation of one <see cref="IntakeChatViewModel"/>, which owns
/// it as <see cref="IntakeChatViewModel.Voice"/>. Spoken messages go through the chat's own send
/// path, so they follow the same rules as typed ones.
/// </summary>
/// <remarks>The rules it keeps are listed in <c>widgets/wpf/docs/intake-chat-state.md</c>.</remarks>
public sealed class IntakeVoiceViewModel : INotifyPropertyChanged
{
    /// <summary>Silence after the last finished phrase that sends it in voice mode.</summary>
    internal static readonly TimeSpan EndOfTurnPause = TimeSpan.FromSeconds(1.2);

    private readonly IntakeChatViewModel _chat;
    private readonly IntakeChatVoice _voice;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly CancellationToken _closing;

    private VoiceLanguage _language;
    private bool _voiceOff;
    private bool _isDictating;
    private VoicePhase _phase;
    private bool _awaitingMicrophone;
    private string _heard = string.Empty;
    private string _interim = string.Empty;
    private string? _notice;

    private TranscribeListener? _listener;
    private Transcript _transcript = Transcript.Empty;
    private CancellationTokenSource? _pause;

    // The pause ran out while a rotated-out session drained; send once it has.
    private bool _sendWhenDrained;
    private SpokenReply? _reply;

    // Dictation: the composer text it started from, and what it last wrote there.
    private string _dictationBase = string.Empty;
    private string _dictationWritten = string.Empty;

    internal IntakeVoiceViewModel(
        IntakeChatViewModel chat,
        IntakeChatVoice voice,
        VoiceLanguage language,
        Func<TimeSpan, CancellationToken, Task> delay,
        CancellationToken closing)
    {
        _chat = chat;
        _voice = voice;
        _language = language;
        _delay = delay;
        _closing = closing;
        _chat.PropertyChanged += OnChatChanged;
        ToggleDictationCommand = Command(ToggleDictation, () => IsDictating || CanDictate);
        TalkCommand = Command(StartVoiceMode, () => CanTalk);
        SendNowCommand = Command(SendHeard, () => Phase == VoicePhase.Listening && Heard.Trim().Length > 0);
        StopReadingCommand = Command(StopReading, () => Phase == VoicePhase.Speaking);
        EndVoiceModeCommand = Command(EndVoiceMode, () => Phase != VoicePhase.Off);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Dictation stopped; the view returns focus to the message box, at its end.</summary>
    public event EventHandler? DictationEnded;

    /// <summary>Starts dictation, or stops it keeping every word heard.</summary>
    public AsyncCommand ToggleDictationCommand { get; }

    /// <summary>Starts the hands-free conversation.</summary>
    public AsyncCommand TalkCommand { get; }

    /// <summary>Sends what was heard without waiting for the pause.</summary>
    public AsyncCommand SendNowCommand { get; }

    /// <summary>Stops reading this reply; listening resumes once the turn is done.</summary>
    public AsyncCommand StopReadingCommand { get; }

    /// <summary>Ends voice mode; words heard but not sent go into the message box.</summary>
    public AsyncCommand EndVoiceModeCommand { get; }

    public bool OffersDictation => _voice.Dictation && !_voiceOff;

    public bool OffersHandsFree => _voice.HandsFree && !_voiceOff;

    /// <summary>Either voice feature is offered, so the voice controls are shown.</summary>
    public bool OffersVoice => OffersDictation || OffersHandsFree;

    public IReadOnlyList<VoiceLanguage> Languages => VoiceLanguage.All;

    /// <summary>The language voice listens and speaks in; it changes only while voice is idle.</summary>
    public VoiceLanguage Language
    {
        get => _language;
        set
        {
            if (CanChooseLanguage)
            {
                Set(ref _language, value);
            }
            else
            {
                // Tell the view to show the language that still holds.
                OnPropertyChanged();
            }
        }
    }

    public bool CanChooseLanguage => !HoldsComposer;

    public bool IsDictating
    {
        get => _isDictating;
        private set
        {
            if (Set(ref _isDictating, value))
            {
                ComposerChanged();
            }
        }
    }

    public VoicePhase Phase
    {
        get => _phase;
        private set
        {
            if (Set(ref _phase, value))
            {
                OnPropertyChanged(nameof(IsVoiceModeOn));
                OnPropertyChanged(nameof(IsListening));
                OnPropertyChanged(nameof(IsSpeaking));
                OnPropertyChanged(nameof(PhaseText));
                ComposerChanged();
            }
        }
    }

    /// <summary>The voice panel replaces the composer.</summary>
    public bool IsVoiceModeOn => Phase != VoicePhase.Off;

    public bool IsListening => Phase == VoicePhase.Listening;

    public bool IsSpeaking => Phase == VoicePhase.Speaking;

    /// <summary>Listening has started but the microphone is not open yet.</summary>
    public bool AwaitingMicrophone
    {
        get => _awaitingMicrophone;
        private set
        {
            if (Set(ref _awaitingMicrophone, value))
            {
                OnPropertyChanged(nameof(PhaseText));
            }
        }
    }

    public string? PhaseText => Phase switch
    {
        VoicePhase.Listening => AwaitingMicrophone ? IntakeChatText.OpeningMicrophone : IntakeChatText.Listening,
        VoicePhase.Waiting => IntakeChatText.WaitingForReply,
        VoicePhase.Speaking => IntakeChatText.ReadingAloud,
        _ => null,
    };

    /// <summary>Everything heard in this listening turn of voice mode, as it will be sent.</summary>
    public string Heard
    {
        get => _heard;
        private set
        {
            if (Set(ref _heard, value))
            {
                SendNowCommand.RaiseCanExecuteChanged();
            }
        }
    }

    /// <summary>Words still being recognized, shown apart until they settle.</summary>
    public string Interim
    {
        get => _interim;
        private set => Set(ref _interim, value);
    }

    /// <summary>Why voice stopped or what went wrong.</summary>
    public string? Notice
    {
        get => _notice;
        private set => Set(ref _notice, value);
    }

    /// <summary>Dictation or voice mode holds the composer: Send and Finish wait, the language is locked.</summary>
    internal bool HoldsComposer => IsDictating || Phase != VoicePhase.Off;

    private bool CanDictate => OffersDictation && _chat.IsOpen && Phase == VoicePhase.Off;

    private bool CanTalk =>
        OffersHandsFree && _chat.IsOpen && !_chat.IsBusy && !_chat.CanCheckAgain && !IsDictating && Phase == VoicePhase.Off;

    /// <summary>The window closed: stop listening and reading quietly, leaving the composer alone.</summary>
    internal void Close()
    {
        CancelPause();
        _listener?.Stop();
        _listener = null;
        SpokenReply? reply = _reply;
        _reply = null;
        reply?.Reader?.Stop();
    }

    internal void RaiseCommands()
    {
        ToggleDictationCommand.RaiseCanExecuteChanged();
        TalkCommand.RaiseCanExecuteChanged();
        SendNowCommand.RaiseCanExecuteChanged();
        StopReadingCommand.RaiseCanExecuteChanged();
        EndVoiceModeCommand.RaiseCanExecuteChanged();
    }

    // Dictation

    private void ToggleDictation()
    {
        if (IsDictating)
        {
            StopDictation();
        }
        else if (CanDictate)
        {
            StartDictation();
        }
    }

    private void StartDictation()
    {
        Notice = null;
        _dictationBase = _chat.Draft.TrimEnd();
        _dictationWritten = _chat.Draft;
        _transcript = Transcript.Empty;
        Interim = string.Empty;
        IsDictating = true;
        Listen(OnDictationResults, OnDictationFailed);
    }

    private void OnDictationResults(IReadOnlyList<TranscriptResult> results)
    {
        // A change to the composer dictation did not make, such as a typed message the chat put
        // back, stops dictation, so the next result cannot overwrite it.
        if (_chat.Draft != _dictationWritten)
        {
            StopDictation();
            return;
        }

        _transcript = _transcript.Apply(results);
        if (_transcript.FinalText.Length > 0)
        {
            _dictationWritten = AppendWords(_dictationBase, _transcript.FinalText);
            _chat.Draft = _dictationWritten;
        }

        Interim = _transcript.InterimText;
    }

    /// <summary>Ends dictation, keeping every word heard, the unsettled ones too.</summary>
    private void StopDictation()
    {
        if (!IsDictating)
        {
            return;
        }

        // Read before stopping. Not dictating before the composer changes below, which would
        // otherwise stop it again.
        string unsettled = Interim;
        string heard = _transcript.Text;
        bool untouched = _chat.Draft == _dictationWritten;
        IsDictating = false;
        StopListening();
        Interim = string.Empty;
        if (untouched)
        {
            // Rebuilt from the transcript, so words of overlapping sessions keep their order.
            if (heard.Length > 0)
            {
                _chat.Draft = AppendWords(_dictationBase, heard);
            }
        }
        else if (unsettled.Length > 0)
        {
            _chat.Draft = AppendWords(_chat.Draft.TrimEnd(), unsettled);
        }

        DictationEnded?.Invoke(this, EventArgs.Empty);
    }

    private void OnDictationFailed(Exception error)
    {
        StopDictation();
        ShowFailure(error, IntakeChatText.RecognitionStoppedDictate);
    }

    // The hands-free conversation

    private void StartVoiceMode()
    {
        if (!CanTalk)
        {
            return;
        }

        Notice = null;
        Phase = VoicePhase.Listening;
        ListenForTurn();
    }

    private void ListenForTurn()
    {
        _transcript = Transcript.Empty;
        Heard = string.Empty;
        Interim = string.Empty;
        Listen(OnVoiceResults, OnVoiceListeningFailed);
    }

    private void OnVoiceResults(IReadOnlyList<TranscriptResult> results)
    {
        if (Phase != VoicePhase.Listening)
        {
            return;
        }

        _transcript = _transcript.Apply(results);
        Heard = _transcript.Text;
        Interim = _transcript.InterimText;
        CancelPause();

        // A drained session's words may still settle before the pause may send.
        if (!results[^1].IsPartial && !_transcript.IsUnsettled)
        {
            _pause = CancellationTokenSource.CreateLinkedTokenSource(_closing);
            _ = SendAfterPauseAsync(_pause.Token);
        }
    }

    private async Task SendAfterPauseAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _delay(EndOfTurnPause, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (_listener is { IsDraining: true })
        {
            // A rotated-out session may still deliver earlier words; they must not be lost.
            _sendWhenDrained = true;
            return;
        }

        SendHeard();
    }

    private void OnDrained()
    {
        if (_sendWhenDrained)
        {
            _sendWhenDrained = false;
            SendHeard();
        }
    }

    /// <summary>Sends what was heard as an ordinary turn; the microphone closes until the reply is read.</summary>
    private void SendHeard()
    {
        string text = Heard.Trim();
        if (Phase != VoicePhase.Listening || text.Length == 0)
        {
            return;
        }

        StopListening();
        Phase = VoicePhase.Waiting;
        SpokenReply reply = new();
        _reply = reply;
        _ = SendSpokenAsync(reply, text);
    }

    private async Task SendSpokenAsync(SpokenReply reply, string text)
    {
        SpokenSendOutcome outcome = await _chat.SendSpokenAsync(text);
        if (reply != _reply)
        {
            return;
        }

        switch (outcome)
        {
            case SpokenSendOutcome.Saved:
                // The live text is a preview: read what the saved reply adds to it, unless the
                // saved reply does not continue what was handed over, or reading was stopped.
                if (_chat.LatestReply is { } saved && saved.StartsWith(reply.HandedOver, StringComparison.Ordinal))
                {
                    Speak(reply, [.. reply.Splitter.Push(saved[reply.HandedOver.Length..]), .. reply.Splitter.Flush()]);
                }

                reply.TurnDone = true;
                reply.Reader?.End();
                FinishIfDone(reply);
                break;
            case SpokenSendOutcome.Kept:
                // The chat put the words back into the composer and says why.
                EndVoiceMode(IntakeChatText.SpokenNotSent, keepHeard: false);
                break;
            case SpokenSendOutcome.Unconfirmed:
                // The chat offers Check again for the message; voice mode cannot go on without its reply.
                EndVoiceMode(notice: null, keepHeard: false);
                break;
        }
    }

    private void OnChatChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Dictation ends when the conversation closes, and when the composer changes in a way
        // dictation did not make, such as a typed message the chat put back.
        if (IsDictating
            && ((e.PropertyName == nameof(IntakeChatViewModel.IsOpen) && !_chat.IsOpen)
                || (e.PropertyName == nameof(IntakeChatViewModel.Draft) && _chat.Draft != _dictationWritten)))
        {
            StopDictation();
            return;
        }

        if (e.PropertyName != nameof(IntakeChatViewModel.LiveReply)
            || _reply is not { TurnDone: false } reply
            || _chat.LiveReply is not { } live
            || !live.StartsWith(reply.HandedOver, StringComparison.Ordinal))
        {
            return;
        }

        string delta = live[reply.HandedOver.Length..];
        if (delta.Length == 0)
        {
            return;
        }

        reply.HandedOver = live;
        Speak(reply, reply.Splitter.Push(delta));
    }

    private void Speak(SpokenReply reply, IReadOnlyList<string> sentences)
    {
        if (sentences.Count == 0 || reply.Stopped)
        {
            return;
        }

        if (reply.Reader is null)
        {
            reply.Reader = new ReplyReader(
                _voice.Api,
                Language,
                _voice.Player,
                onSentence: _ => { },
                onIdle: () =>
                {
                    reply.SpeechDone = true;
                    FinishIfDone(reply);
                },
                onError: error => OnSpeechFailed(reply, error),
                _closing);
            Phase = VoicePhase.Speaking;
        }

        foreach (string sentence in sentences)
        {
            reply.Reader.Enqueue(sentence);
        }
    }

    private void OnSpeechFailed(SpokenReply reply, Exception error)
    {
        if (reply != _reply)
        {
            return;
        }

        if (error is NevaBridgeApiException { StatusCode: 403 })
        {
            TurnVoiceOff();
            return;
        }

        if (error is NevaBridgeApiException { StatusCode: 402 } credits)
        {
            EndVoiceMode(credits.Message, keepHeard: false);
            return;
        }

        // The reply is in the transcript: read past it and listen again once the turn is done.
        Notice = IntakeChatText.SpeechFailed;
        reply.Stopped = true;
        reply.SpeechDone = true;
        FinishIfDone(reply);
    }

    private void StopReading()
    {
        if (_reply is not { } reply)
        {
            return;
        }

        reply.Stopped = true;
        reply.Reader?.Stop();
    }

    /// <summary>
    /// The turn is over for the voice loop once it is settled and its reply was read or stopped;
    /// listening before that would race the pending turn.
    /// </summary>
    private void FinishIfDone(SpokenReply reply)
    {
        if (reply != _reply || !reply.TurnDone || (reply.Reader is not null && !reply.SpeechDone))
        {
            return;
        }

        _reply = null;
        if (!_chat.IsOpen)
        {
            EndVoiceMode(IntakeChatText.VoiceModeClosed, keepHeard: false);
            return;
        }

        Phase = VoicePhase.Listening;
        ListenForTurn();
    }

    private void OnVoiceListeningFailed(Exception error)
    {
        EndVoiceMode(notice: null, keepHeard: true);
        ShowFailure(error, IntakeChatText.RecognitionStoppedTalk);
    }

    private void EndVoiceMode() => EndVoiceMode(notice: null, keepHeard: true);

    /// <summary>
    /// Ends voice mode. Words heard but not sent go into the message box, on their own line after
    /// the typed text, when <paramref name="keepHeard"/> is set.
    /// </summary>
    private void EndVoiceMode(string? notice, bool keepHeard)
    {
        if (Phase == VoicePhase.Off)
        {
            return;
        }

        string unsent = Phase == VoicePhase.Listening ? Heard.Trim() : string.Empty;
        StopListening();
        SpokenReply? reply = _reply;
        _reply = null;
        reply?.Reader?.Stop();
        if (keepHeard && unsent.Length > 0)
        {
            string typed = _chat.Draft.TrimEnd();
            _chat.Draft = typed.Length == 0 ? unsent : typed + Environment.NewLine + unsent;
        }

        Heard = string.Empty;
        Interim = string.Empty;
        Phase = VoicePhase.Off;
        Notice = notice;
    }

    // Listening, shared by both

    private void Listen(Action<IReadOnlyList<TranscriptResult>> onResults, Action<Exception> onFailed)
    {
        TranscribeListener listener = new(
            _voice.Api,
            Language,
            _voice.Microphone,
            new TranscribeListenerOptions { CreateSocket = _voice.CreateSocket, Delay = _delay });
        _listener = listener;
        AwaitingMicrophone = true;
        _ = StartListeningAsync(listener, onResults, onFailed);
    }

    private async Task StartListeningAsync(
        TranscribeListener listener,
        Action<IReadOnlyList<TranscriptResult>> onResults,
        Action<Exception> onFailed)
    {
        TranscribeCallbacks callbacks = new(
            results =>
            {
                if (listener == _listener)
                {
                    onResults(results);
                }
            },
            error =>
            {
                if (listener == _listener)
                {
                    _listener = null;
                    onFailed(error);
                }
            })
        {
            OnMicrophoneOpen = () =>
            {
                if (listener == _listener)
                {
                    AwaitingMicrophone = false;
                }
            },
            OnDrained = () =>
            {
                if (listener == _listener)
                {
                    OnDrained();
                }
            },
        };

        try
        {
            await listener.StartAsync(callbacks, _closing);
        }
        catch (Exception error) when (listener == _listener && !_closing.IsCancellationRequested)
        {
            _listener = null;
            onFailed(error);
        }
        catch (Exception)
        {
            // Stopped before it started: voice moved on or the window closed.
        }
    }

    private void StopListening()
    {
        CancelPause();
        _listener?.Stop();
        _listener = null;
        AwaitingMicrophone = false;
    }

    private void CancelPause()
    {
        _pause?.Cancel();
        _pause = null;
        _sendWhenDrained = false;
    }

    /// <summary>Says why listening stopped. 403 switches voice off for this chat.</summary>
    private void ShowFailure(Exception error, string recognitionStopped)
    {
        Notice = error switch
        {
            MicrophoneUnavailableException => IntakeChatText.MicrophoneUnavailable,
            NevaBridgeApiException { StatusCode: 402 } credits => credits.Message,
            NevaBridgeApiException { StatusCode: 403 } => null,
            NevaBridgeApiException or HttpRequestException or TaskCanceledException => ChatErrors.Describe(error),
            _ => recognitionStopped,
        };
        if (error is NevaBridgeApiException { StatusCode: 403 })
        {
            TurnVoiceOff();
        }
    }

    /// <summary>NevaBridge refused a voice call: voice is off for this chat, and no voice call follows.</summary>
    private void TurnVoiceOff()
    {
        _voiceOff = true;
        StopDictation();
        EndVoiceMode(notice: null, keepHeard: true);
        Notice = IntakeChatText.VoiceOff;
        OnPropertyChanged(nameof(OffersDictation));
        OnPropertyChanged(nameof(OffersHandsFree));
        OnPropertyChanged(nameof(OffersVoice));
        RaiseCommands();
    }

    private void ComposerChanged()
    {
        OnPropertyChanged(nameof(CanChooseLanguage));
        RaiseCommands();
        _chat.RaiseComposerChanged();
    }

    private static string AppendWords(string text, string words) =>
        words.Length == 0 ? text : text.Length == 0 ? words : text + " " + words;

    private static AsyncCommand Command(Action action, Func<bool> canExecute) =>
        new(() =>
        {
            action();
            return Task.CompletedTask;
        }, canExecute);

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(name);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    /// <summary>The reply to one spoken message, read aloud sentence by sentence.</summary>
    private sealed class SpokenReply
    {
        public SentenceSplitter Splitter { get; } = new();

        /// <summary>Live reply text already handed to the splitter.</summary>
        public string HandedOver { get; set; } = string.Empty;

        public ReplyReader? Reader { get; set; }

        /// <summary>Stop reading, or a sentence that failed: nothing more of this reply is read.</summary>
        public bool Stopped { get; set; }

        public bool SpeechDone { get; set; }

        public bool TurnDone { get; set; }
    }
}
