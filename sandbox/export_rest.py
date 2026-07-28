# FBX -> rest.npz: the rest skeleton and rest skinned mesh the cage sandbox works on.
#
# Everything is emitted in "hips space": the rest-global frame of the Hips bone inverted
# out, matching what cage.cs does with root.InverseTransformPoint.
#
# Cluster matrices follow the exporter convention Transform = bone_bind^-1 @ mesh_world and
# TransformLink = bone_bind, so mesh_world = TransformLink @ Transform (same for every
# cluster) and skinning is p' = sum_i w_i * bone_cur_i @ bone_bind_i^-1 @ mesh_world @ v.
# Reading the bind matrices straight off the clusters avoids composing FBX local transforms
# (pre/post-rotation, offsets, inherit types) by hand.

import json
import sys
from pathlib import Path

import numpy as np

import fbx

FBX = Path(__file__).resolve().parent.parent / 'unity/Assets/models/ViconActorFingers_orient_finger_fixed.fbx'
OUT = Path(__file__).parent / 'data'


def mat(props):
    # FBX stores matrices row-major with translation in the last row; transpose to column-vector convention.
    return np.array(props, dtype=np.float64).reshape(4, 4).T


def clean(name):
    return name.split('\x00')[0]


def main():
    root = fbx.load(FBX)
    objects = root.find('Objects')

    by_id = {c.props[0]: c for c in objects.children}

    # Connections are (kind, child_id, parent_id).
    parent_of = {}
    children_of = {}
    for c in root.find('Connections').children:
        if c.props[0] != 'OO':
            continue
        child, par = c.props[1], c.props[2]
        parent_of.setdefault(child, []).append(par)
        children_of.setdefault(par, []).append(child)

    geom = objects.find('Geometry')
    ctrl = np.array(geom.find('Vertices').props[0], dtype=np.float64).reshape(-1, 3)
    pvi = np.array(geom.find('PolygonVertexIndex').props[0], dtype=np.int64)

    # Polygons end on a negative-xor index. This mesh is all quads; keep it general anyway.
    faces = []
    cur = []
    for i in pvi:
        if i < 0:
            cur.append(int(~i))
            faces.append(cur)
            cur = []
        else:
            cur.append(int(i))
    tris = np.array([[f[0], f[k], f[k + 1]] for f in faces for k in range(1, len(f) - 1)], dtype=np.int32)

    clusters = [d for d in objects.find_all('Deformer') if d.props[2] == 'Cluster']

    # Each cluster links to exactly one bone Model; the connection records the bone as a child of the cluster.
    bone_of_cluster = {}
    for cl in clusters:
        kids = children_of.get(cl.props[0], [])
        bones = [k for k in kids if k in by_id and by_id[k].name == 'Model']
        assert len(bones) == 1, (clean(cl.props[1]), bones)
        bone_of_cluster[cl.props[0]] = bones[0]

    models = {m.props[0]: m for m in objects.find_all('Model')}
    limb_ids = [i for i, m in models.items() if m.props[2] == 'LimbNode']

    # Order bones parent-before-child by walking down from the roots.
    limb_set = set(limb_ids)
    kid_limbs = {i: [k for k in children_of.get(i, []) if k in limb_set] for i in limb_ids}
    has_limb_parent = {i: any(p in limb_set for p in parent_of.get(i, [])) for i in limb_ids}
    order = []

    def walk(i):
        order.append(i)
        for k in sorted(kid_limbs[i], key=lambda x: clean(models[x].props[1])):
            walk(k)

    for i in limb_ids:
        if not has_limb_parent[i]:
            walk(i)
    assert len(order) == len(limb_ids)

    slot = {bid: n for n, bid in enumerate(order)}
    names = [clean(models[i].props[1]) for i in order]
    parents = np.array([next((slot[p] for p in parent_of.get(i, []) if p in limb_set), -1) for i in order], dtype=np.int32)

    link = np.tile(np.eye(4), (len(order), 1, 1))
    mesh_bind = None
    weights = {}
    for cl in clusters:
        b = slot[bone_of_cluster[cl.props[0]]]
        link[b] = mat(cl.find('TransformLink').props[0])
        world = link[b] @ mat(cl.find('Transform').props[0])
        if mesh_bind is None:
            mesh_bind = world
        assert np.allclose(mesh_bind, world, atol=1e-4), f'{names[b]} disagrees on the mesh bind transform'
        idx = cl.find('Indexes')
        if idx is not None:
            for v, w in zip(idx.props[0], cl.find('Weights').props[0]):
                weights.setdefault(int(v), []).append((b, float(w)))

    # Hips space, as in cage.cs. Points come in via the mesh bind transform.
    hips = names.index('Hips')
    to_hips = np.linalg.inv(link[hips])
    g = to_hips @ link                                   # bone rest globals, hips space
    verts = (to_hips @ mesh_bind @ np.c_[ctrl, np.ones(len(ctrl))].T).T[:, :3]

    # Top-4 weights per vertex, normalized -- matches the Unity importer's maxBonesPerVertex.
    n = len(ctrl)
    w_idx = np.zeros((n, 4), dtype=np.int32)
    w_val = np.zeros((n, 4), dtype=np.float64)
    for v, ws in weights.items():
        ws = sorted(ws, key=lambda e: -e[1])[:4]
        s = sum(w for _, w in ws)
        for k, (b, w) in enumerate(ws):
            w_idx[v, k] = b
            w_val[v, k] = w / s

    unweighted = int((w_val.sum(axis=1) < 0.5).sum())
    OUT.mkdir(exist_ok=True)
    np.savez_compressed(
        OUT / 'rest.npz',
        bone_name=np.array(names), bone_parent=parents, bone_global=g,
        verts=verts, tris=tris, w_idx=w_idx, w_val=w_val,
    )

    rest_len = np.array([0.0 if parents[i] < 0 else np.linalg.norm(g[i][:3, 3] - g[parents[i]][:3, 3]) for i in range(len(names))])
    (OUT / 'rest_summary.json').write_text(json.dumps({
        'fbx_version': root.version,
        'bones': len(names),
        'control_points': n,
        'faces': len(faces),
        'tris': len(tris),
        'unweighted_verts': unweighted,
        'bbox_min': verts.min(axis=0).tolist(),
        'bbox_max': verts.max(axis=0).tolist(),
        'skeleton': [{'name': names[i], 'parent': names[parents[i]] if parents[i] >= 0 else None,
                      'len': round(float(rest_len[i]), 6),
                      'pos': [round(float(x), 6) for x in g[i][:3, 3]]} for i in range(len(names))],
    }, indent=2), encoding='utf-8')
    print(f'wrote {OUT / "rest.npz"}: {len(names)} bones, {n} verts, {len(tris)} tris, {unweighted} unweighted')


if __name__ == '__main__':
    sys.exit(main())
