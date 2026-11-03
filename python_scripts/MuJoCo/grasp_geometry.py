"""Geometry of the object part at a grasp box, measured on the object mesh only (not on
the box size, which for the GSO automatic grasps depends on the grasp type).

The mesh is sliced by the plane through the box center orthogonal to the box x axis
(the palm width), and the piece of the cross-section closest to the box is kept (e.g.
the handle of a mug, not its body). Features:
- d1, d2: largest and smallest extent of that piece (mm), along its principal axes
- length: how far the piece continues along the box x axis (mm), following it slice by
  slice until it disappears, jumps away or merges with a much larger piece
- d1_local, d2_local: the same within LOCAL_RADIUS of the box center, so that a small
  part joined to a large object (the cap of a bottle) still reads as small
- depth: distance from the box center to the closest point of the piece (mm)
"""

import numpy as np
from scipy.sparse import coo_matrix
from scipy.sparse.csgraph import connected_components
from scipy.spatial import cKDTree

STEP = 0.004              # m between slices when following the part along x
MAX_LENGTH = 0.15         # m followed on each side
JOIN_DISTANCE = 0.002     # m: pieces closer than this are one piece
MAX_SHIFT = 0.010         # m: largest move of the piece between two slices
LOCAL_RADIUS = 0.040      # m around the box center for d1_local, d2_local


def load_obj(path):
    vertices, faces = [], []
    with open(path) as f:
        for line in f:
            if line.startswith("v "):
                vertices.append([float(v) for v in line.split()[1:4]])
            elif line.startswith("f "):
                ids = [int(token.split("/")[0]) - 1 for token in line.split()[1:]]
                for k in range(1, len(ids) - 1):        # fan triangulation
                    faces.append([ids[0], ids[k], ids[k + 1]])
    return np.array(vertices), np.array(faces, dtype=np.int64)


def slice_pieces(vertices, faces, origin, normal, u, w):
    """Pieces of the cross-section of the mesh with a plane: a list of (k, 2) arrays of
    points in the (u, w) coordinates of the plane"""
    s = (vertices - origin) @ normal
    s[np.abs(s) < 1e-9] = 1e-9                           # no vertex exactly on the plane
    sf = s[faces]
    crossing = (sf.min(axis=1) < 0) & (sf.max(axis=1) > 0)
    if not crossing.any():
        return []
    tris, st = faces[crossing], sf[crossing]
    points = np.full((len(tris), 3, 3), np.nan)
    for e, (i, j) in enumerate(((0, 1), (1, 2), (2, 0))):
        a, b = st[:, i], st[:, j]
        m = a * b < 0
        t = (a[m] / (a[m] - b[m]))[:, None]
        points[m, e] = vertices[tris[m, i]] + t * (vertices[tris[m, j]] - vertices[tris[m, i]])
    mask = ~np.isnan(points[:, :, 0])
    ends = points[mask].reshape(-1, 2, 3)                # the 2 crossing edges of each triangle
    n = len(ends)
    flat = ends.reshape(-1, 3) - origin
    planar = np.column_stack([flat @ u, flat @ w])
    # Connect the two ends of each segment, and the ends closer than JOIN_DISTANCE
    pairs = cKDTree(planar).query_pairs(JOIN_DISTANCE, output_type="ndarray")
    rows = np.concatenate([np.arange(n) * 2, pairs[:, 0]])
    cols = np.concatenate([np.arange(n) * 2 + 1, pairs[:, 1]])
    graph = coo_matrix((np.ones(len(rows)), (rows, cols)), shape=(2 * n, 2 * n))
    count, labels = connected_components(graph, directed=False)
    return [planar[labels == k] for k in range(count)]


def extents(points):
    """Largest and smallest extent (m) of 2D points along their principal axes"""
    if len(points) < 3:
        return 0.0, 0.0
    centered = points - points.mean(axis=0)
    _, _, axes = np.linalg.svd(centered, full_matrices=False)
    spans = np.ptp(centered @ axes.T, axis=0)
    return float(spans.max()), float(spans.min())


def part_features(vertices, faces, center, rot):
    """Features of the part at a grasp box (center, rotation with columns x, y, z)"""
    x, y, z = rot[:, 0], rot[:, 1], rot[:, 2]
    pieces = slice_pieces(vertices, faces, center, x, y, z)
    if not pieces:
        return {"d1": 0.0, "d2": 0.0, "d1_local": 0.0, "d2_local": 0.0, "length": 0.0, "depth": 0.0}
    piece = min(pieces, key=lambda p: np.linalg.norm(p, axis=1).min())
    d1, d2 = extents(piece)
    d1_local, d2_local = extents(piece[np.linalg.norm(piece, axis=1) <= LOCAL_RADIUS])
    depth = float(np.linalg.norm(piece, axis=1).min())

    length = STEP
    for sign in (1, -1):
        previous = piece.mean(axis=0)
        for k in range(1, int(MAX_LENGTH / STEP) + 1):
            origin = center + sign * k * STEP * x
            candidates = slice_pieces(vertices, faces, origin, x, y, z)
            if not candidates:
                break
            nearest = min(candidates, key=lambda p: np.linalg.norm(p.mean(axis=0) - previous))
            if np.linalg.norm(nearest.mean(axis=0) - previous) > MAX_SHIFT \
                    or extents(nearest)[0] > 1.5 * d1 + 0.01:
                break
            previous = nearest.mean(axis=0)
            length += STEP
    return {"d1": 1000 * d1, "d2": 1000 * d2, "d1_local": 1000 * d1_local, "d2_local": 1000 * d2_local,
            "length": 1000 * length, "depth": 1000 * depth}
