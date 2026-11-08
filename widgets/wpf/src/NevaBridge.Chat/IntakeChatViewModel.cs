using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace NevaBridge.Chat;

/// <summary>A report a finished Generic Intake conversation filed, as the chat lists it.</summary>
public sealed record FiledReportItem(string Title, string Category, string Status, string? Reason);

/// <summary>Where a file of the chat's list stands.</summary>
public enum AttachmentState
{
    /// <summary>The file is on its way to NevaBridge.</summary>
    Uploading,

    /// <summary>NevaBridge holds the file; it can be removed.</summary>
    Uploaded,

    /// <summary>Storage refused the bytes, so nothing was attached; it can be retried or dismissed.</summary>
    Failed,
}

/// <summary>A file in the chat's list, with the commands its state offers.</summary>
public sealed record AttachmentItem(string FileName, AttachmentState State)
{
    /// <summary>The attachment NevaBridge holds; set only when <see cref="State"/> is Uploaded.</summary>
    public Attachment? Attachment { get; init; }

    /// <summary>Removes an uploaded file; it can run only for one.</summary>
    public required AsyncCommand RemoveCommand { get; init; }

    /// <summary>Uploads a failed file again; it can run only for one.</summary>
    public required AsyncCommand RetryCommand { get; init; }

    /// <summary>Drops a failed file from the list; it can run only for one.</summary>
    public required AsyncCommand DismissCommand { get; init; }

    /// <summary>The admission a failed file was uploaded to, when the chat holds one.</summary>
    internal AttachmentUpload? Admission { get; init; }

    public bool IsUploading => State == AttachmentState.Uploading;

    public bool IsUploaded => State == AttachmentState.Uploaded;

    public bool IsFailed => State == AttachmentState.Failed;
}

/// <summary>What the host supplies beyond the reporter, all optional.</summary>
public sealed record IntakeChatOptions
{
    /// <summary>The application's own context, stored with the conversation and shown in triage.</summary>
    public IReadOnlyDictionary<string, string>? ApplicationContext { get; init; }

    /// <summary>How the team can reach the reporter; the assistant then does not ask.</summary>
    public ReporterContact? ReporterContact { get; init; }

    /// <summary>
    /// Shows a file picker and returns the chosen file, or null when the user cancels. Leave it
    /// null when attachments are off, in your configuration or for the tenant
    /// (<c>chatAttachments</c>); the chat then offers none.
    /// </summary>
    public Func<Task<PickedFile?>>? PickFile { get; init; }

    /// <summary>
    /// Dictation and the hands-free conversation. Leave it null when voice is off, in your
    /// configuration or for the tenant (<c>voiceDictation</c>, <c>voiceConversation</c>); the
    /// chat then offers none.
    /// </summary>
    public IntakeChatVoice? Voice { get; init; }

    /// <summary>
    /// The language voice listens and speaks in at first; the reporter can change it. Null follows
    /// <see cref="System.Globalization.CultureInfo.CurrentUICulture"/>.
    /// </summary>
    public VoiceLanguage? VoiceLanguage { get; init; }

    /// <summary>Waits between retries of a failed poll and times voice. Replaceable so tests need not wait.</summary>
    public Func<TimeSpan, CancellationToken, Task> Delay { get; init; } = Task.Delay;
}

/// <summary>What the chat needs to offer voice, and which of the two voice features it offers.</summary>
/// <param name="Api">NevaBridge's voice operations.</param>
/// <param name="Microphone">The microphone to listen through.</param>
/// <param name="Player">Plays the reply when it is read aloud.</param>
public sealed record IntakeChatVoice(INevaBridgeVoice Api, IMicrophone Microphone, ISpeechPlayer Player)
{
    /// <summary>Offer dictation. Set it only when the tenant has <c>voiceDictation</c>.</summary>
    public bool Dictation { get; init; }

    /// <summary>Offer the hands-free conversation. Set it only when the tenant has <c>voiceConversation</c>.</summary>
    public bool HandsFree { get; init; }

    /// <summary>Creates the connection to Amazon Transcribe for each recognition session.</summary>
    public Func<ITranscribeSocket> CreateSocket { get; init; } = () => new WebSocketTranscribeSocket();
}

/// <summary>How a spoken message's send ended, for the voice loop.</summary>
internal enum SpokenSendOutcome
{
    /// <summary>The conversation saved it; its reply is <see cref="IntakeChatViewModel.LatestReply"/>.</summary>
    Saved,

    /// <summary>It was not sent; its words are back in the composer.</summary>
    Kept,

    /// <summary>Its reply could not be loaded; Check again settles it.</summary>
    Unconfirmed,

    /// <summary>The window closed.</summary>
    Closed,
}

