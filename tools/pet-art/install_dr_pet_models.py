"""Draw the Necromancer and Hunter pets with the Darkness Rising models, as live does (issues #117, #119).

  check   : python install_dr_pet_models.py check      (reads only; says what install would do)
  install : python install_dr_pet_models.py install
  rollback: python install_dr_pet_models.py rollback <backup-folder>

Live's monsters.csv gives these monster rows Catacombs columns (Cata NIF and Cata skins) that point at the
revamped figures: DR_ZombiePet, DR_ReanimatedServant, DR_NecroServant, DR_GreaterNecro, DR_Abomination and
Cata_wolf. Our client ships every one of those NIFs and skins, but its monsters.csv leaves the Catacombs
columns empty on monster rows, so the pets fall back to the old figures (the abomination's gold body is
Abomination_Body.dds on DR_Abomination).

Nothing stock is changed:
  client gamedata.mpk (monsters.csv)   appends one private row per pet, a byte-for-byte clone of the stock row
                                       below with live's Catacombs values filled in (columns 67, 68, 70, 81)
  server NpcTemplate Model             the pet templates move from the stock model to the new row
Only the values listed in PETS are copied from live; no live file is read or copied. Run with the launcher,
client and server CLOSED.

The new client builds pet models by model number from the base columns (build_monsters.py --pets), which
are unchanged, so it keeps drawing the old figures until its pipeline reads the Catacombs columns; rebuild its
pet content after installing so it knows the new numbers.
"""
from __future__ import annotations

import json
import shutil
import sqlite3
import sys
import time
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
from install_pet_art import CLIENT, DB, archive, append_rows, check_catalog_order, csv_table, require, sha  # noqa: E402

# Catacombs columns of a monsters.csv row (0-based): Cata NIF, Cata body skin, Cata head skin, and the column
# live sets to 100 on the larger figures.
CATA_NIF, CATA_BODY, CATA_HEAD, CATA_81 = 67, 68, 70, 81

PETS = [
    # label, stock row cloned (our client), templates moved and the model they must have now, live's Catacombs values
    dict(label="Necromancer Zombie Servant (DR)", clone="110", templates={200: "110", 201: "110", 202: "110"},
         cata={CATA_NIF: "823", CATA_BODY: "6810", CATA_HEAD: "6809"}),
    dict(label="Necromancer Reanimated Servant (DR)", clone="2186", templates={203: "467"},
         cata={CATA_NIF: "822", CATA_BODY: "6810", CATA_HEAD: "6809"}),
    dict(label="Necromancer Necroservant (DR)", clone="2176", templates={204: "106"},
         cata={CATA_NIF: "819", CATA_BODY: "6800", CATA_HEAD: "6801"}),
    dict(label="Necromancer Greater Necroservant (DR)", clone="2177", templates={205: "440"},
         cata={CATA_NIF: "820", CATA_BODY: "6802", CATA_HEAD: "6803", CATA_81: "100"}),
    dict(label="Necromancer Abomination (DR)", clone="653", templates={206: "653"},
         cata={CATA_NIF: "757", CATA_BODY: "6631", CATA_HEAD: "6630", CATA_81: "100"}),
    dict(label="Hunter Spirit Wolf (Cata)", clone="648",
         templates={36: "648", 37: "648", 38: "648", 39: "648", 40: "648", 41: "648", 42: "648"},
         cata={CATA_NIF: "529", CATA_BODY: "3073"}),
]


