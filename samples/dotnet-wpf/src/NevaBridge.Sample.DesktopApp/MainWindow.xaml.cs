using System.Windows;

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

    private void OnNevaBridgeSettings(object sender, RoutedEventArgs e)
    {
        new NevaBridgeSettingsWindow { Owner = this }.ShowDialog();
    }

    private void OnExit(object sender, RoutedEventArgs e) => Close();
}
