"""Killaloe (region 253) keep buildings from live KeepComponentInfo skins.

Live Killaloe 0x69 KeepInfo model is 0x00. The buildings are 0x6C skins mapped
through frontiers/fr_tiers.csv to lvl4 NF pieces. Portal keeps share one
12-piece layout; Dun Killaloe has mainKeep_lvl4 plus gatehouse, short walls,
and corner towers. Staff, realm, and merchants stay on the captured pads.
"""
import json
import math
import re

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

# Dun pad: OpenDAoC's keep row 138 (region 237), heading 0. The capture has no 0x69 for it. Checked against the
# captured doors: the gatehouse Keep Gate and the main keep's Keep Gate land within 16 units of their door rows
# once each model's door offset is added (gatehouse door001 at model 376,779; mainkeep Door001 at 603,431). The
# earlier pad (557435, 557562) took the gate door midpoint as the piece origin, which put the whole keep about
# 750 units south-east of its staff and doors (owner screenshots 2026-11-15).
DUN_KEEP = {
    "id": 0x008A,
    "name": "Dun Killaloe",
    "x": 557067,
    "y": 556900,
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

REALM_SUFFIX = {1: "alb", 2: "mid", 3: "hib"}
REALM_LABEL = {1: "Albion", 2: "Midgard", 3: "Hibernia"}

# Captured Door.InternalID by keep and 0x6C skin. Hibernia has no keep gate in the capture.
PORTAL_DOOR_IDS = {
    0x018A: {24: {1: 253201001, 2: 253201002, 3: 253201003}},
    0x028A: {24: {1: 253202001, 2: 253202002, 3: 253202003}},
    0x038A: {24: {2: 253203002, 3: 253203003}},
}
DUN_DOOR_IDS = {
    24: {1: 253138001, 2: 253138003, 3: 253138004},
    30: {1: 253138002},
}

_MAX_LEFTOVER = re.compile(r"^(editable mesh|editable poly|polymesh)(:\d+)?$", re.I)
_REALM_ALB = re.compile(r"(?<![a-z])alb", re.I)
_REALM_MID = re.compile(r"(?<![a-z])mid", re.I)
_REALM_HIB = re.compile(r"(?<![a-z])hib", re.I)
_DAMAGED = re.compile(r"rubble|siege[-_ ]?tower|(?<!un)(?<!u)dam", re.I)
_TIER1_ONLY = re.compile(r"tiers[_\s-]*1(?![\d-])", re.I)


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
        row = {
            "stem": stem,
            "realm_stem": realm_stem(stem, keep["realm"]),
            "name": "%s %s" % (keep["name"], SKIN_NAME[skin]),
            "pos": fixture_pos(rx, ry, keep["z"]),
            "yaw": fixture_yaw(keep["heading"], chead),
            "id": start_id + i,
            "skin": skin,
            "cx": cx,
            "cy": cy,
            "chead": chead,
            "realm": keep["realm"],
        }
        dids = piece_door_ids(keep, skin)
        if dids:
            row["door_ids"] = dids
        out.append(row)
    return out


def all_piece_fixtures():
    rows = []
    fid = 5001
    for keep in PORTAL_KEEPS:
        rows.extend(keep_fixtures(keep, fid))
        fid += len(keep["pieces"])
    rows.extend(keep_fixtures(DUN_KEEP, fid))
    return rows


def realm_stem(stem, realm):
    return "%s_%s" % (stem, REALM_SUFFIX[realm])


def piece_door_ids(keep, skin):
    if keep["id"] == DUN_KEEP["id"]:
        return dict(DUN_DOOR_IDS.get(skin) or {})
    return dict((PORTAL_DOOR_IDS.get(keep["id"]) or {}).get(skin) or {})


def node_keep_visible(name, realm):
    """True if this converted keep-piece node is the undamaged lvl4 mesh for realm."""
    n = name or ""
    low = n.lower()
    if low.startswith("collision") or low.startswith("door"):
        return True
    if _MAX_LEFTOVER.match(n.strip()):
        return False
    if _DAMAGED.search(n) and "undam" not in low and "udam" not in low:
        return False
    has_alb = bool(_REALM_ALB.search(n))
    has_mid = bool(_REALM_MID.search(n))
    has_hib = bool(_REALM_HIB.search(n))
    if has_alb or has_mid or has_hib:
        want = REALM_SUFFIX[realm]
        if want == "alb" and not has_alb:
            return False
        if want == "mid" and not has_mid:
            return False
        if want == "hib" and not has_hib:
            return False
    if _TIER1_ONLY.search(n) and not re.search(r"tiers[_\s-]*[2-4]", n, re.I):
        return False
    return True


# The lvl4 NF pieces are one NIF per piece, textured for Albion. The client swaps the realm prefix of each texture
# name for the keep's realm. The cake (roof and topper) texture is spelt midcake for Midgard.
_REALM_TEX_PREFIX = {1: "alb_", 2: "mid_", 3: "hib_"}
_REALM_CAKE = {1: "alb_cake", 2: "midcake", 3: "hib_cake"}
_LM_REALM = re.compile(r"_(alb|mid|hib)_cake_(dam|un)_", re.I)


def realm_texture(name, realm, available=None):
    """The realm's version of one texture name, or the name unchanged when the realm has no texture of its own.

    available: lower-case names that exist (converted or in the client). None: assume every realm name exists."""
    low = (name or "").lower()
    if not low:
        return name
    want = None
    if low in ("alb_cake", "midcake", "mid_cake", "hib_cake"):
        want = _REALM_CAKE[realm]
    elif _LM_REALM.search(low):
        # lightmaps: the realm's undamaged map (the damaged one darkens walls that were never hit)
        want = _LM_REALM.sub("_%s_cake_un_" % _REALM_TEX_PREFIX[realm][:3], low)
    elif low.startswith(("alb_", "mid_", "hib_")):
        want = _REALM_TEX_PREFIX[realm] + low[4:]
    if want is None or want == low:
        return name
    for suffix in ("__x2", "__d2"):
        if want.endswith(suffix) and available is not None:
            base_ok = want[:-len(suffix)] in available or want in available
            return want if base_ok else name
    if available is not None and want not in available:
        return name
    return want


def realm_material(spec, realm, available=None):
    """One nif_materials spec for a realm keep, drawn undamaged.

    The NF pieces' vertex colours are near black (0.00 to 0.08): the client uses them to place the damage decal,
    not as light, so multiplying the wall texture by them drew every keep black (owner screenshots 2026-11-15).
    The damage decal slot goes too: these keeps are drawn undamaged."""
    out = dict(spec)
    slots = {}
    for slot, value in (spec.get("slots") or {}).items():
        if slot == "decal" and str((value or {}).get("tex", "")).lower() == "damage":
            continue
        v = dict(value)
        v["tex"] = realm_texture(v.get("tex", ""), realm, available)
        slots[slot] = v
    out["slots"] = slots
    out["vertex_colors"] = False
    return out


def realm_texture_names(stems_used, realms):
    """Every texture name the realm copies may ask for, to convert from the client before the copies are written."""
    names = set()
    for name in stems_used:
        for realm in realms:
            names.add(realm_texture(name, realm, None).lower())
    return names


def rotate_pose_yaw(pose, deg):
    """Rotate a 4x3 Godot pose (9 basis + 3 origin) about +Y at its origin."""
    a = math.radians(deg)
    cy, sy = math.cos(a), math.sin(a)
    cols = [pose[0:3], pose[3:6], pose[6:9]]
    out = []
    for x, y, z in cols:
        out.extend([cy * x + sy * z, y, -sy * x + cy * z])
    out.extend(pose[9:12])
    return [round(float(v), 6) for v in out]


def ensure_door_swing(doors):
    """Keep-piece NIFs store identical closed/open poses. Swing paired leaves apart."""
    seen = {}
    for door in doors:
        if door.get("closed") != door.get("open"):
            continue
        closed = door.get("closed") or []
        if len(closed) < 12:
            continue
        group = door.get("group") or door.get("name") or ""
        n = seen.get(group, 0)
        seen[group] = n + 1
        door["open"] = rotate_pose_yaw(closed, 90.0 if n % 2 == 0 else -90.0)
    return doors


def filter_keep_gltf(src, dest, realm, available=None):
    """Write a realm-skinned copy: other-realm cakes and damaged overlays are dropped, and the materials take the
    realm's own textures (realm_material). available: texture names that exist for the zone (None: assume all)."""
    doc = json.loads(src.read_text(encoding="utf-8"))
    nodes = doc.get("nodes") or []

    def keep_idx(i):
        if i < 0 or i >= len(nodes):
            return False
        return node_keep_visible(nodes[i].get("name") or "", realm)

    def filter_tree(idx):
        node = nodes[idx]
        kids = node.get("children") or []
        if kids:
            node["children"] = [c for c in kids if keep_idx(c)]
            for c in node["children"]:
                filter_tree(c)

    scene = doc["scenes"][doc.get("scene", 0)]
    roots = []
    for r in scene.get("nodes") or []:
        if keep_idx(r):
            filter_tree(r)
            roots.append(r)
    scene["nodes"] = roots
    extras = doc.setdefault("extras", {})
    extras["nif_doors"] = ensure_door_swing(list(extras.get("nif_doors") or []))
    extras["keep_realm"] = realm
    extras["nif_materials"] = [realm_material(spec, realm, available) for spec in extras.get("nif_materials") or []]
    for image in doc.get("images") or []:
        uri = image.get("uri") or ""
        folder, _, file_name = uri.rpartition("/")
        stem, dot, ext = file_name.rpartition(".")
        if dot:
            image["uri"] = "%s/%s.%s" % (folder, realm_texture(stem, realm, available), ext) if folder else \
                "%s.%s" % (realm_texture(stem, realm, available), ext)
    dest.write_text(json.dumps(doc, separators=(",", ":")), encoding="utf-8")
    visible = [nodes[i].get("name") or "" for i in roots]
    return {"doors": extras.get("nif_doors") or [], "roots": visible}
