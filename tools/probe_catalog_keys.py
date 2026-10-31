# -*- coding: utf-8 -*-
"""只读：把 Addressables catalog 的 key 全导出来看一眼（找有没有"格子/棋盘/关卡"相关的键）。

★ 只读：只 json.load 目录文件 + print，不写任何东西（输出用重定向落到 5-DIY关卡\\tools\\）。
用法：python tools\\probe_catalog_keys.py [关键字...]
"""
import io
import json
import os
import sys

CAT = os.path.join(
    os.environ.get("USERPROFILE", ""),
    "AppData", "LocalLow", "feimo", "AstralParty_CN",
    "com.unity.addressables", "catalog_3.2.0.json")


def b64(text):
    import base64
    return base64.b64decode(text)


def keys_of(path):
    raw = json.load(io.open(path, encoding="utf-8"))
    blob = b64(raw["m_KeyDataString"])
    import struct
    count, = struct.unpack_from("<i", blob, 0)
    out = []
    i = 4
    for _ in range(count):
        tag = blob[i]
        if tag == 0:
            length, = struct.unpack_from("<i", blob, i + 1)
            out.append(blob[i + 5:i + 5 + length].decode("utf-8", "replace"))
            i += 5 + length
        elif tag == 1:
            out.append(struct.unpack_from("<i", blob, i + 1)[0])
            i += 5
        else:
            raise ValueError("未知 tag %d @%d" % (tag, i))
    return out


def main(argv):
    keys = keys_of(CAT)
    print("catalog %s：%d 个 key" % (CAT, len(keys)))
    words = [a for a in argv if not a.startswith("-")]
    if not words:
        names = sorted(k for k in keys if isinstance(k, str))
        print("字符串 key 共 %d 个；前 80 个：" % len(names))
        for k in names[:80]:
            print("   " + k)
        return 0
    for w in words:
        hits = sorted(k for k in keys if isinstance(k, str) and w.lower() in k.lower())
        print("=== 含 %r 的 key：%d 个" % (w, len(hits)))
        for k in hits[:200]:
            print("   " + k)
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
