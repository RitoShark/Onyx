using Onyx.Core.Catalog;

namespace Onyx.Tests.Catalog;

public class CatalogReaderTests
{
    const string Minimal = """
    {
      "schema": 1,
      "revision": 3,
      "plugins": [
        {
          "id": "ritotex-photoshop",
          "name": "RitoTex for Photoshop",
          "summary": "Open and save .tex textures directly in Photoshop.",
          "repo": "RitoShark/RitoTex-Photoshop",
          "host": "photoshop",
          "asset": "RitoTex.8bi",
          "steps": [ { "verb": "copy", "from": "RitoTex.8bi", "to": "{host}/Plug-ins/RitoTex.8bi" } ]
        }
      ]
    }
    """;

    [Fact]
    public void Reads_a_minimal_catalog()
    {
        var catalog = CatalogReader.Read(Minimal);

        Assert.Equal(1, catalog.Schema);
        Assert.Equal(3, catalog.Revision);
        var plugin = Assert.Single(catalog.Plugins);
        Assert.Equal("ritotex-photoshop", plugin.Id);
        Assert.Equal("photoshop", plugin.Host);
        Assert.Null(plugin.HostInstance);
        var step = Assert.Single(plugin.Steps);
        Assert.Equal(StepVerb.Copy, step.Verb);
        Assert.Equal("{host}/Plug-ins/RitoTex.8bi", step.To);
    }

    [Fact]
    public void Reads_a_pinned_host_instance()
    {
        var json = Minimal.Replace("\"host\": \"photoshop\",", "\"host\": \"gimp\", \"hostInstance\": \"3.0\",");
        Assert.Equal("3.0", CatalogReader.Read(json).Plugins[0].HostInstance);
    }

    [Fact]
    public void Rejects_a_newer_schema_whole()
    {
        var json = Minimal.Replace("\"schema\": 1", "\"schema\": 2");
        var ex = Assert.Throws<CatalogException>(() => CatalogReader.Read(json));
        Assert.Contains("schema", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Rejects_an_unknown_verb()
    {
        var json = Minimal.Replace("\"verb\": \"copy\"", "\"verb\": \"runScript\"");
        var ex = Assert.Throws<CatalogException>(() => CatalogReader.Read(json));
        Assert.Contains("runScript", ex.Message);
    }

    [Fact]
    public void Rejects_a_duplicate_plugin_id()
    {
        var json = """
        {
          "schema": 1, "revision": 1,
          "plugins": [
            { "id": "a", "name": "A", "summary": "s", "repo": "o/r", "host": "blender",
              "asset": "a.zip", "steps": [ { "verb": "copyDir", "from": ".", "to": "{host}/x" } ] },
            { "id": "a", "name": "A", "summary": "s", "repo": "o/r", "host": "blender",
              "asset": "a.zip", "steps": [ { "verb": "copyDir", "from": ".", "to": "{host}/x" } ] }
          ]
        }
        """;

        var ex = Assert.Throws<CatalogException>(() => CatalogReader.Read(json));
        Assert.Contains("Duplicate", ex.Message);
    }

    [Fact]
    public void Rejects_malformed_json()
    {
        Assert.Throws<CatalogException>(() => CatalogReader.Read("{ not json"));
    }

    [Fact]
    public void Rejects_a_plugin_with_no_steps()
    {
        var json = Minimal.Replace("""[ { "verb": "copy", "from": "RitoTex.8bi", "to": "{host}/Plug-ins/RitoTex.8bi" } ]""", "[]");
        Assert.Throws<CatalogException>(() => CatalogReader.Read(json));
    }
}
