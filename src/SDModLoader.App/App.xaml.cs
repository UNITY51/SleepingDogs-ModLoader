using System.Windows;

namespace SDModLoader.App;

public partial class App : Application
{
    public App()
    {
        DispatcherUnhandledException += (_, e) =>
        {
            MessageBox.Show(e.Exception.Message, Loc.T("Error"), MessageBoxButton.OK, MessageBoxImage.Error);
            e.Handled = true;
        };
    }
}
