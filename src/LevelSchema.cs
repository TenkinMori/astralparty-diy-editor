using System;
using System.Collections.Generic;
using System.Globalization;

namespace DiyEditor
{
    /// <summary>一个字段该怎么显示 / 怎么改。</summary>
    public enum FieldKind
    {
        Int,        // 整数（带范围提示）
        Bool,
        Text,
        MapInt,     // 形如 {"321120": -1} 的 "键是数字的字典" —— 键和值都是整数
    }

    /// <summary>
    /// 关卡配置的**字段注册表**：每个字段的名字、中文说明、类型、范围、
    /// 以及 ★最要紧的一件事★ —— **服务端到底读不读它**。
    ///
    /// 白名单的事实来源（只读侦察，2026-10-03）：
    ///   server\astralnext\levels.py:117-193  `_normalize`
    ///   server\astralnext\levels.py:71-114   `_monster` / `_spawn`（刷怪行的字段表）
    ///
    /// ★ `_normalize` 只挑它认识的键，**其余键一个不留**地丢掉 —— 也就是说
    ///   一个拼错的键（"victoy"）或者在错的位置放的东西（把 note 放进 spawn 行）
    ///   在服务端眼里等于不存在：**不报错、不警告、静默失效**。
    ///   这就是这个编辑器要提前标出来的事。
    /// </summary>
    public sealed class FieldSpec
    {
        public string Path;          // 形如 "rounds" / "progress.start" / "progress_events.*.spawn[].node"
        public string Label;         // 中文名
        public FieldKind Kind;
        public long Min, Max;        // 数值范围（提示用，不硬拦）
        public string Note;          // 说明 / 单位 / 默认值
        public long Fallback;        // 不写时服务端用什么（显示"默认"用）
        public bool HasFallback;

        public FieldSpec(string path, string label, FieldKind kind, string note)
        {
            Path = path; Label = label; Kind = kind; Note = note;
            Min = long.MinValue; Max = long.MaxValue;
        }

        public FieldSpec Range(long min, long max) { Min = min; Max = max; return this; }
        public FieldSpec Default(long value) { Fallback = value; HasFallback = true; return this; }
        public FieldSpec NoDefault() { HasFallback = false; return this; }
    }

    /// <summary>白名单：服务端会读的键集合。</summary>
    public static class LevelSchema
    {
        // ==================================================== 顶层（_normalize:122-193）
        public static readonly string[] TopKeys = new string[]
        {
            "map", "difficulty", "rounds", "round_award", "start_monsters",
            "progress", "progress_events", "victory", "mastermind", "settlement",
        };

        // round_award 子键（levels.py:128-136）
        public static readonly string[] RoundAwardKeys = new string[] { "every", "gold", "cards" };

        // progress 子键（levels.py:142-154）
        // ★ 2026-10-08：多了 `limit`（总进度上限）—— DIY 关卡按自己那份走，
        //   服务端第二批会优先认它（现在读的是难度行的 ProgressLimit）。
        public static readonly string[] ProgressKeys = new string[] { "start", "per_round", "per_death", "mission_delta", "limit" };

        // progress_events.<id> 子键（levels.py:155-173）
        // ★ 2026-10-08：多了 `rewards`（事件奖励：发筹码 / 发牌 / 发钱 / 加扣血），服务端第二批实现。
        //   `trigger_progress` 是编辑器自己的提示，服务端不读（见 KnownDropped）。
        public static readonly string[] EventKeys = new string[] { "spawn", "attack", "defense", "move", "rewards" };

        // progress_events.*.rewards[] 行（★ 2026-10-08 新增的键，服务端第二批读）
        public static readonly string[] RewardKeys = new string[]
        { "kind", "to", "choice", "ids", "count", "amount" };

        // victory 子键（levels.py:174-179）
        public static readonly string[] VictoryKeys = new string[] { "kind", "targets" };

