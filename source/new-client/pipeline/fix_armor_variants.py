"""Issue #45: set the mesh variants in an already-built armor.json the way nif_character.cata_outfit now picks them,
without rebuilding the textures: gloves / boots from the item's Cata Gloves# / Boots# (objects.csv cols 74 / 75),
bodies on the starter mesh 1 except robes (7). Textures are left as they are.

    python fix_armor_variants.py <armor.json> <objects.csv> [--write]

Without --write it only reports what would change. objects.csv is the client's own table (gamedata), read only."""
import csv, json, sys
from pathlib import Path

import nif_character as nc


def main():
    armor_path, objects_path = Path(sys.argv[1]), Path(sys.argv[2])
    objects = {r[0]: r for r in csv.reader(objects_path.open(encoding='latin-1')) if r and r[0].isdigit()}
    doc = json.loads(armor_path.read_text(encoding='utf-8'))
    changed = {}
    for model, by_gender in doc.get('models', {}).items():
        row = objects.get(str(model))
        for parts in by_gender.values():
            for slot, entry in parts.items():
                v, tex = int(entry[0]), str(entry[1])
                want = v
                if slot == 'body' or slot in ('gloves', 'boots') and row is not None:
                    want = nc.cata_mesh(slot, tex, row)            # the same rule build_armor uses (#45)
                if want != v:
                    changed[(slot, v, want)] = changed.get((slot, v, want), 0) + 1
                    entry[0] = want
    for (slot, a, b), n in sorted(changed.items()):
        print(f'{slot:7s} {a} -> {b}: {n}')
    if '--write' in sys.argv and changed:
        armor_path.write_text(json.dumps(doc, indent=1), encoding='utf-8')
        print('written', armor_path)


if __name__ == '__main__':
    main()
