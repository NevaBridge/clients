using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Web.WebView2.Core;
using NevaBridge.Chat;
using NevaBridge.Chat.Settings;
using NevaBridge.Chat.WebChat;

namespace NevaBridge.Wpf;

/// <summary>
/// Hosts the NevaBridge web chat page in WebView2. The page draws the chat; this control
/// relays its requests to NevaBridge through <see cref="WebChatBridge"/>, so the API key stays
/// in the application.
/// </summary>
public partial class NevaBridgeWebChatControl : UserControl
{
    /// <summary>The host name under which a page shipped with the application is served.</summary>
    public const string BundledPageHost = "webchat.nevabridge.example";

    /// <summary>
    /// Where WebView2 keeps its browser data. The default is a per-user folder, because the folder
    /// next to the executable is read-only once the application is installed under Program Files.
    /// </summary>
    public string UserDataFolder { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        AppDomain.CurrentDomain.FriendlyName,
        "NevaBridge.WebView2");

    /// <summary>
    /// Whether the page may offer file attachments (when the tenant has the feature). The user
    /// picks files in the operating system's dialog and this control uploads them.
    /// </summary>
    public bool AllowAttachments { get; set; } = true;

    private WebChatBridge? _bridge;
    private Uri? _pageUrl;

    public NevaBridgeWebChatControl()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Loads the chat page from <see cref="NevaBridgeSettings.WebChatUrl"/>, or the page shipped with
    /// the application when that is not set. Call it after the window containing the control is
    /// shown: WebView2 finishes initializing only inside a shown window.
    /// </summary>
    public async Task InitializeAsync(
        INevaBridgeConversations conversations,
        NevaBridgeSettings settings,
        NevaBridgeReporter reporter,
        IReadOnlyDictionary<string, string>? applicationContext)
    {
        _pageUrl = settings.WebChatUrl ?? new Uri($"https://{BundledPageHost}/index.html");
        string? bundledPageFolder = settings.WebChatUrl is null
            ? Path.Combine(AppContext.BaseDirectory, "webview-chat")
            : null;
        _bridge = new WebChatBridge(
            conversations,
            settings.ProductId,
            reporter,
            applicationContext,
            _pageUrl,
            AllowAttachments ? PickFileAsync : null);

        try
        {
            CoreWebView2Environment environment =
                await CoreWebView2Environment.CreateAsync(userDataFolder: UserDataFolder);
            await WebView.EnsureCoreWebView2Async(environment);
        }
        catch (WebView2RuntimeNotFoundException)
        {
            ShowLoadError("The Microsoft Edge WebView2 Runtime is not installed on this computer.");
            return;
        }

        CoreWebView2 core = WebView.CoreWebView2;
        core.Settings.AreHostObjectsAllowed = false;
        core.Settings.IsStatusBarEnabled = false;
        core.Settings.AreDevToolsEnabled = Debugger.IsAttached;
        if (bundledPageFolder is not null)
        {
            core.SetVirtualHostNameToFolderMapping(
                BundledPageHost,
                bundledPageFolder,
                CoreWebView2HostResourceAccessKind.Deny);
        }

        core.NavigationStarting += KeepNavigationOnThePage;
        core.NewWindowRequested += OpenLinksInTheBrowser;
        core.NavigationCompleted += ReportLoadFailure;
        core.WebMessageReceived += RelayToNevaBridge;
        core.Navigate(_pageUrl.AbsoluteUri);
    }

    private Task<PickedFile?> PickFileAsync(CancellationToken cancellationToken)
    {
        Microsoft.Win32.OpenFileDialog dialog = new()
        {
            Title = "Attach a file",
            Filter = "Screenshots, videos and logs|*.png;*.jpg;*.jpeg;*.gif;*.webp;*.mp4;*.mov;*.webm;*.txt;*.log;*.md;*.csv;*.json;*.xml;*.yaml;*.yml",
            CheckFileExists = true,
        };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true)
        {
            return Task.FromResult<PickedFile?>(null);
        }

        FileInfo file = new(dialog.FileName);
        return Task.FromResult<PickedFile?>(new PickedFile(file.Name, file.Length, file.OpenRead));
    }

    private void KeepNavigationOnThePage(object? sender, CoreWebView2NavigationStartingEventArgs e)
    {
        if (!SameOrigin(new Uri(e.Uri), _pageUrl!))
        {
            e.Cancel = true;
        }
    }

    private static void OpenLinksInTheBrowser(object? sender, CoreWebView2NewWindowRequestedEventArgs e)
    {
        e.Handled = true;
        if (Uri.TryCreate(e.Uri, UriKind.Absolute, out Uri? link) && link.Scheme == Uri.UriSchemeHttps)
        {
            Process.Start(new ProcessStartInfo(link.AbsoluteUri) { UseShellExecute = true });
        }
    }

    private void ReportLoadFailure(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        if (!e.IsSuccess)
        {
            ShowLoadError($"The NevaBridge chat could not be loaded from {_pageUrl} ({e.WebErrorStatus}).");
        }
    }

    private async void RelayToNevaBridge(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        if (_bridge is not { } bridge || !IsLoaded || !Uri.TryCreate(e.Source, UriKind.Absolute, out Uri? source))
        {
            return;
        }

        string message;
        try
        {
            message = e.TryGetWebMessageAsString();
        }
        catch (ArgumentException)
        {
            // The page posted a JSON object instead of a string; the protocol only uses strings.
            return;
        }

        string? response = await bridge.HandleAsync(source, message);
        if (response is null || !IsLoaded)
        {
            return;
        }

        try
        {
            // A host may dispose WebView2 while an API request is still pending.
            WebView.CoreWebView2?.PostWebMessageAsString(response);
        }
        catch (Exception error) when (error is ObjectDisposedException or InvalidOperationException or COMException)
        {
            ShowLoadError("The chat window is no longer available. Close it and open it again.");
        }
    }

    private void ShowLoadError(string text)
    {
        WebView.Visibility = Visibility.Collapsed;
        LoadError.Text = text;
        LoadError.Visibility = Visibility.Visible;
    }

    private static bool SameOrigin(Uri a, Uri b) =>
        string.Equals(a.GetLeftPart(UriPartial.Authority), b.GetLeftPart(UriPartial.Authority), StringComparison.OrdinalIgnoreCase);
}
