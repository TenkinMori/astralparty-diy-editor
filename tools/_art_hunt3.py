# -*- coding: utf-8 -*-
"""★ 只读侦察 · 第二版（分片 + 可续跑 + 低内存）：
在**全部** .bundle 里找「图鉴那套像素风怪物小人」，以及所有按地图命名的图片族。

第一版（_art_hunt2.py）在 5250/5500 卡死 —— worker 常驻内存涨到 1.5GB。
这一版改三件事：
  ① 按 **300 包一片**跑，每片跑完**销毁进程池重建**（内存不累积）；
  ② 每片结果立刻落 `%TEMP%\\_art_hunt3\\shard_XXXX.json`，**可续跑**（已存在的片跳过）；
  ③ 只对 `Texture2D` 读元数据、对 `Sprite` 做一次选择性反序列化，其余类型**直接跳过**
     （`MonoBehaviour` / `GameObject` / `SpriteAtlas` 没有 m_Name，读了纯浪费）。

只读游戏目录；临时片落 %TEMP%；最后只把汇总落 tools\\。
"""
import io
import json
import multiprocessing as mp
import os
import sys
import tempfile
import time
from collections import Counter

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(HERE, "pylibs"))

GAME = r"D:\steam\steamapps\common\Astral Party\8vJXn6CN"
BUNDLES = os.path.join(GAME, "AstralParty_CN_Data", "StreamingAssets", "aa",
                       "StandaloneWindows64")
SHARDS = os.path.join(tempfile.gettempdir(), "_art_hunt3")
CHUNK = 300

# —— 只统计这些类型（其余类型的名字读不到，跳过省时间/内存）——
KEEP_TYPES = ("Texture2D", "Sprite")

KW = ["pixel", "chibi", "retro", "8bit", "8_bit", "voxel", "dot_", "dots",
      "codex", "handbook", "hand_book", "illustrat", "encyclop", "tujian",
      "manual", "gallery", "collection", "album", "archive", "monster",
      "mon_", "icon_mon", "ui_mon", "head", "portrait", "avatar", "sprite",
      "atlas", "uisp", "icon", "图鉴", "怪物", "像素", "小人", "头像",
      "立绘", "图集", "怪物轮", "怪物图", "章节", "选关", "地图", "关卡",
      "异变", "图书馆", "library", "map_", "minimap", "mapscene", "mappreview",
      "guidepic", "loading", "cover", "select"]


def scan_one(path):
    import UnityPy
    bn = os.path.basename(path)
    rec = {"bundle": bn, "types": {}, "hits": [], "err": None}
    try:
        env = UnityPy.load(path)
        types = Counter()
        hits = []
        for o in env.objects:
            tn = o.type.name
            types[tn] += 1
            if tn not in KEEP_TYPES:
                continue
            if tn == "Texture2D":
                try:
                    d = o.read(check_read=False)
                except Exception:
                    continue
                name = getattr(d, "m_Name", None)
                if not isinstance(name, str) or not name:
                    continue
                low = name.lower()
                if not any(k in low for k in KW):
                    continue
                hits.append({"type": "Texture2D", "name": name,
                             "w": getattr(d, "m_Width", 0) or 0,
                             "h": getattr(d, "m_Height", 0) or 0,
                             "fmt": getattr(d, "m_TextureFormat", None)})
            else:
                # Sprite：上一轮完全没看这一族，而这正是「图集里的一块」的真名所在
                try:
                    d = o.read(check_read=False)
                except Exception:
                    continue
                name = getattr(d, "m_Name", None)
                if not isinstance(name, str) or not name:
                    continue
                low = name.lower()
                if not any(k in low for k in KW):
                    continue
                item = {"type": "Sprite", "name": name}
                try:
                    r = d.m_Rect
                    item["rect"] = [round(r.x, 1), round(r.y, 1),
                                    round(r.width, 1), round(r.height, 1)]
                except Exception:
                    item["rect"] = None
                try:
                    item["go"] = d.m_GameObject.read(check_read=False).m_Name
                except Exception:
                    item["go"] = None
                try:
                    item["tex"] = d.m_SpriteAtlas.read(check_read=False).m_Name
                except Exception:
                    item["tex"] = None
                hits.append(item)
        rec["types"] = dict(types)
        rec["hits"] = [h for h in hits if h]
    except Exception as e:
        rec["err"] = "%s: %s" % (type(e).__name__, e)
    return rec