def plan(gentries, con):
    """The rows to append and the template moves, checked against the client and the database."""
    gfiles = {e.name.lower(): e for e in gentries}
    tables = {n: csv_table(gfiles[n].data) for n in ("monsters.csv", "monnifs.csv", "skins.csv")}
    first = {}
    for name, table in tables.items():
        first[name] = {}
        for row in table:
            if row and row[0].strip().isdigit():
                first[name].setdefault(row[0].strip(), row)
    monsters = first["monsters.csv"]
    next_id = max(int(i) for i in monsters) + 1
    rows, moves = [], {}
    for pet in PETS:
        stock = monsters.get(pet["clone"])
        require(stock is not None, f"stock monsters.csv row {pet['clone']} missing")
        for column, value in pet["cata"].items():
            require(len(stock) > column and stock[column].strip() == "",
                    f"row {pet['clone']} column {column} is already set ({stock[column] if len(stock) > column else '-'}):"
                    " this client already draws it some other way")
        require(pet["cata"][CATA_NIF] in first["monnifs.csv"], f"monnifs row {pet['cata'][CATA_NIF]} missing")
        for column in (CATA_BODY, CATA_HEAD):
            if column in pet["cata"]:
                require(pet["cata"][column] in first["skins.csv"], f"skins row {pet['cata'][column]} missing")
        nif = first["monnifs.csv"][pet["cata"][CATA_NIF]][2]
        require((CLIENT / "figures" / f"{nif}.nif").exists() or (CLIENT / "figures" / f"{nif}.NIF").exists(),
                f"figures\\{nif}.nif missing")
        row = list(stock)
        row[0], row[1] = str(next_id), pet["label"]
        for column, value in pet["cata"].items():
            row[column] = value
        rows.append(row)
        for template, expected in pet["templates"].items():
            found = con.execute("SELECT Model FROM NpcTemplate WHERE TemplateId = ?", (template,)).fetchone()
            require(found is not None, f"NpcTemplate {template} missing")
            require(str(found[0]) == expected, f"NpcTemplate {template} model is {found[0]}, expected {expected}")
            moves[template] = (expected, str(next_id))
        next_id += 1
    return rows, moves


def check():
    gname, gentries = archive.read((CLIENT / "gamedata.mpk").read_bytes())
    con = sqlite3.connect(f"file:{DB}?mode=ro", uri=True)
    rows, moves = plan(gentries, con)
    con.close()
    for row in rows:
        print(f"new model {row[0]}: {row[1]} (clone of the stock row, Cata NIF {row[CATA_NIF]}, body {row[CATA_BODY]})")
    for template, (old, new) in sorted(moves.items()):
        print(f"NpcTemplate {template}: model {old} -> {new}")


def install():
    stamp = time.strftime("%Y%m%d-%H%M%S")
    backup = HERE / "install-backups" / f"dr-pet-models-{stamp}"
    backup.mkdir(parents=True)
    gamedata_path = CLIENT / "gamedata.mpk"
    shutil.copy2(gamedata_path, backup / gamedata_path.name)
    manifest = {"created": stamp, "files": {"gamedata.mpk": sha(gamedata_path.read_bytes())}, "templates": {}}

    gname, gentries = archive.read(gamedata_path.read_bytes())
    con = sqlite3.connect(DB)
    rows, moves = plan(gentries, con)
    gfiles = {e.name.lower(): e for e in gentries}
    replaced = {"monsters.csv": append_rows(gfiles["monsters.csv"].data, rows)}
    check_catalog_order(replaced["monsters.csv"], None)
    gout = [archive.Entry(e.name, replaced.get(e.name.lower(), e.data), e.timestamp, e.flags) for e in gentries]
    gblob = archive.write(gname, gout)
    archive.verify_memory_image(gblob)
    for old, fresh in zip(gentries, archive.read(gblob)[1]):
        if old.name.lower() not in replaced:
            require(old == fresh, f"unrelated gamedata entry changed: {old.name}")

    gamedata_path.write_bytes(gblob)
    with con:
        for template, (old, new) in moves.items():
            con.execute("UPDATE NpcTemplate SET Model = ? WHERE TemplateId = ?", (new, template))
            manifest["templates"][str(template)] = old
    con.close()
    manifest["rows"] = [row[:2] for row in rows]
    manifest["after"] = {"gamedata.mpk": sha(gblob)}
    (backup / "manifest.json").write_text(json.dumps(manifest, indent=2))
    print(json.dumps(manifest, indent=2))
    print(f"\nInstalled. Rollback with:\n  python install_dr_pet_models.py rollback \"{backup}\"")


def rollback(folder: Path):
    manifest = json.loads((folder / "manifest.json").read_text())
    shutil.copy2(folder / "gamedata.mpk", CLIENT / "gamedata.mpk")
    con = sqlite3.connect(DB)
    with con:
        for template, model in manifest["templates"].items():
            con.execute("UPDATE NpcTemplate SET Model = ? WHERE TemplateId = ?", (model, int(template)))
    con.close()
    for relative, digest in manifest["files"].items():
        require(sha((CLIENT / relative).read_bytes()) == digest, f"rollback hash mismatch: {relative}")
    print("Rolled back", folder)


if __name__ == "__main__":
    if len(sys.argv) >= 2 and sys.argv[1] == "check":
        check()
    elif len(sys.argv) >= 2 and sys.argv[1] == "install":
        install()
    elif len(sys.argv) >= 3 and sys.argv[1] == "rollback":
        rollback(Path(sys.argv[2]))
    else:
        print(__doc__)
