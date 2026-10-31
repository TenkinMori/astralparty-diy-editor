# -*- coding: utf-8 -*-
"""★ 只读侦察（合并两件事）：

第一节 「异变图书馆」(地图 82016 / 画板 023) 的官方美术图
      —— 走 catalog 的地图族 key（Map_023 / MiniMap_023 / UT_MapScene_021 /
         GuidePic_82016xx …）在**整包**里找 Texture2D / Sprite / SpriteAtlas。

第二节 「图鉴」里那套像素风怪物小人
      —— 上一轮只读了 Texture2D 的 m_Name，**图集里的 Sprite 名是另一套**，
         这轮把**所有对象类型**的名字都读出来（Sprite / SpriteAtlas /
         MonoBehaviour / GameObject / Texture2D / RectTransform …），
         并解析 Sprite 的矩形 + 所属图集，再按像素签名（小尺寸 / 少颜色）判定。

做法：多进程只读打开 StreamingAssets 下全部 .bundle，**只读对象头**（check_read=False），
      不解包到磁盘、不写游戏目录。产物落 tools\\ 下的报告 + JSON。
"""
import io
import json
import multiprocessing as mp
import os
import re
import sys
import time
from collections import Counter

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(HERE, "pylibs"))

GAME = r"D:\steam\steamapps\common\Astral Party\8vJXn6CN"
BUNDLES = os.path.join(GAME, "AstralParty_CN_Data", "StreamingAssets", "aa",
                       "StandaloneWindows64")
OUT_TXT = os.path.join(HERE, "_art_hunt2_report.txt")
OUT_JSON = os.path.join(HERE, "_art_hunt2.json")

# 第二节：像素小人 / 图鉴 相关的名字关键词
KW2 = ["pixel", "chibi", "retro", "8bit", "8_bit", "voxel", "dot", "sd_",
       "codex", "handbook", "hand_book", "handbook", "illustrat", "encyclop",
       "tujian", "manual", "gallery", "collection", "album", "archive",
       "monster", "mon_", "icon_mon", "ui_mon", "head", "portrait", "avatar",
       "sprite", "atlas", "图鉴", "图鉴", "怪物", "像素", "小人", "头像",
       "立绘", "图集", "怪物轮", "怪物图", "怪物立绘"]

# 第一节：地图族
KW1 = ["map_023", "map023", "minimap_023", "82016", "mapscene", "mappreview",
       "guidepic", "library", "异变", "图书馆", "chapter", "loading", "cover",
       "region_select", "selectmap", "mapselect", "levelmap"]

PRE_SKIP = 80


def scan_one(path):
    """只读打开一个包，返回 (bundle名, 对象统计, 命中的对象记录, 全名字样本)。"""
    import UnityPy
    bn = os.path.basename(path)
    rec = {"bundle": bn, "types": {}, "hits": [], "err": None}
    try:
        env = UnityPy.load(path)
        types = Counter()
        for o in env.objects:
            tn = o.type.name
            types[tn] += 1
            try:
                d = o.read(check_read=False)
            except Exception:
                continue
            name = getattr(d, "m_Name", None)
            if not isinstance(name, str) or not name:
                continue
            low = name.lower()
            hit1 = any(k in low for k in KW1)
            hit2 = any(k in low for k in KW2)
            if not (hit1 or hit2):
                continue
            item = {"type": tn, "name": name, "sec": ("1" if hit1 else "") + ("2" if hit2 else "")}
            if tn == "Texture2D":
                item["w"] = getattr(d, "m_Width", 0)
                item["h"] = getattr(d, "m_Height", 0)
                item["fmt"] = getattr(d, "m_TextureFormat", None)
            elif tn == "Sprite":
                item["w"] = getattr(d, "m_Rect", None) and getattr(d.m_Rect, "width", None)
                item["h"] = getattr(d, "m_Rect", None) and getattr(d.m_Rect, "height", None)
                r = getattr(d, "m_Rect", None)
                if r is not None:
                    item["rect"] = [getattr(r, "x", None), getattr(r, "y", None),
                                    getattr(r, "width", None), getattr(r, "height", None)]
                item["rect"] = item.get("rect")
                try:
                    item["go"] = d.m_GameObject.read(check_read=False).m_Name
                except Exception:
                    item["go"] = None
                try:
                    item["pad"] = d.m_RD.read(check_read=False).m_Padding
                except Exception:
                    item["pad"] = None
            rec["hits"].append(item)
        rec["types"] = dict(types)
    except Exception as e:
        rec["err"] = "%s: %s" % (type(e).__name__, e)
    return rec


