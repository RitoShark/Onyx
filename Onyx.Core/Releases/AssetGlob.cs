using System.Text.RegularExpressions;

namespace Onyx.Core.Releases;

public static class AssetGlob
{
    public static ReleaseAsset? Match(IReadOnlyList<ReleaseAsset> assets, string pattern)
    {
        var rx = new Regex(
            "^" + string.Join(".*", pattern.Split('*').Select(Regex.Escape)) + "$",
            RegexOptions.IgnoreCase);

        return assets.FirstOrDefault(a => rx.IsMatch(a.Name));
    }
}
