"""Generate the Killaloe (region 253) mob camps: killaloe_zone253_camps.sql, killaloe-camps.json and the docs table.

Owner, 2026-11-21: most of Killaloe is empty; add more patches of the existing mob types across the zone so the
battleground is a real leveling area. Every camp copies an existing field-pack row (stats, model, loot template, faction)
and only changes X/Y/Z, level and roam. Real DAoC zone files are not read: the ground height is a guess from the nearby
field spawns, and the server snaps every camp mob onto the navmesh at start (KillaloeZone253Camps), dropping any that
land off the mesh or in water.

Camps are spread so each realm's third of the zone (nearest portal keep) ends with the same number of field mobs and
the same level mix (balance changes must be even across realms).

Run from the repo root:  python source/tools/killaloe/gen_killaloe_camps.py
Deterministic (seed 253). Rewrites the three outputs.
"""
import json
import math
import random
import re
import sqlite3
from pathlib import Path

ROOT = Path(__file__).resolve().parents[3]
DATA = ROOT / 'source/tools/OfflineDaoc.Setup/data'
OUT_SQL = DATA / 'killaloe_zone253_camps.sql'
OUT_JSON = ROOT / 'source/server/GameServer/bots/autonomous/data/killaloe-camps.json'
OUT_DOC = ROOT / 'docs/KILLALOE-CAMPS.md'

PACKAGE = 'offline-killaloe-zone253-camps'
SEED = 253
# Playable square in region 253 world units (zone 8192..73728; the existing field pack spans about 15,000..69,000).
LO, HI = 16000, 70000
# Portal keeps (world X/Y from the captured keep staff) and Dun Killaloe.
PORTALS = {'Albion': (28700, 66000), 'Midgard': (68000, 44000), 'Hibernia': (19500, 22300)}
CENTER = (41000, 40500)
MIN_FROM_FIELD = 2300     # new camps go where there is nothing
MIN_FROM_KEEP_STAFF = 2600
MIN_FROM_PORTAL = 4500    # no camp on top of a realm's way in
MIN_BETWEEN_CAMPS = 5000
CAMP_RADIUS = 260         # members stand within this of the camp centre
ROAM = 350
# Type -> (template Mob_ID in the field pack, lowest level, highest level). Ranges are the pack's own.
TYPES = {
    'large rat': ('100032785', 14, 17),
    'boogey spawn': ('100032786', 13, 19),
    'forest cat': ('100032792', 16, 18),
    'clicking beetle': ('100032788', 16, 19),
    'forlorn spectre': ('100032783', 15, 20),
}
# Camp level bands, cycled per realm third so every third gets the same mix.
BANDS = [(15, 'large rat'), (16, 'boogey spawn'), (17, 'forest cat'), (17, 'clicking beetle'),
         (18, 'forlorn spectre'), (16, 'clicking beetle'), (18, 'boogey spawn'), (19, 'forlorn spectre')]

COLS = ('ClassType, TranslationId, Name, Suffix, Guild, ExamineArticle, MessageArticle, Speed, Heading, Region, Model, '
        'Size, Strength, Constitution, Dexterity, Quickness, Intelligence, Piety, Empathy, Charisma, Realm, '
        'EquipmentTemplateID, ItemsListTemplateID, NPCTemplateID, Race, Flags, AggroLevel, AggroRange, MeleeDamageType, '
        'RespawnInterval, FactionID, BodyType, HouseNumber, Brain, PathID, OwnerID, IsCloakHoodUp, Gender, '
        'VisibleWeaponSlots, LastTimeRowUpdated')


def load_world():
    """The Killaloe packs as the server applies them (field pack, keep staff, playable fixes and extras)."""
    db = sqlite3.connect(':memory:')
    first = (DATA / 'killaloe_zone253_mobs.sql').read_text(encoding='utf-8')
    cols = re.search(r'REPLACE INTO `mob` \(([^)]*)\)', first).group(1).replace('`', '')
    db.execute(f'CREATE TABLE Mob ({cols})')
    db.execute('CREATE UNIQUE INDEX mob_id ON Mob(Mob_ID)')
    for table in ('Keep(KeepID,Region,X,Y,Z,Realm,Name)', 'Door(Door_ID,InternalID,Name,Realm,X,Y,Z)',
                  'Area(Area_ID,Region,X,Y,Z,Description)'):
        db.execute('CREATE TABLE ' + table)
    for name in ('killaloe_zone253_mobs.sql', 'killaloe_zone253_keep_npcs.sql', 'killaloe_zone253_playable.sql'):
        text = (DATA / name).read_text(encoding='utf-8').replace('`', '')
        text = '\n'.join(line for line in text.split('\n') if not line.strip().startswith('--'))
        for statement in text.split(';'):
            if statement.strip():
                try:
                    db.execute(statement)
                except sqlite3.Error:
                    pass  # Keep/Door/Area columns this sketch does not model
    field = [(x, y, z, n, lvl) for x, y, z, n, lvl in db.execute(
        "SELECT X,Y,Z,Name,Level FROM Mob WHERE Region=253 AND PackageID IN "
        "('offline-killaloe-zone253','offline-killaloe-zone253-extra')")]
    staff = [(x, y) for x, y in db.execute("SELECT X,Y FROM Mob WHERE PackageID='offline-killaloe-keep-253'")]
    templates = {mob_id for (mob_id,) in db.execute(
        "SELECT Mob_ID FROM Mob WHERE PackageID='offline-killaloe-zone253'")}
    return field, staff, templates


