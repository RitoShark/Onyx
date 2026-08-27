using System.IO;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Onyx;

public static class IconStore
{
    static readonly Dictionary<string, ImageSource?> Cache = new(StringComparer.OrdinalIgnoreCase);

    static readonly Dictionary<string, Lazy<ImageSource>> Bundled = new(StringComparer.Ordinal)
    {
        ["hematite"] = Packed("hematite-logo.png"),
        ["blender"] = Packed("blender-logo.png"),
        ["gimp"] = Packed("gimp-logo.png")
    };

    static Lazy<ImageSource> Packed(string file) =>
        new(() => new BitmapImage(new Uri($"pack://application:,,,/Assets/{file}")));

    public static ImageSource? For(string hostId, string? exePath)
    {
        if (hostId == "hematite") return Bundled["hematite"].Value;

        if (exePath is null)
            return Bundled.TryGetValue(hostId, out var bundled) ? bundled.Value : null;

        if (!Cache.TryGetValue(exePath, out var image))
        {
            image = Extract(exePath);
            Cache[exePath] = image;
        }

        if (image is null && Bundled.TryGetValue(hostId, out var fallback))
            return fallback.Value;

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
