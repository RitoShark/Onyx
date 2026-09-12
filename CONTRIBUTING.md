# Contributing

PRs are welcome. Nothing here is strict except the commit format, which the changelog depends on.

## Getting it running

Needs the .NET 9 SDK and 64-bit Windows.

```powershell
git clone https://github.com/RitoShark/Onyx.git
cd Onyx
dotnet run --project Onyx
```

## Before you push

```powershell
dotnet test
dotnet build Onyx/Onyx.csproj -c Release
```

For a portable build, run
`powershell -NoProfile -ExecutionPolicy Bypass -File scripts/Build-Portable.ps1`.
The executable is written to `artifacts/portable/Onyx.exe`.

## Commits

[Conventional Commits](https://www.conventionalcommits.org), because
[git-cliff](cliff.toml) builds the changelog from them.

```
feat(hosts): detect portable Blender installs
fix(updates): select the latest stable plugin release
```

Types that show up in the changelog: `feat`, `fix`, `perf`, `refactor`, `doc`.
`chore` and `ci` are skipped. Scope is optional.

Keep commits small and focused. One commit per finished piece of work beats one big one at the end.

## Code style

Match the file you are in. Two things are not negotiable:

**No comments.** Zero by default. Naming and structure carry it. The exception is a real landmine,
meaning a non-obvious trap where the next person breaks something without the warning: a format
quirk, an ordering requirement, a platform bug. Keep that to one line. Comments go stale the moment
the code moves and a comment that lies is worse than none.

**Keep installation logic separate from the UI.** Catalog validation, host detection,
release handling, and installation belong in `Onyx.Core`. WPF views, view models,
and theme resources belong in `Onyx`. Add plugins through the catalog rather than
hardcoding plugin-specific behavior into a view model.

## Things worth knowing

- `Onyx.Tests` covers the core; `Onyx.Ui.Tests` covers the Windows view models.
- Keep one row per plugin. Multiple supported host versions are targets inside that row.
- Preserve install journals and graceful host-close checks. Never force-quit an editor
  holding unsaved work. Restarting Explorer to unload a thumbnail handler is a separate case.
- Keep blocking download extraction and file operations off the UI thread, and serialize
  access to installation state.
- The EXE is self-contained, but installation records and caches stay under
  `%LOCALAPPDATA%\RitoShark\Onyx` on each PC.
- Refresh README screenshots with the capture command in [README.md](README.md).
  Use example data rather than personal paths, and do not commit downloaded plugin
  binaries, credentials, or game assets.
