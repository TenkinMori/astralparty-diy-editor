using System;
using System.Collections.Generic;
using System.Globalization;

namespace DiyEditor
{
    public enum LintLevel { Ok, Info, Warn, Bad }

    public sealed class LintFinding
    {
        public LintLevel Level;
        public string Where = "";    // 在文件的哪个位置（人类可读路径）
        public string What = "";     // 发生了什么
        public string Why = "";      // 为什么（引用实现位置）
        public string Fix = "";      // 怎么办

        public LintFinding(LintLevel level, string where, string what, string why, string fix)
        {
            Level = level; Where = where; What = what; Why = why; Fix = fix;
        }

        public string Tag
        {
            get
            {
                switch (Level)
                {
                    case LintLevel.Bad: return "会被丢 / 会出错";
                    case LintLevel.Warn: return "⚠ 要留意";
                    case LintLevel.Info: return "说明";
                    default: return "OK";
                }
            }
        }
    }

    /// <summary>
    /// ★ 这个编辑器的**关键价值**：把"服务端读不读这个字段"逐条摊开。
    ///
    /// 判据全部来自只读侦察（2026-10-03，未做任何修改）：
    ///   * 白名单：`server\astralnext\levels.py` 的 `_normalize`（L117-193）
    ///   * 刷怪行：同文件 `_monster`（L71-91）/ `_spawn`（L94-114）
    ///   * 棋盘：`server\astralnext\board.py`（L66-121）
    ///
    /// ★ 三类问题分开报：
    ///   ① **服务端不会读的字段**（静默丢掉 —— 不报错、不警告，最容易白干）；
    ///   ② **类型/形状对不上**导致整块被丢（progress_events 写成数组这种经典坑）；
    ///   ③ **服务端根本不校验**但会让关卡玩不起来的（怪站在不存在的格子上、
    ///      胜利条件指向一只永远不会出现的怪）。
    /// </summary>
    public static class LevelLint
    {
        public static List<LintFinding> Run(JNode root, BoardModel board, out int readCount, out int droppedCount)
        {
            return Run(root, board, null, "", null, null, out readCount, out droppedCount);
        }

        /// <summary>
        /// 同上，另外把**生效的起点顺序**（起点 1~4 各自的格子 id）带进来查一遍。
        /// ★ `starts` 给 null = 不去读 `diy.starts`（老调用方 / 自测都走这条，结论不变）。
        /// </summary>
        public static List<LintFinding> Run(JNode root, BoardModel board, List<int> starts,
            string startsSource, out int readCount, out int droppedCount)
        {
            return Run(root, board, starts, startsSource, null, null, out readCount, out droppedCount);
        }

