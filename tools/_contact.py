# -*- coding: utf-8 -*-
"""★ 只读侦察 · 把候选 Texture2D 渲染成"联络表"（contact sheet），供人眼扫一眼。

用法（举例）：
    python _contact.py --grep "_atlas[0-9]*$" --max 240 --out _sheet_atlas.png
    python _contact.py --names UT_MapPreview_016,UT_MapScene_016 --out _sheet_map.png

只读游戏包（内存里解码），只往 tools\\ 写联络表 PNG。
"""
import argparse
import io
import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(HERE, "pylibs"))
BUNDLES = r"D:\steam\steamapps\common\Astral Party\8vJXn6CN\AstralParty_CN_Data\StreamingAssets\aa\StandaloneWindows64"
TSV = os.path.join(HERE, "_tex_inv.tsv")

CELL = 220
COLS = 6


def load_index():
    idx = []
    with io.open(TSV, encoding="utf-8") as fh:
        for line in fh:
            parts = line.rstrip("\n").split("\t")
            if len(parts) == 5:
                idx.append(parts)
    return idx


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--grep", default=None)
    ap.add_argument("--names", default=None)
    ap.add_argument("--max", type=int, default=120)
    ap.add_argument("--out", required=True)
    args = ap.parse_args()

    idx = load_index()
    if args.names:
        want = set(n.strip() for n in args.names.split(","))
        sel = [r for r in idx if r[0] in want]
    else:
        rx = re.compile(args.grep)
        sel = [r for r in idx if rx.search(r[0])]
    sel = sel[:args.max]
    print("选中 %d 张" % len(sel))
    if not sel:
        return 1

    import UnityPy
    from PIL import Image, ImageDraw

    bybundle = {}
    for nm, w, h, fmt, bn in sel:
        bybundle.setdefault(bn, []).append(nm)

    thumbs = []
    for bn, names in bybundle.items():
        p = os.path.join(BUNDLES, bn)
        if not os.path.exists(p):
            continue
        try:
            env = UnityPy.load(p)
        except Exception:
            continue
        want = set(names)
        for o in env.objects:
            if o.type.name != "Texture2D":
                continue
            try:
                d = o.read()
            except Exception:
                continue
            nm = getattr(d, "m_Name", None)
            if nm not in want:
                continue
            try:
                im = d.image
            except Exception:
                continue
            if im is None:
                continue
            im = im.convert("RGBA")
            thumbs.append((nm, im, bn))
            want.discard(nm)

    if not thumbs:
        print("一张都没解出来")
        return 1

    thumbs.sort(key=lambda t: t[0])
    rows = (len(thumbs) + COLS - 1) // COLS
    sheet = Image.new("RGBA", (COLS * CELL, rows * (CELL + 16)), (24, 26, 32, 255))
    dr = ImageDraw.Draw(sheet)
    for i, (nm, im, bn) in enumerate(thumbs):
        cx = (i % COLS) * CELL
        cy = (i // COLS) * (CELL + 16)
        t = im.copy()
        t.thumbnail((CELL - 8, CELL - 8), Image.LANCZOS)
        sheet.alpha_composite(t, (cx + 4, cy + 4))
        dr.text((cx + 4, cy + CELL + 2), ("%s  %dx%d" % (nm, im.width, im.height))[:40],
                fill=(220, 226, 240, 255))
    out = os.path.join(HERE, args.out)
    sheet.convert("RGB").save(out, "PNG")
    print("联络表：%s（%d 张，%dx%d）" % (out, len(thumbs), sheet.width, sheet.height))
    return 0


if __name__ == "__main__":
    sys.exit(main())
