using System.Text.Json;
using Onyx.Core.Install;

namespace Onyx.Core.State;

public sealed record InstallRecord(
    string PluginId,
    string InstanceId,
    string Tag,
    IReadOnlyList<string> Written,
    IReadOnlyList<string> Registered,
    DateTimeOffset InstalledAt)
{
    public InstallJournal AsJournal() => new(Written, Registered);
}

public sealed class StateStore
{
    static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    readonly string _path;
    readonly List<InstallRecord> _records;

    StateStore(string path, List<InstallRecord> records)
    {
        _path = path;
        _records = records;
    }

    public IReadOnlyList<InstallRecord> All => _records;

    public static string DefaultPath(string localAppData) =>
        Path.Combine(localAppData, "RitoShark", "Onyx", "state.json");

    public static StateStore Load(string path)
    {
        if (!File.Exists(path)) return new StateStore(path, []);

        try
        {
            var records = JsonSerializer.Deserialize<List<InstallRecord>>(File.ReadAllText(path), Options);
            return new StateStore(path, records ?? []);
        }
        catch (JsonException)
        {
            return new StateStore(path, []);
        }
    }

    public InstallRecord? Find(string pluginId, string instanceId) =>
        _records.FirstOrDefault(r => r.PluginId == pluginId && r.InstanceId == instanceId);

    public void Record(string pluginId, string instanceId, string tag, InstallJournal journal)
    {
        Forget(pluginId, instanceId);
        _records.Add(new InstallRecord(
            pluginId, instanceId, tag, journal.Written, journal.Registered, DateTimeOffset.UtcNow));
    }

    public void Forget(string pluginId, string instanceId) =>
        _records.RemoveAll(r => r.PluginId == pluginId && r.InstanceId == instanceId);

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllText(_path, JsonSerializer.Serialize(_records, Options));
    }
}
