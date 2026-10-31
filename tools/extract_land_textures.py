# -*- coding: utf-8 -*-
"""★ 只读侦察：把「格子类型 landType」和「格子用的贴图名」对上号。

为什么需要它：
    `5-DIY关卡\棋盘缓存\<后缀>.json` 里只有几何 + landType，
    **没有**"这个 landType 该贴哪张 UT_Platform_*.png"。
    GameRes\Img 里散着 44 张 UT_Platform_*（含 25 张纯编号 101..129 / 301..306），
    光看文件名认不出谁是谁。只能回场景里读 UnitLand 的 Renderer。

和 `4-服务端\tools\extract_board.py` 的关系：
    ★ 那一份是**原件，只读，一个字不改**（红线）。
      这一份是**拷过来的分支**，只多干一件事：顺手把 Renderer 上的贴图名也导出来。
      定位逻辑（catalog -> bundle -> 缓存位 -> env.container 复核）原样照搬，
      因为它是对的：宁可失败，也不要静默导出错的对应表。

输出（★ 只写 5-DIY关卡\ 下面）：
    5-DIY关卡\tools\land_textures.json      landType -> [贴图名...] + 每个 landType 的样本
    控制台同时打一张人看的表。

用法（在 5-DIY关卡\tools 里跑）：
    python extract_land_textures.py            # 默认 Map_023
    python extract_land_textures.py 023 007
    python extract_land_textures.py --list
"""
import base64
import io
import json
import os
import struct
import sys

HERE = os.path.dirname(os.path.abspath(__file__))

# ★ 只读地借用服务端那边现成的定位工具（不 import 它们的写盘路径）
SRV_TOOLS = r"D:\小小兔趴\4-服务端\tools"
if os.path.isdir(SRV_TOOLS):
    sys.path.insert(0, SRV_TOOLS)

import paths  # noqa: E402  （服务端那份，只读用它的 ADDR）

CATALOG = os.path.join(paths.ADDR, "catalog_3.2.0.json")
OUT = os.path.join(HERE, "land_textures.json")


# ---------------------------------------------------------------- catalog
class Catalog(object):
    """Addressables 目录：把 key 名字解到「资源 id + 所在 bundle」。"""

    def __init__(self, path):
        self.raw = json.load(io.open(path, encoding="utf-8"))
        self.ids = self.raw["m_InternalIds"]
        self._read_keys(_b64(self.raw["m_KeyDataString"]))
        self._read_buckets(_b64(self.raw["m_BucketDataString"]))
        self._read_entries(_b64(self.raw["m_EntryDataString"]))
        self.by_key_index = {}
        for offset, entries in self.buckets:
            self.by_key_index.setdefault(self.offset_to_key[offset], []).extend(entries)

    def _read_keys(self, blob):
        count, = struct.unpack_from("<i", blob, 0)
        self.offset_to_key = {}
        self.keys = []
        i = 4
        for _ in range(count):
            self.offset_to_key[i] = len(self.keys)
            tag = blob[i]
            if tag == 0:
                length, = struct.unpack_from("<i", blob, i + 1)
                self.keys.append(blob[i + 5:i + 5 + length].decode("utf-8", "replace"))
                i += 5 + length
            elif tag == 1:
                self.keys.append(struct.unpack_from("<i", blob, i + 1)[0])
                i += 5
            else:
                raise ValueError("KeyData 里遇到未知的 tag %d @%d" % (tag, i))

    def _read_buckets(self, blob):
        count, = struct.unpack_from("<i", blob, 0)
        self.buckets = []
        i = 4
        for _ in range(count):
            offset, entry_count = struct.unpack_from("<ii", blob, i)
            i += 8
            entries = list(struct.unpack_from("<%di" % entry_count, blob, i))
            i += 4 * entry_count
            self.buckets.append((offset, entries))

    def _read_entries(self, blob):
        count, = struct.unpack_from("<i", blob, 0)
        self.entries = [struct.unpack_from("<7i", blob, 4 + 28 * k) for k in range(count)]

    def find(self, name):
        if name not in self.keys:
            return []
        return list(self.by_key_index.get(self.keys.index(name), []))

    def internal_id(self, entry_index):
        return self.ids[self.entries[entry_index][0]]

    def bundle_name(self, entry_index):
        dependency = self.entries[entry_index][2]
        if dependency < 0:
            return None
        for bundle_entry in self.by_key_index.get(dependency, []):
            if self.entries[bundle_entry][1] != 0:
                continue
            raw = os.path.basename(self.ids[self.entries[bundle_entry][0]].replace("\\", "/"))
            return raw[:-len(".bundle")] if raw.endswith(".bundle") else raw
        return None


