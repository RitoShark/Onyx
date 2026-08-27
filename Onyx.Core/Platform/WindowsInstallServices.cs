using System.Diagnostics;
using System.Security.Cryptography;
using Onyx.Core.Install;

namespace Onyx.Core.Platform;

public sealed class Regsvr32Registrar : IRegistrar
{
    public void Register(string dllPath) => Run(dllPath, unregister: false);

    public void Unregister(string dllPath) => Run(dllPath, unregister: true);

    static void Run(string dllPath, bool unregister)
    {
        var arguments = unregister ? $"/s /u \"{dllPath}\"" : $"/s \"{dllPath}\"";

        using var process = Process.Start(new ProcessStartInfo("regsvr32", arguments)
        {
            UseShellExecute = false,
            CreateNoWindow = true
        }) ?? throw new PlanException("Could not start regsvr32.");

        process.WaitForExit();

        if (!unregister && process.ExitCode != 0)
            throw new PlanException(
                $"regsvr32 failed with exit code {process.ExitCode}. The DLL may be blocked; try unblocking it in its file properties.");
    }
}

public sealed class FileChecksum : IChecksum
{
    public string Sha256(string path) =>
        Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();

    public string ReadExpected(string sidecarPath) =>
        File.ReadAllText(sidecarPath).Split([' ', '\t', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries)[0]
            .Trim()
            .ToLowerInvariant();
}
