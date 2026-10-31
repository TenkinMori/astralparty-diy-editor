# -*- coding: utf-8 -*-
"""★★ 只读导出：把编辑器缺的贴图从游戏自己的 Addressables 包里抠出来，落到本地缓存。

用户第 9 波 ②：「把图标补全一点，太缺了」。
现状：`GameRes\\Img` 只是**散着的一部分**（格子功能 11 种、怪头像 80 只里 27 只、
      手牌 71 张里 23 张都没有 PNG），而那几张官方**压根有** ——
      它们在游戏自己的包里：
        `<游戏根>\\AstralParty_CN_Data\\StreamingAssets\\aa\\StandaloneWindows64\\<hash>.bundle`

这条工具干的就一件事：拿 `art_names.txt`（由 `DIY关卡编辑器.exe --art-list` 生成）
里的名字，去 Addressables 目录问"这张图在哪个包"，再把那个包**只读**打开、
把同名的 Texture2D 存成 PNG。

红线：
    ★ **游戏目录只读** —— 只 `open(path, "rb")`，一个字节都不写。
    ★ 产物只落 `5-DIY关卡\\贴图缓存\\`（+ 本目录下的报告）。
    ★ 没找到的**不瞎造** —— 如实记进报告，界面上落"色块 + 中文名"。

用法（在 5-DIY关卡\\tools 里跑）：
    python export_art_cache.py                 # 只导还缺的
    python export_art_cache.py --all           # 连游戏目录里已有的也一起导
    python export_art_cache.py --only UT_Platform_   # 只导名字带这个前缀的
    python export_art_cache.py --limit 20      # 先试 20 张
"""
import base64
import io
import json
import os
import struct
import sys
import time

HERE = os.path.dirname(os.path.abspath(__file__))
OWN = os.path.dirname(HERE)                       # 5-DIY关卡
CACHE = os.path.join(OWN, "贴图缓存")
NAMES = os.path.join(HERE, "art_names.txt")
REPORT = os.path.join(HERE, "art_cache_report.txt")

# ★ 只读地借用服务端那边现成的"找游戏目录"（它也只读）
SRV_TOOLS = r"D:\小小兔趴\4-服务端\tools"
if os.path.isdir(SRV_TOOLS):
    sys.path.insert(0, SRV_TOOLS)
sys.path.insert(0, os.path.join(HERE, "pylibs"))

LL = os.path.join(os.environ.get("USERPROFILE", ""), "AppData", "LocalLow",
                  "feimo", "AstralParty_CN")
ADDR = os.path.join(LL, "com.unity.addressables")


def game_dir():
    """游戏根（只读找；找不到回空串）。"""
    env = os.environ.get("ASTRAL_GAME_DIR")
    if env and os.path.isdir(env):
        return env
    try:
        import paths as P
        got = P.game_dir()
        if got:
            return got
    except Exception:
        pass
    direct = r"D:\steam\steamapps\common\Astral Party\8vJXn6CN"
    return direct if os.path.isdir(direct) else ""


def catalog_path():
    """现场最新那本 catalog（跳过 .modded / .prebak 备份）。"""
    best = None
    for name in os.listdir(ADDR):
        if not name.startswith("catalog_") or not name.endswith(".json"):
            continue
        if ".modded" in name or ".prebak" in name:
            continue
        key = [int(x) if x.isdigit() else x
               for x in name[len("catalog_"):-len(".json")].split(".")]
        if best is None or key > best[0]:
            best = (key, name)
    return os.path.join(ADDR, best[1]) if best else None


# ---------------------------------------------------------------- catalog 解析
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


def load_catalog(path):
    """→ {地址: 依赖的 bundle 文件名}（取 AssetBundleProvider 那条依赖的 internalId 基名）。"""
    raw = json.load(io.open(path, encoding="utf-8"))
    ids = raw["m_InternalIds"]
    kd = _b64(raw["m_KeyDataString"])
    buckets = _read_buckets(_b64(raw["m_BucketDataString"]))
    entries = _read_entries(_b64(raw["m_EntryDataString"]))
    bucket_keys = [(_read_key(kd, off) or ("@%d" % off)) for off, _ in buckets]

    by_key = {}
    for i, (_off, idxs) in enumerate(buckets):
        for e in idxs:
            by_key.setdefault(bucket_keys[i], []).append(e)

    out = {}
    for key, idxs in by_key.items():
        for ei in idxs:
            e = entries[ei]
            dep = bucket_keys[e[2]] if e[2] >= 0 else None
            if not dep or not dep.endswith(".bundle"):
                continue
            out.setdefault(key, dep)
            break
    return out, len(by_key)


# ---------------------------------------------------------------- 导出
def export_one(bundle_path, want, out_dir):
    """从一个包里把 `want` 里的 Texture2D 存成 PNG。→ {名字: 字节数}。"""
    import UnityPy
    got = {}
    env = UnityPy.load(bundle_path)
    for obj in env.objects:
        try:
            if obj.type.name != "Texture2D":
                continue
            data = obj.read()
        except Exception:
            continue
        name = getattr(data, "m_Name", None)
        if not name or name not in want or name in got:
            continue
        try:
            img = data.image
        except Exception:
            img = None
        if img is None:
            continue
        path = os.path.join(out_dir, name + ".png")
        img.save(path)
        got[name] = os.path.getsize(path)
    return got


