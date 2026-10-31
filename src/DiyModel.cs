using System;
using System.Collections.Generic;
using System.Globalization;

namespace DiyEditor
{
    // ============================================================================================
    // ★★ 这一份是"DIY 关卡自己的数据形状" —— 完全照用户 2026-10-03 那一段规格写的。
    //
    //   规格里出现的概念一共四个，落盘都在顶层 `diy` 块里（服务端只挑它认识的顶层键，
    //   多出来的 `diy` 整块会被静默丢掉 —— 正是我们想要的：DIY 扩展不污染服务端读的那份）：
    //
    //     ① **怪物池** `diy.monsters[]` —— 棋盘上放过 / 或手动加进来的怪，一只一条。
    //        池子里的每一条都有自己的属性（血量 / 防御 / 攻击 / 掉落星币 / 是否反击），
    //        棋盘格子只记 `diy.board_monsters[].pool`（指向池内的第几条）
    //        ⇒ **同一只怪放在不同格子上**也能各写各的属性（用户的硬要求）。
    //
    //     ② **进度** `diy.events.<事件id>` —— 一个事件 = 一份进度表：
    //          · `progress_limit` 总进度上限
    //          · `items[]`        进度清单，每条 `{ "n": 编号, "trigger_progress": 触发进度,
    //                              "trigger_event": 官方事件 id, "effects": [条词…] }`
    //          · `victory`        获胜条件（击败指定怪物 / 击败所有 BOSS 单位）
    //        `items[].effects` 里 `新增怪物` 那一条会同步写进服务端读的
    //        `progress_events.<该条编号>.spawn[]`（其余三类服务端现在不读，只落 diy）。
    //        ★★ 第 11 波：条目设了触发点（`trigger_progress` + `trigger_event`）⇒
    //        `progress_events` 的**键换成那条官方事件 id**（官方 82016_1.json 就是这么写的）；
    //        没设的照旧用编号 n。触发进度**只能靠事件 id 编码**，见 `DiyTrigger` 的说明。
    //
    //     ③ **DIY 进度占位事件** `diy.events.1001`：名字 = `DIY进度`，简介 = `DIY关卡的配置`。
    //        新建关卡时自动建这一条（内容包预留位）。
    //
    //     ④ **棋盘放怪** `diy.board_monsters[]` = `[{ "node": 格id, "pool": 池内序号 }]`。
    //
    //   难度：界面上一律不出现（客户端那边默认就是 DIY 难度），文件里写 `difficulty = 1001`。
    // ============================================================================================

    /// <summary>★ 池子里的一只怪。属性就规格里那五项，别的一律不加。</summary>
    public sealed class PoolMonster
    {
        /// <summary>池内序号（1 起）。棋盘格子 / 进度条词 / 获胜条件都用它指回来。</summary>
        public int Id;

        /// <summary>`Monster.bin` 的表 id。</summary>
        public int Table;

        /// <summary>血量。</summary>
        public long Hp;
        /// <summary>防御。</summary>
        public long Defense;
        /// <summary>攻击。</summary>
        public long Attack;
        /// <summary>掉落星币。</summary>
        public long Gold;
        /// <summary>是否反击。</summary>
        public bool Counter;

        public MonsterRow TableRow { get { return Table > 0 ? GameTables.Monster(Table) : null; } }
        public string Name { get { MonsterRow m = TableRow; return m == null ? ("怪 " + Table) : m.Name; } }
        public MonsterClass Class { get { MonsterRow m = TableRow; return m == null ? MonsterClass.Unknown : m.Class; } }
        public string ClassName { get { MonsterRow m = TableRow; return m == null ? "不在 Monster.bin 里" : m.ClassName; } }
        public string BustName { get { MonsterRow m = TableRow; return m == null ? null : m.BustName; } }

        /// <summary>★ 界面上真正画的头像（半身像没有时用这只怪的手牌画顶；见 `MonsterRow.AvatarName`）。</summary>
        public string AvatarName { get { MonsterRow m = TableRow; return m == null ? BustName : m.AvatarName; } }

        /// <summary>现在这张是不是"拿手牌画顶的"。</summary>
        public bool AvatarIsFallback { get { MonsterRow m = TableRow; return m != null && m.AvatarIsFallback; } }

        /// <summary>池子里怎么称呼它："#3 1067 兔兔王"。</summary>
        public string Display { get { return "#" + Id + " " + Table + " " + Name; } }

        /// <summary>挂在第几条进度下（没配回 null；= 规格里的"按进度分类"）。</summary>
        public int? ProgressN;

        /// <summary>
        /// ★ 2026-10-08：这只怪是**哪个事件**刷的（`0` = 没有任何事件指着它 ⇒ 开局就在场上）。
        ///
        /// 为什么要单独一个字段（用户 2026-10-09 实机反馈："只显示了一个事件1，不应该是显示每个进度
        /// 刷的怪吗"）：扁平模型下**一个事件只有一条进度条目、编号恒为 1**，所以按 `ProgressN`
        /// 分组会把所有事件刷的怪全挤进"进度 1"。按**事件**分组才是用户要的"每个进度刷的怪"。
        /// `ProgressN` 保留原语义（有些自测断言拿它跟条目编号比），界面改用这个字段。
        /// </summary>
        public int EventId;

        public PoolMonster Clone()
        {
            return (PoolMonster)MemberwiseClone();
        }
    }

    /// <summary>
    /// 棋盘上的**一处放置**：哪一格、放的哪只怪（池内序号）、归哪个事件。
    /// ★★ **同一格可以有多条**（用户 2026-10-03 第 9 波原话："支持在同一个格子里面加多个怪物"）
    ///    —— 所以这是"格 + 池内序号"的**列表**，不是"格 → 一只"的映射。
    ///    同一格放两只同款怪（同一个 pool）也允许：一条一行，界面上逐个列、逐个删。
    ///
    /// ★★ 2026-10-09 用户原话："**我要每个独立的怪物都分开，数值也要分开 diy 而不是现在这样**"
    ///    ⇒ 五项数值（血/防/攻/星币/反击）从"怪种"搬到**这一处放置**上：
    ///      同一只怪放在格 17 和格 25 = 两条 ref，各改各的，互不影响（不同事件里也一样）。
    ///    · 怪种（`PoolMonster`）那份数值从此只当**默认值/种子**：新建放置时从它带一份过来，
    ///      "随机补几只"那一行落盘也用它（其余行一律按 ref 自己的数值写）。
    ///    · 落盘：`progress_events.<事件>.spawn[]` / `start_monsters[]` **本来每行就有
    ///      hp/defense/attack/gold** ⇒ 文件侧不用改形状，只是"写谁的值"变了。
    ///      `counter` 服务端那几行里没有这个键（官方形状就 7 个键，不动它），
    ///      所以每一处放置的"是否反击"记在编辑器自己那块 `diy.board_monsters[].counter` 里。
    /// </summary>
    public sealed class BoardMonsterRef
    {
        public int Node;
        public int Pool;

        /// <summary>
        /// ★ 2026-10-08：这一格上的怪属于**哪个事件**（`0` = 开局就在场上 ⇒ `start_monsters`）。
        ///
        /// 为什么必须记它（用户原话："在棋盘那边加个切换进度的功能，切换到这个进度了就显示
        /// 这个进度刷的怪物，然后放置怪物就是在这个回合放置的"）：
        ///   以前"这格上的怪属于谁"是**猜**出来的（哪条事件的条词指向这只怪就算谁的），
        ///   于是"同一只怪被两条事件引用"时根本没有唯一答案；写盘时还会把**每一格**都写成
        ///   一行 spawn、读回来又变成一条条词 ⇒ N 格 → N 条 → N×N 行，越存越多（真踩过）。
        ///   记上事件归属之后：一只怪 = 一条条词，格子只挂在它下面，写盘按格出行、读回按怪聚合。
        /// </summary>
        public int Event;

        // ★★ 2026-10-09：**这一处放置自己**的五项数值（不再是"从怪种那里借来的一份"）。
        public long Hp;
        public long Defense;
        public long Attack;
        public long Gold;
        public bool Counter;

        /// <summary>新建一处放置时，把怪种（池子里那一条）的五项当种子带过来。</summary>
        public void SeedFrom(PoolMonster m)
        {
            if (m == null) return;
            Hp = m.Hp;
            Defense = m.Defense;
            Attack = m.Attack;
            Gold = m.Gold;
            Counter = m.Counter;
        }

        public BoardMonsterRef Clone() { return (BoardMonsterRef)MemberwiseClone(); }
    }

    /// <summary>开局组里"随机空地补几只"（对应 `start_monsters` 里 `node &lt; 0` 的那些行）。</summary>
    public sealed class StartSpawn
    {
        public int Pool;
        public int Count = 1;
    }

    /// <summary>
    /// ★★ DIY 侧数据"变了"的广播口 —— 怪物池 / 进度 / 棋盘放怪 任何一处改动都会响一下。
    ///
    /// 为什么要它（用户第 9 波原话："怪物页面要实时变化的，我在事件那边配置了还是没显示"）：
    ///   ① `ProgressN`（一只怪挂在哪条进度下）以前只在 `DiyPool.Read()` 里算一次，
    ///      界面在**内存对象上原地改**进度条词之后它就成了旧账 ⇒ 怪物页一直把怪摆在"未配置"；
    ///   ② 页面切来切去时，别的页改了数据、本页还拿着上一次的画面。
    ///   ⇒ 一条规矩：**写盘之后重算派生字段 + 响一声**；每一页在"变回可见"时对一下版本号，
    ///     对不上就自己重建（不靠人肉刷新）。
    /// </summary>
    public static class DiyNotify
    {
        /// <summary>版本号：数据每变一次 +1（页面拿它和"我上次画的那版"比）。</summary>
        public static int Version { get; private set; }

        /// <summary>订阅者（主窗口拿它更新状态行）。</summary>
        public static event Action Changed;

        public static void Touch()
        {
            Version++;
            Action h = Changed;
            if (h != null)
            {
                try { h(); }
                catch (Exception) { }
            }
        }
    }

    /// <summary>
    /// ★ 条词的一种 —— 用户 2026-10-03 确认过**六类**，2026-10-08 的"大改"里补了第七类：
    ///   ① 给所有人发一张牌　② 给所有人加/扣血　③ 新增怪物
    ///   ④ 所有人加星币　　　⑤ 怪物攻击力 +N　⑥ 怪物防御力 +N
    ///   ⑦ **发一次筹码**（走官方那套三选一，用户 2026-10-08 拍板）
    ///   ★ **没有**"词条（Mutator）"这一类 —— 用户没要（界面上不出现）。
    ///
    /// ★ 前三类（发牌 / 加扣血 / 加星币）与第七类（筹码）在落盘时属于**奖励**
    ///   （写进服务端读的 `progress_events.&lt;事件id&gt;.rewards[]`，见 `SyncProgressEvents`）；
    ///   ③新增怪物 → `.spawn[]`；⑤⑥ → `.attack` / `.defense`。
    /// </summary>
    public enum EffectKind
    {
        /// <summary>给所有人发一张牌（手牌 / 贴图）。</summary>
        Card = 0,
        /// <summary>给所有人加多少血，或者扣多少血（负数 = 扣）。</summary>
        Hp = 1,
        /// <summary>新增什么怪物（从怪物池里挑）。</summary>
        Spawn = 2,
        /// <summary>所有人加多少星币。</summary>
        Gold = 3,
        /// <summary>怪物攻击力 +N（场上所有怪）。</summary>
        MonsterAttack = 4,
        /// <summary>怪物防御力 +N（场上所有怪）。</summary>
        MonsterDefense = 5,
        /// <summary>★ 发一次筹码（官方 5211 三选一）。</summary>
        Relic = 6,
    }

    /// <summary>一条"条词"。</summary>
    public sealed class DiyEffect
    {
        public EffectKind Kind;
        /// <summary>Card = 卡牌 id。</summary>
        public int Card;
        /// <summary>Hp / Gold / 怪物攻 / 怪物防 = 数值（Hp 可以是负数 = 扣血）。</summary>
        public long N;
        /// <summary>Spawn = 池内序号。</summary>
        public int Pool;

        /// <summary>★ Spawn = 一次刷几只（用户 2026-10-08："每个事件刷多少怪"）。</summary>
        public int Count = 1;

        /// <summary>
        /// ★ 奖励发给谁（用户 2026-10-08 拍板"可配置"）：
        ///   `team` = 全队每人（默认）／`actor` = 触发这条事件的那个人。
        ///   只对奖励类（发牌 / 加扣血 / 加星币 / 筹码）有意义，落盘写成 `to`。
        /// </summary>
        public string To = ToTeam;

