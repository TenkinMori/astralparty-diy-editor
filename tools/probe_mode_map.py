# -*- coding: utf-8 -*-
"""只读：GameMode.bin / Map.bin 里"模式 -> 地图"和"地图 -> 画板(scene后缀)"的事实核对。

★ 只读：只 open(...).read()，不写任何游戏目录里的东西。
用法：python probe_mode_map.py
"""
import os
import struct
import sys

TABLE = r"D:\小小兔趴\4-服务端\server\data\gamedata"


def read_varint(data, i):
    raw = 0
    shift = 0
    while i < len(data):
        b = data[i]
        i += 1
        raw |= (b & 0x7F) << shift
        if not (b & 0x80):
            break
        shift += 7
    return raw, i


def fields(data):
    i = 0
    while i < len(data):
        try:
            key, i = read_varint(data, i)
        except Exception:
            return
        num = key >> 3
        wire = key & 7
        if num == 0:
            return
        if wire == 0:
            v, i = read_varint(data, i)
            yield num, wire, v
        elif wire == 1:
            yield num, wire, data[i:i + 8]
            i += 8
        elif wire == 2:
            ln, i = read_varint(data, i)
            yield num, wire, data[i:i + ln]
            i += ln
        elif wire == 5:
            yield num, wire, data[i:i + 4]
            i += 4
        else:
            return


def s32(blob):
    return struct.unpack_from("<i", blob, 0)[0]


def entries(data, top_field):
    """顶层 field N 的 map 形态：一条 entry = f1(key) + f2(body)。"""
    out = []
    for num, wire, val in fields(data):
        if num == top_field and wire == 2:
            key = None
            body = None
            for n2, w2, v2 in fields(val):
                if n2 == 1 and w2 in (0, 5):
                    key = v2 if w2 == 0 else s32(v2)
                elif n2 == 2 and w2 == 2:
                    body = v2
            out.append((key, body))
    return out


def game_modes():
    data = open(os.path.join(TABLE, "GameMode.bin"), "rb").read()
    out = []
    for key, body in entries(data, 2):
        maps = []
        for n2, w2, v2 in fields(body):
            if n2 == 7 and w2 == 2:
                for n3, w3, v3 in fields(v2):
                    if n3 == 1 and w3 in (0, 5):
                        maps.append(v3 if w3 == 0 else s32(v3))
        out.append((key, maps))
    return out


def map_scene():
    """Map.bin：地图 id -> 画板后缀（MapInfoConfigure 里的 MapScene 名，Map_0XX）。"""
    data = open(os.path.join(TABLE, "Map.bin"), "rb").read()
    out = {}
    for key, body in entries(data, 2):
        if key is None:
            continue
        found = {}
        for n2, w2, v2 in fields(body):
            if w2 == 2:
                try:
                    text = v2.decode("utf-8")
                except Exception:
                    continue
                if text.startswith("Map_") and "Preview" not in text and "Scene" not in text:
                    found.setdefault("scene", text)
                elif text.startswith("MapScene_"):
                    found["mapscene"] = text
        out[key] = found
    return out


def main():
    modes = game_modes()
    scenes = map_scene()
    print("Map.bin 里登记的地图 %d 张；Map_ 名字样例：" % len(scenes))
    sample = sorted(k for k in scenes if scenes[k].get("scene"))
    for k in sample[:12]:
        print("   %-8s %s" % (k, scenes[k].get("scene")))
    print("")
    print("模式 -> 地图（含每张图的 Map_ 场景名）")
    for mode, maps in modes:
        if not maps:
            continue
        print("  模式 %-4s %d 张" % (mode, len(maps)))
        for m in maps:
            print("      %-8s %s" % (m, scenes.get(m, {}).get("scene", "?")))
    return 0


if __name__ == "__main__":
    sys.exit(main())
