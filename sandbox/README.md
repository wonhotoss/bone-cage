# cage sandbox

Unity-free harness for iterating on the cage generation algorithm. The cage is a pure function of
bone lengths plus baked constants, so it can be developed and scored entirely outside the engine.
See [../docs/cage-lab.md](../docs/cage-lab.md) for the design decisions, scoring weights and results.

Requires Python 3.13 with numpy, scipy and pillow (`py -3.13`).

## layout

| path | role |
|---|---|
| `fbx.py` | minimal binary-FBX reader (node tree + decoded properties) |
| `export_rest.py` | FBX -> `data/rest.npz`: rest skeleton, rest mesh, skin weights. Run once. |
| `cagelab/rest.py` | skeleton model, forward kinematics, LBS, deformation test cases |
| `cagelab/geom.py` | closed-mesh predicates: winding number, topology, self-intersection, volume |
| `cagelab/metrics.py` | the evaluator: containment / collision / bone_fit / tightness / simplicity |
| `cagelab/viz.py` | orthographic PNG previews (body point cloud + cage wireframe + escapes) |
| `cagelab/algos/` | the candidate algorithms |
| `run_eval.py` | score one or more algorithms over the deformation set |
| `sweep.py` | parameter study -> `out/sweep.md` |
| `probe.py` | inspect one bone's stretch profile, or where escaped vertices sit |

## typical loop

```
py -3.13 run_eval.py v2_pipe -v --render     # score + out/v2_pipe/*.png + *.obj
py -3.13 probe.py v2_pipe escapes            # which bones own the escaped vertices
py -3.13 probe.py v2_pipe profile LeftLeg    # why a bone's stretch band is wrong
py -3.13 sweep.py                            # parameter table
```

`run_eval.py v2_pipe:margin=1.1,torso_subdiv=3` builds a parameter variant without editing code.

## algorithm contract

```python
const = algo.bake(sk)              # may read the rest mesh; offline, may be slow
cage  = algo.build(const, sk, scale)   # pure function of bone lengths
```

`scale` is a per-bone length multiplier. `build` returns `cage_out(verts, tris, rig, w)`, where
`rig[i]` / `w[i]` say which joints cage vertex `i` rides on. That rig is what makes bone-length
transmission measurable without implementing the cage deformation itself.
