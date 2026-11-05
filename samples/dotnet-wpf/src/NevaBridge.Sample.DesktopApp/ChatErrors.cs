using System.Windows;
using Microsoft.Data.SqlClient;
using NevaBridge.Chat.Settings;

namespace NevaBridge.Sample.DesktopApp;

/// <summary>
/// Step 4 in your application: explain why the chat cannot open. Problems during a conversation
/// (no credits, NevaBridge busy, no network) are shown inside the chat itself.
/// </summary>
internal static class ChatErrors
{
    public static bool IsSettingsProblem(Exception error) =>
        error is NevaBridgeSettingsException or SqlException;

    public static void ShowSettingsProblem(Window owner, Exception error, string title) =>
        MessageBox.Show(
            owner,
            $"The NevaBridge settings could not be read (source: {NevaBridgeSettingsSource.Name}).\n\n{error.Message}",
            title,
            MessageBoxButton.OK,
            MessageBoxImage.Warning);

    public static void ShowUnavailable(Window owner, string message, string title) =>
        MessageBox.Show(owner, message, title, MessageBoxButton.OK, MessageBoxImage.Information);
}