/// <summary>
/// Drives one Generic Intake conversation for <c>NevaBridgeIntakeChatControl</c>. Every message
/// starts a turn that runs in the background; the chat follows its events until it ends, then
/// shows the conversation as saved. Finish ends the conversation, which files every report it
/// noted. Dispose it when its window closes, which stops following a turn.
/// </summary>
/// <remarks>The rules it keeps are listed in <c>widgets/wpf/docs/intake-chat-state.md</c>.</remarks>
public sealed class IntakeChatViewModel : INotifyPropertyChanged, IDisposable
{
    private const int MaximumPollRetries = 3;
    private static readonly TimeSpan PollRetryPause = TimeSpan.FromSeconds(1);

    private readonly INevaBridgeIntakeConversations _conversations;
    private readonly string _productId;
    private readonly NevaBridgeReporter _reporter;
    private readonly IntakeChatOptions _options;
    private readonly CancellationTokenSource _closing = new();

    private string? _conversationId;

    // Started with files and no message: its first turn's failure keeps it, unlike a start with a message.
    private bool _heldStart;

    // The attachment operation a message waits for, so its reply sees every picked file.
    private Task _attaching = Task.CompletedTask;
    private int _revision;
    private int _savedUserMessages;
    private IReadOnlyList<Message> _transcript = [];
    private Unconfirmed? _unconfirmed;

    private string _draft = string.Empty;
    private bool _isBusy;
    private bool _isAttaching;
    private bool _attachmentsOff;
    private bool _filesUnknown;
    private bool _refreshAfterAttaching;
    private bool _isEnded;
    private string? _progressText;
    private string? _liveReply;
    private string? _errorText;
    private string? _attachmentNotice;
    private bool _sendingSpoken;
    private SpokenSendOutcome _sendOutcome;

    public IntakeChatViewModel(
        INevaBridgeIntakeConversations conversations,
        string productId,
        NevaBridgeReporter reporter,
        IntakeChatOptions? options = null)
    {
        _conversations = conversations;
        _productId = productId;
        _reporter = reporter;
        _options = options ?? new IntakeChatOptions();
        SendCommand = new AsyncCommand(
            () => SendAsync(Draft.Trim(), finish: false, fromDraft: true),
            () => !IsBusy && !IsEnded && _unconfirmed is null && !string.IsNullOrWhiteSpace(Draft) && !VoiceHoldsComposer);
        FinishCommand = new AsyncCommand(
            () => SendAsync(IntakeChatText.FinishMessage, finish: true, fromDraft: false),
            () => CanFinish);
        CheckAgainCommand = new AsyncCommand(CheckAgainAsync, () => _unconfirmed is not null && !IsBusy);
        AttachCommand = new AsyncCommand(AttachAsync, () => CanAttach);
        RefreshFilesCommand = new AsyncCommand(
            async () =>
            {
                AttachmentNotice = null;
                await RefreshAttachmentsAsync();
            },
            () => FilesUnknown && OffersAttachments && !_isAttaching);
        if (_options.Voice is { } voice && (voice.Dictation || voice.HandsFree))
        {
            Voice = new IntakeVoiceViewModel(
                this,
                voice,
                _options.VoiceLanguage ?? VoiceLanguage.For(System.Globalization.CultureInfo.CurrentUICulture),
                _options.Delay,
                _closing.Token);
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>The transcript: the saved messages, plus the message of a turn still running.</summary>
    public ObservableCollection<ChatMessageItem> Messages { get; } = [];

    public ObservableCollection<AttachmentItem> Attachments { get; } = [];

    /// <summary>What the conversation filed, once it has ended.</summary>
    public ObservableCollection<FiledReportItem> FiledReports { get; } = [];

    /// <summary>Dictation and the hands-free conversation; null when this chat offers no voice.</summary>
    public IntakeVoiceViewModel? Voice { get; }

    public AsyncCommand SendCommand { get; }

    public AsyncCommand FinishCommand { get; }

    /// <summary>Reads again whether a reply that could not be loaded has arrived.</summary>
    public AsyncCommand CheckAgainCommand { get; }

    public AsyncCommand AttachCommand { get; }

    /// <summary>Lists the attached files again after reading them failed.</summary>
    public AsyncCommand RefreshFilesCommand { get; }

    /// <summary>Which files are attached could not be read; <see cref="RefreshFilesCommand"/> tries again.</summary>
    public bool FilesUnknown => _filesUnknown;

    public string Draft
    {
        get => _draft;
        set
        {
            if (Set(ref _draft, value))
            {
                RaiseCommands();
                OnPropertyChanged(nameof(FinishHint));
            }
        }
    }

    /// <summary>A message is on its way, or its reply is being followed.</summary>
    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (Set(ref _isBusy, value))
            {
                RaiseCommands();
            }
        }
    }

    /// <summary>The conversation has ended; it takes no more messages.</summary>
    public bool IsEnded
    {
        get => _isEnded;
        private set
        {
            if (Set(ref _isEnded, value))
            {
                OnPropertyChanged(nameof(IsOpen));
                RaiseCommands();
            }
        }
    }

    /// <summary>The conversation still takes messages.</summary>
    public bool IsOpen => !IsEnded;

