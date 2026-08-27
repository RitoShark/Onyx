using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using Onyx.Core;
using Onyx.Core.Releases;

namespace Onyx.ViewModels;

public sealed class VersionChoice(Release release)
{
    public Release Release { get; } = release;
    public string Tag => Release.Tag;

    public string Display => Release.PreRelease
        ? $"{Release.Tag}  ·  {Release.PublishedAt.LocalDateTime:d MMM yyyy}  ·  pre-release"
        : $"{Release.Tag}  ·  {Release.PublishedAt.LocalDateTime:d MMM yyyy}";
}

public sealed class PluginRowViewModel : ObservableObject
{
    readonly MainViewModel _owner;
    PluginStatus _status;
    VersionChoice? _selectedVersion;
    bool _isExpanded;
    bool _isBusy;

    public PluginRowViewModel(MainViewModel owner, PluginStatus status)
    {
        _owner = owner;
        _status = status;

        Versions = new ObservableCollection<VersionChoice>(status.Releases.Select(r => new VersionChoice(r)));
        _selectedVersion = Versions.FirstOrDefault(v => v.Tag == status.InstalledTag) ?? Versions.FirstOrDefault();

        InstallCommand = new RelayCommand(InstallAsync, () => CanAct);
        UninstallCommand = new RelayCommand(UninstallAsync, () => Installed && !IsBusy);
        RevealCommand = new RelayCommand(Reveal, () => Installed);
        ToggleCommand = new RelayCommand(() =>
        {
            IsExpanded = !IsExpanded;
            return Task.CompletedTask;
        });
    }

    public string Key => $"{_status.Plugin.Id}|{_status.Host?.InstanceId}";
    public string Name => _status.Plugin.Name;
    public string Summary => _status.Plugin.Summary;
    public string Repo => _status.Plugin.Repo;

    public string HostLabel => _status.Host is null
        ? $"{HostDisplayName(_status.Plugin.Host)} not found"
        : _status.Host.Label;

    public string HostPath => _status.Host is null ? "" : "  ·  " + _status.Host.Path;

    public string IconGeometry => _status.Plugin.Host switch
    {
        "photoshop" => "M 2,3 H 14 V 15 H 2 Z M 5,11 V 7 H 7.5 A 2,2 0 0 1 7.5,11 Z",
        "paintnet" => "M 3,13 L 9,3 L 13,13 Z M 5.5,9.5 H 12",
        "gimp" => "M 8,2 C 3.5,4 2,7 2,10 A 6,6 0 0 0 14,10 C 14,7 12,5 9,2 Z",
        "maya" => "M 2,13 L 5,3 L 8,10 L 11,3 L 14,13",
        "blender" => "M 8,2 A 6,6 0 1 0 8,14 A 6,6 0 0 0 8,2 M 5,9 A 3,3 0 1 0 11,9 A 3,3 0 0 0 5,9",
        "thumbnails" => "M 2,3 H 14 V 13 H 2 Z M 2,10 L 6,6 L 9,9 L 11,7.5 L 14,10",
        _ => "M 3,10 L 8,4 L 13,10"
    };
    public bool HasHost => _status.Host is not null;
    public bool Installed => _status.Installed;
    public string? InstalledTag => _status.InstalledTag;
    public string? Problem => _status.Problem;

    public string VersionChip => _status.InstalledTag ?? _status.Latest?.Tag ?? "—";

    public string PrimaryActionLabel =>
        !Installed ? "Install" :
        _status.UpdateAvailable ? "Update" :
        SelectedIsInstalled ? "Reinstall" : "Switch";

    public bool ShowsUpdateBadge => _status.UpdateAvailable;

    public bool CanAct => HasHost && Versions.Count > 0 && !IsBusy;

    public ObservableCollection<VersionChoice> Versions { get; }

    public VersionChoice? SelectedVersion
    {
        get => _selectedVersion;
        set
        {
            if (!Set(ref _selectedVersion, value)) return;
            Raise(nameof(ReleaseNotes));
            Raise(nameof(PrimaryActionLabel));
        }
    }

    public string ReleaseNotes
    {
        get
        {
            var body = SelectedVersion?.Release.Body?.Trim();
            return string.IsNullOrEmpty(body) ? "No release notes." : Strip(body);
        }
    }

    public bool IsExpanded
    {
        get => _isExpanded;
        set => Set(ref _isExpanded, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (!Set(ref _isBusy, value)) return;
            Raise(nameof(CanAct));
            InstallCommand.Refresh();
            UninstallCommand.Refresh();
        }
    }

    public RelayCommand InstallCommand { get; }
    public RelayCommand UninstallCommand { get; }
    public RelayCommand RevealCommand { get; }
    public RelayCommand ToggleCommand { get; }

    bool SelectedIsInstalled => SelectedVersion?.Tag == _status.InstalledTag;

    async Task InstallAsync()
    {
        if (_status.Host is null || SelectedVersion is null) return;

        IsBusy = true;
        try
        {
            await _owner.RunGuardedAsync(
                _status.Plugin.Host,
                HostLabel,
                () => _owner.Services.Manager.InstallAsync(
                    _status.Plugin.Id, _status.Host.InstanceId, SelectedVersion.Tag, CancellationToken.None));
        }
        finally
        {
            IsBusy = false;
        }
    }

    async Task UninstallAsync()
    {
        if (_status.Host is null) return;

        IsBusy = true;
        try
        {
            await _owner.RunGuardedAsync(
                _status.Plugin.Host,
                HostLabel,
                () =>
                {
                    _owner.Services.Manager.UninstallAsync(_status.Plugin.Id, _status.Host.InstanceId);
                    return Task.CompletedTask;
                });
        }
        finally
        {
            IsBusy = false;
        }
    }

    Task Reveal()
    {
        var path = _status.Host?.Path;
        if (path is not null && Directory.Exists(path))
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });

        return Task.CompletedTask;
    }

    static string HostDisplayName(string hostId) => hostId switch
    {
        "photoshop" => "Photoshop",
        "paintnet" => "Paint.NET",
        "gimp" => "GIMP",
        "maya" => "Maya",
        "blender" => "Blender",
        "thumbnails" => "Windows Explorer",
        _ => hostId
    };

    static string Strip(string markdown)
    {
        var lines = markdown.Replace("\r", "").Split('\n')
            .Select(line => line.TrimStart('#', ' ', '\t').Replace("**", "").Replace("`", ""))
            .ToList();

        return string.Join('\n', lines).Trim();
    }
}