        /// <summary>
        /// 同上，再带上"换过功能的格子"（`nodes[i].diy.landType`）。
        /// ★ `changedCells` 给 null = 不查这块（老调用方结论不变）。
        /// </summary>
        public static List<LintFinding> Run(JNode root, BoardModel board, List<int> starts,
            string startsSource, Dictionary<int, int> changedCells, List<string> changedLines,
            out int readCount, out int droppedCount)
        {
            var list = new List<LintFinding>();
            readCount = 0;
            droppedCount = 0;

            if (root == null)
            {
                list.Add(new LintFinding(LintLevel.Bad, "(文件)", "没有内容", "解析结果是空的", "确认这个文件是关卡 JSON"));
                return list;
            }
            if (root.Kind != JKind.Object)
            {
                list.Add(new LintFinding(LintLevel.Bad, "(根)", "关卡的根必须是一个对象 { … }",
                    "levels.py:119 `if not isinstance(raw, dict): return None` —— 不是对象就整体作废",
                    "把最外层改成 { … }"));
                return list;
            }

            var seen = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (JMember m in root.Members)
            {
                if (seen.ContainsKey(m.Key)) seen[m.Key]++; else seen[m.Key] = 1;
            }
            foreach (var kv in seen)
            {
                if (kv.Value > 1)
                    list.Add(new LintFinding(LintLevel.Warn, kv.Key,
                        "这个键在文件里出现了 " + kv.Value + " 次",
                        "JSON 允许重复键，Python 的 json.load 取**最后一个**",
                        "删掉多余的那几个，只留一份"));
            }

            // ---------------------------------------------------------- 顶层白名单
            foreach (JMember m in root.Members)
            {
                bool known = Array.IndexOf(LevelSchema.TopKeys, m.Key) >= 0;
                if (known) { readCount++; continue; }
                droppedCount++;
                string why = LevelSchema.DropReason("", m.Key);
                list.Add(new LintFinding(LintLevel.Warn, m.Key,
                    "服务端**不会读**这个字段", why,
                    "留着它不影响加载（导出时原样保留），但它不会改变关卡。要生效请改用白名单里的键（见页面② 的注释列）"));
            }

            // ---------------------------------------------------------- 逐块检查
            CheckRoundAward(root, list, ref readCount, ref droppedCount);
            CheckProgress(root, list, ref readCount, ref droppedCount);
            CheckVictory(root, list, ref readCount, ref droppedCount);
            CheckStartMonsters(root, board, list, ref readCount, ref droppedCount);
            CheckProgressEvents(root, board, list, ref readCount, ref droppedCount);
            CheckSettlement(root, list, ref readCount, ref droppedCount);
            CheckMastermind(root, list, ref readCount, ref droppedCount);
            CheckVictoryTargetsReachable(root, list);
            CheckRounds(root, list);
            if (starts != null) CheckStarts(board, starts, startsSource, list);
            if (changedCells != null) CheckChangedCells(changedCells, changedLines, list);

            // ★ 空白玩法模板的友好提示（用户 2026-10-10：「所有有官方棋盘的图都能新建关卡」）：
            //   那几张图本机**没有官方玩法数据**，所以给的是"空白玩法 + 官方棋盘"。
            //   这里只给一条 Info（不报错）——提醒作者"事件/胜负还没写"，别让它看着像坏了。
            if (readCount == 0)
            {
                list.Add(new LintFinding(LintLevel.Info, "(空白)",
                    "这份关卡还没有任何事件 / 内容",
                    "它可能是从「空白玩法 + 官方棋盘」模板新建的：棋盘与起点都在，事件 / 刷怪 / 胜负由你自己填。",
                    "到「进度 / 事件」页加事件；到「棋盘」页右键格子加怪"));
            }

            // ★★ 有事件、但事件里一只怪都没排 ⇒ Info（用户 2026-10-10：「有事件但没有刷怪」）。
            //   ★ 口径 2026-10-10 晚改成"**明确不刷**"：空白模板写的是 `"spawn": []`（键在、空数组）
            //     —— 服务端看到它就不刷这一波（也**不再**回落官方那条事件的效果）。
            //     所以措辞不能再说"回落默认/还是空的"，要说清"这就是明确不刷 + 怎么改成刷"。
            if (readCount > 0)
            {
                bool anySpawn = false;
                var empties = new List<string>();
                try
                {
                    JNode peAll = DiyJson.Member(root, "progress_events");
                    if (peAll != null && peAll.Kind == JKind.Object && peAll.Members != null)
                        foreach (JMember m in peAll.Members)
                        {
                            JNode sp = DiyJson.Member(m.Value, "spawn");
                            bool has = sp != null && sp.Kind == JKind.Array && sp.Items != null && sp.Items.Count > 0;
                            if (has) anySpawn = true;
                            else empties.Add(m.Key);
                        }
                }
                catch (Exception) { }
                if (!anySpawn)
                {
                    string ids = empties.Count == 0 ? ""
                        : ("（" + empties.Count + " 条：" + string.Join(" / ", empties.ToArray()) + "）");
                    list.Add(new LintFinding(LintLevel.Info, "(刷怪)",
                        "这条事件**明确不刷怪**（`spawn: []`）" + ids,
                        "键在、值是空数组 ⇒ 服务端**这一波一只都不刷**（不是回落官方那条事件的效果）。"
                        + "这份多半是从「不刷怪的空白玩法」模板新建的：官方文案 / 触发进度都在，怪要自己加。",
                        "要刷就在这条事件里加怪物：到「进度 / 事件」页选中它 →「＋ 新增怪物」；"
                        + "或直接在棋盘上右键格子放怪。★ 想让**开局**就用官方那批，去「怪物」页点「用官方开局怪」"));
                }
            }

            // 棋盘本身
            if (board == null || !board.Ok)
            {
                list.Add(new LintFinding(LintLevel.Info, "棋盘",
                    "这一份关卡没有可用的棋盘图",
                    board == null ? "没有加载棋盘" : board.Note,
                    "把对应的 boards\\<后缀>.json 拷进「棋盘缓存」，或在这一页手动指定棋盘目录"));
            }
            else
            {
                list.Add(new LintFinding(LintLevel.Ok, "棋盘",
                    "棋盘图可用：" + System.IO.Path.GetFileName(board.Path) + "（" + board.Nodes.Count + " 格）",
                    board.Note, "落点是否存在的检查已经启用"));
            }

            // 排序：问题大的排前面
            list.Sort(delegate (LintFinding a, LintFinding b) { return ((int)b.Level).CompareTo((int)a.Level); });
            return list;
        }

        // ==================================================================== 各块
        // ================================================================ ★ 起点 1~4
        /// <summary>
        /// 起点校验：**必须恰好 4 个** · **编号不重复** · 格子得真在棋盘上。
        ///
        /// ★ 「第几号起点」在数据里的依据（查清了）：游戏里**座位号 == 起点格的 serial**
        ///   （`board.py:304 start_nodes` / `board.py:211 is_own_born`）；
        ///   官方六张画板实测 007/008/010/016/023 都是 serial 0/1/2/3 各一格，
        ///   020 只有 3 个（serial 0/1/2）。编辑器把 serial 0..3 显示成 起点1..起点4；
        ///   用户在棋盘上右键指定过就落 `diy.starts`，那份说了算。
        ///
        /// ★ 这一块**服务端不读**（`diy` 块结构上就不在 levels.py 的白名单里），
        ///   所以结论是给用户自己看的，不是"服务端会丢"。
        /// </summary>
        private static void CheckStarts(BoardModel board, List<int> starts, string source,
            List<LintFinding> list)
        {
            const string where = "起点（diy.starts / 官方 serial）";
            const string why = "服务端目前不读 diy.starts（diy 块不在 levels.py 白名单里）；"
                             + "编号的依据是起点格的 serial = 座位号（board.py start_nodes / is_own_born）";
            const string how = "在【棋盘】页右键格子 →「设为起点1~4 / 取消起点」；四个都要有、号不能重";

            if (board == null || !board.Ok)
            {
                list.Add(new LintFinding(LintLevel.Info, where,
                    "没有可用棋盘 ⇒ 起点无从谈起", why, how));
                return;
            }

            var notes = new List<string>();
            if (starts.Count == 0)
                notes.Add("一个起点都没有：四个玩家会没地方出生");
            if (starts.Count != 4)
                notes.Add("起点 " + starts.Count + " 个，要求恰好 4 个");

            var dup = new List<int>();
            var vague = new List<int>();
            var gone = new List<int>();
            for (int k = 0; k < starts.Count; k++)
            {
                int id = starts[k];
                if (id < 0) { vague.Add(k + 1); continue; }
                for (int j = 0; j < k; j++) if (starts[j] == id) dup.Add(k + 1);
                if (board.Get(id) == null) gone.Add(k + 1);
            }
            if (dup.Count > 0) notes.Add("编号重复：起点 " + Join(dup) + " 和前面的号指到了同一格");
            if (vague.Count > 0) notes.Add("这几号没指定：起点 " + Join(vague) + " 是空的（diy.starts 里占着 -1）");
            if (gone.Count > 0) notes.Add("这几号的格子不在棋盘上：起点 " + Join(gone));

            if (notes.Count > 0)
            {
                list.Add(new LintFinding(LintLevel.Bad, where,
                    string.Join("；", notes.ToArray()), why, how));
                return;
            }

            var who = new List<string>();
            for (int k = 0; k < starts.Count; k++) who.Add("起点" + (k + 1) + "=格" + starts[k]);
            list.Add(new LintFinding(LintLevel.Ok, where,
                "起点 4 个 ✓ 编号 1~4 不重复、格子都在棋盘上（" + string.Join("，", who.ToArray()) + "）",
                why + "。来源：" + (source.Length > 0 ? source : "（未说明）"), how));
        }

