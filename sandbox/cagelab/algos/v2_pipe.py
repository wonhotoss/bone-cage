# v2: one closed quad pipe with branches.
#
# A single vertical stack of rectangular rings forms the torso; the neck/head continues it; the
# four limbs leave through quad ports inset into a torso face and continue as quad tubes. Every
# ring is sized from a slab of rest flesh, so thickness and direction vary along the pipe. The
# result is watertight by construction -- no boolean union, no overlapping boxes.
#
# Every cage vertex rides on a blend of the two joints bracketing its arc position along its own
# chain, so a ring placed between two joints stretches by the same fraction the skeleton does
# there. Ring stations near the neck override that with a rigid joint, because the trapezius
# buries the Neck/Neck1 joints and blending there smears their (45 mm) lengths across the whole
# shoulder block.

import numpy as np

from .. import geom
from ..metrics import cage_out
from .common import builder, chain, flesh_groups, rect, repair, slab

CORE = ['Hips', 'Spine', 'Spine1', 'Spine2', 'Spine3', 'Neck', 'Neck1', 'LeftShoulder', 'RightShoulder']
HEAD = ['Head']
MIDLINE = ['Hips', 'Spine', 'Spine1', 'Spine2', 'Spine3', 'Neck', 'Neck1', 'Head']
ARM = {'L': ['LeftArm', 'LeftForeArm', 'LeftHand', 'LeftHandMiddle1'],
       'R': ['RightArm', 'RightForeArm', 'RightHand', 'RightHandMiddle1']}
LEG = {'L': ['Hips', 'LeftUpLeg', 'LeftLeg', 'LeftFoot', 'LeftToeBase'],
       'R': ['Hips', 'RightUpLeg', 'RightLeg', 'RightFoot', 'RightToeBase']}

X = np.array([1.0, 0.0, 0.0])
Y = np.array([0.0, 1.0, 0.0])
Z = np.array([0.0, 0.0, 1.0])

DEFAULTS = dict(
    margin=1.12,        # rectangle inflation, so rings contain the flesh instead of touching it
    torso_subdiv=2,     # extra rings per spine segment
    limb_subdiv=2,      # extra rings per limb segment
    head_rings=3,       # rings from the jaw up to the skull cap
    tip_subdiv=1,       # extra rings between the last joint and the tip cap (fingers, toes)
    miter_deg=35,       # bend angle above which a joint gets two rings instead of a bisector one
    port_pad=0.06,      # smallest annulus width left around a port, as a face fraction
    host_margin=1.16,   # extra inflation for a ring that hosts ports, so an annulus survives
    crotch_drop=0.30,   # midline floor vertices pushed below the outer ones, as a fraction of
                        # the pelvis half-depth, so the crotch notch forms in the leg tubes
    yoke_rig='Spine3',  # joint the top of the shoulder block rides. 'Spine3' freezes the block's
                        # height so the arm port cannot punch through its host face when the spine
                        # shortens; 'Neck' instead hands the chest bone a stretch band, which reads
                        # better on paper but measures worse everywhere (docs/cage-lab.md)
    repair_iters=4,     # containment-repair passes after the pipe is built
    repair_pad=0.003,   # clearance the repair leaves beyond an escaped vertex (m)
    repair_poses=4,     # extra random bone-length sets the repair trains on, beyond rest and the
                        # two uniform extremes. Seeded separately from the evaluation cases.
    repair_stride=2,    # mesh subsampling for the non-rest training poses
    repair_smooth=0,    # one-ring dilations of each repair correction (0 measured best)
    repair_step=0.02,   # largest outward slide one repair pass may apply to a vertex (m)
)


