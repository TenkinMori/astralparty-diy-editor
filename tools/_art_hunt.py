# -*- coding: utf-8 -*-
"""★ 只读侦察：给「异变图书馆」(地图 82016 / 画板 023) 找官方美术图。

思路（按性价比）：
  ① 把 catalog 里**所有**跟"地图/画板号/章节/选关/加载图"沾边的 key 全列出来
     —— 看看官方到底按什么命名族登记了每张地图的图（UT_MapPreview_* 只是其中一族）。
  ② 全库扫 key：023 / 82016 / map / bg / scene / stage / chapter / select /
     loading / cover / thumb / icon / pve / library 以及中文。
  ③ 只读方式打开候选 bundle，读 Texture2D / Sprite / SpriteAtlas 的名字 + 尺寸。

产物只落 tools\\_art_hunt_report.txt。游戏目录一个字节都不写。
"""
import base64
import io
import json
import os
import re
import struct
import sys
import time
from collections import Counter

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(HERE, "pylibs"))

GAME = r"D:\steam\steamapps\common\Astral Party\8vJXn6CN"
BUNDLES = os.path.join(GAME, "AstralParty_CN_Data", "StreamingAssets", "aa",
                       "StandaloneWindows64")
ADDR = os.path.join(os.environ.get("USERPROFILE", ""), "AppData", "LocalLow",
                    "feimo", "AstralParty_CN", "com.unity.addressables")
OUT = os.path.join(HERE, "_art_hunt_report.txt")

LOG = []


def say(s):
    LOG.append(str(s))
    print(s)
    sys.stdout.flush()


def catalog_paths():
    out = []
    for name in os.listdir(ADDR):
        if name.startswith("catalog_") and name.endswith(".json"):
            out.append(os.path.join(ADDR, name))
    return sorted(out)


class Catalog(object):
    def __init__(self, path):
        raw = json.load(io.open(path, encoding="utf-8"))
        self.path = path
        self.ids = raw["m_InternalIds"]
        self._keys(base64.b64decode(raw["m_KeyDataString"]))
        self._buckets(base64.b64decode(raw["m_BucketDataString"]))
        self._entries(base64.b64decode(raw["m_EntryDataString"]))
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

    def internal_id(self, entry_index):
        return self.ids[self.entries[entry_index][0]]

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
        j = self.index_of.get(name)
        if j is None:
            return None
        for e in self.by_key_index.get(j, []):
            bn = self.bundle_name(e)
            if bn:
                return bn
        return None

    def location_for_key(self, name):
        """key -> 它自己的 internal id（资产在包里的路径，含资产名）。"""
        j = self.index_of.get(name)
        if j is None:
            return None
        for e in self.by_key_index.get(j, []):
            return self.internal_id(e)
        return None


def main():
    t0 = time.time()
    cps = catalog_paths()
    say("catalog 文件（%d 个）：" % len(cps))
    for p in cps:
        say("   " + p)
    main_cat = None
    for p in cps:
        if ".modded" in p or ".prebak" in p:
            continue
        main_cat = p
    if main_cat is None:
        main_cat = cps[-1]
    cat = Catalog(main_cat)
    say("主 catalog = %s" % main_cat)
    say("key 数 = %d / internal id = %d" % (len(cat.keys), len(cat.ids)))
    say("")

    # ---------- ① 所有"地图族"key 全列 ----------
    say("=" * 78)
    say("① catalog 里全部地图族 key（含画板号的那些）")
    say("=" * 78)
    fam = re.compile(r"Map|map|Scene|scene|Chapter|Stage|Select|Loading|Cover|"
                     r"Thumb|Preview|Board|Level|PVE|Pve|pve|Hangar|Library|"
                     r"Library|BG_|Bg_", re.I)
    groups = {}
    for k in cat.keys:
        if not isinstance(k, str):
            continue
        if not fam.search(k):
            continue
        head = k.split("/")[0]
        head = re.sub(r"\d+", "#", head)
        groups.setdefault(head, []).append(k)
    for head in sorted(groups, key=lambda h: (-len(groups[h]), h)):
        ks = sorted(groups[head])
        say("  [%-40s] %d" % (head, len(ks)))
        for k in ks[:60]:
            say("        %s   (bundle=%s)" % (k, cat.bundle_for_key(k)))
        if len(ks) > 60:
            say("        ... 还有 %d 个" % (len(ks) - 60))
    say("")

    # ---------- ② 全库扫关键词 ----------
    say("=" * 78)
    say("② 全库 key 关键词命中")
    say("=" * 78)
    KWS = ["023", "82016", "82017", "map_", "map0", "bg_", "scene", "stage",
           "chapter", "select", "loading", "cover", "thumb", "icon", "pve",
           "library", "hangar", "board", "preview", "异变", "图书馆", "关卡",
           "章节", "地图", "选关"]
    for kw in KWS:
        hits = [k for k in cat.keys
                if isinstance(k, str) and kw.lower() in k.lower()]
        say("[%-10s] %d" % (kw, len(hits)))
        for k in hits[:25]:
            say("      %s" % k)
        if len(hits) > 25:
            say("      ... 还有 %d 个" % (len(hits) - 25))
    say("")

    # ---------- ③ 数字 key ----------
    say("=" * 78)
    say("③ 纯数字 / 疑似地图 id 的 key")
    say("=" * 78)
    nums = [k for k in cat.keys if isinstance(k, int)]
    say("  整数 key 数 = %d" % len(nums))
    say("  含 023 / 82016 的整数：%s"
        % [n for n in nums if "023" in str(n) or n == 82016][:40])
    say("")

    io.open(OUT, "w", encoding="utf-8").write("\n".join(LOG))
    print("\n报告：" + OUT + "  用时 %.1fs" % (time.time() - t0))
    return 0


if __name__ == "__main__":
    sys.exit(main())
