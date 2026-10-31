using System;
using System.Collections.Generic;

namespace DiyEditor
{
    /// <summary>
    /// ★★ 官方 `landType` ↔ 贴图 / 中文名 对照表 —— **全部取自游戏自己的表，不是猜的**。
    ///
    /// 两张表（都只读，在 `&lt;游戏根&gt;\server\data\gamedata\`）：
    ///
    ///   ① `Land.bin` —— 每个 landType 一行：
    ///        field 1 (wire 0) = landType
    ///        field 9 (wire 2) = **这一格用哪张平台贴图**（"UT_Platform_Shop" 这种）
    ///      （同行的 field 5 = 8000N、field 6 = 8000N0 是资源组号：实测 landType 22 的
    ///       组号 = 80007，与 landType 7 **完全相同** ⇒ 官方就是同一张图，不是我们省事。）
    ///   ② `STRLand.bin` —— key = **80000 + landType** 的中文名（"突击门" "疾行" "怪奇糖果机"…）。
    ///      由 `Land.Name()` 读（见 `BoardModel.cs`）。
    ///
    /// ★ 为什么这张表能解释用户说的「图标对不上」：
    ///   旧版那张表是**照着英文名猜贴图**（`Land_Born` → 找 `UT_Platform_Born.png`，
    ///   找不到就退到列表里下一个名字），于是：
    ///     · `landType 2 保障点` 退到了 `UT_Platform_Start`（和起始点长得一样）；
    ///     · `landType 23 礼物` 退到了 `UT_Platform_Relic`（画成筹码）；
    ///     · `landType 5 给奖励 / 13 医院 / 9 抽奖` 各自退到了**别的功能**的图上；
    ///     · `landType 26`（官方名 **怪奇糖果机**）被当成"小贩"画成商店。
    ///   现在改成"**按 Land.bin 的点名**"，对不上就是**没有**，回落色块 + 官方中文名。
    ///
    /// ★★ 编号贴图不当图标用：`UT_Platform_101…129 / 301…306` 这 30 张，人眼盘点
    ///   （`5-DIY关卡\_截图\_盘点_平台贴图.png`）**全是角色头像**（圆形人物画），
    ///   不是功能图标 —— `Land.bin` 给 landType 1（起始点）写的正是 `UT_Platform_108`，
    ///   说明起始点的底板图是**按玩家角色变的**（一局四个起始点、serial 0~3）。
    ///   ⇒ 按用户的话「认不出来的编号贴图不要瞎配」，这类名字**一律拒绝**，
    ///     落成"色块 + 官方中文名"，并列进待定清单（`Pending()`）。
    /// </summary>
    public static class LandTable
    {
        /// <summary>landType 的取值上界（board.py 的枚举是 0~27）。</summary>
        public const int MaxLand = 27;

        private static Dictionary<int, string> _tex;

        /// <summary>
        /// `landType → 官方贴图名`（**可能这张 png 本机没有**，用 `StemIfUsable` 判）。
        /// 读不到 `Land.bin` 时回落到下面那张从表里抄下来的表（离线也能对上）。
        /// </summary>
        public static Dictionary<int, string> Textures()
        {
            if (_tex != null) return _tex;
            var map = new Dictionary<int, string>();
            byte[] data = GameTables.TableBytes("Land.bin");
            if (data != null)
            {
                // 顶层 field 1 = repeated Land 行（另有一个 field 2 的 map 形态，内容一样，取一边就够）
                foreach (Proto.Field top in Proto.Parse(data))
                {
                    if (top.Number != 1 || top.Wire != 2 || top.Bytes == null) continue;
                    int land = -1;
                    string tex = null;
                    foreach (Proto.Field f in Proto.Parse(top.Bytes))
                    {
                        if (f.Number == 1 && (f.Wire == 0 || f.Wire == 5)) land = (int)f.Int;
                        else if (f.Number == 9 && f.Wire == 2) tex = Proto.Utf8(f.Bytes);
                    }
                    if (land >= 0 && !string.IsNullOrEmpty(tex) && !map.ContainsKey(land)) map[land] = tex;
                }
            }
            if (map.Count == 0) map = FallbackTextures();
            _tex = map;
            return _tex;
        }

