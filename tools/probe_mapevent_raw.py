# -*- coding: utf-8 -*-
"""只读：把 MapEvent.bin 前几行**逐字段原样 dump**出来（排查字段号 / 线型）。"""
import os
import struct
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
            if i + 4 > len(data):
                break
            out.append((num, wire, struct.unpack_from("<i", data, i)[0], None))
            i += 4
        elif wire == 1:
            if i + 8 > len(data):
                break
            out.append((num, wire, struct.unpack_from("<q", data, i)[0], None))
            i += 8
        elif wire == 2:
            ln, i = varint(data, i)
            if i + ln > len(data):
                break
            out.append((num, wire, None, data[i:i + ln]))
            i += ln
        else:
            break
    return out


def main():
    path = os.path.join(TABLE, "MapEvent.bin")
    with open(path, "rb") as fh:
        data = fh.read()
    tops = fields(data)
    print("顶层字段数 = %d" % len(tops))
    from collections import Counter
    print("顶层 (号,线型) 计数：%s" % Counter((n, w) for n, w, _, _ in tops))
    shown = 0
    for n, w, v, blob in tops:
        if w != 2 or blob is None:
            continue
        inner = fields(blob)
        row_id = None
        for k, ww, vv, bb in inner:
            if k == 1 and ww == 5:
                row_id = vv
        if row_id not in (32121, 32122, 32123, 32124, 32101):
            continue
        shown += 1
        print("\n===== 事件 %s 的原始字段 =====" % row_id)
        for k, ww, vv, bb in inner:
            if ww == 2:
                sub = fields(bb)
                print("  field %d wire 2  len=%d  内层=%s" % (k, len(bb), [(a, b, c, (len(d) if d else None)) for a, b, c, d in sub]))
                for a, b, c, d in sub:
                    if b == 2 and d:
                        print("      └ field %d 内层2 = %s" % (a, [(x, y, z, (len(t) if t else None)) for x, y, z, t in fields(d)]))
            else:
                print("  field %d wire %d 值=%s" % (k, ww, vv))
    print("\n命中 %d 行" % shown)


if __name__ == "__main__":
    sys.stdout.reconfigure(encoding="utf-8")
    main()
