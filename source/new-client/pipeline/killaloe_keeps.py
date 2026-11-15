"""Killaloe (region 253) keep buildings.

Zone 237's fixture table has no keep NIFs. Its nifs.csv still lists the small
Abermenai front keeps (bfrontkeep1 / nfrontkeep1 / Hfrontkeep) and bfrontkeep2,
which is the same ~50 x 63 m footprint as bfrontkeep1. Killaloe's painted ground
and captured staff pads are the size of the realm border keeps:

  Albion portal staff 113 x 94 m, pad ~144 m  -> borderkeep1 128 x 74 m
  Midgard portal staff  52 x 84 m, pad ~124 m  -> n-borderkeep1 128 x 77 m
  Hibernia portal staff 76 x 55 m, pad ~128 m  -> hborderkeep1 100 x 94 m
  Dun Killaloe staff    55 x 76 m, pad ~204 m  -> hborderkeep1 (Hibernia Dun)

Those NIFs live in zones/Nifs, not in dat237. This module names them so a zone 253
build can swap the small front-keep fixtures without moving staff.
"""

# zone.json fixture id -> (gltf stem, fixture name)
KEEP_FIXTURES = {
    1: ("borderkeep1", "Albion Portal Keep"),
    2: ("n-borderkeep1", "Midgard Portal Keep"),
    3: ("hborderkeep1", "Dun Killaloe"),
    414: ("hborderkeep1", "Hibernia Portal Keep"),
}

# Already-converted zone to copy each glTF from (same nif_static output).
KEEP_SOURCES = {
    "borderkeep1": 11,
    "n-borderkeep1": 102,
    "hborderkeep1": 206,
}
