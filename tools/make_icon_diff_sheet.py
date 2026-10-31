# -*- coding: utf-8 -*-
"""只读：把「landType → 贴图」的**改前 / 改后**并排画成一张对照图。

- 改前：2026-10-03 第 3 批那份手写猜表（照英文名逐个试，找不到就退到列表下一个）
- 改后：游戏自己的 `Land.bin` 点名的贴图（编号头像一律拒绝 ⇒ 色块 + 官方中文名）
- 官方中文名：`STRLand.bin`（key = 80000 + landType）

输出：`_截图\\图标对照_landType贴图_改前改后.png`
用法：python make_icon_diff_sheet.py
"""
import os
import sys

from PIL import Image, ImageDraw, ImageFont

IMG = r"D:\steam\steamapps\common\Astral Party\8vJXn6CN\GameRes\Img"
OUT = r"D:\小小兔趴\5-DIY关卡\_截图\图标对照_landType贴图_改前改后.png"

# ── 改前那张手写猜表（逐条抄自当时的 src\Art.cs，勿改）─────────────────────
OLD = {
    1: ["UT_Platform_Start", "UT_Platform_Born"],
    2: ["UT_Platform_Start", "UT_Platform_FillingStation"],
    3: ["UT_Platform_Teleport", "UT_Platform_Portal"],
    4: ["UT_Platform_MonsterPursuit", "UT_Platform_Pursuit"],
    5: ["UT_Platform_Relic", "UT_Platform_OfferAReward"],
    6: ["UT_Platform_Divination"],
    7: ["UT_Platform_Shop"],
    8: ["UT_Platform_Event"],
    9: ["UT_Platform_Gold", "UT_Platform_Lottery"],
    10: ["UT_Platform_Gamble"],
    11: ["UT_Platform_Battery", "UT_Platform_FillingStation"],
    12: ["UT_Platform_MoveAgain"],
    13: ["UT_Platform_Heal", "UT_Platform_Hospital"],
    14: ["UT_Platform_Card", "UT_Platform_DrawCard"],
    15: ["UT_Platform_Gold", "UT_Platform_RollGold"],
    16: ["UT_Platform_Destiny"],
    17: ["UT_Platform_Damage", "UT_Platform_BloodLoss"],
    18: ["UT_Platform_Jump"],
    19: ["UT_Platform_Monster"],
    20: ["UT_Platform_Heal"],
    21: ["UT_Platform_MonsterPursuit", "UT_Platform_Monster"],
    22: ["UT_Platform_Shop", "UT_Platform_Pveshop", "UT_Platform_Card"],
    23: ["UT_Platform_Gift", "UT_Platform_Relic"],
    24: ["UT_Platform_Relic", "UT_Platform_Chips"],
    25: ["UT_Platform_Gimmick"],
    26: ["UT_Platform_Vendor", "UT_Platform_Shop"],
    27: ["UT_Platform_MindTraining"],
}

# ── 改后的表（= 游戏 Land.bin 点名的那一张；None = Land.bin 里没有这一行）──
NEW = {
    1: "UT_Platform_108", 2: "UT_Platform_Start", 3: "UT_Platform_Teleport",
    4: "UT_Platform_TeleportPoint", 6: "UT_Platform_Divine", 7: "UT_Platform_Shop",
    8: "UT_Platform_Event", 9: "UT_Platform_Lottery", 10: "UT_Platform_Gamble",
    11: "UT_Platform_Fire", 12: "UT_Platform_MoveAgain", 13: "UT_Platform_Hospital",
    14: "UT_Platform_Card", 15: "UT_Platform_Gold", 16: "UT_Platform_Destiny",
    17: "UT_Platform_Damage", 18: "UT_Platform_Jump", 19: "UT_Platform_Monster",
    20: "UT_Platform_Heal", 21: "UT_Platform_TeleportPoint", 22: "UT_Platform_Shop",
    23: "UT_Platform_Gift", 24: "UT_Platform_Relic", 25: "UT_Platform_Gimmick",
    26: "UT_Platform_CandyGhost", 27: "UT_Platform_MindTraining",
}

