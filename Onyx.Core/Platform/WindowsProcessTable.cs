using System.Diagnostics;
using Onyx.Core.Processes;

namespace Onyx.Core.Platform;

public sealed class WindowsProcessTable : IProcessTable
{
    public IReadOnlyList<RunningProcess> Running(IReadOnlyList<string> names) =>
        names.SelectMany(SafeByName)
            .Select(p => new RunningProcess(p.Id, p.ProcessName))
            .DistinctBy(p => p.Pid)
            .ToList();

    public void RequestClose(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            process.CloseMainWindow();
        }
        catch (ArgumentException)
        {
        }
    }

    static IEnumerable<Process> SafeByName(string name)
    {
        try { return Process.GetProcessesByName(name); }
        catch (InvalidOperationException) { return []; }
    }
}
