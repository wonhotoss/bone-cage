# Evaluator. A cage is judged on five axes; the weights are a deliberate choice, recorded
# in docs/cage-lab.md next to the reasoning:
#
#   containment 0.34  hard requirement -- a vertex outside the cage cannot be mapped at all
#   collision   0.24  hard requirement -- a self-intersecting cage mixes unrelated body parts
#   bone_fit    0.20  hard requirement -- the whole point is transmitting bone length
#   tightness   0.14  soft -- quality of the eventual mapping
#   simplicity  0.08  soft -- cheapest to satisfy, so it gets the smallest say
#
# bone_fit is measured as a stretch-density profile per bone (see stretch_profile): span of the
# band the cage stretches over, and where that band sits. That is stricter than "is there a ring
# at each joint" and it stays meaningful when a cage vertex rides on a blend of joints.
#
# Each axis reports a raw measurement and a [0,1] term (1 = ideal). The aggregate is the
# weighted sum; `feasible` reports the three hard axes separately so a high score can never
# hide a violation.

import numpy as np

from . import geom

WEIGHTS = {'containment': 0.34, 'collision': 0.24, 'bone_fit': 0.20, 'tightness': 0.14, 'simplicity': 0.08}

# A quad pipe over ~25 body segments with junctions is roughly 150 vertices; twice that is
# still "not too complex", so simplicity decays on that scale.
SIMPLE_REF = 300


class cage_out:
    # verts/tris: the cage surface. Each vertex rides on one or more joints -- rig[i] lists the
    # joint indices and w[i] their weights (summing to 1) -- which is what makes bone-length
    # transmission measurable without implementing the deformation itself.
    def __init__(self, verts, tris, rig, w):
        self.verts = np.asarray(verts, dtype=np.float64)
        self.tris = np.asarray(tris, dtype=np.int32)
        self.rig = np.asarray(rig, dtype=np.int32)
        self.w = np.asarray(w, dtype=np.float64)

    def sensitivity(self, joints):
        # How much each cage vertex follows a set of joints: 1 = rides entirely on them.
        return (self.w * np.isin(self.rig, list(joints))).sum(axis=1)


def containment(sk, cage, posed, stride):
    pts = posed[::stride]
    w = geom.winding(pts, cage.verts, cage.tris)
    out = w <= 0.5
    n_out = int(out.sum())
    depth = geom.point_tri_dist(pts[out], cage.verts, cage.tris) if n_out else np.zeros(0)
    frac = n_out / len(pts)
    # A handful of vertices a hair outside is nearly harmless; a deep escape is not. Blend a
    # count term with a depth term, both saturating at what would be a clearly broken cage.
    term = (1.0 - min(1.0, frac / 0.01)) * 0.5 + (1.0 - min(1.0, float(depth.max() if n_out else 0.0) / (0.02 * sk.height))) * 0.5
    return {'outside': n_out, 'tested': len(pts), 'outside_frac': frac,
            'escape_max_mm': float(depth.max() * 1000) if n_out else 0.0,
            'escape_mean_mm': float(depth.mean() * 1000) if n_out else 0.0,
            'term': float(term)}


def collision(cage):
    top = geom.topology(cage.verts, cage.tris)
    pairs = geom.self_intersections(cage.verts, cage.tris)
    clean = top['closed'] and top['oriented'] and len(pairs) == 0
    # Topology defects and intersections are equally fatal; scale by how much of the mesh is
    # involved so a nearly-clean cage scores above a hopeless one.
    bad = len(pairs) / max(1, top['tris']) + (0.0 if top['closed'] else 0.5) + (0.0 if top['oriented'] else 0.25)
    return {'closed': top['closed'], 'oriented': top['oriented'], 'components': top['components'],
            'boundary_edges': top['boundary_edges'], 'nonmanifold_edges': top['nonmanifold_edges'],
            'euler': top['euler'], 'intersecting_pairs': int(len(pairs)),
            'clean': bool(clean), 'term': float(max(0.0, 1.0 - bad))}


def stretch_profile(sk, cage, joint_pos, j, radius):
    # Lengthening bone p->j translates every cage vertex by (its sensitivity to the distal
    # subtree) x dL. Along the bone axis that sensitivity climbs from 0 to 1, and where it
    # climbs is where the cage actually stretches. Reduce that climb to two numbers:
    #   span   = effective width of the stretch band in bone lengths (1 = spread over exactly
    #            the bone, 0.3 = crammed into a third of it, 2 = smeared over its neighbours)
    #   center = where the band sits, in bone lengths from the parent joint (0.5 = centred)
    # The participation ratio 1/integral((ds/dt)^2 dt) gives span without needing thresholds,
    # so it works for blended anchors as well as rigid ones.
    p = int(sk.parent[j])
    a = joint_pos[j] - joint_pos[p]
    L = float(np.linalg.norm(a))
    if L < 1e-9:
        return None
    d = a / L

    rel = cage.verts - joint_pos[p]
    t = (rel @ d) / L
    lat = np.linalg.norm(rel - np.outer(rel @ d, d), axis=1)
    keep = (lat < radius) & (t > -1.5) & (t < 2.5)
    if keep.sum() < 4:
        return None

    s = cage.sensitivity(sk.subtree(j))[keep]
    t = t[keep]
    o = np.argsort(t)
    t, s = t[o], np.maximum.accumulate(s[o])

    # Collapse near-ties in t (keeping the accumulated s), then keep only the rising steps.
    # Co-located vertices with different sensitivities are a shear rather than a stretch; the
    # 1-D profile cannot express that, so they merge instead of producing an infinite gradient.
    last = np.r_[np.diff(t) > 0.02, True]
    tt, ss = t[last], s[last]
    dt, ds = np.diff(tt), np.diff(ss)
    rise = ds > 1e-9
    if not rise.any():
        return None
    dt, ds = dt[rise], ds[rise]
    mid = (tt[:-1][rise] + tt[1:][rise]) * 0.5

    total = ds.sum()
    span = float(total ** 2 / (ds ** 2 / dt).sum())
    center = float((mid * ds).sum() / total)
    return {'span': span, 'center': center, 'reach': float(total)}


