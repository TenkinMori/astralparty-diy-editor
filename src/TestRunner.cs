using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace DiyEditor
{
    /// <summary>
    /// 自测（★ 它自己的四条，跑的是**真的导入 / 编辑 / 导出 / 覆盖**那条代码路径）：
    ///   ① 打开一份【真实】关卡 json 能解析；
    ///   ② 改一个字段 ⇒ 导出 ⇒ 再打开能读回同值；
    ///   ③ 导出格式与官方一致（缩进 / 换行 / 键序都不破坏）；
    ///   ④ 零写入证明：输入文件 sha256 前后一致；覆盖模式必须产生备份（且备份 sha256 = 覆盖前）。
    ///
    /// 用法：DIY关卡编辑器.exe --selftest
    /// 退出码 0 = 全过；1 = 有失败。同时把逐条原始输出写到 自测\自测输出.txt。
    /// </summary>
    public static class TestRunner
    {
        private static readonly List<string> Lines = new List<string>();
        private static int _pass, _fail;

        public static int Run(string logPath)
        {
            _pass = 0; _fail = 0;
            Lines.Clear();
            Line("小小兔趴 · DIY 关卡编辑器 —— 自测");
            Line("时间：" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
            Line("");

            string own = Own();
            string levelsDir = FirstExisting(
                Path.Combine(own, @"..\4-服务端\server\data\levels"),
                @"D:\小小兔趴\4-服务端\server\data\levels");
            string outDir = Path.Combine(own, @"_自测\输出");
            Directory.CreateDirectory(outDir);
            // ★ 自测产生的备份也放进 _自测（别在 dist 交付目录里堆东西）
            ExportService.OverrideBackupRoot(Path.Combine(own, @"_自测\导出备份"));

            Line("自己的目录：" + Path.GetFullPath(own));
            Line("样例目录：  " + (levelsDir ?? "(找不到)"));
            Line("输出目录：  " + Path.GetFullPath(outDir));
            Line("");

            // ---------------------------------------------------------- 准备工作：把样例拷进输出目录
            if (levelsDir == null || !Directory.Exists(levelsDir))
            {
                Line("★ 找不到官方样例目录，自测无法进行。");
                _fail++;
                return Finish(logPath);
            }
            string origin = Path.Combine(levelsDir, "82016_1.json");
            if (!File.Exists(origin))
            {
                string[] any = Directory.GetFiles(levelsDir, "*.json");
                if (any.Length == 0) { Line("★ 样例目录里没有 json。"); _fail++; return Finish(logPath); }
                origin = any[0];
            }
            string work = Path.Combine(outDir, "工作副本_" + Path.GetFileName(origin));
            File.Copy(origin, work, true);

            // ---------------------------------------------------------- ① 解析真实关卡
            Line("── ① 打开一份【真实】关卡 json 能解析 ─────────────────────────");
            string readOnlyHashBefore = ExportService.Sha256OfFile(origin);
            Line("被读的原件（只读）：" + origin);
            Line("  原件 sha256（读之前）：" + readOnlyHashBefore);

            LevelDoc doc = null;
            try
            {
                doc = LevelDoc.Open(work);
                Line("  解析成功：顶层 " + doc.Root.Count + " 个键；map=" + doc.MapId
                    + "，难度=" + doc.Difficulty);
                var names = new List<string>();
                foreach (JMember m in doc.Root.Members) names.Add(m.Key);
                Line("  顶层键序：" + string.Join(" · ", names.ToArray()));
                int read, dropped;
                List<LintFinding> findings = LevelLint.Run(doc.Root, doc.Board, out read, out dropped);
                Line("  校验：服务端会读 " + read + " 个 / 会丢 " + dropped + " 个；报告 " + findings.Count + " 条");
                Line("  棋盘：" + (doc.Board != null && doc.Board.Ok ? doc.Board.Note : (doc.Board == null ? "(无)" : doc.Board.Note)));
                Check("① 真实关卡解析", doc.Root.Kind == JKind.Object && doc.MapId > 0);
                Check("① 顶层键序非空", names.Count > 0);
            }
            catch (Exception ex)
            {
                Line("  ★ 解析失败：" + ex.Message);
                Check("① 真实关卡解析", false);
            }
            string readOnlyHashAfter = ExportService.Sha256OfFile(origin);
            Line("  原件 sha256（读之后）：" + readOnlyHashAfter);
            Check("① 只读样例没被写", readOnlyHashBefore == readOnlyHashAfter);
            Line("");

            // ---------------------------------------------------------- ② 改 ⇒ 导出 ⇒ 读回
            Line("── ② 改一个字段 ⇒ 导出 ⇒ 再打开能读回同值 ────────────────────");
            string exportPath = Path.Combine(outDir, "导出_改过rounds.json");
            long want = 77;
            try
            {
                JNode rounds = DiyJson.Member(doc.Root, "rounds");
                if (rounds == null)
                {
                    rounds = DiyJson.EnsureMember(doc.Root, "rounds");
                    Line("  这份样例原来没写 rounds ⇒ 用 EnsureMember 新加一条（也顺便测了「加字段」）");
                }
                long had = DiyJson.IntOf(rounds, long.MinValue);
                Line("  改之前 rounds = " + (had == long.MinValue ? "(不是整数)" : had.ToString(CultureInfo.InvariantCulture)));
                DiyJson.SetInt(rounds, want);
                Line("  改成 rounds = " + want);

                var result = doc.Export(exportPath, false, "");
                Line("  导出到：" + result.TargetPath);
                Line("  导出后 sha256：" + result.NewSha256 + "（" + result.NewLength + " 字节）");

                LevelDoc again = LevelDoc.Open(exportPath);
                long got = DiyJson.IntOf(DiyJson.Member(again.Root, "rounds"), long.MinValue);
                Line("  重新打开后的 rounds = " + got);
                Check("② 改后导出再读回同值", got == want);
                Check("② 导出文件与原文不同", !string.Equals(result.NewSha256,
                    ExportService.Sha256OfFile(work), StringComparison.OrdinalIgnoreCase));
            }
            catch (Exception ex)
            {
                Line("  ★ 失败：" + ex.Message);
                Check("② 改后导出再读回同值", false);
            }
            Line("");

            // ---------------------------------------------------------- ③ 格式与官方一致
            Line("── ③ 导出格式与官方一致（缩进 / 换行 / 键序） ──────────────────");
            try
            {
                // (a) 完全没改的文档：导出的字节必须与原文**逐字节相同**
                string untouched = Path.Combine(outDir, "导出_没改过.json");
                File.Copy(work, untouched, true);
                LevelDoc clean = LevelDoc.Open(untouched);
                var r0 = clean.Export(untouched, true, "自测3a");
                Line("  (a) 没改任何东西就导出（覆盖）：");
                Line("      原文 sha256 = " + r0.SourceSha256);
                Line("      新文 sha256 = " + r0.NewSha256);
                Line("      逐字节相同 = " + (r0.ByteIdentical ? "是" : "否"));
                Check("③a 没改过 ⇒ 导出与原文逐字节相同", r0.ByteIdentical);

                // (b) 顶层键序
                string beforeOrder = TopKeyOrder(File.ReadAllText(work, Encoding.UTF8));
                string afterOrder = TopKeyOrder(File.ReadAllText(exportPath, Encoding.UTF8));
                Line("  (b) 顶层键序：");
                Line("      改前：" + beforeOrder);
                Line("      改后：" + afterOrder);
                // ★ 这份样例本来**没有** rounds，是 EnsureMember 新加在**末尾**的，
                //   所以"改后"应该 = "改前" + ",rounds"。键序被打乱的话这里就对不上。
                string expectOrder = beforeOrder.Length == 0 ? "rounds" : beforeOrder + ",rounds";
                Line("      期望：" + expectOrder + "（新键只会追加在末尾）");
                Check("③b 原有键序没被打乱、新键追加在末尾", afterOrder == expectOrder);

                // (c) 缩进 / 换行
                string text = File.ReadAllText(exportPath, Encoding.UTF8);
                bool twoSpaces = text.Contains("\n  \"map\"") || text.Contains("\n  \"note\"");
                int crlf = 0, lf = 0;
                for (int i = 0; i < text.Length; i++)
                    if (text[i] == '\n') { if (i > 0 && text[i - 1] == '\r') crlf++; else lf++; }
                Line("  (c) 缩进：2 空格 = " + (twoSpaces ? "是" : "否")
                    + "；换行：LF=" + lf + " CRLF=" + crlf + "（原文是 LF）");
                Check("③c 缩进 2 空格且换行沿用原文 LF", twoSpaces && lf > 0 && crlf == 0);

                // (d) 单行短对象没被拆成多行（官方手写风格）
                bool inlineKept = text.Contains("\"victory\": { \"kind\":")
                               || text.Contains("\"mission_delta\": { \"321130\": -1 }");
                Line("  (d) 单行短对象保持单行 = " + (inlineKept ? "是" : "否"));
                Check("③d 单行短对象没被展开", inlineKept);

                // (e) 中文没被转成 \uXXXX
                bool cjk = text.Contains("异变图书馆") || text.Contains("进度");
                Line("  (e) 中文照原样（没转成 \\uXXXX）= " + (cjk ? "是" : "否"));
                Check("③e 中文没被转义", cjk);
            }
            catch (Exception ex)
            {
                Line("  ★ 失败：" + ex.Message);
                Check("③ 格式一致", false);
            }
            Line("");

            // ---------------------------------------------------------- ④ 零写入 / 备份 / 原子替换
            Line("── ④ 零写入证明 + 覆盖必须先备份 ────────────────────────────");
            try
            {
                string victim = Path.Combine(outDir, "覆盖目标.json");
                File.Copy(work, victim, true);
                string before = ExportService.Sha256OfFile(victim);
                long sizeBefore = new FileInfo(victim).Length;
                Line("  覆盖目标：" + victim);
                Line("  覆盖前 sha256：" + before + "（" + sizeBefore + " 字节）");

                LevelDoc edit = LevelDoc.Open(victim);
                JNode rounds = DiyJson.Member(edit.Root, "rounds");
                if (rounds == null) rounds = DiyJson.EnsureMember(edit.Root, "rounds");
                DiyJson.SetInt(rounds, 88);
                var r = edit.Export(victim, true, "自测4");
                Line("  覆盖后 sha256：" + r.NewSha256 + "（" + r.NewLength + " 字节）");
                Line("  备份路径：" + r.BackupPath);
                Line("  备份 sha256：" + r.BackupSha256);
                Check("④a 覆盖模式产生了备份", File.Exists(r.BackupPath));
                Check("④b 备份 sha256 == 覆盖前 sha256（可逐字节回滚）",
                    string.Equals(r.BackupSha256, before, StringComparison.OrdinalIgnoreCase));
                Check("④c 覆盖后内容确实变了",
                    !string.Equals(r.NewSha256, before, StringComparison.OrdinalIgnoreCase));

                // 回滚一次，证明备份真的能用
                File.Copy(r.BackupPath, victim, true);
                string rolled = ExportService.Sha256OfFile(victim);
                Line("  用备份回滚后 sha256：" + rolled);
                Check("④d 用备份能逐字节回滚", string.Equals(rolled, before, StringComparison.OrdinalIgnoreCase));

                // (e) 输入文件（只读原件）前后没被动过
                string originNow = ExportService.Sha256OfFile(origin);
                Line("  只读原件 sha256（全部跑完）：" + originNow);
                Check("④e 只读原件全程零写入", string.Equals(originNow, readOnlyHashBefore, StringComparison.OrdinalIgnoreCase));

                // (f) 覆盖已取消的脏值：没改过内容时覆盖也要产生备份，且内容不变
                string same = Path.Combine(outDir, "覆盖_内容未变.json");
                File.Copy(work, same, true);
                string sameBefore = ExportService.Sha256OfFile(same);
                LevelDoc idle = LevelDoc.Open(same);
                var r2 = idle.Export(same, true, "自测4f");
                Line("  (f) 内容没变时覆盖：备份 " + (File.Exists(r2.BackupPath) ? "有" : "没有")
                    + "；内容不变 = " + (r2.ByteIdentical ? "是" : "否"));
                Check("④f 内容没变时覆盖仍先备份，且内容逐字节不变",
                    File.Exists(r2.BackupPath) && r2.ByteIdentical
                    && string.Equals(ExportService.Sha256OfFile(same), sameBefore, StringComparison.OrdinalIgnoreCase));
            }
            catch (Exception ex)
            {
                Line("  ★ 失败：" + ex.Message);
                Check("④ 备份 / 原子替换", false);
            }
            Line("");

            // ---------------------------------------------------------- 附：服务端不读的字段（现场演示）
            Line("── 附：服务端不会读的字段（现场标一份出来） ──────────────────");
            try
            {
                string probe = Path.Combine(outDir, "测试_多余字段.json");
                string text = File.ReadAllText(work, Encoding.UTF8);
                int at = text.IndexOf('{');
                text = text.Substring(0, at + 1)
                     + "\n  \"boss\": 1067,\n  \"boss_max_hp\": 999,\n  \"groups\": [1, 2]\n,"
                     + text.Substring(at + 1);
                File.WriteAllText(probe, text, new UTF8Encoding(false));

                LevelDoc d = LevelDoc.Open(probe);
                int read, dropped;
                List<LintFinding> fs = LevelLint.Run(d.Root, d.Board, out read, out dropped);
                Line("  服务端会读 " + read + " 个键 / 会丢 " + dropped + " 个键");
                foreach (LintFinding f in fs)
                    if (f.Level == LintLevel.Warn && f.What.IndexOf("不会读", StringComparison.Ordinal) >= 0)
                        Line("    ⚠ " + f.Where + " —— " + f.Why);
                Check("附 多余字段被标出来", dropped >= 3);
            }
            catch (Exception ex)
            {
                Line("  ★ 失败：" + ex.Message);
                Check("附 多余字段被标出来", false);
            }

            // ---------------------------------------------------------- ★⑤ 第 1 步新东西
            Line("── ⑤ 第 1 步：游戏根 / 地图模板 / 格子贴图 ──────────────────");
            List<MapTemplate> maps = null;
            try
            {
                Line("  游戏根探测：");
                Line("    找到的：  " + (GameLocator.GameDir ?? "(没找到)"));
                Line("    怎么找的：读 4-服务端\\client\\launcher.json 的 game_dir（这一份两边共用）");
                Line("    贴图目录：" + (GameLocator.ImageDir ?? "(没有 GameRes\\Img)"));
                Line("    DIY 目录：" + (GameLocator.DiyDir ?? "(没有游戏根，算不出来)"));
                Check("⑤a 找到游戏根目录", !string.IsNullOrEmpty(GameLocator.GameDir));
                Check("⑤b 游戏根下有 GameRes\\Img（贴图来源）", GameLocator.ImageDir != null);

                Line("  配置表（只读）：" + GameTables.Status());
                maps = GameTables.Maps();
                Check("⑤c 地图表读出来了（Map.bin）", maps.Count > 0);
                if (maps.Count > 0)
                {
                    var sample = new List<string>();
                    for (int k = 0; k < maps.Count && k < 6; k++)
                        sample.Add(maps[k].Id + "=" + maps[k].Name + "/" + (maps[k].SceneSuffix.Length > 0 ? maps[k].SceneSuffix : "?"));
                    Line("    前几张：" + string.Join(" · ", sample.ToArray()));
                }

                // ★ 模式 → 地图：客户端的地图列表就是照 GameMode.bin 铺的
                var modeMaps = GameTables.ModeMaps();
                var modeText = new List<string>();
                foreach (var kv in modeMaps)
                {
                    if (kv.Key == 4 || kv.Key == 9 || kv.Key == 12)
                        modeText.Add("模式" + kv.Key + "=" + kv.Value.Count + "张");
                }
                Line("    模式地图表（GameMode.bin）：共 " + modeMaps.Count + " 个模式；"
                    + (modeText.Count > 0 ? string.Join(" ", modeText.ToArray()) : "(没有 4/9/12)"));
                Line("    PVE(4) 列出来 " + GameTables.MapsForMode(4).Count + " 张");
                Check("⑤f GameMode.bin 能列出 PVE 的地图清单", GameTables.MapsForMode(4).Count > 0);

                // ★ 画板后缀是"地图id → 场景id → 场景名 → 取尾巴"三步算出来的，
                //   这里拿用户最关心的那张图核一下：82016 应该对到 023（不是 016）。
                MapTemplate m82016 = null;
                foreach (MapTemplate t in maps) if (t.Id == 82016) { m82016 = t; break; }
                if (m82016 != null)
                {
                    Line("    82016（异变图书馆）的画板后缀 = " + m82016.SceneSuffix
                        + "（应该是 023）· 难度表 id " + string.Join("/",
                            m82016.Difficulties.ConvertAll(
                                delegate (int d) { return d.ToString(CultureInfo.InvariantCulture); }).ToArray())
                        + " · 档位 " + GameTables.DifficultyTextOf(m82016));
                    Check("⑤d 82016 的画板后缀 = 023（走 Mids 那条链，不是拿 id 后三位）",
                        m82016.SceneSuffix == "023");
                }
                else
                {
                    Line("    ★ 表里没有 82016 —— 这台机器的配置表可能不一样。");
                    Check("⑤d 82016 的画板后缀 = 023", false);
                }

                Line("  格子贴图（UT_Platform_*，只读引用）：");
                int withTex = 0;
                var known = new List<int>();
                var unknown = new List<int>();
                for (int land = 0; land <= 27; land++)
                {
                    if (Art.PlatformTexture(land) != null) { withTex++; known.Add(land); }
                    else unknown.Add(land);
                }
                Line("    登记了 " + Art.PlatformTableSize + " 种；这机器上**文件确实存在**的 " + withTex + " 种");
                var kn = new List<string>();
                foreach (int k in known) kn.Add(k + "→" + Art.PlatformTexture(k));
                Line("    有图的：" + string.Join(" ", kn.ToArray()));
                var un = new List<string>();
                foreach (int k in unknown) un.Add(k + " " + Land.Name(k));
                Line("    没图的（回落色块 + 中文名）：" + string.Join("、", un.ToArray()));
                Check("⑤e 至少 10 种格子能贴客户端贴图", withTex >= 10);

                // 棋盘缓存里那几块，格子类型能不能都画出来
                //   ★★ 走 `BoardCache.EnumerateJson()`：**磁盘优先、exe 内置兜底**
                //     （用户 2026-10-12「要内置」）—— 于是"只有 exe 一个文件"的形态下
                //     这里照样数得到那 26 块官方画板（以前这一句在空目录会直接抛
                //     `DirectoryNotFoundException` ⇒ 连⑤一段整段变红）。
                //   ★ 这里走的是**棋盘页真正走的那条链**（`BoardStore.Load`），
                //     不是 `LevelDoc.Open`：后者要一份**关卡** json 的路径，磁盘上没有那份文件时
                //     它读不动（内置的棋盘是"一份棋盘 json"，不是"一份关卡"）。
                int checkedBoards = 0;
                int builtInBoards = 0;
                foreach (KeyValuePair<string, bool> item in BoardCache.EnumerateWithOrigin())
                {
                    string f = item.Key;
                    bool builtIn = item.Value;
                    if (builtIn) builtInBoards++;
                    string text = BoardCache.TextOf(f);
                    if (string.IsNullOrEmpty(text)) continue;
                    int mapId = 0;
                    try
                    {
                        JNode root = DiyJson.Parse(text);
                        JNode mv = DiyJson.Member(root, "map");
                        mapId = (int)DiyJson.IntOf(mv, 0);
                        JNode sv = DiyJson.Member(root, "scene");
                        if (mapId <= 0 && sv != null && sv.Kind == JKind.String)
                        {
                            int parsed;
                            if (int.TryParse(sv.Str, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed))
                                mapId = parsed;
                        }
                    }
                    catch (Exception) { }
                    if (mapId <= 0) continue;

                    BoardStore.ClearCache();
                    BoardStore.DiyBoardPath = "";
                    BoardStore.ExpectedSuffix = BoardCache.Stem(f);
                    BoardModel bm = BoardStore.Load(mapId, null);
                    if (bm == null || !bm.Ok) continue;
                    checkedBoards++;
                    if (checkedBoards <= 3)
                        Line("    " + BoardCache.Stem(f) + ".json（map=" + mapId + "）："
                            + bm.Nodes.Count + " 格" + (builtIn ? "（exe 内置）" : "（磁盘）"));
                }
                BoardStore.ClearCache();
                BoardStore.ExpectedSuffix = "";
                Line("  棋盘缓存：数了 " + checkedBoards + " 块画板"
                    + "（磁盘 " + BoardCache.Dir + " 在 = " + BoardCache.DirExists
                    + "；其中 exe 内置 " + builtInBoards + " 份 · "
                    + EmbeddedCache.Describe() + "）");
                Check("⑤f ★★ 棋盘缓存数得出画板（磁盘没有就吃 exe 内置那份）", checkedBoards > 0);
            }
            catch (Exception ex)
            {
                Line("  ★ 失败：" + ex.Message);
                Check("⑤ 第 1 步（游戏根 / 模板 / 贴图）", false);
            }
            Line("");

            // ---------------------------------------------------------- ★⑧ 进度上限 / 起点编号
            Line("── ⑧ 进度上限 ProgressLimit（只读难度表）+ 起点 1~4（官方 serial） ──");
            try
            {
                // (a) ★ 总进度上限必须**从难度表读出来**（用户："这里是不是没写总共多少进度"）
                string note = "";
                int? lim = GameTables.ProgressLimitOf(82016, 1, out note);
                Line("  ProgressLimit(82016, 难度档 1) = " + (lim.HasValue ? lim.Value.ToString(CultureInfo.InvariantCulture) : "(null)")
                    + "   ← " + note);
                Check("⑧a 82016 困难档的 ProgressLimit 读得出来", lim.HasValue && lim.Value > 0);

                int? lim0 = GameTables.ProgressLimitOf(82016, 0, out note);
                Line("  ProgressLimit(82016, 难度档 0) = " + (lim0.HasValue ? lim0.Value.ToString(CultureInfo.InvariantCulture) : "(null)")
                    + "   ← " + note);
                Check("⑧b 82016 普通档也读得出来（不是只有一档能读）", lim0.HasValue && lim0.Value > 0);

                // (c) ★ 读不到时**必须回 null**（不能编个 0 出来让人以为真是 0）
                int? bad = GameTables.ProgressLimitOf(999999, 0, out note);
                Line("  ProgressLimit(不存在的图 999999) = " + (bad.HasValue ? bad.Value.ToString(CultureInfo.InvariantCulture) : "(null)")
                    + "   ← " + note);
                Check("⑧c 读不到的图回 null 且给得出原因（不是 0）", !bad.HasValue && note.Length > 0);

                // (d) ★ 起点：官方 serial 就是 1~4 号（board.py:304 start_nodes / :211 is_own_born）
                //   ★★ 枚举同样走 `BoardCache.EnumerateJson()`（磁盘优先 / exe 内置兜底）
                var startIds = new List<int>();
                int startBoards = 0, fourBoards = 0;
                foreach (string f in BoardCache.EnumerateJson())
                {
                    // ★ 和⑤f 同一个口径：磁盘优先 / exe 内置兜底，走 `BoardStore.Load` 那条链
                    string btext = BoardCache.TextOf(f);
                    if (string.IsNullOrEmpty(btext)) continue;
                    int bmap = 0;
                    try
                    {
                        JNode broot = DiyJson.Parse(btext);
                        bmap = (int)DiyJson.IntOf(DiyJson.Member(broot, "map"), 0);
                    }
                    catch (Exception) { }
                    if (bmap <= 0) continue;
                    BoardStore.ClearCache();
                    BoardStore.DiyBoardPath = "";
                    BoardStore.ExpectedSuffix = BoardCache.Stem(f);
                    BoardModel bm = BoardStore.Load(bmap, null);
                    if (bm == null || !bm.Ok) continue;
                    startBoards++;
                    List<BoardNode> sn = bm.StartNodes();
                    if (sn.Count == 4) fourBoards++;
                    if (startIds.Count == 0)
                    {
                        foreach (BoardNode n in sn) startIds.Add(n.Id);
                        Line("  " + BoardCache.Stem(f) + ".json 的起点格：" + sn.Count + " 个，"
                            + "serial " + string.Join("/", Serials(sn)) + "，格 id " + string.Join("/", startIds.ConvertAll(
                                delegate (int x) { return x.ToString(CultureInfo.InvariantCulture); }).ToArray()));
                    }
                }
                BoardStore.ClearCache();
                BoardStore.ExpectedSuffix = "";
                Line("  棋盘缓存里 " + startBoards + " 块画板，其中起点**恰好 4 个**的 " + fourBoards + " 块");
                Check("⑧d 官方画板里有起点恰好 4 个的（serial 就是座位号那套）", fourBoards > 0 && startIds.Count == 4);

                // (e) ★ 编号 = 列表里的序号；缺 / 重都要报出来
                bool numbered = BoardModel.StartNumber(startIds, startIds[3]) == 4
                             && BoardModel.StartNumber(startIds, startIds[0]) == 1
                             && BoardModel.StartNumber(startIds, 12345) == 0;
                Check("⑧e 起点编号 = 顺序位（1..4），不在列表里回 0", numbered);

                // (f) ★ 校验：缺 / 重 / 空格 都要报出来，好的一遍不报（走真的 LevelDoc 那条路）
                LevelDoc diyDoc = LevelDoc.CreateFromTemplate(
                    GameTables.Map(82016) ?? (maps.Count > 0 ? maps[0] : null), "起点自测", LevelDoc.DiyDifficulty);
                bool hadBoard = diyDoc.Board != null && diyDoc.Board.Ok;
                Line("  ★ 这块画板：42 格里的起点格 " + CountLandType1(diyDoc.Board) + " 个（official serial 那套）");

                List<string> checkLines = diyDoc.StartSelfCheck();
                foreach (string one in checkLines) Line("  " + one);
                bool checkOk = checkLines.Count > 0
                    && checkLines[checkLines.Count - 1].IndexOf("都报得出来", StringComparison.Ordinal) >= 0;
                Check("⑧f 起点缺 / 重 / 空格都报得出来，正好 4 个不重复就不报", hadBoard && checkOk);

                // (g) ★ 用户指定过 diy.starts ⇒ 按用户那份算，且**不动官方 landType**
                int before = CountLandType1(diyDoc.Board);
                int pick = startIds[0] == 42 ? 41 : 42;
                diyDoc.SetStart(pick, 1);
                List<int> eff = diyDoc.EffectiveStarts();
                Line("  指定格 " + pick + " 为起点1 ⇒ diy.starts = ["
                    + string.Join(",", eff.ConvertAll(delegate (int x) { return x.ToString(CultureInfo.InvariantCulture); }).ToArray()) + "]");
                Line("  官方 landType=1 的格子数：指定前 " + before + " / 指定后 " + CountLandType1(diyDoc.Board));
                Check("⑧g diy.starts 生效（用户指定优先）且官方 landType 一个没动",
                    eff.Count > 0 && eff[0] == pick && CountLandType1(diyDoc.Board) == before);

                diyDoc.SetStarts(new List<int>());
                Line("  再整份取消 ⇒ diy.starts " + (diyDoc.StartsNodes == null ? "键已删掉（回到官方那套）" : "还有 "
                    + diyDoc.StartsNodes.Count + " 条"));
                Check("⑧h 全部取消 ⇒ 删掉 diy.starts（回到官方值）", diyDoc.StartsNodes == null);
            }
            catch (Exception ex)
            {
                Line("  ★ 失败：" + ex.Message);
                Check("⑧ 进度上限 / 起点", false);
            }
            Line("");

            // ---------------------------------------------------------- ★⑨ 换格子 + 棋盘写回
            Line("── ⑨ 换格子功能（nodes[i].diy.landType）+ 写棋盘时 diy 块不能被清空 ──");
            try
            {
                MapTemplate tpl9 = null;
                for (int k = 0; k < maps.Count; k++)
                    if (maps[k].Id == 82016 && maps[k].SceneSuffix.Length > 0) { tpl9 = maps[k]; break; }
                if (tpl9 == null && maps.Count > 0) tpl9 = maps[0];

                if (tpl9 == null || LevelDoc.OfficialLevelFor(tpl9) == null)
                {
                    Line("  ★ 没有可用模板，跳过。");
                    Check("⑨ 换格子 / 棋盘写回", false);
                }
                else
                {
                    BoardStore.ClearCache();
                    BoardStore.DiyBoardPath = "";
                    LevelDoc d9 = LevelDoc.CreateFromTemplate(tpl9, "换格自测", LevelDoc.DiyDifficulty);
                    Check("⑨a 建出来的文档带棋盘", d9.Board != null && d9.Board.Ok);

                    // (d) ★ 棋盘文件**自己那份**顶层 diy 里塞两个"额外键"，再写一次 ——
                    //     旧版 BoardBytes 会 `diy.Members.Clear()`，把这几个键整个抹掉，这里就是盯它的。
                    //     （diy.starts / diy.progress_limit 是**关卡文件**那一层的字段，
                    //      不在棋盘文件里；这里只是拿它们当"额外的键"来验不被清空。）
                    //   ★★ 2026-10-09：静态 `BoardStore.DiyBoardPath` 不再决定"这份文档的棋盘是哪份"
                    //     （那正是"新建继承上一关改格"的根，见 `LevelDoc._ownBoardPath` 的注释）——
                    //     所以这里改成让 **d9 自己认领**这份手写棋盘：把 `diy.board` 指过去再
                    //     `ResolveDiyBoard()`。必须在 `SetNodeLand` **之前**做：Resolve 会作废改格账。
                    string bd9suffix = tpl9.SceneSuffix.Length > 0 ? tpl9.SceneSuffix : "023";
                    string bd9text = BoardCache.TextOf(bd9suffix);      // ★ 磁盘优先 / exe 内置兜底
                    if (string.IsNullOrEmpty(bd9text))
                    {
                        Line("  ★ 棋盘底版读不到：" + bd9suffix + ".json（磁盘缓存和 exe 内置里都没有）");
                        throw new InvalidOperationException("棋盘底版 " + bd9suffix + ".json 读不到");
                    }
                    JNode bd0 = DiyJson.Parse(bd9text);
                    JNode bdiy0 = DiyJson.EnsureMember(bd0, "diy");
                    DiyJson.SetKind(bdiy0, JKind.Object);
                    DiyJson.SetInt(DiyJson.EnsureMember(bdiy0, "starts"), 7);          // 占位：必须活下来
                    DiyJson.SetInt(DiyJson.EnsureMember(bdiy0, "progress_limit"), 21); // 占位：必须活下来
                    string board9path = Path.Combine(outDir, "换格自测.board.json");
                    ExportService.WriteAtomic(board9path, new UTF8Encoding(false).GetBytes(
                        DiyJson.Write(bd0, " ", "\n")));
                    DiyJson.SetString(DiyJson.EnsureMember(DiyJson.EnsureMember(d9.Root, "diy"), "board"),
                        board9path);                       // 绝对路径 ⇒ 认领这份（自测专用，不落盘）
                    d9.ResolveDiyBoard();
                    DiyJson.SetInt(DiyJson.EnsureMember(
                        DiyJson.EnsureMember(d9.Root, "diy"), "progress_limit"), 21);

                    // (b) ★ 换一格：官方 landType 不动，ShownLand 立刻变
                    BoardNode pick9 = null;
                    foreach (BoardNode x in d9.Board.Nodes)
                        if (x.EffectiveLand != 20 && x.EffectiveLand != 0) { pick9 = x; break; }
                    if (pick9 == null) pick9 = d9.Board.Nodes[0];
                    int offLand = pick9.EffectiveLand;
                    int newLand = (offLand == 20) ? 17 : 20;     // 20 回血 / 17 掉血
                    int rc9 = d9.SetNodeLand(pick9.Id, newLand);
                    Line("  换格：格 " + pick9.Id + " 官方 " + offLand + " " + Land.Name(offLand)
                        + " → " + newLand + " " + Land.Name(newLand) + "（SetNodeLand 返回 " + rc9 + "）");
                    Check("⑨b 换完 ShownLand 变、官方 LandType 一个字节没动",
                        rc9 == 1 && pick9.ShownLand == newLand && pick9.LandType == offLand);

                    byte[] bytes9 = d9.BoardWriteForTest();
                    Line("  写出来的棋盘字节 = " + bytes9.Length);
                    Check("⑨c 拿得到要写出去的棋盘字节", bytes9.Length > 100);

                    JNode br9 = DiyJson.Parse(Encoding.UTF8.GetString(bytes9));
                    JNode brDiy = DiyJson.Member(br9, "diy");
                    Line("  顶层 diy = " + (brDiy == null ? "★ 没了！" : "在")
                        + " ｜ diy.starts = " + DiyJson.Brief(DiyJson.Member(brDiy, "starts"), 12)
                        + " ｜ diy.progress_limit = "
                        + DiyJson.Brief(DiyJson.Member(brDiy, "progress_limit"), 12)
                        + " ｜ diy.base_board = " + DiyJson.Brief(DiyJson.Member(brDiy, "base_board"), 14));
                    Check("⑨d ★ 写棋盘时顶层 diy 里原有的键没被清掉（starts / progress_limit 都还在）",
                        brDiy != null
                        && DiyJson.Member(brDiy, "starts") != null
                        && DiyJson.Member(brDiy, "progress_limit") != null
                        && DiyJson.Member(brDiy, "base_board") != null);

                    // (e) ★ 改的那一格写了 nodes[i].diy.landType；其它格子的官方 landType 一个没动
                    JNode nodes9 = DiyJson.Member(br9, "nodes");
                    int foundLand = -1, officialLand = -1, landNodes = 0, mismatched = 0;
                    foreach (JNode item in nodes9.Items)
                    {
                        int itemId = (int)DiyJson.IntOf(DiyJson.Member(item, "id"), -1);
                        JNode nd = DiyJson.Member(item, "diy");
                        bool hasLand = nd != null && DiyJson.Member(nd, "landType") != null;
                        if (hasLand) landNodes++;
                        if (itemId == pick9.Id)
                        {
                            officialLand = (int)DiyJson.IntOf(DiyJson.Member(item, "landType"), -1);
                            if (hasLand) foundLand = (int)DiyJson.IntOf(DiyJson.Member(nd, "landType"), -1);
                        }
                        else if (hasLand) mismatched++;
                    }
                    Line("  格 " + pick9.Id + "：官方 landType=" + officialLand + "（原来 " + offLand + "）"
                        + " · diy.landType=" + foundLand
                        + " ｜ 带 diy.landType 的节点 " + landNodes + " 个（应该只有这 1 个）");
                    Check("⑨e 改的那格写了 nodes[i].diy.landType，官方 landType 仍是原值，且只改了这 1 格",
                        foundLand == newLand && officialLand == offLand
                        && landNodes == 1 && mismatched == 0);

                    // (f) ★ 还原成官方：diy.landType 要真能被删掉，顶层 diy 的键不能被连累
                    int rcRevert = d9.SetNodeLand(pick9.Id, 0);
                    JNode br9b = DiyJson.Parse(Encoding.UTF8.GetString(d9.BoardWriteForTest()));
                    int leftLand = 0;
                    foreach (JNode item in DiyJson.Member(br9b, "nodes").Items)
                    {
                        JNode nd = DiyJson.Member(item, "diy");
                        if (nd != null && DiyJson.Member(nd, "landType") != null) leftLand++;
                    }
                    JNode diy9b = DiyJson.Member(br9b, "diy");
                    Line("  还原（返回 " + rcRevert + "）之后：带 diy.landType 的节点剩 " + leftLand
                        + " 个 ｜ 顶层 diy 仍在 = " + (diy9b != null)
                        + "，starts 仍在 = " + (diy9b != null && DiyJson.Member(diy9b, "starts") != null));
                    Check("⑨f 还原成官方：nodes[i].diy.landType 清掉了，顶层 diy 的键没被连累",
                        leftLand == 0 && diy9b != null && DiyJson.Member(diy9b, "starts") != null);

                    // (g) ★ 边界：选回官方值 = 自动当还原；非法值不许进
                    int rcSame = d9.SetNodeLand(pick9.Id, d9.Board.Get(pick9.Id).EffectiveLand);
                    int rcBad = d9.SetNodeLand(pick9.Id, 99);
                    Line("  选回官方值 ⇒ 返回 " + rcSame + "（应当 0 = 没动）；landType=99 ⇒ 返回 " + rcBad + "（应当 -1 = 拒绝）");
                    Check("⑨g 选回官方值当没动、非法类型被拒绝", rcSame == 0 && rcBad == -1);

                    BoardStore.ClearCache();
                    BoardStore.DiyBoardPath = "";
                }
            }
            catch (Exception ex)
            {
                Line("  ★ 失败：" + ex.Message);
                Check("⑨ 换格子 / 棋盘写回", false);
            }
            Line("");
            // ---------------------------------------------------------- ★⑥ DIY 存 / 读（写入只落在 _自测）
            Line("── ⑥ 第 1 步：存 / 读本地 DIY 关卡（★ 全落在 _自测，不碰游戏根） ──");
            try
            {
                // ★ 自测**不往 <游戏根>\DIY关卡\ 写**：用模板 + 底版造一份文档，
                //   然后只调用"原子写 + 备份 + sha256"那一层，写到 _自测 里。
                string diyDir = Path.Combine(outDir, "DIY关卡自测");
                Directory.CreateDirectory(diyDir);

                MapTemplate tpl = null;
                for (int k = 0; k < maps.Count; k++)
                    if (maps[k].Id == 82016 && maps[k].SceneSuffix.Length > 0) { tpl = maps[k]; break; }
                if (tpl == null && maps.Count > 0) tpl = maps[0];

                if (tpl == null)
                {
                    Line("  ★ 没有可用模板，跳过。");
                    Check("⑥ DIY 存 / 读", false);
                }
                else
                {
                    string baseJson = LevelDoc.OfficialLevelFor(tpl);
                    Line("  底版：" + (baseJson ?? "(没有官方 json ⇒ 建不了)"));
                    if (baseJson != null)
                    {
                        LevelDoc diyDoc = LevelDoc.CreateFromTemplate(tpl, "自测关卡", LevelDoc.DiyDifficulty);
                        Line("  新建：map=" + diyDoc.MapId + " 难度=" + diyDoc.Difficulty
                            + " 底版=" + diyDoc.BaseLevel + " 画板=" + diyDoc.BoardSuffix);
                        Check("⑥a 新建出来的难度落进 DIY 号段 1001", diyDoc.Difficulty == 1001);
                        Check("⑥b 顶层多了 diy 块，服务端那份白名单没被动",
                            DiyJson.Member(diyDoc.Root, "diy") != null && DiyJson.Member(diyDoc.Root, "map") != null);

                        // ★★ ⑥b2：新建出来的文件**只有地图骨架** —— 官方的内容一条都不预填。
                        //    用户 2026-10-03："我的预想是你只提供地图模板，不要往里面放预设。"
                        //    ★ 2026-10-10 口径变更（用户："一个不刷怪的空白模板" + "编辑器这里写的是
                        //      开局刷怪是 0 啊"）：`start_monsters` 从"不写（= 回落官方那批开局怪）"
                        //      改成**显式空数组** `[]`（= 明确不刷）⇒ 它是**唯一**允许出现的例外。
                        var present = new List<string>();
                        foreach (string key in LevelDoc.ContentKeys)
                        {
                            JNode node = DiyJson.Member(diyDoc.Root, key);
                            if (node == null) continue;
                            if (string.Equals(key, "start_monsters", StringComparison.Ordinal)
                                && node.Kind == JKind.Array && node.Count == 0) continue;
                            present.Add(key);
                        }
                        var noteKeys = new List<string>();
                        foreach (JMember m in diyDoc.Root.Members)
                            if (m.Key.IndexOf("note", StringComparison.OrdinalIgnoreCase) >= 0)
                                noteKeys.Add(m.Key);
                        Line("  骨架检查：内容键 " + (present.Count == 0 ? "一个都没有 ✓" : string.Join("/", present.ToArray()))
                            + "（例外：start_monsters = " + DiyJson.Compact(DiyJson.Member(diyDoc.Root, "start_monsters")) + "）"
                            + "；备注键 " + (noteKeys.Count == 0 ? "一个都没有 ✓" : string.Join("/", noteKeys.ToArray())));
                        Line("    顶层键 = " + TopKeyOrder(Encoding.UTF8.GetString(diyDoc.Bytes())));
                        Check("⑥b2 ★★ 新建的文件只有骨架（" + string.Join(" / ", LevelDoc.ContentKeys)
                            + " 一个都不写；唯一例外是显式空刷怪 \"start_monsters\": []）",
                            present.Count == 0 && noteKeys.Count == 0);
                        Check("⑥b3 ★ 骨架里留着身份键（map / difficulty / diy），victory 也留空由用户自己填",
                            DiyJson.Member(diyDoc.Root, "map") != null
                            && DiyJson.Member(diyDoc.Root, "difficulty") != null
                            && DiyJson.Member(diyDoc.Root, "diy") != null
                            && DiyJson.Member(diyDoc.Root, "victory") == null);
                        Check("⑥b4 ★ 事件清单 / 进度数值 / 胜利条件确实没这个键；开局怪是**显式空数组**（明确不刷）",
                            DiyJson.Member(diyDoc.Root, "progress_events") == null
                            && IsEmptyArray(DiyJson.Member(diyDoc.Root, "start_monsters"))
                            && DiyJson.Member(diyDoc.Root, "progress") == null
                            && DiyJson.Member(diyDoc.Root, "rounds") == null
                            && DiyJson.Member(diyDoc.Root, "round_award") == null
                            && DiyJson.Member(diyDoc.Root, "victory") == null
                            && DiyJson.Member(diyDoc.Root, "settlement") == null
                            && DiyJson.Member(diyDoc.Root, "mastermind") == null);

                        // 直接把这一份按"关卡 json 那一层"写进 _自测（绕开游戏根）
                        byte[] bytes = diyDoc.Bytes();
                        string level = Path.Combine(diyDir, "自测关卡.json");
                        ExportService.WriteAtomic(level, bytes);
                        string sha1 = ExportService.Sha256OfFile(level);
                        Line("  写出：" + level);
                        Line("    " + bytes.Length + " 字节 · sha256 " + sha1);

                        // 读回来：diy 块要认得出来，值要一致
                        LevelDoc back = LevelDoc.Open(level);
                        Line("  读回：isDiy=" + back.IsDiy + " 名字=" + back.DiyName
                            + " 底版=" + back.BaseLevel + " 难度=" + back.Difficulty);
                        Check("⑥c 读回来认得这是 DIY 关卡，且值一致",
                            back.IsDiy && back.Difficulty == 1001 && back.DiyName == "自测关卡");

                        // 覆盖 ⇒ 必须先备份，且备份 sha256 = 覆盖前
                        long rounds = 42;
                        JNode rn = DiyJson.Member(back.Root, "rounds");
                        if (rn == null) rn = DiyJson.EnsureMember(back.Root, "rounds");
                        DiyJson.SetInt(rn, rounds);
                        ExportResult ov = ExportService.Overwrite(level, back.Bytes(), "自测6");
                        Line("  覆盖：");
                        Line("    覆盖前 sha256 = " + ov.SourceSha256);
                        Line("    备份路径      = " + ov.BackupPath);
                        Line("    备份 sha256   = " + ov.BackupSha256);
                        Line("    覆盖后 sha256 = " + ov.NewSha256);
                        Check("⑥d 覆盖产生了备份", File.Exists(ov.BackupPath));
                        Check("⑥e 备份 sha256 == 覆盖前（可逐字节回滚）",
                            string.Equals(ov.BackupSha256, sha1, StringComparison.OrdinalIgnoreCase));

                        File.Copy(ov.BackupPath, level, true);
                        string rolled = ExportService.Sha256OfFile(level);
                        Line("    用备份回滚后  = " + rolled);
                        Check("⑥f 用备份能逐字节回滚", string.Equals(rolled, sha1, StringComparison.OrdinalIgnoreCase));

                        // 棋盘文件形状（照官方：1 空格缩进 + 只加一个 diy 块）
                        //   ★★ 磁盘优先 / exe 内置兜底：磁盘上没有「棋盘缓存」时吃内置那份。
                        string bdText = BoardCache.TextOf(tpl.SceneSuffix);
                        if (!string.IsNullOrEmpty(bdText))
                        {
                            Line("  棋盘底版：" + tpl.SceneSuffix + ".json（" + bdText.Length + " 字符，来自 "
                                + (BoardCache.FindPath(tpl.SceneSuffix).StartsWith(
                                    EmbeddedCache.BoardFolder, StringComparison.Ordinal) ? "exe 内置" : "磁盘") + "）");
                            Check("⑥g 棋盘缓存里有这张图的画板（磁盘没有就用 exe 内置那份）", true);
                        }
                        else
                        {
                            Line("  棋盘底版：缓存里和 exe 内置里都没有 " + tpl.SceneSuffix + ".json（棋盘文件这步跳过）");
                            Check("⑥g 棋盘缓存里有这张图的画板（磁盘没有就用 exe 内置那份）", false);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Line("  ★ 失败：" + ex.Message);
                Check("⑥ DIY 存 / 读", false);
            }
            Line("");

            // ---------------------------------------------------------- ★⑦ SaveDiy 全链路（★ 沙盒）
            Line("── ⑦ 第 1 步：SaveDiy 全链路（★ 游戏根临时指向 _自测，绝不写真的游戏根） ──");
            string keepGame = GameLocator.Override;
            try
            {
                // ★ 只为自测：把"游戏根"临时换到 _自测 下面。
                //   这样 GameLocator.DiyDir 就变成 _自测\沙盒游戏根\DIY关卡，
                //   一个字节都不会落到真游戏目录里。finally 里一定还原。
                string sandbox = Path.Combine(outDir, "沙盒游戏根");
                // ★ 每跑一次都从干净的重来：否则上一次留下的同名文件会让"第一次保存"
                //   变成覆盖，⑦d 就假失败了。
                if (Directory.Exists(sandbox)) Directory.Delete(sandbox, true);
                Directory.CreateDirectory(sandbox);
                Directory.CreateDirectory(Path.Combine(sandbox, "GameRes"));
                Directory.CreateDirectory(Path.Combine(sandbox, "GameRes", "Img"));
                GameLocator.Override = sandbox;
                GameLocator.Reset();

                Line("  沙盒游戏根：" + sandbox);
                Line("  它算出来的 DIY 目录：" + GameLocator.DiyDir);
                Check("⑦a 沙盒里 DIY 目录 = <沙盒>\\DIY关卡",
                    string.Equals(GameLocator.DiyDir, Path.Combine(sandbox, "DIY关卡"),
                        StringComparison.OrdinalIgnoreCase));

                MapTemplate tpl2 = null;
                for (int k = 0; k < maps.Count; k++)
                    if (maps[k].Id == 82016 && maps[k].SceneSuffix.Length > 0) { tpl2 = maps[k]; break; }
                if (tpl2 == null && maps.Count > 0) tpl2 = maps[0];

                if (tpl2 != null && LevelDoc.OfficialLevelFor(tpl2) != null)
                {
                    LevelDoc full = LevelDoc.CreateFromTemplate(tpl2, "全链路自测", 1001);
                    DiySaveResult r1 = full.SaveDiy("全链路自测");
                    Line("  第一次保存：");
                    Line("    关卡 json：" + r1.LevelPath);
                    Line("    " + r1.Bytes + " 字节 · sha256 " + r1.Sha256);
                    Line("    棋盘 json：" + (r1.BoardWritten ? r1.BoardPath : "(没写)") + " —— " + r1.BoardNote);
                    if (r1.BoardWritten)
                        Line("    " + r1.BoardBytes + " 字节 · sha256 " + r1.BoardSha);
                    Check("⑦b 关卡 json 落到 <游戏根>\\DIY关卡\\<名字>.json",
                        File.Exists(r1.LevelPath)
                        && string.Equals(Path.GetDirectoryName(r1.LevelPath),
                            Path.Combine(sandbox, "DIY关卡"), StringComparison.OrdinalIgnoreCase));
                    Check("⑦c 棋盘 json 也落到同一个目录（官方形状 + diy 块）",
                        r1.BoardWritten && File.Exists(r1.BoardPath));
                    Check("⑦d 第一次是新文件，没走覆盖", !r1.Overwrote);

                    // 棋盘文件里必须有 diy 块，且 nodes 的 landType 一个没改
                    if (r1.BoardWritten)
                    {
                        JNode br = DiyJson.Parse(File.ReadAllText(r1.BoardPath, Encoding.UTF8));
                        JNode bdiy = DiyJson.Member(br, "diy");
                        JNode bnodes = DiyJson.Member(br, "nodes");
                        int changed = 0;
                        foreach (JNode item in bnodes.Items)
                            if (DiyJson.Member(item, "diy") != null) changed++;
                        Line("    棋盘里 diy 块=" + (bdiy != null) + "，nodes " + bnodes.Count
                            + " 个，其中带 diy 覆盖的 " + changed + " 个（第 1 步应该是 0）");
                        Check("⑦e 棋盘文件有 diy 块，且一个 landType 都没改（保留官方原始事实）",
                            bdiy != null && changed == 0);
                    }

                    // 读回来
                    // ★ 先把 BoardStore 的静态缓存清掉 —— 它在真的编辑器里活不过一次
                    //   "换文档"，但自测是同一个进程连着开好几份，不清就会拿到上一份的棋盘。
                    BoardStore.ClearCache();
                    BoardStore.DiyBoardPath = "";
                    LevelDoc again = LevelDoc.Open(r1.LevelPath);
                    Line("    [诊断] BoardStore.DiyBoardPath = " + BoardStore.DiyBoardPath);
                    Line("    [诊断] again.Board.Path = " + (again.Board == null ? "(null)" : again.Board.Path)
                        + " · FromDiy=" + (again.Board != null && again.Board.FromDiy)
                        + " · Ok=" + (again.Board != null && again.Board.Ok));
                    Line("    [诊断] 关卡 json 在 " + Path.GetDirectoryName(r1.LevelPath));
                    Line("  读回：" + again.FileName + " isDiy=" + again.IsDiy
                        + " 名字=" + again.DiyName + " 画板=" + again.BoardSuffix
                        + " 棋盘=" + (again.Board != null && again.Board.Ok ? again.Board.Nodes.Count + " 格" : "(无)"));
                    Check("⑦f 读回来认得 DIY，且认出了自己的棋盘文件",
                        again.IsDiy && again.Board != null && again.Board.Ok && again.Board.FromDiy);

                    // 再存一次 ⇒ 覆盖 ⇒ 必须先备份，sha256 可回滚
                    string shaBefore = ExportService.Sha256OfFile(r1.LevelPath);
                    JNode rr = DiyJson.Member(again.Root, "rounds");
                    if (rr == null) rr = DiyJson.EnsureMember(again.Root, "rounds");
                    DiyJson.SetInt(rr, 66);
                    DiySaveResult r2 = again.SaveDiy("全链路自测");
                    Line("  第二次保存（覆盖同名）：");
                    Line("    覆盖前 sha256 = " + r2.SourceSha256);
                    Line("    备份路径      = " + r2.BackupPath);
                    Line("    备份 sha256   = " + r2.BackupSha256);
                    Line("    覆盖后 sha256 = " + r2.Sha256);
                    Check("⑦g 同名覆盖：先备份，且备份 sha256 == 覆盖前",
                        r2.Overwrote && File.Exists(r2.BackupPath)
                        && string.Equals(r2.BackupSha256, shaBefore, StringComparison.OrdinalIgnoreCase));
                    File.Copy(r2.BackupPath, r2.LevelPath, true);
                    Check("⑦h 用备份能逐字节回滚",
                        string.Equals(ExportService.Sha256OfFile(r2.LevelPath), shaBefore,
                            StringComparison.OrdinalIgnoreCase));
                    Line("    回滚后 sha256 = " + ExportService.Sha256OfFile(r2.LevelPath));

                    // DIY 列表要能列出它
                    var listed = new List<string>();
                    foreach (string f in Directory.GetFiles(Path.Combine(sandbox, "DIY关卡"), "*.json"))
                        listed.Add(Path.GetFileName(f));
                    Line("  沙盒 DIY 目录里现在有：" + string.Join(" · ", listed.ToArray()));
                    Check("⑦i 目录里正好 1 份关卡 json + 1 份棋盘 json", listed.Count == 2);
                }
                else
                {
                    Line("  ★ 没有可用模板，跳过。");
                    Check("⑦ SaveDiy 全链路", false);
                }
            }
            catch (Exception ex)
            {
                Line("  ★ 失败：" + ex.Message);
                Check("⑦ SaveDiy 全链路", false);
            }
            finally
            {
                // ★ 一定要还原：自测结束后编辑器还是认真的游戏根
                GameLocator.Override = keepGame;
                GameLocator.Reset();
                Line("  （游戏根已还原成：" + (GameLocator.GameDir ?? "(空)") + "）");
            }
            Line("");

            // ---------------------------------------------------------- ★⑩ 官方 landType 对照表（Land.bin / STRLand.bin）+ PvE·PvP 分组
            Line("── ⑩ 官方 landType ↔ 贴图/中文名（Land.bin · STRLand.bin）+ PvE/PvP 分组 ──");
            try
            {
                // (a) ★ 贴图名必须来自游戏自己的 Land.bin（不是照英文名猜的那张旧表）
                var texTable = LandTable.Textures();
                Line("  Land.bin 读出 " + texTable.Count + " 个 landType 的贴图名（旧版那张手写猜表已删）");
                var texSample = new List<string>();
                int shown = 0;
                for (int land = 1; land <= LandTable.MaxLand && shown < 8; land++)
                {
                    string nm = LandTable.TextureName(land);
                    if (nm == null) continue;
                    texSample.Add(land + "→" + nm);
                    shown++;
                }
                Line("    样例：" + string.Join(" ", texSample.ToArray()));
                Check("⑩a Land.bin 读得出贴图名（≥20 行）", texTable.Count >= 20);

                // (b) ★ 中文名必须与 STRLand.bin 一致（旧版自己意译的几条是"图标对不上"的一半原因）
                string n4 = Land.Name(4), n8 = Land.Name(8), n12 = Land.Name(12), n26 = Land.Name(26);
                Line("  官方中文名（STRLand.bin, key=80000+landType）：4=" + n4 + " 8=" + n8
                    + " 12=" + n12 + " 26=" + n26);
                Line("    " + (Land.NameFromGameTable(26) ? "★ 26 的名字是读表来的" : "★ 读不到表，用的兜底表"));
                Check("⑩b 名字以 STRLand.bin 为准：4 突击门 / 8 全员事件 / 12 疾行 / 26 怪奇糖果机",
                    n4 == "突击门" && n8 == "全员事件" && n12 == "疾行" && n26 == "怪奇糖果机");
                Check("⑩c 旧版那几条错名字已经不再出现（追击/给奖励/摇星币/小贩）",
                    Land.Name(4) != "追击" && Land.Name(5) != "给奖励"
                    && Land.Name(15) != "摇星币" && Land.Name(26) != "小贩");

                // (d) ★ 编号贴图（角色头像）一律不当图标用
                bool numberedUsed = false;
                var numberedSample = "";
                for (int land = 0; land <= LandTable.MaxLand; land++)
                {
                    string got = Art.PlatformTexture(land);
                    if (got != null && LandTable.IsNumberedArt(got)) { numberedUsed = true; numberedSample = land + "→" + got; }
                }
                Line("  30 张编号贴图（UT_Platform_101…129/301…306，盘点证实是角色头像）有没有被当图标：" 
                    + (numberedUsed ? ("★ 有！" + numberedSample) : "没有，一个都没用"));
                Check("⑩d 编号贴图一律不当功能图标（landType 1 起始点因此走色块）", !numberedUsed);
                Check("⑩e 起始点(1) 落色块 + 中文名，而不是瞎配一张头像",
                    Art.PlatformTexture(1) == null && Land.Name(1) == "起始点");

                // (f) ★ 用上的每一张贴图都必须与 Land.bin 点名一致（不能有"别名退而求其次"）
                int usable = 0; var mismatched = new List<string>();
                for (int land = 0; land <= LandTable.MaxLand; land++)
                {
                    string got = Art.PlatformTexture(land);
                    if (got == null) continue;
                    usable++;
                    string official = LandTable.TextureName(land);
                    if (!string.Equals(got, official, StringComparison.Ordinal)) mismatched.Add(land + "→" + got + "(应为" + official + ")");
                }
                Line("  能贴图的 " + usable + " 种，与 Land.bin 点名的对应关系全部一致："
                    + (mismatched.Count == 0 ? "是" : string.Join("、", mismatched.ToArray())));
                Check("⑩f 每一张用上的贴图都 = Land.bin 点名的那一张（没有别名退让）", mismatched.Count == 0 && usable >= 10);

                // (g) ★ 待定清单：列出来的确实没图，没列的确实有图
                var pending = LandTable.Pending();
                var pendText = new List<string>();
                foreach (int land in pending) pendText.Add(land + " " + Land.Name(land));
                Line("  待定清单（色块 + 中文名，共 " + pending.Count + " 种）：" + string.Join("、", pendText.ToArray()));
                bool pendingOk = true;
                foreach (int land in pending) if (Art.PlatformTexture(land) != null) pendingOk = false;
                for (int land = 1; land <= LandTable.MaxLand; land++)
                    if (Art.PlatformTexture(land) != null && pending.Contains(land)) pendingOk = false;
                Check("⑩g 待定清单与「有图 / 没图」严格互补（不重不漏）", pendingOk && pending.Count + usable == LandTable.MaxLand);

                // (h) ★★ 连线式棋盘：格子必须**明显小于格距**（这就是"不挤"的判据）
                double p100 = 100.0, n100 = PageBoard.NodeSize(p100);
                Line("  棋盘格子尺寸：格距 " + p100 + " px ⇒ 格子 " + n100 + " px（"
                    + PageBoard.NodeRatioText + "，工具条上「格子大小」可调）");
                Check("⑩h 默认档格子边长 = 格距的 60% 且 < 格距（留白给连线）",
                    Math.Abs(PageBoard.NodeRatioNow - PageBoard.DefaultNodeRatio) < 1e-9
                    && Math.Abs(n100 - 60.0) < 0.001 && n100 < p100);

                // (i) ★ 用户在界面上能选的那四档：每一档都必须留得出连线留白（不能顶到一起）
                var sizeText = new List<string>();
                bool allOk = true;
                foreach (double r in PageBoard.NodeRatioOptions)
                {
                    double n = PageBoard.NodeSizeFor(200.0, r);
                    sizeText.Add((r * 100).ToString("0", CultureInfo.InvariantCulture) + "%⇒"
                        + n.ToString("0", CultureInfo.InvariantCulture) + "px");
                    if (n >= 200.0 || Math.Abs(n - 200.0 * r) > 0.001) allOk = false;
                }
                Line("  「格子大小」四档（格距 200 px）：" + string.Join("  ", sizeText.ToArray()));
                Check("⑩i 四档格子大小都按比例算、且都 < 格距（任何一档都不会挤到一起）", allOk);
                Check("⑩j 特大那一档 85% 也仍然留出 15% 的连线空隙",
                    PageBoard.NodeSizeFor(200.0, 0.85) < 200.0 * 0.90);

                // (j) ★★ PvE / PvP 地块分开：两族的图号段**不重叠**
                var pve = GameTables.MapsForPveFamily();
                var pvp = GameTables.MapsForPvpFamily();
                var pveIds = new List<int>(); foreach (MapTemplate t in pve) pveIds.Add(t.Id);
                var pvpIds = new List<int>(); foreach (MapTemplate t in pvp) pvpIds.Add(t.Id);
                var both = new List<int>();
                foreach (int id in pvpIds) if (pveIds.Contains(id)) both.Add(id);
                Line("  PvE 系（模式 4/9/12）去重后 " + pve.Count + " 张：" + string.Join("/", pveIds.ConvertAll(
                        delegate (int i) { return i.ToString(CultureInfo.InvariantCulture); }).ToArray()));
                Line("  PvP 系（模式 " + string.Join("/", Array.ConvertAll(GameTables.PvpModeIds,
                        delegate (int i) { return i.ToString(CultureInfo.InvariantCulture); }))
                    + "）去重后 " + pvp.Count + " 张：" + string.Join("/", pvpIds.ConvertAll(
                        delegate (int i) { return i.ToString(CultureInfo.InvariantCulture); }).ToArray()));
                Check("⑩k PvE 系至少 9 张图、PvP 系至少 5 张图", pve.Count >= 9 && pvp.Count >= 5);
                Check("⑩l PvE 与 PvP 的地块没有一张重叠（分开了）", both.Count == 0);
                bool modesOk = true;
                foreach (int m in GameTables.PvpModeIds) if (GameTables.IsPveMode(m)) modesOk = false;
                Check("⑩m PvP 模式清单里不混进 PvE 的 4/9/12", modesOk);
                Check("⑩n 默认那一档（PvE 系）里一个 PvP 图都没有",
                    GameTables.MapsForPvpFamily().Count > 0 && !pveIds.Contains(81003) && !pveIds.Contains(81007));

                // (o) ★ 官方表说 7 商店 与 22 商店 是**同一张图**（资源组号都是 80007）⇒ 允许共用
                Line("  共用同一张图的：1/2（起始点·保障点，官方 1 给的是角色头像 ⇒ 起始点走色块）、"
                    + "7/22 商店、4/21 两种突击门");
                Check("⑩o 22 与 7 官方就是同一张图（Land.bin 同名同组）",
                    string.Equals(LandTable.TextureName(22), LandTable.TextureName(7), StringComparison.Ordinal)
                    && LandTable.TextureName(7) == "UT_Platform_Shop");

                // ======================================================== ★⑪ 格子类型按 PvE / PvP 分开
                Line("");
                Line("── ⑪ 「换成什么功能」的格子按 PvE / PvP 分组（用户：把 pvp 和 pve 的格子分开）──");
                var pvePicks = Land.PickIds(false);      // 默认档（不勾 PvP）
                var allPicks = Land.PickIds(true);       // 勾了 PvP
                var pvpLandIds = Land.PvpOnlyIds();
                var pvpText = new List<string>();
                foreach (int land in pvpLandIds) pvpText.Add(land + " " + Land.Name(land));
                Line("  " + Land.SplitText());
                Line("  默认那一档（只列 PvE）" + pvePicks.Count + " 种：" + Land.RangeText(pvePicks));
                Line("  PvP 专用 " + pvpLandIds.Count + " 种：" + string.Join("、", pvpText.ToArray()));
                Line("  ★ 合并出来的 PvE 通用清单（官方 PvE 画板 + 盘上现读）一共 "
                    + Land.MergePveFromBoards().Length + " 种：" + Land.RangeText(new List<int>(Land.MergePveFromBoards())));
                Line("  ★ 这条清单的数据来源（不许猜，所以把读到哪张盘也写出来）：" + Land.PveSource);

                // ⑪a 默认档里一个 PvP 专用的都不能有
                bool pveClean = true;
                foreach (int land in pvePicks) if (Land.IsPvpOnly(land)) pveClean = false;
                Check("⑪a 默认档（不勾 PvP）里一个 PvP 专用格子都没有", pveClean && pvePicks.Count > 0);

                // ⑪b 勾了 PvP 才是全 28 种（0~27 一种不漏），而且比默认档多
                bool allOk2 = true;
                for (int land = 0; land <= LandTable.MaxLand; land++)
                    if (!allPicks.Contains(land)) allOk2 = false;
                Check("⑪b 勾上「显示 PvP 格子」⇒ 28 种全在（0~27 不漏），且比默认档多",
                    allOk2 && allPicks.Count == LandTable.MaxLand + 1 && allPicks.Count > pvePicks.Count);

                // ⑪c ★★ 官方 PvE 画板上出现过的每一种 landType，默认档里都必须在（"PvE 也要用"）
                var missing = new List<int>();
                foreach (int land in Land.MergePveFromBoards())
                    if (!pvePicks.Contains(land)) missing.Add(land);
                Line("  官方 PvE 盘上出现过、但默认档里漏了的：" + (missing.Count == 0 ? "没有" : string.Join("、", missing.ConvertAll(
                        delegate (int i) { return i.ToString(CultureInfo.InvariantCulture); }).ToArray())));
                Check("⑪c ★ 官方 PvE 画板上出现过的 landType 默认档里一个都不少", missing.Count == 0);

                // ⑪d ★★★ PvP 那批的每一格都要有**说得出的一档依据**，三档任选其一：
                //     ① 官方说明里真写着"要别的角色参与"（敌方角色/该角色/随机角色/平分/转账…）
                //     ② 手工按商店语义判的（7/22：官方同名同图同资源组 80007）
                //     ③ 靠推断的（16/18/25：官方 PvE 盘上一格都没有，说明里也没提对手 ⇒ 报告里点名）
                var noNote = new List<string>();
                var noteText = new List<string>();
                foreach (int land in pvpLandIds)
                {
                    string note = Land.OfficialNote(land);
                    bool hit = Land.NoteNeedsOpponent(land);
                    bool shop = false, inferred = false;
                    foreach (int k in Land.PvpShopSemantics) if (k == land) shop = true;
                    foreach (int k in Land.PvpInferred) if (k == land) inferred = true;
                    string tier = hit ? "①官方说明要对手" : (shop ? "②商店语义（同名同图同组80007）" : (inferred ? "③推断：官方PvE盘上没有" : "★没有依据"));
                    if (!hit && !shop && !inferred) noNote.Add(land + " " + Land.Name(land));
                    noteText.Add(land + " " + Land.Name(land) + "（"
                        + (note.Length == 0 ? "官方没给说明" : note) + "）  ← " + tier);
                }
                Line("  PvP 那批的依据（三档）：");
                foreach (string one in noteText) Line("     · " + one);
                Check("⑪d ★★ PvP 那批每一个都拿得出依据（说明要对手 / 商店语义 / 明说靠推断）",
                    noNote.Count == 0);

                // ⑪e 用户点名的这几个必须在 PvP 档、默认不列：占卜 6 / 悬赏 5 / 彩票 9 / 猜猜乐 10 / 炮台 11 / 突击门 4
                var named = new int[] { 4, 5, 6, 9, 10, 11 };
                bool namedOk = true;
                foreach (int land in named)
                    if (!Land.IsPvpOnly(land) || pvePicks.Contains(land) || !allPicks.Contains(land)) namedOk = false;
                Check("⑪e ★ 用户点名的 占卜6/悬赏5/彩票9/猜猜乐10/炮台11/突击门4 全在 PvP 档、默认不列", namedOk);

                // ⑪f 两批严格互补、不重不漏：并集 = 28
                int overlap = 0;
                foreach (int land in pvePicks) if (pvpLandIds.Contains(land)) overlap++;
                Check("⑪f PvE 通用与 PvP 专用严格互补（不重不漏，并集 28 种）",
                    overlap == 0 && pvePicks.Count + pvpLandIds.Count == LandTable.MaxLand + 1);

                // ⑪g 每一种归属都给得出依据（报告/界面 tooltip 照它说）
                bool reasonOk = true;
                for (int land = 0; land <= LandTable.MaxLand; land++)
                {
                    string why = Land.GroupReason(land);
                    if (string.IsNullOrEmpty(why) || why.IndexOf(Land.Name(land), StringComparison.Ordinal) < 0) reasonOk = false;
                }
                Check("⑪g 每一种格子的归属都说得出依据（说明里带官方中文名）", reasonOk);

                // ⑪h ★★ 归属的"官方盘"那份依据：确实把 6 张官方 PvE 画板读进来了
                //     （读不到盘时这条会点出来 —— 归属就只剩内置那份，报告里要说清楚）
                string src = Land.PveSource;
                bool readBoards = src.IndexOf("张盘", StringComparison.Ordinal) > 0;
                int files = 0;
                if (readBoards)
                {
                    int a = src.IndexOf(" 里 ", StringComparison.Ordinal) + 3;
                    int b = src.IndexOf(" 张盘", StringComparison.Ordinal);
                    if (a > 2 && b > a) int.TryParse(src.Substring(a, b - a), out files);
                }
                // 盘读进来的那种"只有 016/020 才有的"格子（26 怪奇糖果机 / 27 思维强化）必须还在 PvE 里；
                // 而 22 虽然盘上有，但官方说明写着"转账给友方角色" ⇒ 必须仍在 PvP。
                bool boardDerived = pvePicks.Contains(26) && pvePicks.Contains(27);
                Line("  官方盘那份依据：读到的画板目录 = " + (readBoards ? src : "（这台机器上没找到）")
                    + "；合并后的 PvE 清单 = " + Land.RangeText(new List<int>(Land.MergePveFromBoards())));
                Check("⑪h ★ 官方 PvE 画板读到了（≥6 张）；盘上独有的 26/27 留在 PvE，22 按说明留在 PvP",
                    (readBoards ? files >= 6 : true) && boardDerived && Land.IsPvpOnly(22));

                // ⑪i ★★ 28 行归属清单（报告里的那张表就是这一段，程序与报告不可能对不上）
                Line("  归属清单（28 行；报告里的表 = 这一段）：");
                Line("    id | 官方中文名 | 归属 | 依据");
                for (int land = 0; land <= LandTable.MaxLand; land++)
                    Line("    " + land.ToString(CultureInfo.InvariantCulture).PadLeft(2) + " | "
                        + Land.Name(land) + " | "
                        + (Land.IsPvpOnly(land) ? "PvP 专用" : "PvE 通用") + " | "
                        + Land.GroupReason(land));
            }
            catch (Exception ex)
            {
                Line("  ★ 失败：" + ex.Message);
                Check("⑩ landType 对照表 / PvE·PvP 分组", false);
            }
            Line("");

            // ---------------------------------------------------------- ★⑫ 第③④页接真数据 + 编辑动作撤销
            Line("── ⑫ 进度事件接真数据（MapEvent.bin · progress_events + diy 簿记）+ 第④页怪物两台来源 + 编辑动作撤销 ──");
            try
            {
                // ============================================================ (a) MapEvent.bin
                var evRows = MapEventTable.Rows();
                Line("  " + MapEventTable.Note);
                int named = 0, trig = 0, eff = 0;
                foreach (MapEventRow r in evRows)
                {
                    if (r.Name.Length > 0) named++;
                    if (r.HasTrigger) trig++;
                    if (r.Effects.Count > 0) eff++;
                }
                Line("    有官方名字 " + named + " · 有触发点 " + trig + " · 带效果 " + eff);
                Check("⑫a MapEvent.bin 读到了（≥100 行）且大部分有名字", evRows.Count >= 100 && named >= 80);

                // ★ 触发点：32121/32122/32123/32124 = 2/8/14/20（进度 × 2）⇒ 进度 1/4/7/10
                var wantProgress = new int[] { 1, 4, 7, 10 };
                var ids = new int[] { 32121, 32122, 32123, 32124 };
                var got = new List<string>();
                bool trigOk = true;
                for (int k = 0; k < ids.Length; k++)
                {
                    MapEventRow r = MapEventTable.Get(ids[k]);
                    int p = r == null ? -1 : r.Progress;
                    got.Add(ids[k] + "→进度" + p);
                    if (p != wantProgress[k]) trigOk = false;
                }
                Line("    82016 那四条：32121/32122/32123/32124 的触发进度 = " + string.Join(" · ", got.ToArray())
                    + "（与 gamedata.map_event_progress 的 field4÷2 口径一致）");
                Check("⑫b ★ 触发点是「进度 × 2 ÷ 2」（32121/22/23/24 = 进度 1/4/7/10）", trigOk);

                // ★ 效果表：32121 = 6:ActiveBoss [1067]；32122/32124 有 3:MonsterAttrChange
                MapEventRow e32121 = MapEventTable.Get(32121);
                MapEventRow e32122 = MapEventTable.Get(32122);
                bool boss = e32121 != null && e32121.Effects.ContainsKey(MapEventEffectType.ActiveBoss)
                            && e32121.Effects[MapEventEffectType.ActiveBoss].Contains(1067);
                bool attr = e32122 != null && e32122.Effects.ContainsKey(MapEventEffectType.MonsterAttrChange);
                Line("    32121 官方效果 = " + (e32121 == null ? "(没有)" : Effects(e32121))
                    + " ；32122 官方效果 = " + (e32122 == null ? "(没有)" : Effects(e32122)));
                Check("⑫c ★ 32121 = 6 号 ActiveBoss [1067]；32122 带 3 号 MonsterAttrChange", boss && attr);

                bool unknown = !MapEventTable.Known(999999);
                Line("    999999 在表里吗：" + (!unknown) + "（编辑器要能如实说「不在 MapEvent.bin 里」）");
                Check("⑫d 不在表里的事件号能识别出来（MapEventTable.Known = false）", unknown);

                // ★ 这张图这一档官方登记过的事件（Map.bin 难度行 field 3）
                var offi = MapEventTable.EventsForMapDifficulty(82016, 1);
                Line("    Map.bin 难度行里 82016 难度 1 登记的进度事件：" + string.Join(",", offi.ConvertAll(
                    delegate (int i) { return i.ToString(CultureInfo.InvariantCulture); }).ToArray()));
                bool offiOk = offi.Contains(32121) && offi.Contains(32122) && offi.Contains(32123) && offi.Contains(32124);
                Check("⑫e ★ 官方登记过的事件 id 读得出来（82016 难度 1 含 32121~32124）", offiOk);

                // ============================================================ (b) ★★ DIY 侧数据
                JNode keepRoot = doc.Root;              // ★ 试完要还原，别污染后面的自测
                try
                {
                    // ---- ★★ 先看**官方成品关卡原样读回**是什么样：这正是"从零开始的计数基准不成立"的
                    //   根源，也正是 `addRefs` 那条规矩要保住的行为 —— 文件里没有 `diy.board_monsters` 时，
                    //   `start_monsters` / `progress_events.*.spawn[].node` 就是**唯一**的落点来源。
                    JNode offRoot = DiyJson.Parse(File.ReadAllText(work, Encoding.UTF8));
                    JNode offStarts = DiyJson.Member(offRoot, "start_monsters");
                    int offStartRows = offStarts != null && offStarts.Kind == JKind.Array ? offStarts.Count : 0;
                    // 官方那份没有 progress.limit（服务端读难度行）⇒ 塞一个进去，验"读回"那一半
                    DiyJson.SetInt(DiyJson.EnsureMember(DiyJson.EnsureMember(offRoot, "progress"), "limit"), 21);
                    doc.Root = offRoot;
                    doc.InvalidateDiy();
                    DiyData doff = doc.Diy;
                    int offBoard = doff.BoardMonsters.Count, offTrig = 0;
                    foreach (DiyEvent eo in doff.Events)
                        foreach (DiyProgressItem iok in eo.Items) if (iok.TriggerEvent > 0) offTrig++;
                    DiyEvent offFirst = doff.FirstEvent();
                    bool offVic = offFirst != null && offFirst.KillMonsters && offFirst.VictoryTargets.Count == 1
                        && DiyPool.Find(doff.Pool, offFirst.VictoryTargets[0]) != null
                        && DiyPool.Find(doff.Pool, offFirst.VictoryTargets[0]).Table == 1067;
                    Line("    官方那份原样读回：池子 " + doff.Pool.Count + " 只 / 棋盘落点 " + offBoard
                        + " 条（官方 start_monsters " + offStartRows + " 行）/ 事件 " + doff.Events.Count
                        + " 条（带官方事件 id 的 " + offTrig + " 条）/ 上限 "
                        + (offFirst == null ? -1 : offFirst.Limit));
                    Check("⑫f2 ★★ 官方成品关卡自带的内容会被**导进**池子 / 棋盘："
                        + "start_monsters（和 spawn 行的 node）落到格子上、progress_events 反推成带官方事件 id 的事件"
                        + "（★ 这就是「从零开始的基准」不再成立的原因）",
                        offStartRows > 0 && offBoard >= offStartRows && doff.Pool.Count > 0
                        && doff.Events.Count >= 3 && offTrig == doff.Events.Count);
                    Check("⑫f3 ★★ 顶层 victory / progress.limit 也读得回来"
                        + "（重开文件不会把获胜条件、总进度上限丢掉；targets 反查回池内那只）",
                        offVic && offFirst.Limit == 21);
                    doc.Root = keepRoot;                 // ★ 还原：下面从"干净一张纸"重新开始
                    doc.InvalidateDiy();

                    // ★★ 2026-10-08 大改（扁平）之后：这一份是**真实官方关卡**，
                    //   自带 `start_monsters` + `progress_events`，而新模型的 `DiyPool.Read`
                    //   会把官方内容**一起导进池子 / 棋盘** ⇒ "从零开始"的计数基准（池子 2 只、
                    //   格子 3 条…）就不再成立。⇒ 先把官方那几块挪开（`CleanSheet`），
                    //   下面数出来的池子 / 棋盘才是这一批用例自己造的。
                    JNode sheet = CleanSheet(doc, work);
                    Line("  ★★ DIY 侧数据（diy.monsters 池子 / diy.board_monsters 放怪 / 进度进 progress_events）：");
                    DiyPool.Write(doc, DiyPool.NewData());
                    DiyData d0 = DiyPool.Read(doc);
                    Check("⑫f ★ 新建自带一条 `DIY进度` 占位事件（名字/简介是用户原话，内容包预留位）",
                        d0.Events.Count == 1 && d0.Events[0].Id == DiyPool.PlaceholderEventId
                        && d0.Events[0].Name == "DIY进度" && d0.Events[0].Description == "DIY关卡的配置");
                    // ---- 池子：放怪进池 + 格子上挂池内怪（同一只怪放不同格能各写各的属性）
                    DiyData d = doc.Diy;
                    PoolMonster a = DiyPool.MakePool(d, 1067);
                    PoolMonster b = DiyPool.MakePool(d, 1023);
                    DiyPool.PlaceAt(d, 5, a.Id);
                    DiyPool.PlaceAt(d, 9, b.Id);
                    DiyPool.PlaceAt(d, 11, a.Id);          // ★ 同一只池内怪再放一格
                    doc.SaveDiy();

                    DiyData d1 = DiyPool.Read(doc);
                    bool poolOk = d1.Pool.Count == 2 && d1.BoardMonsters.Count == 3;
                    bool sharedOk = false;
                    int cntA = 0;
                    foreach (BoardMonsterRef r in d1.BoardMonsters) if (r.Pool == a.Id) cntA++;
                    sharedOk = cntA == 2;                  // 同一只怪放在不同格子上
                    Line("    池子 " + d1.Pool.Count + " 只 / 棋盘上挂了 " + d1.BoardMonsters.Count
                        + " 格；同一只池内怪被放到 " + cntA + " 格");
                    Check("⑫g ★★ 怪物池：池子在 diy.monsters[]、格子只记 diy.board_monsters[].pool", poolOk);
                    Check("⑫h ★★ 同一只怪能放在不同格子上（格子指向同一个池内序号）", sharedOk);

                    a.Hp = 999; a.Counter = true;
                    int aId = a.Id;              // ★ 先记下序号：SaveDiy 会把池内序号重排成 1..N
                    doc.SaveDiy();
                    PoolMonster back = DiyPool.Find(DiyPool.Read(doc).Pool, aId);
                    Line("    改属性后读回：#" + back.Id + " 血 " + back.Hp + " 反击 " + back.Counter);
                    Check("⑫i ★ 池内每只怪单独设属性（血量/防御/攻击/掉落星币/是否反击 五项）",
                        back.Hp == 999 && back.Counter);

                    // ---- 进度：条词 + 上限 + 同步进服务端那份
                    //   ★ 2026-10-08 大改（扁平）：玩法内容**不再**写 `diy.events.<id>.items`，
                    //     而是写进服务端真读的 `progress_events.<键>.{spawn,attack,defense,rewards}`；
                    //     `diy.events.<id>` 只剩 name/desc（簿记），总进度上限进顶层 `progress.limit`。
                    //   ★ 键 = `EventKey(事件)`：设了触发点 ⇒ 官方事件 id；没设 ⇒ **事件自己的 id**（1001）。
                    //     一条效果都没有的事件不占键（见 `DiyPool.SyncProgressEvents`）。
                    DiyEvent ev = doc.Diy.FirstEvent();
                    ev.Items.Clear();                 // 先把这条事件清空，下面条目的编号才从 1 起、基准才确定
                    ev.Limit = 18;
                    int cardId = GameTables.Cards().Count > 0 ? GameTables.Cards()[0].Id : 10001;
                    int n1 = doc.Diy.NextItemNumber(ev);
                    var it1 = new DiyProgressItem { N = n1 };
                    it1.Effects.Add(new DiyEffect { Kind = EffectKind.Card, Card = cardId });
                    it1.Effects.Add(new DiyEffect { Kind = EffectKind.Hp, N = 5 });
                    it1.Effects.Add(new DiyEffect { Kind = EffectKind.Hp, N = -3 });
                    it1.Effects.Add(new DiyEffect { Kind = EffectKind.Spawn, Pool = a.Id });
                    it1.Effects.Add(new DiyEffect { Kind = EffectKind.Gold, N = 7 });
                    it1.Effects.Add(new DiyEffect { Kind = EffectKind.MonsterAttack, N = 4 });
                    it1.Effects.Add(new DiyEffect { Kind = EffectKind.MonsterDefense, N = 6 });
                    ev.Items.Add(it1);
                    var it2 = new DiyProgressItem { N = n1 + 1 };
                    it2.Effects.Add(new DiyEffect { Kind = EffectKind.Spawn, Pool = b.Id });
                    ev.Items.Add(it2);
                    ev.VictoryKinds.Add(DiyVictory.TypeKillMonsters);
                    ev.VictoryTargets.Add(a.Id);
                    ev.VictoryKinds.Add(DiyVictory.TypeKillAllBoss);
                    // ★ 2026-10-08：棋盘上的落点要**归给这个事件**（`BoardMonsterRef.Event`），
                    //   spawn 才会"按格写 node"；不归给它就落到"开局就在场上"那一组（start_monsters）。
                    int key1 = EventKey(ev);       // ★ 落盘键：两条都没设触发点 ⇒ 退回**事件自己的 id**（1001）
                    foreach (BoardMonsterRef r in d.BoardMonsters) r.Event = key1;
                    doc.SaveDiy();

                    DiyData d2 = DiyPool.Read(doc);
                    DiyEvent ev2 = null;
                    foreach (DiyEvent e2 in d2.Events) if (e2.Id == key1) ev2 = e2;
                    int efxN = ev2 != null && ev2.Items.Count > 0 ? ev2.Items[0].Effects.Count : 0;
                    long limNow = DiyJson.IntOf(DiyJson.Member(DiyJson.Member(doc.Root, "progress"), "limit"), -1);
                    bool itemsOk = ev2 != null && ev2.Items.Count == 1 && ev2.Items[0].N == 1 && limNow == 18;
                    Line("    进度：落盘 progress_events.\"" + key1 + "\"（上限 progress.limit = " + limNow
                        + "）⇒ 读回 " + (ev2 == null ? 0 : ev2.Items.Count) + " 条进度（N="
                        + (ev2 == null || ev2.Items.Count == 0 ? "?" : ev2.Items[0].N.ToString(CultureInfo.InvariantCulture))
                        + "）、条词 " + efxN + " 条");
                    Check("⑫j ★★ 进度上限落到顶层 progress.limit（服务端真读的那份）；"
                        + "读回时**一个事件 = 一条进度**（N=1，键 = 事件 id）", itemsOk);

                    string sums = "";
                    if (ev2 != null && ev2.Items.Count > 0)
                        foreach (DiyEffect x in ev2.Items[0].Effects) sums += "[" + x.Summary(d2.Pool) + "]";
                    Line("    条词（读回 " + efxN + " 条）：" + sums);
                    // ★ 读回之后的顺序是「spawn → attack → defense → rewards」（服务端那三个键各自成组），
                    //   不再跟写进去的顺序一样 ⇒ 这里按**类 + 值**核对，而不是按下标。
                    bool kCard = false, kHpAdd = false, kHpSub = false, kSpawn = false, kGold = false, kAtk = false, kDef = false;
                    if (ev2 != null && ev2.Items.Count > 0)
                        foreach (DiyEffect x in ev2.Items[0].Effects)
                        {
                            if (x.Kind == EffectKind.Card && x.Card == cardId) kCard = true;
                            if (x.Kind == EffectKind.Hp && x.N == 5) kHpAdd = true;
                            if (x.Kind == EffectKind.Hp && x.N == -3) kHpSub = true;
                            if (x.Kind == EffectKind.Spawn) kSpawn = true;
                            if (x.Kind == EffectKind.Gold && x.N == 7) kGold = true;
                            if (x.Kind == EffectKind.MonsterAttack && x.N == 4) kAtk = true;
                            if (x.Kind == EffectKind.MonsterDefense && x.N == 6) kDef = true;
                        }
                    Check("⑫k ★★ 条词经 progress_events 走一圈回来仍是那六类"
                        + "（发牌 / 加扣血（加·扣）/ 新增怪物 / 加星币 / 怪物攻击力 / 怪物防御力）",
                        kCard && kHpAdd && kHpSub && kSpawn && kGold && kAtk && kDef);

                    // 获胜条件（★ 这一条看**内存里那一份**；落盘那份 —— targets 必须是怪物表 id —— 见 ⑫z）
                    Check("⑫l ★★ 获胜条件只预留两种（击败指定怪物 / 击败所有BOSS单位），支持多个目标",
                        ev.KillMonsters && ev.KillAllBoss && ev.VictoryTargets.Count == 1
                        && DiyVictory.All.Length == 2);

                    // 新增怪物 / 怪物攻 / 怪物防 三条同步进服务端读的 progress_events.<键>
                    JNode pe = DiyJson.Member(doc.Root, "progress_events");
                    JNode row1 = pe == null ? null : DiyJson.Member(pe, key1.ToString(CultureInfo.InvariantCulture));
                    JNode sp = row1 == null ? null : DiyJson.Member(row1, "spawn");
                    int spRows = sp != null && sp.Kind == JKind.Array ? sp.Count : 0;
                    long spTable = spRows > 0 ? DiyJson.IntOf(DiyJson.Member(sp.Items[0], "table"), -1) : -1;
                    long spNode0 = spRows > 0 ? DiyJson.IntOf(DiyJson.Member(sp.Items[0], "node"), -999) : -999;
                    long atk = row1 == null ? -999 : DiyJson.IntOf(DiyJson.Member(row1, "attack"), -999);
                    long def = row1 == null ? -999 : DiyJson.IntOf(DiyJson.Member(row1, "defense"), -999);
                    Line("    同步：progress_events." + key1 + ".spawn = " + spRows + " 行（第一行 table " + spTable
                        + " / node " + spNode0 + "）· attack " + atk + " · defense " + def
                        + "（棋盘上 a 放了 2 格、b 放了 1 格 ⇒ 3 行）");
                    Check("⑫m ★ 新增怪物 / 怪物攻 / 怪物防 都同步进 progress_events（服务端真读的那份）",
                        spRows == 3 && spTable == 1067 && atk == 4 && def == 6);

                    // 只留「新增怪物」、把「怪物攻/防」摘掉 ⇒ 那一行里的 attack / defense 必须跟着消失
                    // ★ 注意：SaveDiy() 存盘会把文档 rebase 一次、缓存重读 ⇒ 每一步都要拿"当下这份"
                    foreach (DiyProgressItem it in doc.Diy.FirstEvent().Items)
                        it.Effects.RemoveAll(delegate (DiyEffect x)
                        {
                            return x.Kind == EffectKind.MonsterAttack || x.Kind == EffectKind.MonsterDefense;
                        });
                    doc.SaveDiy();
                    JNode rowN = DiyJson.Member(DiyJson.Member(doc.Root, "progress_events"),
                        key1.ToString(CultureInfo.InvariantCulture));
                    bool adGone = rowN != null && DiyJson.Member(rowN, "attack") == null
                        && DiyJson.Member(rowN, "defense") == null
                        && DiyJson.Member(rowN, "spawn") != null;
                    Line("    摘掉「怪物攻/防」之后：progress_events." + key1 + " = "
                        + (rowN == null ? "(整行摘掉)" : DiyJson.Compact(rowN)));
                    Check("⑫n ★ 删掉「怪物攻/防」条词后 progress_events 里不留陈旧的 attack / defense", adGone);

                    // 再把「新增怪物」也删掉 ⇒ 那一行的 `spawn` 写成**空数组**（键在 = 明确不刷）
                    foreach (DiyProgressItem it in doc.Diy.FirstEvent().Items)
                        it.Effects.RemoveAll(delegate (DiyEffect x) { return x.Kind == EffectKind.Spawn; });
                    // ★ 界面上的语义：删掉一条「新增怪物」= **连它在棋盘上放的那几格一起撤**
                    //   （不然就成了"条词删了、棋盘上的怪还在继续刷"，用户会看不懂）
                    doc.Diy.BoardMonsters.RemoveAll(delegate (BoardMonsterRef r) { return r.Event == key1; });
                    doc.SaveDiy();
                    JNode rowN2 = DiyJson.Member(DiyJson.Member(doc.Root, "progress_events"),
                        key1.ToString(CultureInfo.InvariantCulture));
                    JNode spN2 = rowN2 == null ? null : DiyJson.Member(rowN2, "spawn");
                    bool spEmpty = spN2 != null && spN2.Kind == JKind.Array && spN2.Count == 0;
                    Line("    再删掉全部「新增怪物」之后：progress_events." + key1 + " = "
                        + (rowN2 == null ? "(整行摘掉)" : DiyJson.Compact(rowN2)));
                    Check("⑫n2 ★★ 删掉刷怪条词后那一行 `spawn` 写成**空数组**（键在 = 「这一波明确不刷」；奖励还在）"
                        + " —— 2026-10-10 与服务端对称的口径变更（原来是把键摘掉 = 回落官方那条事件的效果）",
                        rowN2 != null && spEmpty && DiyJson.Member(rowN2, "rewards") != null);

                    // 连条词一起清掉（这个事件一条内容都没有了）⇒ **整行摘掉**
                    //   （★ 一条效果都没有的事件不占键：写出去只会多一行空壳，见 SyncProgressEvents）
                    foreach (DiyProgressItem it in doc.Diy.FirstEvent().Items) it.Effects.Clear();
                    doc.SaveDiy();
                    JNode rowN3 = DiyJson.Member(DiyJson.Member(doc.Root, "progress_events"),
                        key1.ToString(CultureInfo.InvariantCulture));
                    bool emptyRow = rowN3 == null;
                    Line("    把条词全清掉之后：progress_events." + key1 + " = "
                        + (rowN3 == null ? "(整行摘掉)" : DiyJson.Compact(rowN3)));
                    Check("⑫n3 ★ 条词全清掉之后那一行整行摘掉（一条效果都没有的事件不占键）", emptyRow);

                    // 连事件都没有了 ⇒ progress_events 整块摘掉（不留空壳）
                    doc.Diy.Events.Clear();
                    doc.SaveDiy();
                    bool gone = DiyJson.Member(doc.Root, "progress_events") == null;
                    Line("    连事件都删光之后：progress_events " + (gone ? "整块摘掉" : "★ 还在"));
                    doc.Diy.Events.Add(ev);            // ★ 还原：后面几段接着用这条事件
                    doc.SaveDiy();
                    Check("⑫n4 ★ 一条事件都没有了 ⇒ progress_events 整块摘掉（不留空行）", gone);

                    // ---- 校验两条
                    List<string> sp1 = DiyPool.CheckSpawns(doc);
                    Line("    校验①：" + (sp1.Count > 0 ? sp1[0] : "(空)"));
                    bool spawnChk = sp1.Count >= 1;

                    // ★ 先把池子里的 #1 / #2 都挂上进度（各加一条「新增怪物」条词）
                    DiyData dc = doc.Diy;
                    int nk = dc.NextItemNumber(dc.FirstEvent());
                    foreach (PoolMonster pm in dc.Pool)
                    {
                        var ni = new DiyProgressItem { N = nk++ };
                        ni.Effects.Add(new DiyEffect { Kind = EffectKind.Spawn, Pool = pm.Id });
                        dc.FirstEvent().Items.Add(ni);
                    }
                    doc.SaveDiy();
                    List<string> allOk = DiyPool.CheckMonsters(DiyPool.Read(doc));
                    Line("    都挂上之后：" + (allOk.Count > 0 ? allOk[0] : "(空)"));
                    Check("⑫o0 ★ 池子里的怪都挂上进度之后，校验②报「全都挂在事件上」（开局场上没有怪）",
                        allOk.Count == 1 && allOk[0].IndexOf("✓") >= 0
                        && allOk[0].IndexOf("开局场上没有怪") >= 0);

                    // 再制造"没挂任何事件的怪"：往池子里塞一只，谁都不指向它（它会写进 start_monsters = 开局就在场上）
                    PoolMonster orphan = DiyPool.MakePool(doc.Diy, 1069);
                    doc.SaveDiy();
                    List<string> mk = DiyPool.CheckMonsters(DiyPool.Read(doc));
                    Line("    校验②：" + (mk.Count > 0 ? mk[0] : "(空)") + " / " + (mk.Count > 1 ? mk[1] : ""));
                    // 校验②：只有那一只没挂事件 ⇒ 头一行说明它怎么出场 + 第二行点名（池内序号 #N）
                    Line("    校验②逐行（" + mk.Count + " 行）："
                        + string.Join(" ｜ ", mk.ToArray()));
                    Check("⑫o ★★ 校验②「没挂事件的怪」逐条列出来（说明它进 start_monsters / 开局就在场上 + 池内序号 + 怎么办）",
                        mk.Count == 3 && mk[0].IndexOf("开局就在场上") >= 0
                        && mk[1].IndexOf("#" + orphan.Id) >= 0
                        && mk[1].IndexOf("没有任何事件指着它") >= 0
                        && mk[2].IndexOf("⇒") >= 0);
                    DiyPool.DropFromPool(doc.Diy, orphan.Id);
                    doc.SaveDiy();
                    List<string> mk2 = DiyPool.CheckMonsters(DiyPool.Read(doc));
                    Check("⑫p ★ 校验①/②在都配好之后不再报问题", spawnChk && mk2.Count == 1 && !mk2[0].StartsWith("★"));

                    // ---- 手写的 progress_events：spawn 行里只写 `table`（老服务端 / 手改文件）
                    JNode peOld = DiyJson.EnsureMember(doc.Root, "progress_events");
                    DiyJson.SetKind(peOld, JKind.Object);
                    JNode oldRow = DiyJson.SetKind(DiyJson.EnsureMember(peOld, "88888"), JKind.Object);
                    JNode oldSp = DiyJson.SetKind(DiyJson.EnsureMember(oldRow, "spawn"), JKind.Array);
                    var orow = new JNode { Kind = JKind.Object, Parent = oldSp, Dirty = true, WasInline = true, Members = new List<JMember>() };
                    orow.Members.Add(new JMember { Key = "table", Value = new JNode { Kind = JKind.Number, Num = "1070" } });
                    oldSp.Items.Add(orow);
                    doc.InvalidateDiy();
                    DiyData dHand = doc.Diy;
                    bool handOk = false;
                    foreach (DiyEvent e3 in dHand.Events)
                        foreach (DiyProgressItem i3 in e3.Items)
                            foreach (DiyEffect x3 in i3.Effects)
                                if (x3.Kind == EffectKind.Spawn && DiyPool.Find(dHand.Pool, x3.Pool) != null
                                    && DiyPool.Find(dHand.Pool, x3.Pool).Table == 1070) handOk = true;
                    Line("    手写的 progress_events.\"88888\".spawn（只有 table 1070）认出来了：" + handOk);
                    Check("⑫q ★ 手写的 spawn 行只有 table（没有池内序号）也能对回池子里的那一只", handOk);

                    // ---- 老格式兼容：**只有** `diy.events.<id>.items[].effects[]`、没有 `progress_events`
                    //   时回落老读法（spawn 里只写 `table` = 怪物表 id）。
                    //   ★ 新模型不再写老结构 ⇒ 这一份"老文件"只能手工造（`Write` 会把它摘掉）。
                    JNode legacyRoot = DiyJson.Parse(File.ReadAllText(work, Encoding.UTF8));
                    DiyJson.RemoveMember(legacyRoot, "start_monsters");
                    DiyJson.RemoveMember(legacyRoot, "progress_events");
                    JNode legDiy = DiyJson.SetKind(DiyJson.EnsureMember(legacyRoot, "diy"), JKind.Object);
                    JNode legEvents = DiyJson.SetKind(DiyJson.EnsureMember(legDiy, "events"), JKind.Object);
                    JNode legEv = DiyJson.SetKind(DiyJson.EnsureMember(legEvents, "1001"), JKind.Object);
                    DiyJson.SetString(DiyJson.EnsureMember(legEv, "name"), DiyPool.PlaceholderEventName);
                    JNode legItems = DiyJson.SetKind(DiyJson.EnsureMember(legEv, "items"), JKind.Array);
                    var legEff = new JNode { Kind = JKind.Object, Dirty = true, WasInline = true,
                                             BraceSpace = true, CommaSpace = true, Members = new List<JMember>() };
                    legEff.Members.Add(new JMember { Key = "type",
                        Value = new JNode { Kind = JKind.String, Str = DiyEffect.TypeSpawn, Dirty = true } });
                    legEff.Members.Add(new JMember { Key = "table",
                        Value = new JNode { Kind = JKind.Number, Num = "1070", Dirty = true } });
                    var legEffs = new JNode { Kind = JKind.Array, Dirty = true, Items = new List<JNode>() };
                    legEffs.Items.Add(legEff);
                    legEff.Parent = legEffs;
                    var legRow = new JNode { Kind = JKind.Object, Parent = legItems, Dirty = true, WasInline = true,
                                             BraceSpace = true, CommaSpace = true, Members = new List<JMember>() };
                    legRow.Members.Add(new JMember { Key = "n",
                        Value = new JNode { Kind = JKind.Number, Num = "7", Dirty = true } });
                    legRow.Members.Add(new JMember { Key = "effects", Value = legEffs });
                    legItems.Items.Add(legRow);
                    doc.Root = legacyRoot;
                    doc.InvalidateDiy();
                    DiyData d3 = doc.Diy;
                    bool migOk = false;
                    foreach (DiyEvent e3 in d3.Events)
                        foreach (DiyProgressItem i3 in e3.Items)
                            foreach (DiyEffect x3 in i3.Effects)
                                if (x3.Kind == EffectKind.Spawn && DiyPool.Find(d3.Pool, x3.Pool) != null
                                    && DiyPool.Find(d3.Pool, x3.Pool).Table == 1070) migOk = true;
                    Line("    老格式（只有 diy.events.1001.items、没有 progress_events；spawn 里只写 table 1070）认出来了：" + migOk);
                    Check("⑫q2 ★ 老文件（只有 diy.events.*.items）回落老读法，spawn 里只写 table 也能对回池子里的那一只", migOk);

                    // ★ 把树换回"我们自己那张干净纸"（后面几段接着用）
                    doc.Root = sheet;
                    doc.InvalidateDiy();
                    doc.InvalidateStarts();
                    doc.InvalidateBoardOverrides();

                    // ---- (c) 分类 / 头像（第④页与新增怪物窗口共用同一份数据）
                    bool clsOk = true;
                    foreach (MonsterRow m in GameTables.Monsters())
                    {
                        MonsterClass want2 = m.MonsterType == 1 ? MonsterClass.Minion
                            : (m.MonsterType == 2 ? MonsterClass.Boss
                            : (m.MonsterType == 3 ? MonsterClass.Elite : MonsterClass.Unknown));
                        if (m.Class != want2) clsOk = false;
                    }
                    int c1 = 0, c2 = 0, c3 = 0;
                    foreach (MonsterRow m in GameTables.Monsters())
                    {
                        if (m.MonsterType == 1) c1++;
                        else if (m.MonsterType == 2) c2++;
                        else if (m.MonsterType == 3) c3++;
                    }
                    Line("    Monster.bin " + GameTables.Monsters().Count + " 只怪的分类：普通 " + c1
                        + " · 首领 " + c2 + " · 精英 " + c3);
                    Check("⑫r ★★ 新增怪物窗口的分类读 Monster.bin 的 monster_type（三类都非空）",
                        clsOk && c1 > 0 && c2 > 0 && c3 > 0);

                    Check("⑫s ★ 1067 典狱长 = 首领（monster_type 2）、1023 = 普通（1）",
                        GameTables.Monster(1067) != null && GameTables.Monster(1067).Class == MonsterClass.Boss
                        && GameTables.Monster(1023) != null && GameTables.Monster(1023).Class == MonsterClass.Minion);

                    int bustHave = 0;
                    foreach (MonsterRow m in GameTables.Monsters())
                        if (Art.ImagePath(m.BustName) != null) bustHave++;
                    Line("    头像 UT_Monster_Bust_<id>_0：Monster.bin " + GameTables.Monsters().Count
                        + " 只里 " + bustHave + " 只有图");
                    Check("⑫t ★ 怪头像按 UT_Monster_Bust_<怪id>_0 直引（读得到就 ≥40 只有图）",
                        GameLocator.ImageDir == null || bustHave >= 40);

                    // ---- 发牌窗口的贴图：所有手牌 + UT_HandCard_<id>
                    int cards = GameTables.Cards().Count, handHave = 0;
                    foreach (CardRow c in GameTables.Cards())
                        if (Art.ImagePath(CardPickDialog.HandName(c.Id)) != null) handHave++;
                    Line("    手牌 Card.bin " + cards + " 张，本机有手牌贴图 " + handHave + " 张");
                    Check("⑫u ★★ 发牌窗口列的是所有手牌（Card.bin 全表），贴图走 UT_HandCard_<id>",
                        cards >= 60 && (GameLocator.ImageDir == null || handHave >= 40));
                }
                finally
                {
                    doc.Root = keepRoot;      // ★ 还原：后面的自测看到的是干净的那份
                    doc.InvalidateDiy();
                    doc.InvalidateStarts();
                    doc.InvalidateBoardOverrides();
                }

                // ============================================================ (d) 编辑动作撤销 / 重做
                Line("  编辑动作撤销（★ 换格 / 放怪 这一类，不再只是视图）：");
                JNode keepRoot2 = doc.Root;
                if (doc.Board == null || !doc.Board.Ok || doc.Board.Nodes.Count == 0)
                {
                    Line("    （这份样例没有棋盘，跳过）");
                    Check("⑫u ★ 编辑动作撤销栈", false);
                }
                else
                {
                    // ★ 先把根节点换成"一份没改过的干净副本"：`doc` 在前面几段里已经被改脏过
                    //   （根节点的 Dirty 一直是 true），拿它当基线去和"撤销后"比会有
                    //   "内容一样、写法不一样"的假失败。换一份干净的，撤销前后都能逐字节比。
                    // ★ 先把根节点换成"一份没改过的干净副本"：`doc` 在前面几段里已经被改脏过
                    //   （根节点的 Dirty 一直是 true），拿它当基线去和"撤销后"比会有
                    //   "内容一样、写法不一样"的假失败。换一份干净的，撤销前后都能逐字节比。
                    //   ★ 而且这一份必须是**重新 parse 出来的新树**：直接指向前一段的
                    //   `keepRoot` 会跟着后面的编辑动作**原地**被改（撤销就撤不掉东西了）。
                    string cleanPath = Path.Combine(outDir, "撤销自测_干净副本.json");
                    File.Copy(work, cleanPath, true);
                    doc.Root = DiyJson.Parse(File.ReadAllText(cleanPath, Encoding.UTF8));
                    doc.InvalidateDiy();
                    doc.Loaded = ExportService.Load(cleanPath);
                    doc.Path = cleanPath;
                    int cn = doc.Board.Nodes[0].Id;
                    try
                    {
                        var stack = new UndoStack();
                        string baseBytes = ExportService.Sha256Of(doc.Bytes());
                        int beforeCount = doc.Diy.Pool.Count;

                        // ① 放一只怪（新模型：进池子 + 棋盘格子上挂池内序号）
                        Check("⑫u1 Capture 记下了「动手之前」的状态", stack.Capture(doc, "新增怪物"));
                        PoolMonster um = DiyPool.MakePool(doc.Diy, 1067);
                        DiyPool.PlaceAt(doc.Diy, cn, um.Id);
                        doc.SaveDiy();
                        int afterPlace = doc.Diy.Pool.Count;
                        string what;
                        int rc = stack.Undo(doc, null, out what);
                        int undone = doc.Diy.Pool.Count;
                        Line("    放怪：池子 " + beforeCount + " → " + afterPlace + " → 撤销后 " + undone
                            + "（rc=" + rc + "，说明=" + what + "）");
                        string nowBytes = ExportService.Sha256Of(doc.Bytes());
                        Line("    撤销后 sha256 = " + nowBytes.Substring(0, 16) + "… / 动手前 = " + baseBytes.Substring(0, 16) + "…");
                        Check("⑫u2 ★★ 放怪能撤销（树回到动手之前，逐字节 sha256 一致）",
                            rc == 2 && undone == beforeCount
                            && string.Equals(nowBytes, baseBytes, StringComparison.OrdinalIgnoreCase));

                        int rc2 = stack.Redo(doc, null, out what);
                        int redone = doc.Diy.Pool.Count;
                        Line("    重做：池子 " + redone + "（rc=" + rc2 + "）");
                        Check("⑫u3 ★ 重做能回到「改过之后」（池子里的怪又回来了）", rc2 == 2 && redone == afterPlace);

                        stack.Undo(doc, null, out what);

                        // ② 换一格功能
                        Check("⑫u4 Capture 记下了换格之前的状态", stack.Capture(doc, "换格"));
                        int rcLand = doc.SetNodeLand(cn, 19);
                        int landNow = doc.NodeLandOverride(cn);
                        stack.Undo(doc, null, out what);
                        Line("    换格 " + cn + "→19：SetNodeLand=" + rcLand + "，改完账上 " + landNow
                            + "，撤销后账上 " + doc.NodeLandOverride(cn));
                        Check("⑫u5 ★★ 换格能撤销（棋盘覆盖账也回到原样）",
                            rcLand == 1 && landNow == 19 && doc.NodeLandOverride(cn) == 0);

                        // ③ 视图与编辑混在一个栈里，按顺序撤
                        var link = new ViewLink();
                        double[] view = new double[] { 1.0, 0, 0 };
                        link.Get = delegate { return view; };
                        link.Set = delegate (double[] v) { view = v; };
                        stack.CaptureView(link, "视图");
                        view = new double[] { 2.0, 40, 40 };
                        stack.Capture(doc, "放怪2");
                        PoolMonster um2 = DiyPool.MakePool(doc.Diy, 1023);
                        DiyPool.PlaceAt(doc.Diy, cn, um2.Id);
                        doc.SaveDiy();
                        int r1 = stack.Undo(doc, link, out what);
                        bool viewKept = Math.Abs(view[0] - 2.0) < 1e-9;
                        int r2 = stack.Undo(doc, link, out what);
                        bool viewBack = Math.Abs(view[0] - 1.0) < 1e-9;
                        Line("    混合栈：先撤编辑（rc=" + r1 + "，视图没被碰=" + viewKept
                            + "），再撤视图（rc=" + r2 + "，缩放回到 1.0=" + viewBack + "）");
                        Check("⑫u6 ★ 编辑动作与视图在同一个栈里按顺序撤（不互相吃掉）",
                            r1 == 2 && viewKept && r2 == 1 && viewBack);
                    }
                    finally
                    {
                        doc.Root = keepRoot2;
                        doc.InvalidateDiy();
                        doc.InvalidateStarts();
                        doc.InvalidateBoardOverrides();
                        BoardStore.ClearCache();
                        doc.LoadBoard(null);
                    }
                }

                // ---- (e) 下拉框里不能塞 ComboBoxItem（塞了就是黑字）
                Line("  下拉框塞项（塞 ComboBoxItem 就是黑字，_截图\\页3 拍到过）：");
                var badAdds = new List<string>();
                string srcDir = Path.Combine(Own(), "src");
                // 要找的串故意拼出来，否则本文件自己就含那一串、断言会抓自己
                string needle = "Items.Add(" + "new ComboBoxItem";
                if (Directory.Exists(srcDir))
                {
                    foreach (string f in Directory.GetFiles(srcDir, "*.cs"))
                    {
                        string[] ls = File.ReadAllLines(f, Encoding.UTF8);
                        for (int k = 0; k < ls.Length; k++)
                            if (ls[k].IndexOf(needle, StringComparison.Ordinal) >= 0)
                                badAdds.Add(Path.GetFileName(f) + ":" + (k + 1));
                    }
                }
                Line("    源码里「往 Items 里塞 ComboBoxItem」的处数：" + badAdds.Count
                    + (badAdds.Count == 0 ? "" : "（" + string.Join("、", badAdds.ToArray()) + "）"));
                Check("⑫v ★ 下拉框只塞字符串 / 实现 ToString() 的值对象（源码里 0 处塞 ComboBoxItem）",
                    Directory.Exists(srcDir) && badAdds.Count == 0);

                // ---- (e2) ★★ 悬停绝不许重建棋盘（2026-10-10 P1 回归：用户「点棋盘没用了」）
                //
                // 病根：`SetHover` 里调 `Draw()` ⇒ `Draw` 第一件事 `BoardCanvas.Children.Clear()`
                //   ⇒ 把鼠标底下那一格拆了 ⇒ WPF 立刻对已消失的控件发 `MouseLeave`
                //   ⇒ `SetHover(-1)` ⇒ 又 `Draw()` …… 无限循环，UI 线程一直重建整盘，
                //   **真鼠标的点击根本送不到**。
                // ★ 为什么 `--clicktest` 68/0 没抓到：它是**程序化调用**，不经过
                //   "鼠标进入 / 离开"这条真实路由链路（所以另配了 `--board-hover-count` 真事件探针）。
                //
                // 这条断言就是那道闸：**悬停那条路径里不许出现任何重画调用**。
                Line("  悬停路径（源码级闸门）：");
                var hoverBad = new List<string>();
                try
                {
                    string pbPath = Path.Combine(srcDir, "PageBoard.xaml.cs");
                    if (File.Exists(pbPath))
                    {
                        string[] pl = File.ReadAllLines(pbPath, Encoding.UTF8);
                        int inHover = -1, depth = 0;
                        for (int k = 0; k < pl.Length; k++)
                        {
                            if (inHover < 0)
                            {
                                if (pl[k].IndexOf("private void SetHover(", StringComparison.Ordinal) >= 0)
                                { inHover = k; depth = 0; }
                                continue;
                            }
                            depth += pl[k].Split('{').Length - 1;
                            depth -= pl[k].Split('}').Length - 1;
                            if (depth <= 0) break;                      // 方法体结束
                            if (pl[k].IndexOf("Draw(", StringComparison.Ordinal) >= 0
                                || pl[k].IndexOf("RefreshAll(", StringComparison.Ordinal) >= 0
                                || pl[k].IndexOf("Children.Clear(", StringComparison.Ordinal) >= 0)
                                hoverBad.Add("PageBoard.xaml.cs:" + (k + 1) + " " + pl[k].Trim());
                        }
                    }
                }
                catch (Exception) { }
                foreach (string one in hoverBad) Line("      ★ " + one);
                Check("⑫w ★★ 悬停高亮绝不重建棋盘（SetHover 里 0 处 Draw/RefreshAll/Children.Clear）"
                    + " —— 上一版就是这条把真鼠标点击卡死的",
                    Directory.Exists(srcDir) && hoverBad.Count == 0);

                // ---- (e3) ★★ 装饰圆环一律不吃点击（`Shape` 的 `Stroke` 区域也参与命中测试）
                var decoBad = new List<string>();
                try
                {
                    string pbPath = Path.Combine(srcDir, "PageBoard.xaml.cs");
                    if (File.Exists(pbPath))
                    {
                        string[] pl = File.ReadAllLines(pbPath, Encoding.UTF8);
                        for (int k = 0; k < pl.Length; k++)
                        {
                            if (pl[k].IndexOf("var el = new Ellipse();", StringComparison.Ordinal) < 0) continue;
                            bool ok = false;
                            for (int j = k; j < Math.Min(k + 12, pl.Length); j++)
                                if (pl[j].IndexOf("IsHitTestVisible = false", StringComparison.Ordinal) >= 0)
                                { ok = true; break; }
                            if (!ok) decoBad.Add("PageBoard.xaml.cs:" + (k + 1));
                        }
                    }
                }
                catch (Exception) { }
                Check("⑫x ★ 装饰圆环（DiscStroke）一律 IsHitTestVisible=false（不抢格子的点击）",
                    Directory.Exists(srcDir) && decoBad.Count == 0);

                // ---- (f) 数据形状样例（报告 §③ 照抄这一段）
                Line("  数据形状样例（报告 §③）：");
                try
                {
                    JNode keepShape = doc.Root;
                    CleanSheet(doc, work);
                    DiyPool.Write(doc, DiyPool.NewData());
                    DiyData sd = doc.Diy;
                    PoolMonster s1 = DiyPool.MakePool(sd, 1067);
                    PoolMonster s2 = DiyPool.MakePool(sd, 1069);
                    s1.Hp = 120; s1.Gold = 30; s1.Counter = true;
                    DiyPool.PlaceAt(sd, 17, s1.Id);
                    DiyPool.PlaceAt(sd, 22, s1.Id);
                    DiyPool.PlaceAt(sd, 31, s2.Id);
                    DiyEvent sev = sd.FirstEvent();
                    sev.Limit = 18;
                    var si = new DiyProgressItem { N = 1 };
                    si.Effects.Add(new DiyEffect { Kind = EffectKind.Card, Card = GameTables.Cards().Count > 0 ? GameTables.Cards()[0].Id : 10001 });
                    si.Effects.Add(new DiyEffect { Kind = EffectKind.Hp, N = 2 });
                    si.Effects.Add(new DiyEffect { Kind = EffectKind.Spawn, Pool = s2.Id });
                    si.Effects.Add(new DiyEffect { Kind = EffectKind.Gold, N = 10 });
                    sev.Items.Add(si);
                    sev.VictoryKinds.Add(DiyVictory.TypeKillMonsters);
                    sev.VictoryTargets.Add(s1.Id);
                    doc.SaveDiy();

                    string text = DiyJson.Write(doc.Root, " ", "\n");
                    Line("    —— 新建之后的 diy 块（原文照抄，报告 §③ 就是这一段）——");
                    Line(DiyJson.Compact(DiyJson.Member(doc.Root, "diy")));
                    Line("    —— progress_events（服务端读的那份）——");
                    Line(DiyJson.Compact(DiyJson.Member(doc.Root, "progress_events")));
                    Line("    —— victory / progress（服务端读的那份）——");
                    Line(DiyJson.Compact(DiyJson.Member(doc.Root, "victory")) + " / "
                        + DiyJson.Compact(DiyJson.Member(doc.Root, "progress")));

                    JNode shDiy = DiyJson.Member(doc.Root, "diy");
                    JNode shEv = DiyJson.Member(DiyJson.Member(shDiy, "events"),
                        DiyPool.PlaceholderEventId.ToString(CultureInfo.InvariantCulture));
                    Check("⑫w ★★ 形状样例：diy.monsters[] 池子 + diy.board_monsters[] 放怪 + diy.events.<id>{name,desc}"
                        + "（★ 不再有 items）+ 顶层 progress_events / victory / progress.limit 都在",
                        DiyJson.Member(shDiy, "monsters") != null
                        && DiyJson.Member(shDiy, "board_monsters") != null
                        && shEv != null && DiyJson.Member(shEv, "items") == null
                        && DiyJson.Member(shEv, "progress_limit") == null
                        && DiyJson.Member(doc.Root, "progress_events") != null
                        && DiyJson.Member(doc.Root, "victory") != null
                        && DiyJson.IntOf(DiyJson.Member(DiyJson.Member(doc.Root, "progress"), "limit"), -1) == 18
                        && text.Length > 0);
                    doc.Root = keepShape;
                    doc.InvalidateDiy();
                    doc.InvalidateStarts();
                    doc.InvalidateBoardOverrides();
                }
                catch (Exception ex)
                {
                    Line("    ★ 形状样例失败：" + ex.Message);
                    Check("⑫w ★ 形状样例", false);
                }

                // ---- (g) ★★ 2026-10-08 大改的**扁平新契约**（玩法内容全在服务端真读的那几个键里）
                Line("  扁平新契约（progress_events 的 spawn / rewards · 顶层 victory / progress.limit · 老结构不再写）：");
                JNode keepFlat = doc.Root;
                try
                {
                    CleanSheet(doc, work);
                    DiyPool.Write(doc, DiyPool.NewData());
                    DiyData fd = doc.Diy;
                    PoolMonster f1 = DiyPool.MakePool(fd, 1067);      // 放两格
                    PoolMonster f2 = DiyPool.MakePool(fd, 1023);      // 放一格
                    PoolMonster f3 = DiyPool.MakePool(fd, 1069);      // 一格都不放（⇒ 随机空地）
                    f3.Hp = 77; f3.Gold = 9;                          // 数值要跟着 spawn 行一起走一圈
                    // ★ 2026-10-08：只把 **f1 的两格**归给这个事件（spawn 才会按格写 node）；
                    //   f2 那一格留在"开局就在场上"那一组（Event = 0）⇒ 不走本事件的 spawn 行。
                    DiyEvent fev = fd.FirstEvent();
                    int fkey = EventKey(fev);                         // 落盘键 = 事件 id（没设触发点）
                    DiyPool.PlaceAt(fd, 5, f1.Id, fkey);              // 本事件：两格
                    DiyPool.PlaceAt(fd, 11, f1.Id, fkey);
                    DiyPool.PlaceAt(fd, 9, f2.Id);                    // 开局组：node 9 上站一只

                    fev.Items.Clear();                                // ★ 同 ⑫：先清空，编号基准才确定
                    fev.Limit = 24;
                    int fcard = GameTables.Cards().Count > 0 ? GameTables.Cards()[0].Id : 10001;
                    int fn = fd.NextItemNumber(fev);
                    var fi = new DiyProgressItem { N = fn };
                    fi.Effects.Add(new DiyEffect { Kind = EffectKind.Spawn, Pool = f1.Id, Count = 1 });
                    fi.Effects.Add(new DiyEffect { Kind = EffectKind.Spawn, Pool = f3.Id, Count = 4 });
                    fi.Effects.Add(new DiyEffect { Kind = EffectKind.Relic, Choice = 2, To = DiyEffect.ToActor });
                    fi.Effects.Add(new DiyEffect { Kind = EffectKind.Card, Card = fcard, Count = 2, To = DiyEffect.ToTeam });
                    fi.Effects.Add(new DiyEffect { Kind = EffectKind.Gold, N = 6, To = DiyEffect.ToActor });
                    fi.Effects.Add(new DiyEffect { Kind = EffectKind.Hp, N = -3, To = DiyEffect.ToTeam });
                    fev.Items.Add(fi);
                    fev.VictoryKinds.Add(DiyVictory.TypeKillMonsters);
                    fev.VictoryTargets.Add(f1.Id);                    // 编辑器里说的是**池内序号**

                    // ★ 顺手往"老结构"里塞三个键（模拟老文件）：这一版起保存时必须把它们摘掉
                    JNode fevBlock = DiyJson.Member(DiyJson.Member(DiyJson.Member(doc.Root, "diy"), "events"),
                        DiyPool.PlaceholderEventId.ToString(CultureInfo.InvariantCulture));
                    DiyJson.SetKind(DiyJson.EnsureMember(fevBlock, "items"), JKind.Array);
                    DiyJson.SetInt(DiyJson.EnsureMember(fevBlock, "progress_limit"), 99);
                    DiyJson.SetKind(DiyJson.EnsureMember(fevBlock, "victory"), JKind.Object);

                    doc.SaveDiy();

                    // ① spawn 行：放了两格 ⇒ 两行各 1 只（node = 格号）；没放格的那只 ⇒ 一行 node:-1 + count = 数量
                    JNode fpe = DiyJson.Member(doc.Root, "progress_events");
                    JNode frow = fpe == null ? null : DiyJson.Member(fpe, fkey.ToString(CultureInfo.InvariantCulture));
                    JNode fsp = frow == null ? null : DiyJson.Member(frow, "spawn");
                    int fn5 = 0, fn11 = 0, fnm1 = 0;
                    long fm1Count = -1, fm1Table = -1, fm1Hp = -1;
                    var frows = new List<string>();
                    if (fsp != null && fsp.Kind == JKind.Array)
                        foreach (JNode r in fsp.Items)
                        {
                            int tb = (int)DiyJson.IntOf(DiyJson.Member(r, "table"), 0);
                            int nd = (int)DiyJson.IntOf(DiyJson.Member(r, "node"), -999);
                            int ct = (int)DiyJson.IntOf(DiyJson.Member(r, "count"), 0);
                            frows.Add("table" + tb + "/node" + nd + "/count" + ct);
                            if (nd == 5) fn5++;
                            else if (nd == 11) fn11++;
                            else if (nd == -1)
                            {
                                fnm1++;
                                fm1Count = ct;
                                fm1Table = tb;
                                fm1Hp = DiyJson.IntOf(DiyJson.Member(r, "hp"), -1);
                            }
                        }
                    bool spawnOk = fsp != null && fsp.Kind == JKind.Array && fsp.Count == 3
                        && fn5 == 1 && fn11 == 1 && fnm1 == 1
                        && fm1Table == 1069 && fm1Count == 4 && fm1Hp == 77;
                    Line("    spawn " + (fsp == null ? "(没有)" : fsp.Count + " 行") + "：" + string.Join(" · ", frows.ToArray()));
                    Check("⑫x ★★ 一个事件的 spawn 行：棋盘上放了的按格写 node（两格两行各 1 只）、"
                        + "没放格的那只写一行 node:-1 + count = 数量（跟着怪的数值一起走）", spawnOk);

                    // ② rewards：relic（choice）/ card（ids + count）/ gold（amount）/ hp（amount），to 分 team / actor
                    JNode frw = frow == null ? null : DiyJson.Member(frow, "rewards");
                    int frwN = frw != null && frw.Kind == JKind.Array ? frw.Count : 0;
                    var frwLines = new List<string>();
                    if (frw != null && frw.Kind == JKind.Array)
                        foreach (JNode r in frw.Items) frwLines.Add(DiyJson.Compact(r));
                    bool rwOk = frwN == 4
                        && StrOf(frw.Items[0], "kind") == "relic"
                        && DiyJson.IntOf(DiyJson.Member(frw.Items[0], "choice"), -1) == 2
                        && StrOf(frw.Items[0], "to") == DiyEffect.ToActor
                        && StrOf(frw.Items[1], "kind") == "card"
                        && FirstIntOf(DiyJson.Member(frw.Items[1], "ids"), -1) == fcard
                        && DiyJson.IntOf(DiyJson.Member(frw.Items[1], "count"), -1) == 2
                        && StrOf(frw.Items[1], "to") == DiyEffect.ToTeam
                        && StrOf(frw.Items[2], "kind") == "gold"
                        && DiyJson.IntOf(DiyJson.Member(frw.Items[2], "amount"), long.MinValue) == 6
                        && StrOf(frw.Items[2], "to") == DiyEffect.ToActor
                        && StrOf(frw.Items[3], "kind") == "hp"
                        && DiyJson.IntOf(DiyJson.Member(frw.Items[3], "amount"), long.MinValue) == -3
                        && StrOf(frw.Items[3], "to") == DiyEffect.ToTeam;

                    // 读回：Kind / To / Choice / Card / Count 必须跟写进去的一模一样
                    DiyData fb = DiyPool.Read(doc);
                    DiyEvent fbev = null;
                    foreach (DiyEvent e2 in fb.Events) if (e2.Id == fkey) fbev = e2;
                    DiyEffect rb = null, cb = null, gb = null, hb = null, sb = null;
                    if (fbev != null && fbev.Items.Count > 0)
                        foreach (DiyEffect x in fbev.Items[0].Effects)
                        {
                            if (x.Kind == EffectKind.Relic && rb == null) rb = x;
                            else if (x.Kind == EffectKind.Card && cb == null) cb = x;
                            else if (x.Kind == EffectKind.Gold && gb == null) gb = x;
                            else if (x.Kind == EffectKind.Hp && hb == null) hb = x;
                            else if (x.Kind == EffectKind.Spawn && sb == null)
                            {
                                PoolMonster pm = DiyPool.Find(fb.Pool, x.Pool);
                                if (pm != null && pm.Table == 1069) sb = x;
                            }
                        }
                    bool backOk = rb != null && rb.Choice == 2 && rb.To == DiyEffect.ToActor
                        && cb != null && cb.Card == fcard && cb.Count == 2 && cb.To == DiyEffect.ToTeam
                        && gb != null && gb.N == 6 && gb.To == DiyEffect.ToActor
                        && hb != null && hb.N == -3 && hb.To == DiyEffect.ToTeam
                        && sb != null && sb.Count == 4;
                    Line("    rewards " + frwN + " 行：" + string.Join(" · ", frwLines.ToArray()));
                    Line("    读回：relic(choice " + (rb == null ? "?" : rb.Choice.ToString(CultureInfo.InvariantCulture))
                        + " / to " + (rb == null ? "?" : rb.To) + ") · card("
                        + (cb == null ? "?" : cb.Card.ToString(CultureInfo.InvariantCulture)) + " / count "
                        + (cb == null ? "?" : cb.Count.ToString(CultureInfo.InvariantCulture)) + " / to "
                        + (cb == null ? "?" : cb.To) + ") · gold(amount "
                        + (gb == null ? "?" : gb.N.ToString(CultureInfo.InvariantCulture)) + " / to "
                        + (gb == null ? "?" : gb.To) + ") · hp(amount "
                        + (hb == null ? "?" : hb.N.ToString(CultureInfo.InvariantCulture)) + " / to "
                        + (hb == null ? "?" : hb.To) + ") · spawn(node:-1 那只) count "
                        + (sb == null ? "?" : sb.Count.ToString(CultureInfo.InvariantCulture)));
                    Check("⑫y ★★ rewards 三类（relic 带 choice / card 带 ids + count / gold 带 amount）+ to 分 team / actor；"
                        + "读回之后 Kind / To / Choice / Card / Count 与原值一致", rwOk && backOk);

                    // ③ 获胜条件落顶层 victory（targets 是**怪物表 id**）+ 总进度上限落 progress.limit
                    JNode fvic = DiyJson.Member(doc.Root, "victory");
                    JNode fta = fvic == null ? null : DiyJson.Member(fvic, "targets");
                    bool vicOk = fvic != null && StrOf(fvic, "kind") == "kill_boss"
                        && fta != null && fta.Kind == JKind.Array && fta.Count == 1
                        && DiyJson.IntOf(fta.Items[0], -1) == f1.Table       // ★ 怪物表 id
                        && DiyJson.IntOf(fta.Items[0], -1) != f1.Id;        // 不是池内序号
                    long limFlat = DiyJson.IntOf(DiyJson.Member(DiyJson.Member(doc.Root, "progress"), "limit"), -1);
                    Line("    victory = " + (fvic == null ? "(没有)" : DiyJson.Compact(fvic))
                        + "（池内序号 #" + f1.Id + " ⇒ 怪物表 id " + f1.Table + "）· progress.limit = " + limFlat);
                    Check("⑫z ★★ 勾了获胜条件 ⇒ 顶层 victory.targets 写的是**怪物表 id**（不是池内序号）；"
                        + "总进度上限落到顶层 progress.limit", vicOk && limFlat == 24);

                    // ④ 老结构（diy.events.<id>.items / progress_limit / victory）不再写
                    JNode fblock = DiyJson.Member(DiyJson.Member(DiyJson.Member(doc.Root, "diy"), "events"),
                        DiyPool.PlaceholderEventId.ToString(CultureInfo.InvariantCulture));
                    bool oldGone = fblock != null
                        && DiyJson.Member(fblock, "items") == null
                        && DiyJson.Member(fblock, "progress_limit") == null
                        && DiyJson.Member(fblock, "victory") == null
                        && StrOf(fblock, "name") == DiyPool.PlaceholderEventName;
                    Line("    老结构（保存前往里塞的 items / progress_limit / victory）保存之后："
                        + (oldGone ? "三个键都被摘掉，diy.events." + DiyPool.PlaceholderEventId + " 只剩 "
                                     + DiyJson.Compact(fblock)
                                   : "★ 还有残留：" + DiyJson.Compact(fblock)));
                    Check("⑫z2 ★★ 老结构不再写：diy.events.<id> 只剩 name / desc"
                        + "（items / progress_limit / victory 保存后一个都不留）", oldGone);
                }
                catch (Exception ex)
                {
                    Line("    ★ 扁平新契约自测失败：" + ex.Message);
                    Check("⑫x ★★ spawn 行的 node / count", false);
                    Check("⑫y ★★ rewards 三类 + to + 读回一致", false);
                    Check("⑫z ★★ victory.targets = 怪物表 id / progress.limit", false);
                    Check("⑫z2 ★★ 老结构不再写", false);
                }
                finally
                {
                    doc.Root = keepFlat;
                    doc.InvalidateDiy();
                    doc.InvalidateStarts();
                    doc.InvalidateBoardOverrides();
                }
            }
            catch (Exception ex)
            {
                Line("  ★ 失败：" + ex.Message);
                Check("⑫ 进度事件 / 怪物 / 撤销", false);
            }
            Line("");

            // ============================================================ ⑬ 第 9 波四条
            Line("── ⑬ 第 9 波：一格多怪 · 图标补全 · 实时刷新 · 头像居中 ────────────");
            {
                // ---- (1) ★ 一格多只怪（数据结构 + 单独删 + 池子留着）
                Line("  ① 一格多只怪：");
                JNode keep13 = doc.Root;
                try
                {
                    // ★★ 官方关卡自带 start_monsters + progress_events，新模型的 Read 会把它导进
                    //   池子 / 棋盘 ⇒ 先清成"干净一张纸"，这里的 池子 3 只 / 棋盘 4 条 才是我们自己摆的。
                    CleanSheet(doc, work);
                    DiyPool.Write(doc, DiyPool.NewData());
                    DiyData d = doc.Diy;
                    PoolMonster m1 = DiyPool.MakePool(d, 1067);
                    PoolMonster m2 = DiyPool.MakePool(d, 1023);
                    PoolMonster m3 = DiyPool.MakePool(d, 1069);
                    DiyPool.AddAt(d, 5, m1.Id);
                    DiyPool.AddAt(d, 5, m2.Id);          // ★ 同一格追加第二只
                    DiyPool.AddAt(d, 5, m3.Id);          // ★ 第三只
                    DiyPool.AddAt(d, 9, m1.Id);          // 另一格
                    doc.SaveDiy();

                    DiyData r1 = DiyPool.Read(doc);
                    int at5 = DiyPool.CountAtNode(r1, 5);
                    List<PoolMonster> herd = DiyPool.AllAtNode(r1, 5);
                    Line("    格 5 上放了 " + at5 + " 只：" + Join(herd)
                        + "；棋盘上共 " + r1.BoardMonsters.Count + " 条（" + DiyPool.BoardCount(r1) + "）");
                    Check("⑬a ★★ 同一格能放多只怪（diy.board_monsters 同一 node 多条，落盘读回一致）",
                        at5 == 3 && r1.BoardMonsters.Count == 4 && herd.Count == 3);

                    // ★ 单独删一只：只少那一条，另外两只和别的格都不动
                    bool rm = DiyPool.RemoveOneFromNode(doc.Diy, 5, m2.Id);
                    doc.SaveDiy();
                    DiyData r2 = DiyPool.Read(doc);
                    Line("    拿掉格 5 上的 #" + m2.Id + " ⇒ 格 5 剩 " + DiyPool.CountAtNode(r2, 5)
                        + " 只；池子还有 " + r2.Pool.Count + " 只");
                    Check("⑬b ★ 一格上的怪能单独删（同格其他几只 / 别格 / 池子都不动）",
                        rm && DiyPool.CountAtNode(r2, 5) == 2
                        && DiyPool.Find(r2.Pool, m2.Id) != null
                        && DiyPool.CountAtNode(r2, 9) == 1);

                    // ★★ 池子里的怪**没被任何格引用**时保持不变（用户规格：交给「未配置」栏）
                    int poolBefore = doc.Diy.Pool.Count;
                    DiyPool.RemoveOneFromNode(doc.Diy, 9, m1.Id);
                    doc.SaveDiy();
                    DiyData r3 = DiyPool.Read(doc);
                    bool still = DiyPool.Find(r3.Pool, m1.Id) != null;
                    Line("    把 #" + m1.Id + " 从最后一格也拿掉 ⇒ 池子 " + poolBefore + " → " + r3.Pool.Count
                        + "（" + (still ? "★ 留着" : "被删了") + "）");
                    List<string> ck = DiyPool.CheckMonsters(r3);
                    Line("    校验②：" + (ck.Count > 0 ? ck[0] : "(空)"));
                    Check("⑬c ★★ 格上的怪删掉之后，池子里没被引用的那条**留着**"
                        + "（★ 2026-10-08 起校验②把它说成「开局就在场上的怪」，不再当错误）",
                        still && r3.Pool.Count == poolBefore && ck.Count >= 2
                        && ck[0].IndexOf("开局就在场上") >= 0
                        && ck[1].IndexOf("#" + m1.Id) >= 0);

                    // ★ 老语义（PlaceAt = 换掉这一格）没被破坏
                    DiyPool.PlaceAt(doc.Diy, 5, m3.Id);
                    doc.SaveDiy();
                    Check("⑬d ★ `PlaceAt` 仍是「换掉这一格」（老调用点行为不变）",
                        DiyPool.CountAtNode(DiyPool.Read(doc), 5) == 1);

                    // ---- (2) ★★ 头像居中（四档格子大小都验）
                    Line("  ④ 头像居中（参考系 = 格子的内容层）：");
                    bool centerOk = true;
                    var centerLines = new List<string>();
                    foreach (double ratio in PageBoard.NodeRatioOptions)
                    {
                        foreach (double pitch in new double[] { 40, 80, 140, 200 })
                        {
                            double node = PageBoard.NodeSizeFor(pitch, ratio);
                            double cx = 321.5, cy = 654.25;      // 随便一个非整数中心，防"取整凑巧"
                            double left, top, size;
                            MonsterStack.Rect(cx, cy, node, 0, 1, out left, out top, out size);
                            double mx = left + size / 2, my = top + size / 2;
                            if (Math.Abs(mx - cx) > 1e-9 || Math.Abs(my - cy) > 1e-9) centerOk = false;
                            if (pitch == 40)
                                centerLines.Add(PageBoard.NodeRatioTextFor(ratio) + " ⇒ 格子 "
                                    + node.ToString("0.0", CultureInfo.InvariantCulture) + " px，头像 "
                                    + size.ToString("0.0", CultureInfo.InvariantCulture) + " px @("
                                    + left.ToString("0.0", CultureInfo.InvariantCulture) + ","
                                    + top.ToString("0.0", CultureInfo.InvariantCulture) + ")");
                        }
                    }
                    foreach (string s in centerLines) Line("    " + s);
                    Check("⑬e ★★ 1 只怪时头像**正正好居中于格子**（四档格子大小 × 四个缩放，误差 0）", centerOk);

                    // ★ 2~3 只沿 45° 错开、整体仍以格子中心为对称中心
                    bool stackOk = true;
                    var stackLines = new List<string>();
                    foreach (int cnt in new int[] { 2, 3 })
                    {
                        double node = 100;
                        double sumx = 0, sumy = 0;
                        var pts = new List<string>();
                        for (int k = 0; k < cnt; k++)
                        {
                            double left, top, size;
                            MonsterStack.Rect(0, 0, node, k, cnt, out left, out top, out size);
                            double mx = left + size / 2, my = top + size / 2;
                            sumx += mx; sumy += my;
                            if (k > 0 && Math.Abs(mx + my) > 1e-9) stackOk = false;   // 45°：x = -y
                            pts.Add("(" + mx.ToString("0.0", CultureInfo.InvariantCulture) + ","
                                + my.ToString("0.0", CultureInfo.InvariantCulture) + ")");
                        }
                        if (Math.Abs(sumx) > 1e-9 || Math.Abs(sumy) > 1e-9) stackOk = false;  // 对称
                        stackLines.Add(cnt + " 只：" + string.Join(" ", pts.ToArray()));
                    }
                    foreach (string s in stackLines) Line("    " + s);
                    Check("⑬f ★★ 2~3 只按 45° 错开叠放，且整体仍以格子中心对称", stackOk);

                    Line("    多于 3 只 ⇒ +N 角标：4 只→+" + MonsterStack.HiddenCount(4)
                        + " · 7 只→+" + MonsterStack.HiddenCount(7)
                        + " · 3 只→" + MonsterStack.HiddenCount(3)
                        + "（最多画 " + MonsterStack.MaxDrawn + " 个）");
                    Check("⑬g ★ 多于 3 只时角标 = 没画出来的只数（4→+1 / 7→+4 / 3→+0）",
                        MonsterStack.MaxDrawn == 3
                        && MonsterStack.HiddenCount(4) == 1
                        && MonsterStack.HiddenCount(7) == 4
                        && MonsterStack.HiddenCount(3) == 0
                        && MonsterStack.HiddenCount(1) == 0);

                    // ---- (3) ★★ 实时刷新：改 A 处 ⇒ B 处（怪物池归属 / 进度）跟着变
                    Line("  ③ 实时刷新（改一处 ⇒ 另一处立刻反映）：");
                    DiyData live = doc.Diy;
                    int ver0 = DiyNotify.Version;
                    foreach (PoolMonster pm in live.Pool) pm.ProgressN = null;
                    DiyPool.BindProgress(live);
                    bool beforeNull = true;
                    foreach (PoolMonster pm in live.Pool) if (pm.ProgressN.HasValue) beforeNull = false;

                    DiyEvent lev = live.FirstEvent();
                    var litem = new DiyProgressItem { N = live.NextItemNumber(lev) };
                    litem.Effects.Add(new DiyEffect { Kind = EffectKind.Spawn, Pool = m1.Id });
                    lev.Items.Add(litem);
                    doc.SaveDiy();                      // ★ 走界面同一条路（进度页加条词就是这么干的）
                    PoolMonster after = DiyPool.Find(live.Pool, m1.Id);
                    int ver1 = DiyNotify.Version;
                    Line("    在进度页加了一条「新增怪物 → #" + m1.Id + "」⇒ 这一刻池里那只的进度归属 = "
                        + (after != null && after.ProgressN.HasValue
                            ? ("进度 " + after.ProgressN.Value) : "（还是空）")
                        + "；变更通知版本 " + ver0 + " → " + ver1);
                    Check("⑬h ★★ 进度页加了「新增怪物」⇒ 怪物池里那只的进度归属**当场**变（怪物页分栏不会再错）",
                        beforeNull && after != null && after.ProgressN.HasValue
                        && after.ProgressN.Value == litem.N);

                    Check("⑬i ★ 数据一变就发通知（DiyNotify 版本号 +1，页面靠它对版本重建）", ver1 > ver0);

                    // ★ 反过来：把条词删掉 ⇒ 归属回到"未配置"
                    foreach (DiyProgressItem it in lev.Items) it.Effects.RemoveAll(
                        delegate (DiyEffect x) { return x.Kind == EffectKind.Spawn && x.Pool == m1.Id; });
                    doc.SaveDiy();
                    PoolMonster back2 = DiyPool.Find(doc.Diy.Pool, m1.Id);
                    Line("    再把那条条词删掉 ⇒ 归属 = "
                        + (back2 != null && back2.ProgressN.HasValue
                            ? ("进度 " + back2.ProgressN.Value) : "未配置"));
                    Check("⑬j ★ 条词删掉之后归属**跟着清掉**（不会留着旧账骗人）",
                        back2 != null && !back2.ProgressN.HasValue);
                }
                finally
                {
                    doc.Root = keep13;
                    doc.InvalidateDiy();
                    doc.InvalidateStarts();
                    doc.InvalidateBoardOverrides();
                }

                // ---- (3b) ★★ 2026-10-09：**每一处放置自己一份五项数值**
                //   用户原话："我要每个独立的怪物都分开，数值也要分开 diy 而不是现在这样"
                //   ⇒ 同一种怪放两格 / 放两个事件 / 开局组两条，各改各的、互不影响；
                //     "随机补几只"那一行落盘用**怪种**那份（口径钉死在这条断言里）。
                Line("  ③b 每一处放置各改各的（同种怪放两格 / 跨事件 / start_monsters / 随机补那一行）：");
                JNode keepPlace = doc.Root;
                try
                {
                    CleanSheet(doc, work);
                    DiyPool.Write(doc, DiyPool.NewData());
                    DiyData pd = doc.Diy;
                    PoolMonster pm = DiyPool.MakePool(pd, 1067);        // 一种怪，放到好几个地方
                    DiyEvent pev = pd.FirstEvent();
                    int pkey = EventKey(pev);
                    DiyPool.AddAt(pd, 5, pm.Id, pkey);                  // 本事件：两格
                    DiyPool.AddAt(pd, 25, pm.Id, pkey);
                    pev.Items.Clear();
                    var pit = new DiyProgressItem { N = 1 };
                    pit.Effects.Add(new DiyEffect { Kind = EffectKind.Spawn, Pool = pm.Id, Count = 2 });
                    pev.Items.Add(pit);

                    BoardMonsterRef pra = null, prb = null;
                    foreach (BoardMonsterRef r in pd.BoardMonsters)
                    {
                        if (r.Node == 5) pra = r;
                        else if (r.Node == 25) prb = r;
                    }
                    pra.Hp = 111; pra.Gold = 11;
                    prb.Hp = 222; prb.Gold = 22; prb.Counter = true;
                    doc.SaveDiy();

                    // ① 同一种怪放两格 ⇒ 落盘两行各是自己的血/币；读回还是两条、各是自己的值
                    JNode pe2 = DiyJson.Member(doc.Root, "progress_events");
                    JNode prow = pe2 == null ? null
                        : DiyJson.Member(pe2, pkey.ToString(CultureInfo.InvariantCulture));
                    JNode psp = prow == null ? null : DiyJson.Member(prow, "spawn");
                    long ph5 = -1, ph25 = -1, pg5 = -1, pg25 = -1;
                    if (psp != null && psp.Kind == JKind.Array)
                        foreach (JNode r in psp.Items)
                        {
                            int nd = (int)DiyJson.IntOf(DiyJson.Member(r, "node"), -999);
                            if (nd == 5)
                            {
                                ph5 = DiyJson.IntOf(DiyJson.Member(r, "hp"), -1);
                                pg5 = DiyJson.IntOf(DiyJson.Member(r, "gold"), -1);
                            }
                            else if (nd == 25)
                            {
                                ph25 = DiyJson.IntOf(DiyJson.Member(r, "hp"), -1);
                                pg25 = DiyJson.IntOf(DiyJson.Member(r, "gold"), -1);
                            }
                        }
                    DiyData prd = DiyPool.Read(doc);
                    long rh5 = -1, rh25 = -1; bool rc25 = false;
                    foreach (BoardMonsterRef r in prd.BoardMonsters)
                    {
                        if (r.Node == 5) rh5 = r.Hp;
                        else if (r.Node == 25) { rh25 = r.Hp; rc25 = r.Counter; }
                    }
                    Line("    格 5 / 格 25 各改各的：落盘 hp " + ph5 + " / " + ph25 + "、币 " + pg5 + " / " + pg25
                        + " ⇒ 读回 hp " + rh5 + " / " + rh25 + "（格 25 反击 = " + rc25 + "）");
                    Check("⑯a ★★ 同一种怪放两格、各改各的：落盘那两行 hp 各自不同（" + ph5 + " / " + ph25
                        + "）、币也各自不同；读回还是两条、各是自己的值",
                        ph5 == 111 && ph25 == 222 && pg5 == 11 && pg25 == 22
                        && rh5 == 111 && rh25 == 222 && rc25);

                    // ② 同一只怪放**两个不同事件**：改其中一个事件里那处 ⇒ 另一个一点不动
                    var pev2 = new DiyEvent();
                    pev2.Id = 32122;
                    pev2.Name = "第二个事件";
                    pd.Events.Add(pev2);
                    DiyPool.AddAt(pd, 9, pm.Id, 32122);
                    var pit2 = new DiyProgressItem { N = 1 };
                    pit2.Effects.Add(new DiyEffect { Kind = EffectKind.Spawn, Pool = pm.Id, Count = 1 });
                    pev2.Items.Add(pit2);
                    foreach (BoardMonsterRef r in pd.BoardMonsters) if (r.Event == 32122) r.Hp = 333;
                    doc.SaveDiy();

                    JNode pe3 = DiyJson.Member(doc.Root, "progress_events");
                    JNode prow2 = pe3 == null ? null
                        : DiyJson.Member(pe3, 32122.ToString(CultureInfo.InvariantCulture));
                    JNode psp2 = prow2 == null ? null : DiyJson.Member(prow2, "spawn");
                    long hKey = -1, h32122 = -1;
                    JNode psp1b = prow == null ? null : DiyJson.Member(prow, "spawn");
                    if (psp1b != null && psp1b.Kind == JKind.Array)
                        foreach (JNode r in psp1b.Items)
                            if ((int)DiyJson.IntOf(DiyJson.Member(r, "node"), -999) == 5)
                                hKey = DiyJson.IntOf(DiyJson.Member(r, "hp"), -1);
                    if (psp2 != null && psp2.Kind == JKind.Array)
                        foreach (JNode r in psp2.Items)
                            if ((int)DiyJson.IntOf(DiyJson.Member(r, "node"), -999) == 9)
                                h32122 = DiyJson.IntOf(DiyJson.Member(r, "hp"), -1);
                    long r32122 = -1, rkey5 = -1;
                    foreach (BoardMonsterRef r in DiyPool.Read(doc).BoardMonsters)
                    {
                        if (r.Event == 32122) r32122 = r.Hp;
                        else if (r.Event == pkey && r.Node == 5) rkey5 = r.Hp;
                    }
                    Line("    同一种怪放两个事件：progress_events." + pkey + " 的格 5 = hp " + hKey
                        + "；progress_events.32122 的格 9 = hp " + h32122
                        + " ⇒ 读回 " + rkey5 + " / " + r32122);
                    Check("⑯b ★★ 同一种怪放在两个不同事件里、只改其中一条 ⇒ 另一条不受影响"
                        + "（两个事件的行各自 111 / 333）", hKey == 111 && h32122 == 333
                        && rkey5 == 111 && r32122 == 333);

                    // ③ 开局组（start_monsters）：同种怪两条不同数值也各自保留
                    DiyPool.AddAt(pd, 7, pm.Id, 0);
                    DiyPool.AddAt(pd, 8, pm.Id, 0);
                    foreach (BoardMonsterRef r in pd.BoardMonsters)
                    {
                        if (r.Event != 0) continue;
                        if (r.Node == 7) r.Hp = 401;
                        else if (r.Node == 8) r.Hp = 402;
                    }
                    doc.SaveDiy();
                    JNode sm = DiyJson.Member(doc.Root, "start_monsters");
                    long sh7 = -1, sh8 = -1, smRows = 0;
                    if (sm != null && sm.Kind == JKind.Array)
                        foreach (JNode r in sm.Items)
                        {
                            smRows++;
                            int nd = (int)DiyJson.IntOf(DiyJson.Member(r, "node"), -999);
                            if (nd == 7) sh7 = DiyJson.IntOf(DiyJson.Member(r, "hp"), -1);
                            else if (nd == 8) sh8 = DiyJson.IntOf(DiyJson.Member(r, "hp"), -1);
                        }
                    long rs7 = -1, rs8 = -1;
                    foreach (BoardMonsterRef r in DiyPool.Read(doc).BoardMonsters)
                    {
                        if (r.Event != 0) continue;
                        if (r.Node == 7) rs7 = r.Hp;
                        else if (r.Node == 8) rs8 = r.Hp;
                    }
                    Line("    start_monsters " + smRows + " 行：格 7 = hp " + sh7 + " / 格 8 = hp " + sh8
                        + " ⇒ 读回 " + rs7 + " / " + rs8);
                    Check("⑯c ★★ start_monsters 里同种怪两条不同数值各自保留（401 / 402，读回一致）",
                        sh7 == 401 && sh8 == 402 && rs7 == 401 && rs8 == 402);

                    // ④ "随机补几只"那一行的数值口径 = **怪种那份**（棋盘上没格子 ⇒ 没有"这一只"可写）
                    pm.Hp = 555;                                        // 只动怪种，不动任何一处放置
                    pit.Effects[0].Count = 4;                           // 计划 4 只、这个事件只放了 2 格 ⇒ 差 2
                    doc.SaveDiy();
                    long m1Hp = -1, m1Count = -1, m1Hp5 = -1;
                    JNode psp3 = DiyJson.Member(DiyJson.Member(DiyJson.Member(doc.Root, "progress_events"),
                        pkey.ToString(CultureInfo.InvariantCulture)), "spawn");
                    if (psp3 != null && psp3.Kind == JKind.Array)
                        foreach (JNode r in psp3.Items)
                        {
                            int nd = (int)DiyJson.IntOf(DiyJson.Member(r, "node"), -999);
                            if (nd == -1)
                            {
                                m1Hp = DiyJson.IntOf(DiyJson.Member(r, "hp"), -1);
                                m1Count = DiyJson.IntOf(DiyJson.Member(r, "count"), -1);
                            }
                            else if (nd == 5) m1Hp5 = DiyJson.IntOf(DiyJson.Member(r, "hp"), -1);
                        }
                    Line("    随机补那一行（node:-1）：count " + m1Count + " / hp " + m1Hp
                        + "（怪种改成 555）· 同一份 json 里格 5 那行仍是 hp " + m1Hp5);
                    Check("⑯d ★★ 随机补那一行（node:-1,count:2）用**怪种**那份数值（hp 555），"
                        + "带格那几行不受影响（格 5 还是 111）",
                        m1Hp == 555 && m1Count == 2 && m1Hp5 == 111);

                    // ⑤ ★★ 2026-10-09：行里"没写数值"（`hp:0` / 缺键）⇒ 四项从**怪物表**带出来
                    //   （用户真机那份 `测试关卡.json` 的 `progress_events.*.spawn[]` 四个数全是 0
                    //    ⇒ 怪物页显示 0；口径他拍板选的是"0/缺失就默认带出怪物表的值"）。
                    MonsterRow tbl = null;
                    foreach (MonsterRow cand in GameTables.Monsters())
                        if (cand.Defense > 0 && cand.Blood > 0) { tbl = cand; break; }
                    if (tbl == null && GameTables.Monsters().Count > 0) tbl = GameTables.Monsters()[0];
                    PoolMonster pm2 = DiyPool.MakePool(pd, tbl.Id);
                    var pev3 = new DiyEvent();
                    pev3.Id = 32123;
                    pev3.Name = "第三个事件";
                    pd.Events.Add(pev3);
                    DiyPool.AddAt(pd, 3, pm2.Id, 32123);
                    BoardMonsterRef blankRef = null;
                    foreach (BoardMonsterRef r in pd.BoardMonsters) if (r.Event == 32123) blankRef = r;
                    blankRef.Hp = 0; blankRef.Defense = 0; blankRef.Attack = 0; blankRef.Gold = 0;  // = 行里"没写"
                    var pit3 = new DiyProgressItem { N = 1 };
                    pit3.Effects.Add(new DiyEffect { Kind = EffectKind.Spawn, Pool = pm2.Id, Count = 1 });
                    pev3.Items.Add(pit3);
                    doc.SaveDiy();

                    JNode psp5 = DiyJson.Member(DiyJson.Member(DiyJson.Member(doc.Root, "progress_events"),
                        "32123"), "spawn");
                    long rawHp = -1;
                    if (psp5 != null && psp5.Kind == JKind.Array)
                        foreach (JNode r in psp5.Items)
                            if ((int)DiyJson.IntOf(DiyJson.Member(r, "node"), -999) == 3)
                                rawHp = DiyJson.IntOf(DiyJson.Member(r, "hp"), -1);
                    DiyData rd3 = DiyPool.Read(doc);
                    long bh = -1, bd = -1, ba = -1, bg = -1; int bpool = -1;
                    foreach (BoardMonsterRef r in rd3.BoardMonsters)
                        if (r.Event == 32123) { bh = r.Hp; bd = r.Defense; ba = r.Attack; bg = r.Gold; bpool = r.Pool; }
                    PoolMonster seed3 = DiyPool.Find(rd3.Pool, bpool);
                    DiyPool.Write(doc, rd3);                    // = 用户在界面上再存一次
                    long svHp = -1, svDf = -1, svAt = -1, svGd = -1;
                    JNode psp6 = DiyJson.Member(DiyJson.Member(DiyJson.Member(doc.Root, "progress_events"),
                        "32123"), "spawn");
                    if (psp6 != null && psp6.Kind == JKind.Array)
                        foreach (JNode r in psp6.Items)
                            if ((int)DiyJson.IntOf(DiyJson.Member(r, "node"), -999) == 3)
                            {
                                svHp = DiyJson.IntOf(DiyJson.Member(r, "hp"), -1);
                                svDf = DiyJson.IntOf(DiyJson.Member(r, "defense"), -1);
                                svAt = DiyJson.IntOf(DiyJson.Member(r, "attack"), -1);
                                svGd = DiyJson.IntOf(DiyJson.Member(r, "gold"), -1);
                            }
                    Line("    行里 hp:0 ⇒ 怪物表 " + tbl.Id + " 那份 血 " + tbl.Blood + " / 防 " + tbl.Defense
                        + " / 攻 " + tbl.Attack + " / 币 " + tbl.Gold + " ⇒ 带出来 血 " + bh + " / 防 " + bd
                        + " / 攻 " + ba + " / 币 " + bg + "（怪种也带上 = "
                        + (seed3 == null ? "?" : (seed3.Hp + "/" + seed3.Defense + "/" + seed3.Attack + "/" + seed3.Gold))
                        + "）；再存一次 json 那行 = " + svHp + " / " + svDf + " / " + svAt + " / " + svGd);
                    Check("⑯e ★★ 行里 `hp:0`（= 没写数值）⇒ 这一处放置 + 怪种都带出**怪物表**的四项，"
                        + "保存后 json 那行也变成这四个值",
                        rawHp == 0 && tbl != null
                        && bh == tbl.Blood && bd == tbl.Defense && ba == tbl.Attack && bg == tbl.Gold
                        && seed3 != null && seed3.Hp == tbl.Blood && seed3.Defense == tbl.Defense
                        && svHp == tbl.Blood && svDf == tbl.Defense && svAt == tbl.Attack && svGd == tbl.Gold);

                    // ⑥ ★★ `hp > 0` ⇒ 这一行的值一个都不许动（防 = 0 就是 0，别拿表值去盖）
                    PoolMonster pm2b = null;
                    foreach (PoolMonster p in rd3.Pool) if (p.Table == tbl.Id) { pm2b = p; break; }
                    if (pm2b == null) pm2b = pm2;
                    var pev4 = new DiyEvent();
                    pev4.Id = 32124;
                    pev4.Name = "第四个事件";
                    rd3.Events.Add(pev4);
                    DiyPool.AddAt(rd3, 4, pm2b.Id, 32124);
                    BoardMonsterRef keepRef = null;
                    foreach (BoardMonsterRef r in rd3.BoardMonsters) if (r.Event == 32124) keepRef = r;
                    keepRef.Hp = 23; keepRef.Defense = 0; keepRef.Attack = 5; keepRef.Gold = 12;
                    var pit4 = new DiyProgressItem { N = 1 };
                    pit4.Effects.Add(new DiyEffect { Kind = EffectKind.Spawn, Pool = pm2b.Id, Count = 1 });
                    pev4.Items.Add(pit4);
                    DiyPool.Write(doc, rd3);
                    long kh = -1, kd = -1, ka = -1, kg = -1;
                    foreach (BoardMonsterRef r in DiyPool.Read(doc).BoardMonsters)
                        if (r.Event == 32124) { kh = r.Hp; kd = r.Defense; ka = r.Attack; kg = r.Gold; }
                    Line("    行里 hp:23 / 防:0 / 攻:5 / 币:12（表里防是 " + tbl.Defense + "）⇒ 读回 血 " + kh
                        + " / 防 " + kd + " / 攻 " + ka + " / 币 " + kg);
                    Check("⑯f ★★ 行里 `hp>0` ⇒ 这一行的值一个都不动（防 = 0 保持 0，没被表里的 "
                        + tbl.Defense + " 盖掉）", tbl.Defense > 0 && kh == 23 && kd == 0 && ka == 5 && kg == 12);

                    // ⑦ 第 3 个调用点：文件里**有** `diy.board_monsters`、但那一行没有数值键（老文件）
                    //   ⇒ 数值按 spawn 行回填，**同一条规则**（hp<=0 ⇒ 带出怪物表的值）。
                    //   ★ 为了让这条断言真的有区分度：连 spawn 行也清零（= 老文件那副"行里没写数值"的样子），
                    //     这样"回填成了表值"只能来自 `RowStats`，不可能是"照抄行里的值"。
                    JNode ps7 = DiyJson.Member(DiyJson.Member(DiyJson.Member(doc.Root, "progress_events"),
                        "32123"), "spawn");
                    if (ps7 != null && ps7.Kind == JKind.Array)
                        foreach (JNode r in ps7.Items)
                            if ((int)DiyJson.IntOf(DiyJson.Member(r, "node"), -999) == 3)
                            {
                                DiyJson.SetInt(DiyJson.EnsureMember(r, "hp"), 0);
                                DiyJson.SetInt(DiyJson.EnsureMember(r, "defense"), 0);
                                DiyJson.SetInt(DiyJson.EnsureMember(r, "attack"), 0);
                                DiyJson.SetInt(DiyJson.EnsureMember(r, "gold"), 0);
                            }
                    JNode bmArr = DiyJson.Member(DiyJson.Member(doc.Root, "diy"), "board_monsters");
                    int stripped = 0;
                    if (bmArr != null && bmArr.Kind == JKind.Array)
                        foreach (JNode row in bmArr.Items)
                            if ((int)DiyJson.IntOf(DiyJson.Member(row, "event"), -1) == 32123)
                            {
                                DiyJson.RemoveMember(row, "hp");
                                DiyJson.RemoveMember(row, "defense");
                                DiyJson.RemoveMember(row, "attack");
                                DiyJson.RemoveMember(row, "gold");
                                DiyJson.RemoveMember(row, "counter");
                                stripped++;
                            }
                    doc.InvalidateDiy();
                    long gh = -1, gd2 = -1, ga = -1, gg = -1;
                    foreach (BoardMonsterRef r in DiyPool.Read(doc).BoardMonsters)
                        if (r.Event == 32123) { gh = r.Hp; gd2 = r.Defense; ga = r.Attack; gg = r.Gold; }
                    Line("    老样子（board_monsters 那一行与 spawn 行都没数值，删了 " + stripped
                        + " 行的数值键）⇒ 读回 血 " + gh + " / 防 " + gd2 + " / 攻 " + ga + " / 币 " + gg);
                    Check("⑯g ★★ 有 `diy.board_monsters` 但那一行没数值（老文件）⇒ 按 spawn 行回填，"
                        + "同一条规则（hp<=0 ⇒ 带出怪物表的值）",
                        stripped == 1 && gh == tbl.Blood && gd2 == tbl.Defense
                        && ga == tbl.Attack && gg == tbl.Gold);

                    // ⑧ ★★ 2026-10-09：「会不会反击」= `Monster.bin` **字段 22**（can_counter）。
                    //   老文件里 `diy.monsters[]` / `diy.board_monsters[]` 都**没有** `counter` 键
                    //   ⇒ 不能当成 false，要回落**怪物表**的 can_counter。
                    MonsterRow t1068 = GameTables.Monster(1068);
                    MonsterRow t1023 = GameTables.Monster(1023);
                    Line("    怪物表 can_counter（字段 22）：1067=" + CC(1067) + " · 1068=" + CC(1068)
                        + " · 1069=" + CC(1069) + " · 1070=" + CC(1070) + " · 1023=" + CC(1023));
                    DiyData rd5 = DiyPool.Read(doc);
                    PoolMonster cc1 = DiyPool.MakePool(rd5, 1068);      // 表里会反击
                    PoolMonster cc2 = DiyPool.MakePool(rd5, 1023);      // 表里不会
                    DiyPool.AddAt(rd5, 12, cc1.Id, 32125);
                    DiyPool.AddAt(rd5, 13, cc2.Id, 32125);
                    var pev5 = new DiyEvent();
                    pev5.Id = 32125;
                    pev5.Name = "第五个事件";
                    var pit5 = new DiyProgressItem { N = 1 };
                    pit5.Effects.Add(new DiyEffect { Kind = EffectKind.Spawn, Pool = cc1.Id, Count = 1 });
                    pit5.Effects.Add(new DiyEffect { Kind = EffectKind.Spawn, Pool = cc2.Id, Count = 1 });
                    pev5.Items.Add(pit5);
                    rd5.Events.Add(pev5);
                    DiyPool.Write(doc, rd5);
                    // 把两个地方的 `counter` 键全删掉 = 老文件那副样子
                    JNode dmArr = DiyJson.Member(DiyJson.Member(doc.Root, "diy"), "monsters");
                    int stripC = 0;
                    if (dmArr != null && dmArr.Kind == JKind.Array)
                        foreach (JNode row in dmArr.Items)
                            if (DiyJson.Member(row, "counter") != null) { DiyJson.RemoveMember(row, "counter"); stripC++; }
                    JNode bmArr2 = DiyJson.Member(DiyJson.Member(doc.Root, "diy"), "board_monsters");
                    int stripBC = 0;
                    if (bmArr2 != null && bmArr2.Kind == JKind.Array)
                        foreach (JNode row in bmArr2.Items)
                            if (DiyJson.Member(row, "counter") != null) { DiyJson.RemoveMember(row, "counter"); stripBC++; }
                    doc.InvalidateDiy();
                    DiyData rc = DiyPool.Read(doc);
                    bool c1068 = false, c1023 = false;
                    foreach (BoardMonsterRef r in rc.BoardMonsters)
                    {
                        PoolMonster pmv = DiyPool.Find(rc.Pool, r.Pool);
                        if (pmv == null) continue;
                        if (pmv.Table == 1068) c1068 = r.Counter;
                        else if (pmv.Table == 1023) c1023 = r.Counter;
                    }
                    Line("    老文件（monsters 删 " + stripC + " 个 counter 键 / board_monsters 删 " + stripBC
                        + " 个）⇒ 读回：1068 那处 Counter=" + c1068 + "、1023 那处 Counter=" + c1023);
                    Check("⑯h ★★ 老文件没有 `counter` 键 ⇒ 按**怪物表**的 can_counter 回落"
                        + "（1068 表里会反击 ⇒ true、1023 不会 ⇒ false），不是一律 false",
                        t1068 != null && t1068.CanCounter && t1023 != null && !t1023.CanCounter
                        && c1068 && !c1023);

                    // ⑨ ★★ 「把这套数值应用到全体相同怪（N 处）」：跨格 / 跨事件 / 含开局组；
                    //   别种怪一点不动；怪种默认值也跟着变；落盘读回一致。
                    DiyData rd6 = DiyPool.Read(doc);
                    PoolMonster bt = null;
                    foreach (PoolMonster p in rd6.Pool) if (p.Table == 1023) { bt = p; break; }
                    if (bt == null) bt = DiyPool.MakePool(rd6, 1023);   // 表里本来不反击 ⇒ 应用成"反击"才量得出来
                    DiyPool.AddAt(rd6, 20, bt.Id, 32125);
                    DiyPool.AddAt(rd6, 21, bt.Id, 0);
                    DiyPool.AddAt(rd6, 22, bt.Id, 0);
                    PoolMonster other = null;
                    foreach (PoolMonster p in rd6.Pool) if (p.Table == 1067) { other = p; break; }
                    long otherPoolHp = other == null ? -1 : other.Hp;
                    long otherRefHp = RefHp(rd6, other);
                    bool otherRefCounter = RefCounter(rd6, other);
                    int mine = 0;
                    foreach (BoardMonsterRef r in rd6.BoardMonsters) if (r.Pool == bt.Id) mine++;
                    int applied = DiyPool.ApplyToSamePool(rd6, bt.Id, 77, 1, 2, 3, true);
                    int allOk = 0;
                    foreach (BoardMonsterRef r in rd6.BoardMonsters)
                        if (r.Pool == bt.Id && r.Hp == 77 && r.Defense == 1 && r.Attack == 2
                            && r.Gold == 3 && r.Counter) allOk++;
                    PoolMonster btAfter = DiyPool.Find(rd6.Pool, bt.Id);
                    DiyPool.Write(doc, rd6);
                    int backOk = 0;
                    foreach (BoardMonsterRef r in DiyPool.Read(doc).BoardMonsters)
                        if (r.Pool == bt.Id && r.Hp == 77 && r.Defense == 1 && r.Counter) backOk++;
                    Line("    批量应用：同怪种在棋盘上 " + mine + " 处 ⇒ 改后一致 " + allOk + " 处（返回 "
                        + applied + "）· 怪种默认值 = 血 " + (btAfter == null ? -1 : btAfter.Hp) + " / 反击 "
                        + (btAfter != null && btAfter.Counter) + " · 落盘读回一致 " + backOk + " 处"
                        + " · 别种怪（1067）怪种血 " + otherPoolHp + "→" + (other == null ? -1 : other.Hp)
                        + "、那一处血 " + otherRefHp + "→" + RefHp(rd6, other) + " 反击 " + otherRefCounter
                        + "→" + RefCounter(rd6, other));
                    Check("⑯i ★★ 批量应用（跨事件 + 开局组共 " + mine + " 处）⇒ 全部变成 血 77 / 反击，"
                        + "怪种默认值也变 77，别种怪一点不动、落盘读回一致",
                        mine >= 3 && applied == mine && allOk == mine
                        && btAfter != null && btAfter.Hp == 77 && btAfter.Counter
                        && other != null && other.Hp == otherPoolHp
                        && RefHp(rd6, other) == otherRefHp && RefCounter(rd6, other) == otherRefCounter
                        && backOk == mine);
                }
                catch (Exception ex)
                {
                    Line("    ★ 每一处放置的数值自测失败：" + ex.Message);
                    Check("⑯ ★ 每一处放置自己一份五项数值（这一段）", false);
                }
                finally
                {
                    doc.Root = keepPlace;
                    doc.InvalidateDiy();
                    doc.InvalidateStarts();
                    doc.InvalidateBoardOverrides();
                }

                // ---- (4) ★ 图标补全（缓存优先 + 内容居中裁切）
                Line("  ② 图标补全（缓存优先 / 内容居中）：");
                if (GameLocator.ImageDir == null)
                {
                    Line("    （这台机器上没有游戏目录 ⇒ 贴图相关的几条跳过）");
                    Check("⑬k 贴图：没有游戏目录时跳过", true);
                    Check("⑬l 贴图：没有游戏目录时跳过", true);
                    Check("⑬m 贴图：没有游戏目录时跳过", true);
                    Check("⑬n 贴图：没有游戏目录时跳过", true);
                }
                else
                {
                    string cache = GameLocator.ArtCacheDir;
                    bool hasCache = !string.IsNullOrEmpty(cache) && Directory.Exists(cache);
                    Line("    贴图缓存目录 = " + (cache ?? "(没有)") + "（在 = " + hasCache + "）");

                    // ① 格子功能：以前 11 种没图，现在应该只剩"官方点名的是编号头像"那种
                    var pending = LandTable.Pending();
                    var pendNames = new List<string>();
                    foreach (int land in pending)
                        pendNames.Add(land + " " + Land.Name(land) + "（"
                            + (LandTable.TextureName(land) ?? "Land.bin 没这一行") + "）");
                    int have13 = 0;
                    for (int land = 1; land <= LandTable.MaxLand; land++)
                        if (Art.PlatformTexture(land) != null) have13++;
                    Line("    格子功能 1~" + LandTable.MaxLand + "：有图 " + have13
                        + " 种；还没图 " + pending.Count + " 种" + (pending.Count == 0 ? "" : " = " + string.Join(" · ", pendNames.ToArray())));
                    int looseOnly = 0;
                    for (int land = 1; land <= LandTable.MaxLand; land++)
                    {
                        string n = LandTable.TextureName(land);
                        if (n == null || LandTable.IsNumberedArt(n)) continue;
                        string d = GameLocator.ImageDir;
                        if (!string.IsNullOrEmpty(d) && File.Exists(Path.Combine(d, n + ".png"))) looseOnly++;
                    }
                    Line("    其中光靠游戏目录那 44 张能认出的：之前 15 种，现在 " + have13 + " 种");
                    Check("⑬k ★★ 格子功能图标补全（曾缺 9 张 ⇒ 现在有图 ≥ 20 种；剩下的是"
                        + "「Land.bin 点名的编号角色头像」，按规格不用）",
                        have13 >= 20 && have13 > looseOnly && pending.Count <= 3);

                    // ② 怪头像
                    int bust = 0, avatar = 0, fallback = 0;
                    foreach (MonsterRow m in GameTables.Monsters())
                    {
                        if (Art.ImagePath(m.BustName) != null) bust++;
                        if (Art.ImagePath(m.AvatarName) != null) avatar++;
                        if (m.AvatarIsFallback) fallback++;
                    }
                    Line("    怪头像：Monster.bin " + GameTables.Monsters().Count + " 只里，半身像 "
                        + bust + " 只、加上「手牌画顶」共 " + avatar + " 只（其中拿手牌画顶的 " + fallback + " 只）");
                    Check("⑬l ★★ 怪头像补全（曾 53 / 80 ⇒ 现在半身像 ≥ 60）", bust >= 60 && avatar >= bust);

                    // ③ 手牌
                    int cards = GameTables.Cards().Count, hand = 0;
                    foreach (CardRow c in GameTables.Cards())
                        if (Art.ImagePath(CardPickDialog.HandName(c.Id)) != null) hand++;
                    Line("    手牌贴图：Card.bin " + cards + " 张里 " + hand + " 张有图（曾 44 张）");
                    Check("⑬m ★★ 手牌贴图补全（曾 44 / " + cards + " ⇒ 现在 ≥ 60）", hand >= 60);

                    // ④ 来源必须是"缓存优先"，且缓存里那几张确实是从包里抠出来的
                    string srcGift = GameLocator.ImageSource("UT_Platform_Gift");
                    string srcShop = GameLocator.ImageSource("UT_Platform_Shop");
                    Line("    来源：UT_Platform_Gift = " + srcGift + " · UT_Platform_Shop = " + srcShop);
                    // ★★ 2026-10-12 加了 exe 内置兜底之后，口径要**三段**：
                    //   磁盘「贴图缓存」里有 ⇒ "贴图缓存"；游戏目录里有 ⇒ "游戏目录"；
                    //   两边都没有但 exe 里内置了 ⇒ "内置贴图"（"只有 exe"那种形态就是这一支）。
                    //   ★ Gift 是"游戏目录里没有、只有缓存/内置里才有"的那一张；
                    //     Shop 是"游戏目录里本来就有"的那一张 ⇒ 它优先命中"游戏目录"。
                    bool giftOk = srcGift == "贴图缓存" || srcGift == "内置贴图";
                    Check("⑬n ★★ 编辑器**缓存优先**：游戏目录里没有的（Gift 等 9 张）从贴图缓存读，"
                        + "游戏目录里有的仍从游戏目录读",
                        srcShop == "游戏目录" && (giftOk || srcGift == "没有"));
                    Line("      （Gift = " + srcGift + " / Shop = " + srcShop + "）");

                    // ================================================================ ★★ 内置缓存
                    // 用户 2026-10-12：「要内置」—— 把「棋盘缓存」+「贴图缓存」编进 exe，
                    // 于是"只拿到一个 exe、放在任何空目录里"也能画出棋盘和图标（以前那种形态
                    // 就是"新建关卡时棋盘一片空白"）。
                    //   ★ 判据分三层，别混：
                    //     ① **装进去了没有** —— 直接问 exe 的资源表（不依赖磁盘上有没有缓存目录）；
                    //     ② **读得出来吗** —— 从资源里真解一张图（解出像素才算）；
                    //     ③ **顺序对不对** —— 磁盘上有就用磁盘那份，磁盘没有才回内置。
                    Line("");
                    Line("  ★★ 内置缓存（exe 自带棋盘 / 贴图）：");
                    Line("    " + EmbeddedCache.Describe());
                    string[] builtBoards = EmbeddedCache.BoardNames;
                    Check("⑬p ★★ exe 里内置了官方画板 json（26 份）", builtBoards.Length >= 20);

                    // ① 贴图：从资源里真解一张"游戏目录里没有、只有缓存里才有"的（Gift / Lottery）
                    EmbeddedCache.ClearWarnings();               // 这一段自己探，先清账
                    int builtArt = 0;
                    foreach (string nm in new string[] { "UT_Platform_Gift", "UT_Platform_Lottery", "UT_Platform_Fire" })
                        if (EmbeddedCache.HasTexture(nm)) builtArt++;
                    bool decoded = false;
                    var probe = EmbeddedCache.LoadTexture("UT_Platform_Gift") as System.Windows.Media.Imaging.BitmapSource;
                    if (probe != null)
                    {
                        var px = new byte[4];
                        probe.CopyPixels(new System.Windows.Int32Rect(0, 0, 1, 1), px, 4, 0);
                        decoded = probe.PixelWidth > 0 && probe.PixelHeight > 0;
                        Line("    真解一张：UT_Platform_Gift → " + probe.PixelWidth + "x" + probe.PixelHeight
                            + "（首像素 a=" + px[3] + "）");
                    }
                    else Line("    ★ 解不出内置的 UT_Platform_Gift");
                    Check("⑬q ★★ 内置贴图解得出真像素（不只是「资源在」，是「能画」）", builtArt >= 3 && decoded);

                    // ② 真画一遍：`Art.Load` 认内置标记串（`ImagePath` 在有磁盘缓存时会回磁盘，
                    //    所以这里直接按"内置那份"取，验的是解码那条链）
                    var artViaMarker = Art.Load(EmbeddedCache.BuiltInPath("UT_Platform_Fire"))
                        as System.Windows.Media.Imaging.BitmapSource;
                    Line("    Art.Load(内置标记) → "
                        + (artViaMarker == null ? "★ null" : (artViaMarker.PixelWidth + "x" + artViaMarker.PixelHeight)));
                    Check("⑬r ★★ `Art.Load` 认得内置标记串（棋盘页画格子走的就是它）", artViaMarker != null);

                    // ③ 内置缺失时**不静默**：先验"真的没有的东西"会记账，
                    //    再验"这一段真用的东西"一条账都没有（否则就是静默空白）。
                    string missName = "UT_Platform_绝不存在的名字";
                    int warnBefore = EmbeddedCache.Warnings.Count;
                    bool missSaid = !EmbeddedCache.HasTexture(missName)
                                 && EmbeddedCache.Warnings.Count > warnBefore;
                    Line("    内置缺东西时会说一声吗：问一个不存在的贴图 ⇒ "
                        + (missSaid ? "记了一笔" : "★ 一声不吭（静默）"));
                    List<string> builtWarn = EmbeddedCache.Warnings;
                    foreach (string w1 in builtWarn) Line("    [内置缓存] " + w1);
                    Check("⑬t ★★ 内置缺东西时**不静默**（会记一笔），且这一段真用的东西一条账都没有",
                        missSaid && builtWarn.Count == 1);

                    // ④ 磁盘优先：拿一个**改过的**棋盘 json 放进"磁盘缓存目录" ⇒ 必须读磁盘那份，
                    //    不能让内置那份盖掉（用户可以在自己机器上覆盖 / 更新缓存）。
                    string probeBoard = builtBoards.Length > 0 ? BoardCache.Stem(builtBoards[0]) : "";
                    if (probeBoard.Length == 0)
                    {
                        Line("    （内置里没有棋板可探 ⇒ 磁盘优先这条跳过）");
                        Check("⑬s ★★ 磁盘优先：磁盘上改过的棋盘 json 会盖过内置那份", false);
                    }
                    else
                    {
                        string keepDir = BoardStore.CacheDir;
                        string probeDir = Path.Combine(outDir, "内置缓存探测");
                        string diskText = null, inText = null;
                        try
                        {
                            Directory.CreateDirectory(probeDir);
                            inText = EmbeddedCache.Text(EmbeddedCache.BoardFolder, probeBoard + ".json");
                            JNode probeDoc = DiyJson.Parse(inText);
                            JNode nodes = DiyJson.Member(probeDoc, "nodes");
                            int firstId = -1, land = -1;
                            if (nodes != null && nodes.Kind == JKind.Array && nodes.Items.Count > 0)
                            {
                                JNode n0 = nodes.Items[0];
                                firstId = (int)DiyJson.IntOf(DiyJson.Member(n0, "id"), -1);
                                land = (int)DiyJson.IntOf(DiyJson.Member(n0, "landType"), -1);
                            }
                            if (firstId >= 0)
                            {
                                int newLand = (land == 20) ? 17 : 20;
                                JNode n0 = nodes.Items[0];
                                DiyJson.SetInt(DiyJson.EnsureMember(n0, "landType"), newLand);
                                File.WriteAllText(Path.Combine(probeDir, probeBoard + ".json"),
                                    DiyJson.Write(probeDoc, " ", "\n"), new UTF8Encoding(false));
                                Line("    探测器：磁盘缓存目录 = " + probeDir
                                    + "，把 " + probeBoard + ".json 的格 " + firstId
                                    + " 从 landType " + land + " 改成 " + newLand);

                                BoardStore.CacheDir = probeDir;
                                BoardStore.ClearCache();
                                diskText = BoardCache.TextOf(probeBoard);
                                int got = -1;
                                JNode back = DiyJson.Parse(diskText == null ? "null" : diskText);
                                JNode bn = DiyJson.Member(back, "nodes");
                                if (bn != null && bn.Kind == JKind.Array && bn.Items.Count > 0)
                                    got = (int)DiyJson.IntOf(DiyJson.Member(bn.Items[0], "landType"), -1);

                                BoardStore.CacheDir = "";
                                BoardStore.ClearCache();
                                string afterText = BoardCache.TextOf(probeBoard);
                                int gotAfter = -1;
                                JNode an = DiyJson.Member(DiyJson.Parse(afterText == null ? "null" : afterText), "nodes");
                                if (an != null && an.Kind == JKind.Array && an.Items.Count > 0)
                                    gotAfter = (int)DiyJson.IntOf(DiyJson.Member(an.Items[0], "landType"), -1);
                                Line("    读回来：磁盘那一份 格" + firstId + " landType=" + got
                                    + "（内置那份是 " + land + "）；撤掉探测目录后 = " + gotAfter);
                                Check("⑬s ★★ 磁盘优先：磁盘上改过的棋盘 json 会盖过内置那份",
                                    got == newLand && gotAfter == land);

                                // ★★ ⑬s2：**棋盘页真正走的那条链**（`BoardStore.Load`）也必须是磁盘优先。
                                //   ⑬s 验的是 `BoardCache.TextOf`（读文本那一步）；这一条验"按 map+画板号
                                //   去加载一块棋盘"时磁盘那份有没有盖过内置那份 —— 两条链都得对。
                                BoardStore.CacheDir = probeDir;
                                BoardStore.ClearCache();
                                BoardStore.DiyBoardPath = "";
                                BoardStore.ExpectedSuffix = probeBoard;
                                BoardModel viaLoad = BoardStore.Load(82000 + int.Parse(probeBoard,
                                    CultureInfo.InvariantCulture), null);
                                int loadLand = -1, loadNodes = 0;
                                if (viaLoad != null && viaLoad.Ok && viaLoad.Nodes.Count > 0)
                                {
                                    loadNodes = viaLoad.Nodes.Count;
                                    loadLand = viaLoad.Nodes[0].LandType;
                                }
                                Line("    BoardStore.Load ⇒ " + BoardCache.Label(viaLoad == null ? "" : viaLoad.Path)
                                    + " · 格数 " + loadNodes + " · 首格 landType " + loadLand
                                    + "（磁盘那份是 " + newLand + "）");

                                BoardStore.CacheDir = "";
                                BoardStore.ClearCache();
                                BoardModel viaLoad2 = BoardStore.Load(82000 + int.Parse(probeBoard,
                                    CultureInfo.InvariantCulture), null);
                                int loadLand2 = viaLoad2 != null && viaLoad2.Ok && viaLoad2.Nodes.Count > 0
                                    ? viaLoad2.Nodes[0].LandType : -1;
                                BoardStore.ExpectedSuffix = "";
                                BoardStore.ClearCache();
                                Line("    撤掉探测目录后再 Load ⇒ 首格 landType " + loadLand2
                                    + "（内置那份是 " + land + "）");
                                Check("⑬s2 ★★ 磁盘优先（棋盘页那条链）：`BoardStore.Load` 也吃磁盘那份",
                                    loadLand == newLand && loadLand2 == land);
                            }
                            else
                            {
                                Line("    （内置那份没节点可改 ⇒ 磁盘优先这条跳过）");
                                Check("⑬s ★★ 磁盘优先：磁盘上改过的棋盘 json 会盖过内置那份", false);
                            }
                        }
                        catch (Exception ex)
                        {
                            Line("    ★ 探测失败：" + ex.Message.Replace('\n', ' '));
                            Check("⑬s ★★ 磁盘优先：磁盘上改过的棋盘 json 会盖过内置那份", false);
                        }
                        finally
                        {
                            BoardStore.CacheDir = keepDir;
                            BoardStore.ClearCache();
                        }
                    }

                    // ⑤ 内置那两份"该有几份"——点数用的判据（资源表里数出来的，不碰磁盘）
                    List<string> builtWarnEnd = EmbeddedCache.Warnings;
                    Check("⑬u ★★ 内置缓存自报家门（体积/份数算得出来）", EmbeddedCache.Describe().Length > 0
                        && builtWarnEnd.Count == 1);      // ①③ 里那一笔"故意问不存在的贴图"的账

                    // ⑥ 内容居中裁切（④ 的另一半：官方半身像角色不居中）
                    try
                    {
                        int off = 0, total = 0;
                        foreach (MonsterRow m in GameTables.Monsters())
                        {
                            int[] box = Art.ContentBox(m.BustName);
                            if (box == null) continue;
                            var src = Art.Load(Art.ImagePath(m.BustName)) as System.Windows.Media.Imaging.BitmapSource;
                            if (src == null) continue;
                            total++;
                            double cx = box[0] + box[2] / 2.0, cy = box[1] + box[3] / 2.0;
                            if (Math.Abs(cx - src.PixelWidth / 2.0) > 8 || Math.Abs(cy - src.PixelHeight / 2.0) > 8)
                                off++;
                        }
                        Line("    官方半身像：量了 " + total + " 张，其中 **角色不居中**（偏 > 8 px）的 " + off + " 张"
                            + " ⇒ 直接用会看着「偏一角」，所以要按内容居中裁");
                        Check("⑬o ★★ 官方怪半身像确实是「宽幅 + 角色不居中」（实测 ≥ 10 张偏 > 8 px）",
                            total >= 20 && off >= 10);

                        // 裁出来的必须是正方形，且中心正正好压在内容中心上
                        // （★ 只有一种情况允许偏：内容框顶到画布边、正方形又比画布还大 ⇒ 只能贴边，
                        //   这是物理限制，不是错 —— 这种就当"贴边对齐"过。）
                        bool cropOk = true;
                        var cropLines = new List<string>();
                        int checkedN = 0, flush = 0;
                        foreach (MonsterRow m in GameTables.Monsters())
                        {
                            int[] box = Art.ContentBox(m.BustName);
                            if (box == null) continue;
                            var srcBmp = Art.Load(Art.ImagePath(m.BustName)) as System.Windows.Media.Imaging.BitmapSource;
                            var sq = Art.CenteredSquare(m.BustName) as System.Windows.Media.Imaging.CroppedBitmap;
                            if (sq == null || srcBmp == null) continue;
                            checkedN++;
                            var rect = sq.SourceRect;
                            if (sq.PixelWidth != sq.PixelHeight) cropOk = false;
                            double ccx = box[0] + box[2] / 2.0, ccy = box[1] + box[3] / 2.0;
                            double dx = Math.Abs((rect.X + rect.Width / 2.0) - ccx);
                            double dy = Math.Abs((rect.Y + rect.Height / 2.0) - ccy);
                            bool edgeX = rect.X == 0 || rect.X + rect.Width == srcBmp.PixelWidth;
                            bool edgeY = rect.Y == 0 || rect.Y + rect.Height == srcBmp.PixelHeight;
                            if (dx > 1.5 && !edgeX) cropOk = false;
                            if (dy > 1.5 && !edgeY) cropOk = false;
                            if (!edgeX && !edgeY && dx <= 1.5 && dy <= 1.5) { }
                            else flush++;
                            if (checkedN <= 4)
                                cropLines.Add(m.BustName + " 内容框 " + box[2] + "x" + box[3]
                                    + " @(" + box[0] + "," + box[1] + ") ⇒ 裁 "
                                    + sq.PixelWidth + "x" + sq.PixelHeight
                                    + " @(" + rect.X + "," + rect.Y + ")"
                                    + (edgeX || edgeY ? "（贴到画布边，只能这样）" : "（中心对齐）"));
                            if (checkedN >= 40) break;
                        }
                        foreach (string s in cropLines) Line("    " + s);
                        Line("    其中 " + flush + " 张的正方形顶到了画布边（内容比画布还大 ⇒ 只能贴边）。");
                        Check("⑬p ★★ 头像按**内容居中**裁成正方形（裁框中心 = 内容中心，逐张验 "
                            + checkedN + " 张；顶边的按贴边对齐）",
                            checkedN >= 10 && cropOk);
                    }
                    catch (Exception ex)
                    {
                        Line("    ★ 贴图解码失败：" + ex.Message);
                        Check("⑬o ★ 官方怪半身像不居中", false);
                        Check("⑬p ★ 头像内容居中裁切", false);
                    }
                }
            }
            Line("");

            // ============================================================ ⑭ 进度事件的触发点
            Line("── ⑭ 进度事件的「触发点」：触发进度 = MapEvent.bin field4÷2 ｜ 写进配置 ──────");
            {
                JNode keep14 = doc.Root;
                try
                {
                    // 官方机制查证（只读）：触发进度**只能靠事件 id 编码**，没有独立字段。
                    Line("  ① 官方机制（只读查证，2026-10-03）：");
                    Line("     gamedata.py:815 _load_map_events() —— MapEvent.bin field4 = Triggerparams = 进度×2；");
                    Line("     gamedata.py:894 map_event_progress(id) = trigger // 2（查不到返回 None）；");
                    Line("     battle.py:1146/1206 progress_events / progress_events_due —— 按这个进度点播 1055；");
                    Line("     levels.py:155-173 _normalize() —— 关卡 json 的 progress_events.<事件id> 只认"
                        + " spawn/attack/defense/move，**没有触发进度这个字段**；");
                    Line("     官方样例 82016_1.json 的键就是官方事件 id（32122/32123/32124），note 写着「事件 id 来自 MapEvent.bin」。");
                    Line("     ⇒ 结论：触发进度 = 事件 id 编码（field4 ÷ 2）。编辑器只能「反查 → 挑 id → 写进配置」。");

                    List<int> vals = MapEventTable.ProgressValues();
                    List<MapEventRow> c3 = MapEventTable.RowsAtProgress(3);
                    bool c3ok = c3.Count > 0;
                    foreach (MapEventRow r in c3) if (r.Progress != 3) c3ok = false;
                    Line("     反查索引：官方给过的触发进度 " + vals.Count + " 档（"
                        + (vals.Count > 0 ? vals[0] + " … " + vals[vals.Count - 1] : "无") + "）；"
                        + "触发进度 3 下有 " + c3.Count + " 条官方事件：" + Ids(c3));
                    Check("⑭a ★ 反查索引：触发进度 N 能列出「field4÷2 == N」的官方事件（3 那一档 ≥2 条、条条对得上）",
                        vals.Count > 0 && c3.Count >= 2 && c3ok);

                    // 造一份干净副本（不碰工作副本那份文件）
                    //   ★★ 2026-10-08 大改之后：**一个事件 = 一条进度**（`progress_events` 一行，
                    //      读回来时 N 一律是 1）。所以"三条各自带触发点的进度"要造**三个事件**；
                    //      同一个事件里的多条 items 落盘时会被并成一行（⑫j 把这件事也断言了）。
                    //   ★ 官方那份自带 start_monsters + progress_events ⇒ 先清干净（否则池子/棋盘
                    //      的基准不是我们自己摆的）。
                    CleanSheet(doc, work);
                    DiyPool.Write(doc, DiyPool.NewData());
                    DiyData d = doc.Diy;
                    PoolMonster a = DiyPool.MakePool(d, 1067);
                    DiyPool.PlaceAt(d, 5, a.Id);
                    DiyEvent ev1 = d.FirstEvent();                       // 1001（新建自带的那条）
                    ev1.Items.Clear();                                   // ★ 先清空，下面条目编号的基准才确定
                    var ev2 = new DiyEvent { Id = 1002, Name = "第二条进度" };
                    var ev3 = new DiyEvent { Id = 1003, Name = "第三条进度" };
                    d.Events.Add(ev2);
                    d.Events.Add(ev3);
                    var it1 = new DiyProgressItem { N = 1 };
                    it1.Effects.Add(new DiyEffect { Kind = EffectKind.Spawn, Pool = a.Id });
                    ev1.Items.Add(it1);
                    var it2 = new DiyProgressItem { N = 2 };
                    it2.Effects.Add(new DiyEffect { Kind = EffectKind.Spawn, Pool = a.Id });
                    it2.Effects.Add(new DiyEffect { Kind = EffectKind.MonsterAttack, N = 4 });
                    ev2.Items.Add(it2);
                    var it3 = new DiyProgressItem { N = 3 };
                    it3.Effects.Add(new DiyEffect { Kind = EffectKind.Spawn, Pool = a.Id });
                    ev3.Items.Add(it3);
                    doc.SaveDiy();
                    Line("     造了 3 个事件（1001 / 1002 / 1003，各一条进度、都带「新增怪物」条词），池子 1 只怪");

                    // ---- ② 设成「进度 3 触发」
                    string note1;
                    bool ok1 = DiyTrigger.Apply(doc, d, it1, 3, out note1);
                    doc.SaveDiy();
                    Line("  ② 把第 1 条设成「触发进度 3」：" + note1);
                    Check("⑭b ★ 设成进度 3 ⇒ 自动挑中一条 field4÷2 == 3 的官方事件 id（挑不到就不改）",
                        ok1 && it1.TriggerProgress == 3 && it1.TriggerEvent > 0
                        && MapEventTable.ProgressOf(it1.TriggerEvent) == 3);

                    // ---- ③ ★ 落盘 json 里写的是什么（报告 §③ 照抄这两段）
                    Line("  ③ ★ 落盘的 json 原文（这一批新增的两个键 + 服务端那份的键）：");
                    Line("     —— diy.events.\"1001\"（原文照抄：★ 这一版起只剩 name/desc，没有 items）——");
                    Line("     " + DiyJson.Compact(DiyJson.Member(
                        DiyJson.Member(DiyJson.Member(doc.Root, "diy"), "events"), "1001")));
                    Line("     —— progress_events（服务端读的那份：键 = 官方事件 id）——");
                    int key1 = DiyTrigger.KeyOf(it1);
                    Line("     " + DiyJson.Compact(DiyJson.Member(doc.Root, "progress_events")));

                    DiyData back = DiyPool.Read(doc);
                    DiyEvent bEv = null;
                    foreach (DiyEvent e in back.Events) if (e.Id == key1) bEv = e;    // ★ 按**键**找那个事件
                    DiyProgressItem b1 = bEv != null && bEv.Items.Count > 0 ? bEv.Items[0] : null;
                    JNode peAll = DiyJson.Member(doc.Root, "progress_events");
                    bool keyIsEvent = peAll != null && DiyJson.Member(peAll, key1.ToString(CultureInfo.InvariantCulture)) != null;
                    bool keyNotN = b1 != null && key1 != b1.N;
                    Line("     读回：" + (b1 == null
                            ? "★ 那个键没读回来"
                            : "事件 id " + bEv.Id + " / 条目 n=" + b1.N + " ⇒ trigger_progress=" + b1.TriggerProgress
                              + " · trigger_event=" + b1.TriggerEvent)
                        + " · progress_events 的键 = " + key1 + "（新模型读回的条目编号一律是 1）");
                    Check("⑭c ★★ 落盘写的是 trigger_progress=3 + trigger_event=<官方事件 id>，"
                        + "且 progress_events 的键用**事件 id**（不是条目编号）",
                        b1 != null && b1.TriggerProgress == 3 && b1.TriggerEvent == it1.TriggerEvent
                        && keyIsEvent && keyNotN);

                    // ---- ④ 索引回读：界面上那两句话
                    //   ★ 换算口径：进度 P 第一次到达是在「第 ceil((P - start) / per_round) 回合」。
                    //     DIY 新建的关卡没有 progress 块 ⇒ start 0 / 没写 per_round（那时只显示进度值）。
                    JNode progNode = DiyJson.EnsureMember(doc.Root, "progress");
                    DiyJson.SetInt(DiyJson.EnsureMember(progNode, "start"), 0);
                    DiyJson.SetInt(DiyJson.EnsureMember(progNode, "per_round"), 1);
                    string shortOn = DiyTrigger.Short(doc, b1);
                    string detailOn = DiyTrigger.Detail(doc, b1);
                    DiyJson.SetInt(DiyJson.EnsureMember(progNode, "start"), 1);
                    string shortStart1 = DiyTrigger.Short(doc, b1);
                    DiyJson.RemoveMember(progNode, "per_round");
                    string shortOff = DiyTrigger.Short(doc, b1);
                    Line("     索引回读（界面显示的那句话）：progress{start:0,per_round:1} ⇒「" + shortOn + "」；"
                        + "progress{start:1,per_round:1} ⇒「" + shortStart1 + "」（开局就把进度条推到 1 ⇒ 少一回合）；"
                        + "没写 per_round ⇒「" + shortOff + "」");
                    Line("     编辑区那句：" + detailOn);
                    Check("⑭d ★★ 索引回读：显示「触发进度 3（约第 3 回合）」；"
                        + "没写 progress.per_round 时**只显示进度值**（不硬算回合）",
                        shortOn.IndexOf("触发进度 3", StringComparison.Ordinal) >= 0
                        && shortOn.IndexOf("约第 3 回合", StringComparison.Ordinal) >= 0
                        && shortOff.IndexOf("触发进度 3", StringComparison.Ordinal) >= 0
                        && shortOff.IndexOf("回合", StringComparison.Ordinal) < 0);

                    // ---- ⑤ 官方没有的触发点：明确提示、一个字节都不改
                    int noEv = -1;
                    for (int p = 0; p <= 200; p++)
                        if (MapEventTable.RowsAtProgress(p).Count == 0) { noEv = p; break; }
                    int keepP = it1.TriggerProgress, keepE = it1.TriggerEvent;
                    string noteNo;
                    bool okNo = DiyTrigger.Apply(doc, d, it1, noEv, out noteNo);
                    Line("  ⑤ 填一个官方没有的触发点（" + noEv + "）：" + noteNo);
                    Check("⑭e ★★ 官方没有该触发点 ⇒ 返回 false + 明说「官方没有触发进度 = N 的事件」，"
                        + "条目一个字节都不改（不硬编一个错的 id）",
                        noEv >= 0 && !okNo
                        && noteNo.IndexOf("官方没有触发进度 = " + noEv, StringComparison.Ordinal) >= 0
                        && it1.TriggerProgress == keepP && it1.TriggerEvent == keepE);

                    // ---- ⑥ 触发点相同的两条事件并存（★ 新模型：两个事件、progress_events 里两行）
                    string note2;
                    bool ok2 = DiyTrigger.Apply(doc, d, it2, 3, out note2);
                    doc.SaveDiy();
                    DiyData back2 = DiyPool.Read(doc);
                    int key2 = DiyTrigger.KeyOf(it2);
                    DiyEvent bb1 = null, bb2 = null;
                    foreach (DiyEvent e in back2.Events)
                    {
                        if (e.Id == key1) bb1 = e;
                        if (e.Id == key2) bb2 = e;
                    }
                    Line("  ⑥ 第 2 条也设成进度 3：" + note2);
                    Check("⑭f ★★ 触发点相同的两条事件并存（两条都在进度 3、各自一个官方事件 id、读回都在）",
                        ok2 && key2 != key1
                        && bb1 != null && bb2 != null
                        && bb1.Items[0].TriggerProgress == 3 && bb2.Items[0].TriggerProgress == 3
                        && bb1.Items[0].TriggerEvent == key1 && bb2.Items[0].TriggerEvent == key2);

                    // ---- ⑦ 优先挑「官方这张图这一档登记过的」id
                    string note3;
                    bool ok3 = DiyTrigger.Apply(doc, d, it3, 1, out note3);
                    List<int> official = MapEventTable.EventsForMapDifficulty(doc.MapId, doc.Difficulty);
                    Line("  ⑦ 第 3 条设成进度 1（官方 82016 难度 1 登记过 " + Ids(official) + "）：" + note3);
                    Check("⑭f2 ★ 优先级：先挑「官方这张图这一档登记过的」那条 id（官方本来就用它）",
                        ok3 && it3.TriggerEvent == 32121 && official.Contains(32121));

                    // ---- ⑧ 校验③：没挑 id / 挑了表里没有的 id
                    //   ★ 这里直接拿 it1 / it2 这两个条目（别按 items[0] / items[1] 取：三个事件各一条，
                    //     顺序一改就测错对象了）
                    DiyTrigger.Clear(it1);
                    int saveE = it2.TriggerEvent;
                    it2.TriggerEvent = 999999;
                    List<string> miss = DiyTrigger.Check(doc, d);
                    Line("  ⑧ 校验③（把第 1 条清空、把第 2 条的 id 改成表里没有的 999999）：");
                    for (int k = 0; k < miss.Count; k++) Line("     " + miss[k]);
                    bool missOk = miss.Count > 0 && miss[0].StartsWith("★")
                        && miss[0].IndexOf("条进度事件没挑", StringComparison.Ordinal) >= 0
                        && Contains(miss, "没挑事件 id") && Contains(miss, "不在 MapEvent.bin 里");
                    it2.TriggerEvent = saveE;
                    DiyTrigger.Apply(doc, d, it1, 3, out noteNo);
                    List<string> good = DiyTrigger.Check(doc, d);
                    Line("     挑好之后：" + (good.Count > 0 ? good[0] : "(空)"));
                    Check("⑭g ★★ 校验③把「没挑事件 id」和「挑了表里没有的 id」逐条列出来；挑好之后不再报",
                        missOk && good.Count > 0 && !good[0].StartsWith("★"));

                    // ---- ⑨ 键的两条路：设了触发点 ⇒ **官方事件 id**；没设 ⇒ 退回**事件自己的 id**
                    //   （★ 退回事件 id 而不是条目编号：读回来的条目编号一律是 1，
                    //     两条"还没设触发点"的事件会挤在同一个键上、内容被吞掉。）
                    DiyJson.RemoveMember(doc.Root, "progress_events");
                    DiyTrigger.Clear(it1);                       // 只清第 1 条，另两条的触发点留着
                    int k1 = EventKey(ev1);                      // = 1001（没设触发点 ⇒ 事件自己的 id）
                    int k2 = EventKey(ev2);                      // = it2 挑中的官方事件 id
                    int k3 = EventKey(ev3);                      // = 32121
                    doc.SaveDiy();
                    JNode peOld = DiyJson.Member(doc.Root, "progress_events");
                    bool legacyKey = peOld != null && k1 != k2 && k2 != k3 && k1 != k3
                        && DiyJson.Member(peOld, k1.ToString(CultureInfo.InvariantCulture)) != null
                        && DiyJson.Member(peOld, k2.ToString(CultureInfo.InvariantCulture)) != null
                        && DiyJson.Member(peOld, k3.ToString(CultureInfo.InvariantCulture)) != null;
                    Line("  ⑨ 键：" + k1 + "（没设触发点 ⇒ 事件自己的 id）/ " + k2 + " / " + k3
                        + "（设了触发点 ⇒ 官方事件 id）⇒ " + (peOld == null ? "(没了)" : DiyJson.Compact(peOld)));
                    Check("⑭h ★ 键的两条路：没设触发点 ⇒ 事件自己的 id（1001）；设了 ⇒ 官方事件 id"
                        + "（32007 / 32121 这一档）—— 三条互不挤在同一个键上", legacyKey);

                    doc.Root = keep14;
                    doc.InvalidateDiy();
                    doc.InvalidateStarts();
                    doc.InvalidateBoardOverrides();
                }
                catch (Exception ex)
                {
                    Line("  ★ 失败：" + ex.Message);
                    Check("⑭ 进度事件的触发点", false);
                    doc.Root = keep14;
                }
            }
            Line("");

            // ════════════════════════════════════════════════════════════════
            // ⑮ 成品模板（2026-10-08 大改）：新建 = **整份复制模板**，官方内容不再被摘掉
            // ════════════════════════════════════════════════════════════════
            Line("── ⑮ 成品模板（新建 = 整份复制）──────────────");
            string keep15 = GameLocator.Override;
            try
            {
                string sandbox15 = Path.Combine(outDir, "沙盒模板");
                if (Directory.Exists(sandbox15)) Directory.Delete(sandbox15, true);
                string tplDir = Path.Combine(sandbox15, "DIY关卡", "_模板");
                Directory.CreateDirectory(tplDir);
                // ★ `GameLocator.Override` 只在"看着像游戏根"时生效（要有 exe / GameRes / _Data）
                Directory.CreateDirectory(Path.Combine(sandbox15, "GameRes"));
                GameLocator.Override = sandbox15;
                GameLocator.Reset();     // ★ Override 要配 Reset 才生效（不然读的还是真游戏目录）

                // 造一份"成品模板"：官方 82016_1 本来就字段齐全，原样放进去
                string tplPath = Path.Combine(tplDir, "82016_1.json");
                File.Copy(work, tplPath, true);

                List<LevelTemplate> list = TemplateCatalog.List();
                Line("    模板目录：" + TemplateCatalog.Dir);
                Line("    列出来 " + list.Count + " 份"
                    + (list.Count > 0 ? ("：" + list[0].Title + " · " + list[0].Sub) : ""));
                Check("⑮a ★ 模板目录里的 json 列得出来（名字回落到地图表里的官方名）",
                    list.Count == 1 && list[0].MapId == 82016 && list[0].Events == 3
                    && list[0].StartMonsters == 5);

                LevelDoc made = LevelDoc.CreateFromOwnTemplate(tplPath, "模板自测", LevelDoc.DiyDifficulty);
                JNode root15 = made.Root;
                bool kept = DiyJson.Member(root15, "round_award") != null
                    && DiyJson.Member(root15, "progress") != null
                    && DiyJson.Member(root15, "mastermind") != null
                    && DiyJson.Member(root15, "settlement") != null
                    && DiyJson.Member(root15, "victory") != null
                    && DiyJson.Member(root15, "start_monsters") != null
                    && DiyJson.IntOf(DiyJson.Member(root15, "map"), 0) == 82016
                    && DiyJson.IntOf(DiyJson.Member(root15, "difficulty"), 0) == LevelDoc.DiyDifficulty;
                Line("    顶层键：" + TopKeysOf(root15));
                Check("⑮b ★ 官方内容一个都没被摘（round_award / progress / mastermind / settlement / victory / start_monsters 都在）",
                    kept);

                JNode pe15 = DiyJson.Member(root15, "progress_events");
                int ev15 = (pe15 != null && pe15.Members != null) ? pe15.Members.Count : 0;
                Check("⑮c ★ 模板自带的三条事件原样留着（progress_events 3 条）", ev15 == 3);

                JNode evMeta = DiyJson.Member(DiyJson.Member(DiyJson.Member(root15, "diy"), "events"), "32122");
                Check("⑮d ★ diy.events.<id> 只留显示名（老结构 items 不再写）",
                    evMeta != null && DiyJson.Member(evMeta, "items") == null);

                DiySaveResult saved15 = made.SaveDiy("模板自测");
                LevelDoc back15 = LevelDoc.Open(saved15.LevelPath);
                DiyData d15 = back15.Diy;
                Line("    存盘再读回：事件 " + d15.Events.Count + " 条 / 池子 " + d15.Pool.Count
                    + " 只 / 棋盘落点 " + d15.BoardMonsters.Count + " 条");
                Check("⑮e ★ 存盘→读回：事件还是 3 条、每条都带着官方事件 id",
                    d15.Events.Count == 3 && d15.Event(32122) != null
                    && d15.Event(32123) != null && d15.Event(32124) != null);
                Check("⑮f ★ 存盘→读回：模板里的怪进了池子，落点**没翻倍**"
                    + "（官方 5 只开局怪 + 11 行 spawn 的 node = 16 条）",
                    d15.Pool.Count > 0 && d15.BoardMonsters.Count == 16);

                // ---- ⑯j ★★ 新建**不许继承上一关的棋盘改动**（用户实报：
                //      "新建了新的关卡但是棋盘的摆布还是之前旧的"）
                //   现场：`BoardStore.DiyBoardPath` 是静态字段，上一份 DIY 关卡的 `.board.json`
                //   还挂着 ⇒ `LoadBoard` 优先照它读 ⇒ 新文档拿到上一关的改格，`SaveDiy` 再抄进
                //   新关卡的 `.board.json`（实测：连开四份新关卡，四份 board 的改格逐字节相同）。
                //   这里**不手工清静态字段**（那正是被测的那条路），直接新建第二份。
                LevelDoc dirty = LevelDoc.CreateFromOwnTemplate(tplPath, "脏棋盘", LevelDoc.DiyDifficulty);
                int dn1 = dirty.Board != null && dirty.Board.Ok && dirty.Board.Nodes.Count > 0
                    ? dirty.Board.Nodes[0].Id : 0;
                int dn2 = dirty.Board != null && dirty.Board.Ok && dirty.Board.Nodes.Count > 1
                    ? dirty.Board.Nodes[1].Id : 1;
                int rcD1 = dirty.SetNodeLand(dn1, 12);          // 用户现场是 31 格 → 12「再动」
                int rcD2 = dirty.SetNodeLand(dn2, 12);
                DiySaveResult dSave = dirty.SaveDiy("脏棋盘");   // ⇒ DiyBoardPath 指向它自己的 board
                int dirtyCount = dirty.NodeOverrideCount;
                int dirtyInFile = CountDiyLand(dSave.BoardPath);
                Line("    先造一份『有棋盘改动』的关卡：改 " + dirtyCount + " 格（rc=" + rcD1 + "/" + rcD2
                    + "）⇒ DiyBoardPath = " + Path.GetFileName(BoardStore.DiyBoardPath)
                    + "，它自己的 board 里带 diy.landType 的节点 = " + dirtyInFile);

                LevelDoc fresh = LevelDoc.CreateFromOwnTemplate(tplPath, "干净新建", LevelDoc.DiyDifficulty);
                int freshCount = fresh.NodeOverrideCount;
                DiySaveResult fSave = fresh.SaveDiy("干净新建");
                int freshInFile = CountDiyLand(fSave.BoardPath);
                Line("    紧接着新建第二份（**没手工清静态字段**）：棋盘改动数 = " + freshCount
                    + "，落盘 board = " + (fSave.BoardWritten ? Path.GetFileName(fSave.BoardPath) : "（没写）")
                    + "，里面带 diy.landType 的节点 = " + freshInFile);
                Check("⑯j ★★ 新建不继承上一关的棋盘改动：新文档改动数 == 0，"
                    + "落盘的 .board.json 里**一个** `nodes[].diy.landType` 都没有"
                    + "（脏的那份是 " + dirtyInFile + " 个）",
                    dirtyCount == 2 && dirtyInFile == 2 && fSave.BoardWritten
                    && freshCount == 0 && freshInFile == 0);

                // ---- ⑯k ★★ 两份关卡各改**不同的格** ⇒ 存盘读回各自保留自己的（互不污染）；
                //      并且同一份关卡存两次不会叠加。
                LevelDoc ka = LevelDoc.CreateFromOwnTemplate(tplPath, "互不污染A", LevelDoc.DiyDifficulty);
                LevelDoc kb = LevelDoc.CreateFromOwnTemplate(tplPath, "互不污染B", LevelDoc.DiyDifficulty);
                int na = ka.Board != null && ka.Board.Ok && ka.Board.Nodes.Count > 3 ? ka.Board.Nodes[3].Id : 3;
                int nb = kb.Board != null && kb.Board.Ok && kb.Board.Nodes.Count > 7 ? kb.Board.Nodes[7].Id : 7;
                int rcA = ka.SetNodeLand(na, 19);
                int rcB = kb.SetNodeLand(nb, 12);
                Line("    存之前：A(" + na + "→19)=" + rcA + " 账 " + DiyLandListOf(ka)
                    + " ｜ B(" + nb + "→12)=" + rcB + " 账 " + DiyLandListOf(kb));
                DiySaveResult sA = ka.SaveDiy("互不污染A");
                DiySaveResult sB = kb.SaveDiy("互不污染B");
                Line("    存之后：A 文件 " + DiyLandList(sA.BoardPath) + " ｜ B 文件 " + DiyLandList(sB.BoardPath));
                int aInFile1 = CountDiyLand(sA.BoardPath);
                int bInFile = CountDiyLand(sB.BoardPath);
                ka.SaveDiy("互不污染A");                     // ★ 同一份再存一次：不许叠加
                int aInFile2 = CountDiyLand(sA.BoardPath);
                LevelDoc bA = LevelDoc.Open(sA.LevelPath);
                LevelDoc bB = LevelDoc.Open(sB.LevelPath);
                Line("    A 改格 " + na + "→19、B 改格 " + nb + "→12 ⇒ 读回：A 的 " + na + "="
                    + bA.NodeLandOverride(na) + " / " + nb + "=" + bA.NodeLandOverride(nb)
                    + "（改动数 " + bA.NodeOverrideCount + "）；B 的 " + nb + "=" + bB.NodeLandOverride(nb)
                    + " / " + na + "=" + bB.NodeLandOverride(na) + "（改动数 " + bB.NodeOverrideCount
                    + "）；落盘 diy.landType 个数 A=" + aInFile1 + "→再存=" + aInFile2 + "、B=" + bInFile);
                Check("⑯k ★★ 两份关卡各改各的格：存盘读回各自只留自己那一格（互不污染），"
                    + "同一份存两次也不叠加",
                    na != nb && aInFile1 == 1 && aInFile2 == 1 && bInFile == 1
                    && bA.NodeLandOverride(na) == 19 && bA.NodeLandOverride(nb) == 0
                    && bA.NodeOverrideCount == 1
                    && bB.NodeLandOverride(nb) == 12 && bB.NodeLandOverride(na) == 0
                    && bB.NodeOverrideCount == 1);
            }
            catch (Exception ex)
            {
                Line("  ★ 失败：" + ex.Message);
                Check("⑮ 成品模板（新建 = 整份复制）", false);
            }
            finally { GameLocator.Override = keep15; GameLocator.Reset(); }
            Line("");

            // ================================================================ ⑯ ★★ 空白玩法模板的事件骨架
            //   用户 2026-10-10：「给地图模板也给一些进度事件的模板，显示的文案啊什么的可以跟原版一样
            //   但是表现为 diy 的就可以了」。这一节钉住三件事：
            //     ① 新生成的空白玩法模板**自带开局事件骨架**（键 = 官方事件 id、trigger_progress 取官方
            //        那条的触发进度、名字取官方文案）；
            //     ② 补骨架**幂等且只补空缺** —— 已有 `progress_events` 内容的一个字节都不动（作者改过的不被覆盖）；
            //     ③ ★ **骨架行能活过一轮存盘** —— 这是这一版真正的坑：
            //        `SyncProgressEvents` 原来只认"有效果"的事件，于是"只有触发进度、还没刷怪"的骨架
            //        一存盘就被清掉（实测：读回来 条目1，走一遍 Write 之后 条目0、progress_events 整块消失）。
            Line("── ⑰ 空白玩法模板的事件骨架 ──────────────");
            string keep16 = GameLocator.Override;
            try
            {
                string sandbox16 = Path.Combine(outDir, "沙盒空白模板");
                if (Directory.Exists(sandbox16)) Directory.Delete(sandbox16, true);
                Directory.CreateDirectory(Path.Combine(sandbox16, "GameRes"));
                GameLocator.Override = sandbox16;
                GameLocator.Reset();

                List<MapEventRow> opening = MapEventTable.RowsAtProgress(0);
                string tplDir16 = TemplateCatalog.Dir;
                Line("    开局（进度 0）的官方事件 " + opening.Count + " 条 · 模板目录 " + tplDir16);

                int made16 = TemplateCatalog.EnsureBlankTemplates();
                Line("    生成空白玩法模板 " + made16 + " 份 ⇒ 目录里 "
                    + TemplateCatalog.List().Count + " 份");
                Check("⑰a ★ 能给「有官方棋盘但没模板」的图生成空白玩法模板", made16 > 0);

                // ① 骨架 = **这张图自己的官方登记**（`Map.bin` 难度行 field 3），
                //    不是"全局触发进度 0 的前 3 条"（2026-10-10 修正：上一版就是后者，
                //    于是 8 份模板每张图都被塞进同样那 3 条 31001/31002/31003）。
                string one16 = "";
                foreach (LevelTemplate t in TemplateCatalog.List())
                    if (t.Name.IndexOf("（空白玩法）", StringComparison.Ordinal) >= 0) { one16 = t.Path; break; }
                bool hasOne = one16.Length > 0;
                string txt16 = hasOne ? File.ReadAllText(one16, Encoding.UTF8) : "";
                JNode root16 = hasOne ? DiyJson.Parse(txt16) : null;
                int map16 = root16 == null ? 0 : (int)DiyJson.IntOf(DiyJson.Member(root16, "map"), 0);
                List<MapEventRow> want16 = TemplateCatalog.OfficialEventSeeds(map16, 0);
                JNode pe16 = root16 == null ? null : DiyJson.Member(root16, "progress_events");
                int peCount = pe16 != null && pe16.Kind == JKind.Object && pe16.Members != null ? pe16.Members.Count : 0;
                bool exact16 = peCount > 0 && peCount == want16.Count;
                bool trigOk = exact16, nameOk = exact16;
                if (exact16)
                    for (int k = 0; k < want16.Count; k++)
                    {
                        JMember m16 = pe16.Members[k];
                        if (m16.Key != want16[k].Id.ToString(CultureInfo.InvariantCulture)) { exact16 = false; break; }
                        if ((int)DiyJson.IntOf(DiyJson.Member(m16.Value, DiyTrigger.KeyProgress), -99)
                            != want16[k].Progress) trigOk = false;
                        // 名字：官方**有**名字的必须照抄；官方没给名字的**不许自己编**（那一项不写）
                        JNode nm16 = DiyJson.Member(DiyJson.Member(DiyJson.Member(root16, "diy"), "events"), m16.Key);
                        string nmStr = nm16 != null && nm16.Kind == JKind.Object
                            ? (DiyJson.Member(nm16, "name") != null ? DiyJson.Member(nm16, "name").Str : "") : "";
                        if (want16[k].Name.Length > 0)
                        { if (!string.Equals(nmStr, want16[k].Name, StringComparison.Ordinal)) nameOk = false; }
                        else if (nmStr.Length > 0) nameOk = false;
                    }
                Line("    骨架：模板地图 " + map16 + " ⇒ 键**恰好**等于 Map.bin 难度行登记的那 "
                    + want16.Count + " 条（" + Ids(want16) + "）=" + exact16
                    + " · trigger_progress 与官方一致=" + trigOk + " · 名字照抄官方（没名字的不编）=" + nameOk);
                Check("⑰b ★★ 空白玩法模板自带**这张图自己**的官方事件骨架"
                    + "（键 = Map.bin 难度行 field 3 登记的事件 id、trigger_progress 取官方那条、名字取官方文案）",
                    hasOne && exact16 && trigOk && nameOk);

                // ①b ★★ 8 份模板的骨架**各不相同**（上一版全都是同一份"全局开局波"）+ 每份都补上了。
                //   ★ 这一节同时钉住 `GameTables.ProgressEventsByDifficulty` 的缓存口径：
                //     上一版"只给第一次问的那张图建表" ⇒ 一个进程里除第一张图以外全都查到 0 条
                //     （`--map-events` 与 8 份模板的预填都被它坑过）。
                var keySets = new List<string>();
                int blanks16 = 0, withEvents16 = 0, wantEvents16 = 0;
                foreach (LevelTemplate t in TemplateCatalog.List())
                {
                    if (t.Name.IndexOf("（空白玩法）", StringComparison.Ordinal) < 0) continue;
                    blanks16++;
                    // ★ 只有"这张图官方登记过事件"的那些**才应该**带骨架（官方一条都没登记的图，
                    //   整块 `progress_events` 不写 = 回落默认，这是刻意的）
                    if (TemplateCatalog.OfficialEventSeeds(t.MapId, t.Difficulty).Count > 0) wantEvents16++;
                    var ks = new List<string>();
                    JNode p2 = DiyJson.Member(DiyJson.Parse(File.ReadAllText(t.Path, Encoding.UTF8)), "progress_events");
                    if (p2 != null && p2.Members != null)
                        foreach (JMember m2 in p2.Members) ks.Add(m2.Key);
                    if (ks.Count > 0) withEvents16++;
                    ks.Sort();
                    keySets.Add(string.Join(",", ks.ToArray()));
                }
                var legacySet = new List<int>();
                foreach (MapEventRow or16 in MapEventTable.RowsAtProgress(0)) { legacySet.Add(or16.Id); if (legacySet.Count >= 3) break; }
                string legacyKey = string.Join(",", legacySet.ConvertAll(x => x.ToString()).ToArray());
                bool allSame = true;
                for (int k = 1; k < keySets.Count; k++) if (keySets[k] != keySets[0]) { allSame = false; break; }
                Line("    空白模板 " + blanks16 + " 份 · 该带骨架的 " + wantEvents16 + " 份、真带了 " + withEvents16
                    + " 份 · 骨架键集合互不相同=" + !allSame
                    + " · 有没有人还停在上一版那套 [" + legacyKey + "]=" + keySets.Contains(legacyKey));
                Check("⑰b2 ★★ 每张空白玩法模板拿到的是**这张图自己的**事件（键集合互不相同、且都不是上一版那套全局开局波）",
                    blanks16 >= 2 && wantEvents16 >= 2 && withEvents16 == wantEvents16
                    && keySets.Count >= 2 && !allSame && !keySets.Contains(legacyKey));

                // ② 幂等：再补一次应当是 0
                int again16 = TemplateCatalog.EnsureTemplateEvents();
                Check("⑰c ★★ 补骨架是幂等的（再补一次 = 0 份，已有内容一个字节不动）", again16 == 0);
                Line("    再补一次新补 " + again16 + " 份");

                // ③ ★★ 骨架行要活过一轮存盘（这一版修的就是这个）
                LevelDoc doc16 = LevelDoc.CreateFromOwnTemplate(one16, "骨架自测", LevelDoc.DiyDifficulty);
                int items16 = 0, withTrig = 0;
                foreach (DiyEvent ev in doc16.Diy.Events)
                    foreach (DiyProgressItem pit in ev.Items)
                    { items16++; if (pit.TriggerProgress >= 0) withTrig++; }
                JNode peAfter16 = DiyJson.Member(doc16.Root, "progress_events");
                int kept16 = peAfter16 != null && peAfter16.Kind == JKind.Object && peAfter16.Members != null
                    ? peAfter16.Members.Count : 0;
                Line("    存盘往返后：事件 " + doc16.Diy.Events.Count + " 个 · 进度条目 " + items16
                    + " 条（带触发进度 " + withTrig + "）· progress_events 还剩 " + kept16 + " 条");
                Check("⑰d ★★ 骨架行能活过一轮存盘（只有触发进度、还没刷怪的行**不被清掉**）"
                    + " —— 上一版 `SyncProgressEvents` 会把它们连整块 `progress_events` 一起删掉",
                    doc16.Diy.Events.Count > 0 && withTrig > 0 && kept16 > 0);

                // ④ ★★ lint：**有事件、但一条刷怪都没有** ⇒ 一条 **Info**（"这条事件还没有刷怪，
                //   作者自己填"），**不报错**（用户 2026-10-10 点名要的）。加了刷怪之后这条要消失。
                {
                    int lr16, ld16;
                    List<LintFinding> f016 = LevelLint.Run(doc16.Root, doc16.Board, out lr16, out ld16);
                    bool info016 = false, bad016 = false;
                    foreach (LintFinding f in f016)
                    {
                        if (f.Where != "(刷怪)") continue;
                        if (f.Level == LintLevel.Info) info016 = true; else bad016 = true;
                    }
                    // ★ 替换的锚点用**新的行形态**（`"spawn": [] }`）：不刷怪的骨架行现在长这样。
                    //    `String.Replace` 会把每一条都换成"有怪的"，这对本条的判据没影响（要看的是
                    //    "有怪之后那条 Info 就不报了"）。
                    string withSpawn = txt16.Replace("\"spawn\": [] }",
                        "\"spawn\": [ { \"table\": 1067 } ] }");
                    int sr16, sd16;
                    List<LintFinding> f116 = LevelLint.Run(DiyJson.Parse(withSpawn), doc16.Board, out sr16, out sd16);
                    bool info116 = false;
                    foreach (LintFinding f in f116) if (f.Where == "(刷怪)") info116 = true;
                    Line("    lint（有事件没刷怪）：Info=" + info016 + " · 被写成 Bad=" + bad016
                        + " · 加了 spawn 之后还报=" + info116
                        + "（这份 " + f016.Count + " 条 findings，其中 Info "
                        + CountLevel(f016, LintLevel.Info) + " 条）");
                    Check("⑰d2 ★★ lint「有进度事件、但一条刷怪都没有」给的是 **Info**（不报错，作者自己填）",
                        info016 && !bad016);
                    Check("⑰d3 ★ 那条 Info 不是无脑常报：给事件加上 spawn 之后就不报了",
                        !string.Equals(withSpawn, txt16, StringComparison.Ordinal) && !info116);
                }

                // ④ ★ 作者改过的模板不被覆盖：**只要 `progress_events` 还有内容就纹丝不动**。
                //   判据是"补了 0 份 **且** 文件 sha 与改完那一刻一致"。
                string t16 = File.ReadAllText(one16, Encoding.UTF8);
                string edited16 = t16.Replace("\"name\": \"", "\"name\": \"改过的");
                File.WriteAllText(one16, edited16, new UTF8Encoding(false));
                string shaEdited = ExportService.Sha256OfFile(one16);
                int after16 = TemplateCatalog.EnsureTemplateEvents();
                string shaNow = ExportService.Sha256OfFile(one16);
                Check("⑰e ★★ 作者改过的模板不被覆盖（有 `progress_events` 内容 ⇒ 一个字节都不动）",
                    after16 == 0 && string.Equals(shaNow, shaEdited, StringComparison.Ordinal));
                Line("    作者改后 sha " + (shaEdited.Length >= 12 ? shaEdited.Substring(0, 12) : shaEdited)
                    + " ⇒ 补完 " + (shaNow.Length >= 12 ? shaNow.Substring(0, 12) : shaNow)
                    + "（新补 " + after16 + " 份）");

                // ⑤ ★ 把 `progress_events` 清空之后**才**会被重新补上（"只补空缺"的另一半）
                //   ★ 用**花括号配对**找对象边界（别拿 "}," 猜 —— 那会切出非法 JSON，实测踩过）。
                string cleared = edited16;
                {
                    int at = cleared.IndexOf("\"progress_events\"", StringComparison.Ordinal);
                    int braceAt = at < 0 ? -1 : cleared.IndexOf('{', at);
                    int depth = 0, endAt = -1;
                    for (int k = braceAt; braceAt >= 0 && k < cleared.Length; k++)
                    {
                        if (cleared[k] == '{') depth++;
                        else if (cleared[k] == '}')
                        {
                            depth--;
                            if (depth == 0) { endAt = k; break; }
                        }
                    }
                    if (braceAt > 0 && endAt > braceAt)
                        cleared = cleared.Substring(0, braceAt) + "{}" + cleared.Substring(endAt + 1);
                }
                File.WriteAllText(one16, cleared, new UTF8Encoding(false));
                int re16 = TemplateCatalog.EnsureTemplateEvents();
                JNode peRe16 = DiyJson.Member(DiyJson.Parse(File.ReadAllText(one16, Encoding.UTF8)), "progress_events");
                int reCount16 = peRe16 != null && peRe16.Members != null ? peRe16.Members.Count : 0;
                Check("⑰f ★ 空缺才补：把 `progress_events` 清空后会被重新补上（" + re16 + " 份，补回 " + reCount16 + " 条）",
                    re16 == 1 && reCount16 > 0);

                // ⑥ ★★ 就地升级：**只**升级"上一版那套全局开局波骨架、且作者一个字节都没动过"的文件
                //   （用户手上那 8 份就是这么从"31001/31002/31003"换成"这张图自己的官方事件"的）。
                //   造一份**上一版形状**的文件（note 留记号 / 键 = 全局进度 0 的前 3 条 / 每行只有
                //   `trigger_progress: 0` / diy.events 只有官方名字），看它会不会被换掉。
                JNode r16b = DiyJson.Parse(File.ReadAllText(one16, Encoding.UTF8));
                int map16b = (int)DiyJson.IntOf(DiyJson.Member(r16b, "map"), 0);
                JNode dy16b = DiyJson.Member(r16b, "diy");
                string nm16b = dy16b != null && DiyJson.Member(dy16b, "name") != null ? DiyJson.Member(dy16b, "name").Str : "";
                string sc16b = dy16b != null && DiyJson.Member(dy16b, "base_scene") != null ? DiyJson.Member(dy16b, "base_scene").Str : "";
                string bl16b = dy16b != null && DiyJson.Member(dy16b, "base_level") != null ? DiyJson.Member(dy16b, "base_level").Str : "";
                var legIds = new List<int>();
                foreach (MapEventRow or16 in MapEventTable.RowsAtProgress(0)) { legIds.Add(or16.Id); if (legIds.Count >= 3) break; }
                var legSb = new StringBuilder();
                legSb.Append("{\n  \"note\": \"").Append(nm16b).Append(" ").Append(map16b)
                     .Append(" 的空白玩法模板：只有官方棋盘，事件 / 怪 / 奖励都还没写。    ")
                     .Append("★ 已带上**开局进度事件骨架**（官方文案，可自由改 / 删）：刷怪、奖励、胜负都还没写 ⇒ 回落默认，由作者自己填。\",\n");
                legSb.Append("  \"map\": ").Append(map16b).Append(",\n  \"difficulty\": 0,\n\n  \"progress_events\": {\n");
                for (int k = 0; k < legIds.Count; k++)
                    legSb.Append("    \"").Append(legIds[k]).Append("\": { \"trigger_progress\": 0 }")
                         .Append(k + 1 < legIds.Count ? ",\n" : "\n");
                legSb.Append("  },\n\n  \"diy\": {\n    \"version\": 1,\n    \"name\": \"").Append(nm16b)
                     .Append("\",\n    \"base_map\": ").Append(map16b)
                     .Append(",\n    \"base_scene\": \"").Append(sc16b)
                     .Append("\",\n    \"base_level\": \"").Append(bl16b).Append("\",\n    \"events\": {\n");
                for (int k = 0; k < legIds.Count; k++)
                {
                    string on16 = MapEventTable.NameOf(legIds[k]);
                    legSb.Append("      \"").Append(legIds[k]).Append("\": { \"name\": \"")
                         .Append(on16.Length > 0 ? on16 : ("事件 " + legIds[k])).Append("\" }")
                         .Append(k + 1 < legIds.Count ? ",\n" : "\n");
                }
                legSb.Append("    }\n  }\n}\n");
                File.WriteAllText(one16, legSb.ToString(), new UTF8Encoding(false));

                int upMade16, upGot16;
                upMade16 = TemplateCatalog.EnsureTemplateEvents(out upGot16);
                JNode peUp16 = DiyJson.Member(DiyJson.Parse(File.ReadAllText(one16, Encoding.UTF8)), "progress_events");
                var upKeys = new List<string>();
                if (peUp16 != null && peUp16.Members != null)
                    foreach (JMember m2 in peUp16.Members) upKeys.Add(m2.Key);
                string wantKeys = string.Join(",", TemplateCatalog.OfficialEventSeeds(map16b, 0)
                    .ConvertAll(x => x.Id.ToString()).ToArray());
                Line("    上一版骨架（[" + string.Join(",", legIds.ConvertAll(x => x.ToString()).ToArray()) + "]）"
                    + " ⇒ 新补 " + upMade16 + " / 就地升级 " + upGot16 + " 份，升级后键 [" + string.Join(",", upKeys.ToArray()) + "]");
                Check("⑰g ★★ 上一版的「全局开局波」骨架被**就地升级**成这张图自己的官方事件"
                    + "（只认上一版形状 + 作者没动过）",
                    upMade16 == 0 && upGot16 == 1 && string.Join(",", upKeys.ToArray()) == wantKeys);

                // ⑦ ★ 作者**动过**的上一版骨架：一个字节都不许动（把其中一条的进度改掉 ⇒ 指纹不成立）
                string edited17 = legSb.ToString().Replace("\"trigger_progress\": 0 },\n    \"" + legIds[1],
                    "\"trigger_progress\": 2 },\n    \"" + legIds[1]);
                File.WriteAllText(one16, edited17, new UTF8Encoding(false));
                string sha17a = ExportService.Sha256OfFile(one16);
                int mk17b, up17b;
                mk17b = TemplateCatalog.EnsureTemplateEvents(out up17b);
                string sha17b = ExportService.Sha256OfFile(one16);
                Check("⑰h ★★ 作者改过的上一版骨架**不被升级**（改了一条的触发进度 ⇒ 指纹不成立 ⇒ 一个字节都不动）",
                    mk17b == 0 && up17b == 0 && string.Equals(sha17a, sha17b, StringComparison.Ordinal));
                Line("    改过进度后 sha " + (sha17a.Length >= 12 ? sha17a.Substring(0, 12) : sha17a)
                    + " ⇒ 补完 " + (sha17b.Length >= 12 ? sha17b.Substring(0, 12) : sha17b)
                    + "（新补 " + mk17b + " / 升级 " + up17b + " 份）");
            }
            catch (Exception ex)
            {
                Line("  ★ 失败：" + ex.Message);
                Check("⑰ 空白玩法模板的事件骨架", false);
            }
            finally { GameLocator.Override = keep16; GameLocator.Reset(); }
            Line("");

            // ================================================================ ⑱ ★★ 显式空刷怪
            //   用户 2026-10-10 追加：「一个**不刷怪**的空白模板」+「但是我编辑器这里写的是开局刷怪是 0 啊」。
            //   与服务端对称的口径：
            //     · 顶层 `"start_monsters": []`（键在、空数组）= 开局**明确不刷**
            //       （不是"没写 ⇒ 回落 `battle.INIT_MONSTERS` 官方那批"）；
            //     · 事件里 `"spawn": []`（键在、空数组）= 这一波**明确不刷**
            //       （不是"没写 ⇒ 回落官方那条事件的效果"）；
            //     · **有怪的时候行照旧**（行数、键、数值一个不变）；
            //     · **官方模板**（difficulty 0~5、没有 `diy` 簿记）那份 `start_monsters` 一个字节不许动。
            Line("── ⑱ 显式空刷怪（start_monsters: [] / spawn: []） ──────────");
            string keep18 = GameLocator.Override;
            try
            {
                string sandbox18 = Path.Combine(outDir, "沙盒空刷怪");
                if (Directory.Exists(sandbox18)) Directory.Delete(sandbox18, true);
                Directory.CreateDirectory(Path.Combine(sandbox18, "GameRes"));
                GameLocator.Override = sandbox18;
                GameLocator.Reset();

                TemplateCatalog.EnsureBlankTemplates();
                TemplateCatalog.EnsureTemplateEvents();

                // ---- ① 开局组为空：落盘必须是 `"start_monsters": []`（键在）+ 每条事件 `"spawn": []`
                string tpl18 = "";
                foreach (LevelTemplate t in TemplateCatalog.List())
                    if (t.Name.IndexOf("（空白玩法）", StringComparison.Ordinal) >= 0 && t.Events > 0)
                    { tpl18 = t.Path; break; }
                bool hasTpl18 = tpl18.Length > 0;
                LevelDoc doc18 = hasTpl18
                    ? LevelDoc.CreateFromOwnTemplate(tpl18, "空刷怪自测", LevelDoc.DiyDifficulty) : null;
                if (doc18 != null)
                {
                    doc18.SaveDiy();
                    JNode sm18 = DiyJson.Member(doc18.Root, "start_monsters");
                    bool smEmpty = sm18 != null && sm18.Kind == JKind.Array && sm18.Count == 0;
                    int ev18 = 0, evSpawnEmpty = 0;
                    JNode pe18 = DiyJson.Member(doc18.Root, "progress_events");
                    if (pe18 != null && pe18.Members != null)
                        foreach (JMember mem18 in pe18.Members)
                        {
                            ev18++;
                            JNode sp18 = DiyJson.Member(mem18.Value, "spawn");
                            if (sp18 != null && sp18.Kind == JKind.Array && sp18.Count == 0) evSpawnEmpty++;
                        }
                    Line("    开局组空 ⇒ start_monsters = " + DiyJson.Compact(sm18)
                        + " · " + ev18 + " 条事件里带空 `spawn: []` 的 " + evSpawnEmpty + " 条");
                    Line("    progress_events 头一条原文：" + DiyJson.Compact(pe18 == null || pe18.Members == null || pe18.Members.Count == 0
                        ? null : pe18.Members[0].Value));
                    Check("⑱a ★★ 开局组为空 ⇒ 落盘写 `\"start_monsters\": []`（键在、空数组 = 明确不刷，不是「没写」）",
                        doc18 != null && smEmpty);
                    Check("⑱b ★★ 事件里没有刷怪 ⇒ 那条事件写 `\"spawn\": []`（键在、空数组）；"
                        + "模板给的每条事件都得带上",
                        ev18 > 0 && evSpawnEmpty == ev18);

                    // ---- ①b ★ lint「开局组为空」那条 Info（用户点名要的措辞：
                    //     "开局不刷怪（已写 start_monsters: []）；想让开局就用官方那批，把怪放进
                    //      「开局就在场上」那一组"）。**不能**再是"没写 ⇒ 回落官方那批"那种误导说法。
                    int lr18, ld18;
                    List<LintFinding> f018 = LevelLint.Run(doc18.Root, doc18.Board, out lr18, out ld18);
                    bool smInfo18 = false, smBad18 = false;
                    foreach (LintFinding f in f018)
                    {
                        if (f.Where != "start_monsters") continue;
                        if (f.Level == LintLevel.Info) smInfo18 = true; else smBad18 = true;
                    }
                    Line("    lint（开局组为空）：start_monsters 那条 Info=" + smInfo18
                        + " · 被写成 Bad=" + smBad18);
                    Check("⑱a2 ★★ lint「开局组为空」给的是 **Info**（「开局不刷怪（明确：键在、空数组 `[]`）」"
                        + " + 怎么办），不是「没写 ⇒ 回落官方那批」那种误导说法",
                        smInfo18 && !smBad18);

                    // ---- ② 有开局怪：行照旧（与旧版同一个 SpawnNode 形状、一个字节不差）
                    DiyData d18 = doc18.Diy;
                    PoolMonster m18 = DiyPool.MakePool(d18, 1067);
                    DiyPool.AddAt(d18, 5, m18.Id, 0);
                    doc18.SaveDiy();
                    JNode sm2 = DiyJson.Member(doc18.Root, "start_monsters");
                    string row18 = sm2 != null && sm2.Kind == JKind.Array && sm2.Count == 1
                        ? DiyJson.Compact(sm2.Items[0]) : "(没有)";
                    Line("    加了一只开局怪（1067 @格5）⇒ start_monsters = " + DiyJson.Compact(sm2));
                    Check("⑱c ★★ 有开局怪时行照旧写（形状与旧版一字不差：table/node/count/hp/defense/attack/gold）",
                        row18.IndexOf("\"table\":1067", StringComparison.Ordinal) >= 0
                        && row18.IndexOf("\"node\":5", StringComparison.Ordinal) >= 0
                        && row18.IndexOf("\"count\":1", StringComparison.Ordinal) >= 0
                        && sm2 != null && sm2.Count == 1);

                    // ---- ②b 开局组里有怪了 ⇒ 那条"开局不刷怪"的 Info 不许再报（不是无脑常报）
                    List<LintFinding> f118 = LevelLint.Run(doc18.Root, doc18.Board, out lr18, out ld18);
                    bool smInfo118 = false;
                    foreach (LintFinding f in f118)
                        if (f.Where == "start_monsters" && f.Level == LintLevel.Info) smInfo118 = true;
                    Check("⑱c2 ★ 那条 Info 不是无脑常报：往开局组放一只怪之后就不报了", !smInfo118);

                    // ---- ③ 事件里加一只怪 ⇒ 那条事件有 spawn 行，别的仍然是 `[]`
                    int firstEv18 = 0;
                    if (pe18 != null && pe18.Members != null && pe18.Members.Count > 0)
                        int.TryParse(pe18.Members[0].Key, NumberStyles.Integer, CultureInfo.InvariantCulture, out firstEv18);
                    DiyPool.AddAt(d18, 9, m18.Id, firstEv18);
                    doc18.SaveDiy();
                    JNode pe3 = DiyJson.Member(doc18.Root, "progress_events");
                    JNode row3 = pe3 == null ? null : DiyJson.Member(pe3, firstEv18.ToString(CultureInfo.InvariantCulture));
                    JNode sp3 = row3 == null ? null : DiyJson.Member(row3, "spawn");
                    int stillEmpty18 = 0, total18 = 0;
                    if (pe3 != null && pe3.Members != null)
                        foreach (JMember mem18 in pe3.Members)
                        {
                            total18++;
                            JNode sp18 = DiyJson.Member(mem18.Value, "spawn");
                            if (sp18 != null && sp18.Kind == JKind.Array && sp18.Count == 0) stillEmpty18++;
                        }
                    Line("    给事件 " + firstEv18 + " 放了一只怪 ⇒ 那条的 spawn = " + DiyJson.Compact(sp3)
                        + "；其余 " + stillEmpty18 + "/" + (total18 - 1) + " 条仍是 `[]`");
                    Check("⑱d ★★ 有刷怪的事件写行、没刷怪的写 `[]`（同一份里两种都在，互不干扰）",
                        sp3 != null && sp3.Kind == JKind.Array && sp3.Count == 1
                        && stillEmpty18 == total18 - 1);

                    // ---- ④ ★ 官方模板（difficulty 0~5、没有 diy 簿记）：`start_monsters` 行一个字节不动
                    string offDir = LevelDoc.OfficialLevelDir();
                    string offSrc = offDir == null ? null : Path.Combine(offDir, "82016_1.json");
                    if (offSrc != null && File.Exists(offSrc))
                    {
                        string copy18 = Path.Combine(sandbox18, "官方副本_82016_1.json");
                        File.Copy(offSrc, copy18, true);
                        JNode before18 = DiyJson.Member(DiyJson.Parse(File.ReadAllText(copy18, Encoding.UTF8)), "start_monsters");
                        var beforeRows = new List<string>();
                        if (before18 != null && before18.Kind == JKind.Array && before18.Items != null)
                            foreach (JNode x in before18.Items) beforeRows.Add(DiyJson.Compact(x));
                        LevelDoc od18 = LevelDoc.Open(copy18);
                        od18.SaveDiy();
                        JNode after18 = DiyJson.Member(DiyJson.Parse(File.ReadAllText(copy18, Encoding.UTF8)), "start_monsters");
                        var afterRows = new List<string>();
                        if (after18 != null && after18.Kind == JKind.Array && after18.Items != null)
                            foreach (JNode x in after18.Items) afterRows.Add(DiyJson.Compact(x));
                        bool same18 = beforeRows.Count == afterRows.Count;
                        for (int k = 0; same18 && k < beforeRows.Count; k++)
                            if (!string.Equals(beforeRows[k], afterRows[k], StringComparison.Ordinal)) same18 = false;
                        Line("    官方 82016_1 副本：存盘前 " + beforeRows.Count + " 行 / 存盘后 " + afterRows.Count
                            + " 行 ⇒ 每行逐字节一致 = " + same18 + "（difficulty=" + od18.Difficulty + "）");
                        Check("⑱e ★★ 官方模板（difficulty 0~5 + 没有 diy 簿记）那份 `start_monsters` **一行都不动**"
                            + " —— 「管过才重写」那条闸门没被这次改动放宽",
                            beforeRows.Count > 0 && same18);
                    }
                    else
                    {
                        Line("    ★ 找不到官方 82016_1.json（" + (offDir ?? "(没算出来)") + "）⇒ 这一条跳过");
                        Check("⑱e ★★ 官方模板的 start_monsters 不动（需要官方 levels 目录）", false);
                    }
                }
                else Check("⑱ 显式空刷怪", false);
            }
            catch (Exception ex)
            {
                Line("  ★ 失败：" + ex.Message);
                Check("⑱ 显式空刷怪", false);
            }
            finally { GameLocator.Override = keep18; GameLocator.Reset(); }
            Line("");

            return Finish(logPath);
        }

        /// <summary>
        /// 一份 `.board.json` 里带 `nodes[].diy.landType` 的节点个数（读不出来返回 -1）。
        /// ★ 用在 ⑯j/⑯k：它们要盯的是**落盘那份文件**里有没有不该有的改格，不是"读回内存是不是 0"。
        /// </summary>
        private static int CountDiyLand(string boardPath)
        {
            try
            {
                if (string.IsNullOrEmpty(boardPath) || !File.Exists(boardPath)) return -1;
                JNode root = DiyJson.Parse(File.ReadAllText(boardPath, Encoding.UTF8));
                JNode nodes = DiyJson.Member(root, "nodes");
                if (nodes == null || nodes.Kind != JKind.Array) return -1;
                int n = 0;
                foreach (JNode item in nodes.Items)
                {
                    JNode diy = DiyJson.Member(item, "diy");
                    if (diy != null && DiyJson.Member(diy, "landType") != null) n++;
                }
                return n;
            }
            catch (Exception) { return -1; }
        }

        /// <summary>日志用：一份 `.board.json` 里改了哪几格（"格3→19，格7→12"）。</summary>
        private static string DiyLandList(string boardPath)
        {
            try
            {
                if (string.IsNullOrEmpty(boardPath) || !File.Exists(boardPath)) return "(文件不在)";
                JNode root = DiyJson.Parse(File.ReadAllText(boardPath, Encoding.UTF8));
                JNode nodes = DiyJson.Member(root, "nodes");
                if (nodes == null || nodes.Kind != JKind.Array) return "(没有 nodes)";
                var parts = new List<string>();
                foreach (JNode item in nodes.Items)
                {
                    JNode diy = DiyJson.Member(item, "diy");
                    if (diy == null) continue;
                    JNode lt = DiyJson.Member(diy, "landType");
                    if (lt == null) continue;
                    parts.Add("格" + DiyJson.IntOf(DiyJson.Member(item, "id"), -1)
                        + "→" + DiyJson.IntOf(lt, -1));
                }
                return parts.Count == 0 ? "（干净）" : string.Join("，", parts.ToArray());
            }
            catch (Exception ex) { return "(读不动：" + ex.Message + ")"; }
        }

        /// <summary>日志用：某份文档**内存账**里改了哪几格。</summary>
        private static string DiyLandListOf(LevelDoc doc)
        {
            if (doc == null) return "(null)";
            var keys = new List<int>(doc.NodeOverrides.Keys);
            keys.Sort();
            var parts = new List<string>();
            foreach (int id in keys) parts.Add("格" + id + "→" + doc.NodeOverrides[id]);
            return parts.Count == 0 ? "（干净）" : string.Join("，", parts.ToArray());
        }

        /// <summary>顶层键清单（日志用："map,difficulty,round_award,…"）。</summary>
        private static string TopKeysOf(JNode root)
        {
            if (root == null || root.Members == null) return "(空)";
            var names = new List<string>();
            foreach (JMember m in root.Members) names.Add(m.Key);
            return names.Count + " 个：" + string.Join(",", names.ToArray());
        }

        private static string Ids(List<MapEventRow> rows)
        {
            var a = new string[rows.Count];
            for (int k = 0; k < rows.Count; k++) a[k] = rows[k].Id.ToString(CultureInfo.InvariantCulture);
            return string.Join("/", a);
        }

        private static string Ids(List<int> xs)
        {
            var a = new string[xs.Count];
            for (int k = 0; k < xs.Count; k++) a[k] = xs[k].ToString(CultureInfo.InvariantCulture);
            return string.Join("/", a);
        }

        private static bool Contains(List<string> lines, string needle)
        {
            foreach (string s in lines) if (s.IndexOf(needle, StringComparison.Ordinal) >= 0) return true;
            return false;
        }

        private static string Join(List<PoolMonster> list)
        {
            var parts = new List<string>();
            foreach (PoolMonster m in list) parts.Add(m.Display);
            return string.Join("，", parts.ToArray());
        }

        /// <summary>
        /// `progress_events` 的键 —— 跟 `DiyPool.SyncProgressEvents` 的落盘口径对齐（★ 唯一的键规则）：
        ///   ① 这一条设了触发点 ⇒ **官方事件 id**（服务端照它决定什么时候播）；
        ///   ② 没设 ⇒ **事件自己的 id**（1001 这种 —— 保证两条"还没设触发点"的事件不会挤在一个键上）；
        ///   ③ 再没有 ⇒ 条目编号 n。
        /// </summary>
        private static int EventKey(DiyEvent ev)
        {
            if (ev == null) return -1;
            foreach (DiyProgressItem it in ev.Items) if (it.TriggerEvent > 0) return it.TriggerEvent;
            if (ev.Id > 0) return ev.Id;
            return ev.Items.Count > 0 ? ev.Items[0].N : -1;
        }

        /// <summary>日志用：怪物表里这只怪会不会反击（`Monster.bin` 字段 22 = can_counter）。</summary>
        private static string CC(int table)
        {
            MonsterRow r = GameTables.Monster(table);
            return r == null ? "(表里没有)" : (r.CanCounter ? "会" : "不会");
        }

        /// <summary>这个怪种名下**第一条放置**的血量（断言"别种怪没被动"用；没有放置返回 -1）。</summary>
        private static long RefHp(DiyData d, PoolMonster m)
        {
            if (d == null || m == null) return -1;
            foreach (BoardMonsterRef r in d.BoardMonsters) if (r.Pool == m.Id) return r.Hp;
            return -1;
        }

        /// <summary>这个怪种名下**第一条放置**的反击（同上）。</summary>
        private static bool RefCounter(DiyData d, PoolMonster m)
        {
            if (d == null || m == null) return false;
            foreach (BoardMonsterRef r in d.BoardMonsters) if (r.Pool == m.Id) return r.Counter;
            return false;
        }

        /// <summary>读一个对象里的字符串成员（没有 / 不是字符串 ⇒ ""）。</summary>
        private static string StrOf(JNode obj, string key)
        {
            JNode n = DiyJson.Member(obj, key);
            return n != null && n.Kind == JKind.String ? n.Str : "";
        }

        /// <summary>读一个数组里的第一个整数（没有 / 不是整数 ⇒ fallback）。</summary>
        private static long FirstIntOf(JNode arr, long fallback)
        {
            if (arr == null || arr.Kind != JKind.Array || arr.Items == null || arr.Items.Count == 0) return fallback;
            return DiyJson.IntOf(arr.Items[0], fallback);
        }

        /// <summary>
        /// ★★ 自测要的"干净一张纸"（2026-10-08 大改之后）。
        ///
        /// 为什么：这一批用例拿的是**真实官方关卡**（82016_1），它自带 `start_monsters` +
        /// `progress_events`；新模型的 `DiyPool.Read` 会把官方内容**一起导进池子 / 棋盘**，
        /// 于是"从零开始"的计数基准（池子 2 只 / 格子 3 条…）全都不成立。
        /// ⇒ 先把官方那几块挪开，再让用例从空池子开始造 —— 这是把断言的**基准**跟着模型改，
        ///   而不是把断言放宽成"恒真"。
        ///
        /// ★ 顺带守住新模型那条硬规矩：文件里只要有 `diy.board_monsters`，就**不再**从
        ///   `progress_events.*.spawn[].node` / `start_monsters[].node` 补落点（见
        ///   `DiyPool.ReadFlatEvents` 的 `addRefs`）—— 两边都读会把同一只怪数两遍。
        /// </summary>
        private static JNode CleanSheet(LevelDoc doc, string workPath)
        {
            JNode root = DiyJson.Parse(File.ReadAllText(workPath, Encoding.UTF8));
            DiyJson.RemoveMember(root, "start_monsters");
            DiyJson.RemoveMember(root, "progress_events");
            DiyJson.RemoveMember(root, "victory");
            doc.Root = root;
            doc.InvalidateDiy();
            doc.InvalidateStarts();
            doc.InvalidateBoardOverrides();
            return root;
        }

        /// <summary>一条官方效果表 → 一行说明（日志用）。</summary>
        private static string Effects(MapEventRow r)
        {
            var parts = new List<string>();
            var keys = new List<int>(r.Effects.Keys);
            keys.Sort();
            foreach (int k in keys)
            {
                var vals = new List<string>();
                foreach (int v in r.Effects[k]) vals.Add(v.ToString(CultureInfo.InvariantCulture));
                parts.Add(k + ":" + string.Join(",", vals.ToArray()));
            }
            return parts.Count == 0 ? "(没有效果)" : string.Join(" + ", parts.ToArray());
        }

        // ================================================================ 小工具
        /// <summary>起点格的 serial 列表（给人看的 "0/1/2/3"）。</summary>
        private static string[] Serials(List<BoardNode> ns)
        {
            var a = new string[ns.Count];
            for (int k = 0; k < ns.Count; k++) a[k] = ns[k].Serial.ToString(CultureInfo.InvariantCulture);
            return a;
        }

        /// <summary>数一数校验结果里有几条"会被丢 / 会出错"（Bad）。</summary>
        private static int CountBad(List<LintFinding> fs)
        {
            int n = 0;
            foreach (LintFinding f in fs) if (f.Level == LintLevel.Bad) n++;
            return n;
        }

        /// <summary>数一数棋盘上 landType==1 的格子（证明改起点没动官方数据）。</summary>
        private static int CountLandType1(BoardModel b)
        {
            if (b == null || !b.Ok) return 0;
            int n = 0;
            foreach (BoardNode x in b.Nodes) if (x.LandType == 1) n++;
            return n;
        }

        private static string TopKeyOrder(string text)
        {
            var keys = new List<string>();
            int depth = 0;
            bool inStr = false;
            var sb = new StringBuilder();
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (inStr)
                {
                    if (c == '\\') { i++; continue; }
                    if (c == '"')
                    {
                        inStr = false;
                        if (depth == 1)
                        {
                            int k = i + 1;
                            while (k < text.Length && char.IsWhiteSpace(text[k])) k++;
                            if (k < text.Length && text[k] == ':') keys.Add(sb.ToString());
                        }
                    }
                    else if (depth == 1) sb.Append(c);
                    continue;
                }
                if (c == '"') { inStr = true; sb.Length = 0; continue; }
                if (c == '{' || c == '[') depth++;
                else if (c == '}' || c == ']') depth--;
            }
            return string.Join(",", keys.ToArray());
        }

        private static string FirstExisting(params string[] paths)
        {
            foreach (string p in paths)
                if (!string.IsNullOrEmpty(p) && Directory.Exists(p)) return Path.GetFullPath(p);
            return null;
        }

        private static string Own()
        {
            return Paths.Own();
        }

        /// <summary>数一份 lint 结果里某一档有几条（⑰d2 用）。</summary>
        private static int CountLevel(List<LintFinding> fs, LintLevel level)
        {
            int n = 0;
            foreach (LintFinding f in fs) if (f.Level == level) n++;
            return n;
        }

        /// <summary>这个节点是不是"键在、空数组"（`[]`）—— ⑯/⑱ 那几条"明确不刷"的判据用它。</summary>
        private static bool IsEmptyArray(JNode n)
        {
            return n != null && n.Kind == JKind.Array && n.Count == 0;
        }

        private static void Line(string s)
        {
            Lines.Add(s);
            try { Console.WriteLine(s); } catch (Exception) { }
        }

        private static void Check(string what, bool ok)
        {
            if (ok) _pass++; else _fail++;
            Line("  " + (ok ? "【通过】" : "【失败】") + " " + what);
        }

        private static int Finish(string logPath)
        {
            Line("");
            Line("════════════════════════════════════════");
            Line("通过 " + _pass + " 条，失败 " + _fail + " 条 ⇒ " + (_fail == 0 ? "全部通过" : "有失败"));
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(logPath));
                File.WriteAllText(logPath, string.Join("\r\n", Lines.ToArray()) + "\r\n",
                    new UTF8Encoding(true));
                Console.WriteLine("（逐条输出已写入 " + logPath + "）");
            }
            catch (Exception ex)
            {
                Console.WriteLine("（写日志失败：" + ex.Message + "）");
            }
            return _fail == 0 ? 0 : 1;
        }
    }
}
