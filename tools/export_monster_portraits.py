# -*- coding: utf-8 -*-
"""★★ 只读导出：把每只怪的**立绘/头像**从游戏自己的 Addressables 包里抠出来，
按 `<monsterId>.png` 落到 `5-DIY关卡\\图标缓存\\`（编辑器优先读这个目录）。

资源链（已实测核对，见报告里的逐个 key）：
    `Monster.bin` 字段 21 → `Skin.bin` 顶层行（f1 = 那个 id）→ 内层 f13 `CharacterLabel`
    （怪是 `Monster1005`、角色是 `Hero101`）
        ↓
    ① 首选 key `<CharacterLabel>_PC`（真·立绘 / PC sprite）
    ② 没有这个 key ⇒ `UT_Monster_Bust_<id>_0`（官方半身像，也就是"立绘"那一张）
    ③ 再没有 ⇒ `UT_Monster_Card_<id>`（官方手牌画）
    ④ 全没有 ⇒ 如实报"缺"，不瞎造

★ 为什么要 ①→②→③ 的退路：本机 catalog（3.3.0）里 `_PC` 结尾的 key 只有
  `Hero1xx_Walk_PC` 那一族；**怪**只有 1068 一张 `Monster1068/PC/...`；
  所以 `Monster10xx_PC` 这个写法在**这一版的 catalog 里根本不存在**（不是资源没下）。
  ⇒ 立绘实际取的是 ②官方半身像（`UT_Monster_Bust_<id>_0`，即 Skin.bin f15 登记的那张）。

红线：
  * 游戏目录 / `%LocalLow%` catalog **只读**（只 open rb）；
  * 产物只落 `5-DIY关卡\\图标缓存\\` + 本目录的报告；
  * **不联网**（catalog 与 6371 个 bundle 都在本机）。

用法（在 5-DIY关卡\\tools 里跑）：
    python export_monster_portraits.py                 # 只补还缺的
    python export_monster_portraits.py --force         # 全部重导
    python export_monster_portraits.py --only 1071,1074
    python export_monster_portraits.py --table-dir <另一份 gamedata>
"""
import io
import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
OWN = os.path.dirname(HERE)                      # 5-DIY关卡
CACHE = os.path.join(OWN, "图标缓存")
REPORT = os.path.join(HERE, "monster_portrait_report.txt")
DEFAULT_TABLE = r"C:\Users\DELL\Documents\AstralParty Next\测试服\server\data\gamedata"

sys.path.insert(0, HERE)
sys.path.insert(0, os.path.join(HERE, "pylibs"))
import export_art_cache as E  # noqa: E402

LOG = []


def say(s=""):
    LOG.append(str(s))
    print(s)
    sys.stdout.flush()


# ---------------------------------------------------------------- protobuf 小工具
def varint(buf, i):
    v = 0
    shift = 0
    while True:
        b = buf[i]
        i += 1
        v |= (b & 0x7F) << shift
        shift += 7
        if not (b & 0x80):
            return v, i


def parse_fields(buf):
    out = []
    i = 0
    n = len(buf)
    while i < n:
        key, i = varint(buf, i)
        num, wire = key >> 3, key & 7
        if wire == 0:
            v, i = varint(buf, i)
            out.append((num, wire, v))
        elif wire == 2:
            ln, i = varint(buf, i)
            out.append((num, wire, buf[i:i + ln]))
            i += ln
        elif wire == 5:
            out.append((num, wire, int.from_bytes(buf[i:i + 4], "little", signed=True)))
            i += 4
        elif wire == 1:
            out.append((num, wire, buf[i:i + 8]))
            i += 8
        else:
            break
    return out


def num_of(rec, fn, default=0):
    for w, v in rec.get(fn, []):
        if w in (0, 5):
            return v
    return default


def str_of(rec, fn):
    for w, v in rec.get(fn, []):
        if w == 2:
            try:
                t = v.decode("utf-8")
            except Exception:
                continue
            if t and all(32 <= ord(c) for c in t):
                return t
    return ""


def load_monsters(table_dir):
    """→ [{id, name_id, skin_id, label, bust, card}]"""
    data = io.open(os.path.join(table_dir, "Monster.bin"), "rb").read()
    out = []
    for num, wire, val in parse_fields(data):
        if num != 1 or wire != 2:
            continue
        rec = {}
        for n2, w2, v2 in parse_fields(val):
            rec.setdefault(n2, []).append((w2, v2))
        mid = num_of(rec, 1, 0)
        if not mid:
            continue
        out.append(dict(id=mid, name_id=num_of(rec, 3), skin_id=num_of(rec, 21),
                        label="", bust="", card=""))
    return out


def load_skins(table_dir):
    """Skin.bin：顶层 f1 行（怪）；行内 f2 是明细，f13 = CharacterLabel，f15 = 半身像，f7 = 手牌画。"""
    data = io.open(os.path.join(table_dir, "Skin.bin"), "rb").read()
    out = {}
    for num, wire, val in parse_fields(data):
        if wire != 2:
            continue
        rec = {}
        for n2, w2, v2 in parse_fields(val):
            rec.setdefault(n2, []).append((w2, v2))
        key = num_of(rec, 1, None)
        if key is None:
            continue
        # 明细 f2（可能有多条：不同皮肤）—— 取第一条带 f13 的
        best = None
        for w, v in rec.get(2, []):
            if w != 2:
                continue
            sub = {}
            for n3, w3, v3 in parse_fields(v):
                sub.setdefault(n3, []).append((w3, v3))
            if not str_of(sub, 13):
                continue
            label = str_of(sub, 13)
            bust = ""
            for w4, v4 in sub.get(15, []):
                if w4 == 2:
                    try:
                        t = v4.decode("utf-8")
                    except Exception:
                        continue
                    if t.startswith("UT_Monster_Bust_"):
                        bust = t
                        break
            if not bust:
                bust = str_of(sub, 15)
            card = str_of(sub, 7)
            best = dict(label=label, bust=bust, card=card)
            break
        if best:
            out[key] = best
    return out


