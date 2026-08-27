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
        ("textures", "Texture Plugins", "M 5.0,3.0 h 14.0 a 2.0,2.0 0 0 1 2.0,2.0 v 14.0 a 2.0,2.0 0 0 1 -2.0,2.0 h -14.0 a 2.0,2.0 0 0 1 -2.0,-2.0 v -14.0 a 2.0,2.0 0 0 1 2.0,-2.0 z M 7.0,9.0 a 2.0,2.0 0 1 0 4.0,0 a 2.0,2.0 0 1 0 -4.0,0 m21 15-3.086-3.086a2 2 0 0 0-2.828 0L6 21"),
        ("dcc", "3D Software", "M21 8a2 2 0 0 0-1-1.73l-7-4a2 2 0 0 0-2 0l-7 4A2 2 0 0 0 3 8v8a2 2 0 0 0 1 1.73l7 4a2 2 0 0 0 2 0l7-4A2 2 0 0 0 21 16Z m3.3 7 8.7 5 8.7-5 M12 22V12"),
        ("system", "Windows", "M 4.0,4.0 h 16.0 a 2.0,2.0 0 0 1 2.0,2.0 v 12.0 a 2.0,2.0 0 0 1 -2.0,2.0 h -16.0 a 2.0,2.0 0 0 1 -2.0,-2.0 v -12.0 a 2.0,2.0 0 0 1 2.0,-2.0 z M10 4v4 M2 8h20 M6 4v4"),
        ("misc", "Misc", "M11 21.73a2 2 0 0 0 2 0l7-4A2 2 0 0 0 21 16V8a2 2 0 0 0-1-1.73l-7-4a2 2 0 0 0-2 0l-7 4A2 2 0 0 0 3 8v8a2 2 0 0 0 1 1.73z M12 22V12 M 3.29,7 L 12,12 L 20.71,7 m7.5 4.27 9 5.15")
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

    PluginRowViewModel? _detail;

    public Services Services { get; }
    public SheetViewModel Sheet { get; } = new();

    public PluginRowViewModel? Detail
    {
        get => _detail;
        private set
        {
            if (!Set(ref _detail, value)) return;
            Raise(nameof(IsDetailOpen));
        }
    }

    public bool IsDetailOpen => _detail is not null;

    public RelayCommand CloseDetailCommand => new(() =>
    {
        Detail = null;
        return Task.CompletedTask;
    });

    public void ShowDetail(PluginRowViewModel row) => Detail = row;
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

            if (Detail is not null)
                Detail = Groups.SelectMany(g => g.Rows).FirstOrDefault(r => r.PluginId == Detail.PluginId);

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
