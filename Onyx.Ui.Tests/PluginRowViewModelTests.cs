using Onyx.Core;
using Onyx.Core.Catalog;
using Onyx.Core.Hosts;
using Onyx.Core.Releases;
using Onyx.ViewModels;

namespace Onyx.Ui.Tests;

public sealed class PluginRowViewModelTests
{
    static readonly Release Installed = new("v1.0.0", DateTimeOffset.UtcNow.AddDays(-2), false, "", []);
    static readonly Release Latest = new("v1.1.0", DateTimeOffset.UtcNow.AddDays(-1), false, "", []);
    static readonly Release Preview = new("v2.0.0-beta", DateTimeOffset.UtcNow, true, "", []);

    static PluginRowViewModel Row(string? installedTag, Release? latest, params Release[] releases)
    {
        var plugin = CatalogSource.Embedded().Plugins[0];
        var target = new PluginTarget(new HostInstance(plugin.Host, "test", "Test host", "C:\\Test"),
            installedTag, ReleaseSet.UpdateAvailable(installedTag, latest), false);
        return new PluginRowViewModel(null!, new PluginStatus(plugin, [target], latest, releases, null));
    }

    [Fact]
    public void Update_targets_latest_stable_even_when_installed_and_prerelease_come_first()
    {
        var row = Row(Installed.Tag, Latest, Installed, Preview, Latest);

        Assert.Same(Latest, row.SelectedVersion!.Release);
        Assert.Equal("Update", row.PrimaryActionLabel);
        Assert.True(row.InstallCommand.CanExecute(null));
        Assert.Equal(Installed.Tag, row.VersionChip);
    }

    [Fact]
    public void Manual_reinstall_and_version_switch_have_accurate_labels()
    {
        var row = Row(Installed.Tag, Latest, Installed, Preview, Latest);

        row.SelectedVersion = row.Versions.Single(v => v.Tag == Installed.Tag);
        Assert.Equal("Reinstall", row.PrimaryActionLabel);
        row.SelectedVersion = row.Versions.Single(v => v.Tag == Preview.Tag);
        Assert.Equal("Switch", row.PrimaryActionLabel);
        row.SelectedVersion = row.Versions.Single(v => v.Tag == Latest.Tag);
        Assert.Equal("Update", row.PrimaryActionLabel);
    }

    [Fact]
    public void Fresh_install_defaults_to_stable_instead_of_first_prerelease()
    {
        var row = Row(null, Latest, Preview, Latest);

        Assert.Same(Latest, row.SelectedVersion!.Release);
        Assert.Equal("Install", row.PrimaryActionLabel);
    }

    [Fact]
    public void Current_install_is_a_reinstall()
    {
        var row = Row(Latest.Tag, Latest, Preview, Latest);

        Assert.Equal("Reinstall", row.PrimaryActionLabel);
    }

    [Fact]
    public void Missing_latest_falls_back_to_installed_release()
    {
        var row = Row(Installed.Tag, null, Preview, Installed);

        Assert.Same(Installed, row.SelectedVersion!.Release);
        Assert.Equal("Reinstall", row.PrimaryActionLabel);
    }

    [Fact]
    public void Missing_releases_disable_install()
    {
        var row = Row(Installed.Tag, null);

        Assert.Null(row.SelectedVersion);
        Assert.False(row.InstallCommand.CanExecute(null));
    }
}
