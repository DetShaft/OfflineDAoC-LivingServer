"""Place Killaloe's live NF keep pieces into zone 253.

    python apply_killaloe_keeps.py <zone253-dir> [<zone253-dir> ...]

Retargets fixtures 1, 2, 3 and 414 off the border keeps, drops those models so
they do not stack, and writes one fixture per captured 0x6C piece. Does not
move staff. Does not start a server or write the played database.
"""
import json
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


def ensure_piece_models(zone, dest_zone):
    models = zone["models"]
    kept = []
    for m in models:
        stem = Path(m.get("file") or "").stem.lower()
        if stem in kk.BORDER_STEMS or str(m.get("nif") or "").startswith("killaloe:border") or str(m.get("nif") or "").startswith("killaloe:n-border") or str(m.get("nif") or "").startswith("killaloe:hborder"):
            continue
        kept.append(m)
    zone["models"] = kept
    index = {}
    for i, m in enumerate(zone["models"]):
        stem = Path(m.get("file") or "").stem.lower()
        if stem in kk.PIECE_STEMS:
            index[stem] = i
    for stem in kk.PIECE_STEMS:
        gltf = dest_zone / "models" / f"{stem}.gltf"
        if not gltf.is_file():
            raise FileNotFoundError(gltf)
        tris, col = MODEL_STATS[stem]
        if stem in index:
            m = zone["models"][index[stem]]
            m["triangles"] = tris
            m["collision_triangles"] = col
            continue
        zone["models"].append({
            "nif": f"killaloe:{stem}",
            "name": kk.PIECE_DISPLAY[stem],
            "file": f"models/{stem}.gltf",
            "triangles": tris,
            "collision_triangles": col,
            "ambient_only": False,
            "warnings": [],
            "doors": 0,
        })
        index[stem] = len(zone["models"]) - 1
    return index


def patch_zone(dest_zone):
    dest_zone = Path(dest_zone)
    zpath = dest_zone / "zone.json"
    zone = json.loads(zpath.read_text(encoding="utf-8"))
    index = ensure_piece_models(zone, dest_zone)
    old_ids = set(kk.OLD_KEEP_FIXTURE_IDS)
    fixtures = [f for f in zone["fixtures"] if f.get("id") not in old_ids]
    used = {f.get("id") for f in fixtures}
    pieces = kk.all_piece_fixtures()
    fid0 = max(used) + 1 if used else 5001
    for i, piece in enumerate(pieces):
        fid = piece["id"]
        if fid in used or fid in old_ids:
            fid = fid0 + i
        used.add(fid)
        fixtures.append({
            "m": index[piece["stem"]],
            "name": piece["name"],
            "pos": piece["pos"],
            "yaw": piece["yaw"],
            "scale": 1.0,
            "collide": True,
            "radius": 0,
            "id": fid,
        })
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
