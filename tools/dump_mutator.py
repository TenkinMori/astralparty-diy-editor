# -*- coding: utf-8 -*-
"""只读：把 Mutator.bin / STRMutator.bin / Card.bin 的形状 dump 出来（确认字段号 + 名字 + 图标）。"""
import os
import struct
import sys

TABLE = r"D:\小小兔趴\4-服务端\server\data\gamedata"


def varint(data, i):
    v = 0
    shift = 0
    while i < len(data) and shift < 64:
        b = data[i]
        i += 1
        v |= (b & 0x7F) << shift
        if not (b & 0x80):
            return v, i
        shift += 7
    return v, i


def fields(data):
    out = []
    i = 0
    while i < len(data):
        key, i = varint(data, i)
        num = key >> 3
        wire = key & 7
        if num <= 0:
            break
        if wire == 0:
            v, i = varint(data, i)
            out.append((num, wire, v, None))
        elif wire == 5:
            out.append((num, wire, struct.unpack_from("<i", data, i)[0], None))
            i += 4
        elif wire == 1:
            out.append((num, wire, struct.unpack_from("<q", data, i)[0], None))
            i += 8
        elif wire == 2:
            ln, i = varint(data, i)
            out.append((num, wire, None, data[i:i + ln]))
            i += ln
        else:
            break
    return out


def fixed32s(blob):
    if not blob:
        return []
    if len(blob) % 4 == 0:
        vals = list(struct.unpack("<%di" % (len(blob) // 4), blob))
        if all(0 <= v <= 10 ** 7 for v in vals):
            return vals
    out = []
    i = 0
    while i < len(blob):
        raw, i = varint(blob, i)
        out.append((raw >> 1) ^ -(raw & 1))
    return out


def strtable(name):
    path = os.path.join(TABLE, name)
    table = {}
    if not os.path.exists(path):
        return table
    with open(path, "rb") as fh:
        data = fh.read()
    for num, wire, _v, blob in fields(data):
        if wire != 2 or blob is None:
            continue
        key, text = None, None
        for k, w, v, b in fields(blob):
            if k == 1 and w in (0, 5):
                key = v
            elif k == 2 and w == 2 and b is not None:
                try:
                    text = b.decode("utf-8")
                except Exception:
                    text = None
        if key is None or not text:
            continue
        if any(ord(c) < 32 for c in text):
            continue
        table.setdefault(key, text)
    return table


def dump_rows(fname, topfield, limit=200):
    path = os.path.join(TABLE, fname)
    with open(path, "rb") as fh:
        data = fh.read()
    print("=== %s 顶层 (号,线型) 计数：%s" % (fname, {}))
    from collections import Counter
    tops = fields(data)
    print("   ", Counter((n, w) for n, w, _, _ in tops))
    rows = []
    for n, w, _v, blob in tops:
        if w != 2 or blob is None:
            continue
        if topfield and n != topfield:
            continue
        rows.append(list(fields(blob)))
    print("   行数 %d；前 2 行的字段：" % len(rows))
    for r in rows[:2]:
        print("     ", [(k, ww, vv, (len(bb) if bb else None)) for k, ww, vv, bb in r])
    return rows


def main():
    rows = dump_rows("Mutator.bin", 1)
    names = strtable("STRMutator.bin")
    print("\n=== Mutator 表（id / NameID / 图标字段 / STRMutator 名字）")
    n_hit = 0
    for r in rows:
        rid = next((vv for k, ww, vv, _b in r if k == 1 and ww in (0, 5)), None)
        nid = next((vv for k, ww, vv, _b in r if k == 2 and ww in (0, 5)), None)
        icon = next((bb for k, ww, _v, bb in r if k == 5 and ww == 2), None)
        icon_s = None
        if icon:
            try:
                icon_s = icon.decode("utf-8")
            except Exception:
                icon_s = "<bin>"
        nm = names.get(nid, "") if nid is not None else ""
        if nm:
            n_hit += 1
        print("  id=%s NameID=%s 图标=%s 名字=%s" % (rid, nid, icon_s, nm or "(无)"))
    print("有名字 %d / %d" % (n_hit, len(rows)))
    print("\nSTRMutator 总条目 = %d" % len(names))

    cards = dump_rows("Card.bin", 1, 3)
    cnames = strtable("STRCard.bin")
    print("\n=== Card 前 6 张（id / NameID / 名字）")
    for r in cards[:6]:
        rid = next((vv for k, ww, vv, _b in r if k == 1 and ww in (0, 5)), None)
        nid = next((vv for k, ww, vv, _b in r if k == 2 and ww in (0, 5)), None)
        print("  id=%s NameID=%s 名字=%s" % (rid, nid, cnames.get(nid, "")))
    print("Card 行数 = %d ；STRCard 条目 = %d" % (len(cards), len(cnames)))


if __name__ == "__main__":
    sys.stdout.reconfigure(encoding="utf-8")
    main()
