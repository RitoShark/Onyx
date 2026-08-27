using System.Text.Json;

namespace Onyx.Core.Hosts;

public sealed class HostOverrides
{
    public const string ManualInstance = "manual";

    static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    readonly string _path;
    readonly Dictionary<string, string> _paths;

    HostOverrides(string path, Dictionary<string, string> paths)
    {
        _path = path;
        _paths = paths;
    }

    public static string DefaultPath(string localAppData) =>
        Path.Combine(localAppData, "RitoShark", "Onyx", "hosts.json");

    public static HostOverrides Load(string path)
    {
        if (!File.Exists(path)) return new HostOverrides(path, []);

        try
        {
            var paths = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path));
            return new HostOverrides(path, paths ?? []);
        }
        catch (JsonException)
        {
            return new HostOverrides(path, []);
        }
    }

    public static HostOverrides Empty() => new("", []);

    public string? Get(string hostId, string instanceId) => _paths.GetValueOrDefault(Key(hostId, instanceId));

    public void Set(string hostId, string instanceId, string path)
    {
        _paths[Key(hostId, instanceId)] = path;
        Save();
    }

    public void Clear(string hostId, string instanceId)
    {
        _paths.Remove(Key(hostId, instanceId));
        Save();
    }

    public IReadOnlyList<(string HostId, string InstanceId, string Path)> All =>
        _paths.Select(pair =>
        {
            var parts = pair.Key.Split('|', 2);
            return (parts[0], parts[1], pair.Value);
        }).ToList();

    void Save()
    {
        if (_path.Length == 0) return;

        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllText(_path, JsonSerializer.Serialize(_paths, Options));
    }

    static string Key(string hostId, string instanceId) => $"{hostId}|{instanceId}";
}
