# -*- coding: utf-8 -*-
"""★ 只读侦察：怪物像素小人的 PNG **到底有没有自带不透明黑底**。

用户说"怪物底部有个黑底" —— 先分清是我们画的容器，还是图本身烘了黑底。
判据：
  * 四角 / 边缘像素的 **alpha** 与 **RGB**；
  * 整张图 alpha 直方图（有多少像素 alpha==0 / ==255 / 中间）；
  * 把所有非透明像素的平均 RGB 打出来 —— 若"非透明区域"里大量是纯黑，就是烘了黑底。

只读 `贴图缓存\\UT_AccountHeadShot_*.png`，产物落 tools\\_alpha_report.txt。
"""
import io
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(HERE, "pylibs"))
from PIL import Image

CACHE = os.path.join(os.path.dirname(HERE), "贴图缓存")
GAME_IMG = r"D:\steam\steamapps\common\Astral Party\8vJXn6CN\GameRes\Img"
OUT = os.path.join(HERE, "_alpha_report.txt")

LOG = []
def say(s):
    LOG.append(str(s))


def probe(path, tag):
    im = Image.open(path).convert("RGBA")
    w, h = im.size
    px = list(im.getdata())
    n = len(px)
    a0 = sum(1 for p in px if p[3] == 0)
    a255 = sum(1 for p in px if p[3] == 255)
    amax_lt64 = sum(1 for p in px if 0 < p[3] < 64)
    mid = n - a0 - a255 - amax_lt64
    # 非透明像素里"接近纯黑"的占比（黑底烘进去的话这个数会很高）
    op = [p for p in px if p[3] > 128]
    black = sum(1 for p in op if p[0] < 20 and p[1] < 20 and p[2] < 20)
    # 四角 alpha
    corners = [im.getpixel((0, 0)), im.getpixel((w - 1, 0)),
               im.getpixel((0, h - 1)), im.getpixel((w - 1, h - 1))]
    # 最外一圈的 alpha 均值
    edge = []
    for x in range(w):
        edge.append(im.getpixel((x, 0))[3]); edge.append(im.getpixel((x, h - 1))[3])
    for y in range(h):
        edge.append(im.getpixel((0, y))[3]); edge.append(im.getpixel((w - 1, y))[3])
    say("  %-44s %-8s %3dx%-3d alpha0=%5.1f%% alpha255=%5.1f%% 中间=%4.1f%%"
        % (os.path.basename(path), tag, w, h,
           100.0 * a0 / n, 100.0 * a255 / n, 100.0 * mid / n))
    say("        四角 alpha = %s" % (", ".join(str(c[3]) for c in corners)))
    say("        四角 RGB   = %s" % (", ".join("%d,%d,%d" % (c[0], c[1], c[2]) for c in corners)))
    say("        最外一圈 alpha 均值 = %.1f" % (sum(edge) / float(len(edge))))
    say("        非透明(>128)像素里近纯黑占比 = %.1f%%（%d / %d）"
        % (100.0 * black / max(1, len(op)), black, len(op)))


def main():
    say("=" * 92)
    say("① 贴图缓存里的怪物像素小人（Monster_01/11/46 · Boss_01）")
    say("=" * 92)
    for n in ("UT_AccountHeadShot_Monster_01", "UT_AccountHeadShot_Monster_11",
              "UT_AccountHeadShot_Monster_46", "UT_AccountHeadShot_Boss_01"):
        p = os.path.join(CACHE, n + ".png")
        if os.path.exists(p):
            probe(p, "缓存")
        else:
            say("  %s ：缓存里没有" % n)

    say("")
    say("=" * 92)
    say("② 游戏目录里的同一族（对照：口径是否一致）")
    say("=" * 92)
    for n in ("UT_AccountHeadShot_Monster_01", "UT_AccountHeadShot_Monster_46"):
        p = os.path.join(GAME_IMG, n + ".png")
        if os.path.exists(p):
            probe(p, "游戏目录")
        else:
            say("  %s ：游戏目录里没有" % n)

    say("")
    say("=" * 92)
    say("③ 对照：老的半身像（本来就该是透明底的宽幅图）")
    say("=" * 92)
    for n in ("UT_Monster_Bust_1001_0", "UT_Monster_Bust_1068_0"):
        for d, tag in ((CACHE, "缓存"), (GAME_IMG, "游戏目录")):
            p = os.path.join(d, n + ".png")
            if os.path.exists(p):
                probe(p, tag)
                break
        else:
            say("  %s ：两边都没有" % n)

    io.open(OUT, "w", encoding="utf-8").write("\n".join(LOG))
    try:
        sys.stdout.write("\n".join(LOG) + "\n")
    except Exception:
        pass
    return 0


if __name__ == "__main__":
    sys.exit(main())
