# Mesh predicates the evaluator needs. Everything is vectorised over numpy arrays and
# chunked over points, because the body mesh has 33k vertices and gets tested against
# every candidate cage.

import numpy as np


def write_obj(path, verts, tris, groups=None):
    lines = [f'v {p[0]:.6f} {p[1]:.6f} {p[2]:.6f}' for p in verts]
    if groups is None:
        lines += [f'f {a + 1} {b + 1} {c + 1}' for a, b, c in tris]
    else:
        order = np.argsort(groups, kind='stable')
        last = None
        for t in order:
            if groups[t] != last:
                last = groups[t]
                lines.append(f'g {last}')
            a, b, c = tris[t]
            lines.append(f'f {a + 1} {b + 1} {c + 1}')
    with open(path, 'w', encoding='utf-8') as f:
        f.write('\n'.join(lines) + '\n')


def orient(verts, tris):
    # Make the winding consistent across the whole surface, then flip so the normals face out.
    # Lets a builder emit faces in whatever order is convenient.
    tris = np.array(tris, dtype=np.int32).copy()
    side = {}
    for t, (a, b, c) in enumerate(tris):
        for e in ((a, b), (b, c), (c, a)):
            side.setdefault(tuple(sorted(e)), []).append((t, e))

    seen = np.zeros(len(tris), dtype=bool)
    for root in range(len(tris)):
        if seen[root]:
            continue
        seen[root] = True
        stack = [root]
        while stack:
            t = stack.pop()
            a, b, c = tris[t]
            for e in ((a, b), (b, c), (c, a)):
                for t2, e2 in side[tuple(sorted(e))]:
                    if t2 == t or seen[t2]:
                        continue
                    seen[t2] = True
                    # Neighbours agree when the shared edge runs opposite ways in each.
                    if (e2[0], e2[1]) == (e[0], e[1]):
                        tris[t2] = tris[t2][::-1]
                    stack.append(t2)

    if volume(verts, tris) < 0:
        tris = tris[:, ::-1]
    return tris


def volume(verts, tris):
    a, b, c = verts[tris[:, 0]], verts[tris[:, 1]], verts[tris[:, 2]]
    return float(np.einsum('ij,ij->i', a, np.cross(b, c)).sum() / 6.0)


def weld(verts, tris, tol=1e-7):
    # Collapse coincident vertices so that topology checks see the mesh a modeller would.
    key = np.round(verts / tol).astype(np.int64)
    _, first, inv = np.unique(key, axis=0, return_index=True, return_inverse=True)
    tris = inv[tris.reshape(-1)].reshape(-1, 3)
    keep = (tris[:, 0] != tris[:, 1]) & (tris[:, 1] != tris[:, 2]) & (tris[:, 0] != tris[:, 2])
    return verts[first], tris[keep]


def topology(verts, tris):
    # Closed, single-component, consistently oriented manifold?
    v, t = weld(verts, tris)
    directed = np.r_[t[:, [0, 1]], t[:, [1, 2]], t[:, [2, 0]]]
    undirected = np.sort(directed, axis=1)
    uniq, counts = np.unique(undirected, axis=0, return_counts=True)

    # Consistent orientation: every interior edge is traversed once in each direction.
    dir_uniq, dir_counts = np.unique(directed, axis=0, return_counts=True)
    flipped = np.unique(dir_uniq[:, ::-1], axis=0)
    paired = len(np.unique(np.r_[dir_uniq, flipped], axis=0)) == len(dir_uniq)

    # Vertex connectivity via union-find over edges.
    parent = np.arange(len(v))

    def root(i):
        while parent[i] != i:
            parent[i] = parent[parent[i]]
            i = parent[i]
        return i

    for a, b in uniq:
        ra, rb = root(a), root(b)
        if ra != rb:
            parent[ra] = rb
    used = np.unique(t)
    comps = len({root(i) for i in used})

    return {
        'verts': int(len(v)),
        'tris': int(len(t)),
        'boundary_edges': int((counts == 1).sum()),
        'nonmanifold_edges': int((counts > 2).sum()),
        'components': comps,
        'oriented': bool(paired) and bool((dir_counts == 1).all()),
        'euler': int(len(used) - len(uniq) + len(t)),
        'closed': bool((counts == 2).all()) and comps == 1,
    }


