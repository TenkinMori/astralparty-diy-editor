# -*- coding: utf-8 -*-
"""★ 只读：出最终联络表 —— 像素怪小人 01..48 全图 + BOSS 族（含缓存补的 Boss_14）。"""
import io
import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(HERE, "pylibs"))
CACHE = os.path.join(os.path.dirname(HERE), "贴图缓存")


def sheet(files, out, cols=8, cell=200, label=True, title=None):
    from PIL import Image, ImageDraw
    rows = (len(files) + cols - 1) // cols
    head = 30 if title else 0
    img = Image.new("RGBA", (cols * cell, head + rows * (cell + 18)), (24, 26, 32, 255))
    dr = ImageDraw.Draw(img)
    if title:
        dr.text((6, 8), title, fill=(255, 220, 120, 255))
    for i, fn in enumerate(files):
        p = os.path.join(CACHE, fn)
        if not os.path.exists(p):
            continue
        im = Image.open(p).convert("RGBA")
        im.thumbnail((cell - 6, cell - 6), Image.NEAREST)
        cx = (i % cols) * cell
        cy = head + (i // cols) * (cell + 18)
        img.alpha_composite(im, (cx + 3, cy + 3))
        if label:
            dr.text((cx + 3, cy + cell + 3),
                    fn.replace("UT_AccountHeadShot_", "").replace(".png", "")[:26],
                    fill=(220, 226, 240, 255))
    img.convert("RGB").save(os.path.join(HERE, out), "PNG")
    print("%s：%d 张 -> %dx%d" % (out, len(files), img.width, img.height))


def main():
    mons = sorted(f for f in os.listdir(CACHE)
                  if re.match(r"^UT_AccountHeadShot_Monster_\d+\.png$", f))
    bosses = sorted(f for f in os.listdir(CACHE)
                    if re.match(r"^UT_AccountHeadShot_Boss_\d+\.png$", f))
    print("缓存里 Monster = %d 张，Boss = %d 张" % (len(mons), len(bosses)))
    sheet(mons, "_sheet_pixel_monster.png", cols=8, cell=200,
          title="像素怪小人 UT_AccountHeadShot_Monster_01..%d（46/47/48 为本轮补漏）"
                % len(mons))
    sheet(bosses, "_sheet_pixel_boss.png", cols=8, cell=200,
          title="BOSS 族 UT_AccountHeadShot_Boss_*（共 %d 张）" % len(bosses))


if __name__ == "__main__":
    sys.exit(main())