        private static string Join(List<int> xs)
        {
            var a = new string[xs.Count];
            for (int k = 0; k < xs.Count; k++) a[k] = xs[k].ToString(System.Globalization.CultureInfo.InvariantCulture);
            return string.Join("、", a);
        }

        // ================================================================ ★ 换过功能的格子
        /// <summary>
        /// 换过的格子（棋盘文件 `nodes[i].diy.landType`）。
        ///
        /// ★ 这一块**服务端也不读**：`nodes[i].diy` 是编辑器加的，官方只认 `nodes[i].landType`。
        ///   ⇒ 结论是给用户自己看的：改了哪几格、有没有写出界。
        /// </summary>
        private static void CheckChangedCells(Dictionary<int, int> changed,
            List<string> changedLines, List<LintFinding> list)
        {
            const string where = "格子功能（nodes[].diy.landType）";
            const string why = "服务端目前只认官方的 nodes[i].landType；nodes[i].diy 是编辑器加的"
                             + "（所以「还原成官方」永远做得到 —— 删掉那个子键就行）";
            const string how = "在【棋盘】页左键点一格 → 右侧「格子属性」换；右键格子也能换常用类型；「还原成官方」撤销";

            if (changed.Count == 0)
            {
                list.Add(new LintFinding(LintLevel.Ok, where,
                    "这块棋盘还是官方原样，一个格子都没改", why, how));
                return;
            }

            var bad = new List<int>();
            foreach (var kv in changed)
                if (kv.Value <= 0 || kv.Value > 27) bad.Add(kv.Key);

            string what = "改了 " + changed.Count + " 格功能：" + string.Join("；", (changedLines ?? new List<string>()).ToArray());
            if (bad.Count > 0)
            {
                list.Add(new LintFinding(LintLevel.Warn, where,
                    what + "。★ 其中 " + Join(bad) + " 号的 landType 不在 0~27 里（客户端可能认不出这一格，会画成色块）",
                    why, how));
            }
            else
            {
                list.Add(new LintFinding(LintLevel.Info, where, what, why, how));
            }
        }

        private static void CheckRounds(JNode root, List<LintFinding> list)
        {
            JNode n = DiyJson.Member(root, "rounds");
            if (n == null) return;
            if (n.Kind == JKind.Null)
            {
                list.Add(new LintFinding(LintLevel.Info, "rounds", "写成了 null ⇒ 等于没写",
                    "levels.py:125 `_int(raw.get(\"rounds\"))` 得 0，0 不进白名单",
                    "想「不按回合判负」就整条删掉，或者干脆填一个 99"));
                return;
            }
            long v;
            if (!DiyJson.IsInt(n, out v))
            {
                list.Add(new LintFinding(LintLevel.Warn, "rounds", "不是整数（" + DiyJson.Brief(n, 40) + "）",
                    "levels.py:125 会当成 0 ⇒ 这个字段被丢掉", "填整数，例如 99"));
                return;
            }
            if (v <= 0)
                list.Add(new LintFinding(LintLevel.Warn, "rounds", "值是 " + v + "（<= 0）⇒ 这个字段会被丢掉",
                    "levels.py:126 `if rounds > 0`", "正数才有用，否则删掉整条"));
        }

        private static void CheckRoundAward(JNode root, List<LintFinding> list, ref int read, ref int drop)
        {
            JNode n = DiyJson.Member(root, "round_award");
            if (n == null) return;
            if (n.Kind != JKind.Object)
            {
                drop++;
                list.Add(new LintFinding(LintLevel.Bad, "round_award", "必须是对象 { … }，现在是 " + Kind(n),
                    "levels.py:129 `if isinstance(award, dict)` —— 不是对象整块丢掉，轮次奖励回落默认 3/6/1",
                    "改成 { \"every\": 3, \"gold\": 6, \"cards\": 1 }"));
                return;
            }
            read++;
            foreach (JMember m in n.Members)
                if (Array.IndexOf(LevelSchema.RoundAwardKeys, m.Key) < 0)
                {
                    drop++;
                    list.Add(new LintFinding(LintLevel.Warn, "round_award." + m.Key, "服务端不会读这个子键",
                        LevelSchema.DropReason("round_award", m.Key), "只留 every / gold / cards"));
                }
            long every = DiyJson.IntOf(DiyJson.Member(n, "every"), 0);
            if (every <= 0)
                list.Add(new LintFinding(LintLevel.Warn, "round_award.every",
                    "every 是 " + every + "（<= 0）⇒ **整个 round_award 都被丢掉**",
                    "levels.py:131 `if every > 0` 才把这段收进去（gold / cards 一起没了）",
                    "every 填正数，例如 3"));
        }

