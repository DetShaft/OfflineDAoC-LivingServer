# Killaloe mob camps

Killaloe (region 253, the 15-19 battleground) gets mob camps across the parts of the zone that had no spawns
(owner, 2026-11-21). They are copies of the existing field mobs: no new mob types, models or loot.

- SQL: `source/tools/OfflineDaoc.Setup/data/killaloe_zone253_camps.sql`, applied at every server start by
  `KillaloeZone253Seeds` (PackageID `offline-killaloe-zone253-camps`).
- Ground: the Z in the SQL is a guess. At start `KillaloeZone253Camps` moves each camp mob onto the navmesh
  (land only) and removes any that are off it. Log lines: `KILLALOE_CAMPS placed=.. moved=.. dropped=..`, and
  `KILLALOE_CAMP_DROPPED` for each removed mob.
- Data for bot goals: `source/server/GameServer/bots/autonomous/data/killaloe-camps.json` (same camps, with members).
- Regenerate: `python source/tools/killaloe/gen_killaloe_camps.py` (deterministic). Do not hand-edit the outputs.

Each realm third (nearest portal keep) ends with the same number of field mobs and the same mix of camp levels.

| Realm third | Field mobs before | Camps added | Field mobs after |
|---|---:|---:|---:|
| Albion | 47 | 20 | 127 |
| Midgard | 58 | 18 | 130 |
| Hibernia | 42 | 22 | 130 |

