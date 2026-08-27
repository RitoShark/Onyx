using Onyx.Core.Catalog;
using Onyx.Core.Hosts;
using Onyx.Core.Install;
using Onyx.Core.Processes;
using Onyx.Core.Releases;
using Onyx.Core.State;

namespace Onyx.Core;

public sealed record PluginStatus(
    PluginEntry Plugin,
    HostInstance? Host,
    string? InstalledTag,
    Release? Latest,
    IReadOnlyList<Release> Releases,
    bool UpdateAvailable,
    string? Problem)
{
    public bool Installed => InstalledTag is not null;
}

public sealed class PluginManager(
    Catalog.Catalog catalog,
    HostRegistry hosts,
    GitHubClient github,
    PayloadFetcher payloads,
    InstallEngine engine,
    ProcessGuard guard,
    StateStore state)
{
    readonly Dictionary<string, IReadOnlyList<Release>> _releases = new(StringComparer.Ordinal);

    public Catalog.Catalog Catalog => catalog;

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

            var instances = hosts.For(plugin.Host)
                .Where(h => plugin.HostInstance is null || h.InstanceId == plugin.HostInstance)
                .ToList();

            if (instances.Count == 0)
            {
                statuses.Add(new PluginStatus(plugin, null, null, latest, releases, false, problem));
                continue;
            }

            foreach (var host in instances)
            {
                var installed = state.Find(plugin.Id, host.InstanceId)?.Tag;
                statuses.Add(new PluginStatus(
                    plugin, host, installed, latest, releases,
                    ReleaseSet.UpdateAvailable(installed, latest), problem));
            }
        }

        return statuses;
    }

    public async Task InstallAsync(string pluginId, string instanceId, string tag, CancellationToken ct)
    {
        var (plugin, host) = Locate(pluginId, instanceId);
        RequireClosed(plugin.Host, host.Label);

        var releases = await ReleasesAsync(plugin.Repo, ct);
        var release = releases.FirstOrDefault(r => r.Tag == tag)
            ?? throw new PlanException($"{plugin.Name} has no release tagged '{tag}'.");

        var plan = PlanResolver.Resolve(plugin, host, release);

        Uninstall(plugin.Id, host.InstanceId);

        using var payload = await payloads.FetchAsync(plan, ct);
        var journal = engine.Apply(plan, payload);

        state.Record(plugin.Id, host.InstanceId, release.Tag, journal);
        state.Save();
    }

    public void UninstallAsync(string pluginId, string instanceId)
    {
        var (plugin, host) = Locate(pluginId, instanceId);
        RequireClosed(plugin.Host, host.Label);

        Uninstall(pluginId, instanceId);
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

    (PluginEntry Plugin, HostInstance Host) Locate(string pluginId, string instanceId)
    {
        var plugin = catalog.Plugins.FirstOrDefault(p => p.Id == pluginId)
            ?? throw new PlanException($"No plugin '{pluginId}' in the catalog.");

        var host = hosts.For(plugin.Host).FirstOrDefault(h => h.InstanceId == instanceId)
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
