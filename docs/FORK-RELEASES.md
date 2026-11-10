# Publishing Living Server releases from the DetShaft fork

This fork’s launcher looks for updates on **`DetShaft/OfflineDAoC-LivingServer`** by default
(`runtime/updates/update-settings.json` → `ReleaseRepository`). Tag and zip naming must match
upstream so `PackageCheck` and the download-prefix trust check accept the package:

| Piece | Shape |
|-------|--------|
| Git tag | `vX.Y.Z-beta` (example: `v1.1.4-beta`) |
| Zip asset | `OfflineDAoC-LivingServer-X.Y.Z-beta.zip` |
| Build stamp | `ForkBuildInfo.BuildName` = `X.Y.Z-beta` (must match the zip / tag) |

Do **not** create a tag or publish a release from this change alone. When Jay is ready:

## 0. New-client scripts (0.37c)

The Godot client loads loose `.gd` files from `new-client\godot` (not a `.pck`). Ship UI/net changes through
`tools/fork-build/content-patches.json` (File or Ops) and payloads under `tools/fork-build/content/`, then
`INSTALL.cmd` / `install-server-build.ps1`. New scripts use `"Create": true`; `MMOEngine.dll` uses `"ReplaceAny": true`.
CI runs `python3 tools/release/check_newclient_packaged.py` so a `game/ui` or `game/net` change without a patch fails.

## 1. Name the release on `main`

1. Merge this work (and any other approved PRs) to `main`.
2. Rename the changelog’s `## Beta X (in development)` heading to `## Beta X — <date>`.
3. Set `source/server/GameServer/gameutils/ForkBuildInfo.BuildName` to `X-beta`.
4. Point `README.md` at the new Beta (sentence, release links, zip/Setup file names). Links may use
   `DetShaft/…` once the release exists; until then `check_versions.py` still accepts upstream
   `chris4aker/…` release URLs.
5. Confirm locally: `python tools/release/check_versions.py` (exit 0).

## 2. Build package on Actions

1. Push the versioned `main` commit.
2. Wait for **`build-and-test`** on that push to finish green.
3. Note the run id and the package artifact name  
   `OfflineDAoC-LivingServer-<build>-<7-char-sha>`.

## 3. Publish (creates the tag — Jay only)

**Preferred (same as upstream):** Actions → **publish-release** → Run workflow:

- `tag`: `vX.Y.Z-beta`
- `run_id`: the green `build-and-test` push run on `main`
- `title`: e.g. `Beta X.Y.Z`
- start with **`dry_run: true`**, read the notes, then re-run with `dry_run: false`

That workflow zips the artifact as `OfflineDAoC-LivingServer-<build>.zip`, fills the SHA-256 into
the release notes, creates the tag on the run’s commit, and publishes the GitHub release.

**Manual alternative:** build with `tools/fork-build`, zip to the same name, upload on GitHub
**Create release** with the same tag. The zip must pass the same `PackageCheck` layout
(`BUILD-INFO.txt`, `GameServer.dll`, `INSTALL.cmd`, …).

## 4. Optional Setup.exe

After the zip release exists, run Actions → **release** with the tag (dry run first). It builds
`LivingServer-Setup-<build>.exe` from this fork’s package.

## 5. Launcher settings Jay can use after the first DetShaft release

In the launcher title strip (or edit `runtime/updates/update-settings.json`):

| Setting | Default | Meaning |
|---------|---------|---------|
| `ReleaseRepository` | `DetShaft/OfflineDAoC-LivingServer` | GitHub owner/repo for `/releases` |
| `CheckAutomatically` | `true` | Quiet check at most once a day |
| `InstallAutomatically` | `false` | If check finds a newer build and nothing is running, install without the dialog |
| `AllowPrereleases` | `false` | Also offer GitHub prereleases (uses `/releases` instead of `/latest`) |

Install still **never** runs while the server, game client, Godot client, or another launcher is
running. Rollback remains the package’s `ROLLBACK.cmd` / deployment backups.

## 6. Recovery if an update fails

1. Previous zips are kept under `runtime/updates/` (newest two).
2. Package folder `ROLLBACK.cmd` restores replaced binaries (not the live DB).
3. Or reinstall from the zip / Setup manually into the playable folder with the server stopped.
