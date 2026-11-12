# Living Server Beta 1.1.15: release notes

The text below the line is the GitHub release notes for `v1.1.15-beta`. In the file table the Setup row must keep
exactly `(filled in when the exe is built)`: the release workflow replaces only that text with the exe's SHA-256. The
zip's SHA-256 is filled in when publishing.

---

### Paladin chants, healers, helper levels, befriended helpers, robes, city stalls

1.1.15 is a **DetShaft fork prerelease**. Same Offline DAoC **0.37 / 0.37b / 0.37c** base. The 1.1.9 class-cache
repair is unchanged: INSTALL/Setup still restore `global_script_class_cache.cfg` and never delete it.
The banner reads `LivingServer client patch 1.1.15`.

**Paladin bots hold one chant.** A Paladin bot no longer recasts chants every second or two. A grouped Paladin holds
one chant: endurance out of combat, the combat heal while someone nearby is hurt in a fight, damage add otherwise. It
keeps its chant while it or its leader moves, holds a chant at least 10 seconds before switching, and never recasts the
same chant within 6 seconds. A group now gets one Paladin chant at a time. Smarter, level-based twisting for every
chanting class comes later.

**Healers in every realm hold their place.** Friar, Shaman, Warden and Bard bots no longer run back and forth between
the mob and their formation slot. Walking back to the slot ends a chase on an enemy, and a hybrid that stops to heal
ends a chase on an enemy out of melee reach. They still fight in melee between heals.

**Helpers show their new level.** After a /spawn helper matches your level, the group window shows its new level and
it cons at that level.

**Befriended helpers keep their gear and come online.** A befriended helper's gear is saved when it leaves you, and it
stays online as a gamebot for 2 to 3 hours, then follows the normal schedule. A befriended helper released by 1.1.14
without gear (such as Kettleboil) gets a kit for its level the first time it logs in. Friends no longer shows a helper
that left you as online before its gamebot logs in.

**Robes look like robes in the new client.** A robe is drawn as the long robe instead of a short tunic over bare legs,
for players, companions, gamebots and NPCs.

**Fewer stalls in crowded cities.** Figures no longer switch their shadows on and off as they leave and enter the camera
view, which likely caused the long frames when turning the camera in a crowd. `perf_live.log` now records figures built
and shadow switches, so a city log can show what is behind any stalls that remain.

These notes are from unit tests and headless client checks. A feature counts as working in game only after it
shows up in a log from Jay's PC.

### Files

| File | SHA-256 |
|---|---|
| `LivingServer-Setup-1.1.15-beta.exe` | (filled in when the exe is built) |
| `OfflineDAoC-LivingServer-1.1.15-beta.zip` | (zip SHA-256: filled in by hand at publish) |

### Credits

Built on [shadowofze's Offline DAoC](https://github.com/shadowofze/OfflineDAoC). DetShaft fork packaging. GPL-3.0.
