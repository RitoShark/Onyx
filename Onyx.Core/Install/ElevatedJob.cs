using System.Text.Json;

namespace Onyx.Core.Install;

public sealed record ElevatedJob(InstallPlan? Plan, string? PayloadRoot, string JournalPath, InstallJournal? Revert = null)
{
    sealed class RootedPayload(string root) : IPayload
    {
        public string Root => root;
    }

    public IPayload Payload() =>
        new RootedPayload(PayloadRoot ?? throw new PlanException("This job carries no payload."));

    public static string Write(ElevatedJob job, string directory)
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"job-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, JsonSerializer.Serialize(job));
        return path;
    }

    public static ElevatedJob Read(string path) =>
        JsonSerializer.Deserialize<ElevatedJob>(File.ReadAllText(path))
        ?? throw new PlanException($"Could not read the install job at {path}.");

    public void WriteJournal(InstallJournal journal) =>
        File.WriteAllText(JournalPath, JsonSerializer.Serialize(journal));

    public InstallJournal ReadJournal() =>
        JsonSerializer.Deserialize<InstallJournal>(File.ReadAllText(JournalPath))
        ?? InstallJournal.Empty;
}

public interface IElevator
{
    bool CanWrite(string path);
    Task RunAsync(string jobPath, CancellationToken ct);
}
