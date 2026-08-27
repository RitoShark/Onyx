using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows.Threading;
using Onyx.Core.Install;
using Onyx.Core.Processes;

namespace Onyx.ViewModels;

public sealed class SectionViewModel(string title, string icon, IReadOnlyList<PluginRowViewModel> rows)
{
    public string Title { get; } = title;
    public string Icon { get; } = icon;
    public IReadOnlyList<PluginRowViewModel> Rows { get; } = rows;
}

public sealed class MainViewModel : ObservableObject
{
    static readonly TimeSpan CloseTimeout = TimeSpan.FromSeconds(30);

    static readonly (string Key, string Title, string Icon)[] Sections =
    [
        ("textures", "Texture Plugins", "M 2,3 H 14 V 13 H 2 Z M 2,10 L 6,6 L 9,9 L 11,7.5 L 14,10 M 11,5.5 A 0.9,0.9 0 1 0 11,5.49"),
        ("dcc", "3D Software", "M 8,1.5 L 14,5 V 11 L 8,14.5 L 2,11 V 5 Z M 2,5 L 8,8.5 L 14,5 M 8,8.5 V 14.5"),
        ("system", "Windows", "M 2,4 L 7.4,3.2 V 7.6 L 2,7.6 Z M 8.6,3 L 14,2.2 V 7.6 L 8.6,7.6 Z M 2,8.6 L 7.4,8.6 V 13 L 2,12.2 Z M 8.6,8.6 L 14,8.6 V 14 L 8.6,13.2 Z"),
        ("misc", "Misc", "M 2,4.5 H 14 M 2,8 H 14 M 2,11.5 H 9")
    ];

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
    public ObservableCollection<SectionViewModel> Groups { get; } = [];
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

            Groups.Clear();
            foreach (var (key, title, icon) in Sections)
            {
                var rows = statuses
                    .Where(s => s.Plugin.Category == key)
                    .Select(s => new PluginRowViewModel(this, s))
                    .ToList();

                if (rows.Count > 0)
                    Groups.Add(new SectionViewModel(title, icon, rows));
            }

            var known = Sections.Select(s => s.Key).ToHashSet();
            var stray = statuses
                .Where(s => !known.Contains(s.Plugin.Category))
                .Select(s => new PluginRowViewModel(this, s))
                .ToList();

            if (stray.Count > 0)
                Groups.Add(new SectionViewModel("Other", Sections[^1].Icon, stray));

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