def main(argv):
    only = None
    limit = 0
    include_have = "--all" in argv
    for i, a in enumerate(argv):
        if a == "--only" and i + 1 < len(argv):
            only = argv[i + 1]
        if a == "--limit" and i + 1 < len(argv):
            limit = int(argv[i + 1])

    log = []
    t0 = time.time()

    def say(s):
        log.append(s)
        print(s)
        sys.stdout.flush()

    g = game_dir()
    cat_path = catalog_path() if os.path.isdir(ADDR) else None
    bundles = os.path.join(g, "AstralParty_CN_Data", "StreamingAssets", "aa",
                           "StandaloneWindows64") if g else ""
    img_dir = os.path.join(g, "GameRes", "Img") if g else ""

    say("游戏根      = %s" % (g or "(没找到)"))
    say("游戏包目录  = %s（存在 = %s）" % (bundles, os.path.isdir(bundles)))
    say("catalog     = %s" % cat_path)
    say("产物缓存    = %s" % CACHE)
    say("")

    if not os.path.exists(NAMES):
        say("★ 先跑 `DIY关卡编辑器.exe --art-list \"%s\"` 生成要用的名字清单。" % NAMES)
        return 2
    if not cat_path or not os.path.isdir(bundles):
        say("★ 没有 catalog 或游戏包目录 ⇒ 导不了（界面上会落「色块 + 中文名」）。")
        return 3

    want_rows = []
    for line in io.open(NAMES, encoding="utf-8"):
        line = line.rstrip("\n")
        if not line or line.startswith("#"):
            continue
        parts = line.split("\t")
        if parts and parts[0]:
            want_rows.append(parts)
    say("要用的贴图名 %d 个" % len(want_rows))

    cat, key_count = load_catalog(cat_path)
    say("catalog 地址 %d 条（能解到 bundle 的 %d 条）" % (key_count, len(cat)))

    os.makedirs(CACHE, exist_ok=True)

    # 分类：已有缓存 / 游戏目录里就有 / 得从包里抠 / 官方压根没有
    plan = []
    have_cache = have_game = no_key = 0
    for row in want_rows:
        name = row[0]
        if only and only not in name:
            continue
        if os.path.exists(os.path.join(CACHE, name + ".png")):
            have_cache += 1
            if not include_have:
                continue
        in_game = os.path.exists(os.path.join(img_dir, name + ".png")) if img_dir else False
        if in_game:
            have_game += 1
            if not include_have:
                continue
        dep = cat.get(name)
        if not dep:
            no_key += 1
            plan.append((name, None, row[1] if len(row) > 1 else "", "catalog"))
            continue
        plan.append((name, dep, row[1] if len(row) > 1 else "", ""))

    say("已在缓存 %d · 游戏目录里已有 %d · catalog 里查不到 %d" % (have_cache, have_game, no_key))
    say("这一趟要从包里抠 %d 张" % len([p for p in plan if p[1]]))
    if limit:
        plan = plan[:limit]
    say("")

    by_bundle = {}
    for name, dep, where, miss in plan:
        if dep:
            by_bundle.setdefault(dep, []).append(name)

    done = {}
    failed = []
    for k, (dep, names) in enumerate(sorted(by_bundle.items())):
        path = os.path.join(bundles, dep)
        if not os.path.exists(path):
            for n in names:
                failed.append((n, "包不在游戏目录里：" + dep))
            continue
        try:
            got = export_one(path, set(names), CACHE)
        except Exception as ex:
            for n in names:
                failed.append((n, "包读不出来（%s）：%s" % (dep, ex)))
            continue
        done.update(got)
        missing = [n for n in names if n not in got]
        for n in missing:
            failed.append((n, "包 %s 里没有同名的 Texture2D" % dep))
        say("  [%d/%d] %s → 拿到 %d / 要 %d"
            % (k + 1, len(by_bundle), dep[:12], len(got), len(names)))

    # —— 报告 ——
    say("")
    say("=" * 78)
    ok = 0
    for row in want_rows:
        n = row[0]
        if os.path.exists(os.path.join(CACHE, n + ".png")):
            ok += 1
    total = len(want_rows)
    say("现在能读到的：%d / %d（比动手前多 %d）" % (ok, total, ok - (have_cache + have_game - have_cache)))
    say("这一趟从包里抠出来：%d 张" % len(done))
    say("这一趟失败：%d 张" % len(failed))

    still = []
    for row in want_rows:
        n = row[0]
        if os.path.exists(os.path.join(CACHE, n + ".png")):
            continue
        if img_dir and os.path.exists(os.path.join(img_dir, n + ".png")):
            continue
        still.append((n, cat.get(n), row[1] if len(row) > 1 else "",
                      row[2] if len(row) > 2 else ""))
    say("")
    say("—— ★ 还是缺的 %d 张（官方包里也没有 / 名字对不上）——" % len(still))
    for n, dep, where, cn in still:
        why = "catalog 里没登记这个名字" if not dep else ("包 %s 里没有" % dep[:12])
        say("  %-32s %-16s %-10s %s" % (n, cn, where, why))

    say("")
    say("用时 %.1f 秒" % (time.time() - t0))
    io.open(REPORT, "w", encoding="utf-8").write("\n".join(log))
    print("")
    print("报告：" + REPORT)
    return 0 if not failed else 1


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
