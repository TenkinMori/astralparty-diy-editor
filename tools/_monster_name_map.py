# -*- coding: utf-8 -*-
"""★ 只读：把「像素怪头像 -> 怪 id -> 中文名」的最后一段补上。

前一步 `field 2` 猜成"名字 id"是错的（对不上 STRMonster 的键）。
这里把 Monster.bin 一行的**所有** wire5 整数字段与 STRMonster 的键集合求交集，
用交集字段来配名字；配不上的把候选都打出来，人工判断。
只读 4-服务端 的表；产物落 tools\\_monster_name_map.txt。
"""
import io
import os
import re
import sys
from collections import OrderedDict, Counter

HERE = os.path.dirname(os.path.abspath(__file__))
GD = r"D:\小小兔趴\4-服务端\server\data\gamedata"
CACHE = os.path.join(os.path.dirname(HERE), "贴图缓存")
OUT = os.path.join(HERE, "_monster_name_map.txt")
PAT = re.compile(r"^UT_AccountHeadShot_Monster_(\d+)$")
LOG = []


def say(s):
    LOG.append(str(s))
    print(s)
    sys.stdout.flush()


def parse_fields(buf):
    out, i, n = [], 0, len(buf)
    while i < n:
        key = shift = 0
        while True:
            if i >= n:
                return out
            b = buf[i]; i += 1
            key |= (b & 0x7F) << shift; shift += 7
            if not (b & 0x80):
                break
        num, wire = key >> 3, key & 7
        if wire == 0:
            v = shift = 0
            while True:
                if i >= n:
                    return out
                b = buf[i]; i += 1
                v |= (b & 0x7F) << shift; shift += 7
                if not (b & 0x80):
                    break
            out.append((num, wire, v))
        elif wire == 2:
            ln = shift = 0
            while True:
                if i >= n:
                    return out
                b = buf[i]; i += 1
                ln |= (b & 0x7F) << shift; shift += 7
                if not (b & 0x80):
                    break
            out.append((num, wire, buf[i:i + ln])); i += ln
        elif wire == 5:
            if i + 4 > n:
                return out
            out.append((num, wire, int.from_bytes(buf[i:i + 4], "little", signed=True))); i += 4
        elif wire == 1:
            if i + 8 > n:
                return out
            out.append((num, wire, buf[i:i + 8])); i += 8
        else:
            return out
    return out


def unwrap(buf, top):
    res = []
    for num, wire, val in parse_fields(buf):
        if num != top or wire != 2:
            continue
        key = body = None
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
    return t if t and all(32 <= ord(c) for c in t) and len(t) < 200 else None


def main():
    strs = io.open(os.path.join(GD, "STRMonster.bin"), "rb").read()
    names = {}
    for key, body in unwrap(strs, 2):
        for n2, w2, v2 in parse_fields(body or b""):
            if n2 == 2 and w2 == 2:
                t = text(v2)
                if t:
                    names[key] = t
                break
    say("STRMonster 键数 = %d；样例：%s"
        % (len(names), ", ".join("%s=%s" % (k, v) for k, v in list(names.items())[:6])))
    say("")

    mon = io.open(os.path.join(GD, "Monster.bin"), "rb").read()
    rows = []
    for key, body in unwrap(mon, 2):
        fs = parse_fields(body or b"")
        ints = {}
        head = None
        for n2, w2, v2 in fs:
            if w2 == 5:
                ints.setdefault(n2, v2)
            elif n2 == 19 and w2 == 2:
                head = text(v2)
        rows.append((key, ints, head))

    # 哪个字段最像"名字 id"：与 STRMonster 键的交集比例
    say("=" * 78)
    say("Monster.bin 里各 wire5 字段 与 STRMonster 键的交集（找名字 id 字段）")
    say("=" * 78)
    score = []
    for fnum in sorted(set(f for _, ints, _ in rows for f in ints)):
        vals = [ints[fnum] for _, ints, _ in rows if fnum in ints]
        hit = [v for v in vals if v in names]
        score.append((len(hit), fnum, len(vals)))
    for n, f, tot in sorted(score, reverse=True):
        say("   field %-3d  命中 %3d / %3d" % (f, n, tot))
    say("")

    best = None
    for n, f, tot in sorted(score, reverse=True):
        if n > 0:
            best = f
            break
    say("选定名字字段 = field %s" % best)
    say("")

    # 头像 -> 怪 id -> 名字
    byhead = OrderedDict()
    for key, ints, head in sorted(rows, key=lambda r: (str(r[2]), r[0])):
        if not head or not PAT.match(head):
            continue
        nm = names.get(ints.get(best)) if best else None
        byhead.setdefault(head, []).append((key, nm, ints))

    say("=" * 78)
    say("像素怪头像  ->  怪 id  ->  中文名")
    say("=" * 78)
    for head in sorted(byhead, key=lambda h: int(PAT.match(h).group(1))):
        who = byhead[head]
        say("   %-34s %s" % (head,
                             "; ".join("%d %s" % (k, n or "?") for k, n, _ in who)))
    say("")
    say("头像总数 = %d（Monster.bin 里被引用的）" % len(byhead))
    have = sorted(f[:-4] for f in os.listdir(CACHE) if PAT.match(f))
    say("本地包里有实体的 = %d" % len(have))
    miss = [h for h in byhead if h not in have]
    say("表里登记但**本地包没有**的 = %s"
        % (", ".join("%s(%s)" % (h, "/".join(str(k) for k, _, _ in byhead[h]))
                     for h in miss) or "无"))
    io.open(OUT, "w", encoding="utf-8").write("\n".join(LOG) + "\n")
    print("报告：" + OUT)


if __name__ == "__main__":
    main()
