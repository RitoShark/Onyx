using Onyx.Core;
using Onyx.Core.Catalog;
using Onyx.Core.Hosts;
using Onyx.Core.Install;
using Onyx.Core.Processes;
using Onyx.Core.Releases;
using Onyx.Core.State;

namespace Onyx.Tests;

public class PluginManagerTests : IDisposable
{
    readonly string _statePath = Path.Combine(Path.GetTempPath(), $"onyx-{Guid.NewGuid():N}.json");

    public void Dispose()
    {
        if (File.Exists(_statePath)) File.Delete(_statePath);
    }

    sealed class StaticDetector(string hostId, IReadOnlyList<HostInstance> instances) : IHostDetector
    {
        public string HostId => hostId;
        public IReadOnlyList<HostInstance> Detect() => instances;
    }

    sealed class FakeProcessTable(params string[] running) : IProcessTable
    {
        public IReadOnlyList<RunningProcess> Running(IReadOnlyList<string> names) =>
            running.Where(r => names.Contains(r, StringComparer.OrdinalIgnoreCase))
                   .Select((r, i) => new RunningProcess(i + 1, r))
                   .ToList();

        public void RequestClose(int pid) { }
    }

    sealed class FakeRegistrar : IRegistrar
    {
        public void Register(string dllPath) { }
        public void Unregister(string dllPath) { }
    }

    sealed class FakeChecksum : IChecksum
    {
        public string Sha256(string path) => "same";
        public string ReadExpected(string sidecarPath) => "same";
    }

    PluginManager Build(IReadOnlyList<HostInstance> hosts, params string[] running)
    {
        var detectors = hosts
            .GroupBy(h => h.HostId)
            .Select(g => (IHostDetector)new StaticDetector(g.Key, g.ToList()))
            .ToList();

        var handler = StubHandler.Json(
            Releases.GitHubClientTests.Fixture("releases-aventurine.json"));

        return new PluginManager(
            CatalogSource.Embedded(),
            new HostRegistry(detectors),
            new GitHubClient(new HttpClient(handler)),
            new PayloadFetcher(new HttpClient(handler)),
            new InstallEngine(new FakeFileSystem(), new FakeRegistrar(), new FakeChecksum()),
            new ProcessGuard(new FakeProcessTable(running)),
            StateStore.Load(_statePath));
    }

    static HostInstance Blender(string version) =>
        new("blender", version, $"Blender {version}", $@"C:\cfg\Blender\{version}");

    [Fact]
    public async Task Pairs_a_plugin_with_every_matching_host_instance()
    {
        var manager = Build([Blender("4.1"), Blender("4.3")]);

        var statuses = await manager.StatusAsync(default);

        Assert.Equal(2, statuses.Count(s => s.Plugin.Id == "aventurine-blender" && s.Host is not null));
    }

    [Fact]
    public async Task A_plugin_whose_host_is_absent_still_appears_with_no_host()
    {
        var manager = Build([Blender("4.3")]);

        var photoshop = statusFor(await manager.StatusAsync(default), "ritotex-photoshop");

        Assert.Null(photoshop.Host);
        Assert.False(photoshop.Installed);

        static PluginStatus statusFor(IReadOnlyList<PluginStatus> all, string id) =>
            all.Single(s => s.Plugin.Id == id);
    }

    [Fact]
    public async Task A_pinned_host_instance_never_pairs_with_another_version()
    {
        var manager = Build([new HostInstance("gimp", "2.10", "GIMP 2.10", @"C:\gimp\2.10")]);

        var statuses = await manager.StatusAsync(default);

        Assert.NotNull(statuses.Single(s => s.Plugin.Id == "tex-gimp2").Host);
        Assert.Null(statuses.Single(s => s.Plugin.Id == "tex-gimp3").Host);
    }

    [Fact]
    public async Task Every_catalog_plugin_produces_at_least_one_row()
    {
        var manager = Build([Blender("4.3")]);

        var statuses = await manager.StatusAsync(default);

        Assert.Equal(7, statuses.Select(s => s.Plugin.Id).Distinct().Count());
    }

    [Fact]
    public async Task Install_refuses_while_the_host_is_running()
    {
        var manager = Build([Blender("4.3")], running: "blender");

        var ex = await Assert.ThrowsAsync<HostRunningException>(
            () => manager.InstallAsync("aventurine-blender", "4.3", "3.1.5", default));

        Assert.Contains("Blender 4.3", ex.Message);
        Assert.Contains("blender", ex.ProcessNames);
    }

    [Fact]
    public void Uninstall_refuses_while_the_host_is_running()
    {
        var manager = Build([Blender("4.3")], running: "blender");

        Assert.Throws<HostRunningException>(() => manager.UninstallAsync("aventurine-blender", "4.3"));
    }

    [Fact]
    public async Task An_unrelated_running_application_does_not_block_an_install()
    {
        var manager = Build([Blender("4.3")], running: "chrome");

        var ex = await Record.ExceptionAsync(
            () => manager.InstallAsync("aventurine-blender", "4.3", "does-not-exist", default));

        Assert.IsType<PlanException>(ex);
    }

    [Fact]
    public async Task Installing_an_unknown_tag_names_the_plugin_in_the_error()
    {
        var manager = Build([Blender("4.3")]);

        var ex = await Assert.ThrowsAsync<PlanException>(
            () => manager.InstallAsync("aventurine-blender", "4.3", "99.0.0", default));

        Assert.Contains("Aventurine", ex.Message);
    }

    [Fact]
    public async Task Installing_into_an_undetected_host_fails_rather_than_guessing_a_path()
    {
        var manager = Build([Blender("4.3")]);

        await Assert.ThrowsAsync<PlanException>(
            () => manager.InstallAsync("aventurine-blender", "4.0", "3.1.5", default));
    }

    [Fact]
    public async Task Update_availability_follows_the_recorded_tag()
    {
        var state = StateStore.Load(_statePath);
        state.Record("aventurine-blender", "4.3", "3.1.2", InstallJournal.Empty);
        state.Save();

        var manager = Build([Blender("4.3")]);
        var status = (await manager.StatusAsync(default))
            .Single(s => s.Plugin.Id == "aventurine-blender" && s.Host is not null);

        Assert.Equal("3.1.2", status.InstalledTag);
        Assert.Equal("3.1.5", status.Latest!.Tag);
        Assert.True(status.UpdateAvailable);
    }
}
