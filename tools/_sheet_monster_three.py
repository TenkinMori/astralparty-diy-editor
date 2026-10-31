# -*- coding: utf-8 -*-
"""★ 只读：给补漏的三只做画风核对 —— 拼一张「新增 46/47/48 + 既有样本」对照表，
并算像素画签名（颜色数）。

产物：tools\\_sheet_monster_three.png
"""
import io
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(HERE, "pylibs"))
CACHE = os.path.join(os.path.dirname(HERE), "贴图缓存")

NEW = ["UT_AccountHeadShot_Monster_46.png",
       "UT_AccountHeadShot_Monster_47.png",
       "UT_AccountHeadShot_Monster_48.png"]
SAMPLE = ["UT_AccountHeadShot_Monster_%02d.png" % i
          for i in (28, 35, 37, 40, 44, 45)]


def main():
    from PIL import Image, ImageDraw
    rows_spec = [("★ 本轮补漏（来自 GameRes\\Img）", NEW),
                 ("对照：既有（来自 bundle）", SAMPLE)]
    CELL, COLS = 200, 6
    height = len(rows_spec) * (CELL + 60)
    sheet = Image.new("RGBA", (COLS * CELL, height), (24, 26, 32, 255))
    dr = ImageDraw.Draw(sheet)
    y = 0
    for title, files in rows_spec:
        dr.text((6, y + 4), title, fill=(255, 220, 120, 255))
        y += 26
        for i, fn in enumerate(files):
            p = os.path.join(CACHE, fn)
            if not os.path.exists(p):
                continue
            im = Image.open(p).convert("RGBA")
            cols = im.getcolors(maxcolors=300000)
            ncol = len(cols) if cols else -1
            t = im.copy()
            t.thumbnail((CELL - 8, CELL - 8), Image.NEAREST)
            cx = i * CELL
            sheet.alpha_composite(t, (cx + 4, y + 4))
            dr.text((cx + 4, y + CELL + 2),
                    fn.replace("UT_AccountHeadShot_Monster_", "M").replace(".png", "")
                    + "  %dx%d" % (im.width, im.height),
                    fill=(220, 226, 240, 255))
            dr.text((cx + 4, y + CELL + 18), "颜色 %d" % ncol,
                    fill=(150, 210, 160, 255))
            print("  %-40s %dx%d  颜色 %d" % (fn, im.width, im.height, ncol))
        y += CELL + 60
    out = os.path.join(HERE, "_sheet_monster_three.png")
    sheet.convert("RGB").save(out, "PNG")
    print("对照表：%s" % out)
    return 0


if __name__ == "__main__":
    sys.exit(main())
