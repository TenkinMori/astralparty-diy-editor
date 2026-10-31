# -*- coding: utf-8 -*-
"""★ 只读侦察（第三步）：贴图的"实际内容框"和画布尺寸对不对得上。

动机（用户第 9 波 ② 的提示）：
    "如果贴图本身尺寸/裁切不对（例如 512x512 带透明边），画出来就会看起来偏"
所以这里对三类图各算一遍**不透明像素的外接框**（alpha bbox）：
    · 棋盘格子用的 UT_Platform_*.png（260x260）
    · 怪头像 UT_Monster_Bust_<id>_0（1568x900，宽幅半身像）
    · 手牌 UT_HandCard_<id>（404x400）

**只读游戏目录**，结论只写 5-DIY关卡\\tools\\art_bbox_report.txt。
"""
import io
import os
import sys

from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, r"D:\小小兔趴\4-服务端\tools")
try:
    import paths as P
    GAME = P.game_dir()
except Exception:
    GAME = os.environ.get("ASTRAL_GAME_DIR", "")
IMG = os.path.join(GAME, "GameRes", "Img") if GAME else ""
OUT = os.path.join(HERE, "art_bbox_report.txt")


def bbox_note(path):
    im = Image.open(path)
    im.load()
    w, h = im.size
    if im.mode not in ("RGBA", "LA"):
        im = im.convert("RGBA")
    alpha = im.getchannel("A")
    box = alpha.getbbox()
    if not box:
        return w, h, None, "整张全透明"
    l, t, r, b = box
    cx, cy = (l + r) / 2.0, (t + b) / 2.0
    return w, h, box, "内容 %dx%d @(%d,%d) 中心(%d,%d) 画布中心(%d,%d) 偏(%+.1f,%+.1f)" % (
        r - l, b - t, l, t, cx, cy, w / 2.0, h / 2.0, cx - w / 2.0, cy - h / 2.0)


def main():
    out = []

    def say(s):
        out.append(s)

    say("游戏根 = %s" % GAME)
    say("Img = %s（存在 = %s）" % (IMG, os.path.isdir(IMG)))
    if not os.path.isdir(IMG):
        io.open(OUT, "w", encoding="utf-8").write("\n".join(out))
        print("\n".join(out))
        return 2

    for prefix, head in (("UT_Platform_", "① 格子平台贴图（棋盘格子用）"),
                         ("UT_Monster_Bust_", "② 怪头像（棋盘上的怪用）"),
                         ("UT_HandCard_", "③ 手牌（发牌窗口用）")):
        files = sorted(f for f in os.listdir(IMG)
                       if f.startswith(prefix) and f.endswith(".png"))
        say("")
        say("—— %s：%d 张 ——" % (head, len(files)))
        sizes = {}
        offs = []
        for fn in files:
            w, h, box, note = bbox_note(os.path.join(IMG, fn))
            sizes[(w, h)] = sizes.get((w, h), 0) + 1
            if box:
                l, t, r, b = box
                offs.append((fn, (l + r) / 2.0 - w / 2.0, (t + b) / 2.0 - h / 2.0))
            if len(files) <= 60 or fn in files[:8]:
                say("  %-34s %s" % (fn, note))
        say("  画布尺寸分布：%s" % ", ".join("%dx%d ×%d" % (k[0], k[1], v)
                                             for k, v in sorted(sizes.items())))
        if offs:
            xmax = max(offs, key=lambda o: abs(o[1]))
            ymax = max(offs, key=lambda o: abs(o[2]))
            say("  内容中心偏画布中心最多：横向 %s（%+.1f px）、纵向 %s（%+.1f px）"
                % (xmax[0], xmax[1], ymax[0], ymax[2]))
            big = [o for o in offs if abs(o[1]) > 8 or abs(o[2]) > 8]
            say("  |偏移| > 8 px 的张数：%d / %d" % (len(big), len(offs)))

    text = "\n".join(out)
    io.open(OUT, "w", encoding="utf-8").write(text)
    sys.stdout.write(text + "\n")
    return 0


if __name__ == "__main__":
    sys.exit(main())
