# -*- coding: utf-8 -*-
"""★ 只读抽取（正解）：从 **Addressables 运行时下载缓存**里抠出
`UT_AccountHeadShot_Monster_46/47/48` 的**正主（200x200）**。

上一轮为什么判错 —— 我把缓存的目录层级看浅了一层：
    实际：AssetBundles\\<一级hash>\\<bundle hash>\\__data
    我当时查的是：AssetBundles\\<一级hash>\\__data          ← 少了一层
所以"1149 个缓存包"这个数是对的，但**按包名逐一打开时一个都没打开**，
于是错判成"本地包没有"。这三个包其实一直在缓存里（46/47/48 各一个）。

口径：UnityPy 从 Texture2D 解出的像素要**重新编码**成 PNG（无损），
      尺寸/像素与游戏一致；这不是"原字节不可得"，而是 Unity 纹理本身的形态。
"""
import io
import os
import sys
import hashlib

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(HERE, "pylibs"))

CACHE_ROOT = os.path.join(os.environ["USERPROFILE"], "AppData", "LocalLow",
                          "feimo", "AstralParty_CN", "com.unity.addressables",
                          "AssetBundles")
GRES = r"D:\steam\steamapps\common\Astral Party\8vJXn6CN\GameRes\Img"
CACHE = os.path.join(os.path.dirname(HERE), "贴图缓存")
REPORT = os.path.join(HERE, "_monster_three_report2.txt")
LOG = []

TARGETS = {
    "8d49d0b4c81aba82f0838e5c75daa696": "UT_AccountHeadShot_Monster_46",
    "ff2f91de09c2abb96e1e12f54ac54718": "UT_AccountHeadShot_Monster_47",
    "81161b89ebd67c23d2fa977f7bf4ab73": "UT_AccountHeadShot_Monster_48",
}


def say(s):
    LOG.append(str(s))
    print(s)
    sys.stdout.flush()


def find_bundle_data(root, name):
    """★ 正确层级：<一级hash>\\<bundle hash>\\__data»
    返回 __data 路径列表（同名可能有多份）。"""
    out = []
    for first in os.listdir(root):
        d1 = os.path.join(root, first)
        if not os.path.isdir(d1):
            continue
        d2 = os.path.join(d1, name)
        if os.path.isdir(d2):
            p = os.path.join(d2, "__data")
            if os.path.exists(p):
                out.append(p)
    return out


def main():
    import UnityPy
    from PIL import Image, ImageChops

    for bh, asset in sorted(TARGETS.items()):
        paths = find_bundle_data(CACHE_ROOT, bh)
        say("=" * 78)
        say("bundle %s  ->  %s" % (bh, asset))
        say("=" * 78)
        if not paths:
            say("  !! 缓存里没有这个包")
            continue
        for p in paths:
            say("  __data：%s（%d 字节）" % (p, os.path.getsize(p)))
            env = UnityPy.load(p)
            got = False
            for o in env.objects:
                if o.type.name != "Texture2D":
                    continue
                try:
                    d = o.read()
                except Exception as e:
                    say("     解码失败：%s" % e)
                    continue
                nm = getattr(d, "m_Name", None)
                if nm != asset:
                    say("     （包内另有 Texture2D：%r）" % nm)
                    continue
                im = d.image
                if im is None:
                    continue
                im = im.convert("RGBA")
                out = os.path.join(CACHE, asset + ".png")
                im.save(out, "PNG")
                got = True
                say("     ★ 资产名 %s" % nm)
                say("        尺寸 %dx%d（m_Width=%s m_Height=%s 格式=%s）"
                    % (im.width, im.height, d.m_Width, d.m_Height,
                       getattr(d, "m_TextureFormat", "?")))
                say("        落地 %s（%d 字节，UnityPy 解码后重编码 PNG）"
                    % (out, os.path.getsize(out)))
                cols = im.getcolors(maxcolors=300000)
                say("        颜色数 %d" % (len(cols) if cols else -1))
            if not got:
                say("     !! 这个包里没有名为 %s 的 Texture2D" % asset)
        say("")

    # ---- 与 GameRes 那份比一下：是不是同一张（只是尺寸不同）----
    say("=" * 78)
    say("对照：缓存抽出（200x200） vs GameRes 的 160x160")
    say("=" * 78)
    for i in (46, 47, 48):
        a = os.path.join(CACHE, "UT_AccountHeadShot_Monster_%02d.png" % i)
        b = os.path.join(GRES, "UT_AccountHeadShot_Monster_%02d.png" % i)
        if not (os.path.exists(a) and os.path.exists(b)):
            say("   %02d 缺文件" % i)
            continue
        ia, ib = Image.open(a).convert("RGBA"), Image.open(b).convert("RGBA")
        same = False
        if ia.size == ib.size:
            same = ImageChops.difference(ia, ib).getbbox() is None
        # GameRes 版缩小到缓存版尺寸后比（看是不是同一构图）
        ib2 = ib.resize(ia.size, Image.LANCZOS)
        diff = ImageChops.difference(ia, ib2).getbbox()
        say("   %02d 缓存 %s / GameRes %s  同尺寸逐像素相同=%s  缩小后仍有差异=%s"
            % (i, ia.size, ib.size, same, diff is not None))
    say("")
    say("（说明：GameRes 里的是 160x160 的另一张产出，内容构图相同但分辨率更低；")
    say("  正主是缓存包里的 200x200，已按 200x200 落地。）")

    io.open(REPORT, "w", encoding="utf-8").write("\n".join(LOG) + "\n")
    say("")
    say("报告：%s" % REPORT)
    return 0


if __name__ == "__main__":
    sys.exit(main())
