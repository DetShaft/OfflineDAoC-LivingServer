# Living Server Beta 1.1.13: release notes

The text below the line is the GitHub release notes for `v1.1.13-beta`. In the file table the Setup row must keep
exactly `(filled in when the exe is built)`: the release workflow replaces only that text with the exe's SHA-256. The
zip's SHA-256 is filled in when publishing.

---

### Spell effects, healer bots, Realm Exchange prices, commander hammer

1.1.13 is a **DetShaft fork prerelease**. Same Offline DAoC **0.37 / 0.37b / 0.37c** base. The 1.1.9 class-cache
repair is unchanged: INSTALL/Setup still restore `global_script_class_cache.cfg` and never delete it.
The banner reads `LivingServer client patch 1.1.13`.

**Spell effects in the new client.** Most spell effects did not show. The cause was the new client's performance
governor: with a target of 120 fps on a 144 Hz vsync display it could only give detail back above 144 fps, which vsync
never allows, so after the first busy moment it stayed at its heaviest savings (30 effects within 10 m of the camera).
Now:
- Effects in your own fight always show: anything on you, aimed at you, or within 25 m of your character.
- The governor aims for the target frame rate or the display's cap, whichever is lower, and gives detail back near it.
- At its heaviest it keeps 60 effects within 30 m and casters' hand glows within 15 m.

**Healer bots hold their place in fights.** A healer in your group with nothing to cast walked to the mob to nuke or
swing at it, then back to its formation slot to heal, over and over. Idle offense now never moves a healer: it casts
only at a target already in spell range and swings only at one already in melee reach, and holding for a heal ends a
follow on an enemy. Follows on the leader, the group and its own pet are kept. Heals, cures and rezzes still come first.

**Realm Exchange prices.** My Listings in the new client showed SET PRICE for items that had a price on the server. The
client now reads the price the server puts at the end of a listing's name, so every priced listing shows it, including
ones priced from another PC or the classic client. A priced name over the 55-character limit now shortens the item's
name and keeps the price whole.

**Commander hammer styles.** A level-1 Returned Commander had no hammer style, because the hammer line starts at level
2. Each weapon line now always gives at least its first style that can be picked as a favorite (Thor's Anvil for the
hammer).

These notes are from unit tests and headless client checks. A feature counts as working in game only after it
shows up in a log from Jay's PC.

### Files

| File | SHA-256 |
|---|---|
| `LivingServer-Setup-1.1.13-beta.exe` | (filled in when the exe is built) |
| `OfflineDAoC-LivingServer-1.1.13-beta.zip` | (zip SHA-256: filled in by hand at publish) |

### Credits

Built on [shadowofze's Offline DAoC](https://github.com/shadowofze/OfflineDAoC). DetShaft fork packaging. GPL-3.0.
