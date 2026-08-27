using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows.Threading;
using Onyx.Core.Install;
using Onyx.Core.Processes;

namespace Onyx.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    static readonly TimeSpan CloseTimeout = TimeSpan.FromSeconds(30);

    readonly DispatcherTimer _refreshTimer;
    string _statusMessage = "";
    string _lastChecked = "";
    bool _isBusy;

    public MainViewModel(Services services)
    {
        Services = services;
        RefreshCommand = new RelayCommand(() => RefreshAsync(fromNetwork: true), () => !IsBusy);

        _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromHours(6) };
        _refreshTimer.Tick += async (_, _) => await RefreshAsync(fromNetwork: true);
        _refreshTimer.Start();
    }

    public Services Services { get; }
    public SheetViewModel Sheet { get; } = new();
    public ObservableCollection<PluginRowViewModel> Rows { get; } = [];
    public RelayCommand RefreshCommand { get; }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => Set(ref _statusMessage, value);
    }

    public string LastChecked
    {
        get => _lastChecked;
        private set => Set(ref _lastChecked, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (!Set(ref _isBusy, value)) return;
            RefreshCommand.Refresh();
        }
    }

    public async Task RefreshAsync(bool fromNetwork)
    {
        IsBusy = true;
        StatusMessage = "";

        try
        {
            if (fromNetwork)
            {
                Services.Manager.ForgetCachedReleases();

                try
                {
                    await Services.RefreshCatalogAsync(CancellationToken.None);
                }
                catch (Exception)
                {
                }
            }

            var statuses = await Services.Manager.StatusAsync(CancellationToken.None);

            Rows.Clear();
            foreach (var status in statuses)
                Rows.Add(new PluginRowViewModel(this, status));

            var problem = statuses.Select(s => s.Problem).FirstOrDefault(p => p is not null);
            if (problem is not null) StatusMessage = problem;

            LastChecked = $"Last checked {DateTime.Now:HH:mm}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task RunGuardedAsync(string hostId, string hostLabel, Func<Task> action)
    {
        try
        {
            await action();
            await RefreshAsync(fromNetwork: false);
        }
        catch (HostRunningException)
        {
            ShowCloseHostSheet(hostId, hostLabel, action);
        }
        catch (FileLockedException e)
        {
            ShowLockedFileSheet(hostId, hostLabel, e, action);
        }
        catch (Exception e)
        {
            StatusMessage = e is PlanException ? e.Message : $"{hostLabel}: {e.Message}";
        }
    }

    void ShowCloseHostSheet(string hostId, string hostLabel, Func<Task> action) =>
        Sheet.Show(
            $"{hostLabel} is open",
            $"{hostLabel} has to close before Onyx can change its plugin files. Nothing is forced — save your work first.",
            $"Close {hostLabel}",
            async () =>
            {
                if (!await Services.Guard.CloseAndWaitAsync(hostId, CloseTimeout, CancellationToken.None))
                {
                    Sheet.Message = $"{hostLabel} is still open. Close it yourself, then try again.";
                    return;
                }

                Sheet.Close();
                await RunGuardedAsync(hostId, hostLabel, action);
            });

    void ShowLockedFileSheet(string hostId, string hostLabel, FileLockedException locked, Func<Task> action)
    {
        var explorer = hostId == "thumbnails";

        Sheet.Show(
            explorer ? "Windows Explorer is holding the old handler open" : "A file is in use",
            explorer
                ? "Explorer still has the current thumbnail handler loaded, so it has to restart before the new one can be written. Your folder windows will reopen."
                : $"{System.IO.Path.GetFileName(locked.Path)} is open in another program. Close it, then try again.",
            explorer ? "Restart Explorer" : "Try again",
            async () =>
            {
                if (explorer) await RestartExplorerAsync();

                Sheet.Close();
                await RunGuardedAsync(hostId, hostLabel, action);
            });
    }

    static async Task RestartExplorerAsync()
    {
        foreach (var process in Process.GetProcessesByName("explorer"))
        {
            try { process.Kill(); }
            catch (Exception) { }
        }

        await Task.Delay(1200);

        if (Process.GetProcessesByName("explorer").Length == 0)
            Process.Start(new ProcessStartInfo("explorer.exe") { UseShellExecute = true });
    }
}
