using System.Windows;
using NevaBridge.Sample.Client;

namespace NevaBridge.Sample.DesktopApp;

public sealed record Invoice(string Number, string Customer, DateTime Due, decimal Amount);

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Invoices.ItemsSource = new[]
        {
            new Invoice("INV-1041", "Northwind Traders", DateTime.Today.AddDays(14), 1280.00m),
            new Invoice("INV-1042", "Fabrikam", DateTime.Today.AddDays(21), 342.50m),
            new Invoice("INV-1043", "Adventure Works", DateTime.Today.AddDays(30), 9120.00m),
        };
        ShowChatEntries(new TenantFeatures(null, Problem: null));
        Loaded += async (_, _) => ShowChatEntries(await NevaBridgeIntegration.GetTenantFeaturesAsync());
    }

    /// <summary>
    /// One entry per chat the features file switches on. The Generic Intake entry also needs the
    /// tenant's genericIntake gate, so it stays hidden while the gates are unknown (no settings
    /// yet, or NevaBridge unreachable), and the window says why, with a way to check again.
    /// </summary>
    private void ShowChatEntries(TenantFeatures tenant)
    {
        SampleFeatures file = NevaBridgeFeatureSwitches.File;
        bool intake = tenant.Gates is { } gates && NevaBridgeFeatureSwitches.GenericIntakeChat(gates);
        bool unknown = file.GenericIntakeChat && tenant.Problem is not null;
        FeaturesProblemText.Text = unknown ? $"The support assistant is hidden. {tenant.Problem}" : null;
        FeaturesProblem.Visibility = unknown ? Visibility.Visible : Visibility.Collapsed;
        Show(intake, IntakeChatButton, IntakeChatMenuItem);
        Show(file.ClassicChat, ClassicChatButton, ClassicChatMenuItem);
        Show(file.WebChat, WebChatButton, WebChatMenuItem);
    }

    private static void Show(bool visible, params UIElement[] entries)
    {
        foreach (UIElement entry in entries)
        {
            entry.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private async void OnOpenIntakeChat(object sender, RoutedEventArgs e)
    {
        IntakeChatWindow? window = await IntakeChatWindow.OpenAsync(this, screen: "Invoices");
        window?.Show();
    }

    private async void OnOpenSupport(object sender, RoutedEventArgs e)
    {
        SupportWindow? window = await SupportWindow.OpenAsync(this, screen: "Invoices");
        window?.Show();
    }

    private async void OnOpenNevaBridgeChat(object sender, RoutedEventArgs e)
    {
        await NevaBridgeChatWindow.ShowAsync(this, screen: "Invoices");
    }

    private async void OnNevaBridgeSettings(object sender, RoutedEventArgs e)
    {
        new NevaBridgeSettingsWindow { Owner = this }.ShowDialog();
        ShowChatEntries(await NevaBridgeIntegration.GetTenantFeaturesAsync());
    }

    private async void OnCheckFeaturesAgain(object sender, RoutedEventArgs e) =>
        ShowChatEntries(await NevaBridgeIntegration.GetTenantFeaturesAsync());

    private void OnExit(object sender, RoutedEventArgs e) => Close();
}
