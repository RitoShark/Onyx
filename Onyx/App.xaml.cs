using System.Windows;
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

            if (job.Revert is not null)
            {
                engine.Revert(job.Revert);
                return 0;
            }

            job.WriteJournal(engine.Apply(job.Plan!, job.Payload()));
            return 0;
        }
        catch (Exception)
        {
            return 1;
        }
    }
}
