# -*- coding: utf-8 -*-
"""★ 只读侦察：catalog -> bundle 文件到底怎么对上的（Map_023 那一族实测）。

probe_catalog_art.py 里 bundle_name() 用的是 entries[e][1] != 0 过滤，
这里把原始 entry 全打出来，看看到底哪儿不对。游戏目录只读。
"""
import base64
import io
import json
import os
import struct
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
ADDR = os.path.join(os.environ.get("USERPROFILE", ""), "AppData", "LocalLow",
                    "feimo", "AstralParty_CN", "com.unity.addressables")
CAT = os.path.join(ADDR, "catalog_3.2.0.json")
GAME = r"D:\steam\steamapps\common\Astral Party\8vJXn6CN"
BUNDLES = os.path.join(GAME, "AstralParty_CN_Data", "StreamingAssets", "aa",
                       "StandaloneWindows64")
OUT = os.path.join(HERE, "_bundle_map_report.txt")
LOG = []


def say(s):
    LOG.append(str(s))
    print(s)


class Catalog(object):
    def __init__(self, path):
        raw = json.load(io.open(path, encoding="utf-8"))
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

    def dump(self, key):
        j = self.index_of.get(key)
        say("--- key %r  index=%s ---" % (key, j))
        if j is None:
            return
        for e in self.by_key_index.get(j, []):
            row = self.entries[e]
            say("   entry[%d] raw=%s" % (e, row))
            say("        internalId[%d]=%s" % (row[0], self.ids[row[0]]))
            dep = row[2]
            if dep >= 0:
                say("        dep entry=%d raw=%s" % (dep, self.entries[dep]))
                say("        dep internalId=%s" % self.ids[self.entries[dep][0]])
                for be in self.by_key_index.get(dep, []):
                    say("          dep-key entry=%d raw=%s -> %s"
                        % (be, self.entries[be], self.ids[self.entries[be][0]]))


def main():
    cat = Catalog(CAT)
    say("catalog internalId 条数 = %d" % len(cat.ids))
    say("前 8 条 internalId:")
    for x in cat.ids[:8]:
        say("   " + x)
    say("")
    say("StandaloneWindows64 下 .bundle 文件数 = %d"
        % len([f for f in os.listdir(BUNDLES) if f.endswith(".bundle")]))
    say("")
    for k in ("Map_023", "MiniMap_023", "UT_MapScene_021", "UT_MapPreview_009",
              "GuidePic_8201601", "GuidePic_PVE01", "NewGameLibrary_atlas0",
              "NewGameLibrary_fui", "ActivityMapPass_atlas0", "Map_007",
              "UT_Monster_Bust_1003_0", "UT_MapPreview_007"):
        cat.dump(k)
        say("")
    io.open(OUT, "w", encoding="utf-8").write("\n".join(LOG) + "\n")
    print("报告：" + OUT)
    return 0


if __name__ == "__main__":
    sys.exit(main())
