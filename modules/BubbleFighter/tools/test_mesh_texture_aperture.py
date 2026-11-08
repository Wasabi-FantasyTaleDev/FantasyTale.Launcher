"""Geometry and skinning invariants for the offline aperture conversion."""
import struct
import unittest

import numpy as np

from mesh_texture_aperture import cut_apertures, parse_mesh


def sample_mesh(uv_offset=0., collapsed=False):
    """Two independent material subsets with overlapping bone influences."""
    identity = np.eye(4, dtype='<f4').tobytes()
    data = bytearray(struct.pack('<I', 3) + identity + struct.pack('<II', 0, 0) + bytes(32))
    vertices = [(x, y, 0, 0, 0, 1, .375 if collapsed else x + uv_offset, .375 if collapsed else y)
                for x, y in ((0, 0), (1, 0), (1, 1), (0, 1))] * 2
    indices = (0, 1, 2, 0, 2, 3, 4, 5, 6, 4, 6, 7)
    data += struct.pack('<II', 8, 12) + b''.join(struct.pack('<8f', *v) for v in vertices)
    data += struct.pack('<12H', *indices) + struct.pack('<I', 2)
    for _ in range(2):
        data += bytes(68) + struct.pack('<I', 0)
    data += struct.pack('<I8I', 2, 0, 4, 0, 2, 4, 4, 2, 2) + struct.pack('<I', 2)
    for name, values in (('A', [v[0] for v in vertices]), ('B', [1.25 - v[0] for v in vertices])):
        data += struct.pack('<I', 1) + name.encode('utf-16le') + struct.pack('<I8I', 8, *range(8))
        data += struct.pack('<8f', *values) + identity
    return bytes(data) + b'exporter-only tail'


class ApertureTests(unittest.TestCase):
    def test_empty_or_unreferenced_mask_is_byte_identical(self):
        source = sample_mesh()
        for masks in ([None, None], [np.zeros((4, 4), dtype=bool), None]):
            self.assertEqual(cut_apertures(source, masks)[0], source)
        mask = np.zeros((4, 4), dtype=bool)
        mask[0, 0] = True
        self.assertEqual(cut_apertures(sample_mesh(collapsed=True), [mask, None])[0], sample_mesh(collapsed=True))

    def test_hole_retains_opaque_surface_material_and_all_bone_weights(self):
        source = sample_mesh()
        mask = np.zeros((4, 4), dtype=bool)
        mask[1:3, 1:3] = True
        output, report = cut_apertures(source, [mask, None])
        self.assertEqual(output, cut_apertures(source, [mask, None])[0])
        original, result = parse_mesh(source), parse_mesh(output)
        self.assertEqual(result.prefix, original.prefix)
        self.assertEqual(result.materials, original.materials)
        self.assertEqual(result.vertex_bytes[:8 * 32], original.vertex_bytes)
        self.assertEqual(result.indices[result.ranges[1][2] * 3:], original.indices[6:])
        self.assertEqual(report['changed_triangles'], [0, 1])
        for b, bone in enumerate(result.bones):
            self.assertEqual(bone.offset, original.bones[b].offset)
            self.assertEqual(bone.weights[:8], original.bones[b].weights)
            for i, weight in zip(bone.indices[8:], bone.weights[8:]):
                expected = result.vertices[i, 0] if b == 0 else 1.25 - result.vertices[i, 0]
                self.assertAlmostEqual(weight, expected, places=6)
        area = 0.
        for f in range(result.ranges[0][3]):
            tri = result.vertices[result.indices[f * 3:f * 3 + 3], :2]
            a, b, c = tri
            area += abs(float(np.linalg.det(np.array([b-a, c-a])))) * .5
            centre = tri.mean(axis=0)
            self.assertFalse(.25 < centre[0] < .75 and .25 < centre[1] < .75)
        self.assertAlmostEqual(area, .75, places=6)

    def test_repeated_uv_and_collapsed_uv_obey_color_key(self):
        mask = np.zeros((4, 4), dtype=bool)
        mask[1:3, 1:3] = True
        shifted, report = cut_apertures(sample_mesh(uv_offset=-2), [mask, None])
        self.assertEqual(report['changed_triangles'], [0, 1])
        self.assertGreater(len(shifted), 0)
        result = parse_mesh(cut_apertures(sample_mesh(collapsed=True), [mask, None])[0])
        self.assertEqual(result.ranges[0][3], 0)
        self.assertEqual(result.indices, [4, 5, 6, 4, 6, 7])

    def test_malformed_mesh_fails(self):
        with self.assertRaises(ValueError):
            cut_apertures(sample_mesh()[:120], [None, None])
        with self.assertRaises(ValueError):
            cut_apertures(sample_mesh(), [None])


if __name__ == '__main__':
    unittest.main()
