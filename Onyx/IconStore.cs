using System.IO;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Onyx;

public static class IconStore
{
    static readonly Dictionary<string, ImageSource?> Cache = new(StringComparer.OrdinalIgnoreCase);

    static readonly Lazy<ImageSource> HematiteLogo = new(() =>
        new BitmapImage(new Uri("pack://application:,,,/Assets/hematite-logo.png")));

    public static ImageSource? For(string hostId, string? exePath)
    {
        if (hostId == "hematite") return HematiteLogo.Value;
        if (exePath is null) return null;

        if (Cache.TryGetValue(exePath, out var cached)) return cached;

        var image = Extract(exePath);
        Cache[exePath] = image;
        return image;
    }

    static ImageSource? Extract(string exePath)
    {
        try
        {
            if (!File.Exists(exePath)) return null;

            using var icon = System.Drawing.Icon.ExtractAssociatedIcon(exePath);
            if (icon is null) return null;

            var source = Imaging.CreateBitmapSourceFromHIcon(
                icon.Handle,
                System.Windows.Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());
            source.Freeze();
            return source;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
