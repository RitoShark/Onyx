using Onyx.Core.Catalog;
using Onyx.Core.Hosts;
using Onyx.Core.Install;
using Onyx.Core.Releases;

namespace Onyx.Tests.Install;

public class PlanResolverTests
{
    static Release Release(string tag, params string[] assets) =>
        new(tag, DateTimeOffset.UnixEpoch, false, "",
            assets.Select(a => new ReleaseAsset(a, $"https://x/{a}", 1)).ToList());

    static PluginEntry Plugin(string asset, params InstallStep[] steps) =>
        new("p", "P", "s", "Owner/Repo", "blender", null, asset, steps);

    [Fact]
    public void Substitutes_the_host_path_into_targets()
    {
        var host = new HostInstance("blender", "4.3", "Blender 4.3", @"C:\cfg\Blender\4.3");
        var plugin = Plugin("Aventurine-*.zip",
            new InstallStep(StepVerb.CopyDir, "Aventurine", "{host}/scripts/addons/Aventurine"));

        var plan = PlanResolver.Resolve(plugin, host, Release("3.1.5", "Aventurine-3.1.5.zip"));

        Assert.Equal("3.1.5", plan.Tag);
        Assert.Equal("Aventurine-3.1.5.zip", plan.Asset.Name);
        Assert.Equal(@"C:\cfg\Blender\4.3\scripts\addons\Aventurine", Assert.Single(plan.Operations).To);
    }

    [Fact]
    public void Carries_the_host_instance_so_state_can_be_keyed_by_it()
    {
        var host = new HostInstance("blender", "4.1", "Blender 4.1", @"C:\cfg\Blender\4.1");
        var plugin = Plugin("a.zip", new InstallStep(StepVerb.CopyDir, ".", "{host}/x"));

        Assert.Equal("4.1", PlanResolver.Resolve(plugin, host, Release("1", "a.zip")).InstanceId);
    }

    [Fact]
    public void Fails_when_the_release_has_no_matching_asset()
    {
        var host = new HostInstance("blender", "4.3", "Blender 4.3", @"C:\cfg");
        var plugin = Plugin("Aventurine-*.zip", new InstallStep(StepVerb.CopyDir, "Aventurine", "{host}/x"));

        var ex = Assert.Throws<PlanException>(() => PlanResolver.Resolve(plugin, host, Release("3.1.5", "notes.txt")));
        Assert.Contains("Aventurine-*.zip", ex.Message);
    }

    [Fact]
    public void Fails_when_a_target_escapes_the_host_directory()
    {
        var host = new HostInstance("blender", "4.3", "Blender 4.3", @"C:\cfg\Blender\4.3");
        var plugin = Plugin("a.zip",
            new InstallStep(StepVerb.CopyDir, "x", "{host}/../../../Windows/System32/evil"));

        Assert.Throws<PlanException>(() => PlanResolver.Resolve(plugin, host, Release("1", "a.zip")));
    }

    [Fact]
    public void Allows_a_target_that_is_the_host_directory_itself()
    {
        var host = new HostInstance("thumbnails", "default", "Explorer", @"C:\local\RitoShark\TexThumbnailProvider");
        var plugin = Plugin("a.dll", new InstallStep(StepVerb.Copy, "a.dll", "{host}"));

        Assert.Equal(@"C:\local\RitoShark\TexThumbnailProvider",
            Assert.Single(PlanResolver.Resolve(plugin, host, Release("1", "a.dll")).Operations).To);
    }

    [Fact]
    public void Keeps_a_sha256_step_ahead_of_every_write()
    {
        var host = new HostInstance("thumbnails", "default", "Explorer", @"C:\local\RitoShark\TexThumbnailProvider");
        var plugin = Plugin("TexThumbnailProvider.dll",
            new InstallStep(StepVerb.Copy, "TexThumbnailProvider.dll", "{host}/TexThumbnailProvider.dll"),
            new InstallStep(StepVerb.Sha256, "TexThumbnailProvider.dll.sha256", null));

        var plan = PlanResolver.Resolve(plugin, host,
            Release("v1.1.0", "TexThumbnailProvider.dll", "TexThumbnailProvider.dll.sha256"));

        Assert.Equal(StepVerb.Sha256, plan.Operations[0].Verb);
    }
}
