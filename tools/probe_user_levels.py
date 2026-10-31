# -*- coding: utf-8 -*-
"""只读诊断：把用户 DIY 关卡里实际用到的 landType 摊出来 + 房间帧的座位/模式线索。

★ 只读：用户的关卡/棋盘 json 一个字节都不写；抓包读的是只读备份。
用法：python probe_user_levels.py
"""
import io
import json
import os
import re

USER_DIY = r"d:\steam\steamapps\common\Astral Party\8vJXn6CN\DIY关卡"
CAPTURE = (r"D:\小小兔趴\3-工具\_真机LocalLow备份_20261002-200053"
           r"\AstralParty_CN\pve_capture\frames.jsonl")


def scan_boards():
    print("=" * 74)
    print("① 用户 DIY关卡 目录里的棋盘 json：官方 landType + diy 覆盖")
    if not os.path.isdir(USER_DIY):
        print("   目录不存在：" + USER_DIY)
        return
    for name in sorted(os.listdir(USER_DIY)):
        if not name.endswith(".board.json"):
            continue
        path = os.path.join(USER_DIY, name)
        try:
            raw = io.open(path, encoding="utf-8").read()
        except Exception as exc:
            print("   %s 读不了：%s" % (name, exc))
            continue
        nodes = re.findall(r'"landType"\s*:\s*(\d+)', raw)
        official = sorted(set(int(x) for x in nodes))
        print("   %-26s %d 格；官方 landType = %s" % (name, len(nodes), official))
        # diy 覆盖：nodes 里带 "diy" 的那些
        diy = re.findall(r'"diy"\s*:\s*\{[^}]*"landType"\s*:\s*(\d+)', raw)
        if diy:
            print("        其中被改过的格子（diy.landType）= %s"
                  % sorted(set(int(x) for x in diy)))


def scan_levels():
    print("")
    print("=" * 74)
    print("② 用户 DIY 关卡 json（.json 不含 .board.json）")
    if not os.path.isdir(USER_DIY):
        return
    for name in sorted(os.listdir(USER_DIY)):
        if not name.endswith(".json") or name.endswith(".board.json"):
            continue
        path = os.path.join(USER_DIY, name)
        try:
            raw = io.open(path, encoding="utf-8").read()
        except Exception as exc:
            print("   %s 读不了：%s" % (name, exc))
            continue
        mapid = re.search(r'"map"\s*:\s*(\d+)', raw)
        print("   %-20s %6d 字节  map=%s" % (name, len(raw), mapid.group(1) if mapid else "?"))


def rv(d, i):
    raw, s = 0, 0
    while i < len(d):
        x = d[i]
        i += 1
        raw |= (x & 0x7F) << s
        if not (x & 0x80):
            break
        s += 7
    return raw, i


def fields(d):
    i = 0
    while i < len(d):
        try:
            k, i = rv(d, i)
        except Exception:
            return
        n, w = k >> 3, k & 7
        if n == 0:
            return
        if w == 0:
            v, i = rv(d, i)
            yield n, w, v
        elif w == 1:
            yield n, w, d[i:i + 8]
            i += 8
        elif w == 2:
            ln, i = rv(d, i)
            yield n, w, d[i:i + ln]
            i += ln
        elif w == 5:
            yield n, w, d[i:i + 4]
            i += 4
        else:
            return


def scan_capture():
    print("")
    print("=" * 74)
    print("③ 抓包那条整局快照：座位 / 玩家数线索（判断它到底是不是 PvE 局）")
    if not os.path.exists(CAPTURE):
        print("   抓包不在：" + CAPTURE)
        return
    rows = []
    with io.open(CAPTURE, encoding="utf-8") as handle:
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
    b = bytes.fromhex(best["hex"])
    # 帧头实测：4 字节长度 + 4 字节保留 + 2 字节长度(0x02xx) ⇒ 正文从 10 开始
    for skip in (8, 10):
        body = b[skip:]
        head = list(fields(body))[:1]
        firstnum = head[0][0] if head else None
        print("   从 +%d 起解：第一个字段号 = %s（小才好）" % (skip, firstnum))
    body = b[10:]
    from collections import Counter
    c = Counter()
    for n, w, v in fields(body):
        c[(n, w)] += 1
    print("   顶层字段（+10 起）：" + ", ".join("f%d/w%d×%d" % (k[0], k[1], v)
                                              for k, v in sorted(c.items())))
    # 找"座位号"似的小整数
    small = Counter()
    for n, w, v in fields(body):
        if w == 0 and 0 <= v <= 8:
            small[v] += 1
    print("   顶层小整数(varint 0..8)出现次数：" + repr(sorted(small.items())))


def main():
    scan_boards()
    scan_levels()
    scan_capture()
    return 0


if __name__ == "__main__":
    main()