def main(argv):
    table = DEFAULT_TABLE
    force = False
    only = None
    for i, a in enumerate(argv):
        if a == "--table-dir" and i + 1 < len(argv):
            table = argv[i + 1]
        if a == "--force":
            force = True
        if a == "--only" and i + 1 < len(argv):
            only = set(int(x) for x in argv[i + 1].split(",") if x.strip().isdigit())

    g = E.game_dir()
    cat_path = E.catalog_path()
    bundles = os.path.join(g, "AstralParty_CN_Data", "StreamingAssets", "aa",
                           "StandaloneWindows64")
    say("表目录   = %s" % table)
    say("游戏根   = %s" % g)
    say("catalog  = %s" % cat_path)
    say("包目录   = %s（%s）" % (bundles, "在" if os.path.isdir(bundles) else "★不在"))
    say("产物     = %s" % CACHE)
    say("")
    if not os.path.isdir(table):
        say("★ 表目录不存在，退出")
        return 2
    cat, keys = E.load_catalog(cat_path)
    say("catalog 地址 %d 条（解到 bundle 的 %d 条）" % (keys, len(cat)))
    os.makedirs(CACHE, exist_ok=True)

    mons = load_monsters(table)
    skins = load_skins(table)
    say("Monster.bin 怪 %d 只 · Skin.bin 明细 %d 条" % (len(mons), len(skins)))
    say("")

    plan = {}       # bundle -> [(name, monsterId)]
    rows = []       # 报告行
    for m in mons:
        mid = m["id"]
        if only and mid not in only:
            continue
        have = os.path.join(CACHE, "%d.png" % mid)
        if os.path.exists(have) and not force:
            rows.append((mid, "（已有）", "图标缓存\\%d.png" % mid, "跳过"))
            continue
        sk = skins.get(m["skin_id"]) or {}
        label = sk.get("label", "")
        want = []
        if label:
            want.append(label + "_PC")
        if sk.get("bust"):
            want.append(sk["bust"])
        if m.get("id"):
            want.append("UT_Monster_Bust_%d_0" % mid)
        if sk.get("card"):
            want.append(sk["card"])
        want.append("UT_Monster_Card_%d" % mid)
        hit = None
        for w in want:
            if w and w in cat:
                hit = w
                break
        if not hit:
            rows.append((mid, label or "（Skin 无 f13）", "★缺", "catalog 里没有可用的 key：" + ", ".join(want)))
            continue
        plan.setdefault(cat[hit], []).append((hit, mid))
        rows.append((mid, label or "（无）", hit, cat[hit][:12]))

    say("要抠 %d 张（%d 个包）" % (sum(len(v) for v in plan.values()), len(plan)))
    tmpdir = os.path.join(CACHE, "_tmp")
    os.makedirs(tmpdir, exist_ok=True)
    got = {}
    failed = []
    for dep, items in sorted(plan.items()):
        path = os.path.join(bundles, dep)
        if not os.path.exists(path):
            for name, mid in items:
                failed.append((mid, name, "包不在游戏目录：" + dep))
            continue
        by_name = {}
        for name, mid in items:
            by_name.setdefault(name, []).append(mid)
        try:
            one = E.export_one(path, set(by_name.keys()), tmpdir)
        except Exception as ex:
            for name, mid in items:
                failed.append((mid, name, "包读不出来（%s）：%s" % (dep, ex)))
            continue
        for name, mids in by_name.items():
            tmp = os.path.join(CACHE, "_tmp", name + ".png")
            if name not in one or not os.path.exists(tmp):
                for mid in mids:
                    failed.append((mid, name, "包 %s 里没有同名 Texture2D" % dep[:12]))
                continue
            for mid in mids:
                dst = os.path.join(CACHE, "%d.png" % mid)
                if os.path.exists(dst) and not force:
                    continue
                with io.open(tmp, "rb") as f:
                    blob = f.read()
                with io.open(dst, "wb") as f:
                    f.write(blob)
                got[mid] = (name, len(blob))
        say("  %-16s → %d 张" % (dep[:16], len(one)))

    # 清临时目录
    tmpdir = os.path.join(CACHE, "_tmp")
    if os.path.isdir(tmpdir):
        for f in os.listdir(tmpdir):
            try:
                os.remove(os.path.join(tmpdir, f))
            except Exception:
                pass
        try:
            os.rmdir(tmpdir)
        except Exception:
            pass

    say("")
    say("=" * 78)
    say("这一趟落盘 %d 张 · 失败 %d 条" % (len(got), len(failed)))
    if failed:
        for mid, name, why in failed:
            say("  ★ 怪 %-5d %-30s %s" % (mid, name, why))
    say("")
    say("—— 逐只（%d 只）——" % len([r for r in rows]))
    for mid, label, src, note in rows:
        p = os.path.join(CACHE, "%d.png" % mid)
        size = os.path.getsize(p) if os.path.exists(p) else 0
        say("  怪 %-5d 标签 %-14s 用 %-32s %-12s %s"
            % (mid, label, src, note, ("%d 字节" % size) if size else "★没落盘"))
    have_n = len([1 for f in os.listdir(CACHE) if re.match(r"^\d+\.png$", f)])
    say("")
    say("图标缓存里现在有 %d 个 <id>.png" % have_n)
    io.open(REPORT, "w", encoding="utf-8", newline="\n").write("\n".join(LOG) + "\n")
    print("报告：" + REPORT)
    return 0 if not failed else 1


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