    /// <summary>What the assistant is doing while a turn runs.</summary>
    public string? ProgressText
    {
        get => _progressText;
        private set => Set(ref _progressText, value);
    }

    /// <summary>The reply as it is being written; replaced by the saved reply once the turn ends.</summary>
    public string? LiveReply
    {
        get => _liveReply;
        private set => Set(ref _liveReply, value);
    }

    public string? ErrorText
    {
        get => _errorText;
        private set => Set(ref _errorText, value);
    }

    /// <summary>Why the last attachment action failed, or that attachments were switched off.</summary>
    public string? AttachmentNotice
    {
        get => _filesUnknown
            ? _attachmentNotice is null ? IntakeChatText.FilesUnknown : _attachmentNotice + " " + IntakeChatText.FilesUnknown
            : _attachmentNotice;
        private set
        {
            _attachmentNotice = value;
            OnPropertyChanged(nameof(AttachmentNotice));
        }
    }

    /// <summary>A reply could not be loaded; <see cref="CheckAgainCommand"/> looks again.</summary>
    public bool CanCheckAgain => _unconfirmed is not null;

    /// <remarks>Only after the first reply: NevaBridge refuses to finish a conversation without a message.</remarks>
    public bool CanFinish =>
        _conversationId is not null && _revision > 0 && !IsBusy && !IsEnded && !_isAttaching && _unconfirmed is null
        && string.IsNullOrWhiteSpace(Draft) && !VoiceHoldsComposer;

    /// <summary>Why Finish is unavailable while the composer holds text.</summary>
    public string? FinishHint =>
        _conversationId is not null && _revision > 0 && !IsEnded && !string.IsNullOrWhiteSpace(Draft) ? IntakeChatText.FinishBlocked : null;

    /// <summary>Whether this chat offers attachments at all; <see cref="CanAttach"/> says whether now.</summary>
    public bool OffersAttachments => _options.PickFile is not null && !_attachmentsOff;

    /// <remarks>
    /// Before the first message the first file starts the conversation. A conversation started with
    /// a message takes files only once that message is saved: a first turn that fails deletes it,
    /// and files attached to it would go with it.
    /// </remarks>
    public bool CanAttach =>
        OffersAttachments && !IsEnded && !_isAttaching
        && (_conversationId is null ? !IsBusy : _heldStart || _savedUserMessages > 0);

    public string? EndedText => IsEnded
        ? FiledReports.Count == 0 ? IntakeChatText.Ended + " " + IntakeChatText.NothingFiled : IntakeChatText.Ended
        : null;

    /// <summary>The newest saved reply, read aloud after a spoken message.</summary>
    internal string? LatestReply => _transcript.Count > 0 && _transcript[^1].Role != "user" ? _transcript[^1].Content : null;

    /// <summary>Dictation or voice mode holds the composer: Send and Finish wait.</summary>
    private bool VoiceHoldsComposer => Voice?.HoldsComposer == true;

    public void Dispose()
    {
        // Not disposed: operations still running check it once they are cancelled.
        _closing.Cancel();
        Voice?.Close();
    }

    /// <summary>
    /// Sends what the reporter said on the same path as a typed message. When it is not sent, its
    /// words go back into the composer after the text typed before voice mode.
    /// </summary>
    internal async Task<SpokenSendOutcome> SendSpokenAsync(string text)
    {
        _sendingSpoken = true;
        try
        {
            await SendAsync(text, finish: false, fromDraft: false);
            return _closing.IsCancellationRequested ? SpokenSendOutcome.Closed : _sendOutcome;
        }
        finally
        {
            _sendingSpoken = false;
        }
    }

    /// <summary>Re-checks the commands after voice took or released the composer.</summary>
    internal void RaiseComposerChanged() => RaiseCommands();

    private async Task SendAsync(string text, bool finish, bool fromDraft)
    {
        if (fromDraft)
        {
            Draft = string.Empty;
        }

        _sendOutcome = SpokenSendOutcome.Saved;
        ErrorText = null;
        IsBusy = true;
        ShowTranscript(pendingText: text);
        int savedBefore = _savedUserMessages;
        try
        {
            if (!_attaching.IsCompleted)
            {
                // The reply should see every picked file; a file that fails stays listed for Retry.
                ProgressText = IntakeChatText.WaitingForFiles;
                await _attaching.WaitAsync(_closing.Token);
            }

            if (_conversationId is null)
            {
                await StartConversationAsync(text, savedBefore);
            }
            else
            {
                await StartTurnAsync(_conversationId, text, finish, savedBefore);
            }
        }
        catch (OperationCanceledException) when (_closing.IsCancellationRequested)
        {
            // The window closed; the turn still finishes on the server.
        }
        finally
        {
            ProgressText = null;
            LiveReply = null;
            IsBusy = false;
        }
    }

