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

    def test_borrowed_dat_does_not_replace_exits_or_fixtures(self):
        self.assertEqual(set(kt.BORROWED_DAT) & set(kt.KEPT_FROM_BATTLEGROUND), set())
        for key in kt.NEEDED_IN_DAT:
            self.assertIn(key, kt.BORROWED_DAT)

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
    def test_keep_fixtures_are_realm_border_keeps_not_front_keeps(self):
        import killaloe_keeps as kk
        self.assertEqual(kk.KEEP_FIXTURES[1][0], 'borderkeep1')
        self.assertEqual(kk.KEEP_FIXTURES[2][0], 'n-borderkeep1')
        self.assertEqual(kk.KEEP_FIXTURES[3][0], 'hborderkeep1')
        self.assertEqual(kk.KEEP_FIXTURES[414][0], 'hborderkeep1')
        self.assertTrue(all('frontkeep' not in stem for stem, _ in kk.KEEP_FIXTURES.values()))


if __name__ == '__main__':
    unittest.main()
