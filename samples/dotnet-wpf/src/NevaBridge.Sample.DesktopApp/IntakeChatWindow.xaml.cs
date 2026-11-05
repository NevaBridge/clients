using System.Net.Http;
using System.Windows;
using NevaBridge.Chat;

namespace NevaBridge.Sample.DesktopApp;

public partial class IntakeChatWindow : Window
{
    private IntakeChatWindow()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Creates the window with a fresh Generic Intake conversation, or explains why it is not
    /// available and returns null.
    /// </summary>
    public static async Task<IntakeChatWindow?> OpenAsync(Window owner, string screen)
    {
        IntakeChatWindow window = new() { Owner = owner };
        IntakeChatViewModel? chat;
        try
        {
            chat = await NevaBridgeIntegration.CreateIntakeChatAsync(screen, window.Chat.PickFileAsync);
        }
        catch (Exception error) when (ChatErrors.IsSettingsProblem(error))
        {
            window.Close();
            ChatErrors.ShowSettingsProblem(owner, error, "Support assistant");
            return null;
        }
        catch (Exception error) when (error is HttpRequestException or TaskCanceledException or NevaBridgeApiException)
        {
            window.Close();
            ChatErrors.ShowUnavailable(owner, "NevaBridge could not be reached. Please try again in a moment.", "Support assistant");
            return null;
        }

        if (chat is null)
        {
            window.Close();
            ChatErrors.ShowUnavailable(owner, "The support assistant is not available for your organization.", "Support assistant");
            return null;
        }

        window.Chat.DataContext = chat;

        // Stops following a reply; NevaBridge finishes the turn and files an idle chat itself.
        window.Closed += (_, _) => chat.Dispose();
        return window;
    }
}