        private static void CheckProgress(JNode root, List<LintFinding> list, ref int read, ref int drop)
        {
            JNode n = DiyJson.Member(root, "progress");
            if (n == null) return;
            if (n.Kind != JKind.Object)
            {
                drop++;
                list.Add(new LintFinding(LintLevel.Bad, "progress", "必须是对象 { … }，现在是 " + Kind(n),
                    "levels.py:143 `if isinstance(progress, dict)` —— 不是对象整块丢掉，进度回落默认 1/1/1",
                    "改成 { \"start\": 1, \"per_round\": 1, \"per_death\": 1 }"));
                return;
            }
            read++;
            foreach (JMember m in n.Members)
                if (Array.IndexOf(LevelSchema.ProgressKeys, m.Key) < 0)
                {
                    drop++;
                    list.Add(new LintFinding(LintLevel.Warn, "progress." + m.Key, "服务端不会读这个子键",
                        LevelSchema.DropReason("progress", m.Key), "只留 start / per_round / per_death / mission_delta"));
                }

            JNode md = DiyJson.Member(n, "mission_delta");
            if (md == null) return;
            if (md.Kind != JKind.Object)
            {
                drop++;
                list.Add(new LintFinding(LintLevel.Bad, "progress.mission_delta", "必须是对象 { \"任务id\": 增减 }",
                    "levels.py:149 `if isinstance(deltas, dict)`", "改成对象形式"));
                return;
            }
            foreach (JMember m in md.Members)
            {
                long k;
                if (!long.TryParse(m.Key, NumberStyles.Integer, CultureInfo.InvariantCulture, out k))
                {
                    drop++;
                    list.Add(new LintFinding(LintLevel.Warn, "progress.mission_delta[\"" + m.Key + "\"]", "键不是整数 ⇒ 这一条被剔掉",
                        "levels.py:151 `_int(k, -1) >= 0` 过滤", "键写成任务 id 的数字，例如 \"321130\""));
                }
                JNode v = m.Value;
                if (v.Kind != JKind.Number)
                    list.Add(new LintFinding(LintLevel.Warn, "progress.mission_delta[\"" + m.Key + "\"]", "值不是数字",
                        "levels.py:151 `_int(v)`，解不出来变成 0", "填整数，例如 -1"));
            }
            // ★ 崩点：levels.py:317 是 int(k)，键解不出整数会直接抛异常
            bool fatal = false;
            foreach (JMember m in md.Members)
            {
                long k;
                if (!long.TryParse(m.Key, NumberStyles.Integer, CultureInfo.InvariantCulture, out k)) fatal = true;
            }
            if (fatal)
                list.Add(new LintFinding(LintLevel.Bad, "progress.mission_delta", "★ 这里会让服务端**抛异常**",
                    "levels.py:317 `dict((int(k), int(v)) for …)` —— `_normalize` 虽然剔掉了非整数键，"
                    + "但 `progress()` 这一层是对**已过滤后的表**再 int()，键能过过滤就一定是整数；"
                    + "真正会炸的是**值**写成非数字字符串",
                    "键和值都写成整数；改完在服务端日志里确认没有 traceback"));
        }

        private static void CheckVictory(JNode root, List<LintFinding> list, ref int read, ref int drop)
        {
            JNode n = DiyJson.Member(root, "victory");
            if (n == null) return;
            if (n.Kind != JKind.Object)
            {
                drop++;
                list.Add(new LintFinding(LintLevel.Bad, "victory", "必须是对象 { \"kind\": …, \"targets\": [ … ] }，现在是 " + Kind(n),
                    "levels.py:175 `if isinstance(victory, dict) and victory.get(\"kind\")` —— 形状不对整块丢掉",
                    "改成 { \"kind\": \"kill_boss\", \"targets\": [1067] }"));
                return;
            }
            read++;
            foreach (JMember m in n.Members)
                if (Array.IndexOf(LevelSchema.VictoryKeys, m.Key) < 0)
                {
                    drop++;
                    list.Add(new LintFinding(LintLevel.Warn, "victory." + m.Key, "服务端不会读这个子键",
                        LevelSchema.DropReason("victory", m.Key), "只留 kind / targets"));
                }
            JNode kind = DiyJson.Member(n, "kind");
            string k = kind != null && kind.Kind == JKind.String ? kind.Str : null;
            if (string.IsNullOrEmpty(k))
                list.Add(new LintFinding(LintLevel.Bad, "victory.kind", "空的或不是字符串 ⇒ **整个 victory 被丢掉**",
                    "levels.py:175 `victory.get(\"kind\")` 为假就不收", "填 \"kill_boss\""));
            else if (k == "kill_all_boss")
                list.Add(new LintFinding(LintLevel.Info, "victory.kind", "「击败所有 BOSS 单位」—— 服务端还没实现这个 kind",
                    "★ 2026-10-08：`levels.victory` 现在只认 kill_boss（服务端第二批会加 kill_all_boss）",
                    "先用 kill_boss + targets 表达，或者等第二批"));
            else if (k != "kill_boss")
                list.Add(new LintFinding(LintLevel.Bad, "victory.kind", "\"" + k + "\" 服务端没有实现",
                    "levels.py:35-36 只说明并实现了 kill_boss", "改成 \"kill_boss\""));

            JNode targets = DiyJson.Member(n, "targets");
            if (targets == null || targets.Kind != JKind.Array)
            {
                list.Add(new LintFinding(LintLevel.Warn, "victory.targets", "没写或不是数组",
                    "levels.py:178 `(victory.get(\"targets\") or [])` ⇒ 空列表 = 没有目标 = 打不死人",
                    "写成 [1067] 这样的怪物表 id 列表"));
                return;
            }
            if (targets.Count == 0)
                list.Add(new LintFinding(LintLevel.Bad, "victory.targets", "是空数组 ⇒ 没有目标",
                    "levels.py:178 ⇒ targets = [] ⇒ kill_boss 永远不成立（这局没法赢）",
                    "填上 Boss 的**怪物表 id**"));
            foreach (JNode t in targets.Items)
            {
                long v;
                if (!DiyJson.IsInt(t, out v) || v <= 0)
                {
                    drop++;
                    list.Add(new LintFinding(LintLevel.Warn, "victory.targets", "有一个目标 " + DiyJson.Brief(t, 20) + " 不是正整数 ⇒ 被剔掉",
                        "levels.py:178 `if _int(x) > 0`", "填正整数怪物表 id"));
                }
            }
        }

