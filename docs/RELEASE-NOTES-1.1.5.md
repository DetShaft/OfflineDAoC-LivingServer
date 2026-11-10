# Living Server Beta 1.1.5: release notes

The text below the line is the GitHub release notes for `v1.1.5-beta`. In the file table the Setup row must keep
exactly `(filled in when the exe is built)`: the release workflow replaces only that text with the exe's SHA-256. The
zip's SHA-256 is filled in when publishing.

---

### New-client friends, LFG and commander actually install on 0.37c

1.1.5 is a **DetShaft fork prerelease** on top of 1.1.4. Same Offline DAoC **0.37 / 0.37b / 0.37c** base.

**Bug in 1.1.4:** the zip had no `new-client` Godot scripts, so Shift+F and Shift+L did nothing on 0.37c even though
the server and launcher were updated. This build ships the friends, LFG and commander `.gd` scripts, the matching
`game_ui` / `keybinds` / `server_world` edits, and a rebuilt `MMOEngine.dll` (FindGroup packets) through
`content-patches.json`. INSTALL creates or replaces those files; ROLLBACK restores or removes them.

Turn on **Allow prereleases** in the launcher (DetShaft feed) to be offered this build from 1.1.4.

### Files

| File | SHA-256 |
|---|---|
| `LivingServer-Setup-1.1.5-beta.exe` | `(filled in when the exe is built)` |
| `OfflineDAoC-LivingServer-1.1.5-beta.zip` | `(zip SHA-256: filled in by hand at publish)` |

### Credits

Built on [shadowofze's Offline DAoC](https://github.com/shadowofze/OfflineDAoC). DetShaft fork packaging. GPL-3.0.
