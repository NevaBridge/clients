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
/// <summary>The tenant's feature gates, or, when they are null, why they could not be read.</summary>
internal sealed record TenantFeatures(IReadOnlyDictionary<string, bool>? Gates, string? Problem);

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
    /// The native Generic Intake chat: set it as the DataContext of a NevaBridgeIntakeChatControl.
    /// Returns null when the tenant does not have Generic Intake. Pass the control's file picker;
    /// it is used only when attachments are switched on and the tenant has them. Dictation and the
    /// hands-free conversation appear each when switched on and the tenant has them.
    /// </summary>
    public static async Task<IntakeChatViewModel?> CreateIntakeChatAsync(string screen, Func<Task<PickedFile?>> pickFile)
    {
        NevaBridgeSettings settings = await NevaBridgeSettingsSource.Create().GetSettingsAsync();
        NevaBridgeClient client = new(Http, settings);

        // Gates can change at any time, so they are read again whenever a chat opens.
        IReadOnlyDictionary<string, bool> tenant = await client.GetFeaturesAsync();
        if (!NevaBridgeFeatureSwitches.GenericIntakeChat(tenant))
        {
            return null;
        }

        bool dictation = NevaBridgeFeatureSwitches.Dictation(tenant);
        bool handsFree = NevaBridgeFeatureSwitches.HandsFreeVoice(tenant);
        return new IntakeChatViewModel(
            client,
            settings.ProductId,
            CurrentReporter.Get(),
            new IntakeChatOptions
            {
                ApplicationContext = ApplicationContext(screen),
                ReporterContact = CurrentReporter.Contact(),
                PickFile = NevaBridgeFeatureSwitches.Attachments(tenant) ? pickFile : null,
                Voice = dictation || handsFree
                    ? new IntakeChatVoice(client, new NAudioMicrophone(), new NAudioSpeechPlayer())
                    {
                        Dictation = dictation,
                        HandsFree = handsFree,
                    }
                    : null,
            });
    }

    /// <summary>
    /// The tenant's feature gates, or why they cannot be read, for example before the settings are
    /// saved or while NevaBridge is unreachable.
    /// </summary>
    public static async Task<TenantFeatures> GetTenantFeaturesAsync()
    {
        try
        {
            NevaBridgeSettings settings = await NevaBridgeSettingsSource.Create().GetSettingsAsync();
            return new TenantFeatures(await new NevaBridgeClient(Http, settings).GetFeaturesAsync(), Problem: null);
        }
        catch (Exception error) when (ChatErrors.IsSettingsProblem(error))
        {
            return new TenantFeatures(null, $"The NevaBridge settings could not be read: {error.Message}");
        }
        catch (Exception error) when (error is HttpRequestException or TaskCanceledException or NevaBridgeApiException)
        {
            return new TenantFeatures(null, $"NevaBridge could not be reached: {error.Message}");
        }
    }

    /// <summary>
    /// The NevaBridge web chat in WebView2. The page comes from the settings' web chat URL, or ships
    /// with the application when none is set. Call it after the window is shown.
    /// </summary>
    public static async Task InitializeWebChatAsync(NevaBridgeWebChatControl control, string screen)
    {
        NevaBridgeSettings settings = await NevaBridgeSettingsSource.Create().GetSettingsAsync();

        // The page offers attachments only when the tenant has them too.
        control.AllowAttachments = NevaBridgeFeatureSwitches.File.Attachments;
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