    private async Task StartConversationAsync(string text, int savedBefore)
    {
        IntakeConversationStarted started;
        try
        {
            started = await _conversations.StartIntakeConversationAsync(
                _productId,
                _reporter,
                new StartIntakeConversationRequest(new UserMessage(text))
                {
                    ApplicationContext = _options.ApplicationContext,
                    ReporterContact = _options.ReporterContact,
                },
                _closing.Token);
        }
        catch (Exception error) when (error is not OperationCanceledException || !_closing.IsCancellationRequested)
        {
            // Without the answer there is no conversation to look at, so the message counts as unsent.
            FailSend(text, ChatErrors.Describe(error));
            return;
        }

        _conversationId = started.ConversationId;
        RaiseCommands();
        await FollowTurnAsync(new Unconfirmed(text, savedBefore, started.TurnId, Cursor: null, FirstTurn: true));
    }

    private async Task StartTurnAsync(string conversationId, string text, bool finish, int savedBefore)
    {
        IntakeTurnStarted started;
        try
        {
            started = await _conversations.StartIntakeTurnAsync(
                conversationId,
                new StartIntakeTurnRequest(text, _revision) { Finish = finish ? true : null },
                _closing.Token);
        }
        catch (NevaBridgeApiException error) when (error is { ErrorName: "IntakeTurnRunning", TurnId: not null })
        {
            // Another send got there first, for example one whose answer was lost: follow it.
            await FollowTurnAsync(new Unconfirmed(text, savedBefore, error.TurnId, Cursor: null, FirstTurn: false));
            return;
        }
        catch (NevaBridgeApiException error) when (error.ErrorName == "RevisionConflict")
        {
            await SettleAsync(text, savedBefore);
            return;
        }
        catch (Exception error) when (error is not OperationCanceledException || !_closing.IsCancellationRequested)
        {
            FailSend(text, ChatErrors.Describe(error));
            return;
        }

        await FollowTurnAsync(new Unconfirmed(text, savedBefore, started.TurnId, Cursor: null, FirstTurn: false));
    }

    /// <summary>Reads a turn's events until it ends, then settles the message it carried.</summary>
    private async Task FollowTurnAsync(Unconfirmed turn)
    {
        string? cursor = turn.Cursor;

        // Reading on from a cursor skips the text events before it, so their text comes along.
        LiveReply = cursor is null ? null : turn.ReplySoFar;

        ProgressText = IntakeChatText.Working;
        int retries = 0;
        while (true)
        {
            IntakeTurnEvents page;
            try
            {
                page = await _conversations.GetIntakeTurnEventsAsync(turn.TurnId!, cursor, _closing.Token);
            }
            catch (Exception error) when (IsRepeatable(error) && retries < MaximumPollRetries)
            {
                retries++;
                await _options.Delay(PollRetryPause, _closing.Token);
                continue;
            }
            catch (Exception error) when (error is not OperationCanceledException || !_closing.IsCancellationRequested)
            {
                ShowUnconfirmed(turn with { Cursor = cursor, ReplySoFar = LiveReply });
                return;
            }

            retries = 0;
            foreach (IntakeTurnEvent turnEvent in page.Events)
            {
                switch (turnEvent.Type)
                {
                    case "status":
                        ProgressText = IntakeChatText.Progress(turnEvent.Phase ?? string.Empty, turnEvent.Tool);
                        break;
                    case "text":
                        LiveReply = (LiveReply ?? string.Empty) + turnEvent.Delta;
                        break;
                    case "done":
                        _revision = turnEvent.Revision ?? _revision;
                        await SettleAsync(turn.Text, turn.SavedBefore);
                        return;
                    case "error":
                        if (turn.FirstTurn && !_heldStart)
                        {
                            // A failed first turn deletes its conversation; the next message starts a new one.
                            _conversationId = null;
                        }

                        FailSend(turn.Text, turnEvent.Error?.Message ?? IntakeChatText.Unreadable);
                        return;
                }
            }

            cursor = page.Cursor;
            if (page.Finished)
            {
                // The terminal event always comes last, so a finished page without one cannot be read.
                ShowUnconfirmed(turn with { Cursor = cursor, ReplySoFar = LiveReply });
                return;
            }
        }
    }

    /// <summary>
    /// Reads the conversation, follows a turn that still runs, and keeps the message in the
    /// composer unless the conversation saved it.
    /// </summary>
    private async Task SettleAsync(string text, int savedBefore)
    {
        ConversationDetail detail;
        try
        {
            detail = await _conversations.GetConversationAsync(_conversationId!, _closing.Token);
        }
        catch (Exception error) when (IsRepeatable(error))
        {
            ShowUnconfirmed(new Unconfirmed(text, savedBefore, TurnId: null, Cursor: null, FirstTurn: false));
            return;
        }
        catch (Exception error) when (error is not OperationCanceledException || !_closing.IsCancellationRequested)
        {
            FailSend(text, ChatErrors.Describe(error));
            return;
        }

        ShowConversation(detail);
        if (detail.ActiveTurnId is { } activeTurnId)
        {
            ShowTranscript(pendingText: text);

            // A running turn's message is not saved yet, so a conversation without messages is
            // still on its first turn, whose failure deletes it.
            await FollowTurnAsync(new Unconfirmed(text, savedBefore, activeTurnId, Cursor: null, FirstTurn: detail.Messages.Count == 0 && !_heldStart));
            return;
        }

        await RefreshAttachmentsAsync();

        List<Message> userMessages = detail.Messages.Where(message => message.Role == "user").ToList();
        bool saved = userMessages.Count > savedBefore && userMessages[^1].Content == text;
        if (!saved)
        {
            KeepInComposer(text, IntakeChatText.TurnConflict);
        }
    }

