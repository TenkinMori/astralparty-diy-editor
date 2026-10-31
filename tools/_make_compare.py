# -*- coding: utf-8 -*-
"""★ 出「本轮改动」的前后对照放大图（照 `_本轮-格子局部放大.png` 那种口径）。

三组：
  ① 棋盘格子（色块底去掉 + 起始点换补给站贴图）
  ② 起始点特写
  ③ 怪物列表 / 详情的小头像（黑底去掉）
只读 `_截图\\` 里的 png，产物写回 `_截图\\`。
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


def strip(before, after, box, scale, out, title, before_tag="改前", after_tag="改后"):
    a = Image.open(os.path.join(SHOT, before)).convert("RGB").crop(box)
    b = Image.open(os.path.join(SHOT, after)).convert("RGB").crop(box)
    w = int(a.width * scale)
    h = int(a.height * scale)
    kind = Image.LANCZOS if scale <= 4 else Image.NEAREST
    a = a.resize((w, h), kind)
    b = b.resize((w, h), kind)

    pad, lab, head = 18, 30, 34
    W = pad + w * 2 + pad * 3
    H = head + lab + h + pad
    sheet = Image.new("RGB", (W, H), (24, 28, 38))
    d = ImageDraw.Draw(sheet)
    d.text((pad, 8), title, fill=(235, 240, 248), font=cjk(17))
    d.text((pad, head), before_tag, fill=(240, 170, 170), font=cjk(15))
    d.text((pad * 2 + w, head), after_tag, fill=(160, 225, 175), font=cjk(15))
    sheet.paste(a, (pad, head + lab))
    sheet.paste(b, (pad * 2 + w, head + lab))
    d.rectangle([pad - 1, head + lab - 1, pad + w, head + lab + h], outline=(90, 98, 112))
    d.rectangle([pad * 2 + w - 1, head + lab - 1, pad * 2 + w * 2, head + lab + h], outline=(90, 98, 112))
    sheet.save(os.path.join(SHOT, out))
    print("wrote", out, sheet.size)


def main():
    b2, a2 = "页2-棋盘_本轮前.png", "页2-棋盘_本轮后.png"
    b4, a4 = "页4-怪物_本轮前3.png", "页4-怪物_本轮后.png"

    # ① 棋盘格子：只裁一小块（几格 + 一格起始点），这样「贴图外面那圈色」看得清
    strip(b2, a2, (300, 330, 620, 640), 2.0,
          "_本轮对照1-棋盘格子.png",
          "① 棋盘格子：去掉「色块底」，格子就是那张圆形贴图本身")

    # ② 起始点特写：补给站描边色 (#3AA0C8) 实测落在左下那个簇 (372,557)/(370,597) 附近，
    #    裁它周围一块，把「改前画绿起始点 / 改后画补给站」的对比框进去。
    strip(b2, a2, (300, 470, 460, 700), 3.0,
          "_本轮对照2-起始点.png",
          "② 起始点特写：改画补给站贴图（起点N 绿徽标保留）")

    # ③ 怪物列表小头像（事件 32122 那一段的圆头像）
    strip(b4, a4, (296, 226, 440, 560), 2.4,
          "_本轮对照3-怪物列表黑底.png",
          "③ 怪物列表：去掉头像的深色容器（原来整圈是黑底）")

    # ④ 怪物详情大头像
    strip(b4, a4, (1050, 258, 1180, 340), 4.0,
          "_本轮对照4-怪物详情黑底.png",
          "④ 怪物详情：去掉深色方底，透明像素图直接落在面板上")

    # ⑤ CellMonsterDialog（改前那张不再单独拍，用 after 展示现状）
    return 0


if __name__ == "__main__":
    sys.exit(main())