        /// <summary>★ Relic = 三选一给几张（默认为 3）。</summary>
        public int Choice = 3;

        public const string ToTeam = "team";
        public const string ToActor = "actor";

        /// <summary>`to` 的合法值 → 界面上的说法。</summary>
        public static string ToName(string to)
        {
            return string.Equals(to, ToActor, StringComparison.OrdinalIgnoreCase) ? "触发者" : "全队";
        }

        /// <summary>认 `to` 字段：不写 / 认不出 = 全队（跟服务端的默认口径一致）。</summary>
        public static string ParseTo(string to)
        {
            return string.Equals(to, ToActor, StringComparison.OrdinalIgnoreCase) ? ToActor : ToTeam;
        }

        /// <summary>这一条属不属于"奖励"（发牌 / 加扣血 / 加星币 / 筹码）。</summary>
        public bool IsReward
        {
            get
            {
                return Kind == EffectKind.Card || Kind == EffectKind.Hp
                    || Kind == EffectKind.Gold || Kind == EffectKind.Relic;
            }
        }

        public DiyEffect Clone() { return (DiyEffect)MemberwiseClone(); }

        public string KindLabel { get { return KindName(Kind); } }

        public static string KindName(EffectKind k)
        {
            switch (k)
            {
                case EffectKind.Card: return "给所有人发一张牌";
                case EffectKind.Hp: return "给所有人加/扣血";
                case EffectKind.Spawn: return "新增怪物";
                case EffectKind.Gold: return "所有人加星币";
                case EffectKind.MonsterAttack: return "怪物攻击力 +N";
                case EffectKind.MonsterDefense: return "怪物防御力 +N";
                case EffectKind.Relic: return "发一次筹码";
                default: return "?";
            }
        }

        /// <summary>界面上"这是哪一类"用的短标签。</summary>
        public static string KindShort(EffectKind k)
        {
            switch (k)
            {
                case EffectKind.Card: return "发牌";
                case EffectKind.Hp: return "加/扣血";
                case EffectKind.Spawn: return "新增怪物";
                case EffectKind.Gold: return "加星币";
                case EffectKind.MonsterAttack: return "怪物攻击力";
                case EffectKind.MonsterDefense: return "怪物防御力";
                case EffectKind.Relic: return "发筹码";
                default: return "?";
            }
        }

        /// <summary>落盘的 `type` 值（用中文，跟界面上说的一模一样）。</summary>
        public const string TypeCard = "给所有人发一张牌";
        public const string TypeHp = "给所有人加扣血";
        public const string TypeSpawn = "新增怪物";
        public const string TypeGold = "所有人加星币";
        public const string TypeMonsterAttack = "怪物攻击力";
        public const string TypeMonsterDefense = "怪物防御力";
        public const string TypeRelic = "发一次筹码";

        public static string TypeOf(EffectKind k)
        {
            switch (k)
            {
                case EffectKind.Card: return TypeCard;
                case EffectKind.Hp: return TypeHp;
                case EffectKind.Spawn: return TypeSpawn;
                case EffectKind.Gold: return TypeGold;
                case EffectKind.MonsterAttack: return TypeMonsterAttack;
                case EffectKind.MonsterDefense: return TypeMonsterDefense;
                case EffectKind.Relic: return TypeRelic;
                default: return TypeGold;
            }
        }

        /// <summary>
        /// 认 `type` 字段。★ 老文件里写的是英文（`gold` / `card` / `spawn` /
        /// `monster_attack` / `monster_defense`）—— 这几个都照收。
        /// ★ 只有 `mutator`（词条）不认：用户没要这一类，界面上也不出现。
        /// </summary>
        public static bool TryKind(string type, out EffectKind kind)
        {
            kind = EffectKind.Gold;
            if (string.IsNullOrEmpty(type)) return false;
            switch (type)
            {
                case TypeCard: case "card": kind = EffectKind.Card; return true;
                case TypeHp: case "hp": case "所有玩家加扣血": case "所有玩家加血": kind = EffectKind.Hp; return true;
                case TypeSpawn: case "spawn": kind = EffectKind.Spawn; return true;
                case TypeGold: case "gold": kind = EffectKind.Gold; return true;
                case TypeMonsterAttack: case "monster_attack":
                case "所有玩家怪物攻击力": kind = EffectKind.MonsterAttack; return true;
                case TypeMonsterDefense: case "monster_defense":
                case "所有玩家怪物防御力": kind = EffectKind.MonsterDefense; return true;
                case TypeRelic: case "relic": case "chip": case "筹码":
                    kind = EffectKind.Relic; return true;
                default: return false;
            }
        }

        /// <summary>发给谁（界面上那句话里的主语）：全队 = "所有人"（沿用老说法）／触发者。</summary>
        public string WhoText
        {
            get { return To == ToActor ? "触发者" : "所有人"; }
        }

        public string Summary(List<PoolMonster> pool)
        {
            switch (Kind)
            {
                case EffectKind.Card:
                    {
                        CardRow c = GameTables.Card(Card);
                        return "给" + WhoText + "发" + (Count > 1 ? (" " + Count + " 张") : "一张") + "牌：" + Card
                             + (c != null && c.Name.Length > 0 ? "（" + c.Name + "）" : "");
                    }
                case EffectKind.Hp:
                    return "给" + WhoText + (N >= 0 ? ("加 " + N + " 血") : ("扣 " + (-N) + " 血"));
                case EffectKind.Spawn:
                    {
                        PoolMonster m = DiyPool.Find(pool, Pool);
                        return "新增怪物：" + (m == null ? ("池内 #" + Pool + "（池子里没有这一条）") : m.Display)
                             + (Count > 1 ? (" × " + Count) : "");
                    }
                case EffectKind.Gold:
                    return WhoText + "加 " + N + " 星币";
                case EffectKind.Relic:
                    return "给" + WhoText + "发一次筹码（" + (Choice > 1 ? (Choice + " 选 1") : "1 张") + "）";
                case EffectKind.MonsterAttack:
                    return "怪物攻击力 " + (N >= 0 ? "+" + N : N.ToString()) + "（场上所有怪）";
                case EffectKind.MonsterDefense:
                    return "怪物防御力 " + (N >= 0 ? "+" + N : N.ToString()) + "（场上所有怪）";
                default:
                    return "?";
            }
        }
    }

    /// <summary>★ 一条进度（进度清单里的一行）。</summary>
    public sealed class DiyProgressItem
    {
        /// <summary>编号（用户明确要求"记得编号作区分"）。</summary>
        public int N;
        /// <summary>这一条下的条词。</summary>
        public List<DiyEffect> Effects = new List<DiyEffect>();

        /// <summary>
        /// ★★ 触发进度（第 11 波）：这一条进度事件在**第几点进度**触发（-1 = 还没设）。
        ///   口径 = 服务端 `gamedata.map_event_progress(event_id)` = MapEvent.bin 的 `field4 ÷ 2`；
        ///   按 `progress.per_round` 换算成"约第几回合"。见 `DiyTrigger`。
        /// </summary>
        public int TriggerProgress = -1;

        /// <summary>
        /// ★★ 编码这个触发点的**官方事件 id**（-1 = 还没挑）。
        ///   它是 `MapEvent.bin` 里 `field4 ÷ 2 == TriggerProgress` 的那一行；
        ///   同时用作服务端那份 `progress_events` 的**键**（官方就是这么干的）。
        /// </summary>
        public int TriggerEvent = -1;

        public DiyProgressItem Clone()
        {
            var c = new DiyProgressItem();
            c.N = N;
            c.TriggerProgress = TriggerProgress;
            c.TriggerEvent = TriggerEvent;
            foreach (DiyEffect e in Effects) c.Effects.Add(e.Clone());
            return c;
        }

        public string Title { get { return N.ToString(CultureInfo.InvariantCulture) + " 号进度"; } }

        public string Sub
        {
            get
            {
                if (Effects.Count == 0) return "还没有条词";
                var parts = new List<string>();
                foreach (DiyEffect e in Effects) parts.Add(DiyEffect.KindShort(e.Kind));
                return Effects.Count + " 条条词 · " + string.Join(" / ", parts.ToArray());
            }
        }
    }

    /// <summary>★ 获胜条件（规格里只预留这两种）。</summary>
    public static class DiyVictory
    {
        public const string TypeKillMonsters = "击败指定怪物";
        public const string TypeKillAllBoss = "击败所有BOSS单位";

        public static readonly string[] All = new string[] { TypeKillMonsters, TypeKillAllBoss };
    }

    /// <summary>★ 一个进度事件 = 一份进度表（`diy.events.&lt;id&gt;`）。</summary>
    public sealed class DiyEvent
    {
        public int Id;
        public string Name = "";
        /// <summary>简介。</summary>
        public string Description = "";
        /// <summary>总进度上限（-1 = 没写）。</summary>
        public long Limit = -1;
        public List<DiyProgressItem> Items = new List<DiyProgressItem>();
        /// <summary>勾上的获胜条件（`DiyVictory.All` 里的值）。</summary>
        public List<string> VictoryKinds = new List<string>();
        /// <summary>`击败指定怪物` 选中的池内序号。</summary>
        public List<int> VictoryTargets = new List<int>();

        public bool KillMonsters { get { return VictoryKinds.Contains(DiyVictory.TypeKillMonsters); } }
        public bool KillAllBoss { get { return VictoryKinds.Contains(DiyVictory.TypeKillAllBoss); } }

        public DiyEvent Clone()
        {
            var c = new DiyEvent();
            c.Id = Id; c.Name = Name; c.Description = Description; c.Limit = Limit;
            foreach (DiyProgressItem it in Items) c.Items.Add(it.Clone());
            c.VictoryKinds.AddRange(VictoryKinds);
            c.VictoryTargets.AddRange(VictoryTargets);
            return c;
        }
    }

    /// <summary>★ 整份 DIY 关卡的 DIY 侧数据（怪物池 + 棋盘放怪 + 进度事件）。</summary>
    public sealed class DiyData
    {
        public List<PoolMonster> Pool = new List<PoolMonster>();
        public List<BoardMonsterRef> BoardMonsters = new List<BoardMonsterRef>();
        /// <summary>开局组里"随机空地补几只"（没有具体格子的那种）。</summary>
        public List<StartSpawn> StartRandom = new List<StartSpawn>();
        public List<DiyEvent> Events = new List<DiyEvent>();

        /// <summary>下一个空出来的池内序号（1 起，取 max+1）。</summary>
        public int NextPoolId()
        {
            int max = 0;
            foreach (PoolMonster m in Pool) if (m.Id > max) max = m.Id;
            return max + 1;
        }

        public DiyEvent Event(int id)
        {
            foreach (DiyEvent e in Events) if (e.Id == id) return e;
            return null;
        }

        /// <summary>没给 id 就用第一条（新建关卡永远是 `DIY进度` 那一条）。</summary>
        public DiyEvent FirstEvent()
        {
            return Events.Count == 0 ? null : Events[0];
        }

        public List<int> EventIds()
        {
            var ids = new List<int>();
            foreach (DiyEvent e in Events) ids.Add(e.Id);
            ids.Sort();
            return ids;
        }

        /// <summary>下一格空着的进度编号（1 起，取 max+1 —— 用户要的"编号作区分"）。</summary>
        public int NextItemNumber(DiyEvent ev)
        {
            int max = 0;
            if (ev != null) foreach (DiyProgressItem it in ev.Items) if (it.N > max) max = it.N;
            return max + 1;
        }
    }

    /// <summary>★ `diy` 块（怪物池 + 进度）的读写。</summary>
    public static class DiyPool
    {
        public const string DiyKey = "diy";
        public const string PoolKey = "monsters";
        public const string BoardKey = "board_monsters";
        public const string EventsKey = "events";

        /// <summary>★ 内容包预留的占位事件（名字 / 简介是用户给定的原话）。</summary>
        public const int PlaceholderEventId = 1001;
        public const string PlaceholderEventName = "DIY进度";
        public const string PlaceholderEventDesc = "DIY关卡的配置";

        public static PoolMonster Find(List<PoolMonster> pool, int id)
        {
            if (pool == null) return null;
            foreach (PoolMonster m in pool) if (m.Id == id) return m;
            return null;
        }

