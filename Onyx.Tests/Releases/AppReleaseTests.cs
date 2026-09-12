using Onyx.Core.Releases;

namespace Onyx.Tests.Releases;

public sealed class AppReleaseTests
{
    static Release Release(string tag, bool prerelease = false) =>
        new(tag, DateTimeOffset.UtcNow, prerelease, "", []);

    [Fact]
    public void Picks_highest_stable_version_instead_of_publication_order()
    {
        var latest = AppRelease.Latest([Release("v1.10.0"), Release("v1.9.0"), Release("v2.0.0", true), Release("v3.0.0-beta"), Release("nightly")]);

        Assert.Equal("v1.10.0", latest!.Tag);
        Assert.True(latest.IsNewerThan(new Version(1, 9, 0)));
        Assert.False(latest.IsNewerThan(new Version(1, 11, 0)));
        Assert.Equal("https://github.com/RitoShark/Onyx/releases/tag/v1.10.0", latest.Url);
    }

    [Theory]
    [InlineData("v1.0")]
    [InlineData("1.0.0")]
    [InlineData("V1.0.0.0")]
    [InlineData("1.0.0+build.1")]
    public void Equivalent_versions_do_not_offer_an_update(string tag)
    {
        Assert.False(AppRelease.Latest([Release(tag)])!.IsNewerThan(new Version(1, 0, 0)));
    }

    [Fact]
    public void No_stable_release_is_not_reported_as_current()
    {
        Assert.Null(AppRelease.Latest([]));
        Assert.Null(AppRelease.Latest([Release("nightly"), Release("2.0.0-rc1")]));
    }
}
