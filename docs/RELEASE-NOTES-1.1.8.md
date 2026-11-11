# Living Server Beta 1.1.8: release notes

The text below the line is the GitHub release notes for `v1.1.8-beta`. In the file table the Setup row must keep
exactly `(filled in when the exe is built)`: the release workflow replaces only that text with the exe's SHA-256. The
zip's SHA-256 is filled in when publishing.

---

### Safe new-client rollback + install cache refresh

1.1.8 is a **DetShaft fork prerelease**. Same Offline DAoC **0.37 / 0.37b / 0.37c** base.

**Why 1.1.7 still hung (confirmed on Jay's 20:41 run):** not a second folder and not a `.gdc` cache. INSTALL
and the launcher both use the same game-root `new-client\godot` (launcher `--path` is
`new-client\runtime\..\godot`). Content patches only replace a file when it still has the *Released* (stock
0.37c) hash, unless the entry sets `ReplaceAny`. After 1.1.6, `game_ui.gd` was already a Living Server Fixed
hash, so 1.1.7 left it alone (`not the released file, left alone`) while still updating the server/launcher —
apply-update only checked those DLLs. Jay's Godot log still showed parse errors at the old typed lines
(`LfgWindow` at :812, etc.): that is the 1.1.6 `game_ui.gd`, not the published 1.1.7 preload file (where
:808–818 is siege/guild code). Server WorldInit completed because the hang is client-side.

**1.1.8 client:** ships the **1.1.7 preload-based** friends / LFG / commander UI (`const FriendsWindow :=
preload(...)` etc.), not stock 0.37c. `game_ui.gd` prints `LivingServer client patch 1.1.8
(social_windows=...)` at startup so godot logs show which script loaded. **Off switch:** in
`user://settings.cfg` (under `new-client/save`) set `[living_server] social_windows=false` — windows stay
unwired without rolling scripts back (default `true`). INSTALL `ReplaceAny` / always-replace for Living
Server `new-client/godot/game/**.gd` File payloads, clears `global_script_class_cache.cfg` /
`uid_cache.bin` / loose `.gdc`, and apply-update verifies those Fixed hashes before success.

**Server:** keeps the `TryResurrectGroupMember` null-guard from 1.1.7.

**panel_sheets "cast a freed object":** those lines come from stock `panel_sheets.gd` skill-list refresh lambdas
during world entry, not from the Create friends/LFG scripts. Not treated as the hang root cause.

Turn on **Allow prereleases** to be offered this build.

### Files

| File | SHA-256 |
|---|---|
| `LivingServer-Setup-1.1.8-beta.exe` | `(filled in when the exe is built)` |
| `OfflineDAoC-LivingServer-1.1.8-beta.zip` | `(zip SHA-256: filled in by hand at publish)` |

### Credits

Built on [shadowofze's Offline DAoC](https://github.com/shadowofze/OfflineDAoC). DetShaft fork packaging. GPL-3.0.