def winding(points, verts, tris, chunk=1500):
    # Generalized winding number (Jacobson et al.): the solid angle each triangle subtends,
    # summed. ~1 strictly inside a closed outward-oriented mesh, ~0 outside. Robust where
    # ray parity is fragile (grazing hits, coincident faces from overlapping tubes).
    a, b, c = verts[tris[:, 0]], verts[tris[:, 1]], verts[tris[:, 2]]
    out = np.empty(len(points))
    for s in range(0, len(points), chunk):
        p = points[s:s + chunk, None, :]
        pa, pb, pc = a[None] - p, b[None] - p, c[None] - p
        la = np.linalg.norm(pa, axis=2)
        lb = np.linalg.norm(pb, axis=2)
        lc = np.linalg.norm(pc, axis=2)
        num = np.einsum('ijk,ijk->ij', pa, np.cross(pb, pc))
        den = (la * lb * lc
               + lc * np.einsum('ijk,ijk->ij', pa, pb)
               + la * np.einsum('ijk,ijk->ij', pb, pc)
               + lb * np.einsum('ijk,ijk->ij', pc, pa))
        out[s:s + chunk] = np.arctan2(num, den).sum(axis=1) / (2.0 * np.pi)
    return out


def inside(points, verts, tris, chunk=1500):
    return winding(points, verts, tris, chunk) > 0.5


def point_tri_dist(points, verts, tris, chunk=400):
    return closest_tri(points, verts, tris, chunk)[0]


def closest_tri(points, verts, tris, chunk=400):
    # Distance, triangle index and footpoint of the nearest triangle, per point.
    a, b, c = verts[tris[:, 0]], verts[tris[:, 1]], verts[tris[:, 2]]
    ab, ac = b - a, c - a
    d00 = np.einsum('ij,ij->i', ab, ab)
    d01 = np.einsum('ij,ij->i', ab, ac)
    d11 = np.einsum('ij,ij->i', ac, ac)
    det = d00 * d11 - d01 * d01
    det = np.where(np.abs(det) < 1e-24, 1e-24, det)

    dist = np.empty(len(points))
    which = np.empty(len(points), dtype=np.int32)
    foot = np.empty((len(points), 3))
    for s in range(0, len(points), chunk):
        p = points[s:s + chunk, None, :]
        ap = p - a[None]
        d20 = np.einsum('ijk,jk->ij', ap, ab)
        d21 = np.einsum('ijk,jk->ij', ap, ac)
        u = (d11 * d20 - d01 * d21) / det
        v = (d00 * d21 - d01 * d20) / det

        # Clamp the barycentric pair into the triangle, edge by edge.
        u = np.clip(u, 0.0, 1.0)
        v = np.clip(v, 0.0, 1.0)
        excess = np.where(u + v > 1.0, (u + v - 1.0) * 0.5, 0.0)
        u = np.clip(u - excess, 0.0, 1.0)
        v = np.clip(v - excess, 0.0, 1.0)

        q = a[None] + u[..., None] * ab[None] + v[..., None] * ac[None]
        d = np.linalg.norm(q - p, axis=2)
        k = d.argmin(axis=1)
        r = np.arange(len(k))
        dist[s:s + chunk] = d[r, k]
        which[s:s + chunk] = k
        foot[s:s + chunk] = q[r, k]
    return dist, which, foot


def _sat_overlap(p, q, eps):
    # Separating-axis test between two triangle batches: 2 face normals + 9 edge crosses.
    axes = [np.cross(p[:, 1] - p[:, 0], p[:, 2] - p[:, 0]),
            np.cross(q[:, 1] - q[:, 0], q[:, 2] - q[:, 0])]
    for i in range(3):
        for j in range(3):
            axes.append(np.cross(p[:, (i + 1) % 3] - p[:, i], q[:, (j + 1) % 3] - q[:, j]))

    hit = np.ones(len(p), dtype=bool)
    for ax in axes:
        n = np.linalg.norm(ax, axis=1, keepdims=True)
        ax = np.where(n > 1e-12, ax / np.maximum(n, 1e-30), 0.0)
        pp = np.einsum('ijk,ik->ij', p, ax)
        qq = np.einsum('ijk,ik->ij', q, ax)
        gap = np.maximum(pp.min(1) - qq.max(1), qq.min(1) - pp.max(1))
        hit &= ~(gap > eps)
    return hit


def self_intersections(verts, tris, eps=1e-6):
    # Pairs of non-adjacent triangles that genuinely overlap. Vertices are welded first so
    # that tubes meeting at a shared ring count as adjacent instead of intersecting.
    v, t = weld(verts, tris)
    lo = v[t].min(axis=1)
    hi = v[t].max(axis=1)

    i, j = np.triu_indices(len(t), k=1)
    box = np.all((lo[i] <= hi[j] + eps) & (lo[j] <= hi[i] + eps), axis=1)
    i, j = i[box], j[box]

    shared = (t[i][:, :, None] == t[j][:, None, :]).any(axis=(1, 2))
    i, j = i[~shared], j[~shared]
    if len(i) == 0:
        return np.zeros((0, 2), dtype=np.int32)

    hit = _sat_overlap(v[t[i]], v[t[j]], eps)
    return np.c_[i[hit], j[hit]]
