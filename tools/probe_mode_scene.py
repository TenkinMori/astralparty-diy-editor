# -*- coding: utf-8 -*-
"""只读：模式 -> 地图 -> 画板(scene)。

★ 关键教训（这一稿改对了）：**只能一层一层解，不能递归 walk**。
  递归 walk 会把"外层 f2 里嵌着的那个 f2"也当成本层的 f2，于是把内层消息
  错配到外层的 key 上（前两稿就是这么错的）。这里每一层都只解一层。

实测结构：
    Map.bin
      外层 f1(w5) = 地图 id
      外层 f2(w2) = MapInfoConfigure
          f8/f9(w2) = 'UT_MapPreview_0XX' / 'UT_MapScene_0XX'  ← 0XX 就是画板号
    GameMode.bin
      外层 f1(w5) = 模式 id
      外层 f2(w2) = GameModeInfoConfigure
          f7(w2, len%4==0) = 地图 id 列表（packed int32 小端）

★ 只读游戏表；不写游戏侧任何文件。
用法：python probe_mode_scene.py
"""
import os
import struct

TABLE = r"D:\小小兔趴\4-服务端\server\data\gamedata"
BOARD_DIR = r"D:\小小兔趴\4-服务端\server\data\boards"
MODE_NAMES = {1: "标准模式", 2: "新手", 3: "激战模式", 4: "合作挑战", 5: "单人模式",
              6: "单人模式", 7: "礼物大作战", 8: "玩家对战练习", 9: "合作挑战练习",
              10: "限时玩法", 11: "幸运星争夺战", 12: "畸变组件"}
PVE_MODES = (4, 9, 12)


def rv(data, i):
    raw, shift = 0, 0
    while i < len(data):
        b = data[i]
        i += 1
        raw |= (b & 0x7F) << shift
        if not (b & 0x80):
            break
        shift += 7
    return raw, i


def fields(data):
    """只解一层：yield (num, wire, value)。"""
    i = 0
    while i < len(data):
        key, i = rv(data, i)
        num, wire = key >> 3, key & 7
        if num == 0:
            return
        if wire == 0:
            v, i = rv(data, i)
            yield num, wire, v
        elif wire == 1:
            yield num, wire, data[i:i + 8]
            i += 8
        elif wire == 2:
            ln, i = rv(data, i)
            yield num, wire, data[i:i + ln]
            i += ln
        elif wire == 5:
            yield num, wire, data[i:i + 4]
            i += 4
        else:
            return


def s32(b):
    return struct.unpack_from("<i", b, 0)[0]


def int_of(wire, val):
    if wire == 0:
        return val
    if wire in (1, 5):
        return s32(val)
    return None


def outer(data):
    """[{key, body}]：外层 f1 是 key、外层 f2 是 body（**只看这一层**）。"""
    out, key = [], None
    for n, w, v in fields(data):
        if n == 1:
            key = int_of(w, v)
        elif n == 2 and w == 2:
            out.append({"key": key, "body": v})
            key = None
    return out


def map_scenes():
    data = open(os.path.join(TABLE, "Map.bin"), "rb").read()
    out = {}
    for row in outer(data):
        scene = None
        for n, w, v in fields(row["body"]):
            if w != 2 or n not in (8, 9):
                continue
            try:
                t = v.decode("utf-8")
            except Exception:
                continue
            if "MapScene_" in t:
                scene = t.split("MapScene_")[-1]
        out[row["key"]] = scene
    return out


def mode_maps():
    data = open(os.path.join(TABLE, "GameMode.bin"), "rb").read()
    out = []
    for row in outer(data):
        maps = []
        for n, w, v in fields(row["body"]):
            if n != 7:
                continue
            if w == 2 and len(v) % 4 == 0:
                maps = [struct.unpack_from("<i", v, k)[0] for k in range(0, len(v), 4)]
            elif w in (1, 5):
                maps = [s32(v)]
        out.append((row["key"], maps))
    return out


def main():
    scenes = map_scenes()
    have = sorted(f[:-5] for f in os.listdir(BOARD_DIR) if f.endswith(".json"))
    print("Map.bin：%d 张地图，有 scene 的 %d 张"
          % (len(scenes), len([1 for v in scenes.values() if v])))
    print("boards 目录已有画板：%s" % have)
    print("")
    pve, pvp = set(), set()
    for mode, maps in mode_maps():
        family = "PvE" if mode in PVE_MODES else "PvP"
        print("模式 %-3s %-10s [%s] %d 张" % (mode, MODE_NAMES.get(mode, "?"), family, len(maps)))
        for m in maps:
            sc = scenes.get(m) or "?"
            (pve if family == "PvE" else pvp).add(sc)
            print("      %-8s scene=%s  %s" % (m, sc, "★本机已有画板" if sc in have else ""))
    print("")
    print("PvE 系 scene：%s" % sorted(pve))
    print("PvP 系 scene：%s" % sorted(pvp))
    print("两边都有    ：%s" % sorted(pve & pvp))
    return 0


if __name__ == "__main__":
    main()
