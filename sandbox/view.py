# Bake a set of algorithm variants and write them into one self-contained HTML page, so the
# cages can be orbited and compared instead of read off a score table.
#
#   py -3.13 view.py                                     defaults over the interesting cases
#   py -3.13 view.py v2_pipe v2_pipe:repair_iters=0      one variant per dropdown entry
#   py -3.13 view.py v2_pipe --cases rest,uniform_0.7
#
# Writes out/view.html. Everything the page needs is inlined, so it opens from disk.

import argparse
import json
from pathlib import Path

import numpy as np

from cagelab import algos, geom, metrics
from cagelab.rest import case_set, skeleton

HERE = Path(__file__).resolve().parent
OUT = HERE / 'out'
TEMPLATE = HERE / 'viewer.html'
TOKEN = '__PAYLOAD__'

# Cases worth looking at by default: rest, both uniform extremes, and the two deformation
# families. The full 19 make the page slow to bake and tedious to step through.
CASES = 'rest,uniform_0.7,uniform_1.4,grouped_2,grouped_7,random_0'

# Dihedral above which a cage edge counts as structure rather than a quad's own diagonal.
COS_FLAT = float(np.cos(np.radians(12.0)))


def coords(pts, digits):
    return [round(float(x), digits) for x in np.asarray(pts).reshape(-1)]


def edge_split(cage):
    # Creases versus the diagonal inside a near-flat quad. Drawing the diagonals dimly is what
    # makes the wireframe read as rings and quads instead of a triangle soup.
    v, t = cage.verts, cage.tris
    n = np.cross(v[t[:, 1]] - v[t[:, 0]], v[t[:, 2]] - v[t[:, 0]])
    n = n / np.maximum(np.linalg.norm(n, axis=1, keepdims=True), 1e-12)
    pairs = np.sort(np.r_[t[:, [0, 1]], t[:, [1, 2]], t[:, [2, 0]]], axis=1)
    uniq, inv = np.unique(pairs, axis=0, return_inverse=True)

    faces_of = {}
    for e, f in zip(inv.reshape(-1).tolist(), np.tile(np.arange(len(t)), 3).tolist()):
        faces_of.setdefault(e, []).append(f)
    is_flat = [len(fs) == 2 and float(n[fs[0]] @ n[fs[1]]) > COS_FLAT for _, fs in sorted(faces_of.items())]

    flat_mask = np.array(is_flat)
    return ([int(i) for i in uniq[~flat_mask].reshape(-1)],
            [int(i) for i in uniq[flat_mask].reshape(-1)])


def xsect_tris(cage):
    # The intersecting triangles as loose coordinates. self_intersections indexes the welded
    # mesh, so carrying the corners avoids reproducing that indexing in the page.
    pairs = geom.self_intersections(cage.verts, cage.tris)
    v, t = geom.weld(cage.verts, cage.tris)
    return coords(v[t[np.unique(pairs)]], 4) if len(pairs) else []


def stat_html(m):
    c, col, bf = m['containment'], m['collision'], m['bone_fit']
    rows = [
        ('score', f"<b>{m['score']:.3f}</b> {'feasible' if m['feasible'] else 'not feasible'}"),
        ('outside', f"<b>{c['outside']}</b>/{c['tested']}, max <b>{c['escape_max_mm']:.1f}</b> mm"),
        ('self-x', f"<b>{col['intersecting_pairs']}</b> pairs, "
                   f"{'closed' if col['closed'] else 'OPEN'}, euler {col['euler']}"),
        ('bone err', f"<b>{bf['err_mean']:.2f}</b> mean"),
        ('bone miss', ', '.join(bf['unreflected']) or 'none'),
        ('volume', f"<b>{m['tightness']['volume_ratio']:.2f}</b>&times; body"),
        ('cage', f"<b>{m['simplicity']['cage_verts']}</b> verts, {m['simplicity']['cage_tris']} tris"),
    ]
    return ''.join(f'<div><span>{k}</span><i>{val}</i></div>' for k, val in rows)


def one_view(sk, a, const, case, scale, body_points, stride):
    cage = a.build(const, sk, scale)
    m = metrics.evaluate(sk, cage, scale, stride=stride)
    body = sk.skin(scale)
    step = max(1, len(body) // body_points)
    pts = body[::step]
    escaped = pts[~geom.inside(pts, cage.verts, cage.tris)]
    crease, diagonal = edge_split(cage)
    lo = np.minimum(body.min(axis=0), cage.verts.min(axis=0))
    hi = np.maximum(body.max(axis=0), cage.verts.max(axis=0))
    return {
        'algo': a.name, 'case': case, 'score': m['score'], 'stat': stat_html(m),
        'cage': {'v': coords(cage.verts, 4), 'e': crease, 'd': diagonal,
                 't': [int(i) for i in cage.tris.reshape(-1)]},
        'body': coords(pts, 3),
        'escape': coords(escaped, 4),
        'joints': coords(sk.joint_pos(scale), 4),
        'xsect': xsect_tris(cage),
    }, (lo, hi)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('algo', nargs='*', default=None,
                    help='algorithm names, "v2_pipe:margin=1.1" style variants included')
    ap.add_argument('--cases', default=CASES, help='comma-separated case subset, or "all"')
    ap.add_argument('--body-points', type=int, default=2600, help='body points kept per view')
    ap.add_argument('--stride', type=int, default=3, help='mesh subsampling for the metrics')
    ap.add_argument('--out', default=str(OUT / 'view.html'))
    args = ap.parse_args()

    sk = skeleton()
    cases = case_set(sk)
    if args.cases != 'all':
        want = args.cases.split(',')
        by_name = dict(cases)
        missing = [n for n in want if n not in by_name]
        assert not missing, f'unknown cases: {missing}'
        cases = [(n, by_name[n]) for n in want]

    views, boxes = [], []
    for name in args.algo or list(algos.REGISTRY):
        a = algos.get(name)
        const = a.bake(sk)
        print(f'== {a.name}')
        for case, scale in cases:
            v, box = one_view(sk, a, const, case, scale, args.body_points, args.stride)
            views.append(v)
            boxes.append(box)
            print(f'  {case:14s} score {v["score"]:.3f}  escaped {len(v["escape"]) // 3}  '
                  f'xsect tris {len(v["xsect"]) // 9}')

    payload = {'views': views,
               'bbox': [np.min([b[0] for b in boxes], axis=0).tolist(),
                        np.max([b[1] for b in boxes], axis=0).tolist()]}
    path = Path(args.out)
    path.parent.mkdir(parents=True, exist_ok=True)
    tpl = TEMPLATE.read_text(encoding='utf-8')
    assert tpl.count(TOKEN) == 1, f'{TEMPLATE.name} must hold exactly one {TOKEN}'
    path.write_text(tpl.replace(TOKEN, json.dumps(payload)), encoding='utf-8')
    print(f'\nwrote {path} ({path.stat().st_size / 1024:.0f} KB, {len(views)} views)')


if __name__ == '__main__':
    main()
