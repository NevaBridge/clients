using System.Windows;

namespace NevaBridge.Sample.DesktopApp;

public partial class NevaBridgeChatWindow : Window
{
    private NevaBridgeChatWindow()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Shows the window and loads the web chat into it, or closes it again and explains why
    /// NevaBridge is not available.
    /// </summary>
    public static async Task ShowAsync(Window owner, string screen)
    {
        NevaBridgeChatWindow window = new() { Owner = owner };

        // WebView2 finishes initializing only once it is part of a shown window.
        window.Show();
        try
        {
            await NevaBridgeIntegration.InitializeWebChatAsync(window.Chat, screen);
        }
        catch (Exception error) when (ChatErrors.IsSettingsProblem(error))
        {
            window.Close();
            ChatErrors.ShowSettingsProblem(owner, error, "NevaBridge");
        }
    }
}