def third(x, y):
    return min(PORTALS, key=lambda realm: math.dist((x, y), PORTALS[realm]))


def ground_guess(x, y, field):
    near = sorted(field, key=lambda f: math.dist((x, y), f[:2]))[:4]
    weights = [1.0 / max(1.0, math.dist((x, y), f[:2])) ** 2 for f in near]
    return int(round(sum(w * f[2] for w, f in zip(weights, near)) / sum(weights)))


def main():
    field, staff, templates = load_world()
    for name, (template, _, _) in TYPES.items():
        assert template in templates, f'{name} template {template} is not in the field pack'
    rng = random.Random(SEED)

    candidates = [(rng.uniform(LO, HI), rng.uniform(LO, HI)) for _ in range(40000)]
    centres = []
    for x, y in candidates:
        if any(math.dist((x, y), f[:2]) < MIN_FROM_FIELD for f in field):
            continue
        if any(math.dist((x, y), s) < MIN_FROM_KEEP_STAFF for s in staff):
            continue
        if any(math.dist((x, y), p) < MIN_FROM_PORTAL for p in PORTALS.values()):
            continue
        if any(math.dist((x, y), c) < MIN_BETWEEN_CAMPS for c in centres):
            continue
        centres.append((x, y))

    # Even out the thirds: every third ends with the same field-mob count (existing + new).
    existing = {realm: 0 for realm in PORTALS}
    for f in field:
        existing[third(f[0], f[1])] += 1
    by_third = {realm: [c for c in centres if third(*c) == realm] for realm in PORTALS}
    per_camp = 4
    # Largest total a third can reach with its own empty ground; the others are trimmed to match.
    reach = {realm: existing[realm] + per_camp * len(by_third[realm]) for realm in PORTALS}
    target = min(reach.values())
    camps = []
    for realm in PORTALS:
        mine = by_third[realm]
        # Keep the camps that are furthest from the existing spawns first (the emptiest ground).
        mine.sort(key=lambda c: -min(math.dist(c, f[:2]) for f in field))
        count = (target - existing[realm]) // per_camp
        for index, (x, y) in enumerate(sorted(mine[:count], key=lambda c: (round(c[1] / 4096), c[0]))):
            level, name = BANDS[index % len(BANDS)]
            camps.append({'realm_third': realm, 'x': int(x), 'y': int(y), 'base_level': level, 'type': name})

    rows = []
    for number, camp in enumerate(sorted(camps, key=lambda c: (c['realm_third'], c['y'], c['x'])), start=1):
        camp['id'] = f'killaloe-camp-{number:02d}'
        template, low, high = TYPES[camp['type']]
        camp['z_guess'] = ground_guess(camp['x'], camp['y'], field)
        camp['members'] = []
        for member in range(per_camp):
            angle = rng.uniform(0, 2 * math.pi)
            radius = rng.uniform(90, CAMP_RADIUS) if member else rng.uniform(0, 60)
            mx = int(camp['x'] + radius * math.cos(angle))
            my = int(camp['y'] + radius * math.sin(angle))
            level = max(low, min(high, camp['base_level'] + (-1, 0, 1, 0)[member]))
            mob_id = f'{camp["id"]}-{member + 1}'
            camp['members'].append({'mob_id': mob_id, 'x': mx, 'y': my, 'level': level})
            rows.append((mob_id, template, mx, my, ground_guess(mx, my, field), level, rng.randrange(4096)))
        camp['levels'] = [m['level'] for m in camp['members']]

    write_sql(rows, len(camps))
    write_json(camps)
    write_doc(camps, existing, per_camp)
    print(f'{len(camps)} camps, {len(rows)} mobs; field mobs per third before {existing}, after '
          f'{ {r: existing[r] + per_camp * sum(1 for c in camps if c["realm_third"] == r) for r in PORTALS} }')


