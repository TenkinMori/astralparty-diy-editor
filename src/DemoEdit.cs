using System;
using System.Collections.Generic;

namespace DiyEditor
{
    /// <summary>截图用的"先动一次手"（--demo-edit）。走界面同一批函数，并记进 MainWindow.Undo。</summary>
    public static class DemoEdit
    {
        public static readonly string[] Names = new string[] { "cellland", "monster", "stack", "progress" };

        public static string Apply(LevelDoc doc, string what)
        {
            if (doc == null || doc.Root == null) return null;
            switch ((what ?? "").Trim().ToLowerInvariant())
            {
                case "cellland": return CellLand(doc);
                case "monster": return Monster(doc);
                case "stack": return Stack(doc);
                case "progress": return Progress(doc);
                default: return null;
            }
        }

        /// <summary>
        /// ★ 演示"一格多只怪"（第 9 波 ①）：同一格摞 4 只（含 1 只重复的）+ 旁边一格 2 只。
        ///   ⇒ 棋盘上能看到 3 个错开叠放的头像 + "+1" 角标。
        /// </summary>
        private static string Stack(LevelDoc doc)
        {
            int node = NotStartNode(doc);
            if (node < 0) return null;
            List<MonsterRow> all = GameTables.Monsters();
            if (all.Count < 3) return null;

            MainWindow.Undo.Capture(doc, "一格多只怪");
            DiyData d = doc.Diy;
            var made = new List<PoolMonster>();
            for (int k = 0; k < 3; k++)
            {
                int table = all[Math.Min(k, all.Count - 1)].Id;
                made.Add(DiyPool.MakePool(d, table));
            }
            DiyPool.AddAt(d, node, made[0].Id);
            DiyPool.AddAt(d, node, made[1].Id);
            DiyPool.AddAt(d, node, made[2].Id);
            DiyPool.AddAt(d, node, made[0].Id);      // ★ 同一只再放一次 ⇒ 4 只，角标 +1
            int two = NextNode(doc, node);
            if (two >= 0)
            {
                DiyPool.AddAt(d, two, made[1].Id);
                DiyPool.AddAt(d, two, made[2].Id);
            }
            doc.SaveDiy();
            return "演示：格 " + node + " 摞了 " + DiyPool.CountAtNode(d, node) + " 只怪（含重复的同一只）"
                + (two >= 0 ? "；格 " + two + " 放了 2 只" : "")
                + " ⇒ 画面上 3 个头像按 45° 错开 + 「+"
                + MonsterStack.HiddenCount(DiyPool.CountAtNode(d, node)) + "」角标";
        }

        /// <summary>棋盘上"下一个格子"（跟传入的不一样就行，给演示用）。</summary>
        private static int NextNode(LevelDoc doc, int notThis)
        {
            return NextNodeExcept(doc, notThis);
        }

        /// <summary>棋盘上第一个没被 `used` 点名的格子（演示要往好几个格子上摆东西）。</summary>
        private static int NextNodeExcept(LevelDoc doc, params int[] used)
        {
            if (doc.Board == null || !doc.Board.Ok) return -1;
            foreach (BoardNode n in doc.Board.Nodes)
            {
                bool skip = false;
                foreach (int u in used) if (u == n.Id) skip = true;
                if (!skip) return n.Id;
            }
            return -1;
        }

        /// <summary>不是起点的那一格（演示"一格多只怪"别摞在出生点上）。</summary>
        private static int NotStartNode(LevelDoc doc)
        {
            if (doc.Board == null || !doc.Board.Ok) return -1;
            List<int> starts = doc.EffectiveStarts();
            foreach (BoardNode n in doc.Board.Nodes)
                if (BoardModel.StartNumber(starts, n.Id) <= 0) return n.Id;
            int fb = FreeNode(doc);
            return fb;
        }

        private static string CellLand(LevelDoc doc)
        {
            int node = FreeNode(doc);
            if (node < 0) return null;
            MainWindow.Undo.Capture(doc, "换格子功能");
            int rc = doc.SetNodeLand(node, 19);
            return "演示：把格 " + node + " 换成 landType 19 怪物（SetNodeLand = " + rc + "）";
        }

