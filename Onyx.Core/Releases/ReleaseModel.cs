namespace Onyx.Core.Releases;

public sealed record ReleaseAsset(string Name, string DownloadUrl, long Size);

public sealed record Release(
    string Tag,
    DateTimeOffset PublishedAt,
    bool PreRelease,
    string Body,
    IReadOnlyList<ReleaseAsset> Assets);
