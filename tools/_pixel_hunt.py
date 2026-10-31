# -*- coding: utf-8 -*-
"""★ 只读侦察：游戏里到底有没有"像素风怪物小人"。

① 用 catalog 把 UT_Monster_* / Summon_* / Map* 等 key 解到具体 bundle，
   打开这些包、读**包里 Texture2D 的 m_Name**（catalog key ≠ 资产名，这步是关键），
   按关键词过滤 + 统计真实前缀分布。
② 扫 GameRes\\Img 与 贴图缓存 里尺寸 <=200px 的 PNG，数颜色数
   —— 像素画的签名是"小尺寸 + 极少颜色"。
③ 看 MovieClip 结构。

产物只落 tools\\_pixel_hunt_report.txt。游戏目录一个字节都不写。
"""
import base64
import io
import json
import os
import struct
import sys
import time
from collections import Counter

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(HERE, "pylibs"))

GAME = r"D:\steam\steamapps\common\Astral Party\8vJXn6CN"
BUNDLES = os.path.join(GAME, "AstralParty_CN_Data", "StreamingAssets", "aa",
                       "StandaloneWindows64")
IMG = os.path.join(GAME, "GameRes", "Img")
CACHE = os.path.join(os.path.dirname(HERE), "贴图缓存")
ADDR = os.path.join(os.environ.get("USERPROFILE", ""), "AppData", "LocalLow",
                    "feimo", "AstralParty_CN", "com.unity.addressables")
OUT = os.path.join(HERE, "_pixel_hunt_report.txt")

LOG = []


def say(s):
    LOG.append(str(s))
    print(s)
    sys.stdout.flush()


def catalog_path():
    best = None
    for name in os.listdir(ADDR):
        if not name.startswith("catalog_") or not name.endswith(".json"):
            continue
        if ".modded" in name or ".prebak" in name:
            continue
        key = [int(x) if x.isdigit() else x
               for x in name[len("catalog_"):-len(".json")].split(".")]
        if best is None or key > best[0]:
            best = (key, name)
    return os.path.join(ADDR, best[1]) if best else None


class Catalog(object):
    """照抄 probe_catalog_art.py 的解析（已实测可用）。"""

    def __init__(self, path):
        raw = json.load(io.open(path, encoding="utf-8"))
        self.ids = raw["m_InternalIds"]
        self._keys(base64.b64decode(raw["m_KeyDataString"]))
        self._buckets(base64.b64decode(raw["m_BucketDataString"]))
        self._entries(base64.b64decode(raw["m_EntryDataString"]))
        # ★ 与 probe_catalog_art.py 完全一致：bucket 里的 offset 用
        #   offset_to_key 翻成 **key 下标**，by_key_index 才是以下标为键。
        self.by_key_index = {}
        for offset, ent in self.buckets:
            self.by_key_index.setdefault(self.offset_to_key[offset], []).extend(ent)
        self.index_of = {}
        for j, k in enumerate(self.keys):
            self.index_of.setdefault(k, j)

    def _keys(self, blob):
        count, = struct.unpack_from("<i", blob, 0)
        self.offset_to_key = {}
        self.keys = []
        i = 4
        for _ in range(count):
            self.offset_to_key[i] = len(self.keys)
            tag = blob[i]
            if tag == 0:
                ln, = struct.unpack_from("<i", blob, i + 1)
                self.keys.append(blob[i + 5:i + 5 + ln].decode("utf-8", "replace"))
                i += 5 + ln
            elif tag == 1:
                self.keys.append(struct.unpack_from("<i", blob, i + 1)[0])
                i += 5
            else:
                raise ValueError("tag %d" % tag)

    def _buckets(self, blob):
        count, = struct.unpack_from("<i", blob, 0)
        self.buckets = []
        i = 4
        for _ in range(count):
            off, cnt = struct.unpack_from("<ii", blob, i)
            i += 8
            ent = list(struct.unpack_from("<%di" % cnt, blob, i))
            i += 4 * cnt
            self.buckets.append((off, ent))

    def _entries(self, blob):
        count, = struct.unpack_from("<i", blob, 0)
        self.entries = [struct.unpack_from("<7i", blob, 4 + 28 * k)
                        for k in range(count)]

    def bundle_name(self, entry_index):
        dep = self.entries[entry_index][2]
        if dep < 0:
            return None
        for e in self.by_key_index.get(dep, []):
            if self.entries[e][1] != 0:
                continue
            raw = os.path.basename(self.ids[self.entries[e][0]].replace("\\", "/"))
            return raw[:-len(".bundle")] if raw.endswith(".bundle") else raw
        return None

    def bundle_for_key(self, name):
        """key 名 -> 它所在 bundle 文件名。

        ★ `by_key_index` 的键是 **key 下标**（offset_to_key 翻出来的），
          `index_of` 给的也是下标 ⇒ 直接查即可，不要拿 offset 去查。
        """
        j = self.index_of.get(name)
        if j is None:
            return None
        for e in self.by_key_index.get(j, []):
            bn = self.bundle_name(e)
            if bn:
                return bn
        return None


