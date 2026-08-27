using System.Text.Json;

namespace Onyx.Core.Install;

public static class Elevation
{
    public static bool NeedsElevation(InstallPlan plan, Func<string, bool> canWrite) =>
        plan.Operations
            .Select(o => o.To)
            .OfType<string>()
            .Any(target => IsProtected(target) && !canWrite(target));

    public static bool IsProtected(string path)
    {
        foreach (var folder in new[] { Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86 })
        {
            var root = Environment.GetFolderPath(folder);
            if (root.Length > 0 &&
                path.StartsWith(root + System.IO.Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    public static string Write(InstallPlan plan, string directory)
    {
        System.IO.Directory.CreateDirectory(directory);
        var path = System.IO.Path.Combine(directory, $"plan-{Guid.NewGuid():N}.json");
        System.IO.File.WriteAllText(path, JsonSerializer.Serialize(plan));
        return path;
    }

    public static InstallPlan Read(string path) =>
        JsonSerializer.Deserialize<InstallPlan>(System.IO.File.ReadAllText(path))
        ?? throw new PlanException($"Could not read the install plan at {path}.");
}
