# -*- coding: utf-8 -*-
"""★ 追加：把「补漏 46/47/48」这一轮写进简报（含错误根因与更正）。"""
import io
import os

HERE = os.path.dirname(os.path.abspath(__file__))
DOC = os.path.join(os.path.dirname(HERE), "文档", "异变图书馆美术图与图鉴像素小人-侦察简报.md")

ADD = u"""

---

# 追加 · 第 2.5 节：补漏 `Monster_46/47/48`（用户在图鉴里看到的那三只）

## 结论

**找到了，三只都在本地**，而且**三只都在我上一轮已经数过的那个"1149 个缓存包"里** ——
是我把缓存的**目录层级看浅了一层**，导致按包名逐个打开时一个都没打开，于是错判成"本地包没有"。

| 序号 | 怪 id | 名字 | 正主来源 | 尺寸 | 落地 |
|---|---|---|---|---|---|
| 46 | 1068 | 看守 | 运行时缓存包 `8d49d0b4c81aba82f0838e5c75daa696` | **200x200** | `贴图缓存\\UT_AccountHeadShot_Monster_46.png` |
| 47 | 1069 | 罪恶茶壶 | 运行时缓存包 `ff2f91de09c2abb96e1e12f54ac54718` | **160x160** | `贴图缓存\\UT_AccountHeadShot_Monster_47.png` |
| 48 | 1070 | 大盗 | 运行时缓存包 `81161b89ebd67c23d2fa977f7bf4ab73` | **160x160** | `贴图缓存\\UT_AccountHeadShot_Monster_48.png` |

源路径（完整）：
```
%USERPROFILE%\\AppData\\LocalLow\\feimo\\AstralParty_CN\\com.unity.addressables\\AssetBundles\\
    32f361363ee97c2eb5aec8dd7a1fcf5a\\8d49d0b4c81aba82f0838e5c75daa696\\__data   -> Monster_46
    ab4c4c3099096d64aaf0c0a52356eeab\\ff2f91de09c2abb96e1e12f54ac54718\\__data   -> Monster_47
    a536b06021dbf6bc6d9cca6bde202cef\\81161b89ebd67c23d2fa977f7bf4ab73\\__data   -> Monster_48
```
（一级目录名是另一套 hash，二级目录名才是 catalog 里 `bundle_name()` 给出的那个 —— 这就是我上一轮漏掉的地方。）

口径：与上一轮 01..45 一致 —— **UnityPy 从 `Texture2D` 解出的像素重新编码为 PNG**（无损），尺寸/像素与游戏一致。
（`GameRes\\Img` 里也有这三张成品 PNG，我比对过：**逐像素完全相同**，所以两条路是同一份资源的两处存放。）

## 错误根因（我的锅，写清楚以免再犯）

Addressables 运行时缓存的真实层级是：

```
AssetBundles\\<一级hash>\\<bundle hash>\\__data        ← 正确（两层）
AssetBundles\\<一级hash>\\__data                       ← 我上一轮查的（少一层）
```

`<一级hash>` 与 `<bundle hash>` **不是同一个值** ——
所以我拿 catalog 给的 `<bundle hash>` 去拼 `AssetBundles\\<bundle hash>\\__data`，路径根本不存在，
`Test-Path` 全 false，于是得出「本地包没有」。
"1149 个缓存包"这个**数字**是对的（我是用 `Get-ChildItem -Recurse -Filter __data` 数的），
但我**逐个打开**时用了浅一层的路径 —— 数字对、路径错，结论就反了。

## 更正后的完整来源表（01..48 全齐，三条路都没有的：无）

| 序号 | 怪 id | 名字 | 安装包 5500 | 运行时缓存 1149 | GameRes\\Img |
|---|---|---|---|---|---|
| 01..28 | 1001..1040 | （见清单） | 200x200 | - | 有 |
| 29 | 1041 | 武魂灵体-戮 | 200x200 | - | - |
| 30 | 1042 | 武魂灵体-怨 | 200x200 | - | - |
| 31..35 | 1043..1047 | 迎宾燕 / 家禽服务员 / 铜盏仙鹤·孔雀·鸳鸯 | 200x200 | - | 有 |
| 36 | 1054 | 三联爆竹呱 | 200x200 | - | - |
| 37 | 1056 | 智械海盗鲨 | 200x200 | - | 有 |
| 38 | 1057 | 智械海王鲨 | 200x200 | - | - |
| 39 | 1058 | 智械中枢 | 200x200 | - | - |
| 40..45 | 1059..1066 | 宝藏桶 / 裁判艾米 / 机械投手 / 机械四分卫 / 机械监控师 / 思维飞艇 | 200x200 | - | 有 |
| **46** | **1068** | **看守** | **-** | **200x200** | 有 |
| **47** | **1069** | **罪恶茶壶** | **-** | **160x160** | 有 |
| **48** | **1070** | **大盗** | **-** | **160x160** | 有 |

* 安装包（5500 个 `.bundle`，全量扫过）**只有 01..45** —— 这部分上一轮没错。
* 运行时下载缓存（1149 个包，**修正层级后重扫**）里**正好是 46/47/48** 这三只，别无其他 Monster。
* 三条路并起来 = **01..48 全齐**。
* ★ 尺寸疑点：`Monster.bin` 对 47/48 的登记与别的怪没区别，但**本地这两张 `Texture2D` 本身就是 160x160**
  （`m_Width=160`，缓存包与 `GameRes` 两处一致）；46 是 200x200。不是抽错，是资源本来如此。

## 顺手补齐

缓存包里还**独有** `UT_AccountHeadShot_Boss_14`（200x200），也一并落地了。
于是缓存现状：**Monster 48 张 + Boss 14 张 + Emoji 2 张**，这一族在本地是全的。

## 画风核对

`tools\\_sheet_pixel_monster.png` 已重出为 **48 张全图**（46/47/48 排在末尾）；
`tools\\_sheet_monster_three.png` 是专门的「新增三只 vs 既有样本」对照表。
三只都是同一套像素画法（纯色块 + 描边，颜色数 338 / 364 / 591，和既有 45 只的 389~565 同档）。

## 复查用的证据文件

* `tools\\_cache_inv.tsv` / `_cache_inv_report.txt` —— **修正层级后**的缓存包清单（1149 包 / 6766 条 Texture2D），
  里面 `UT_AccountHeadShot_Monster_*` **只有 46/47/48 三个**，可直接核对。
* `tools\\_monster_three_report.txt` —— GameRes 路径的抽取与逐像素比对记录。
* `tools\\_monster_three_report2.txt` —— 缓存包路径的抽取记录（含包内 `m_Width/m_Height`）。
* `tools\\_monster48_report.txt` —— 01..48 完整来源表。
"""

with io.open(DOC, "a", encoding="utf-8") as fh:
    fh.write(ADD)
print("简报已追加：%s（现 %d 字节）" % (DOC, os.path.getsize(DOC)))
