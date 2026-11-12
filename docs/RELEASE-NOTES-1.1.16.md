# Living Server Beta 1.1.16: release notes

The text below the line is the GitHub release notes for `v1.1.16-beta`. In the file table the Setup row must keep
exactly `(filled in when the exe is built)`: the release workflow replaces only that text with the exe's SHA-256. The
zip's SHA-256 is filled in when publishing.

---

### Healers stay idle between heals

1.1.16 is a **DetShaft fork prerelease**. Same Offline DAoC **0.37 / 0.37b / 0.37c** base. The 1.1.9 class-cache
repair is unchanged: INSTALL/Setup still restore `global_script_class_cache.cfg` and never delete it.
The banner reads `LivingServer client patch 1.1.16`.

**Healers stay idle when nobody needs healing.** Healer bots go back to standing by between heals while their AI is
reworked. A Cleric, Druid or Healer that is a group's only support no longer nukes, debuffs or swings at the group's
target when nobody needs healing. This idle combat is now off by default, under the new server setting
`bots / healer_idle_combat_enabled`. A `True` saved under the old setting name does not carry over. 1.1.15's change to
how Friars, Shamans, Wardens and Bards walk back to their formation slot is reverted. Heals, cures, resurrection, group
rez priority and leader chat orders are unchanged.

Everything else from [1.1.15](https://github.com/DetShaft/OfflineDAoC-LivingServer/releases/tag/v1.1.15-beta) is
included: Paladins holding one chant, helpers showing their new level, befriended helpers keeping their gear, robes in
the new client and fewer stalls in crowded cities.

These notes are from unit tests and headless client checks. A feature counts as working in game only after it
shows up in a log from Jay's PC.

### Files

| File | SHA-256 |
|---|---|
| `LivingServer-Setup-1.1.16-beta.exe` | (filled in when the exe is built) |
| `OfflineDAoC-LivingServer-1.1.16-beta.zip` | (zip SHA-256: filled in by hand at publish) |

### Credits

Built on [shadowofze's Offline DAoC](https://github.com/shadowofze/OfflineDAoC). DetShaft fork packaging. GPL-3.0.
