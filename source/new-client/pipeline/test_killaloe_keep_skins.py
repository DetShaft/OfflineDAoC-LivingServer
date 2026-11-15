"""Killaloe keep pieces take their realm's textures and draw undamaged, without the black vertex colours.

    python -m unittest test_killaloe_keep_skins
"""
import unittest

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
