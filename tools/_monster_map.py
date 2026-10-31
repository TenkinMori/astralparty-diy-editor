# -*- coding: utf-8 -*-
"""★ 只读侦察：把 Monster.bin / STRMonster.bin 解出来，
看看 `UT_AccountHeadShot_Monster_01..45` 这 45 张头像能不能对上怪物 id。

思路：
  Monster.bin 是 key->body 的 map；先把所有 entry 的 key 与各字段原值 dump 出来，
  找有没有指向 `UT_AccountHeadShot_Monster_*` 的字符串字段（或者能算出来的下标）。
  STRMonster.bin 是 id->名字，用来给对上的 id 配中文名。

只读（这些表在 4-服务端 目录，只读打开）；产物落 tools\\_monster_map.txt。
"""
import io
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
GD = r"D:\小小兔趴\4-服务端\server\data\gamedata"
OUT = os.path.join(HERE, "_monster_map.txt")
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
    """顶层 map<key, body>；两层 MapEntry 都钻。"""
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


def main():
    for fn in ("Monster.bin", "STRMonster.bin"):
        p = os.path.join(GD, fn)
        data = io.open(p, "rb").read()
        say("=" * 78)
        say("%s（%d 字节）" % (fn, len(data)))
        say("=" * 78)
        top = {}
        for num, wire, val in parse_fields(data):
            top.setdefault((num, wire), []).append(val)
        for (num, wire), vals in sorted(top.items()):
            say("  顶层 field %-3d wire %d  ×%d" % (num, wire, len(vals)))
        say("")

        for tf in sorted(set(n for n, w in top)):
            rs = unwrap(data, tf)
            if not rs:
                continue
            say("  —— field %d 的 entry 数 = %d ——" % (tf, len(rs)))
            for key, body in rs[:6]:
                fs = parse_fields(body or b"")
                desc = []
                for n2, w2, v2 in fs:
                    if w2 == 2:
                        try:
                            t = v2.decode("utf-8")
                            if t and all(32 <= ord(c) for c in t) and len(t) < 80:
                                desc.append("f%d=%r" % (n2, t))
                                continue
                        except Exception:
                            pass
                        desc.append("f%d=bytes[%d]" % (n2, len(v2)))
                    else:
                        desc.append("f%d=%s" % (n2, v2))
                say("      key %-8s  %s" % (key, "  ".join(desc)))
            if len(rs) > 6:
                say("      ...（共 %d 条）" % len(rs))
            say("")
        say("")

    io.open(OUT, "w", encoding="utf-8").write("\n".join(LOG) + "\n")
    print("报告：" + OUT)


if __name__ == "__main__":
    main()
