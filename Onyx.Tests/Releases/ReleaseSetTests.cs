using Onyx.Core.Releases;

namespace Onyx.Tests.Releases;

public class ReleaseSetTests
{
    static Release R(string tag, string date, bool pre = false) =>
        new(tag, DateTimeOffset.Parse(date), pre, "", []);

    [Fact]
    public void Latest_is_the_most_recently_published_stable_release()
    {
        var releases = new[] { R("v1.0.0", "2025-11-13"), R("v1.1.0", "2026-05-29") };
        Assert.Equal("v1.1.0", ReleaseSet.Latest(releases, includePrerelease: false)!.Tag);
    }

    [Fact]
    public void Latest_ignores_prereleases_unless_asked()
    {
        var releases = new[] { R("v1.1.0", "2026-05-29"), R("v2.0.0-rc1", "2026-06-01", pre: true) };
        Assert.Equal("v1.1.0", ReleaseSet.Latest(releases, false)!.Tag);
        Assert.Equal("v2.0.0-rc1", ReleaseSet.Latest(releases, true)!.Tag);
    }

    [Fact]
    public void Latest_is_null_for_an_empty_set()
    {
        Assert.Null(ReleaseSet.Latest([], false));
    }

    [Fact]
    public void Update_is_available_when_the_installed_tag_is_not_the_latest()
    {
        var latest = R("v1.1.0", "2026-05-29");
        Assert.True(ReleaseSet.UpdateAvailable("v1.0.0", latest));
        Assert.False(ReleaseSet.UpdateAvailable("v1.1.0", latest));
    }

    [Fact]
    public void A_deliberate_downgrade_still_reads_as_an_update_being_available()
    {
        var latest = R("3.1.5", "2026-07-24");
        Assert.True(ReleaseSet.UpdateAvailable("3.1.2", latest));
    }

    [Fact]
    public void Update_is_not_available_when_nothing_is_installed()
    {
        Assert.False(ReleaseSet.UpdateAvailable(null, R("v1.1.0", "2026-05-29")));
    }
}