        private static void CheckStartMonsters(JNode root, BoardModel board, List<LintFinding> list, ref int read, ref int drop)
        {
            JNode n = DiyJson.Member(root, "start_monsters");
            if (n == null)
            {
                // ★★ 2026-10-10：**没写这个键**不是"等于 0 只"，而是"**按官方那张图那批开局怪来**"
                //   （服务端 `battle.spawn_init_monsters`：`levels.start_monsters(room)` 是 None 就
                //    回落到 `INIT_MONSTERS[map_id]`）—— 用户实机就是这么被坑的：
                //   编辑器「怪物」页显示"开局就在场上 0 只"，进游戏开局却冒出一排官方怪。
                string what = "没有 `start_monsters` 键 ⇒ 开局**按官方那张图那批**来（不是 0 只）";
                string why = "服务端：配置里没写就回落到 `battle.INIT_MONSTERS[地图id]` 那批官方开局怪。";
                string how = "编辑器保存时会把它写成 `[]`（= 明确不刷）；想让开局就刷，就在「怪物」页把怪放进「开局就在场上」那一组。";
                string batch = OpeningMonsters.Hint(mapIdOf(root));
                if (batch.Length > 0) why += batch;
                list.Add(new LintFinding(LintLevel.Info, "start_monsters", what, why, how));
                return;
            }
            if (n.Kind != JKind.Array)
            {
                drop++;
                list.Add(new LintFinding(LintLevel.Bad, "start_monsters", "必须是数组 [ … ]，现在是 " + Kind(n),
                    "levels.py:138 `if isinstance(monsters, list)` —— 形状不对整块丢掉，回落到 battle.INIT_MONSTERS",
                    "改成数组形式"));
                return;
            }
            read++;
            if (n.Count == 0)
                list.Add(new LintFinding(LintLevel.Info, "start_monsters", "开局不刷怪（明确：键在、空数组 `[]`）",
                    "★ 2026-10-10 起服务端口径：「键存在但为空列表」= **明确不刷**（不再回落官方那批开局怪）。",
                    "想让开局就用官方那批：在「怪物」页点「用官方开局怪」（有那批数据时才有这个按钮），"
                    + "或者自己往「开局就在场上」那一组放怪"));

            for (int i = 0; i < n.Count; i++)
            {
                JNode row = n.Items[i];
                string at = "start_monsters[" + i + "]";
                if (row.Kind != JKind.Object)
                {
                    drop++;
                    list.Add(new LintFinding(LintLevel.Bad, at, "这一行是 " + Kind(row) + "，不是对象 ⇒ 整行被丢掉",
                        "levels.py:77-78 `if not isinstance(row, dict): return None`",
                        "改成一个对象；旧式 8 元组数组服务端也认，但**顺序**是 table/node/hp/gold/defense/attack/type/weight"));
                    continue;
                }
                long table = DiyJson.IntOf(DiyJson.Member(row, "table"), 0);
                if (table <= 0)
                    table = DiyJson.IntOf(DiyJson.Member(row, "id"), 0);
                if (table <= 0)
                    table = DiyJson.IntOf(DiyJson.Member(row, "monster_id"), 0);
                if (table <= 0)
                {
                    drop++;
                    list.Add(new LintFinding(LintLevel.Bad, at, "没有能用的怪物 id ⇒ **整行被丢掉**",
                        "levels.py:79-81 `table = _int(row.get(\"table\") or row.get(\"id\") or row.get(\"monster_id\"))`，"
                        + "`<= 0` 就 return None",
                        "补上 \"table\": <怪物表 id>"));
                    continue;
                }

                foreach (JMember m in row.Members)
                    if (Array.IndexOf(LevelSchema.MonsterKeys, m.Key) < 0 && m.Key != "id" && m.Key != "monster_id")
                    {
                        drop++;
                        list.Add(new LintFinding(LintLevel.Warn, at + "." + m.Key, "服务端不会读这个键",
                            LevelSchema.DropReason("start_monsters[]", m.Key),
                            "刷怪行只认 table / node / hp / gold / defense / attack / type / weight"));
                    }

                // ★ 与 spawn 行不同：开局怪**缺什么补 0**，不是回落到怪物表
                foreach (string key in new string[] { "hp", "gold", "defense", "attack", "type", "weight" })
                {
                    if (DiyJson.Member(row, key) == null)
                        list.Add(new LintFinding(LintLevel.Warn, at + "." + key, "这一行没写 " + key + " ⇒ 服务端补 **0**",
                            "levels.py:82-91 `_monster` 对每个字段都是 `_int(row.get(key))`（不是回落到 Monster.bin）",
                            "想要怪物表里的值就显式写上（progress_events 的 spawn 行才是「不写走表」）"));
                }

                long node = DiyJson.IntOf(DiyJson.Member(row, "node"), 0);
                CheckNodePlacement(board, at + ".node", node, "开局怪", list);
            }
        }

