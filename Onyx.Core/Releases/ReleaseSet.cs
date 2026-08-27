namespace Onyx.Core.Releases;

public static class ReleaseSet
{
    public static Release? Latest(IReadOnlyList<Release> releases, bool includePrerelease) =>
        releases
            .Where(r => includePrerelease || !r.PreRelease)
            .OrderByDescending(r => r.PublishedAt)
            .FirstOrDefault();

    public static bool UpdateAvailable(string? installedTag, Release? latest) =>
        installedTag is not null && latest is not null &&
        !string.Equals(installedTag, latest.Tag, StringComparison.Ordinal);
}
