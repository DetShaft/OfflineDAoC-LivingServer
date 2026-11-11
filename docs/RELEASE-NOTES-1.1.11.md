# Living Server Beta 1.1.11: release notes

The text below the line is the GitHub release notes for `v1.1.11-beta`. In the file table the Setup row must keep
exactly `(filled in when the exe is built)`: the release workflow replaces only that text with the exe's SHA-256. The
zip's SHA-256 is filled in when publishing.

---

### Commander dialog, weapon swaps, styles

1.1.11 is a **DetShaft fork prerelease**. Same Offline DAoC **0.37 / 0.37b / 0.37c** base. The 1.1.9 class-cache
repair is unchanged: INSTALL/Setup still restore `global_script_class_cache.cfg` and never delete it.
The banner reads `LivingServer client patch 1.1.11`.

**Commander.** Targeting your commander, right-clicking it, or pressing Orders opens a small dialog.
The commander's lines use bracketed keywords. Clicking one whispers it back, and the reply appends
in the same window. A skeletal commander offers lines such as
`I can use a [Twohanded Sword] [Twohanded Axe] [Hammer] [Sword] [Axe]` and
`My minions can [assist] me or [split]`. Replies look like `My minions will assist me.` and
`I will no longer taunt.`

**Weapon swap.** One click changes the weapon and the client is sent an equipment update even when
the slot stays the same. A one-handed sword and a one-handed axe both use the standard slot, so the
old update was skipped and the figure kept the previous weapon until a later click changed the slot.
`/commander weapon` no longer shares the 1500 ms silent command drop.

**Styles.** The dialog lists only styles for the weapon that is out, and the commander only uses
those styles. A preferred style that does not match the new weapon is cleared on the swap.
Havoc (axe) does not stay selected when a sword is equipped, and Polar Rift (sword) does not stay
selected when an axe is equipped.

**Windows.** Looking For Group and Friends use aligned columns, one font size, and a header rule.
Invite and Tell stay grey until a row is selected.

These notes are from unit tests and a headless client check. A feature counts as working in game
only after it shows up in a log from Jay's PC.

### Files

| File | SHA-256 |
|---|---|
| `LivingServer-Setup-1.1.11-beta.exe` | (filled in when the exe is built) |
| `OfflineDAoC-LivingServer-1.1.11-beta.zip` | (zip SHA-256: filled in by hand at publish) |

### Credits

Built on [shadowofze's Offline DAoC](https://github.com/shadowofze/OfflineDAoC). DetShaft fork packaging. GPL-3.0.
