# -*- coding: utf-8 -*-
"""只读：把 GameMode.bin 的嵌套结构**逐层**打出来，别再靠猜。

用法：python dump_gamemode.py
"""
import os
import struct
import sys


def read_varint(data, i):
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
        try:
            key, i = read_varint(data, i)
        except Exception:
            return
        num = key >> 3
        wire = key & 7
        if num == 0:
            return
        if wire == 0:
            v, i = read_varint(data, i)
            yield num, wire, v
        elif wire == 1:
            yield num, wire, data[i:i + 8]
            i += 8
        elif wire == 2:
            ln, i = read_varint(data, i)
            yield num, wire, data[i:i + ln]
            i += ln
        elif wire == 5:
            yield num, wire, data[i:i + 4]
            i += 4
        else:
            return


def is_text(blob):
    if not blob:
        return False
    try:
        s = blob.decode("utf-8")
    except Exception:
        return False
    return all(32 <= ord(c) < 127 for c in s)


def dump(data, indent=0, depth=0, maxdepth=4, label=""):
    pad = "  " * indent
    for num, wire, val in fields(data):
        if wire == 5:
            print("%sf%-3d w5  fixed32=%d" % (pad, num, struct.unpack_from("<i", val, 0)[0]))
        elif wire == 0:
            print("%sf%-3d w0  varint=%d" % (pad, num, val))
        elif wire == 1:
            print("%sf%-3d w1  64bit" % (pad, num))
        else:
            hexs = val[:20].hex()
            if is_text(val):
                print("%sf%-3d w2  text=%r" % (pad, num, val.decode("utf-8")))
                continue
            n4 = len(val) % 4 == 0
            fixed = [struct.unpack_from("<i", val, k)[0] for k in range(0, len(val) - len(val) % 4, 4)] if n4 else None
            extra = ""
            if fixed is not None and fixed and all(0 < v < 10000000 for v in fixed):
                extra = "  fixed32列表=%r" % fixed[:8]
            print("%sf%-3d w2  len=%-4d bytes=%s%s" % (pad, num, len(val), hexs, extra))
            if depth < maxdepth:
                dump(val, indent + 1, depth + 1, maxdepth)


def main():
    table = r"D:\小小兔趴\4-服务端\server\data\gamedata"
    path = os.path.join(table, "GameMode.bin")
    data = open(path, "rb").read()
    print("GameMode.bin  %d 字节" % len(data))
    # 只看头两条顶层 field-2
    shown = 0
    for num, wire, val in fields(data):
        if not (num == 2 and wire == 2):
            continue
        shown += 1
        print("")
        print("################ 顶层 field 2 第 %d 条 ################" % shown)
        dump(val, 0, 0, 4)
        if shown >= 2:
            break
    return 0


if __name__ == "__main__":
    sys.exit(main())
