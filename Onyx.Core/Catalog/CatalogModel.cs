namespace Onyx.Core.Catalog;

public enum StepVerb { Copy, CopyDir, Regsvr32, Sha256 }

public sealed record InstallStep(StepVerb Verb, string? From, string? To);

public sealed record PluginVariant(
    string InstancePrefix,
    string Asset,
    IReadOnlyList<InstallStep> Steps,
    IReadOnlyList<string> DetectPaths);

public sealed record PluginEntry(
    string Id,
    string Name,
    string Summary,
    string Repo,
    string Host,
    string? HostInstance,
    string Category,
    string Asset,
    IReadOnlyList<InstallStep> Steps,
    IReadOnlyList<PluginVariant> Variants,
    IReadOnlyList<string> DetectPaths,
    string Description = "",
    IReadOnlyList<string>? Conflicts = null)
{
    public bool Supports(string instanceId) =>
        Variants.Count == 0 || Variants.Any(v => instanceId.StartsWith(v.InstancePrefix, StringComparison.Ordinal));

    public (string Asset, IReadOnlyList<InstallStep> Steps) For(string instanceId)
    {
        var variant = Variants.FirstOrDefault(v => instanceId.StartsWith(v.InstancePrefix, StringComparison.Ordinal));
        return variant is null ? (Asset, Steps) : (variant.Asset, variant.Steps);
    }

    public IReadOnlyList<string> DetectFor(string instanceId)
    {
        var variant = Variants.FirstOrDefault(v => instanceId.StartsWith(v.InstancePrefix, StringComparison.Ordinal));
        return variant is { DetectPaths.Count: > 0 } ? variant.DetectPaths : DetectPaths;
    }
}

public sealed record Catalog(int Schema, int Revision, IReadOnlyList<PluginEntry> Plugins);

public sealed class CatalogException(string message) : Exception(message);
