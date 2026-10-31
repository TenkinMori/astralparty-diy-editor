# -*- coding: utf-8 -*-
"""★ 只读侦察：Addressables 目录里到底有没有我们缺的那几张图。

只读两个地方：
    ① %LocalLow%\\feimo\\AstralParty_CN\\com.unity.addressables\\catalog_3.2.0.json
    ② 同目录 AssetBundles\\**\\__data（只 stat，不读内容）

**一个字节都不写**（除了把结论打屏）。产物落 5-DIY关卡\\tools\\probe_catalog_art.txt。

用法：
    python probe_catalog_art.py                 # 查内置那批名字
    python probe_catalog_art.txt ...
"""
import base64
import io
import json
import os
import struct
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
LL = os.path.join(os.environ.get("USERPROFILE", ""), "AppData", "LocalLow",
                  "feimo", "AstralParty_CN")
ADDR = os.path.join(LL, "com.unity.addressables")
CATALOG = os.path.join(ADDR, "catalog_3.2.0.json")
OUT = os.path.join(HERE, "probe_catalog_art.txt")

# ★ 我们缺的那些（第 8 步报告 §② 的清单）
WANT = [
    "UT_Platform_Gift", "UT_Platform_Hospital", "UT_Platform_Lottery",
    "UT_Platform_Fire", "UT_Platform_Divine", "UT_Platform_Gamble",
    "UT_Platform_Destiny", "UT_Platform_Jump", "UT_Platform_Gimmick",
    "UT_Platform_108", "UT_Platform_Start", "UT_Platform_Shop",
    "UT_Monster_Bust_1003_0", "UT_Monster_Bust_1006_0", "UT_Monster_Bust_1012_0",
    "UT_Monster_Bust_1017_0", "UT_Monster_Bust_1027_0", "UT_Monster_Bust_1030_0",
    "UT_HandCard_10001", "UT_HandCard_10002",
    "UT_MapPreview_023", "UT_MapScene_023",
]


def _b64(text):
    return base64.b64decode(text)


class Catalog(object):
    def __init__(self, path):
        self.raw = json.load(io.open(path, encoding="utf-8"))
        self.ids = self.raw["m_InternalIds"]
        self._read_keys(_b64(self.raw["m_KeyDataString"]))
        self._read_buckets(_b64(self.raw["m_BucketDataString"]))
        self._read_entries(_b64(self.raw["m_EntryDataString"]))
        self.by_key_index = {}
        for offset, entries in self.buckets:
            self.by_key_index.setdefault(self.offset_to_key[offset], []).extend(entries)

    def _read_keys(self, blob):
        count, = struct.unpack_from("<i", blob, 0)
        self.offset_to_key = {}
        self.keys = []
        i = 4
        for _ in range(count):
            self.offset_to_key[i] = len(self.keys)
            tag = blob[i]
            if tag == 0:
                length, = struct.unpack_from("<i", blob, i + 1)
                self.keys.append(blob[i + 5:i + 5 + length].decode("utf-8", "replace"))
                i += 5 + length
            elif tag == 1:
                self.keys.append(struct.unpack_from("<i", blob, i + 1)[0])
                i += 5
            else:
                raise ValueError("KeyData 里遇到未知的 tag %d @%d" % (tag, i))

    def _read_buckets(self, blob):
        count, = struct.unpack_from("<i", blob, 0)
        self.buckets = []
        i = 4
        for _ in range(count):
            offset, entry_count = struct.unpack_from("<ii", blob, i)
            i += 8
            entries = list(struct.unpack_from("<%di" % entry_count, blob, i))
            i += 4 * entry_count
            self.buckets.append((offset, entries))

    def _read_entries(self, blob):
        count, = struct.unpack_from("<i", blob, 0)
        self.entries = [struct.unpack_from("<7i", blob, 4 + 28 * k) for k in range(count)]

    def find(self, name):
        if name not in self.keys:
            return []
        return list(self.by_key_index.get(self.keys.index(name), []))

    def internal_id(self, entry_index):
        return self.ids[self.entries[entry_index][0]]

    def bundle_name(self, entry_index):
        dependency = self.entries[entry_index][2]
        if dependency < 0:
            return None
        for bundle_entry in self.by_key_index.get(dependency, []):
            if self.entries[bundle_entry][1] != 0:
                continue
            raw = os.path.basename(self.ids[self.entries[bundle_entry][0]].replace("\\", "/"))
            return raw[:-len(".bundle")] if raw.endswith(".bundle") else raw
        return None


def find_cached_bundle(bundle_name):
    root = os.path.join(ADDR, "AssetBundles")
    if not os.path.isdir(root):
        return None
    for first in os.listdir(root):
        candidate = os.path.join(root, first, bundle_name, "__data")
        if os.path.exists(candidate):
            return candidate
    return None


def main():
    out = []
    def say(s):
        out.append(s)

    say("catalog = " + CATALOG)
    say("catalog 在 = %s" % os.path.exists(CATALOG))
    if not os.path.exists(CATALOG):
        io.open(OUT, "w", encoding="utf-8").write("\n".join(out))
        print("\n".join(out))
        return 0

    cat = Catalog(CATALOG)
    say("key 数 = %d / internal id 数 = %d" % (len(cat.keys), len(cat.ids)))
    say("")

    names = sys.argv[1:] or WANT
    for name in names:
        hits = cat.find(name)
        lines = []
        for e in hits:
            lines.append((cat.bundle_name(e), cat.internal_id(e)))
        bundle = lines[0][0] if lines else None
        data = find_cached_bundle(bundle) if bundle else None
        say("%-32s entries=%-2d bundle=%-44s __data=%s"
            % (name, len(hits), str(bundle), "有" if data else "无"))

    # ★ 再反过来看：key 里有多少个 UT_ 前缀的，按前缀统计（判断"目录里到底登记了哪些贴图"）
    say("")
    say("—— catalog key 里 UT_ 前缀统计 ——")
    from collections import Counter
    c = Counter()
    for k in cat.keys:
        if not isinstance(k, str) or not k.startswith("UT_"):
            continue
        parts = k.split("_")
        c["_".join(parts[:2])] += 1
    for k, v in c.most_common(40):
        say("  %-28s %d" % (k, v))

    text = "\n".join(out)
    io.open(OUT, "w", encoding="utf-8").write(text)
    sys.stdout.write(text + "\n")
    return 0


if __name__ == "__main__":
    sys.exit(main())
