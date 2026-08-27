using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace Onyx.Core.Releases;

public sealed class RateLimitedException() : Exception("GitHub rate limit reached. Try again later.");

public sealed class GitHubClient(HttpClient http)
{
    public async Task<IReadOnlyList<Release>> ListReleasesAsync(string repo, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"https://api.github.com/repos/{repo}/releases?per_page=100");

        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("Onyx", "1.0"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

        using var response = await http.SendAsync(request, ct);

        if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests &&
            response.Headers.TryGetValues("X-RateLimit-Remaining", out var remaining) &&
            remaining.FirstOrDefault() == "0")
            throw new RateLimitedException();

        response.EnsureSuccessStatusCode();

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
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
