# Pieces every cage algorithm reuses: folding the skeleton down to the joints a cage cares
# about, gathering the flesh each of those joints owns, and rectangular cross-section fitting.

import numpy as np


def fold_map(sk, listed):
    # Every bone maps to the nearest listed ancestor (fingers fold to the hand, twist bones to
    # their parent), so no flesh is left ungrouped.
    want = {sk.index[n] for n in listed}
    fold = np.full(len(sk.name), -1, dtype=int)
    for i in range(len(sk.name)):
        c = i
        while c >= 0 and c not in want:
            c = int(sk.parent[c])
        fold[i] = c
    return fold


def flesh_groups(sk, listed):
    # Rest vertices grouped by the listed joint their dominant bone folds to.
    owner = fold_map(sk, listed)[sk.dominant]
    return {j: sk.verts[owner == j] for j in sorted(set(owner.tolist())) if j >= 0}, owner


def frame_from(axis, seed_u=None):
    a = axis / np.linalg.norm(axis)
    if seed_u is None:
        seed = np.array([0.0, 1.0, 0.0]) if abs(a[1]) < 0.9 else np.array([0.0, 0.0, 1.0])
        u = np.cross(a, seed)
    else:
        u = seed_u - a * (seed_u @ a)
    n = np.linalg.norm(u)
    if n < 1e-8:
        seed = np.array([1.0, 0.0, 0.0]) if abs(a[0]) < 0.9 else np.array([0.0, 0.0, 1.0])
        u = np.cross(a, seed)
        n = np.linalg.norm(u)
    u = u / n
    return u, np.cross(a, u)


def half_extents(pts, center, u, v, margin):
    if len(pts) == 0:
        return 0.0, 0.0
    d = pts - center
    return float(np.abs(d @ u).max()) * (1.0 + margin), float(np.abs(d @ v).max()) * (1.0 + margin)


def quad(tris, a, b, c, d):
    tris.append((a, b, c))
    tris.append((a, c, d))


def slab(pts, center, axis, back, front):
    # Flesh inside a slab around a cross-section station, used to size that station's rectangle.
    if len(pts) == 0:
        return pts
    t = (pts - center) @ axis
    return pts[(t >= -back) & (t <= front)]


def rect(pts, center, u, v, margin, floor):
    # Asymmetric rectangle bounding the projected flesh: (u_lo, u_hi, v_lo, v_hi) offsets from
    # the center. Asymmetry matters -- a thigh slice is not centred on the hip joint.
    if len(pts) == 0:
        return (-floor, floor, -floor, floor)
    d = pts - center
    a, b = d @ u, d @ v
    du = max(a.max() - a.min(), 2 * floor) * margin * 0.5
    dv = max(b.max() - b.min(), 2 * floor) * margin * 0.5
    cu, cv = (a.max() + a.min()) * 0.5, (b.max() + b.min()) * 0.5
    return (cu - du, cu + du, cv - dv, cv + dv)


def repair(pos, dirs, tris, rig, w, sk, scales, iters, pad, stride, smooth, step, geom):
    # Grow the cage until it swallows the flesh: for every escaped vertex, displace the nearest
    # triangle's corners far enough to cover it. Far more general than hand-widening whichever
    # cross-section happens to clip a bulge, and it converges because each pass only ever moves the
    # surface outward.
    #
    # The correction is a constant offset added after the rig blend, so it is pose-independent:
    # training over several bone-length sets at bake time yields one cage that contains the body in
    # all of them. Repairing the rest pose alone leaves deformed poses leaking (857 -> 102 escaped
    # vertices over the test set).
    #
    # Two variants of the displacement rule were measured and both lose (numbers in
    # docs/cage-lab.md): sliding each vertex along its own outward normal by a scalar, and sliding
    # it strictly inside its own ring plane. The per-axis rule below wins because a corner is free
    # to answer an escape in whichever direction that escape actually lies.
    off = pos - np.einsum('vk,vki->vi', w, sk.rest_pos[rig])
    posed = [(np.einsum('vk,vki->vi', w, sk.joint_pos(s)[rig]), sk.skin(s)[::stride]) for s in scales]
    nb = [np.unique(np.r_[tris[(tris == i).any(axis=1)].reshape(-1), i]) for i in range(len(pos))]

    for _ in range(iters):
        hi = np.zeros_like(off)
        lo = np.zeros_like(off)
        leaks = 0
        for anchor, pts in posed:
            verts = anchor + off
            out = pts[geom.winding(pts, verts, tris) <= 0.5]
            if len(out) == 0:
                continue
            leaks += len(out)
            _, which, foot = geom.closest_tri(out, verts, tris)
            delta = out - foot
            reach = np.linalg.norm(delta, axis=1, keepdims=True)
            delta = np.clip(delta * (1.0 + pad / np.maximum(reach, 1e-9)), -step, step)
            # Per axis, keep the most extreme correction in each direction. Averaging would
            # under-cover the deepest escape; summing would overshoot when many escaped vertices
            # share one triangle.
            for k in range(3):
                idx = tris[which, k]
                np.maximum.at(hi, idx, np.maximum(delta, 0.0))
                np.minimum.at(lo, idx, np.minimum(delta, 0.0))
        if leaks == 0:
            break
        for _ in range(smooth):
            hi = np.array([hi[n].max(axis=0) for n in nb])
            lo = np.array([lo[n].min(axis=0) for n in nb])
        off = off + hi + lo
    return np.einsum('vk,vki->vi', w, sk.rest_pos[rig]) + off