        // ------------------------------------------------------------------ 读
        public static DiyData Read(LevelDoc doc)
        {
            var d = new DiyData();
            if (doc == null || doc.Root == null) return d;
            JNode diy = DiyJson.Member(doc.Root, DiyKey);
            bool hasDiy = diy != null && diy.Kind == JKind.Object;

            // ① 池子（编辑器自己的簿记：怪的"种类 + 数值 + 池内序号"）
            JNode pool = hasDiy ? DiyJson.Member(diy, PoolKey) : null;
            if (pool != null && pool.Kind == JKind.Array)
            {
                foreach (JNode row in pool.Items)
                {
                    if (row == null || row.Kind != JKind.Object) continue;
                    var m = new PoolMonster();
                    m.Id = (int)DiyJson.IntOf(DiyJson.Member(row, "id"), 0);
                    m.Table = (int)DiyJson.IntOf(DiyJson.Member(row, "table"), 0);
                    m.Hp = DiyJson.IntOf(DiyJson.Member(row, "hp"), 0);
                    m.Defense = DiyJson.IntOf(DiyJson.Member(row, "defense"), 0);
                    m.Attack = DiyJson.IntOf(DiyJson.Member(row, "attack"), 0);
                    m.Gold = DiyJson.IntOf(DiyJson.Member(row, "gold"), 0);
                    // ★ 老文件那一行可能没有 `counter` 键 ⇒ 回落**怪物表**的 can_counter（字段 22），
                    //   不能当成 false（否则 1067/1068/1069/1070 会被读成不反击）。
                    m.Counter = DiyJson.Member(row, "counter") != null
                        ? DiyJson.IntOf(DiyJson.Member(row, "counter"), 0) != 0
                        : TableCounter(m.Table);
                    if (m.Id <= 0) m.Id = d.NextPoolId();
                    if (m.Table <= 0) continue;
                    if (Find(d.Pool, m.Id) != null) continue;
                    d.Pool.Add(m);
                }
            }

            // ② 棋盘放怪（编辑器簿记）
            //   ★★ 2026-10-09：每一处放置**自己**的五项数值就记在这里（服务端那几行只有四个数值键，
            //      没有 `counter`），所以 `diy.board_monsters[]` 从这一版起多带 hp/defense/attack/gold/counter。
            //   ★ 老文件（那几行只有 node/pool/event）不报错：数值先留空，
            //      等 ③ 读 `spawn` / `start_monsters` 行时**按格补上**（见 AssignRowValues）。
            JNode bm = hasDiy ? DiyJson.Member(diy, BoardKey) : null;
            if (bm != null && bm.Kind == JKind.Array)
            {
                foreach (JNode row in bm.Items)
                {
                    if (row == null || row.Kind != JKind.Object) continue;
                    var r = new BoardMonsterRef();
                    r.Node = (int)DiyJson.IntOf(DiyJson.Member(row, "node"), -1);
                    r.Pool = (int)DiyJson.IntOf(DiyJson.Member(row, "pool"), 0);
                    r.Event = (int)DiyJson.IntOf(DiyJson.Member(row, "event"), 0);
                    if (r.Node < 0 || r.Pool <= 0) continue;
                    // ★★ 2026-10-09：先把怪种/表那份当兜底（**含"会不会反击"**）——
                    //   老文件这一行既可能没有数值键、也可能没有 `counter` 键，
                    //   而"缺键"绝不能当成 false（1067/1068/1069/1070 表里本来就会反击）。
                    r.SeedFrom(Find(d.Pool, r.Pool));
                    if (DiyJson.Member(row, "hp") != null)
                    {
                        r.Hp = DiyJson.IntOf(DiyJson.Member(row, "hp"), 0);
                        r.Defense = DiyJson.IntOf(DiyJson.Member(row, "defense"), 0);
                        r.Attack = DiyJson.IntOf(DiyJson.Member(row, "attack"), 0);
                        r.Gold = DiyJson.IntOf(DiyJson.Member(row, "gold"), 0);
                    }
                    if (DiyJson.Member(row, "counter") != null)
                        r.Counter = DiyJson.IntOf(DiyJson.Member(row, "counter"), 0) != 0;
                    d.BoardMonsters.Add(r);
                }
            }

            // ★ 开局组的"随机补几只"（编辑器簿记）：有它就不再从 `start_monsters` 的 node<0 行推
            bool startOwned = false;
            JNode sr = hasDiy ? DiyJson.Member(diy, "start_random") : null;
            if (sr != null && sr.Kind == JKind.Array)
            {
                startOwned = true;
                foreach (JNode row in sr.Items)
                {
                    if (row == null || row.Kind != JKind.Object) continue;
                    int pid = (int)DiyJson.IntOf(DiyJson.Member(row, "pool"), 0);
                    int cnt = (int)DiyJson.IntOf(DiyJson.Member(row, "count"), 1);
                    if (pid <= 0 || cnt < 1) continue;
                    d.StartRandom.Add(new StartSpawn { Pool = pid, Count = cnt });
                }
            }

            // ③ 事件：★ 新的扁平结构优先（`progress_events` + `start_monsters`）；
            //    老文件（只有 `diy.events.items`）走 legacy 那一支。
            //    ★ 棋盘落点只认一处：有 `diy.board_monsters` 就用它（编辑器自己的账），
            //      否则才从 spawn 行的 `node` 反推 —— 两边都读会**重复计数**。
            bool boardOwned = bm != null && bm.Kind == JKind.Array;
            bool flat = ReadFlatEvents(doc, d, diy, boardOwned, startOwned);
            if (!flat)
            {
                JNode events = hasDiy ? DiyJson.Member(diy, EventsKey) : null;
                if (events != null && events.Kind == JKind.Object && events.Members != null)
                {
                    foreach (JMember mem in events.Members)
                    {
                        int id;
                        if (!int.TryParse(mem.Key, NumberStyles.Integer, CultureInfo.InvariantCulture, out id)) continue;
                        if (mem.Value == null || mem.Value.Kind != JKind.Object) continue;
                        d.Events.Add(ReadEvent(id, mem.Value));
                    }
                    d.Events.Sort(delegate (DiyEvent a, DiyEvent b) { return a.Id.CompareTo(b.Id); });
                }
            }

            // ④ 池子里的怪"挂在哪条进度下" = 哪条进度的哪条条词指向它（取最靠前的那条）
            BindProgress(d);
            return d;
        }

        private static bool ReadFlatEvents(LevelDoc doc, DiyData d, JNode diy, bool boardOwned, bool startOwned)
        {
            // ★ 棋盘落点只认一处：文件里已经有 `diy.board_monsters`（boardOwned）时，
            //   就**不能**再按 spawn / start_monsters 行的 `node` 补一遍，否则同一只怪数两遍。
            //   （但要按格把**数值**补给它名下那几条放置 —— 老文件的 `diy.board_monsters[]`
            //     没有数值键，见 `AssignRowValues`。）
            bool addRefs = !boardOwned;
            var taken = new HashSet<BoardMonsterRef>();
            JNode pe = DiyJson.Member(doc.Root, "progress_events");
            bool flat = pe != null && pe.Kind == JKind.Object;
            if (flat && pe.Members != null)
            {
                foreach (JMember mem in pe.Members)
                {
                    int id;
                    if (!int.TryParse(mem.Key, NumberStyles.Integer, CultureInfo.InvariantCulture, out id)) continue;
                    if (mem.Value == null || mem.Value.Kind != JKind.Object) continue;
                    d.Events.Add(ReadFlatEvent(id, mem.Value, d, diy, addRefs, taken));
                }
                d.Events.Sort(delegate (DiyEvent a, DiyEvent b) { return a.Id.CompareTo(b.Id); });
            }

            // 开局就在场上的怪（Event = 0 那一组）：有格子的进 `diy.board_monsters`，
            // 没格子的（node < 0）记进 `d.StartRandom`（写盘时再变回一行 `node:-1`）。
            //   ★ 随机补那一行的数值口径：**怪种（`diy.monsters[]`）那份**（见 SyncStartMonsters 的注释）。
            JNode starts = DiyJson.Member(doc.Root, "start_monsters");
            if (starts != null && starts.Kind == JKind.Array)
                foreach (JNode row in starts.Items)
                {
                    int table, node, count, hp, df, at, gd;
                    if (!ReadSpawnRow(row, out table, out node, out count, out hp, out df, out at, out gd)) continue;
                    PoolMonster m = FindOrMakePool(d, table, hp, df, at, gd);
                    if (m == null) continue;
                    // ★ 怪种也按同一条口径被"种"上（行里没写数值 ⇒ 带出怪物表那份）
                    ApplyRowStatsToPool(m, table, hp, df, at, gd);
                    if (node >= 0)
                    {
                        if (addRefs)
                            for (int k = 0; k < count; k++)
                            {
                                var nr = new BoardMonsterRef { Node = node, Pool = m.Id, Event = 0 };
                                nr.SeedFrom(m);                             // 五项先按怪种/表那份（含反击）
                                ApplyRowStats(nr, table, hp, df, at, gd);   // 四项按行（0/缺 ⇒ 表值）
                                d.BoardMonsters.Add(nr);
                            }
                        else
                            AssignRowValues(d, 0, m.Id, table, node, count, hp, df, at, gd, taken);
                    }
                    else if (!startOwned) d.StartRandom.Add(new StartSpawn { Pool = m.Id, Count = count });
                }

            // ★ 顶层 `victory` / `progress.limit` 也读回来 —— 它们是**一关一个**，
            //   编辑器里挂在第一条事件上（`SyncVictory` / `SyncLimit` 写出去时也是取第一条）。
            //   不读回来，重开文件之后 ④ 获胜条件和 ① 总进度上限都会是空的。
            if (d.Events.Count > 0)
            {
                JNode vic = DiyJson.Member(doc.Root, "victory");
                if (vic != null && vic.Kind == JKind.Object)
                {
                    DiyEvent ev = d.Events[0];
                    JNode kk = DiyJson.Member(vic, "kind");
                    string kind = kk != null && kk.Kind == JKind.String ? kk.Str : "";
                    if (kind == "kill_all_boss")
                    {
                        if (!ev.VictoryKinds.Contains(DiyVictory.TypeKillAllBoss))
                            ev.VictoryKinds.Add(DiyVictory.TypeKillAllBoss);
                    }
                    else if (kind == "kill_boss")
                    {
                        if (!ev.VictoryKinds.Contains(DiyVictory.TypeKillMonsters))
                            ev.VictoryKinds.Add(DiyVictory.TypeKillMonsters);
                    }
                    JNode tg = DiyJson.Member(vic, "targets");
                    if (tg != null && tg.Kind == JKind.Array)
                        foreach (JNode t in tg.Items)
                        {
                            long v;
                            if (!DiyJson.IsInt(t, out v)) continue;
                            foreach (PoolMonster m in d.Pool)
                                if (m.Table == v && !ev.VictoryTargets.Contains(m.Id))
                                { ev.VictoryTargets.Add(m.Id); break; }
                        }
                }

                JNode prog = DiyJson.Member(doc.Root, "progress");
                JNode lim = DiyJson.Member(prog, "limit");
                if (lim != null && DiyJson.IsInt(lim, out long lv) && lv >= 0) d.Events[0].Limit = lv;
            }

            return flat;
        }

