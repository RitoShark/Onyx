# Onyx Plugin Manager Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A .NET 9 WPF app that installs, updates, downgrades and removes the seven RitoShark plugins from their GitHub Releases.

**Architecture:** All logic lives in `Onyx.Core` behind interfaces (`IRegistry`, `IFileSystem`, `IProcessTable`, `HttpMessageHandler`) so every decision is unit-testable without a real machine. A plugin is a `catalog.json` entry, never C# code; the install engine understands exactly four verbs. The WPF project is views, view models and theme, and holds no decisions.

**Tech Stack:** .NET 9, WPF (`net9.0-windows`), xunit, `System.Text.Json`, `System.IO.Compression`. No third-party packages outside the test project.

**Spec:** `docs/superpowers/specs/2026-08-27-onyx-plugin-manager-design.md`

## Global Constraints

- Target frameworks: `Onyx.Core` and `Onyx.Tests` are `net9.0`; `Onyx` is `net9.0-windows` with `<UseWPF>true</UseWPF>`.
- `Onyx.Core` must never reference WPF, `System.Windows.*`, or any UI type. A decision that lives in the WPF project is in the wrong project.
- `RuntimeIdentifier` is `win-x64`. Publish is self-contained, single-file.
- Zero code comments (ecosystem golden rule 6). Knowledge goes in `CLAUDE.md`.
- No AI attribution anywhere — commits, code, docs.
- Conventional Commits. Commit after every task.
- Supported catalog schema is `1`. A catalog declaring a higher schema is rejected whole, never partially applied.
- The four install verbs are exactly `copy`, `copyDir`, `regsvr32`, `sha256`. Adding a fifth is a schema bump and a deliberate decision.
- Never force-kill a host process. Graceful close and poll, or refuse the action.
- The dev machine is Windows 10 19045: no Mica, no Acrylic. The theme must look correct on solid surfaces.
- `<Nullable>enable</Nullable>` and `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>` in every project.

**Verify the whole solution at any point with:** `dotnet test` from the repo root.

---

### Task 1: Solution scaffold and catalog parsing

**Files:**
- Create: `Onyx.sln`, `Onyx.Core/Onyx.Core.csproj`, `Onyx.Tests/Onyx.Tests.csproj`, `Directory.Build.props`
- Create: `Onyx.Core/Catalog/CatalogModel.cs`, `Onyx.Core/Catalog/CatalogReader.cs`
- Test: `Onyx.Tests/Catalog/CatalogReaderTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: `Onyx.Core.Catalog.Catalog`, `PluginEntry`, `InstallStep`, `StepVerb`, `CatalogReader.Read(string json) -> Catalog`, `CatalogException`.

- [ ] **Step 1: Scaffold the solution**

```bash
cd "e:/RitoShark/Tools/Onyx"
dotnet new sln -n Onyx
dotnet new classlib -n Onyx.Core -f net9.0
dotnet new xunit -n Onyx.Tests -f net9.0
dotnet sln add Onyx.Core/Onyx.Core.csproj Onyx.Tests/Onyx.Tests.csproj
dotnet add Onyx.Tests/Onyx.Tests.csproj reference Onyx.Core/Onyx.Core.csproj
rm Onyx.Core/Class1.cs
```

- [ ] **Step 2: Add `Directory.Build.props`**

```xml
<Project>
  <PropertyGroup>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <LangVersion>latest</LangVersion>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <InvariantGlobalization>true</InvariantGlobalization>
  </PropertyGroup>
</Project>
```

- [ ] **Step 3: Write the failing tests**

`Onyx.Tests/Catalog/CatalogReaderTests.cs`:

```csharp
using Onyx.Core.Catalog;

namespace Onyx.Tests.Catalog;

public class CatalogReaderTests
{
    const string Minimal = """
    {
      "schema": 1,
      "revision": 3,
      "plugins": [
        {
          "id": "ritotex-photoshop",
          "name": "RitoTex for Photoshop",
          "summary": "Open and save .tex textures directly in Photoshop.",
          "repo": "RitoShark/RitoTex-Photoshop",
          "host": "photoshop",
          "asset": "RitoTex.8bi",
          "steps": [ { "verb": "copy", "from": "RitoTex.8bi", "to": "{host}/Plug-ins/RitoTex.8bi" } ]
        }
      ]
    }
    """;

    [Fact]
    public void Reads_a_minimal_catalog()
    {
        var catalog = CatalogReader.Read(Minimal);

        Assert.Equal(1, catalog.Schema);
        Assert.Equal(3, catalog.Revision);
        var plugin = Assert.Single(catalog.Plugins);
        Assert.Equal("ritotex-photoshop", plugin.Id);
        Assert.Equal("photoshop", plugin.Host);
        var step = Assert.Single(plugin.Steps);
        Assert.Equal(StepVerb.Copy, step.Verb);
        Assert.Equal("{host}/Plug-ins/RitoTex.8bi", step.To);
    }