def bone_fit(sk, cage, joint_pos, names, radii):
    rows = {}
    for n in names:
        r = stretch_profile(sk, cage, joint_pos, sk.index[n], radii[n])
        if r is None or r['reach'] < 0.9 or r['span'] < 0.05:
            rows[n] = {'reflected': False, 'err': 3.0, **(r or {'span': 0.0, 'center': 0.0, 'reach': 0.0})}
        else:
            rows[n] = {'reflected': True, 'err': abs(r['span'] - 1.0) + 2.0 * abs(r['center'] - 0.5), **r}

    errs = np.array([r['err'] for r in rows.values()])
    ok = all(r['reflected'] for r in rows.values())
    return {'all_reflected': bool(ok),
            'unreflected': [n for n, r in rows.items() if not r['reflected']],
            'err_mean': float(errs.mean()), 'err_max': float(errs.max()),
            'per_bone': rows, 'term': float(np.exp(-errs / 0.5).mean())}


def corridor_radii(sk, names):
    # Radius around each bone axis that counts as "on this limb", from the flesh the bone owns.
    out = {}
    for n in names:
        j = sk.index[n]
        p = int(sk.parent[j])
        a = sk.rest_pos[j] - sk.rest_pos[p]
        d = a / max(np.linalg.norm(a), 1e-9)
        pts = sk.verts[np.isin(sk.dominant, [j, p])]
        rel = pts - sk.rest_pos[p]
        lat = np.linalg.norm(rel - np.outer(rel @ d, d), axis=1)
        out[n] = float(max(0.06, lat.max() * 1.5)) if len(pts) else 0.1
    return out


def tightness(sk, cage, posed, stride):
    body = abs(geom.volume(posed, sk.tris))
    cv = abs(geom.volume(cage.verts, cage.tris))
    ratio = cv / body
    pts = posed[::max(1, stride * 8)]
    gap = geom.point_tri_dist(pts, cage.verts, cage.tris)
    # A cage that hugs the body sits near ratio 1. Twice the body volume is loose but usable;
    # decay so that 1.0 -> 1, 2.0 -> ~0.37.
    return {'volume_ratio': float(ratio), 'gap_mean_mm': float(gap.mean() * 1000),
            'gap_max_mm': float(gap.max() * 1000),
            'term': float(np.exp(-max(0.0, ratio - 1.0)))}


def simplicity(cage):
    n = len(np.unique(geom.weld(cage.verts, cage.tris)[0], axis=0))
    return {'cage_verts': int(n), 'cage_tris': int(len(cage.tris)),
            'term': float(np.exp(-max(0.0, n - SIMPLE_REF) / SIMPLE_REF))}


_RADII = {}


def evaluate(sk, cage, scale, stride=3):
    posed = sk.skin(scale)
    jp = sk.joint_pos(scale)
    from .rest import REQUIRED
    if not _RADII:
        _RADII.update(corridor_radii(sk, REQUIRED))
    m = {'containment': containment(sk, cage, posed, stride),
         'collision': collision(cage),
         'bone_fit': bone_fit(sk, cage, jp, REQUIRED, _RADII),
         'tightness': tightness(sk, cage, posed, stride),
         'simplicity': simplicity(cage)}
    m['score'] = float(sum(WEIGHTS[k] * m[k]['term'] for k in WEIGHTS))
    m['feasible'] = bool(m['containment']['outside'] == 0 and m['collision']['clean'] and m['bone_fit']['all_reflected'])
    return m


def brief(m):
    return (f"score {m['score']:.3f} {'OK ' if m['feasible'] else 'BAD'} "
            f"out {m['containment']['outside']}/{m['containment']['tested']}"
            f"(max {m['containment']['escape_max_mm']:.1f}mm) "
            f"xsect {m['collision']['intersecting_pairs']} closed {int(m['collision']['closed'])} "
            f"vol {m['tightness']['volume_ratio']:.2f} "
            f"bone {m['bone_fit']['err_mean']:.2f}/{len(m['bone_fit']['unreflected'])}miss "
            f"v {m['simplicity']['cage_verts']}")
