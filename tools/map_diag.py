# -*- coding: utf-8 -*-
"""★ 只读诊断：Map.bin / GameMode.bin 到底长什么样。

用途：写 C# 那套 Proto 读表器时，**不靠猜字段号**。这里照抄
`4-服务端\server\astralnext\codec.py` 的线格式读法，把每层的
字段号 / 线型 / 值类型 / 样本 打出来，跟 `gamedata.py:79-140` 的读法逐条对齐。

★ 只读，不写任何地方（除了把结果打印出来）。
用法：python map_diag.py
"""
import io
import json
import os
import struct
import sys

SRV = r"D:\小小兔趴\4-服务端"
sys.path.insert(0, os.path.join(SRV, "tools"))
sys.path.insert(0, os.path.join(SRV, "server"))

TABLE = os.path.join(SRV, "server", "data", "gamedata")


# ---------------------------------------------------------------- 线格式
def parse_fields(data, start=0, end=None):
    """照抄 codec.parse_fields 的语义：yield (field, wire, value)。"""
    if end is None:
        end = len(data)
    i = start
    while i < end:
        raw = 0
        shift = 0
        while i < end:
            b = data[i]
            i += 1
            raw |= (b & 0x7F) << shift
            if not (b & 0x80):
                break
            shift += 7
        field = raw >> 3
        wire = raw & 7
        if field == 0:
            return
        if wire == 0:
            val = 0
            shift = 0
            while i < end:
                b = data[i]
                i += 1
                val |= (b & 0x7F) << shift
                if not (b & 0x80):
                    break
                shift += 7
            yield field, wire, val
        elif wire == 1:
            yield field, wire, data[i:i + 8]
            i += 8
        elif wire == 2:
            ln = 0
            shift = 0
            while i < end:
                b = data[i]
                i += 1
                ln |= (b & 0x7F) << shift
                if not (b & 0x80):
                    break
                shift += 7
            yield field, wire, data[i:i + ln]
            i += ln
        elif wire == 5:
            yield field, wire, data[i:i + 4]
            i += 4
        else:
            return


def describe(v):
    if isinstance(v, bytes):
        try:
            s = v.decode("utf-8")
            if s.isprintable() and len(s) < 60:
                return "bytes(%d)=%r" % (len(v), s)
        except Exception:
            pass
        return "bytes(%d)=%s" % (len(v), v[:24].hex())
    return "int=%d" % v


def fixed32s(blob):
    return [struct.unpack_from("<i", blob, i)[0] for i in range(0, len(blob) - len(blob) % 4, 4)]


def zigzag32s(blob):
    out = []
    for _f, _w, v in parse_fields(blob):
        pass
    i = 0
    while i < len(blob):
        raw = 0
        shift = 0
        while i < len(blob):
            b = blob[i]
            i += 1
            raw |= (b & 0x7F) << shift
            if not (b & 0x80):
                break
            shift += 7
        out.append((raw >> 1) ^ -(raw & 1))
    return out


def dump_map():
    path = os.path.join(TABLE, "Map.bin")
    print("=" * 78)
    print("Map.bin  %s  (%d 字节)" % (path, os.path.getsize(path)))
    data = open(path, "rb").read()
    top = {}
    for f, w, v in parse_fields(data):
        top.setdefault((f, w), 0)
        top[(f, w)] += 1
    print("  顶层字段：")
    for (f, w), n in sorted(top.items()):
        print("    field %-4d wire %-2d ×%d" % (f, w, n))

    print("")
    print("  各 field 的第一条解出来看看：")
    shown = set()
    for f, w, v in parse_fields(data):
        if (f, w) in shown:
            continue
        shown.add((f, w))
        print("    -- field %d wire %d --" % (f, w))
        if w != 2:
            print("       %s" % describe(v))
            continue
        inner = list(parse_fields(v))
        print("       内层：")
        for f2, w2, v2 in inner:
            line = "         field %-4d wire %-2d %s" % (f2, w2, describe(v2))
            if w2 == 2 and f2 not in (1, 2):
                line += "  packed→" + repr(zigzag32s(v2)[:6])
            print(line)


def dump_mode():
    path = os.path.join(TABLE, "GameMode.bin")
    print("=" * 78)
    print("GameMode.bin  %s  (%d 字节)" % (path, os.path.getsize(path)))
    data = open(path, "rb").read()
    top = {}
    for f, w, v in parse_fields(data):
        top.setdefault((f, w), 0)
        top[(f, w)] += 1
    print("  顶层字段：")
    for (f, w), n in sorted(top.items()):
        print("    field %-4d wire %-2d ×%d" % (f, w, n))

    print("")
    n = 0
    for f, w, v in parse_fields(data):
        if not (f == 2 and w == 2):
            continue
        n += 1
        if n > 3:
            break
        print("  -- 第 %d 条 field-2 entry --" % n)
        for f2, w2, v2 in parse_fields(v):
            extra = ""
            if w2 == 2 and f2 == 7:
                extra = "  packed→" + repr(zigzag32s(v2)[:8])
            elif w2 == 2 and f2 == 2:
                try:
                    extra = "  text=" + repr(v2.decode("utf-8"))[:40]
                except Exception:
                    extra = "  (非文本)"
            print("     field %-4d wire %-2d %s%s" % (f2, w2, describe(v2), extra))


def main():
    dump_map()
    dump_mode()

    print("=" * 78)
    print("照 gamedata.py:79-140 的读法复算一遍：")
    data = open(os.path.join(TABLE, "Map.bin"), "rb").read()
    scenes = {}
    for f, w, v in parse_fields(data):
        if not (f == 4 and w == 2):
            continue
        key = None
        for f2, w2, v2 in parse_fields(v):
            if f2 == 1 and w2 == 5:
                key = struct.unpack_from("<i", v2, 0)[0]
            elif f2 == 2 and w2 == 2:
                scenes[key] = v2.decode("utf-8", "replace")
    print("  场景表 %d 条，样本：%r" % (len(scenes), list(scenes.items())[:5]))

    got = {}
    for f, w, v in parse_fields(data):
        if not (f == 2 and w == 2):
            continue
        map_id = None
        mids = None
        diffs = None
        for f2, w2, v2 in parse_fields(v):
            if f2 == 1 and w2 == 5:
                map_id = struct.unpack_from("<i", v2, 0)[0]
            elif f2 == 5 and w2 == 2:
                mids = zigzag32s(v2)
            elif f2 == 6 and w2 == 2:
                diffs = fixed32s(v2)
        if map_id is None:
            continue
        suffix = None
        if mids:
            name = scenes.get(mids[0])
            if name:
                suffix = name.rsplit("_", 1)[-1]
        got[map_id] = (suffix, diffs)
    for want in (82016, 82007, 82017):
        print("  map %d -> suffix=%r difficulties=%r" % (want, got.get(want, (None, None))[0],
                                                          got.get(want, (None, None))[1]))
    return 0


if __name__ == "__main__":
    sys.exit(main())
