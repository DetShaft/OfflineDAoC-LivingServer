# OpenDAoC-BuildNav

A fork of the [buildnav tool](https://github.com/thekroko/uthgard-opensource/tree/master/pathing/buildnav) from the [uthgard-opensource repository](https://github.com/thekroko/uthgard-opensource), originally created by [thekroko](https://github.com/thekroko).

## How it works

1.  The tool reads game asset files from DAoC’s `zones` folder and writes intermediate geometry (`.obj` / `.gset`) plus ladder definitions (`.ladders.json`).
2.  `RecastDemo.exe` builds a walkable navmesh.
3.  The final navigation meshes (`*.nav`) are used by the game server.

Shared Detour P/Invoke lives in `OpenDAoC-Core/Pathing/Detour.Managed` (also used by the game server’s `LocalPathfindingMgr`).

## Prerequisites

1.  **64-bit Environment:** All components must be built and run as 64-bit.
2.  **Detour.dll:** Build the native library from `OpenDAoC-Core/Pathing/Detour`. The build copies it to `base/lib/Detour.dll` (process working directory is `base/`). Required for ladder link placement.
3.  **Sibling repo layout:** `OpenDAoC-BuildNav` and `OpenDAoC-Core` should sit next to each other so the `Detour.Managed` project reference resolves.

## Recast integration

Recast is included as a precompiled executable in the `base` directory.

This `RecastDemo.exe` is a repurposed 64-bit build of the [official RecastDemo](https://github.com/recastnavigation/recastnavigation/tree/main/RecastDemo), slightly tweaked to accept command-line arguments and handle large worlds.
Parameters for navmesh generation (agent size, etc.) are currently hardcoded and not configurable.

It can also be used to manually inspect the generated meshes:
1.  Open `RecastDemo.exe` from the output directory.
2.  Select "Tile Mesh" as the sample.
3.  Select any generated `*.gset` as the input mesh.
4.  The corresponding `*.nav` file will also be loaded for inspection.

## Usage instructions

1.  Build and run `OpenDAoC-BuildNav.exe --daoc=<gamedir>`. Use either `--all=true`, or `--zones="id1,id2..."` and/or `--regions="id1,id2..."`.
2.  To take one zone's ground from the new client's `heights.f32` instead of `terrain.pcx`, add `--heights=<zone>=<full path to heights.f32>`. See below.
3.  Copy the generated `*.nav` files from the output directory into your server's `/navmesh` directory.

## Ground from the new client

Outdoor ground normally comes from the classic client's `terrain.pcx` (and `offset.pcx`) in that zone's `dat` archive. Zone 253 in the classic client is Abermenai, so rebuilding 253 from those files bakes Abermenai's ground.

The new client stores a zone's ground in `heights.f32`, written by `source/new-client/pipeline/build_zone.py`. The file is 257 by 257 little-endian float32 heights in metres (the same 0.025 metres per DAoC unit the new client uses). Row is south, column is east, samples are 256 units apart, and the last row and column are the zone's far edge — the edge the new client draws, including a stitched border when `stitch_zone_edges.py` has been run.

```
OpenDAoC-BuildNav.exe --daoc=<gamedir> --zones=253 --heights=253=<full path to heights.f32>
```

Pass the full path. The tool runs with its working directory in `base\`. The named zone reads that file. Zones with no entry in `--heights` keep reading `terrain.pcx`. A bare path, with no zone id, is accepted only when `--zones` names exactly one zone. `--heights=253=<path>` is the form to use whenever more than one zone is in the run; the file still applies to zone 253 alone.

`heights_2x.f32` is the refined render grid. The builder reads `heights.f32`.

Water, rivers, fixtures and ladders still come from the classic files for that zone id. On zone 253 those files are Abermenai's, so this switch changes the ground surface. A zone with no river list is built with no rivers.  

## Output folder structure

```
bin/Release/.../
├── OpenDAoC-BuildNav.exe
└── base/                  # Working directory at runtime
    ├── RecastDemo.exe     # Pre-compiled and tweaked Recast executable
    ├── lib/Detour.dll     # Native Detour (ladder second-pass queries)
    └── zones/             # Intermediate files (*.obj, *.gset, *.ladders.json) and *.nav
```
