using System.Windows;
using Microsoft.Win32;
using Onyx.Core.Install;
using Onyx.Core.Platform;
using Onyx.ViewModels;

namespace Onyx;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        if (e.Args is ["--apply", var jobPath])
        {
            Shutdown(RunElevatedJob(jobPath));
            return;
        }

        ApplyTheme();

        var services = Services.Build();
        var model = new MainViewModel(services);

        var window = new MainWindow { DataContext = model };
        window.Show();

        _ = model.RefreshAsync(fromNetwork: true);
    }

    static int RunElevatedJob(string jobPath)
    {
        try
        {
            var job = ElevatedJob.Read(jobPath);

            var engine = new InstallEngine(
                new PhysicalFileSystem(), new Regsvr32Registrar(), new FileChecksum());

            job.WriteJournal(engine.Apply(job.Plan, job.Payload()));
            return 0;
        }
        catch (Exception)
        {
            return 1;
        }
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
