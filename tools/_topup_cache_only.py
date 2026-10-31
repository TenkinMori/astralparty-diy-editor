# -*- coding: utf-8 -*-
"""★ 只读：把**只有运行时缓存里才有**的那几个头像补落地
（安装目录 + GameRes 都没有的）。

从 `_cache_inv.tsv` 与 `_tex_inv.tsv` 求差集得到候选，再逐个从缓存包里抠出来。
"""
import io
import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(HERE, "pylibs"))

ROOT = os.path.join(os.environ["USERPROFILE"], "AppData", "LocalLow", "feimo",
                    "AstralParty_CN", "com.unity.addressables", "AssetBundles")
CACHE = os.path.join(os.path.dirname(HERE), "贴图缓存")
GRES = r"D:\steam\steamapps\common\Astral Party\8vJXn6CN\GameRes\Img"
PAT = re.compile(r"^UT_AccountHeadShot_(Boss|Emoji|Npc|Monster)_\d+$")


def load_tsv(p):
    d = {}
    if not os.path.exists(p):
        return d
    for l in io.open(p, encoding="utf-8"):
        f = l.rstrip("\n").split("\t")
        if len(f) >= 4:
            d.setdefault(f[0], f[-1])
    return d


def find_data(name):
    for first in os.listdir(ROOT):
        d1 = os.path.join(ROOT, first)
        if not os.path.isdir(d1):
            continue
        d2 = os.path.join(d1, name)
        p = os.path.join(d2, "__data")
        if os.path.isdir(d2) and os.path.exists(p):
            return p
    return None


def main():
    inst = load_tsv(os.path.join(HERE, "_tex_inv.tsv"))
    run = load_tsv(os.path.join(HERE, "_cache_inv.tsv"))
    gres = set(os.path.splitext(f)[0] for f in os.listdir(GRES))

    # 只差集：缓存有、安装包没有
    cand = [n for n in sorted(run) if PAT.match(n) and n not in inst]
    print("运行时缓存独有（安装包没有）的头像族 = %d 个：" % len(cand))
    for n in cand:
        print("   %-38s GameRes也有=%s" % (n, n in gres))
    print()

    import UnityPy
    for nm in cand:
        bn = run[nm]
        p = find_data(bn)
        if not p:
            print("  !! 缓存包目录找不到：%s" % bn)
            continue
        env = UnityPy.load(p)
        for o in env.objects:
            if o.type.name != "Texture2D":
                continue
            try:
                d = o.read()
            except Exception:
                continue
            if getattr(d, "m_Name", None) != nm:
                continue
            im = d.image
            if im is None:
                continue
            im = im.convert("RGBA")
            out = os.path.join(CACHE, nm + ".png")
            im.save(out, "PNG")
            print("   ★ %-38s %dx%d -> %s（%d 字节）"
                  % (nm, im.width, im.height, os.path.basename(out),
                     os.path.getsize(out)))
            break
        else:
            print("   !! %s 的包里没找到这个 Texture2D" % nm)
    return 0


if __name__ == "__main__":
    sys.exit(main())
