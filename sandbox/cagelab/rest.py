# The rest skeleton + rest skinned mesh, and the deformations we test cages against.
#
# A deformation is a per-bone length multiplier. Changing a bone's length only scales the
# translation of its local transform, so rest directions are invariant -- exactly the
# assumption cage.build() relies on.

from pathlib import Path

import numpy as np

DATA = Path(__file__).resolve().parent.parent / 'data' / 'rest.npz'


class skeleton:
    def __init__(self, path=DATA):
        d = np.load(path, allow_pickle=False)
        self.name = [str(s) for s in d['bone_name']]
        self.parent = d['bone_parent'].astype(int)
        self.rest_global = d['bone_global']
        self.verts = d['verts']
        self.tris = d['tris']
        self.w_idx = d['w_idx']
        self.w_val = d['w_val']

        self.index = {n: i for i, n in enumerate(self.name)}
        self.rest_local = np.array([self.rest_global[i] if self.parent[i] < 0
                                    else np.linalg.inv(self.rest_global[self.parent[i]]) @ self.rest_global[i]
                                    for i in range(len(self.name))])
        self.rest_pos = self.rest_global[:, :3, 3]
        self.rest_len = np.array([0.0 if self.parent[i] < 0
                                  else np.linalg.norm(self.rest_local[i][:3, 3])
                                  for i in range(len(self.name))])
        self.inv_bind = np.linalg.inv(self.rest_global)
        self.dominant = self.w_idx[np.arange(len(self.w_idx)), self.w_val.argmax(axis=1)]
        self.height = float(self.verts[:, 1].max() - self.verts[:, 1].min())

        kids = [[] for _ in self.name]
        for i, p in enumerate(self.parent):
            if p >= 0:
                kids[p].append(i)
        self.children = kids

    def subtree(self, j):
        out, stack = [], [j]
        while stack:
            i = stack.pop()
            out.append(i)
            stack += self.children[i]
        return out

    def globals(self, scale):
        # scale: (N,) length multiplier per bone (1.0 = rest).
        g = np.empty_like(self.rest_global)
        for i, p in enumerate(self.parent):
            local = self.rest_local[i].copy()
            if p < 0:
                g[i] = local
            else:
                local[:3, 3] *= scale[i]
                g[i] = g[p] @ local
        return g

    def joint_pos(self, scale):
        return self.globals(scale)[:, :3, 3]

    def skin(self, scale):
        g = self.globals(scale)
        m = g @ self.inv_bind                                # (N,4,4) bind-space -> posed
        p = np.c_[self.verts, np.ones(len(self.verts))]
        out = np.zeros((len(self.verts), 3))
        for k in range(4):
            b = self.w_idx[:, k]
            w = self.w_val[:, k][:, None]
            out += w * np.einsum('vij,vj->vi', m[b], p)[:, :3]
        return out

    def lengths(self, scale):
        return {self.name[i]: float(self.rest_len[i] * scale[i]) for i in range(len(self.name))}


# Bones whose length a cage must transmit as a control-point offset. Named by the distal
# joint, as in mapping_tester.anatomy.
REQUIRED = [
    'Spine', 'Spine1', 'Spine2', 'Spine3',          # midline: pelvis .. upper thorax
    'Neck', 'Neck1', 'Head',                        # midline: chest, lower/upper neck
    'LeftForeArm', 'LeftHand',                      # upper arm, forearm
    'RightForeArm', 'RightHand',
    'LeftLeg', 'LeftFoot', 'LeftToeBase',           # thigh, calf, foot
    'RightLeg', 'RightFoot', 'RightToeBase',
]

# Length changes that do occur in a real body but that the cage need not resolve as its own
# control points -- credited as a bonus, never required.
OPTIONAL = ['LeftArm', 'RightArm', 'LeftUpLeg', 'RightUpLeg']

# Bones excluded from both the requirement and the test deformations: a buried bone whose
# length cannot change the silhouette (Spine3->Shoulder sits on the midline), and the
# metacarpals, which do not lengthen on their own.
FROZEN_PREFIX = ('LeftShoulder', 'RightShoulder')
FROZEN_SUFFIX = ('Thumb1', 'Index1', 'Middle1', 'Ring1', 'Pinky1')


def deformable(sk):
    def ok(n):
        if n in FROZEN_PREFIX:
            return False
        return not n.endswith(FROZEN_SUFFIX)
    return [i for i, n in enumerate(sk.name) if sk.parent[i] >= 0 and ok(n)]


def case_rest(sk):
    return np.ones(len(sk.name))


def case_uniform(sk, f):
    s = np.ones(len(sk.name))
    s[deformable(sk)] = f
    return s


def case_random(sk, rng, lo=0.5, hi=1.5):
    s = np.ones(len(sk.name))
    idx = deformable(sk)
    s[idx] = rng.uniform(lo, hi, len(idx))
    return s


def case_grouped(sk, rng, lo=0.5, hi=1.5):
    # Asymmetric but anatomically coherent: one factor per limb segment group, so left and
    # right and upper and lower body drift apart without individual bones going ragged.
    groups = {
        'spine': ['Spine', 'Spine1', 'Spine2', 'Spine3'],
        'neck': ['Neck', 'Neck1', 'Head'],
        'l_upper_arm': ['LeftForeArm'], 'l_forearm': ['LeftHand'],
        'r_upper_arm': ['RightForeArm'], 'r_forearm': ['RightHand'],
        'l_thigh': ['LeftLeg'], 'l_calf': ['LeftFoot'], 'l_foot': ['LeftToeBase'],
        'r_thigh': ['RightLeg'], 'r_calf': ['RightFoot'], 'r_foot': ['RightToeBase'],
        'l_clavicle': ['LeftArm'], 'r_clavicle': ['RightArm'],
        'l_hip': ['LeftUpLeg'], 'r_hip': ['RightUpLeg'],
    }
    s = np.ones(len(sk.name))
    for names in groups.values():
        f = rng.uniform(lo, hi)
        for n in names:
            s[sk.index[n]] = f
    return s


def case_set(sk, seed=7, n_random=6, n_grouped=10):
    rng = np.random.default_rng(seed)
    cases = [('rest', case_rest(sk)),
             ('uniform_0.7', case_uniform(sk, 0.7)),
             ('uniform_1.4', case_uniform(sk, 1.4))]
    cases += [(f'grouped_{i}', case_grouped(sk, rng)) for i in range(n_grouped)]
    cases += [(f'random_{i}', case_random(sk, rng)) for i in range(n_random)]
    return cases
