"""The input folder for OpenDAoC-BuildNav's --zone-files, made from a converted zone's zone.json.

A swapped zone (Killaloe in zone 253) keeps the classic client's zone id, so BuildNav would read the old zone's
fixtures. This writes the new zone's own files into one folder:

- fixtures.csv and nifs.csv from zone.json, so the navmesh has exactly the fixtures the new client draws (trees
  with their trunk radius, rope bridges, the keep pieces at their pads with their doors)
- bound.csv, sector.dat and water.pcx copied from the retail zone's csvNNN.mpk / datNNN.mpk

terrain.pcx and offset.pcx are left out on purpose: BuildNav's --heights gives it the converted ground.

    python navmesh_zone_input.py --zone-json <zone253>/zone.json --retail-zone <retail>/frontiers/zones/zone237
        --nif-dirs "<retail>/zones/nifs;<retail>/frontiers/NIFS" --out <folder>

Then on Windows:

    OpenDAoC-BuildNav.exe --zones=253 --heights=253=<zone253>/heights.f32 --zone-files=253=<folder>
        --nif-dirs="<retail>/zones/nifs;<retail>/frontiers/NIFS"
"""
import argparse
import json
import math
import re
import sys
from pathlib import Path

import daoc_mpk

M = 0.025
REALM_SUFFIX = re.compile(r"_(alb|mid|hib)$", re.I)
COPIED = ("bound.csv", "sector.dat", "water.pcx")

FIXTURE_HEADER = (
    "Fixtures,,,,,,,,NIF,Collide,,On\n"
    "ID,NIF #,Textual Name,X,Y,Z,A,Scale,Collide,Radius,Animate,Ground,Flip,Cave,Unique ID,"
    "3D Angle,3D Axis X,3D Axis Y,3D Axis Z\n"
)
NIF_HEADER = (
    "Grid Nifs,,,Ambient,Merlin Data\n"
    "NIF,Textual Name,Filename,Only,Shadow,Color,Animate,Collide,Ground,MinAngle,MaxAngle,MinScale,MaxScale,"
    "Radius,LOD 1,LOD 2,LOD 3,LOD 4,Ref Height,Ref Width,Unique,Local,Terrain\n"
)


def model_nif_file(model):
    """The NIF a zone.json model was converted from: models/gatehouse_lvl4_mid.gltf is gatehouse_lvl4.nif."""
    stem = Path(model.get("file") or "").stem
    if str(model.get("nif", "")).startswith("killaloe:") or model.get("doors") is not None:
        stem = REALM_SUFFIX.sub("", stem)
    return stem + ".nif" if stem else None


def nif_exists(nif, nif_dirs):
    """True if a NIF is in one of the folders, loose or as <name>.npk (names compared without case)."""
    if not nif_dirs:
        return True
    want = {nif.lower(), Path(nif).with_suffix(".npk").name.lower()}
    for d in nif_dirs:
        if any(p.name.lower() in want for p in Path(d).iterdir()):
            return True
    return False


def safe_name(text):
    return str(text).replace(",", " ").strip() or "fixture"


def fixture_angle(yaw):
    """zone.json yaw is minus the DAoC fixture angle. Returns (whole degrees 0-359, exact radians)."""
    a = (-float(yaw)) % 360.0
    return int(round(a)) % 360, math.radians(a)


def build_rows(zone, nif_dirs=None):
    """(nif rows, fixture rows, notes). Each model gets its own NIF row; a radius-only model whose NIF the client
    doesn't have borrows one that exists, because BuildNav only places a fixture whose NIF loads."""
    models = zone["models"]
    fixtures = zone["fixtures"]
    used = sorted({f["m"] for f in fixtures})
    radius_only = {m for m in used if all(f.get("radius", 0) > 0 and not f.get("collide") for f in fixtures if f["m"] == m)}
    files = {m: model_nif_file(models[m]) for m in used}
    found = {m: bool(files[m]) and nif_exists(files[m], nif_dirs) for m in used}
    stand_in = next((files[m] for m in used if m in radius_only and found[m]), None)
    notes = []
    nif_rows = []
    nif_id = {}
    for i, m in enumerate(used):
        nif = files[m]
        if not found[m]:
            if m in radius_only and stand_in:
                notes.append(f"{nif or models[m].get('name')}: not in the NIF folders, its trunk cylinders use {stand_in}")
                nif = stand_in
            else:
                notes.append(f"{nif or models[m].get('name')}: not in the NIF folders, BuildNav will skip its fixtures")
        nif_id[m] = 1 + i
        nif_rows.append(f"{nif_id[m]},{safe_name(models[m].get('name'))},{nif},0,0,0,0,0,0,0,0,100,100,0,0,0,0,0,0,0,0,0,0\n")
    fix_rows = []
    for f in fixtures:
        x, z_up, y = (float(v) / M for v in f["pos"])
        a_deg, a_rad = fixture_angle(f.get("yaw", 0.0))
        scale = int(round(float(f.get("scale", 1.0)) * 100))
        collide = 1 if f.get("collide") else 0
        radius = int(round(float(f.get("radius", 0))))
        fix_rows.append(
            f"{int(f['id'])},{nif_id[f['m']]},{safe_name(f.get('name'))},{x:.2f},{y:.2f},{z_up:.2f},{a_deg},{scale},"
            f"{collide},{radius},0,0,0,0,0,{a_rad:.6f},0.000000,0.000000,-1.000000\n")
    return nif_rows, fix_rows, notes


def retail_file(retail_zone, name):
    """One file from the retail zone's csvNNN.mpk, else its datNNN.mpk (BuildNav's own order)."""
    retail_zone = Path(retail_zone)
    for pattern in ("csv*.mpk", "dat*.mpk"):
        for archive in sorted(retail_zone.glob(pattern)):
            arc = daoc_mpk.MpakArchive(archive)
            if name.lower() in arc.entries:
                return arc.read(name)
    return None


def write_folder(zone_json, retail_zone, out, nif_dirs=None):
    zone = json.loads(Path(zone_json).read_text(encoding="utf-8"))
    out = Path(out)
    out.mkdir(parents=True, exist_ok=True)
    nif_rows, fix_rows, notes = build_rows(zone, nif_dirs)
    (out / "nifs.csv").write_text(NIF_HEADER + "".join(nif_rows), encoding="ascii", newline="\r\n")
    (out / "fixtures.csv").write_text(FIXTURE_HEADER + "".join(fix_rows), encoding="ascii", newline="\r\n")
    for name in COPIED:
        data = retail_file(retail_zone, name) if retail_zone else None
        if data is None:
            notes.append(f"{name}: not in the retail zone, BuildNav reads the classic zone's")
            continue
        (out / name).write_bytes(data)
    return {"fixtures": len(fix_rows), "nifs": len(nif_rows), "notes": notes}


def main(argv=None):
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--zone-json", required=True)
    ap.add_argument("--retail-zone", help="retail zone folder with csvNNN.mpk and datNNN.mpk")
    ap.add_argument("--nif-dirs", default="", help="';'-separated NIF folders, to check every NIF is there")
    ap.add_argument("--out", required=True)
    args = ap.parse_args(argv)
    dirs = [d for d in args.nif_dirs.split(";") if d.strip()]
    info = write_folder(args.zone_json, args.retail_zone, args.out, dirs)
    print(f"{info['fixtures']} fixtures, {info['nifs']} NIFs -> {args.out}")
    for n in info["notes"]:
        print("note:", n)
    return 0


if __name__ == "__main__":
    sys.exit(main())
