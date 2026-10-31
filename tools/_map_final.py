# -*- coding: utf-8 -*-
"""★ 只读：把 Map.bin 的「地图id -> 名字 + 官方图片名」一次对齐打出来（终版核对）。

结论用：82016（异变图书馆）到底登记的是哪两张图。
只读 4-服务端 的表。
"""
import io
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
GD = r"D:\小小兔趴\4-服务端\server\data\gamedata"

from _map_images2 import parse_fields, walk, unwrap  # noqa: E402


def tx(v):
    try:
        t = v.decode("utf-8")
    except Exception:
        return None
    return t if t and all(32 <= ord(c) for c in t) and len(t) < 200 else None


def main():
    strs = io.open(os.path.join(GD, "STRMap.bin"), "rb").read()
    raw = io.open(os.path.join(GD, "Map.bin"), "rb").read()
    names = {}
    for k, b in unwrap(strs, 2):
        for n2, w2, v2 in parse_fields(b or b""):
            if n2 == 2 and w2 == 2:
                t = tx(v2)
                if t:
                    names[k] = t
                break
    print("STRMap 条目 %d" % len(names))
    print()
    print("%-9s %-14s %-26s %-26s %s" % ("地图id", "名字", "MapPreview(字段8)",
                                         "MapScene(字段9)", "字段5"))
    print("-" * 104)
    rows = []
    for k, b in unwrap(raw, 1):
        acc = []
        walk(b or b"", [], 0, acc)
        d = {}
        for p, t in acc:
            if t is not None and len(p) == 1:
                d[p[0][0]] = t
            elif p and p[-1][1] == "fix32" and len(p) == 1:
                d[("int", p[0][0])] = p[-1][2]
        rows.append((k, names.get(d.get(("int", 3)), "?"), d.get(8), d.get(9),
                     d.get(5), d.get(("int", 3))))
    for k, nm, p8, p9, f5, nid in sorted(rows):
        print("%-9s %-14s %-26s %-26s %s" % (k, nm, p8, p9, f5))
    print()
    for k, nm, p8, p9, f5, nid in rows:
        if k == 82016:
            print("★ 82016 的官方登记：名字=%s  MapImage=%s  MapSceneImage=%s"
                  % (nm, p8, p9))


if __name__ == "__main__":
    main()
