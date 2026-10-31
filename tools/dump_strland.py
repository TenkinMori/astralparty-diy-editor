# -*- coding: utf-8 -*-
"""只读：把 STRLand.bin 里 80000+landType 的**官方说明全文**+名字抠出来。

STRLand.bin 结构（与 gamedata.py / 本工程 GameTables.StrTable 一致）：
    顶层 repeated { f1 = key(int) , f2 = 文本(string) }
key 有三种形态：
    8000N / 8000N0 / 8000N00  ← 名字 / 短说明 / 长说明（实测见输出）
用法：python dump_strland.py
"""
import io
import os
import struct
import sys


def rv(d, i):
    raw, s = 0, 0
    while i < len(d):
        x = d[i]
        i += 1
        raw |= (x & 0x7F) << s
        if not (x & 0x80):
            break
        s += 7
    return raw, i


def fields(d):
    i = 0
    while i < len(d):
        try:
            k, i = rv(d, i)
        except Exception:
            return
        n, w = k >> 3, k & 7
        if n == 0:
            return
        if w == 0:
            v, i = rv(d, i)
            yield n, w, v
        elif w == 1:
            yield n, w, d[i:i + 8]
            i += 8
        elif w == 2:
            ln, i = rv(d, i)
            yield n, w, d[i:i + ln]
            i += ln
        elif w == 5:
            yield n, w, d[i:i + 4]
            i += 4
        else:
            return


def s32(b):
    return struct.unpack_from("<i", b, 0)[0]


def table(name):
    """{key: 文本}。顶层 repeated：f1=key(可能是 w0/w5)、f2=文本；也兼容 map 形态。"""
    path = os.path.join(r"D:\小小兔趴\4-服务端\server\data\gamedata", name + ".bin")
    data = open(path, "rb").read()
    out = {}
    for n, w, v in fields(data):
        if w != 2:
            continue
        key = None
        text = None
        for n2, w2, v2 in fields(v):
            if n2 == 1 and w2 in (0, 5):
                key = v2 if w2 == 0 else s32(v2)
            elif n2 == 2 and w2 == 2:
                try:
                    text = v2.decode("utf-8")
                except Exception:
                    text = None
        if key is not None and text is not None and key not in out:
            out[key] = text
    return out


def main():
    names = table("STRLand")
    print("STRLand.bin 条目 %d 个；key 范围 %s..%s"
          % (len(names), min(names), max(names)))
    print("")
    # 80000~80027 是"名字"，8000N0/8000N00 这类是说明：先把所有 8xxxx 的键按数值排出来看规律
    keys = sorted(k for k in names if 80000 <= k < 90000)
    print("80000~90000 的键（前 80 个）：%s" % keys[:80])
    print("")
    for land in range(0, 28):
        base = 80000 + land
        found = []
        for k in keys:
            if k == base or (k > base and str(k).startswith(str(base))):
                found.append((k, names[k]))
        if not found:
            continue
        print("=" * 74)
        print("landType %-3d" % land)
        for k, t in found:
            one = t.replace("\n", " / ")
            print("   [%d] %s" % (k, one))
    return 0


if __name__ == "__main__":
    sys.exit(main())
