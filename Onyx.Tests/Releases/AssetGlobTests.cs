using Onyx.Core.Releases;

namespace Onyx.Tests.Releases;

public class AssetGlobTests
{
    static ReleaseAsset A(string name) => new(name, $"https://example/{name}", 1);

    [Fact]
    public void Matches_an_exact_name()
    {
        var assets = new[] { A("RitoTex.8bi"), A("other.txt") };
        Assert.Equal("RitoTex.8bi", AssetGlob.Match(assets, "RitoTex.8bi")!.Name);
    }

    [Fact]
    public void Matches_a_version_stamped_name()
    {
        var assets = new[] { A("TexFileTypeSetup-3.0.0.exe"), A("TexFileType-3.0.0.zip") };
        Assert.Equal("TexFileType-3.0.0.zip", AssetGlob.Match(assets, "TexFileType-*.zip")!.Name);
    }

    [Fact]
    public void Picks_the_named_platform_asset_out_of_a_multi_platform_release()
    {
        var assets = new[]
        {
            A("GIMP2_TEX_Plugin_Linux.tar.gz"),
            A("GIMP2_TEX_Plugin_Windows.zip"),
            A("GIMP3_TEX_Plugin_Windows.zip")
        };

        Assert.Equal("GIMP3_TEX_Plugin_Windows.zip", AssetGlob.Match(assets, "GIMP3_TEX_Plugin_Windows.zip")!.Name);
    }

    [Fact]
    public void Does_not_let_a_setup_exe_win_over_the_zip()
    {
        var assets = new[] { A("GIMP_TEX_Plugin_Setup.exe"), A("GIMP2_TEX_Plugin_Windows.zip") };
        Assert.Equal("GIMP2_TEX_Plugin_Windows.zip", AssetGlob.Match(assets, "GIMP2_*.zip")!.Name);
    }

    [Fact]
    public void Returns_null_when_nothing_matches()
    {
        Assert.Null(AssetGlob.Match(new[] { A("a.zip") }, "b-*.zip"));
    }
}
