# Living Server Beta 1.1.26: release notes

The text below the line is the GitHub release notes for `v1.1.26-beta`. In the file table the Setup row must keep
exactly `(filled in when the exe is built)`: the release workflow replaces only that text with the exe's SHA-256. The
zip's SHA-256 is filled in when publishing.

---

### Killaloe leveling and battleground bot life

1.1.26 is a **DetShaft fork prerelease**. Same Offline DAoC **0.37 / 0.37b / 0.37c** base as 1.1.25. The banner reads
`LivingServer client patch 1.1.26`.

**Killaloe XP.** Gamebot kills pay the stock player-kill XP, battleground monster kills give double XP, and Killaloe has
60 more mob camps.

**Battleground bots.** Bots spend 10-16 minute phases grinding camps, fighting at keeps or hunting the outskirts, by level
tier. They form real groups, respond to keep sieges, and hunters jump camps of three or fewer. Log lines to look for:
`KILLALOE_CAMPS`, `BG_ACTIVITY`, `BG_GRIND_CAMP`, `BG_HUNT_AVOID`, `BG_PARTY_FORMED`, `BG_SIEGE_RESPONSE`.

All of this is from unit tests and simulations. A feature counts as working in game only after it shows up in a log from
Jay's PC. The full list is in `CHANGELOG-LIVING-SERVER.md`.

### Files

| File | SHA-256 |
|---|---|
| `LivingServer-Setup-1.1.26-beta.exe` | (filled in when the exe is built) |
| `OfflineDAoC-LivingServer-1.1.26-beta.zip` | (zip SHA-256: filled in by hand at publish) |

### Credits

Built on [shadowofze's Offline DAoC](https://github.com/shadowofze/OfflineDAoC). DetShaft fork packaging. GPL-3.0.
