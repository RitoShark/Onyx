namespace Onyx.Core.Catalog;

public enum StepVerb { Copy, CopyDir, Regsvr32, Sha256 }

public sealed record InstallStep(StepVerb Verb, string? From, string? To);

public sealed record PluginEntry(
    string Id,
    string Name,
    string Summary,
    string Repo,
    string Host,
    string? HostInstance,
    string Asset,
    IReadOnlyList<InstallStep> Steps);

public sealed record Catalog(int Schema, int Revision, IReadOnlyList<PluginEntry> Plugins);

public sealed class CatalogException(string message) : Exception(message);