        // start_monsters[] 行（levels.py:71-91 _monster）
        public static readonly string[] MonsterKeys = new string[]
        { "table", "node", "hp", "gold", "defense", "attack", "type", "weight" };

        // progress_events.*.spawn[] 行（levels.py:94-114 _spawn）
        public static readonly string[] SpawnKeys = new string[]
        { "table", "node", "count", "hp", "gold", "defense", "attack", "type", "weight" };

        /// <summary>被静默丢弃的键（服务端明确不读，但人容易以为它有用）。</summary>
        public static readonly Dictionary<string, string> KnownDropped = NewDropped();

        private static Dictionary<string, string> NewDropped()
        {
            var d = new Dictionary<string, string>(StringComparer.Ordinal);
            d["note"] = "写给人看的备注，服务端不读（留着无害，导出时原样保留）";
            d["progress_events_note"] = "写给人看的备注，服务端不读";
            d["boss"] = "服务端不读；Boss 要用 start_monsters 里的 table + victory.targets 表达";
            d["boss_max_hp"] = "服务端不读；血量写在 start_monsters 那一行的 hp 上";
            d["groups"] = "编辑器预留的自定义分组，服务端不读";
            d["id"] = "怪物行里可以当 table 的别名（levels.py:79 认），顶层不认";
            d["monster_id"] = "怪物行里可以当 table 的别名（levels.py:79 认），顶层不认";
            d["trigger_progress"] = "编辑器用的提示（「这一条想在第几点进度触发」）；服务端靠事件 id 自己就知道，不读";
            return d;
        }

        /// <summary>补充说明（给"这一项服务端会丢"的提示加一句人话）。</summary>
        public static string DropReason(string path, string key)
        {
            string why;
            if (KnownDropped.TryGetValue(key, out why)) return why;
            if (path == "" ) return "服务端白名单（levels.py:122-193）里没有这个顶层键 ⇒ 整段被丢掉";
            if (path == "victory") return "victory 只认 kind / targets（levels.py:176）⇒ 这个子键被丢掉";
            if (path == "round_award") return "round_award 只认 every / gold / cards（levels.py:132）⇒ 被丢掉";
            if (path == "progress") return "progress 只认 start / per_round / per_death / mission_delta / limit（levels.py:145）⇒ 被丢掉";
            if (path.StartsWith("progress_events.", StringComparison.Ordinal))
                return "进度事件只认 spawn / attack / defense / move / rewards（levels.py:167 + 本工程新增的 rewards）⇒ 被丢掉";
            if (path == "progress_events.*.rewards[]") return "奖励行只认 kind / to / choice / ids / count / amount ⇒ 被丢掉";
            if (path == "start_monsters[]") return "开局怪只认 table/node/hp/gold/defense/attack/type/weight（levels.py:82）⇒ 被丢掉";
            if (path == "progress_events.*.spawn[]") return "刷怪行只认 table/node/count/hp/gold/defense/attack/type/weight（levels.py:108）⇒ 被丢掉";
            if (path == "mastermind") return "mastermind 整块原样收下（levels.py:180），但里面**用不上的键**由真凶机制忽略";
            return "服务端不读这个键";
        }

        // ==================================================== 展示 / 编辑用的注册表
        private static readonly Dictionary<string, FieldSpec> Table = Build();

        public static FieldSpec Get(string path)
        {
            FieldSpec spec;
            return Table.TryGetValue(path, out spec) ? spec : null;
        }

        public static IEnumerable<FieldSpec> All { get { return Table.Values; } }

        private static void Add(Dictionary<string, FieldSpec> t, FieldSpec s) { t[s.Path] = s; }

