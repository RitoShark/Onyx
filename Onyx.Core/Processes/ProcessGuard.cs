namespace Onyx.Core.Processes;

public sealed record RunningProcess(int Pid, string Name);

public interface IProcessTable
{
    IReadOnlyList<RunningProcess> Running(IReadOnlyList<string> names);
    void RequestClose(int pid);
}

public sealed class HostRunningException(string hostLabel, IReadOnlyList<string> processNames)
    : Exception($"{hostLabel} is open. It has to close before the plugin can be installed.")
{
    public string HostLabel { get; } = hostLabel;
    public IReadOnlyList<string> ProcessNames { get; } = processNames;
}

public sealed class ProcessGuard(IProcessTable table)
{
    static readonly Dictionary<string, string[]> Guards = new(StringComparer.Ordinal)
    {
        ["photoshop"] = ["Photoshop"],
        ["paintnet"] = ["paintdotnet", "PaintDotNet"],
        ["gimp"] = ["gimp-2.10", "gimp-3.0", "gimp"],
        ["maya"] = ["maya"],
        ["blender"] = ["blender"]
    };

    public static IReadOnlyList<string> ProcessNamesFor(string hostId) =>
        Guards.GetValueOrDefault(hostId) ?? [];

    public IReadOnlyList<RunningProcess> Check(string hostId) =>
        table.Running(ProcessNamesFor(hostId));

    public async Task<bool> CloseAndWaitAsync(string hostId, TimeSpan timeout, CancellationToken ct)
    {
        foreach (var process in Check(hostId))
            table.RequestClose(process.Pid);

        var deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (Check(hostId).Count == 0) return true;
            await Task.Delay(100, ct);
        }

        return Check(hostId).Count == 0;
    }
}
