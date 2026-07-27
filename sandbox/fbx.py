# Minimal binary-FBX reader (Kaydara FBX Binary, version 7100..7700).
#
# Only what the cage sandbox needs: the node tree with properties decoded.
# Nothing here is Unity-aware; the interpretation lives in export_rest.py.

import struct
import zlib


class node:
    def __init__(self, name):
        self.name = name
        self.props = []
        self.children = []

    def find(self, name):
        return next((c for c in self.children if c.name == name), None)

    def find_all(self, name):
        return [c for c in self.children if c.name == name]

    def __repr__(self):
        return f'<{self.name} props={len(self.props)} children={len(self.children)}>'


_scalar = {'Y': ('<h', 2), 'C': ('<?', 1), 'I': ('<i', 4), 'F': ('<f', 4), 'D': ('<d', 8), 'L': ('<q', 8)}
_array = {'f': ('<f', 4), 'd': ('<d', 8), 'l': ('<q', 8), 'i': ('<i', 4), 'b': ('<?', 1)}


class reader:
    def __init__(self, buf):
        self.buf = buf
        self.at = 0

    def take(self, n):
        s = self.at
        self.at += n
        return self.buf[s:self.at]

    def u32(self):
        return struct.unpack('<I', self.take(4))[0]

    def u64(self):
        return struct.unpack('<Q', self.take(8))[0]

    def u8(self):
        return self.buf[self.at:self.at + 1][0] if self.take(1) else 0


def _prop(r):
    t = chr(r.take(1)[0])
    if t in _scalar:
        fmt, w = _scalar[t]
        return struct.unpack(fmt, r.take(w))[0]
    if t in _array:
        fmt, w = _array[t]
        n, enc, clen = r.u32(), r.u32(), r.u32()
        raw = r.take(clen)
        if enc == 1:
            raw = zlib.decompress(raw)
        return struct.unpack(f'<{n}{fmt[1]}', raw[:n * w])
    if t in 'SR':
        n = r.u32()
        raw = r.take(n)
        return raw if t == 'R' else raw.decode('utf-8', 'replace')
    raise ValueError(f'unknown fbx property type {t!r} at {r.at}')


def _read_node(r, wide):
    end = r.u64() if wide else r.u32()
    nprops = r.u64() if wide else r.u32()
    r.u64() if wide else r.u32()          # property list length, unused
    name_len = r.take(1)[0]
    name = r.take(name_len).decode('utf-8', 'replace')

    if end == 0:                          # null record: end of a sibling list
        return None, 0

    n = node(name)
    n.props = [_prop(r) for _ in range(nprops)]

    # Nested nodes fill whatever is left before end, terminated by a null record.
    while r.at < end:
        child, child_end = _read_node(r, wide)
        if child is None:
            break
        n.children.append(child)
    r.at = end
    return n, end


def load(path):
    with open(path, 'rb') as f:
        buf = f.read()
    assert buf[:20] == b'Kaydara FBX Binary  ', 'not a binary fbx'
    version = struct.unpack('<I', buf[23:27])[0]
    wide = version >= 7500

    r = reader(buf)
    r.at = 27
    root = node('__root__')
    while r.at < len(buf) - 16:
        n, _ = _read_node(r, wide)
        if n is None:
            break
        root.children.append(n)
    root.version = version
    return root
