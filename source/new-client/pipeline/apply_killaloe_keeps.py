"""Place Killaloe's live NF keep pieces into zone 253.

    python apply_killaloe_keeps.py <zone253-dir> [<zone253-dir> ...]

Writes realm-skinned copies of the six lvl4 glTFs, points each keep at its
realm, wires captured door InternalIDs onto gatehouse/mainkeep leaves, and
drops border-keep fixtures so they do not stack. Dun Killaloe uses
mainkeep_lvl4 (Midgard skin). Does not move staff. Does not start a server
or write the played database.
"""
import json
import shutil
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
import killaloe_keeps as kk

MODEL_STATS = {
    "gatehouse_lvl4": (17868, 3192),
    "straighttower_lvl4": (12482, 2257),
    "longwall_lvl4": (10137, 1814),
    "shortwall_lvl4": (3826, 836),
    "cornertower_lvl4": (17147, 4303),
    "mainkeep_lvl4": (39392, 4806),
}

TEXTURE_MARKERS = (
    "alb_", "mid_", "hib_", "cake", "buttress", "stonetrim", "mainwall", "wood_lvl",
)


def ensure_keep_textures(zone_dir):
    orig = zone_dir / "textures" / "original"
    x4 = zone_dir / "textures" / "x4plus"
    if not orig.is_dir():
        return
    x4.mkdir(parents=True, exist_ok=True)
    for src in orig.glob("*.png"):
        name = src.name.lower()
        if not any(mark in name for mark in TEXTURE_MARKERS):
            continue
        dest = x4 / src.name
        if not dest.is_file() or dest.stat().st_size != src.stat().st_size:
            shutil.copy2(src, dest)


def needed_realm_stems(pieces):
    needed = set()
    for piece in pieces:
        needed.add((piece["stem"], piece["realm"]))
    return needed


def write_realm_models(zone_dir, pieces):
    models_dir = zone_dir / "models"
    written = {}
    for stem, realm in sorted(needed_realm_stems(pieces)):
        src = models_dir / ("%s.gltf" % stem)
        if not src.is_file():
            raise FileNotFoundError(src)
        dest = models_dir / ("%s.gltf" % kk.realm_stem(stem, realm))
        info = kk.filter_keep_gltf(src, dest, realm)
        written[(stem, realm)] = {
            "file": dest,
            "doors": len(info["doors"]),
            "roots": info["roots"],
        }
        if stem == "mainkeep_lvl4" and not any("keep" in (n or "").lower() or "undam" in (n or "").lower()
                                               or "static" in (n or "").lower() or n.startswith("collision")
                                               for n in info["roots"]):
            raise RuntimeError("%s has no visible keep mesh after the %s skin filter" % (dest.name, kk.REALM_SUFFIX[realm]))
    return written


def is_piece_model(file_name):
    stem = Path(file_name or "").stem.lower()
    if stem in kk.PIECE_STEMS or stem in kk.BORDER_STEMS:
        return True
    for base in kk.PIECE_STEMS:
        for suffix in kk.REALM_SUFFIX.values():
            if stem == "%s_%s" % (base, suffix):
                return True
    nif_like = str(file_name or "")
    return nif_like.startswith("killaloe:border") or nif_like.startswith("killaloe:n-border") or nif_like.startswith("killaloe:hborder")


def ensure_piece_models(zone, zone_dir, pieces, written):
    zone["models"] = [m for m in zone["models"]
                      if not is_piece_model(m.get("file")) and not str(m.get("nif") or "").startswith("killaloe:")]
    index = {}
    for stem, realm in sorted(needed_realm_stems(pieces)):
        rstem = kk.realm_stem(stem, realm)
        gltf = zone_dir / "models" / ("%s.gltf" % rstem)
        if not gltf.is_file():
            raise FileNotFoundError(gltf)
        tris, col = MODEL_STATS[stem]
        doors = written[(stem, realm)]["doors"]
        zone["models"].append({
            "nif": "killaloe:%s" % rstem,
            "name": "%s %s" % (kk.PIECE_DISPLAY[stem], kk.REALM_LABEL[realm]),
            "file": "models/%s.gltf" % rstem,
            "triangles": tris,
            "collision_triangles": col,
            "ambient_only": False,
            "warnings": [],
            "doors": doors,
        })
        index[(stem, realm)] = len(zone["models"]) - 1
    return index


def patch_zone(dest_zone):
    dest_zone = Path(dest_zone)
    ensure_keep_textures(dest_zone)
    zpath = dest_zone / "zone.json"
    zone = json.loads(zpath.read_text(encoding="utf-8"))
    pieces = kk.all_piece_fixtures()
    written = write_realm_models(dest_zone, pieces)
    index = ensure_piece_models(zone, dest_zone, pieces, written)
    old_ids = set(kk.OLD_KEEP_FIXTURE_IDS)
    keep_names = (
        "Albion Portal Keep", "Midgard Portal Keep", "Hibernia Portal Keep", "Dun Killaloe",
    )
    fixtures = []
    for f in zone["fixtures"]:
        fid = int(f.get("id") or 0)
        name = str(f.get("name") or "")
        if fid in old_ids or 5001 <= fid <= 5100:
            continue
        if any(name.startswith(prefix) for prefix in keep_names):
            continue
        fixtures.append(f)
    used = {f.get("id") for f in fixtures}
    fid0 = max(used) + 1 if used else 5001
    dun_main = 0
    for i, piece in enumerate(pieces):
        fid = piece["id"]
        if fid in used or fid in old_ids:
            fid = fid0 + i
        used.add(fid)
        row = {
            "m": index[(piece["stem"], piece["realm"])],
            "name": piece["name"],
            "pos": piece["pos"],
            "yaw": piece["yaw"],
            "scale": 1.0,
            "collide": True,
            "radius": 0,
            "id": fid,
        }
        if piece.get("door_ids"):
            row["door_ids"] = {str(k): v for k, v in piece["door_ids"].items()}
        fixtures.append(row)
        if piece["stem"] == "mainkeep_lvl4":
            dun_main += 1
            if Path(zone["models"][row["m"]]["file"]).stem != "mainkeep_lvl4_mid":
                raise RuntimeError("Dun Killaloe main keep is %s, not mainkeep_lvl4_mid" % zone["models"][row["m"]]["file"])
    if dun_main != 1:
        raise RuntimeError("Dun Killaloe must place exactly one mainkeep_lvl4, got %s" % dun_main)
    zone["fixtures"] = fixtures
    zpath.write_text(json.dumps(zone, indent=1) + "\n", encoding="utf-8")
    print(dest_zone, "pieces", len(pieces), "models", len(zone["models"]), "fixtures", len(zone["fixtures"]))
    return pieces


def main(argv):
    dirs = [Path(a) for a in argv[1:]]
    if not dirs:
        raise SystemExit("usage: apply_killaloe_keeps.py <zone253-dir> ...")
    for d in dirs:
        if not (d / "zone.json").is_file():
            raise SystemExit("no zone.json in %s" % d)
        if not (d / "models" / "mainkeep_lvl4.gltf").is_file():
            raise SystemExit("missing converted pieces in %s" % d)
        patch_zone(d)


if __name__ == "__main__":
    main(sys.argv)