def run_chunk(paths, shard_id):
    out = os.path.join(SHARDS, "shard_%04d.json" % shard_id)
    res = []
    nproc = max(2, min(8, (os.cpu_count() or 4)))
    with mp.Pool(nproc) as pool:
        for rec in pool.imap_unordered(scan_one, paths, chunksize=4):
            res.append(rec)
    with io.open(out, "w", encoding="utf-8") as fh:
        json.dump(res, fh, ensure_ascii=False)
    return out, len(res)


def main():
    t0 = time.time()
    os.makedirs(SHARDS, exist_ok=True)
    files = sorted(f for f in os.listdir(BUNDLES) if f.endswith(".bundle"))
    paths = [os.path.join(BUNDLES, f) for f in files]
    chunks = [paths[i:i + CHUNK] for i in range(0, len(paths), CHUNK)]
    print("包数 = %d，分 %d 片（每片 %d），片目录 = %s"
          % (len(paths), len(chunks), CHUNK, SHARDS))
    sys.stdout.flush()

    for i, ch in enumerate(chunks):
        out = os.path.join(SHARDS, "shard_%04d.json" % i)
        if os.path.exists(out):
            print("  片 %d/%d 已有，跳过" % (i + 1, len(chunks)))
            sys.stdout.flush()
            continue
        t1 = time.time()
        try:
            run_chunk(ch, i)
        except Exception as e:
            print("  片 %d/%d 失败：%s" % (i + 1, len(chunks), e))
            sys.stdout.flush()
            continue
        print("  片 %d/%d 完成（%.0fs，总 %.0fs）"
              % (i + 1, len(chunks), time.time() - t1, time.time() - t0))
        sys.stdout.flush()

    # 汇总
    results = []
    for i in range(len(chunks)):
        p = os.path.join(SHARDS, "shard_%04d.json" % i)
        if not os.path.exists(p):
            continue
        results.extend(json.load(io.open(p, encoding="utf-8")))

    errs = [r for r in results if r["err"]]
    tt = Counter()
    for r in results:
        tt.update(r["types"])
    byname = {}
    for r in results:
        for h in r["hits"]:
            byname.setdefault((h["type"], h["name"]), []).append((r["bundle"], h))

    LOG = []

    def say(s):
        LOG.append(str(s))
        print(s)
        sys.stdout.flush()

    say("=" * 78)
    say("全库扫描汇总：%d / %d 个 bundle（打开失败 %d）"
        % (len(results), len(paths), len(errs)))
    say("=" * 78)
    say("—— 对象类型分布（前 25）——")
    for k, v in tt.most_common(25):
        say("   %-22s %d" % (k, v))
    say("")
    say("—— 名字命中（去重）= %d ——" % len(byname))
    for (t, n), lst in sorted(byname.items()):
        b, h = lst[0]
        if t == "Texture2D":
            extra = "%dx%d" % (h.get("w", 0), h.get("h", 0))
        else:
            extra = "rect=%s" % (h.get("rect"),)
        say("   [%-10s] %-56s %-12s bundle=%s"
            % (t, n, extra, b[:16]))
    say("")
    say("用时 %.1f 秒" % (time.time() - t0))
    io.open(os.path.join(HERE, "_art_hunt3_report.txt"), "w",
            encoding="utf-8").write("\n".join(LOG) + "\n")
    io.open(os.path.join(HERE, "_art_hunt3_names.txt"), "w",
            encoding="utf-8").write(
        "\n".join("%s\t%s\t%s" % (t, n, lst[0][0]) for (t, n), lst in
                  sorted(byname.items())) + "\n")
    print("报告：tools\\_art_hunt3_report.txt")
    return 0


if __name__ == "__main__":
    sys.exit(main())