        /// <summary>
        /// 一条 `progress_events.&lt;事件id&gt;` → 一个事件。
        ///
        /// ★★ 刷怪**按怪聚合**（用户 2026-10-08："有多少个就显示几行"）：
        ///   文件里是"**每一格一行**"（服务端要知道站哪一格），编辑器里是"**一只怪一条**"，
        ///   只数 = 这一行里所有 spawn 行的 count 之和（棋盘上放的那几格也在里面）。
        ///   以前是一条行 → 一条条词，于是"棋盘上放 N 格 ⇒ 写 N 行 ⇒ 读回 N 条 ⇒ 再写 N×N 行"，
        ///   越存越多（真踩过：一份文件里同一只怪重复几百行）。
        /// </summary>
        private static DiyEvent ReadFlatEvent(int id, JNode row, DiyData d, JNode diy, bool addRefs,
                                             HashSet<BoardMonsterRef> taken)
        {
            var ev = new DiyEvent();
            ev.Id = id;

            JNode meta = DiyJson.Member(DiyJson.Member(diy, EventsKey),
                                        id.ToString(CultureInfo.InvariantCulture));
            if (meta != null && meta.Kind == JKind.Object)
            {
                JNode nm = DiyJson.Member(meta, "name");
                if (nm != null && nm.Kind == JKind.String) ev.Name = nm.Str;
                JNode ds = DiyJson.Member(meta, "desc");
                if (ds != null && ds.Kind == JKind.String) ev.Description = ds.Str;
            }

            var it = new DiyProgressItem();
            it.N = 1;
            // ★ 行键是**官方事件 id** 才算"设了触发点"；不是（= 还没设触发点时拿事件 id 当的键）
            //   就如实标成"还没设"，否则界面会假装它已经挑好了官方事件。
            it.TriggerEvent = MapEventTable.Known(id) ? id : -1;
            it.TriggerProgress = (int)DiyJson.IntOf(DiyJson.Member(row, DiyTrigger.KeyProgress), -1);
            if (it.TriggerProgress < 0) it.TriggerProgress = -1;

            // ① 刷怪：同一只怪合成一条条词（只数 = 所有行 count 之和）
            //   ★★ 2026-10-09：**带格子的行 = 一处放置一条 ref，数值取这一行的**
            //      （一行 count:N ⇒ 那一格上 N 处放置，各一份同样的数值 —— "每处放置都算一只"）；
            //      文件里已经有 `diy.board_monsters` 时落点以它为准，这里只把数值按格补给那几条
            //      （`AssignRowValues`，老文件的 board_monsters 没有数值键才用得上）。
            //   随机补那一行（node:-1）**不建实例**：它的只数就是这条条词的 `Count`（计划只数），
            //      写盘时按"计划只数 − 实际放了几格"补回一行（现行规则，没动）。
            var pools = new List<int>();
            var counts = new List<int>();
            JNode arr = DiyJson.Member(row, "spawn");
            if (arr != null && arr.Kind == JKind.Array)
                foreach (JNode r in arr.Items)
                {
                    int table, node, count, hp, df, at, gd;
                    if (!ReadSpawnRow(r, out table, out node, out count, out hp, out df, out at, out gd)) continue;
                    PoolMonster m = FindOrMakePool(d, table, hp, df, at, gd);
                    if (m == null) continue;
                    // ★ 怪种也按同一条口径被"种"上（行里没写数值 ⇒ 带出怪物表那份）
                    ApplyRowStatsToPool(m, table, hp, df, at, gd);
                    if (node >= 0)
                    {
                        if (addRefs)
                            for (int k = 0; k < count; k++)
                            {
                                var nr = new BoardMonsterRef { Node = node, Pool = m.Id, Event = id };
                                nr.SeedFrom(m);                             // 五项先按怪种/表那份（含反击）
                                ApplyRowStats(nr, table, hp, df, at, gd);   // 四项按行（0/缺 ⇒ 表值）
                                d.BoardMonsters.Add(nr);
                            }
                        else
                            AssignRowValues(d, id, m.Id, table, node, count, hp, df, at, gd, taken);
                    }
                    int idx = pools.IndexOf(m.Id);
                    if (idx < 0) { pools.Add(m.Id); counts.Add(count); }
                    else counts[idx] += count;
                }
            for (int k = 0; k < pools.Count; k++)
                it.Effects.Add(new DiyEffect { Kind = EffectKind.Spawn, Pool = pools[k], Count = counts[k] });

            // ② 场上所有怪加攻 / 加防
            long v;
            JNode att = DiyJson.Member(row, "attack");
            if (att != null && DiyJson.IsInt(att, out v))
                it.Effects.Add(new DiyEffect { Kind = EffectKind.MonsterAttack, N = v });
            JNode def = DiyJson.Member(row, "defense");
            if (def != null && DiyJson.IsInt(def, out v))
                it.Effects.Add(new DiyEffect { Kind = EffectKind.MonsterDefense, N = v });

            // ③ 奖励
            JNode rw = DiyJson.Member(row, "rewards");
            if (rw != null && rw.Kind == JKind.Array)
                foreach (JNode r in rw.Items)
                {
                    DiyEffect x = ReadReward(r);
                    if (x != null) it.Effects.Add(x);
                }

            // ★ 空行（既没有效果、也没写触发进度）**不造进度条目** —— 否则"新建的关卡点进去
            //   就有一条空进度"。这种行保存时也会被 `SyncProgressEvents` 顺手清掉。
            if (it.Effects.Count > 0 || it.TriggerProgress >= 0) ev.Items.Add(it);
            return ev;
        }

        /// <summary>一行 spawn / start_monsters 读成 7 个数（读不出返回 false）。</summary>
        private static bool ReadSpawnRow(JNode r, out int table, out int node, out int count,
                                        out int hp, out int defense, out int attack, out int gold)
        {
            table = node = count = hp = defense = attack = gold = 0;
            if (r == null || r.Kind != JKind.Object) return false;
            table = (int)DiyJson.IntOf(DiyJson.Member(r, "table"), 0);
            if (table <= 0) return false;
            node = (int)DiyJson.IntOf(DiyJson.Member(r, "node"), -1);
            count = (int)DiyJson.IntOf(DiyJson.Member(r, "count"), 1);
            if (count < 1) count = 1;
            hp = (int)DiyJson.IntOf(DiyJson.Member(r, "hp"), 0);
            defense = (int)DiyJson.IntOf(DiyJson.Member(r, "defense"), 0);
            attack = (int)DiyJson.IntOf(DiyJson.Member(r, "attack"), 0);
            gold = (int)DiyJson.IntOf(DiyJson.Member(r, "gold"), 0);
            return true;
        }

        /// <summary>
        /// 池子里找**这一种怪**（按 `table` 认）；没有就按这一行的数值建一条。
        /// ★★ 2026-10-09 改了合并口径：**不再按 (table, 数值) 合并**。
        ///   老口径下"同一种怪的两行只要数值一样就并成一条池内怪"，于是棋盘上放几处都指着同一条、
        ///   数值也就只有一份 —— 用户看到的就是"没分开"。现在：
        ///     · 池子 = **怪种清单**（一种怪一条，值只当"种子/默认值"）；
        ///     · 每一处放置（`BoardMonsterRef`）**自己带一份五项**，各改各的。
        ///   所以这里只按 table 认种，数值只在"新建这一条种"时用得上。
        /// </summary>
        private static PoolMonster FindOrMakePool(DiyData d, int table, int hp, int defense, int attack, int gold)
        {
            if (d == null || table <= 0) return null;
            foreach (PoolMonster p in d.Pool) if (p.Table == table) return p;
            var m = new PoolMonster();
            m.Id = d.NextPoolId();
            m.Table = table;
            m.Hp = hp; m.Defense = defense; m.Attack = attack; m.Gold = gold;
            // ★ 这一行（spawn / start_monsters）没有 counter 键 ⇒ 按**怪物表**的 can_counter 兜底
            m.Counter = TableCounter(table);
            d.Pool.Add(m);
            return m;
        }

        /// <summary>
        /// ★★ 2026-10-09（用户拍板的口径）：一行 `spawn` / `start_monsters` 的四个数值怎么算
        ///   —— **整个模型里只有这一处写这个判断**，三处调用点都调它。
        ///
        /// 用户原话：「**要：0/缺失就默认带出怪物表的值**」。落成规矩：
        ///   · `hp &lt;= 0`（或这一行压根没写那几个数值键 —— `ReadSpawnRow` 读出来就是 0）⇒
        ///     视为"这一行没写数值"：四项（血 / 防 / 攻 / 星币）**一律从怪物表 `Monster.bin` 带出来**
        ///     （就是 `MakePool` 用的那份基础值 `Blood / Defense / Attack / Gold`）。
        ///   · `hp &gt; 0` ⇒ 这一行的值**一个都不许动** —— 攻击 / 防御本来就是 0 的怪很多，
        ///     不能拿表值去盖它们的 0（用户点名的一条）。
        ///   · 表里查不到这一只（`Monster.bin` 没这一行）⇒ 只能照行里的值（几个 0）。
        ///
        /// ★ 带出来的值就是这一处放置 / 这一条怪种的**实际数值** ⇒ 下一次保存会**写进 json**
        ///   （用户要的就是"在合理的数上改"，这是预期行为，不是"改了用户的文件"）。
        /// </summary>
        private static void RowStats(int table, int hp, int df, int at, int gd,
                                    out long ohp, out long odf, out long oat, out long ogd)
        {
            if (hp > 0) { ohp = hp; odf = df; oat = at; ogd = gd; return; }
            MonsterRow row = GameTables.Monster(table);
            if (row == null) { ohp = hp; odf = df; oat = at; ogd = gd; return; }
            ohp = row.Blood; odf = row.Defense; oat = row.Attack; ogd = row.Gold;
        }

        /// <summary>把一行数值按 `RowStats` 的口径写进**一处放置**。</summary>
        private static void ApplyRowStats(BoardMonsterRef r, int table, int hp, int df, int at, int gd)
        {
            if (r == null) return;
            long h, d2, a, g;
            RowStats(table, hp, df, at, gd, out h, out d2, out a, out g);
            r.Hp = h; r.Defense = d2; r.Attack = a; r.Gold = g;
        }

        /// <summary>
        /// 同一条规则用在**怪种**（`diy.monsters[]` 那一条）上 —— 它是"新建放置 / 随机补那一行"的
        /// 种子来源，所以也得跟着被种上。
        /// ★ 只有"没写数值"（`hp &lt;= 0`）的行才动怪种：`hp &gt; 0` 的行**不碰**它
        ///   （怪种那份可能是用户自己调的默认值，别被某一行具体放置的数值带跑）。
        /// </summary>
        private static void ApplyRowStatsToPool(PoolMonster m, int table, int hp, int df, int at, int gd)
        {
            if (m == null || hp > 0) return;
            long h, d2, a, g;
            RowStats(table, hp, df, at, gd, out h, out d2, out a, out g);
            m.Hp = h; m.Defense = d2; m.Attack = a; m.Gold = g;
        }

        /// <summary>
        /// 把一行 `spawn` / `start_monsters` 的**数值**补给"它名下那几条放置"。
        ///
        /// 什么时候用得上：文件里有 `diy.board_monsters`（= 编辑器自己排过版）时，落点是**以它为准**
        ///   读的（见 `ReadFlatEvents` 的 `addRefs`），而老文件的 `diy.board_monsters[]` 只有
        ///   node/pool/event 三个键、没有数值 ⇒ 这里按 (事件, 怪种, 格号) 把行里的数值一一对上补进去。
        ///   新文件里两边本来就一样（同一批 ref 写出来的），补一次等于没补。
        /// ★ 一个格子上同种怪放了两条、数值还不一样时：这里按**出现顺序**一一对应
        ///   （写盘也是按 `BoardMonsters` 的顺序出行 ⇒ 顺序稳定，来回一趟不串值）。
        /// ★ 数值口径走 `ApplyRowStats`（同一个判断，不再抄一份）：行里 `hp &lt;= 0` ⇒ 带出怪物表的值。
        /// </summary>
        private static void AssignRowValues(DiyData d, int eventId, int poolId, int table, int node, int count,
                                           long hp, long df, long at, long gd,
                                           HashSet<BoardMonsterRef> taken)
        {
            int left = count;
            foreach (BoardMonsterRef r in d.BoardMonsters)
            {
                if (left <= 0) break;
                if (r.Event != eventId || r.Pool != poolId || r.Node != node) continue;
                if (taken != null && taken.Contains(r)) continue;
                ApplyRowStats(r, table, (int)hp, (int)df, (int)at, (int)gd);
                if (taken != null) taken.Add(r);
                left--;
            }
        }

        /// <summary>一行 `rewards[]` → 一条奖励条词；认不出返回 null。</summary>
        private static DiyEffect ReadReward(JNode r)
        {
            if (r == null || r.Kind != JKind.Object) return null;
            JNode k = DiyJson.Member(r, "kind");
            string kind = (k != null && k.Kind == JKind.String) ? k.Str : "";
            var x = new DiyEffect();
            JNode to = DiyJson.Member(r, "to");
            if (to != null && to.Kind == JKind.String) x.To = DiyEffect.ParseTo(to.Str);
            switch (kind)
            {
                case "relic":
                case "chip":
                    x.Kind = EffectKind.Relic;
                    x.Choice = (int)DiyJson.IntOf(DiyJson.Member(r, "choice"), 3);
                    if (x.Choice < 1) x.Choice = 1;
                    break;
                case "card":
                    x.Kind = EffectKind.Card;
                    JNode ids = DiyJson.Member(r, "ids");
                    if (ids != null && ids.Kind == JKind.Array && ids.Items.Count > 0)
                    {
                        long v;
                        if (DiyJson.IsInt(ids.Items[0], out v)) x.Card = (int)v;
                    }
                    if (x.Card <= 0) x.Card = (int)DiyJson.IntOf(DiyJson.Member(r, "card"), 0);
                    x.Count = (int)DiyJson.IntOf(DiyJson.Member(r, "count"), 1);
                    if (x.Count < 1) x.Count = 1;
                    break;
                case "gold":
                    x.Kind = EffectKind.Gold;
                    x.N = DiyJson.IntOf(DiyJson.Member(r, "amount"), DiyJson.IntOf(DiyJson.Member(r, "n"), 0));
                    break;
                case "hp":
                    x.Kind = EffectKind.Hp;
                    x.N = DiyJson.IntOf(DiyJson.Member(r, "amount"), DiyJson.IntOf(DiyJson.Member(r, "n"), 0));
                    break;
                default:
                    return null;
            }
            return x;
        }

