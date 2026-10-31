# -*- coding: utf-8 -*-
"""只读：对比 MapEvent.bin 里 32121/32122 在 field 1（Infos）与 field 2（map 形态）两份里的原始字节。"""
import os
import sys

TABLE = r"D:\小小兔趴\4-服务端\server\data\gamedata"


def varint(data, i):
    v = 0
    shift = 0
    while i < len(data) and shift < 64:
        b = data[i]
        i += 1
        v |= (b & 0x7F) << shift
        if not (b & 0x80):
            return v, i
        shift += 7
    return v, i


def fields(data):
    out = []
    i = 0
    while i < len(data):
        key, i = varint(data, i)
        num = key >> 3
        wire = key & 7
        if num <= 0:
            break
        if wire == 0:
            v, i = varint(data, i)
            out.append((num, wire, v, None))
        elif wire == 5:
            import struct
            out.append((num, wire, struct.unpack_from("<i", data, i)[0], None))
            i += 4
        elif wire == 1:
            import struct
            out.append((num, wire, struct.unpack_from("<q", data, i)[0], None))
            i += 8
        elif wire == 2:
            ln, i = varint(data, i)
            out.append((num, wire, None, data[i:i + ln]))
            i += ln
        else:
            break
    return out


with open(os.path.join(TABLE, "MapEvent.bin"), "rb") as fh:
    data = fh.read()

for num, wire, _v, blob in fields(data):
    if wire != 2 or blob is None:
        continue
    inner = fields(blob)
    rid = None
    for k, w, v, b in inner:
        if k == 1 and w == 5:
            rid = v
    if rid not in (32121, 32122):
        continue
    print("顶 field %d  id=%s  blob len=%d" % (num, rid, len(blob)))
    for k, w, v, b in inner:
        if k == 4 and w == 2:
            print("   field4 len=%d hex=%s inner=%s" % (len(b), b.hex(), fields(b)))
        if k == 5 and w == 2:
            print("   field5 len=%d hex=%s" % (len(b), b.hex()))
    print()

sys.stdout.reconfigure(encoding="utf-8")
