# Ad-hoc inspection: the stretch profile of one bone, and where escaped vertices sit.
#
#   py -3.13 probe.py v2_pipe profile LeftFoot Spine1
#   py -3.13 probe.py v2_pipe escapes

import sys

import numpy as np

from cagelab import metrics
from cagelab.algos import get
from cagelab.rest import skeleton, REQUIRED


def profile(sk, cage, jp, name, radius):
    j = sk.index[name]
    p = int(sk.parent[j])
    a = jp[j] - jp[p]
    L = np.linalg.norm(a)
    d = a / L
    rel = cage.verts - jp[p]
    t = (rel @ d) / L
    lat = np.linalg.norm(rel - np.outer(rel @ d, d), axis=1)
    keep = (lat < radius) & (t > -1.5) & (t < 2.5)
    s = cage.sensitivity(sk.subtree(j))
    rows = sorted(zip(t[keep], s[keep], lat[keep]))
    print(f'--- {name}  L={L:.4f} radius={radius:.3f}  n={keep.sum()}')
    prev = None
    for tv, sv, lv in rows:
        if prev is not None and abs(tv - prev[0]) < 1e-4 and abs(sv - prev[1]) < 1e-9:
            continue
        print(f'   t={tv:+.4f} s={sv:.4f} lat={lv:.3f}')
        prev = (tv, sv)
    print('   ', metrics.stretch_profile(sk, cage, jp, j, radius))


def main():
    name = sys.argv[1]
    what = sys.argv[2]
    sk = skeleton()
    a = get(name)
    const = a.bake(sk)
    scale = np.ones(len(sk.name))
    cage = a.build(const, sk, scale)
    jp = sk.joint_pos(scale)

    if what == 'profile':
        radii = metrics.corridor_radii(sk, REQUIRED)
        for n in sys.argv[3:] or REQUIRED:
            profile(sk, cage, jp, n, radii[n])
    elif what == 'escapes':
        from cagelab import geom
        body = sk.skin(scale)
        out = body[~geom.inside(body, cage.verts, cage.tris)]
        print(f'{len(out)} / {len(body)} outside')
        owner = sk.name
        dom = sk.dominant[~geom.inside(body, cage.verts, cage.tris)]
        import collections
        for b, c in collections.Counter(dom.tolist()).most_common(14):
            sel = out[dom == b]
            print(f'  {owner[b]:18s} {c:5d}  x[{sel[:,0].min():+.3f},{sel[:,0].max():+.3f}]'
                  f' y[{sel[:,1].min():+.3f},{sel[:,1].max():+.3f}] z[{sel[:,2].min():+.3f},{sel[:,2].max():+.3f}]')


if __name__ == '__main__':
    main()