        /// <summary>
        /// ★★ 重算"池子里每只怪挂在哪条进度下"（= 怪物页分栏的依据）。
        ///
        /// 为什么要单独一个方法：`Read()` 只在"缓存作废、重新从树里读"时跑，
        /// 而界面上的编辑是**在内存对象上原地改**的（`SaveDiy` 不换对象）。
        /// 不在这里重算，怪物页就会拿着"上一版的进度归属" —— 用户看到的现象就是
        /// 「事件那边配了新增怪物，怪物页还是没显示（还挂在未配置）」。
        ///
        /// 口径：先全部清空，再按**编号最小的那条**重新挂（一只有多条指向它时取最靠前的）。
        /// </summary>
        public static void BindProgress(DiyData d)
        {
            if (d == null) return;
            foreach (PoolMonster m in d.Pool) { m.ProgressN = null; m.EventId = 0; }
            foreach (DiyEvent ev in d.Events)
            {
                int key = EventKeyOf(ev);       // ★ 界面按"事件"分组就用它（0 = 开局组）
                foreach (DiyProgressItem it in ev.Items)
                    foreach (DiyEffect x in it.Effects)
                        if (x.Kind == EffectKind.Spawn)
                        {
                            PoolMonster m = Find(d.Pool, x.Pool);
                            if (m == null) continue;
                            if (!m.ProgressN.HasValue) m.ProgressN = it.N;
                            // ★ 一只怪被两个事件指着的：按**第一个**算（跟 ProgressN 一个口径）
                            if (m.EventId == 0) m.EventId = key;
                        }
            }
        }

        private static DiyEvent ReadEvent(int id, JNode block)
        {
            var ev = new DiyEvent();
            ev.Id = id;

            JNode nm = DiyJson.Member(block, "name");
            if (nm != null && nm.Kind == JKind.String) ev.Name = nm.Str;
            JNode ds = DiyJson.Member(block, "desc");
            if (ds != null && ds.Kind == JKind.String) ev.Description = ds.Str;
            ev.Limit = -1;
            JNode lim = DiyJson.Member(block, "progress_limit");
            if (lim != null)
            {
                long v;
                if (DiyJson.IsInt(lim, out v)) ev.Limit = v;
            }

            JNode items = DiyJson.Member(block, "items");
            if (items != null && items.Kind == JKind.Array)
            {
                int auto = 0;
                foreach (JNode row in items.Items)
                {
                    auto++;
                    if (row == null || row.Kind != JKind.Object) continue;
                    var it = new DiyProgressItem();
                    it.N = (int)DiyJson.IntOf(DiyJson.Member(row, "n"), auto);
                    if (it.N <= 0) it.N = auto;
                    // ★ 触发点（第 11 波）：trigger_progress = 想在第几点进度触发；
                    //   trigger_event = 用哪条官方事件 id 编码它（MapEvent.bin field4 ÷ 2）。
                    it.TriggerProgress = (int)DiyJson.IntOf(DiyJson.Member(row, DiyTrigger.KeyProgress), -1);
                    it.TriggerEvent = (int)DiyJson.IntOf(DiyJson.Member(row, DiyTrigger.KeyEvent), -1);
                    if (it.TriggerProgress < 0) it.TriggerProgress = -1;
                    if (it.TriggerEvent <= 0) it.TriggerEvent = -1;

                    JNode eff = DiyJson.Member(row, "effects");
                    if (eff != null && eff.Kind == JKind.Array)
                    {
                        foreach (JNode e in eff.Items)
                        {
                            DiyEffect x = ReadEffect(e);
                            if (x != null) it.Effects.Add(x);
                        }
                    }
                    ev.Items.Add(it);
                }
                ev.Items.Sort(delegate (DiyProgressItem a, DiyProgressItem b) { return a.N.CompareTo(b.N); });
            }

            JNode vic = DiyJson.Member(block, "victory");
            if (vic != null && vic.Kind == JKind.Object)
            {
                JNode kinds = DiyJson.Member(vic, "kinds");
                if (kinds != null && kinds.Kind == JKind.Array)
                    foreach (JNode k in kinds.Items)
                        if (k != null && k.Kind == JKind.String && Array.IndexOf(DiyVictory.All, k.Str) >= 0)
                            if (!ev.VictoryKinds.Contains(k.Str)) ev.VictoryKinds.Add(k.Str);
                JNode tg = DiyJson.Member(vic, "targets");
                if (tg != null && tg.Kind == JKind.Array)
                    foreach (JNode t in tg.Items)
                    {
                        long v;
                        if (DiyJson.IsInt(t, out v) && v > 0 && !ev.VictoryTargets.Contains((int)v))
                            ev.VictoryTargets.Add((int)v);
                    }
            }
            return ev;
        }

        private static DiyEffect ReadEffect(JNode e)
        {
            if (e == null || e.Kind != JKind.Object) return null;
            JNode t = DiyJson.Member(e, "type");
            if (t == null || t.Kind != JKind.String) return null;
            EffectKind kind;
            if (!DiyEffect.TryKind(t.Str, out kind)) return null;

            var x = new DiyEffect();
            x.Kind = kind;
            // ★ 2026-10-08 大改：数量 / 给谁 / 筹码张数 —— 老文件不写这几个键 = 走默认值
            x.Count = (int)DiyJson.IntOf(DiyJson.Member(e, "count"), 1);
            if (x.Count < 1) x.Count = 1;
            x.Choice = (int)DiyJson.IntOf(DiyJson.Member(e, "choice"), 3);
            if (x.Choice < 1) x.Choice = 1;
            JNode toNode = DiyJson.Member(e, "to");
            if (toNode != null && toNode.Kind == JKind.String) x.To = DiyEffect.ParseTo(toNode.Str);
            switch (kind)
            {
                case EffectKind.Card:
                    x.Card = (int)DiyJson.IntOf(DiyJson.Member(e, "card"), 0);
                    break;
                case EffectKind.Hp:
                case EffectKind.Gold:
                case EffectKind.MonsterAttack:
                case EffectKind.MonsterDefense:
                    x.N = DiyJson.IntOf(DiyJson.Member(e, "n"), 0);
                    break;
                case EffectKind.Spawn:
                    x.Pool = (int)DiyJson.IntOf(DiyJson.Member(e, "monster"), 0);
                    // 老写法：`table` 指怪物表 id ⇒ 看看池子里有没有这一只，有就转成池内序号
                    if (x.Pool <= 0)
                    {
                        int tbl = (int)DiyJson.IntOf(DiyJson.Member(e, "table"), 0);
                        if (tbl > 0) x.Pool = -tbl;      // 负数先记着，调用方用 BindLegacy 修
                    }
                    break;
            }
            return x;
        }

        /// <summary>
        /// ★ 老文件（`table` 直指怪物表 id）的兼容：`Read` 之后调一次，
        /// 把"负数占位"换成池内序号 —— 池子里有这一只就用它，没有就补进池子。
        /// </summary>
        public static void BindLegacy(DiyData d)
        {
            if (d == null) return;
            foreach (DiyEvent ev in d.Events)
                foreach (DiyProgressItem it in ev.Items)
                    foreach (DiyEffect x in it.Effects)
                        if (x.Kind == EffectKind.Spawn && x.Pool < 0)
                        {
                            int tbl = -x.Pool;
                            PoolMonster hit = null;
                            foreach (PoolMonster m in d.Pool) if (m.Table == tbl) { hit = m; break; }
                            if (hit == null)
                            {
                                hit = MakePool(d, tbl);
                            }
                            x.Pool = hit.Id;
                        }
        }

        // ------------------------------------------------------------------ 写
        private static JNode EnsureDiy(LevelDoc doc)
        {
            JNode diy = DiyJson.EnsureMember(doc.Root, DiyKey);
            DiyJson.SetKind(diy, JKind.Object);
            return diy;
        }

        /// <summary>
        /// 把整份 DIY 数据写回树里。
        ///
        /// ★ 2026-10-08 大改（用户拍板「扁平」）之后，这个函数只写**编辑器自己的簿记**：
        ///   `diy.monsters`（怪的池子 + 数值）、`diy.board_monsters`（棋盘上哪一格放了哪只）、
        ///   `diy.events.&lt;id&gt;.{name,desc}`（显示名）——
        ///   玩法内容（刷怪 / 攻防 / 奖励 / 胜利条件 / 进度上限）全在**服务端真读的那些键**里，
        ///   由本函数末尾的 `SyncProgressEvents` / `SyncVictory` / `SyncLimit` 写出去。
        ///   `diy` 整块服务端是**忽略**的，所以簿记放这儿不影响服务端。
        /// </summary>
        public static void Write(LevelDoc doc, DiyData d)
        {
            if (doc == null || doc.Root == null || d == null) return;
            JNode diy = EnsureDiy(doc);

            // ★ 这份文件有没有被这套编辑器管过：管过就按编辑器里的状态**整体重写**
            //   `start_monsters`；没管过（刚打开的成品模板 / 官方关卡）就一个字节都别动它自带的开局怪。
            //
            // ★★ 2026-10-10 扩了一条判据（用户原话：「但是我编辑器这里写的是开局刷怪是 0 啊」）：
            //   **编辑器自己造的 DIY 关卡**（`difficulty >= DiyDifficulty`，官方那 6 份模板是 0~5）
            //   也算"管过" —— 那种关卡里"开局组 0 只"就是编辑器的结论，必须落成
            //   `start_monsters: []`（明确不刷），不能靠"没写 = 回落官方 `battle.INIT_MONSTERS`"，
            //   否则就是**编辑器显示 0 只、进游戏却刷官方那一排**。
            //   ★ 官方那 6 份模板（difficulty 0~5、也没有 `diy.monsters` / `diy.board_monsters`）
            //     依旧一个字不动，它们自带的开局怪行原样留着。
            bool managed = d.Pool.Count > 0 || d.BoardMonsters.Count > 0
                || DiyJson.Member(diy, PoolKey) != null || DiyJson.Member(diy, BoardKey) != null
                || doc.Difficulty >= LevelDoc.DiyDifficulty;

            // ① 池子：id 重排成 1..N（池内序号必须连续，否则界面上的 # 会跳号）
            var pool = new List<PoolMonster>(d.Pool);
            pool.Sort(delegate (PoolMonster a, PoolMonster b) { return a.Id.CompareTo(b.Id); });
            var remap = new Dictionary<int, int>();
            for (int k = 0; k < pool.Count; k++)
            {
                remap[pool[k].Id] = k + 1;
                pool[k].Id = k + 1;
            }
            RemapRefs(d, remap);

            if (pool.Count == 0)
            {
                DiyJson.RemoveMember(diy, PoolKey);
            }
            else
            {
                JNode arr = DiyJson.SetKind(DiyJson.EnsureMember(diy, PoolKey), JKind.Array);
                arr.Items.Clear();
                foreach (PoolMonster m in pool)
                {
                    JNode o = NewObj(arr);
                    AddNum(o, "id", m.Id);
                    AddNum(o, "table", m.Table);
                    AddNum(o, "hp", m.Hp);
                    AddNum(o, "defense", m.Defense);
                    AddNum(o, "attack", m.Attack);
                    AddNum(o, "gold", m.Gold);
                    AddNum(o, "counter", m.Counter ? 1 : 0);
                    arr.Items.Add(o);
                }
                DiyJson.MarkDirty(arr);
            }

            // ② 棋盘放怪（哪一格放了哪只 —— 棋盘页/怪物页要显示它）
            //   ★★ 2026-10-09：每一处放置**自己**的五项也写在这儿（`counter` 只有这里有，
            //      服务端那几行没有这个键 —— 官方形状别动）。
            d.BoardMonsters.RemoveAll(delegate (BoardMonsterRef r) { return Find(pool, r.Pool) == null; });
            if (d.BoardMonsters.Count == 0)
            {
                DiyJson.RemoveMember(diy, BoardKey);
            }
            else
            {
                JNode arr = DiyJson.SetKind(DiyJson.EnsureMember(diy, BoardKey), JKind.Array);
                arr.Items.Clear();
                foreach (BoardMonsterRef r in d.BoardMonsters)
                {
                    JNode o = NewObj(arr);
                    AddNum(o, "node", r.Node);
                    AddNum(o, "pool", r.Pool);
                    AddNum(o, "event", r.Event);
                    AddNum(o, "hp", r.Hp);
                    AddNum(o, "defense", r.Defense);
                    AddNum(o, "attack", r.Attack);
                    AddNum(o, "gold", r.Gold);
                    AddNum(o, "counter", r.Counter ? 1 : 0);
                    arr.Items.Add(o);
                }
                DiyJson.MarkDirty(arr);
            }

            // ②b 开局组里"随机空地补几只"（簿记；写盘时会变成 `start_monsters` 的 node:-1 行）
            d.StartRandom.RemoveAll(delegate (StartSpawn s) { return Find(pool, s.Pool) == null || s.Count < 1; });
            if (d.StartRandom.Count == 0)
            {
                DiyJson.RemoveMember(diy, "start_random");
            }
            else
            {
                JNode arr = DiyJson.SetKind(DiyJson.EnsureMember(diy, "start_random"), JKind.Array);
                arr.Items.Clear();
                foreach (StartSpawn s in d.StartRandom)
                {
                    JNode o = NewObj(arr);
                    AddNum(o, "pool", s.Pool);
                    AddNum(o, "count", s.Count);
                    arr.Items.Add(o);
                }
                DiyJson.MarkDirty(arr);
            }

            // ③ 事件：**只留显示名 / 简介**（玩法内容不在这儿了，见 SyncProgressEvents）
            JNode events = DiyJson.EnsureMember(diy, EventsKey);
            DiyJson.SetKind(events, JKind.Object);
            var keep = new List<int>();
            foreach (DiyEvent ev in d.Events)
            {
                JNode block = DiyJson.SetKind(DiyJson.EnsureMember(events, ev.Id.ToString(CultureInfo.InvariantCulture)), JKind.Object);
                DiyJson.SetString(DiyJson.EnsureMember(block, "name"), ev.Name);
                DiyJson.SetString(DiyJson.EnsureMember(block, "desc"), ev.Description);
                // ★ 老结构（items/progress_limit/victory）从这一版起**不再写**：
                //   刷怪与奖励进 progress_events、胜利条件进顶层 victory、上限进 progress.limit。
                DiyJson.RemoveMember(block, "items");
                DiyJson.RemoveMember(block, "progress_limit");
                DiyJson.RemoveMember(block, "victory");
                keep.Add(ev.Id);
            }
            // 删掉不在清单里的事件块
            if (events.Members != null)
            {
                var drop = new List<string>();
                foreach (JMember m in events.Members)
                {
                    int id;
                    if (int.TryParse(m.Key, NumberStyles.Integer, CultureInfo.InvariantCulture, out id)
                        && !keep.Contains(id)) drop.Add(m.Key);
                }
                foreach (string k in drop) DiyJson.RemoveMember(events, k);
            }

            SyncProgressEvents(doc, d, managed);
            SyncVictory(doc, d);
            SyncLimit(doc, d);
            DiyJson.MarkDirty(diy);
        }

