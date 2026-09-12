using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Onyx;
using Onyx.Core;
using Onyx.Core.Catalog;
using Onyx.Core.Hosts;
using Onyx.Core.Releases;
using Onyx.ViewModels;

internal static class Program
{
    static readonly DateTimeOffset Published = new(2026, 9, 12, 12, 0, 0, TimeSpan.Zero);

    [STAThread]
    static void Main(string[] args)
    {
        var output = Path.GetFullPath(args.Single());
        Directory.CreateDirectory(output);
        var app = new Application();
        foreach (var source in new[] { "Theme/Onyx.xaml", "Assets/Brand.xaml", "Theme/Typography.xaml", "Theme/Controls.xaml", "Theme/Window.xaml" })
            app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/Onyx;component/" + source) });
        app.Resources.Add("BoolToVisibility", new BoolToVisibilityConverter());
        app.Resources.Add("InverseBoolToVisibility", new InverseBoolToVisibilityConverter());
        app.Resources.Add("NotEmptyToVisibility", new NotEmptyToVisibilityConverter());

        var model = new MainViewModel(null!);
        var sections = ((string Key, string Title, string Icon)[])typeof(MainViewModel)
            .GetField("Sections", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        foreach (var section in sections)
        {
            var rows = CatalogSource.Embedded().Plugins.Where(p => p.Category == section.Key)
                .Select(p => new PluginRowViewModel(model, ExampleStatus(p))).ToList();
            if (rows.Count > 0) model.Groups.Add(new SectionViewModel(section.Title, section.Icon, rows));
        }

        var window = new MainWindow
        {
            DataContext = model, ShowActivated = false, ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.Manual, Left = -10000, Top = -10000
        };
        try
        {
            window.Show();
            Capture(window, output, "plugins");
            var blender = model.Groups.SelectMany(g => g.Rows).Single(r => r.PluginId == "aventurine-blender");
            model.ShowDetail(blender);
            blender.SelectedVersion = blender.Versions.Last();
            Capture(window, output, "versions");
            model.Sheet.Show("Blender is open",
                "Blender has to close before Onyx can change its plugin files. Nothing is forced — save your work first.",
                "Close Blender", () => Task.CompletedTask);
            Capture(window, output, "close-host");
        }
        finally
        {
            window.Close();
            app.Shutdown();
        }
        Console.WriteLine($"Saved three screenshots to {output}");
    }

    static PluginStatus ExampleStatus(PluginEntry plugin)
    {
        var (latestTag, previousTag) = plugin.Host switch
        {
            "photoshop" => ("v2.0.2", "v2.0.1"),
            "paintnet" => ("3.0", "2.9"),
            "gimp" => ("v4.2.0", "v4.1.0"),
            "maya" => ("v0.3.1", "v0.3.0"),
            "blender" => ("3.1.5", "3.1.4"),
            _ => ("v1.1.0", "v1.0.0")
        };
        var latest = new Release(latestTag, Published, false, "", []);
        var previous = new Release(previousTag, Published.AddDays(-14), false, "", []);
        var older = new Release("1.0.0", Published.AddDays(-30), false, "", []);
        var hosts = plugin.Host switch
        {
            "photoshop" => new[] { ("2026", "Adobe Photoshop 2026", @"C:\Apps\Adobe Photoshop 2026") },
            "paintnet" => [("default", "Paint.NET", @"C:\Apps\paint.net")],
            "gimp" => [],
            "maya" => [("2023", "Maya 2023", @"C:\Users\Example\Documents\maya\2023")],
            "blender" => [("4.1", "Blender 4.1", @"C:\Apps\Blender 4.1"), ("4.3", "Blender 4.3", @"C:\Apps\Blender 4.3")],
            "hematite" => [("default", "Hematite", @"C:\Tools\Hematite")],
            _ => [("default", "Windows Explorer", @"C:\Tools\Thumbnails")]
        };
        var installed = plugin.Host switch
        {
            "photoshop" or "thumbnails" => latestTag,
            "maya" or "blender" => previousTag,
            _ => null
        };
        var targets = hosts.Select(h => new PluginTarget(new HostInstance(plugin.Host, h.Item1, h.Item2, h.Item3),
            installed, ReleaseSet.UpdateAvailable(installed, latest), false)).ToList();
        return new PluginStatus(plugin, targets, latest, [latest, previous, older], null);
    }

    static void Capture(Window window, string output, string name)
    {
        window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        Thread.Sleep(250);
        window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        window.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(window);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(Path.Combine(output, name + ".png"));
        encoder.Save(stream);
    }
}
