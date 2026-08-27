namespace Onyx.Core.Hosts;

public sealed record HostInstance(string HostId, string InstanceId, string Label, string Path, string? ExePath = null);

public interface IHostDetector
{
    string HostId { get; }
    IReadOnlyList<HostInstance> Detect();
}