        private static void CheckProgressEvents(JNode root, BoardModel board, List<LintFinding> list, ref int read, ref int drop)
        {
            JNode n = DiyJson.Member(root, "progress_events");
            if (n == null) return;

            if (n.Kind != JKind.Object)
            {
                drop++;
                string extra = n.Kind == JKind.Array
                    ? "★ 你的写法是**数组**（带 at / spawn 的清单）。服务端要的是**以事件 id 为键的对象**："
                      + "{ \"32122\": { \"spawn\": [ … ] } }。写成数组等于**这一整块都不存在**。"
                    : "";
                list.Add(new LintFinding(LintLevel.Bad, "progress_events", "必须是以事件 id 为键的对象，现在是 " + Kind(n),
                    "levels.py:156 `if isinstance(events, dict)` —— 不是对象整块丢掉。" + extra,
                    "改成 { \"32122\": { \"spawn\": [ { \"table\": 1070, \"node\": 17 } ] } }"));
                // 数组形式很容易被当成"对的"，这里把每条都点一遍
                if (n.Kind == JKind.Array)
                    foreach (JNode item in n.Items)
                        if (item.Kind == JKind.Object)
                        {
                            JNode at = DiyJson.Member(item, "at");
                            list.Add(new LintFinding(LintLevel.Warn, "progress_events[].at",
                                "这条算作进度 " + DiyJson.Brief(at, 20) + " 触发（在服务端眼里不存在）",
                                "服务端按 MapEvent.bin 的**事件 id** 触发，不认 at 这种阈值写法",
                                "把 at 换成真正的 MapEvent id 当键"));
                        }
                return;
            }

            read++;
            foreach (JMember ev in n.Members)
            {
                long id;
                if (!long.TryParse(ev.Key, NumberStyles.Integer, CultureInfo.InvariantCulture, out id) || id < 0)
                {
                    drop++;
                    list.Add(new LintFinding(LintLevel.Warn, "progress_events[\"" + ev.Key + "\"]", "键不是非负整数 ⇒ 这一条被剔掉",
                        "levels.py:159-160 `event_id = _int(key, -1)`；`< 0` 就 continue",
                        "键要写 MapEvent.bin 里的事件 id（例如 32122）"));
                    continue;
                }
                string at = "progress_events[\"" + ev.Key + "\"]";
                if (ev.Value.Kind != JKind.Object)
                {
                    drop++;
                    list.Add(new LintFinding(LintLevel.Bad, at, "这一条的值是 " + Kind(ev.Value) + "，不是对象 ⇒ 被剔掉",
                        "levels.py:160 `and isinstance(value, dict)`", "改成 { \"spawn\": [ … ] }"));
                    continue;
                }

                foreach (JMember m in ev.Value.Members)
                    if (Array.IndexOf(LevelSchema.EventKeys, m.Key) < 0)
                    {
                        drop++;
                        list.Add(new LintFinding(LintLevel.Warn, at + "." + m.Key, "服务端不会读这个子键",
                            LevelSchema.DropReason("progress_events.*", m.Key), "只留 spawn / attack / defense / move"));
                    }

                JNode spawn = DiyJson.Member(ev.Value, "spawn");
                if (spawn != null)
                {
                    if (spawn.Kind != JKind.Array)
                    {
                        drop++;
                        list.Add(new LintFinding(LintLevel.Bad, at + ".spawn", "必须是数组，现在是 " + Kind(spawn),
                            "levels.py:164 `if isinstance(spawn, list)` —— 形状不对这个键丢掉",
                            "改成 [ { \"table\": 1070, \"node\": 17 } ]"));
                    }
                    else
                    {
                        for (int i = 0; i < spawn.Count; i++)
                        {
                            JNode row = spawn.Items[i];
                            string rat = at + ".spawn[" + i + "]";
                            if (row.Kind != JKind.Object)
                            {
                                drop++;
                                list.Add(new LintFinding(LintLevel.Bad, rat, "这一行是 " + Kind(row) + "，不是对象 ⇒ 整行被丢掉",
                                    "levels.py:103-104 `if not isinstance(row, dict): return None`",
                                    "旧式 9 元组数组也认，顺序是 table/node/count/hp/gold/defense/attack/type/weight"));
                                continue;
                            }
                            long table = DiyJson.IntOf(DiyJson.Member(row, "table"), 0);
                            if (table <= 0) table = DiyJson.IntOf(DiyJson.Member(row, "id"), 0);
                            if (table <= 0) table = DiyJson.IntOf(DiyJson.Member(row, "monster_id"), 0);
                            if (table <= 0)
                            {
                                drop++;
                                list.Add(new LintFinding(LintLevel.Bad, rat, "没有能用的怪物 id ⇒ **整行被丢掉**",
                                    "levels.py:105-107 `table <= 0` 就 return None", "补上 \"table\""));
                                continue;
                            }
                            foreach (JMember m in row.Members)
                                if (Array.IndexOf(LevelSchema.SpawnKeys, m.Key) < 0 && m.Key != "id" && m.Key != "monster_id")
                                {
                                    drop++;
                                    list.Add(new LintFinding(LintLevel.Warn, rat + "." + m.Key, "服务端不会读这个键",
                                        LevelSchema.DropReason("progress_events.*.spawn[]", m.Key),
                                        "刷怪行只认 table / node / count / hp / gold / defense / attack / type / weight"));
                                }
                            long cnt = DiyJson.IntOf(DiyJson.Member(row, "count"), 1);
                            if (cnt < 1)
                                list.Add(new LintFinding(LintLevel.Warn, rat + ".count", "count 写成 " + cnt + " ⇒ 会被抬成 1",
                                    "levels.py:110 `max(1, _int(row.get(\"count\"), 1))`", "想刷几只就写几只；不写就是 1"));
                            long node = DiyJson.IntOf(DiyJson.Member(row, "node"), -1);
                            CheckNodePlacement(board, rat + ".node", node, "刷怪", list);
                        }
                    }
                }

                // ★ 2026-10-08：奖励（发筹码 / 发牌 / 发钱 / 加扣血）—— 服务端第二批实现
                JNode rw = DiyJson.Member(ev.Value, "rewards");
                if (rw != null)
                {
                    if (rw.Kind != JKind.Array)
                    {
                        drop++;
                        list.Add(new LintFinding(LintLevel.Bad, at + ".rewards", "必须是数组，现在是 " + Kind(rw),
                            "★ 本工程新增的键：`rewards: [ {kind, to, …}, … ]`", "改成数组形式"));
                    }
                    else
                    {
                        for (int i = 0; i < rw.Count; i++)
                        {
                            JNode row = rw.Items[i];
                            string rat = at + ".rewards[" + i + "]";
                            if (row.Kind != JKind.Object)
                            {
                                drop++;
                                list.Add(new LintFinding(LintLevel.Bad, rat, "这一行不是对象 ⇒ 整行被丢掉",
                                    "奖励行要写成对象", "写成 { \"kind\": \"gold\", \"to\": \"team\", \"amount\": 6 }"));
                                continue;
                            }
                            foreach (JMember m in row.Members)
                                if (Array.IndexOf(LevelSchema.RewardKeys, m.Key) < 0)
                                    list.Add(new LintFinding(LintLevel.Warn, rat + "." + m.Key, "奖励行不认这个键",
                                        LevelSchema.DropReason("progress_events.*.rewards[]", m.Key),
                                        "只留 kind / to / choice / ids / count / amount"));
                            JNode kk = DiyJson.Member(row, "kind");
                            string rk = kk != null && kk.Kind == JKind.String ? kk.Str : "";
                            if (rk != "relic" && rk != "card" && rk != "gold" && rk != "hp")
                                list.Add(new LintFinding(LintLevel.Bad, rat + ".kind",
                                    (rk.Length == 0 ? "(没写)" : "\"" + rk + "\"") + " 不是认得出的奖励种类",
                                    "★ 只认 relic（发一次筹码）/ card（发牌）/ gold（发钱）/ hp（加扣血）",
                                    "把 kind 改成这四个之一"));
                            JNode to = DiyJson.Member(row, "to");
                            if (to != null && (to.Kind != JKind.String
                                || (to.Str != "team" && to.Str != "actor")))
                                list.Add(new LintFinding(LintLevel.Warn, rat + ".to",
                                    "to 只认 \"team\"（全队每人）/ \"actor\"（触发者）；别的值会被当成 team",
                                    "★ 本工程约定", "改成 team 或 actor"));
                        }
                    }
                }

                // 全是空的事件行
                if (DiyJson.Member(ev.Value, "spawn") == null
                    && DiyJson.Member(ev.Value, "attack") == null
                    && DiyJson.Member(ev.Value, "defense") == null
                    && DiyJson.Member(ev.Value, "move") == null
                    && DiyJson.Member(ev.Value, "rewards") == null)
                    list.Add(new LintFinding(LintLevel.Info, at, "这一条什么效果都没有 ⇒ 会被剔掉",
                        "levels.py:170-171 `if row:` —— 空的 row 不收", "补上 spawn / attack / defense / move / rewards，或者删掉这条"));
            }
        }

