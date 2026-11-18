# Living Server Beta 1.1.22: release notes

The text below the line is the GitHub release notes for `v1.1.22-beta`. In the file table the Setup row must keep
exactly `(filled in when the exe is built)`: the release workflow replaces only that text with the exe's SHA-256. The
zip's SHA-256 is filled in when publishing.

---

### Bots stay put-together after keep doors and keep their shields

1.1.22 is a **DetShaft fork prerelease**. Same Offline DAoC **0.37 / 0.37b / 0.37c** base. The 1.1.9 class-cache
repair is unchanged: INSTALL/Setup still restore `global_script_class_cache.cfg` and never delete it.
The banner reads `LivingServer client patch 1.1.22`.

**Keep doors.** A grouped bot that passed a keep door used to stand still on the far side until it was attacked or
invited again. Bots now keep following you through the door and out again.

**Shields.** Bots that can't dual wield (Minstrels, Clerics and every other shield class) keep their left hand for the
shield. A bot found holding two weapons gets its shield back and the weapon goes to its backpack.

**Siege.** Whisper ram, ballista, catapult or trebuchet to the Siegemaster to hand in a siege token. Group bots help
at level-1 keep doors, and a bot gives its ram to you when you take over.

**Smoother play.** Database checkpoints run off the game loop, so the server stutters less. The new client no longer
rebuilds its animation cache in cities. Bot camps in the open light a campfire where the party rests.

All of this is from unit tests and headless client checks. A feature counts as working in game only after it shows up in
a log from Jay's PC. The full list is in `CHANGELOG-LIVING-SERVER.md`.

### Files

| File | SHA-256 |
|---|---|
| `LivingServer-Setup-1.1.22-beta.exe` | (filled in when the exe is built) |
| `OfflineDAoC-LivingServer-1.1.22-beta.zip` | (zip SHA-256: filled in by hand at publish) |

### Credits

Built on [shadowofze's Offline DAoC](https://github.com/shadowofze/OfflineDAoC). DetShaft fork packaging. GPL-3.0.
