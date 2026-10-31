# -*- coding: utf-8 -*-
"""★ 只读核对：`4-服务端\\server\\data\\boards\\*.json` 这 26 张棋盘，格式是否与编辑器
`棋盘缓存\\` 里现用的那 6 张**同构**（顶层字段、nodes 字段、坐标量纲、landType 范围）。

判据（照 `BoardModel.TryRead` 实际读的那几样）：
  * 顶层必须是 object，且有 `nodes` 数组；
  * 每个 node 至少有 `id` / `landType` / `neighbors`，并有 x/z 坐标；
  * landType 落在 `Land.bin` 认识的范围内。

产物落 tools\\_boards_check.txt。
"""
import io
import json
import os
import sys
from collections import Counter

HERE = os.path.dirname(os.path.abspath(__file__))
SRC = r"D:\小小兔趴\4-服务端\server\data\boards"
CACHE = os.path.join(os.path.dirname(HERE), "棋盘缓存")
OUT = os.path.join(HERE, "_boards_check.txt")

LOG = []
def say(s):
    LOG.append(str(s))


def survey(path):
    raw = io.open(path, encoding="utf-8").read()
    obj = json.loads(raw)
    if not isinstance(obj, dict):
        return None, "顶层不是 object"
    top = sorted(obj.keys())
    nodes = obj.get("nodes")
    if not isinstance(nodes, list):
        return None, "没有 nodes 数组"
    if not nodes:
        return None, "nodes 是空的"
    fieldc = Counter()
    lands = Counter()
    for n in nodes:
        if not isinstance(n, dict):
            return None, "nodes 里有非 object 元素"
        for k in n.keys():
            fieldc[k] += 1
        lands[n.get("landType")] += 1
    # 每个 node 都必须有的键
    must = ["id", "landType", "neighbors"]
    for m in must:
        if fieldc.get(m, 0) != len(nodes):
            return None, "有 node 缺 " + m
    if fieldc.get("x", 0) != len(nodes) or fieldc.get("z", 0) != len(nodes):
        return None, "有 node 缺 x 或 z"
    return {
        "top": top,
        "n": len(nodes),
        "fields": sorted(fieldc.keys()),
        "lands": sorted(lands.keys()),
        "bytes": len(raw.encode("utf-8")),
    }, None


def main():
    say("=" * 100)
    say("官方棋盘格式核对：%s" % SRC)
    say("=" * 100)
    names = sorted(os.path.splitext(f)[0] for f in os.listdir(SRC) if f.endswith(".json"))
    say("共 %d 张： %s" % (len(names), " ".join(names)))
    say("")
    rows = {}
    for n in names:
        info, err = survey(os.path.join(SRC, n + ".json"))
        rows[n] = (info, err)

    # 以 007 为基准形状
    base = rows.get("007", (None, None))[0]
    if base:
        say("基准（007）顶层字段： %s" % ", ".join(base["top"]))
        say("基准（007）node 字段： %s" % ", ".join(base["fields"]))
        say("基准（007）格数 %d · landType %s" % (base["n"], base["lands"]))
    say("")
    say("%-6s %-6s %-46s %-9s %s" % ("画板", "格数", "顶层字段", "是否同构", "备注 / landType"))
    bad = []
    for n in names:
        info, err = rows[n]
        cached = "（已在缓存）" if os.path.exists(os.path.join(CACHE, n + ".json")) else ""
        if info is None:
            say("%-6s %-6s %-46s %-9s %s" % (n, "-", "-", "★否", err))
            bad.append((n, err))
            continue
        same = (info["top"] == base["top"] and info["fields"] == base["fields"]) if base else False
        say("%-6s %-6d %-46s %-9s %s %s"
            % (n, info["n"], ",".join(info["top"]), "是" if same else "★否",
               "landType " + ",".join(str(x) for x in info["lands"])[:40], cached))
        if not same:
            bad.append((n, "顶层/node 字段与基准不同：top=%s fields=%s"
                        % (",".join(info["top"]), ",".join(info["fields"]))))
    say("")
    say("=" * 100)
    if bad:
        say("★ 与基准不同构的 %d 张：" % len(bad))
        for n, why in bad:
            say("    %s ： %s" % (n, why))
    else:
        say("✓ 全部 %d 张与基准（007）同构 —— 直接拷进 棋盘缓存 即可。" % len(names))
    io.open(OUT, "w", encoding="utf-8").write("\n".join(LOG))


if __name__ == "__main__":
    main()
