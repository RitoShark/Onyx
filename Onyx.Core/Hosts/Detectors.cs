namespace Onyx.Core.Hosts;

public sealed class BlenderDetector(IFileSystem fs, IKnownFolders folders) : IHostDetector
{
    public string HostId => "blender";

    public IReadOnlyList<HostInstance> Detect()
    {
        var root = Path.Combine(folders.AppData, "Blender Foundation", "Blender");
        if (!fs.DirectoryExists(root)) return [];

        return fs.Directories(root)
            .Select(Path.GetFileName)
            .Where(v => v is not null && Version.TryParse(v, out _))
            .OrderBy(v => Version.Parse(v!))
            .Select(v => new HostInstance(HostId, v!, $"Blender {v}", Path.Combine(root, v!),
                Exe(Path.Combine(folders.ProgramFiles, "Blender Foundation", $"Blender {v}", "blender.exe"))))
            .ToList();
    }

    string? Exe(string candidate) => fs.FileExists(candidate) ? candidate : null;
}

public sealed class GimpDetector(IFileSystem fs, IKnownFolders folders) : IHostDetector
{
    public string HostId => "gimp";

    public IReadOnlyList<HostInstance> Detect()
    {
        var root = Path.Combine(folders.AppData, "GIMP");
        if (!fs.DirectoryExists(root)) return [];

        return fs.Directories(root)
            .Select(Path.GetFileName)
            .Where(v => v is not null && Version.TryParse(v, out _))
            .OrderBy(v => Version.Parse(v!))
            .Select(v => new HostInstance(HostId, v!, $"GIMP {v}", Path.Combine(root, v!), Exe(v!)))
            .ToList();
    }

    string? Exe(string version)
    {
        var major = version.Split('.')[0];
        var candidate = Path.Combine(folders.ProgramFiles, $"GIMP {major}", "bin", $"gimp-{version}.exe");
        return fs.FileExists(candidate) ? candidate : null;
    }
}

public sealed class MayaDetector(IRegistry registry, IFileSystem fs, IKnownFolders folders) : IHostDetector
{
    public const int OldestSupportedYear = 2023;

    public string HostId => "maya";

    public IReadOnlyList<HostInstance> Detect()
    {
        var root = UserDirectory();

        return registry.SubKeys(Hive.LocalMachine, @"SOFTWARE\Autodesk\Maya")
            .Where(k => int.TryParse(k, out var year) && year >= OldestSupportedYear)
            .OrderBy(int.Parse)
            .Select(year => new HostInstance(HostId, year, $"Maya {year}", Path.Combine(root, year), Exe(year)))
            .ToList();
    }

    string? Exe(string year)
    {
        var install = registry.GetValue(
            Hive.LocalMachine, $@"SOFTWARE\Autodesk\Maya\{year}\Setup\InstallPath", "MAYA_INSTALL_LOCATION");
        if (install is null) return null;

        var candidate = Path.Combine(install.TrimEnd('\\'), "bin", "maya.exe");
        return fs.FileExists(candidate) ? candidate : null;
    }

    public string UserDirectory()
    {
        var candidates = new List<string>();

        var configured = folders.Variable("MAYA_APP_DIR");
        if (!string.IsNullOrWhiteSpace(configured)) candidates.Add(configured);

        candidates.Add(Path.Combine(folders.Documents, "maya"));
        candidates.Add(Path.Combine(folders.UserProfile, "Documents", "maya"));
        candidates.Add(Path.Combine(folders.UserProfile, "OneDrive", "Documents", "maya"));

        var ordered = candidates.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        return ordered.FirstOrDefault(fs.DirectoryExists) ?? ordered[0];
    }
}

public sealed class PhotoshopDetector(IRegistry registry, IFileSystem fs) : IHostDetector
{
    static readonly string[] Roots =
    [
        @"SOFTWARE\Adobe\Photoshop",
        @"SOFTWARE\WOW6432Node\Adobe\Photoshop"
    ];

    public string HostId => "photoshop";

    public IReadOnlyList<HostInstance> Detect()
    {
        var found = new List<HostInstance>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var root in Roots)
        foreach (var version in registry.SubKeys(Hive.LocalMachine, root).OrderBy(v => v, StringComparer.OrdinalIgnoreCase))
        {
            var path = registry.GetValue(Hive.LocalMachine, $@"{root}\{version}", "ApplicationPath")?.TrimEnd('\\');
            if (path is null || !fs.DirectoryExists(Path.Combine(path, "Plug-ins")) || !seen.Add(path)) continue;

            var exe = Path.Combine(path, "Photoshop.exe");
            found.Add(new HostInstance(HostId, version, Label(path), path, fs.FileExists(exe) ? exe : null));
        }

        return found;
    }

    static string Label(string path)
    {
        var folder = Path.GetFileName(path);
        return string.IsNullOrEmpty(folder) ? "Photoshop" : folder;
    }
}

public sealed class PaintNetDetector(IRegistry registry, IFileSystem fs, IKnownFolders folders) : IHostDetector
{
    public string HostId => "paintnet";

    public IReadOnlyList<HostInstance> Detect()
    {
        var found = new List<HostInstance>();

        var target = registry.GetValue(Hive.LocalMachine, @"SOFTWARE\paint.net", "TARGETDIR")?.TrimEnd('\\');
        if (target is not null && fs.DirectoryExists(Path.Combine(target, "FileTypes")))
        {
            var exe = Path.Combine(target, "paintdotnet.exe");
            found.Add(new HostInstance(HostId, "classic", "Paint.NET", target, fs.FileExists(exe) ? exe : null));
        }

        var store = Path.Combine(folders.Documents, "paint.net App Files");
        if (fs.DirectoryExists(store))
            found.Add(new HostInstance(HostId, "store", "Paint.NET (Store)", store));

        return found;
    }
}

public sealed class ThumbnailHostDetector(IKnownFolders folders) : IHostDetector
{
    public string HostId => "thumbnails";

    public IReadOnlyList<HostInstance> Detect()
    {
        var windir = folders.Variable("WINDIR");

        return
        [
            new(HostId, "default", "Windows Explorer",
                Path.Combine(folders.LocalAppData, "RitoShark", "TexThumbnailProvider"),
                windir is null ? null : Path.Combine(windir, "explorer.exe"))
        ];
    }
}

public sealed class HematiteHostDetector(IKnownFolders folders) : IHostDetector
{
    public string HostId => "hematite";

    public IReadOnlyList<HostInstance> Detect() =>
    [
        new(HostId, "default", "Command-line tool",
            Path.Combine(folders.LocalAppData, "RitoShark", "Hematite"))
    ];
}
