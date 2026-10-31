# -*- coding: utf-8 -*-
"""只读：把 Map.bin / GameMode.bin 的头几条 entry 逐层摊开（核对模式->地图->画板）。"""
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


def text(blob):
    try:
        return blob.decode("utf-8")
    except Exception:
        return None


def dump(data, indent=0, depth=0, maxdepth=3):
    pad = "  " * indent
    for num, wire, val in fields(data):
        if wire == 5:
            print("%sf%-3d w5  fixed32=%d" % (pad, num, struct.unpack_from("<i", val, 0)[0]))
        elif wire == 0:
            print("%sf%-3d w0  varint=%d" % (pad, num, val))
        elif wire == 1:
            print("%sf%-3d w1  64bit" % (pad, num))
        else:
            t = text(val)
            if t is not None and t.isprintable() and t:
                print("%sf%-3d w2  text=%r" % (pad, num, t))
                continue
            print("%sf%-3d w2  len=%-4d %s" % (pad, num, len(val), val[:24].hex()))
            if depth < maxdepth:
                dump(val, indent + 1, depth + 1, maxdepth)


def top_entries(data, field_no):
    out = []
    for num, wire, val in fields(data):
        if num == field_no and wire == 2:
            out.append(val)
    return out


for name, field_no, take in (("Map.bin", 2, 2), ("GameMode.bin", 2, 2)):
    path = os.path.join(TABLE, name)
    data = open(path, "rb").read()
    print("=" * 70)
    print("%s  %d 字节  顶层 field %d 条目" % (name, len(data), field_no))
    ents = top_entries(data, field_no)
    print("顶层 field %d 共 %d 条" % (field_no, len(ents)))
    for k, ent in enumerate(ents[:take]):
        print("---- 第 %d 条 ----" % (k + 1))
        dump(ent, 0, 0, 3)
