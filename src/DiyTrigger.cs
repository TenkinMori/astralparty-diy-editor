using System;
using System.Collections.Generic;
using System.Globalization;

namespace DiyEditor
{
    /// <summary>
    /// ★★ 「一条进度事件在**第几个回合 / 第几点进度**触发」这件事的**唯一所有者**
    ///   （用户 2026-10-03 第 11 波原话："要能给每个进度事件写出哪个回合触发"）。
    ///
    /// ============================================================================================
    /// ★★ 官方机制（只读查证，2026-10-03 —— 结论：**触发进度只能靠事件 id 编码**）
    ///
    ///   ① `4-服务端\server\astralnext\gamedata.py:815` `_load_map_events()`：
    ///      MapEvent.bin 一行 = `MapEventInfoConfigure`，其中
    ///        field 1 = Id、field 4 = **Triggerparams 触发点（进度 × 2）**、
    ///        field 6 = MapEventEffectConfigs（效果）。
    ///      `gamedata.py:894` `map_event_progress(event_id)` = `row["trigger"] // 2`
    ///      —— 表里查不到返回 None（"调用方别猜，宁可不播"）。
    ///   ② `battle.py:1146` `progress_events(room)`：难度行那一串 id **按 map_event_progress 排序**；
    ///      `battle.py:1206` `progress_events_due()`：进度走到触发点就播 1055
    ///      （`if at is None or at > now: break`）。
    ///   ③ `levels.py:155-173` `_normalize()`：关卡 json 的 `progress_events.<事件id>` 这个块
    ///      **只认 spawn / attack / defense / move 四个子键** —— **没有**任何"触发进度"字段
    ///      （`levels.py:279` 的 docstring 也写明："事件 id 是 MapEvent.bin 的 Id"）。
    ///   ④ 官方样例 `4-服务端\server\data\levels\82016_1.json`：键就是官方事件 id
    ///      （`"32122"` / `"32123"` / `"32124"`），它的 `progress_events_note` 原话：
    ///      "事件 id 来自 MapEvent.bin"、"32121（开局那条，触发于进度 1）故意不写"。
    ///
    ///   ⇒ **没有任何"独立字段"能指定触发进度**。想要"进度 3 触发"，唯一的路是
    ///     **用一条 field4÷2 == 3 的官方事件 id 去编码它**。所以编辑器这一页做的是：
    ///       用户填 3  →  反查 MapEvent.bin 里 field4÷2 == 3 的官方事件
    ///                 →  挑一条（优先"官方这张图这一档登记过的"、再"还没被别的条目用过的"）
    ///                 →  **把这条 id 落进配置**（`progress_events` 的键 + `trigger_event` 字段）
    ///                 →  找不到就**明说"官方没有触发进度 = N 的事件"**，一个字节都不改
    ///                    （绝不硬编一个错的 id）。
    ///
    /// ============================================================================================
    /// ★★ 落盘（都在**客户端本地**那份关卡 json 里，写 `diy.events.<事件id>.items[]` 的每一行）：
    ///
    ///     "items": [ { "n": 1,
    ///                  "trigger_progress": 3,      ← 想在第几点进度触发（用户填的那个数）
    ///                  "trigger_event": 32001,     ← 用哪条官方事件 id 编码它（MapEvent.bin field4=6）
    ///                  "effects": [ … ] } ]
    ///
    ///   ★ 同时 `progress_events` 的**键**也用这条事件 id（而不是条目的编号 n）——
    ///     官方就是这么干的（82016_1.json 的键是 32122/32123/32124），
    ///     服务端 `battle.progress_events_due` 才认得出"什么时候播"。
    ///   ★ 没设触发点的条目，键**照旧**用编号 n（老文件逐字节不变）。
    ///
    /// ★ 关卡配置存在**客户端本地** `<游戏根>\DIY关卡\*.json`（+ 同名 `.board.json`）；
    ///   **服务端将来是去适配读取/接收这份配置**，不是把配置搬进服务端目录。
    ///   ⇒ 这一批没有、也不会往 `4-服务端\` 写任何东西。
    /// ============================================================================================
    /// </summary>
    public static class DiyTrigger
    {
        /// <summary>`diy.events.&lt;id&gt;.items[]` 里那个"想在第几点进度触发"的键名。</summary>
        public const string KeyProgress = "trigger_progress";

        /// <summary>同一行里那个"用哪条官方事件 id 编码"的键名。</summary>
        public const string KeyEvent = "trigger_event";

        // ============================================================ 换算：进度 → 约第几回合
        //
        // 口径：`progress.per_round` = 每个回合开始 +N（levels.py:28 / py:313，服务端默认 1）。
        // ★ 用户要求："没设就只显示进度值（别硬算）"——所以**没写 progress.per_round 时不给回合数**。

