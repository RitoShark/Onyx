# Onyx — RitoShark plugin manager

Design document. 2026-08-27.

## Purpose

Onyx installs, updates, downgrades and removes the RitoShark plugins on a user's
machine. Today each plugin ships its own installer, PowerShell script or manual
copy instructions, and a user who wants the Photoshop plugin, the GIMP plugin and
Explorer thumbnails has to follow three unrelated procedures and re-follow them on
every release. Onyx replaces all of that with one window.

Success criteria:

- A user with no prior setup installs any plugin in two clicks.
- The app finds host applications wherever they are installed, including
  non-default drives and portable copies.
- Any past release can be installed, not only the latest.
- The app never corrupts a host application by writing files it has open.
- A new plugin can be added to the catalog without shipping a new Onyx build.

Out of scope: League of Legends detection, patch-compatibility logic, anything
touching game files, and Onyx updating itself — the first version is a portable
executable the user replaces by hand.

## Plugins

| Plugin | Repo | Release asset | Install target |
|---|---|---|---|
| RitoTex for Photoshop | `RitoShark/RitoTex-Photoshop` | `RitoTex.8bi` | `<Photoshop>\Plug-ins\` |
| Paint.NET .tex | `RitoShark/Paint.NET-Tex-Plugin` | `TexFileType-*.zip` | `<Paint.NET>\FileTypes\` |
| GIMP 2 .tex | `RitoShark/Gimp-Tex-Plugin` | `GIMP2_TEX_Plugin_Windows.zip` | `%APPDATA%\GIMP\2.10\plug-ins\` |
| GIMP 3 .tex | `RitoShark/Gimp-Tex-Plugin` | `GIMP3_TEX_Plugin_Windows.zip` | `%APPDATA%\GIMP\3.0\plug-ins\` |
| RitoShark Maya | `RitoShark/RitoShark-Maya` | `RitoShark-Maya-*.zip` | `Documents\maya\<year>\` |
| .tex Explorer thumbnails | `RitoShark/TexThumbnailProvider` | `TexThumbnailProvider.dll`, `.sha256` | `%LOCALAPPDATA%\RitoShark\TexThumbnailProvider\` + regsvr32 |

There is no Blender plugin repository yet. Onyx ships no Blender row and no
disabled placeholder; the row appears when the catalog gains an entry.

## Stack

.NET 9 WPF, `win-x64`, published self-contained and single-file so users need no
runtime. WPF rather than Tauri or WinUI 3 is a deliberate choice by the owner: it
compiles with the SDK already installed, needs no workload, and gives full control
over control templates.

The visual target is macOS System Settings. WPF's default look is not that, so
buttons, list rows, dropdowns, toggles and window chrome are all custom
`ControlTemplate`s. This is the largest single piece of work in the project and is
budgeted as such.

### Projects

```
Onyx.Core/     net9.0          catalog, GitHub client, host detection,
                               install engine, state store, process guard
