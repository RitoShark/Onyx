using Onyx.Core.Catalog;
using Onyx.Core.Hosts;
using Onyx.Core.Releases;

namespace Onyx.Core.Install;

public static class PlanResolver
{
    public static InstallPlan Resolve(PluginEntry plugin, HostInstance host, Release release)
    {
        var (assetPattern, steps) = plugin.For(host.InstanceId);

        var asset = AssetGlob.Match(release.Assets, assetPattern)
            ?? throw new PlanException($"Release {release.Tag} has no asset matching '{assetPattern}'.");

        var operations = steps
            .Select(s => new PlannedOperation(s.Verb, s.From, s.To is null ? null : Target(host, s.To)))
            .OrderBy(o => o.Verb == StepVerb.Sha256 ? 0 : 1)
            .ToList();

        var sidecars = operations
            .Where(o => o.Verb == StepVerb.Sha256 && o.From is not null)
            .Select(o => AssetGlob.Match(release.Assets, o.From!))
            .OfType<ReleaseAsset>()
            .ToList();

        return new InstallPlan(plugin.Id, host.InstanceId, release.Tag, asset, sidecars, operations);
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
