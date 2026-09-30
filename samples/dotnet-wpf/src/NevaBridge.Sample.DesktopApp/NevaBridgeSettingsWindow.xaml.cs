using System.Windows;
using System.Windows.Controls;
using NevaBridge.Chat.Settings;
using NevaBridge.Wpf;

namespace NevaBridge.Sample.DesktopApp;

/// <summary>Saves the NevaBridge settings once to Windows Credential Manager.</summary>
public partial class NevaBridgeSettingsWindow : Window
{
    private readonly WindowsCredentialSettingsStore _store = new();

    public NevaBridgeSettingsWindow()
    {
        InitializeComponent();
        Loaded += async (_, _) => await ShowSavedAsync();
    }

    private async Task ShowSavedAsync()
    {
        if (!_store.Exists())
        {
            return;
        }

        try
        {
            NevaBridgeSettings saved = await _store.GetSettingsAsync();
            ProductId.Text = saved.ProductId;
            WebChatUrl.Text = saved.WebChatUrl?.AbsoluteUri ?? string.Empty;
            Environment.SelectedIndex = saved.ApiBaseUrl == NevaBridgeSettings.ProductionApiBaseUrl ? 1 : 0;

            // The saved key is never shown again; leave the field empty to keep it.
            ApiKey.Tag = saved.ApiKey;
        }
        catch (NevaBridgeSettingsException error)
        {
            ShowProblem(error.Message);
        }
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        try
        {
            string apiKey = ApiKey.Password.Length > 0 ? ApiKey.Password : ApiKey.Tag as string ?? string.Empty;
            Uri apiBaseUrl = new((string)((ComboBoxItem)Environment.SelectedItem).Tag);
            Uri? webChatUrl = WebChatUrl.Text.Trim() is { Length: > 0 } text
                ? Uri.TryCreate(text, UriKind.Absolute, out Uri? url)
                    ? url
                    : throw new NevaBridgeSettingsException($"'{text}' is not an absolute address.")
                : null;

            _store.Save(new NevaBridgeSettings(apiBaseUrl, ProductId.Text, apiKey, webChatUrl));
            DialogResult = true;
        }
        catch (NevaBridgeSettingsException error)
        {
            ShowProblem(error.Message);
        }
    }

    private void OnRemove(object sender, RoutedEventArgs e)
    {
        _store.Delete();
        DialogResult = true;
    }

    private void ShowProblem(string message)
    {
        Problem.Text = message;
        Problem.Visibility = Visibility.Visible;
    }
}
