using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using Microsoft.Win32;
using Onyx.Core;
using Onyx.Core.Hosts;
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

public sealed class TargetViewModel(PluginTarget target)
{
    public string Label => target.Host.Label;
    public string Path => target.Host.Path;

    public string Status =>
        target.External ? "detected" : target.InstalledTag ?? "not installed";

    public bool Installed => target.InstalledTag is not null;

    public RelayCommand OpenCommand { get; } = new(() =>
    {
        if (Directory.Exists(target.Host.Path))
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{target.Host.Path}\"") { UseShellExecute = true });
        return Task.CompletedTask;
    });
}

public sealed class PluginRowViewModel : ObservableObject
{
    readonly MainViewModel _owner;
    readonly PluginStatus _status;
    VersionChoice? _selectedVersion;
    bool _isExpanded;
    bool _isBusy;

    public PluginRowViewModel(MainViewModel owner, PluginStatus status)
    {
        _owner = owner;
        _status = status;

        Versions = new ObservableCollection<VersionChoice>(status.Releases.Select(r => new VersionChoice(r)));
        _selectedVersion = Versions.FirstOrDefault(v => v.Tag == status.InstalledTag) ?? Versions.FirstOrDefault();

        Targets = new ObservableCollection<TargetViewModel>(status.Targets.Select(t => new TargetViewModel(t)));

        InstallCommand = new RelayCommand(InstallAsync, () => CanAct);
        UninstallCommand = new RelayCommand(UninstallAsync, () => Installed && !IsBusy);
        ChooseFolderCommand = new RelayCommand(ChooseFolderAsync, () => !IsBusy);
        ToggleCommand = new RelayCommand(() =>
        {
            IsExpanded = !IsExpanded;
            return Task.CompletedTask;
        });
    }

    public string Name => _status.Plugin.Name;
    public string Summary => _status.Plugin.Summary;
    public string Category => _status.Plugin.Category;

    public ObservableCollection<TargetViewModel> Targets { get; }

    public string HostLabel => _status.Targets.Count switch
    {
        0 => $"{HostDisplayName(_status.Plugin.Host)} not found",
        1 => _status.Targets[0].Host.Label,
        _ => string.Join("  ·  ", _status.Targets.Select(t => t.Host.Label))
    };

    public string HostPath =>
        _status.Targets.Count == 1 ? "  ·  " + _status.Targets[0].Host.Path : "";

    public string IconGeometry => _status.Plugin.Host switch
    {
        "photoshop" => "M 2,3 H 14 V 15 H 2 Z M 5,11 V 7 H 7.5 A 2,2 0 0 1 7.5,11 Z",
        "paintnet" => "M 3,13 L 9,3 L 13,13 Z M 5.5,9.5 H 12",
        "gimp" => "M 8,2 C 3.5,4 2,7 2,10 A 6,6 0 0 0 14,10 C 14,7 12,5 9,2 Z",
        "maya" => "M 2,13 L 5,3 L 8,10 L 11,3 L 14,13",
        "blender" => "M 8,2 A 6,6 0 1 0 8,14 A 6,6 0 0 0 8,2 M 5,9 A 3,3 0 1 0 11,9 A 3,3 0 0 0 5,9",
        "thumbnails" => "M 2,3 H 14 V 13 H 2 Z M 2,10 L 6,6 L 9,9 L 11,7.5 L 14,10",
        "hematite" => "M 2,4 H 14 V 12 H 2 Z M 4,7 L 6,9 L 4,11 M 8,11 H 12",
        _ => "M 3,10 L 8,4 L 13,10"
    };

    public bool HasHost => _status.HasHost;
    public bool Installed => _status.Installed;

    public string VersionChip => _status.InstalledTag ?? _status.Latest?.Tag ?? "—";

    public string PrimaryActionLabel =>
        !Installed ? "Install" :
        _status.UpdateAvailable ? "Update" :
        SelectedIsInstalled ? "Reinstall" : "Switch";

    public bool ShowsUpdateBadge => _status.UpdateAvailable;

    public bool CanAct => HasHost && Versions.Count > 0 && !IsBusy;

    public bool ShowsChooseFolder => _status.Targets.Count <= 1;

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
    public RelayCommand ChooseFolderCommand { get; }
    public RelayCommand ToggleCommand { get; }

    bool SelectedIsInstalled => SelectedVersion?.Tag == _status.InstalledTag;

    string HostDisplay => HostDisplayName(_status.Plugin.Host);

    async Task InstallAsync()
    {
        if (SelectedVersion is null) return;

        IsBusy = true;
        try
        {
            await _owner.RunGuardedAsync(
                _status.Plugin.Host,
                HostDisplay,
                () => _owner.Services.Manager.InstallAsync(
                    _status.Plugin.Id, SelectedVersion.Tag, CancellationToken.None));
        }
        finally
        {
            IsBusy = false;
        }
    }

    async Task UninstallAsync()
    {
        IsBusy = true;
        try
        {
            await _owner.RunGuardedAsync(
                _status.Plugin.Host,
                HostDisplay,
                () =>
                {
                    _owner.Services.Manager.UninstallAll(_status.Plugin.Id);
                    return Task.CompletedTask;
                });
        }
        finally
        {
            IsBusy = false;
        }
    }

    async Task ChooseFolderAsync()
    {
        var dialog = new OpenFolderDialog
        {
            Title = $"Choose the {HostDisplay} folder",
            InitialDirectory = _status.Targets.FirstOrDefault()?.Host.Path ?? ""
        };

        if (dialog.ShowDialog() != true) return;

        var instanceId = _status.Targets.FirstOrDefault()?.Host.InstanceId ?? HostOverrides.ManualInstance;
        _owner.Services.Manager.Hosts.Overrides.Set(_status.Plugin.Host, instanceId, dialog.FolderName);

        await _owner.RefreshAsync(fromNetwork: false);
    }

    static string HostDisplayName(string hostId) => hostId switch
    {
        "photoshop" => "Photoshop",
        "paintnet" => "Paint.NET",
        "gimp" => "GIMP",
        "maya" => "Maya",
        "blender" => "Blender",
        "thumbnails" => "Windows Explorer",
        "hematite" => "Hematite",
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