    private async Task CheckAgainAsync()
    {
        if (_unconfirmed is not { } unconfirmed)
        {
            return;
        }

        SetUnconfirmed(null);
        ErrorText = null;
        IsBusy = true;
        ShowTranscript(pendingText: unconfirmed.Text);
        _sendingSpoken = unconfirmed.Spoken;
        try
        {
            if (unconfirmed is { FirstTurn: true, TurnId: not null })
            {
                // The conversation of a failed first turn is gone; only its turn tells how it ended.
                await FollowTurnAsync(unconfirmed);
            }
            else
            {
                await SettleAsync(unconfirmed.Text, unconfirmed.SavedBefore);
            }
        }
        catch (OperationCanceledException) when (_closing.IsCancellationRequested)
        {
        }
        finally
        {
            _sendingSpoken = false;
            ProgressText = null;
            LiveReply = null;
            IsBusy = false;
        }
    }

    // Attachment state follows these rules:
    // 1. One attachment operation (attach, retry, remove, list) runs at a time, guarded by
    //    _isAttaching, so a list answer can never overwrite a later upload or removal.
    // 2. A 403 from any attachment call means staff switched attachments off: the chat stops
    //    offering them.
    // 3. After an action whose outcome is uncertain, the chat lists what NevaBridge holds; when
    //    that fails too, FilesUnknown says so and RefreshFilesCommand tries again. A failed upload
    //    keeps its file listed with its admission, which only deleting releases.
    // 4. Closing the window ends any of them quietly.

    private Task AttachAsync()
    {
        if (_options.PickFile is not { } pickFile || !CanAttach)
        {
            return Task.CompletedTask;
        }

        return RunAttachmentAsync(async () =>
        {
            PickedFile? file = await pickFile();
            if (file is not null)
            {
                await UploadAsync(file, Track(Uploading(file.Name)));
            }
        });
    }

    /// <summary>
    /// Uploads a failed file again. A file that kept its admission first has that admission deleted:
    /// its bytes may have arrived, and NevaBridge would attach them later, so a new copy goes up only
    /// once the old one cannot.
    /// </summary>
    private Task RetryAsync(AttachmentItem failed, PickedFile file, AttachmentUpload? admission)
    {
        if (_conversationId is not { } conversationId || _isAttaching || IsEnded)
        {
            return Task.CompletedTask;
        }

        return RunAttachmentAsync(async () =>
        {
            if (admission is not null)
            {
                await _conversations.DeleteAttachmentAsync(conversationId, admission.AttachmentId, _closing.Token);
            }

            await UploadAsync(file, Replace(failed, Uploading(file.Name)));
        });
    }

    /// <summary>
    /// Deletes a failed file's admission on NevaBridge, so bytes that did arrive cannot attach it
    /// later. A closed conversation takes no change to its files, so there the row only goes.
    /// </summary>
    private Task DismissAsync(AttachmentItem failed, AttachmentUpload? admission)
    {
        if (admission is null || IsEnded || _conversationId is not { } conversationId)
        {
            Attachments.Remove(failed);
            AttachmentNotice = null;
            return Task.CompletedTask;
        }

        if (_isAttaching)
        {
            return Task.CompletedTask;
        }

        return RunAttachmentAsync(async () =>
        {
            await _conversations.DeleteAttachmentAsync(conversationId, admission.AttachmentId, _closing.Token);
            Attachments.Remove(failed);
        });
    }

    /// <summary>
    /// Runs one attachment operation under the lock. A message sent meanwhile waits for it, so the
    /// reply sees the files.
    /// </summary>
    private async Task RunAttachmentAsync(Func<Task> operation)
    {
        AttachmentNotice = null;
        SetAttaching(true);
        Task running = RunGuardedAsync(operation);
        _attaching = running;
        await running;
    }

    private async Task RunGuardedAsync(Func<Task> operation)
    {
        try
        {
            await operation();
        }
        catch (OperationCanceledException) when (_closing.IsCancellationRequested)
        {
            // The window closed while the file was on its way.
        }
        catch (Exception error) when (_conversationId is { } conversationId)
        {
            // The file picker failed, or something past the upload; what NevaBridge holds is uncertain.
            await ShowAttachmentFailureAsync(conversationId, error);
        }
        catch (Exception error)
        {
            AttachmentNotice = ChatErrors.Describe(error);
        }
        finally
        {
            SetAttaching(false);
            await RunQueuedRefreshAsync();
        }
    }