OLD_NAMES = {0: "普通", 1: "起始点", 2: "保障点", 3: "传送门", 4: "追击", 5: "给奖励",
             6: "占卜", 7: "商店", 8: "事件", 9: "抽奖", 10: "赌一把", 11: "电池",
             12: "再走一次", 13: "医院", 14: "抽卡", 15: "摇星币", 16: "命运",
             17: "掉血", 18: "跳格", 19: "怪物", 20: "回血", 21: "怪物追击",
             22: "卡牌商店", 23: "礼物", 24: "筹码商店", 25: "机关", 26: "小贩",
             27: "心智训练"}

# ── STRLand.bin 的官方中文名（只读 dump 出来的，逐条抄；自测 ⑩b 会核对其中几条）──
OFFICIAL = {1: "起始点", 2: "保障点", 3: "传送门", 4: "突击门", 5: "悬赏", 6: "占卜",
            7: "商店", 8: "全员事件", 9: "彩票", 10: "猜猜乐", 11: "炮台", 12: "疾行",
            13: "医院", 14: "卡牌奖励", 15: "天降横财", 16: "个人命运", 17: "天降横祸",
            18: "跳跃", 19: "怪物", 20: "恢复", 21: "怪物突击门", 22: "商店", 23: "礼物",
            24: "筹码商店", 25: "机关控制台", 26: "怪奇糖果机", 27: "思维强化"}

# 功能色（跟编辑器一致，色块用它）
HEX = {0: "#2E3646", 1: "#3FBF7F", 2: "#3AA0C8", 3: "#9A6BE0", 4: "#D8623F", 5: "#C9A227",
       6: "#8E6BC8", 7: "#E0A93B", 8: "#D2B24A", 9: "#B98B3A", 10: "#A8573F", 11: "#5C8F4A",
       12: "#4E8FBF", 13: "#4FA98C", 14: "#7E8FD8", 15: "#C9A227", 16: "#8A7BD8",
       17: "#C0453F", 18: "#5FA3D8", 19: "#B0453F", 20: "#57C08A", 21: "#CC5A38",
       22: "#D9A03B", 23: "#C77FA8", 24: "#CFAE4A", 25: "#7A6AA8", 26: "#B98B3A",
       27: "#7E9AD8"}

W, ROW, PAD = 1180, 58, 8
HEAD = 74
COLS = [("id", 0, 44), ("改前：名字", 44, 118), ("改前：图标/贴图名", 162, 262),
        ("改后：官方名", 424, 112), ("改后：图标/贴图名", 536, 262), ("这一格为什么", 798, 374)]


def have(stem):
    return stem is not None and os.path.exists(os.path.join(IMG, stem + ".png"))


def old_pick(land):
    """改前那张表的算法：名字列表里第一个**文件存在**的。都没有 ⇒ 色块。"""
    for stem in OLD.get(land, []):
        if have(stem):
            return stem
    return None


def new_pick(land):
    """改后的算法：Land.bin 点名 + 文件在 + 不是编号头像。"""
    stem = NEW.get(land)
    if stem is None or not have(stem):
        return None
    if stem.startswith("UT_Platform_") and stem[len("UT_Platform_"):].isdigit():
        return None
    return stem


def why(land):
    stem = NEW.get(land)
    if stem is None:
        return "Land.bin 里没有这一行 ⇒ 不瞎配"
    if stem.startswith("UT_Platform_") and stem[len("UT_Platform_"):].isdigit():
        return "Land.bin 点名 " + stem + "（角色头像）⇒ 不瞎配"
    if not have(stem):
        return "Land.bin 点名 " + stem + "，本机没有这张 png"
    if old_pick(land) != stem:
        return "Land.bin 点名 " + stem + "（改前用的是别的）"
    return "Land.bin 点名 " + stem + "（与改前一致）"


