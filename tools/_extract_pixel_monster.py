# -*- coding: utf-8 -*-
"""★ 只读抽取：把「图鉴/头像」里那套**像素风怪物小人**
（`UT_AccountHeadShot_Monster_01..45`，200x200）从游戏包里抠出来。

- 源：游戏 Addressables 包（**只读**打开，内存里解码）
- 落：5-DIY关卡\\贴图缓存\\  （照现有命名约定，一个怪一张 PNG）
- 同时产出一份 矩形/尺寸清单 与一张联络表，供人眼核对

游戏目录一个字节都不写。
"""
import io
import json
import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(HERE, "pylibs"))

BUNDLES = r"D:\steam\steamapps\common\Astral Party\8vJXn6CN\AstralParty_CN_Data\StreamingAssets\aa\StandaloneWindows64"
TSV = os.path.join(HERE, "_tex_inv.tsv")
CACHE = os.path.join(os.path.dirname(HERE), "贴图缓存")
REPORT = os.path.join(HERE, "_monster_pixel_report.txt")

PAT = re.compile(r"^UT_AccountHeadShot_Monster_(\d+)$")

LOG = []


def say(s):
    LOG.append(str(s))
    print(s)
    sys.stdout.flush()


def main():
    rows = [l.rstrip("\n").split("\t") for l in io.open(TSV, encoding="utf-8")]
    want = {}
    for nm, w, h, fmt, bn in rows:
        m = PAT.match(nm)
        if m:
            want.setdefault(bn, []).append((nm, int(w), int(h)))
    total = sum(len(v) for v in want.values())
    say("源包数 = %d，目标贴图 = %d 张" % (len(want), total))

    import UnityPy
    from PIL import Image

    os.makedirs(CACHE, exist_ok=True)
    done = []
    for bn, items in sorted(want.items()):
        p = os.path.join(BUNDLES, bn)
        if not os.path.exists(p):
            say("  !! 包不存在：%s" % bn)
            continue
        env = UnityPy.load(p)
        names = dict((n, (w, h)) for n, w, h in items)
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
                say("  !! 解码失败 %s：%s" % (nm, e))
                continue
            if im is None:
                continue
            im = im.convert("RGBA")
            out = os.path.join(CACHE, nm + ".png")
            im.save(out, "PNG")
            done.append((nm, im.width, im.height, bn,
                         os.path.getsize(out)))
            names.pop(nm, None)
        for nm in names:
            say("  !! 包里没读到：%s（%s）" % (nm, bn))

    done.sort()
    say("")
    say("=" * 78)
    say("抽出的像素怪物小人（%d 张）" % len(done))
    say("=" * 78)
    for nm, w, h, bn, sz in done:
        say("   %-36s %3dx%-3d  %7d 字节   bundle=%s.bundle" % (nm, w, h, sz, bn))

    io.open(REPORT, "w", encoding="utf-8").write("\n".join(LOG) + "\n")

    # 联络表（给报告用）
    if done:
        CELL, COLS = 200, 8
        rows_n = (len(done) + COLS - 1) // COLS
        from PIL import ImageDraw
        sheet = Image.new("RGBA", (COLS * CELL, rows_n * (CELL + 18)),
                          (24, 26, 32, 255))
        dr = ImageDraw.Draw(sheet)
        for i, (nm, w, h, bn, sz) in enumerate(done):
            im = Image.open(os.path.join(CACHE, nm + ".png")).convert("RGBA")
            im.thumbnail((CELL - 6, CELL - 6), Image.NEAREST)
            cx = (i % COLS) * CELL
            cy = (i // COLS) * (CELL + 18)
            sheet.alpha_composite(im, (cx + 3, cy + 3))
            dr.text((cx + 3, cy + CELL + 3), nm.replace("UT_AccountHeadShot_", "")[:26],
                    fill=(220, 226, 240, 255))
        out = os.path.join(HERE, "_sheet_pixel_monster.png")
        sheet.convert("RGB").save(out, "PNG")
        say("联络表：%s" % out)
    say("报告：%s" % REPORT)
    return 0


if __name__ == "__main__":
    sys.exit(main())
