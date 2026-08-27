using System.IO.Compression;
using Onyx.Core.Releases;

namespace Onyx.Core.Install;

public sealed class TempPayload(string root) : IPayload, IDisposable
{
    public string Root { get; } = root;

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}

public sealed class PayloadFetcher(HttpClient http)
{
    public async Task<TempPayload> FetchAsync(InstallPlan plan, CancellationToken ct)
    {
        var payload = new TempPayload(
            Path.Combine(Path.GetTempPath(), "onyx-" + Guid.NewGuid().ToString("N")));

        Directory.CreateDirectory(payload.Root);

        try
        {
            await AddAsync(payload.Root, plan.Asset, ct);

            foreach (var sidecar in plan.Sidecars)
                await AddAsync(payload.Root, sidecar, ct);

            return payload;
        }
        catch
        {
            payload.Dispose();
            throw;
        }
    }

    async Task AddAsync(string root, ReleaseAsset asset, CancellationToken ct)
    {
        var bytes = await http.GetByteArrayAsync(asset.DownloadUrl, ct);

        if (!asset.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            await File.WriteAllBytesAsync(Path.Combine(root, asset.Name), bytes, ct);
            return;
        }

        using var archive = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
        var strip = StripPrefix(archive, asset.Name);

        foreach (var entry in archive.Entries)
        {
            if (entry.FullName.EndsWith('/')) continue;

            var relative = strip is null ? entry.FullName : entry.FullName[strip.Length..];
            if (relative.Length == 0) continue;

            var target = Path.GetFullPath(
                Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));

            if (!target.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new PlanException($"Archive entry '{entry.FullName}' escapes the payload directory.");

            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            entry.ExtractToFile(target, overwrite: true);
        }
    }

    static string? StripPrefix(ZipArchive archive, string assetName)
    {
        var stem = Path.GetFileNameWithoutExtension(assetName);

        var tops = archive.Entries
            .Select(e => e.FullName.Split('/')[0])
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return tops.Count == 1 && string.Equals(tops[0], stem, StringComparison.OrdinalIgnoreCase)
            ? tops[0] + "/"
            : null;
    }
}
