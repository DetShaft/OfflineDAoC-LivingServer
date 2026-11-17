# Living Server Beta 1.1.20: release notes

The text below the line is the GitHub release notes for `v1.1.20-beta`. In the file table the Setup row must keep
exactly `(filled in when the exe is built)`: the release workflow replaces only that text with the exe's SHA-256. The
zip's SHA-256 is filled in when publishing.

---

### Killaloe battleground, Bonedancer commanders, Necromancer servants

1.1.20 is a **DetShaft fork prerelease**. Same Offline DAoC **0.37 / 0.37b / 0.37c** base. The 1.1.9 class-cache
repair is unchanged: INSTALL/Setup still restore `global_script_class_cache.cfg` and never delete it.
The banner reads `LivingServer client patch 1.1.20`.

**Killaloe replaces Abermenai** as the 15-19 battleground in the new client. Group bots now follow you through a keep
door, you arrive at the right height, and the Siegemaster takes a siege token you drop beside them.

**Bonedancer commanders.** The level-40 commanders now load properly, the Bone Magus no longer crashes the client, each
commander has three minions, the Dread Commander wears its cloak, and commanders use a shield and hammer styles.

**Necromancer servants** take weapons, shields and styles, and the Abomination is bigger. The pet dialog lists a pet's
styles.

**Shared realm alts.** A player's characters in one realm share a guild and a friends list.

These notes are from unit tests and headless client checks. A feature counts as working in game only after it
shows up in a log from Jay's PC. The full list is in `CHANGELOG-LIVING-SERVER.md`.

### Files

| File | SHA-256 |
|---|---|
| `LivingServer-Setup-1.1.20-beta.exe` | (filled in when the exe is built) |
| `OfflineDAoC-LivingServer-1.1.20-beta.zip` | (zip SHA-256: filled in by hand at publish) |

### Credits

Built on [shadowofze's Offline DAoC](https://github.com/shadowofze/OfflineDAoC). DetShaft fork packaging. GPL-3.0.
