"""Ground for the 15-19 battleground (region 253, Killaloe).

Retail replaced Abermenai with Killaloe. On the live client Killaloe is zone 237, in
frontiers/zones/zone237, and zones.dat puts that zone at region_offset 64, 64. This
world's keeps, portals and teleports for the 15-19 battleground are in region 253,
in the same 0..65536 box as the other three battlegrounds (central keep 33089, 38271).
An offset of 64 sectors is 524288 region units, so a player standing on that keep
would not be inside zone 237's rectangle.

The region id and the client zone id therefore stay 253. When the retail files are
present, build_zone.py copies Killaloe's height, water, sector and painted ground
into the zone 253 output. Fixtures and the three realm exits stay the ones in zone
253. The retail offset is not copied. Generated content/zones/zone253 is not committed.

This module does not import the rest of the pipeline, so the path rules can be tested
without the client and without numpy.
"""
import os
from pathlib import Path

BATTLEGROUND_ZONE = 253
TERRAIN_ZONE = 237
PLACE_NAME = 'Killaloe'
# Retail zones.dat. Deliberately not applied to region 253. 64 * 8192 = 524288.
RETAIL_OFFSET_SECTORS = (64, 64)
SECTOR = 8192

# ter and tex are the painted ground. dat holds the heightmap, water and sector.
REQUIRED = ('ter237.mpk', 'tex237.mpk', 'dat237.mpk')
# Members copied out of dat237.mpk. zonejump, fixtures, nifs and lights stay zone 253.
BORROWED_DAT = ('terrain.pcx', 'offset.pcx', 'water.pcx', 'grassmap.pcx', 'densemap.pcx', 'sector.dat')
NEEDED_IN_DAT = ('terrain.pcx', 'offset.pcx', 'water.pcx', 'sector.dat')
KEPT_FROM_BATTLEGROUND = ('zonejump.csv', 'fixtures.csv', 'nifs.csv', 'lights.csv')

RETAIL_DIR = 'e:\\Electronic Arts\\Dark Age of Camelot\\frontiers\\zones\\zone237'
# Sizes measured on the live client (read-only). Named so a build can be checked against them.
RETAIL_SIZES = {'ter237.mpk': 205836, 'tex237.mpk': 5661950}


def default_roots():
    """Retail install first, then the offline client (DAOC_CLIENT or the pipeline default)."""
    roots = []
    retail = os.environ.get('DAOC_RETAIL_CLIENT')
    if retail:
        roots.append(Path(retail))
    client = os.environ.get('DAOC_CLIENT')
    if client:
        roots.append(Path(client))
    else:
        roots.append(Path(__file__).resolve().parent.parent / 'external/old_project/runtime/client-opendaoc/app')
    return roots


def candidate_folders(client_roots):
    """frontiers/zones/zone237, then zones/zone237, under each root in order."""
    rels = (
        f'frontiers/zones/zone{TERRAIN_ZONE:03d}',
        f'zones/zone{TERRAIN_ZONE:03d}',
    )
    for root in client_roots:
        for rel in rels:
            yield Path(root) / rel


def folder_ready(path):
    return all((path / name).is_file() for name in REQUIRED)


def terrain_folder(client_roots=None):
    """The zone 237 folder that holds Killaloe's ground, or None. Never a zone 253 folder."""
    roots = default_roots() if client_roots is None else client_roots
    for folder in candidate_folders(roots):
        if folder_ready(folder):
            return folder
    return None


def missing_message():
    """What a build should print when the retail ground is not on this machine."""
    sizes = ', '.join(f'{name} ({RETAIL_SIZES[name]} bytes)' for name in ('ter237.mpk', 'tex237.mpk'))
    return (
        'Killaloe ground was not found, so zone 253 builds without painted ground.\n'
        f'Need {RETAIL_DIR}\\\n'
        f'  {sizes}, lod237.mpk, dat237.mpk\n'
        'Set DAOC_RETAIL_CLIENT to e:\\Electronic Arts\\Dark Age of Camelot and run: python build_zone.py 253\n'
        'That writes godot/content/zones/zone253/ (heights.f32, heights_2x.f32, water.png, '
        'terrain/original/tex165.png, terrain/original/lod.png, splat/). Do not commit that output.'
    )
