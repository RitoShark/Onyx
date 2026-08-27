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

    sealed class StaticProbe(string pluginId, string? found) : IPresenceProbe
    {
        public string PluginId => pluginId;
        public string? DetectInstalled(string hostPath) => found;
    }

    PluginManager Build(
        IReadOnlyList<HostInstance> hosts,
        string[]? running = null,
        IReadOnlyList<IPresenceProbe>? probes = null,
        IFileSystem? fs = null)
    {
        var detectors = hosts
            .GroupBy(h => h.HostId)
            .Select(g => (IHostDetector)new StaticDetector(g.Key, g.ToList()))
            .ToList();

        var handler = new StubHandler(request =>
            request.RequestUri!.Host == "api.github.com"
                ? new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent(Releases.GitHubClientTests.Fixture("releases-aventurine.json"))
                }
                : new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(TinyZip())
                });

        return new PluginManager(
            CatalogSource.Embedded(),
            new HostRegistry(detectors),
            new GitHubClient(new HttpClient(handler)),
            new PayloadFetcher(new HttpClient(handler)),
            new InstallEngine(fs ?? new FakeFileSystem(), new FakeRegistrar(), new FakeChecksum()),
            new ProcessGuard(new FakeProcessTable(running ?? [])),
            StateStore.Load(_statePath),
            fs ?? new FakeFileSystem(),
            probes: probes);
    }

    static HostInstance Blender(string version) =>
        new("blender", version, $"Blender {version}", $@"C:\cfg\Blender\{version}");

    static byte[] TinyZip()
    {
        using var buffer = new MemoryStream();
        using (var archive = new System.IO.Compression.ZipArchive(buffer, System.IO.Compression.ZipArchiveMode.Create, leaveOpen: true))
        {
            using var writer = new StreamWriter(archive.CreateEntry("Aventurine/__init__.py").Open());
            writer.Write("x");
        }
        return buffer.ToArray();
    }

    [Fact]
    public async Task A_plugin_gets_one_status_with_a_target_per_host_instance()
    {
        var manager = Build([Blender("4.1"), Blender("4.3")]);

        var status = (await manager.StatusAsync(default)).Single(s => s.Plugin.Id == "aventurine-blender");

        Assert.Equal(["4.1", "4.3"], status.Targets.Select(t => t.Host.InstanceId));
    }

    [Fact]
    public async Task A_plugin_whose_host_is_absent_still_appears_with_no_targets()
    {
        var manager = Build([Blender("4.3")]);

        var photoshop = (await manager.StatusAsync(default)).Single(s => s.Plugin.Id == "ritotex-photoshop");

        Assert.False(photoshop.HasHost);
        Assert.False(photoshop.Installed);
    }

    [Fact]
    public async Task Gimp_targets_every_supported_version_from_one_row()
    {
        var manager = Build(
        [
            new HostInstance("gimp", "2.10", "GIMP 2.10", @"C:\gimp\2.10"),
            new HostInstance("gimp", "3.0", "GIMP 3.0", @"C:\gimp\3.0")
        ]);

        var gimp = (await manager.StatusAsync(default)).Single(s => s.Plugin.Id == "tex-gimp");

        Assert.Equal(["2.10", "3.0"], gimp.Targets.Select(t => t.Host.InstanceId));
    }

    [Fact]
    public async Task An_unsupported_gimp_version_is_not_a_target()
    {
        var manager = Build(
        [
            new HostInstance("gimp", "3.0", "GIMP 3.0", @"C:\gimp\3.0"),
            new HostInstance("gimp", "4.0", "GIMP 4.0", @"C:\gimp\4.0")
        ]);

        var gimp = (await manager.StatusAsync(default)).Single(s => s.Plugin.Id == "tex-gimp");

        Assert.Equal(["3.0"], gimp.Targets.Select(t => t.Host.InstanceId));
    }

    [Fact]
    public async Task Every_catalog_plugin_produces_exactly_one_status()
    {
        var manager = Build([Blender("4.0"), Blender("4.1"), Blender("4.3")]);

        var statuses = await manager.StatusAsync(default);

        Assert.Equal(8, statuses.Count);
        Assert.Equal(8, statuses.Select(s => s.Plugin.Id).Distinct().Count());
    }

    [Fact]
    public async Task Install_refuses_while_the_host_is_running()
    {
        var manager = Build([Blender("4.3")], running: ["blender"]);

        var ex = await Assert.ThrowsAsync<HostRunningException>(
            () => manager.InstallAsync("aventurine-blender", "3.1.5", default));

        Assert.Contains("blender", ex.ProcessNames);
    }

    [Fact]
    public async Task UninstallAll_refuses_while_the_host_is_running()
    {
        var state = StateStore.Load(_statePath);
        state.Record("aventurine-blender", "4.3", "3.1.5", InstallJournal.Empty);
        state.Save();

        var manager = Build([Blender("4.3")], running: ["blender"]);

        await Assert.ThrowsAsync<HostRunningException>(() => manager.UninstallAll("aventurine-blender", default));
    }

    static HostInstance Thumbs() =>
        new("thumbnails", "default", "Windows Explorer", @"C:\local\RitoShark\TexThumbnailProvider");

    static HostInstance LtkThumbs() =>
        new("ltkthumbs", "default", "Windows Explorer", @"C:\pf\LeagueToolkit\ltk-tex-thumb-handler");

    [Fact]
    public async Task An_installed_thumbnail_provider_blocks_the_alternative()
    {
        var state = StateStore.Load(_statePath);
        state.Record("tex-thumbnails", "default", "v1.1.0", InstallJournal.Empty);
        state.Save();

        var statuses = await Build([Thumbs(), LtkThumbs()]).StatusAsync(default);

        Assert.Equal("Texture Thumbnails", statuses.Single(s => s.Plugin.Id == "ltk-tex-thumbnails").BlockedBy);
        Assert.Null(statuses.Single(s => s.Plugin.Id == "tex-thumbnails").BlockedBy);
    }

    [Fact]
    public async Task An_externally_detected_provider_blocks_the_alternative_too()
    {
        var fs = new FakeFileSystem()
            .WithFile(@"C:\pf\LeagueToolkit\ltk-tex-thumb-handler\ltk-tex-thumb-handler.dll");

        var statuses = await Build([Thumbs(), LtkThumbs()], fs: fs).StatusAsync(default);

        Assert.Equal("LTK Thumbnails", statuses.Single(s => s.Plugin.Id == "tex-thumbnails").BlockedBy);
        Assert.Null(statuses.Single(s => s.Plugin.Id == "ltk-tex-thumbnails").BlockedBy);
    }

    [Fact]
    public async Task Installing_an_unknown_tag_names_the_plugin_in_the_error()
    {
        var manager = Build([Blender("4.3")]);

        var ex = await Assert.ThrowsAsync<PlanException>(
            () => manager.InstallAsync("aventurine-blender", "99.0.0", default));

        Assert.Contains("Aventurine", ex.Message);
    }

    [Fact]
    public async Task Installing_with_no_detected_host_fails_rather_than_guessing_a_path()
    {
        var manager = Build([Blender("4.3")]);

        await Assert.ThrowsAsync<PlanException>(
            () => manager.InstallAsync("ritotex-photoshop", "v2.0.2", default));
    }

    [Fact]
    public async Task Install_reaches_every_detected_instance()
    {
        var sandbox = Path.Combine(Path.GetTempPath(), $"onyx-mgr-{Guid.NewGuid():N}");
        try
        {
            HostInstance Real(string v) =>
                new("blender", v, $"Blender {v}", Path.Combine(sandbox, v));

            var manager = Build(
                [Real("4.1"), Real("4.3")],
                fs: new Onyx.Core.Platform.PhysicalFileSystem());

            await manager.InstallAsync("aventurine-blender", "3.1.5", default);

            var status = (await manager.StatusAsync(default)).Single(s => s.Plugin.Id == "aventurine-blender");
            Assert.All(status.Targets, t => Assert.Equal("3.1.5", t.InstalledTag));
            Assert.Equal("3.1.5", status.InstalledTag);
            Assert.True(File.Exists(Path.Combine(sandbox, "4.1", "scripts", "addons", "Aventurine", "__init__.py")));
            Assert.True(File.Exists(Path.Combine(sandbox, "4.3", "scripts", "addons", "Aventurine", "__init__.py")));
        }
        finally
        {
            if (Directory.Exists(sandbox)) Directory.Delete(sandbox, recursive: true);
        }
    }

    [Fact]
    public async Task Mixed_versions_across_instances_read_as_mixed()
    {
        var state = StateStore.Load(_statePath);
        state.Record("aventurine-blender", "4.1", "3.1.2", InstallJournal.Empty);
        state.Record("aventurine-blender", "4.3", "3.1.5", InstallJournal.Empty);
        state.Save();

        var manager = Build([Blender("4.1"), Blender("4.3")]);
        var status = (await manager.StatusAsync(default)).Single(s => s.Plugin.Id == "aventurine-blender");

        Assert.Equal("mixed", status.InstalledTag);
        Assert.True(status.UpdateAvailable);
    }

    [Fact]
    public async Task Update_availability_follows_the_recorded_tag()
    {
        var state = StateStore.Load(_statePath);
        state.Record("aventurine-blender", "4.3", "3.1.2", InstallJournal.Empty);
        state.Save();

        var manager = Build([Blender("4.3")]);
        var status = (await manager.StatusAsync(default)).Single(s => s.Plugin.Id == "aventurine-blender");

        Assert.Equal("3.1.2", status.InstalledTag);
        Assert.Equal("3.1.5", status.Latest!.Tag);
        Assert.True(status.UpdateAvailable);
    }

    [Fact]
    public async Task A_presence_probe_reports_an_untracked_install_as_installed()
    {
        var manager = Build(
            [new HostInstance("thumbnails", "default", "Windows Explorer", @"C:\local\Thumbs")],
            probes: [new StaticProbe("tex-thumbnails", @"C:\somewhere\TexThumbnailProvider.dll")]);

        var status = (await manager.StatusAsync(default)).Single(s => s.Plugin.Id == "tex-thumbnails");

        var target = Assert.Single(status.Targets);
        Assert.Equal("installed", target.InstalledTag);
        Assert.True(target.External);
        Assert.True(target.UpdateAvailable);
    }

    [Fact]
    public async Task A_marker_file_reports_an_untracked_install_as_installed()
    {
        var fs = new FakeFileSystem()
            .WithFile(@"C:\maya\2023\plug-ins\ritoshark_plugin.py");

        var manager = Build(
            [new HostInstance("maya", "2023", "Maya 2023", @"C:\maya\2023")],
            fs: fs);

        var status = (await manager.StatusAsync(default)).Single(s => s.Plugin.Id == "ritoshark-maya");

        var target = Assert.Single(status.Targets);
        Assert.Equal("installed", target.InstalledTag);
        Assert.True(target.External);
    }

    [Fact]
    public async Task The_photoshop_marker_covers_the_required_file_formats_folder()
    {
        var fs = new FakeFileSystem()
            .WithFile(@"C:\ps\Required\Plug-ins\File Formats\RitoTex.8bi")
            .WithDirectory(@"C:\ps\Plug-ins");

        var manager = Build(
            [new HostInstance("photoshop", "200.0", "Adobe Photoshop 2026", @"C:\ps")],
            fs: fs);

        var status = (await manager.StatusAsync(default)).Single(s => s.Plugin.Id == "ritotex-photoshop");

        Assert.Equal("installed", Assert.Single(status.Targets).InstalledTag);
    }

    [Fact]
    public async Task Gimp_markers_follow_the_variant_for_each_version()
    {
        var fs = new FakeFileSystem()
            .WithFile(@"C:\gimp.10\plug-ins\gimp2_tex_plugin.py");

        var manager = Build(
            [
                new HostInstance("gimp", "2.10", "GIMP 2.10", @"C:\gimp.10"),
                new HostInstance("gimp", "3.0", "GIMP 3.0", @"C:\gimp.0")
            ],
            fs: fs);

        var gimp = (await manager.StatusAsync(default)).Single(s => s.Plugin.Id == "tex-gimp");

        Assert.Equal("installed", gimp.Targets[0].InstalledTag);
        Assert.Null(gimp.Targets[1].InstalledTag);
        Assert.Equal("installed", gimp.InstalledTag);
    }

    [Fact]
    public async Task A_tracked_install_wins_over_the_presence_probe()
    {
        var state = StateStore.Load(_statePath);
        state.Record("tex-thumbnails", "default", "v1.1.0", InstallJournal.Empty);
        state.Save();

        var manager = Build(
            [new HostInstance("thumbnails", "default", "Windows Explorer", @"C:\local\Thumbs")],
            probes: [new StaticProbe("tex-thumbnails", @"C:\somewhere\TexThumbnailProvider.dll")]);

        var target = Assert.Single(
            (await manager.StatusAsync(default)).Single(s => s.Plugin.Id == "tex-thumbnails").Targets);

        Assert.Equal("v1.1.0", target.InstalledTag);
        Assert.False(target.External);
    }

    [Fact]
    public async Task A_probe_that_finds_nothing_leaves_the_plugin_uninstalled()
    {
        var manager = Build(
            [new HostInstance("thumbnails", "default", "Windows Explorer", @"C:\local\Thumbs")],
            probes: [new StaticProbe("tex-thumbnails", null)]);

        var status = (await manager.StatusAsync(default)).Single(s => s.Plugin.Id == "tex-thumbnails");

        Assert.False(status.Installed);
    }
}
