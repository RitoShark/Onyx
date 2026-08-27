using Onyx.Core.Hosts;
using Onyx.Core.Install;

namespace Onyx.Tests.Install;

public class PresenceProbeTests
{
    const string Server = @"Software\Classes\CLSID\{243B3EEC-8FD0-44CD-95AD-BEAFDCE52CBF}\InProcServer32";

    [Fact]
    public void Thumbnails_are_detected_from_the_registered_dll()
    {
        var registry = new FakeRegistry()
            .WithValue(Hive.CurrentUser, Server, "", @"C:\Users\t\AppData\Local\RitoShark\TexThumbnailProvider\TexThumbnailProvider.dll");
        var fs = new FakeFileSystem()
            .WithFile(@"C:\Users\t\AppData\Local\RitoShark\TexThumbnailProvider\TexThumbnailProvider.dll");

        var found = new ThumbnailPresenceProbe(registry, fs).DetectInstalled(@"C:\anything");

        Assert.EndsWith("TexThumbnailProvider.dll", found);
    }

    [Fact]
    public void A_registration_whose_dll_is_gone_does_not_count_as_installed()
    {
        var registry = new FakeRegistry()
            .WithValue(Hive.CurrentUser, Server, "", @"C:\gone\TexThumbnailProvider.dll");

        Assert.Null(new ThumbnailPresenceProbe(registry, new FakeFileSystem()).DetectInstalled(@"C:\anything"));
    }

    [Fact]
    public void No_registration_means_not_installed()
    {
        Assert.Null(new ThumbnailPresenceProbe(new FakeRegistry(), new FakeFileSystem()).DetectInstalled(@"C:\anything"));
    }

    [Fact]
    public void Hematite_is_detected_from_its_exe_in_the_host_folder()
    {
        var fs = new FakeFileSystem().WithFile(@"C:\local\RitoShark\Hematite\hematite-cli.exe");

        Assert.NotNull(new HematitePresenceProbe(fs).DetectInstalled(@"C:\local\RitoShark\Hematite"));
        Assert.Null(new HematitePresenceProbe(fs).DetectInstalled(@"C:\elsewhere"));
    }
}