        /// <summary>
        /// ★ 获胜条件写到**顶层 `victory`**（服务端真读的那个键）。
        ///   编辑器里勾在哪条事件上不影响结果：一关就一个获胜条件，取第一条勾了的。
        ///   `targets` 服务端要的是**怪物表 id**，所以池内序号得翻成 `table`。
        /// </summary>
        private static void SyncVictory(LevelDoc doc, DiyData d)
        {
            bool kill = false, allBoss = false;
            var targets = new List<int>();
            foreach (DiyEvent ev in d.Events)
            {
                if (!ev.KillMonsters && !ev.KillAllBoss) continue;
                kill = ev.KillMonsters;
                allBoss = ev.KillAllBoss;
                foreach (int poolId in ev.VictoryTargets)
                {
                    PoolMonster m = Find(d.Pool, poolId);
                    if (m != null && m.Table > 0 && !targets.Contains(m.Table)) targets.Add(m.Table);
                }
                break;
            }
            if (!kill && !allBoss)
            {
                DiyJson.RemoveMember(doc.Root, "victory");
                return;
            }
            JNode vic = DiyJson.SetKind(DiyJson.EnsureMember(doc.Root, "victory"), JKind.Object);
            // ★ "击败所有BOSS单位" 服务端还没有这个 kind（第二批实现）—— 先把意图如实写出去
            DiyJson.SetString(DiyJson.EnsureMember(vic, "kind"),
                (allBoss && !kill) ? "kill_all_boss" : "kill_boss");
            JNode ta = DiyJson.SetKind(DiyJson.EnsureMember(vic, "targets"), JKind.Array);
            ta.Items.Clear();
            foreach (int t in targets)
            {
                var n = new JNode { Kind = JKind.Number, Parent = ta, Dirty = true,
                                    Num = t.ToString(CultureInfo.InvariantCulture) };
                ta.Items.Add(n);
            }
            if (ta.Items.Count == 0) DiyJson.RemoveMember(vic, "targets");
            else DiyJson.MarkDirty(ta);
        }

        /// <summary>
        /// ★ 总进度上限写到顶层 `progress.limit`。
        ///   （服务端现在读的是难度行 `ProgressLimit`；DIY 关卡要按自己这份走，
        ///     服务端第二批会优先认 `progress.limit`，见 `levels.progress`。）
        /// </summary>
        private static void SyncLimit(LevelDoc doc, DiyData d)
        {
            long lim = -1;
            foreach (DiyEvent ev in d.Events) if (ev.Limit >= 0) { lim = ev.Limit; break; }
            JNode prog = DiyJson.Member(doc.Root, "progress");
            if (lim < 0)
            {
                if (prog != null && prog.Kind == JKind.Object) DiyJson.RemoveMember(prog, "limit");
                return;
            }
            prog = DiyJson.SetKind(DiyJson.EnsureMember(doc.Root, "progress"), JKind.Object);
            DiyJson.SetInt(DiyJson.EnsureMember(prog, "limit"), lim);
        }

        /// <summary>池内序号被重排之后，棋盘放怪 / 进度条词 / 获胜条件里的指向一起改。</summary>
        private static void RemapRefs(DiyData d, Dictionary<int, int> remap)
        {
            foreach (BoardMonsterRef r in d.BoardMonsters)
            {
                int to;
                if (remap.TryGetValue(r.Pool, out to)) r.Pool = to;
            }
            foreach (DiyEvent ev in d.Events)
            {
                foreach (DiyProgressItem it in ev.Items)
                    foreach (DiyEffect x in it.Effects)
                        if (x.Kind == EffectKind.Spawn)
                        {
                            int to;
                            if (remap.TryGetValue(x.Pool, out to)) x.Pool = to;
                        }
                var targets = new List<int>();
                foreach (int t in ev.VictoryTargets)
                {
                    int to;
                    if (remap.TryGetValue(t, out to)) targets.Add(to);
                }
                ev.VictoryTargets = targets;
            }
        }

        /// <summary>
        /// 事件用它哪个键写进 `progress_events`：
        ///   设了触发点 ⇒ **官方事件 id**（服务端认得出什么时候播）；
        ///   没设 ⇒ **事件自己的 id**（唯一，保证两个"还没设触发点"的事件不会互相覆盖）。
        /// ★ 别用 `DiyTrigger.KeyOf` 直接当键：它对没设触发点的会返回条目编号，而读回来的
        ///   条目编号一律是 1 ⇒ 两条这样的事件会挤在同一个键上、内容被吞掉。
        /// </summary>
        public static int EventKeyOf(DiyEvent ev)
        {
            if (ev == null) return -1;
            foreach (DiyProgressItem it in ev.Items)
                if (it.TriggerEvent > 0) return it.TriggerEvent;
            if (ev.Id > 0) return ev.Id;
            foreach (DiyProgressItem it in ev.Items) if (it.N > 0) return it.N;
            return -1;
        }

        /// <summary>
        /// ★★ 2026-10-08 大改（用户拍板「扁平」）：事件**直接写成服务端读的那一份**。
        ///
        ///     progress_events.&lt;事件id&gt; = {
        ///         "trigger_progress": 3,          ← 只给编辑器看（服务端忽略不认识的键）
        ///         "spawn":  [ {table,node,count,hp,defense,attack,gold}, … ],
        ///         "attack": 1, "defense": 1,
        ///         "rewards": [ … ]
        ///     }
        ///
        /// ★★ 刷怪**一只怪一组行**（用户："有多少个就显示几行"）：
        ///   组内 = 这只怪在**本事件**下棋盘上放的每一格各一行（count 1），
        ///   加上"计划只数 − 已放格数"的差额一行 `node:-1`（随机空地）。
        ///   ⇒ 棋盘上放 3 格、只数填 5，就是 3 行带格 + 1 行随机补 2；
        ///     只数比格数少时以**格子为准**（放的格不会被吃掉）。
        ///   ★ 落点归属看 `BoardMonsterRef.Event`（0 = 开局组），不再"猜谁引用了它"。
        /// </summary>
        private static void SyncProgressEvents(LevelDoc doc, DiyData d, bool managed)
        {
            // ① 事件 → 键
            //   ★★ 2026-10-10（用户："给地图模板也给一些进度事件的模板 … 文案跟原版一样、
            //      但表现为 diy 的"）：**"设了触发点"的行也要算一条**。
            //
            //   原来这里只认"有效果"（effects == 0 就丢），于是**"只有触发进度、还没刷怪"的骨架行
            //   一存盘就被清掉** —— 空白模板预填的官方事件骨架会当场消失（实测：读回来
            //   `条目1 / p=0`，走一遍 `DiyPool.Write` 之后就变成 `条目0`、`progress_events` 整块被删）。
            //
            //   现在判据放宽成"**有内容**"：有任一效果 **或** `TriggerEvent` 是**表里认得的官方事件 id**
            //   （那种行本身就会触发官方那条事件，是货真价实的配置）。
            //   ★ 仍然丢掉"既没效果、又没官方事件 id"的**纯展示行** —— 那种留在 `diy.events` 里当名字就行，
            //     不占 `progress_events` 的键（原有口径没被推翻）。
            var keys = new List<int>();
            var byKey = new Dictionary<int, DiyEvent>();
            foreach (DiyEvent ev in d.Events)
            {
                int effects = 0;
                foreach (DiyProgressItem it in ev.Items) effects += it.Effects.Count;
                bool officialTrigger = false;
                foreach (DiyProgressItem it in ev.Items)
                    if (it.TriggerEvent > 0 && MapEventTable.Known(it.TriggerEvent)) { officialTrigger = true; break; }
                if (effects == 0 && !officialTrigger) continue;
                int key = EventKeyOf(ev);
                if (key <= 0 || byKey.ContainsKey(key)) continue;
                byKey[key] = ev;
                keys.Add(key);
            }

            JNode pe = DiyJson.Member(doc.Root, "progress_events");
            if (keys.Count == 0)
            {
                if (pe != null && pe.Kind == JKind.Object) DiyJson.RemoveMember(doc.Root, "progress_events");
                pe = null;
            }
            else
            {
                pe = DiyJson.SetKind(DiyJson.EnsureMember(doc.Root, "progress_events"), JKind.Object);
            }

            keys.Sort();
            var keepKeys = new List<string>();
            foreach (int n in keys)
            {
                string key = n.ToString(CultureInfo.InvariantCulture);
                keepKeys.Add(key);
                DiyEvent ev = byKey[n];
                JNode row = DiyJson.SetKind(DiyJson.EnsureMember(pe, key), JKind.Object);

                // 触发进度（给人看的提示）
                int trig = -1;
                foreach (DiyProgressItem it in ev.Items)
                    if (it.TriggerEvent == n) { trig = it.TriggerProgress; break; }
                if (trig < 0)
                    foreach (DiyProgressItem it in ev.Items) { trig = it.TriggerProgress; break; }
                if (trig >= 0) DiyJson.SetInt(DiyJson.EnsureMember(row, DiyTrigger.KeyProgress), trig);
                else DiyJson.RemoveMember(row, DiyTrigger.KeyProgress);

                // ② spawn：先按怪合只数（同一个事件里同一只怪只该有一条条词，这里再兜一次底）
                var pools = new List<int>();
                var wants = new List<int>();
                foreach (DiyProgressItem it in ev.Items)
                    foreach (DiyEffect x in it.Effects)
                    {
                        if (x.Kind != EffectKind.Spawn) continue;
                        if (Find(d.Pool, x.Pool) == null) continue;
                        int want = x.Count < 1 ? 1 : x.Count;
                        int idx = pools.IndexOf(x.Pool);
                        if (idx < 0) { pools.Add(x.Pool); wants.Add(want); }
                        else if (want > wants[idx]) wants[idx] = want;
                    }

                JNode arr = DiyJson.SetKind(DiyJson.EnsureMember(row, "spawn"), JKind.Array);
                arr.Items.Clear();
                for (int k = 0; k < pools.Count; k++)
                {
                    PoolMonster m = Find(d.Pool, pools[k]);
                    if (m == null) continue;
                    int placed = 0;
                    foreach (BoardMonsterRef r in d.BoardMonsters)
                    {
                        if (r.Pool != pools[k] || r.Event != n) continue;
                        // ★★ 2026-10-09：这一处放置**自己**的四个数值（不再借怪种那份）
                        SpawnNode(arr, m.Table, r.Node, 1, r.Hp, r.Defense, r.Attack, r.Gold);
                        placed++;
                    }
                    // ★ 差额（随机空地补几只）那一行用**怪种上的数值**当默认 —— 棋盘上没格子，
                    //   所以"这一只"没有自己的五项可写；口径写死在这里，别处别再猜。
                    int rest = placed == 0 ? wants[k] : (wants[k] - placed);
                    if (rest > 0) SpawnNode(arr, m, -1, rest);
                }
                // 棋盘上给这个事件放了、条词里却没有的怪 ⇒ 也得出行，不然那几格白放了
                foreach (BoardMonsterRef r in d.BoardMonsters)
                {
                    if (r.Event != n || pools.Contains(r.Pool)) continue;
                    PoolMonster m = Find(d.Pool, r.Pool);
                    if (m != null) SpawnNode(arr, m.Table, r.Node, 1, r.Hp, r.Defense, r.Attack, r.Gold);
                }
                // ★★ 2026-10-10（与服务端对称）：**没有刷怪也要把 `spawn` 键写出来**，写成空数组
                //   `"spawn": []` = "这一波**明确不刷**"。原来这里是把键摘掉 ⇒ 服务端看见"没写"
                //   就去回落官方那条事件的效果（还会打"组表没解出来"的 WARN），于是编辑器里
                //   显示"刷怪 0 只"、进游戏却冒出怪来。有行的时候照旧一行不少、一个字节不改。
                DiyJson.MarkDirty(arr);

                // ③ 场上所有怪加攻 / 加防（服务端一个键一个数 ⇒ 只留第一条，老口径不变）
                bool hasA = false, hasD = false;
                long av = 0, dv = 0;
                foreach (DiyProgressItem it in ev.Items)
                    foreach (DiyEffect x in it.Effects)
                    {
                        if (x.Kind == EffectKind.MonsterAttack && !hasA) { av = x.N; hasA = true; }
                        if (x.Kind == EffectKind.MonsterDefense && !hasD) { dv = x.N; hasD = true; }
                    }
                if (hasA) DiyJson.SetInt(DiyJson.EnsureMember(row, "attack"), av);
                else DiyJson.RemoveMember(row, "attack");
                if (hasD) DiyJson.SetInt(DiyJson.EnsureMember(row, "defense"), dv);
                else DiyJson.RemoveMember(row, "defense");

                // ④ 奖励（发一次筹码 / 发牌 / 发钱 / 加扣血）
                JNode rw = DiyJson.SetKind(DiyJson.EnsureMember(row, "rewards"), JKind.Array);
                rw.Items.Clear();
                foreach (DiyProgressItem it in ev.Items)
                    foreach (DiyEffect x in it.Effects)
                        if (x.IsReward) rw.Items.Add(RewardNode(rw, x));
                if (rw.Items.Count == 0) DiyJson.RemoveMember(row, "rewards");
                else DiyJson.MarkDirty(rw);
            }
            // 上一次同步留下的、现在没人指着的那些键：整行摘掉（别留空行骗人）
            if (pe != null && pe.Members != null)
            {
                var drop = new List<string>();
                foreach (JMember m in pe.Members) if (!keepKeys.Contains(m.Key)) drop.Add(m.Key);
                foreach (string k in drop) DiyJson.RemoveMember(pe, k);
            }

            SyncStartMonsters(doc, d, managed);
        }

