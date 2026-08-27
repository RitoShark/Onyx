using Onyx.Core.Catalog;
using Onyx.Core.Install;
using Onyx.Core.Releases;

namespace Onyx.Tests.Install;

public class ElevationTests
{
    static InstallPlan Plan(params string[] targets) =>
        new("p", "i", "v1", new ReleaseAsset("a.zip", "https://x/a.zip", 1), [],
            targets.Select(t => new PlannedOperation(StepVerb.Copy, "a", t)).ToList());

    static string ProgramFiles => Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);

    [Fact]
    public void A_program_files_target_is_protected()
    {
        Assert.True(Elevation.IsProtected(Path.Combine(ProgramFiles, "Adobe", "Photoshop", "Plug-ins", "RitoTex.8bi")));
    }

    [Fact]
    public void A_per_user_target_is_not_protected()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        Assert.False(Elevation.IsProtected(Path.Combine(appData, "Blender Foundation", "Blender", "4.3", "x.py")));
    }

    [Fact]
    public void Elevation_is_needed_for_a_protected_target_that_cannot_be_written()
    {
        var plan = Plan(Path.Combine(ProgramFiles, "Adobe", "Plug-ins", "RitoTex.8bi"));
        Assert.True(Elevation.NeedsElevation(plan, _ => false));
    }

    [Fact]
    public void Elevation_is_skipped_when_the_protected_target_is_already_writable()
    {
        var plan = Plan(Path.Combine(ProgramFiles, "Adobe", "Plug-ins", "RitoTex.8bi"));
        Assert.False(Elevation.NeedsElevation(plan, _ => true));
    }

    [Fact]
    public void Elevation_is_never_needed_for_per_user_targets()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var plan = Plan(Path.Combine(appData, "GIMP", "3.0", "plug-ins", "x.py"));

        Assert.False(Elevation.NeedsElevation(plan, _ => false));
    }

    [Fact]
    public void An_elevated_job_round_trips_through_a_file()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"onyx-jobs-{Guid.NewGuid():N}");
        var plan = Plan(@"C:\target\a.dll");
        var job = new ElevatedJob(plan, @"C:\payload", Path.Combine(directory, "journal.json"));

        var path = ElevatedJob.Write(job, directory);
        try
        {
            var read = ElevatedJob.Read(path);

            Assert.Equal(@"C:\payload", read.PayloadRoot);
            Assert.Equal(@"C:\payload", read.Payload().Root);
            Assert.Equal("p", read.Plan.PluginId);
            Assert.Equal(@"C:\target\a.dll", Assert.Single(read.Plan.Operations).To);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void A_journal_round_trips_back_to_the_parent_process()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"onyx-jobs-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);

        var job = new ElevatedJob(Plan(@"C:\t\a.dll"), @"C:\payload", Path.Combine(directory, "journal.json"));
        try
        {
            job.WriteJournal(new InstallJournal([@"C:\t\a.dll"], [@"C:\t\a.dll"]));
            var journal = job.ReadJournal();

            Assert.Equal([@"C:\t\a.dll"], journal.Written);
            Assert.Equal([@"C:\t\a.dll"], journal.Registered);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
