# -*- coding: utf-8 -*-
"""★ 只读侦察 · Texture2D **全量**清单（不按关键词过滤）。

为什么还要这一步：
  `_art_hunt3.py` 只留下名字里带关键词的对象 —— 这会把"名字不含 pixel/monster 的
  像素风图集"整个漏掉。本脚本把**全部** 5500 个包里每个 Texture2D 的名字 + 尺寸 +
  所在包落成一张总表（分片可续跑），后面的判定都基于这张总表。

产物：%TEMP%\\_tex_inv\\shard_*.json  →  汇总落 tools\\_tex_inv.tsv
游戏目录只读。
"""
import io
import json
import multiprocessing as mp
import os
import sys
import tempfile
import time

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(HERE, "pylibs"))

BUNDLES = r"D:\steam\steamapps\common\Astral Party\8vJXn6CN\AstralParty_CN_Data\StreamingAssets\aa\StandaloneWindows64"
SHARDS = os.path.join(tempfile.gettempdir(), "_tex_inv")
CHUNK = 300


def scan_one(path):
    import UnityPy
    bn = os.path.basename(path)
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
            rows.append([nm, getattr(d, "m_Width", 0) or 0,
                         getattr(d, "m_Height", 0) or 0,
                         getattr(d, "m_TextureFormat", None)])
    except Exception:
        return bn, None, rows
    return bn, True, rows


def main():
    t0 = time.time()
    os.makedirs(SHARDS, exist_ok=True)
    files = sorted(f for f in os.listdir(BUNDLES) if f.endswith(".bundle"))
    paths = [os.path.join(BUNDLES, f) for f in files]
    chunks = [paths[i:i + CHUNK] for i in range(0, len(paths), CHUNK)]
    print("包数 = %d，%d 片" % (len(paths), len(chunks)))
    sys.stdout.flush()
    for i, ch in enumerate(chunks):
        out = os.path.join(SHARDS, "shard_%04d.json" % i)
        if os.path.exists(out):
            continue
        res = []
        with mp.Pool(8) as pool:
            for bn, ok, rows in pool.imap_unordered(scan_one, ch, chunksize=4):
                res.append([bn, ok, rows])
        with io.open(out, "w", encoding="utf-8") as fh:
            json.dump(res, fh, ensure_ascii=False)
        if (i + 1) % 3 == 0 or i + 1 == len(chunks):
            print("  片 %d/%d（%.0fs）" % (i + 1, len(chunks), time.time() - t0))
            sys.stdout.flush()

    lines = []
    bundles = {}
    for i in range(len(chunks)):
        p = os.path.join(SHARDS, "shard_%04d.json" % i)
        if not os.path.exists(p):
            continue
        for bn, ok, rows in json.load(io.open(p, encoding="utf-8")):
            bundles[bn] = ok
            for nm, w, h, fmt in rows:
                lines.append("%s\t%d\t%d\t%s\t%s" % (nm, w, h, fmt, bn))
    lines.sort()
    io.open(os.path.join(HERE, "_tex_inv.tsv"), "w",
            encoding="utf-8").write("\n".join(lines) + "\n")
    print("Texture2D 记录 = %d（去重名 = %d），包 = %d（打开失败 %d）"
          % (len(lines), len(set(l.split("\t")[0] for l in lines)),
             len(bundles), sum(1 for v in bundles.values() if not v)))
    print("表：tools\\_tex_inv.tsv   用时 %.0fs" % (time.time() - t0))
    return 0


if __name__ == "__main__":
    sys.exit(main())
