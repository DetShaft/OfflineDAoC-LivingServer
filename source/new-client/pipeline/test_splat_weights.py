"""Splat weight maps of different sizes must still pack. No retail client files.

The zone 253 build (Killaloe's ground, borrowed from zone 237) wrote the painted ground and then
died in build_zone while stacking one patch's weight channels:

    ValueError: all input arrays must have the same shape

Missing overlays were 128x128 zeros. The real maps were not.
"""
import unittest

import numpy as np

import terrain_splat


class SplatWeightTests(unittest.TestCase):
    def test_the_old_stack_rejects_a_128_pad_on_a_larger_map(self):
        # What build() did: one real map, then 128x128 zeros for the overlays that patch lacks.
        weight = np.full((256, 256), 0.5, np.float32)
        pad = np.zeros((128, 128), np.float32)
        with self.assertRaises(ValueError):
            np.stack([weight, pad, pad, pad], axis=2)

    def test_a_short_patch_packs_at_the_real_maps_size(self):
        weight = np.full((256, 256), 0.5, np.float32)
        image = terrain_splat.pack_weights([weight, None, None, None])
        self.assertEqual(image.size, (256, 256))
        pixels = np.asarray(image)
        self.assertEqual(pixels.shape, (256, 256, 4))
        self.assertEqual(int(pixels[0, 0, 0]), int(0.5 * 255))
        self.assertEqual(int(pixels[..., 1:].max()), 0)

    def test_the_second_image_of_a_patch_accepts_a_short_tail(self):
        # Five overlays at 256. The first four stack. The next image is the fifth map plus three
        # pads, which is where the zone 253 build died.
        over = [np.full((256, 256), 0.2 * (i + 1), np.float32) for i in range(5)]
        second = [over[i] if i < len(over) else np.zeros((128, 128), np.float32) for i in range(4, 8)]
        with self.assertRaises(ValueError):
            np.stack(second, axis=2)
        images = []
        for start in (0, 4):
            chans = [over[i] if i < len(over) else None for i in range(start, start + 4)]
            images.append(terrain_splat.pack_weights(chans))
        self.assertEqual([image.size for image in images], [(256, 256), (256, 256)])
        tail = np.asarray(images[1])
        self.assertEqual(int(tail[0, 0, 0]), int(0.2 * 5 * 255))
        self.assertEqual(int(tail[..., 1:].max()), 0)

    def test_a_smaller_map_is_fitted_to_the_common_size(self):
        common = np.zeros((128, 128), np.float32)
        odd = np.ones((64, 64), np.float32)
        image = terrain_splat.pack_weights([common, common, odd, None])
        self.assertEqual(image.size, (128, 128))
        pixels = np.asarray(image)
        self.assertGreater(int(pixels[..., 2].min()), 250)
        self.assertEqual(int(pixels[..., 3].max()), 0)

    def test_four_matching_maps_match_a_direct_stack(self):
        maps = [np.full((128, 128), value, np.float32) for value in (0.0, 0.25, 0.5, 1.0)]
        direct = (np.clip(np.stack(maps, axis=2), 0, 1) * 255).astype(np.uint8)
        self.assertTrue(np.array_equal(np.asarray(terrain_splat.pack_weights(maps)), direct))

    def test_a_classic_patch_with_one_overlay_stays_128(self):
        weight = np.full((128, 128), 0.5, np.float32)
        pad = np.zeros((128, 128), np.float32)
        old = (np.clip(np.stack([weight, pad, pad, pad], axis=2), 0, 1) * 255).astype(np.uint8)
        new = np.asarray(terrain_splat.pack_weights([weight, None, None, None]))
        self.assertTrue(np.array_equal(new, old))


if __name__ == '__main__':
    unittest.main()