| Camp | Third | Type | Levels | X | Y |
|---|---|---|---|---:|---:|
| killaloe-camp-01 | Albion | large rat | 14-15-16 | 37370 | 44141 |
| killaloe-camp-02 | Albion | forest cat | 16-17-18 | 40834 | 48394 |
| killaloe-camp-03 | Albion | boogey spawn | 15-16-17 | 35799 | 49106 |
| killaloe-camp-04 | Albion | clicking beetle | 16-17-18 | 17429 | 53453 |
| killaloe-camp-05 | Albion | forlorn spectre | 17-18-19 | 32848 | 54395 |
| killaloe-camp-06 | Albion | clicking beetle | 16-17 | 39007 | 54518 |
| killaloe-camp-07 | Albion | forlorn spectre | 18-19-20 | 45440 | 57218 |
| killaloe-camp-08 | Albion | boogey spawn | 17-18-19 | 19743 | 58103 |
| killaloe-camp-09 | Albion | large rat | 14-15-16 | 26480 | 59935 |
| killaloe-camp-10 | Albion | forest cat | 16-17-18 | 41117 | 60065 |
| killaloe-camp-11 | Albion | boogey spawn | 15-16-17 | 35134 | 61755 |
| killaloe-camp-12 | Albion | boogey spawn | 17-18-19 | 48448 | 63568 |
| killaloe-camp-13 | Albion | forlorn spectre | 17-18-19 | 20778 | 63957 |
| killaloe-camp-14 | Albion | clicking beetle | 16-17-18 | 16007 | 65758 |
| killaloe-camp-15 | Albion | clicking beetle | 16-17 | 43405 | 66229 |
| killaloe-camp-16 | Albion | clicking beetle | 16-17-18 | 53098 | 67907 |
| killaloe-camp-17 | Albion | forest cat | 16-17-18 | 47785 | 68980 |
| killaloe-camp-18 | Albion | boogey spawn | 15-16-17 | 34178 | 69368 |
| killaloe-camp-19 | Albion | forlorn spectre | 18-19-20 | 19371 | 69776 |
| killaloe-camp-20 | Albion | large rat | 14-15-16 | 24404 | 69815 |
| killaloe-camp-21 | Hibernia | clicking beetle | 16-17-18 | 33158 | 16024 |
| killaloe-camp-22 | Hibernia | forlorn spectre | 17-18-19 | 39999 | 16359 |
| killaloe-camp-23 | Hibernia | large rat | 14-15-16 | 16281 | 16435 |
| killaloe-camp-24 | Hibernia | boogey spawn | 15-16-17 | 21581 | 16460 |
| killaloe-camp-25 | Hibernia | forest cat | 16-17-18 | 28326 | 18143 |
| killaloe-camp-26 | Hibernia | boogey spawn | 17-18-19 | 33662 | 21222 |
| killaloe-camp-27 | Hibernia | forlorn spectre | 18-19-20 | 40783 | 21748 |
| killaloe-camp-28 | Hibernia | clicking beetle | 16-17 | 24214 | 22313 |
| killaloe-camp-29 | Hibernia | large rat | 14-15-16 | 43485 | 26619 |
| killaloe-camp-30 | Hibernia | forest cat | 16-17-18 | 27552 | 26706 |
| killaloe-camp-31 | Hibernia | forlorn spectre | 17-18-19 | 37364 | 27295 |
| killaloe-camp-32 | Hibernia | boogey spawn | 15-16-17 | 18733 | 27702 |
| killaloe-camp-33 | Hibernia | clicking beetle | 16-17-18 | 32476 | 28648 |
| killaloe-camp-34 | Hibernia | boogey spawn | 17-18-19 | 24297 | 31418 |
| killaloe-camp-35 | Hibernia | large rat | 14-15-16 | 39036 | 32173 |
| killaloe-camp-36 | Hibernia | forlorn spectre | 18-19-20 | 31116 | 33539 |
| killaloe-camp-37 | Hibernia | clicking beetle | 16-17 | 16171 | 33559 |
| killaloe-camp-38 | Hibernia | forest cat | 16-17-18 | 25501 | 36560 |
| killaloe-camp-39 | Hibernia | forlorn spectre | 17-18-19 | 38670 | 37741 |
| killaloe-camp-40 | Hibernia | clicking beetle | 16-17-18 | 33280 | 38085 |
| killaloe-camp-41 | Hibernia | boogey spawn | 15-16-17 | 19046 | 38558 |
| killaloe-camp-42 | Hibernia | clicking beetle | 16-17 | 23577 | 43349 |
| killaloe-camp-43 | Midgard | large rat | 14-15-16 | 55005 | 16350 |
| killaloe-camp-44 | Midgard | boogey spawn | 15-16-17 | 69915 | 17959 |
| killaloe-camp-45 | Midgard | forest cat | 16-17-18 | 62793 | 19387 |
| killaloe-camp-46 | Midgard | clicking beetle | 16-17 | 69486 | 24194 |
| killaloe-camp-47 | Midgard | clicking beetle | 16-17-18 | 55619 | 24678 |
| killaloe-camp-48 | Midgard | forlorn spectre | 17-18-19 | 61531 | 24784 |
| killaloe-camp-49 | Midgard | boogey spawn | 17-18-19 | 59953 | 36064 |
| killaloe-camp-50 | Midgard | large rat | 14-15-16 | 64060 | 41061 |
| killaloe-camp-51 | Midgard | forlorn spectre | 18-19-20 | 58610 | 41861 |
| killaloe-camp-52 | Midgard | boogey spawn | 15-16-17 | 50527 | 43180 |
| killaloe-camp-53 | Midgard | forest cat | 16-17-18 | 55030 | 45794 |
| killaloe-camp-54 | Midgard | clicking beetle | 16-17-18 | 61689 | 45935 |
| killaloe-camp-55 | Midgard | forlorn spectre | 17-18-19 | 65167 | 61145 |
| killaloe-camp-56 | Midgard | clicking beetle | 16-17 | 61073 | 64028 |
| killaloe-camp-57 | Midgard | boogey spawn | 17-18-19 | 69357 | 64205 |
| killaloe-camp-58 | Midgard | large rat | 14-15-16 | 64527 | 67854 |
| killaloe-camp-59 | Midgard | forlorn spectre | 18-19-20 | 58998 | 68640 |
| killaloe-camp-60 | Midgard | boogey spawn | 15-16-17 | 69653 | 69440 |
