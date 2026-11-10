# Living Server Beta 1.1.5: release notes

The text below the line is the GitHub release notes for `v1.1.5-beta`. In the file table the Setup row must keep
exactly `(filled in when the exe is built)`: the release workflow replaces only that text with the exe's SHA-256. The
zip's SHA-256 is filled in when publishing.

---

### New-client friends/LFG/commander install + Active Population grid fix

1.1.5 is a **DetShaft fork prerelease** on top of 1.1.4. Same Offline DAoC **0.37 / 0.37b / 0.37c** base.

**Bug in 1.1.4 (new client):** the zip had no `new-client` Godot scripts, so Shift+F and Shift+L did nothing on
0.37c even though the server and launcher were updated. This build ships the friends, LFG and commander `.gd`
scripts, the matching `game_ui` / `keybinds` / `server_world` edits, and a rebuilt `MMOEngine.dll` (FindGroup
packets) through `content-patches.json`. INSTALL creates or replaces those files; ROLLBACK restores or removes them.

**Bug in 1.1.4 (launcher):** Active Population could show an empty grid while the header still listed online bots
(for example 66 online, realm cards populated, Spec stuck on Auto / Online Only). Two fixes ship together:

1. Population rebind muted `ListChanged` while `CurrencyManager` was suspended, so the grid never painted rows.
2. Spec was the create-time picker, stayed **disabled** on "Auto (hashed)" when Class was Any, and looked like a
   stuck filter. Spec is now a real population filter with an **Any** option (Any / Auto / disabled never hide
   rows); it stays enabled in the population view, and Auto (hashed) remains available for create when a class
   is chosen. Class also filters the roster when not Any.

1.1.5 keeps `ListChanged` live and resets bindings after resume so a populated status payload shows rows under
the default filters; Active Groups, Active RvR and Realm Exchange still bind from the same snapshot.

Turn on **Allow prereleases** in the launcher (DetShaft feed) to be offered this build from 1.1.4.

### Files

| File | SHA-256 |
|---|---|
| `LivingServer-Setup-1.1.5-beta.exe` | `(filled in when the exe is built)` |
| `OfflineDAoC-LivingServer-1.1.5-beta.zip` | `(zip SHA-256: filled in by hand at publish)` |

### Credits

Built on [shadowofze's Offline DAoC](https://github.com/shadowofze/OfflineDAoC). DetShaft fork packaging. GPL-3.0.
