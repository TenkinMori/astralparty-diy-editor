# -*- coding: utf-8 -*-
"""★ 只读侦察：Map.bin 里「地图id -> 官方图片名」到底嵌在第几层。

map_diag_report.txt 已实证 field 1 的**第一条** entry 里躺着
    field 8 = 'UT_MapPreview_003'、field 9 = 'UT_MapScene_003'。
本脚本把 field 1 的**每一条**逐层展开到能看见这两个字符串为止，
专门定位 82016 那一行的值。游戏目录只读。
"""
import io
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, "_map_images2.txt")
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


NAMES = ("UT_", "Map_", "MiniMap_")


def unwrap(buf, top):
    """顶层 map<key, body>（两层 MapEntry 都钻）。"""
    res = []
    for num, wire, val in parse_fields(buf):
        if num != top or wire != 2:
            continue
        key = body = None
        sub = parse_fields(val)
        inner = None
        for n2, w2, v2 in sub:
            if n2 == 1 and w2 == 2:
                inner = parse_fields(v2)
        for n2, w2, v2 in (inner if inner is not None else sub):
            if n2 == 1 and w2 in (0, 5):
                key = v2
            elif n2 == 2 and w2 == 2:
                body = v2
        if key is not None:
            res.append((key, body))
    return res


def walk(buf, path, depth, acc):
    """把每个 wire2 字段都试着当嵌套消息再解一层；收集看着像资源名的字符串。"""
    for num, wire, val in parse_fields(buf):
        if wire != 2:
            if wire == 5 and depth <= 2:
                acc.append((path + [(num, "fix32", val)], None))
            continue
        txt = None
        try:
            s = val.decode("ascii")
            if s and all(32 <= ord(c) < 127 for c in s):
                txt = s
        except Exception:
            txt = None
        if txt is not None:
            acc.append((path + [(num, "str", txt)], txt))
            continue
        if depth < 4:
            walk(val, path + [(num, "msg", len(val))], depth + 1, acc)


def main():
    data = io.open(MAP_BIN, "rb").read()
    say("Map.bin = %s（%d 字节）" % (MAP_BIN, len(data)))
    say("")

    # 顶层 field 1 的每条 entry，**不做任何 unwrap**，原样递归
    idx = 0
    for num, wire, val in parse_fields(data):
        if num != 1 or wire != 2:
            continue
        idx += 1
        acc = []
        walk(val, [], 0, acc)
        # 找地图 id（第一个 fix32）
        mapid = None
        for p, t in acc:
            if p and p[0][1] == "fix32":
                mapid = p[0][2]
                break
        names = [t for p, t in acc if t and t.startswith(NAMES)]
        if idx <= 3 or (mapid in (82016, 82007, 82017, 81007)):
            say("-" * 74)
            say("entry #%d  地图 id = %s" % (idx, mapid))
            for p, t in acc:
                if t is not None:
                    say("    %-52s = %r" % (".".join(str(x[0]) for x in p), t))
                elif p and p[-1][1] == "fix32" and len(p) == 1:
                    say("    %-52s = %s" % (".".join(str(x[0]) for x in p), p[-1][2]))
        else:
            say("entry #%-2d 地图 id = %-7s 图片名 = %s" % (idx, mapid, names))
    say("")
    say("总 entry = %d" % idx)
    io.open(OUT, "w", encoding="utf-8").write("\n".join(LOG) + "\n")
    print("报告：" + OUT)


if __name__ == "__main__":
    main()
