using System.Windows;
using NevaBridge.Sample.Client;

namespace NevaBridge.Sample.DesktopApp;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        try
        {
            NevaBridgeFeatureSwitches.Load();
        }
        catch (SampleFeaturesException error)
        {
            // A broken features file would silently hide chats; say what is wrong instead.
            MessageBox.Show(error.Message, "NevaBridge features file", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
            return;
        }

        new MainWindow().Show();
    }
}