def _b64(text):
    return base64.b64decode(text)


def find_cached_bundle(bundle_name):
    root = os.path.join(paths.ADDR, "AssetBundles")
    if not os.path.isdir(root):
        return None
    for first in os.listdir(root):
        candidate = os.path.join(root, first, bundle_name, "__data")
        if os.path.exists(candidate):
            return candidate
    return None


# ---------------------------------------------------------------- 场景
def extract(bundle_path, expect_container):
    """读出场景里全部带 nodeInfo 的 MonoBehaviour，连它的贴图名一起。

    贴图名怎么拿：MonoBehaviour -> m_GameObject -> GameObject.m_Component
    里找 Renderer（MeshRenderer / SkinnedMeshRenderer），
    Renderer.m_Materials[i].m_PathID -> Material -> m_SavedProperties
    里 TexEnv / _MainTex 的 PPtr -> Texture2D 的 m_Name。
    ★ 每一步都可能缺（有的格子是空物件、有的材质没贴图），缺就当 None，不猜。
    """
    import UnityPy
    env = UnityPy.load(bundle_path)
    containers = list((env.container or {}).keys())
    if expect_container not in containers:
        raise RuntimeError("bundle 里没有 %s（container=%r），定位错了"
                           % (expect_container, containers[:5]))

    trees = {}
    for obj in env.objects:
        if obj.type.name not in ("GameObject", "Transform", "MonoBehaviour",
                                 "MeshRenderer", "SkinnedMeshRenderer", "Material",
                                 "Texture2D"):
            continue
        try:
            trees[(obj.type.name, obj.path_id)] = obj.read_typetree()
        except Exception:
            pass

    def go_components(go_id):
        go = trees.get(("GameObject", go_id))
        if not go:
            return []
        out = []
        for component in go.get("m_Component", []):
            ref = (component.get("component") or component.get("second") or component)
            out.append(ref.get("m_PathID"))
        return out

    # 父子关系：Transform.m_Children -> 父 Transform 路径 id
    go_of_transform = {}    # Transform 路径 id -> GameObject 路径 id
    transform_of_go = {}    # GameObject 路径 id -> Transform 路径 id
    for (_kind, _pid), tree in trees.items():
        if _kind != "GameObject":
            continue
        for comp in go_components(_pid):
            if ("Transform", comp) in trees:
                go_of_transform[comp] = _pid
                transform_of_go[_pid] = comp
    parent_of = {}          # Transform 路径 id -> 父 Transform 路径 id
    for (_kind, _pid), tree in trees.items():
        if _kind != "Transform":
            continue
        for child in (tree.get("m_Children") or []):
            cid = (child or {}).get("m_PathID")
            if cid is not None:
                parent_of[cid] = _pid

    def ancestors(go_id):
        """自己 + 所有祖先 GameObject（贴图可能挂在子物件上，也可能挂在父物件上）。"""
        out = [go_id]
        tr = transform_of_go.get(go_id)
        seen = 0
        while tr is not None and seen < 12:
            seen += 1
            tr = parent_of.get(tr)
            if tr is None:
                break
            gid = go_of_transform.get(tr)
            if gid is None:
                break
            out.append(gid)
        return out

    def descendants(go_id, depth=0):
        out = [go_id]
        if depth > 6:
            return out
        tr = transform_of_go.get(go_id)
        if tr is None:
            return out
        tree = trees.get(("Transform", tr)) or {}
        for child in (tree.get("m_Children") or []):
            cid = (child or {}).get("m_PathID")
            gid = go_of_transform.get(cid)
            if gid is not None:
                out.extend(descendants(gid, depth + 1))
        return out

    def local_position(go_id):
        for comp in go_components(go_id):
            transform = trees.get(("Transform", comp))
            if transform:
                pos = transform.get("m_LocalPosition") or {}
                if isinstance(pos, dict):
                    return pos.get("x"), pos.get("y"), pos.get("z")
                return tuple(pos)[:3]
        return None

    def prop_texture_name(prop):
        """m_SavedProperties 的一条：{first: "名字", second: PPtr} 或 {_texEnv...}。"""
        if not isinstance(prop, dict):
            return None
        ptr = prop.get("second") or prop.get("value")
        if not isinstance(ptr, dict):
            return None
        tex = trees.get(("Texture2D", ptr.get("m_PathID")))
        if not tex:
            return None
        name = tex.get("m_Name")
        return name if isinstance(name, str) else None

    def renderer_info(root_go_id):
        """返回 (renderer 类型名, [材质名...], [贴图名...], 主贴图名, 挂着的物件名)。

        ★ 贴图不一定挂在 UnitLand 自己身上 —— 场景里 Land_XXX 根物件常常只有
          nodeInfo，真正带 Renderer 的是**子物件**。所以先把"自己 + 后代 + 祖先"
          都搜一遍（先自己、再后代、最后祖先），找到第一个带 Renderer 的就算。
        """
        order = []
        for gid in [root_go_id] + descendants(root_go_id)[1:]:
            order.append(gid)
        for gid in ancestors(root_go_id)[1:]:
            order.append(gid)

        for gid in order:
            for comp in go_components(gid):
                for kind in ("MeshRenderer", "SkinnedMeshRenderer"):
                    rend = trees.get((kind, comp))
                    if not rend:
                        continue
                    mats, texs, main = [], [], None
                    for mat_ref in (rend.get("m_Materials") or []):
                        mat = trees.get(("Material", (mat_ref or {}).get("m_PathID")))
                        if not mat:
                            continue
                        mats.append(mat.get("m_Name"))
                        saved = mat.get("m_SavedProperties") or {}
                        for entry in (saved.get("m_TexEnvs") or []):
                            if not isinstance(entry, dict):
                                continue
                            got = prop_texture_name(entry)
                            if got:
                                texs.append(got)
                                if entry.get("first") in ("_MainTex", "_BaseMap", "_BaseColorMap"):
                                    main = got
                    if main is None and texs:
                        main = texs[0]
                    go = trees.get(("GameObject", gid)) or {}
                    return kind, mats, texs, main, go.get("m_Name")
        return None, [], [], None, None

    nodes = []
    for (kind, _pid), tree in trees.items():
        if kind != "MonoBehaviour" or "nodeInfo" not in tree:
            continue
        info = tree["nodeInfo"]
        go_id = (tree.get("m_GameObject") or {}).get("m_PathID")
        if go_id is None:
            rkind, mats, texs, main, holder = None, [], [], None, None
        else:
            rkind, mats, texs, main, holder = renderer_info(go_id)
        node = {
            "id": info.get("_id"),
            "landType": info.get("_landType"),
            "runtimeLandType": tree.get("_RunTimeLandType") or 0,
            "serial": info.get("_playerSerialNumber"),
            "initDir": info.get("_initDirLandId"),
            "jump": info.get("_JumpNodeId"),
            "neighbors": list(info.get("_neighborLandIds") or ()),
            "gimmick": list(info.get("_GimmickData") or ()),
            "renderer": rkind,
            "materials": mats,
            "textures": texs,
            "mainTexture": main,
            "texHolder": holder,
            "goName": None,
            "childNames": [],
        }
        go = trees.get(("GameObject", go_id)) if go_id is not None else None
        if go:
            node["goName"] = go.get("m_Name")
            names = []
            for gid in descendants(go_id)[1:]:
                child = trees.get(("GameObject", gid)) or {}
                nm = child.get("m_Name")
                if isinstance(nm, str) and nm not in names:
                    names.append(nm)
            node["childNames"] = names[:12]
        pos = local_position(go_id) if go_id is not None else None
        if pos:
            node["x"], node["y"], node["z"] = [round(v, 4) if v is not None else None for v in pos]
        nodes.append(node)
    nodes.sort(key=lambda n: (n["id"] if isinstance(n["id"], int) else -1))
    return nodes


