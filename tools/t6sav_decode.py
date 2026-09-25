#!/usr/bin/env python3
"""Tropico 6 .t6sav decoder (reverse-engineered, see docs/reverse_engineering_report.md).

Container:    [Lama header ~212-219 B][zlib stream to EOF]
Decompressed: [u32 N][N x (i32 len, cstr)]   name table
              [u32 M][M x object record]     object table
              [blobs...]                     per-object data, blob base = end of object table
Object record: u8 kind(1|2|3) | i32 len | cstr path | tail
               tail = (u8 flag, u32 blob_offset[, i32 x]) for kind 1/2 ; nothing for kind 3
Blob: [24-byte native transform preamble for actor blueprints: f32 x,y,z, u32, f32 yaw?, u32]
      then tagged properties: FName name(u32 idx,u32 num) FName type(8) u32 size u32 array_index
      [type extras: FName x1 (Array/Struct/Enum/Set/Byte) or x2 (Map)] [u8 bool value if Bool] u8 guid_flag  value[size]
      terminated by FName 'None'. Some blobs carry trailing native bytes after 'None'.
"""
import struct, zlib, re, logging

log = logging.getLogger("t6sav")
SIGS = (b'\x78\x01', b'\x78\x5e', b'\x78\x9c', b'\x78\xda')
EXTRA = {'ArrayProperty': 1, 'StructProperty': 1, 'EnumProperty': 1, 'SetProperty': 1, 'MapProperty': 2, 'ByteProperty': 1}


def find_streams(b):
    """Try zlib at every candidate signature offset; return [(offset, comp_len, data)] largest first."""
    out = []
    for sig in SIGS:
        i = b.find(sig)
        while i != -1:
            if not any(o <= i < o + c for o, c, _ in out):
                try:
                    dobj = zlib.decompressobj()
                    data = dobj.decompress(b[i:])
                    if len(data) > 1000 and dobj.eof:
                        out.append((i, len(b) - i - len(dobj.unused_data), data))
                        log.info("zlib stream @0x%x -> %d bytes", i, len(data))
                except zlib.error as e:
                    log.debug("zlib fail @0x%x: %s", i, e)
            i = b.find(sig, i + 1)
    return sorted(out, key=lambda x: -len(x[2]))


