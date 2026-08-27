using Onyx.Core.Catalog;
using Onyx.Core.Processes;

namespace Onyx.Tests.Catalog;

public class EmbeddedCatalogTests
{
    static readonly Core.Catalog.Catalog Shipped = CatalogSource.Embedded();

    [Fact]
    public void The_shipped_catalog_parses()
    {
        Assert.Equal(7, Shipped.Plugins.Count);
    }

    [Theory]
    [InlineData("ritotex-photoshop", "photoshop")]
    [InlineData("tex-paintnet", "paintnet")]
    [InlineData("tex-gimp2", "gimp")]
    [InlineData("tex-gimp3", "gimp")]
    [InlineData("ritoshark-maya", "maya")]
    [InlineData("aventurine-blender", "blender")]
    [InlineData("tex-thumbnails", "thumbnails")]
    public void Every_expected_plugin_is_present_on_its_host(string id, string host)
    {
        Assert.Equal(host, Shipped.Plugins.Single(p => p.Id == id).Host);
    }

    [Fact]
    public void Every_plugin_targets_a_host_that_has_a_process_guard()
    {
        foreach (var plugin in Shipped.Plugins)
            Assert.NotEmpty(ProcessGuard.ProcessNamesFor(plugin.Host));
    }

    [Fact]
    public void The_gimp_entries_target_different_versions_and_different_assets()
    {
        var two = Shipped.Plugins.Single(p => p.Id == "tex-gimp2");
        var three = Shipped.Plugins.Single(p => p.Id == "tex-gimp3");

        Assert.Equal("2.10", two.HostInstance);
        Assert.Equal("3.0", three.HostInstance);
        Assert.NotEqual(two.Asset, three.Asset);
    }

    [Fact]
    public void Gimp3_installs_into_its_own_named_folder_as_the_plugin_requires()
    {
        var three = Shipped.Plugins.Single(p => p.Id == "tex-gimp3");
        Assert.Equal("{host}/plug-ins/gimp3_tex_plugin", Assert.Single(three.Steps).To);
    }

    [Fact]
    public void Gimp2_installs_flat_into_the_plugins_folder()
    {
        var two = Shipped.Plugins.Single(p => p.Id == "tex-gimp2");
        Assert.Equal("{host}/plug-ins", Assert.Single(two.Steps).To);
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
    public void Every_plugin_names_a_ritoshark_repository()
    {
        foreach (var plugin in Shipped.Plugins)
            Assert.StartsWith("RitoShark/", plugin.Repo);
    }

    [Fact]
    public void No_step_target_escapes_the_host_directory()
    {
        foreach (var step in Shipped.Plugins.SelectMany(p => p.Steps).Where(s => s.To is not null))
            Assert.DoesNotContain("..", step.To!);
    }
}
