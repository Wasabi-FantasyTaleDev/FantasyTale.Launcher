"""Convert exact color-key footprints into mesh openings without moving the face.

The layout follows cn-324 sub_900550. KR-1256 sub_106DAF0 keys magenta,
but kr-1256 sub_1080320 can draw an ordinary head without alpha testing.
Only offline package creation uses this module; game resources never enter Git.
"""
from __future__ import annotations

from dataclasses import dataclass
import math
import struct

import numpy as np


class Reader:
    """Read bounded mesh fields while retaining uninterpreted bytes verbatim."""

    def __init__(self, data: bytes):
        self.data, self.pos = data, 0

    def take(self, count: int) -> bytes:
        if count < 0 or count > len(self.data) - self.pos:
            raise ValueError(f"Truncated mesh at {self.pos}, requested {count} bytes")
        result = self.data[self.pos:self.pos + count]
        self.pos += count
        return result

    def u32(self) -> int:
        return struct.unpack('<I', self.take(4))[0]

    def string(self) -> str:
        return self.take(self.u32() * 2).decode('utf-16le')


@dataclass
class Bone:
    """A skinning row whose original indices, weights and inverse bind survive."""

    name_bytes: bytes
    indices: list[int]
    weights: list[float]
    offset: bytes


@dataclass
class Mesh:
    """The serialized regions needed for a topology edit, with original metadata."""

    prefix: bytes
    vertex_bytes: bytes
    vertices: np.ndarray
    indices: list[int]
    materials: bytes
    textures: list[str | None]
    ranges: list[tuple[int, int, int, int]]
    bones: list[Bone]


def parse_mesh(data: bytes) -> Mesh:
    """Read the mesh loader's fields and reject unsafe topology or skinning."""
    reader = Reader(data)
    if reader.u32() != 3:
        raise ValueError('Only the verified version-3 mesh layout can be transformed')
    reader.take(68)
    for _ in range(reader.u32()):
        reader.string()
        reader.take(4)
    reader.take(32)
    prefix = data[:reader.pos]
    vertex_count, index_count = reader.u32(), reader.u32()
    if vertex_count > 65536 or index_count % 3:
        raise ValueError('Invalid ushort triangle-list mesh')
    vertex_bytes = reader.take(vertex_count * 32)
    vertices = np.frombuffer(vertex_bytes, dtype='<f4').reshape(-1, 8).astype(float)
    if not np.all(np.isfinite(vertices)):
        raise ValueError('Non-finite mesh vertex')
    indices = list(struct.unpack(f'<{index_count}H', reader.take(index_count * 2)))
    if any(i >= vertex_count for i in indices):
        raise ValueError('Mesh index is outside the vertex array')
    start = reader.pos
    textures = []
    for _ in range(reader.u32()):
        reader.take(68)
        textures.append(reader.string() if reader.u32() else None)
    materials = data[start:reader.pos]
    ranges = [struct.unpack('<4I', reader.take(16)) for _ in range(reader.u32())]
    if len(ranges) != len(textures):
        raise ValueError('Material and face-range counts disagree')
    faces = sorted((f, f + n) for _, _, f, n in ranges if n)
    cursor = 0
    for start, end in faces:
        if start != cursor or end > index_count // 3:
            raise ValueError('Material ranges do not partition the triangle list')
        cursor = end
    if cursor != index_count // 3:
        raise ValueError('Material ranges leave triangles unassigned')
    bones = []
    for _ in range(reader.u32()):
        start = reader.pos
        reader.string()
        name_bytes = data[start:reader.pos]
        count = reader.u32()
        ids = list(struct.unpack(f'<{count}I', reader.take(count * 4)))
        weights = list(struct.unpack(f'<{count}f', reader.take(count * 4)))
        if len(set(ids)) != len(ids) or any(i >= vertex_count for i in ids):
            raise ValueError('Invalid or duplicate skinning vertex')
        if any(not math.isfinite(w) or w < 0 for w in weights):
            raise ValueError('Invalid skinning weight')
        bones.append(Bone(name_bytes, ids, weights, reader.take(64)))
    return Mesh(prefix, vertex_bytes, vertices, indices, materials, textures, ranges, bones)


