using System.Net.Http;
using System.Reflection;
using NevaBridge.Chat;
using NevaBridge.Chat.Settings;
using NevaBridge.Sample.Client;
using NevaBridge.Wpf;

namespace NevaBridge.Sample.DesktopApp;

/// <summary>
/// Step 3 in your application: open the chat, as the native control or as the NevaBridge web chat.
/// </summary>
internal static class NevaBridgeIntegration
{
    // One HttpClient for the lifetime of the application. NevaBridge turns can take up to about
    // 30 seconds while the assistant writes.
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(60) };

    /// <summary>The native WPF chat: set it as the DataContext of a NevaBridgeChatControl.</summary>
    public static async Task<ChatViewModel> CreateNativeChatAsync(string screen)
    {
        NevaBridgeSettings settings = await NevaBridgeSettingsSource.Create().GetSettingsAsync();
        return new ChatViewModel(
            new NevaBridgeClient(Http, settings),
            settings.ProductId,
            CurrentReporter.Get(),
            ApplicationContext(screen));
    }

    /// <summary>
    /// The NevaBridge web chat in WebView2. The page comes from the settings' web chat URL, or ships
    /// with the application when none is set. Call it after the window is shown.
    /// </summary>
    public static async Task InitializeWebChatAsync(NevaBridgeWebChatControl control, string screen)
    {
        NevaBridgeSettings settings = await NevaBridgeSettingsSource.Create().GetSettingsAsync();
        await control.InitializeAsync(
            new NevaBridgeClient(Http, settings),
            settings,
            CurrentReporter.Get(),
            ApplicationContext(screen));
    }

    // Anything that helps the support team reproduce the problem. Shown in triage and in the
    // delivered ticket.
    private static Dictionary<string, string> ApplicationContext(string screen) => new()
    {
        ["application"] = "NevaBridge WPF example",
        ["version"] = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "unknown",
        ["screen"] = screen,
        ["os"] = Environment.OSVersion.VersionString,
    };
}