class algo:
    name = 'v2_pipe'

    def __init__(self, **kw):
        self.p = dict(DEFAULTS, **kw)
        if kw:
            self.name = 'v2_pipe[' + ','.join(f'{k}={v}' for k, v in sorted(kw.items())) + ']'

    def bake(self, sk):
        p = self.p
        listed = CORE + HEAD + [n for v in ARM.values() for n in v] + [n for v in LEG.values() for n in v]
        flesh, _ = flesh_groups(sk, listed)

        def pts(names):
            got = [flesh[sk.index[n]] for n in names if sk.index[n] in flesh]
            return np.concatenate(got) if got else np.zeros((0, 3))

        core, head = pts(CORE), pts(HEAD)
        arm = {s: [pts([n]) for n in ARM[s]] for s in ARM}
        leg = {s: [np.zeros((0, 3))] + [pts([n]) for n in LEG[s][1:]] for s in LEG}

        b = builder(sk)
        mid = chain(sk, MIDLINE)

        # --- torso + neck + head: one vertical stack ------------------------------------------
        y_floor = float(core[:, 1].min()) - 0.005
        girdle = core[np.abs(core[:, 0]) > 0.12]
        y_yoke = float(girdle[:, 1].max()) + 0.010                # top of the shoulder block
        y_skull = float(head[:, 1].max()) + 0.008

        stack = [(y_floor, 'floor', 'chain')]
        spine_y = [float(sk.rest_pos[sk.index[n]][1]) for n in ['Hips', 'Spine', 'Spine1', 'Spine2', 'Spine3']]
        for k, y in enumerate(spine_y):
            stack.append((y, 'core', 'chain'))
            if k + 1 < len(spine_y):
                for i in range(1, p['torso_subdiv'] + 1):
                    stack.append((y + (spine_y[k + 1] - y) * i / (p['torso_subdiv'] + 1), 'core', 'chain'))
        arm_face = len(stack) - 1                                 # face above the Spine3 ring
        stack[arm_face] = (stack[arm_face][0], 'girdle', stack[arm_face][2])
        stack.append((y_yoke, 'girdle', p['yoke_rig']))
        y_neck = y_yoke + (y_skull - y_yoke) * 0.10
        stack.append((y_neck, 'core+head', 'Neck1'))
        # A ring on the Head joint itself, so the (45 mm) Head bone is not smeared over the skull.
        y_head = max(y_neck + 0.008, float(sk.rest_pos[sk.index['Head']][1]))
        stack.append((y_head, 'core+head', 'Head'))
        for i in range(1, p['head_rings'] + 1):
            stack.append((y_head + (y_skull - y_head) * i / p['head_rings'], 'core+head', 'Head'))

        # The two rings bounding the arm face are seeded with the deltoid cross-section, so the
        # face is always big enough to inset the port into.
        grow = p['margin'] * (1.0 + 4.0 * p['port_pad'])
        girdle_pts = np.r_[core, *[deltoid(sk, ARM[s], np.concatenate(arm[s]), grow) for s in ARM]]
        legs = np.concatenate([q for s in LEG for q in leg[s]])
        sets = {'floor': np.r_[core, legs], 'core': core, 'girdle': girdle_pts,
                'core+head': np.r_[core, head]}
        ys = [s[0] for s in stack]
        centers = [mid_point(mid, y) for y, _, _ in stack]
        # Rings sit perpendicular to the local chain direction, not to world Y: a ring skewed
        # against the bone it spans would spread that bone's stretch across its own depth.
        axes = [station_axis(mid, mid.project(c)) for c in centers]
        rings = []
        u = X
        for k, (y, key, rig_spec) in enumerate(stack):
            u, v = frame_pair(axes[k], u)
            back = (y - ys[k - 1]) * 0.5 if k > 0 else 0.02
            front = (ys[k + 1] - y) * 0.5 if k + 1 < len(ys) else 0.02
            c = centers[k]
            m = p['host_margin'] if k == 0 else p['margin']
            box = rect(slab(sets[key], c, axes[k], back, front), c, u, v, m, 0.02)
            if k == 0:
                # Pelvis width is governed by the hips, so the floor corners ride the leg chains
                # -- otherwise they shear against the leg ports inset into this same ring.
                rings.append(floor_ring(b, sk, c, u, v, box))
            else:
                rings.append(b.ring(c, u, v, box, *rig_of(mid, sk, c, rig_spec)))

        for k in range(len(rings) - 1):
            # The left (+x) and right (-x) sides of the shoulder segment become the arm ports.
            b.tube(rings[k], rings[k + 1], skip=(1, 3) if k == arm_face else ())
        b.cap(rings[-1][::-1])

        # --- legs: two ports sharing the midline edge of the pelvis floor ---------------------
        floor = rings[0]
        u0, v0 = frame_pair(axes[0], X)
        thigh = {s: rect(slab(np.concatenate(leg[s]), centers[0], axes[0], 0.0, 0.05),
                         centers[0], u0, v0, p['margin'], 0.02) for s in ('L', 'R')}
        inner, ports = floor_ports(b, sk, centers[0], u0, v0, thigh, p)
        b.annulus(floor, inner, [0, 2, 3, 5])

        for side in ('L', 'R'):
            # The foot is an L, not a bend: sole and heel run forward while the calf drops in from
            # above. Modelling it as a block the calf enters through a port covers the heel, which
            # any single cross-section at the ankle cuts off.
            ankle_port = emit_foot(b, sk, side, leg[side][3:], p)
            ch = chain(sk, LEG[side][:-1])
            emit_limb(b, sk, ch, ports[side], leg[side][:-1], p, X, end=ankle_port)

        # --- arms: one port per torso side face -----------------------------------------------
        lo, hi = rings[arm_face], rings[arm_face + 1]
        for side, k0 in (('L', 3), ('R', 1)):
            ch = chain(sk, ARM[side])
            face = [lo[k0], lo[(k0 + 1) % 4], hi[(k0 + 1) % 4], hi[k0]]
            port = side_port(b, sk, ch, face, np.concatenate(arm[side]), p)
            b.annulus(face, port, [0, 1, 2, 3])
            emit_limb(b, sk, ch, port, arm[side], p, Z)

        pos, rig, w, tris, dirs = b.finish()
        tris = geom.orient(pos, tris)
        if p['repair_iters']:
            pos = repair(pos, dirs, tris, rig, w, sk, train_scales(sk, p['repair_poses']),
                         p['repair_iters'], p['repair_pad'], p['repair_stride'], p['repair_smooth'],
                         p['repair_step'], geom)
        base = np.einsum('vk,vki->vi', w, sk.rest_pos[rig])
        return {'rig': rig, 'w': w, 'offset': pos - base, 'tris': tris}

    def build(self, const, sk, scale):
        jp = sk.joint_pos(scale)
        verts = np.einsum('vk,vki->vi', const['w'], jp[const['rig']]) + const['offset']
        return cage_out(verts, const['tris'], const['rig'], const['w'])


