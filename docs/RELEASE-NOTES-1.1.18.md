# Living Server Beta 1.1.18: release notes

The text below the line is the GitHub release notes for `v1.1.18-beta`. In the file table the Setup row must keep
exactly `(filled in when the exe is built)`: the release workflow replaces only that text with the exe's SHA-256. The
zip's SHA-256 is filled in when publishing.

---

### Killaloe, group loot, Realm Exchange deposit, and dead-bot resurrection

1.1.18 is a **DetShaft fork prerelease**. Same Offline DAoC **0.37 / 0.37b / 0.37c** base. The 1.1.9 class-cache
repair is unchanged: INSTALL/Setup still restore `global_script_class_cache.cfg` and never delete it.
The banner reads `LivingServer client patch 1.1.18`.

**Killaloe is the 15-19 battleground.** The zone stays region 253. The name the player sees, the teleporters, and the
bots' speech say Killaloe. A fresh world includes Killaloe's field spawns, keep staff and doors on the captured keep
pads. Hibernia's keep gate was not in the capture and is not invented. The generated Killaloe ground is not in this
package; it is built on the owner's PC when the retail files are present.

**Murdaigean portal keeps get guards.** A guardless portal keep in Murdaigean copies its realm's Thidranki or
Caledonia portal-keep guards at server start. Killaloe's captured staff is left alone, so those copies are not stacked
on the new pads.

**Group loot is shared in turn.** In a group, each drop takes a turn across you, companion bots and gamebots. `/look`
lists your share; `/look take` puts the one you choose in your bag.

**Realm Exchange deposit.** **Deposit this bag** lists the gear in the open backpack bag at a suggested price you can
still change. Bots use the same price and will not pay more than it.

**Dead bots can be rezzed in the new client.** A dead companion or gamebot of your realm can be targeted so a
resurrection can be cast on it. Monster and enemy corpses stay untargetable.

**Launcher Changelog tab.** Release notes from GitHub's releases feed, with a local cache if the fetch fails.

These notes are from unit tests and headless client checks. A feature counts as working in game only after it
shows up in a log from Jay's PC.

### Files

| File | SHA-256 |
|---|---|
| `LivingServer-Setup-1.1.18-beta.exe` | (filled in when the exe is built) |
| `OfflineDAoC-LivingServer-1.1.18-beta.zip` | (zip SHA-256: filled in by hand at publish) |

### Credits

Built on [shadowofze's Offline DAoC](https://github.com/shadowofze/OfflineDAoC). DetShaft fork packaging. GPL-3.0.
