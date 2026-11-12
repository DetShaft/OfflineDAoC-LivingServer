# Long-term direction: a preservation-minded progression server

Status: direction only, not a plan of record. Written 2026-11-12.

## Why

Dark Age of Camelot is one of the best MMOs ever made, and its classic era is already gone from retail.
This fork exists to **preserve** it: first the game as it was in 2001-2003, then, over time, the way it
grew. The long-term idea is a **progression server**: start at Classic + Shrouded Isles and unlock each later
era in order, the way the live game changed, until it reaches roughly where Divided Realms sits (the 1.130 era).

Neither upstream does this. Living Server is frozen in 2003. Divided Realms jumps straight to 1.130.

## Guiding principles

1. **Classic first, and done well.** The current priority stays the same: Classic/SI on the Living Server base,
   with bug fixes and small, faithful additions. Nothing below should get in the way of that.
2. **Faithful over convenient.** Each era should play the way it did at the time (rules, numbers, content),
   documented with sources where possible (patch notes, period guides, `docs/research/`).
3. **Every era is a switch, not a fork.** One codebase; the current era is server configuration. Turning an
   era off must give back exactly the earlier game.
4. **The Godot client is the long-term client.** The old client's limits are why the 0.37c Godot client
   matters. Later eras are added to it rather than switching to the retail 1.130 client.
5. **No game assets in the repo, ever.** Models, zones and textures come from the player's own install,
   the same way Offline DAoC and Divided Realms handle it.
6. **Small, verifiable steps.** Each piece ships as a normal fork release and is smoke-tested against a
   real install.

## How it would fit together

### 1. Era gates (the foundation)
A single server setting for the current era, checked by every system that differs between eras:
classes and races available, zones and portals open, realm ability tables, item stat caps, combat rules,
loot tables, and bot behavior. Build this before any expansion, and start with the classic era as the only
value so it changes nothing.

Rough era order (from the live game): Classic, Shrouded Isles, Foundations (housing), Trials of Atlantis,
New Frontiers, Catacombs, Darkness Rising, Labyrinth of the Minotaur, then later live-era rule changes
up to 1.130.

### 2. Borrow, don't rewrite
Divided Realms is built on the same Offline DAoC / Living Server base and is also **GPL-3.0**, so its
live-era systems can be ported behind era gates with credit: Master Levels, Champion Levels, Trials of
Atlantis item caps and mythicals, live realm point values, cast pushback, chant stacking, and its 1.130
world-data work. OpenDAoC upstream is another source.

### 3. The Godot client grows with each era
This is the hardest and most valuable part. Each era needs the client to load that era's models, zones,
races and UI from the player's install. Server-heavy eras (Trials of Atlantis items and Master Levels)
are lighter here; Catacombs onward (new races, instanced zones, new art) is where client work gets heavy.

### 4. Bots that know what year it is
Living Server's bot layer (personalities, guilds, chat, raids, sieges) should follow the era: raid
targets, classes bots roll, and chat. The existing ~34,000 lines are written for launch through 1.65,
so later eras would need new period-accurate lines.

## Suggested order, when the time comes

1. Keep polishing Classic/SI (current work).
2. Era gate scaffolding with only "Classic/SI" defined. No behavior change.
3. Trials of Atlantis as the first unlock: mostly server-side (items, Master Levels, artifacts), so a
   good test of the gate design with lighter client work.
4. New Frontiers, then Catacombs, where the Godot client work begins in earnest.
5. Later eras, toward the 1.130 target.

## Open questions

- How the player chooses or advances eras: a fixed setting, a timed unlock, or a world milestone.
- Whether housing (Foundations) is in scope.
- How far toward 1.130 to go, or whether a later era is a natural stopping point.
- Which pieces could be offered back upstream to Living Server or Divided Realms.
