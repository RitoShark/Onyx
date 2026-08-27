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
        target.External ? "found" : target.InstalledTag ?? "not installed";

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
        DetailsCommand = new RelayCommand(() =>
        {
            _owner.ShowDetail(this);
            return Task.CompletedTask;
        });
    }

    public string PluginId => _status.Plugin.Id;
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

    public System.Windows.Media.ImageSource? IconImage =>
        IconStore.For(_status.Plugin.Host, _status.Targets.Select(t => t.Host.ExePath).FirstOrDefault(e => e is not null));

    public bool HasIconImage => IconImage is not null;

    public string IconGeometry => _status.Plugin.Host switch
    {
        "photoshop" => "M12 22a1 1 0 0 1 0-20 10 9 0 0 1 10 9 5 5 0 0 1-5 5h-2.25a1.75 1.75 0 0 0-1.4 2.8l.3.4a1.75 1.75 0 0 1-1.4 2.8z M 13.0,6.5 a 0.5,0.5 0 1 0 1.0,0 a 0.5,0.5 0 1 0 -1.0,0 M 17.0,10.5 a 0.5,0.5 0 1 0 1.0,0 a 0.5,0.5 0 1 0 -1.0,0 M 6.0,12.5 a 0.5,0.5 0 1 0 1.0,0 a 0.5,0.5 0 1 0 -1.0,0 M 8.0,7.5 a 0.5,0.5 0 1 0 1.0,0 a 0.5,0.5 0 1 0 -1.0,0",
        "paintnet" => "m14.622 17.897-10.68-2.913 M18.376 2.622a1 1 0 1 1 3.002 3.002L17.36 9.643a.5.5 0 0 0 0 .707l.944.944a2.41 2.41 0 0 1 0 3.408l-.944.944a.5.5 0 0 1-.707 0L8.354 7.348a.5.5 0 0 1 0-.707l.944-.944a2.41 2.41 0 0 1 3.408 0l.944.944a.5.5 0 0 0 .707 0z M9 8c-1.804 2.71-3.97 3.46-6.583 3.948a.507.507 0 0 0-.302.819l7.32 8.883a1 1 0 0 0 1.185.204C12.735 20.405 16 16.792 16 15",
        "gimp" => "m11 10 3 3 M6.5 21A3.5 3.5 0 1 0 3 17.5a2.62 2.62 0 0 1-.708 1.792A1 1 0 0 0 3 21z M9.969 17.031 21.378 5.624a1 1 0 0 0-3.002-3.002L6.967 14.031",
        "maya" => "M21 8a2 2 0 0 0-1-1.73l-7-4a2 2 0 0 0-2 0l-7 4A2 2 0 0 0 3 8v8a2 2 0 0 0 1 1.73l7 4a2 2 0 0 0 2 0l7-4A2 2 0 0 0 21 16Z m3.3 7 8.7 5 8.7-5 M12 22V12",
        "blender" => "M20.341 6.484A10 10 0 0 1 10.266 21.85 M3.659 17.516A10 10 0 0 1 13.74 2.152 M 9.0,12.0 a 3.0,3.0 0 1 0 6.0,0 a 3.0,3.0 0 1 0 -6.0,0 M 17.0,5.0 a 2.0,2.0 0 1 0 4.0,0 a 2.0,2.0 0 1 0 -4.0,0 M 3.0,19.0 a 2.0,2.0 0 1 0 4.0,0 a 2.0,2.0 0 1 0 -4.0,0",
        "thumbnails" => "m22 11-1.296-1.296a2.4 2.4 0 0 0-3.408 0L11 16 M4 8a2 2 0 0 0-2 2v10a2 2 0 0 0 2 2h10a2 2 0 0 0 2-2 M 12.0,7.0 a 1.0,1.0 0 1 0 2.0,0 a 1.0,1.0 0 1 0 -2.0,0 M 10.0,2.0 h 10.0 a 2.0,2.0 0 0 1 2.0,2.0 v 10.0 a 2.0,2.0 0 0 1 -2.0,2.0 h -10.0 a 2.0,2.0 0 0 1 -2.0,-2.0 v -10.0 a 2.0,2.0 0 0 1 2.0,-2.0 z",
        "hematite" => "M12 19h8 m4 17 6-6-6-6",
        _ => "M11 21.73a2 2 0 0 0 2 0l7-4A2 2 0 0 0 21 16V8a2 2 0 0 0-1-1.73l-7-4a2 2 0 0 0-2 0l-7 4A2 2 0 0 0 3 8v8a2 2 0 0 0 1 1.73z M12 22V12 M 3.29,7 L 12,12 L 20.71,7 m7.5 4.27 9 5.15"
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
    public RelayCommand DetailsCommand { get; }

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