        /// <summary>读 `progress.per_round`。★ 没写 / 不是正整数 ⇒ null（"别硬算"）。</summary>
        public static int? PerRound(LevelDoc doc)
        {
            if (doc == null || doc.Root == null) return null;
            JNode n = DiyJson.Member(DiyJson.Member(doc.Root, "progress"), "per_round");
            long v;
            if (n == null || !DiyJson.IsInt(n, out v)) return null;
            if (v <= 0 || v > 100000) return null;
            return (int)v;
        }

        /// <summary>读 `progress.start`（开局进度；没写按 0）。</summary>
        public static int Start(LevelDoc doc)
        {
            if (doc == null || doc.Root == null) return 0;
            long v;
            JNode n = DiyJson.Member(DiyJson.Member(doc.Root, "progress"), "start");
            if (n != null && DiyJson.IsInt(n, out v) && v >= 0 && v < 100000) return (int)v;
            return 0;
        }

        /// <summary>
        /// 进度 → "约第 N 回合"。★ **没写 `progress.per_round` 就返回空串**（只显示进度值）。
        /// </summary>
        public static string RoundText(LevelDoc doc, int progress)
        {
            if (progress < 0) return "";
            int? per = PerRound(doc);
            if (!per.HasValue) return "";
            int start = Start(doc);
            int need = progress - start;
            int rounds = need <= 0 ? 1 : (need + per.Value - 1) / per.Value;
            return "约第 " + rounds.ToString(CultureInfo.InvariantCulture) + " 回合";
        }

        /// <summary>为什么是这个回合数（界面上的小字）。</summary>
        public static string RoundWhy(LevelDoc doc)
        {
            if (!PerRound(doc).HasValue)
                return "（你没写 progress.per_round ⇒ 只显示进度值，不硬算回合）";
            int start = Start(doc);
            return "（按 progress.per_round = " + PerRound(doc).Value
                 + (start != 0 ? "、开局进度 start = " + start : "") + " 换算）";
        }

        // ============================================================ 显示
        /// <summary>短句：清单里每条事件后面挂的那个（"触发进度 3（约第 3 回合）"）。</summary>
        public static string Short(LevelDoc doc, DiyProgressItem it)
        {
            if (it == null) return "";
            if (it.TriggerProgress < 0) return "触发点没设";
            string s = "触发进度 " + it.TriggerProgress.ToString(CultureInfo.InvariantCulture);
            string r = RoundText(doc, it.TriggerProgress);
            if (r.Length > 0) s += "（" + r + "）";
            return s;
        }

        /// <summary>长句：编辑区里那一段说明（含"触发进度由事件 id 决定"这句）。</summary>
        public static string Detail(LevelDoc doc, DiyProgressItem it)
        {
            if (it == null) return "先在上面点一条进度。";
            if (it.TriggerProgress < 0)
            {
                return "这条进度事件还没设触发点 ⇒ 服务端不知道它什么时候播。"
                     + "触发进度只能靠「事件 id」编码（MapEvent.bin 的 field4 ÷ 2），"
                     + "所以在上面那个框里填「第几个进度 / 第几回合」，或者点「挑官方事件…」从官方事件里挑一条。";
            }

            string s = "触发进度 " + it.TriggerProgress.ToString(CultureInfo.InvariantCulture);
            string r = RoundText(doc, it.TriggerProgress);
            s += r.Length > 0 ? "（" + r + "）" : "";
            s += " · 编码：";

            if (it.TriggerEvent <= 0)
            {
                s += "★ 没有对应的官方事件 id ⇒ 服务端不会播这条（触发进度由事件 id 决定，重挑一条）。";
                return s;
            }

            MapEventRow row = MapEventTable.Get(it.TriggerEvent);
            if (row == null)
            {
                s += "★ 官方事件 id " + it.TriggerEvent + " 不在 MapEvent.bin 里 ⇒ 服务端不会播这条，请重挑。";
                return s;
            }
            s += "官方事件 id " + row.Id + "「" + (row.Name.Length > 0 ? row.Name : "(官方没给名字)") + "」"
               + "（MapEvent.bin field4=" + (row.TriggerX2.HasValue ? row.TriggerX2.Value.ToString(CultureInfo.InvariantCulture) : "?")
               + " ⇒ " + row.TriggerX2.GetValueOrDefault() / 2 + "）";
            if (row.Progress != it.TriggerProgress)
                s += " ★ 可是它表里的触发进度是 " + row.Progress + "，跟这里写的 " + it.TriggerProgress + " 对不上";
            s += " · " + RoundWhy(doc);
            if (it.TriggerProgress == 0)
                s += " ★ 触发进度 0 = 开局那一刻（服务端只播那张卡，效果走 start_monsters）";
            else if (it.TriggerProgress == 1)
                s += " ★ 触发进度 1 通常被官方当「开局事件」，服务端会跳过它的效果（battle.progress_events_due 只播 1055）";
            return s;
        }

