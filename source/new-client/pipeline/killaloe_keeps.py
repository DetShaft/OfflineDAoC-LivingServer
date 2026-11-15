"""Killaloe (region 253) keep buildings from live KeepComponentInfo skins.

Live Killaloe 0x69 KeepInfo model is 0x00. The buildings are 0x6C skins mapped
through frontiers/fr_tiers.csv to lvl4 NF pieces. Portal keeps share one
12-piece layout; Dun Killaloe has mainKeep_lvl4 plus gatehouse, short walls,
and corner towers. Staff, realm, and merchants stay on the captured pads.
"""
import math

M = 0.025
ZONE_OFF = 524288
GRID = 148
PORTAL_Z = 8288
DUN_Z = 8768  # Keep Gate / huscarl floor from the capture

SKIN_STEM = {
    21: "shortwall_lvl4",
    23: "longwall_lvl4",
    24: "gatehouse_lvl4",
    27: "cornertower_lvl4",
    29: "straighttower_lvl4",
    30: "mainkeep_lvl4",
}

SKIN_NAME = {
    21: "Short Wall",
    23: "Long Wall",
    24: "Gatehouse",
    27: "Corner Tower",
    29: "Straight Tower",
    30: "Main Keep",
}

# 0x6C relative (cx, cy, heading 0-3). Same 12 pieces on all three portal keeps.
PORTAL_PIECES = [
    (24, -2, -3, 0),
    (27, -6, 10, 1),
    (27, 13, 4, 2),
    (27, 7, -9, 3),
    (27, -12, -3, 0),
    (23, -9, 3, 1),
    (23, 10, -2, 3),
    (21, -5, -6, 0),
    (21, 4, -6, 0),
    (29, 3, 4, 2),
    (21, -3, 7, 2),
    (21, 6, 7, 2),
]

# Dun 0x008A: no 0x69 in the streams. Pieces from 0x6C.
DUN_PIECES = [
    (24, -4, -3, 0),
    (30, 4, 1, 2),
    (21, 2, -6, 0),
    (27, -11, -3, 0),
    (27, 5, -9, 3),
    (27, 11, 4, 2),
    (27, -5, 10, 1),
    (21, 8, 1, 3),
    (21, 8, -2, 3),
    (21, -8, 3, 1),
    (21, -8, 0, 1),
]

# 0x69 pads (region X, region Y, heading degrees, realm). local = region - 524288.
PORTAL_KEEPS = [
    {"id": 0x018A, "name": "Albion Portal Keep", "x": 544824, "y": 582232, "heading": 0x00B9, "realm": 1, "z": PORTAL_Z, "pieces": PORTAL_PIECES},
    {"id": 0x028A, "name": "Midgard Portal Keep", "x": 584945, "y": 559497, "heading": 0x005A, "realm": 2, "z": PORTAL_Z, "pieces": PORTAL_PIECES},
    {"id": 0x038A, "name": "Hibernia Portal Keep", "x": 535251, "y": 537567, "heading": 0x0135, "realm": 3, "z": PORTAL_Z, "pieces": PORTAL_PIECES},
]

# Dun pad from captured gatehouse-door midpoint, keep heading 0 from Keep Gate 0x0000.
# Gatehouse rel (-4,-3), heading 0: keep = door_mid - component offset at heading 0.
DUN_KEEP = {
    "id": 0x008A,
    "name": "Dun Killaloe",
    "x": 557435,
    "y": 557562,
    "heading": 0,
    "realm": 2,
    "z": DUN_Z,
    "pieces": DUN_PIECES,
}

OLD_KEEP_FIXTURE_IDS = (1, 2, 3, 414)
BORDER_STEMS = ("borderkeep1", "n-borderkeep1", "hborderkeep1")
PIECE_STEMS = (
    "gatehouse_lvl4",
    "straighttower_lvl4",
    "longwall_lvl4",
    "shortwall_lvl4",
    "cornertower_lvl4",
    "mainkeep_lvl4",
)

# Display names for zone.json models.
PIECE_DISPLAY = {
    "gatehouse_lvl4": "Gatehouse lvl4",
    "straighttower_lvl4": "Straight Tower lvl4",
    "longwall_lvl4": "Long Wall lvl4",
    "shortwall_lvl4": "Short Wall lvl4",
    "cornertower_lvl4": "Corner Tower lvl4",
    "mainkeep_lvl4": "Main Keep lvl4",
}


def component_xy(keep_x, keep_y, keep_heading_deg, cx, cy):
    a = math.radians(keep_heading_deg)
    x = keep_x + (cx * GRID * math.cos(a) + cy * GRID * math.sin(a))
    y = keep_y - (cy * GRID * math.cos(a) - cx * GRID * math.sin(a))
    return x, y


def fixture_pos(region_x, region_y, z):
    return [(region_x - ZONE_OFF) * M, z * M, (region_y - ZONE_OFF) * M]


def fixture_yaw(keep_heading_deg, chead):
    return -(keep_heading_deg + chead * 90.0)


def keep_fixtures(keep, start_id):
    """Zone.json fixture dicts for one keep's 0x6C pieces. Does not move staff."""
    out = []
    for i, (skin, cx, cy, chead) in enumerate(keep["pieces"]):
        rx, ry = component_xy(keep["x"], keep["y"], keep["heading"], cx, cy)
        stem = SKIN_STEM[skin]
        out.append({
            "stem": stem,
            "name": "%s %s" % (keep["name"], SKIN_NAME[skin]),
            "pos": fixture_pos(rx, ry, keep["z"]),
            "yaw": fixture_yaw(keep["heading"], chead),
            "id": start_id + i,
            "skin": skin,
            "cx": cx,
            "cy": cy,
            "chead": chead,
        })
    return out


def all_piece_fixtures():
    rows = []
    fid = 5001
    for keep in PORTAL_KEEPS:
        rows.extend(keep_fixtures(keep, fid))
        fid += len(keep["pieces"])
    rows.extend(keep_fixtures(DUN_KEEP, fid))
    return rows