    /// <summary>
    /// Uploads a picked file. Before the first message it starts the conversation with the file;
    /// afterwards it takes the ordinary upload.
    /// </summary>
    private async Task UploadAsync(PickedFile file, AttachmentItem row)
    {
        if (_conversationId is { } conversationId)
        {
            await AdmitAndUploadAsync(conversationId, file, row);
            return;
        }

        HeldIntakeConversation held;
        try
        {
            held = await _conversations.StartHeldIntakeConversationAsync(
                _productId,
                _reporter,
                new StartHeldIntakeConversationRequest([new AttachmentUploadRequest(file.Name, file.SizeBytes)])
                {
                    ApplicationContext = _options.ApplicationContext,
                    ReporterContact = _options.ReporterContact,
                },
                _closing.Token);
        }
        catch (NevaBridgeApiException error) when (error is { StatusCode: 424, ConversationId: { } stored })
        {
            // Stored without signed uploads: request this file's upload again, as for a later file.
            HoldConversation(stored);
            await AdmitAndUploadAsync(stored, file, row);
            return;
        }
        catch (Exception error) when (error is not OperationCanceledException || !_closing.IsCancellationRequested)
        {
            // A refused file creates nothing; NevaBridge names the file and the reason.
            Attachments.Remove(row);
            if (IsForbidden(error))
            {
                TurnAttachmentsOff();
            }
            else
            {
                AttachmentNotice = ChatErrors.Describe(error);
            }

            return;
        }

        HoldConversation(held.ConversationId);
        await UploadAdmittedAsync(held.ConversationId, file, row, held.Attachments[0]);
    }

    /// <summary>
    /// Uploads to an admission the chat holds, for the first file and every later one alike. On any
    /// failure but a 403 the file stays listed with
    /// its admission, which Dismiss deletes and Retry deletes before a new upload: whether or not
    /// the bytes arrived, neither can leave a second or a forgotten copy behind.
    /// </summary>
    private async Task UploadAdmittedAsync(string conversationId, PickedFile file, AttachmentItem row, AttachmentUpload admission)
    {
        try
        {
            Replace(row, Uploaded(await _conversations.UploadAttachmentAsync(conversationId, admission, file, _closing.Token)));
        }
        catch (OperationCanceledException) when (_closing.IsCancellationRequested)
        {
            Attachments.Remove(row);
            throw;
        }
        catch (Exception error) when (IsForbidden(error))
        {
            Attachments.Remove(row);
            TurnAttachmentsOff();
        }
        catch (Exception error)
        {
            Replace(row, Failed(file, admission));
            AttachmentNotice = ChatErrors.Describe(error);

            // The bytes may have arrived and been confirmed; a listing that shows the admission drops the row.
            await ListHeldAttachmentsAsync(conversationId);
        }
    }

    /// <summary>
    /// Admits a file to an existing conversation, then uploads to that admission. When admitting
    /// fails, the file stays listed without an admission: no bytes were sent, so nothing can attach
    /// later, Retry admits it afresh and Dismiss only drops the row.
    /// </summary>
    private async Task AdmitAndUploadAsync(string conversationId, PickedFile file, AttachmentItem row)
    {
        AttachmentUpload admission;
        try
        {
            admission = await _conversations.RequestAttachmentUploadAsync(
                conversationId,
                _reporter.Id,
                new AttachmentUploadRequest(file.Name, file.SizeBytes),
                _closing.Token);
        }
        catch (Exception error) when (error is not OperationCanceledException || !_closing.IsCancellationRequested)
        {
            if (IsForbidden(error))
            {
                Attachments.Remove(row);
            }
            else
            {
                Replace(row, Failed(file, admission: null));
            }

            await ShowAttachmentFailureAsync(conversationId, error);
            return;
        }
        catch (OperationCanceledException)
        {
            Attachments.Remove(row);
            throw;
        }

        await UploadAdmittedAsync(conversationId, file, row, admission);
    }

    private void HoldConversation(string conversationId)
    {
        _conversationId = conversationId;
        _heldStart = true;
        _revision = 0;
        RaiseCommands();
    }

    private async Task RemoveAsync(AttachmentItem item)
    {
        if (_conversationId is not { } conversationId || item.Attachment is not { } attachment || _isAttaching)
        {
            return;
        }

        AttachmentNotice = null;
        SetAttaching(true);
        try
        {
            await _conversations.DeleteAttachmentAsync(conversationId, attachment.Id, _closing.Token);
            Attachments.Remove(item);
        }
        catch (OperationCanceledException) when (_closing.IsCancellationRequested)
        {
        }
        catch (Exception error)
        {
            await ShowAttachmentFailureAsync(conversationId, error);
        }
        finally
        {
            SetAttaching(false);
            await RunQueuedRefreshAsync();
        }
    }