        /// <summary>读不到 `Land.bin` 时的兜底（★ 逐条抄自 2026-10-03 的只读 dump，见 `tools\land_modes_report.txt`）。</summary>
        private static Dictionary<int, string> FallbackTextures()
        {
            var m = new Dictionary<int, string>
            {
                { 1,  "UT_Platform_108" },          // ★ 编号贴图（角色头像）—— 会被拒绝
                { 2,  "UT_Platform_Start" },
                { 3,  "UT_Platform_Teleport" },
                { 4,  "UT_Platform_TeleportPoint" },
                { 6,  "UT_Platform_Divine" },
                { 7,  "UT_Platform_Shop" },
                { 8,  "UT_Platform_Event" },
                { 9,  "UT_Platform_Lottery" },
                { 10, "UT_Platform_Gamble" },
                { 11, "UT_Platform_Fire" },
                { 12, "UT_Platform_MoveAgain" },
                { 13, "UT_Platform_Hospital" },
                { 14, "UT_Platform_Card" },
                { 15, "UT_Platform_Gold" },
                { 16, "UT_Platform_Destiny" },
                { 17, "UT_Platform_Damage" },
                { 18, "UT_Platform_Jump" },
                { 19, "UT_Platform_Monster" },
                { 20, "UT_Platform_Heal" },
                { 21, "UT_Platform_TeleportPoint" },
                { 22, "UT_Platform_Shop" },
                { 23, "UT_Platform_Gift" },
                { 24, "UT_Platform_Relic" },
                { 25, "UT_Platform_Gimmick" },
                { 26, "UT_Platform_CandyGhost" },
                { 27, "UT_Platform_MindTraining" },
            };
            return m;
        }

        /// <summary>官方给这一格点名的贴图（可能本机没有这张 png）；没有这一行就回 null。</summary>
        public static string TextureName(int land)
        {
            string s;
            return Textures().TryGetValue(land, out s) ? s : null;
        }

        /// <summary>
        /// ★ 这张贴图名是不是"编号贴图"（`UT_Platform_101` 这种）——
        ///   实测那 30 张全是**角色头像**，不是功能图标 ⇒ **绝不当格子图标用**。
        /// </summary>
        public static bool IsNumberedArt(string stem)
        {
            if (string.IsNullOrEmpty(stem)) return false;
            const string head = "UT_Platform_";
            if (!stem.StartsWith(head, StringComparison.Ordinal)) return false;
            string tail = stem.Substring(head.Length);
            if (tail.Length == 0) return false;
            foreach (char c in tail) if (c < '0' || c > '9') return false;
            return true;
        }

        /// <summary>
        /// ★ 真正能拿来当图标的贴图名：官方点名 + **文件确实在** + **不是编号头像**。
        /// 三条缺一条就回 null（调用方落"色块 + 官方中文名"）。
        /// </summary>
        public static string StemIfUsable(int land)
        {
            string name = TextureName(land);
            if (name == null) return null;
            if (IsNumberedArt(name)) return null;              // ★ 不瞎配
            return Art.ImagePath(name) != null ? name : null;   // ★ 本机没有就不装作有
        }

        /// <summary>为什么这一格有图 / 没图 —— 报告和界面都照这句话说，别自己编。</summary>
        public static string Reason(int land)
        {
            string name = TextureName(land);
            if (name == null) return "Land.bin 里没有 landType " + land + " 这一行";
            if (IsNumberedArt(name))
                return "Land.bin 写的是编号贴图 " + name + "（角色头像，不是功能图 ⇒ 不用）";
            if (Art.ImagePath(name) == null)
                return "Land.bin 写的是 " + name + "，但这台机器的 GameRes\\Img / 贴图缓存 / exe 内置里都没有 "
                     + name + ".png";
            // ★ 2026-10-12 加了"exe 内置兜底"之后，来源要说清（别把内置说成"游戏目录里在"）
            if (EmbeddedCache.IsBuiltInPath(Art.ImagePath(name)))
                return "Land.bin 写的是 " + name + "（磁盘上没有 ⇒ 用的是 exe 内置那份）";
            return "Land.bin 写的是 " + name + "（文件在）";
        }

        /// <summary>没有可用贴图、只能"色块 + 官方中文名"的 landType（0~27 里挑）。</summary>
        public static List<int> Pending()
        {
            var list = new List<int>();
            for (int land = 1; land <= MaxLand; land++)
                if (StemIfUsable(land) == null) list.Add(land);
            return list;
        }

        /// <summary>登记了几种（= Land.bin 里几行）。</summary>
        public static int TableSize { get { return Textures().Count; } }
    }
}
