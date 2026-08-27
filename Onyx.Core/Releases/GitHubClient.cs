using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace Onyx.Core.Releases;

public sealed class RateLimitedException() : Exception("GitHub rate limit reached. Try again later.");

public interface IReleaseCache
{
    (string ETag, string Json)? Get(string repo);
    void Put(string repo, string etag, string json);
}

public sealed class GitHubClient(HttpClient http, IReleaseCache? cache = null)
{
    public async Task<IReadOnlyList<Release>> ListReleasesAsync(string repo, CancellationToken ct)
    {
        var cached = cache?.Get(repo);

        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"https://api.github.com/repos/{repo}/releases?per_page=100");

        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("Onyx", "1.0"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

        if (cached is not null)
            request.Headers.TryAddWithoutValidation("If-None-Match", cached.Value.ETag);

        using var response = await http.SendAsync(request, ct);

        if (response.StatusCode == HttpStatusCode.NotModified && cached is not null)
            return Parse(cached.Value.Json);

        if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests &&
            response.Headers.TryGetValues("X-RateLimit-Remaining", out var remaining) &&
            remaining.FirstOrDefault() == "0")
        {
            if (cached is not null) return Parse(cached.Value.Json);
            throw new RateLimitedException();
        }

        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(ct);

        var etag = response.Headers.ETag?.Tag;
        if (etag is not null) cache?.Put(repo, etag, json);

        return Parse(json);
    }

    static IReadOnlyList<Release> Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.EnumerateArray().Select(ReadRelease).ToList();
    }

    static Release ReadRelease(JsonElement e) => new(
        e.GetProperty("tag_name").GetString()!,
        e.GetProperty("published_at").GetDateTimeOffset(),
        e.GetProperty("prerelease").GetBoolean(),
        e.TryGetProperty("body", out var b) && b.ValueKind == JsonValueKind.String ? b.GetString()! : "",
        e.GetProperty("assets").EnumerateArray().Select(a => new ReleaseAsset(
            a.GetProperty("name").GetString()!,
            a.GetProperty("browser_download_url").GetString()!,
            a.GetProperty("size").GetInt64())).ToList());
}
