using Onyx.Core.Catalog;
using Onyx.Core.Processes;

namespace Onyx.Tests.Catalog;

public class EmbeddedCatalogTests
{
    static readonly Core.Catalog.Catalog Shipped = CatalogSource.Embedded();

    [Fact]
    public void The_shipped_catalog_parses()
    {
        Assert.Equal(8, Shipped.Plugins.Count);
    }

    [Theory]
    [InlineData("ritotex-photoshop", "photoshop", "textures")]
    [InlineData("tex-paintnet", "paintnet", "textures")]
    [InlineData("tex-gimp", "gimp", "textures")]
    [InlineData("ritoshark-maya", "maya", "dcc")]
    [InlineData("aventurine-blender", "blender", "dcc")]
    [InlineData("tex-thumbnails", "thumbnails", "system")]
    [InlineData("ltk-tex-thumbnails", "ltkthumbs", "system")]
    [InlineData("hematite", "hematite", "misc")]
    public void Every_expected_plugin_is_present_on_its_host_and_category(string id, string host, string category)
    {
        var plugin = Shipped.Plugins.Single(p => p.Id == id);
        Assert.Equal(host, plugin.Host);
        Assert.Equal(category, plugin.Category);
    }

    [Fact]
    public void Every_plugin_with_a_launchable_host_has_a_process_guard()
    {
        foreach (var plugin in Shipped.Plugins.Where(p => p.Host is not ("thumbnails" or "ltkthumbs" or "hematite")))
            Assert.NotEmpty(ProcessGuard.ProcessNamesFor(plugin.Host));
    }

    [Fact]
    public void Hematite_is_a_single_exe_copied_into_a_per_user_folder()
    {
        var hematite = Shipped.Plugins.Single(p => p.Id == "hematite");
        var step = Assert.Single(hematite.Steps);

        Assert.Equal(StepVerb.Copy, step.Verb);
        Assert.Equal("hematite-cli.exe", step.From);
        Assert.Equal("{host}/hematite-cli.exe", step.To);
    }

    [Fact]
    public void Gimp_is_one_plugin_whose_variants_pick_the_asset_per_major_version()
    {
        var gimp = Shipped.Plugins.Single(p => p.Id == "tex-gimp");

        var (asset2, steps2) = gimp.For("2.10");
        Assert.Equal("GIMP2_TEX_Plugin_Windows.zip", asset2);
        Assert.Equal("{host}/plug-ins", Assert.Single(steps2).To);

        var (asset3, steps3) = gimp.For("3.0");
        Assert.Equal("GIMP3_TEX_Plugin_Windows.zip", asset3);
        Assert.Equal("{host}/plug-ins/gimp3_tex_plugin", Assert.Single(steps3).To);
    }

    [Fact]
    public void Gimp_supports_both_major_versions_and_nothing_else()
    {
        var gimp = Shipped.Plugins.Single(p => p.Id == "tex-gimp");

        Assert.True(gimp.Supports("2.10"));
        Assert.True(gimp.Supports("3.0"));
        Assert.False(gimp.Supports("4.0"));
    }

    [Fact]
    public void A_plugin_without_variants_supports_every_instance()
    {
        var blender = Shipped.Plugins.Single(p => p.Id == "aventurine-blender");
        Assert.True(blender.Supports("4.3"));
        Assert.True(blender.Supports("anything"));
    }

    [Fact]
    public void Maya_carries_the_vendored_ritoshark_library_into_the_scripts_folder()
    {
        var maya = Shipped.Plugins.Single(p => p.Id == "ritoshark-maya");

        Assert.Contains(maya.Steps, s => s.From == "vendor/ritoshark" && s.To == "{host}/scripts/ritoshark");
        Assert.Contains(maya.Steps, s => s.From == "plug-ins");
        Assert.Contains(maya.Steps, s => s.From == "scripts");
        Assert.Contains(maya.Steps, s => s.From == "prefs");
    }

    [Fact]
    public void The_thumbnail_provider_verifies_its_checksum_and_registers_itself()
    {
        var thumbnails = Shipped.Plugins.Single(p => p.Id == "tex-thumbnails");

        Assert.Contains(thumbnails.Steps, s => s.Verb == StepVerb.Sha256);
        Assert.Contains(thumbnails.Steps, s => s.Verb == StepVerb.Regsvr32);
    }

    [Fact]
    public void Every_plugin_names_a_ritoshark_repository_except_the_ltk_alternative()
    {
        foreach (var plugin in Shipped.Plugins.Where(p => p.Id != "ltk-tex-thumbnails"))
            Assert.StartsWith("RitoShark/", plugin.Repo);

        Assert.Equal("LeagueToolkit/ltk-tex-utils", Shipped.Plugins.Single(p => p.Id == "ltk-tex-thumbnails").Repo);
    }

    [Fact]
    public void The_ltk_handler_copies_and_registers_a_single_dll()
    {
        var ltk = Shipped.Plugins.Single(p => p.Id == "ltk-tex-thumbnails");

        Assert.Equal(2, ltk.Steps.Count);
        Assert.Equal(StepVerb.Copy, ltk.Steps[0].Verb);
        Assert.Equal(StepVerb.Regsvr32, ltk.Steps[1].Verb);
        Assert.Equal("{host}/ltk-tex-thumb-handler.dll", ltk.Steps[1].To);
        Assert.Contains("Not recommended", ltk.Description);
    }

    [Fact]
    public void No_step_target_escapes_the_host_directory()
    {
        foreach (var step in Shipped.Plugins.SelectMany(p => p.Steps).Where(s => s.To is not null))
            Assert.DoesNotContain("..", step.To!);
    }
}
