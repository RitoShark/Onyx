using System.Text.Json;

namespace Onyx.Core.Catalog;

public static class CatalogReader
{
    public const int SupportedSchema = 1;

    public static Catalog Read(string json)
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(json);
        }
        catch (JsonException e)
        {
            throw new CatalogException($"Catalog is not valid JSON: {e.Message}");
        }

        using (doc)
        {
            var root = doc.RootElement;
            var schema = Int(root, "schema");
            if (schema > SupportedSchema)
                throw new CatalogException($"Catalog schema {schema} is newer than this build supports ({SupportedSchema}).");

            var revision = Int(root, "revision");
            var plugins = new List<PluginEntry>();
            var seen = new HashSet<string>(StringComparer.Ordinal);

            foreach (var p in Array(root, "plugins"))
            {
                var id = Str(p, "id");
                if (!seen.Add(id))
                    throw new CatalogException($"Duplicate plugin id '{id}'.");

                var steps = Array(p, "steps").Select(ReadStep).ToList();
                if (steps.Count == 0)
                    throw new CatalogException($"Plugin '{id}' has no install steps.");

                plugins.Add(new PluginEntry(
                    id,
                    Str(p, "name"),
                    Str(p, "summary"),
                    Str(p, "repo"),
                    Str(p, "host"),
                    Opt(p, "hostInstance"),
                    Opt(p, "category") ?? "misc",
                    Str(p, "asset"),
                    steps));
            }

            if (plugins.Count == 0)
                throw new CatalogException("Catalog contains no plugins.");

            return new Catalog(schema, revision, plugins);
        }
    }

    static InstallStep ReadStep(JsonElement e)
    {
        var verb = Str(e, "verb");
        var parsed = verb switch
        {
            "copy" => StepVerb.Copy,
            "copyDir" => StepVerb.CopyDir,
            "regsvr32" => StepVerb.Regsvr32,
            "sha256" => StepVerb.Sha256,
            _ => throw new CatalogException($"Unknown install verb '{verb}'.")
        };

        return new InstallStep(parsed, Opt(e, "from"), Opt(e, "to"));
    }

    static IEnumerable<JsonElement> Array(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Array
            ? v.EnumerateArray()
            : throw new CatalogException($"Missing array '{name}'.");

    static string Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString()!
            : throw new CatalogException($"Missing string '{name}'.");

    static string? Opt(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    static int Int(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var i)
            ? i
            : throw new CatalogException($"Missing integer '{name}'.");
}
