# v1: the algorithm currently in unity/Assets/Scenes/cage.cs, ported verbatim as the baseline.
#
# Six independent quad tubes (spine, neck+head, two arms, two legs) whose cross-sections are
# measured from the rest flesh. Limbs are simply buried in the torso where they meet, so the
# result is neither a single closed mesh nor free of self-intersection -- which is exactly the
# gap this sandbox exists to close.

import numpy as np

from ..metrics import cage_out
from .common import flesh_groups, frame_from, half_extents, quad

JOINTS = ['Hips', 'Spine', 'Spine1', 'Spine2', 'Spine3',
          'Neck', 'Neck1', 'Head',
          'LeftShoulder', 'LeftArm', 'LeftForeArm', 'LeftHand', 'LeftHandMiddle1',
          'RightShoulder', 'RightArm', 'RightForeArm', 'RightHand', 'RightHandMiddle1',
          'LeftUpLeg', 'LeftLeg', 'LeftFoot', 'LeftToeBase',
          'RightUpLeg', 'RightLeg', 'RightFoot', 'RightToeBase']

CHAINS = [(['Hips', 'Spine', 'Spine1', 'Spine2', 'Spine3'], False),
          (['Neck', 'Neck1', 'Head'], True),
          (['LeftShoulder', 'LeftArm', 'LeftForeArm', 'LeftHand', 'LeftHandMiddle1'], True),
          (['RightShoulder', 'RightArm', 'RightForeArm', 'RightHand', 'RightHandMiddle1'], True),
          (['LeftUpLeg', 'LeftLeg', 'LeftFoot', 'LeftToeBase'], True),
          (['RightUpLeg', 'RightLeg', 'RightFoot', 'RightToeBase'], True)]

MARGIN = 0.05


class algo:
    name = 'v1_tubes'

    def bake(self, sk):
        flesh, _ = flesh_groups(sk, JOINTS)
        rings = []          # (joint, ext_from, ext_dir, ext_len, u, v, hu, hv)
        chains = []

        for names, extend in CHAINS:
            js = [sk.index[n] for n in names]
            start = len(rings)
            axis = ring_axes(js, sk.rest_pos)

            u = None
            frames = []
            for a in axis:
                u, v = frame_from(a, u)
                frames.append((u, v))

            for s, j in enumerate(js):
                pts = flesh.get(j, np.zeros((0, 3)))
                if s > 0:
                    pts = np.r_[pts, flesh.get(js[s - 1], np.zeros((0, 3)))]
                u, v = frames[s]
                hu, hv = half_extents(pts, sk.rest_pos[j], u, v, MARGIN)
                rings.append({'joint': j, 'from': -1, 'dir': np.zeros(3), 'len': 0.0, 'u': u, 'v': v, 'hu': hu, 'hv': hv})

            if extend:
                tip = js[-1]
                a = axis[-1]
                u, v = frames[-1]
                pts = flesh.get(tip, np.zeros((0, 3)))
                reach = float(((pts - sk.rest_pos[tip]) @ a).max()) if len(pts) else 0.0
                hu, hv = half_extents(pts, sk.rest_pos[tip] + a * reach, u, v, MARGIN)
                rings.append({'joint': -1, 'from': tip, 'dir': a, 'len': reach, 'u': u, 'v': v, 'hu': hu, 'hv': hv})

            chains.append((start, len(rings) - start))

        tris = []
        for start, count in chains:
            emit_tube(tris, start, count)
        return {'rings': rings, 'chains': chains, 'tris': np.array(tris, dtype=np.int32)}

    def build(self, const, sk, scale):
        jp = sk.joint_pos(scale)
        rings = const['rings']
        verts = np.empty((len(rings) * 4, 3))
        rig = np.empty((len(rings) * 4, 2), dtype=np.int32)
        for i, r in enumerate(rings):
            j = r['joint'] if r['joint'] >= 0 else r['from']
            c = jp[j] + (r['dir'] * r['len'] if r['joint'] < 0 else 0.0)
            u, v = r['u'] * r['hu'], r['v'] * r['hv']
            verts[i * 4:i * 4 + 4] = [c + u + v, c - u + v, c - u - v, c + u - v]
            rig[i * 4:i * 4 + 4] = j
        w = np.tile([1.0, 0.0], (len(rig), 1))
        return cage_out(verts, const['tris'], rig, w)


def ring_axes(js, pos):
    seg = np.array([pos[js[i + 1]] - pos[js[i]] for i in range(len(js) - 1)])
    seg = seg / np.linalg.norm(seg, axis=1, keepdims=True)
    axis = []
    for i in range(len(js)):
        a = seg[i - 1] if i > 0 else seg[0]
        b = seg[i] if i < len(seg) else seg[-1]
        m = a + b
        axis.append(m / np.linalg.norm(m))
    return axis


def emit_tube(tris, start, count):
    for s in range(count - 1):
        a, b = (start + s) * 4, (start + s + 1) * 4
        for k in range(4):
            kn = (k + 1) % 4
            quad(tris, a + k, a + kn, b + kn, b + k)
    first, last = start * 4, (start + count - 1) * 4
    quad(tris, first + 3, first + 2, first + 1, first + 0)
    quad(tris, last + 0, last + 1, last + 2, last + 3)