        /// <summary>★ 往池子里加一只怪并放到棋盘格上（新的"新增怪物"那条路）。</summary>
        private static string Monster(LevelDoc doc)
        {
            int node = FreeNode(doc);
            if (node < 0) return null;
            int table = FirstMinion();
            MainWindow.Undo.Capture(doc, "新增怪物");
            PoolMonster m = DiyPool.MakePool(doc.Diy, table);
            DiyPool.PlaceAt(doc.Diy, node, m.Id);
            doc.SaveDiy();
            return "演示：格 " + node + " 放了一只 " + table + "（进池子 #" + m.Id + "，" + m.Name + "）";
        }

        /// <summary>
        /// ★ 给 `DIY进度` 那一条占位事件铺满条词 + 获胜条件，并把"编辑过之后"的那份树写到关卡旁边
        ///   （`演示-开过进度.json`）。**这一份同时是默认 `--clicktest` 的夹具**（见 ClickTest.RunInner 的
        ///   默认路径），所以它必须把新扁平格式的每一样都摆上：
        ///     · 奖励四类都在（发牌 / 加扣血 / 加星币 / **发一次筹码**），其中筹码那条 `to = actor`；
        ///     · `spawn` 行**两种都有**：棋盘上放了格的（带 `node`）+ 计划只数多出来的随机补（`node:-1, count>1`）；
        ///     · `diy.board_monsters[]` 带 `event`：两条挂这个事件、一条是开局组（`event:0` ⇒ `start_monsters`）；
        ///     · 顶层 `victory`（targets = 怪物表 id）与 `progress.limit`。
        ///   ⇒ 夹具是**跑出来的**、不是手抄的：改这里再跑一次 `--demo-edit progress` 就能重生成。
        /// </summary>
        private static string Progress(LevelDoc doc)
        {
            DiyData d = doc.Diy;
            DiyEvent ev = d.FirstEvent();
            if (ev == null)
            {
                ev = new DiyEvent();
                ev.Id = DiyPool.PlaceholderEventId;
                ev.Name = DiyPool.PlaceholderEventName;
                ev.Description = DiyPool.PlaceholderEventDesc;
                d.Events.Add(ev);
            }
            MainWindow.Undo.Capture(doc, "加进度条词");

            // ★ 演示要同时拍到"按进度分类"和"未配置那一栏" ⇒ 池子里保证有两只：
            //   第 1 只被下面那条「新增怪物」条词指着（有进度），第 2 只谁都指着它（未配置）。
            if (d.Pool.Count == 0) DiyPool.MakePool(d, FirstMinion());
            if (d.Pool.Count < 2) DiyPool.MakePool(d, SecondMinion());

            // ★ 棋盘落点：第 1 只在这个事件下放两格（⇒ `spawn` 出两行带 `node` 的），
            //   第 2 只放进**开局组**（⇒ `diy.board_monsters` 里一条 `event:0` + `start_monsters` 一行）。
            int evKey = DiyPool.EventKeyOf(ev);
            int nodeA = FreeNode(doc);
            int nodeB = NextNodeExcept(doc, nodeA);
            int nodeC = NextNodeExcept(doc, nodeA, nodeB);
            if (nodeA >= 0) DiyPool.AddAt(d, nodeA, d.Pool[0].Id, evKey);
            if (nodeB >= 0) DiyPool.AddAt(d, nodeB, d.Pool[0].Id, evKey);
            if (nodeC >= 0) DiyPool.AddAt(d, nodeC, d.Pool[1].Id, 0);

            int n = d.NextItemNumber(ev);
            var it = new DiyProgressItem { N = n };
            List<CardRow> cards = GameTables.Cards();
            if (cards.Count > 0) it.Effects.Add(new DiyEffect { Kind = EffectKind.Card, Card = cards[0].Id, Count = 2 });
            it.Effects.Add(new DiyEffect { Kind = EffectKind.Hp, N = 2 });
            it.Effects.Add(new DiyEffect { Kind = EffectKind.Hp, N = -3 });
            it.Effects.Add(new DiyEffect { Kind = EffectKind.Gold, N = 10 });
            // ★ 第七类「发一次筹码」+ 至少一条 `to = actor`（服务端读 `rewards[].to`）
            it.Effects.Add(new DiyEffect { Kind = EffectKind.Relic, Choice = 3, To = DiyEffect.ToActor });
            ev.Items.Add(it);

            var it2 = new DiyProgressItem { N = n + 1 };
            it2.Effects.Add(new DiyEffect { Kind = EffectKind.MonsterAttack, N = 1 });
            it2.Effects.Add(new DiyEffect { Kind = EffectKind.MonsterDefense, N = 2 });
            // ★ 计划 4 只、棋盘上只放了 2 格 ⇒ 写盘时 2 行带格 + 1 行 `node:-1, count:2`（随机空地补）
            it2.Effects.Add(new DiyEffect { Kind = EffectKind.Spawn, Pool = d.Pool[0].Id, Count = 4 });
            ev.Items.Add(it2);

            ev.Limit = 18;
            if (!ev.VictoryKinds.Contains(DiyVictory.TypeKillMonsters))
                ev.VictoryKinds.Add(DiyVictory.TypeKillMonsters);
            if (!ev.VictoryTargets.Contains(d.Pool[0].Id))
                ev.VictoryTargets.Add(d.Pool[0].Id);
            if (!ev.VictoryKinds.Contains(DiyVictory.TypeKillAllBoss))
                ev.VictoryKinds.Add(DiyVictory.TypeKillAllBoss);
            doc.SaveDiy();

            // ★ 顺手把"编辑过之后"的那份树写到关卡旁边（报告 §③ 的 json 就是从这儿抄的）。
            //   纯内存 + 只写自己那份关卡附近，不碰任何别的地方。
            try
            {
                if (doc.Path.Length > 0 && System.IO.File.Exists(doc.Path))
                    System.IO.File.WriteAllText(
                        System.IO.Path.Combine(System.IO.Path.GetDirectoryName(doc.Path), "演示-开过进度.json"),
                        DiyJson.Write(doc.Root, " ", "\n"), new System.Text.UTF8Encoding(false));
            }
            catch (Exception) { }

            PageStory.ShotsPickEvent = ev.Id;
            return "演示：进度事件 " + ev.Id + "（" + ev.Name + "）加了 " + ev.Items.Count
                 + " 条进度，" + (it.Effects.Count + it2.Effects.Count) + " 条条词（发牌/加扣血/加星币/怪物攻/怪物防/新增怪物）";
        }

