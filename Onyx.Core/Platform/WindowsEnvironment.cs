using System.Runtime.Versioning;
using Microsoft.Win32;
using Onyx.Core.Hosts;

namespace Onyx.Core.Platform;

[SupportedOSPlatform("windows")]
public sealed class WindowsRegistry : IRegistry
{
    static RegistryKey Root(Hive hive) => RegistryKey.OpenBaseKey(
        hive == Hive.LocalMachine ? RegistryHive.LocalMachine : RegistryHive.CurrentUser,
        RegistryView.Registry64);

    public string? GetValue(Hive hive, string key, string name)
    {
        using var root = Root(hive);
        using var sub = root.OpenSubKey(key);
        return sub?.GetValue(name) as string;
    }

    public IReadOnlyList<string> SubKeys(Hive hive, string key)
    {
        using var root = Root(hive);
        using var sub = root.OpenSubKey(key);
        return sub?.GetSubKeyNames() ?? [];
    }
}

public sealed class PhysicalFileSystem : IFileSystem
{
    public bool DirectoryExists(string path) => Directory.Exists(path);

    public bool FileExists(string path) => File.Exists(path);

    public IReadOnlyList<string> Directories(string path) => Directory.GetDirectories(path);

    public IReadOnlyList<string> Files(string path) => Directory.GetFiles(path);

    public void CreateDirectory(string path) => Directory.CreateDirectory(path);

    public void Copy(string from, string to, bool overwrite)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(to)!);
        File.Copy(from, to, overwrite);
    }

    public void DeleteFile(string path) => File.Delete(path);

    public void DeleteDirectory(string path) => Directory.Delete(path, recursive: true);
}

public sealed class KnownFolders : IKnownFolders
{
    public string AppData => Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
    public string LocalAppData => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    public string Documents => Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
    public string UserProfile => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    public string ProgramFiles => Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
    public string? Variable(string name) => Environment.GetEnvironmentVariable(name);
}