def mask_rectangles(mask: np.ndarray) -> list[tuple[float, float, float, float]]:
    """Merge equal pixel runs exactly; do not enlarge or smooth the keyed area."""
    height, width = mask.shape
    if not height or not width:
        raise ValueError('Empty aperture texture')
    active, done = {}, []
    for y in range(height + 1):
        edges = np.flatnonzero(np.diff(np.pad(mask[y].astype(np.int8), (1, 1)))) if y < height else []
        runs = list(zip(edges[::2], edges[1::2]))
        for run in list(active):
            if run not in runs:
                done.append((*run, active.pop(run), y))
        for run in runs:
            active.setdefault(run, y)
    return [(x0 / width, y0 / height, x1 / width, y1 / height) for x0, x1, y0, y1 in done]


def _clip(poly, axis, boundary, positive):
    result = []
    for a, b in zip(poly, poly[1:] + poly[:1]):
        da, db = a[axis] - boundary, b[axis] - boundary
        inside_a = da >= 0 if positive else da <= 0
        inside_b = db >= 0 if positive else db <= 0
        if inside_a:
            result.append(a)
        if inside_a != inside_b:
            result.append(a + (b - a) * da / (da - db))
    return result


def _area(poly):
    # Barycentric area also handles triangles whose UVs collapse to a line/point.
    return abs(sum(a[3] * b[4] - b[3] * a[4] for a, b in zip(poly, poly[1:] + poly[:1]))) * .5


def _subtract(poly, rect):
    x0, y0, x1, y1 = rect
    if (max(v[0] for v in poly) < x0 or min(v[0] for v in poly) >= x1 or
            max(v[1] for v in poly) < y0 or min(v[1] for v in poly) >= y1):
        return [poly]
    remainder, pieces = poly, []
    for axis, value, positive in ((0, x0, True), (0, x1, False), (1, y0, True), (1, y1, False)):
        outer = _clip(remainder, axis, value, not positive)
        if len(outer) >= 3 and _area(outer) > 1e-12:
            # A collapsed UV at the included edge belongs to the aperture.
            if not all(v[axis] == value for v in remainder):
                pieces.append(outer)
        remainder = _clip(remainder, axis, value, positive)
        if len(remainder) < 3 or _area(remainder) < 1e-12:
            break
    return pieces


