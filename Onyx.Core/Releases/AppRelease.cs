namespace Onyx.Core.Releases;

public sealed record AppRelease(string Tag, Version Version)
{
    public const string Repository = "RitoShark/Onyx";

    public string Url => $"https://github.com/{Repository}/releases/tag/{Uri.EscapeDataString(Tag)}";

    public bool IsNewerThan(Version current) => Version > Normalize(current);

    public static AppRelease? Latest(IReadOnlyList<Release> releases) => releases
        .Where(release => !release.PreRelease)
        .Select(release => TryParse(release.Tag))
        .OfType<AppRelease>()
        .OrderByDescending(release => release.Version)
        .FirstOrDefault();

    static AppRelease? TryParse(string tag)
    {
        var number = tag.TrimStart('v', 'V').Split('+')[0];
        return System.Version.TryParse(number, out var version)
            ? new AppRelease(tag, Normalize(version))
            : null;
    }

    static Version Normalize(Version version) =>
        new(version.Major, version.Minor, Math.Max(0, version.Build), Math.Max(0, version.Revision));
}
