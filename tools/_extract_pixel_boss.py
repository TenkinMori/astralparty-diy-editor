# -*- coding: utf-8 -*-
"""★ 只读抽取（补）：`UT_AccountHeadShot_Boss_01..13` / `_Emoji_01..02`
—— 和像素怪小人同一族、同一像素画风（都是 200x200 头像），顺手抠出来，
    这样「图鉴头像」这一族是完整的。

只读游戏包；只往 贴图缓存\\ 写。
"""
import io
import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(HERE, "pylibs"))
BUNDLES = r"D:\steam\steamapps\common\Astral Party\8vJXn6CN\AstralParty_CN_Data\StreamingAssets\aa\StandaloneWindows64"
TSV = os.path.join(HERE, "_tex_inv.tsv")
CACHE = os.path.join(os.path.dirname(HERE), "贴图缓存")
PAT = re.compile(r"^UT_AccountHeadShot_(Boss|Emoji)_\d+$")


def main():
    rows = [l.rstrip("\n").split("\t") for l in io.open(TSV, encoding="utf-8")]
    want = {}
    for nm, w, h, fmt, bn in rows:
        if PAT.match(nm):
            want.setdefault(bn, []).append(nm)
    print("目标 %d 张，源包 %d 个" % (sum(len(v) for v in want.values()), len(want)))
    import UnityPy
    os.makedirs(CACHE, exist_ok=True)
    done = []
    for bn, items in sorted(want.items()):
        p = os.path.join(BUNDLES, bn)
        if not os.path.exists(p):
            print("  !! 包不存在 %s" % bn)
            continue
        env = UnityPy.load(p)
        names = set(items)
        for o in env.objects:
            if o.type.name != "Texture2D":
                continue
            try:
                d = o.read()
            except Exception:
                continue
            nm = getattr(d, "m_Name", None)
            if nm not in names:
                continue
            try:
                im = d.image
            except Exception as e:
                print("  !! 解码失败 %s %s" % (nm, e))
                continue
            if im is None:
                continue
            im = im.convert("RGBA")
            out = os.path.join(CACHE, nm + ".png")
            im.save(out, "PNG")
            done.append((nm, im.width, im.height, bn, os.path.getsize(out)))
            names.discard(nm)
        for nm in sorted(names):
            print("  !! 包里没读到 %s（%s）" % (nm, bn))
    done.sort()
    for nm, w, h, bn, sz in done:
        print("   %-34s %3dx%-3d %7d 字节  %s.bundle" % (nm, w, h, sz, bn))
    print("共 %d 张" % len(done))
    return 0


if __name__ == "__main__":
    sys.exit(main())
