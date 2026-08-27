using System.Net;
using System.Net.Http.Headers;
using Onyx.Core.Releases;

namespace Onyx.Tests.Releases;

public class GitHubClientTests
{
    public static string Fixture(string name) => File.ReadAllText(Path.Combine("Fixtures", name));

    [Fact]
    public async Task Parses_recorded_releases()
    {
        var handler = StubHandler.Json(Fixture("releases-texthumbnailprovider.json"));
        var client = new GitHubClient(new HttpClient(handler));

        var releases = await client.ListReleasesAsync("RitoShark/TexThumbnailProvider", default);

        var latest = releases.Single(r => r.Tag == "v1.1.0");
        Assert.Contains(latest.Assets, a => a.Name == "TexThumbnailProvider.dll");
        Assert.Contains(latest.Assets, a => a.Name == "TexThumbnailProvider.dll.sha256");
        Assert.All(latest.Assets, a => Assert.StartsWith("https://", a.DownloadUrl));
        Assert.False(latest.PreRelease);
    }

    [Fact]
    public async Task Parses_a_release_history_thirty_deep()
    {
        var handler = StubHandler.Json(Fixture("releases-aventurine.json"));
        var client = new GitHubClient(new HttpClient(handler));

        var releases = await client.ListReleasesAsync("RitoShark/Aventurine-League-Tools", default);

        Assert.Equal(30, releases.Count);
        Assert.Equal("3.1.5", ReleaseSet.Latest(releases, includePrerelease: false)!.Tag);
        Assert.Equal("Aventurine-3.1.5.zip", AssetGlob.Match(releases[0].Assets, "Aventurine-*.zip")!.Name);
    }

    [Fact]
    public async Task Sends_a_user_agent_and_the_api_accept_header()
    {
        var handler = StubHandler.Json("[]");
        var client = new GitHubClient(new HttpClient(handler));

        await client.ListReleasesAsync("RitoShark/Flint", default);

        var request = Assert.Single(handler.Requests);
        Assert.Equal("Onyx", request.Headers.UserAgent.Single().Product!.Name);
        Assert.Contains("application/vnd.github+json", request.Headers.Accept.Select(a => a.MediaType));
    }

