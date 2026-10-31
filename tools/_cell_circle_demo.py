# -*- coding: utf-8 -*-
"""★ 造两张对照图，说明「格子四个角溢出色块」是怎么来的、裁圆之后什么样。

在编辑器里那一格是这样叠的（PageBoard.xaml.cs Draw() ③）：
    Border{ Background = landType 的色块(方) ; BorderBrush = 该色 ; BorderThickness }
      └── Image{ UT_Platform_xxx (圆内容 + 四角透明) }

所以「色块」在圆贴图之外、方框之内露出来 —— 就是四个角。
右边那张是把整个 Border 一起 Clip 成正圆之后的实际观感。

产物：_截图\_格子裁圆对照.png（只写我们自己的目录）
"""
import io
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(HERE, "pylibs"))
from PIL import Image, ImageDraw, ImageFont


def cjk_font(size):
    """找一个能画中文的字体（这台机器上总有 msyh/simhei）。"""
    for name in ("msyh.ttc", "msyhbd.ttc", "simhei.ttf", "simsun.ttc"):
        p = os.path.join(os.environ.get("WINDIR", r"C:\Windows"),
                         "Fonts", name)
        if os.path.exists(p):
            try:
                return ImageFont.truetype(p, size)
            except Exception:
                pass
    return ImageFont.load_default()

GAME = r"D:\steam\steamapps\common\Astral Party\8vJXn6CN"
IMG = os.path.join(GAME, "GameRes", "Img")
SHOT = os.path.join(os.path.dirname(HERE), "_截图")
OUT = os.path.join(SHOT, "_格子裁圆对照.png")

# 棋盘页默认档位下的实际格距（--celltest 报的 90/120/144/170 px）
NODE = 120
COLS = 4
BG = (11, 16, 26, 255)

# 取几张有代表性的平台贴图 + 编辑器回落时用的色
CASES = [
    ("UT_Platform_Start.png", (99, 230, 168)),
    ("UT_Platform_Monster.png", (232, 96, 110)),
    ("UT_Platform_Shop.png", (240, 190, 90)),
    ("UT_Platform_Teleport.png", (120, 160, 245)),
]


def load_platform(fn):
    p = os.path.join(IMG, fn)
    im = Image.open(p).convert("RGBA")
    return im.resize((NODE, NODE), Image.LANCZOS)


def main():
    os.makedirs(SHOT, exist_ok=True)
    pad = 26
    head_h = 30
    label_h = 28
    cell = NODE
    W = pad + COLS * (cell + pad)
    H = head_h + 2 * (label_h + cell + pad) + pad
    sheet = Image.new("RGBA", (W, H), BG)
    d = ImageDraw.Draw(sheet)
    d.text((pad, 7), "棋盘格子：四个角为什么有色块 / 裁圆之后", fill=(230, 236, 245, 255),
           font=cjk_font(17))

    rows = [("改前：方形色块打底 + 圆形贴图 ⇒ 四角露出色块", True),
            ("改后：整格 Clip 成正圆 ⇒ 四角干净（贴图/描边/编号都还在）", False)]

    for ri, (title, square) in enumerate(rows):
        y0 = head_h + ri * (label_h + cell + pad)
        d.text((pad, y0 + 6), title, fill=(210, 220, 235, 255), font=cjk_font(14))
        y0 += label_h
        for ci, (fn, rgb) in enumerate(CASES):
            x0 = pad + ci * (cell + pad)
            layer = Image.new("RGBA", (cell, cell), (0, 0, 0, 0))

            # ① Border 的方底色（bug 的根源）
            border = Image.new("RGBA", (cell, cell), rgb + (255,))
            # ② 贴上圆内容 + 四角透明的平台贴图（编辑器用的是 Uniform，尺寸一样就 1:1）
            plat = load_platform(fn)
            border.alpha_composite(plat)
            layer.alpha_composite(border)

            if not square:
                # ③ 改后：整格裁成正圆
                mask = Image.new("L", (cell, cell), 0)
                ImageDraw.Draw(mask).ellipse((0, 0, cell - 1, cell - 1), fill=255)
                layer.putalpha(Image.composite(layer.getchannel("A"),
                                               Image.new("L", (cell, cell), 0), mask))

            sheet.alpha_composite(layer, (x0, y0))

    sheet.convert("RGB").save(OUT)
    print("wrote " + OUT)
    try:
        sys.stdout.write("ok\n")
    except Exception:
        pass


if __name__ == "__main__":
    main()
