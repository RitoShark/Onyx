using Onyx.Core.Catalog;
using Onyx.Core.Releases;

namespace Onyx.Core.Install;

public sealed record PlannedOperation(StepVerb Verb, string? From, string? To);

public sealed record InstallPlan(
    string PluginId,
    string InstanceId,
    string Tag,
    ReleaseAsset Asset,
    IReadOnlyList<ReleaseAsset> Sidecars,
    IReadOnlyList<PlannedOperation> Operations);

public sealed class PlanException(string message) : Exception(message);

public sealed class FileLockedException(string path)
    : Exception($"Another program is holding {System.IO.Path.GetFileName(path)} open, so it could not be replaced.")
{
    public string Path { get; } = path;
}
