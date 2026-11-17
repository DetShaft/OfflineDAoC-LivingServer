# Living Server Beta 1.1.19: release notes

The text below the line is the GitHub release notes for `v1.1.19-beta`. In the file table the Setup row must keep
exactly `(filled in when the exe is built)`: the release workflow replaces only that text with the exe's SHA-256. The
zip's SHA-256 is filled in when publishing.

---

### Bonedancer commanders, pet orders, group loot rolls, and smarter healers

1.1.19 is a **DetShaft fork prerelease**. Same Offline DAoC **0.37 / 0.37b / 0.37c** base. The 1.1.9 class-cache
repair is unchanged: INSTALL/Setup still restore `global_script_class_cache.cfg` and never delete it.
The banner reads `LivingServer client patch 1.1.19`.

**Level-40 Bonedancer commanders.** Each specialization line teaches its own commander: the Bone Magus (Darkness, a
caster), the Bone Keeper (Suppression, lifedrain and suppress spells) and the Bone Captain (Bone Army, a bowman). Each
controls three minions. A commander lands its spells about as often as its Bonedancer would; minions make up half of
their level gap. The missing rows are added on the server's first start.

**Pets.** Every pet class has an order dialog (right-click the pet in the new client, `/pet`, or whisper it). Spiritmaster
and Enchanter pets choose one-handed weapons, and taunting pets take a shield.

**Group gear is rolled for.** In a group, weapons, armour, shields, instruments and jewels open a Need/Greed roll and,
in the new client, a Loot Rolls window. Bots answer by themselves.

**Healers hold a safe post.** A group's back line (Cleric, Druid, Healer, Shaman, Bard) stays out of enemy reach and in
casting range of the group. Shamans and Bards never melee.

**Smaller changes.** Helper bots are named in the combat log. The new client has fewer hitches, offers the Savage at
character creation, and gloves and boots use the item's own mesh. The launcher has one Active tab. Gamebots roll low-level alts.

These notes are from unit tests and headless client checks. A feature counts as working in game only after it
shows up in a log from Jay's PC. The full list is in `CHANGELOG-LIVING-SERVER.md`.

### Files

| File | SHA-256 |
|---|---|
| `LivingServer-Setup-1.1.19-beta.exe` | (filled in when the exe is built) |
| `OfflineDAoC-LivingServer-1.1.19-beta.zip` | (zip SHA-256: filled in by hand at publish) |

### Credits

Built on [shadowofze's Offline DAoC](https://github.com/shadowofze/OfflineDAoC). DetShaft fork packaging. GPL-3.0.