    [Fact]
    public async Task Throws_RateLimited_when_the_quota_is_exhausted()
    {
        var handler = new StubHandler(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.Forbidden) { Content = new StringContent("{}") };
            response.Headers.Add("X-RateLimit-Remaining", "0");
            return response;
        });
        var client = new GitHubClient(new HttpClient(handler));

        await Assert.ThrowsAsync<RateLimitedException>(
            () => client.ListReleasesAsync("RitoShark/Flint", default));
    }

    static HttpResponseMessage RateLimited()
    {
        var response = new HttpResponseMessage(HttpStatusCode.Forbidden) { Content = new StringContent("{}") };
        response.Headers.Add("X-RateLimit-Remaining", "0");
        return response;
    }

    const string Feed =
        "<feed xmlns=\"http://www.w3.org/2005/Atom\">" +
        "<entry><id>tag:github.com,2008:Repository/1/v0.6.0</id><updated>2026-08-25T10:00:00Z</updated></entry>" +
        "<entry><id>tag:github.com,2008:Repository/1/v0.6.1</id><updated>2026-08-25T22:55:58Z</updated></entry>" +
        "</feed>";

    [Fact]
    public async Task A_rate_limit_without_a_cache_falls_back_to_the_release_feed()
    {
        var handler = new StubHandler(request =>
            request.RequestUri!.Host == "github.com"
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Feed) }
                : RateLimited());

        var releases = await new GitHubClient(new HttpClient(handler))
            .ListReleasesAsync("RitoShark/Hematite", default);

        Assert.Equal(["v0.6.1", "v0.6.0"], releases.Select(r => r.Tag));
        Assert.All(releases, r => Assert.Empty(r.Assets));
        Assert.Contains(handler.Requests, r => r.RequestUri!.AbsolutePath.EndsWith("releases.atom"));
    }

    [Fact]
    public async Task A_rate_limit_with_a_dead_feed_still_reports_the_rate_limit()
    {
        var handler = new StubHandler(request =>
            request.RequestUri!.Host == "github.com"
                ? new HttpResponseMessage(HttpStatusCode.NotFound)
                : RateLimited());

        await Assert.ThrowsAsync<RateLimitedException>(
            () => new GitHubClient(new HttpClient(handler)).ListReleasesAsync("RitoShark/Hematite", default));
    }

    [Fact]
    public async Task Assets_are_scraped_from_the_expanded_assets_page()
    {
        const string html =
            "<div><a href=\"/RitoShark/Hematite/releases/download/v0.6.1/hematite-cli.exe\" rel=\"nofollow\">x</a>" +
            "<a href=\"/RitoShark/Hematite/releases/download/v0.6.1/hematite-cli-windows-x64.zip\">y</a>" +
            "<a href=\"/RitoShark/Hematite/archive/refs/tags/v0.6.1.zip\">source</a></div>";

        var handler = StubHandler.Json(html);

        var assets = await new GitHubClient(new HttpClient(handler))
            .ListAssetsAsync("RitoShark/Hematite", "v0.6.1", default);

        Assert.Equal(2, assets.Count);
        Assert.Equal("hematite-cli.exe", assets[0].Name);
        Assert.Equal("https://github.com/RitoShark/Hematite/releases/download/v0.6.1/hematite-cli.exe", assets[0].DownloadUrl);
    }

    [Fact]
    public async Task A_plain_forbidden_is_not_mistaken_for_a_rate_limit()
    {
        var handler = StubHandler.Json("{}", HttpStatusCode.Forbidden);
        var client = new GitHubClient(new HttpClient(handler));

        await Assert.ThrowsAsync<HttpRequestException>(
            () => client.ListReleasesAsync("RitoShark/Flint", default));
    }

    sealed class MemoryCache : IReleaseCache
    {
        readonly Dictionary<string, (string ETag, string Json)> _entries = new(StringComparer.Ordinal);

        public (string ETag, string Json)? Get(string repo) =>
            _entries.TryGetValue(repo, out var entry) ? entry : null;

        public void Put(string repo, string etag, string json) => _entries[repo] = (etag, json);
    }

    [Fact]
    public async Task A_first_call_stores_the_etag()
    {
        var cache = new MemoryCache();
        var handler = new StubHandler(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(Fixture("releases-texthumbnailprovider.json"))
            };
            response.Headers.ETag = new EntityTagHeaderValue("\"abc123\"");
            return response;
        });

        await new GitHubClient(new HttpClient(handler), cache)
            .ListReleasesAsync("RitoShark/TexThumbnailProvider", default);

        Assert.Equal("\"abc123\"", cache.Get("RitoShark/TexThumbnailProvider")!.Value.ETag);
    }

    [Fact]
    public async Task A_second_call_sends_the_etag_and_serves_a_304_from_cache()
    {
        var cache = new MemoryCache();
        cache.Put("RitoShark/TexThumbnailProvider", "\"abc123\"", Fixture("releases-texthumbnailprovider.json"));

        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.NotModified));

        var releases = await new GitHubClient(new HttpClient(handler), cache)
            .ListReleasesAsync("RitoShark/TexThumbnailProvider", default);

        Assert.Contains(releases, r => r.Tag == "v1.1.0");
        Assert.Equal("\"abc123\"", Assert.Single(handler.Requests).Headers.GetValues("If-None-Match").Single());
    }

    [Fact]
    public async Task A_rate_limit_falls_back_to_the_cache_instead_of_throwing()
    {
        var cache = new MemoryCache();
        cache.Put("RitoShark/TexThumbnailProvider", "\"abc123\"", Fixture("releases-texthumbnailprovider.json"));

        var handler = new StubHandler(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.Forbidden) { Content = new StringContent("{}") };
            response.Headers.Add("X-RateLimit-Remaining", "0");
            return response;
        });

        var releases = await new GitHubClient(new HttpClient(handler), cache)
            .ListReleasesAsync("RitoShark/TexThumbnailProvider", default);

        Assert.Contains(releases, r => r.Tag == "v1.1.0");
    }

    [Fact]
    public async Task A_server_error_falls_back_to_the_cache_too()
    {
        var cache = new MemoryCache();
        cache.Put("RitoShark/TexThumbnailProvider", "\"abc123\"", Fixture("releases-texthumbnailprovider.json"));

        var handler = StubHandler.Json("{}", HttpStatusCode.Forbidden);

        var releases = await new GitHubClient(new HttpClient(handler), cache)
            .ListReleasesAsync("RitoShark/TexThumbnailProvider", default);

        Assert.Contains(releases, r => r.Tag == "v1.1.0");
    }

    [Fact]
    public async Task A_rate_limit_with_no_cache_still_throws()
    {
        var handler = new StubHandler(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.Forbidden) { Content = new StringContent("{}") };
            response.Headers.Add("X-RateLimit-Remaining", "0");
            return response;
        });

        await Assert.ThrowsAsync<RateLimitedException>(
            () => new GitHubClient(new HttpClient(handler), new MemoryCache())
                .ListReleasesAsync("RitoShark/Flint", default));
    }
}
