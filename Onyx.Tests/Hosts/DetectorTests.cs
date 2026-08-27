using Onyx.Core.Hosts;

namespace Onyx.Tests.Hosts;

public class DetectorTests
{
    static readonly FakeFolders Folders = FakeFolders.Default;

    [Fact]
    public void Blender_finds_every_version_directory()
    {
        var fs = new FakeFileSystem()
            .WithDirectory($@"{Folders.AppData}\Blender Foundation\Blender\4.0")
            .WithDirectory($@"{Folders.AppData}\Blender Foundation\Blender\4.1")
            .WithDirectory($@"{Folders.AppData}\Blender Foundation\Blender\4.3");

        var found = new BlenderDetector(fs, Folders).Detect();

        Assert.Equal(["4.0", "4.1", "4.3"], found.Select(h => h.InstanceId));
        Assert.All(found, h => Assert.Equal("blender", h.HostId));
        Assert.Equal("Blender 4.3", found[2].Label);
        Assert.Equal($@"{Folders.AppData}\Blender Foundation\Blender\4.3", found[2].Path);
    }

    [Fact]
    public void Blender_orders_by_version_not_by_string()
    {
        var fs = new FakeFileSystem()
            .WithDirectory($@"{Folders.AppData}\Blender Foundation\Blender\4.10")
            .WithDirectory($@"{Folders.AppData}\Blender Foundation\Blender\4.9");

        Assert.Equal(["4.9", "4.10"], new BlenderDetector(fs, Folders).Detect().Select(h => h.InstanceId));
    }

    [Fact]
    public void Blender_ignores_a_directory_that_is_not_a_version()
    {
        var fs = new FakeFileSystem()
            .WithDirectory($@"{Folders.AppData}\Blender Foundation\Blender\4.1")
            .WithDirectory($@"{Folders.AppData}\Blender Foundation\Blender\backup");

        Assert.Equal(["4.1"], new BlenderDetector(fs, Folders).Detect().Select(h => h.InstanceId));
    }

    [Fact]
    public void Blender_finds_nothing_when_blender_is_absent()
    {
        Assert.Empty(new BlenderDetector(new FakeFileSystem(), Folders).Detect());
    }

    [Fact]
    public void Gimp_finds_each_installed_config_version()
    {
        var fs = new FakeFileSystem()
            .WithDirectory($@"{Folders.AppData}\GIMP\2.10")
            .WithDirectory($@"{Folders.AppData}\GIMP\3.0");

        Assert.Equal(["2.10", "3.0"], new GimpDetector(fs, Folders).Detect().Select(h => h.InstanceId));
    }

    [Fact]
    public void Maya_uses_the_registry_years_and_targets_the_documents_folder()
    {
        var reg = new FakeRegistry()
            .WithKeys(Hive.LocalMachine, @"SOFTWARE\Autodesk\Maya", "2024", "2026", "Capabilities");

        var found = new MayaDetector(reg, new FakeFileSystem(), Folders).Detect();

        Assert.Equal(["2024", "2026"], found.Select(h => h.InstanceId));
        Assert.Equal($@"{Folders.Documents}\maya\2026", found[1].Path);
    }

    [Fact]
    public void Maya_ignores_versions_older_than_the_plugin_supports()
    {
        var reg = new FakeRegistry()
            .WithKeys(Hive.LocalMachine, @"SOFTWARE\Autodesk\Maya", "2020", "2024");

        Assert.Equal(["2024"], new MayaDetector(reg, new FakeFileSystem(), Folders).Detect().Select(h => h.InstanceId));
    }

    [Fact]
    public void Maya_honours_MAYA_APP_DIR_over_the_documents_folder()
    {
        var folders = Folders with { Variables = new() { ["MAYA_APP_DIR"] = @"D:\maya-prefs" } };
        var reg = new FakeRegistry().WithKeys(Hive.LocalMachine, @"SOFTWARE\Autodesk\Maya", "2024");
        var fs = new FakeFileSystem().WithDirectory(@"D:\maya-prefs");

        Assert.Equal(@"D:\maya-prefs\2024", Assert.Single(new MayaDetector(reg, fs, folders).Detect()).Path);
    }

    [Fact]
    public void Maya_follows_a_redirected_documents_folder()
    {
        var folders = Folders with { Documents = @"C:\Users\t\OneDrive\Documents" };
        var reg = new FakeRegistry().WithKeys(Hive.LocalMachine, @"SOFTWARE\Autodesk\Maya", "2023");
        var fs = new FakeFileSystem().WithDirectory(@"C:\Users\t\OneDrive\Documents\maya");

        Assert.Equal(@"C:\Users\t\OneDrive\Documents\maya\2023",
            Assert.Single(new MayaDetector(reg, fs, folders).Detect()).Path);
    }

    [Fact]
    public void Maya_prefers_a_candidate_that_exists_over_one_that_does_not()
    {
        var folders = Folders with { Variables = new() { ["MAYA_APP_DIR"] = @"D:\never-created" } };
        var reg = new FakeRegistry().WithKeys(Hive.LocalMachine, @"SOFTWARE\Autodesk\Maya", "2024");
        var fs = new FakeFileSystem().WithDirectory($@"{Folders.Documents}\maya");

        Assert.Equal($@"{Folders.Documents}\maya\2024",
            Assert.Single(new MayaDetector(reg, fs, folders).Detect()).Path);
    }

