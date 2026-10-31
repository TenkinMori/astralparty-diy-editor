# -*- coding: utf-8 -*-
"""★ 只读：把「安装目录 5500 包」+「运行时下载缓存 1149 包」+「GameRes\\Img」三条路
合起来，给出像素怪小人 01..48 的**完整来源表**，并更新落地清单。

同时把 47/48 的尺寸疑点讲清楚：
  Monster.bin 登记到 48；安装包里只有 01..45；
  缓存包补上 46(200x200) / 47(160x160) / 48(160x160)；
  GameRes\\Img 也有 43 个（缺 29/30/36/38/39），且不含 46 之外的 47/48 的 200x200 版本。
"""
import io
import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
GD = r"D:\小小兔趴\4-服务端\server\data\gamedata"
CACHE = os.path.join(os.path.dirname(HERE), "贴图缓存")
GRES = r"D:\steam\steamapps\common\Astral Party\8vJXn6CN\GameRes\Img"
INSTALL_TSV = os.path.join(HERE, "_tex_inv.tsv")
RUNTIME_TSV = os.path.join(HERE, "_cache_inv.tsv")

from _map_images2 import parse_fields, unwrap  # noqa: E402

PAT = re.compile(r"^UT_AccountHeadShot_Monster_(\d+)$")
REPORT = os.path.join(HERE, "_monster48_report.txt")


def tx(v):
    try:
        t = v.decode("utf-8")
    except Exception:
        return None
    return t if t and all(32 <= ord(c) for c in t) and len(t) < 200 else None


def load(tsv):
    d = {}
    if not os.path.exists(tsv):
        return d
    for l in io.open(tsv, encoding="utf-8"):
        p = l.rstrip("\n").split("\t")
        if len(p) < 4:
            continue
        nm = p[0]
        if PAT.match(nm):
            # install tsv: name w h fmt bundle ; runtime tsv: name w h bundle
            d.setdefault(int(PAT.match(nm).group(1)), (int(p[1]), int(p[2]), p[-1]))
    return d


def main():
    inst = load(INSTALL_TSV)
    run = load(RUNTIME_TSV)
    gres = {}
    for fn in sorted(os.listdir(GRES)):
        s = os.path.splitext(fn)[0]
        if PAT.match(s):
            gres[int(PAT.match(s).group(1))] = fn

    # 怪物表：序号 -> [(怪id, 名字)]
    names = {}
    for k, b in unwrap(io.open(os.path.join(GD, "STRMonster.bin"), "rb").read(), 2):
        for n2, w2, v2 in parse_fields(b or b""):
            if n2 == 2 and w2 == 2:
                t = tx(v2)
                if t:
                    names[k] = t
                break
    who = {}
    for k, b in unwrap(io.open(os.path.join(GD, "Monster.bin"), "rb").read(), 2):
        nid = head = None
        for n2, w2, v2 in parse_fields(b or b""):
            if n2 == 4 and w2 == 5:
                nid = v2
            elif n2 == 19 and w2 == 2:
                head = tx(v2)
        if head and PAT.match(head):
            who.setdefault(int(PAT.match(head).group(1)), []).append(
                (k, names.get(nid, "?")))

    LOG = []

    def say(s):
        LOG.append(str(s))
        print(s)
        sys.stdout.flush()

    say("=" * 96)
    say("像素怪小人 01..48 完整来源表")
    say("=" * 96)
    say("%-4s %-14s %-14s %-16s %-16s %s"
        % ("序", "怪id", "名字", "安装包5500", "运行时缓存1149", "GameRes\\Img"))
    say("-" * 96)
    missing = []
    for i in range(1, 49):
        ids = ",".join(str(k) for k, _ in who.get(i, [])) or "-"
        nm = " / ".join(n for _, n in who.get(i, [])) or "?"
        a = ("%dx%d" % (inst[i][0], inst[i][1])) if i in inst else "-"
        b = ("%dx%d" % (run[i][0], run[i][1])) if i in run else "-"
        c = "有" if i in gres else "-"
        say("%-4d %-14s %-14s %-16s %-16s %s" % (i, ids, nm[:12], a, b, c))
        if not (i in inst or i in run or i in gres):
            missing.append(i)
    say("")
    say("三条路都没有的：%s" % (missing or "无"))
    say("★ 安装包（5500 个 .bundle，本次全量扫过）里只有 01..45")
    say("★ 运行时下载缓存（1149 个包，**修正层级后**重扫）里正好是 46/47/48")
    say("★ 两条路合起来 = 01..48 全齐；GameRes\\Img 另有 43 个（缺 29/30/36/38/39）")
    say("")
    say("尺寸疑点：Monster.bin 对 47/48 登记的名字与其他一样，但本地缓存包里")
    say("          这两张 Texture2D 本身就是 160x160（m_Width=160）；")
    say("          46 是 200x200。同一族里 47/48 是 160 这一档，不是我们抽错。")
    say("")

    # ---- 更新落地清单 ----
    lines = ["# 「图鉴」里那套像素风怪物小人 —— 落地清单（01..48 全齐）",
             "#",
             "# 源（三条路，按优先级）",
             "#   ① 安装目录 Addressables 包：AstralParty_CN_Data\\StreamingAssets\\aa\\StandaloneWindows64\\<hash>.bundle",
             "#   ② 运行时下载缓存：      %LocalLow%\\feimo\\AstralParty_CN\\com.unity.addressables\\AssetBundles\\<h1>\\<hash>\\__data",
             "#   ③ 成品 PNG 直放：       GameRes\\Img\\<资产名>.png",
             "# 与怪物 id / 名字的对应：Monster.bin field 19（头像名）+ field 4（名字 id） -> STRMonster.bin field 2",
             "# 落地目录：D:\\小小兔趴\\5-DIY关卡\\贴图缓存\\",
             "#",
             "# 序号\t怪id\t中文名\t尺寸\t来源\t落地文件"]
    for i in range(1, 49):
        ids = ",".join(str(k) for k, _ in who.get(i, [])) or "-"
        nm = " / ".join(n for _, n in who.get(i, [])) or "?"
        if i in inst:
            src, wh = "安装包", "%dx%d" % (inst[i][0], inst[i][1])
        elif i in run:
            src, wh = "运行时缓存", "%dx%d" % (run[i][0], run[i][1])
        elif i in gres:
            src, wh = "GameRes", "?"
        else:
            src, wh = "-", "-"
        fn = "UT_AccountHeadShot_Monster_%02d.png" % i
        have = "已落地" if os.path.exists(os.path.join(CACHE, fn)) else "缺"
        lines.append("%d\t%s\t%s\t%s\t%s\t%s\t%s"
                     % (i, ids, nm, wh, src, fn, have))
    man = os.path.join(CACHE, "像素怪物小人清单.txt")
    io.open(man, "w", encoding="utf-8").write("\n".join(lines) + "\n")
    say("清单已更新：%s" % man)
    io.open(REPORT, "w", encoding="utf-8").write("\n".join(LOG) + "\n")
    return 0


if __name__ == "__main__":
    sys.exit(main())