class Save:
    def __init__(self, path):
        self.path = path
        self.raw = open(path, 'rb').read()
        st = find_streams(self.raw)
        if not st:
            raise SystemExit("no zlib stream found")
        self.zoff, self.zlen, self.d = st[0]
        self.header = self.raw[:self.zoff]
        self.parse_tables()

    def parse_tables(self):
        d = self.d
        n = struct.unpack_from("<I", d, 0)[0]
        p = 4
        self.names = []
        for _ in range(n):
            l = struct.unpack_from("<i", d, p)[0]
            self.names.append(d[p + 4:p + 3 + l].decode('latin1'))
            p += 4 + l
        self.name_table_end = p
        m = struct.unpack_from("<I", d, p)[0]
        pos = p + 4
        pr = re.compile(rb'[\x20-\x7e]+\Z')

        def rec_at(q):
            if q + 6 > len(d) or d[q] not in (1, 2, 3):
                return None
            l = struct.unpack_from("<i", d, q + 1)[0]
            if 3 < l < 400 and d[q + 4 + l] == 0 and pr.match(d[q + 5:q + 4 + l]):
                return l

        self.recs = []
        for i in range(m):
            l = rec_at(pos)
            e = pos + 5 + l
            kind = d[pos]
            if i == m - 1:  # last record: no successor to sync on
                g = 9 if d[e] in (1, 4) else 5
            else:
                g = next((g for g in (0, 5, 9) if rec_at(e + g)), None)
                if g is None:
                    raise SystemExit("object table desync at record %d (0x%x)" % (i, e))
            t = d[e:e + g]
            self.recs.append(dict(idx=i, at=pos, kind=kind, path=d[pos + 5:e - 1].decode(),
                                  flag=t[0] if g else None,
                                  off=struct.unpack_from("<I", t, 1)[0] if g >= 5 else None,
                                  x=struct.unpack_from("<i", t, 5)[0] if g == 9 else None))
            pos = e + g
        self.base = pos
        log.info("names=%d objects=%d blob_base=0x%x", n, m, pos)
        offs = sorted(set(r['off'] for r in self.recs if r['off'] is not None))
        nxt = {o: (offs[k + 1] if k + 1 < len(offs) else len(d) - self.base) for k, o in enumerate(offs)}
        for r in self.recs:
            if r['off'] is not None:
                r['start'] = self.base + r['off']
                r['end'] = self.base + nxt[r['off']]

    # ---- tagged property walker ---------------------------------------------
    def nm(self, p):
        i, n = struct.unpack_from("<II", self.d, p)
        return (self.names[i] if i < len(self.names) else None), n

    KNOWN_TYPES = None

    def tag_ok(self, p, end):
        """plausible property tag at p (used only for resync after unparsed native bytes)"""
        d = self.d
        if p + 25 > end: return False
        i, n, ti, tn, sz, ai = struct.unpack_from("<IIIIII", d, p)
        if not (0 < i < len(self.names) and n < 64 and ti < len(self.names) and tn == 0 and ai < 64): return False
        if self.names[ti] not in ('IntProperty', 'FloatProperty', 'ObjectProperty', 'BoolProperty', 'EnumProperty', 'ArrayProperty',
                                  'StructProperty', 'ByteProperty', 'DoubleProperty', 'NameProperty', 'StrProperty', 'MapProperty',
                                  'SetProperty', 'UInt32Property', 'Int64Property', 'TextProperty'): return False
        return sz < end - p

    def walk(self, p, end, resync=False, stop_at_none=False):
        d = self.d
        props = []
        gaps = []
        while p + 8 <= end:
            i, n = struct.unpack_from("<II", d, p)
            bad = i >= len(self.names) or (i and not self.tag_ok(p, end))
            if bad and resync:
                q = p + 1
                while q < end - 24 and not (self.tag_ok(q, end) and self._chain(q, end)): q += 1
                if q >= end - 24:
                    self.last_gaps = gaps
                    return props, p, "badname"
                gaps.append((p, q)); p = q
                i, n = struct.unpack_from("<II", d, p)
            elif i >= len(self.names):
                return props, p, "badname"
            name = self.names[i]
            if name == "None":  # section terminator; more sections (super-class / nested data) may follow
                p += 8
                if stop_at_none:
                    return props, p, "ok"
                continue
            if p + 24 > end:
                return props, p, "trunc"
            ti = struct.unpack_from("<I", d, p + 8)[0]
            if ti >= len(self.names):
                return props, p, "badtype"
            ty = self.names[ti]
            sz, ai = struct.unpack_from("<II", d, p + 16)
            q = p + 24
            ex = []
            for _ in range(EXTRA.get(ty, 0)):
                ex.append(self.names[struct.unpack_from("<I", d, q)[0]])
                q += 8
            bv = None
            if ty == 'BoolProperty':
                bv = d[q]
                q += 1
            q += 1
            if q + sz > end:
                return props, p, "overrun"
            props.append(dict(at=p, name=name, type=ty, extra=ex, vat=q, size=sz, aidx=ai, bool=bv))
            p = q + sz
        self.last_gaps = gaps
        return props, p, ("ok" if p == end else "noterm")

    def _chain(self, q, end):
        """the tag at q must be followed by another plausible tag or None (2-step check)"""
        d = self.d
        sz = struct.unpack_from("<I", d, q + 16)[0]
        ex = EXTRA.get(self.names[struct.unpack_from("<I", d, q + 8)[0]], 0)
        r = q + 24 + 8 * ex + 1 + (1 if self.names[struct.unpack_from("<I", d, q + 8)[0]] == 'BoolProperty' else 0) + sz
        if r + 8 > end: return r + 8 == end + 0
        return struct.unpack_from("<I", d, r)[0] == 0 or self.tag_ok(r, end)

    def value(self, pr, depth=0):
        d = self.d
        t = pr['type']
        a = pr['vat']
        s = pr['size']
        ex = pr['extra']
        try:
            if t == 'BoolProperty': return bool(pr['bool'])
            if t == 'IntProperty': return struct.unpack_from("<i", d, a)[0]
            if t == 'UInt32Property': return struct.unpack_from("<I", d, a)[0]
            if t == 'Int64Property': return struct.unpack_from("<q", d, a)[0]
            if t == 'FloatProperty': return round(struct.unpack_from("<f", d, a)[0], 6)
            if t == 'DoubleProperty': return struct.unpack_from("<d", d, a)[0]
            if t == 'ObjectProperty': return {"obj": struct.unpack_from("<i", d, a)[0]}
            if t in ('EnumProperty', 'NameProperty'): return self.nm(a)[0]
            if t == 'ByteProperty': return d[a] if s == 1 else self.nm(a)[0]
            if t == 'StrProperty':
                l = struct.unpack_from("<i", d, a)[0]
                return d[a + 4:a + 3 + l].decode('latin1')
            if t == 'ArrayProperty': return self.array(pr, depth)
            if t == 'StructProperty':
                props, p, st = self.walk(a, a + s)
                if props and st in ("ok", "trunc", "noterm") and a + s - p < 64:
                    o = {x['name'] + ("[%d]" % x['aidx'] if x['aidx'] else ""): self.value(x, depth + 1) for x in props}
                    if p != a + s and d[p:a + s].strip(bytes(1)): o["_trailing_hex"] = d[p:a + s].hex()
                    return o
                return {"_struct": ex[0], "_raw": d[a:a + s].hex()}
        except Exception as e:
            return {"_err": str(e)}
        return {"_raw": d[a:a + min(s, 64)].hex(), "_type": t}

    def array(self, pr, depth):
        d = self.d
        a = pr['vat']
        s = pr['size']
        inner = pr['extra'][0]
        cnt = struct.unpack_from("<I", d, a)[0]
        fmt = {'ObjectProperty': ('i', 4, lambda v: {"obj": v}), 'IntProperty': ('i', 4, None),
               'FloatProperty': ('f', 4, None), 'UInt32Property': ('I', 4, None),
               'BoolProperty': ('B', 1, bool), 'DoubleProperty': ('d', 8, None)}.get(inner)
        if fmt and 4 + cnt * fmt[1] == s:
            f, w, w2 = fmt
            return [w2(v) if w2 else v for v in struct.unpack_from("<%d%s" % (cnt, f), d, a + 4)]
        if inner in ('EnumProperty', 'NameProperty', 'ByteProperty') and 4 + cnt * 8 == s:
            return [self.nm(a + 4 + 8 * k)[0] for k in range(cnt)]
        if inner == 'StructProperty':   # UE array-of-struct: count, one header tag, then `count` None-terminated property lists
            try:
                q = a + 4 + 8 + 8 + 4 + 4 + 8 + 16 + 1
                out = []
                for _ in range(cnt):
                    props, q, st = self.walk(q, a + s, stop_at_none=True)
                    if st != "ok": raise ValueError(st)
                    out.append({x['name'] + ("[%d]" % x['aidx'] if x['aidx'] else ""): self.value(x, depth + 1) for x in props})
                if q == a + s: return out
            except Exception as e:
                return {"_array": inner, "count": cnt, "_err": str(e), "_raw_head": d[a + 4:a + 4 + 48].hex()}
        return {"_array": inner, "count": cnt, "_raw_head": d[a + 4:a + 4 + min(s, 48)].hex()}

    ACTOR_PREAMBLE = 24

    def decode_object(self, r):
        """returns (props, status, transform|None)"""
        if r.get('start') is None:
            return [], "noblob", None
        actor = r['path'].startswith('/Game/') and r['kind'] in (1, 2) and r['flag'] in (2, 4, 8)
        order = (self.ACTOR_PREAMBLE, 0) if actor else (0, self.ACTOR_PREAMBLE)
        for rs in (False, True):   # strict pass first; resync (heuristic, last resort) only if strict fails
            for skip in order:
                props, p, st = self.walk(r['start'] + skip, r['end'], resync=rs)
                if st == "ok" and props is not None:
                    tr = None
                    if skip:
                        x, y, z, _, yaw = struct.unpack_from("<fffIf", self.d, r['start'])
                        tr = dict(x=x, y=y, z=z, yaw=yaw)
                    r['gaps'] = list(self.last_gaps)
                    return props, "ok" + ("+gaps%d" % len(self.last_gaps) if self.last_gaps else ""), tr
        best = None
        for skip in order:   # keep partial result: props parsed before the first undecodable byte
            props, p, st = self.walk(r['start'] + skip, r['end'], resync=False)
            if props and (best is None or len(props) > len(best[0])):
                tr = None
                if skip:
                    x, y, z, _, yaw = struct.unpack_from("<fffIf", self.d, r['start'])
                    tr = dict(x=x, y=y, z=z, yaw=yaw)
                best = (props, "partial:%s@+0x%x/%d" % (st, p - r['start'], r['end'] - r['start']), tr)
        return best if best else ([], "fail:" + st, None)
