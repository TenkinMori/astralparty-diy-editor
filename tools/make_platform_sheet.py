# -*- coding: utf-8 -*-
"""只读：把 GameRes\\Img 里所有 UT_Platform_*.png 拼成一张盘点图（带文件名），方便人眼认。

用法：python make_platform_sheet.py 输出.png
"""
import os
import sys

from PIL import Image, ImageDraw, ImageFont

IMG = r"D:\steam\steamapps\common\Astral Party\8vJXn6CN\GameRes\Img"
CELL = 150
PAD = 8
LABEL = 22
COLS = 7


def main():
    out = sys.argv[1] if len(sys.argv) > 1 else r"D:\小小兔趴\5-DIY关卡\_截图\_盘点_平台贴图.png"
    names = sorted(fn for fn in os.listdir(IMG)
                   if fn.startswith("UT_Platform_") and fn.lower().endswith(".png"))
    rows = (len(names) + COLS - 1) // COLS
    W = COLS * (CELL + PAD) + PAD
    H = rows * (CELL + PAD + LABEL) + PAD
    sheet = Image.new("RGB", (W, H), (24, 28, 38))
    d = ImageDraw.Draw(sheet)
    try:
        font = ImageFont.truetype("C:/Windows/Fonts/msyh.ttc", 13)
    except Exception:
        font = ImageFont.load_default()

    for i, fn in enumerate(names):
        r, c = divmod(i, COLS)
        x = PAD + c * (CELL + PAD)
        y = PAD + r * (CELL + PAD + LABEL)
        try:
            im = Image.open(os.path.join(IMG, fn)).convert("RGBA")
        except Exception:
            continue
        im.thumbnail((CELL, CELL), Image.LANCZOS)
        bg = Image.new("RGBA", (CELL, CELL), (12, 15, 22, 255))
        bg.alpha_composite(im, ((CELL - im.width) // 2, (CELL - im.height) // 2))
        sheet.paste(bg.convert("RGB"), (x, y))
        d.rectangle([x, y, x + CELL, y + CELL], outline=(70, 80, 100))
        label = fn[len("UT_Platform_"):-len(".png")]
        d.text((x + 3, y + CELL + 3), "%d %s" % (i + 1, label), fill=(220, 226, 240), font=font)

    sheet.save(out)
    print("写出 %s  %dx%d  %d 张" % (out, W, H, len(names)))
    for i, fn in enumerate(names):
        print("  %2d %s" % (i + 1, fn))
    return 0


if __name__ == "__main__":
    sys.exit(main())
