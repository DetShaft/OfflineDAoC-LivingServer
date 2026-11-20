# Living Server Beta 1.1.24: release notes

The text below the line is the GitHub release notes for `v1.1.24-beta`. In the file table the Setup row must keep
exactly `(filled in when the exe is built)`: the release workflow replaces only that text with the exe's SHA-256. The
zip's SHA-256 is filled in when publishing.

---

### Group bots fight on their own, and the Killaloe siege works

1.1.24 is a **DetShaft fork prerelease**. Same Offline DAoC **0.37 / 0.37b / 0.37c** base. The 1.1.9 class-cache
repair is unchanged: INSTALL/Setup still restore `global_script_class_cache.cfg` and never delete it.
The banner reads `LivingServer client patch 1.1.24`.

**Group fight mode.** In the frontier, battlegrounds and Darkness Falls, the bots in your group no longer wait for you to
swing. They fight when anyone in the group is attacked or an enemy player, bot or pet comes near. Your target comes first
while you attack it. Otherwise tanks peel enemies off your healers and casters, damage dealers go for enemy healers, then
casters, then the weakest, and healers keep healing. Bots drop enemies that run far off and come back (setting
`bots / group_fight_chase_distance`, default 1500). If you die, the group keeps fighting. Switch it off with
`bots / group_fight_mode`. Monsters and PvE pulls stay yours.

**Crowd control.** Mezzers hold a cast-time mez while an enemy is hitting them, or a caster or melee could interrupt it,
and bots leave a mezzed enemy alone while another is free.

**Siege.** The ram and every siege kit now work in battlegrounds, so the siege engineer deploys at Killaloe. Group bots no
longer loop through the keep's upper door while you are on the floor below. Jarl Killaloe no longer aggroes bots under his
room. In the new client, your figure no longer runs in place or turns while stunned or mezzed.

**Developers.** A headless bot AI simulation harness (`docs/BOT-AI-SIMULATION.md`) now runs scenarios in CI.

All of this is from unit tests, simulations and headless client checks. A feature counts as working in game only after it
shows up in a log from Jay's PC. The full list is in `CHANGELOG-LIVING-SERVER.md`.

### Files

| File | SHA-256 |
|---|---|
| `LivingServer-Setup-1.1.24-beta.exe` | (filled in when the exe is built) |
| `OfflineDAoC-LivingServer-1.1.24-beta.zip` | (zip SHA-256: filled in by hand at publish) |

### Credits

Built on [shadowofze's Offline DAoC](https://github.com/shadowofze/OfflineDAoC). DetShaft fork packaging. GPL-3.0.