    /// <summary>
    /// Lists the files NevaBridge holds. While another attachment operation runs, the listing waits
    /// until that operation has ended.
    /// </summary>
    private async Task RefreshAttachmentsAsync()
    {
        if (_conversationId is not { } conversationId || !OffersAttachments)
        {
            return;
        }

        if (_isAttaching)
        {
            _refreshAfterAttaching = true;
            return;
        }

        SetAttaching(true);
        try
        {
            await ListHeldAttachmentsAsync(conversationId);
        }
        finally
        {
            SetAttaching(false);
            await RunQueuedRefreshAsync();
        }
    }

    /// <summary>Runs a listing a conversation read asked for while this operation held the lock.</summary>
    private async Task RunQueuedRefreshAsync()
    {
        if (_refreshAfterAttaching && !_closing.IsCancellationRequested)
        {
            _refreshAfterAttaching = false;
            await RefreshAttachmentsAsync();
        }
    }

    private void ShowAttachment(Attachment attachment) => Attachments.Add(Uploaded(attachment));

    private AttachmentItem Uploading(string fileName) => new(fileName, AttachmentState.Uploading)
    {
        RemoveCommand = Unavailable,
        RetryCommand = Unavailable,
        DismissCommand = Unavailable,
    };

    private AttachmentItem Uploaded(Attachment attachment)
    {
        AttachmentItem? item = null;
        item = new AttachmentItem(attachment.FileName, AttachmentState.Uploaded)
        {
            Attachment = attachment,
            RemoveCommand = new AsyncCommand(() => RemoveAsync(item!), () => !IsEnded && !_isAttaching),
            RetryCommand = Unavailable,
            DismissCommand = Unavailable,
        };
        return item;
    }

    /// <param name="admission">The admission the file was uploaded to, when the chat holds one.</param>
    private AttachmentItem Failed(PickedFile file, AttachmentUpload? admission)
    {
        AttachmentItem? item = null;
        item = new AttachmentItem(file.Name, AttachmentState.Failed)
        {
            Admission = admission,
            RemoveCommand = Unavailable,
            RetryCommand = new AsyncCommand(() => RetryAsync(item!, file, admission), () => !IsEnded && !_isAttaching),
            DismissCommand = new AsyncCommand(() => DismissAsync(item!, admission), () => !_isAttaching),
        };
        return item;
    }

    private static AsyncCommand Unavailable { get; } = new(() => Task.CompletedTask, () => false);

    private AttachmentItem Track(AttachmentItem row)
    {
        Attachments.Add(row);
        return row;
    }

    private AttachmentItem Replace(AttachmentItem row, AttachmentItem replacement)
    {
        int index = Attachments.IndexOf(row);
        if (index < 0)
        {
            Attachments.Add(replacement);
        }
        else
        {
            Attachments[index] = replacement;
        }

        RaiseCommands();
        return replacement;
    }

    /// <summary>Says why an attachment action failed and, since its outcome is uncertain, lists the files.</summary>
    private async Task ShowAttachmentFailureAsync(string conversationId, Exception error)
    {
        if (IsForbidden(error))
        {
            TurnAttachmentsOff();
            return;
        }

        AttachmentNotice = ChatErrors.Describe(error);
        await ListHeldAttachmentsAsync(conversationId);
    }

    /// <summary>Replaces the list with the files NevaBridge holds. The caller holds _isAttaching.</summary>
    private async Task ListHeldAttachmentsAsync(string conversationId)
    {
        IReadOnlyList<Attachment> held;
        try
        {
            held = await _conversations.ListAttachmentsAsync(conversationId, _closing.Token);
        }
        catch (OperationCanceledException) when (_closing.IsCancellationRequested)
        {
            return;
        }
        catch (Exception error) when (IsForbidden(error))
        {
            TurnAttachmentsOff();
            return;
        }
        catch (Exception)
        {
            SetFilesUnknown(true);
            return;
        }

        // Files that are uploading or whose upload failed are not NevaBridge's yet; they stay, unless
        // NevaBridge lists a failed file's admission because its bytes arrived after all.
        HashSet<string> listed = held.Select(attachment => attachment.Id).ToHashSet();
        List<AttachmentItem> local = Attachments
            .Where(item => !item.IsUploaded
                && !(item.Admission is { } admission && listed.Contains(admission.AttachmentId)))
            .ToList();
        Attachments.Clear();
        foreach (Attachment attachment in held)
        {
            ShowAttachment(attachment);
        }

        foreach (AttachmentItem item in local)
        {
            Attachments.Add(item);
        }

        SetFilesUnknown(false);
    }

    private static bool IsForbidden(Exception error) => error is NevaBridgeApiException { StatusCode: 403 };

