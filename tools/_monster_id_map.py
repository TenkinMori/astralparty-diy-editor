# -*- coding: utf-8 -*-
"""★ 只读：把 Monster.bin 的 field 19（`UT_AccountHeadShot_Monster_XX`）
与怪物 id、名字对上，产出「像素小人 -> 怪 id -> 中文名」总表。

同时把没有怪物引用的那几张（如果有）标出来。
只读 4-服务端 的表；产物落 tools\\_monster_id_map.txt。
"""
import io
import os
import re
import sys
from collections import OrderedDict

HERE = os.path.dirname(os.path.abspath(__file__))
GD = r"D:\小小兔趴\4-服务端\server\data\gamedata"
OUT = os.path.join(HERE, "_monster_id_map.txt")
CACHE = os.path.join(os.path.dirname(HERE), "贴图缓存")
LOG = []


def say(s):
    LOG.append(str(s))
    print(s)
    sys.stdout.flush()


def parse_fields(buf):
    out = []
    i = 0
    n = len(buf)
    while i < n:
        key = 0
        shift = 0
        while True:
            if i >= n:
                return out
            b = buf[i]
            i += 1
            key |= (b & 0x7F) << shift
            shift += 7
            if not (b & 0x80):
                break
        num, wire = key >> 3, key & 7
        if wire == 0:
            v = 0
            shift = 0
            while True:
                if i >= n:
                    return out
                b = buf[i]
                i += 1
                v |= (b & 0x7F) << shift
                shift += 7
                if not (b & 0x80):
                    break
            out.append((num, wire, v))
        elif wire == 2:
            ln = 0
            shift = 0
            while True:
                if i >= n:
                    return out
                b = buf[i]
                i += 1
                ln |= (b & 0x7F) << shift
                shift += 7
                if not (b & 0x80):
                    break
            out.append((num, wire, buf[i:i + ln]))
            i += ln
        elif wire == 5:
            if i + 4 > n:
                return out
            out.append((num, wire, int.from_bytes(buf[i:i + 4], "little", signed=True)))
            i += 4
        elif wire == 1:
            if i + 8 > n:
                return out
            out.append((num, wire, buf[i:i + 8]))
            i += 8
        else:
            return out
    return out


def unwrap(buf, top):
    res = []
    for num, wire, val in parse_fields(buf):
        if num != top or wire != 2:
            continue
        key = None
        body = None
        sub = parse_fields(val)
        inner = None
        for n2, w2, v2 in sub:
            if n2 == 1 and w2 == 2:
                inner = parse_fields(v2)
        for n2, w2, v2 in (inner if inner is not None else sub):
            if n2 == 1 and w2 in (0, 5):
                key = v2
            elif n2 == 2 and w2 == 2:
                body = v2
        if key is not None:
            res.append((key, body))
    return res


def text(v):
    try:
        t = v.decode("utf-8")
    except Exception:
        return None
    if t and all(32 <= ord(c) for c in t) and len(t) < 200:
        return t
    return None


def main():
    mon = io.open(os.path.join(GD, "Monster.bin"), "rb").read()
    strs = io.open(os.path.join(GD, "STRMonster.bin"), "rb").read()

    # STRMonster: id -> 中文名（field 2）
    name_of = {}
    for key, body in unwrap(strs, 2):
        for n2, w2, v2 in parse_fields(body or b""):
            if n2 == 2 and w2 == 2:
                t = text(v2)
                if t:
                    name_of[key] = t
                break

    rows = []
    for key, body in unwrap(mon, 2):
        name_id = None
        head = None
        for n2, w2, v2 in parse_fields(body or b""):
            if n2 == 1 and w2 == 5:
                pass
            elif n2 == 2 and w2 == 5:
                name_id = v2
            elif n2 == 19 and w2 == 2:
                head = text(v2)
        rows.append((key, name_id, head))

    say("Monster.bin entry = %d" % len(rows))
    say("")
    say("=" * 86)
    say("像素小人  ->  怪物 id  ->  名字（Monster.bin field 19）")
    say("=" * 86)
    byhead = OrderedDict()
    for key, name_id, head in sorted(rows, key=lambda r: (str(r[2]), r[0])):
        if not head:
            continue
        byhead.setdefault(head, []).append((key, name_of.get(name_id, "?")))

    for head in sorted(byhead, key=lambda h: (h.split("_")[-1])):
        who = byhead[head]
        ids = ", ".join("%d(%s)" % (k, n) for k, n in who)
        say("  %-34s %d 个怪：%s" % (head, len(who), ids))
    say("")

    # 缓存里实际有哪些
    have = sorted(f for f in os.listdir(CACHE)
                  if re.match(r"^UT_AccountHeadShot_Monster_\d+\.png$", f))
    say("=" * 86)
    say("贴图缓存里已落地 %d 张" % len(have))
    say("=" * 86)
    missing = []
    for f in have:
        stem = f[:-4]
        who = byhead.get(stem)
        if who:
            say("  %-40s -> %s" % (f, ", ".join("%d %s" % (k, n) for k, n in who)))
        else:
            say("  %-40s -> （Monster.bin 里没有怪引用它）" % f)
            missing.append(f)
    say("")
    say("没被怪物表引用的：%s" % (", ".join(missing) if missing else "无"))
    io.open(OUT, "w", encoding="utf-8").write("\n".join(LOG) + "\n")
    print("报告：" + OUT)


if __name__ == "__main__":
    main()