        private static void CheckSettlement(JNode root, List<LintFinding> list, ref int read, ref int drop)
        {
            JNode n = DiyJson.Member(root, "settlement");
            if (n == null) return;
            if (n.Kind != JKind.Object)
            {
                drop++;
                list.Add(new LintFinding(LintLevel.Warn, "settlement", "必须是对象，现在是 " + Kind(n),
                    "levels.py:183 `if isinstance(settlement, dict)`", "改成 { \"awards\": { \"1\": 45 } }"));
                return;
            }
            read++;
            JNode awards = DiyJson.Member(n, "awards");
            if (awards == null)
            {
                list.Add(new LintFinding(LintLevel.Info, "settlement", "没有 awards ⇒ 收成空对象",
                    "levels.py:191-192 没有 awards 就存 {}", "要发东西就写 awards"));
                return;
            }
            if (awards.Kind != JKind.Object)
            {
                drop++;
                list.Add(new LintFinding(LintLevel.Warn, "settlement.awards", "必须是对象 { \"物品id\": 个数 }",
                    "levels.py:186 `if isinstance(awards, dict)`", "改成对象形式"));
                return;
            }
            foreach (JMember m in awards.Members)
            {
                long k;
                if (!long.TryParse(m.Key, NumberStyles.Integer, CultureInfo.InvariantCulture, out k) || k < 0)
                {
                    drop++;
                    list.Add(new LintFinding(LintLevel.Warn, "settlement.awards[\"" + m.Key + "\"]", "键不是非负整数 ⇒ 这一条被剔掉",
                        "levels.py:188-189 `if _int(k, -1) >= 0`", "键写成物品 id 的数字"));
                }
                if (m.Value.Kind != JKind.Number)
                    list.Add(new LintFinding(LintLevel.Warn, "settlement.awards[\"" + m.Key + "\"]", "值不是数字",
                        "levels.py:188 `_int(v)` 解不出来变 0", "填整数个数"));
            }
        }

