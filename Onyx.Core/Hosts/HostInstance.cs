namespace Onyx.Core.Hosts;

public sealed record HostInstance(string HostId, string InstanceId, string Label, string Path);

public interface IHostDetector
{
    string HostId { get; }
    IReadOnlyList<HostInstance> Detect();
}
