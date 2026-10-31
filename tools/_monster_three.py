# -*- coding: utf-8 -*-
"""★ 只读：把补漏的三只像素怪小人（Monster_46/47/48）从 GameRes 落地，
并把 GameRes 这一族与「bundle 里那份」做逐像素对照，确认是同一族同一张。

背景（上一轮为什么漏）：
  `tools\\_tex_inv.tsv` 是**Addressables 包里的 Texture2D 清单**，
  而 `GameRes\\Img\\` 是**成品 PNG 直放目录**（另一条平行路径）。
  上一轮只在 bundle 清单里 grep，所以把「包里有登记、但实体只存在于 GameRes」的
  46/47/48 判成了"本地没有" —— 实际它们在 GameRes 里躺着。

只读游戏目录；只往 贴图缓存\\ 写图（46/47/48 为原字节直拷）。
"""
import io
import os
import re
import sys
import hashlib

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(HERE, "pylibs"))

GAME = r"D:\steam\steamapps\common\Astral Party\8vJXn6CN"
GRES = os.path.join(GAME, "GameRes", "Img")
CACHE = os.path.join(os.path.dirname(HERE), "贴图缓存")
REPORT = os.path.join(HERE, "_monster_three_report.txt")
PAT = re.compile(r"^UT_AccountHeadShot_Monster_(\d+)$")
LOG = []


def say(s):
    LOG.append(str(s))
    print(s)
    sys.stdout.flush()


def main():
    from PIL import Image, ImageChops

    # ---- ① GameRes 里这一族的全量 ----
    gres = {}
    for fn in sorted(os.listdir(GRES)):
        stem, ext = os.path.splitext(fn)
        if not PAT.match(stem):
            continue
        gres[int(PAT.match(stem).group(1))] = (fn, os.path.getsize(os.path.join(GRES, fn)))
    say("=" * 78)
    say("① GameRes\\Img 里 UT_AccountHeadShot_Monster_* 全量 = %d 个" % len(gres))
    say("=" * 78)
    idxs = sorted(gres)
    say("   序号：%s" % ",".join("%02d" % i for i in idxs))
    say("   连续区间内的缺口：%s"
        % (",".join("%02d" % i for i in range(min(idxs), max(idxs) + 1)
                    if i not in gres) or "无"))
    say("")

    # ---- ② 缓存里已有的（上一轮从 bundle 抽的）----
    have = {}
    for fn in sorted(os.listdir(CACHE)):
        stem, ext = os.path.splitext(fn)
        if PAT.match(stem) and ext.lower() == ".png":
            have[int(PAT.match(stem).group(1))] = fn
    say("② 贴图缓存里已有（上一轮从 bundle 抽的）= %d 个" % len(have))
    say("   缺的：%s" % (",".join("%02d" % i for i in idxs if i not in have) or "无"))
    say("")

    # ---- ③ 逐像素对照：bundle 那份 vs GameRes 那份 ----
    say("=" * 78)
    say("③ 逐像素对照（bundle 抽的 vs GameRes 的，同一序号）")
    say("=" * 78)
    same = diff = 0
    for i in sorted(set(have) & set(gres)):
        a = Image.open(os.path.join(CACHE, have[i])).convert("RGBA")
        b = Image.open(os.path.join(GRES, gres[i][0])).convert("RGBA")
        if a.size != b.size:
            say("   %02d 尺寸不同：缓存 %s vs GameRes %s" % (i, a.size, b.size))
            diff += 1
            continue
        bbox = ImageChops.difference(a, b).getbbox()
        if bbox is None:
            same += 1
        else:
            diff += 1
            say("   %02d 像素有差异，bbox=%s" % (i, bbox))
    say("   逐像素完全相同 = %d，有差异 = %d" % (same, diff))
    say("   ⇒ %s" % ("两条路径是同一张图，只是 PNG 编码不同"
                    if diff == 0 else "存在差异，需人工确认"))
    say("")

    # ---- ④ 落地补漏的三只（原字节直拷）----
    say("=" * 78)
    say("④ 补漏落地（GameRes 原字节直拷）")
    say("=" * 78)
    for i in sorted(idxs):
        if i in have:
            continue
        src = os.path.join(GRES, gres[i][0])
        dst = os.path.join(CACHE, gres[i][0])
        raw = io.open(src, "rb").read()
        io.open(dst, "wb").write(raw)
        h_src = hashlib.sha256(raw).hexdigest()
        h_dst = hashlib.sha256(io.open(dst, "rb").read()).hexdigest()
        im = Image.open(dst)
        say("   %-40s %dx%d  %d 字节  sha256 一致=%s"
            % (gres[i][0], im.width, im.height, len(raw), h_src == h_dst))
        say("        源：%s" % src)
        say("        落地：%s" % dst)
    say("")

    io.open(REPORT, "w", encoding="utf-8").write("\n".join(LOG) + "\n")
    say("报告：%s" % REPORT)
    return 0


if __name__ == "__main__":
    sys.exit(main())