def map_name(suffix):
    return "Map_%s" % suffix


# ---------------------------------------------------------------- main
def main(argv):
    if not os.path.exists(CATALOG):
        print("找不到 Addressables 目录：%s" % CATALOG)
        return 2
    catalog = Catalog(CATALOG)

    if "--list" in argv:
        names = sorted(k for k in catalog.keys if isinstance(k, str) and k.startswith("Map_0"))
        for name in names:
            entries = catalog.find(name)
            bundle = catalog.bundle_name(entries[0]) if entries else None
            cached = find_cached_bundle(bundle) if bundle else None
            print("  %-10s bundle=%-36s %s" % (name, bundle or "?", "有" if cached else "缓存里没有"))
        return 0

    suffixes = [a for a in argv if not a.startswith("-")] or ["023"]
    report = {"maps": {}, "byLandType": {}}
    failed = 0
    for suffix in suffixes:
        name = map_name(suffix)
        entries = catalog.find(name)
        if not entries:
            print("%s：catalog 里没有这个 key" % name)
            failed += 1
            continue
        scene_id = catalog.internal_id(entries[0])
        bundle = catalog.bundle_name(entries[0])
        path = find_cached_bundle(bundle) if bundle else None
        if path is None:
            print("%s：缓存位里没有 bundle %s（先让客户端加载过这张图）" % (name, bundle))
            failed += 1
            continue

        nodes = extract(path, scene_id)
        report["maps"][suffix] = {"scene": name, "bundle": bundle, "nodes": nodes}

        pairs = {}
        for n in nodes:
            key = n["landType"]
            slot = pairs.setdefault(key, {"count": 0, "textures": [], "materials": [],
                                          "renderers": [], "goNames": [], "samples": []})
            slot["count"] += 1
            for t in n["textures"]:
                if t not in slot["textures"]:
                    slot["textures"].append(t)
            for m in n["materials"]:
                if m and m not in slot["materials"]:
                    slot["materials"].append(m)
            if n["goName"] not in slot["goNames"]:
                slot["goNames"].append(n["goName"])
            if n["renderer"] not in slot["renderers"]:
                slot["renderers"].append(n["renderer"])
            if len(slot["samples"]) < 3:
                slot["samples"].append({"id": n["id"], "go": n["goName"],
                                        "child": n["childNames"][:4],
                                        "holder": n["texHolder"],
                                        "main": n["mainTexture"]})
            if key not in report["byLandType"]:
                report["byLandType"][key] = {"textures": [], "materials": [], "goNames": []}
            for t in n["textures"]:
                if t not in report["byLandType"][key]["textures"]:
                    report["byLandType"][key]["textures"].append(t)
            for m in n["materials"]:
                if m and m not in report["byLandType"][key]["materials"]:
                    report["byLandType"][key]["materials"].append(m)
            if n["goName"] not in report["byLandType"][key]["goNames"]:
                report["byLandType"][key]["goNames"].append(n["goName"])

        print("=" * 78)
        print("%s（%d 个格子）bundle=%s" % (name, len(nodes), bundle))
        print("%-8s %-6s %-30s %-26s %s" % ("landType", "格子数", "物件名(=贴图线索)", "主贴图", "全部贴图"))
        for key in sorted(pairs, key=lambda k: (k is None, k)):
            slot = pairs[key]
            print("%-8s %-6d %-30s %-26s %s" % (
                key, slot["count"],
                ",".join(slot["goNames"]) or "-",
                (slot["samples"][0]["main"] if slot["samples"] else None) or "-",
                ",".join(slot["textures"]) or "-"))

    with io.open(OUT, "w", encoding="utf-8") as fh:
        fh.write(json.dumps(report, ensure_ascii=False, indent=1))
    print("")
    print("已写出：%s" % OUT)

    # 汇总一份"landType -> 贴图"的最小表，方便直接抄进 C#
    print("")
    print("── landType -> 贴图名（跨所有导出的图汇总） ──")
    for key in sorted(report["byLandType"], key=lambda k: (k is None, k)):
        slot = report["byLandType"][key]
        print("  %-6s tex=%-34s go=%s" % (
            key, ",".join(slot["textures"]) or "(没有贴图)",
            ",".join(slot["goNames"]) or "-"))

    # ★ 物件名去掉结尾的 _<格子号> 就是 Land_<名字>，
    #   而 GameRes\Img 里贴图叫 UT_Platform_<名字>.png —— 两边名字基本一致。
    #   这里把两边对一下，认得出几张就是几张，不猜。
    print("")
    print("── 交叉核对：Land_<名字> ↔ GameRes\\Img\\UT_Platform_<名字>.png ──")
    img_dir = None
    try:
        game = paths.game_dir()
        if game:
            img_dir = os.path.join(game, "GameRes", "Img")
    except Exception:
        img_dir = None
    have = set()
    if img_dir and os.path.isdir(img_dir):
        for fn in os.listdir(img_dir):
            if fn.startswith("UT_Platform_") and fn.endswith(".png"):
                have.add(fn[len("UT_Platform_"):-len(".png")])
    import re
    suffix_of_land = {}
    for key in sorted(report["byLandType"], key=lambda k: (k is None, k)):
        for go in report["byLandType"][key]["goNames"]:
            if not isinstance(go, str) or not go.startswith("Land_"):
                continue
            stem = re.sub(r"_\d+$", "", go[len("Land_"):])
            if stem and re.match(r"^[A-Za-z]", stem):
                suffix_of_land.setdefault(key, stem)
    for key in sorted(suffix_of_land):
        stem = suffix_of_land[key]
        mark = "有图" if stem in have else ("★ 没有 UT_Platform_%s.png" % stem)
        print("  landType %-4s -> Land_%-22s %s" % (key, stem, mark))
    print("  （GameRes\\Img 里 UT_Platform_* 共 %d 张）" % len(have))
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
