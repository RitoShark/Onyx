using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using Onyx.Core.Install;

namespace Onyx;

public sealed class ProcessElevator : IElevator
{
    public bool CanWrite(string path)
    {
        var directory = Path.GetDirectoryName(path);
        if (directory is null) return false;

        try
        {
            Directory.CreateDirectory(directory);
            var probe = Path.Combine(directory, $".onyx-{Guid.NewGuid():N}");
            File.WriteAllBytes(probe, []);
            File.Delete(probe);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public async Task RunAsync(string jobPath, CancellationToken ct)
    {
        var executable = Environment.ProcessPath
            ?? throw new PlanException("Onyx could not locate its own executable to elevate.");

        var start = new ProcessStartInfo(executable, $"--apply \"{jobPath}\"")
        {
            UseShellExecute = true,
            Verb = "runas"
        };

        Process process;
        try
        {
            process = Process.Start(start) ?? throw new PlanException("Onyx could not start an elevated helper.");
        }
        catch (Win32Exception)
        {
            throw new PlanException("Administrator permission is needed to write into this folder, and it was declined.");
        }

        using (process)
        {
            await process.WaitForExitAsync(ct);

            if (process.ExitCode != 0)
                throw new PlanException("The elevated install step failed.");
        }
    }
}