def train_scales(sk, n):
    from ..rest import case_grouped, case_rest, case_uniform
    rng = np.random.default_rng(9091)
    return ([case_rest(sk), case_uniform(sk, 0.55), case_uniform(sk, 1.45)]
            + [case_grouped(sk, rng, 0.5, 1.5) for _ in range(n)])


def mid_point(ch, y):
    # The midline is monotone in y, so a height picks one point on it; extrapolate past the ends.
    ys = ch.pos[:, 1]
    k = int(np.clip(np.searchsorted(ys, y) - 1, 0, len(ys) - 2))
    f = (y - ys[k]) / (ys[k + 1] - ys[k])
    return ch.pos[k] + (ch.pos[k + 1] - ch.pos[k]) * f


def leg_rig(sk, side, pos):
    ch = chain(sk, LEG[side])
    return ch.at(ch.project(pos))


def floor_ring(b, sk, center, u, v, box):
    u_lo, u_hi, v_lo, v_hi = box
    out = []
    for a, c in ((u_hi, v_hi), (u_lo, v_hi), (u_lo, v_lo), (u_hi, v_lo)):
        pos = center + u * a + v * c
        out.append(b.vert(pos, *leg_rig(sk, 'L' if a > 0 else 'R', pos)))
    return out


def rig_of(ch, sk, center, spec):
    if spec == 'chain':
        return ch.at(ch.project(center))
    j = sk.index[spec]
    return (j, j), (1.0, 0.0)


def station_axis(ch, s):
    # Segment direction, or the bisector when the station sits on an interior joint.
    seg = np.diff(ch.pos, axis=0)
    seg = seg / np.linalg.norm(seg, axis=1, keepdims=True)
    k = int(np.clip(np.searchsorted(ch.arc, s) - 1, 0, len(seg) - 1))
    at_joint = abs(s - ch.arc[k]) < 1e-6
    if at_joint and k > 0:
        a = seg[k - 1] + seg[k]
        return a / np.linalg.norm(a)
    return seg[k]


