# -*- coding: utf-8 -*-
"""★ 只读侦察：把 Map.bin 里 82016（异变图书馆）那一行**完整 dump** 出来。

目的：MapInfoConfigure 里除了 Mids（field 5），还有 MapImage / MapSceneImage 之类的
字段 —— 它们指向的就是"游戏在选图界面给这张地图显示的那张图"的资源名。
只要读出它们的值，就知道该去包里找哪个名字。

同时把 field 4（场景表：场景id -> "Map_023"）也 dump 出来对照。
游戏目录只读；产物只落 tools\\_map_dump.txt。
"""
import io
import os
import sys
from collections import defaultdict

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(HERE, "pylibs"))
sys.path.insert(0, os.path.join(os.path.dirname(HERE), "tools"))

OUT = os.path.join(HERE, "_map_dump.txt")
LOG = []


def say(s):
    LOG.append(str(s))
    print(s)
    sys.stdout.flush()


# ---- 找 Map.bin ----
CAND = [r"D:\小小兔趴\5-DIY关卡\src\..\dist", r"D:\小小兔趴\5-DIY关卡"]
CAND = []


def find_map_bin():
    """只在编辑器自己找得到的那几个位置里找（服务端目录只读看一眼也可）。"""
    roots = [r"D:\小小兔趴\5-DIY关卡", r"D:\小小兔趴"]
    hits = []
    for r in roots:
        for dp, dn, fn in os.walk(r):
            if "node_modules" in dp or "\\.git" in dp:
                continue
            for f in fn:
                if f.lower() in ("map.bin", "mapdata.bin"):
                    hits.append(os.path.join(dp, f))
    return hits


def parse_fields(buf):
    """极简 protobuf：产出 [(field, wire, value)]，只处理 wire 0/1/2/5。"""
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
            out.append((num, wire, buf[i:i + 4]))
            i += 4
        elif wire == 1:
            if i + 8 > n:
                return out
            out.append((num, wire, buf[i:i + 8]))
            i += 8
        else:
            return out
    return out


def entries(buf, field):
    """顶层 repeated field -> [(key, body)]"""
    res = []
    for num, wire, val in parse_fields(buf):
        if num != field or wire != 2:
            continue
        sub = parse_fields(val)
        key = None
        for n2, w2, v2 in sub:
            if n2 == 1 and w2 == 0:
                key = v2
                break
        body = None
        for n2, w2, v2 in sub:
            if n2 == 2 and w2 == 2:
                body = v2
                break
        if key is not None:
            res.append((key, body))
    return res


def zigzag(lst):
    return [(x >> 1) ^ -(x & 1) for x in lst]


def sint32_list(b):
    vals = []
    i = 0
    while i < len(b):
        v = 0
        shift = 0
        while True:
            if i >= len(b):
                break
            c = b[i]
            i += 1
            v |= (c & 0x7F) << shift
            shift += 7
            if not (c & 0x80):
                break
        vals.append((v >> 1) ^ -(v & 1))
    return vals


def main():
    hits = find_map_bin()
    say("找到的 Map.bin 候选：")
    for h in hits:
        say("   %s  (%d 字节)" % (h, os.path.getsize(h)))
    if not hits:
        say("（一个都没找到）")
        io.open(OUT, "w", encoding="utf-8").write("\n".join(LOG) + "\n")
        return 1
    # 优先选服务端样例目录旁边那个 / 最小的那个
    hits.sort(key=lambda p: os.path.getsize(p))
    path = hits[0]
    say("用：%s" % path)
    data = io.open(path, "rb").read()
    say("")

    say("=" * 78)
    say("field 4 = 场景表（场景id -> 场景名）")
    say("=" * 78)
    scenename = {}
    for k, body in entries(data, 4):
        nm = None
        for n2, w2, v2 in parse_fields(body or b""):
            if n2 == 2 and w2 == 2:
                nm = v2.decode("utf-8", "replace")
        scenename[k] = nm
        say("   场景id %-6s -> %s" % (k, nm))
    say("")

    say("=" * 78)
    say("field 2 = 地图表（全部地图，含每个字段原值）")
    say("=" * 78)
    for k, body in entries(data, 2):
        fs = parse_fields(body or b"")
        parts = []
        for n2, w2, v2 in fs:
            if w2 == 5:
                parts.append("f%d=fix32:%s" % (n2, v2.hex() if isinstance(v2, bytes) else v2))
            elif w2 == 2:
                raw = v2
                txt = None
                try:
                    txt = raw.decode("ascii")
                    if not all(32 <= ord(c) < 127 for c in txt):
                        txt = None
                except Exception:
                    txt = None
                if txt and len(txt) < 60:
                    parts.append("f%d=str:%r" % (n2, txt))
                else:
                    parts.append("f%d=bytes[%d]:%s" % (n2, len(raw), raw[:24].hex()))
            else:
                parts.append("f%d=%s" % (n2, v2))
        mark = ""
        if k == 82016:
            mark = "   <<<<<<<<<< 异变图书馆"
        say("   地图 %-7s  %s%s" % (k, "  ".join(parts), mark))
    say("")

    say("=" * 78)
    say("82016 逐字段展开（重点看 Mids / 图片名）")
    say("=" * 78)
    for k, body in entries(data, 2):
        if k != 82016:
            continue
        for n2, w2, v2 in parse_fields(body or b""):
            if w2 == 2:
                say("   field %d (bytes[%d])" % (n2, len(v2)))
                mids = sint32_list(v2)
                say("        sint32 解：%s" % mids[:40])
                try:
                    s = v2.decode("ascii")
                    if all(32 <= ord(c) < 127 for c in s):
                        say("        ascii ：%r" % s)
                except Exception:
                    pass
                say("        hex   ：%s" % v2[:64].hex())
            else:
                say("   field %d = %s" % (n2, v2))
    say("")

    io.open(OUT, "w", encoding="utf-8").write("\n".join(LOG) + "\n")
    print("报告：" + OUT)
    return 0


if __name__ == "__main__":
    sys.exit(main())
