# Startup log hygiene

This note tracks which startup-time messages were demoted from repeating ERR spam to **one WARN per session per key**, and what still logs at ERR.

## Demoted (expected world-data drift)

| Source | Former noise | New behavior |
|--------|----------------|---------------|
| `SkillBase.LoadSpellLines` | `LineXSpell Spell Adding Error: key not present` on Item Effects / Potions / Poisons | One WRN per `(LineName, SpellID)` when the spell row is missing |
| `NpcTemplateMgr.GetTemplate` | `No npctemplate with ID … found` | One WRN per template id |
| `NpcTemplate` ctor | `Tried to load a null spell into NPC template` | One WRN per `(template, spell)`; spell id `0` is ignored |
| `DoorMgr.RegisterDoor` | `Failed to register door in pathfinding provider` | One WRN per door id (navmesh/coordinate mismatch) |

After spell lines load, look for `STARTUP_LOG_HYGIENE demoted=N` in the log for how many unique keys were demoted that boot.

## Still ERR (investigate)

- `LineXSpell` failures whose exception is **not** a missing spell key (unexpected parse/load errors).
- `NpcTemplateMgr.Init` — `Error loading template {name}` with a stack trace (corrupt template row).
- `ScriptMgr.LoadCommands` — unhandled handler construction failures (duplicate primary commands are suppressed, not thrown).
- Spell load errors in `SkillBase` outside LineXSpell relation (bad spell rows, unknown effect types).
- Region NPC `ClassType` missing (e.g. `RPTradeInMerchant`).
- Missing `SpellHandler` constructors (UniPortal, ArmorFactorBuff, …).
- Gameplay-time missing `ItemTemplate` (e.g. loot tables) — not part of startup hygiene.

## Related fixes (same release)

- **`bonedancer_item`:** `TrainerStarterItems` adds the row at server start when absent (clone from another Midgard caster staff or a built-in默认 staff, model 3464).
- **`/tc`:** Realm Exchange teleport only (`&tc`). Corpse summon: `/transfercorpse` or `/tfcorpse`.
- **Dashboard snapshot:** `AutonomousBotDashboard` stops writing after one access-denied on the snapshot path (no stack every 500 ms).