def corner_pts(c, u, v, box):
    return np.array([c + u * box[i] + v * box[k] for i in (0, 1) for k in (2, 3)])


def deltoid(sk, names, pts, grow):
    # The limb's cross-section where it leaves the torso, as four corner points. Seeds the host
    # rings so the port -- which is inset from the face -- still covers the limb.
    j0 = sk.index[names[0]]
    axis = sk.rest_pos[sk.index[names[1]]] - sk.rest_pos[j0]
    axis = axis / np.linalg.norm(axis)
    u, v = frame_pair(axis, Z)
    c = sk.rest_pos[j0]
    sl = slab(pts, c, axis, 0.0, 0.09)
    return corner_pts(c, u, v, rect(sl if len(sl) else pts, c, u, v, grow, 0.02))


def limb_stations(ch, p, reach):
    # (arc, axis, side) per ring. side is 'in'/'out' at a mitered bend, where two rings share one
    # arc: one square to the incoming direction, one to the outgoing. Without it the loft across
    # a sharp joint cuts the corner off -- the heel, in practice.
    seg = np.diff(ch.pos, axis=0)
    seg = seg / np.linalg.norm(seg, axis=1, keepdims=True)
    out = []
    n = len(ch.arc) - 1
    for k in range(n):
        if k == 0:
            out.append((ch.arc[k], seg[k], 'out', k))
        elif float(seg[k - 1] @ seg[k]) < np.cos(np.radians(p['miter_deg'])):
            out.append((ch.arc[k], seg[k - 1], 'in', k))
            out.append((ch.arc[k], seg[k], 'out', k))
        else:
            a = seg[k - 1] + seg[k]
            out.append((ch.arc[k], a / np.linalg.norm(a), 'mid', k - 1))
        for i in range(1, p['limb_subdiv'] + 1):
            out.append((ch.arc[k] + (ch.arc[k + 1] - ch.arc[k]) * i / (p['limb_subdiv'] + 1), seg[k], 'mid', k))
    out.append((ch.arc[-1], seg[-1], 'mid', n - 1))
    for i in range(1, p['tip_subdiv'] + 2):
        if reach > 0:
            out.append((ch.arc[-1] + reach * i / (p['tip_subdiv'] + 1), seg[-1], 'mid', n - 1))
    return out


def emit_foot(b, sk, side, groups, p):
    # A short tube along the sole (heel -> toe base -> toe tip) whose top face carries the port
    # the calf drops into. Returns that port.
    name = 'Left' if side == 'L' else 'Right'
    ankle = sk.rest_pos[sk.index[name + 'Foot']]
    ch = chain(sk, [name + 'Foot', name + 'ToeBase'])
    d = (ch.pos[1] - ch.pos[0]) / ch.arc[1]
    u, v = frame_pair(d, X)
    pts = np.concatenate(groups)
    calf = slab(sk.verts[sk.dominant == sk.index[name + 'Leg']], ankle, Y, 0.0, 0.05)
    ankle_box = rect(calf, ankle, X, d, p['margin'] * (1.0 + 4.0 * p['port_pad']), 0.02)
    seed = corner_pts(ankle, X, d, ankle_box)
    q = (pts - ankle) @ d
    q_calf = (seed - ankle) @ d

    # The block must reach behind and in front of the ankle cross-section, not just the sole, or
    # the calf port gets clamped inside it and the ankle escapes at the back.
    heel = min(float(q.min()), float(q_calf.min())) - 0.008
    tip = float(q.max()) + 0.006
    instep = min(ch.arc[1] * 0.85, float(q_calf.max()) + 0.020)   # front edge of the calf port
    stations = [heel, instep, ch.arc[1]]
    stations += [ch.arc[1] + (tip - ch.arc[1]) * i / (p['tip_subdiv'] + 1) for i in range(1, p['tip_subdiv'] + 2)]

    rings = []
    for k, s in enumerate(stations):
        back = (s - stations[k - 1]) * 0.5 if k else 0.01
        front = (stations[k + 1] - s) * 0.5 if k + 1 < len(stations) else 0.01
        c = ankle + d * s
        src = np.r_[pts, seed] if k < 2 else pts
        sl = slab(src, c, d, back, front)
        box = rect(sl if len(sl) else src, c, u, v, p['margin'], 0.012)
        rings.append(b.ring(c, u, v, box, *ch.at(max(0.0, s))))

    b.cap(rings[0])
    for k in range(len(rings) - 1):
        b.tube(rings[k], rings[k + 1], skip=(0,) if k == 0 else ())   # v_hi side is the top
    b.cap(rings[-1][::-1])

    face = [rings[0][0], rings[0][1], rings[1][1], rings[1][0]]
    q_face = np.array([b.pos[i] for i in face])
    box = rect(calf, ankle, X, d, p['margin'], 0.02)
    port = []
    for su, sv in ((1, 1), (1, -1), (-1, -1), (-1, 1)):
        target = ankle + X * box[1 if su > 0 else 0] + d * box[3 if sv > 0 else 2]
        a, bt = face_coords(q_face, target, p['port_pad'])
        pos = bilerp(q_face, a, bt)
        port.append(b.vert(pos, *ch.at(max(0.0, float((pos - ankle) @ d)))))
    port = order_like(b, port, q_face)
    b.annulus(face, port, [0, 1, 2, 3])
    return port


