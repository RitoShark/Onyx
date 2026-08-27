using Onyx.Core.Hosts;

namespace Onyx.Tests.Hosts;

public class HostOverrideTests : IDisposable
{
    readonly string _path = Path.Combine(Path.GetTempPath(), $"onyx-hosts-{Guid.NewGuid():N}.json");

    public void Dispose()
    {
        if (File.Exists(_path)) File.Delete(_path);
    }

    sealed class StaticDetector(string hostId, params HostInstance[] instances) : IHostDetector
    {
        public string HostId => hostId;
        public IReadOnlyList<HostInstance> Detect() => instances;
    }

    [Fact]
    public void An_override_round_trips_to_disk()
    {
        var overrides = HostOverrides.Load(_path);
        overrides.Set("photoshop", "180.0", @"X:\Portable\Photoshop");

        Assert.Equal(@"X:\Portable\Photoshop", HostOverrides.Load(_path).Get("photoshop", "180.0"));
    }

    [Fact]
    public void An_override_redirects_a_detected_instance()
    {
        var overrides = HostOverrides.Load(_path);
        overrides.Set("blender", "4.3", @"X:\Portable\Blender\4.3");

        var registry = new HostRegistry(
            [new StaticDetector("blender", new HostInstance("blender", "4.3", "Blender 4.3", @"C:\normal"))],
            overrides);

        var host = Assert.Single(registry.For("blender"));
        Assert.Equal(@"X:\Portable\Blender\4.3", host.Path);
        Assert.Equal("4.3", host.InstanceId);
    }

    [Fact]
    public void An_override_gives_an_undetected_host_a_manual_instance()
    {
        var overrides = HostOverrides.Load(_path);
        overrides.Set("gimp", HostOverrides.ManualInstance, @"D:\Portable\GIMP\2.10");

        var registry = new HostRegistry([new StaticDetector("gimp")], overrides);

        var host = Assert.Single(registry.For("gimp"));
        Assert.Equal(HostOverrides.ManualInstance, host.InstanceId);
        Assert.Equal(@"D:\Portable\GIMP\2.10", host.Path);
    }

    [Fact]
    public void Clearing_an_override_restores_the_detected_path()
    {
        var overrides = HostOverrides.Load(_path);
        overrides.Set("blender", "4.3", @"X:\elsewhere");
        overrides.Clear("blender", "4.3");

        var registry = new HostRegistry(
            [new StaticDetector("blender", new HostInstance("blender", "4.3", "Blender 4.3", @"C:\normal"))],
            overrides);

        Assert.Equal(@"C:\normal", Assert.Single(registry.For("blender")).Path);
    }

    [Fact]
    public void Without_overrides_detection_is_unchanged()
    {
        var registry = new HostRegistry(
            [new StaticDetector("blender", new HostInstance("blender", "4.3", "Blender 4.3", @"C:\normal"))]);

        Assert.Equal(@"C:\normal", Assert.Single(registry.For("blender")).Path);
    }

    [Fact]
    public void A_corrupt_override_file_loads_as_empty()
    {
        File.WriteAllText(_path, "{ not json");
        Assert.Empty(HostOverrides.Load(_path).All);
    }
}
