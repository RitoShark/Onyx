using System.Net;
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

    [Fact]
    public async Task A_plain_forbidden_is_not_mistaken_for_a_rate_limit()
    {
        var handler = StubHandler.Json("{}", HttpStatusCode.Forbidden);
        var client = new GitHubClient(new HttpClient(handler));

        await Assert.ThrowsAsync<HttpRequestException>(
            () => client.ListReleasesAsync("RitoShark/Flint", default));
    }
}