        /// <summary>
        /// ★ 开局就在场上的怪 = `BoardMonsterRef.Event == 0` 的那些格子（+ `StartRandom` 里
        ///   "随机空地补几只"）。
        ///   ★ `managed` = 这份文件由这套编辑器负责写这个键（`Write` 开头算好传进来）：
        ///     是 ⇒ **完全按编辑器里的状态重写**，包括"开局组一只都没有" ⇒ 写 **`[]`**（键在、
        ///     空数组 = 服务端**明确不刷**）；不是（刚打开的成品模板 / 官方关卡）⇒ 一个字节别动。
        ///   ★★ 2026-10-10 口径变更：以前"开局组空"是把 `start_monsters` **整个摘掉**，
        ///     而官方语义是"**没写 = 回落 `battle.INIT_MONSTERS`（那张图官方那批开局怪）**"
        ///     ⇒ 编辑器显示 0 只、进游戏却刷官方那一排（用户实机反馈）。现在改成显式空数组。
        /// </summary>
        private static void SyncStartMonsters(LevelDoc doc, DiyData d, bool managed)
        {
            if (!managed) return;

            JNode arr = DiyJson.SetKind(DiyJson.EnsureMember(doc.Root, "start_monsters"), JKind.Array);
            arr.Items.Clear();
            foreach (BoardMonsterRef r in d.BoardMonsters)
            {
                if (r.Event != 0) continue;
                PoolMonster m = Find(d.Pool, r.Pool);
                if (m == null) continue;
                // ★★ 2026-10-09：写这一处放置**自己**的四项（同一格两条同种怪也能各是各的）
                SpawnNode(arr, m.Table, r.Node, 1, r.Hp, r.Defense, r.Attack, r.Gold);
            }
            foreach (StartSpawn s in d.StartRandom)
            {
                PoolMonster m = Find(d.Pool, s.Pool);
                if (m == null || s.Count < 1) continue;
                // ★ 随机补那一行没有"格子" ⇒ 用**怪种**那份数值（口径与事件里的随机补一致）
                SpawnNode(arr, m, -1, s.Count);
            }
            // ★★ 一行都没有 ⇒ 保持成 `[]`（**键必须在**，不摘）—— 见上面那段口径变更。
            DiyJson.MarkDirty(arr);
        }
        /// <summary>往 spawn 数组里塞一行（table + node + count + 四个数值）。</summary>
        private static void SpawnNode(JNode arr, PoolMonster m, int node, int count)
        {
            SpawnNode(arr, m.Table, node, count, m.Hp, m.Defense, m.Attack, m.Gold);
        }

        /// <summary>
        /// ★★ 2026-10-09：数值**由调用方给** —— 带格子的行写的是"这一处放置自己的那份"
        ///   （`BoardMonsterRef` 的四项），随机补的那一行才写怪种那份。
        ///   `counter` **不写进这一行**：服务端这几行的官方形状就是 7 个键，别往里加东西
        ///   （每一处放置的"是否反击"记在 `diy.board_monsters[].counter`）。
        /// </summary>
        private static void SpawnNode(JNode arr, int table, int node, int count,
                                      long hp, long defense, long attack, long gold)
        {
            JNode o = NewObj(arr);
            AddNum(o, "table", table);
            AddNum(o, "node", node);
            AddNum(o, "count", count < 1 ? 1 : count);
            AddNum(o, "hp", hp);
            AddNum(o, "defense", defense);
            AddNum(o, "attack", attack);
            AddNum(o, "gold", gold);
            arr.Items.Add(o);
        }

        /// <summary>一条奖励 → `rewards[]` 里的一行（服务端第二批按这个读）。</summary>
        private static JNode RewardNode(JNode parent, DiyEffect x)
        {
            JNode o = NewObj(parent);
            switch (x.Kind)
            {
                case EffectKind.Relic:
                    AddStr(o, "kind", "relic");
                    AddNum(o, "choice", x.Choice < 1 ? 1 : x.Choice);
                    break;
                case EffectKind.Card:
                    {
                        AddStr(o, "kind", "card");
                        JNode ids = DiyJson.SetKind(DiyJson.EnsureMember(o, "ids"), JKind.Array);
                        ids.Items.Clear();
                        var n = new JNode { Kind = JKind.Number, Parent = ids, Dirty = true,
                                            Num = x.Card.ToString(CultureInfo.InvariantCulture) };
                        ids.Items.Add(n);
                        if (x.Count > 1) AddNum(o, "count", x.Count);
                        break;
                    }
                case EffectKind.Gold:
                    AddStr(o, "kind", "gold");
                    AddNum(o, "amount", x.N);
                    break;
                default:
                    AddStr(o, "kind", "hp");
                    AddNum(o, "amount", x.N);
                    break;
            }
            AddStr(o, "to", x.To == DiyEffect.ToActor ? DiyEffect.ToActor : DiyEffect.ToTeam);
            return o;
        }

        // ------------------------------------------------------------------ 新建
        /// <summary>
        /// ★ 新建一份 DIY 关卡时把 DIY 侧数据铺上：空池子 + 空棋盘放怪 +
        /// **`DIY进度` 这一条占位事件**（内容包预留位）。
        /// </summary>
        public static DiyData NewData()
        {
            var d = new DiyData();
            var ev = new DiyEvent();
            ev.Id = PlaceholderEventId;
            ev.Name = PlaceholderEventName;
            ev.Description = PlaceholderEventDesc;
            d.Events.Add(ev);
            return d;
        }

        // ------------------------------------------------------------------ 池子操作
        /// <summary>往池子里加一只怪（四项数值 + **会不会反击**都从 `Monster.bin` 取默认值）。</summary>
        public static PoolMonster MakePool(DiyData d, int tableId)
        {
            var m = new PoolMonster();
            m.Id = d.NextPoolId();
            m.Table = tableId;
            MonsterRow row = GameTables.Monster(tableId);
            if (row != null)
            {
                m.Hp = row.Blood;
                m.Defense = row.Defense;
                m.Attack = row.Attack;
                m.Gold = row.Gold;
                // ★★ 2026-10-09：以前这里写死 false ⇒ 界面上处处"不反击"（用户实机反馈的 bug）。
                //   怪物表的字段 22 = can_counter，服务端读的就是它。
                m.Counter = row.CanCounter;
            }
            d.Pool.Add(m);
            return m;
        }

        /// <summary>
        /// 这一只怪**表里**会不会反击（`Monster.bin` 字段 22）。
        /// ★ 用在哪：老文件里 `diy.board_monsters[].counter` 缺键 / 怪种那行缺 `counter` 时回落用它
        ///   —— 不然"缺键 = false"会把 1067/1068/1069/1070 这些**本来就会反击**的怪读成不反击。
        /// </summary>
        public static bool TableCounter(int table)
        {
            MonsterRow row = GameTables.Monster(table);
            return row != null && row.CanCounter;
        }

        /// <summary>
        /// ★★ 2026-10-09（用户要的「把这套数值应用到全体相同怪」）：把这一套数值写进
        /// **这个怪种在棋盘上的每一处放置**（跨格、跨事件、含开局组那批），并**同步怪种默认值**
        /// （这样"新建放置 / 随机补那一行"也跟着变）。返回改了几处放置（不含随机那一行）。
        /// </summary>
        public static int ApplyToSamePool(DiyData d, int poolId,
                                          long hp, long defense, long attack, long gold, bool counter)
        {
            if (d == null || poolId <= 0) return 0;
            int n = 0;
            foreach (BoardMonsterRef r in d.BoardMonsters)
            {
                if (r.Pool != poolId) continue;
                r.Hp = hp; r.Defense = defense; r.Attack = attack; r.Gold = gold; r.Counter = counter;
                n++;
            }
            PoolMonster m = Find(d.Pool, poolId);
            if (m != null)
            {
                m.Hp = hp; m.Defense = defense; m.Attack = attack; m.Gold = gold; m.Counter = counter;
            }
            return n;
        }

        /// <summary>这一格上放的**第一条**（老调用点用；一格多只请用 `AllAtNode` / `RefsAtNode`）。</summary>
        public static PoolMonster AtNode(DiyData d, int node)
        {
            foreach (BoardMonsterRef r in d.BoardMonsters)
                if (r.Node == node) return Find(d.Pool, r.Pool);
            return null;
        }

        /// <summary>这一格上的**全部**怪（按落盘顺序；★ 一格多只就是靠它）。</summary>
        public static List<PoolMonster> AllAtNode(DiyData d, int node)
        {
            var got = new List<PoolMonster>();
            foreach (BoardMonsterRef r in d.BoardMonsters)
                if (r.Node == node)
                {
                    PoolMonster m = Find(d.Pool, r.Pool);
                    if (m != null) got.Add(m);
                }
            return got;
        }

