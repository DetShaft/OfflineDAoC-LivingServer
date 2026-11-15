"""Swap Killaloe's small Abermenai front-keep fixtures for the realm border keeps.

    python apply_killaloe_keeps.py <zone253-dir> [<zone253-dir> ...]

Copies already-converted borderkeep glTFs and any missing textures from other
built zones, then retargets fixtures 1, 2, 3 and 414. Does not move staff.
"""
import json
import shutil
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
from assets_pipeline import ROOT
import killaloe_keeps as kk


def zone_roots():
    roots = []
    extra = Path(r"e:\Offline DAOC\new-client\godot\content\zones")
    for root in (extra, ROOT / "godot" / "content" / "zones"):
        if root not in roots:
            roots.append(root)
    return roots


def copy_model(stem, dest_zone):
    src_zone = None
    src_gltf = None
    for root in zone_roots():
        candidate = root / f"zone{kk.KEEP_SOURCES[stem]:03d}" / "models" / f"{stem}.gltf"
        if candidate.is_file():
            src_gltf = candidate
            src_zone = candidate.parent.parent
            break
    if src_gltf is None:
        raise FileNotFoundError(stem)
    dmodels = dest_zone / "models"
    dmodels.mkdir(exist_ok=True)
    shutil.copy2(src_gltf, dmodels / f"{stem}.gltf")
    src_bin = src_gltf.with_suffix(".bin")
    if src_bin.is_file():
        shutil.copy2(src_bin, dmodels / f"{stem}.bin")
    gltf = json.loads(src_gltf.read_text(encoding="utf-8"))
    copied = []
    for image in gltf.get("images") or []:
        uri = image.get("uri") or ""
        name = Path(uri).name
        if not name:
            continue
        for folder in ("original", "x4plus"):
            src = src_zone / "textures" / folder / name
            dst_dir = dest_zone / "textures" / folder
            dst_dir.mkdir(parents=True, exist_ok=True)
            dst = dst_dir / name
            if src.is_file() and not dst.is_file():
                shutil.copy2(src, dst)
                copied.append(f"{folder}/{name}")
            elif folder == "x4plus" and not dst.is_file():
                orig = dest_zone / "textures" / "original" / name
                if orig.is_file():
                    shutil.copy2(orig, dst)
                    copied.append(f"x4plus/{name} (from original)")
    tris = None
    col = None
    for m in json.loads((src_zone / "zone.json").read_text(encoding="utf-8"))["models"]:
        if Path(m["file"]).stem.lower() == stem:
            tris = m.get("triangles")
            col = m.get("collision_triangles")
            break
    return tris, col, copied


def ensure_model(zone, stem, display_name, triangles, collision):
    models = zone["models"]
    for i, m in enumerate(models):
        if Path(m["file"]).stem.lower() == stem:
            return i
    models.append({
        "nif": f"killaloe:{stem}",
        "name": display_name,
        "file": f"models/{stem}.gltf",
        "triangles": triangles,
        "collision_triangles": collision or 0,
        "ambient_only": False,
        "warnings": [],
        "doors": 0,
    })
    return len(models) - 1


def patch_zone(dest_zone):
    dest_zone = Path(dest_zone)
    zpath = dest_zone / "zone.json"
    zone = json.loads(zpath.read_text(encoding="utf-8"))
    model_index = {}
    for stem in dict.fromkeys(src for src, _ in kk.KEEP_FIXTURES.values()):
        tris, col, copied = copy_model(stem, dest_zone)
        name = {
            "borderkeep1": "Albion Border Keep",
            "n-borderkeep1": "Midgard Border Keep",
            "hborderkeep1": "Hibernia Border Keep",
        }[stem]
        model_index[stem] = ensure_model(zone, stem, name, tris, col)
        print(dest_zone, stem, "model", model_index[stem], "new textures", copied)

    changed = []
    for fix in zone["fixtures"]:
        mapping = kk.KEEP_FIXTURES.get(fix.get("id"))
        if mapping is None:
            continue
        stem, name = mapping
        old = fix.get("m"), fix.get("name")
        fix["m"] = model_index[stem]
        fix["name"] = name
        changed.append((fix["id"], old, fix["m"], name, list(fix["pos"]), fix.get("yaw"), fix.get("scale")))
    zpath.write_text(json.dumps(zone, indent=1) + "\n", encoding="utf-8")
    for row in changed:
        print("fixture", row)
    return changed


def main(argv):
    dirs = [Path(a) for a in argv[1:]]
    if not dirs:
        raise SystemExit("usage: apply_killaloe_keeps.py <zone253-dir> ...")
    for d in dirs:
        if not (d / "zone.json").is_file():
            raise SystemExit(f"no zone.json in {d}")
        patch_zone(d)


if __name__ == "__main__":
    main(sys.argv)
