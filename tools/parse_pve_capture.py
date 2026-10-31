# -*- coding: utf-8 -*-
"""只读：把抓包里那条**整局房间快照**（最大帧）准确解出来 —— 模式 / 地图 / 每张画板的格子。

结构（实测）：
    帧 = 长度前缀(4B) + 6 字节保留 + payload
    payload 里有：房间 1003 主体；每个玩家 12 字节里 1 个 Battle；Battle 里
    f7 是 packed int32 的 [NodeId, LandType] 交替列表。

★ 只读那个 jsonl；输出写到本目录。
用法：python parse_pve_capture.py
"""
import io
import json
import os
import struct
import sys

DEFAULT = (r"D:\小小兔趴\3-工具\_真机LocalLow备份_20261002-200053"
           r"\AstralParty_CN\pve_capture\frames.jsonl")
MODE_NAMES = {1: "标准模式", 2: "新手", 3: "激战模式", 4: "合作挑战", 5: "单人模式",
              6: "单人模式", 7: "礼物大作战", 8: "玩家对战练习", 9: "合作挑战练习",
              10: "限时玩法", 11: "幸运星争夺战", 12: "畸变组件"}
MAP_SCENE = {81003: "003", 81004: "004", 81005: "005", 81006: "006", 81007: "007",
             81011: "011", 82007: "007", 82008: "008", 82009: "009", 82010: "010",
             82012: "012", 82013: "013", 82014: "014", 82015: "015", 82016: "023",
             82017: "017", 83001: "001", 83002: "002", 83003: "003"}


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


def packed(data):
    return [struct.unpack_from("<i", data, k)[0] for k in range(0, len(data) - 3, 4)]


def main(argv):
    path = argv[0] if argv else DEFAULT
    rows = []
    with io.open(path, encoding="utf-8") as handle:
        for line in handle:
            line = line.strip()
            if not line:
                continue
            try:
                rows.append(json.loads(line))
            except ValueError:
                continue
    rows = [r for r in rows if r.get("hex")]
    best = max(rows, key=lambda r: len(r["hex"]))
    raw = bytes.fromhex(best["hex"])
    print("最大帧：ts=%s dir=%s 长度 %d 字节" % (best.get("ts"), best.get("dir"), len(raw)))
    payload = raw[10:]
    print("payload %d 字节；头 24 字节 %s" % (len(payload), payload[:24].hex()))

    # 顶层：把所有 w5 的小整数收起来看模式；再找 packed 的 [node,land] 交替表
    print("")
    print("顶层字段（前 30 个）：")
    shown = 0
    for n, w, v in fields(payload):
        if shown >= 30:
            break
        shown += 1
        if w == 5:
            print("   f%-3d w5  int=%d" % (n, s32(v)))
        elif w == 0:
            print("   f%-3d w0  varint=%d" % (n, v))
        elif w == 1:
            print("   f%-3d w1  64bit %s" % (n, v.hex()))
        else:
            print("   f%-3d w2  len=%d %s" % (n, len(v), v[:20].hex()))

    # 全树搜 packed [node,land] 交替表：长度是 8 的倍数、偶数下标是 0..60、奇数下标 <= 27
    print("")
    print("候选棋盘（packed [NodeId,LandType] 交替表）：")
    found = []

    def scan(data, depth=0):
        if depth > 8:
            return
        for n, w, v in fields(data):
            if w == 2:
                if len(v) >= 16 and len(v) % 8 == 0:
                    nums = packed(v)
                    nodes = nums[0::2]
                    lands = nums[1::2]
                    if all(0 <= x <= 80 for x in nodes) and all(0 <= x <= 27 for x in lands):
                        found.append((n, nodes, lands))
                scan(v, depth + 1)

    scan(payload)
    for n, nodes, lands in found:
        counts = {}
        for land in lands:
            counts[land] = counts.get(land, 0) + 1
        print("   field f%-3d：%d 个格；node %d..%d；构成 %r"
              % (n, len(nodes), min(nodes), max(nodes), sorted(counts.items())))
    if found:
        _, nodes, lands = found[0]
        print("")
        print("第一张棋盘 node→land：%r" % list(zip(nodes, lands)))
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
