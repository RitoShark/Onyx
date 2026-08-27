using Onyx.Core.Catalog;
using Onyx.Core.Hosts;
using Onyx.Core.Install;
using Onyx.Core.Processes;
using Onyx.Core.Releases;
using Onyx.Core.State;

namespace Onyx.Core;

public sealed record PluginTarget(
    HostInstance Host,
    string? InstalledTag,
    bool UpdateAvailable,
    bool External);

public sealed record PluginStatus(
    PluginEntry Plugin,
    IReadOnlyList<PluginTarget> Targets,
    Release? Latest,
    IReadOnlyList<Release> Releases,
    string? Problem)
{
    public bool HasHost => Targets.Count > 0;

    public bool Installed => Targets.Any(t => t.InstalledTag is not null);

    public bool UpdateAvailable => Targets.Any(t => t.UpdateAvailable);

    public string? InstalledTag
    {
        get
        {
            var tags = Targets.Select(t => t.InstalledTag).OfType<string>().Distinct().ToList();
            return tags.Count switch { 0 => null, 1 => tags[0], _ => "mixed" };
        }
    }
}

public sealed class PluginManager(
    Catalog.Catalog catalog,
    HostRegistry hosts,
    GitHubClient github,
    PayloadFetcher payloads,
    InstallEngine engine,
    ProcessGuard guard,
    StateStore state,
    IElevator? elevator = null,
    IReadOnlyList<IPresenceProbe>? probes = null)
{
    readonly Dictionary<string, IReadOnlyList<Release>> _releases = new(StringComparer.Ordinal);
    readonly IReadOnlyList<IPresenceProbe> _probes = probes ?? [];

    public Catalog.Catalog Catalog => catalog;
    public HostRegistry Hosts => hosts;

    public async Task<IReadOnlyList<PluginStatus>> StatusAsync(CancellationToken ct)
    {
        var statuses = new List<PluginStatus>();

        foreach (var plugin in catalog.Plugins)
        {
            IReadOnlyList<Release> releases;
            string? problem = null;

            try
            {
                releases = await ReleasesAsync(plugin.Repo, ct);
            }
            catch (Exception e) when (e is RateLimitedException or HttpRequestException or TaskCanceledException)
            {
                releases = [];
                problem = e is RateLimitedException
                    ? "GitHub rate limit reached."
                    : "Couldn't reach GitHub.";
            }

            var latest = ReleaseSet.Latest(releases, includePrerelease: false);
            var probe = _probes.FirstOrDefault(p => p.PluginId == plugin.Id);

            var targets = InstancesFor(plugin)
                .Select(host => Target(plugin, host, latest, probe))
                .ToList();

            statuses.Add(new PluginStatus(plugin, targets, latest, releases, problem));
        }

        return statuses;
    }

    PluginTarget Target(PluginEntry plugin, HostInstance host, Release? latest, IPresenceProbe? probe)
    {
        var installed = state.Find(plugin.Id, host.InstanceId)?.Tag;
        if (installed is not null)
            return new PluginTarget(host, installed, ReleaseSet.UpdateAvailable(installed, latest), false);

        var external = probe?.DetectInstalled(host.Path);
        return external is not null
            ? new PluginTarget(host, "detected", latest is not null, true)
            : new PluginTarget(host, null, false, false);
    }

    public async Task InstallAsync(string pluginId, string tag, CancellationToken ct)
    {
        var plugin = Find(pluginId);
        var instances = InstancesFor(plugin);
        if (instances.Count == 0)
            throw new PlanException($"{plugin.Name} has no detected install target.");

        RequireClosed(plugin.Host, instances[0].Label);

        foreach (var host in instances)
            await InstallIntoAsync(plugin, host, tag, ct);
    }

    public async Task InstallIntoAsync(string pluginId, string instanceId, string tag, CancellationToken ct)
    {
        var (plugin, host) = Locate(pluginId, instanceId);
        RequireClosed(plugin.Host, host.Label);
        await InstallIntoAsync(plugin, host, tag, ct);
    }

    async Task InstallIntoAsync(PluginEntry plugin, HostInstance host, string tag, CancellationToken ct)
    {
        var releases = await ReleasesAsync(plugin.Repo, ct);
        var release = releases.FirstOrDefault(r => r.Tag == tag)
            ?? throw new PlanException($"{plugin.Name} has no release tagged '{tag}'.");

        var plan = PlanResolver.Resolve(plugin, host, release);

        Uninstall(plugin.Id, host.InstanceId);

        using var payload = await payloads.FetchAsync(plan, ct);
        var journal = await ApplyAsync(plan, payload, ct);

        state.Record(plugin.Id, host.InstanceId, release.Tag, journal);
        state.Save();
    }

    async Task<InstallJournal> ApplyAsync(InstallPlan plan, IPayload payload, CancellationToken ct)
    {
        if (elevator is null || !Elevation.NeedsElevation(plan, elevator.CanWrite))
            return engine.Apply(plan, payload);

        var staging = Path.Combine(Path.GetTempPath(), "onyx-jobs");
        var journalPath = Path.Combine(staging, $"journal-{Guid.NewGuid():N}.json");
        var job = new ElevatedJob(plan, payload.Root, journalPath);
        var jobPath = ElevatedJob.Write(job, staging);

        try
        {
            await elevator.RunAsync(jobPath, ct);
            return job.ReadJournal();
        }
        finally
        {
            TryDelete(jobPath);
            TryDelete(journalPath);
        }
    }

    static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (IOException) { }
    }

    public void UninstallAll(string pluginId)
    {
        var plugin = Find(pluginId);
        var instances = InstancesFor(plugin);
        if (instances.Count > 0) RequireClosed(plugin.Host, instances[0].Label);

        foreach (var host in instances)
            Uninstall(pluginId, host.InstanceId);

        state.Save();
    }

    void Uninstall(string pluginId, string instanceId)
    {
        var record = state.Find(pluginId, instanceId);
        if (record is null) return;

        engine.Revert(record.AsJournal());
        state.Forget(pluginId, instanceId);
    }

    void RequireClosed(string hostId, string hostLabel)
    {
        var running = guard.Check(hostId);
        if (running.Count > 0)
            throw new HostRunningException(hostLabel, running.Select(p => p.Name).Distinct().ToList());
    }

    IReadOnlyList<HostInstance> InstancesFor(PluginEntry plugin) =>
        hosts.For(plugin.Host)
            .Where(h => plugin.HostInstance is null || h.InstanceId == plugin.HostInstance)
            .Where(h => plugin.Supports(h.InstanceId))
            .ToList();

    PluginEntry Find(string pluginId) =>
        catalog.Plugins.FirstOrDefault(p => p.Id == pluginId)
            ?? throw new PlanException($"No plugin '{pluginId}' in the catalog.");

    (PluginEntry Plugin, HostInstance Host) Locate(string pluginId, string instanceId)
    {
        var plugin = Find(pluginId);

        var host = InstancesFor(plugin).FirstOrDefault(h => h.InstanceId == instanceId)
            ?? throw new PlanException($"{plugin.Name} has no detected host '{instanceId}'.");

        return (plugin, host);
    }

    async Task<IReadOnlyList<Release>> ReleasesAsync(string repo, CancellationToken ct)
    {
        if (_releases.TryGetValue(repo, out var cached)) return cached;

        var releases = await github.ListReleasesAsync(repo, ct);
        _releases[repo] = releases;
        return releases;
    }

    public void ForgetCachedReleases() => _releases.Clear();
}
