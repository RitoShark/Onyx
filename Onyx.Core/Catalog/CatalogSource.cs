namespace Onyx.Core.Catalog;

public static class CatalogSource
{
    public const string RemoteUrl =
        "https://raw.githubusercontent.com/RitoShark/Onyx/main/Onyx.Core/Catalog/catalog.json";

    public static Catalog Embedded()
    {
        using var stream = typeof(CatalogSource).Assembly.GetManifestResourceStream("Onyx.Core.catalog.json")
            ?? throw new CatalogException("Embedded catalog is missing.");
        using var reader = new StreamReader(stream);
        return CatalogReader.Read(reader.ReadToEnd());
    }

    public static Catalog Best(Catalog embedded, string? cachedJson)
    {
        if (cachedJson is null) return embedded;

        try
        {
            var cached = CatalogReader.Read(cachedJson);
            return cached.Revision > embedded.Revision ? cached : embedded;
        }
        catch (CatalogException)
        {
            return embedded;
        }
    }
}
