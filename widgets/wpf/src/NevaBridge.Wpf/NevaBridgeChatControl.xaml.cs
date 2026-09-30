using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using NevaBridge.Chat;
using System.Windows.Input;

namespace NevaBridge.Wpf;

/// <summary>
/// A chat panel that runs one NevaBridge reporting conversation. Set its DataContext to a
/// <see cref="ChatViewModel"/>.
/// </summary>
public partial class NevaBridgeChatControl : UserControl
{
    public NevaBridgeChatControl()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is ChatViewModel previous)
        {
            previous.Messages.CollectionChanged -= ScrollToNewestMessage;
        }

        if (e.NewValue is ChatViewModel current)
        {
            current.Messages.CollectionChanged += ScrollToNewestMessage;
        }
    }

    // Handled in the preview phase because a multi-line TextBox consumes Ctrl+Enter as a line
    // break before a KeyBinding on it would run.
    private void OnDraftPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter
            && Keyboard.Modifiers == ModifierKeys.Control
            && DataContext is ChatViewModel chat
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
}
