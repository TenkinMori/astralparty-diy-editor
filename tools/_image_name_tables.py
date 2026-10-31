# -*- coding: utf-8 -*-
"""★ 只读交叉验证：`UT_MapPreview_*` / `UT_MapScene_*` 这些**资源名**到底出现在
哪些客户端配置表里 —— 用来确认「82016 该用哪张图」不是只看服务端样例 Map.bin。

做法：把 4-服务端\\server\\data\\gamedata 下所有 .bin 当字节流扫，
找含 `UT_MapPreview_` / `UT_MapScene_` 的表，并把命中处的上下文解出来。
（只读；产物落 tools\\_image_name_tables.txt）
"""
import io
import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
GD = r"D:\小小兔趴\4-服务端\server\data\gamedata"
OUT = os.path.join(HERE, "_image_name_tables.txt")
LOG = []


def say(s):
    LOG.append(str(s))
    print(s)
    sys.stdout.flush()


NEEDLES = (b"UT_MapPreview_", b"UT_MapScene_", b"UT_AccountHeadShot_Monster_",
           b"MiniMap_")


def main():
    hits = {}
    for fn in sorted(os.listdir(GD)):
        if not fn.endswith(".bin"):
            continue
        p = os.path.join(GD, fn)
        data = io.open(p, "rb").read()
        got = {}
        for nd in NEEDLES:
            if nd in data:
                got[nd.decode()] = data.count(nd)
        if got:
            hits[fn] = got
    say("含这些资源名的表：")
    for fn, got in sorted(hits.items()):
        say("   %-34s %s" % (fn, got))
    say("")

    # ImageLocalization 里把 82016 / 016 的条目挑出来
    p = os.path.join(GD, "ImageLocalization.bin")
    if os.path.exists(p):
        data = io.open(p, "rb").read()
        say("=" * 78)
        say("ImageLocalization.bin（%d 字节）里所有 UT_MapPreview_* / UT_MapScene_* 字面量"
            % len(data))
        say("=" * 78)
        for m in re.finditer(rb"UT_Map(?:Preview|Scene)_\d+", data):
            s = max(0, m.start() - 40)
            ctx = data[s:m.end() + 40]
            say("   @%-8d %r" % (m.start(), ctx))
        say("")

    # STRMap 里 82016 那一行
    p = os.path.join(GD, "STRMap.bin")
    if os.path.exists(p):
        data = io.open(p, "rb").read()
        say("STRMap.bin 里含 '异变图书馆' = %s" % (b"\xe5\xbc\x82\xe5\x8f\x98" in data))

    io.open(OUT, "w", encoding="utf-8").write("\n".join(LOG) + "\n")
    print("报告：" + OUT)


if __name__ == "__main__":
    main()
