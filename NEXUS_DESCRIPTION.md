# Nexus mod page — draft description

Copy the BBCode block below into the Nexus description editor. The source link points at
https://github.com/wwasplin/SkyrimVersionManager. The VirusTotal link must point at a scan of the
exact uploaded zip — redo it for every release (v1.0.0: 0/67 detections).

Suggested category: **Utilities**. Upload only `release\SkyrimVersionManager-vX.Y.Z.zip`
produced by `build.ps1` — never anything from `publish\data`.

---

```
[size=4][b]Skyrim Version Manager[/b][/size]

A standalone tool that switches your Steam install of Skyrim Special Edition between game
versions — e.g. downgrading the August 2026 updates (1.7.99 / 1.7.104) back to the mod-stable 1.6.1170 so
SKSE-based mods keep working. One exe, no installation; delete its data folder and it's gone.

[size=3][b]What it does[/b][/size]
[list]
[*]Auto-detects your Skyrim install and its exact version, and tells you whether it matches the version you want.
[*]One-click switch between every known Steam release: 1.5.97, 1.6.317 through 1.6.1170, 1.7.99, and the current 1.7.104.
[*]Downloads exact old versions [b]from Steam itself[/b] using your own Steam account's ownership of the game. [b]No game files are distributed with this tool.[/b]
[*]Caches downloads locally, so switching back and forth never re-downloads.
[*]Backs up the files it replaces, locks Steam updates so the downgrade sticks, warns on SKSE mismatches, and keeps each version's save games separate so a newer save is never loaded by an older exe.
[*]Pre-flight checks catch the dangerous cases before anything is touched (a fresh install Steam hasn't finished setting up, 1.71-header plugins on old executables, era changes that break DLL mods, stranded saves).
[/list]

[size=3][b]Before first use[/b][/size]
After a [b]fresh install[/b], launch Skyrim once from Steam (reach the main menu, quit) before
using this tool. Steam finishes the install on that first Play - install script, registry entry,
Anniversary Edition creations download - and switching versions before that can leave Steam with
a half-registered install it tries to repair. The tool detects this and warns you.

[size=3][b]Steam access — no password needed[/b][/size]
By default the tool uses your [b]already-logged-in desktop Steam client[/b]: it opens Steam's
built-in console, gives you the download command to paste, and picks up the files when Steam
finishes. [b]It never sees or asks for your password in this mode.[/b]

An optional second mode drives [url=https://github.com/SteamRE/DepotDownloader]DepotDownloader[/url]
(the well-known open-source SteamRE tool, MIT licensed) with your Steam login. Credentials are
passed straight to Steam's official auth servers and are never written to disk; after the first
login a Steam token is remembered so the password is a one-time entry. If you're not comfortable
with that, simply stay on the default mode.

[size=3][b]Transparency[/b][/size]
[list]
[*]Full source code: [url=https://github.com/wwasplin/SkyrimVersionManager]GitHub[/url] (MIT license)
[*]VirusTotal scan of this exact upload: [url=https://www.virustotal.com/gui/file/6f774747d19f77df77e971ffbf76df35c875611961e2dab7d8ff1e71fbb4cb83]VirusTotal[/url]
[*]The exe is an unsigned self-contained .NET app, which SmartScreen may warn about on first run ("More info" → "Run anyway"). This is normal for unsigned indie tools; the source and scan above are the proof of what it does.
[*]This tool was made with AI assistance (Claude by Anthropic). The full source is on GitHub for anyone to review.
[/list]

[size=3][b]What it touches on your PC[/b][/size]
[list]
[*]Your Skyrim Special Edition folder (the files being switched, backed up first by default)
[*]The Steam appmanifest for Skyrim (to set "update only on launch" and mark it read-only — reversible via the Unlock button)
[*]Documents\My Games\Skyrim Special Edition\Saves (only if "Manage saves with game version" is on; saves are moved, never deleted)
[*]A "data" folder next to the exe (settings, download cache, backups, log) — delete it to remove everything
[/list]

[size=3][b]After switching[/b][/size]
Launch via skse64_loader.exe or your mod manager, [b]not[/b] the Steam Play button, and make
sure your SKSE build matches the game version (the tool warns you if it doesn't).

[size=3][b]Credits[/b][/size]
[list]
[*][url=https://github.com/SteamRE/DepotDownloader]DepotDownloader[/url] by the SteamRE team (MIT) — downloaded from its official GitHub releases at runtime, not bundled
[*]The community-maintained [url=https://www.nexusmods.com/skyrimspecialedition/articles/6536]Steam Manifest List[/url] for the depot manifest IDs
[/list]
```

---

## Permissions section (Nexus form)

- **Source**: open, link the GitHub repo. License: MIT.
- **Distribution**: allow with credit (matches MIT); no asset extraction concerns — the tool
  contains no game assets.

## Release checklist (every upload)

1. Run `.\build.ps1` — it builds and produces `release\SkyrimVersionManager-vX.Y.Z.zip`
   containing only the exe, README, and LICENSE (the script fails if anything else slips in).
2. Bump `<Version>` in `SkyrimVersionManager.csproj` first; the zip name and title bar follow it.
3. Upload the zip to VirusTotal, update the link in the description.
4. Tag the matching commit on GitHub so the source for each release is identifiable.