        /// <summary>给报告 / 自测用的一行（"条目 n=2 ⇒ 触发进度 3（约第 3 回合）· 事件 id 32001"）。</summary>
        public static string OneLine(LevelDoc doc, DiyProgressItem it)
        {
            if (it == null) return "(空)";
            string s = "条目 n=" + it.N + " ⇒ " + Short(doc, it);
            if (it.TriggerEvent > 0) s += " · 事件 id " + it.TriggerEvent;
            return s;
        }

        // ============================================================ 反查 + 落定
        /// <summary>★ 触发进度 = progress 的官方事件（按 id 升序）。一条都没有 ⇒ 空表。</summary>
        public static List<MapEventRow> Candidates(int progress)
        {
            return progress < 0 ? new List<MapEventRow>() : MapEventTable.RowsAtProgress(progress);
        }

        /// <summary>"官方没有触发进度 = N 的事件"那句话（两处用同一句，别各写各的）。</summary>
        public static string NoCandidateText(int progress)
        {
            int max = MapEventTable.MaxProgress();
            return "★ 官方没有触发进度 = " + progress + " 的事件"
                 + "（MapEvent.bin 里 field4 ÷ 2 没有一条等于 " + progress
                 + (max >= 0 ? "；官方最大的触发进度是 " + max : "")
                 + "）⇒ 没改任何东西。换个进度值，或者点「挑官方事件…」看看官方给的是哪几档。";
        }

        /// <summary>
        /// ★★ 把"第 progress 点进度触发"落进这条条目：
        ///   反查官方事件 → 挑一条 → 写 `trigger_progress` + `trigger_event`。失败**一个字节都不改**。
        /// </summary>
        /// <param name="note">给人看的结果（成功/失败都说清）。</param>
        public static bool Apply(LevelDoc doc, DiyData d, DiyProgressItem it, int progress, out string note)
        {
            if (it == null) { note = "没有选中哪一条进度。"; return false; }
            if (progress < 0)
            {
                note = "触发进度要填 0 或更大的整数（0 = 开局那一刻）。";
                return false;
            }

            List<MapEventRow> rows = Candidates(progress);
            if (rows.Count == 0)
            {
                note = NoCandidateText(progress);
                return false;
            }

            // 别的条目已经用掉的事件 id（尽量让两条事件别挤在同一个 id 上）
            var used = new List<int>();
            if (d != null)
                foreach (DiyEvent ev in d.Events)
                    foreach (DiyProgressItem x in ev.Items)
                        if (!ReferenceEquals(x, it) && x.TriggerEvent > 0) used.Add(x.TriggerEvent);

            // 官方这张图这一档登记过的那批（"官方本来就用它"）
            List<int> official = doc == null ? new List<int>()
                : MapEventTable.EventsForMapDifficulty(doc.MapId, doc.Difficulty);

            MapEventRow pick = null;
            bool officialHit = false, reused = false;
            foreach (MapEventRow r in rows)
                if (official.Contains(r.Id) && !used.Contains(r.Id)) { pick = r; officialHit = true; break; }
            if (pick == null)
                foreach (MapEventRow r in rows)
                    if (!used.Contains(r.Id)) { pick = r; break; }
            if (pick == null)
            {
                // 候选都用过了：**允许复用**（触发点相同的两条事件并存 —— 这是用户点名要的）
                pick = rows[0];
                reused = true;
            }

            it.TriggerProgress = progress;
            it.TriggerEvent = pick.Id;

            note = "触发进度 " + progress;
            string rt = RoundText(doc, progress);
            if (rt.Length > 0) note += "（" + rt + "）";
            note += " ⇒ 用官方事件 id " + pick.Id + "「" + (pick.Name.Length > 0 ? pick.Name : "(官方没给名字)") + "」编码";
            note += officialHit ? "（官方这张图这一档登记过的）" : "";
            note += reused ? "（这一档下别的 id 都被占用了，两条事件共用同一个 id —— 触发点相同，允许）" : "";
            note += "。这条进度下的官方事件一共 " + rows.Count + " 条："
                 + JoinIds(rows) + "。";
            return true;
        }

