# Swapping a zone for a later-era version (Killaloe template)

Killaloe replaced Abermenai as the level 15-19 battleground in retail. This world keeps Abermenai's region
and client zone id (253) and puts Killaloe's ground, props, keeps and spawns into it. Use these steps for the
next zone that gets the same treatment.

Status: the client-side steps were applied on the owner's PC on 2026-11-15. Tests only otherwise: nothing below
has been seen working in a play log yet.

## 1. Coordinates: work out the three frames first

Most Killaloe bugs came from mixing these frames:

| Frame | Killaloe example | Notes |
|---|---|---|
| Retail region | region 237, zone offset 64,64 sectors = 524288 | Live packets, the OpenDAoC dump (`keep`, `mob` rows for region 237) |
| Zone-local | 0..65536 | `fixtures.csv`, `heights.f32` and every zone.json position (times 0.025 m) |
| This world's region | region 253, zone offset 1,1 = 8192 | Server rows (`Mob`, `Door`, `Keep`, `Area`) |

`region253 = retail237 - 524288 + 8192`. Every server row needs the +8192, including the field spawns.

Checks that caught the real errors:
- **Spawns:** sample `heights.f32` at each spawn's zone-local X/Y and compare with its Z. Killaloe's field pack:
  median 0 with the offset, and 33 of 74 buried without it.
- **Leftovers:** list region spawns that stand far below the new ground. Abermenai's 98 spawns sat at Z
  2,900-4,900 under ground at Z 7,900 and up.

## 2. Ground and props (new client)

`source/new-client/pipeline/build_zone.py <zone> --no-upscale`. On Windows, leave out `--no-upscale` so
Real-ESRGAN makes the x4plus textures. Set `DAOC_CLIENT` to the offline client and `DAOC_RETAIL_CLIENT` to the
live install (read-only). For zone 253, `killaloe_terrain.py` makes the build:
- borrow the heights, water, sector and painted ground from `dat237`/`ter237`/`tex237`;
- borrow the props, lights and bounds from `csv237.mpk` (`BORROWED_CSV`). Abermenai's props stood in the wrong
  places on Killaloe's ground;
- drop the old zone's `zonejump.csv` exits.

Tree-cluster NIFs (`Elm1CL5.nif` and similar) are not real files. `expand_tree_clusters` places their single
trees from `zones/trees/tree_clusters.csv`. Killaloe has 89 such props.

Output goes to `source/new-client/godot/content/zones/zoneNNN`. Copy it into the game's
`new-client/godot/content/zones/` after a backup. Generated content is never committed.

## 3. Keeps

Live battleground keeps are NF keep components (0x6C packets) on a 148-unit grid. The new client draws them as
zone fixtures. `killaloe_keeps.py` holds the layouts and `apply_killaloe_keeps.py <zone dir>` writes them.

- **Keep pad and heading:** take them from the 0x69 KeepInfo packet when the capture has it. When it doesn't
  (Dun Killaloe), use the OpenDAoC `keep` row. Before trusting either, check that each door in the model lands
  on its captured `Door` row: piece position, plus `component_xy`, plus the door's offset inside the model
  (`nif_doors` closed pose). Portal keeps came within 10 units and Dun Killaloe within 16. The earlier Dun pad
  had treated the gate door as the gatehouse's origin, which put the keep 750 units off.
- **Skins:** each lvl4 NIF holds every realm's toppers and cake plus damaged layers. `filter_keep_gltf` keeps
  one realm's undamaged nodes. `realm_material` swaps `alb_` textures for the realm's (`midcake` for the
  Midgard cake) and uses the `_cake_un_` lightmaps. It also turns off vertex colours: the NF pieces' vertex
  colours are near black (0.00-0.08) and only mask the damage decal, so multiplying by them drew the keeps black.
- **Old keeps:** fixtures go by model (`frontkeep` NIFs), not by fixture id. Killaloe's ids 1-3 are its rope
  bridges.

## 4. Server rows

`GameServer/scripts/dbupdater/KillaloeZone253Seeds.cs` applies the SQL in `source/tools/OfflineDaoc.Setup/data/
killaloe_zone253_*.sql` on every start, for fresh and already-played worlds. `REPLACE` resets each pack, then
`killaloe_zone253_playable.sql` adds the zone offset, fixes classes, merchants and doors, and deletes the old
zone's own spawns (those with no PackageID). Asserts in the same file fail the start if a step drifts.

## 5. Bot pathing

The server navmesh `runtime/server/navmesh/zoneNNN.nav` must be built from what the new client draws. The
classic client's files for that zone id belong to the old zone, so OpenDAoC-BuildNav gets three overrides:

1. `navmesh_zone_input.py --zone-json <zoneNNN>/zone.json --retail-zone <retail>/frontiers/zones/zoneRRR
   --nif-dirs "<retail>/zones/nifs;<retail>/frontiers/NIFS" --out <folder>` writes `fixtures.csv` and
   `nifs.csv` from zone.json (every drawn fixture: trees with their trunk radius, bridges, keep pieces at
   their pads mapped back to the realmless retail NIF, so their doors and collision come in). It copies
   `bound.csv`, `sector.dat` and `water.pcx` from the retail zone. It leaves out `terrain.pcx`.
2. Build BuildNav on Windows (`dotnet build -c Release`) from a git archive of the branch
   (`OpenDAoC-BuildNav` and `OpenDAoC-Core` side by side), and copy `runtime/server/lib/Detour.dll` to
   `<output>/base/lib/`.
3. Run with `base\` on PATH, because BuildNav starts `RecastDemo.exe` by bare name:
   `OpenDAoC-BuildNav.exe --daoc=<classic client> --zones=NNN --heights=NNN=<zoneNNN>/heights.f32
   --zone-files=NNN=<folder> --nif-dirs="<retail>/zones/nifs;<retail>/frontiers/NIFS"`.
   The log names the override folder. The `.doors` file next to the mesh lists the doors it found (Killaloe:
   18). The server doesn't read that file. It matches its own door rows to the mesh's door polygons.
4. Back up the live `zoneNNN.nav` and copy the new one in while the server is stopped.

Killaloe (2026-11-15) took 78 seconds and gave a 28.9 MB mesh.

## 6. Verify in game

1. Each portal keep and the centre keep: textured, the right realm's look, guards on the walls and towers.
2. Doors open for the owning realm, and the lord and staff stand inside the main keep.
3. Field mobs stand on the ground with nothing buried, and trees and bridges are in Killaloe's places.
4. `server.log`: no `DoorMgr ... could not register with pathfinding` lines for the zone (after the navmesh step).