def emit_limb(b, sk, ch, port, groups, p, seed_u, end=None):
    # port is the opening the limb grows from; its arc position sets where the tube starts.
    # groups[k] is the flesh owned by chain joint k, so each ring is sized only from the flesh of
    # the segments it actually spans.
    port_pos = np.array([b.pos[i] for i in port])
    s0 = ch.project(port_pos.mean(axis=0))
    tail = np.concatenate(groups[-2:])
    reach = float(((tail - ch.pos[-1]) @ ch_dir(ch, -1)).max()) + 0.006 if len(tail) and end is None else 0.0

    if end is None:
        stations = [e for e in limb_stations(ch, p, reach) if e[0] > s0 + 1e-4]
    else:
        # Stop short of the closing port and add one ring just above it, so the last loft is short.
        s_end = ch.project(np.array([b.pos[i] for i in end]).mean(axis=0))
        stations = [e for e in limb_stations(ch, p, 0.0) if s0 + 1e-4 < e[0] < s_end - 0.04]
        stations.append((s_end - 0.03, ch_dir(ch, -1), 'mid', len(ch.arc) - 2))
    # Corner order is inherited from the port so the first loft cannot twist.
    u, v = frame_pair(stations[0][1], seed_u)
    signs = corner_signs(port_pos, port_pos.mean(axis=0), u, v)

    prev = port
    for k, (s, a, side, j) in enumerate(stations):
        u, v = frame_pair(a, u)
        c = chain_point(ch, s)
        prev_s = stations[k - 1][0] if k else s0
        next_s = stations[k + 1][0] if k + 1 < len(stations) else s + 0.04
        if side == 'in':
            # Square to the incoming direction: only the flesh arriving at this joint.
            pts, back, front = groups[j - 1], (s - prev_s) * 0.5, 0.0
        elif side == 'out':
            # Square to the outgoing direction: the downstream flesh, including whatever hangs
            # behind this plane (the heel behind the ankle).
            pts, back, front = np.concatenate(groups[j:]), 0.20, (next_s - s) * 0.5
        else:
            pts = np.concatenate(groups[j:j + 2])
            back, front = (s - prev_s) * 0.5, (next_s - s) * 0.5
        sl = slab(pts, c, a, back, front)
        if len(sl) == 0:
            sl = slab(pts, c, a, back * 3 + 0.02, front * 3 + 0.02)
        box = rect(sl, c, u, v, p['margin'], 0.012)
        ring = [b.vert(c + u * box[1 if su > 0 else 0] + v * box[3 if sv > 0 else 2], *ch.at(s))
                for su, sv in signs]
        b.tube(prev, ring)
        prev = ring
    if end is None:
        b.cap(prev)
    else:
        b.tube(prev, order_like(b, end, np.array([b.pos[i] for i in prev])))


def ch_dir(ch, k):
    d = ch.pos[k] - ch.pos[k - 1]
    return d / np.linalg.norm(d)


