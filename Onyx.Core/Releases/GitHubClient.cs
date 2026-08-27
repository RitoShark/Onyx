using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

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

        try
        {
            return await FetchAsync(repo, cached, ct);
        }
        catch (Exception e) when (e is RateLimitedException or HttpRequestException or TaskCanceledException)
        {
            if (cached is not null) return Parse(cached.Value.Json);

            try
            {
                return await FetchFeedAsync(repo, ct);
            }
            catch (Exception)
            {
                throw e;
            }
        }
    }

    public async Task<IReadOnlyList<ReleaseAsset>> ListAssetsAsync(string repo, string tag, CancellationToken ct)
    {
        var html = await GetWebAsync($"https://github.com/{repo}/releases/expanded_assets/{Uri.EscapeDataString(tag)}", ct);
        var prefix = $"/{repo}/releases/download/";

        return Regex.Matches(html, "href=\"(/[^\"]+/releases/download/[^\"]+)\"")
            .Select(m => m.Groups[1].Value)
            .Where(path => path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .Select(path => new ReleaseAsset(
                WebUtility.UrlDecode(path[(path.LastIndexOf('/') + 1)..]),
                $"https://github.com{path}",
                0))
            .ToList();
    }

    async Task<IReadOnlyList<Release>> FetchFeedAsync(string repo, CancellationToken ct)
    {
        var xml = await GetWebAsync($"https://github.com/{repo}/releases.atom", ct);
        var ns = (XNamespace)"http://www.w3.org/2005/Atom";

        return XDocument.Parse(xml).Root!
            .Elements(ns + "entry")
            .Select(e => new Release(
                e.Element(ns + "id")!.Value.Split('/')[^1],
                DateTimeOffset.Parse(e.Element(ns + "updated")!.Value, CultureInfo.InvariantCulture),
                false,
                "",
                []))
            .OrderByDescending(r => r.PublishedAt)
            .ToList();
    }

    async Task<string> GetWebAsync(string url, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("Onyx", "1.0"));

        using var response = await http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(ct);
    }

    async Task<IReadOnlyList<Release>> FetchAsync(string repo, (string ETag, string Json)? cached, CancellationToken ct)
    {
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
            throw new RateLimitedException();

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
