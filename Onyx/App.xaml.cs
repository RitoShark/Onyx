using System.Windows;
using Microsoft.Win32;
using Onyx.ViewModels;

namespace Onyx;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        ApplyTheme();

        var services = Services.Build();
        var model = new MainViewModel(services);

        var window = new MainWindow { DataContext = model };
        window.Show();

        _ = model.RefreshAsync(fromNetwork: true);
    }

    void ApplyTheme()
    {
        var source = SystemUsesLightTheme() ? "Theme/Light.xaml" : "Theme/Dark.xaml";

        Resources.MergedDictionaries[0] = new ResourceDictionary
        {
            Source = new Uri(source, UriKind.Relative)
        };
    }

    static bool SystemUsesLightTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");

            return key?.GetValue("AppsUseLightTheme") is not int value || value != 0;
        }
        catch (Exception)
        {
            return true;
        }
    }
}
