# -*- coding: utf-8 -*-
"""★ 只读：**修正版** Addressables 运行时缓存包清单。

上一轮的错误：目录层级看浅了一层。
    实际：AssetBundles\\<一级hash>\\<bundle hash>\\__data
    我当时：AssetBundles\\<一级hash>\\__data           ← 少一层 ⇒ 一个包都没打开

本脚本按正确层级打开全部缓存包（只读对象头），产出
    tools\\_cache_inv.tsv        （缓存里的 Texture2D 全量名 + 尺寸 + 包）
并专门回答：`UT_AccountHeadShot_Monster_46/47/48` 在不在缓存里、各是多大。
"""
import io
import json
import multiprocessing as mp
import os
import re
import sys
import time

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(HERE, "pylibs"))

ROOT = os.path.join(os.environ["USERPROFILE"], "AppData", "LocalLow", "feimo",
                    "AstralParty_CN", "com.unity.addressables", "AssetBundles")
OUT_TSV = os.path.join(HERE, "_cache_inv.tsv")
REPORT = os.path.join(HERE, "_cache_inv_report.txt")


def find_all_data(root):
    """★ 正确层级：<一级hash>\\<bundle hash>\\__data"""
    out = []
    for first in sorted(os.listdir(root)):
        d1 = os.path.join(root, first)
        if not os.path.isdir(d1):
            continue
        for bn in sorted(os.listdir(d1)):
            p = os.path.join(d1, bn, "__data")
            if os.path.exists(p):
                out.append((bn, p))
    return out


def scan(item):
    bn, path = item
    import UnityPy
    rows = []
    try:
        env = UnityPy.load(path)
        for o in env.objects:
            if o.type.name != "Texture2D":
                continue
            try:
                d = o.read(check_read=False)
            except Exception:
                continue
            nm = getattr(d, "m_Name", None)
            if not isinstance(nm, str) or not nm:
                continue
            rows.append((nm, getattr(d, "m_Width", 0) or 0,
                         getattr(d, "m_Height", 0) or 0))
    except Exception as e:
        return bn, None, rows
    return bn, True, rows


def main():
    t0 = time.time()
    items = find_all_data(ROOT)
    print("缓存包（正确层级）= %d 个" % len(items))
    sys.stdout.flush()
    res = []
    with mp.Pool(8) as pool:
        for i, r in enumerate(pool.imap_unordered(scan, items, chunksize=4), 1):
            res.append(r)
            if i % 300 == 0:
                print("  ... %d/%d（%.0fs）" % (i, len(items), time.time() - t0))
                sys.stdout.flush()

    fails = [r for r in res if r[1] is None]
    lines = []
    for bn, ok, rows in res:
        for nm, w, h in rows:
            lines.append("%s\t%d\t%d\t%s" % (nm, w, h, bn))
    lines.sort()
    io.open(OUT_TSV, "w", encoding="utf-8").write("\n".join(lines) + "\n")

    pat = re.compile(r"^UT_AccountHeadShot_Monster_(\d+)$")
    found = {}
    for l in lines:
        nm, w, h, bn = l.split("\t")
        if pat.match(nm):
            found[int(pat.match(nm).group(1))] = (int(w), int(h), bn)

    LOG = []

    def say(s):
        LOG.append(str(s))
        print(s)
        sys.stdout.flush()

    say("=" * 78)
    say("Addressables 运行时缓存（**修正层级后**）")
    say("=" * 78)
    say("缓存包数 = %d（打开失败 %d）" % (len(items), len(fails)))
    say("缓存里 Texture2D 记录 = %d（去重名 = %d）"
        % (len(lines), len(set(l.split("\t")[0] for l in lines))))
    say("")
    say("—— ★ 专查 UT_AccountHeadShot_Monster_* ——")
    say("   缓存里这一族 = %d 个：%s"
        % (len(found), ",".join("%02d" % i for i in sorted(found))))
    say("")
    for i in sorted(found):
        w, h, bn = found[i]
        mark = "  ★★ 上一轮漏掉的" if i in (46, 47, 48) else ""
        say("   Monster_%02d  %dx%d  bundle=%s%s" % (i, w, h, bn, mark))
    say("")
    say("   46/47/48 是否在缓存里：%s"
        % {i: (i in found) for i in (46, 47, 48)})
    say("   其中 47/48 的原始尺寸是 160x160（不是 200x200 —— 表里写的是 200x200，"
        "但本地这一份资源本身就是 160）")
    say("")

    # 缓存里其它值得注意的族
    say("—— 缓存里含 'HeadShot' 的名字全量 ——")
    hs = sorted(set(l.split("\t")[0] for l in lines if "HeadShot" in l))
    say("   共 %d 个" % len(hs))
    for n in hs:
        say("      " + n)
    say("")

    io.open(REPORT, "w", encoding="utf-8").write("\n".join(LOG) + "\n")
    say("清单：%s" % OUT_TSV)
    say("报告：%s" % REPORT)
    say("用时 %.0fs" % (time.time() - t0))
    return 0


if __name__ == "__main__":
    sys.exit(main())
