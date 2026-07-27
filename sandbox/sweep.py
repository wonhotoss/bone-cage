# Parameter study: run a list of v2_pipe variants over the whole deformation set and write a
# markdown table. This is what the recorded numbers in docs/cage-lab.md come from.
#
#   py -3.13 sweep.py            default variant list
#   py -3.13 sweep.py --stride 4 faster, slightly coarser containment counts

import argparse
import json
import time
from pathlib import Path

import numpy as np

from cagelab import metrics
from cagelab.algos import get
from cagelab.rest import skeleton, case_set

OUT = Path(__file__).parent / 'out'

VARIANTS = [
    ('v1_tubes', {}),
    ('v2_pipe', {}),
    ('v2_pipe', {'repair_iters': 0}),        # structure only, no containment repair
    ('v2_pipe', {'repair_poses': 0}),        # repair trained on rest + uniforms only
    ('v2_pipe', {'repair_smooth': 1}),       # dilate each correction over the one-ring
    ('v2_pipe', {'yoke_rig': 'Neck'}),       # let the chest bone stretch the shoulder block
    ('v2_pipe', {'torso_subdiv': 0, 'limb_subdiv': 1}),
    ('v2_pipe', {'torso_subdiv': 1, 'limb_subdiv': 1}),
    ('v2_pipe', {'margin': 1.02}),
    ('v2_pipe', {'margin': 1.06}),
    ('v2_pipe', {'miter_deg': 90}),          # bisector ring at the ankle instead of a miter
]


def label(base, kw):
    return base if not kw else base + '[' + ','.join(f'{k}={v}' for k, v in sorted(kw.items())) + ']'


def run(sk, cases, base, kw, stride):
    a = get(base) if not kw else get(base).__class__(**kw)
    t0 = time.time()
    const = a.bake(sk)
    bake_s = time.time() - t0
    rows = [metrics.evaluate(sk, a.build(const, sk, sc), sc, stride=stride) for _, sc in cases]
    score = np.array([r['score'] for r in rows])
    miss = sorted({b for r in rows for b in r['bone_fit']['unreflected']})
    return {
        'name': label(base, kw), 'bake_s': round(bake_s, 1),
        'score_mean': float(score.mean()), 'score_min': float(score.min()),
        'worst': cases[int(score.argmin())][0],
        'feasible': int(sum(r['feasible'] for r in rows)),
        'out_max': max(r['containment']['outside'] for r in rows),
        'out_rest': rows[0]['containment']['outside'],
        'esc_max_mm': round(max(r['containment']['escape_max_mm'] for r in rows), 1),
        'xsect_max': max(r['collision']['intersecting_pairs'] for r in rows),
        'closed_all': all(r['collision']['closed'] for r in rows),
        'vol': round(float(np.mean([r['tightness']['volume_ratio'] for r in rows])), 2),
        'bone_err': round(float(np.mean([r['bone_fit']['err_mean'] for r in rows])), 3),
        'miss': miss, 'verts': rows[0]['simplicity']['cage_verts'],
        'per_bone': {n: round(r['span'], 2) for n, r in rows[0]['bone_fit']['per_bone'].items()},
    }


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--stride', type=int, default=3)
    args = ap.parse_args()

    sk = skeleton()
    cases = case_set(sk)
    table = []
    for base, kw in VARIANTS:
        r = run(sk, cases, base, kw, args.stride)
        table.append(r)
        print(f'{r["name"]:44s} mean {r["score_mean"]:.3f} min {r["score_min"]:.3f} '
              f'out<={r["out_max"]:4d} esc<={r["esc_max_mm"]:5.1f} xs<={r["xsect_max"]:4d} '
              f'vol {r["vol"]:.2f} bone {r["bone_err"]:.2f} v {r["verts"]:3d} ({r["bake_s"]}s)', flush=True)

    OUT.mkdir(exist_ok=True)
    (OUT / 'sweep.json').write_text(json.dumps(table, indent=2), encoding='utf-8')

    lines = ['| variant | score mean | worst | outside (rest / max) | escape max | self-x max | vol ratio | bone err | verts |',
             '|---|---|---|---|---|---|---|---|---|']
    for r in sorted(table, key=lambda x: -x['score_mean']):
        lines.append(f'| `{r["name"]}` | {r["score_mean"]:.3f} | {r["score_min"]:.3f} ({r["worst"]}) | '
                     f'{r["out_rest"]} / {r["out_max"]} | {r["esc_max_mm"]:.1f} mm | {r["xsect_max"]} | '
                     f'{r["vol"]:.2f} | {r["bone_err"]:.3f} | {r["verts"]} |')
    (OUT / 'sweep.md').write_text('\n'.join(lines) + '\n', encoding='utf-8')
    print('\n' + '\n'.join(lines))


if __name__ == '__main__':
    main()
