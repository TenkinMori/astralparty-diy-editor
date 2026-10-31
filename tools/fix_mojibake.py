# -*- coding: utf-8 -*-
"""把被 PowerShell 5.1 用 ANSI 解码 + UTF-8 写回搞成"乱码"的 .cs 文件还原回来。

原理：原文件是 UTF-8 字节 B。
  错的一步：B.decode('cp936', errors='?') -> str S，再 S.encode('utf-8') 写盘。
要还原：把现在文件里的 UTF-8 文本按 **cp936** 编码回字节，就得到 B。
★ 只对"确实乱了"的文件用；还原前先把当前文件另存一份留档。
用法：
    python fix_mojibake.py <要修的文件> <留档目录>
"""
import codecs
import io
import os
import shutil
import sys


def looks_mojibake(text):
    bad = 0
    for ch in text:
        if ch in "锛鍒娓鏂鎵鐨鈽鈶銆鈥":
            bad += 1
    return bad


def main(argv):
    if len(argv) < 2:
        print(__doc__)
        return 2
    path = argv[0]
    keep_dir = argv[1] if len(argv) > 1 else None

    raw = io.open(path, "rb").read()
    text = raw.decode("utf-8", "replace")
    bad = looks_mojibake(text)
    print("文件：%s" % path)
    print("  乱码特征字符数：%d" % bad)
    if bad == 0:
        print("  看着没乱，不动它。")
        return 0

    if keep_dir:
        os.makedirs(keep_dir, exist_ok=True)
        shutil.copy2(path, os.path.join(keep_dir, os.path.basename(path) + ".mojibake"))
        print("  已留档（乱码版）：%s" % os.path.join(keep_dir, os.path.basename(path) + ".mojibake"))

    # ★ 关键一步：按 cp936 编回字节，再按 utf-8 解
    try:
        back = text.encode("cp936", "strict")
    except UnicodeEncodeError as exc:
        print("  直接 cp936 编不回去：%s" % exc)
        return 1
    try:
        fixed = back.decode("utf-8", "strict")
    except UnicodeDecodeError as exc:
        print("  编回去的字节不是合法 UTF-8：%s" % exc)
        print("  ⇒ 说明当初不是简单的一次 cp936 误解码，需要人工看。")
        return 1

    io.open(path, "w", encoding="utf-8", newline="").write(fixed)
    print("  已还原写回：%s" % path)

    # 顺手报一下哪些行引号不成对（那些行原来就被 0x80 吃掉了收尾引号）
    lines = fixed.split("\n")
    broken = []
    for i, line in enumerate(lines, 1):
        if line.count('"') % 2 == 1:
            broken.append((i, line))
    if broken:
        print("  ★ 这些行引号不成对（原来是行尾 UTF-8 字符被吃掉了收尾引号），要人工补：")
        for i, line in broken:
            print("    L%-4d %s" % (i, line.rstrip()))
    else:
        print("  引号全部成对。")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
