# Living Server Beta 1.1.9: release notes

The text below the line is the GitHub release notes for `v1.1.9-beta`. In the file table the Setup row must keep
exactly `(filled in when the exe is built)`: the release workflow replaces only that text with the exe's SHA-256. The
zip's SHA-256 is filled in when publishing.

---

### Grey screen after 1.1.8: put the Godot class cache back

1.1.9 is a **DetShaft fork prerelease**. Same Offline DAoC **0.37 / 0.37b / 0.37c** base.

**If 1.1.8 left a grey window:** install 1.1.9 (Setup or the zip). It writes
`new-client\godot\.godot\global_script_class_cache.cfg` back. Do not delete that file.

**Why the window was grey:** 1.1.8 deleted `global_script_class_cache.cfg` (and `uid_cache.bin`) after patching
scripts. The launcher starts the client with `godot --path new-client\godot` and does not run an editor import,
so Godot never rebuilds that cache. Without it every global `class_name` is unknown: `ZoneAudio` in
`pregame_music.gd`, `Accounts` in `login_screen.gd`, and the login scene never loads. Jay's log that still
shows typed `LfgWindow` at `game_ui.gd:812` is a leftover 1.1.6 `game_ui.gd`. After the zone built, `GameUI.new()`
threw `Nonexistent function 'new'` because that script had failed to compile.

**1.1.9:** does not delete the class cache. If it is missing, INSTALL/Setup restore the shipped cache. If it is
present but missing a `class_name`, those entries are appended. The install fails if any `class_name` in the
project is still absent. `zone_viewer.gd` checks that `game_ui.gd` compiled before calling `.new()`. Preload
friends / LFG / commander UI, the version banner (`LivingServer client patch 1.1.9`), and the 1.1.8
hash-replace / post-install checks stay.

**Server:** the 1.1.7 `TryResurrectGroupMember` null-guard stays. No further server change in this build.

Turn on **Allow prereleases** to be offered this build.

### Files

| File | SHA-256 |
|---|---|
| `LivingServer-Setup-1.1.9-beta.exe` | `(filled in when the exe is built)` |
| `OfflineDAoC-LivingServer-1.1.9-beta.zip` | `(zip SHA-256: filled in by hand at publish)` |

### Credits

Built on [shadowofze's Offline DAoC](https://github.com/shadowofze/OfflineDAoC). DetShaft fork packaging. GPL-3.0.
