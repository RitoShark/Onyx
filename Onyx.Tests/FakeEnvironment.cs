using Onyx.Core.Hosts;

namespace Onyx.Tests;

public sealed class FakeRegistry : IRegistry
{
    readonly Dictionary<string, string> _values = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, List<string>> _keys = new(StringComparer.OrdinalIgnoreCase);

    public string? GetValue(Hive hive, string key, string name) =>
        _values.GetValueOrDefault($"{hive}|{key}|{name}");

    public IReadOnlyList<string> SubKeys(Hive hive, string key) =>
        _keys.GetValueOrDefault($"{hive}|{key}") ?? [];

    public FakeRegistry WithValue(Hive hive, string key, string name, string value)
    {
        _values[$"{hive}|{key}|{name}"] = value;
        return this;
    }

    public FakeRegistry WithKeys(Hive hive, string key, params string[] subKeys)
    {
        _keys[$"{hive}|{key}"] = [.. subKeys];
        return this;
    }
}

public sealed class FakeFileSystem : IFileSystem
{
    readonly HashSet<string> _dirs = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, byte[]> _files = new(StringComparer.OrdinalIgnoreCase);

    public FakeFileSystem WithDirectory(string path)
    {
        for (var p = path.TrimEnd('\\'); !string.IsNullOrEmpty(p); p = Path.GetDirectoryName(p) ?? "")
            _dirs.Add(p.TrimEnd('\\'));
        return this;
    }

    public FakeFileSystem WithFile(string path, byte[]? content = null)
    {
        WithDirectory(Path.GetDirectoryName(path)!);
        _files[path] = content ?? [];
        return this;
    }

    public bool DirectoryExists(string path) => _dirs.Contains(path.TrimEnd('\\'));

    public bool FileExists(string path) => _files.ContainsKey(path);

    public IReadOnlyList<string> Directories(string path) =>
        _dirs.Where(d => string.Equals(Path.GetDirectoryName(d), path.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
             .OrderBy(d => d, StringComparer.OrdinalIgnoreCase)
             .ToList();

    public IReadOnlyList<string> Files(string path) =>
        _files.Keys.Where(f => string.Equals(Path.GetDirectoryName(f), path.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
              .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
              .ToList();

    public void CreateDirectory(string path) => WithDirectory(path);

    public void Copy(string from, string to, bool overwrite)
    {
        WithFile(to, _files[from]);
    }

    public void DeleteFile(string path) => _files.Remove(path);

    public void DeleteDirectory(string path)
    {
        var prefix = path.TrimEnd('\\') + "\\";
        foreach (var f in _files.Keys.Where(f => f.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToList())
            _files.Remove(f);
        foreach (var d in _dirs.Where(d => d.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToList())
            _dirs.Remove(d);
        _dirs.Remove(path.TrimEnd('\\'));
    }
}

public sealed record FakeFolders(
    string AppData,
    string LocalAppData,
    string Documents,
    string UserProfile) : IKnownFolders
{
    public Dictionary<string, string> Variables { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    public string? Variable(string name) => Variables.GetValueOrDefault(name);

    public static FakeFolders Default => new(
        @"C:\Users\t\AppData\Roaming",
        @"C:\Users\t\AppData\Local",
        @"C:\Users\t\Documents",
        @"C:\Users\t");
}