    [Fact]
    public void Maya_falls_back_to_the_first_candidate_when_none_exist()
    {
        var reg = new FakeRegistry().WithKeys(Hive.LocalMachine, @"SOFTWARE\Autodesk\Maya", "2024");

        Assert.Equal($@"{Folders.Documents}\maya\2024",
            Assert.Single(new MayaDetector(reg, new FakeFileSystem(), Folders).Detect()).Path);
    }

    [Fact]
    public void Photoshop_reads_the_application_path_for_each_version()
    {
        var reg = new FakeRegistry()
            .WithKeys(Hive.LocalMachine, @"SOFTWARE\Adobe\Photoshop", "180.0", "260.0")
            .WithValue(Hive.LocalMachine, @"SOFTWARE\Adobe\Photoshop\180.0", "ApplicationPath", @"D:\Adobe\Photoshop 2024\")
            .WithValue(Hive.LocalMachine, @"SOFTWARE\Adobe\Photoshop\260.0", "ApplicationPath", @"D:\Adobe\Photoshop 2026\");
        var fs = new FakeFileSystem()
            .WithDirectory(@"D:\Adobe\Photoshop 2024\Plug-ins")
            .WithDirectory(@"D:\Adobe\Photoshop 2026\Plug-ins");

        var found = new PhotoshopDetector(reg, fs).Detect();

        Assert.Equal(2, found.Count);
        Assert.Equal(@"D:\Adobe\Photoshop 2026", found[1].Path);
        Assert.Equal("Photoshop 2026", found[1].Label);
    }

    [Fact]
    public void Photoshop_finds_a_non_default_drive()
    {
        var reg = new FakeRegistry()
            .WithKeys(Hive.LocalMachine, @"SOFTWARE\Adobe\Photoshop", "180.0")
            .WithValue(Hive.LocalMachine, @"SOFTWARE\Adobe\Photoshop\180.0", "ApplicationPath", @"X:\Portable\Photoshop 2024\");
        var fs = new FakeFileSystem().WithDirectory(@"X:\Portable\Photoshop 2024\Plug-ins");

        Assert.Equal(@"X:\Portable\Photoshop 2024", Assert.Single(new PhotoshopDetector(reg, fs).Detect()).Path);
    }

    [Fact]
    public void Photoshop_skips_a_version_whose_folder_is_gone()
    {
        var reg = new FakeRegistry()
            .WithKeys(Hive.LocalMachine, @"SOFTWARE\Adobe\Photoshop", "180.0")
            .WithValue(Hive.LocalMachine, @"SOFTWARE\Adobe\Photoshop\180.0", "ApplicationPath", @"D:\Gone\");

        Assert.Empty(new PhotoshopDetector(reg, new FakeFileSystem()).Detect());
    }

    [Fact]
    public void Photoshop_does_not_report_the_same_install_twice_through_the_wow_node()
    {
        var reg = new FakeRegistry()
            .WithKeys(Hive.LocalMachine, @"SOFTWARE\Adobe\Photoshop", "180.0")
            .WithValue(Hive.LocalMachine, @"SOFTWARE\Adobe\Photoshop\180.0", "ApplicationPath", @"D:\Adobe\Photoshop 2024\")
            .WithKeys(Hive.LocalMachine, @"SOFTWARE\WOW6432Node\Adobe\Photoshop", "180.0")
            .WithValue(Hive.LocalMachine, @"SOFTWARE\WOW6432Node\Adobe\Photoshop\180.0", "ApplicationPath", @"D:\Adobe\Photoshop 2024\");
        var fs = new FakeFileSystem().WithDirectory(@"D:\Adobe\Photoshop 2024\Plug-ins");

        Assert.Single(new PhotoshopDetector(reg, fs).Detect());
    }

    [Fact]
    public void PaintNet_finds_the_classic_install_and_the_store_build()
    {
        var reg = new FakeRegistry()
            .WithValue(Hive.LocalMachine, @"SOFTWARE\paint.net", "TARGETDIR", @"C:\Program Files\paint.net\");
        var fs = new FakeFileSystem()
            .WithDirectory(@"C:\Program Files\paint.net\FileTypes")
            .WithDirectory($@"{Folders.Documents}\paint.net App Files");

        Assert.Equal(["classic", "store"], new PaintNetDetector(reg, fs, Folders).Detect().Select(h => h.InstanceId));
    }

    [Fact]
    public void Thumbnails_is_always_a_single_fixed_instance()
    {
        var only = Assert.Single(new ThumbnailHostDetector(Folders).Detect());
        Assert.Equal($@"{Folders.LocalAppData}\RitoShark\TexThumbnailProvider", only.Path);
    }

    [Fact]
    public void The_standard_registry_covers_every_host_the_catalog_names()
    {
        var registry = HostRegistry.Standard(new FakeRegistry(), new FakeFileSystem(), Folders);

        foreach (var host in new[] { "photoshop", "paintnet", "gimp", "maya", "blender" })
            Assert.Empty(registry.For(host));

        Assert.Single(registry.For("thumbnails"));
    }
}
