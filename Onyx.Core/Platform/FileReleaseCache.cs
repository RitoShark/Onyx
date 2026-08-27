using System.Text.Json;
using Onyx.Core.Releases;

namespace Onyx.Core.Platform;

public sealed class FileReleaseCache(string directory) : IReleaseCache
{
    sealed record Entry(string ETag, string Json);

    public (string ETag, string Json)? Get(string repo)
    {
        try
        {
            var path = PathFor(repo);
            if (!File.Exists(path)) return null;

            var entry = JsonSerializer.Deserialize<Entry>(File.ReadAllText(path));
            return entry is null ? null : (entry.ETag, entry.Json);
        }
        catch (Exception e) when (e is IOException or JsonException)
        {
            return null;
        }
    }

    public void Put(string repo, string etag, string json)
    {
        try
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(PathFor(repo), JsonSerializer.Serialize(new Entry(etag, json)));
        }
        catch (IOException)
        {
        }
    }

    string PathFor(string repo) =>
        Path.Combine(directory, repo.Replace('/', '-') + ".json");
}