def tile(stem, size=44):
    """贴图缩略图；stem=None 就是一块功能色（色块）。land 参数由调用处补色。"""
    im = Image.open(os.path.join(IMG, stem + ".png")).convert("RGBA")
    im.thumbnail((size, size), Image.LANCZOS)
    bg = Image.new("RGBA", (size, size), (10, 14, 22, 255))
    bg.alpha_composite(im, ((size - im.width) // 2, (size - im.height) // 2))
    return bg.convert("RGB")


def main():
    rows = list(range(1, 28))
    H = HEAD + ROW * len(rows) + PAD * 2
    sheet = Image.new("RGB", (W, H), (18, 22, 30))
    d = ImageDraw.Draw(sheet)
    try:
        f = ImageFont.truetype("C:/Windows/Fonts/msyh.ttc", 15)
        fb = ImageFont.truetype("C:/Windows/Fonts/msyh.ttc", 17)
        fs = ImageFont.truetype("C:/Windows/Fonts/msyh.ttc", 13)
    except Exception:
        f = fb = fs = ImageFont.load_default()

    d.text((PAD, 12), "landType ↔ 贴图 对照表：改前（照英文名猜） vs 改后（游戏 Land.bin 点名）",
           fill=(232, 238, 250), font=fb)
    d.text((PAD, 40), "★ 改后：贴图名 = Land.bin 的 texture 字段；中文名 = STRLand.bin；"
                      "UT_Platform_101…129/301…306 是角色头像，一律不当图标 ⇒ 色块 + 官方中文名",
           fill=(150, 168, 195), font=fs)

    y0 = HEAD
    for name, x, w in COLS:
        d.rectangle([x + 2, y0, x + w - 2, y0 + 30], fill=(30, 38, 52))
        d.text((x + 8, y0 + 6), name, fill=(190, 204, 224), font=f)
    y = y0 + 34

    for land in rows:
        o = old_pick(land)
        n = new_pick(land)
        changed = (o != n) or (OLD_NAMES.get(land) != OFFICIAL.get(land))
        bg = (26, 34, 46) if changed else (21, 26, 35)
        d.rectangle([2, y, W - 2, y + ROW - 4], fill=bg)
        if changed:
            d.rectangle([2, y, 5, y + ROW - 4], fill=(224, 169, 59))

        # id
        d.text((PAD + 6, y + 18), str(land), fill=(226, 234, 246), font=fb)
        # 改前名字
        d.text((COLS[1][1] + 6, y + 18), OLD_NAMES.get(land, "?"), fill=(196, 160, 160), font=f)
        # 改前图标
        ox = COLS[2][1]
        if o:
            sheet.paste(tile(o), (ox + 4, y + 6))
            d.text((ox + 54, y + 18), o.replace("UT_Platform_", ""), fill=(170, 180, 196), font=fs)
        else:
            d.rectangle([ox + 4, y + 6, ox + 48, y + 50], fill=HEX[land])
            d.text((ox + 54, y + 18), "（色块）", fill=(170, 180, 196), font=fs)
        # 改后官方名
        d.text((COLS[3][1] + 6, y + 18), OFFICIAL.get(land, "?"),
               fill=(150, 226, 180) if OLD_NAMES.get(land) != OFFICIAL.get(land) else (200, 210, 226), font=f)
        # 改后图标
        nx = COLS[4][1]
        if n:
            sheet.paste(tile(n), (nx + 4, y + 6))
            d.text((nx + 54, y + 18), n.replace("UT_Platform_", ""), fill=(170, 180, 196), font=fs)
        else:
            d.rectangle([nx + 4, y + 6, nx + 48, y + 50], fill=HEX[land])
            d.text((nx + 54, y + 10), "色块 +", fill=(238, 200, 120), font=fs)
            d.text((nx + 54, y + 28), OFFICIAL.get(land, "?"), fill=(238, 200, 120), font=fs)
        # 依据
        d.text((COLS[5][1] + 6, y + 18), why(land), fill=(160, 172, 190), font=fs)
        y += ROW

    sheet.save(OUT)
    print("写出", OUT, sheet.size)
    return 0


if __name__ == "__main__":
    sys.exit(main())