        private static Dictionary<string, FieldSpec> Build()
        {
            var t = new Dictionary<string, FieldSpec>(StringComparer.Ordinal);

            Add(t, new FieldSpec("map", "地图 id", FieldKind.Int,
                "这张图在 Map.bin 里的 id（例如 82016 = 异变图书馆）。只影响文件名该怎么取：levels\\<map>_<diff>.json")
                .NoDefault());
            Add(t, new FieldSpec("difficulty", "难度档", FieldKind.Int,
                "GameDifficultyType，0 普通 / 1 困难 / 4 极限 / 5 超·炼狱 … 同一张图每档一份文件")
                .NoDefault());
            Add(t, new FieldSpec("rounds", "总回合数", FieldKind.Int,
                "到点还没赢就判负。不写 = 不按回合判负（主判负是难度行的 ProgressLimit 跑满）")
                .Range(1, 9999).Default(0));

            Add(t, new FieldSpec("round_award.every", "轮次奖励·每 N 回合", FieldKind.Int,
                "每 N 个回合发一次奖励").Range(1, 999).Default(3));
            Add(t, new FieldSpec("round_award.gold", "轮次奖励·星币", FieldKind.Int,
                "每次发几个星币").Range(0, 99999).Default(6));
            Add(t, new FieldSpec("round_award.cards", "轮次奖励·手牌", FieldKind.Int,
                "每次发几张手牌").Range(0, 99).Default(1));

            Add(t, new FieldSpec("progress.start", "进度·开局值", FieldKind.Int,
                "进度条（1071）开局在第几格").Range(0, 99).Default(1));
            Add(t, new FieldSpec("progress.per_round", "进度·每回合 +", FieldKind.Int,
                "每个回合开始涨多少").Range(0, 9).Default(1));
            Add(t, new FieldSpec("progress.per_death", "进度·每倒地 +", FieldKind.Int,
                "角色倒地一次涨多少").Range(0, 9).Default(1));
            Add(t, new FieldSpec("progress.mission_delta", "进度·任务增减", FieldKind.MapInt,
                "覆盖默认：{ \"任务id\": 增减 }。键必须能当整数，否则服务端整块崩")
                .NoDefault());

            Add(t, new FieldSpec("start_monsters[].table", "开局怪·怪物表 id", FieldKind.Int,
                "必填。<= 0 ⇒ 这一行整条被丢掉").Range(1, 999999).NoDefault());
            Add(t, new FieldSpec("start_monsters[].node", "开局怪·格子号", FieldKind.Int,
                "地板号（board 的 node id）。服务端不校验这一格存不存在")
                .Range(-1, 9999).Default(0));
            Add(t, new FieldSpec("start_monsters[].hp", "开局怪·血量", FieldKind.Int, "不写补 0").Range(0, 999999).Default(0));
            Add(t, new FieldSpec("start_monsters[].gold", "开局怪·星币", FieldKind.Int, "不写补 0").Range(0, 99999).Default(0));
            Add(t, new FieldSpec("start_monsters[].defense", "开局怪·防御", FieldKind.Int, "不写补 0").Range(0, 9999).Default(0));
            Add(t, new FieldSpec("start_monsters[].attack", "开局怪·攻击", FieldKind.Int, "不写补 0").Range(0, 9999).Default(0));
            Add(t, new FieldSpec("start_monsters[].type", "开局怪·类型", FieldKind.Int,
                "怪物类型编号（10 = Boss 那一类，12 = 小怪那一类）").Range(0, 99).Default(0));
            Add(t, new FieldSpec("start_monsters[].weight", "开局怪·权重", FieldKind.Int,
                "同组里的抽取权重，越大越常见").Range(0, 9999).Default(0));

            Add(t, new FieldSpec("progress_events.*.spawn[].table", "刷怪·怪物表 id", FieldKind.Int,
                "必填。<= 0 ⇒ 这一行整条被丢掉").Range(1, 999999).NoDefault());
            Add(t, new FieldSpec("progress_events.*.spawn[].node", "刷怪·格子号", FieldKind.Int,
                "-1 = 随机挑一块空地；写具体号则服务端不校验格子是否存在")
                .Range(-1, 9999).Default(-1));
            Add(t, new FieldSpec("progress_events.*.spawn[].count", "刷怪·只数", FieldKind.Int,
                "至少 1（写 0 会被抬成 1）").Range(1, 999).Default(1));
            Add(t, new FieldSpec("progress_events.*.spawn[].hp", "刷怪·血量", FieldKind.Int,
                "不写走怪物表 Monster.bin").Range(0, 999999).NoDefault());
            Add(t, new FieldSpec("progress_events.*.spawn[].gold", "刷怪·星币", FieldKind.Int,
                "不写走怪物表（这里回落表，和 start_monsters 补 0 不一样）").Range(0, 99999).NoDefault());
            Add(t, new FieldSpec("progress_events.*.spawn[].defense", "刷怪·防御", FieldKind.Int, "不写走怪物表").Range(0, 9999).NoDefault());
            Add(t, new FieldSpec("progress_events.*.spawn[].attack", "刷怪·攻击", FieldKind.Int, "不写走怪物表").Range(0, 9999).NoDefault());
            Add(t, new FieldSpec("progress_events.*.spawn[].type", "刷怪·类型", FieldKind.Int, "不写走怪物表").Range(0, 99).NoDefault());
            Add(t, new FieldSpec("progress_events.*.spawn[].weight", "刷怪·权重", FieldKind.Int, "不写走怪物表").Range(0, 9999).NoDefault());

            Add(t, new FieldSpec("progress_events.*.attack", "进度事件·全场攻击 +", FieldKind.Int,
                "3 号效果 MonsterAttrChange：作用于场上所有怪").Range(-99, 99).NoDefault());
            Add(t, new FieldSpec("progress_events.*.defense", "进度事件·全场防御 +", FieldKind.Int,
                "同 attack").Range(-99, 99).NoDefault());
            Add(t, new FieldSpec("progress_events.*.move", "进度事件·额外移动", FieldKind.Int,
                "服务端会读").Range(-99, 99).NoDefault());

            Add(t, new FieldSpec("victory.kind", "胜利条件·种类", FieldKind.Text,
                "目前实现的是 kill_boss = 打死 targets 里任意一只。空字符串会被整块丢掉")
                .NoDefault());
            Add(t, new FieldSpec("victory.targets[]", "胜利条件·目标怪 id", FieldKind.Int,
                "按怪物表 id（不是格子号）。<= 0 的会被剔掉")
                .Range(1, 999999).NoDefault());

            Add(t, new FieldSpec("settlement.awards", "结算奖励", FieldKind.MapInt,
                "发 1016 时带的 Awards：{ \"物品id\": 个数 }。键当整数解不出来就被剔掉（levels.py:188）")
                .NoDefault());

            Add(t, new FieldSpec("mastermind.enabled", "真凶机制·开关", FieldKind.Bool, "整块原样收下（levels.py:180）；具体语义见 mastermind.py").NoDefault());
            Add(t, new FieldSpec("mastermind.boss", "真凶机制·真凶怪 id", FieldKind.Int, "怪物表 id").Range(1, 999999).NoDefault());
            Add(t, new FieldSpec("mastermind.watcher", "真凶机制·目击者怪 id", FieldKind.Int, "怪物表 id").Range(1, 999999).NoDefault());
            Add(t, new FieldSpec("mastermind.reveal_skill", "真凶机制·揭露技能 id", FieldKind.Int, "Skill.bin 的 id").Range(1, 9999999).NoDefault());
            Add(t, new FieldSpec("mastermind.boss_skill", "真凶机制·真凶技能 id", FieldKind.Int, "Skill.bin 的 id").Range(1, 9999999).NoDefault());
            Add(t, new FieldSpec("mastermind.watcher_skill", "真凶机制·目击者技能 id", FieldKind.Int, "Skill.bin 的 id").Range(1, 9999999).NoDefault());

            return t;
        }

        /// <summary>把整数格式化成 JSON 里的样子（不变文化）。</summary>
        public static string Num(long v) { return v.ToString(CultureInfo.InvariantCulture); }
    }
}