KW = ["pixel", "chibi", "retro", "8bit", "dot", "sprite", "mini", "sd",
      "avatar", "icon", "head", "monster", "summon", "idle", "walk",
      "battle", "fight", "chara", "spine", "atlas", "role", "bust", "card"]


def main():
    t0 = time.time()
    cat_path = catalog_path()
    cat = Catalog(cat_path)
    say("catalog = %s" % cat_path)
    say("catalog key = %d" % len(cat.keys))
    say("")

    # ---------- ① 解 bundle ----------
    want_prefix = ("UT_Monster_", "Summon_", "VFight_", "UT_Summon",
                   "UT_MapPreview", "UT_MapScene", "MiniMap_", "Map_",
                   "UT_HandCard_")
    target_bundles = {}
    missed = 0
    for k in cat.keys:
        if not isinstance(k, str) or not k.startswith(want_prefix):
            continue
        bn = cat.bundle_for_key(k)
        if bn:
            target_bundles.setdefault(bn, []).append(k)
        else:
            missed += 1

    say("=" * 78)
    say("① 相关 key 解到 bundle = %d 个（解不到的 key %d 个）"
        % (len(target_bundles), missed))
    say("=" * 78)

    import UnityPy
    name_counter = Counter()
    kw_hits = Counter()
    kw_examples = {}
    sizes = {}
    scanned = failed = t2_total = 0
    cap = 400

    # ★ 优先级：怪物包先扫（本轮真正要找的就是怪），再扫地图/手牌包。
    def prio(bn):
        ks = target_bundles[bn]
        if any(k.startswith("UT_Monster_") for k in ks):
            return 0
        if any(k.startswith(("Summon_", "VFight_")) for k in ks):
            return 1
        if any(k.startswith(("UT_MapPreview", "UT_MapScene", "MiniMap_", "Map_"))
               for k in ks):
            return 2
        return 3

    order = sorted(target_bundles, key=lambda b: (prio(b), b))
    for bn in order:
        if scanned >= cap:
            break
        p = os.path.join(BUNDLES, bn + ".bundle")
        if not os.path.exists(p):
            failed += 1
            continue
        try:
            env = UnityPy.load(p)
            for obj in env.objects:
                if obj.type.name != "Texture2D":
                    continue
                try:
                    d = obj.read()
                except Exception:
                    continue
                nm = getattr(d, "m_Name", None)
                if not nm:
                    continue
                t2_total += 1
                name_counter[nm] += 1
                w = getattr(d, "m_Width", 0) or 0
                h = getattr(d, "m_Height", 0) or 0
                sizes[nm] = (w, h)
                low = nm.lower()
                for kw in KW:
                    if kw in low:
                        kw_hits[kw] += 1
                        kw_examples.setdefault(kw, []).append((nm, w, h))
                        break
            scanned += 1
        except Exception:
            failed += 1
        if scanned and scanned % 40 == 0:
            say("  ... 已扫 %d 包 / Texture2D %d 个（%.0fs）"
                % (scanned, t2_total, time.time() - t0))

    say("")
    say("实际打开 bundle = %d / 目标 %d（失败 %d）"
        % (scanned, len(target_bundles), failed))
    say("看到的 Texture2D 名字（去重）= %d" % len(name_counter))
    say("")
    say("—— ★ 关键词命中（读的是包里 Texture2D 的 m_Name）——")
    for kw in KW:
        say("  [%-9s] %d" % (kw, kw_hits.get(kw, 0)))
        for nm, w, h in kw_examples.get(kw, [])[:6]:
            say("        %-58s %dx%d" % (nm, w, h))
    say("")
    say("—— Texture2D 名字最常见前缀（前 40）——")
    pre = Counter()
    for nm in name_counter:
        pre["_".join(nm.split("_")[:2]) if "_" in nm else nm] += 1
    for k, v in pre.most_common(40):
        say("  %-44s %d" % (k, v))
    say("")
    say("—— ★ 尺寸 <=200px 的 Texture2D（像素画的尺寸签名）——")
    small = sorted((v[0] * v[1], k, v[0], v[1]) for k, v in sizes.items()
                   if 0 < v[0] <= 200 and 0 < v[1] <= 200)
    say("  共 %d 个" % len(small))
    for area, k, w, h in small[:40]:
        say("      %-58s %dx%d" % (k, w, h))
    say("")

    # ---------- ② 小图 + 少颜色 ----------
    say("=" * 78)
    say("② 小尺寸 PNG（<=200px）与颜色数 <=64 的像素画候选")
    say("=" * 78)
    from PIL import Image
    cands = []
    for d, tag in ((IMG, "游戏目录"), (CACHE, "贴图缓存")):
        if not os.path.isdir(d):
            continue
        for fn in sorted(os.listdir(d)):
            if not fn.lower().endswith(".png"):
                continue
            try:
                im = Image.open(os.path.join(d, fn))
                w, h = im.size
            except Exception:
                continue
            if w > 200 or h > 200:
                continue
            try:
                cols = im.convert("RGBA").getcolors(maxcolors=100000)
                ncol = len(cols) if cols else 999999
            except Exception:
                ncol = -1
            cands.append((ncol, w, h, tag, fn))
    cands.sort()
    say("  小图总数 = %d" % len(cands))
    say("  —— 颜色数最少的 25 张 ——")
    for ncol, w, h, tag, fn in cands[:25]:
        say("      %-50s %3dx%-3d 颜色 %-7s [%s]" % (fn, w, h, ncol, tag))
    say("")
    say("  —— 颜色数 <=64 的（≤64 = 真像素画签名）——")
    few = [c for c in cands if 0 <= c[0] <= 64]
    say("      命中 %d 张" % len(few))
    for ncol, w, h, tag, fn in few[:40]:
        say("      %-50s %3dx%-3d 颜色 %-7s [%s]" % (fn, w, h, ncol, tag))
    say("")
    hit = [c for c in cands if c[4].startswith("UT_Monster")]
    say("  —— UT_Monster_* 里 <=200px 的：%d 张 ——" % len(hit))
    for ncol, w, h, tag, fn in hit[:20]:
        say("      %-50s %3dx%-3d 颜色 %s" % (fn, w, h, ncol))
    say("")

    # ---------- ③ MovieClip ----------
    say("=" * 78)
    say("③ GameRes\\MovieClip")
    say("=" * 78)
    mc = os.path.join(GAME, "GameRes", "MovieClip")
    if os.path.isdir(mc):
        ents = sorted(os.listdir(mc))
        say("  条目数 = %d" % len(ents))
        for e in ents[:50]:
            say("      " + e)
    else:
        say("  没有这个目录")
    say("")
    say("用时 %.1f 秒" % (time.time() - t0))

    io.open(OUT, "w", encoding="utf-8").write("\n".join(LOG))
    print("\n报告：" + OUT)
    return 0


if __name__ == "__main__":
    sys.exit(main())
