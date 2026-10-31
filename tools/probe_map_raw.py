# -*- coding: utf-8 -*-
"""只读：Map.bin / GameMode.bin 的字段号与内容逐条核对（上一版解错了，这里只看原始）。"""
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


def packed(b):
    out = []
    for k in range(0, len(b) - 3, 4):
        out.append(struct.unpack_from("<i", b, k)[0])
    return out


def main():
    for name in ("Map.bin", "GameMode.bin"):
        data = open(os.path.join(TABLE, name), "rb").read()
        print("=" * 72)
        print(name, len(data), "bytes")
        ents = [v for n, w, v in fields(data) if n == 2 and w == 2]
        print("顶层 f2 条目 %d 条" % len(ents))
        for idx, ent in enumerate(ents[:3]):
            print("-- 第 %d 条 --" % (idx + 1))
            for n, w, v in fields(ent):
                if w == 5:
                    print("   f%-3d w5 %d" % (n, s32(v)))
                elif w == 0:
                    print("   f%-3d w0 %d" % (n, v))
                elif w == 1:
                    print("   f%-3d w1 %s" % (n, v.hex()))
                else:
                    try:
                        t = v.decode("utf-8")
                        if t.isprintable() and t:
                            print("   f%-3d w2 text=%r" % (n, t))
                            continue
                    except Exception:
                        pass
                    print("   f%-3d w2 len=%d %s  packed_int=%s"
                          % (n, len(v), v[:32].hex(), packed(v)[:10]))
    return 0


if __name__ == "__main__":
    main()