def main():
    t0 = time.time()
    files = sorted(f for f in os.listdir(BUNDLES) if f.endswith(".bundle"))
    paths = [os.path.join(BUNDLES, f) for f in files]
    nproc = max(2, min(12, (os.cpu_count() or 4)))
    print("包数 = %d，进程 = %d" % (len(paths), nproc))
    sys.stdout.flush()

    results = []
    with mp.Pool(nproc) as pool:
        for i, rec in enumerate(pool.imap_unordered(scan_one, paths, chunksize=8), 1):
            results.append(rec)
            if i % 250 == 0:
                print("  ... %d/%d  (%.0fs)" % (i, len(paths), time.time() - t0))
                sys.stdout.flush()

    errs = [r for r in results if r["err"]]
    with_hits = [r for r in results if r["hits"]]
    print("打开失败 = %d / %d" % (len(errs), len(results)))
    print("有命中的包 = %d，命中记录 = %d"
          % (len(with_hits), sum(len(r["hits"]) for r in with_hits)))
    sys.stdout.flush()

    with io.open(OUT_JSON, "w", encoding="utf-8") as fh:
        json.dump(results, fh, ensure_ascii=False)

    # ---- 汇总 ----
    LOG = []

    def say(s):
        LOG.append(str(s))
        print(s)
        sys.stdout.flush()

    say("=" * 78)
    say("全库对象头扫描：%d 个 bundle（失败 %d）" % (len(results), len(errs)))
    say("=" * 78)
    tt = Counter()
    for r in results:
        tt.update(r["types"])
    say("—— 全库对象类型分布（前 30）——")
    for k, v in tt.most_common(30):
        say("   %-24s %d" % (k, v))
    say("")
    say("—— 打开失败的包（前 20）——")
    for r in errs[:20]:
        say("   %-46s %s" % (r["bundle"], r["err"]))
    say("")

    def dump_section(sec, title):
        say("=" * 78)
        say(title)
        say("=" * 78)
        byname = {}
        for r in results:
            for h in r["hits"]:
                if sec not in h["sec"]:
                    continue
                byname.setdefault((h["type"], h["name"]), []).append(
                    (r["bundle"], h.get("w"), h.get("h"), h.get("rect"), h.get("go")))
        say("  去重后对象名 = %d" % len(byname))
        for (t, n), lst in sorted(byname.items()):
            b, w, h, rect, go = lst[0]
            extra = ""
            if t == "Sprite":
                extra = "  go=%s rect=%s" % (go, rect)
            elif t == "Texture2D":
                extra = "  %sx%s" % (w, h)
            say("   [%-14s] %-52s bundle=%s%s" % (t, n, b[:16], extra))
        say("")
        return byname

    sec1 = dump_section("1", "① 地图族命中（异变图书馆 82016 / 画板 023 相关）")
    sec2 = dump_section("2", "② 像素小人 / 图鉴 关键词命中（**含 Sprite / SpriteAtlas**）")

    say("用时 %.1f 秒" % (time.time() - t0))
    io.open(OUT_TXT, "w", encoding="utf-8").write("\n".join(LOG) + "\n")
    print("报告：" + OUT_TXT)
    return 0


if __name__ == "__main__":
    sys.exit(main())
