# Orthographic previews: the body as a point cloud, the cage as a wireframe, escaped
# vertices in red. Enough to see what an algorithm actually did without leaving the sandbox.

import numpy as np
from PIL import Image, ImageDraw

from . import geom

VIEWS = {'front': (0, 1, 2), 'side': (2, 1, 0), 'top': (0, 2, 1)}
BG = (16, 18, 22)
BODY = (86, 92, 104)
CAGE = (60, 220, 255)
ESCAPE = (255, 70, 70)
JOINT = (255, 210, 80)


def _project(p, view, box, size, pad):
    a, b, _ = VIEWS[view]
    lo, hi = box
    span = max(hi[a] - lo[a], hi[b] - lo[b])
    s = (size - 2 * pad) / span
    x = (p[:, a] - (lo[a] + hi[a]) * 0.5) * s + size * 0.5
    y = size - ((p[:, b] - (lo[b] + hi[b]) * 0.5) * s + size * 0.5)
    return np.c_[x, y]


def panel(body, cage, escaped, joints, view, box, size, pad):
    img = Image.new('RGB', (size, size), BG)
    d = ImageDraw.Draw(img)

    if len(body):
        px = _project(body, view, box, size, pad).astype(int)
        ok = (px[:, 0] >= 0) & (px[:, 0] < size) & (px[:, 1] >= 0) & (px[:, 1] < size)
        buf = np.array(img)
        buf[px[ok][:, 1], px[ok][:, 0]] = BODY
        img = Image.fromarray(buf)
        d = ImageDraw.Draw(img)

    if cage is not None:
        cv = _project(cage.verts, view, box, size, pad)
        edges = np.unique(np.sort(np.r_[cage.tris[:, [0, 1]], cage.tris[:, [1, 2]], cage.tris[:, [2, 0]]], axis=1), axis=0)
        for a, b in edges:
            d.line([tuple(cv[a]), tuple(cv[b])], fill=CAGE, width=1)

    if escaped is not None and len(escaped):
        ep = _project(escaped, view, box, size, pad)
        for x, y in ep:
            d.ellipse([x - 1.6, y - 1.6, x + 1.6, y + 1.6], fill=ESCAPE)

    if joints is not None:
        jp = _project(joints, view, box, size, pad)
        for x, y in jp:
            d.ellipse([x - 1.4, y - 1.4, x + 1.4, y + 1.4], fill=JOINT)

    d.text((6, 6), view, fill=(200, 200, 200))
    return img


def render(path, sk, cage, scale, title='', size=420, pad=18, stride=2, show_escape=True, zoom=None):
    body = sk.skin(scale)
    joints = sk.joint_pos(scale)
    escaped = None
    if show_escape and cage is not None:
        pts = body[::3]
        escaped = pts[~geom.inside(pts, cage.verts, cage.tris)]

    if zoom is not None:
        lo, hi = np.array(zoom[0], float), np.array(zoom[1], float)
        keep = lambda q: q[np.all((q >= lo - 0.02) & (q <= hi + 0.02), axis=1)] if len(q) else q
        body, joints = keep(body), keep(joints)
        escaped = keep(escaped) if escaped is not None else None
    else:
        lo = np.minimum(body.min(axis=0), cage.verts.min(axis=0) if cage else body.min(axis=0))
        hi = np.maximum(body.max(axis=0), cage.verts.max(axis=0) if cage else body.max(axis=0))
    box = (lo, hi)

    panels = [panel(body[::stride], cage, escaped, joints, v, box, size, pad) for v in ('front', 'side', 'top')]
    sheet = Image.new('RGB', (size * 3, size + 20), BG)
    for i, p in enumerate(panels):
        sheet.paste(p, (i * size, 20))
    ImageDraw.Draw(sheet).text((8, 4), title, fill=(230, 230, 230))
    sheet.save(path)
    return path
