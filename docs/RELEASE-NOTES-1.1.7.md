# Living Server Beta 1.1.7: release notes

The text below the line is the GitHub release notes for `v1.1.7-beta`. In the file table the Setup row must keep
exactly `(filled in when the exe is built)`: the release workflow replaces only that text with the exe's SHA-256. The
zip's SHA-256 is filled in when publishing.

---

### New-client loading hang + healer resurrection NRE

1.1.7 is a **DetShaft fork prerelease** on top of 1.1.6. Same Offline DAoC **0.37 / 0.37b / 0.37c** base.

**Hang after 1.1.6 (new client):** INSTALL replaced `game_ui.gd` with a Living Server copy that type-annotated
`FriendsWindow`, `LfgWindow` and `CommanderPanel`. Those scripts are Create-added on disk, but a packaged 0.37c
`global_script_class_cache.cfg` does not list them, so GameUI failed to compile during zone load and the
"Entering Offline DAoC" loading screen never finished (server WorldInit still completed). 1.1.7 loads those
windows through `preload("res://…")` by path instead of global class types. CI runs a headless Godot 4.7 check
that strips those classes from the cache and still requires every shipped content `.gd` to load.

Older Godot logs also show many `Trying to cast a freed object` lines from stock `server_world.gd` NPC view casts (`as Npc` on recycled/freed views). Those are not introduced by the Create friends/LFG/commander scripts (`server_world.gd` / `keybinds.gd` have no class_name type annotations for them).

**Server:** `BotBrain.TryResurrectGroupMember` null-guards `Body.Group` before `GetMembersInTheGroup` (ungrouped
healers and mid-check disbands were NRE'ing from CheckHeals / BotState_Aggro).

Turn on **Allow prereleases** in the launcher (DetShaft feed) to be offered this build from 1.1.4–1.1.6.

### Files

| File | SHA-256 |
|---|---|
| `LivingServer-Setup-1.1.7-beta.exe` | `(filled in when the exe is built)` |
| `OfflineDAoC-LivingServer-1.1.7-beta.zip` | `(zip SHA-256: filled in by hand at publish)` |

### Credits

Built on [shadowofze's Offline DAoC](https://github.com/shadowofze/OfflineDAoC). DetShaft fork packaging. GPL-3.0.