        /// <summary>挑一格没被改过、也不是起点的空地。</summary>
        private static int FreeNode(LevelDoc doc)
        {
            if (doc.Board == null || !doc.Board.Ok) return -1;
            int fallback = -1;
            foreach (BoardNode n in doc.Board.Nodes)
            {
                if (fallback < 0) fallback = n.Id;
                if (n.ShownLand == 0 && !n.HasDiy && BoardModel.StartNumber(doc.EffectiveStarts(), n.Id) <= 0)
                    return n.Id;
            }
            return fallback;
        }

        private static int FirstMinion()
        {
            foreach (MonsterRow m in GameTables.Monsters())
                if (m.Class == MonsterClass.Minion) return m.Id;
            List<MonsterRow> all = GameTables.Monsters();
            return all.Count > 0 ? all[0].Id : 1001;
        }

        /// <summary>第二只普通小怪（跟 FirstMinion 不一样，用来演示"未配置的那一栏"）。</summary>
        private static int SecondMinion()
        {
            int first = FirstMinion();
            foreach (MonsterRow m in GameTables.Monsters())
                if (m.Class == MonsterClass.Minion && m.Id != first) return m.Id;
            return first;
        }

        public static string Usage()
        {
            return "--demo-edit 认得的名字：" + string.Join(" / ", Names)
                 + "（cellland = 换一格功能；monster = 棋盘上放一只怪进池子；"
                 + "stack = 同一格摞 4 只怪（演示一格多只 + 「+N」角标）；"
                 + "progress = 给 DIY进度 加两条进度条词）"
                 + "；--demo-undo 再撤一步（用来拍撤销前后对比）。";
        }
    }
}
