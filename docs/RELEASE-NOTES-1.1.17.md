# Living Server Beta 1.1.17: release notes

The text below the line is the GitHub release notes for `v1.1.17-beta`. In the file table the Setup row must keep
exactly `(filled in when the exe is built)`: the release workflow replaces only that text with the exe's SHA-256. The
zip's SHA-256 is filled in when publishing.

---

### LFG bots travel to you, groups stand by role, quickbars keep their icons

1.1.17 is a **DetShaft fork prerelease**. Same Offline DAoC **0.37 / 0.37b / 0.37c** base. The 1.1.9 class-cache
repair is unchanged: INSTALL/Setup still restore `global_script_class_cache.cfg` and never delete it.
The banner reads `LivingServer client patch 1.1.17`.

**LFG bots come to you.** Inviting a bot from the find-group window when it is far away no longer tells you to meet up
first. A same-realm bot with an LFG ad, not in a group of its own, travels to you the way a friend does (30 seconds,
plus 30 for each zone between you) and joins on arrival. A bot on a duty still says it is busy.

**Bot groups stand by role and rest at camp.** Tanks in front, melee on the sides, archers behind, casters and healers
further back, facing the way the leader faces. Your own helpers stand beside and behind you when you stand still. A bot
party that reaches its camp rests there for 90 seconds, sitting if it is low, then pulls again.

**ADD TARGET always adds the friend.** ADD TARGET on a /spawn helper now always puts the name on your friends list,
even if its gamebot signup fails, and the Friends window updates after the add.

**Helper jewelry.** A helper given new jewelry now swaps out the old piece instead of failing on a full neck slot.

**New client: quickbars and skills panel.** Quickbar buttons remember which skill, spell or item they are, so they keep
the right icon and action after logout, and the bars are saved safely on logout and on a forced close. Switching between
the skills, magic and combat sheets no longer fills the log with "Trying to cast a freed object".

These notes are from unit tests and headless client checks. A feature counts as working in game only after it
shows up in a log from Jay's PC.

### Files

| File | SHA-256 |
|---|---|
| `LivingServer-Setup-1.1.17-beta.exe` | (filled in when the exe is built) |
| `OfflineDAoC-LivingServer-1.1.17-beta.zip` | (zip SHA-256: filled in by hand at publish) |

### Credits

Built on [shadowofze's Offline DAoC](https://github.com/shadowofze/OfflineDAoC). DetShaft fork packaging. GPL-3.0.
