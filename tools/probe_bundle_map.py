# -*- coding: utf-8 -*-
"""★ 只读侦察（第二步）：catalog 里一条地址 -> 它依赖的 bundle -> 现场缓存位上的 __data。

关键未知量：Addressables 的 `AssetBundleRequestOptions.m_Hash`（32 位 hex）到底是不是
`AssetBundles\\<hash1>\\<hash2>\\__data` 里的某一层。这里把两端都打出来对。
**只读，一个字节都不写**（除 5-DIY关卡\\tools\\probe_bundle_map.txt）。
"""
import base64
import io
import json
import os
import re
import struct
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
LL = os.path.join(os.environ.get("USERPROFILE", ""), "AppData", "LocalLow",
                  "feimo", "AstralParty_CN")
ADDR = os.path.join(LL, "com.unity.addressables")
CATALOG = os.path.join(ADDR, "catalog_3.2.0.json")
OUT = os.path.join(HERE, "probe_bundle_map.txt")

WANT = ["UT_Platform_Gift", "UT_Platform_Shop", "UT_Monster_Bust_1003_0",
        "UT_HandCard_10001", "Map_023", "UT_Platform_108"]


def _b64(t):
    return base64.b64decode(t)


def _read_key(blob, offset):
    if offset < 0 or offset >= len(blob):
        return None
    tag = blob[offset]
    if tag not in (0, 1):
        return None
    length = struct.unpack_from("<i", blob, offset + 1)[0]
    try:
        return blob[offset + 5:offset + 5 + length].decode("utf-8")
    except Exception:
        return None


def _read_buckets(blob):
    count = struct.unpack_from("<i", blob, 0)[0]
    offset = 4
    out = []
    for _ in range(count):
        data_offset = struct.unpack_from("<i", blob, offset)[0]
        offset += 4
        entries = struct.unpack_from("<i", blob, offset)[0]
        offset += 4
        idx = list(struct.unpack_from("<%di" % entries, blob, offset))
        offset += 4 * entries
        out.append((data_offset, idx))
    return out


def _read_entries(blob):
    count = struct.unpack_from("<i", blob, 0)[0]
    return [list(struct.unpack_from("<7i", blob, 4 + 28 * i)) for i in range(count)]


def main():
    out = []

    def say(s):
        out.append(s)

    raw = json.load(io.open(CATALOG, encoding="utf-8"))
    ids = raw["m_InternalIds"]
    providers = raw["m_ProviderIds"]
    types = [t.get("m_ClassName") for t in (raw.get("m_resourceTypes") or [])]
    extra = _b64(raw.get("m_ExtraDataString") or "")
    kd = _b64(raw["m_KeyDataString"])
    buckets = _read_buckets(_b64(raw["m_BucketDataString"]))
    entries = _read_entries(_b64(raw["m_EntryDataString"]))
    bucket_keys = [(_read_key(kd, off) or ("@%d" % off)) for off, _ in buckets]

    say("catalog = %s" % CATALOG)
    say("extra 字节 = %d" % len(extra))
    say("")

    by_key = {}
    for i, (_off, idxs) in enumerate(buckets):
        for e in idxs:
            by_key.setdefault(bucket_keys[i], []).append(e)

    for name in (sys.argv[1:] or WANT):
        say("=== %s ===" % name)
        for ei in by_key.get(name, []):
            e = entries[ei]
            internal = ids[e[0]] if 0 <= e[0] < len(ids) else None
            provider = providers[e[1]] if 0 <= e[1] < len(providers) else None
            dep = bucket_keys[e[2]] if e[2] >= 0 else None
            say("  internalId = %s" % internal)
            say("  provider   = %s" % provider)
            say("  dep(bundle)= %s" % dep)
            say("  dep_hash   = %d" % e[3])
            say("  extra@%d" % e[4])
            if 0 <= e[4] < len(extra):
                blob = extra[e[4]:e[4] + 400]
                say("    hex = %s" % blob[:64].hex())
                say("    str = %r" % blob[:200])
            say("  type       = %s" % (types[e[6]] if 0 <= e[6] < len(types) else None))
            # 依赖那条 bundle location
            if dep:
                for bi in by_key.get(dep, []):
                    be = entries[bi]
                    say("    [bundle loc] internalId = %s" % (ids[be[0]] if 0 <= be[0] < len(ids) else None))
                    say("    [bundle loc] provider   = %s" % (providers[be[1]] if 0 <= be[1] < len(providers) else None))
                    say("    [bundle loc] dep        = %s" % (bucket_keys[be[2]] if be[2] >= 0 else None))
                    say("    [bundle loc] extra@%d" % be[4])
                    if 0 <= be[4] < len(extra):
                        blob = extra[be[4]:be[4] + 400]
                        say("      hex = %s" % blob[:80].hex())
                        say("      str = %r" % blob[:260])
                        for m in re.finditer(rb"[0-9a-f]{32}", blob):
                            say("      ★ 32hex 候选 = %s" % m.group(0).decode())
        say("")

    text = "\n".join(out)
    io.open(OUT, "w", encoding="utf-8").write(text)
    sys.stdout.write(text + "\n")
    return 0


if __name__ == "__main__":
    sys.exit(main())
