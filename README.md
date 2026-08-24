# Skyrim Version Manager

A standalone Windows app that manages which version of **Skyrim Special Edition (Steam)** is
installed — e.g. downgrading the August 2026 update **1.7.99** back to the mod-stable
**1.6.1170** so SKSE-based mods keep working.

No installation: it's a single `SkyrimVersionManager.exe`. All of its data (downloaded version
files, backups, settings, tools) lives in a `data` folder next to the exe, so deleting the folder
removes everything.

## What it does

- **Auto-detects** your Skyrim install by reading the Steam registry entry and every Steam
  library in `libraryfolders.vdf` (manual override via Browse).
- **Detects the installed version** on launch from `SkyrimSE.exe`'s version resource and compares
  it to your previously selected desired version, telling you whether they match and offering a
  one-click switch when they don't.
- **Version dropdown** covering every known Steam release: 1.5.97, 1.6.317 through 1.6.1170, and
  the current 1.7.99.
- **Downloads exact old versions from Steam itself** using [DepotDownloader](https://github.com/SteamRE/DepotDownloader)
  (SteamRE, MIT license) with the community-verified depot manifest IDs. Downloads are **cached
  locally** in `data\cache`, so switching back and forth never re-downloads.
- **Scope choice**: *Executables only* (default — a small download of depot 489833, which is what
  SKSE compatibility actually depends on; this is the community's "Best of Both Worlds" approach)
  or *Full game* (~12 GB, an exact clean copy of the selected version).
- **Safety features** (all on by default, each toggleable):
  - *Backup current files first* — files about to be replaced are saved to `data\backups\<version>`,
    so returning to the current version later is instant and offline.
  - *Lock Steam updates* — sets `appmanifest_489830.acf` to "update only on launch" and marks it
    read-only so Steam can't silently reinstall the newest patch. An **Unlock** button reverses it.
  - *SKSE check* — warns when the installed `skse64_loader.exe` doesn't match the SKSE version the
    selected game version expects.

## Steam access (two methods, no mobile app required)

Old depot versions can only be downloaded by an account that owns the game. Pick either method
under **Steam access**:

- **Desktop Steam client** (default, no password ever): the app opens Steam's built-in console
  (`steam://open/console`) in your already-logged-in desktop client and puts the
  `download_depot` command(s) in your clipboard. You paste, press Enter, and the app watches
  Steam's download folder, then caches and applies the files automatically when Steam finishes.
- **Steam account login**: enter your username and password in the app; they are handed straight
  to DepotDownloader → Steam's official auth servers and are never written to disk. If Steam
  Guard asks for a code (email or authenticator), the app prompts for it. After the first login
  a Steam token is remembered, so the password is a one-time entry.

## Building

```powershell
.\build.ps1
```

Requires the .NET 8 SDK. Output: `publish\SkyrimVersionManager.exe` (self-contained single
file) plus a clean distribution zip in `release\` containing only the exe, README, and LICENSE.

## Releasing

Upload **only** the zip from `release\` (the build script verifies its contents and fails if
anything else slips in). Never distribute anything from `publish\data` — it holds your personal
settings, stashed saves, and cached game depots, which are Bethesda's copyrighted files.
`NEXUS_DESCRIPTION.md` contains a ready-to-paste Nexus page draft and a per-release checklist.

## After switching versions

- Launch the game via `skse64_loader.exe` (or your mod manager), **not** the Steam Play button —
  the Play button can trigger the update Steam has queued.
- Make sure your SKSE build matches the game version (`1.6.1170` needs SKSE 2.2.6, from
  [skse.silverlock.org](https://skse.silverlock.org/)).

## Notes / limitations

- Version 1.7.99 has no pinned manifests in `versions.json` (Steam always serves the newest
  build for it). Returning to it uses your local backup when one exists.
- A *full game* downgrade covers the three main depots (489831/489832/489833). Anniversary
  Edition creation-club content downloaded in-game is not touched.
- `versions.json` can be copied into the `data` folder and edited to add future versions (new
  manifest IDs are published on SteamDB and the community
  [Steam Manifest List](https://www.nexusmods.com/skyrimspecialedition/articles/6536)).
- Everything shown in the in-app log is also written to `data\log.txt` — attach it when
  reporting a problem.
- In the account-login mode, the password is passed to DepotDownloader on its command line
  (that is the tool's interface), which is briefly visible to other processes running as you on
  your own PC. The default desktop-Steam-client mode involves no credentials at all.
- After an *executables only* switch the install is a hybrid (old exe, new data files). A later
  backup is labeled with the exe's version, so its data files may be newer than the label
  suggests. Backups also record their scope: an executables-only backup is never used to
  "restore" a full-game version.

## License

MIT — see [LICENSE](LICENSE). Uses [DepotDownloader](https://github.com/SteamRE/DepotDownloader)
(SteamRE, MIT), downloaded from its official GitHub releases at runtime.
