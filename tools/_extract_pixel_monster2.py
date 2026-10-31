# -*- coding: utf-8 -*-
"""★ 只读抽取（补齐 + 清单）：像素怪物小人**全 48 张**
（`UT_AccountHeadShot_Monster_01..48`），并把怪物 id / 名字映射写成清单。

上一步只抠了 `Monster_01..45`（缓存里当时只有这 45 张的清单）。
Monster.bin 里其实登记到 `Monster_48`，这里把缺的 46/47/48 一并补上。

产物：
  5-DIY关卡\\贴图缓存\\UT_AccountHeadShot_Monster_XX.png   （原始像素，解码即存，不改字节）
  5-DIY关卡\\贴图缓存\\像素怪物小人清单.txt                  （序号 -> 怪 id -> 名字）
游戏目录只读。
"""
import io
import json
import os
import re
import sys
from collections import OrderedDict

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(HERE, "pylibs"))

BUNDLES = r"D:\steam\steamapps\common\Astral Party\8vJXn6CN\AstralParty_CN_Data\StreamingAssets\aa\StandaloneWindows64"
TSV = os.path.join(HERE, "_tex_inv.tsv")
GD = r"D:\小小兔趴\4-服务端\server\data\gamedata"
CACHE = os.path.join(os.path.dirname(HERE), "贴图缓存")
REPORT = os.path.join(HERE, "_monster_pixel_report2.txt")

PAT = re.compile(r"^UT_AccountHeadShot_Monster_(\d+)$")
LOG = []


def say(s):
    LOG.append(str(s))
    print(s)
    sys.stdout.flush()


# ---- 复用 _monster_id_map.py 的解析（同样的线格式读法）----
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


def monster_tables():
    mon = io.open(os.path.join(GD, "Monster.bin"), "rb").read()
    strs = io.open(os.path.join(GD, "STRMonster.bin"), "rb").read()
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
        name_id = head = None
        for n2, w2, v2 in parse_fields(body or b""):
            if n2 == 4 and w2 == 5:
                name_id = v2
            elif n2 == 19 and w2 == 2:
                head = text(v2)
        rows.append((key, name_id, head))
    return rows, name_of


def main():
    rows, name_of = monster_tables()
    byhead = OrderedDict()
    for key, name_id, head in sorted(rows, key=lambda r: (str(r[2]), r[0])):
        if head and PAT.match(head):
            byhead.setdefault(head, []).append((key, name_id))
    say("Monster.bin 登记的像素怪头像（UT_AccountHeadShot_Monster_*）= %d 个名字"
        % len(byhead))

    allrows = [l.rstrip("\n").split("\t") for l in io.open(TSV, encoding="utf-8")]
    want = {}
    for nm, w, h, fmt, bn in allrows:
        if PAT.match(nm):
            want.setdefault(bn, []).append(nm)
    say("全库清单里这一族 = %d 张，分布在 %d 个包"
        % (sum(len(v) for v in want.values()), len(want)))

    import UnityPy
    os.makedirs(CACHE, exist_ok=True)
    done = []
    for bn, items in sorted(want.items()):
        p = os.path.join(BUNDLES, bn)
        if not os.path.exists(p):
            continue
        env = UnityPy.load(p)
        names = set(items)
        for o in env.objects:
            if o.type.name != "Texture2D":
                continue
            try:
                d = o.read()
            except Exception:
                continue
            nm = getattr(d, "m_Name", None)
            if nm not in names:
                continue
            try:
                im = d.image
            except Exception:
                continue
            if im is None:
                continue
            im = im.convert("RGBA")
            out = os.path.join(CACHE, nm + ".png")
            im.save(out, "PNG")
            done.append((nm, im.width, im.height, bn, os.path.getsize(out)))
            names.discard(nm)
        for nm in sorted(names):
            say("  !! 包里没读到 %s（%s）" % (nm, bn))

    done.sort()
    say("")
    say("落地/复核 = %d 张" % len(done))

    # ---- 清单 ----
    lines = []
    lines.append("# 像素风怪物小人（「图鉴」那一套）")
    lines.append("# 源：游戏 Addressables 包里的 Texture2D  UT_AccountHeadShot_Monster_XX")
    lines.append("# 名字与 id 的对应来自 4-服务端\\server\\data\\gamedata\\Monster.bin 的 field 19")
    lines.append("# 字段：序号\t怪物id(可多个)\t怪物名ID\t图片尺寸\t落地文件")
    for nm, w, h, bn, sz in done:
        idx = PAT.match(nm).group(1)
        who = byhead.get(nm, [])
        ids = ",".join(str(k) for k, _ in who) or "-"
        nids = ",".join(str(n) for _, n in who) or "-"
        names = " / ".join(name_of.get(n, "?") for _, n in who) or "-"
        lines.append("%s\t%s\t%s\t%dx%d\t%s.png\t%s"
                     % (idx, ids, nids, w, h, nm, names))
    man = os.path.join(CACHE, "像素怪物小人清单.txt")
    io.open(man, "w", encoding="utf-8").write("\n".join(lines) + "\n")
    say("清单：%s" % man)
    say("")

    for nm, w, h, bn, sz in done:
        idx = PAT.match(nm).group(1)
        who = byhead.get(nm, [])
        names = " / ".join(name_of.get(n, "?") for _, n in who) or "-"
        say("  %-38s %3dx%-3d 怪 id %-16s %s"
            % (nm, w, h, ",".join(str(k) for k, _ in who) or "-", names))

    io.open(REPORT, "w", encoding="utf-8").write("\n".join(LOG) + "\n")
    say("")
    say("报告：%s" % REPORT)
    return 0


if __name__ == "__main__":
    sys.exit(main())
