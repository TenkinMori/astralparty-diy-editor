using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace DiyEditor
{
    /// <summary>一张官方地图（= 一个"地图模板"）。</summary>
    public sealed class MapTemplate
    {
        public int Id;
        public string Name = "";
        public int NameId;
        /// <summary>场景后缀，例如 82016 -> "023"（`board.board()` 用它找棋盘）。</summary>
        public string SceneSuffix = "";

        /// <summary>
        /// ★★ `Map.bin` 顶层 field 1 登记的**卡片美术图**（field 8 = `MapImage`），
        ///   例如 82016 -> `UT_MapPreview_016`。
        ///
        /// ★ 2026-10-10 认知修正（很重要）：**美术图号 ≠ 画板号**，两套编号会不一致：
        ///     * 82016 -> 画板 `023`（`Map_023`）／ 美术 `016`（`UT_MapPreview_016`）
        ///     * 81007 -> 画板 `021`（`Map_021`）／ 美术 `007` + `UT_MapScene_021`
        ///   以前 `MapThumb` 拿**画板号**去拼 `"UT_MapPreview_" + suffix`，所以 82016 永远
        ///   找不到图（`UT_MapPreview_023` 不存在），81007 还会错配到别人家的图。
        ///   现在一律用这张表里登记的名字，不再猜。
        /// </summary>
        public string MapImage = "";

        /// <summary>★ 同上，field 9 = `MapSceneImage`（横版大图/载入图），例如 `UT_MapScene_016`。</summary>
        public string MapSceneImage = "";

        public List<int> Difficulties = new List<int>();
        /// <summary>本机有没有这张图的棋盘数据（有才能编辑棋盘 / 标红落点）。</summary>
        public bool HasBoard;
        public string BoardFile = "";
        public bool BoardChecked;

        /// <summary>
        /// ★ 旧的按画板号拼名字 —— **只留给自测/报告说明"当年错在哪"**，取材一律走
        /// `MapImage` / `MapSceneImage`。别在新代码里用它。
        /// </summary>
        public string ThumbName { get { return SceneSuffix.Length > 0 ? "UT_MapPreview_" + SceneSuffix : null; } }

        /// <summary>旧的按画板号拼大图名（同上，仅留作对照）。</summary>
        public string BigName { get { return SceneSuffix.Length > 0 ? "UT_MapScene_" + SceneSuffix : null; } }

        public string Display { get { return Name + "（" + Id + "）"; } }
    }

    /// <summary>怪物的三种分类（★ `Monster.bin` 的 `monster_type`，见 设计稿 v2 §3.3）。</summary>
    public enum MonsterClass { Unknown = 0, Minion = 1, Boss = 2, Elite = 3 }

    /// <summary>一只怪的表数据（只读）。</summary>
    public sealed class MonsterRow
    {
        public int Id;
        public int NameId;
        public int MonsterType;
        public int Gold;
        public int Blood;
        public int Attack;
        public int Defense;
        public int ActiveSkill;
        public List<int> PassiveSkills = new List<int>();

        /// <summary>
        /// ★★ 2026-10-09：**会不会反击** = `Monster.bin` 的**字段 22**（`can_counter`，bool/varint）。
        ///   服务端就是这么读的（`测试服\server\astralnext\gamedata.py:1120-1121`）。
        ///   以前编辑器没解析这一格，`MakePool` 又写死 `Counter = false` ⇒ 界面上处处"不反击"
        ///   （用户实机反馈："是否反击默认全是不反击"）。
        /// </summary>
        public bool CanCounter;

        public string Name = "";

        public MonsterClass Class
        {
            get
            {
                switch (MonsterType)
                {
                    case 1: return MonsterClass.Minion;
                    case 2: return MonsterClass.Boss;
                    case 3: return MonsterClass.Elite;
                    default: return MonsterClass.Unknown;
                }
            }
        }

        public string ClassName
        {
            get
            {
                switch (Class)
                {
                    case MonsterClass.Minion: return "普通小怪";
                    case MonsterClass.Boss: return "Boss 怪";
                    case MonsterClass.Elite: return "精英怪";
                    default: return "未分类";
                }
            }
        }

        /// <summary>
        /// ★ 2026-10-17：`5-DIY关卡\图标缓存\&lt;id&gt;.png` 里的**立绘**（官方半身立绘，
        /// 由 `tools\export_monster_portraits.py` 从游戏自己的包里只读抠出来）；没有回空串。
        /// ★ 与 `AvatarAsset`（图鉴像素小人）是**两套**：小的好认（棋盘 15 px 用它），
        /// 大的好看（怪物页 / 池子大图用它，`编辑器配置.json` 的 `monster_art: "portrait"`）。
        /// </summary>
        public string PortraitPath
        {
            get { return GameLocator.MonsterIconPath(Id) ?? ""; }
        }

        /// <summary>头像文件名：`UT_Monster_Bust_<id>_0`（实测 53 张，配置里用的怪都有）。</summary>
        public string BustName { get { return "UT_Monster_Bust_" + Id + "_0"; } }

        /// <summary>这只怪的**手牌画**（`UT_Monster_Card_<id>`）—— 头像缺图时拿它顶。</summary>
        public string CardArtName { get { return "UT_Monster_Card_" + Id; } }

        /// <summary>
        /// ★★ 2026-10-10：`Monster.bin` **field 19 = 头像资产名**（官方配置表里写死的）。
        ///   例如 `1001 -> UT_AccountHeadShot_Monster_01`、`1003 -> UT_AccountHeadShot_Boss_01`。
        ///
        ///   这一族就是**「图鉴」里那套像素风怪物小人**（200x200，低分辨率像素画）——
        ///   它不是靠名字线索找出来的（`pixel`/`chibi` 这些词在整个游戏资源里命中 0），
        ///   而是**表里登记的**。空串 = 这一行没登记。
        /// </summary>
        public string AvatarAsset = "";

        /// <summary>
        /// ★★ 界面上就画这一张（2026-10-10 用户要求：怪物图换成图鉴那套像素小人）。
        ///   顺序（前一级没有才退后一级）：
        ///     ① **`Monster.bin` f19 登记的像素小人**（`UT_AccountHeadShot_*`，最佳）；
        ///     ② 官方半身像 `UT_Monster_Bust_&lt;id&gt;_0`；
        ///     ③ 本机没有半身像但官方有这只怪的手牌画 ⇒ 用那张顶（同一只怪的官方美术，不是瞎配）；
        ///     ④ 都没有 ⇒ 回半身像的名字，调用方照旧落「色块 + 中文名」。
        ///
        ///   ★ 有 3 只（1068 看守 / 1069 罪恶茶壶 / 1070 大盗）表里登记了像素图但**本地没下到**，
        ///     这里会**干净地**掉到 ②/③，不会破图（判据就是"本地有没有这个文件"）。
        /// </summary>
        public string AvatarName
        {
            get
            {
                if (PixelArtName.Length > 0) return PixelArtName;
                if (Art.ImagePath(BustName) != null) return BustName;
                if (Art.ImagePath(CardArtName) != null) return CardArtName;
                return BustName;
            }
        }

        /// <summary>f19 登记的像素小人资产名，且**本机确实读得到**；读不到回空串。</summary>
        public string PixelArtName
        {
            get
            {
                if (string.IsNullOrEmpty(AvatarAsset)) return "";
                return Art.ImagePath(AvatarAsset) != null ? AvatarAsset : "";
            }
        }

        /// <summary>这只怪表里登记了像素小人，但**本地没有那张图**（界面/报告要如实说）。</summary>
        public bool PixelArtMissing
        {
            get { return AvatarAsset.Length > 0 && Art.ImagePath(AvatarAsset) == null; }
        }

        /// <summary>现在画的这张是"图鉴像素小人"（界面据此决定用 NearestNeighbor 放大）。</summary>
        public bool AvatarIsPixel
        {
            get { return AvatarAsset.Length > 0 && Art.ImagePath(AvatarAsset) != null; }
        }

        /// <summary>现在画的这张是不是"拿手牌画顶的"（界面 / 报告要如实说）。</summary>
        public bool AvatarIsFallback
        {
            get
            {
                if (PixelArtName.Length > 0) return false;
                return Art.ImagePath(BustName) == null && Art.ImagePath(CardArtName) != null;
            }
        }

        /// <summary>
        /// ★ 界面上"这张图是哪来的"一行字 —— 三只缺像素图的怪就靠它把话说清楚
        ///   （不弹窗、不打扰，只挂在悬停 / 详情里）。
        /// </summary>
        public string AvatarSourceNote
        {
            get
            {
                // ★ 2026-10-17：画的是图标缓存里的立绘时，先如实说这一句（配置 monster_art=portrait，
                //   或者像素小人缺图时，都会走到这一支）。
                bool portrait = PortraitPath.Length > 0 && (GameLocator.PreferPortrait || !AvatarIsPixel);
                if (portrait)
                    return (GameLocator.PreferPortrait ? "立绘（图标缓存）" : "立绘（图标缓存，像素小人缺图）")
                        + " 图标缓存\\" + Id + ".png";
                if (AvatarIsPixel) return "图鉴像素小人 " + AvatarAsset;
                if (PixelArtMissing)
                    return "★ 官方登记了像素小人 " + AvatarAsset + "，但这台机器的客户端没下到 ⇒ 回落"
                        + (Art.ImagePath(BustName) != null ? "官方半身像" : "手牌画");
                if (Art.ImagePath(BustName) != null) return "官方半身像 " + BustName;
                if (Art.ImagePath(CardArtName) != null) return "官方没有半身像 ⇒ 用它的手牌画顶的";
                return "官方没有这张图 ⇒ 色块 + 中文名";
            }
        }
    }

    /// <summary>一个"词条"（Mutator）—— ★ 用户说的"条词添加"。</summary>
    public sealed class MutatorRow
    {
        public int Id;
        public int NameId;
        public string Name = "";
        public string Icon = "";

        public string Display { get { return Name.Length > 0 ? Name : ("词条 " + Id); } }
    }

    /// <summary>
    /// ★ 一张卡牌的表数据（只读）—— 第③页的「所有玩家获得某张牌」效果条目要用它。
    ///
    /// 字段（`tools\dump_mutator.py` 实测 + 服务端 `gamedata.card_name` 的读法）：
    ///   `Card.bin` repeated 行：**1 Id / 2 NameID**（名字在 `STRCard`，71 行全都有名字）。
    /// </summary>
    public sealed class CardRow
    {
        public int Id;
        public int NameId;
        public string Name = "";

        public string Display
        {
            get { return Id.ToString(System.Globalization.CultureInfo.InvariantCulture)
                       + " " + (Name.Length > 0 ? Name : "(没名字)"); }
        }
    }

    /// <summary>
    /// 客户端配置表的只读入口（protobuf，见 `Proto`）。
    ///
    /// 字段号一律**照抄**服务端已经核对过的读法（`server\astralnext\gamedata.py`），
    /// 不凭印象改：那边为了"房间能不能建起来"已经把这些字段逐条对过表了。
    ///    Map.bin      field 2 = map&lt;地图id, MapInfoConfigure&gt;，其中 field 2 = NameID
    ///                 field 4 = map&lt;场景id, ...&gt;（用来算"画板后缀"）
    ///                 field 6 = map&lt;难度表id, map&lt;档位, 难度行&gt;&gt;
    ///    STRMap.bin   field 1 = key，field 2 = 文本
    ///    Monster.bin  repeated 行：1 Id / 3 NameID / 10 MonsterType /
    ///                 13 Gold / 14 Blood / 15 Attack / 16 Defense / 17 ActiveSkill / 18 PassiveSkills
    ///    Mutator.bin  repeated 行：1 Id / 2 NameID / 5 图标名；名字在 STRMutator
    /// </summary>
    public static class GameTables
    {
        /// <summary>档位中文名（`gamedata.py:252` 的 DIFFICULTY_NAMES，以客户端真正显示的那套为准）。</summary>
        public static readonly Dictionary<int, string> DifficultyNames = new Dictionary<int, string>
        {
            { 0, "普通" }, { 1, "困难" }, { 2, "噩梦" }, { 3, "疯狂" }, { 4, "极限" },
        };

        public static string DifficultyName(int d)
        {
            string s;
            return DifficultyNames.TryGetValue(d, out s) ? s : ("档 " + d);
        }

        /// <summary>按地图 id 取一张模板（没有回 null）。</summary>
        public static MapTemplate Map(int mapId)
        {
            foreach (MapTemplate t in Maps()) if (t.Id == mapId) return t;
            return null;
        }

        private static Dictionary<int, List<int>> _diffRows;

        /// <summary>
        /// 难度表 id -> 这张表里有哪几档（GameDifficultyType）。
        ///
        /// ★ 照抄 `gamedata.py:266-327`：
        ///   Map.bin 顶层 **field 6** = repeated Map&lt;难度表id, 难度配置&gt;；
        ///   每个配置体里 **field 2** 是一行行难度行，行里的 **field 1（wire 5，sfixed32）**
        ///   就是 GameDifficultyType（0 普通 / 1 困难 / 2 噩梦 / 3 疯狂 / 4 极限）。
        ///   ★ 值为 0 时 protobuf 不写，所以"普通"那一档是**默认值**、不能当没有。
        /// </summary>
        public static Dictionary<int, List<int>> DifficultyRows()
        {
            if (_diffRows != null) return _diffRows;
            _diffRows = new Dictionary<int, List<int>>();
            byte[] data = Read("Map.bin");
            if (data == null) return _diffRows;
            foreach (Proto.Entry e in Proto.Entries(data, 6))
            {
                var kinds = new List<int>();
                foreach (Proto.Field f in Proto.Parse(e.Body))
                {
                    if (f.Number != 2 || f.Wire != 2 || f.Bytes == null) continue;
                    int index = 0;
                    foreach (Proto.Field a in Proto.Parse(f.Bytes))
                        if (a.Number == 1 && a.Wire == 5) { index = (int)a.Int; break; }
                    if (!kinds.Contains(index)) kinds.Add(index);
                }
                kinds.Sort();
                _diffRows[e.Key] = kinds;
            }
            return _diffRows;
        }

        /// <summary>这张图在难度面板上会出现哪几档（中文，例如 "普通/困难"）。没有难度表就回空串。</summary>
        public static string DifficultyTextOf(MapTemplate t)
        {
            if (t == null || t.Difficulties.Count == 0) return "";
            var rows = DifficultyRows();
            List<int> kinds;
            if (!rows.TryGetValue(t.Difficulties[0], out kinds) || kinds.Count == 0) return "";
            var parts = new List<string>();
            foreach (int k in kinds) parts.Add(DifficultyName(k));
            return string.Join("/", parts.ToArray());
        }

        /// <summary>PVE 相关的模式（`gamedata.py:364-365`：4 PVE / 9 练习PVE / 12 词条PVE）。</summary>
        public static readonly int[] PveModeIds = new int[] { 4, 9, 12 };
        public static readonly string[] PveModeLabels = new string[] { "PVE", "练习PVE", "词条PVE" };

        /// <summary>
        /// ★★ PvP 那一档有哪些模式 —— **按 `GameMode.bin` 的实测地图号段定的**（2026-10-03 只读 dump）：
        ///
        ///   | 模式 | STRGameMode 名字 | 地图 |
        ///   |---|---|---|
        ///   | 4  | 合作挑战（PVE）      | 82007/82008/82010/82012/82013/82014/82015/82016/82017 |
        ///   | 9  | 合作挑战练习          | 同 4（9 张） |
        ///   | 12 | 畸变组件（词条PVE）   | 83001/83002/83003 |
        ///   | 1  | 标准模式             | 81003/81004/81005/81006/81011 |
        ///   | 2  | 新手                 | 81099 |
        ///   | 3  | 激战模式             | 同 1（5 张） |
        ///   | 5  | 单人模式             | 103 |
        ///   | 6  | 单人模式             | 101/102 |
        ///   | 7  | 礼物大作战            | 82009 |
        ///   | 8  | 玩家对战练习          | 同 1（5 张） |
        ///   | 10 | 限时玩法             | 1001/1002 |
        ///   | 11 | 幸运星争夺战          | 81007 |
        ///
        ///   ⇒ **PvE 系 = 4/9/12（图号 82xxx、83xxx）**，其余 9 个模式（含 81xxx 那一族、
        ///     1001/1002、101/102/103、82009）都是**对抗/限时**那一类 = PvP 系。
        ///   ★ 用户这一批明确"先不用 pvp 的地块" ⇒ 编辑器**默认不列**，勾了才只读展示。
        /// </summary>
        public static readonly int[] PvpModeIds = new int[] { 1, 2, 3, 5, 6, 7, 8, 10, 11 };
        public static readonly string[] PvpModeLabels = new string[]
        {
            "标准模式", "新手", "激战模式", "单人模式", "单人模式",
            "礼物大作战", "玩家对战练习", "限时玩法", "幸运星争夺战",
        };

        /// <summary>某个模式是不是 PvE 系（4/9/12）。</summary>
        public static bool IsPveMode(int mode)
        {
            foreach (int m in PveModeIds) if (m == mode) return true;
            return false;
        }

        /// <summary>某个模式是不是 PvP 系（对抗/限时那一族）。</summary>
        public static bool IsPvpMode(int mode)
        {
            foreach (int m in PvpModeIds) if (m == mode) return true;
            return false;
        }

        /// <summary>模式的官方名字（`STRGameMode.bin`，key = 模式 id）。读不到回 "模式 N"。</summary>
        public static string ModeName(int mode)
        {
            string s;
            if (StrTable("STRGameMode").TryGetValue(mode, out s) && !string.IsNullOrEmpty(s)) return s;
            return "模式 " + mode;
        }

        /// <summary>把一串模式的地图**并起来**（去重，按传入顺序）。PvE 系"全部"那一档用它。</summary>
        public static List<MapTemplate> MapsForAnyMode(int[] modes)
        {
            var outList = new List<MapTemplate>();
            if (modes == null) return outList;
            foreach (int m in modes)
                foreach (MapTemplate t in MapsForMode(m))
                    if (!outList.Contains(t)) outList.Add(t);
            return outList;
        }

        /// <summary>PvE 系（4+9+12）的全部图 —— 模板页**默认**就显示这一档。</summary>
        public static List<MapTemplate> MapsForPveFamily() { return MapsForAnyMode(PveModeIds); }

        /// <summary>
        /// ★★ 「新建关卡」那个二级窗口里能选的底版 —— 用户 2026-10-03 的规格：
        ///   **列出所有的 PVE 图，不要 PVP 和练习图，把词条 PVE 也加里面**。
        ///   ⇒ 模式 **4（PVE）+ 12（词条PVE）**，**排除 9（练习PVE）**、也排除 81xxx 那族 PvP。
        /// </summary>
        public static List<MapTemplate> MapsForDiyPicker()
        {
            return MapsForAnyMode(new int[] { 4, 12 });
        }

        /// <summary>这张图在不在"能拿来新建"的那一档里（模式 4 / 12，且认得出画板号）。</summary>
        public static bool CanBuildDiyFrom(MapTemplate t)
        {
            if (t == null) return false;
            foreach (int m in ModesOfMap(t.Id)) if (m == 4 || m == 12) return true;
            return false;
        }

        /// <summary>PvP 系的全部图（只读展示用，编辑器不支持拿它新建）。</summary>
        public static List<MapTemplate> MapsForPvpFamily() { return MapsForAnyMode(PvpModeIds); }

        /// <summary>这张图属于哪几档模式（PvP 的卡上要标"暂不支持"）。</summary>
        public static List<int> ModesOfMap(int mapId)
        {
            var list = new List<int>();
            foreach (var kv in ModeMaps())
                if (kv.Value.Contains(mapId) && !list.Contains(kv.Key)) list.Add(kv.Key);
            return list;
        }

        private static Dictionary<int, List<int>> _modeMaps;

        /// <summary>
        /// 模式 → 地图列表。★ 以 `GameMode.bin` 为准（`gamedata.py:370-402`）：
        /// 客户端的地图列表就是从这里取的，**别自己编"哪些图属于 PVE"**。
        ///
        /// ★ 实测结构（`tools\dump_gamemode.py` 逐层 dump 出来的，别凭印象改）：
        ///     GameMode.bin
        ///       field 2 (wire 2)          ← repeated map entry
        ///         field 1 (wire 5) = 模式 id        ← 这一层是"包装"
        ///         field 2 (wire 2) = GameModeInfoConfigure
        ///             field 7 (wire 2) = MapID 列表
        ///                 ← 里面还是 map：field 1 (wire 5) = 地图 id，field 2 = 该图的配置
        ///   所以地图 id 要从 **field 7 里那层 map 的 key** 取（`Proto.Entries` 恰好干这个）。
        ///   ★ 不能拿 `Proto.Fixed32List` 直接把 field 7 当 packed 数组 —— 它不是 packed，
        ///     是"带 key 的 map"，那样解出来会是一堆长度前缀，不是地图 id。
        /// </summary>
        public static Dictionary<int, List<int>> ModeMaps()
        {
            if (_modeMaps != null) return _modeMaps;
            _modeMaps = new Dictionary<int, List<int>>();
            byte[] data = Read("GameMode.bin");
            if (data == null) return _modeMaps;

            foreach (Proto.Entry e in Proto.Entries(data, 2))
            {
                // ★ 实测（`tools\dump_gamemode.py`）：`Proto.Entries` 已经把外层的
                //   MapEntry 剥掉一层，这里拿到的 `e.Body` 是同级的 GameModeInfoConfigure，
                //   **MapID 列表就是它的 field 7**（里面还是 map：key = 地图 id）。
                var ids = new List<int>();
                foreach (Proto.Entry m in Proto.Entries(e.Body, 7))
                    if (m.Key > 0) ids.Add(m.Key);
                // 兜底：万一这一版表把 field 7 写成了 packed 数组
                if (ids.Count == 0)
                    foreach (Proto.Field f in Proto.Parse(e.Body))
                        if (f.Number == 7 && f.Wire == 2 && f.Bytes != null)
                        {
                            List<int> packed = Proto.Fixed32List(f.Bytes);
                            if (packed.Count > 0) ids.AddRange(packed);
                            break;
                        }

                if (ids.Count > 0) _modeMaps[e.Key] = ids;
            }
            return _modeMaps;
        }

        /// <summary>某个模式下的地图模板（按 GameMode.bin 的顺序）。</summary>
        public static List<MapTemplate> MapsForMode(int mode)
        {
            var all = new Dictionary<int, MapTemplate>();
            foreach (MapTemplate t in Maps()) all[t.Id] = t;
            var outList = new List<MapTemplate>();
            List<int> ids;
            if (ModeMaps().TryGetValue(mode, out ids))
            {
                foreach (int id in ids)
                {
                    MapTemplate t;
                    if (id > 0 && all.TryGetValue(id, out t) && !outList.Contains(t)) outList.Add(t);
                }
            }
            return outList;
        }

        private static string Path_(string name)
        {
            string dir = GameLocator.TableDir;
            if (string.IsNullOrEmpty(dir)) return null;
            string p = System.IO.Path.Combine(dir, name);
            return File.Exists(p) ? p : null;
        }

        private static byte[] Read(string name)
        {
            string p = Path_(name);
            if (p == null) return null;
            try { return File.ReadAllBytes(p); }
            catch (Exception) { return null; }
        }

        /// <summary>
        /// ★ 给 `LandTable` 用的只读入口：把 `gamedata\&lt;名字&gt;.bin` 原样读出来。
        ///   **只读，不写**（找不到就回 null，调用方自己回落）。
        /// </summary>
        public static byte[] TableBytes(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            return Read(name.EndsWith(".bin", StringComparison.OrdinalIgnoreCase) ? name : name + ".bin");
        }

        // ==================================================== 字符串表
        private static readonly Dictionary<string, Dictionary<int, string>> _strCache =
            new Dictionary<string, Dictionary<int, string>>(StringComparer.OrdinalIgnoreCase);

        /// <summary>STR*.bin：field 1 = key，field 2 = 文本（`gamedata.py:202-219`）。</summary>
        public static Dictionary<int, string> StrTable(string name)
        {
            Dictionary<int, string> hit;
            if (_strCache.TryGetValue(name, out hit)) return hit;
            var table = new Dictionary<int, string>();
            byte[] data = Read(name.EndsWith(".bin", StringComparison.OrdinalIgnoreCase) ? name : name + ".bin");
            if (data != null)
            {
                foreach (Proto.Field top in Proto.Parse(data))
                {
                    if (top.Number != 1 || top.Wire != 2 || top.Bytes == null) continue;
                    List<Proto.Field> entry = Proto.Parse(top.Bytes);
                    int? key = null;
                    string text = null;
                    foreach (Proto.Field f in entry)
                    {
                        if (f.Number == 1 && (f.Wire == 0 || f.Wire == 5)) key = (int)f.Int;
                        else if (f.Number == 2 && f.Wire == 2) text = Proto.Utf8(f.Bytes);
                    }
                    if (!key.HasValue || string.IsNullOrEmpty(text)) continue;
                    // 控字符的条目是二进制垃圾，跳过（照抄 Python 那边的过滤）
                    bool bad = false;
                    foreach (char c in text) if (c < 32) { bad = true; break; }
                    if (bad) continue;
                    if (!table.ContainsKey(key.Value)) table[key.Value] = text;
                }
            }
            _strCache[name] = table;
            return table;
        }

        // ==================================================== 地图模板
        private static List<MapTemplate> _maps;

        public static List<MapTemplate> Maps()
        {
            if (_maps != null) return _maps;
            var list = new List<MapTemplate>();
            byte[] data = Read("Map.bin");
            if (data == null) { _maps = list; return _maps; }

            Dictionary<int, string> strMap = StrTable("STRMap");

            // ★ 字段号是**实测**出来的（`tools\map_diag.py` 把 Map.bin 每层都打出来了），
            //   跟服务端 `gamedata.py:94-106` 的读法一致：
            //     field 2 = repeated Map<int32, MapInfoConfigure>
            //                 entry.field 1 (wire 5) = 地图 id
            //                 entry.field 2 (wire 2) = 配置体
            //                     体.field 2  (wire 5) = MapName（STRMap 的 key）
            //                     体.field 5  (wire 2) = Mids（packed **sint32**，第一个是模型 id）
            //                     体.field 6  (wire 2) = DifficultyIds（packed **fixed32**）
            //   ★ 原来这里把 NameID 当成 field 2 的 wire 5 是对的，但难度表错写成 field 3，
            //     而且**没有**走 Mids -> 场景名 -> 后缀那条链，所以画板号一直是空的
            //     （82016 会被算成 "016"，实际是 "023"）。
            var byId = new Dictionary<int, MapTemplate>();
            foreach (Proto.Entry e in Proto.Entries(data, 2))
            {
                int nameId = 0;
                var dlist = new List<int>();
                foreach (Proto.Field f in Proto.Parse(e.Body))
                {
                    if (f.Number == 2 && f.Wire == 5) nameId = (int)f.Int;
                    else if (f.Number == 6 && f.Wire == 2) dlist.AddRange(Proto.Fixed32List(f.Bytes));
                }
                var t = new MapTemplate { Id = e.Key, NameId = nameId, Difficulties = dlist };
                byId[t.Id] = t;
            }

            // 地图id -> 画板后缀（走模型 id -> 场景名 -> 取 "_" 后面那截）
            var mapScene = SceneSuffixByMap(data);
            foreach (MapTemplate t in byId.Values)
            {
                string suffix;
                if (mapScene.TryGetValue(t.Id, out suffix)) t.SceneSuffix = suffix;
            }

            // ★★ 地图id -> 官方登记的**美术图名**（`MapImage` / `MapSceneImage`）
            var mapArt = MapArtByMap(data);
            foreach (MapTemplate t in byId.Values)
            {
                string[] art;
                if (mapArt.TryGetValue(t.Id, out art) && art != null)
                {
                    t.MapImage = art[0] ?? "";
                    t.MapSceneImage = art[1] ?? "";
                }
            }

            foreach (MapTemplate t in byId.Values)
            {
                string name;
                t.Name = strMap.TryGetValue(t.NameId, out name) && !string.IsNullOrEmpty(name)
                    ? name : ("地图 " + t.Id);
                list.Add(t);
            }
            list.Sort(delegate (MapTemplate a, MapTemplate b) { return a.Id.CompareTo(b.Id); });
            _maps = list;
            return _maps;
        }

        /// <summary>
        /// 地图id -> 画板后缀（"023" 这种）。★ 照抄 `gamedata.py:85-106`：
        ///   Map.bin field 4 = map&lt;场景id, 场景信息&gt;，其中 field 2 = 场景名（ASCII，"Map_023"）；
        ///   Map.bin field 2 每行的 **field 5 = Mids（packed sint32）**，Mids[0] 就是场景id；
        ///   最后把场景名按 `_` 切一刀取尾巴。
        ///
        /// ★ 注意：**不能**拿"场景id 本身"后缀当画板号 —— 实测 82016 的
        ///   MapSceneImage 认的是 023 那张画板，场景id 却是别的数。这条链路
        ///   服务端在 `_load_maps` 里已经逐条对过，这里照抄它。
        /// </summary>
        private static Dictionary<int, string> SceneSuffixByMap(byte[] mapData)
        {
            var result = new Dictionary<int, string>();

            // ① field 4 = repeated Map<int32, MapSceneInfoConfigure>：模型 id -> 场景信息（field 2 = 场景名）
            var sceneName = new Dictionary<int, string>();
            foreach (Proto.Entry e in Proto.Entries(mapData, 4))
            {
                string name = null;
                foreach (Proto.Field f in Proto.Parse(e.Body))
                    if (f.Number == 2 && f.Wire == 2) name = Proto.Utf8(f.Bytes);
                if (!string.IsNullOrEmpty(name)) sceneName[e.Key] = name;
            }

            // ② field 2 = repeated Map<int32, MapInfoConfigure>：地图id -> 配置
            //    配置里 **field 5 = Mids（packed sint32）**，Mids[0] 就是模型 id
            //    ★ 实测（`tools\map_diag.py`）：不能拿"场景id 本身"当后缀 ——
            //      82016 的 Mids[0] 指向的才是 `Map_023`。
            foreach (Proto.Entry e in Proto.Entries(mapData, 2))
            {
                int? modelId = null;
                foreach (Proto.Field f in Proto.Parse(e.Body))
                {
                    if (f.Number != 5 || f.Wire != 2) continue;
                    List<int> mids = Proto.Zigzag32List(f.Bytes);
                    if (mids.Count > 0) modelId = mids[0];
                    break;
                }
                if (!modelId.HasValue) continue;
                string name;
                if (sceneName.TryGetValue(modelId.Value, out name))
                {
                    // "Map_023" -> "023"（照 gamedata.py:106 的 rsplit("_", 1)[-1]）
                    int at = name.LastIndexOf('_');
                    if (at >= 0 && at + 1 < name.Length) name = name.Substring(at + 1);
                    result[e.Key] = name;
                }
            }
            return result;
        }

        /// <summary>
        /// ★★ 地图id -> 官方登记的**美术图名**（`Map.bin` 顶层 **field 1** 那张「地图 -> 图片名」登记表）。
        ///
        /// 每条登记行的字段布局（`tools\_map_field1_dump.py` 实测 dump 出来的）：
        ///   * field  1 (wire 5) = **地图 id**（81003 / 82016 …）
        ///   * field  2 (wire 5) = 同上（地图 id 的另一个副本）
        ///   * field  3 (wire 5) = 名字 id（翻 `STRMap`）
        ///   * **field  8 (wire 2) = `MapImage`** —— 选图卡片那张美术预览
        ///   * **field  9 (wire 2) = `MapSceneImage`** —— 横版大图 / 载入图
        ///   * field 10 (wire 5) = 地图 id（第三个副本）
        ///
        /// ★ **这是唯一的权威来源**：美术图号与「画板号」是两套编号，会不一致
        ///   （82016 画板 023、美术 016；81007 画板 021、美术 007 + 大图 021）。
        ///   以前按画板号拼名字，所以 82016 永远找不到、81007 会错配。
        ///
        /// 返回 `{ id -> [MapImage, MapSceneImage] }`（没登记的就是空串）。
        /// </summary>
        private static Dictionary<int, string[]> MapArtByMap(byte[] mapData)
        {
            var result = new Dictionary<int, string[]>();
            foreach (Proto.Field row in Proto.Parse(mapData))
            {
                if (row.Number != 1 || row.Wire != 2 || row.Bytes == null) continue;
                int id = 0;
                string mapImage = "", sceneImage = "";
                foreach (Proto.Field f in Proto.Parse(row.Bytes))
                {
                    if (f.Number == 1 && f.Wire == 5) id = (int)f.Int;
                    else if (f.Number == 8 && f.Wire == 2 && f.Bytes != null) mapImage = Proto.Utf8(f.Bytes);
                    else if (f.Number == 9 && f.Wire == 2 && f.Bytes != null) sceneImage = Proto.Utf8(f.Bytes);
                }
                if (id <= 0) continue;
                result[id] = new string[] { mapImage ?? "", sceneImage ?? "" };
            }
            return result;
        }

        /// <summary>
        /// ★★ 少数地图（例如 **82017「锥尖的炽热挑战」**）在 `Map.bin` 的登记表里**根本没有行**，
        ///   所以 `MapImage` / `MapSceneImage` 都是空串。
        ///
        ///   但它的**画板号和 82007 是同一个（007）** —— 也就是"同一张棋盘换了个关卡皮"
        ///   （`DIY关卡\_模板\82017_5.json` 与 `82007_5.json` 指着同一个 `007.json` 棋盘）。
        ///   这种时候卡片上画"它自己那张棋盘所属的美术图"是**有依据的**，比空白强。
        ///
        ///   这里就去找**同一画板号、且官方登记了美术图**的那张地图，把它的 `MapImage` 借过来。
        ///   找不到就回空串（界面照旧落「（没有缩略图）」）。
        /// </summary>
        public static string SiblingArtName(MapTemplate t)
        {
            if (t == null || string.IsNullOrEmpty(t.SceneSuffix)) return "";
            if (t.MapImage.Length > 0) return t.MapImage;      // 自己有，不用借
            foreach (MapTemplate other in Maps())
            {
                if (other.Id == t.Id) continue;
                if (!string.Equals(other.SceneSuffix, t.SceneSuffix, StringComparison.Ordinal)) continue;
                if (other.MapImage.Length > 0) return other.MapImage;
            }
            return "";
        }

        /// <summary>
        /// 这张图有没有棋盘数据（只读看缓存 / 工作副本 / **exe 内置那份**，不碰服务端）。
        ///   ★ 读取顺序 = **磁盘优先、内置兜底**（用户 2026-10-12）：磁盘缓存 / 工作副本里
        ///     有就用那份；都没有才认 exe 内置的官方画板 —— 于是"只有 exe"也能新建关卡。
        ///   ★ <paramref name="t"/>.BoardFile 在内置那一路会拿到一个**内置标记**（磁盘上没有这个文件），
        ///     只该拿去显示 / 当身份，不该当磁盘路径用（判据见 `BoardCache.IsBuiltIn`）。
        /// </summary>
        public static bool EnsureBoardInfo(MapTemplate t)
        {
            if (t == null) return false;
            if (t.BoardChecked) return t.HasBoard;
            t.BoardChecked = true;
            if (t.SceneSuffix.Length == 0) return false;

            // ① 磁盘：本工具自己的「棋盘缓存」→ ② 磁盘：官方工作副本的 boards
            var dirs = new List<string>();
            string cache = BoardStore.CacheDir;
            if (Directory.Exists(cache)) dirs.Add(cache);
            string src = GameLocator.BoardSourceDir;
            if (!string.IsNullOrEmpty(src)) dirs.Add(src);
            foreach (string dir in dirs)
            {
                string path = System.IO.Path.Combine(dir, t.SceneSuffix + ".json");
                if (File.Exists(path))
                {
                    t.HasBoard = true;
                    t.BoardFile = path;
                    return true;
                }
            }

            // ③ ★ exe 内置的那一份（磁盘上一个都没有时）
            if (EmbeddedCache.HasLogical(EmbeddedCache.BoardLogicalName(t.SceneSuffix)))
            {
                t.HasBoard = true;
                t.BoardFile = BoardCache.Marker(t.SceneSuffix);
                return true;
            }
            return false;
        }

        // ==================================================== 怪物
        private static List<MonsterRow> _monsters;

        public static List<MonsterRow> Monsters()
        {            if (_monsters != null) return _monsters;
            var list = new List<MonsterRow>();
            byte[] data = Read("Monster.bin");
            if (data == null) { _monsters = list; return _monsters; }
            Dictionary<int, string> strs = StrTable("STRMonster");

            foreach (Proto.Field top in Proto.Parse(data))
            {
                if (top.Number != 1 || top.Wire != 2 || top.Bytes == null) continue;
                var m = new MonsterRow();
                foreach (Proto.Field f in Proto.Parse(top.Bytes))
                {
                    if (f.Wire == 2)
                    {
                        if (f.Number == 18) m.PassiveSkills.AddRange(Proto.Fixed32List(f.Bytes));
                        continue;
                    }
                    switch (f.Number)
                    {
                        case 1: m.Id = (int)f.Int; break;
                        case 3: m.NameId = (int)f.Int; break;
                        case 10: m.MonsterType = (int)f.Int; break;
                        case 13: m.Gold = (int)f.Int; break;
                        case 14: m.Blood = (int)f.Int; break;
                        case 15: m.Attack = (int)f.Int; break;
                        case 16: m.Defense = (int)f.Int; break;
                        case 17: m.ActiveSkill = (int)f.Int; break;
                        // ★ 22 = can_counter（会不会反击）：服务端读的就是这一格，见 gamedata.py
                        case 22: m.CanCounter = f.Int != 0; break;
                    }
                }
                // ★★ 2026-10-10：field 19 = 头像资产名（**wire 2 字符串**，不是数字）——
                //   就是「图鉴」那套像素怪小人 `UT_AccountHeadShot_Monster_<NN>` / `_Boss_<NN>`。
                //   单独在这里取，因为上面的 switch 只处理非字符串字段（数字）。
                foreach (Proto.Field f in Proto.Parse(top.Bytes))
                    if (f.Number == 19 && f.Wire == 2 && f.Bytes != null && f.Bytes.Length > 0)
                    {
                        m.AvatarAsset = Proto.Utf8(f.Bytes);
                        break;
                    }
                if (m.Id <= 0) continue;
                string name;
                m.Name = strs.TryGetValue(m.NameId, out name) && !string.IsNullOrEmpty(name)
                    ? name : ("怪 " + m.Id);
                list.Add(m);
            }
            list.Sort(delegate (MonsterRow a, MonsterRow b) { return a.Id.CompareTo(b.Id); });
            _monsters = list;
            return _monsters;
        }

        public static MonsterRow Monster(int id)
        {
            foreach (MonsterRow m in Monsters())
                if (m.Id == id) return m;
            return null;
        }

        private static List<MonsterRow> _monstersShown;

        /// <summary>
        /// ★ 2026-10-08（用户原话："怪物列表对不上图标的怪物都删掉，那个是隐藏起来的，不应该出来"）：
        ///   只列**有半身像**的怪 —— `Monster.bin` 里 80 只，但客户端 `GameRes\Img` 里只有
        ///   一部分有 `UT_Monster_Bust_&lt;id&gt;_0`。没有半身像的那些是官方藏着/没启用的条目，
        ///   摆进列表里只会让人挑到"没图/图对不上"的怪。
        ///   ★ 已经放进池子里的怪**不**受这条影响（`MonsterPickDialog.ForPool` 走的是池子）。
        /// </summary>
        public static List<MonsterRow> MonstersWithAvatar()
        {
            if (_monstersShown != null) return _monstersShown;
            var list = new List<MonsterRow>();
            foreach (MonsterRow m in Monsters())
                if (Art.ImagePath(m.BustName) != null) list.Add(m);
            _monstersShown = list;
            return _monstersShown;
        }

        /// <summary>被"只列有半身像的怪"那条规则挡掉的怪（给提示语用）。</summary>
        public static int MonstersHiddenCount()
        {
            return Monsters().Count - MonstersWithAvatar().Count;
        }

        // ==================================================== 词条（Mutator）
        private static List<MutatorRow> _mutators;

        /// <summary>
        /// ★ 用户说的"条词"—— 按 `Mutator.bin` 读（"词条PVE" 那套整局规则）。
        /// 行字段：1 Id / 2 NameID（名字在 STRMutator）/ 5 图标名（UT_Mutator_&lt;id&gt;）。
        /// </summary>
        public static List<MutatorRow> Mutators()
        {
            if (_mutators != null) return _mutators;
            var list = new List<MutatorRow>();
            byte[] data = Read("Mutator.bin");
            if (data == null) { _mutators = list; return _mutators; }
            Dictionary<int, string> strs = StrTable("STRMutator");

            foreach (Proto.Field top in Proto.Parse(data))
            {
                if (top.Number != 1 || top.Wire != 2 || top.Bytes == null) continue;
                var r = new MutatorRow();
                foreach (Proto.Field f in Proto.Parse(top.Bytes))
                {
                    if (f.Number == 1 && f.Wire != 2) r.Id = (int)f.Int;
                    else if (f.Number == 2 && f.Wire != 2) r.NameId = (int)f.Int;
                    else if (f.Number == 5 && f.Wire == 2) r.Icon = Proto.Utf8(f.Bytes);
                }
                if (r.Id <= 0) continue;
                string name;
                r.Name = strs.TryGetValue(r.NameId, out name) ? name : "";
                if (string.IsNullOrEmpty(r.Icon)) r.Icon = "UT_Mutator_" + r.Id;
                list.Add(r);
            }
            list.Sort(delegate (MutatorRow a, MutatorRow b) { return a.Id.CompareTo(b.Id); });
            _mutators = list;
            return _mutators;
        }

        /// <summary>表能不能用（页面上要如实显示"读不到表"）。</summary>
        public static string Status()
        {
            string dir = GameLocator.TableDir;
            if (string.IsNullOrEmpty(dir)) return "读不到配置表目录";
            var sb = new StringBuilder();
            sb.Append("表目录 ").Append(dir);
            sb.Append(" · 地图 ").Append(Maps().Count);
            sb.Append(" · 怪物 ").Append(Monsters().Count);
            sb.Append(" · 词条 ").Append(Mutators().Count);
            sb.Append(" · 卡牌 ").Append(Cards().Count);
            return sb.ToString();
        }

        // ==================================================== 卡牌（第③页「获得某张牌」用）
        private static List<CardRow> _cards;

        /// <summary>
        /// ★ `Card.bin` 的卡牌清单（只读）。行字段：**1 Id / 2 NameID**，名字在 `STRCard`。
        ///   ★ 实测 71 行、71 行全都在 `STRCard` 里查得到名字
        ///     （`tools\dump_mutator.py` 打的；`STRCard` 里别的高号是效果文案，不用）。
        /// </summary>
        public static List<CardRow> Cards()
        {
            if (_cards != null) return _cards;
            var list = new List<CardRow>();
            byte[] data = Read("Card.bin");
            if (data == null) { _cards = list; return _cards; }
            Dictionary<int, string> strs = StrTable("STRCard");

            foreach (Proto.Field top in Proto.Parse(data))
            {
                if (top.Number != 1 || top.Wire != 2 || top.Bytes == null) continue;
                var c = new CardRow();
                foreach (Proto.Field f in Proto.Parse(top.Bytes))
                {
                    if (f.Wire == 2) continue;
                    if (f.Number == 1) c.Id = (int)f.Int;
                    else if (f.Number == 2) c.NameId = (int)f.Int;
                }
                if (c.Id <= 0) continue;
                string name;
                c.Name = strs.TryGetValue(c.NameId, out name) && !string.IsNullOrEmpty(name) ? name : "";
                list.Add(c);
            }
            list.Sort(delegate (CardRow a, CardRow b) { return a.Id.CompareTo(b.Id); });
            _cards = list;
            return _cards;
        }

        public static CardRow Card(int id)
        {
            foreach (CardRow c in Cards()) if (c.Id == id) return c;
            return null;
        }

        public static string CardName(int id)
        {
            CardRow c = Card(id);
            return c == null ? "" : c.Name;
        }

        // ==================================================== ★ 难度行里的进度事件 id
        private static Dictionary<int, Dictionary<int, List<int>>> _progressEventsByDiff;
        private static readonly Dictionary<int, List<int>> _progressEventsEmpty =
            new Dictionary<int, List<int>>();

        /// <summary>
        /// ★★ 这张图**每一档难度行**登记过的进度事件 id —— `Map.bin` 的
        ///   难度表 → 难度行 **field 3**（packed fixed32）。
        ///
        /// 照抄服务端 `gamedata._load_difficulty_rows`：
        ///   `Map.bin` 顶层 field 6 = 难度表 id → 配置；配置里 field 2 = 一行难度行；
        ///   行里 field 1(sfixed32) = Index(= GameDifficultyType)、**field 3 = progress_events**。
        ///
        /// 返回：`难度档 → 事件 id 列表`。读不到就空表（页面上如实说）。
        ///
        /// ★★ 2026-10-10 修缓存口径（**原来是一张图盖住全表**）：
        ///   老写法是 `if (_progressEventsByDiff == null) { 建**当前这一张图**的 rows }`
        ///   —— 于是**一个进程里只有第一次问的那张图查得到，其余每张图都回空表**。
        ///   实测：`--map-events` 里 82007 有 4~5 条、紧跟着的 82016 / 82008 全是 0 条，
        ///   而 82016 难度 1 官方**确实**登记了 32121~32124（自测 ⑫e 单独先问 82016 所以一直是对的）。
        ///   现在改成**按难度表 id 逐个惰性建、逐个缓存**。
        /// </summary>
        public static Dictionary<int, List<int>> ProgressEventsByDifficulty(int mapId)
        {
            MapTemplate tm = Map(mapId);
            if (tm == null || tm.Difficulties.Count == 0) return _progressEventsEmpty;
            int diffId = tm.Difficulties[0];

            if (_progressEventsByDiff == null)
                _progressEventsByDiff = new Dictionary<int, Dictionary<int, List<int>>>();
            Dictionary<int, List<int>> cached;
            if (_progressEventsByDiff.TryGetValue(diffId, out cached))
                return cached == null || cached.Count == 0 ? _progressEventsEmpty : cached;

            var rows = new Dictionary<int, List<int>>();
            byte[] data = Read("Map.bin");
            if (data != null)
            {
                foreach (Proto.Entry e in Proto.Entries(data, 6))
                {
                    if (e.Key != diffId) continue;
                    foreach (Proto.Field cfg in Proto.Parse(e.Body))
                    {
                        if (cfg.Number != 2 || cfg.Wire != 2 || cfg.Bytes == null) continue;
                        int index = 0;
                        var ids = new List<int>();
                        foreach (Proto.Field a in Proto.Parse(cfg.Bytes))
                        {
                            if (a.Number == 1 && a.Wire == 5) index = (int)a.Int;
                            else if (a.Number == 3 && a.Wire == 2 && a.Bytes != null)
                                ids.AddRange(Proto.Fixed32List(a.Bytes));
                        }
                        rows[index] = ids;
                    }
                }
            }
            _progressEventsByDiff[diffId] = rows;      // ★ 空表也缓存（表在运行期不会变）
            return rows.Count == 0 ? _progressEventsEmpty : rows;
        }

        /// <summary>
        /// ★★ 这张图难度表里**登记过进度事件的那几档**（`GameDifficultyType` 序号，升序）。
        ///
        /// ★ 别拿 `MapTemplate.Difficulties` 当档次用 —— 那是 **`Map.bin` 的难度表 id**
        ///   （字典键，例如 82016 / 8201201），跟 `EventsForMapDifficulty` 要的
        ///   **档次序号**（0 普通 / 1 困难 / …）是两套号。2026-10-10 实测：拿表 id 去查
        ///   `ProgressEventsByDifficulty` 必然一条都查不到（`--map-events` 那一节当年就是这么错的）。
        /// </summary>
        public static List<int> DifficultyIndexesWithEvents(int mapId)
        {
            var outList = new List<int>();
            Dictionary<int, List<int>> rows = ProgressEventsByDifficulty(mapId);
            if (rows == null) return outList;
            foreach (var kv in rows)
                if (kv.Value != null && kv.Value.Count > 0 && !outList.Contains(kv.Key)) outList.Add(kv.Key);
            outList.Sort();
            return outList;
        }


        // ================================================================ ★ 难度行（只读）
        /// <summary>
        /// ★ 进度上限 ProgressLimit —— **读出来给用户看**，绝不改表。
        ///
        /// 来源（逐字段照抄 `server\astralnext\gamedata.py:_load_difficulty_rows`）：
        ///   `Map.bin` **顶层 field 6** = `GameDifficultyDict`：难度表 id → 一份配置；
        ///   配置体里 **field 2（wire 2）** = 一行难度行（`MapGameDifficultyConfigureItem`）；
        ///   行里 **field 1（wire 5, sfixed32）** = Index（= GameDifficultyType，0 普通…4 极限）；
        ///   行里 **field 2（wire 5, sfixed32）** = **ProgressLimit**（进度上限，客户端画格数）。
        ///
        /// ★ 值为 0 时 protobuf 不写 ⇒ 读不到就是"表里没写"，**不能当成 0**（要如实说"未知"）。
        /// </summary>
        /// <param name="mapId">地图 id（`map` 字段，例如 82016）。</param>
        /// <param name="index">难度档 = GameDifficultyType（0 普通 / 1 困难 / …）。</param>
        /// <param name="note">读不到时的原因（给人看的一句话）。</param>
        /// <returns>读到了回上限；读不到回 null。</returns>
        public static int? ProgressLimitOf(int mapId, int index, out string note)
        {
            note = "";
            if (mapId <= 0) { note = "没有 map id，查不了难度表。"; return null; }

            MapTemplate t = Map(mapId);
            if (t == null) { note = "Map.bin 里没有 " + mapId + " 这张图，查不到难度表。"; return null; }
            if (t.Difficulties.Count == 0)
            { note = mapId + " 在 Map.bin 里没挂难度表（GameDifficultyDict 空），所以没有 ProgressLimit。"; return null; }

            int diffId = t.Difficulties[0];
            byte[] data = Read("Map.bin");
            if (data == null) { note = "读不到 Map.bin（游戏目录 / GameRes\\Bin 没找到）。"; return null; }

            bool sawTable = false, sawRow = false;
            foreach (Proto.Entry e in Proto.Entries(data, 6))
            {
                if (e.Key != diffId) continue;
                sawTable = true;
                foreach (Proto.Field cfg in Proto.Parse(e.Body))
                {
                    if (cfg.Number != 2 || cfg.Wire != 2 || cfg.Bytes == null) continue;   // field 2 = 一行难度行
                    int idx = 0, limit = 0;
                    foreach (Proto.Field a in Proto.Parse(cfg.Bytes))
                    {
                        if (a.Number == 1 && a.Wire == 5) idx = (int)a.Int;             // GameDifficultyType
                        else if (a.Number == 2 && a.Wire == 5) limit = (int)a.Int;      // ProgressLimit
                    }
                    if (idx != index) continue;
                    sawRow = true;
                    if (limit > 0) return limit;
                    note = "难度表 " + diffId + " 的 Index=" + index
                         + " 那一行没写 ProgressLimit（protobuf 里 0 不落盘）⇒ 上限未知。";
                    return null;
                }
            }

            if (!sawTable) note = "Map.bin 的难度表里没有 id=" + diffId + "（" + mapId + " 挂的那张表）。";
            else if (!sawRow) note = "难度表 " + diffId + " 里没有 Index=" + index + " 这一档。";
            else note = "表里没读到 ProgressLimit。";
            return null;
        }
    }
}
