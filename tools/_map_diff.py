# -*- coding: utf-8 -*-
"""★ 只读：`Map.bin` 的「难度登记」到底登记了哪几档（决定空白模板的 base_level 取哪一档）。

`GameTables.DifficultyRows()` 读的是另一张表；这里直接从 `Map.bin` field 2 每一行里
把 **field 6（DifficultyIds，packed fixed32）** 打出来，再看每张可新建图的档位值。
"""
import io
import os
import struct
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(HERE, "pylibs"))

BIN = r"D:\小小兔趴\4-服务端\server\data\gamedata\Map.bin"
OUT = os.path.join(HERE, "_map_diff.txt")

LOG = []
def say(s):
    LOG.append(str(s))


def read_varint(buf, i):
    shift = 0
    val = 0
    while True:
        b = buf[i]; i += 1
        val |= (b & 0x7F) << shift
        if not (b & 0x80):
            break
        shift += 7
    return val, i


def fields(buf):
    out = []
    i = 0
    n = len(buf)
    while i < n:
        key, i = read_varint(buf, i)
        num, wire = key >> 3, key & 7
        if wire == 0:
            v, i = read_varint(buf, i); out.append((num, wire, v))
        elif wire == 1:
            out.append((num, wire, buf[i:i + 8])); i += 8
        elif wire == 2:
            ln, i = read_varint(buf, i); out.append((num, wire, buf[i:i + ln])); i += ln
        elif wire == 5:
            out.append((num, wire, struct.unpack_from("<I", buf, i)[0])); i += 4
        else:
            raise ValueError("wire %d" % wire)
    return out


def main():
    raw = io.open(BIN, "rb").read()
    say("Map.bin field 2 每行的 field 6（DifficultyIds）—— 前 3 个值：")
    say("")
    say("%-8s %-30s %s" % ("地图id", "field6 原始值（前3）", "其他 wire2 字段号"))
    for num, wire, val in fields(raw):
        if num != 2 or wire != 2:
            continue
        mid = None
        d6 = []
        w2 = []
        for f, w, v in fields(val):
            if f == 1 and w == 5:
                mid = v
            elif f == 6 and w == 2:
                n = len(v) // 4
                d6 = list(struct.unpack_from("<%dI" % n, v, 0)) if n else []
            elif w == 2 and f != 6:
                w2.append(str(f))
        if mid is None:
            continue
        say("%-8s %-30s %s" % (mid, ",".join(str(x) for x in d6[:3]), ",".join(w2)))
    io.open(OUT, "w", encoding="utf-8").write("\n".join(LOG))
    sys.stdout.write("report: %s\n" % OUT)


if __name__ == "__main__":
    main()