        /// <summary>这一格上的**全部**引用条目（要"单独删第几条"时用它，因为同款怪可能放两只）。</summary>
        public static List<BoardMonsterRef> RefsAtNode(DiyData d, int node)
        {
            var got = new List<BoardMonsterRef>();
            foreach (BoardMonsterRef r in d.BoardMonsters) if (r.Node == node) got.Add(r);
            return got;
        }

        /// <summary>
        /// ★ 2026-10-08："这一格上**这一组**（事件 / 开局组）放的怪" —— 棋盘页切换进度之后
        ///   画怪、右键菜单都用它。`int.MinValue` = 不看组（等于 `RefsAtNode(d, node)`）。
        /// </summary>
        public static List<BoardMonsterRef> RefsAtNode(DiyData d, int node, int eventId)
        {
            if (eventId == int.MinValue) return RefsAtNode(d, node);
            var got = new List<BoardMonsterRef>();
            foreach (BoardMonsterRef r in d.BoardMonsters)
                if (r.Node == node && r.Event == eventId) got.Add(r);
            return got;
        }

        /// <summary>这一格上这一组放的怪（池子对象版；`int.MinValue` = 不看组）。</summary>
        public static List<PoolMonster> AllAtNode(DiyData d, int node, int eventId)
        {
            var got = new List<PoolMonster>();
            foreach (BoardMonsterRef r in RefsAtNode(d, node, eventId))
            {
                PoolMonster m = Find(d.Pool, r.Pool);
                if (m != null) got.Add(m);
            }
            return got;
        }

        /// <summary>
        /// 把一只池内怪**追加**到棋盘格子上（★ 用户要的"同一个格子里面加多个怪物"）。
        ///   ★ 不碰这一格原来已有的怪 —— 要"换掉"请用 `PlaceAt`。
        /// </summary>
        public static void AddAt(DiyData d, int node, int poolId) { AddAt(d, node, poolId, 0); }

        /// <summary>
        /// ★ 2026-10-08：往这一格加一只怪，并记下它是**哪个事件**的
        ///   （`eventId` = 0 ⇒ 开局就在场上 = `start_monsters`）。
        ///   棋盘页的"切换进度 → 放置怪物"就靠它。
        /// ★★ 2026-10-09：新加的这**一处放置**从怪种（池子里那一条）带一份五项数值当种子 ——
        ///   之后它就是独立的一份了（改它不会动怪种，也不会动别处放置）。
        /// </summary>
        public static void AddAt(DiyData d, int node, int poolId, int eventId)
        {
            if (d == null || node < 0 || poolId <= 0) return;
            var r = new BoardMonsterRef();
            r.Node = node;
            r.Pool = poolId;
            r.Event = eventId;
            r.SeedFrom(Find(d.Pool, poolId));
            d.BoardMonsters.Add(r);
        }

        /// <summary>
        /// 把这一格上的**某一只**拿掉（同款怪放了两只时只拿掉一条）。
        /// ★★ 池子里的那一条**留着**（用户规格：删格上的怪 ≠ 从池子里拿掉；
        ///    没被任何格引用的怪交给怪物页的「未配置」那一栏显示）。
        /// </summary>
        public static bool RemoveOneFromNode(DiyData d, int node, int poolId) { return RemoveOneFromNode(d, node, poolId, int.MinValue); }

        /// <summary>
        /// 把这一格上的某一只拿掉；`eventId` 给了就只在这一组里找
        /// （`int.MinValue` = 不挑组，老调用点的行为不变）。
        /// </summary>
        public static bool RemoveOneFromNode(DiyData d, int node, int poolId, int eventId)
        {
            for (int k = 0; k < d.BoardMonsters.Count; k++)
            {
                BoardMonsterRef r = d.BoardMonsters[k];
                if (r.Node != node || r.Pool != poolId) continue;
                if (eventId != int.MinValue && r.Event != eventId) continue;
                d.BoardMonsters.RemoveAt(k);
                return true;
            }
            return false;
        }

        /// <summary>把这一格上的怪全拿掉（池子留着）。返回拿掉几条。</summary>
        public static int ClearNode(DiyData d, int node) { return ClearNode(d, node, int.MinValue); }

        /// <summary>把这一格上**某一组**（`eventId`）的怪全拿掉；`int.MinValue` = 不看组。</summary>
        public static int ClearNode(DiyData d, int node, int eventId)
        {
            int n = 0;
            for (int k = d.BoardMonsters.Count - 1; k >= 0; k--)
            {
                BoardMonsterRef r = d.BoardMonsters[k];
                if (r.Node != node) continue;
                if (eventId != int.MinValue && r.Event != eventId) continue;
                d.BoardMonsters.RemoveAt(k);
                n++;
            }
            return n;
        }

        public static void PlaceAt(DiyData d, int node, int poolId) { PlaceAt(d, node, poolId, 0); }

        /// <summary>把一只池内怪放到棋盘格子上（这一格已经有的先撤掉）。追加请用 `AddAt`。</summary>
        public static void PlaceAt(DiyData d, int node, int poolId, int eventId)
        {
            ClearNode(d, node);
            AddAt(d, node, poolId, eventId);
        }

        /// <summary>这一格放了几只。</summary>
        public static int CountAtNode(DiyData d, int node)
        {
            int n = 0;
            foreach (BoardMonsterRef r in d.BoardMonsters) if (r.Node == node) n++;
            return n;
        }

        /// <summary>棋盘上一共放了几只（★ 一格多只要按条数算，不是按格数）。</summary>
        public static int BoardCount(DiyData d)
        {
            return d == null ? 0 : d.BoardMonsters.Count;
        }

        /// <summary>把某只怪从池子里拿掉（棋盘 / 条词 / 获胜条件里指着它的地方一起清）。</summary>
        public static void DropFromPool(DiyData d, int poolId)
        {
            for (int k = d.Pool.Count - 1; k >= 0; k--) if (d.Pool[k].Id == poolId) d.Pool.RemoveAt(k);
            for (int k = d.BoardMonsters.Count - 1; k >= 0; k--)
                if (d.BoardMonsters[k].Pool == poolId) d.BoardMonsters.RemoveAt(k);
            foreach (DiyEvent ev in d.Events)
            {
                foreach (DiyProgressItem it in ev.Items)
                    it.Effects.RemoveAll(delegate (DiyEffect x) { return x.Kind == EffectKind.Spawn && x.Pool == poolId; });
                ev.VictoryTargets.RemoveAll(delegate (int t) { return t == poolId; });
            }
        }

        // ------------------------------------------------------------------ 校验（规格里那两条）
        /// <summary>
        /// ★ 校验①：**缺少四个角色的出生点**。返回一行行给人看的结果（第一条是结论）。
        /// </summary>
        public static List<string> CheckSpawns(LevelDoc doc)
        {
            var lines = new List<string>();
            if (doc == null || doc.Root == null) { lines.Add("还没打开关卡。"); return lines; }
            if (doc.Board == null || !doc.Board.Ok) { lines.Add("这块棋盘读不出来 ⇒ 出生点没法判。"); return lines; }

            List<int> list = doc.EffectiveStarts();
            var bad = new bool[5];
            var note = new string[5];
            for (int k = 0; k < 4; k++)
            {
                if (k >= list.Count || list[k] < 0) { bad[k] = true; note[k] = "没指定（这一号空着）"; continue; }
                bool dup = false;
                for (int j = 0; j < k; j++) if (j < list.Count && list[j] == list[k]) dup = true;
                if (dup) { bad[k] = true; note[k] = "跟前面的角色指到了同一格（格 " + list[k] + "）"; continue; }
                if (doc.Board.Get(list[k]) == null)
                { bad[k] = true; note[k] = "指到了棋盘上没有的格 " + list[k]; continue; }
                note[k] = "格 " + list[k];
            }
            int extra = list.Count > 4 ? list.Count - 4 : 0;
            int badCount = 0;
            for (int k = 0; k < 4; k++) if (bad[k]) badCount++;

            if (badCount == 0 && extra == 0)
            {
                lines.Add("四个角色的出生点都在 ✓");
                var where = new List<string>();
                for (int k = 0; k < 4; k++) where.Add("角色" + (k + 1) + " = 格 " + list[k]);
                lines.Add(string.Join(" · ", where.ToArray()));
                return lines;
            }

            lines.Add("★ 缺少四个角色的出生点 ✗"
                + (badCount > 0 ? "（缺 " + badCount + " 个）" : "")
                + (extra > 0 ? "（还多写了 " + extra + " 个起点）" : ""));
            for (int k = 0; k < 4; k++)
                lines.Add("  角色 " + (k + 1) + "：" + (bad[k] ? "★ " + note[k] : note[k]));
            if (extra > 0) lines.Add("  ★ 起点一共写了 " + list.Count + " 个 —— 一局只有四个角色。");
            lines.Add("  ⇒ 去棋盘页右键格子 →「设为起点 1 / 2 / 3 / 4」。");
            return lines;
        }

        /// <summary>
        /// ★ 校验②：**没挂任何事件的怪**。
        ///   ★ 2026-10-08 起这不是错误：它们会被写进 `start_monsters`（**开局就在场上**），
        ///     服务端照这份配置一开局就把它们摆上去。所以只是"告诉你它们会怎么出场"。
        /// </summary>
        public static List<string> CheckMonsters(DiyData d)
        {
            var lines = new List<string>();
            if (d == null || d.Pool.Count == 0)
            {
                lines.Add("怪物池是空的（棋盘上还没放过怪）⇒ 没有开局怪。");
                return lines;
            }
            var bad = new List<PoolMonster>();
            foreach (PoolMonster m in d.Pool) if (m.EventId == 0) bad.Add(m);
            if (bad.Count == 0)
            {
                lines.Add("怪物池 " + d.Pool.Count + " 只，全都挂在事件上 ✓（开局场上没有怪）");
                return lines;
            }
            lines.Add("开局就在场上的怪 " + bad.Count + " 只（池子共 " + d.Pool.Count + " 只）"
                + " —— 写进 start_monsters，一开局就站在棋盘上。");
            foreach (PoolMonster m in bad) lines.Add("  " + m.Display + " → 没有任何事件指着它");
            lines.Add("  ⇒ 想让它改成「某个事件刷出来」，去进度页给那条事件加一条「新增怪物」。");
            return lines;
        }

        // ------------------------------------------------------------------ 小工具
        private static JNode NewObj(JNode parent)
        {
            var o = new JNode
            {
                Kind = JKind.Object, Parent = parent, Dirty = true, WasInline = true,
                BraceSpace = true, CommaSpace = true, Members = new List<JMember>(),
            };
            return o;
        }

        /// <summary>一个空数组节点（条词清单用）。</summary>
        private static JNode NewArr(JNode parent)
        {
            return new JNode { Kind = JKind.Array, Parent = parent, Dirty = true, Items = new List<JNode>() };
        }

        private static void AddNum(JNode o, string key, long v)
        {
            var n = new JNode { Kind = JKind.Number, Parent = o, Dirty = true, Num = v.ToString(CultureInfo.InvariantCulture) };
            o.Members.Add(new JMember { Key = key, Value = n });
        }

        private static JNode EffectNode(DiyEffect x, JNode parent)
        {
            JNode o = NewObj(parent);
            AddStr(o, "type", DiyEffect.TypeOf(x.Kind));
            switch (x.Kind)
            {
                case EffectKind.Card: AddNum(o, "card", x.Card); break;
                case EffectKind.Hp:
                case EffectKind.Gold:
                case EffectKind.MonsterAttack:
                case EffectKind.MonsterDefense: AddNum(o, "n", x.N); break;
                case EffectKind.Spawn: AddNum(o, "monster", x.Pool); break;
            }
            // ★ 2026-10-08 大改：数量 / 给谁 / 筹码张数（默认值不写，别留空键骗人）
            if (x.Kind == EffectKind.Spawn && x.Count > 1) AddNum(o, "count", x.Count);
            if (x.IsReward)
            {
                AddStr(o, "to", x.To == DiyEffect.ToActor ? DiyEffect.ToActor : DiyEffect.ToTeam);
                if (x.Kind == EffectKind.Relic) AddNum(o, "choice", x.Choice);
                if (x.Kind == EffectKind.Card && x.Count > 1) AddNum(o, "count", x.Count);
                if (x.Kind == EffectKind.Hp && x.Count > 1) AddNum(o, "count", x.Count);
            }
            return o;
        }

        private static void AddStr(JNode o, string key, string s)
        {
            var n = new JNode { Kind = JKind.String, Parent = o, Dirty = true, Str = s };
            o.Members.Add(new JMember { Key = key, Value = n });
        }
    }
}
