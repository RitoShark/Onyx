using System.IO.Compression;
using System.Net;
using Onyx.Core.Install;
using Onyx.Core.Releases;

namespace Onyx.Tests.Install;

public class PayloadFetcherTests
{
    static byte[] Zip(params string[] paths)
    {
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
            foreach (var path in paths)
            {
                using var writer = new StreamWriter(archive.CreateEntry(path).Open());
                writer.Write("x");
            }

        return buffer.ToArray();
    }

    static InstallPlan Plan(string assetName, params ReleaseAsset[] sidecars) =>
        new("p", "i", "v1", new ReleaseAsset(assetName, $"https://x/{assetName}", 1), sidecars, []);

    static PayloadFetcher Serving(Dictionary<string, byte[]> bodies) =>
        new(new HttpClient(new StubHandler(request =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(bodies[request.RequestUri!.Segments[^1]])
            })));

    [Fact]
    public async Task A_loose_file_asset_lands_in_the_payload_root()
    {
        var fetcher = Serving(new() { ["RitoTex.8bi"] = [1, 2, 3] });

        using var payload = await fetcher.FetchAsync(Plan("RitoTex.8bi"), default);

        Assert.True(File.Exists(Path.Combine(payload.Root, "RitoTex.8bi")));
    }

    [Fact]
    public async Task A_zip_is_extracted()
    {
        var fetcher = Serving(new() { ["Aventurine-3.1.5.zip"] = Zip("Aventurine/__init__.py") });

        using var payload = await fetcher.FetchAsync(Plan("Aventurine-3.1.5.zip"), default);

        Assert.True(File.Exists(Path.Combine(payload.Root, "Aventurine", "__init__.py")));
    }

    [Fact]
    public async Task A_wrapper_directory_named_after_the_asset_is_stripped()
    {
        var fetcher = Serving(new()
        {
            ["RitoShark-Maya-v0.2.0.zip"] = Zip(
                "RitoShark-Maya-v0.2.0/plug-ins/ritoshark_plugin.py",
                "RitoShark-Maya-v0.2.0/vendor/ritoshark/ritoshark.pyd")
        });

        using var payload = await fetcher.FetchAsync(Plan("RitoShark-Maya-v0.2.0.zip"), default);

        Assert.True(File.Exists(Path.Combine(payload.Root, "plug-ins", "ritoshark_plugin.py")));
        Assert.True(File.Exists(Path.Combine(payload.Root, "vendor", "ritoshark", "ritoshark.pyd")));
    }

    [Fact]
    public async Task A_directory_not_named_after_the_asset_is_kept()
    {
        var fetcher = Serving(new() { ["Aventurine-3.1.5.zip"] = Zip("Aventurine/io/import_skn.py") });

        using var payload = await fetcher.FetchAsync(Plan("Aventurine-3.1.5.zip"), default);

        Assert.True(Directory.Exists(Path.Combine(payload.Root, "Aventurine")));
    }

    [Fact]
    public async Task A_flat_zip_is_left_flat()
    {
        var fetcher = Serving(new()
        {
            ["TexFileType-3.0.0.zip"] = Zip("TexFileType.dll", "Bc7Native.dll")
        });

        using var payload = await fetcher.FetchAsync(Plan("TexFileType-3.0.0.zip"), default);

        Assert.True(File.Exists(Path.Combine(payload.Root, "TexFileType.dll")));
        Assert.True(File.Exists(Path.Combine(payload.Root, "Bc7Native.dll")));
    }

    [Fact]
    public async Task Sidecar_assets_are_downloaded_alongside_the_primary_asset()
    {
        var fetcher = Serving(new()
        {
            ["TexThumbnailProvider.dll"] = [1],
            ["TexThumbnailProvider.dll.sha256"] = "abc"u8.ToArray()
        });

        var plan = Plan("TexThumbnailProvider.dll",
            new ReleaseAsset("TexThumbnailProvider.dll.sha256", "https://x/TexThumbnailProvider.dll.sha256", 1));

        using var payload = await fetcher.FetchAsync(plan, default);

        Assert.True(File.Exists(Path.Combine(payload.Root, "TexThumbnailProvider.dll")));
        Assert.True(File.Exists(Path.Combine(payload.Root, "TexThumbnailProvider.dll.sha256")));
    }

    [Fact]
    public async Task A_zip_entry_that_escapes_the_root_is_rejected()
    {
        var fetcher = Serving(new() { ["bad.zip"] = Zip("../evil.txt") });

        await Assert.ThrowsAsync<PlanException>(() => fetcher.FetchAsync(Plan("bad.zip"), default));
    }

    [Fact]
    public async Task Disposing_the_payload_removes_the_temp_directory()
    {
        var fetcher = Serving(new() { ["a.bin"] = [1] });

        string root;
        using (var payload = await fetcher.FetchAsync(Plan("a.bin"), default))
            root = payload.Root;

        Assert.False(Directory.Exists(root));
    }
}
