using System.IO;
using System.Net.Http;
using Onyx.Core;
using Onyx.Core.Catalog;
using Onyx.Core.Hosts;
using Onyx.Core.Install;
using Onyx.Core.Platform;
using Onyx.Core.Processes;
using Onyx.Core.Releases;
using Onyx.Core.State;

namespace Onyx;

public sealed class Services
{
    Services(PluginManager manager, ProcessGuard guard, string cachePath, HttpClient http)
    {
        Manager = manager;
        Guard = guard;
        CachePath = cachePath;
        Http = http;
    }

    public PluginManager Manager { get; }
    public ProcessGuard Guard { get; }
    public string CachePath { get; }
    public HttpClient Http { get; }

    public static Services Build()
    {
        var folders = new KnownFolders();
        var fs = new PhysicalFileSystem();
        var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };

        var cachePath = Path.Combine(folders.LocalAppData, "RitoShark", "Onyx", "catalog.json");
        var catalog = CatalogSource.Best(CatalogSource.Embedded(), ReadCache(cachePath));

        var guard = new ProcessGuard(new WindowsProcessTable());

        var manager = new PluginManager(
            catalog,
            HostRegistry.Standard(new WindowsRegistry(), fs, folders),
            new GitHubClient(http),
            new PayloadFetcher(http),
            new InstallEngine(fs, new Regsvr32Registrar(), new FileChecksum()),
            guard,
            StateStore.Load(StateStore.DefaultPath(folders.LocalAppData)));

        return new Services(manager, guard, cachePath, http);
    }

    public async Task RefreshCatalogAsync(CancellationToken ct)
    {
        var json = await Http.GetStringAsync(CatalogSource.RemoteUrl, ct);
        CatalogReader.Read(json);

        Directory.CreateDirectory(Path.GetDirectoryName(CachePath)!);
        await File.WriteAllTextAsync(CachePath, json, ct);
    }

    static string? ReadCache(string path)
    {
        try
        {
            return File.Exists(path) ? File.ReadAllText(path) : null;
        }
        catch (IOException)
        {
            return null;
        }
    }
}