        /// <summary>从对话框里挑了一条官方事件（进度 + id 一起定）。</summary>
        public static bool ApplyRow(LevelDoc doc, DiyProgressItem it, MapEventRow row, out string note)
        {
            if (it == null || row == null) { note = "没有挑中哪条官方事件。"; return false; }
            if (!row.HasTrigger)
            {
                note = "★ 官方事件 " + row.Id + " 那一行**没写触发点**（field4 缺）⇒ 这条不能用来编码触发进度，没改。";
                return false;
            }
            it.TriggerProgress = row.Progress;
            it.TriggerEvent = row.Id;
            note = "挑了官方事件 " + row.Id + "「" + (row.Name.Length > 0 ? row.Name : "(官方没给名字)") + "」"
                 + " ⇒ 触发进度 " + row.Progress;
            string rt = RoundText(doc, row.Progress);
            if (rt.Length > 0) note += "（" + rt + "）";
            note += " · 效果：" + MapEventTable.EffectSummary(row);
            return true;
        }

        /// <summary>清掉触发点（留空 = 这一条暂时不管触发）。</summary>
        public static void Clear(DiyProgressItem it)
        {
            if (it == null) return;
            it.TriggerProgress = -1;
            it.TriggerEvent = -1;
        }

        /// <summary>服务端那份 `progress_events` 该用哪个键（★ 设了触发点就用**官方事件 id**）。</summary>
        public static int KeyOf(DiyProgressItem it)
        {
            if (it == null) return -1;
            return it.TriggerEvent > 0 ? it.TriggerEvent : it.N;
        }

        // ============================================================ 校验③
        /// <summary>
        /// ★ 校验③：**有进度事件没挑 id / 挑了表里没有的 id**（用户点名要的那一条）。
        ///   写法 / 口吻照 `DiyPool.CheckSpawns` / `CheckMonsters`：第一行是结论，然后逐条点名，最后一句怎么办。
        /// </summary>
        public static List<string> Check(LevelDoc doc, DiyData d)
        {
            var lines = new List<string>();
            if (d == null || d.Events.Count == 0)
            {
                lines.Add("还没有进度事件 ⇒ 没有要挑的。");
                return lines;
            }

            var items = new List<DiyProgressItem>();
            foreach (DiyEvent ev in d.Events) foreach (DiyProgressItem it in ev.Items) items.Add(it);
            if (items.Count == 0)
            {
                lines.Add("还没有进度条目 ⇒ 没有事件要挑 id。");
                return lines;
            }

            var missing = new List<DiyProgressItem>();   // 没挑
            var ghost = new List<DiyProgressItem>();     // 挑了表里没有的
            var mismatch = new List<DiyProgressItem>();  // 挑了，但表里那一行的触发进度跟写的不一样
            foreach (DiyProgressItem it in items)
            {
                if (it.TriggerEvent <= 0) { missing.Add(it); continue; }
                MapEventRow row = MapEventTable.Get(it.TriggerEvent);
                if (row == null) { ghost.Add(it); continue; }
                if (it.TriggerProgress >= 0 && row.Progress != it.TriggerProgress) mismatch.Add(it);
            }

            int bad = missing.Count + ghost.Count + mismatch.Count;
            if (bad == 0)
            {
                lines.Add(items.Count + " 条进度事件都挑好了触发点 ✓");
                foreach (DiyProgressItem it in items) lines.Add("  " + OneLine(doc, it));
                return lines;
            }

            lines.Add("★ 有 " + bad + " 条进度事件没挑（或挑错）事件 id ✗（共 " + items.Count + " 条）");
            foreach (DiyProgressItem it in missing)
                lines.Add("  条目 n=" + it.N + " → ★ 没挑事件 id（触发进度没写）⇒ 服务端不知道它什么时候播");
            foreach (DiyProgressItem it in ghost)
                lines.Add("  条目 n=" + it.N + " → ★ 挑了 trigger_event = " + it.TriggerEvent
                    + "，这个号**不在 MapEvent.bin 里**（官方没这个事件）");
            foreach (DiyProgressItem it in mismatch)
                lines.Add("  条目 n=" + it.N + " → ★ trigger_progress = " + it.TriggerProgress
                    + "，可事件 " + it.TriggerEvent + " 在表里的触发进度是 "
                    + MapEventTable.ProgressOf(it.TriggerEvent) + "（对不上）");
            lines.Add("  ⇒ 去「进度 / 事件」页点那一条，在「触发进度 / 回合」里填一个数"
                    + "（或者点「挑官方事件…」从官方事件里挑）—— 触发进度由事件 id 决定。");
            return lines;
        }

        private static string JoinIds(List<MapEventRow> rows)
        {
            var a = new string[rows.Count];
            for (int k = 0; k < rows.Count; k++) a[k] = rows[k].Id.ToString(CultureInfo.InvariantCulture);
            return string.Join("/", a);
        }
    }
}
