# -*- coding: utf-8 -*-
"""★ 只读：产出两份清单 —— ① 异变图书馆美术图登记表 ② 全套头像族清单。"""
import io
import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
GD = r"D:\小小兔趴\4-服务端\server\data\gamedata"
CACHE = os.path.join(os.path.dirname(HERE), "贴图缓存")
from _map_images2 import parse_fields, unwrap  # noqa: E402


def tx(v):
    try:
        t = v.decode("utf-8")
    except Exception:
        return None
    return t if t and all(32 <= ord(c) for c in t) and len(t) < 200 else None


def map_rows():
    names = {}
    for k, b in unwrap(io.open(os.path.join(GD, "STRMap.bin"), "rb").read(), 2):
        for n2, w2, v2 in parse_fields(b or b""):
            if n2 == 2 and w2 == 2:
                t = tx(v2)
                if t:
                    names[k] = t
                break
    out = []
    for num, wire, val in parse_fields(io.open(os.path.join(GD, "Map.bin"), "rb").read()):
        if num != 1 or wire != 2:
            continue
        f = {}
        for n, w, v in parse_fields(val):
            if w == 5:
                f[n] = v
            elif w == 2:
                s = tx(v)
                if s:
                    f[n] = s
        if 1 in f:
            out.append((f[1], names.get(f.get(3), "?"), f.get(8), f.get(9)))
    out.sort()
    return out


def main():
    rows = map_rows()
    lines = ["# 官方「地图美术图」登记表（来自 Map.bin 顶层 field 1）",
             "#",
             "# 字段 8 = MapImage（选图卡片那张美术预览，资产名 UT_MapPreview_<画板号>）",
             "# 字段 9 = MapSceneImage（横版大图/载入图，资产名 UT_MapScene_<画板号>）",
             "# 名字 = STRMap 里 field 3（名字 id）对应的文案",
             "#",
             "# 地图id\t名字\tMapImage\tMapSceneImage\t本地是否已有该 PNG"]
    miss = []
    for mid, nm, p8, p9 in rows:
        have = []
        for a in (p8, p9):
            if not a:
                have.append("-")
                continue
            if os.path.exists(os.path.join(CACHE, a + ".png")):
                have.append("有")
            elif os.path.exists(os.path.join(
                    r"D:\steam\steamapps\common\Astral Party\8vJXn6CN\GameRes\Img",
                    a + ".png")):
                have.append("游戏目录有")
            else:
                have.append("无")
                miss.append((mid, nm, a))
        lines.append("%s\t%s\t%s\t%s\t%s" % (mid, nm, p8 or "-", p9 or "-",
                                             " / ".join(have)))
    p = os.path.join(CACHE, "官方地图美术图登记表.txt")
    io.open(p, "w", encoding="utf-8").write("\n".join(lines) + "\n")
    print("登记表：%s（%d 张地图）" % (p, len(rows)))
    print()
    for mid, nm, p8, p9 in rows:
        if mid in (81007, 82007, 82016, 82014, 82015, 82017):
            print("  %-7s %-16s %-22s %s" % (mid, nm[:16], p8, p9))
    print()
    print("登记了但本地客户端没有实体的：")
    for mid, nm, a in miss:
        print("   地图 %-7s %-16s %s" % (mid, nm[:16], a))
    return 0


if __name__ == "__main__":
    sys.exit(main())
