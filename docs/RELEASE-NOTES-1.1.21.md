# Living Server Beta 1.1.21: release notes

The text below the line is the GitHub release notes for `v1.1.21-beta`. In the file table the Setup row must keep
exactly `(filled in when the exe is built)`: the release workflow replaces only that text with the exe's SHA-256. The
zip's SHA-256 is filled in when publishing.

---

### Smarter group bots, a /bug command, a cleaner combat log

1.1.21 is a **DetShaft fork prerelease**. Same Offline DAoC **0.37 / 0.37b / 0.37c** base. The 1.1.9 class-cache
repair is unchanged: INSTALL/Setup still restore `global_script_class_cache.cfg` and never delete it.
The banner reads `LivingServer client patch 1.1.21`.

**Group bots in a fight.** Bots in your group now pass a keep door when you do, sprint beside you and when they chase,
and healers hold a post closer to you. Melee classes keep swinging through instant spells, a grouped Cleric smites, and
casters back away from a player who rushes them while pets and melee bots step in.

**New client.** `/bug` opens a prefilled GitHub issue in your browser. Target Closest Enemy now also finds enemy-realm
guards and players. Options > CHAT has a switch for helpers' damage numbers in the combat log.

**Killaloe main keep.** Its collision line was turned 90 degrees from the drawn keep, which could block the doorway.
The converter is fixed, but the keep's model files are converted game content and are not part of this package.
A game gets the corrected keep only after its zone files are converted again.

These notes are from unit tests and headless client checks. A feature counts as working in game only after it
shows up in a log from Jay's PC. The full list is in `CHANGELOG-LIVING-SERVER.md`.

### Files

| File | SHA-256 |
|---|---|
| `LivingServer-Setup-1.1.21-beta.exe` | (filled in when the exe is built) |
| `OfflineDAoC-LivingServer-1.1.21-beta.zip` | (zip SHA-256: filled in by hand at publish) |

### Credits

Built on [shadowofze's Offline DAoC](https://github.com/shadowofze/OfflineDAoC). DetShaft fork packaging. GPL-3.0.
