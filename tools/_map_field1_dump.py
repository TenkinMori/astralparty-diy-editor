# -*- coding: utf-8 -*-
"""★ 只读：把 Map.bin 顶层 field 1（地图id -> 官方图片名 登记表）的**原始字段结构**打出来，
   用来确认 field 8/9 到底是不是 MapImage / MapSceneImage，以及名字 id 在哪一格。

只读 <游戏根>\server\data\gamedata\Map.bin，产物落 tools\_map_field1_dump.txt。
"""
import io
import os
import struct
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(HERE, "pylibs"))

GAME = r"D:\steam\steamapps\common\Astral Party\8vJXn6CN"
# ★ 注意：游戏根下**没有** server\data\gamedata（那是开发机的工作副本形态）。
#   表在本机的工作副本里 —— 和编辑器 `GameLocator.TableDir` 的兜底路径一致。**只读**。
BIN = r"D:\小小兔趴\4-服务端\server\data\gamedata\Map.bin"
OUT = os.path.join(HERE, "_map_field1_dump.txt")

LOG = []
def say(s):
    LOG.append(str(s))


def read_varint(buf, i):
    shift = 0
    val = 0
    while True:
        b = buf[i]
        i += 1
        val |= (b & 0x7F) << shift
        if not (b & 0x80):
            break
        shift += 7
    return val, i


def fields(buf):
    """→ [(number, wire, value_bytes_or_int)]"""
    out = []
    i = 0
    n = len(buf)
    while i < n:
        key, i = read_varint(buf, i)
        num, wire = key >> 3, key & 7
        if wire == 0:
            v, i = read_varint(buf, i)
            out.append((num, wire, v))
        elif wire == 1:
            out.append((num, wire, buf[i:i + 8])); i += 8
        elif wire == 2:
            ln, i = read_varint(buf, i)
            out.append((num, wire, buf[i:i + ln])); i += ln
        elif wire == 5:
            out.append((num, wire, struct.unpack_from("<I", buf, i)[0])); i += 4
        else:
            raise ValueError("wire %d" % wire)
    return out


def zigzag(v):
    return (v >> 1) ^ -(v & 1)


def main():
    say("Map.bin = " + BIN)
    say("在 = %s" % os.path.exists(BIN))
    if not os.path.exists(BIN):
        io.open(OUT, "w", encoding="utf-8").write("\n".join(LOG))
        return 1
    raw = io.open(BIN, "rb").read()
    say("字节数 = %d" % len(raw))
    say("")

    top = fields(raw)
    from collections import Counter
    say("—— 顶层字段分布 ——")
    c = Counter()
    for num, wire, val in top:
        c[(num, wire)] += 1
    for (num, wire), cnt in sorted(c.items()):
        say("  field %-3d wire %d  x%d" % (num, wire, cnt))
    say("")

    # field 1 = 登记表
    f1 = [(num, wire, val) for num, wire, val in top if num == 1 and wire == 2]
    say("=" * 78)
    say("field 1 的每一条（登记表）—— 共 %d 条" % len(f1))
    say("=" * 78)
    for idx, (_, _, body) in enumerate(f1):
        say("── 第 %d 条 ──" % (idx + 1))
        for num, wire, val in fields(body):
            if wire == 2:
                # 试着当 UTF-8 字符串 / 子消息两种都报
                txt = None
                try:
                    txt = val.decode("utf-8")
                except Exception:
                    txt = None
                printable = txt is not None and all(ch.isprintable() for ch in txt)
                sub = None
                try:
                    sub = fields(val)
                except Exception:
                    sub = None
                if printable and txt:
                    say("    f%-3d wire2 字符串 = %r" % (num, txt))
                elif sub:
                    say("    f%-3d wire2 子消息(%d 字节) 内含字段 %s"
                        % (num, len(val), ",".join(str(s[0]) for s in sub)))
                else:
                    say("    f%-3d wire2 二进制 %d 字节" % (num, len(val)))
            else:
                say("    f%-3d wire%d = %s" % (num, wire, val))
        if idx >= 30:
            say("  …（后面还有 %d 条，略）" % (len(f1) - idx - 1))
            break

    io.open(OUT, "w", encoding="utf-8").write("\n".join(LOG))
    sys.stdout.write("report: %s\n" % OUT)
    return 0


if __name__ == "__main__":
    sys.exit(main())
