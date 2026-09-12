using System.Diagnostics;
using Onyx.Core.Releases;

namespace Onyx.ViewModels;

public sealed class AppUpdateViewModel(Func<CancellationToken, Task<IReadOnlyList<Release>>> fetch) : ObservableObject
{
    static readonly Version Current = typeof(AppUpdateViewModel).Assembly.GetName().Version ?? new Version(1, 0, 0);
    string _status = "Updates not checked";
    string _detail = "Check for updates to Onyx.";
    bool _checking;
    AppRelease? _available;
    RelayCommand? _command;

    public string VersionLabel => $"v{Current.ToString(3)}";
    public string Status { get => _status; private set => Set(ref _status, value); }
    public string Detail { get => _detail; private set => Set(ref _detail, value); }
    public string ActionLabel => _available is null ? "Check for updates" : $"Download {_available.Tag}";
    public RelayCommand ActionCommand => _command ??= new RelayCommand(ActAsync, () => !_checking);

    public async Task CheckAsync()
    {
        if (_checking) return;
        _checking = true;
        ActionCommand.Refresh();
        Status = "Checking for updates...";

        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            var latest = AppRelease.Latest(await fetch(timeout.Token));
            _available = latest?.IsNewerThan(Current) == true ? latest : null;
            Status = latest is null ? "No published version found" : _available is null ? "Up to date" : "Update available";
            Detail = _available is null ? "Check for updates to Onyx." : $"Open the Onyx {_available.Tag} release to download the update.";
        }
        catch (Exception)
        {
            Status = _available is null ? "Update check unavailable" : "Update available";
            Detail = "Could not reach Onyx releases. The repository may be unavailable or your connection may be offline.";
        }
        finally
        {
            _checking = false;
            Raise(nameof(ActionLabel));
            ActionCommand.Refresh();
        }
    }

    async Task ActAsync()
    {
        if (_available is null)
        {
            await CheckAsync();
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(_available.Url) { UseShellExecute = true });
        }
        catch (Exception)
        {
            Status = "Could not open browser";
            Detail = _available.Url;
        }
    }
}
