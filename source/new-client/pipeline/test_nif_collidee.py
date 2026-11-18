"""Collision geometry under a NIF's collidee nodes keeps the same frame as the drawn model.

    python -m unittest test_nif_collidee
"""
import unittest

import numpy as np

import nif_static as ns
from netimmerse import Block, Nif


def _node(type_name, name, index, **fields):
    b = Block(type_name, index)
    b['name'] = name
    for k, v in fields.items():
        b[k] = v
    return b


QUARTER_TURN = {'m11': 0, 'm12': 1, 'm13': 0, 'm21': -1, 'm22': 0, 'm23': 0, 'm31': 0, 'm32': 0, 'm33': 1}


class CollideeStaticTest(unittest.TestCase):
    def test_static_collidee_transform_is_applied_once(self):
        # Issue #127: mainKeep_lvl4 keeps its walls under a turned '!collidee_static' node. Its transform was applied
        # twice, so the new client's main keep collision stood a quarter turn off the drawn keep.
        shape = _node('NiTriShape', 'wall', 4, data={'num_vertices': 3})
        static = _node('NiNode', '!collidee_static', 3, rotation=QUARTER_TURN,
                       translation={'x': 100, 'y': 50, 'z': 0}, scale=1.0, children=[shape])
        placeholder = _node('NiNode', 'collidee', 2, children=[])
        root = _node('NiNode', 'root', 1, translation={'x': 10, 'y': 0, 'z': 0}, scale=1.0,
                     children=[placeholder, static])
        nif = Nif()
        nif.roots = [root]
        nif.blocks = [root, placeholder, static, shape]
        shapes = ns.collidee_shapes(nif)
        self.assertEqual(len(shapes), 1)
        expected = ns.local_matrix(shape) @ ns.local_matrix(static) @ ns.local_matrix(root)
        np.testing.assert_allclose(shapes[0][2], expected)


if __name__ == '__main__':
    unittest.main()
