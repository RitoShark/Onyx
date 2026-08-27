using Onyx.Core.Catalog;
using Onyx.Core.Hosts;
using Onyx.Core.Releases;

namespace Onyx.Core.Install;

public static class PlanResolver
{
    public static InstallPlan Resolve(PluginEntry plugin, HostInstance host, Release release)
    {
        var asset = AssetGlob.Match(release.Assets, plugin.Asset)
            ?? throw new PlanException($"Release {release.Tag} has no asset matching '{plugin.Asset}'.");

        var operations = plugin.Steps
            .Select(s => new PlannedOperation(s.Verb, s.From, s.To is null ? null : Target(host, s.To)))
            .OrderBy(o => o.Verb == StepVerb.Sha256 ? 0 : 1)
            .ToList();

        return new InstallPlan(plugin.Id, host.InstanceId, release.Tag, asset, operations);
    }

    static string Target(HostInstance host, string template)
    {
        var root = Path.GetFullPath(host.Path).TrimEnd(Path.DirectorySeparatorChar);
        var combined = Path.GetFullPath(
            template.Replace("{host}", root).Replace('/', Path.DirectorySeparatorChar));

        if (!combined.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(combined, root, StringComparison.OrdinalIgnoreCase))
            throw new PlanException($"Install target '{combined}' escapes the host directory '{root}'.");

        return combined;
    }
}