    private void TurnAttachmentsOff()
    {
        _attachmentsOff = true;
        SetFilesUnknown(false);
        OnPropertyChanged(nameof(OffersAttachments));
        AttachmentNotice = IntakeChatText.AttachmentsOff;
        RaiseCommands();
    }

    private void SetFilesUnknown(bool unknown)
    {
        _filesUnknown = unknown;
        OnPropertyChanged(nameof(FilesUnknown));
        OnPropertyChanged(nameof(AttachmentNotice));
        RaiseCommands();
    }

    private void ShowConversation(ConversationDetail detail)
    {
        _transcript = detail.Messages;
        _revision = detail.Revision ?? _revision;
        _savedUserMessages = detail.Messages.Count(message => message.Role == "user");
        ShowTranscript(pendingText: null);
        FiledReports.Clear();
        if (detail.IsClosed)
        {
            foreach (Report report in detail.Reports)
            {
                FiledReports.Add(new FiledReportItem(
                    report.DisplayTitle ?? IntakeChatText.Category(report.Category),
                    IntakeChatText.Category(report.Category),
                    IntakeChatText.Status(report.Status),
                    IntakeChatText.StatusReason(report.StatusReason)));
            }
        }

        IsEnded = detail.IsClosed;
        OnPropertyChanged(nameof(EndedText));
    }

    private void ShowTranscript(string? pendingText)
    {
        Messages.Clear();
        foreach (Message message in _transcript)
        {
            Messages.Add(new ChatMessageItem(message.Content, IsFromReporter: message.Role == "user"));
        }

        if (pendingText is not null)
        {
            Messages.Add(new ChatMessageItem(pendingText, IsFromReporter: true));
        }
    }

    private void FailSend(string text, string reason)
    {
        ShowTranscript(pendingText: null);
        KeepInComposer(text, reason);
        RaiseCommands();
    }

    /// <summary>
    /// Puts an unsent message back in the composer: a typed one ahead of anything typed since, a
    /// spoken one after the text typed before voice mode, each on its own line.
    /// </summary>
    private void KeepInComposer(string text, string reason)
    {
        _sendOutcome = SpokenSendOutcome.Kept;
        Draft = string.IsNullOrWhiteSpace(Draft) ? text
            : _sendingSpoken ? Draft.TrimEnd() + Environment.NewLine + text
            : text + Environment.NewLine + Draft;
        ErrorText = reason;
    }

    private void ShowUnconfirmed(Unconfirmed unconfirmed)
    {
        _sendOutcome = SpokenSendOutcome.Unconfirmed;
        ShowTranscript(pendingText: unconfirmed.Text);
        SetUnconfirmed(unconfirmed with { Spoken = _sendingSpoken });
        ErrorText = IntakeChatText.Unreadable;
    }

    private void SetUnconfirmed(Unconfirmed? unconfirmed)
    {
        _unconfirmed = unconfirmed;
        OnPropertyChanged(nameof(CanCheckAgain));
        RaiseCommands();
    }

    private void SetAttaching(bool attaching)
    {
        _isAttaching = attaching;
        RaiseCommands();
    }

    /// <summary>A read that failed this way can be repeated unchanged.</summary>
    private bool IsRepeatable(Exception error) => error switch
    {
        NevaBridgeApiException api => api.IsEdgeFailure,
        HttpRequestException => true,
        // HttpClient reports its own timeout as a cancellation.
        TaskCanceledException => !_closing.IsCancellationRequested,
        _ => false,
    };

    private void RaiseCommands()
    {
        SendCommand.RaiseCanExecuteChanged();
        FinishCommand.RaiseCanExecuteChanged();
        CheckAgainCommand.RaiseCanExecuteChanged();
        AttachCommand.RaiseCanExecuteChanged();
        RefreshFilesCommand.RaiseCanExecuteChanged();
        foreach (AttachmentItem item in Attachments)
        {
            item.RemoveCommand.RaiseCanExecuteChanged();
            item.RetryCommand.RaiseCanExecuteChanged();
            item.DismissCommand.RaiseCanExecuteChanged();
        }

        OnPropertyChanged(nameof(CanFinish));
        OnPropertyChanged(nameof(CanAttach));
        OnPropertyChanged(nameof(FinishHint));
        Voice?.RaiseCommands();
    }

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

    private void OnPropertyChanged(string? name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    /// <summary>
    /// A sent message whose outcome is not known yet. <see cref="SavedBefore"/> is the number of
    /// saved reporter messages before it was sent; <see cref="TurnId"/> and <see cref="Cursor"/>
    /// say where its turn was being read.
    /// </summary>
    private sealed record Unconfirmed(string Text, int SavedBefore, string? TurnId, string? Cursor, bool FirstTurn)
    {
        /// <summary>The message was spoken; if it is not saved, its words go after the typed text.</summary>
        public bool Spoken { get; init; }

        /// <summary>The reply as far as the events up to <see cref="Cursor"/> wrote it.</summary>
        public string? ReplySoFar { get; init; }
    }
}
