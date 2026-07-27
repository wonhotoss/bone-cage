# Sandbox driver: bake an algorithm once, then score it over the deformation test set.
#
#   py -3.13 run_eval.py                    all algorithms, summary table
#   py -3.13 run_eval.py v3_bmesh -v        per-case detail
#   py -3.13 run_eval.py v3_bmesh --render  also write out/<algo>/*.png and rest.obj

import argparse
import json
import time
from pathlib import Path

import numpy as np

from cagelab import metrics, geom, viz
from cagelab.rest import skeleton, case_set, REQUIRED
from cagelab import algos

OUT = Path(__file__).parent / 'out'


def run(sk, a, cases, stride, verbose, render, render_cases):
    const = a.bake(sk)
    rows = []
    for name, scale in cases:
        t0 = time.time()
        cage = a.build(const, sk, scale)
        m = metrics.evaluate(sk, cage, scale, stride=stride)
        m['case'] = name
        m['build_ms'] = (time.time() - t0) * 1000
        rows.append(m)
        if verbose:
            print(f'  {name:14s} {metrics.brief(m)}')
        if render and (render_cases is None or name in render_cases):
            d = OUT / a.name
            d.mkdir(parents=True, exist_ok=True)
            viz.render(d / f'{name}.png', sk, cage, scale, title=f'{a.name} / {name} / {metrics.brief(m)}')
            geom.write_obj(d / f'{name}.obj', cage.verts, cage.tris)
    return rows


def summarise(name, rows):
    sc = np.array([r['score'] for r in rows])
    feas = sum(r['feasible'] for r in rows)
    worst = min(rows, key=lambda r: r['score'])
    return {
        'algo': name, 'cases': len(rows), 'feasible': feas,
        'score_mean': float(sc.mean()), 'score_min': float(sc.min()),
        'worst_case': worst['case'],
        'outside_max': max(r['containment']['outside'] for r in rows),
        'escape_max_mm': max(r['containment']['escape_max_mm'] for r in rows),
        'xsect_max': max(r['collision']['intersecting_pairs'] for r in rows),
        'closed_all': all(r['collision']['closed'] for r in rows),
        'vol_ratio_mean': float(np.mean([r['tightness']['volume_ratio'] for r in rows])),
        'bone_err_mean': float(np.mean([r['bone_fit']['err_mean'] for r in rows])),
        'bone_miss': sorted({b for r in rows for b in r['bone_fit']['unreflected']}),
        'cage_verts': rows[0]['simplicity']['cage_verts'],
    }


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('algo', nargs='*', default=None)
    ap.add_argument('--stride', type=int, default=3)
    ap.add_argument('-v', '--verbose', action='store_true')
    ap.add_argument('--render', action='store_true')
    ap.add_argument('--render-cases', default='rest,uniform_0.7,uniform_1.4,grouped_0,grouped_3,random_0')
    ap.add_argument('--cases', default=None, help='comma-separated case subset')
    args = ap.parse_args()

    sk = skeleton()
    cases = case_set(sk)
    if args.cases:
        want = set(args.cases.split(','))
        cases = [c for c in cases if c[0] in want]

    names = args.algo or list(algos.REGISTRY)
    table = []
    for n in names:
        a = algos.get(n)
        print(f'== {n}')
        rows = run(sk, a, cases, args.stride, args.verbose, args.render,
                   set(args.render_cases.split(',')) if args.render_cases else None)
        s = summarise(n, rows)
        table.append(s)
        print(f'  mean {s["score_mean"]:.3f}  min {s["score_min"]:.3f} ({s["worst_case"]})  '
              f'feasible {s["feasible"]}/{s["cases"]}  out<={s["outside_max"]} '
              f'esc<={s["escape_max_mm"]:.1f}mm  xsect<={s["xsect_max"]}  closed {s["closed_all"]}  '
              f'vol {s["vol_ratio_mean"]:.2f}  boneerr {s["bone_err_mean"]:.3f} miss {s["bone_miss"]}  v {s["cage_verts"]}')

    OUT.mkdir(exist_ok=True)
    (OUT / 'summary.json').write_text(json.dumps(table, indent=2), encoding='utf-8')

    print()
    print(f'{"algo":22s} {"mean":>6s} {"min":>6s} {"feas":>6s} {"out":>6s} {"esc":>7s} {"xs":>5s} {"vol":>5s} {"bone":>6s} {"v":>5s}')
    for s in sorted(table, key=lambda r: -r['score_mean']):
        print(f'{s["algo"]:22s} {s["score_mean"]:6.3f} {s["score_min"]:6.3f} '
              f'{s["feasible"]:3d}/{s["cases"]:<2d} {s["outside_max"]:6d} {s["escape_max_mm"]:7.1f} '
              f'{s["xsect_max"]:5d} {s["vol_ratio_mean"]:5.2f} {s["bone_err_mean"]:6.3f} {s["cage_verts"]:5d}')


if __name__ == '__main__':
    main()
