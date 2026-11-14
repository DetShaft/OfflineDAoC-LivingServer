"""The zone-edge stitch must not reject Killaloe's borrowed ground. No retail client files.

build_zone copies retail zone 237 onto zone 253 and drops region offset 64, 64. The stitch then
compared the written heights with the zones.dat sampler and exited 1:

    AssertionError: zone 253: interior samples differ from the build

Those samples are not supposed to match that placement. A normal zone still has to.
"""
import unittest

import numpy as np

import stitch_zone_edges as stitch


N = stitch.N
M = stitch.M
# Retail Killaloe sits at region offset 64, 64. 64 * 8192 / 256 = 2048 samples.
RETAIL_CORNER = (64 * stitch.BLOCK // stitch.CELL, 64 * stitch.BLOCK // stitch.CELL)


def _zone(region, ox, oy):
    return {'region': region, 'type': '0', 'ox': ox, 'oy': oy, 'w': 8, 'h': 8}


def _interior(grid):
    """The 256 square the stitch compares, inside the padded grid() result."""
    base = grid[2:2 + N + 1, 2:2 + N + 1]
    return base[:N, :N]


class StitchBorrowedGroundTests(unittest.TestCase):
    def test_a_normal_zone_checks_the_client_placement_and_borrowed_ground_does_not(self):
        self.assertTrue(stitch.checks_client_placement(200, {}))
        self.assertTrue(stitch.checks_client_placement(253, {'terrain_from': 253}))
        self.assertFalse(stitch.checks_client_placement(253, {'terrain_from': 237}))
        self.assertTrue(stitch.checks_client_placement(253, {'terrain_from': 'not-a-zone'}))

    def test_the_old_comparison_rejects_borrowed_samples(self):
        # What line 142 did: the build's interior against the sampler's interior.
        client = np.zeros((N, N), np.float64)             # zone 253's own dat, at its own offset
        retail = np.ones((N, N), np.float64)              # zone 237, placed at offset 64, 64
        borrowed = np.full((N, N), 42.0, np.float64)      # those samples copied onto zone 253
        region = stitch.Region(
            {253: _zone(253, 0, 0), 237: _zone(253, 64, 64)},
            253,
            grids={253: client, 237: retail})
        sampled = _interior(region.grid((0, 0), 2))
        with self.assertRaises(AssertionError) as caught:
            assert np.allclose(borrowed * M, sampled * M, atol=1e-3), \
                'zone 253: interior samples differ from the build'
        self.assertIn('interior samples differ from the build', str(caught.exception))
        self.assertEqual(RETAIL_CORNER, (2048, 2048))
        self.assertTrue(np.allclose(_interior(region.grid(RETAIL_CORNER, 2)), retail))

    def test_borrowed_ground_keeps_its_own_interior(self):
        client = np.zeros((N, N), np.float64)
        retail = np.ones((N, N), np.float64)
        borrowed = np.full((N, N), 42.0, np.float64)
        neighbour = np.full((N, N), 7.0, np.float64)
        neighbour[:, 0] = 9.0                              # the column that meets zone 253's east edge
        region = stitch.Region(
            {253: _zone(253, 0, 0), 237: _zone(253, 64, 64), 254: _zone(253, 8, 0)},
            253,
            grids={253: client, 237: retail, 254: neighbour})
        written = (np.pad(borrowed, ((0, 1), (0, 1)), mode='edge') * M).astype('<f4')
        home = np.asarray(written[:N, :N], dtype=np.float64) / M
        stitched = region.grid((0, 0), 2, home)
        base = stitched[2:2 + N + 1, 2:2 + N + 1]
        self.assertTrue(np.allclose(written[:N, :N], base[:N, :N] * M, atol=1e-3))
        self.assertFalse(np.allclose(base[:N, :N], client))
        self.assertFalse(np.allclose(base[:N, :N], retail))
        # The east edge is the neighbour at this zone's own offset, not the retail placement.
        self.assertTrue(np.allclose(base[:N, N], neighbour[:N, 0]))

    def test_a_normal_zone_still_has_to_match_and_still_takes_its_neighbour(self):
        own = np.full((N, N), 3.0, np.float64)
        neighbour = np.full((N, N), 5.0, np.float64)
        neighbour[:, 0] = 8.0
        region = stitch.Region(
            {200: _zone(1, 0, 0), 201: _zone(1, 8, 0)},
            1,
            grids={200: own, 201: neighbour})
        base = region.grid((0, 0), 2)[2:2 + N + 1, 2:2 + N + 1]
        self.assertTrue(np.allclose(own, base[:N, :N]))
        self.assertTrue(np.allclose(base[:N, N], neighbour[:N, 0]))
        other = np.full((N, N), 42.0, np.float64)
        with self.assertRaises(AssertionError) as caught:
            assert np.allclose(other * M, base[:N, :N] * M, atol=1e-3), \
                'zone 200: interior samples differ from the build'
        self.assertIn('zone 200: interior samples differ from the build', str(caught.exception))


if __name__ == '__main__':
    unittest.main()
