using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace NevaBridge.Chat;

/// <summary>
/// The rating control under one assistant reply: thumbs up and down, the comment prompt after a
/// thumbs down, and the sent comment.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>A press shows at once and is saved behind it; a failed save restores the rating shown
/// before the press.</item>
/// <item>One save runs at a time per reply, so a failure never restores over a newer press: the
/// thumbs and Send wait while a save runs.</item>
/// <item>Pressing the pressed thumb clears the rating and its comment; the comment changes only
/// through Send in the prompt, never through a thumb.</item>
/// <item>A comment the user wrote is never lost: a failed Send reopens the prompt with it.</item>
/// <item>Focus moves only on the user's own action: into the comment box when thumbs down or Edit
/// comment opens the prompt, back to thumbs down when Send or Skip closes it. The view takes each
/// request once, so redrawing the transcript never moves focus. A failed Send reopens the prompt
/// without taking the focus, since the user may be typing elsewhere by the time it fails.</item>
/// </list>
/// </remarks>
public sealed class ReplyRatingItem : INotifyPropertyChanged
{
    /// <summary>The longest comment NevaBridge stores.</summary>
    public const int MaxCommentLength = 1000;

    private const string Up = "up";
    private const string Down = "down";

    private readonly Func<ReplyRatingItem, string?, string?, Task<bool>> _save;
    private string? _value;
    private string? _comment;
    private bool _isPromptOpen;
    private string _commentDraft = string.Empty;
    private bool _isSaving;
    private bool _commentFocusRequested;
    private bool _thumbFocusRequested;

    /// <param name="messageId">The rated reply.</param>
    /// <param name="stored">The rating NevaBridge holds for it.</param>
    /// <param name="save">
    /// Saves a rating (null value clears it) and reports whether it was saved; it shows the reason
    /// itself when not.
    /// </param>
    internal ReplyRatingItem(string messageId, MessageRating stored, Func<ReplyRatingItem, string?, string?, Task<bool>> save)
    {
        MessageId = messageId;
        _value = stored.Value is Up or Down ? stored.Value : null;
        _comment = _value == Down ? stored.Comment : null;
        _save = save;
        RateUpCommand = new AsyncCommand(() => PressAsync(Up), () => !_isSaving);
        RateDownCommand = new AsyncCommand(() => PressAsync(Down), () => !_isSaving);
        SendCommentCommand = new AsyncCommand(SendCommentAsync, () => !_isSaving);
        SkipCommentCommand = new AsyncCommand(SkipComment, () => true);
        EditCommentCommand = new AsyncCommand(EditComment, () => !_isSaving);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string MessageId { get; }

    public bool IsUp => _value == Up;

    public bool IsDown => _value == Down;

    public string? Comment => _comment;

    public bool HasComment => _comment is not null;

    /// <summary>The comment prompt is open under this reply.</summary>
    public bool IsPromptOpen
    {
        get => _isPromptOpen;
        private set
        {
            _isPromptOpen = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ShowsComment));
        }
    }

    /// <summary>The sent comment shows while the prompt is closed.</summary>
    public bool ShowsComment => HasComment && !IsPromptOpen;

    public string CommentDraft
    {
        get => _commentDraft;
        set
        {
            _commentDraft = value;
            OnPropertyChanged();
        }
    }

    /// <summary>The thumbs up's tooltip: how to remove the rating once it is pressed.</summary>
    public string UpHint => IsUp ? IntakeChatText.ClearRatingHint : IntakeChatText.GoodReply;

    /// <summary>The thumbs down's tooltip: how to remove the rating once it is pressed.</summary>
    public string DownHint => IsDown ? IntakeChatText.ClearRatingHint : IntakeChatText.BadReply;

    public AsyncCommand RateUpCommand { get; }

    public AsyncCommand RateDownCommand { get; }

    public AsyncCommand SendCommentCommand { get; }

    public AsyncCommand SkipCommentCommand { get; }

    public AsyncCommand EditCommentCommand { get; }

    /// <summary>
    /// Whether the comment box should take the focus now, because the user opened the prompt. True
    /// once per opening; a redrawn prompt asks for nothing.
    /// </summary>
    public bool TakeCommentFocus()
    {
        bool requested = _commentFocusRequested;
        _commentFocusRequested = false;
        return requested;
    }

    /// <summary>Whether thumbs down should take the focus now, because Send or Skip closed the prompt. True once.</summary>
    public bool TakeThumbFocus()
    {
        bool requested = _thumbFocusRequested;
        _thumbFocusRequested = false;
        return requested;
    }

    private async Task PressAsync(string thumb)
    {
        if (_isSaving)
        {
            return;
        }

        if (_value == thumb)
        {
            IsPromptOpen = false;
            await SaveAsync(value: null, comment: null, keepDraftOnFailure: false);
            return;
        }

        if (thumb == Down)
        {
            CommentDraft = string.Empty;
            OpenPrompt();
        }
        else
        {
            IsPromptOpen = false;
        }

        await SaveAsync(thumb, comment: null, keepDraftOnFailure: false);
    }

    private async Task SendCommentAsync()
    {
        if (_isSaving)
        {
            return;
        }

        string? comment = CommentDraft.Trim() is { Length: > 0 } text
            ? text[..Math.Min(text.Length, MaxCommentLength)]
            : null;
        ClosePrompt();
        if (_value == Down && comment == _comment)
        {
            return;
        }

        await SaveAsync(Down, comment, keepDraftOnFailure: true);
    }

    private Task SkipComment()
    {
        ClosePrompt();
        return Task.CompletedTask;
    }

    private Task EditComment()
    {
        CommentDraft = _comment ?? string.Empty;
        OpenPrompt();
        return Task.CompletedTask;
    }

    private void OpenPrompt()
    {
        _thumbFocusRequested = false;
        _commentFocusRequested = true;
        IsPromptOpen = true;
    }

    private void ClosePrompt()
    {
        _commentFocusRequested = false;
        _thumbFocusRequested = true;
        IsPromptOpen = false;
    }

    /// <param name="keepDraftOnFailure">Reopens the prompt with the draft when the save fails.</param>
    private async Task SaveAsync(string? value, string? comment, bool keepDraftOnFailure)
    {
        string? previousValue = _value;
        string? previousComment = _comment;
        Show(value, comment);
        SetSaving(true);
        bool saved;
        try
        {
            saved = await _save(this, value, comment);
        }
        finally
        {
            SetSaving(false);
        }

        if (!saved)
        {
            Show(previousValue, previousComment);
            if (keepDraftOnFailure)
            {
                _commentFocusRequested = false;
                IsPromptOpen = true;
            }
            else
            {
                IsPromptOpen = false;
            }
        }
    }

    private void Show(string? value, string? comment)
    {
        _value = value;
        _comment = value == Down ? comment : null;

        // Raised even when nothing changed: a toggle button flips itself on a press and reads its
        // state back from these.
        OnPropertyChanged(nameof(IsUp));
        OnPropertyChanged(nameof(IsDown));
        OnPropertyChanged(nameof(UpHint));
        OnPropertyChanged(nameof(DownHint));
        OnPropertyChanged(nameof(Comment));
        OnPropertyChanged(nameof(HasComment));
        OnPropertyChanged(nameof(ShowsComment));
    }

    private void SetSaving(bool saving)
    {
        _isSaving = saving;
        RateUpCommand.RaiseCanExecuteChanged();
        RateDownCommand.RaiseCanExecuteChanged();
        SendCommentCommand.RaiseCanExecuteChanged();
        EditCommentCommand.RaiseCanExecuteChanged();
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