def cut_apertures(data: bytes, masks: list[np.ndarray | None]) -> tuple[bytes, dict]:
    """Subtract per-material color-key pixels, interpolating all skin influences.

    Unaffected meshes return their exact input bytes, including the exporter tail.
    Changed meshes retain original vertices and metadata; only added vertices and
    triangle ranges are serialized anew. Skin weights are not renormalized.
    """
    mesh = parse_mesh(data)
    if len(masks) != len(mesh.textures):
        raise ValueError('Exactly one mask is required for each mesh material')
    rectangles = [mask_rectangles(m) if m is not None else [] for m in masks]
    vertex_count = len(mesh.vertices)
    weights = np.zeros((vertex_count, len(mesh.bones)))
    for b, bone in enumerate(mesh.bones):
        weights[bone.indices, b] = bone.weights
    vertices = [mesh.vertex_bytes[i * 32:(i + 1) * 32] for i in range(vertex_count)]
    vertex_weights = [row.copy() for row in weights]
    lookup = {(v, np.asarray(w, dtype='<f4').tobytes()): i for i, (v, w) in enumerate(zip(vertices, weights))}
    indices, ranges, changed = [], [], []
    removed_area = 0.
    for subset, (_, _, first, count) in enumerate(mesh.ranges):
        start = len(indices) // 3
        for face in range(first, first + count):
            original = mesh.indices[face * 3:face * 3 + 3]
            triangle = mesh.vertices[original]
            poly = [np.concatenate((triangle[i, 6:8], np.eye(3)[i])) for i in range(3)]
            polygons = [poly]
            if rectangles[subset]:
                lo, hi = np.floor(triangle[:, 6:8].min(axis=0)).astype(int), np.floor(triangle[:, 6:8].max(axis=0)).astype(int)
                if int(hi[0] - lo[0] + 1) * int(hi[1] - lo[1] + 1) > 65536:
                    raise ValueError('Repeated UV aperture exceeds the ushort mesh address space')
                for u in range(lo[0], hi[0] + 1):
                    for v in range(lo[1], hi[1] + 1):
                        for x0, y0, x1, y1 in rectangles[subset]:
                            polygons = [piece for p in polygons for piece in _subtract(p, (x0 + u, y0 + v, x1 + u, y1 + v))]
            retained = sum(_area(p) for p in polygons)
            if abs(.5 - retained) < 1e-10:
                indices.extend(original)
                continue
            changed.append(face)
            removed_area += .5 - retained
            for p in polygons:
                ids = []
                for point in p:
                    bary = point[2:]
                    record = np.asarray(bary @ triangle, dtype='<f4').tobytes()
                    skin = np.asarray(bary @ weights[original], dtype='<f4')
                    if np.count_nonzero(skin) > 4:
                        raise ValueError('Interpolated vertex exceeds the four-influence client layout')
                    key = record, skin.tobytes()
                    if key not in lookup:
                        if len(vertices) >= 65536:
                            raise ValueError('Aperture conversion exceeds ushort vertex indices')
                        lookup[key] = len(vertices)
                        vertices.append(record)
                        vertex_weights.append(skin)
                    ids.append(lookup[key])
                for i in range(1, len(ids) - 1):
                    if _area([p[0], p[i], p[i + 1]]) > 1e-12:
                        indices.extend((ids[0], ids[i], ids[i + 1]))
        used = indices[start * 3:]
        ranges.append((min(used) if used else 0, max(used) - min(used) + 1 if used else 0,
                       start, len(indices) // 3 - start))
    report = dict(original_vertices=vertex_count, vertices=len(vertices),
                  original_triangles=len(mesh.indices) // 3, triangles=len(indices) // 3,
                  changed_triangles=changed, removed_barycentric_area=removed_area,
                  mask_pixels=[int(m.sum()) if m is not None else 0 for m in masks],
                  materials=len(mesh.textures), bones=len(mesh.bones))
    if not changed:
        return data, report
    out = bytearray(mesh.prefix + struct.pack('<II', len(vertices), len(indices)))
    out += b''.join(vertices) + struct.pack(f'<{len(indices)}H', *indices) + mesh.materials
    out += struct.pack('<I', len(ranges)) + b''.join(struct.pack('<4I', *r) for r in ranges)
    out += struct.pack('<I', len(mesh.bones))
    for b, bone in enumerate(mesh.bones):
        ids, values = list(bone.indices), list(bone.weights)
        for i in range(vertex_count, len(vertices)):
            value = float(vertex_weights[i][b])
            if value != 0:
                ids.append(i)
                values.append(value)
        out += bone.name_bytes + struct.pack('<I', len(ids))
        out += struct.pack(f'<{len(ids)}I', *ids) + struct.pack(f'<{len(values)}f', *values) + bone.offset
    # cn-324 sub_900550 stops after bones. Exporter adjacency describes the old
    # topology and must not survive a topology edit.
    result = bytes(out)
    check = parse_mesh(result)
    if check.vertex_bytes[:len(mesh.vertex_bytes)] != mesh.vertex_bytes:
        raise ValueError('Original vertex records changed during conversion')
    return result, report
