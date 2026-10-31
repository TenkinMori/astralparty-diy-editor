# -*- coding: utf-8 -*-
"""只读：Map.bin / GameMode.bin 递归摊开（一层都不省），核对"模式 -> 地图 -> 画板"。"""
import os
import struct

TABLE = r"D:\小小兔趴\4-服务端\server\data\gamedata"


def rv(data, i):
    raw = 0
    shift = 0
    while i < len(data):
        b = data[i]
        i += 1
        raw |= (b & 0x7F) << shift
        if not (b & 0x80):
            break
        shift += 7
    return raw, i


def fields(data):
    i = 0
    while i < len(data):
        key, i = rv(data, i)
        num = key >> 3
        wire = key & 7
        if num == 0:
            return
        if wire == 0:
            v, i = rv(data, i)
            yield num, wire, v
        elif wire == 1:
            yield num, wire, data[i:i + 8]
            i += 8
        elif wire == 2:
            ln, i = rv(data, i)
            yield num, wire, data[i:i + ln]
            i += ln
        elif wire == 5:
            yield num, wire, data[i:i + 4]
            i += 4
        else:
            return


def s32(b):
    return struct.unpack_from("<i", b, 0)[0]


def walk(data, indent=0, depth=0, maxdepth=5, out=None):
    if out is None:
        out = []
    pad = "  " * indent
    for n, w, v in fields(data):
        if w == 5:
            out.append("%sf%-3d w5  int=%d" % (pad, n, s32(v)))
        elif w == 0:
            out.append("%sf%-3d w0  varint=%d" % (pad, n, v))
        elif w == 1:
            out.append("%sf%-3d w1  64bit %s" % (pad, n, v.hex()))
        else:
            try:
                t = v.decode("utf-8")
                if t.isprintable() and t:
                    out.append("%sf%-3d w2  text=%r" % (pad, n, t))
                    continue
            except Exception:
                pass
            sub = walk(v, indent + 1, depth + 1, maxdepth, []) if depth < maxdepth else []
            if sub:
                out.append("%sf%-3d w2  <msg len=%d>" % (pad, n, len(v)))
                out.extend(sub)
            else:
                ints = [struct.unpack_from("<i", v, k)[0] for k in range(0, len(v) - 3, 4)]
                out.append("%sf%-3d w2  len=%-4d hex=%s  int32=%s"
                           % (pad, n, len(v), v[:24].hex(), ints[:8]))
    return out


def main():
    for name, show in (("Map.bin", 2), ("GameMode.bin", 3)):
        data = open(os.path.join(TABLE, name), "rb").read()
        ents = [v for n, w, v in fields(data) if n == 2 and w == 2]
        print("=" * 74)
        print("%s  %d 字节，顶层 f2 共 %d 条" % (name, len(data), len(ents)))
        for idx, ent in enumerate(ents[:show]):
            print("-- 第 %d 条 --" % (idx + 1))
            for line in walk(ent):
                print(line)
    return 0


if __name__ == "__main__":
    main()
