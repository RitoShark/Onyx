using Onyx.Core.Catalog;

namespace Onyx.Tests.Catalog;

public class CatalogSourceTests
{
    static string WithRevision(int revision) => $$"""
    {
      "schema": 1,
      "revision": {{revision}},
      "plugins": [
        { "id": "x", "name": "X", "summary": "s", "repo": "RitoShark/R", "host": "blender",
          "asset": "a.zip", "steps": [ { "verb": "copyDir", "from": ".", "to": "{host}/x" } ] }
      ]
    }
    """;

    [Fact]
    public void A_newer_cached_catalog_wins()
    {
        var embedded = CatalogReader.Read(WithRevision(1));
        Assert.Equal(5, CatalogSource.Best(embedded, WithRevision(5)).Revision);
    }

    [Fact]
    public void An_older_cached_catalog_loses()
    {
        var embedded = CatalogReader.Read(WithRevision(9));
        Assert.Equal(9, CatalogSource.Best(embedded, WithRevision(2)).Revision);
    }

    [Fact]
    public void An_equal_revision_keeps_the_embedded_copy()
    {
        var embedded = CatalogReader.Read(WithRevision(4));
        Assert.Same(embedded, CatalogSource.Best(embedded, WithRevision(4)));
    }

    [Fact]
    public void A_corrupt_cached_catalog_falls_back_to_embedded()
    {
        var embedded = CatalogReader.Read(WithRevision(1));
        Assert.Equal(1, CatalogSource.Best(embedded, "{ not json").Revision);
    }

    [Fact]
    public void A_newer_schema_in_the_cache_falls_back_to_embedded()
    {
        var embedded = CatalogReader.Read(WithRevision(1));
        var future = WithRevision(99).Replace("\"schema\": 1", "\"schema\": 2");
        Assert.Equal(1, CatalogSource.Best(embedded, future).Revision);
    }

    [Fact]
    public void No_cache_means_embedded()
    {
        var embedded = CatalogReader.Read(WithRevision(1));
        Assert.Same(embedded, CatalogSource.Best(embedded, null));
    }
}