    [Fact]
    public void Rejects_a_newer_schema_whole()
    {
        var json = Minimal.Replace("\"schema\": 1", "\"schema\": 2");
        var ex = Assert.Throws<CatalogException>(() => CatalogReader.Read(json));
        Assert.Contains("schema", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Rejects_an_unknown_verb()
    {
        var json = Minimal.Replace("\"verb\": \"copy\"", "\"verb\": \"runScript\"");
        var ex = Assert.Throws<CatalogException>(() => CatalogReader.Read(json));
        Assert.Contains("runScript", ex.Message);
    }

    [Fact]
    public void Rejects_a_duplicate_plugin_id()
    {
        var one = Minimal[..Minimal.LastIndexOf(']')];
        var json = one + "," + one[(one.IndexOf('[') + 1)..] + "]}";
        Assert.Throws<CatalogException>(() => CatalogReader.Read(json));
    }

    [Fact]
    public void Rejects_malformed_json()
    {
        Assert.Throws<CatalogException>(() => CatalogReader.Read("{ not json"));
    }
}
```

- [ ] **Step 4: Run tests to verify they fail**

Run: `dotnet test --filter CatalogReaderTests`
Expected: FAIL — `CatalogReader` does not exist.

- [ ] **Step 5: Write the model**

`Onyx.Core/Catalog/CatalogModel.cs`:

```csharp
namespace Onyx.Core.Catalog;

public enum StepVerb { Copy, CopyDir, Regsvr32, Sha256 }

public sealed record InstallStep(StepVerb Verb, string? From, string? To);

public sealed record PluginEntry(
    string Id,
    string Name,
    string Summary,
    string Repo,
    string Host,
    string Asset,
    IReadOnlyList<InstallStep> Steps);

public sealed record Catalog(int Schema, int Revision, IReadOnlyList<PluginEntry> Plugins);

public sealed class CatalogException(string message) : Exception(message);
```

- [ ] **Step 6: Write the reader**

`Onyx.Core/Catalog/CatalogReader.cs`:

```csharp
using System.Text.Json;

namespace Onyx.Core.Catalog;

public static class CatalogReader
{
    public const int SupportedSchema = 1;

    public static Catalog Read(string json)
    {
        JsonDocument doc;
        try { doc = JsonDocument.Parse(json); }
        catch (JsonException e) { throw new CatalogException($"Catalog is not valid JSON: {e.Message}"); }

        using (doc)
        {
            var root = doc.RootElement;
            var schema = Int(root, "schema");
            if (schema > SupportedSchema)
                throw new CatalogException($"Catalog schema {schema} is newer than this build supports ({SupportedSchema}).");

            var revision = Int(root, "revision");
            var plugins = new List<PluginEntry>();
            var seen = new HashSet<string>(StringComparer.Ordinal);

            foreach (var p in Array(root, "plugins"))
            {
                var id = Str(p, "id");
                if (!seen.Add(id)) throw new CatalogException($"Duplicate plugin id '{id}'.");

                var steps = Array(p, "steps").Select(ReadStep).ToList();
                if (steps.Count == 0) throw new CatalogException($"Plugin '{id}' has no install steps.");

                plugins.Add(new PluginEntry(
                    id, Str(p, "name"), Str(p, "summary"), Str(p, "repo"),
                    Str(p, "host"), Str(p, "asset"), steps));
            }

            if (plugins.Count == 0) throw new CatalogException("Catalog contains no plugins.");
            return new Catalog(schema, revision, plugins);
        }
    }

    static InstallStep ReadStep(JsonElement e)
    {
        var verb = Str(e, "verb");
        var parsed = verb switch
        {
            "copy" => StepVerb.Copy,
            "copyDir" => StepVerb.CopyDir,
            "regsvr32" => StepVerb.Regsvr32,
            "sha256" => StepVerb.Sha256,
            _ => throw new CatalogException($"Unknown install verb '{verb}'.")
        };
        return new InstallStep(parsed, Opt(e, "from"), Opt(e, "to"));
    }

    static JsonElement.ArrayEnumerator Array(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Array
            ? v.EnumerateArray()
            : throw new CatalogException($"Missing array '{name}'.");

    static string Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString()!
            : throw new CatalogException($"Missing string '{name}'.");

    static string? Opt(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    static int Int(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.TryGetInt32(out var i)
            ? i
            : throw new CatalogException($"Missing integer '{name}'.");
}
```

- [ ] **Step 7: Run tests to verify they pass**

Run: `dotnet test --filter CatalogReaderTests`
Expected: PASS, 5 tests.

- [ ] **Step 8: Commit**

```bash
git add -A
git commit -m "feat: add catalog model and reader"
```

---

### Task 2: Releases, asset globs and update availability

**Files:**
- Create: `Onyx.Core/Releases/ReleaseModel.cs`, `Onyx.Core/Releases/AssetGlob.cs`, `Onyx.Core/Releases/ReleaseSet.cs`
- Test: `Onyx.Tests/Releases/AssetGlobTests.cs`, `Onyx.Tests/Releases/ReleaseSetTests.cs`

**Interfaces:**
- Consumes: nothing from Task 1.
- Produces: `Release`, `ReleaseAsset`, `AssetGlob.Match(IReadOnlyList<ReleaseAsset>, string pattern) -> ReleaseAsset?`, `ReleaseSet.Latest(IReadOnlyList<Release>, bool includePrerelease) -> Release?`, `ReleaseSet.UpdateAvailable(string? installedTag, Release? latest) -> bool`.

- [ ] **Step 1: Write the failing tests**

`Onyx.Tests/Releases/AssetGlobTests.cs`:

```csharp
using Onyx.Core.Releases;

namespace Onyx.Tests.Releases;

public class AssetGlobTests
{
    static ReleaseAsset A(string name) => new(name, $"https://example/{name}", 1);

    [Fact]
    public void Matches_an_exact_name()
    {
        var assets = new[] { A("RitoTex.8bi"), A("other.txt") };
        Assert.Equal("RitoTex.8bi", AssetGlob.Match(assets, "RitoTex.8bi")!.Name);
    }

    [Fact]
    public void Matches_a_version_stamped_name()
    {
        var assets = new[] { A("TexFileTypeSetup-3.0.0.exe"), A("TexFileType-3.0.0.zip") };
        Assert.Equal("TexFileType-3.0.0.zip", AssetGlob.Match(assets, "TexFileType-*.zip")!.Name);
    }

    [Fact]
    public void Does_not_match_a_sibling_platform_asset()
    {
        var assets = new[] { A("GIMP2_TEX_Plugin_Linux.tar.gz"), A("GIMP2_TEX_Plugin_Windows.zip") };
        Assert.Equal("GIMP2_TEX_Plugin_Windows.zip", AssetGlob.Match(assets, "GIMP2_TEX_Plugin_Windows.zip")!.Name);
    }

    [Fact]
    public void Returns_null_when_nothing_matches()
    {
        Assert.Null(AssetGlob.Match(new[] { A("a.zip") }, "b-*.zip"));
    }
}
```

`Onyx.Tests/Releases/ReleaseSetTests.cs`:

```csharp
using Onyx.Core.Releases;

namespace Onyx.Tests.Releases;

public class ReleaseSetTests
{
    static Release R(string tag, string date, bool pre = false) =>
        new(tag, DateTimeOffset.Parse(date), pre, "", []);

    [Fact]
    public void Latest_is_the_most_recently_published_stable_release()
    {
        var releases = new[] { R("v1.0.0", "2025-11-13"), R("v1.1.0", "2026-05-29") };
        Assert.Equal("v1.1.0", ReleaseSet.Latest(releases, includePrerelease: false)!.Tag);
    }

    [Fact]
    public void Latest_ignores_prereleases_unless_asked()
    {
        var releases = new[] { R("v1.1.0", "2026-05-29"), R("v2.0.0-rc1", "2026-06-01", pre: true) };
        Assert.Equal("v1.1.0", ReleaseSet.Latest(releases, false)!.Tag);
        Assert.Equal("v2.0.0-rc1", ReleaseSet.Latest(releases, true)!.Tag);
    }

    [Fact]
    public void Latest_is_null_for_an_empty_set()
    {
        Assert.Null(ReleaseSet.Latest([], false));
    }

    [Fact]
    public void Update_is_available_when_the_installed_tag_is_not_the_latest()
    {
        var latest = R("v1.1.0", "2026-05-29");
        Assert.True(ReleaseSet.UpdateAvailable("v1.0.0", latest));
        Assert.False(ReleaseSet.UpdateAvailable("v1.1.0", latest));
    }

    [Fact]
    public void Update_is_not_available_when_nothing_is_installed()
    {
        Assert.False(ReleaseSet.UpdateAvailable(null, R("v1.1.0", "2026-05-29")));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter "AssetGlobTests|ReleaseSetTests"`
Expected: FAIL — types do not exist.

- [ ] **Step 3: Write the implementation**

`Onyx.Core/Releases/ReleaseModel.cs`:

```csharp
namespace Onyx.Core.Releases;

public sealed record ReleaseAsset(string Name, string DownloadUrl, long Size);

public sealed record Release(
    string Tag,
    DateTimeOffset PublishedAt,
    bool PreRelease,
    string Body,
    IReadOnlyList<ReleaseAsset> Assets);
```

`Onyx.Core/Releases/AssetGlob.cs`:

```csharp
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
```

`Onyx.Core/Releases/ReleaseSet.cs`:

```csharp
namespace Onyx.Core.Releases;

public static class ReleaseSet
{
    public static Release? Latest(IReadOnlyList<Release> releases, bool includePrerelease) =>
        releases
            .Where(r => includePrerelease || !r.PreRelease)
            .OrderByDescending(r => r.PublishedAt)
            .FirstOrDefault();

    public static bool UpdateAvailable(string? installedTag, Release? latest) =>
        installedTag is not null && latest is not null &&
        !string.Equals(installedTag, latest.Tag, StringComparison.Ordinal);
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test --filter "AssetGlobTests|ReleaseSetTests"`
Expected: PASS, 9 tests.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat: add release model, asset globs and update availability"
```

---

### Task 3: GitHub releases client

**Files:**
- Create: `Onyx.Core/Releases/GitHubClient.cs`, `Onyx.Core/Releases/RateLimitedException.cs`
- Create: `Onyx.Tests/Fixtures/releases-texthumbnailprovider.json` (recorded from `gh api repos/RitoShark/TexThumbnailProvider/releases`)
- Test: `Onyx.Tests/Releases/GitHubClientTests.cs`, `Onyx.Tests/StubHandler.cs`

**Interfaces:**
- Consumes: `Release`, `ReleaseAsset` (Task 2).
- Produces: `GitHubClient(HttpClient)`, `Task<IReadOnlyList<Release>> ListReleasesAsync(string repo, CancellationToken)`, `RateLimitedException`.

- [ ] **Step 1: Record the fixture**

```bash
gh api "repos/RitoShark/TexThumbnailProvider/releases" > Onyx.Tests/Fixtures/releases-texthumbnailprovider.json
```

Mark it as copied to output in `Onyx.Tests.csproj`:

```xml
<ItemGroup>
  <None Update="Fixtures\**\*.json" CopyToOutputDirectory="PreserveNewest" />
</ItemGroup>
```

- [ ] **Step 2: Write the stub handler and failing tests**

`Onyx.Tests/StubHandler.cs`:

```csharp
using System.Net;

namespace Onyx.Tests;

public sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
{
    public List<HttpRequestMessage> Requests { get; } = [];

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        Requests.Add(request);
        return Task.FromResult(respond(request));
    }

    public static StubHandler Json(string body, HttpStatusCode status = HttpStatusCode.OK) =>
        new(_ => new HttpResponseMessage(status) { Content = new StringContent(body) });
}
```

`Onyx.Tests/Releases/GitHubClientTests.cs`:

```csharp
using System.Net;
using Onyx.Core.Releases;

namespace Onyx.Tests.Releases;

public class GitHubClientTests
{
    static string Fixture(string name) => File.ReadAllText(Path.Combine("Fixtures", name));

    [Fact]
    public async Task Parses_recorded_releases()
    {
        var handler = StubHandler.Json(Fixture("releases-texthumbnailprovider.json"));
        var client = new GitHubClient(new HttpClient(handler));

        var releases = await client.ListReleasesAsync("RitoShark/TexThumbnailProvider", default);

        Assert.Contains(releases, r => r.Tag == "v1.1.0");
        var latest = releases.Single(r => r.Tag == "v1.1.0");
        Assert.Contains(latest.Assets, a => a.Name == "TexThumbnailProvider.dll");
        Assert.All(latest.Assets, a => Assert.StartsWith("https://", a.DownloadUrl));
    }

    [Fact]
    public async Task Sends_a_user_agent_and_the_api_accept_header()
    {
        var handler = StubHandler.Json("[]");
        var client = new GitHubClient(new HttpClient(handler));

        await client.ListReleasesAsync("RitoShark/Flint", default);

        var request = Assert.Single(handler.Requests);
        Assert.Equal("Onyx", request.Headers.UserAgent.Single().Product!.Name);
        Assert.Contains("application/vnd.github+json", request.Headers.Accept.Select(a => a.MediaType));
    }

    [Fact]
    public async Task Throws_RateLimited_when_the_quota_is_exhausted()
    {
        var handler = new StubHandler(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.Forbidden) { Content = new StringContent("{}") };
            response.Headers.Add("X-RateLimit-Remaining", "0");
            return response;
        });
        var client = new GitHubClient(new HttpClient(handler));

        await Assert.ThrowsAsync<RateLimitedException>(
            () => client.ListReleasesAsync("RitoShark/Flint", default));
    }
}
```

- [ ] **Step 3: Run tests to verify they fail**

Run: `dotnet test --filter GitHubClientTests`
Expected: FAIL — `GitHubClient` does not exist.

- [ ] **Step 4: Write the client**

`Onyx.Core/Releases/RateLimitedException.cs`:

```csharp
namespace Onyx.Core.Releases;

public sealed class RateLimitedException() : Exception("GitHub rate limit reached. Try again later.");
```

`Onyx.Core/Releases/GitHubClient.cs`:

```csharp
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace Onyx.Core.Releases;

public sealed class GitHubClient(HttpClient http)
{
    public async Task<IReadOnlyList<Release>> ListReleasesAsync(string repo, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{repo}/releases?per_page=100");
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("Onyx", "1.0"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

        using var response = await http.SendAsync(request, ct);

        if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests &&
            response.Headers.TryGetValues("X-RateLimit-Remaining", out var remaining) &&
            remaining.FirstOrDefault() == "0")
            throw new RateLimitedException();

        response.EnsureSuccessStatusCode();

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        return doc.RootElement.EnumerateArray().Select(ReadRelease).ToList();
    }

    static Release ReadRelease(JsonElement e) => new(
        e.GetProperty("tag_name").GetString()!,
        e.GetProperty("published_at").GetDateTimeOffset(),
        e.GetProperty("prerelease").GetBoolean(),
        e.TryGetProperty("body", out var b) && b.ValueKind == JsonValueKind.String ? b.GetString()! : "",
        e.GetProperty("assets").EnumerateArray().Select(a => new ReleaseAsset(
            a.GetProperty("name").GetString()!,
            a.GetProperty("browser_download_url").GetString()!,
            a.GetProperty("size").GetInt64())).ToList());
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test --filter GitHubClientTests`
Expected: PASS, 3 tests.

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "feat: add github releases client"
```

---

### Task 4: Host detection

**Files:**
- Create: `Onyx.Core/Hosts/Abstractions.cs`, `Onyx.Core/Hosts/HostInstance.cs`, `Onyx.Core/Hosts/Detectors.cs`, `Onyx.Core/Hosts/HostRegistry.cs`
- Create: `Onyx.Core/Platform/WindowsRegistry.cs`, `Onyx.Core/Platform/PhysicalFileSystem.cs`
- Test: `Onyx.Tests/Hosts/DetectorTests.cs`, `Onyx.Tests/FakeEnvironment.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: `IRegistry`, `IFileSystem`, `HostInstance(string HostId, string InstanceId, string Label, string Path)`, `IHostDetector.Detect() -> IReadOnlyList<HostInstance>`, `HostRegistry.DetectAll() -> IReadOnlyList<HostInstance>`.

- [ ] **Step 1: Write the abstractions**

`Onyx.Core/Hosts/Abstractions.cs`:

```csharp
namespace Onyx.Core.Hosts;

public enum Hive { LocalMachine, CurrentUser }

public interface IRegistry
{
    string? GetValue(Hive hive, string key, string name);
    IReadOnlyList<string> SubKeys(Hive hive, string key);
}

public interface IFileSystem
{
    bool DirectoryExists(string path);
    bool FileExists(string path);
    IReadOnlyList<string> Directories(string path);
    IReadOnlyList<string> Files(string path);
    void CreateDirectory(string path);
    void Copy(string from, string to, bool overwrite);
    void Move(string from, string to, bool overwrite);
    void DeleteFile(string path);
    void DeleteDirectory(string path);
}

public interface IKnownFolders
{
    string AppData { get; }
    string LocalAppData { get; }
    string Documents { get; }
}
```

`Onyx.Core/Hosts/HostInstance.cs`:

```csharp
namespace Onyx.Core.Hosts;

public sealed record HostInstance(string HostId, string InstanceId, string Label, string Path);

public interface IHostDetector
{
    string HostId { get; }
    IReadOnlyList<HostInstance> Detect();
}
```

- [ ] **Step 2: Write the fake environment and failing tests**

`Onyx.Tests/FakeEnvironment.cs`:

```csharp
using Onyx.Core.Hosts;

namespace Onyx.Tests;

public sealed class FakeRegistry : IRegistry
{
    public Dictionary<string, string> Values { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, List<string>> Keys { get; } = new(StringComparer.OrdinalIgnoreCase);

    public string? GetValue(Hive hive, string key, string name) =>
        Values.GetValueOrDefault($"{hive}|{key}|{name}");

    public IReadOnlyList<string> SubKeys(Hive hive, string key) =>
        Keys.GetValueOrDefault($"{hive}|{key}") ?? [];

    public FakeRegistry WithValue(Hive hive, string key, string name, string value)
    { Values[$"{hive}|{key}|{name}"] = value; return this; }

    public FakeRegistry WithKeys(Hive hive, string key, params string[] subKeys)
    { Keys[$"{hive}|{key}"] = [.. subKeys]; return this; }
}

public sealed class FakeFileSystem : IFileSystem
{
    readonly HashSet<string> _dirs = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, byte[]> _files = new(StringComparer.OrdinalIgnoreCase);

    public FakeFileSystem WithDirectory(string path)
    {
        for (var p = path; !string.IsNullOrEmpty(p); p = Path.GetDirectoryName(p) ?? "")
            _dirs.Add(p.TrimEnd('\\'));
        return this;
    }

    public FakeFileSystem WithFile(string path, byte[]? content = null)
    {
        WithDirectory(Path.GetDirectoryName(path)!);
        _files[path] = content ?? [];
        return this;
    }

    public bool DirectoryExists(string path) => _dirs.Contains(path.TrimEnd('\\'));
    public bool FileExists(string path) => _files.ContainsKey(path);

    public IReadOnlyList<string> Directories(string path) =>
        _dirs.Where(d => string.Equals(Path.GetDirectoryName(d), path.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
             .OrderBy(d => d, StringComparer.OrdinalIgnoreCase).ToList();

    public IReadOnlyList<string> Files(string path) =>
        _files.Keys.Where(f => string.Equals(Path.GetDirectoryName(f), path.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
              .OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList();

    public void CreateDirectory(string path) => WithDirectory(path);
    public void Copy(string from, string to, bool overwrite) { WithFile(to, _files[from]); }
    public void Move(string from, string to, bool overwrite) { Copy(from, to, overwrite); _files.Remove(from); }
    public void DeleteFile(string path) => _files.Remove(path);
    public void DeleteDirectory(string path)
    {
        foreach (var f in _files.Keys.Where(f => f.StartsWith(path, StringComparison.OrdinalIgnoreCase)).ToList())
            _files.Remove(f);
        foreach (var d in _dirs.Where(d => d.StartsWith(path, StringComparison.OrdinalIgnoreCase)).ToList())
            _dirs.Remove(d);
    }
}

public sealed record FakeFolders(string AppData, string LocalAppData, string Documents) : IKnownFolders
{
    public static FakeFolders Default => new(@"C:\Users\t\AppData\Roaming", @"C:\Users\t\AppData\Local", @"C:\Users\t\Documents");
}
```

`Onyx.Tests/Hosts/DetectorTests.cs`:

```csharp
using Onyx.Core.Hosts;

namespace Onyx.Tests.Hosts;

public class DetectorTests
{
    static readonly FakeFolders Folders = FakeFolders.Default;

    [Fact]
    public void Blender_finds_every_version_directory()
    {
        var fs = new FakeFileSystem()
            .WithDirectory($@"{Folders.AppData}\Blender Foundation\Blender\4.0")
            .WithDirectory($@"{Folders.AppData}\Blender Foundation\Blender\4.1")
            .WithDirectory($@"{Folders.AppData}\Blender Foundation\Blender\4.3");

        var found = new BlenderDetector(fs, Folders).Detect();

        Assert.Equal(["4.0", "4.1", "4.3"], found.Select(h => h.InstanceId));
        Assert.All(found, h => Assert.Equal("blender", h.HostId));
        Assert.Contains("4.3", found[2].Label);
    }

    [Fact]
    public void Blender_ignores_a_directory_that_is_not_a_version()
    {
        var fs = new FakeFileSystem()
            .WithDirectory($@"{Folders.AppData}\Blender Foundation\Blender\4.1")
            .WithDirectory($@"{Folders.AppData}\Blender Foundation\Blender\backup");

        Assert.Equal(["4.1"], new BlenderDetector(fs, Folders).Detect().Select(h => h.InstanceId));
    }

    [Fact]
    public void Blender_finds_nothing_when_blender_is_absent()
    {
        Assert.Empty(new BlenderDetector(new FakeFileSystem(), Folders).Detect());
    }

    [Fact]
    public void Maya_uses_the_registry_years_and_targets_the_documents_folder()
    {
        var reg = new FakeRegistry()
            .WithKeys(Hive.LocalMachine, @"SOFTWARE\Autodesk\Maya", "2024", "2026", "Capabilities");

        var found = new MayaDetector(reg, Folders).Detect();

        Assert.Equal(["2024", "2026"], found.Select(h => h.InstanceId));
        Assert.Equal($@"{Folders.Documents}\maya\2026", found[1].Path);
    }

    [Fact]
    public void Photoshop_reads_the_application_path_for_each_version()
    {
        var reg = new FakeRegistry()
            .WithKeys(Hive.LocalMachine, @"SOFTWARE\Adobe\Photoshop", "180.0", "260.0")
            .WithValue(Hive.LocalMachine, @"SOFTWARE\Adobe\Photoshop\180.0", "ApplicationPath", @"D:\Adobe\Photoshop 2024\")
            .WithValue(Hive.LocalMachine, @"SOFTWARE\Adobe\Photoshop\260.0", "ApplicationPath", @"D:\Adobe\Photoshop 2026\");
        var fs = new FakeFileSystem()
            .WithDirectory(@"D:\Adobe\Photoshop 2024\Plug-ins")
            .WithDirectory(@"D:\Adobe\Photoshop 2026\Plug-ins");

        var found = new PhotoshopDetector(reg, fs).Detect();

        Assert.Equal(2, found.Count);
        Assert.Equal(@"D:\Adobe\Photoshop 2026", found[1].Path);
    }

    [Fact]
    public void Photoshop_skips_a_version_whose_folder_is_gone()
    {
        var reg = new FakeRegistry()
            .WithKeys(Hive.LocalMachine, @"SOFTWARE\Adobe\Photoshop", "180.0")
            .WithValue(Hive.LocalMachine, @"SOFTWARE\Adobe\Photoshop\180.0", "ApplicationPath", @"D:\Gone\");

        Assert.Empty(new PhotoshopDetector(reg, new FakeFileSystem()).Detect());
    }

    [Fact]
    public void Gimp_finds_each_installed_config_version()
    {
        var fs = new FakeFileSystem()
            .WithDirectory($@"{Folders.AppData}\GIMP\2.10")
            .WithDirectory($@"{Folders.AppData}\GIMP\3.0");

        Assert.Equal(["2.10", "3.0"], new GimpDetector(fs, Folders).Detect().Select(h => h.InstanceId));
    }

    [Fact]
    public void Thumbnails_is_always_a_single_fixed_instance()
    {
        var found = new ThumbnailHostDetector(Folders).Detect();
        var only = Assert.Single(found);
        Assert.Equal($@"{Folders.LocalAppData}\RitoShark\TexThumbnailProvider", only.Path);
    }
}
```

- [ ] **Step 3: Run tests to verify they fail**

Run: `dotnet test --filter DetectorTests`
Expected: FAIL — detector types do not exist.

- [ ] **Step 4: Write the detectors**

`Onyx.Core/Hosts/Detectors.cs`:

```csharp
namespace Onyx.Core.Hosts;

public sealed class BlenderDetector(IFileSystem fs, IKnownFolders folders) : IHostDetector
{
    public string HostId => "blender";

    public IReadOnlyList<HostInstance> Detect()
    {
        var root = Path.Combine(folders.AppData, "Blender Foundation", "Blender");
        if (!fs.DirectoryExists(root)) return [];

        return fs.Directories(root)
            .Select(d => Path.GetFileName(d))
            .Where(v => Version.TryParse(v, out _))
            .OrderBy(v => Version.Parse(v))
            .Select(v => new HostInstance(HostId, v, $"Blender {v}", Path.Combine(root, v)))
            .ToList();
    }
}

public sealed class MayaDetector(IRegistry registry, IKnownFolders folders) : IHostDetector
{
    public string HostId => "maya";

    public IReadOnlyList<HostInstance> Detect() =>
        registry.SubKeys(Hive.LocalMachine, @"SOFTWARE\Autodesk\Maya")
            .Where(k => int.TryParse(k, out var year) && year >= 2023)
            .OrderBy(int.Parse)
            .Select(year => new HostInstance(
                HostId, year, $"Maya {year}",
                Path.Combine(folders.Documents, "maya", year)))
            .ToList();
}

public sealed class PhotoshopDetector(IRegistry registry, IFileSystem fs) : IHostDetector
{
    public string HostId => "photoshop";

    static readonly string[] Roots =
    [
        @"SOFTWARE\Adobe\Photoshop",
        @"SOFTWARE\WOW6432Node\Adobe\Photoshop"
    ];

    public IReadOnlyList<HostInstance> Detect()
    {
        var found = new List<HostInstance>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var root in Roots)
        foreach (var version in registry.SubKeys(Hive.LocalMachine, root).OrderBy(v => v, StringComparer.OrdinalIgnoreCase))
        {
            var path = registry.GetValue(Hive.LocalMachine, $@"{root}\{version}", "ApplicationPath")?.TrimEnd('\\');
            if (path is null || !fs.DirectoryExists(Path.Combine(path, "Plug-ins")) || !seen.Add(path)) continue;

            found.Add(new HostInstance(HostId, version, $"Photoshop ({Path.GetFileName(path)})", path));
        }

        return found;
    }
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
            .Select(v => new HostInstance(HostId, v!, $"GIMP {v}", Path.Combine(root, v!)))
            .ToList();
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
            found.Add(new HostInstance(HostId, "classic", "Paint.NET", target));

        var store = Path.Combine(folders.Documents, "paint.net App Files");
        if (fs.DirectoryExists(store))
            found.Add(new HostInstance(HostId, "store", "Paint.NET (Store)", store));

        return found;
    }
}

public sealed class ThumbnailHostDetector(IKnownFolders folders) : IHostDetector
{
    public string HostId => "thumbnails";

    public IReadOnlyList<HostInstance> Detect() =>
    [
        new(HostId, "default", "Windows Explorer",
            Path.Combine(folders.LocalAppData, "RitoShark", "TexThumbnailProvider"))
    ];
}
```

`Onyx.Core/Hosts/HostRegistry.cs`:

```csharp
namespace Onyx.Core.Hosts;

public sealed class HostRegistry(IReadOnlyList<IHostDetector> detectors)
{
    public static HostRegistry Standard(IRegistry registry, IFileSystem fs, IKnownFolders folders) =>
        new([
            new PhotoshopDetector(registry, fs),
            new PaintNetDetector(registry, fs, folders),
            new GimpDetector(fs, folders),
            new MayaDetector(registry, folders),
            new BlenderDetector(fs, folders),
            new ThumbnailHostDetector(folders)
        ]);

    public IReadOnlyList<HostInstance> DetectAll() =>
        detectors.SelectMany(d => d.Detect()).ToList();

    public IReadOnlyList<HostInstance> For(string hostId) =>
        detectors.Where(d => d.HostId == hostId).SelectMany(d => d.Detect()).ToList();
}
```

- [ ] **Step 5: Write the real platform implementations**

`Onyx.Core/Platform/WindowsRegistry.cs` and `Onyx.Core/Platform/PhysicalFileSystem.cs` are thin adapters over `Microsoft.Win32.Registry` and `System.IO`. Add the package reference the registry adapter needs:

```bash
dotnet add Onyx.Core/Onyx.Core.csproj package Microsoft.Win32.Registry
```

```csharp
using Microsoft.Win32;
using Onyx.Core.Hosts;

namespace Onyx.Core.Platform;

public sealed class WindowsRegistry : IRegistry
{
    static RegistryKey Root(Hive hive) => hive == Hive.LocalMachine
        ? RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64)
        : RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry64);

    public string? GetValue(Hive hive, string key, string name)
    {
        using var root = Root(hive);
        using var sub = root.OpenSubKey(key);
        return sub?.GetValue(name) as string;
    }

    public IReadOnlyList<string> SubKeys(Hive hive, string key)
    {
        using var root = Root(hive);
        using var sub = root.OpenSubKey(key);
        return sub?.GetSubKeyNames() ?? [];
    }
}

public sealed class PhysicalFileSystem : IFileSystem
{
    public bool DirectoryExists(string path) => Directory.Exists(path);
    public bool FileExists(string path) => File.Exists(path);
    public IReadOnlyList<string> Directories(string path) => Directory.GetDirectories(path);
    public IReadOnlyList<string> Files(string path) => Directory.GetFiles(path);
    public void CreateDirectory(string path) => Directory.CreateDirectory(path);
    public void Copy(string from, string to, bool overwrite)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(to)!);
        File.Copy(from, to, overwrite);
    }
    public void Move(string from, string to, bool overwrite)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(to)!);
        File.Move(from, to, overwrite);
    }
    public void DeleteFile(string path) => File.Delete(path);
    public void DeleteDirectory(string path) => Directory.Delete(path, recursive: true);
}

public sealed class KnownFolders : IKnownFolders
{
    public string AppData => Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
    public string LocalAppData => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    public string Documents => Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
}
```

- [ ] **Step 6: Run tests to verify they pass**

Run: `dotnet test --filter DetectorTests`
Expected: PASS, 8 tests.

- [ ] **Step 7: Commit**

```bash
git add -A
git commit -m "feat: add host detection for all seven plugin hosts"
```

---

### Task 5: Plan resolution

**Files:**
- Create: `Onyx.Core/Install/InstallPlan.cs`, `Onyx.Core/Install/PlanResolver.cs`
- Test: `Onyx.Tests/Install/PlanResolverTests.cs`

**Interfaces:**
- Consumes: `PluginEntry`, `InstallStep`, `StepVerb` (Task 1); `Release`, `ReleaseAsset`, `AssetGlob` (Task 2); `HostInstance` (Task 4).
- Produces: `InstallPlan(string PluginId, string InstanceId, string Tag, ReleaseAsset Asset, IReadOnlyList<PlannedOperation> Operations)`, `PlannedOperation(StepVerb Verb, string? From, string? To)`, `PlanResolver.Resolve(...) -> InstallPlan`, `PlanException`.

- [ ] **Step 1: Write the failing tests**

`Onyx.Tests/Install/PlanResolverTests.cs`:

```csharp
using Onyx.Core.Catalog;
using Onyx.Core.Hosts;
using Onyx.Core.Install;
using Onyx.Core.Releases;

namespace Onyx.Tests.Install;

public class PlanResolverTests
{
    static Release Release(string tag, params string[] assets) =>
        new(tag, DateTimeOffset.UnixEpoch, false, "", assets.Select(a => new ReleaseAsset(a, $"https://x/{a}", 1)).ToList());

    static PluginEntry Plugin(string asset, params InstallStep[] steps) =>
        new("p", "P", "s", "Owner/Repo", "blender", asset, steps);

    [Fact]
    public void Substitutes_the_host_path_into_targets()
    {
        var host = new HostInstance("blender", "4.3", "Blender 4.3", @"C:\cfg\Blender\4.3");
        var plugin = Plugin("Aventurine-*.zip", new InstallStep(StepVerb.CopyDir, "Aventurine", "{host}/scripts/addons/Aventurine"));

        var plan = PlanResolver.Resolve(plugin, host, Release("3.1.5", "Aventurine-3.1.5.zip"));

        Assert.Equal("3.1.5", plan.Tag);
        Assert.Equal("Aventurine-3.1.5.zip", plan.Asset.Name);
        Assert.Equal(@"C:\cfg\Blender\4.3\scripts\addons\Aventurine", Assert.Single(plan.Operations).To);
    }

    [Fact]
    public void Fails_when_the_release_has_no_matching_asset()
    {
        var host = new HostInstance("blender", "4.3", "Blender 4.3", @"C:\cfg");
        var plugin = Plugin("Aventurine-*.zip", new InstallStep(StepVerb.CopyDir, "Aventurine", "{host}/x"));

        var ex = Assert.Throws<PlanException>(() => PlanResolver.Resolve(plugin, host, Release("3.1.5", "notes.txt")));
        Assert.Contains("Aventurine-*.zip", ex.Message);
    }

    [Fact]
    public void Fails_when_a_target_escapes_the_host_directory()
    {
        var host = new HostInstance("blender", "4.3", "Blender 4.3", @"C:\cfg\Blender\4.3");
        var plugin = Plugin("a.zip", new InstallStep(StepVerb.CopyDir, "x", "{host}/../../../Windows/System32/evil"));

        Assert.Throws<PlanException>(() => PlanResolver.Resolve(plugin, host, Release("1", "a.zip")));
    }

    [Fact]
    public void Keeps_a_sha256_step_ahead_of_every_write()
    {
        var host = new HostInstance("thumbnails", "default", "Explorer", @"C:\local\RitoShark\TexThumbnailProvider");
        var plugin = Plugin("TexThumbnailProvider.dll",
            new InstallStep(StepVerb.Copy, "TexThumbnailProvider.dll", "{host}/TexThumbnailProvider.dll"),
            new InstallStep(StepVerb.Sha256, "TexThumbnailProvider.dll.sha256", null));

        var plan = PlanResolver.Resolve(plugin, host, Release("v1.1.0", "TexThumbnailProvider.dll", "TexThumbnailProvider.dll.sha256"));

        Assert.Equal(StepVerb.Sha256, plan.Operations[0].Verb);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter PlanResolverTests`
Expected: FAIL — `PlanResolver` does not exist.

- [ ] **Step 3: Write the resolver**

`Onyx.Core/Install/InstallPlan.cs`:

```csharp
using Onyx.Core.Catalog;
using Onyx.Core.Releases;

namespace Onyx.Core.Install;

public sealed record PlannedOperation(StepVerb Verb, string? From, string? To);

public sealed record InstallPlan(
    string PluginId,
    string InstanceId,
    string Tag,
    ReleaseAsset Asset,
    IReadOnlyList<PlannedOperation> Operations);

public sealed class PlanException(string message) : Exception(message);
```

`Onyx.Core/Install/PlanResolver.cs`:

```csharp
using Onyx.Core.Catalog;
using Onyx.Core.Hosts;
using Onyx.Core.Releases;

namespace Onyx.Core.Install;

public static class PlanResolver
{
    public static InstallPlan Resolve(PluginEntry plugin, HostInstance host, Release release)
    {
        var asset = AssetGlob.Match(release.Assets, plugin.Asset)
            ?? throw new PlanException($"Release {release.Tag} has no asset matching '{plugin.Asset}'.");

        var operations = plugin.Steps
            .Select(s => new PlannedOperation(s.Verb, s.From, s.To is null ? null : Target(host, s.To)))
            .OrderBy(o => o.Verb == StepVerb.Sha256 ? 0 : 1)
            .ToList();

        return new InstallPlan(plugin.Id, host.InstanceId, release.Tag, asset, operations);
    }

    static string Target(HostInstance host, string template)
    {
        var root = Path.GetFullPath(host.Path);
        var combined = Path.GetFullPath(template.Replace("{host}", root).Replace('/', Path.DirectorySeparatorChar));

        if (!combined.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(combined, root, StringComparison.OrdinalIgnoreCase))
            throw new PlanException($"Install target '{combined}' escapes the host directory '{root}'.");

        return combined;
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test --filter PlanResolverTests`
Expected: PASS, 4 tests.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat: resolve catalog steps into concrete install plans"
```

---

### Task 6: Install engine with journal and rollback

**Files:**
- Create: `Onyx.Core/Install/InstallJournal.cs`, `Onyx.Core/Install/IPayload.cs`, `Onyx.Core/Install/InstallEngine.cs`
- Test: `Onyx.Tests/Install/InstallEngineTests.cs`

**Interfaces:**
- Consumes: `InstallPlan`, `PlannedOperation`, `StepVerb`, `IFileSystem`.
- Produces: `IPayload.Root`, `IRegistrar.Register/Unregister`, `InstallJournal(IReadOnlyList<string> Written, IReadOnlyList<string> Registered)`, `InstallEngine.Apply(InstallPlan, IPayload) -> InstallJournal`, `InstallEngine.Revert(InstallJournal)`.

- [ ] **Step 1: Write the failing tests**

`Onyx.Tests/Install/InstallEngineTests.cs`:

```csharp
using Onyx.Core.Catalog;
using Onyx.Core.Install;
using Onyx.Core.Releases;

namespace Onyx.Tests.Install;

public class InstallEngineTests
{
    sealed class FakePayload(string root) : IPayload { public string Root => root; }

    sealed class FakeRegistrar : IRegistrar
    {
        public List<string> Registered { get; } = [];
        public bool Fail { get; set; }
        public void Register(string dll)
        {
            if (Fail) throw new InvalidOperationException("regsvr32 failed");
            Registered.Add(dll);
        }
        public void Unregister(string dll) => Registered.Remove(dll);
    }

    static InstallPlan Plan(params PlannedOperation[] ops) =>
        new("p", "i", "v1", new ReleaseAsset("a.zip", "https://x/a.zip", 1), ops);

    [Fact]
    public void Copies_a_file_and_records_it_in_the_journal()
    {
        var fs = new FakeFileSystem().WithFile(@"C:\payload\RitoTex.8bi", [1, 2, 3]);
        var engine = new InstallEngine(fs, new FakeRegistrar());

        var journal = engine.Apply(
            Plan(new PlannedOperation(StepVerb.Copy, "RitoTex.8bi", @"C:\ps\Plug-ins\RitoTex.8bi")),
            new FakePayload(@"C:\payload"));

        Assert.True(fs.FileExists(@"C:\ps\Plug-ins\RitoTex.8bi"));
        Assert.Equal([@"C:\ps\Plug-ins\RitoTex.8bi"], journal.Written);
    }

    [Fact]
    public void Copies_a_directory_tree()
    {
        var fs = new FakeFileSystem()
            .WithFile(@"C:\payload\Aventurine\__init__.py")
            .WithFile(@"C:\payload\Aventurine\io\import_skn.py");
        var engine = new InstallEngine(fs, new FakeRegistrar());

        var journal = engine.Apply(
            Plan(new PlannedOperation(StepVerb.CopyDir, "Aventurine", @"C:\cfg\scripts\addons\Aventurine")),
            new FakePayload(@"C:\payload"));

        Assert.True(fs.FileExists(@"C:\cfg\scripts\addons\Aventurine\io\import_skn.py"));
        Assert.Equal(2, journal.Written.Count);
    }

    [Fact]
    public void Registers_a_dll_and_records_it()
    {
        var fs = new FakeFileSystem().WithFile(@"C:\payload\TexThumbnailProvider.dll");
        var registrar = new FakeRegistrar();
        var engine = new InstallEngine(fs, registrar);

        var journal = engine.Apply(
            Plan(
                new PlannedOperation(StepVerb.Copy, "TexThumbnailProvider.dll", @"C:\local\TexThumbnailProvider.dll"),
                new PlannedOperation(StepVerb.Regsvr32, null, @"C:\local\TexThumbnailProvider.dll")),
            new FakePayload(@"C:\payload"));

        Assert.Equal([@"C:\local\TexThumbnailProvider.dll"], registrar.Registered);
        Assert.Equal([@"C:\local\TexThumbnailProvider.dll"], journal.Registered);
    }

    [Fact]
    public void A_failing_step_leaves_the_filesystem_as_it_started()
    {
        var fs = new FakeFileSystem().WithFile(@"C:\payload\TexThumbnailProvider.dll");
        var registrar = new FakeRegistrar { Fail = true };
        var engine = new InstallEngine(fs, registrar);

        Assert.Throws<InvalidOperationException>(() => engine.Apply(
            Plan(
                new PlannedOperation(StepVerb.Copy, "TexThumbnailProvider.dll", @"C:\local\TexThumbnailProvider.dll"),
                new PlannedOperation(StepVerb.Regsvr32, null, @"C:\local\TexThumbnailProvider.dll")),
            new FakePayload(@"C:\payload")));

        Assert.False(fs.FileExists(@"C:\local\TexThumbnailProvider.dll"));
    }

    [Fact]
    public void Revert_removes_written_files_and_unregisters()
    {
        var fs = new FakeFileSystem().WithFile(@"C:\payload\a.dll");
        var registrar = new FakeRegistrar();
        var engine = new InstallEngine(fs, registrar);

        var journal = engine.Apply(
            Plan(
                new PlannedOperation(StepVerb.Copy, "a.dll", @"C:\local\a.dll"),
                new PlannedOperation(StepVerb.Regsvr32, null, @"C:\local\a.dll")),
            new FakePayload(@"C:\payload"));

        engine.Revert(journal);

        Assert.False(fs.FileExists(@"C:\local\a.dll"));
        Assert.Empty(registrar.Registered);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter InstallEngineTests`
Expected: FAIL — `InstallEngine` does not exist.

- [ ] **Step 3: Write the engine**

`Onyx.Core/Install/IPayload.cs`:

```csharp
namespace Onyx.Core.Install;

public interface IPayload { string Root { get; } }

public interface IRegistrar
{
    void Register(string dllPath);
    void Unregister(string dllPath);
}
```

`Onyx.Core/Install/InstallJournal.cs`:

```csharp
namespace Onyx.Core.Install;

public sealed record InstallJournal(IReadOnlyList<string> Written, IReadOnlyList<string> Registered)
{
    public static InstallJournal Empty => new([], []);
}
```

`Onyx.Core/Install/InstallEngine.cs`:

```csharp
using System.Security.Cryptography;
using Onyx.Core.Catalog;
using Onyx.Core.Hosts;

namespace Onyx.Core.Install;

public sealed class InstallEngine(IFileSystem fs, IRegistrar registrar)
{
    public InstallJournal Apply(InstallPlan plan, IPayload payload)
    {
        var written = new List<string>();
        var registered = new List<string>();

        try
        {
            foreach (var op in plan.Operations)
                switch (op.Verb)
                {
                    case StepVerb.Sha256: Verify(payload, op); break;
                    case StepVerb.Copy: written.Add(CopyOne(payload, op)); break;
                    case StepVerb.CopyDir: written.AddRange(CopyTree(payload, op)); break;
                    case StepVerb.Regsvr32: registrar.Register(op.To!); registered.Add(op.To!); break;
                    default: throw new PlanException($"Unhandled verb {op.Verb}.");
                }
        }
        catch
        {
            Revert(new InstallJournal(written, registered));
            throw;
        }

        return new InstallJournal(written, registered);
    }

    public void Revert(InstallJournal journal)
    {
        foreach (var dll in journal.Registered.Reverse())
            try { registrar.Unregister(dll); } catch { }

        foreach (var file in journal.Written.Reverse())
            try { if (fs.FileExists(file)) fs.DeleteFile(file); } catch { }
    }

    string CopyOne(IPayload payload, PlannedOperation op)
    {
        var from = Path.Combine(payload.Root, op.From!);
        fs.CreateDirectory(Path.GetDirectoryName(op.To!)!);
        fs.Copy(from, op.To!, overwrite: true);
        return op.To!;
    }

    IEnumerable<string> CopyTree(IPayload payload, PlannedOperation op)
    {
        var from = Path.Combine(payload.Root, op.From!);
        var written = new List<string>();
        Walk(from, op.To!, written);
        return written;
    }

    void Walk(string from, string to, List<string> written)
    {
        fs.CreateDirectory(to);
        foreach (var file in fs.Files(from))
        {
            var target = Path.Combine(to, Path.GetFileName(file));
            fs.Copy(file, target, overwrite: true);
            written.Add(target);
        }
        foreach (var dir in fs.Directories(from))
            Walk(dir, Path.Combine(to, Path.GetFileName(dir)), written);
    }

    void Verify(IPayload payload, PlannedOperation op)
    {
        var sidecar = Path.Combine(payload.Root, op.From!);
        if (!fs.FileExists(sidecar)) return;

        var subject = sidecar[..^".sha256".Length];
        var expected = File.ReadAllText(sidecar).Split(' ', '\n')[0].Trim().ToLowerInvariant();
        var actual = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(subject))).ToLowerInvariant();

        if (expected != actual)
            throw new PlanException($"Checksum mismatch for {Path.GetFileName(subject)}.");
    }
}
```

Note the `Verify` step reads through `System.IO` directly rather than `IFileSystem`, because `IFileSystem` has no byte-level read. The fixture tests for `sha256` cover it in Task 11's end-to-end run rather than here.

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test --filter InstallEngineTests`
Expected: PASS, 5 tests.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat: add install engine with journal and rollback"
```

---

### Task 7: State store

**Files:**
- Create: `Onyx.Core/State/InstallRecord.cs`, `Onyx.Core/State/StateStore.cs`
- Test: `Onyx.Tests/State/StateStoreTests.cs`

**Interfaces:**
- Consumes: `InstallJournal` (Task 6).
- Produces: `InstallRecord(string PluginId, string InstanceId, string Tag, IReadOnlyList<string> Written, IReadOnlyList<string> Registered, DateTimeOffset InstalledAt)`, `StateStore.Load()/Save()`, `StateStore.Record(...)`, `StateStore.Find(pluginId, instanceId) -> InstallRecord?`, `StateStore.Forget(pluginId, instanceId)`.

- [ ] **Step 1: Write the failing tests**

`Onyx.Tests/State/StateStoreTests.cs`:

```csharp
using Onyx.Core.Install;
using Onyx.Core.State;

namespace Onyx.Tests.State;

public class StateStoreTests : IDisposable
{
    readonly string _path = Path.Combine(Path.GetTempPath(), $"onyx-{Guid.NewGuid():N}.json");

    public void Dispose() { if (File.Exists(_path)) File.Delete(_path); }

    [Fact]
    public void Round_trips_a_record()
    {
        var store = StateStore.Load(_path);
        store.Record("aventurine", "4.3", "3.1.5", new InstallJournal([@"C:\a\b.py"], []));
        store.Save();

        var reloaded = StateStore.Load(_path);
        var record = reloaded.Find("aventurine", "4.3");

        Assert.NotNull(record);
        Assert.Equal("3.1.5", record.Tag);
        Assert.Equal([@"C:\a\b.py"], record.Written);
    }

    [Fact]
    public void Records_are_keyed_by_plugin_and_instance()
    {
        var store = StateStore.Load(_path);
        store.Record("aventurine", "4.1", "3.1.4", InstallJournal.Empty);
        store.Record("aventurine", "4.3", "3.1.5", InstallJournal.Empty);

        Assert.Equal("3.1.4", store.Find("aventurine", "4.1")!.Tag);
        Assert.Equal("3.1.5", store.Find("aventurine", "4.3")!.Tag);
    }

    [Fact]
    public void Recording_the_same_key_twice_replaces_it()
    {
        var store = StateStore.Load(_path);
        store.Record("aventurine", "4.3", "3.1.4", InstallJournal.Empty);
        store.Record("aventurine", "4.3", "3.1.5", InstallJournal.Empty);

        Assert.Equal("3.1.5", store.Find("aventurine", "4.3")!.Tag);
        Assert.Single(store.All);
    }

    [Fact]
    public void Forget_removes_a_record()
    {
        var store = StateStore.Load(_path);
        store.Record("aventurine", "4.3", "3.1.5", InstallJournal.Empty);
        store.Forget("aventurine", "4.3");

        Assert.Null(store.Find("aventurine", "4.3"));
    }

    [Fact]
    public void A_missing_file_loads_as_empty()
    {
        Assert.Empty(StateStore.Load(_path).All);
    }

    [Fact]
    public void A_corrupt_file_loads_as_empty_rather_than_throwing()
    {
        File.WriteAllText(_path, "{ not json");
        Assert.Empty(StateStore.Load(_path).All);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter StateStoreTests`
Expected: FAIL — `StateStore` does not exist.

- [ ] **Step 3: Write the store**

`Onyx.Core/State/InstallRecord.cs`:

```csharp
namespace Onyx.Core.State;

public sealed record InstallRecord(
    string PluginId,
    string InstanceId,
    string Tag,
    IReadOnlyList<string> Written,
    IReadOnlyList<string> Registered,
    DateTimeOffset InstalledAt);
```

`Onyx.Core/State/StateStore.cs`:

```csharp
using System.Text.Json;
using Onyx.Core.Install;

namespace Onyx.Core.State;

public sealed class StateStore
{
    static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    readonly string _path;
    readonly List<InstallRecord> _records;

    StateStore(string path, List<InstallRecord> records) { _path = path; _records = records; }

    public IReadOnlyList<InstallRecord> All => _records;

    public static StateStore Load(string path)
    {
        if (!File.Exists(path)) return new StateStore(path, []);
        try
        {
            var records = JsonSerializer.Deserialize<List<InstallRecord>>(File.ReadAllText(path), Options);
            return new StateStore(path, records ?? []);
        }
        catch (JsonException)
        {
            return new StateStore(path, []);
        }
    }

    public static string DefaultPath(string localAppData) =>
        Path.Combine(localAppData, "RitoShark", "Onyx", "state.json");

    public InstallRecord? Find(string pluginId, string instanceId) =>
        _records.FirstOrDefault(r => r.PluginId == pluginId && r.InstanceId == instanceId);

    public void Record(string pluginId, string instanceId, string tag, InstallJournal journal)
    {
        Forget(pluginId, instanceId);
        _records.Add(new InstallRecord(pluginId, instanceId, tag, journal.Written, journal.Registered, DateTimeOffset.UtcNow));
    }

    public void Forget(string pluginId, string instanceId) =>
        _records.RemoveAll(r => r.PluginId == pluginId && r.InstanceId == instanceId);

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllText(_path, JsonSerializer.Serialize(_records, Options));
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test --filter StateStoreTests`
Expected: PASS, 6 tests.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat: add install state store"
```

---

### Task 8: Process guard

**Files:**
- Create: `Onyx.Core/Processes/IProcessTable.cs`, `Onyx.Core/Processes/ProcessGuard.cs`, `Onyx.Core/Processes/WindowsProcessTable.cs`
- Test: `Onyx.Tests/Processes/ProcessGuardTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: `IProcessTable.Running(IReadOnlyList<string> names) -> IReadOnlyList<RunningProcess>`, `IProcessTable.RequestClose(int pid)`, `RunningProcess(int Pid, string Name)`, `ProcessGuard.Check(hostId) -> IReadOnlyList<RunningProcess>`, `ProcessGuard.CloseAndWaitAsync(...) -> Task<bool>`, `ProcessGuard.ProcessNamesFor(hostId)`.

- [ ] **Step 1: Write the failing tests**

`Onyx.Tests/Processes/ProcessGuardTests.cs`:

```csharp
using Onyx.Core.Processes;

namespace Onyx.Tests.Processes;

public class ProcessGuardTests
{
    sealed class FakeProcessTable : IProcessTable
    {
        public List<RunningProcess> Processes { get; } = [];
        public List<int> CloseRequests { get; } = [];
        public bool CloseActuallyExits { get; set; } = true;

        public IReadOnlyList<RunningProcess> Running(IReadOnlyList<string> names) =>
            Processes.Where(p => names.Contains(p.Name, StringComparer.OrdinalIgnoreCase)).ToList();

        public void RequestClose(int pid)
        {
            CloseRequests.Add(pid);
            if (CloseActuallyExits) Processes.RemoveAll(p => p.Pid == pid);
        }
    }

    [Fact]
    public void Reports_a_running_host()
    {
        var table = new FakeProcessTable();
        table.Processes.Add(new RunningProcess(42, "Photoshop"));

        Assert.Single(new ProcessGuard(table).Check("photoshop"));
    }

    [Fact]
    public void Reports_nothing_when_the_host_is_closed()
    {
        Assert.Empty(new ProcessGuard(new FakeProcessTable()).Check("photoshop"));
    }

    [Fact]
    public void The_thumbnail_host_is_guarded_by_explorer()
    {
        Assert.Contains("explorer", ProcessGuard.ProcessNamesFor("thumbnails"), StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void Blender_maya_gimp_and_paintnet_all_have_guards()
    {
        foreach (var host in new[] { "blender", "maya", "gimp", "paintnet", "photoshop" })
            Assert.NotEmpty(ProcessGuard.ProcessNamesFor(host));
    }

    [Fact]
    public async Task Close_requests_a_graceful_exit_and_succeeds_when_the_host_quits()
    {
        var table = new FakeProcessTable();
        table.Processes.Add(new RunningProcess(42, "blender"));

        var closed = await new ProcessGuard(table).CloseAndWaitAsync("blender", TimeSpan.FromSeconds(1), default);

        Assert.True(closed);
        Assert.Equal([42], table.CloseRequests);
    }

    [Fact]
    public async Task Close_returns_false_rather_than_killing_a_host_that_refuses()
    {
        var table = new FakeProcessTable { CloseActuallyExits = false };
        table.Processes.Add(new RunningProcess(42, "blender"));

        var closed = await new ProcessGuard(table).CloseAndWaitAsync("blender", TimeSpan.FromMilliseconds(200), default);

        Assert.False(closed);
        Assert.Single(table.Processes);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter ProcessGuardTests`
Expected: FAIL — `ProcessGuard` does not exist.

- [ ] **Step 3: Write the guard**

`Onyx.Core/Processes/IProcessTable.cs`:

```csharp
namespace Onyx.Core.Processes;

public sealed record RunningProcess(int Pid, string Name);

public interface IProcessTable
{
    IReadOnlyList<RunningProcess> Running(IReadOnlyList<string> names);
    void RequestClose(int pid);
}
```

`Onyx.Core/Processes/ProcessGuard.cs`:

```csharp
namespace Onyx.Core.Processes;

public sealed class ProcessGuard(IProcessTable table)
{
    static readonly Dictionary<string, string[]> Guards = new(StringComparer.Ordinal)
    {
        ["photoshop"] = ["Photoshop"],
        ["paintnet"] = ["paintdotnet", "PaintDotNet"],
        ["gimp"] = ["gimp-2.10", "gimp-3.0", "gimp"],
        ["maya"] = ["maya"],
        ["blender"] = ["blender"],
        ["thumbnails"] = ["explorer"]
    };

    public static IReadOnlyList<string> ProcessNamesFor(string hostId) =>
        Guards.GetValueOrDefault(hostId) ?? [];

    public IReadOnlyList<RunningProcess> Check(string hostId) =>
        table.Running(ProcessNamesFor(hostId));

    public async Task<bool> CloseAndWaitAsync(string hostId, TimeSpan timeout, CancellationToken ct)
    {
        foreach (var process in Check(hostId))
            table.RequestClose(process.Pid);

        var deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (Check(hostId).Count == 0) return true;
            await Task.Delay(100, ct);
        }

        return Check(hostId).Count == 0;
    }
}
```

`Onyx.Core/Processes/WindowsProcessTable.cs`:

```csharp
using System.Diagnostics;

namespace Onyx.Core.Processes;

public sealed class WindowsProcessTable : IProcessTable
{
    public IReadOnlyList<RunningProcess> Running(IReadOnlyList<string> names) =>
        names.SelectMany(Process.GetProcessesByName)
             .Select(p => new RunningProcess(p.Id, p.ProcessName))
             .DistinctBy(p => p.Pid)
             .ToList();

    public void RequestClose(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            process.CloseMainWindow();
        }
        catch (ArgumentException) { }
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test --filter ProcessGuardTests`
Expected: PASS, 6 tests.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat: add process guard for running hosts"
```

---

### Task 9: The catalog itself and its source resolution

**Files:**
- Create: `Onyx.Core/Catalog/catalog.json` (embedded resource), `Onyx.Core/Catalog/CatalogSource.cs`
- Modify: `Onyx.Core/Onyx.Core.csproj` (embed the resource)
- Test: `Onyx.Tests/Catalog/EmbeddedCatalogTests.cs`, `Onyx.Tests/Catalog/CatalogSourceTests.cs`

**Interfaces:**
- Consumes: `CatalogReader`, `Catalog` (Task 1).
- Produces: `CatalogSource.Embedded() -> Catalog`, `CatalogSource.Best(Catalog embedded, string? cachedJson) -> Catalog`.

- [ ] **Step 1: Write `catalog.json` with all seven plugins**

```json
{
  "schema": 1,
  "revision": 1,
  "plugins": [
    {
      "id": "ritotex-photoshop",
      "name": "RitoTex for Photoshop",
      "summary": "Open and save .tex and .dds textures directly in Photoshop.",
      "repo": "RitoShark/RitoTex-Photoshop",
      "host": "photoshop",
      "asset": "RitoTex.8bi",
      "steps": [
        { "verb": "copy", "from": "RitoTex.8bi", "to": "{host}/Plug-ins/RitoTex.8bi" }
      ]
    },
    {
      "id": "tex-paintnet",
      "name": "Paint.NET .tex",
      "summary": "Import and save League .tex files in Paint.NET.",
      "repo": "RitoShark/Paint.NET-Tex-Plugin",
      "host": "paintnet",
      "asset": "TexFileType-*.zip",
      "steps": [
        { "verb": "copyDir", "from": ".", "to": "{host}/FileTypes" }
      ]
    },
    {
      "id": "tex-gimp2",
      "name": "GIMP 2 .tex",
      "summary": "Import and save League .tex files in GIMP 2.10.",
      "repo": "RitoShark/Gimp-Tex-Plugin",
      "host": "gimp",
      "hostInstance": "2.10",
      "asset": "GIMP2_TEX_Plugin_Windows.zip",
      "steps": [
        { "verb": "copyDir", "from": ".", "to": "{host}/plug-ins" }
      ]
    },
    {
      "id": "tex-gimp3",
      "name": "GIMP 3 .tex",
      "summary": "Import and save League .tex files in GIMP 3.",
      "repo": "RitoShark/Gimp-Tex-Plugin",
      "host": "gimp",
      "hostInstance": "3.0",
      "asset": "GIMP3_TEX_Plugin_Windows.zip",
      "steps": [
        { "verb": "copyDir", "from": ".", "to": "{host}/plug-ins" }
      ]
    },
    {
      "id": "ritoshark-maya",
      "name": "RitoShark for Maya",
      "summary": "Open League models, skeletons and animations natively in Maya.",
      "repo": "RitoShark/RitoShark-Maya",
      "host": "maya",
      "asset": "RitoShark-Maya-*.zip",
      "steps": [
        { "verb": "copyDir", "from": "plug-ins", "to": "{host}/plug-ins" },
        { "verb": "copyDir", "from": "scripts", "to": "{host}/scripts" },
        { "verb": "copyDir", "from": "prefs", "to": "{host}/prefs" }
      ]
    },
    {
      "id": "aventurine-blender",
      "name": "Aventurine for Blender",
      "summary": "Native Blender addon for League file formats.",
      "repo": "RitoShark/Aventurine-League-Tools",
      "host": "blender",
      "asset": "Aventurine-*.zip",
      "steps": [
        { "verb": "copyDir", "from": "Aventurine", "to": "{host}/scripts/addons/Aventurine" }
      ]
    },
    {
      "id": "tex-thumbnails",
      "name": ".tex Explorer thumbnails",
      "summary": "Show thumbnails for .tex files in Windows Explorer.",
      "repo": "RitoShark/TexThumbnailProvider",
      "host": "thumbnails",
      "asset": "TexThumbnailProvider.dll",
      "steps": [
        { "verb": "sha256", "from": "TexThumbnailProvider.dll.sha256" },
        { "verb": "copy", "from": "TexThumbnailProvider.dll", "to": "{host}/TexThumbnailProvider.dll" },
        { "verb": "regsvr32", "to": "{host}/TexThumbnailProvider.dll" }
      ]
    }
  ]
}
```

The Maya zip nests everything under `RitoShark-Maya-<version>/`. The payload extractor in Task 11 strips a single top-level directory when the archive has exactly one, so `plug-ins` here resolves correctly. The Aventurine zip's single top-level `Aventurine/` is the directory being copied, so that entry names it explicitly — verify this against the extractor's stripping rule during Task 11 and adjust the `from` to `.` if stripping applies.

GIMP is two entries, not one. Every Gimp-Tex-Plugin release carries both a
`GIMP2_TEX_Plugin_Windows.zip` and a `GIMP3_TEX_Plugin_Windows.zip`, so a shared
`GIMP*_` glob would match whichever asset the API happens to list first. Each entry
therefore names its asset exactly and pins `hostInstance` to the config-version
directory it belongs in.

- [ ] **Step 2: Add `hostInstance` to the model and reader**

In `PluginEntry`, add `string? HostInstance` after `Host`. In `CatalogReader.Read`, read it with `Opt(p, "hostInstance")`. A plugin with `HostInstance` set applies only to the host instance whose `InstanceId` equals it.

- [ ] **Step 3: Write the failing tests**

`Onyx.Tests/Catalog/EmbeddedCatalogTests.cs`:

```csharp
using Onyx.Core.Catalog;

namespace Onyx.Tests.Catalog;

public class EmbeddedCatalogTests
{
    [Fact]
    public void The_shipped_catalog_parses()
    {
        var catalog = CatalogSource.Embedded();
        Assert.Equal(7, catalog.Plugins.Count);
    }

    [Theory]
    [InlineData("ritotex-photoshop", "photoshop")]
    [InlineData("tex-paintnet", "paintnet")]
    [InlineData("tex-gimp2", "gimp")]
    [InlineData("tex-gimp3", "gimp")]
    [InlineData("ritoshark-maya", "maya")]
    [InlineData("aventurine-blender", "blender")]
    [InlineData("tex-thumbnails", "thumbnails")]
    public void Every_expected_plugin_is_present_on_its_host(string id, string host)
    {
        var plugin = CatalogSource.Embedded().Plugins.Single(p => p.Id == id);
        Assert.Equal(host, plugin.Host);
    }

    [Fact]
    public void The_gimp_entries_target_different_versions_and_assets()
    {
        var plugins = CatalogSource.Embedded().Plugins;
        var two = plugins.Single(p => p.Id == "tex-gimp2");
        var three = plugins.Single(p => p.Id == "tex-gimp3");

        Assert.Equal("2.10", two.HostInstance);
        Assert.Equal("3.0", three.HostInstance);
        Assert.NotEqual(two.Asset, three.Asset);
    }
}
```

`Onyx.Tests/Catalog/CatalogSourceTests.cs`:

```csharp
using Onyx.Core.Catalog;

namespace Onyx.Tests.Catalog;

public class CatalogSourceTests
{
    static string WithRevision(int revision) => $$"""
    {
      "schema": 1,
      "revision": {{revision}},
      "plugins": [
        { "id": "x", "name": "X", "summary": "s", "repo": "o/r", "host": "blender",
          "asset": "a.zip", "steps": [ { "verb": "copyDir", "from": ".", "to": "{host}/x" } ] }
      ]
    }
    """;

    [Fact]
    public void A_newer_cached_catalog_wins()
    {
        var embedded = CatalogReader.Read(WithRevision(1));
        Assert.Equal(5, CatalogSource.Best(embedded, WithRevision(5)).Revision);
    }

    [Fact]
    public void An_older_cached_catalog_loses()
    {
        var embedded = CatalogReader.Read(WithRevision(9));
        Assert.Equal(9, CatalogSource.Best(embedded, WithRevision(2)).Revision);
    }

    [Fact]
    public void A_corrupt_cached_catalog_falls_back_to_embedded()
    {
        var embedded = CatalogReader.Read(WithRevision(1));
        Assert.Equal(1, CatalogSource.Best(embedded, "{ not json").Revision);
    }

    [Fact]
    public void A_newer_schema_in_the_cache_falls_back_to_embedded()
    {
        var embedded = CatalogReader.Read(WithRevision(1));
        var future = WithRevision(99).Replace("\"schema\": 1", "\"schema\": 2");
        Assert.Equal(1, CatalogSource.Best(embedded, future).Revision);
    }

    [Fact]
    public void No_cache_means_embedded()
    {
        var embedded = CatalogReader.Read(WithRevision(1));
        Assert.Same(embedded, CatalogSource.Best(embedded, null));
    }
}
```

- [ ] **Step 4: Embed the resource**

In `Onyx.Core/Onyx.Core.csproj`:

```xml
<ItemGroup>
  <EmbeddedResource Include="Catalog\catalog.json" LogicalName="Onyx.Core.catalog.json" />
</ItemGroup>
```

- [ ] **Step 5: Write `CatalogSource`**

```csharp
using System.Reflection;

namespace Onyx.Core.Catalog;

public static class CatalogSource
{
    public static Catalog Embedded()
    {
        using var stream = typeof(CatalogSource).Assembly
            .GetManifestResourceStream("Onyx.Core.catalog.json")
            ?? throw new CatalogException("Embedded catalog is missing.");
        using var reader = new StreamReader(stream);
        return CatalogReader.Read(reader.ReadToEnd());
    }

    public static Catalog Best(Catalog embedded, string? cachedJson)
    {
        if (cachedJson is null) return embedded;

        try
        {
            var cached = CatalogReader.Read(cachedJson);
            return cached.Revision > embedded.Revision ? cached : embedded;
        }
        catch (CatalogException)
        {
            return embedded;
        }
    }
}
```

- [ ] **Step 6: Run tests to verify they pass**

Run: `dotnet test --filter "EmbeddedCatalogTests|CatalogSourceTests"`
Expected: PASS, 13 tests.

- [ ] **Step 7: Commit**

```bash
git add -A
git commit -m "feat: ship the plugin catalog and resolve its source"
```

---

### Task 10: Download and payload extraction

**Files:**
- Create: `Onyx.Core/Install/PayloadFetcher.cs`, `Onyx.Core/Install/TempPayload.cs`
- Test: `Onyx.Tests/Install/PayloadFetcherTests.cs`

**Interfaces:**
- Consumes: `ReleaseAsset` (Task 2), `IPayload` (Task 6).
- Produces: `TempPayload : IPayload, IDisposable`, `PayloadFetcher.FetchAsync(ReleaseAsset, CancellationToken) -> Task<TempPayload>`.

- [ ] **Step 1: Write the failing tests**

`Onyx.Tests/Install/PayloadFetcherTests.cs`:

```csharp
using System.IO.Compression;
using System.Net;
using Onyx.Core.Install;
using Onyx.Core.Releases;

namespace Onyx.Tests.Install;

public class PayloadFetcherTests
{
    static byte[] Zip(params (string Path, string Content)[] entries)
    {
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
            foreach (var (path, content) in entries)
            {
                using var writer = new StreamWriter(archive.CreateEntry(path).Open());
                writer.Write(content);
            }
        return buffer.ToArray();
    }

    static StubHandler Binary(byte[] bytes) =>
        new(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) });

    [Fact]
    public async Task A_loose_file_asset_lands_in_the_payload_root()
    {
        var handler = Binary([1, 2, 3]);
        var fetcher = new PayloadFetcher(new HttpClient(handler));

        using var payload = await fetcher.FetchAsync(new ReleaseAsset("RitoTex.8bi", "https://x/RitoTex.8bi", 3), default);

        Assert.True(File.Exists(Path.Combine(payload.Root, "RitoTex.8bi")));
    }

    [Fact]
    public async Task A_zip_is_extracted()
    {
        var handler = Binary(Zip(("Aventurine/__init__.py", "x")));
        var fetcher = new PayloadFetcher(new HttpClient(handler));

        using var payload = await fetcher.FetchAsync(new ReleaseAsset("Aventurine-3.1.5.zip", "https://x/a.zip", 1), default);

        Assert.True(File.Exists(Path.Combine(payload.Root, "Aventurine", "__init__.py")));
    }

    [Fact]
    public async Task A_single_wrapper_directory_is_stripped()
    {
        var handler = Binary(Zip(
            ("RitoShark-Maya-v0.2.0/plug-ins/ritoshark_plugin.py", "x"),
            ("RitoShark-Maya-v0.2.0/scripts/ritoshark_maya/__init__.py", "y")));
        var fetcher = new PayloadFetcher(new HttpClient(handler));

        using var payload = await fetcher.FetchAsync(new ReleaseAsset("RitoShark-Maya-v0.2.0.zip", "https://x/m.zip", 1), default);

        Assert.True(File.Exists(Path.Combine(payload.Root, "plug-ins", "ritoshark_plugin.py")));
    }

    [Fact]
    public async Task A_zip_entry_that_escapes_the_root_is_rejected()
    {
        var handler = Binary(Zip(("../evil.txt", "x")));
        var fetcher = new PayloadFetcher(new HttpClient(handler));

        await Assert.ThrowsAsync<PlanException>(
            () => fetcher.FetchAsync(new ReleaseAsset("bad.zip", "https://x/bad.zip", 1), default));
    }

    [Fact]
    public async Task Disposing_the_payload_removes_the_temp_directory()
    {
        var fetcher = new PayloadFetcher(new HttpClient(Binary([1])));
        string root;
        using (var payload = await fetcher.FetchAsync(new ReleaseAsset("a.bin", "https://x/a.bin", 1), default))
            root = payload.Root;

        Assert.False(Directory.Exists(root));
    }
}
```

Note: the Maya stripping test and the Aventurine non-stripping test conflict — stripping applies only when the archive's single top-level directory is the *only* top-level entry AND the plugin's steps do not name it. Resolve this by stripping only when the single top-level directory name matches the asset's file name without extension (`RitoShark-Maya-v0.2.0.zip` → `RitoShark-Maya-v0.2.0/`). Aventurine's zip is `Aventurine-3.1.5.zip` containing `Aventurine/`, which does not match, so it is not stripped and the catalog's `from: "Aventurine"` is correct.

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter PayloadFetcherTests`
Expected: FAIL — `PayloadFetcher` does not exist.

- [ ] **Step 3: Write the fetcher**

```csharp
using System.IO.Compression;

namespace Onyx.Core.Install;

public sealed class TempPayload : IPayload, IDisposable
{
    public TempPayload(string root) => Root = root;
    public string Root { get; }
    public void Dispose()
    {
        try { if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true); } catch { }
    }
}

public sealed class PayloadFetcher(HttpClient http)
{
    public async Task<TempPayload> FetchAsync(Onyx.Core.Releases.ReleaseAsset asset, CancellationToken ct)
    {
        var root = Path.Combine(Path.GetTempPath(), "onyx-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var payload = new TempPayload(root);

        try
        {
            var bytes = await http.GetByteArrayAsync(asset.DownloadUrl, ct);

            if (!asset.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                await File.WriteAllBytesAsync(Path.Combine(root, asset.Name), bytes, ct);
                return payload;
            }

            using var archive = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
            var strip = StripPrefix(archive, asset.Name);

            foreach (var entry in archive.Entries)
            {
                if (entry.FullName.EndsWith('/')) continue;

                var relative = strip is null ? entry.FullName : entry.FullName[strip.Length..];
                var target = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));

                if (!target.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    throw new PlanException($"Archive entry '{entry.FullName}' escapes the payload directory.");

                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                entry.ExtractToFile(target, overwrite: true);
            }

            return payload;
        }
        catch
        {
            payload.Dispose();
            throw;
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
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test --filter PayloadFetcherTests`
Expected: PASS, 5 tests.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat: download and extract release payloads"
```

---

### Task 11: The application service that ties Core together

**Files:**
- Create: `Onyx.Core/PluginManager.cs`, `Onyx.Core/PluginStatus.cs`
- Test: `Onyx.Tests/PluginManagerTests.cs`

**Interfaces:**
- Consumes: everything from Tasks 1-10.
- Produces: `PluginStatus(PluginEntry Plugin, HostInstance? Host, string? InstalledTag, Release? Latest, bool UpdateAvailable)`, `PluginManager.StatusAsync()`, `PluginManager.InstallAsync(pluginId, instanceId, tag)`, `PluginManager.UninstallAsync(pluginId, instanceId)`.

- [ ] **Step 1: Write the failing tests**

Test that `StatusAsync` pairs each catalog plugin with its detected host instances, honours `HostInstance` filtering (a GIMP 3 entry never pairs with a GIMP 2.10 instance), reports `InstalledTag` from the state store, and computes `UpdateAvailable`. Test that `InstallAsync` refuses when the process guard reports a running host, and that a successful install writes a state record.

```csharp
using Onyx.Core;
using Onyx.Core.Catalog;
using Onyx.Core.Hosts;
using Onyx.Core.Releases;

namespace Onyx.Tests;

public class PluginManagerTests
{
    [Fact]
    public async Task Pairs_each_plugin_with_every_matching_host_instance()
    {
        var manager = Build(hosts:
        [
            new HostInstance("blender", "4.1", "Blender 4.1", @"C:\cfg\4.1"),
            new HostInstance("blender", "4.3", "Blender 4.3", @"C:\cfg\4.3")
        ]);

        var statuses = await manager.StatusAsync(default);

        Assert.Equal(2, statuses.Count(s => s.Plugin.Id == "aventurine-blender"));
    }

    [Fact]
    public async Task A_pinned_host_instance_never_pairs_with_another_version()
    {
        var manager = Build(hosts: [new HostInstance("gimp", "2.10", "GIMP 2.10", @"C:\gimp\2.10")]);

        var statuses = await manager.StatusAsync(default);

        Assert.Contains(statuses, s => s.Plugin.Id == "tex-gimp2" && s.Host is not null);
        Assert.DoesNotContain(statuses, s => s.Plugin.Id == "tex-gimp3" && s.Host is not null);
    }

    [Fact]
    public async Task Install_refuses_while_the_host_is_running()
    {
        var manager = Build(
            hosts: [new HostInstance("blender", "4.3", "Blender 4.3", @"C:\cfg\4.3")],
            running: ["blender"]);

        await Assert.ThrowsAsync<HostRunningException>(
            () => manager.InstallAsync("aventurine-blender", "4.3", "3.1.5", default));
    }
}
```

Build a `Build(...)` helper in the test class that wires `PluginManager` from `CatalogSource.Embedded()`, a fake `HostRegistry` seeded with the given instances, a `GitHubClient` over a `StubHandler` returning a recorded release list, a temp `StateStore`, a `FakeProcessTable` seeded with the given running names, and the `FakeFileSystem`.

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter PluginManagerTests`
Expected: FAIL — `PluginManager` does not exist.

- [ ] **Step 3: Write `PluginManager`**

`StatusAsync` cross-joins catalog plugins with `HostRegistry.For(plugin.Host)`, filtered by `plugin.HostInstance`, and for each pair looks up the state record and the cached release list. A plugin whose host is not installed yields one status with `Host = null` so the UI can show it greyed with "Photoshop not found".

`InstallAsync` runs: process guard check (throw `HostRunningException` with the blocking process names) → fetch releases → find the requested tag → `PlanResolver.Resolve` → `PayloadFetcher.FetchAsync` → `InstallEngine.Apply` → `StateStore.Record` + `Save`.

`UninstallAsync` runs: process guard check → load the record → `InstallEngine.Revert(record as journal)` → `StateStore.Forget` + `Save`.

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test`
Expected: PASS, all tests green.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat: add the plugin manager service"
```

---

### Task 12: WPF shell, theme and window chrome

**Files:**
- Create: `Onyx/Onyx.csproj`, `Onyx/App.xaml`, `Onyx/App.xaml.cs`, `Onyx/MainWindow.xaml`, `Onyx/MainWindow.xaml.cs`
- Create: `Onyx/Theme/Palette.xaml`, `Onyx/Theme/Typography.xaml`, `Onyx/Theme/Controls.xaml`, `Onyx/Theme/Window.xaml`
- Modify: `Onyx.sln`

**Interfaces:**
- Consumes: nothing from Core yet.
- Produces: `OnyxWindow` base style, `Card`, `PrimaryButton`, `PillButton`, `SubtleButton`, `OnyxComboBox` styles; brush keys `Surface`, `SurfaceRaised`, `Separator`, `TextPrimary`, `TextSecondary`, `Accent`, `AccentPressed`.

- [ ] **Step 1: Scaffold the WPF project**

```bash
dotnet new wpf -n Onyx -f net9.0
dotnet sln add Onyx/Onyx.csproj
dotnet add Onyx/Onyx.csproj reference Onyx.Core/Onyx.Core.csproj
```

In `Onyx/Onyx.csproj` add `<RuntimeIdentifier>win-x64</RuntimeIdentifier>`, `<SelfContained>true</SelfContained>`, `<PublishSingleFile>true</PublishSingleFile>`, `<ApplicationManifest>app.manifest</ApplicationManifest>`.

- [ ] **Step 2: Write the palette**

`Onyx/Theme/Palette.xaml` defines the light palette on `ResourceDictionary` level and a dark dictionary swapped at runtime by `App.xaml.cs` reading `AppsUseLightTheme` from `HKCU\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize`. Values:

Light: `Surface #FFFFFF`, `SurfaceRaised #F5F5F7`, `Separator #E3E3E6`, `TextPrimary #1D1D1F`, `TextSecondary #6E6E73`, `Accent #0071E3`, `AccentPressed #0058B8`.

Dark: `Surface #1C1C1E`, `SurfaceRaised #2C2C2E`, `Separator #3A3A3C`, `TextPrimary #F5F5F7`, `TextSecondary #98989D`, `Accent #0A84FF`, `AccentPressed #0060DF`.

Because the dev machine is Windows 10, do not use `DwmSetWindowAttribute` backdrops. `Surface` is painted solid.

- [ ] **Step 3: Write typography and controls**

`Typography.xaml`: `TitleText` 28px SemiBold, `HeadingText` 17px SemiBold, `BodyText` 13px Regular, `CaptionText` 12px Regular on `TextSecondary`. Font family `Segoe UI Variable Display, Segoe UI`.

`Controls.xaml`: `ControlTemplate`s for the three buttons and the combo box. Every button is a `Border` with `CornerRadius="6"`, a `ContentPresenter` centred by `HorizontalAlignment="Center" VerticalAlignment="Center"` on the presenter — never by margin nudges — and a `RenderTransform` `ScaleTransform` driven to `0.97` on `IsPressed` over 90ms.

Any glyph inside a button is a `Path` inside a `Grid` with both alignments `Center`. Per ecosystem UI rules: no hardcoded pixel nudges, ever.

- [ ] **Step 4: Write the window chrome**

`Window.xaml`: a `Style` for `Window` setting `WindowChrome.WindowChrome` with `CaptionHeight="38"`, `GlassFrame Thickness="0"`, `CornerRadius="0"`, `ResizeBorderThickness="6"`. The template draws a `Border` with `CornerRadius="10"` and `Background="{DynamicResource Surface}"`, a 38px drag bar, and minimise/close buttons on the right using `WindowChrome.IsHitTestVisibleInChrome="True"`.

Per ecosystem UI rules the window has no Cancel affordance, so a close button here is correct.

- [ ] **Step 5: Verify it runs**

Run: `dotnet run --project Onyx`
Expected: a rounded, correctly-toned empty window that drags, minimises and closes, in both light and dark system themes.

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "feat: add wpf shell, palette and window chrome"
```

---

### Task 13: The plugin list

**Files:**
- Create: `Onyx/ViewModels/MainViewModel.cs`, `Onyx/ViewModels/PluginRowViewModel.cs`, `Onyx/ViewModels/ObservableObject.cs`, `Onyx/ViewModels/RelayCommand.cs`
- Create: `Onyx/Views/PluginRow.xaml`, `Onyx/Views/PluginRow.xaml.cs`
- Modify: `Onyx/MainWindow.xaml`

**Interfaces:**
- Consumes: `PluginManager`, `PluginStatus` (Task 11).
- Produces: `MainViewModel.Rows`, `MainViewModel.RefreshCommand`, `MainViewModel.LastChecked`, `PluginRowViewModel.{Name, Summary, HostLabel, InstalledTag, Versions, SelectedVersion, PrimaryActionLabel, IsExpanded, InstallCommand, UninstallCommand, ChooseFolderCommand}`.

- [ ] **Step 1: Write `ObservableObject` and `RelayCommand`**

Minimal hand-rolled `INotifyPropertyChanged` base and an `ICommand` taking `Func<Task>` plus a `canExecute` predicate. No MVVM framework dependency.

- [ ] **Step 2: Write `MainViewModel`**

Holds `ObservableCollection<PluginRowViewModel> Rows`, a `RefreshCommand` calling `PluginManager.StatusAsync`, a `LastChecked` string, and an `IsBusy` flag. A failed refresh sets a muted `StatusMessage` rather than showing a dialog.

- [ ] **Step 3: Write `PluginRow.xaml`**

A `Card`-styled `Border` containing a `Grid` with three columns: a 40px icon column, a star-width text column (`Name` in `HeadingText`, `Summary` in `BodyText`, `HostLabel` in `CaptionText`), and an auto column for the version chip and the primary button. Below, an expansion row bound to `IsExpanded` holding the version `ComboBox`, the release-notes `TextBlock`, and the Uninstall / Reveal / Choose folder buttons.

The expansion animates its `MaxHeight` and `Opacity` over 220ms with a `CubicEase` `EaseOut` — the closest `CubicEase` approximation of the spec's `cubic-bezier(.32,.72,0,1)`.

Every icon is a `Path` inside a `Grid` with `HorizontalAlignment="Center" VerticalAlignment="Center"`; no `Margin` nudges anywhere in this file.

- [ ] **Step 4: Verify it runs**

Run: `dotnet run --project Onyx`
Expected: the real seven rows, real detected hosts (Blender 4.0/4.1/4.3 on this machine), real version dropdowns populated from GitHub. Nothing installed yet.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat: add the plugin list and row view"
```

---

### Task 14: Install, uninstall and the process-guard sheet

**Files:**
- Create: `Onyx/Views/Sheet.xaml`, `Onyx/Views/Sheet.xaml.cs`, `Onyx/ViewModels/SheetViewModel.cs`
- Create: `Onyx/Elevation.cs`, `Onyx/app.manifest`
- Modify: `Onyx/ViewModels/PluginRowViewModel.cs`, `Onyx/App.xaml.cs`

**Interfaces:**
- Consumes: `PluginManager`, `HostRunningException`, `ProcessGuard` (Tasks 8, 11).
- Produces: `Sheet.ShowAsync(title, message, primaryLabel, cancelLabel) -> Task<bool>`, `Elevation.NeedsElevation(InstallPlan) -> bool`, `Elevation.RunElevated(planPath) -> Task<int>`.

- [ ] **Step 1: Write the sheet**

A `Border` overlaid on the window with a scrim, sliding down 220ms. It carries a title, a message, a primary button and a Cancel button — and therefore **no `×` close glyph**, per ecosystem UI rules.

- [ ] **Step 2: Wire the guard**

`PluginRowViewModel.InstallCommand` catches `HostRunningException` and shows the sheet: *"Blender is open. It has to close before the plugin can be installed."* The primary button calls `ProcessGuard.CloseAndWaitAsync` with a 30s timeout and re-runs the install on success. If the wait times out, the sheet's message changes to *"Blender is still open."* and the install does not proceed. Nothing is force-killed.

For `thumbnails`, the primary button reads **Restart Explorer** and calls a `RestartExplorerAsync` helper that unregisters, closes and relaunches `explorer.exe`.

- [ ] **Step 3: Write elevation**

`app.manifest` declares `asInvoker`. `Elevation.NeedsElevation` returns true when any plan operation targets a path under `%ProgramFiles%` or `%ProgramFiles(x86)%`. `RunElevated` serialises the resolved `InstallPlan` to a temp JSON file and starts the same executable with `--apply <path>` and `Verb = "runas"`, awaiting exit. `App.OnStartup` checks for `--apply`, runs `InstallEngine` headlessly against the deserialised plan, and exits with a non-zero code on failure.

- [ ] **Step 4: Verify end to end on this machine**

Run: `dotnet run --project Onyx`

Verify by hand, in order:
1. Install Aventurine into Blender 4.3. Confirm `%APPDATA%\Blender Foundation\Blender\4.3\scripts\addons\Aventurine\__init__.py` exists.
2. With Blender open, attempt an install. Confirm the sheet appears and no files change.
3. Downgrade Aventurine to 3.1.4 from the dropdown. Confirm the version chip updates and the addon folder is replaced.
4. Uninstall. Confirm the addon folder is gone and `state.json` no longer holds the record.
5. Install the thumbnail provider. Confirm `.tex` files show thumbnails in a fresh Explorer window.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat: wire install, uninstall, process guard and elevation"
```

---

### Task 15: Publish, README and repo push

**Files:**
- Create: `README.md`, `.github/workflows/release.yml`
- Modify: `CLAUDE.md` (record anything learned)

- [ ] **Step 1: Verify a self-contained publish**

```bash
dotnet publish Onyx/Onyx.csproj -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o publish
```

Expected: `publish/Onyx.exe` runs on a machine with no .NET runtime.

- [ ] **Step 2: Write the README**

What Onyx is, the seven plugins it manages, a screenshot, and the note that adding a plugin is a `catalog.json` edit. No AI attribution anywhere.

- [ ] **Step 3: Add the release workflow**

A `workflow_dispatch` + tag-triggered job that publishes single-file win-x64 and attaches `Onyx.exe` to the release.

- [ ] **Step 4: Push**

```bash
gh repo create RitoShark/Onyx --public --source . --remote origin --push
```

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "doc: add readme and release workflow"
```

---

## Self-Review

**Spec coverage:** Purpose → Task 11. Plugins table → Task 9 catalog. Stack/projects → Tasks 1, 12. Catalog + source resolution → Tasks 1, 9. Install verbs → Tasks 1, 6. Host detection → Task 4. Choose folder → Task 13 (`ChooseFolderCommand`). Process guard → Tasks 8, 14. Versions/downgrade → Tasks 2, 13. State → Task 7. Applying a plan + rollback → Tasks 5, 6, 10. Elevation → Task 14. Update checking → Tasks 3, 13. UI → Tasks 12, 13, 14. Testing → every task. Repository/distribution → Task 15.

**Known gaps to resolve during execution:**
- ETag caching is specified but not implemented in Task 3. Add it to `GitHubClient` in Task 3 if the 60/hour unauthenticated budget proves tight in practice; six repos per refresh leaves ample headroom, so it is deliberately deferred rather than silently dropped.
- The six-hourly background refresh is specified; wire it in Task 13 as a `DispatcherTimer` on `MainViewModel`.
- `Choose folder…` persistence needs a small settings file; fold it into Task 13 alongside `StateStore`.

**Type consistency:** `HostInstance.InstanceId` is the key everywhere — `PluginEntry.HostInstance`, `InstallPlan.InstanceId`, `InstallRecord.InstanceId`, `PluginManager.InstallAsync(pluginId, instanceId, tag)`. `InstallJournal.Written`/`Registered` match `InstallRecord.Written`/`Registered` so a record reverts directly.
