# -*- coding: utf-8 -*-
"""只读侦察：Land.bin / STRLand.bin / GameMode.bin / STRGameMode.bin。

目的（2026-10-03 第 4 批）：
  A. `Land.bin` 里每个 landType 到底带哪些字段（有没有贴图名 / 资源名线索）；
  B. `STRLand.bin` 的 landType -> 中文名（服务端认的名字，用来核对编辑器那张对照表）；
  C. `GameMode.bin` 的模式 id -> 地图数 + `STRGameMode.bin` 的名字（用来定 PvP 是哪几个模式）。

★ 一个字节都不写表；只读 + 打报告。
用法：python dump_land_modes.py
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
            if is_text(val):
                print("%sf%-3d w2  text=%r" % (pad, num, val.decode("utf-8")))
                continue
            n4 = len(val) % 4 == 0
            fixed = [struct.unpack_from("<i", val, k)[0]
                     for k in range(0, len(val) - len(val) % 4, 4)] if n4 else None
            extra = ""
            if fixed and all(0 < v < 10000000 for v in fixed):
                extra = "  fixed32列表=%r" % fixed[:10]
            print("%sf%-3d w2  len=%-4d bytes=%s%s" % (pad, num, len(val), val[:20].hex(), extra))
            if depth < maxdepth:
                dump(val, indent + 1, depth + 1, maxdepth)


TABLE = r"D:\小小兔趴\4-服务端\server\data\gamedata"


def load(name):
    p = os.path.join(TABLE, name)
    if not os.path.exists(p):
        return None
    return open(p, "rb").read()


def str_table(data, key_field=1, val_field=2):
    """STR*.bin：顶层 repeated field 1 = 一条（里面 field1=key, field2=文本）。"""
    out = {}
    if data is None:
        return out
    for num, wire, val in fields(data):
        if not (num == 1 and wire == 2):
            continue
        k = None
        t = None
        for n2, w2, v2 in fields(val):
            if n2 == key_field and w2 == 0:
                k = v2
            elif n2 == val_field and w2 == 2:
                try:
                    t = v2.decode("utf-8")
                except Exception:
                    t = None
        if k is not None:
            out[k] = t
    return out


def main():
    print("=" * 78)
    print("A. Land.bin 结构（前 40 条顶层 entries）")
    print("=" * 78)
    land = load("Land.bin")
    print("Land.bin  %s 字节" % (len(land) if land else "缺失"))
    if land:
        shown = 0
        for num, wire, val in fields(land):
            if wire != 2:
                print("顶层 f%d w%d = %r" % (num, wire, val))
                continue
            shown += 1
            print("---- 顶层 field %d w2 第 %d 条 ----" % (num, shown))
            dump(val, 1, 0, 3)
            if shown >= 40:
                break
        print("（顶层 w2 条目数上限 40，实际见上）")

    print("")
    print("=" * 78)
    print("B. STRLand.bin：landType -> 中文名")
    print("=" * 78)
    sl = str_table(load("STRLand.bin"))
    for k in sorted(sl):
        print("  %-4s %s" % (k, sl[k]))

    print("")
    print("=" * 78)
    print("C. GameMode.bin 模式 -> 地图数 / STRGameMode.bin 名字")
    print("=" * 78)
    gm = load("GameMode.bin")
    names = str_table(load("STRGameMode.bin"))
    print("STRGameMode.bin 条目：")
    for k in sorted(names):
        print("  %-4s %s" % (k, names[k]))
    print("")
    if gm:
        for num, wire, val in fields(gm):
            if not (num == 2 and wire == 2):
                continue
            mode = None
            maps = []
            for n2, w2, v2 in fields(val):
                if n2 == 1 and w2 == 0:
                    mode = v2
                elif n2 == 2 and w2 == 2:
                    for n3, w3, v3 in fields(v2):
                        if n3 == 7 and w3 == 2:
                            for n4, w4, v4 in fields(v3):
                                if n4 == 1 and w4 == 0:
                                    maps.append(v4)
                                elif n4 == 1 and w4 == 5:
                                    maps.append(struct.unpack_from("<i", v4, 0)[0])
            print("  模式 %-4s 名字=%-14s 地图 %d 张 %s"
                  % (mode, names.get(mode, "?"), len(maps), maps[:12]))
    return 0


if __name__ == "__main__":
    sys.exit(main())