Onyx/          net9.0-windows  WPF app: views, view models, theme
Onyx.Tests/    net9.0          xunit over Onyx.Core
```

`Onyx.Core` has no WPF reference and no UI concepts. Everything with a decision in
it lives there and is tested; the WPF project is views, view models and theme.

## Catalog

A plugin is data, not code. `catalog.json` describes every plugin:

```json
{
  "schema": 1,
  "revision": 1,
  "plugins": [
    {
      "id": "ritotex-photoshop",
      "name": "RitoTex for Photoshop",
      "summary": "Open and save .tex textures directly in Photoshop.",
      "repo": "RitoShark/RitoTex-Photoshop",
      "host": "photoshop",
      "asset": "RitoTex.8bi",
      "steps": [
        { "verb": "copy", "from": "RitoTex.8bi", "to": "{host}/Plug-ins/RitoTex.8bi" }
      ]
    }
  ]
}
```

`asset` is a glob so version-stamped names (`TexFileType-3.0.0.zip`) match without
a catalog edit per release. `{host}` resolves to the detected (or user-chosen)
host path for that install.

### Where the catalog comes from

1. An embedded copy compiled into the binary — the app always works offline and on
   first run.
2. On launch, a fetch of `catalog.json` from the Onyx repo's raw GitHub URL,
   cached at `%LOCALAPPDATA%\RitoShark\Onyx\catalog.json`.
3. The newest of (cached, embedded) by `revision` wins, provided its `schema` is
   one this build understands; a malformed or newer-schema fetch is discarded and
   logged, never applied.

This is what lets a Blender plugin — or any future plugin — appear for existing
users through one JSON commit.

### Install verbs

The engine understands exactly four verbs. This covers all six rows above and is
deliberately not extensible into a scripting language.

| Verb | Meaning |
|---|---|
| `copy` | one file from the downloaded (or extracted) payload to a target path |
| `copyDir` | a directory tree merged into a target directory |
| `regsvr32` | register a DLL; the inverse (`/u`) runs on uninstall |
| `sha256` | verify the payload against a sidecar asset before anything is written |

An unknown verb fails the whole plan before any file is touched, so an Onyx build
never half-applies a catalog written for a newer schema.

## Host detection

Detection reads the registry rather than probing well-known folders, so custom
install drives are found rather than guessed.

| Host | Source |
|---|---|
| Photoshop | `HKLM\SOFTWARE\Adobe\Photoshop\<ver>\ApplicationPath`, and the WOW6432Node mirror. Each installed year is a separate install target. |
| Paint.NET | The uninstall key's `TARGETDIR`; additionally the Microsoft Store build's per-user `Documents\paint.net App Files\`. |
| GIMP 2 / 3 | The per-user plug-ins directory under `%APPDATA%\GIMP\<ver>\`, which exists independent of where the program is installed. |
| Maya | `HKLM\SOFTWARE\Autodesk\Maya\<year>\Setup\InstallPath` for the set of installed years; files go to `Documents\maya\<year>\`, matching the existing `install.py`. |
| Thumbnail provider | No host. Fixed per-user path. |

A host can resolve to several instances (Photoshop 2023 and 2025; Maya 2024 and
2026). Each instance is its own row state: installed version, files written,
update availability.

Every host row also offers **Choose folder…**, persisted per instance, for
portable installs and anything detection misses. A user-chosen path is validated —
the expected subdirectory must exist — before it is accepted.

## Process guard

Writing a plugin into a host that is running risks a locked file at best and a
confused host at worst. Before any install, downgrade or uninstall, Onyx checks
for the host's processes.

If any are running, the action stops and a sheet explains which application must
close. **Close Photoshop** issues a graceful main-window close and polls until the
process exits, with a visible countdown and a cancel. Onyx never force-kills a
process — an unsaved document is the user's, not ours.

The thumbnail provider is a special case: Explorer loads the DLL, so the file is
locked while Explorer runs. That row offers **Restart Explorer**, which
unregisters, restarts `explorer.exe`, then applies.

## Versions

Each row's dropdown lists every release for its repo: tag, publish date, and a
pre-release badge. Selecting any entry installs that exact tag, so a downgrade is
the same code path as an upgrade — there is no separate rollback mechanism.

Release notes for the selected version render below the row as plain text
(GitHub's markdown body, stripped rather than rendered — an HTML renderer is not
worth the dependency here).

## State

`%LOCALAPPDATA%\RitoShark\Onyx\state.json`, keyed by `(pluginId, hostInstanceId)`:

- the installed version tag
- the exact list of absolute paths written, in write order
- registry/regsvr32 actions performed, for inversion
- install timestamp

Uninstall replays that list in reverse; it never deletes by pattern or by guessing
what a plugin's files are called. "Update available" is computed from the recorded
tag against the newest release, never from a file's timestamp or its own version
resource.

## Applying a plan

1. Resolve the plan: host instance, asset, target paths, verbs. Fail here on
   anything unknown, before touching disk.
2. Download the asset to a temp directory. Verify `sha256` if the catalog declares
   it.
3. Extract, if the asset is an archive.
4. Apply the steps, appending each written path to a journal as it lands.
5. On success, commit the journal into `state.json`.
6. On any failure, roll back from the journal — delete files written, restore any
   file that was overwritten. Originals are moved aside rather than overwritten in
   place, so a rollback has something to restore from.

### Elevation

Onyx runs unelevated. Per-user targets (GIMP, Maya, thumbnails, Store Paint.NET)
never prompt. Program Files targets (Photoshop's `Plug-ins`, classic Paint.NET's
`FileTypes`) do: the app writes the resolved plan to a temp file and relaunches
itself with `--apply <planfile>`, elevated, for that one job. One UAC prompt, only
for the jobs that genuinely need it, and the elevated instance runs the same
engine rather than a second code path.

## Update checking

On launch, on manual refresh, and every six hours while the window is open. The
GitHub Releases API is queried unauthenticated — 60 requests/hour is ample for
five repos — with ETag caching so repeat checks are usually a `304`.

Rate-limited or offline: the app shows the last known state with a muted "couldn't
check — last checked <time>" line. It never blocks the UI on the network and never
shows an error dialog for a failed background check.

## User interface

One window, one column, closest in feel to macOS System Settings → Software Update.

- Custom chrome via `WindowChrome`: rounded corners, thin drag bar, minimise and
  close at the right (Windows placement, Apple styling).
- Header: large display-weight title, a "Check for Updates" pill, and the
  last-checked timestamp beneath it.
- One row per plugin: icon, name, one-line summary, detected host beneath it
  (`Photoshop 2025 · C:\Program Files\Adobe\…`, or a muted "Not installed"), and
  on the right a version chip plus the primary action — Install, Update, or
  Installed with a disclosure.
- Expanding a row reveals the version dropdown, release notes, Uninstall, Reveal
  in Explorer, and Choose folder…
- A plugin with several host instances expands into a sub-row per instance, each
  with its own state and actions.
- Motion: 220 ms, `cubic-bezier(.32,.72,0,1)`, on row expansion and sheet entry.
- Light and dark follow the Windows system setting.

The machine this is developed on runs Windows 10 19045, where Mica and Acrylic
backdrops are unavailable. The design uses solid, correctly-toned surfaces and
does not depend on a Win11-only effect; a backdrop may be layered on later for
Win11 without the Win10 appearance regressing.

## Testing

xunit over `Onyx.Core`:

- catalog parsing, including an unknown verb and an unknown schema being rejected
- asset glob matching against real release asset names
- version ordering and update-availability, including pre-releases
- plan generation against an in-memory filesystem, per plugin
- rollback: a step that fails mid-plan leaves the filesystem as it started
- state round-trip and uninstall inversion
- GitHub API parsing from recorded JSON fixtures, including a rate-limit response

No UI tests. Host detection is tested through a registry-reader interface with
recorded key data; the concrete registry reader is thin enough to verify by hand.

## Repository

`E:\RitoShark\Tools\Onyx`, its own git repo, origin `RitoShark/Onyx`, branch
`main`. `CLAUDE.md` is gitignored per ecosystem rule. Conventional Commits, so
git-cliff can build a changelog as it does for Flint and Quartz.

Distribution: a self-contained single-file `Onyx.exe` attached to a GitHub
release. An installer is not part of the first version — the app that installs
things should not itself need an installer.
