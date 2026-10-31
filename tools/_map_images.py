# -*- coding: utf-8 -*-
"""★ 只读侦察：Map.bin 顶层 field 1 = 「地图id -> 官方图片名」注册表。

tools\\map_diag_report.txt 已经实证（field 1 的第一条）：
    inner.field 8 (wire 2) = 'UT_MapPreview_003'
    inner.field 9 (wire 2) = 'UT_MapScene_003'
也就是说 —— **游戏自己在"选图界面"给每张地图配的那张图，名字就写在这张表里**。
这比"猜命名族"可靠得多。本脚本把 24 条全 dump 出来，重点看 82016。

注意：顶层 field 1 的 entry 要**钻两层**（外层 field 1 里再套 MapEntry）。
游戏目录只读；产物只落 tools\\_map_images.txt。
"""
import io
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, "_map_images.txt")
LOG = []

MAP_BIN = r"D:\小小兔趴\4-服务端\server\data\gamedata\Map.bin"


def say(s):
    LOG.append(str(s))
    print(s)
    sys.stdout.flush()


def parse_fields(buf):
    out = []
    i = 0
    n = len(buf)
    while i < n:
        key = 0
        shift = 0
        while True:
            if i >= n:
                return out
            b = buf[i]
            i += 1
            key |= (b & 0x7F) << shift
            shift += 7
            if not (b & 0x80):
                break
        num, wire = key >> 3, key & 7
        if wire == 0:
            v = 0
            shift = 0
            while True:
                if i >= n:
                    return out
                b = buf[i]
                i += 1
                v |= (b & 0x7F) << shift
                shift += 7
                if not (b & 0x80):
                    break
            out.append((num, wire, v))
        elif wire == 2:
            ln = 0
            shift = 0
            while True:
                if i >= n:
                    return out
                b = buf[i]
                i += 1
                ln |= (b & 0x7F) << shift
                shift += 7
                if not (b & 0x80):
                    break
            out.append((num, wire, buf[i:i + ln]))
            i += ln
        elif wire == 5:
            if i + 4 > n:
                return out
            out.append((num, wire, int.from_bytes(buf[i:i + 4], "little", signed=True)))
            i += 4
        elif wire == 1:
            if i + 8 > n:
                return out
            out.append((num, wire, buf[i:i + 8]))
            i += 8
        else:
            return out
    return out


def unwrap_entries(buf, field):
    """顶层 field 的每条 entry：外 field1(wire2) -> {inner field1=key, field2=body}"""
    res = []
    for num, wire, val in parse_fields(buf):
        if num != field or wire != 2:
            continue
        key = None
        body = None
        sub = parse_fields(val)
        inner = None
        for n2, w2, v2 in sub:
            if n2 == 1 and w2 == 2:
                inner = parse_fields(v2)
        scan = inner if inner is not None else sub
        for n2, w2, v2 in scan:
            if n2 == 1 and w2 in (0, 5):
                key = v2
            elif n2 == 2 and w2 == 2:
                body = v2
        if key is not None:
            res.append((key, body))
    return res


def as_text(b):
    if b is None:
        return None
    try:
        s = b.decode("utf-8")
    except Exception:
        return None
    if s and all(32 <= ord(c) or c in "\t" for c in s) and len(s) < 200:
        return s
    return None


def main():
    say("Map.bin = %s（%d 字节）" % (MAP_BIN, os.path.getsize(MAP_BIN)))
    data = io.open(MAP_BIN, "rb").read()
    say("")

    rows = unwrap_entries(data, 1)
    say("=" * 78)
    say("顶层 field 1 的 entry 数 = %d  —— 「地图id -> 官方图片名」注册表" % len(rows))
    say("=" * 78)
    for key, body in rows:
        fs = parse_fields(body or b"")
        strs = {}
        others = []
        for n2, w2, v2 in fs:
            if w2 == 2:
                t = as_text(v2)
                if t is not None:
                    strs[n2] = t
                else:
                    others.append((n2, w2, len(v2)))
            else:
                others.append((n2, w2, v2))
        say("  地图 id %-9s  %s" % (key, ""))
        for n2 in sorted(strs):
            say("        field %-3d = %r" % (n2, strs[n2]))
        say("        其它字段：%s" % others)
    say("")

    # 场景表（顶层 field 3 / field 4 都是 key->场景名）
    for top in (3, 4):
        rs = unwrap_entries(data, top)
        say("=" * 78)
        say("顶层 field %d entry 数 = %d（场景表）" % (top, len(rs)))
        say("=" * 78)
        for key, body in rs:
            fs = parse_fields(body or b"")
            strs = [(n2, as_text(v2)) for n2, w2, v2 in fs if w2 == 2]
            say("   场景 %-5s  %s" % (key, strs))
        say("")


if __name__ == "__main__":
    main()