def write_sql(rows, camp_count):
    lines = [
        '-- Killaloe mob camps (owner, 2026-11-21: "more patches spread across the zone to give it more of an alive feeling',
        '-- and offer it as a valid leveling area"). Generated by source/tools/killaloe/gen_killaloe_camps.py (seed 253);',
        f'-- {camp_count} camps of 4, {len(rows)} rows. Each row copies a field-pack mob (stats, model, templates, faction)',
        '-- and sets its own X/Y, level, heading and roam. Z is a guess from the nearby field spawns: the server puts every',
        '-- camp mob on the navmesh at start and drops any that are off the mesh (KillaloeZone253Camps). Own PackageID, so',
        '-- the field-pack checks, the extras check and the Abermenai clean-up leave them alone. Camp list: docs/KILLALOE-CAMPS.md.',
    ]
    for mob_id, template, x, y, z, level, heading in rows:
        select_cols = COLS.replace('Heading', str(heading))
        lines.append(
            f'INSERT OR REPLACE INTO Mob ({COLS}, Level, RoamingRange, X, Y, Z, PackageID, Mob_ID)\n'
            f' SELECT {select_cols}, {level}, {ROAM}, {x}, {y}, {z}, \'{PACKAGE}\', \'{mob_id}\'\n'
            f' FROM Mob WHERE Mob_ID=\'{template}\' AND PackageID=\'offline-killaloe-zone253\';')
    OUT_SQL.write_text('\n'.join(lines) + '\n', encoding='utf-8')


def write_json(camps):
    OUT_JSON.parent.mkdir(parents=True, exist_ok=True)
    payload = {
        '_source': 'source/tools/killaloe/gen_killaloe_camps.py. Region 253 world X/Y. z_guess is not the ground: the '
                   'server snaps camp mobs to the navmesh at start. Mob_IDs match killaloe_zone253_camps.sql.',
        'region': 253,
        'package_id': PACKAGE,
        'camps': [{k: camp[k] for k in ('id', 'type', 'realm_third', 'x', 'y', 'z_guess', 'base_level', 'levels', 'members')}
                  for camp in sorted(camps, key=lambda c: c['id'])],
    }
    OUT_JSON.write_text(json.dumps(payload, indent=1) + '\n', encoding='utf-8')


def write_doc(camps, existing, per_camp):
    lines = [
        '# Killaloe mob camps',
        '',
        'Killaloe (region 253, the 15-19 battleground) gets mob camps across the parts of the zone that had no spawns',
        '(owner, 2026-11-21). They are copies of the existing field mobs: no new mob types, models or loot.',
        '',
        '- SQL: `source/tools/OfflineDaoc.Setup/data/killaloe_zone253_camps.sql`, applied at every server start by',
        '  `KillaloeZone253Seeds` (PackageID `offline-killaloe-zone253-camps`).',
        '- Ground: the Z in the SQL is a guess. At start `KillaloeZone253Camps` moves each camp mob onto the navmesh',
        '  (land only) and removes any that are off it. Log lines: `KILLALOE_CAMPS placed=.. moved=.. dropped=..`, and',
        '  `KILLALOE_CAMP_DROPPED` for each removed mob.',
        '- Data for bot goals: `source/server/GameServer/bots/autonomous/data/killaloe-camps.json` (same camps, with members).',
        '- Regenerate: `python source/tools/killaloe/gen_killaloe_camps.py` (deterministic). Do not hand-edit the outputs.',
        '',
        'Each realm third (nearest portal keep) ends with the same number of field mobs and the same mix of camp levels.',
        '',
        '| Realm third | Field mobs before | Camps added | Field mobs after |',
        '|---|---:|---:|---:|',
    ]
    for realm in PORTALS:
        added = sum(1 for c in camps if c['realm_third'] == realm)
        lines.append(f'| {realm} | {existing[realm]} | {added} | {existing[realm] + per_camp * added} |')
    lines += ['', '| Camp | Third | Type | Levels | X | Y |', '|---|---|---|---|---:|---:|']
    for camp in sorted(camps, key=lambda c: c['id']):
        lines.append(f'| {camp["id"]} | {camp["realm_third"]} | {camp["type"]} | '
                     f'{"-".join(str(level) for level in sorted(set(camp["levels"])))} | {camp["x"]} | {camp["y"]} |')
    OUT_DOC.write_text('\n'.join(lines) + '\n', encoding='utf-8')


if __name__ == '__main__':
    main()