        private static void CheckMastermind(JNode root, List<LintFinding> list, ref int read, ref int drop)
        {
            JNode n = DiyJson.Member(root, "mastermind");
            if (n == null) return;
            if (n.Kind != JKind.Object)
            {
                drop++;
                list.Add(new LintFinding(LintLevel.Warn, "mastermind", "必须是对象，现在是 " + Kind(n),
                    "levels.py:181 `if isinstance(mastermind, dict)`", "改成对象形式"));
                return;
            }
            read++;
            list.Add(new LintFinding(LintLevel.Info, "mastermind", "这一整块会被**原样**收下",
                "levels.py:180-182 `out[\"mastermind\"] = dict(mastermind)` —— 服务端不做白名单过滤，"
                + "用不上的键由 mastermind.py 自己忽略 ⇒ 拼错的键在这里**不会**被拦下来",
                "按 server\\astralnext\\mastermind.py 读的键名写：enabled / boss / watcher / reveal_skill / boss_skill / watcher_skill / monster_skills"));
        }

        /// <summary>★ 服务端**不校验**的一类：怪站在不存在的格子上。</summary>
        private static void CheckNodePlacement(BoardModel board, string at, long node, string what, List<LintFinding> list)
        {
            if (node < 0)
            {
                list.Add(new LintFinding(LintLevel.Info, at, what + "落点是 " + node + " ⇒ **随机挑一块空地**",
                    "levels.py:109 `_int(row.get(\"node\"), -1)` —— -1 是「随机」的约定值",
                    "想让怪固定站在某格，就填那一格的 node id"));
                return;
            }
            if (board == null || !board.Ok)
                return;   // 没棋盘图就不硬猜，页面④已经说了为什么
            if (board.Get((int)node) == null)
                list.Add(new LintFinding(LintLevel.Bad, at,
                    "★ 棋盘上**没有 " + node + " 号格**（这块画板一共 " + board.Nodes.Count + " 格）",
                    "服务端**完全不校验** node 是否存在于棋盘（board.py 只在算路径时查 node_map）"
                    + " ⇒ 这一行不会报错，但那只怪在游戏里**不会出现**",
                    "改成棋盘上真实存在的格子号（去页面④看哪些格子有编号）"));
        }

        /// <summary>顶层 `map` 字段（读不到回 0）—— 有几条 lint 要说"官方那张图是怎么配的"。</summary>
        private static int mapIdOf(JNode root)
        {
            return (int)DiyJson.IntOf(DiyJson.Member(root, "map"), 0);
        }

        /// <summary>胜利条件指向一只永远不出现的怪 —— 这局将无法获胜。</summary>
        private static void CheckVictoryTargetsReachable(JNode root, List<LintFinding> list)
        {
            JNode victory = DiyJson.Member(root, "victory");
            if (victory == null || victory.Kind != JKind.Object) return;
            JNode kind = DiyJson.Member(victory, "kind");
            if (kind == null || kind.Kind != JKind.String || kind.Str != "kill_boss") return;
            JNode targets = DiyJson.Member(victory, "targets");
            if (targets == null || targets.Kind != JKind.Array) return;

            var present = new HashSet<long>();
            CollectMonsterTables(DiyJson.Member(root, "start_monsters"), present);
            JNode events = DiyJson.Member(root, "progress_events");
            if (events != null && events.Kind == JKind.Object)
                foreach (JMember ev in events.Members)
                {
                    if (ev.Value.Kind != JKind.Object) continue;
                    CollectMonsterTables(DiyJson.Member(ev.Value, "spawn"), present);
                }

            foreach (JNode t in targets.Items)
            {
                long v;
                if (!DiyJson.IsInt(t, out v) || v <= 0) continue;
                if (!present.Contains(v))
                    list.Add(new LintFinding(LintLevel.Bad, "victory.targets",
                        "目标怪 " + v + " **在这份配置里根本不会出现**",
                        "服务端只在「某个怪被打死」时比对 targets（战斗逻辑），不管这只怪有没有被刷出来"
                        + " ⇒ 这局赢不了，而且不报任何错",
                        "把 " + v + " 加进 start_monsters 或某个 progress_events 的 spawn（table = " + v + "）"));
            }
        }

        private static void CollectMonsterTables(JNode array, HashSet<long> into)
        {
            if (array == null || array.Kind != JKind.Array) return;
            foreach (JNode row in array.Items)
            {
                if (row.Kind != JKind.Object) continue;
                long t = DiyJson.IntOf(DiyJson.Member(row, "table"), 0);
                if (t <= 0) t = DiyJson.IntOf(DiyJson.Member(row, "id"), 0);
                if (t <= 0) t = DiyJson.IntOf(DiyJson.Member(row, "monster_id"), 0);
                if (t > 0) into.Add(t);
            }
        }

        private static string Kind(JNode n)
        {
            if (n == null) return "(缺)";
            switch (n.Kind)
            {
                case JKind.Object: return "对象 {…}";
                case JKind.Array: return "数组 […]";
                case JKind.String: return "字符串";
                case JKind.Number: return "数字";
                case JKind.Bool: return "布尔";
                default: return "null";
            }
        }
    }
}