def chain_point(ch, s):
    k = int(np.clip(np.searchsorted(ch.arc, s) - 1, 0, len(ch.arc) - 2))
    span = ch.arc[k + 1] - ch.arc[k]
    f = (s - ch.arc[k]) / span if span > 1e-9 else 0.0
    return ch.pos[k] + (ch.pos[k + 1] - ch.pos[k]) * f


def frame_pair(axis, seed):
    a = axis / np.linalg.norm(axis)
    u = seed - a * (seed @ a)
    n = np.linalg.norm(u)
    if n < 1e-6:
        alt = X if abs(a[0]) < 0.9 else Z
        u = alt - a * (alt @ a)
        n = np.linalg.norm(u)
    u = u / n
    return u, np.cross(a, u)


def corner_signs(loop, center, u, v):
    return [(np.sign((q - center) @ u) or 1.0, np.sign((q - center) @ v) or 1.0) for q in loop]


def bilerp(face, alpha, beta):
    a, bb, c, d = face
    return (1 - alpha) * ((1 - beta) * a + beta * bb) + alpha * ((1 - beta) * d + beta * c)


def face_coords(face, target, pad):
    # Bilinear (alpha, beta) of a point on a possibly non-planar quad face, least-squares.
    a, bb, c, d = face
    e_alpha = ((d - a) + (c - bb)) * 0.5
    e_beta = ((bb - a) + (c - d)) * 0.5
    ab, _, _, _ = np.linalg.lstsq(np.c_[e_alpha, e_beta], target - a, rcond=None)
    return tuple(np.clip(ab, pad, 1.0 - pad))


def side_port(b, sk, ch, face, pts, p):
    # Inset a quad into a torso side face, sized to the limb's own cross-section where it leaves
    # -- a fixed inset would either clip the deltoid or leave no annulus to stitch.
    q = np.array([b.pos[i] for i in face])
    mid = q.mean(axis=0)
    axis = station_axis(ch, ch.arc[0])
    u, v = frame_pair(axis, Z)
    sl = slab(pts, mid, axis, 0.0, 0.06)
    box = rect(sl if len(sl) else pts, mid, u, v, p['margin'], 0.02)

    rig, w = ch.at(ch.project(mid))
    out = []
    for su, sv in ((1, 1), (1, -1), (-1, -1), (-1, 1)):
        target = mid + u * box[1 if su > 0 else 0] + v * box[3 if sv > 0 else 2]
        a, bt = face_coords(q, target, p['port_pad'])
        out.append(b.vert(bilerp(q, a, bt), rig, w))
    return order_like(b, out, q)


def order_like(b, loop, face):
    # Pair each face corner with its nearest port corner, so the annulus quads cannot cross.
    pos = np.array([b.pos[i] for i in loop])
    idx = [int(np.argmin(np.linalg.norm(pos - f, axis=1))) for f in face]
    assert len(set(idx)) == len(loop), 'port does not match its host face'
    return [loop[i] for i in idx]


def floor_ports(b, sk, center, u, v, thigh, p):
    # The pelvis floor quad is consumed by two leg ports that share its midline edge, so the
    # crotch notch appears where the two leg tubes diverge instead of needing its own geometry.
    hips = sk.index['Hips']
    drop = np.array([0.0, -abs(thigh['L'][3] - thigh['L'][2]) * p['crotch_drop'], 0.0])

    def leg_v(su, sv, side):
        box = thigh[side]
        pos = center + u * box[1 if su > 0 else 0] + v * box[3 if sv > 0 else 2]
        return b.vert(pos, *leg_rig(sk, side, pos))

    def mid_v(sv):
        box = thigh['L']
        return b.vert(center + v * box[3 if sv > 0 else 2] + drop, hips)

    # Order matches the floor loop: (+u,+v) -> (-u,+v) -> (-u,-v) -> (+u,-v).
    inner = [leg_v(1, 1, 'L'), mid_v(1), leg_v(-1, 1, 'R'),
             leg_v(-1, -1, 'R'), mid_v(-1), leg_v(1, -1, 'L')]
    ports = {'L': [inner[0], inner[1], inner[4], inner[5]],
             'R': [inner[1], inner[2], inner[3], inner[4]]}
    return inner, ports
