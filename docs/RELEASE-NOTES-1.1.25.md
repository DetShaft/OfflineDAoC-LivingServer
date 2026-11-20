# Living Server Beta 1.1.25: release notes

The text below the line is the GitHub release notes for `v1.1.25-beta`. In the file table the Setup row must keep
exactly `(filled in when the exe is built)`: the release workflow replaces only that text with the exe's SHA-256. The
zip's SHA-256 is filled in when publishing.

---

### Better group fights, and bots that Need only what suits them

1.1.25 is a **DetShaft fork prerelease**. Same Offline DAoC **0.37 / 0.37b / 0.37c** base as 1.1.25. The banner reads
`LivingServer client patch 1.1.25`.

**Group fight mode.** Pets rank below all players when bots pick targets. Bots walk around a ridge or corner that blocks
their line of sight and remember an enemy they saw for 6 s. Support bots stay on a leash near the group. Cleric bots smite
single targets only.

**Loot.** Bots roll Need only on loot whose bonuses suit their class, and the best class fit wins the roll. Friars want
Strength first, then Piety with Dexterity, Quickness and Constitution.

All of this is from unit tests and simulations. A feature counts as working in game only after it shows up in a log from
Jay's PC. The full list is in `CHANGELOG-LIVING-SERVER.md`.

### Files

| File | SHA-256 |
|---|---|
| `LivingServer-Setup-1.1.25-beta.exe` | (filled in when the exe is built) |
| `OfflineDAoC-LivingServer-1.1.25-beta.zip` | (zip SHA-256: filled in by hand at publish) |

### Credits

Built on [shadowofze's Offline DAoC](https://github.com/shadowofze/OfflineDAoC). DetShaft fork packaging. GPL-3.0.
