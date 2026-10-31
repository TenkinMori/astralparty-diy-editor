# -*- coding: utf-8 -*-
"""★ 只读侦察：格子贴图 UT_Platform_* 的四角到底有没有"溢出的色块"。

判据分两档：
  · 外圈（最外 0..3% 的边角）—— 用户说的"四个角溢出的色块"就在这儿；
  · 内圈（12% 的角区）—— 格子里那圈装饰。

alpha 均值高 / 不透明像素占比高 ⇒ 方形贴图带角落内容 ⇒ 需要圆形裁剪。
"""
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(HERE, "pylibs"))
from PIL import Image

GAME = r"D:\steam\steamapps\common\Astral Party\8vJXn6CN"
DIRS = [(os.path.join(GAME, "GameRes", "Img"), "游戏目录"),
        (os.path.join(os.path.dirname(HERE), "贴图缓存"), "贴图缓存")]
OUT = os.path.join(HERE, "_platform_corner_report.txt")

LOG = []


def say(s):
    LOG.append(str(s))


def ring_stats(im, frac):
    """四个角各取 frac×frac 的小方块 → (alpha 均值, 不透明占比)。"""
    im = im.convert("RGBA")
    w, h = im.size
    cw, ch = max(1, int(round(w * frac))), max(1, int(round(h * frac)))
    out = []
    for bx, by in ((0, 0), (w - cw, 0), (0, h - ch), (w - cw, h - ch)):
        px = list(im.crop((bx, by, bx + cw, by + ch)).getdata())
        n = float(len(px))
        a = sum(p[3] for p in px) / n
        op = sum(1 for p in px if p[3] > 32) / n
        out.append((a, op))
    return out


def main():
    say("=" * 96)
    say("格子贴图四角侦察：外圈=最外 3% 边角（用户说的'溢出色块'），内圈=12% 角区")
    say("=" * 96)
    say("%-32s %-8s %-10s %-30s %-30s"
        % ("文件", "来源", "尺寸", "外圈3% alpha/不透明", "内圈12% alpha/不透明"))
    rows = []
    for d, tag in DIRS:
        if not os.path.isdir(d):
            continue
        for fn in sorted(os.listdir(d)):
            if not fn.startswith("UT_Platform") or not fn.lower().endswith(".png"):
                continue
            try:
                im = Image.open(os.path.join(d, fn))
                w, h = im.size
            except Exception:
                continue
            outer = ring_stats(im, 0.03)
            inner = ring_stats(im, 0.12)
            rows.append((fn, tag, w, h, outer, inner))
            say("%-32s %-8s %-10s %-30s %-30s"
                % (fn, tag, "%dx%d" % (w, h),
                   " ".join("%5.1f/%.2f" % (a, o) for a, o in outer),
                   " ".join("%5.1f/%.2f" % (a, o) for a, o in inner)))

    say("")
    say("—— 结论 ——")
    if not rows:
        say("  没扫到任何 UT_Platform_*.png")
    else:
        bad = [r for r in rows if max(a for a, _ in r[4]) > 40]
        mid = [r for r in rows if 8 < max(a for a, _ in r[4]) <= 40]
        good = [r for r in rows if max(a for a, _ in r[4]) <= 8]
        say("  外圈四角明显有色（alpha 均值 > 40）= %d 张" % len(bad))
        for r in bad:
            say("      " + r[0])
        say("  外圈四角轻微有内容（8..40）= %d 张" % len(mid))
        for r in mid:
            say("      " + r[0])
        say("  外圈四角本来就透明（<= 8）= %d 张 "
            "⇒ 贴图自带 alpha，四个角本来就不会溢出色块" % len(good))
        for r in good:
            say("      " + r[0])

    # ★ 先把报告落盘，再尝试打屏（这台机器控制台是 GBK，箭头字符会炸）
    import io as _io
    _io.open(OUT, "w", encoding="utf-8").write("\n".join(LOG))
    try:
        sys.stdout.write("\n".join(LOG[-12:]) + "\n")
    except Exception:
        pass
    sys.stdout.write("\nreport: " + OUT + "\n")


if __name__ == "__main__":
    main()
