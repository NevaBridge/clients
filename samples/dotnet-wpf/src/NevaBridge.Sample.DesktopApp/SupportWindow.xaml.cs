using System.Windows;

namespace NevaBridge.Sample.DesktopApp;

public partial class SupportWindow : Window
{
    private SupportWindow()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Creates the window with a fresh conversation, or explains why NevaBridge is not
    /// available and returns null.
    /// </summary>
    public static async Task<SupportWindow?> OpenAsync(Window owner, string screen)
    {
        try
        {
            SupportWindow window = new() { Owner = owner };
            window.Chat.DataContext = await NevaBridgeIntegration.CreateNativeChatAsync(screen);
            return window;
        }
        catch (Exception error) when (ChatErrors.IsSettingsProblem(error))
        {
            ChatErrors.ShowSettingsProblem(owner, error, "Support");
            return null;
        }
    }
}
