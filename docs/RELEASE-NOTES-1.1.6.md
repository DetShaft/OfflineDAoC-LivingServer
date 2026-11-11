# Living Server Beta 1.1.6: release notes

The text below the line is the GitHub release notes for `v1.1.6-beta`. In the file table the Setup row must keep
exactly `(filled in when the exe is built)`: the release workflow replaces only that text with the exe's SHA-256. The
zip's SHA-256 is filled in when publishing.

---

### New-client packaging + Active Population / Spec (launcher-offered after 1.1.5)

1.1.6 is a **DetShaft fork prerelease** on top of 1.1.5. Same Offline DAoC **0.37 / 0.37b / 0.37c** base.

Replacing 1.1.5 assets in place would not make the launcher's update check offer the build again
(`IsNewerThan`), so this is a new tag with the same product fixes:

1. **New-client friends/LFG/commander:** the zip ships the Godot `.gd` scripts, matching UI/net edits, and
   `MMOEngine.dll` through `content-patches.json` (Create / ReplaceAny). INSTALL applies them on 0.37c;
   classic installs without `new-client` skip those Create entries instead of growing a half-empty tree.
2. **Active Population grid:** rebind keeps `ListChanged` live so a populated status payload paints rows under
   the default filters (All realms / Spec Any / Online Only). Spec is a real population filter with **Any**
   (Any / Auto / disabled never hide rows) and stays enabled in the population view; Auto (hashed) remains
   for create when a class is chosen. Class also filters the roster when not Any.

Turn on **Allow prereleases** in the launcher (DetShaft feed) to be offered this build from 1.1.4 or 1.1.5.

### Files

| File | SHA-256 |
|---|---|
| `LivingServer-Setup-1.1.6-beta.exe` | `(filled in when the exe is built)` |
| `OfflineDAoC-LivingServer-1.1.6-beta.zip` | `(zip SHA-256: filled in by hand at publish)` |

### Credits

Built on [shadowofze's Offline DAoC](https://github.com/shadowofze/OfflineDAoC). DetShaft fork packaging. GPL-3.0.
