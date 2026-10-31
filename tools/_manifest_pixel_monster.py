# -*- coding: utf-8 -*-
"""★ 只读：产出最终清单「像素小人 -> 怪 id -> 名字」（含本地没有实体的 46/47/48）。

只读 4-服务端 的表；只往 贴图缓存\\ 写一份 清单.txt。
"""
import io
import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
GD = r"D:\小小兔趴\4-服务端\server\data\gamedata"
CACHE = os.path.join(os.path.dirname(HERE), "贴图缓存")

from _map_images2 import parse_fields, unwrap  # noqa: E402

PAT = re.compile(r"^UT_AccountHeadShot_Monster_(\d+)$")


def tx(v):
    try:
        t = v.decode("utf-8")
    except Exception:
        return None
    return t if t and all(32 <= ord(c) for c in t) and len(t) < 200 else None


def main():
    names = {}
    for k, b in unwrap(io.open(os.path.join(GD, "STRMonster.bin"), "rb").read(), 2):
        for n2, w2, v2 in parse_fields(b or b""):
            if n2 == 2 and w2 == 2:
                t = tx(v2)
                if t:
                    names[k] = t
                break
    rows = []
    for k, b in unwrap(io.open(os.path.join(GD, "Monster.bin"), "rb").read(), 2):
        nid = head = None
        for n2, w2, v2 in parse_fields(b or b""):
            if n2 == 4 and w2 == 5:
                nid = v2
            elif n2 == 19 and w2 == 2:
                head = tx(v2)
        if head and PAT.match(head):
            rows.append((int(PAT.match(head).group(1)), head, k, names.get(nid, "?")))
    rows.sort()

    have = set(f[:-4] for f in os.listdir(CACHE)
               if f.lower().endswith(".png") and PAT.match(f[:-4]))

    out = []
    out.append("# 「图鉴」里那套像素风怪物小人 —— 落地清单")
    out.append("#")
    out.append("# 源：游戏 Addressables 包里的 Texture2D 资产")
    out.append("#     资产名 = UT_AccountHeadShot_Monster_<NN>（200x200，像素风）")
    out.append("# 与怪物 id / 名字的对应关系来自客户端配置表：")
    out.append("#     Monster.bin    field 19 = 头像资产名；field 4 = 名字 id")
    out.append("#     STRMonster.bin field 2  = 中文名")
    out.append("# 落地目录：D:\\小小兔趴\\5-DIY关卡\\贴图缓存\\")
    out.append("#")
    out.append("# 序号\t怪id\t中文名\t落地文件\t本地包是否有该实体")
    for idx, head, mid, nm in rows:
        fn = head + ".png"
        out.append("%d\t%d\t%s\t%s\t%s"
                   % (idx, mid, nm, fn, "有" if head in have else "无（本地客户端没下到，表里有登记）"))
    man = os.path.join(CACHE, "像素怪物小人清单.txt")
    io.open(man, "w", encoding="utf-8").write("\n".join(out) + "\n")
    print("清单：%s（%d 行数据）" % (man, len(rows)))
    return 0


if __name__ == "__main__":
    sys.exit(main())
