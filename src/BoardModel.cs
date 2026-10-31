using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace DiyEditor
{
    /// <summary>棋盘上的一格（字段照 server\astralnext\board.py 的读法：id / landType / neighbors / x,y,z）。</summary>
    public sealed class BoardNode
    {
        public int Id;
        public int LandType;
        public int RuntimeLandType;
        public int Serial;
        public int Jump;
        public int Disable;
        public List<int> Neighbors = new List<int>();
        public double X, Y, Z;

        /// <summary>runtimeLandType 优先（board.py:118 就是这么取的）。</summary>
        public int EffectiveLand { get { return RuntimeLandType != 0 ? RuntimeLandType : LandType; } }

        /// <summary>★ 用户改过这一格（写进节点的 diy.landType）。第 2 步才用得上，这里先把形状留好。</summary>
        public int DiyLandType;

        public bool HasDiy { get { return DiyLandType > 0; } }

        /// <summary>★ 用户改过就用改的，没改就用官方的（"还原这一格"永远做得到）。</summary>
        public int ShownLand { get { return DiyLandType > 0 ? DiyLandType : EffectiveLand; } }
    }

    /// <summary>一块画板 = 服务端现成的 `boards\&lt;后缀&gt;.json` + 一个只给编辑器看的 `diy` 块。</summary>
    public sealed class BoardModel
    {
        public string Path = "";
        public string Text = "";            // 原文（保序回写用；没改过一个字节都不动）
        public int MapField;                // 文件里自己写的 map
        public string Scene = "";
        public List<BoardNode> Nodes = new List<BoardNode>();
        public string Note = "";            // 找不到时的原因 / 找到了的说明
        public bool FromDiy = false;        // 是从 DIY 关卡自己的 .board.json 读的

        public bool Ok { get { return Nodes.Count > 0; } }

        private Dictionary<int, BoardNode> _byId;

        public BoardNode Get(int id)
        {
            if (_byId == null)
            {
                _byId = new Dictionary<int, BoardNode>();
                foreach (BoardNode n in Nodes) _byId[n.Id] = n;
            }
            BoardNode found;
            return _byId.TryGetValue(id, out found) ? found : null;
        }

        /// <summary>出生点：serial -> 格（board.py:304 start_nodes）。</summary>
        public Dictionary<int, BoardNode> BornNodes()
        {
            var outMap = new Dictionary<int, BoardNode>();
            foreach (BoardNode n in Nodes)
                if (n.LandType == 1) outMap[n.Serial] = n;
            return outMap;
        }

        /// <summary>
        /// ★ 「起点 1 / 2 / 3 / 4」在数据里到底怎么表示 —— **查清了的结论**：
        ///
        ///   起点格是 `landType == 1`（`board.py:22 LAND_BORN = 1`），一局四个。
        ///   区分它们的是**格子的 `serial`**，而且**座位号 == serial**：
        ///     · `board.py:304 start_nodes(map_id)` —— `{item["serial"]: item}`，
        ///       docstring 说"抓包里 Player.Slot(6) = 1/2/3 的机器人正好站在
        ///       Born 节点里 serial 为 1/2/3 的那三个格子上"；
        ///     · `board.py:211 is_own_born` —— 拿 `battle.seats` 里的座位号去
        ///       `start_for_serial(.., seat)` 反查"这是不是我的起始点"。
        ///
        ///   官方数据实测（`5-DIY关卡\棋盘缓存\*.json`，以及 `4-服务端\进度.md:9295`
        ///   "4 个出生点 serial=[0,1,2,3] → 节点 42 / 47 / 21 / 1"）：
        ///     · 007 56 格 → serial 0/1/2/3 各有 1 格（其余 53 格 serial=0 但 landType≠1）；
        ///     · 008 / 010 / 016 / 023 同样 4 个，020 只有 3 个（serial 0/1/2）。
        ///   ⇒ 所以**不用自己发明 1~4 号**，serial 就是官方那套座位号，
        ///     编辑器把它显示成「起点1…起点4」= serial 0…3。
        ///
        ///   ★ 若上了 `diy.starts`（用户手动指定，见 `LevelDoc.StartsNodes`），
        ///     那就是**用户口径**，优先级高于官方 serial。
        /// </summary>
        public List<BoardNode> StartNodes()
        {
            var list = new List<BoardNode>();
            foreach (BoardNode n in Nodes) if (n.LandType == 1) list.Add(n);
            list.Sort(delegate (BoardNode a, BoardNode b)
            {
                int c = a.Serial.CompareTo(b.Serial);
                return c != 0 ? c : a.Id.CompareTo(b.Id);
            });
            return list;
        }

        /// <summary>官方那套起点（按 serial 排好），= `diy.starts` 没写时的默认值。</summary>
        public List<int> DefaultStarts()
        {
            var ids = new List<int>();
            foreach (BoardNode n in StartNodes()) ids.Add(n.Id);
            return ids;
        }

        /// <summary>
        /// 这一格是第几号起点（1..4）。不在起点表里回 0。
        /// ★ `order` = 生效的起点格 id 顺序（通常是 `LevelDoc.StartsNodes`）。
        /// </summary>
        public static int StartNumber(List<int> order, int nodeId)
        {
            if (order == null) return 0;
            for (int k = 0; k < order.Count; k++) if (order[k] == nodeId) return k + 1;
            return 0;
        }

        /// <summary>★ 有没有"被改过的格子"（第 2 步做换格；现在恒为 false）。</summary>
        public bool HasDiyEdits()
        {
            foreach (BoardNode n in Nodes) if (n.HasDiy) return true;
            return false;
        }

        /// <summary>
        /// 棋盘文件 → 画板名（"023"）。
        ///
        /// ★ 优先用 `scene` 字段（"Map_023" 取尾巴）—— 那是最准的；
        ///   没有 `scene` 才回落到 `map` 字段（棋盘缓存里写的是画板号本身，例如 23）。
        /// ★ 为什么不能拿"关卡 map 的后三位"当画板号（旧版就是这么错的）：
        ///   82016 的画板是 **023**（走 Map.bin 的 Mids→场景名那条链），
        ///   后三位是 016 —— 拿它去找，就会把**别的图的棋盘**当成这张图的。
        /// </summary>
        public string BoardSuffix()
        {
            if (Scene != null && Scene.Length > 0)
            {
                int at = Scene.LastIndexOf('_');
                if (at >= 0 && at + 1 < Scene.Length) return Scene.Substring(at + 1);
                return Scene;
            }
            return MapField.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>★ 认画板的判据（给 BoardStore 用）。</summary>
        public bool Matches(int mapId, string suffix)
        {
            if (mapId <= 0) return true;
            string mine = BoardSuffix();
            if (!string.IsNullOrEmpty(suffix))
            {
                // 期望的画板号已知（Map.bin 算出来的）：必须对得上
                int a, b;
                if (int.TryParse(mine, NumberStyles.Integer, CultureInfo.InvariantCulture, out a) &&
                    int.TryParse(suffix, NumberStyles.Integer, CultureInfo.InvariantCulture, out b))
                    return a == b;
                return string.Equals(mine, suffix, StringComparison.OrdinalIgnoreCase);
            }
            // 不知道画板号时才允许按 map 号宽松认
            return MapField == mapId
                || (MapField % 1000) == (mapId % 1000)
                || mapId.ToString(CultureInfo.InvariantCulture) == mine;
        }
    }

    /// <summary>地板类型 → 名字 / 颜色（编号照 board.py:20-61 的 LandType 枚举）。</summary>
    public static class Land
    {
        /// <summary>
        /// ★★ 中文名**以游戏自己的 `STRLand.bin` 为准**（key = 80000 + landType，
        ///   由 `Name()` 在运行时读表），下面这张只是**读不到表时的兜底**，
        ///   内容与 `STRLand.bin` 逐条对齐。
        ///
        /// ★ 为什么必须改：旧版这张表是"board.py 的英文名 + 自己意译"，好几条**跟游戏对不上**，
        ///   而图标又是照这些名字挑的 ⇒ 用户看到的「图标对不上」有一半是名字错了：
        ///     · 4  旧写"追击"     官方叫 **突击门**（Assault）
        ///     · 5  旧写"给奖励"   官方叫 **悬赏**
        ///     · 8  旧写"事件"     官方叫 **全员事件**
        ///     · 9  旧写"抽奖"     官方叫 **彩票**（Lottery）
        ///     · 10 旧写"赌一把"   官方叫 **猜猜乐**（Guessing）
        ///     · 11 旧写"电池"     官方叫 **炮台**（Cannon）
        ///     · 12 旧写"再走一次" 官方叫 **疾行**（Haste）
        ///     · 14 旧写"抽卡"     官方叫 **卡牌奖励**
        ///     · 15 旧写"摇星币"   官方叫 **天降横财**
        ///     · 16 旧写"命运"     官方叫 **个人命运**
        ///     · 17 旧写"掉血"     官方叫 **天降横祸**
        ///     · 18 旧写"跳格"     官方叫 **跳跃**
        ///     · 20 旧写"回血"     官方叫 **恢复**
        ///     · 21 旧写"怪物追击" 官方叫 **怪物突击门**
        ///     · 22 旧写"卡牌商店" 官方就叫 **商店**（和 7 同一张图、同一个资源组 80007）
        ///     · 25 旧写"机关"     官方叫 **机关控制台**
        ///     · 26 旧写"小贩"     官方叫 **怪奇糖果机**（贴图是糖果幽灵 ⇒ 名字对了图标才通）
        ///     · 27 旧写"心智训练" 官方叫 **思维强化**
        /// </summary>
        public static readonly Dictionary<int, string> Names = new Dictionary<int, string>
        {
            {0,"普通"}, {1,"起始点"}, {2,"保障点"}, {3,"传送门"}, {4,"突击门"}, {5,"悬赏"},
            {6,"占卜"}, {7,"商店"}, {8,"全员事件"}, {9,"彩票"}, {10,"猜猜乐"}, {11,"炮台"},
            {12,"疾行"}, {13,"医院"}, {14,"卡牌奖励"}, {15,"天降横财"}, {16,"个人命运"},
            {17,"天降横祸"}, {18,"跳跃"}, {19,"怪物"}, {20,"恢复"}, {21,"怪物突击门"},
            {22,"商店"}, {23,"礼物"}, {24,"筹码商店"}, {25,"机关控制台"}, {26,"怪奇糖果机"},
            {27,"思维强化"},
        };

        /// <summary>★ 配色：功能相近的用同一个色系，一眼能分出"打架的 / 买东西的 / 走路的"。</summary>
        public static string Hex(int land)
        {
            switch (land)
            {
                case 0: return "#FF2E3646";   // 普通
                case 1: return "#FF3FBF7F";   // 起始点 绿
                case 2: return "#FF3AA0C8";   // 保障点 蓝
                case 3: return "#FF9A6BE0";   // 传送门 紫
                case 4: return "#FFD8623F";   // 追击 橙红
                case 5: return "#FFC9A227";   // 给奖励
                case 6: return "#FF8E6BC8";   // 占卜
                case 7: return "#FFE0A93B";   // 商店 金
                case 8: return "#FFD2B24A";   // 事件
                case 9: return "#FFB98B3A";   // 抽奖
                case 10: return "#FFA8573F";  // 赌
                case 11: return "#FF5C8F4A";  // 电池
                case 12: return "#FF4E8FBF";  // 再走一次
                case 13: return "#FF4FA98C";  // 医院
                case 14: return "#FF7E8FD8";  // 抽卡
                case 15: return "#FFC9A227";  // 摇星币
                case 16: return "#FF8A7BD8";  // 命运
                case 17: return "#FFC0453F";  // 掉血
                case 18: return "#FF5FA3D8";  // 跳格
                case 19: return "#FFB0453F";  // 怪物（打架）
                case 20: return "#FF57C08A";  // 回血
                case 21: return "#FFCC5A38";  // 怪物追击
                case 22: return "#FFD9A03B";  // 卡牌商店
                case 23: return "#FFC77FA8";  // 礼物
                case 24: return "#FFCFAE4A";  // 筹码商店
                case 25: return "#FF7A6AA8";  // 机关
                case 26: return "#FFB98B3A";  // 小贩
                case 27: return "#FF7E9AD8";  // 心智训练
                default: return "#FF3A4354";
            }
        }

        /// <summary>
        /// ★ 中文名：**先读游戏 `STRLand.bin`**（key = 80000 + landType），读不到才用兜底表。
        ///   这样名字永远和游戏里显示的一致（旧版手工意译的名字就是"图标对不上"的一半原因）。
        /// </summary>
        public static string Name(int land)
        {
            string s;
            Dictionary<int, string> official = GameTables.StrTable("STRLand");
            if (official.TryGetValue(80000 + land, out s) && !string.IsNullOrEmpty(s)) return s;
            return Names.TryGetValue(land, out s) ? s : ("未知(" + land + ")");
        }

        /// <summary>这个名字是读游戏表来的，还是用的兜底表（报告里要如实说）。</summary>
        public static bool NameFromGameTable(int land)
        {
            string s;
            return GameTables.StrTable("STRLand").TryGetValue(80000 + land, out s)
                && !string.IsNullOrEmpty(s);
        }

        /// <summary>★ 贴图名（认得出才有）；认不出就回空串，调用方走"色块 + 中文名"。</summary>
        public static string TextureStem(int land)
        {
            return Art.PlatformTexture(land) ?? "";
        }

        /// <summary>官方的 STRLand key（报告 / 自测要对这个号）。</summary>
        public static int StrKey(int land) { return 80000 + land; }

        // ============================================================ ★★ 格子的 PvE / PvP 分组
        //
        // 为什么要有这一节：用户说「把 pvp 和 pve 的格子分开」「先不用 pvp 的地块」
        // —— 上一批只分了**地图模板**，可「换成什么功能」那个下拉框里还是**全部 28 种**。
        // 这一节给每一种 landType 定**归属**，界面按归属列，默认只列 PvE。
        //
        // ★★ 依据（不是猜的，全部只读）：
        //   ① **官方 PvE 关卡的棋盘**：`<游戏根>\server\data\boards\` 里那 6 张
        //      007/008/010/016/020/023，就是 PvE 系（模式 4 合作挑战 / 9 练习 / 12 畸变组件）
        //      的 82007/82008/82010/82016/82020(?)/82023 用的画板（`Map.bin` 的
        //      `UT_MapScene_0XX` 与 `GameMode.bin` 的 f7 地图号段实测对得上）。
        //      **这些盘上真出现过的 landType 就是"PvE 用得到"** —— 直接读盘统计，见
        //      `MergePveFromBoards()`（运行时还会把官方盘并进来，游戏更新了也不用改代码）。
        //   ② **真机抓包**：`3-工具\_真机LocalLow备份_20261002-200053\...\pve_capture\frames.jsonl`
        //      里那条整局快照（36 KB，带角色名/UID 的那条）能抠出 PvE 局里出现的
        //      0/1/2/3/4/5/6/8/10/11/20 —— 补上了官方那 6 张盘上**没有**的 4/5/6/10/11。
        //   ③ **规则语义**（这一条是【推断】，报告里点了名）：
        //      `server\astralnext\board.py` 把 4(突击门)/21(怪物突击门) 归在
        //      "突击门"这一族、`handlers.py:5549` 写「条件是格子的 LandType（21 怪物突击门 / 12 疾行）」，
        //      而 23(礼物)/5(悬赏) 只服务对抗玩法（礼物大作战 / 悬赏分）。
        //
        // ★ 口径与模板页一致：**两边都出现的算 PvE**（PvE 也要用），只有 PvP 独有才收起来。
        // ★ 默认不显示 PvP，给一个勾选框（默认不勾）—— 与模板页的「显示 PvP（暂不支持）」同款。

        /// <summary>格子归属：PvE 也用得到的 / 只有 PvP 用的。</summary>
        public enum LandGroup
        {
            /// <summary>PvE 通用（官方 PvE 盘上出现过，或 PvE 局抓包 / 规则语义证实用得上）。</summary>
            PveCommon = 1,
            /// <summary>PvP 专用（只在对抗/限时玩法里用；本项目**暂不支持**，默认不列）。</summary>
            PvpOnly = 2,
        }

        /// <summary>
        /// ★★ **官方 PvE 关卡的棋盘**（`server\data\boards\` 那 6 张 007/008/010/016/020/023）
        ///   上真出现过的 landType，按"读盘统计"抄下来（`tools\land_pve_pvp_split.txt`），
        ///   外加 **0 普通**（官方不给它起名/写说明，但"没有功能"的空白格到处都是，官方盘也用它）。
        ///   运行时还会再读一遍盘并合并 —— 这几条只是"盘读不到时的底"。
        ///   ★ 22 商店**不在**这里：它虽然也长在那 6 张盘上，但官方说明写着"**转账给友方角色**" ⇒ PvP。
        /// </summary>
        public static readonly int[] PveLandsFromOfficialBoards =
            new int[] { 0, 1, 2, 3, 8, 12, 14, 15, 17, 19, 20, 21, 24, 26, 27 };

        /// <summary>
        /// ★★★ **只有 PvP 用得到的格子** —— 判据是**官方自己的说明文案**
        ///   （`STRLand.bin`，key = **800000 + landType×10** 那一条；`OfficialNote` 就是读它）。
        ///   说明里要求"**敌方角色 / 该角色 / 随机角色 / 每个角色平分 / 转账给友方 / 归还**"这类
        ///   **必须存在其他玩家**的前提，或者这一格在官方 PvE 那 6 张盘上**一格都没有** ⇒ 收进 PvP。
        ///
        ///   | landType | 官方说明（原样抄） | 为什么 PvP |
        ///   |---|---|---|
        ///   | 4  突击门   | 可以指定一个**敌方角色**，进行突击挑战 | 要有敌方角色 |
        ///   | 5  悬赏     | 角色被【悬赏】，击倒**该角色**可以获得【赏金】 | 被悬赏的是别的角色 |
        ///   | 6  占卜     | 为**随机角色**触发一次占卜事件 | 多人玩法的娱乐格（用户点名） |
        ///   | 7  商店     | 经过或停留时，可以购买卡牌 | 与 22 同名同图同资源组 80007 ⇒ 官方这一族是 PvP 商店；PvE 用 24/26 |
        ///   | 9  彩票     | 自选1-12内不可重复的数字，开奖后清空 | 开奖要对多个玩家 |
        ///   | 10 猜猜乐   | 押注后**每个角色**猜奇偶数，猜对的角色将**平分**奖金 | 平分 ⇒ 多人 |
        ///   | 11 炮台     | 可以指定一个**敌方角色**，造成2点伤害 | 要有敌方角色 |
        ///   | 13 医院     | 判定生病则住院并且恢复2点生命值。**在此格免疫一切伤害** | 关人 + 免疫 = 对抗玩法的惩罚格 |
        ///   | 16 个人命运 | 触发**自身**的随机事件 | 官方 PvE 盘上一格都没有 |
        ///   | 18 跳跃     | 传送到对应的**跳跃格** | 要成对布置跳跃格（对抗/谜题盘），PvE 盘上没有 |
        ///   | 22 商店     | 经过或停留时，可以购买卡牌或将星币**转账给友方角色** | 转账 ⇒ 多人 |
        ///   | 23 礼物     | 经过时可以停留，停留时可以获得或**归还**一个礼物 | 礼物大作战（PvP 模式 7 的玩法） |
        ///
        ///   ⇒ PvE 侧留下 **16 种**：既在官方 PvE 盘上出现、说明里又不依赖其他玩家。
        ///   ★ 想调某一格归哪边，只改这一个数组（必要时再加 `PveLandsFromOfficialBoards`）。
        /// </summary>
        public static readonly int[] PvpOnlyBuiltIn =
            new int[] { 4, 5, 6, 7, 9, 10, 11, 13, 16, 18, 22, 23 };

        /// <summary>
        /// ★★ PvP 侧第一档：**官方说明里明写了"要别的角色"**（自测 ⑪d 逐条对 `OpponentWords` 验）。
        ///   4 突击门 / 5 悬赏 / 6 占卜 / 9 彩票 / 10 猜猜乐 / 11 炮台 / 13 医院 / 22 商店 / 23 礼物。
        /// </summary>
        public static readonly int[] PvpNoteVsPlayer = new int[] { 4, 5, 6, 9, 10, 11, 13, 22, 23 };

        /// <summary>
        /// ★ PvP 侧第三档：**靠推断**判 PvP 的 4 种（★ 报告里点名，是这批最软的依据）：
        ///   13 医院 / 16 个人命运 / 18 跳跃 / 25 机关控制台。
        ///   依据只有"**官方 PvE 那 6 张盘上一格都没有**"，官方说明里**没写**要对手。
        ///   ★ 用户口径（2026-10-03）：这类 PvP 味道的格子**先别列进 PvE** ⇒ 照样收在 PvP 侧。
        /// </summary>
        public static readonly int[] PvpInferred = new int[] { 13, 16, 18, 25 };

        /// <summary>
        /// ★ 官方说明里出现这些字样，就说明这一格**要求有对手 / 多个玩家** ⇒ 天生属于 PvP。
        ///   （自测 ⑪d 用它盯"PvP 那批是不是都真有对手前提"。）
        ///   ★ "开奖"：彩票的原话是"自选1-12内不可重复的数字，**开奖**后清空" ——
        ///     开奖这件事本身要"有别人也下了注"，一个人凑不出开奖。
        /// </summary>
        public static readonly string[] OpponentWords = new string[]
        {
            "敌方角色", "该角色", "随机角色", "每个角色", "平分", "转账", "归还", "开奖",
        };

        /// <summary>
        /// ★ 这两种**不是**靠"说明里有对手字样"判的，是**手工按商店语义**判的（报告里点了名）：
        ///   7 商店 与 22 商店 官方**同名、同图、同资源组 80007** ⇒ 属同一族；
        ///   22 的说明写着"将星币**转账给友方角色**"（多人前提）⇒ 这一族都是 PvP 商店。
        ///   PvE 侧用 24 筹码商店 / 26 怪奇糖果机 来买东西。
        /// </summary>
        public static readonly int[] PvpShopSemantics = new int[] { 7, 22 };

        /// <summary>
        /// 这一格的**官方说明文案**（`STRLand.bin` key = 800000 + landType×10）；
        /// 读不到就回空串（0 普通官方就没给说明）。界面 / 报告都用它，别自己编。
        /// </summary>
        public static string OfficialNote(int land)
        {
            string s;
            var t = GameTables.StrTable("STRLand");
            if (t.TryGetValue(800000 + land * 10, out s) && !string.IsNullOrEmpty(s)) return s;
            return "";
        }

        /// <summary>官方说明里有没有"要求别的角色参与"的字样（自测 ⑪d 用它）。</summary>
        public static bool NoteNeedsOpponent(int land)
        {
            string note = OfficialNote(land);
            foreach (string w in OpponentWords)
                if (note.IndexOf(w, StringComparison.Ordinal) >= 0) return true;
            return false;
        }

        private static int[] _pveLands;

        /// <summary>
        /// ★ 自测 / 报告用：这条清单是**从哪读来的**（盘目录 + 读到几张 + 补了几种）。
        ///   "归属不许猜" ⇒ 依据必须能一眼看到，所以把它当事实记下来给界面/自测读。
        /// </summary>
        public static string PveSource = "（还没算过）";

        /// <summary>
        /// ★ 运行时把"官方 PvE 关卡盘上出现的 landType"读出来（只读 `server\data\boards\*.json`），
        ///   与内置那份**合并**。读不到盘（没装游戏 / 用户机器上没有服务端）就只用内置那份。
        ///   —— 这样游戏以后加一张 PvE 图、盘上多一种格子，编辑器**不用改代码**也会跟上。
        ///   ★★ 但**盘上见过的格子只进 PvE**；`PvpOnlyBuiltIn` 里那 12 种是"官方说明要求有对手"
        ///      ⇒ **不因为盘上出现就翻案**（用户 2026-10-03 明确点过：占卜这类是 PvP 的）。
        /// </summary>
        public static int[] MergePveFromBoards()
        {
            if (_pveLands != null) return _pveLands;
            var set = new List<int>();
            foreach (int land in PveLandsFromOfficialBoards) if (!set.Contains(land)) set.Add(land);
            int builtin = set.Count;
            string dir = BoardScanDir();
            int files = 0, fromBoards = 0, skipped = 0;
            if (dir != null)
            {
                string[] got;
                try { got = System.IO.Directory.GetFiles(dir, "*.json"); }
                catch (Exception) { got = new string[0]; }
                foreach (string f in got)
                {
                    files++;
                    foreach (int land in LandsInBoardFile(f))
                    {
                        if (land < 0 || land > LandTable.MaxLand) continue;
                        if (IsPvpBuiltIn(land)) { skipped++; continue; }   // ★ 说明里要对手 ⇒ 不翻案
                        if (!set.Contains(land)) { set.Add(land); fromBoards++; }
                    }
                }
            }
            set.Sort();
            _pveLands = set.ToArray();
            PveSource = (dir == null
                    ? "没找到官方棋盘目录（只用了内置那份）"
                    : dir + " 里 " + files + " 张盘")
                + "；内置 " + builtin + " 种 + 盘上新发现 " + fromBoards + " 种 = " + _pveLands.Length + " 种"
                + (skipped > 0 ? "（盘上出现但按官方说明判为 PvP、没翻案的 " + skipped + " 次）" : "");
            return _pveLands;
        }

        /// <summary>内置的"PvP 专用"清单里有这一种吗。</summary>
        public static bool IsPvpBuiltIn(int land)
        {
            foreach (int k in PvpOnlyBuiltIn) if (k == land) return true;
            return false;
        }

        /// <summary>
        /// 官方棋盘目录（只读；找不到回 null）。
        /// ★ 先问 `GameLocator.BoardSourceDir`（它已经带"从本工具目录往上找 `4-服务端\server\data\boards`"
        ///   和 `D:\小小兔趴\...` 这两条回落），再退回"游戏根下的 server\data\boards"。
        /// </summary>
        private static string BoardScanDir()
        {
            string a = GameLocator.BoardSourceDir;
            if (!string.IsNullOrEmpty(a)) return a;
            string g = GameLocator.GameDir;
            if (string.IsNullOrEmpty(g)) return null;
            string p = System.IO.Path.Combine(g, "server", "data", "boards");
            try { return System.IO.Directory.Exists(p) ? p : null; }
            catch (Exception) { return null; }
        }

        /// <summary>从一份棋盘 json 里把出现过的 landType 抠出来（**纯读**，不解析整个模型）。</summary>
        private static List<int> LandsInBoardFile(string path)
        {
            var outList = new List<int>();
            string text;
            try { text = System.IO.File.ReadAllText(path); }
            catch (Exception) { return outList; }
            // 盘里的形状是 {"landType": 8, ...}；只认这个键，别把 runtimeLandType 也算成一种
            const string key = "\"landType\"";
            int at = 0;
            while (true)
            {
                int i = text.IndexOf(key, at, StringComparison.Ordinal);
                if (i < 0) break;
                at = i + key.Length;
                int c = text.IndexOf(':', at);
                if (c < 0) break;
                int e = c + 1;
                while (e < text.Length && (text[e] == ' ' || text[e] == '\t')) e++;
                int s = e;
                while (e < text.Length && text[e] >= '0' && text[e] <= '9') e++;
                int val;
                if (e > s && int.TryParse(text.Substring(s, e - s), out val)
                    && !outList.Contains(val)) outList.Add(val);
            }
            return outList;
        }

        /// <summary>这一种格子归哪一组（PvE 通用 / PvP 专用）。</summary>
        public static LandGroup Group(int land)
        {
            foreach (int k in MergePveFromBoards()) if (k == land) return LandGroup.PveCommon;
            return LandGroup.PvpOnly;
        }

        /// <summary>是不是 PvP 专用（界面上默认不列的那一批）。</summary>
        public static bool IsPvpOnly(int land) { return Group(land) == LandGroup.PvpOnly; }

        /// <summary>
        /// 「换成什么功能」列哪些：`showPvp=false`（**默认**）⇒ 只列 PvE 那批；
        /// true ⇒ 两批都列（PvP 那批在界面上标"暂不支持"）。
        /// </summary>
        public static List<int> PickIds(bool showPvp)
        {
            var list = new List<int>();
            if (showPvp) { for (int land = 0; land <= LandTable.MaxLand; land++) list.Add(land); return list; }
            int[] pve = MergePveFromBoards();
            for (int land = 0; land <= LandTable.MaxLand; land++)
                foreach (int k in pve) if (k == land) { list.Add(land); break; }
            return list;
        }

        /// <summary>PvP 专用那一批（按 id 排序）。</summary>
        public static List<int> PvpOnlyIds()
        {
            var list = new List<int>();
            for (int land = 0; land <= LandTable.MaxLand; land++) if (IsPvpOnly(land)) list.Add(land);
            return list;
        }

        /// <summary>
        /// 归属的依据（报告 / 界面 tooltip 都照这句说，别自己编）。
        /// ★ PvP 那批一律**引官方说明原文**（`STRLand.bin` key=800000+land×10），
        ///   因为"为什么它算 PvP"的硬证据就是那句话（"敌方角色 / 随机角色 / 平分 / 转账…"）。
        /// </summary>
        public static string GroupReason(int land)
        {
            string name = Name(land);
            string note = OfficialNote(land);
            if (Group(land) == LandGroup.PveCommon)
            {
                foreach (int k in PveLandsFromOfficialBoards)
                    if (k == land)
                        return name + "：官方 PvE 关卡的画板（007/008/010/016/020/023）上出现过"
                             + (note.Length > 0 ? "；官方说明：" + note : "") + " ⇒ PvE 通用";
                return name + "：官方 PvE 画板上出现过 ⇒ PvE 通用";
            }
            if (note.Length > 0 && NoteNeedsOpponent(land))
                return name + "：官方说明「" + note + "」要求有别的角色参与 ⇒ PvP 专用（本项目暂不支持）";
            foreach (int k in PvpShopSemantics)
                if (k == land)
                    return name + "：与 22 同名同图同资源组 80007（官方这一族是 PvP 商店，22 的说明是"
                         + "「" + OfficialNote(22) + "」）⇒ PvP 专用（本项目暂不支持）";
            return name + "：官方 PvE 盘上一格都没有，且官方也没给它（依赖多个玩家的）说明 ⇒ PvP 专用（本项目暂不支持）";
        }

        /// <summary>把一串 id 压成"1-3、8、12"这种给人看的写法（界面提示 / 报告用）。</summary>
        public static string RangeText(List<int> ids)
        {
            if (ids == null || ids.Count == 0) return "（无）";
            var sorted = new List<int>(ids);
            sorted.Sort();
            var parts = new List<string>();
            int start = sorted[0], prev = sorted[0];
            for (int i = 1; i <= sorted.Count; i++)
            {
                int cur = i < sorted.Count ? sorted[i] : int.MinValue;
                if (i < sorted.Count && cur == prev + 1) { prev = cur; continue; }
                parts.Add(start == prev ? start.ToString(CultureInfo.InvariantCulture)
                                        : start.ToString(CultureInfo.InvariantCulture) + "-"
                                          + prev.ToString(CultureInfo.InvariantCulture));
                if (i < sorted.Count) { start = cur; prev = cur; }
            }
            return string.Join("、", parts.ToArray());
        }

        /// <summary>自测 / 报告用：把两批一次说清。</summary>
        public static string SplitText()
        {
            var pve = new List<int>();
            for (int land = 0; land <= LandTable.MaxLand; land++) if (!IsPvpOnly(land)) pve.Add(land);
            return "PvE 通用 " + pve.Count + " 种（" + RangeText(pve) + "）· PvP 专用 "
                 + PvpOnlyIds().Count + " 种（" + RangeText(PvpOnlyIds()) + "）";
        }
    }

    /// <summary>
    /// ★ 棋盘怎么找到（全部**只读**）：
    ///   ① 这份 DIY 关卡自己带的 `.board.json`（`diy.board` 指的，跟关卡文件同目录）—— 最优先；
    ///   ② `5-DIY关卡\棋盘缓存\&lt;后缀&gt;.json`（本工具自己的副本，随时可拔线）；
    ///   ③ 用户手动指定的棋盘目录；
    ///   ④ ★★ **exe 里内置的那 26 份官方画板**（用户 2026-10-12：「要内置」）——
    ///      磁盘上 ①②③ 都没有时才走它。于是"只拿到一个 exe、放在任何空目录里"也能出棋盘。
    ///   ★ 读取顺序 = **磁盘优先、内置兜底**，而且内置的那份**绝不落盘**
    ///     （不解包、不生成缓存副本）——见 `EmbeddedCache` / `BoardCache`。
    ///
    /// ★ 为什么不去 `4-服务端\server\data\boards\`：那是**别人的线上数据**，
    ///   这个编辑器只该读、不该依赖、更不该写。要就拷进「棋盘缓存」。
    ///
    /// ★ 认画板的判据：文件里的 `map` 字段要跟关卡对得上（`map` 取后三位）。
    ///   两条都试不通就当**不是这一张**，绝不硬用（用错棋盘比没有棋盘更糟）。
    /// </summary>
    public static class BoardStore
    {
        /// <summary>用户手动指定的棋盘目录（可为空）。</summary>
        public static string UserBoardDir = "";

        /// <summary>这份 DIY 关卡自己带的棋盘文件绝对路径（可为空）。</summary>
        public static string DiyBoardPath = "";

        /// <summary>
        /// ★ 期望的画板号（"023"）。由 `LevelDoc` 从 Map.bin 算出来填进来；
        /// 有了它，认棋盘就是"画板号对得上才算"，比拿 map 号猜可靠得多。
        /// </summary>
        public static string ExpectedSuffix = "";

        /// <summary>本工具自己的棋盘缓存目录（跟着 exe 走；开发时从 bin 往上找到 5-DIY关卡）。
        ///   ★ 这是**磁盘**目录；磁盘上没有时由 `BoardCache` 回落到 exe 内置的那一份。
        ///   ★ 可写：自测里要"临时拿一个带改动的目录当磁盘缓存"来验**磁盘优先**（验完还原）。</summary>
        public static string CacheDir
        {
            get
            {
                if (!string.IsNullOrEmpty(_cacheDirOverride)) return _cacheDirOverride;
                string dir = AppContext.BaseDirectory.TrimEnd('\\');
                for (int k = 0; k < 10 && !string.IsNullOrEmpty(dir); k++)
                {
                    if (string.Equals(System.IO.Path.GetFileName(dir), "5-DIY关卡",
                            StringComparison.OrdinalIgnoreCase))
                        return System.IO.Path.Combine(dir, "棋盘缓存");
                    dir = System.IO.Path.GetDirectoryName(dir);
                }
                return System.IO.Path.Combine(AppContext.BaseDirectory, "棋盘缓存");
            }
            set { _cacheDirOverride = value ?? ""; }
        }

        /// <summary>自测临时指定的磁盘缓存目录（空 = 按 exe 自己算）。</summary>
        private static string _cacheDirOverride = "";

        private static readonly Dictionary<string, BoardModel> _cache = new Dictionary<string, BoardModel>();

        public static BoardModel Load(int mapId, string boardDirHint)
        {
            string key = mapId + "|" + (boardDirHint ?? "") + "|" + DiyBoardPath + "|" + ExpectedSuffix;
            BoardModel hit;
            if (_cache.TryGetValue(key, out hit)) return hit;
            BoardModel model = LoadCore(mapId, boardDirHint);
            _cache[key] = model;
            return model;
        }

        /// <summary>棋盘缓存整体作废（自测里换目录时用）。</summary>
        public static void ClearCache()
        {
            _cache.Clear();
        }

        private static List<string> CandidateDirs(string boardDirHint)
        {
            var dirs = new List<string>();
            if (!string.IsNullOrEmpty(boardDirHint) && Directory.Exists(boardDirHint)) dirs.Add(boardDirHint);
            if (!string.IsNullOrEmpty(UserBoardDir) && Directory.Exists(UserBoardDir)) dirs.Add(UserBoardDir);
            // ★ 磁盘上的「棋盘缓存」**不在这儿加** —— 那一条在 `LoadCore` 里**显式先试一轮**（磁盘优先）。
            //   这里只加一个**哨兵**：它让循环至少跑一轮，而 `TryReadPathOrBuiltIn` 看到哨兵就
            //   走"内置兜底"那一支 ⇒ "磁盘（DIY 自带 / 用户指定 / 磁盘缓存）都没有 ⇒ 用 exe 里那份"。
            dirs.Add(BuiltInSentinel);
            return dirs;
        }

        /// <summary>"轮到内置那一步了"的哨兵值（不是真目录，见 `TryReadPathOrBuiltIn`）。</summary>
        private const string BuiltInSentinel = "\u0001内置";

        /// <summary>
        /// ★★ **一处收口**：按后缀读一份棋盘 —— 磁盘（DIY 自带 / 用户指定目录）优先，
        ///   磁盘没有才回 exe 内置的那份。读不出回 null。
        ///   ★ 只有走到"内置"那一支时才会给模型补一句"**磁盘上没有 ⇒ 用的是 exe 内置那一份**"，
        ///     让人一眼看出这块画板是从哪儿来的（而不是以为磁盘上真有那份文件）。
        /// </summary>
        private static BoardModel TryReadPathOrBuiltIn(string dir, string name, int mapId)
        {
            bool builtInStep = string.Equals(dir, BuiltInSentinel, StringComparison.Ordinal);

            if (!builtInStep)
            {
                string path = Path.Combine(dir, name + ".json");
                if (File.Exists(path))
                {
                    BoardModel got = TryRead(path, mapId, false);
                    if (got != null) return got;
                }
                return null;      // 这一趟是磁盘目录：没有就是没有（内置那份由哨兵那一趟负责）
            }

            // ★★ 内置兜底：**只有磁盘上哪儿都没有才走到这儿**（`LoadCore` 已经先把
            //   磁盘缓存目录显式试过一轮了，见那一处的注释）。
            string logical = EmbeddedCache.BoardLogicalName(name);
            if (!EmbeddedCache.HasLogical(logical)) return null;
            string text = EmbeddedCache.Text(EmbeddedCache.BoardFolder, name + ".json");
            if (text == null) return null;
            string marker = EmbeddedCache.BoardFolder + Path.DirectorySeparatorChar + name + ".json";
            BoardModel one = TryReadText(text, marker, mapId, false);
            if (one != null) one.Note += "（磁盘上没有 ⇒ 用的是 exe 内置那一份）";
            return one;
        }

        private static BoardModel LoadCore(int mapId, string boardDirHint)
        {
            var model = new BoardModel();
            if (mapId <= 0)
            {
                model.Note = "关卡里没写 map（或者写成了 0）⇒ 不知道该找哪块画板。";
                return model;
            }

            string suffix3 = (mapId % 1000).ToString("000", CultureInfo.InvariantCulture);
            string suffixRaw = (mapId % 1000).ToString(CultureInfo.InvariantCulture);

            // ★ 期望的画板号：优先用 Map.bin 算出来的那个（82016 → "023"），
            //   没有才回落到"map 后三位"这种将就办法。
            string want = !string.IsNullOrEmpty(ExpectedSuffix) ? ExpectedSuffix : suffix3;

            // ① ★ 这份 DIY 关卡自己带的棋盘文件
            if (!string.IsNullOrEmpty(DiyBoardPath) && File.Exists(DiyBoardPath))
            {
                BoardModel got = TryRead(DiyBoardPath, mapId, true);
                if (got != null) return got;
                model.Note = "这份 DIY 关卡指了自己的棋盘文件，但读不动：" + DiyBoardPath + "。";
            }

            var names = new List<string> { want, suffix3, suffixRaw, mapId.ToString(CultureInfo.InvariantCulture) };

            // ★★ ② ★ **磁盘上的「棋盘缓存」先试一遍**（用户 2026-10-12 定的口径：**磁盘优先**）——
            //   这一步**明写在这儿**，不靠循环里的某一趟：磁盘上有 `<后缀>.json` 就直接用它，
            //   磁盘上没有才轮到下面的内置兜底。用户可以在自己机器上覆盖 / 更新 / 删掉这份缓存。
            foreach (string name in names)
            {
                string cachedPath = BoardCache.TargetPath(name);
                if (cachedPath == null || !File.Exists(cachedPath)) continue;
                BoardModel hit = TryRead(cachedPath, mapId, false);
                if (hit != null) return hit;
            }

            List<string> cands = CandidateDirs(boardDirHint);
            foreach (string dir in cands)
            {
                foreach (string name in names)
                {
                    BoardModel got = TryReadPathOrBuiltIn(dir, name, mapId);
                    if (got != null) return got;
                }

                if (string.Equals(dir, BuiltInSentinel, StringComparison.Ordinal)) continue;   // 哨兵没有磁盘目录可扫

                try
                {
                    foreach (string file in Directory.GetFiles(dir, "*.json"))
                    {
                        int asInt;
                        string stem = Path.GetFileNameWithoutExtension(file);
                        if (!int.TryParse(stem, NumberStyles.Integer, CultureInfo.InvariantCulture, out asInt)) continue;
                        // 文件名只是"可能"，真正的判据在 TryRead 里（画板号对不上就否掉）
                        if (asInt != mapId && (asInt % 1000) != (mapId % 1000)
                            && !string.Equals(stem, want, StringComparison.OrdinalIgnoreCase)) continue;
                        BoardModel got = TryRead(file, mapId, false);
                        if (got != null) return got;
                    }
                }
                catch (Exception) { }
            }

            // ③ ★ 磁盘缓存目录里"按文件名扫一遍"（`TryReadPathOrBuiltIn` 只试了几个名字，
            //    这里是"文件名跟画板号对不上的那些"）—— **磁盘优先，没扫到才回内置那份**。
            try
            {
                foreach (string marker in BoardCache.EnumerateJson())
                {
                    int asInt;
                    string stem = Path.GetFileNameWithoutExtension(marker);
                    if (!int.TryParse(stem, NumberStyles.Integer, CultureInfo.InvariantCulture, out asInt)) continue;
                    if (asInt != mapId && (asInt % 1000) != (mapId % 1000)
                        && !string.Equals(stem, want, StringComparison.OrdinalIgnoreCase)) continue;
                    string text = BoardCache.TextOf(marker);
                    if (text == null) continue;
                    BoardModel got = TryReadText(text, marker, mapId, false);
                    if (got != null)
                    {
                        if (BoardCache.IsBuiltIn(marker))
                            got.Note += "（磁盘上没有 ⇒ 用的是 exe 内置那一份）";
                        return got;
                    }
                }
            }
            catch (Exception) { }

            string where = BoardCache.DirExists                ? "「棋盘缓存」目录和 exe 内置的那 26 份官方画板里都没有"
                : "磁盘上没有「棋盘缓存」目录，exe 内置的那 26 份官方画板里也没有";
            model.Note = "没找到 map=" + mapId + "（画板 " + want + "）的棋盘图。"
                       + where + "。"
                       + "把 boards\\" + want + ".json 拷进「棋盘缓存」目录（或放到「指定棋盘目录…」那个目录）就能显示棋盘。";
            return model;
        }

        private static BoardModel TryRead(string path, int mapId, bool fromDiy)
        {
            string text;
            try { text = File.ReadAllText(path, Encoding.UTF8); }
            catch (Exception) { return null; }
            return TryReadText(text, path, mapId, fromDiy);
        }

        /// <summary>
        /// ★ 原文 → 模型（磁盘那份和**内置那份**走的是同一条路，判据只有一份）。
        ///   <paramref name="pathOrMarker"/> 只用来记"这份是从哪来的"（内置时是标记串）。
        /// </summary>
        private static BoardModel TryReadText(string text, string pathOrMarker, int mapId, bool fromDiy)
        {
            try
            {
                JNode root = DiyJson.Parse(text);
                if (root == null || root.Kind != JKind.Object) return null;

                var model = new BoardModel();
                model.Path = pathOrMarker;
                model.Text = text;
                model.FromDiy = fromDiy;
                model.MapField = (int)DiyJson.IntOf(DiyJson.Member(root, "map"), 0);
                JNode scene = DiyJson.Member(root, "scene");
                model.Scene = scene != null && scene.Kind == JKind.String ? scene.Str : "";

                // ★ 认画板的判据：优先"画板号对得上"（最准），没有期望值时按 map 号宽松认。
                if (!model.Matches(mapId, ExpectedSuffix)) return null;

                JNode nodes = DiyJson.Member(root, "nodes");
                if (nodes == null || nodes.Kind != JKind.Array) return null;
                foreach (JNode item in nodes.Items)
                {
                    if (item.Kind != JKind.Object) continue;
                    var n = new BoardNode();
                    n.Id = (int)DiyJson.IntOf(DiyJson.Member(item, "id"), -1);
                    n.LandType = (int)DiyJson.IntOf(DiyJson.Member(item, "landType"), 0);
                    n.RuntimeLandType = (int)DiyJson.IntOf(DiyJson.Member(item, "runtimeLandType"), 0);
                    n.Serial = (int)DiyJson.IntOf(DiyJson.Member(item, "serial"), 0);
                    n.Jump = (int)DiyJson.IntOf(DiyJson.Member(item, "jump"), 0);
                    n.Disable = (int)DiyJson.IntOf(DiyJson.Member(item, "disable"), 0);
                    n.X = DiyJson.DoubleOf(DiyJson.Member(item, "x"), 0);
                    n.Y = DiyJson.DoubleOf(DiyJson.Member(item, "y"), 0);
                    n.Z = DiyJson.DoubleOf(DiyJson.Member(item, "z"), 0);
                    JNode nb = DiyJson.Member(item, "neighbors");
                    if (nb != null && nb.Kind == JKind.Array)
                        foreach (JNode x in nb.Items) n.Neighbors.Add((int)DiyJson.IntOf(x, -1));
                    JNode diy = DiyJson.Member(item, "diy");
                    if (diy != null && diy.Kind == JKind.Object)
                        n.DiyLandType = (int)DiyJson.IntOf(DiyJson.Member(diy, "landType"), 0);
                    if (n.Id >= 0) model.Nodes.Add(n);
                }
                if (model.Nodes.Count == 0) return null;
                model.Note = (fromDiy ? "这份 DIY 自带棋盘：" : "棋盘图：")
                           + BoardCache.Stem(pathOrMarker) + ".json"
                           + "（" + model.Nodes.Count + " 个格子"
                           + (model.Scene.Length > 0 ? "，" + model.Scene : "") + "）";
                return model;
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
