"""zone.json fixtures become BuildNav fixtures.csv / nifs.csv rows.

    python -m unittest test_navmesh_zone_input
"""
import tempfile
import unittest
from pathlib import Path

import navmesh_zone_input as nz


def zone():
    return {
        "models": [
            {"nif": 431, "name": "H_ropeBrdge_F", "file": "models/h_ropebrdge_f.gltf"},
            {"nif": 1000000, "name": "tree cluster - Hoak tree tree", "file": "models/hoaktree.gltf"},
            {"nif": 402, "name": "Tree-Elm1", "file": "models/elm1.gltf"},
            {"nif": "killaloe:gatehouse_lvl4_mid", "name": "Gatehouse lvl4 Midgard", "file": "models/gatehouse_lvl4_mid.gltf", "doors": 4},
        ],
        "fixtures": [
            {"m": 0, "name": "H_ropeBrdge_F", "pos": [456.625, 221.85, 525.1], "yaw": -37.0, "scale": 1.0, "collide": True, "radius": 0, "id": 1},
            {"m": 1, "name": "oak", "pos": [10.0, 200.0, 20.0], "yaw": 12.5, "scale": 1.4, "collide": False, "radius": 10, "id": 2},
            {"m": 2, "name": "elm", "pos": [11.0, 200.0, 21.0], "yaw": 0.0, "scale": 1.0, "collide": False, "radius": 8, "id": 3},
            {"m": 3, "name": "Dun Killaloe Gatehouse", "pos": [800.0, 219.2, 810.0], "yaw": -90.0, "scale": 1.0, "collide": True, "radius": 0, "id": 5036},
        ],
    }


class RowsTest(unittest.TestCase):
    def test_keep_pieces_use_the_realmless_retail_nif(self):
        self.assertEqual(nz.model_nif_file(zone()["models"][3]), "gatehouse_lvl4.nif")
        self.assertEqual(nz.model_nif_file(zone()["models"][0]), "h_ropebrdge_f.nif")

    def test_fixture_rows_are_zone_local_units_with_the_daoc_angle(self):
        _nifs, rows, _ = nz.build_rows(zone())
        cols = rows[0].strip().split(",")
        self.assertEqual(len(cols), 19)
        self.assertEqual(cols[3:7], ["18265.00", "21004.00", "8874.00", "37"])
        self.assertAlmostEqual(float(cols[15]), 0.645772, places=5)
        self.assertEqual(cols[16:], ["0.000000", "0.000000", "-1.000000"])
        gate = rows[3].strip().split(",")
        self.assertEqual((gate[0], gate[6], gate[8]), ("5036", "90", "1"))
        oak = rows[1].strip().split(",")
        self.assertEqual((oak[6], oak[7], oak[8], oak[9]), ("348", "140", "0", "10"))

    def test_a_missing_tree_nif_borrows_one_that_loads(self):
        with tempfile.TemporaryDirectory() as tmp:
            for n in ("Elm1.npk", "H_ropeBrdge_F.npk", "GateHouse_lvl4.npk"):
                Path(tmp, n).write_bytes(b"")
            nifs, _rows, notes = nz.build_rows(zone(), [tmp])
        self.assertIn(",elm1.nif,", nifs[1])
        self.assertTrue(any("hoaktree.nif" in n for n in notes))


if __name__ == "__main__":
    unittest.main()
