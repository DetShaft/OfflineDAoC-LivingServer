"""Killaloe keep pieces take their realm's textures and draw undamaged, without the black vertex colours.

    python -m unittest test_killaloe_keep_skins
"""
import json
import tempfile
import unittest
from pathlib import Path

import killaloe_keeps as kk


class RealmTextureTest(unittest.TestCase):
    def test_walls_and_cake_follow_the_realm(self):
        self.assertEqual(kk.realm_texture("alb_mainwall_lvl4", 2), "mid_mainwall_lvl4")
        self.assertEqual(kk.realm_texture("alb_mainwall_lvl4", 3), "hib_mainwall_lvl4")
        self.assertEqual(kk.realm_texture("alb_cake", 2), "midcake")
        self.assertEqual(kk.realm_texture("alb_cake", 3), "hib_cake")
        self.assertEqual(kk.realm_texture("alb_cake", 1), "alb_cake")

    def test_lightmaps_use_the_undamaged_realm_map(self):
        self.assertEqual(kk.realm_texture("_lm_gatehouse_alb_cake_dam_01", 1), "_lm_gatehouse_alb_cake_un_01")
        self.assertEqual(kk.realm_texture("_lm_gatehouse_alb_cake_dam_01__x2", 3), "_lm_gatehouse_hib_cake_un_01__x2")
        self.assertEqual(kk.realm_texture("_lm_gatehouse01", 2), "_lm_gatehouse01")

    def test_a_realm_texture_the_client_lacks_stays_albion(self):
        available = {"mid_mainwall_lvl4"}
        self.assertEqual(kk.realm_texture("alb_stone_floor", 2, available), "alb_stone_floor")
        self.assertEqual(kk.realm_texture("alb_mainwall_lvl4", 2, available), "mid_mainwall_lvl4")

    def test_material_drops_vertex_colours_and_damage_decal(self):
        spec = {"slots": {"base": {"tex": "alb_cake", "uv": 0},
                          "dark": {"tex": "_lm_keep_alb_cake_dam_01", "uv": 1},
                          "decal": {"tex": "damage", "uv": 2}},
                "vertex_colors": True}
        out = kk.realm_material(spec, 2)
        self.assertFalse(out["vertex_colors"])
        self.assertNotIn("decal", out["slots"])
        self.assertEqual(out["slots"]["base"]["tex"], "midcake")
        self.assertEqual(out["slots"]["dark"]["tex"], "_lm_keep_mid_cake_un_01")
        self.assertTrue(spec["vertex_colors"])          # the source spec is not changed


if __name__ == "__main__":
    unittest.main()


class DamagedToppersTest(unittest.TestCase):
    """The damaged topper is named like the undamaged one; its damaged-cake lightmap gives it away."""

    def _doc(self):
        def mat(lm):
            return {"slots": {"base": {"tex": "alb_cake", "uv": 0}, "dark": {"tex": lm, "uv": 1}}}
        return {
            "scene": 0, "scenes": [{"nodes": [0, 1, 2, 3]}],
            "nodes": [{"name": "!Tiers_4_Alb_topper:9", "mesh": 0}, {"name": "!Tiers_4_Alb_topper01:6", "mesh": 1},
                      {"name": "door0_58", "mesh": 2}, {"name": "gatestatic_57", "mesh": 2}],
            "meshes": [{"primitives": [{"material": 0}]}, {"primitives": [{"material": 1}]},
                       {"primitives": [{"material": 2}]}],
            "materials": [{"name": "nifmat:0"}, {"name": "nifmat:1"}, {"name": "nifmat:2"}],
            "extras": {"nif_materials": [mat("_lm_gatehouse_alb_cake_dam_01"), mat("_lm_gatehouse_alb_cake_un_01"),
                                         mat("_lm_gatehouse01")]},
        }

    def test_only_the_undamaged_topper_and_the_doors_are_kept(self):
        with tempfile.TemporaryDirectory() as tmp:
            src, dest = Path(tmp, "gatehouse_lvl4.gltf"), Path(tmp, "gatehouse_lvl4_alb.gltf")
            src.write_text(json.dumps(self._doc()), encoding="utf-8")
            info = kk.filter_keep_gltf(src, dest, 1)
        self.assertEqual(info["roots"], ["!Tiers_4_Alb_topper01:6", "door0_58", "gatestatic_57"])

    def test_meshes_from_a_damaged_branch_are_dropped(self):
        doc = self._doc()
        doc["nodes"].append({"name": "!Tiers_4_Alb_cake_ch01:9", "mesh": 2})   # lit like an undamaged piece
        doc["scenes"][0]["nodes"].append(4)
        doc["extras"]["nif_damaged_nodes"] = [4]
        with tempfile.TemporaryDirectory() as tmp:
            src, dest = Path(tmp, "straighttower_lvl4.gltf"), Path(tmp, "straighttower_lvl4_alb.gltf")
            src.write_text(json.dumps(doc), encoding="utf-8")
            info = kk.filter_keep_gltf(src, dest, 1)
        self.assertNotIn("!Tiers_4_Alb_cake_ch01:9", info["roots"])

