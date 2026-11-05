using System.ComponentModel;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using NevaBridge.Chat;

namespace NevaBridge.Wpf;

/// <summary>
/// A chat panel that runs one NevaBridge Generic Intake conversation. Set its DataContext to an
/// <see cref="IntakeChatViewModel"/>, and pass <see cref="PickFileAsync"/> as
/// <see cref="IntakeChatOptions.PickFile"/> to offer attachments.
/// </summary>
public partial class NevaBridgeIntakeChatControl : UserControl
{
    public NevaBridgeIntakeChatControl()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    /// <summary>Shows the operating system's file dialog over the window holding this control.</summary>
    public Task<PickedFile?> PickFileAsync() => Task.FromResult(FilePicker.Pick(Window.GetWindow(this)));

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is IntakeChatViewModel previous)
        {
            previous.Messages.CollectionChanged -= ScrollToNewestMessage;
            previous.PropertyChanged -= ScrollWithTheLiveReply;
        }

        if (e.NewValue is IntakeChatViewModel current)
        {
            current.Messages.CollectionChanged += ScrollToNewestMessage;
            current.PropertyChanged += ScrollWithTheLiveReply;
        }
    }

    // Handled in the preview phase because a multi-line TextBox consumes Ctrl+Enter as a line
    // break before a KeyBinding on it would run.
    private void OnDraftPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter
            && Keyboard.Modifiers == ModifierKeys.Control
            && DataContext is IntakeChatViewModel chat
            && chat.SendCommand.CanExecute(null))
        {
            chat.SendCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void ScrollToNewestMessage(object? sender, NotifyCollectionChangedEventArgs e)
    {
        TranscriptScroller.ScrollToEnd();
        DraftBox.Focus();
    }

    private void ScrollWithTheLiveReply(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(IntakeChatViewModel.LiveReply) or nameof(IntakeChatViewModel.IsEnded))
        {
            TranscriptScroller.ScrollToEnd();
        }
    }
}
