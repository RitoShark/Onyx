using Onyx.Core.Catalog;
using Onyx.Core.Releases;

namespace Onyx.Core.Install;

public sealed record PlannedOperation(StepVerb Verb, string? From, string? To);

public sealed record InstallPlan(
    string PluginId,
    string InstanceId,
    string Tag,
    ReleaseAsset Asset,
    IReadOnlyList<PlannedOperation> Operations);

public sealed class PlanException(string message) : Exception(message);