class chain:
    # A limb as a polyline of joints. Any point near it gets a rig -- the two joints bracketing
    # its arc position, blended -- so that a cage vertex placed between two joints stretches by
    # the same fraction the skeleton does there. That is what keeps a bone's length change from
    # smearing across its neighbours when no cage ring sits exactly on the joint.
    def __init__(self, sk, names):
        self.joint = [sk.index[n] for n in names]
        self.pos = sk.rest_pos[self.joint]
        seg = np.linalg.norm(np.diff(self.pos, axis=0), axis=1)
        self.arc = np.r_[0.0, np.cumsum(seg)]

    def at(self, s):
        # s in arc-length units from the first joint; clamped past the ends onto the end joint.
        k = int(np.clip(np.searchsorted(self.arc, s) - 1, 0, len(self.arc) - 2))
        span = self.arc[k + 1] - self.arc[k]
        f = float(np.clip((s - self.arc[k]) / span, 0.0, 1.0)) if span > 1e-9 else 0.0
        return (self.joint[k], self.joint[k + 1]), (1.0 - f, f)

    def project(self, p):
        # Arc position of the closest point on the polyline.
        best, best_d = 0.0, float('inf')
        for k in range(len(self.pos) - 1):
            a, b = self.pos[k], self.pos[k + 1]
            ab = b - a
            n2 = float(ab @ ab)
            f = float(np.clip((p - a) @ ab / n2, 0.0, 1.0)) if n2 > 1e-12 else 0.0
            d = np.linalg.norm(p - (a + ab * f))
            if d < best_d:
                best_d, best = d, self.arc[k] + f * (self.arc[k + 1] - self.arc[k])
        return best


class builder:
    # Accumulates cage vertices (each riding on a blend of joints) and n-gon faces. Vertices are
    # stored as rest positions; build() reconstructs them as sum(w * joint_pos) + offset.
    def __init__(self, sk):
        self.sk = sk
        self.pos = []
        self.rig = []
        self.w = []
        self.dir = []
        self.faces = []

    def vert(self, p, rig, w=None, out=None):
        # out: the direction this vertex may grow along if the cage has to be widened later. For a
        # ring corner that is the in-plane radial direction, which keeps the cross-section planar.
        self.pos.append(np.asarray(p, dtype=np.float64))
        if isinstance(rig, (int, np.integer)):
            rig, w = (int(rig), int(rig)), (1.0, 0.0)
        self.rig.append(tuple(rig))
        self.w.append(tuple(w))
        self.dir.append(None if out is None else np.asarray(out, dtype=np.float64))
        return len(self.pos) - 1

    def ring(self, center, u, v, box, rig, w=None):
        # Corner order runs around the axis u x v, so consecutive rings loft without twisting.
        u_lo, u_hi, v_lo, v_hi = box
        return [self.vert(center + u * a + v * b, rig, w)
                for a, b in ((u_hi, v_hi), (u_lo, v_hi), (u_lo, v_lo), (u_hi, v_lo))]

    def face(self, loop):
        self.faces.append(list(loop))

    def tube(self, a, b, skip=()):
        for k in range(len(a)):
            if k in skip:
                continue                    # side left open for a branch port
            kn = (k + 1) % len(a)
            self.face([a[k], a[kn], b[kn], b[k]])

    def cap(self, r):
        self.face(list(r))

    def annulus(self, outer, inner, link):
        # link[i] is the index into `inner` that outer[i] connects to; inner vertices between two
        # links land on the strip spanning that gap, so the loops need not be the same size. The
        # strip is emitted as a quad plus triangles rather than one n-gon: fanning a long thin
        # frame from a corner throws a diagonal clean across it, which self-intersects.
        n = len(outer)
        for i in range(n):
            j = (i + 1) % n
            run = [inner[link[i]]]
            k = link[i]
            while k != link[j]:
                k = (k + 1) % len(inner)
                run.append(inner[k])
            if len(run) == 1:
                self.face([outer[i], outer[j], run[0]])
                continue
            self.face([outer[i], outer[j], run[1], run[0]])
            for m in range(1, len(run) - 1):
                self.face([outer[j], run[m + 1], run[m]])

    def finish(self):
        tris = np.array([(f[0], f[k], f[k + 1]) for f in self.faces for k in range(1, len(f) - 1)],
                        dtype=np.int32)
        pos = np.array(self.pos)

        # Growth directions for the repair pass: the area-weighted vertex normal.
        normal = np.zeros_like(pos)
        fn = np.cross(pos[tris[:, 1]] - pos[tris[:, 0]], pos[tris[:, 2]] - pos[tris[:, 0]])
        for k in range(3):
            np.add.at(normal, tris[:, k], fn)
        dirs = np.array([normal[i] if d is None else d for i, d in enumerate(self.dir)])
        n = np.linalg.norm(dirs, axis=1, keepdims=True)
        dirs = np.where(n > 1e-12, dirs / np.maximum(n, 1e-30), 0.0)
        return pos, np.array(self.rig, dtype=np.int32), np.array(self.w, dtype=np.float64), tris, dirs
