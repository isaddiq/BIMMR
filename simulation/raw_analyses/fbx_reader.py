"""Minimal binary FBX reader: returns Model nodes (id, name, type) and parent links.

Only what the binding analysis needs. Supports FBX 7.x binary (32- and 64-bit offsets).
"""
import struct, zlib

def _read_props(buf, pos, n):
    props = []
    for _ in range(n):
        t = chr(buf[pos]); pos += 1
        if t == 'Y': v = struct.unpack_from('<h', buf, pos)[0]; pos += 2
        elif t == 'C': v = buf[pos] != 0; pos += 1
        elif t == 'I': v = struct.unpack_from('<i', buf, pos)[0]; pos += 4
        elif t == 'F': v = struct.unpack_from('<f', buf, pos)[0]; pos += 4
        elif t == 'D': v = struct.unpack_from('<d', buf, pos)[0]; pos += 8
        elif t == 'L': v = struct.unpack_from('<q', buf, pos)[0]; pos += 8
        elif t in 'SR':
            ln = struct.unpack_from('<I', buf, pos)[0]; pos += 4
            v = bytes(buf[pos:pos + ln]); pos += ln
        elif t in 'fdlib':
            ln, enc, clen = struct.unpack_from('<III', buf, pos); pos += 12
            v = None; pos += clen          # arrays are skipped
        else:
            raise ValueError(f"unknown FBX property type {t!r} at {pos}")
        props.append(v)
    return props, pos

def _walk(buf, pos, end, wide, want, out, path):
    hdr = 25 if wide else 13
    fmt = '<QQQB' if wide else '<IIIB'
    while pos < end:
        end_off, nprops, plen, nlen = struct.unpack_from(fmt, buf, pos)
        if end_off == 0:
            return pos + hdr
        name = bytes(buf[pos + hdr: pos + hdr + nlen]).decode('ascii', 'replace')
        ppos = pos + hdr + nlen
        here = path + (name,)
        if here in want:
            props, _ = _read_props(buf, ppos, nprops)
            out.append((here, props))
        child = ppos + plen
        if child < end_off and here in {w[:len(here)] for w in want}:
            _walk(buf, child, end_off, wide, want, out, here)
        pos = end_off
    return pos

def read_models(path):
    buf = memoryview(open(path, 'rb').read())
    assert bytes(buf[:20]) == b'Kaydara FBX Binary  ', 'not a binary FBX'
    version = struct.unpack_from('<I', buf, 23)[0]
    wide = version >= 7500
    out = []
    _walk(buf, 27, len(buf), wide, {('Objects', 'Model'), ('Connections', 'C')}, out, ())
    models, links = {}, []
    for p, props in out:
        if p == ('Objects', 'Model'):
            uid, raw, mtype = props[0], props[1], props[2]
            nm = raw.split(b'\x00\x01')[0].decode('utf-8', 'replace')
            models[uid] = (nm, mtype.decode('ascii', 'replace'))
        else:
            if props[0] == b'OO':
                links.append((props[1], props[2]))   # child -> parent
    return version, models, links
