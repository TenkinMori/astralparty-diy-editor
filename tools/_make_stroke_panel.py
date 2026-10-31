# -*- coding: utf-8 -*-
"""★ 出「描边改成正圆」的状态对照图。

从**真实渲染**的截图里，按 `--dump-cells` 给的画布坐标裁出各种状态的格子，
放大后排成一张"状态一览"，让人一眼看出描边是正圆、颜色粗细与原来一致、起点N 徽标还在。

画布坐标 → 截图坐标：棋盘画布左上角在截图上的偏移是实测出来的
（用"起始点格"的绿色圆环质心标定，见 OFFSET 注释）。
"""
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(HERE, "pylibs"))
from PIL import Image, ImageDraw, ImageFont

SHOT = os.path.join(os.path.dirname(HERE), "_截图")


def cjk(size):
    for n in ("msyh.ttc", "msyhbd.ttc", "simhei.ttf"):
        p = os.path.join(os.environ.get("WINDIR", r"C:\Windows"), "Fonts", n)
        if os.path.exists(p):
            try:
                return ImageFont.truetype(p, size)
            except Exception:
                pass
    return ImageFont.load_default()


# 截图文件名 → 各状态下要裁的 (画布中心x, 画布中心y, 标签)
# 画布原点在截图上约 (258, 315)；下一行由标定确认为 (258, 315)。
OX, OY = 258.0, 315.0


def canv2shot(cx, cy):
    return (OX + cx, OY + cy)


def main():
    board = "页2-棋盘_圆环.png"       # 选中 18 + 悬停 20
    plain = "页2-棋盘_本轮后.png"      # 什么都不选

    imgs = {k: Image.open(os.path.join(SHOT, v)).convert("RGB")
            for k, v in (("state", board), ("plain", plain))}

    # (标签, 用哪张图, 画布中心x, 画布中心y, 半宽, 说明色)
    items = [
        ("起点格（补给站贴图+绿环）", "plain", 662.3, 250.0, 40, (120, 200, 160)),
        ("选中（Accent 圆环）", "state", 662.3, 250.0, 40, (143, 216, 255)),
        ("悬停（Accent 圆环，稍大）", "state", 662.3, 354.3, 44, (143, 216, 255)),
        ("改过（Wait 圆环，加粗）", "state", 129.7, 250.0, 40, (240, 200, 120)),
        ("保障点（landType 2 圆环）", "state", 395.1, 45.0, 40, (58, 160, 200)),
        ("普通格（landType 0 默认圆环）", "state", 129.7, 509.0, 40, (200, 200, 200)),
    ]

    CELL = 150           # 每格画到多大
    PAD = 16
    LAB = 26
    HEAD = 40
    COLS = 3
    ROWS = (len(items) + COLS - 1) // COLS
    W = PAD + COLS * (CELL + PAD)
    H = HEAD + ROWS * (LAB + CELL + PAD)
    sheet = Image.new("RGB", (W, H), (22, 26, 36))
    d = ImageDraw.Draw(sheet)
    d.text((PAD, 10), "格子状态描边：全部改成正圆（放大对照）", fill=(235, 240, 248), font=cjk(18))

    for i, (label, key, cx, cy, half, col) in enumerate(items):
        sx, sy = canv2shot(cx, cy)
        box = (int(sx - half), int(sy - half), int(sx + half), int(sy + half))
        crop = imgs[key].crop(box).resize((CELL, CELL), Image.LANCZOS)
        r, c = divmod(i, COLS)
        x = PAD + c * (CELL + PAD)
        y = HEAD + r * (LAB + CELL + PAD)
        d.text((x, y), label, fill=col, font=cjk(14))
        sheet.paste(crop, (x, y + LAB))
        d.rectangle([x - 1, y + LAB - 1, x + CELL, y + LAB + CELL], outline=(80, 88, 104))

    sheet.save(os.path.join(SHOT, "_本轮对照5-描边正圆状态一览.png"))
    print("wrote _本轮对照5-描边正圆状态一览.png", sheet.size)
    return 0


if __name__ == "__main__":
    sys.exit(main())
