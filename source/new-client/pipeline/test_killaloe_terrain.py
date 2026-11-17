"""Path rules for borrowing Killaloe's ground into zone 253. No client files, no numpy."""
import os
import unittest
from pathlib import Path

import killaloe_terrain as kt


def _fill(folder):
    folder.mkdir(parents=True, exist_ok=True)
    for name in kt.REQUIRED:
        (folder / name).write_bytes(b'x')


class KillaloeTerrainTests(unittest.TestCase):
    def test_keeps_are_inside_zone_253_and_outside_offset_64(self):
        # The shared battleground layout. Offset 64 would start the rectangle at 524288.
        keeps = [(33089, 38271), (18362, 18257), (54053, 24680), (37301, 52362)]
        ox, oy = kt.RETAIL_OFFSET_SECTORS
        self.assertEqual((ox, oy), (64, 64))
        origin = ox * kt.SECTOR
        self.assertEqual(origin, 524288)
        self.assertEqual(oy * kt.SECTOR, 524288)
        for x, y in keeps:
            self.assertLess(x, 65536)
            self.assertLess(y, 65536)
            self.assertFalse(origin <= x < origin + 65536)
            self.assertFalse(origin <= y < origin + 65536)

    def test_borrowed_dat_has_the_ground(self):
        for key in kt.NEEDED_IN_DAT:
            self.assertIn(key, kt.BORROWED_DAT)

    def test_killaloe_props_come_from_csv237_and_abermenai_exits_go(self):
        for key in ('fixtures.csv', 'nifs.csv', 'lights.csv'):
            self.assertIn(key, kt.BORROWED_CSV)
        for key in kt.NEEDED_IN_CSV:
            self.assertIn(key, kt.BORROWED_CSV)
        self.assertEqual(kt.CSV_ARCHIVE, 'csv237.mpk')
        self.assertIn('zonejump.csv', kt.DROPPED_FROM_BATTLEGROUND)
        self.assertEqual(set(kt.BORROWED_CSV) & set(kt.DROPPED_FROM_BATTLEGROUND), set())

    def test_frontiers_zone_237_wins_over_a_zones_copy(self):
        import tempfile
        with tempfile.TemporaryDirectory() as raw:
            root = Path(raw)
            _fill(root / 'zones/zone237')
            _fill(root / 'frontiers/zones/zone237')
            _fill(root / 'zones/zone253')
            found = kt.terrain_folder([root])
            self.assertEqual(found, root / 'frontiers/zones/zone237')

    def test_zones_zone_237_is_used_when_frontiers_has_no_ground(self):
        import tempfile
        with tempfile.TemporaryDirectory() as raw:
            root = Path(raw)
            (root / 'frontiers/zones/zone237').mkdir(parents=True)
            _fill(root / 'zones/zone237')
            self.assertEqual(kt.terrain_folder([root]), root / 'zones/zone237')

    def test_a_zone_253_folder_is_never_the_terrain(self):
        import tempfile
        with tempfile.TemporaryDirectory() as raw:
            root = Path(raw)
            _fill(root / 'zones/zone253')
            _fill(root / 'frontiers/zones/zone253')
            self.assertIsNone(kt.terrain_folder([root]))

    def test_an_incomplete_folder_is_skipped(self):
        import tempfile
        with tempfile.TemporaryDirectory() as raw:
            root = Path(raw)
            folder = root / 'frontiers/zones/zone237'
            folder.mkdir(parents=True)
            (folder / 'dat237.mpk').write_bytes(b'x')
            self.assertIsNone(kt.terrain_folder([root]))

    def test_retail_client_is_searched_before_the_offline_client(self):
        import tempfile
        with tempfile.TemporaryDirectory() as raw:
            root = Path(raw)
            retail = root / 'retail'
            offline = root / 'offline'
            _fill(retail / 'frontiers/zones/zone237')
            _fill(offline / 'zones/zone237')
            old_retail = os.environ.get('DAOC_RETAIL_CLIENT')
            old_client = os.environ.get('DAOC_CLIENT')
            os.environ['DAOC_RETAIL_CLIENT'] = str(retail)
            os.environ['DAOC_CLIENT'] = str(offline)
            try:
                self.assertEqual(kt.terrain_folder(), retail / 'frontiers/zones/zone237')
            finally:
                if old_retail is None:
                    os.environ.pop('DAOC_RETAIL_CLIENT', None)
                else:
                    os.environ['DAOC_RETAIL_CLIENT'] = old_retail
                if old_client is None:
                    os.environ.pop('DAOC_CLIENT', None)
                else:
                    os.environ['DAOC_CLIENT'] = old_client

    def test_missing_message_names_the_retail_path_and_the_uncommitted_output(self):
        text = kt.missing_message()
        self.assertIn(kt.RETAIL_DIR, text)
        self.assertIn('ter237.mpk (205836 bytes)', text)
        self.assertIn('tex237.mpk (5661950 bytes)', text)
        self.assertIn('lod237.mpk', text)
        self.assertIn('dat237.mpk', text)
        self.assertIn('DAOC_RETAIL_CLIENT', text)
        self.assertIn('python build_zone.py 253', text)
        self.assertIn('godot/content/zones/zone253/', text)
        self.assertIn('Do not commit that output.', text)
        self.assertNotIn('524288', text)


class KillaloeKeepTests(unittest.TestCase):
    def test_portal_keeps_share_the_captured_lvl4_layout(self):
        import killaloe_keeps as kk
        self.assertEqual(len(kk.PORTAL_KEEPS), 3)
        self.assertEqual(kk.PORTAL_PIECES, kk.PORTAL_KEEPS[0]['pieces'])
        self.assertEqual(kk.PORTAL_PIECES, kk.PORTAL_KEEPS[1]['pieces'])
        self.assertEqual(kk.PORTAL_PIECES, kk.PORTAL_KEEPS[2]['pieces'])
        skins = [s for s, *_ in kk.PORTAL_PIECES]
        self.assertEqual(skins.count(24), 1)
        self.assertEqual(skins.count(29), 1)
        self.assertEqual(skins.count(27), 4)
        self.assertEqual(skins.count(23), 2)
        self.assertEqual(skins.count(21), 4)
        self.assertNotIn(30, skins)

    def test_dun_killaloe_has_mainkeep_gatehouse_short_walls_and_corner_towers(self):
        import killaloe_keeps as kk
        skins = [s for s, *_ in kk.DUN_PIECES]
        self.assertEqual(skins.count(30), 1)
        self.assertEqual(skins.count(24), 1)
        self.assertEqual(skins.count(21), 5)
        self.assertEqual(skins.count(27), 4)
        self.assertNotIn(29, skins)
        self.assertNotIn(23, skins)

    def test_border_keeps_are_not_the_live_layout(self):
        import killaloe_keeps as kk
        self.assertTrue(all(stem not in kk.PIECE_STEMS for stem in kk.BORDER_STEMS))
        self.assertNotIn('frontkeep', ''.join(kk.PIECE_STEMS))

    def test_component_grid_places_albion_gatehouse_from_the_0x69_pad(self):
        import killaloe_keeps as kk
        alb = kk.PORTAL_KEEPS[0]
        x, y = kk.component_xy(alb['x'], alb['y'], alb['heading'], -2, -3)
        self.assertAlmostEqual(x, 545157.57, places=1)
        self.assertAlmostEqual(y, 581815.49, places=1)
        pos = kk.fixture_pos(alb['x'], alb['y'], alb['z'])
        self.assertAlmostEqual(pos[0], 513.4, places=4)
        self.assertAlmostEqual(pos[1], 207.2, places=4)
        self.assertAlmostEqual(pos[2], 1448.6, places=4)

    def test_realm_skins_hide_other_cakes_and_damage(self):
        import killaloe_keeps as kk
        self.assertTrue(kk.node_keep_visible('!Tiers_4_alb UN:6', 1))
        self.assertFalse(kk.node_keep_visible('!Tiers_4_mid01 UN:6', 1))
        self.assertFalse(kk.node_keep_visible('!Tiers_4_hib01 UN:6', 1))
        self.assertTrue(kk.node_keep_visible('!Tiers_4_MidToppers01:6', 2))
        self.assertFalse(kk.node_keep_visible('!Tiers_4_Alb_topper01:6', 2))
        self.assertFalse(kk.node_keep_visible('!Tiers_4_alb DAM:9', 1))
        self.assertFalse(kk.node_keep_visible('vi-siege-tower:0', 1))
        self.assertTrue(kk.node_keep_visible('vis undamaged:0', 1))
        self.assertTrue(kk.node_keep_visible('collision_0', 3))
        self.assertTrue(kk.node_keep_visible('doorcol0_1', 1))
        self.assertFalse(kk.node_keep_visible('Editable Mesh', 1))
        self.assertFalse(kk.node_keep_visible('!Tiers_1_-undamaged:4', 1))
        self.assertTrue(kk.node_keep_visible('!Tiers_2-3-4_facade_undamaged:4', 1))

    def test_dun_mainkeep_is_the_midgard_lvl4_keep(self):
        import killaloe_keeps as kk
        rows = kk.all_piece_fixtures()
        dun = [r for r in rows if r['name'].startswith('Dun Killaloe')]
        mains = [r for r in dun if r['stem'] == 'mainkeep_lvl4']
        self.assertEqual(len(mains), 1)
        self.assertEqual(mains[0]['realm'], 2)
        self.assertEqual(mains[0]['realm_stem'], 'mainkeep_lvl4_mid')
        self.assertEqual(mains[0]['door_ids'], {1: 253138002})
        self.assertTrue(all(r['stem'] != 'borderkeep1' for r in dun))
        self.assertNotIn('straighttower_lvl4', [r['stem'] for r in dun])

    def test_portal_gatehouse_door_ids_match_the_capture(self):
        import killaloe_keeps as kk
        rows = kk.all_piece_fixtures()
        alb = next(r for r in rows if r['name'] == 'Albion Portal Keep Gatehouse')
        hib = next(r for r in rows if r['name'] == 'Hibernia Portal Keep Gatehouse')
        self.assertEqual(alb['door_ids'][1], 253201001)
        self.assertNotIn(1, hib['door_ids'])
        self.assertEqual(hib['door_ids'][2], 253203002)
        self.assertEqual(kk.realm_stem('gatehouse_lvl4', 1), 'gatehouse_lvl4_alb')

    def test_door_swing_splits_a_stuck_double_leaf(self):
        import killaloe_keeps as kk
        pose = [-1.0, 0.0, 0.0, 0.0, 1.0, 0.0, 0.0, 0.0, -1.0, 1.0, 0.0, 2.0]
        doors = [
            {'group': 'door001', 'closed': pose, 'open': pose},
            {'group': 'door001', 'closed': pose, 'open': pose},
        ]
        kk.ensure_door_swing(doors)
        self.assertNotEqual(doors[0]['open'], doors[0]['closed'])
        self.assertNotEqual(doors[1]['open'], doors[0]['open'])


if __name__ == '__main__':
    unittest.main()
