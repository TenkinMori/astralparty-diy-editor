using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace DiyEditor
{
    /// <summary>表单里的一行：一个 FieldSpec + 树里对应的那个节点。</summary>
    public sealed class FieldInstance
    {
        public FieldSpec Spec;
        public JNode Node;
        public string Where = "";     // 人类可读位置，例如 progress_events["32122"].spawn[0].node
        public int Occurrence;        // 同一种 spec 的第几处（从 0 起）
        public JNode Row;             // 所在的行 / 事件（结构性编辑要用）
        public string Owner;          // 更短的归属，例如 "32122 号事件"

        public string Display
        {
            get { return Occurrence > 0 ? Spec.Label + "（第 " + (Occurrence + 1) + " 处）" : Spec.Label; }
        }
    }

    /// <summary>★ 一次"存到本地 DIY 目录"的结果（给人看的凭据，页⑤和棋盘页都显示它）。</summary>
    public sealed class DiySaveResult
    {
        public string DiyDir = "";
        public string LevelPath = "";
        public string BoardPath = "";
        public bool Overwrote;
        public string BackupPath = "";
        public string SourceSha256 = "";
        public string BackupSha256 = "";
        public string Sha256 = "";
        public long Bytes;
        public bool ByteIdentical;
        public string BoardNote = "";
        public string BoardSource = "";
        public string BoardBackup = "";
        public string BoardSha = "";
        public long BoardBytes;
        public bool BoardWritten;
        /// <summary>★ 这一格里改过功能的格子数（写进 boards\&lt;后缀&gt;.json 的 nodes[i].diy.landType）。</summary>
        public int BoardNodesChanged;
    }

    /// <summary>打开过的一份关卡：原文 + 解析树 + 棋盘。</summary>
    public sealed class LevelDoc
    {
        public string Path = "";
        public ExportService.Loaded Loaded;
        public JNode Root;
        public BoardModel Board;
        public bool ReadOnlyDemo;      // ★ 从服务端只读样例目录打开的（导出一律另存）

        // ★ DIY 相关的身份（从 `diy` 块读出来，或者新建模板时直接给）
        public bool IsDiy;
        public string DiyName = "";
        public string BaseLevel = "";      // 底版官方 json 的文件名
        public string BoardSuffix = "";    // 底版画板号（"023"）

        public bool Changed { get { return Root != null && Root.Dirty; } }
        public string FileName { get { return Path.Length == 0 ? "(未保存)" : System.IO.Path.GetFileName(Path); } }

        // ============================================================ ★ DIY 侧数据（怪物池 / 进度）
        private DiyData _diy;
        private bool _diyRead;

        /// <summary>
        /// ★ 这份关卡的 DIY 侧数据（怪物池 + 棋盘放怪 + 进度事件）。
        ///   读一次缓存住；改动走 `Diy.Save()`（写完会 `InvalidateDiy()`）。
        /// </summary>
        public DiyData Diy
        {
            get
            {
                if (!_diyRead)
                {
                    _diyRead = true;
                    _diy = DiyPool.Read(this);
                    DiyPool.BindLegacy(_diy);
                }
                return _diy;
            }
        }

        /// <summary>树被换过 / 存盘之后调一次，下次读重来。</summary>
        public void InvalidateDiy() { _diyRead = false; _diy = null; }

        /// <summary>把内存里的 DIY 数据写回树里（怪物池 / 棋盘放怪 / 进度事件 + 同步 spawn）。</summary>
        public void SaveDiy()
        {
            SaveDiy(Diy);
        }

        /// <summary>
        /// 把**指定那一份** DIY 数据写回树里。★ **缓存不作废** —— 这个对象就是权威的那一份，
        ///   树只是它落盘时的形状（写完 `Diy` 还是它，`NextPoolId()` 之类的账面不会跳）。
        ///
        /// ★★ 写完还做两件"派生账"的事（用户第 9 波："怪物页面要实时变化的"）：
        ///   ① `DiyPool.BindProgress(d)` 重算"每只怪挂在哪条进度下"（怪物页分栏的依据）；
        ///   ② `DiyNotify.Touch()` 响一声，各页在变回可见时自己重建。
        /// </summary>
        public void SaveDiy(DiyData d)
        {
            if (Root == null) return;
            DiyPool.Write(this, d ?? Diy);
            CacheStable();
            if (d != null)
            {
                DiyPool.BindProgress(d);
                DiyNotify.Touch();
            }
        }

        /// <summary>
        /// ★ 让"内存里那一份"跟刚铺进树里的内容**重新对齐一次**。
        ///
        /// ★★ 只动**字段**、**不换对象**：界面上的编辑（`PoolMonster` 引用）必须活着，
        ///    所以这里只把"池内序号被重排"这件事反映回内存对象的 Id，
        ///    并把进度条词 / 获胜条件里的指向一起改成新号。
        ///    （换成"重新 Read 一份"就会把用户手上那只怪的属性丢掉 —— 自测 ⑫i 抓的就是这个。）
        /// </summary>
        private void CacheStable()
        {
            if (!_diyRead || _diy == null) return;

            var remap = new Dictionary<int, int>();
            for (int k = 0; k < _diy.Pool.Count; k++)
            {
                int want = k + 1;
                if (_diy.Pool[k].Id != want) remap[_diy.Pool[k].Id] = want;
                _diy.Pool[k].Id = want;
            }
            if (remap.Count == 0) return;

            foreach (BoardMonsterRef r in _diy.BoardMonsters)
            {
                int to;
                if (remap.TryGetValue(r.Pool, out to)) r.Pool = to;
            }
            foreach (DiyEvent ev in _diy.Events)
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
                    else targets.Add(t);
                }
                ev.VictoryTargets = targets;
            }
        }

        /// <summary>
        /// ★★ **内存里的那一份** DIY 数据：读一次就缓存住，**写回树之后也保持是同一对象**
        ///   （`SaveDiy` 只把当前内容铺进树，不会把缓存扔掉）。
        ///
        /// 为什么必须这样：界面上一堆地方是这样用的 ——
        ///   `DiyData d = doc.Diy;` → 改 `d` → `doc.SaveDiy();` → **接着用 `d`**。
        /// 如果 `SaveDiy` 把缓存作废了，那 `d` 就成了"没人认的孤本"，
        /// 后面每一次 `doc.Diy` 都会**重新从 json 读一份新的**，
        /// 于是"我刚加的那只怪"在下一次读里对不上号（池内序号还会被重排）。
        /// ⇒ 这里明确：**这个对象就是权威的那一份**，树只是它的落盘形状。
        /// </summary>
        public DiyData MemoryDiy
        {
            get { return Diy; }
        }

        public static LevelDoc Open(string path)
        {
            var doc = new LevelDoc();
            doc.Path = System.IO.Path.GetFullPath(path);
            doc.Loaded = ExportService.Load(doc.Path);
            doc.Root = DiyJson.Parse(doc.Loaded.Text);
            doc.ReadOnlyDemo = doc.Path.IndexOf(@"\4-服务端\", StringComparison.OrdinalIgnoreCase) >= 0;
            // ★ 换文档就先把这个静态棋盘缓存清掉：否则"先进 A、再开指着别的棋盘的 B"
            //   会拿到 A 那份模型（编辑器一次只开一份文档，清了代价极小）。
            BoardStore.ClearCache();
            doc.ResolveDiyBoard();
            doc.LoadBoard(null);
            return doc;
        }

        // ============================================================ ★ DIY：从地图模板新建
        /// <summary>DIY 难度号段从 1001 起（用户已拍板）。</summary>
        public const int DiyDifficulty = 1001;

        /// <summary>
        /// ★ 新建一份 DIY 关卡：**只给地图骨架**，官方那几张图上写着的内容一条都不预填。
        ///
        /// "骨架" = 地图 id + 难度档 + 画板号 + 棋盘本身（格子布局、起点 1~4 的 serial、
        ///   每格的官方功能）—— 从官方 json 借的只有 `map` / `victory` 这两个身份键，
        ///   棋盘另有一份 `.board.json` 快照。
        ///
        /// ★ 不预填的（用户 2026-10-03："我的预想是你只提供地图模板，不要往里面放预设"）：
        ///   `progress_events`（事件清单与 attack/defense/spawn）、`start_monsters`（开局怪）、
        ///   `progress`（start / per_round / per_death / mission_delta）、`rounds`、
        ///   `round_award`、`settlement`、`mastermind`，以及官方 json 里那些
        ///   `note` / `note_*` / `_note_*` 写给人看的备注 —— 这些键整块**摘掉**。
        ///
        /// ★ 为什么是"摘掉"而不是"写个空值"：官方 levels 的语义就是"没写的字段回落到
        ///   表/代码默认值"，而 `start_monsters: []` 这种**空数组会让服务端真的一只怪都不刷**
        ///   （不会回落）。摘掉 = 回落；空容器 = 明确地"一个都不要"。要的是前者。
        ///
        /// 底版怎么挑（只借棋盘，不借内容）：
        ///   ① 优先 `<map>_<这一档里最小的难度>.json`；
        ///   ② 没有就用 `<map>_*.json` 里文件名最小的那一份；
        ///   ③ 一份都没有 ⇒ 抛异常说清楚（页面上会把这张卡片的按钮灰掉）。
        /// </summary>
        public static LevelDoc CreateFromTemplate(MapTemplate template, string diyName, int difficulty)
        {
            if (template == null) throw new InvalidOperationException("没给地图模板。");
            if (string.IsNullOrEmpty(template.SceneSuffix))
                throw new InvalidOperationException("这张图认不出画板号，没法定棋盘。");
            if (difficulty < DiyDifficulty)
                throw new InvalidOperationException("DIY 难度号要 >= " + DiyDifficulty + "（避开官方的 0~4）。");

            string source = OfficialLevelFor(template);
            if (source == null)
                throw new InvalidOperationException(
                    "找不到 map " + template.Id + " 的官方 levels json ⇒ 没有底版可用。\n\n"
                    + "（底版只从「4-服务端\\server\\data\\levels」里挑现成的，不凭空造。）");

            var doc = new LevelDoc();
            doc.Path = "";
            doc.Loaded = ExportService.Load(source);
            doc.Root = DiyJson.Parse(doc.Loaded.Text);
            doc.ReadOnlyDemo = false;

            doc.IsDiy = true;
            doc.DiyName = diyName;
            doc.BaseLevel = System.IO.Path.GetFileName(source);
            doc.BoardSuffix = template.SceneSuffix;

            // 官方白名单里的两个身份字段
            DiyJson.SetInt(DiyJson.EnsureMember(doc.Root, "map"), template.Id);
            DiyJson.SetInt(DiyJson.EnsureMember(doc.Root, "difficulty"), difficulty);

            // ★ DIY 扩展块：服务端 `_normalize` 只挑它认识的顶层键，所以这一整块会被静默丢掉
            //   —— 正是我们想要的（设计稿 v2 §2.2：扩展都塞进一个 diy 块）。
            JNode diy = DiyJson.EnsureMember(doc.Root, "diy");
            DiyJson.SetKind(diy, JKind.Object);
            if (diy.Members == null) diy.Members = new List<JMember>();
            else diy.Members.Clear();
            DiyJson.MarkDirty(diy);
            DiyJson.SetInt(DiyJson.EnsureMember(diy, "version"), 1);
            DiyJson.SetString(DiyJson.EnsureMember(diy, "name"), diyName);
            DiyJson.SetInt(DiyJson.EnsureMember(diy, "base_map"), template.Id);
            DiyJson.SetString(DiyJson.EnsureMember(diy, "base_scene"), template.SceneSuffix);
            DiyJson.SetString(DiyJson.EnsureMember(diy, "base_level"), doc.BaseLevel);
            DiyJson.SetString(DiyJson.EnsureMember(diy, "base_level_sha256"),
                ExportService.Sha256OfFile(source));
            DiyJson.SetString(DiyJson.EnsureMember(diy, "board"), doc.BoardFileName);
            DiyJson.SetString(DiyJson.EnsureMember(diy, "created"),
                DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));

            // ★★ 只留骨架：把官方那份图上的"内容"整块摘掉（见上面那段说明）。
            StripOfficialContent(doc.Root);

            // ★★ 铺上 DIY 侧数据：空池子 + 空棋盘放怪 + `DIY进度` 那一条占位事件
            //    （内容包预留位，名字 / 简介是用户给定的原话）。
            DiyPool.Write(doc, DiyPool.NewData());

            // ★★ 2026-10-09 同一个坑（用户实报："新建了新的关卡但是棋盘的摆布还是之前旧的"）：
            //   `BoardStore.DiyBoardPath` 是**静态**字段、`ExpectedSuffix` / 棋盘缓存也全是静态的 ——
            //   这份新文档`new` 出来时它们还挂着**上一份关卡**的值，`LoadBoard` 会优先照
            //   `DiyBoardPath` 读 ⇒ 新关卡继承上一关的改格。
            //   老 `CreateFromOwnTemplate` 已经这么修了；这里（从官方骨架新建那条路）同样口径补上，
            //   只留 `BoardSuffix` 指向的那份**官方画板缓存**当底。
            BoardStore.DiyBoardPath = "";
            BoardStore.ExpectedSuffix = doc.BoardSuffix == null ? "" : doc.BoardSuffix;
            BoardStore.ClearCache();

            doc.LoadBoard(null);
            return doc;
        }

        // ============================================================ ★★ 成品模板（2026-10-08 大改）
        /// <summary>
        /// 从**我们提供的成品模板**新建一份 DIY 关卡。
        ///
        /// 和老 `CreateFromTemplate`（只给地图骨架、把官方内容整块摘掉）的区别：
        ///   · 模板里有什么就**原样留着**：`rounds` / `round_award` / `progress` /
        ///     `start_monsters` / `progress_events` / `victory` / `mastermind` / `settlement`；
        ///   · 只改身份：`map` 保持模板的（认得出是给哪张图做的）、`difficulty` 换成 DIY 号段、
        ///     再挂上 `diy` 身份块（名字 / 模板文件名 / 画板号 / 棋盘文件名 / 时间）；
        ///   · 最后 `DiyPool.Read` → `Write` 走一遍：把模板自带的**事件与怪**读成编辑器里的
        ///     对象再写回去 —— 打开就能直接看到并修改它们，不是一张白纸。
        /// </summary>
        public static LevelDoc CreateFromOwnTemplate(string templatePath, string diyName, int difficulty)
        {
            if (string.IsNullOrEmpty(templatePath) || !File.Exists(templatePath))
                throw new InvalidOperationException("模板文件不在：" + templatePath);
            if (difficulty < DiyDifficulty)
                throw new InvalidOperationException("DIY 难度号要 >= " + DiyDifficulty + "（避开官方的 0~4）。");

            var doc = new LevelDoc();
            doc.Path = "";
            doc.Loaded = ExportService.Load(templatePath);
            doc.Root = DiyJson.Parse(doc.Loaded.Text);
            doc.ReadOnlyDemo = false;
            doc.IsDiy = true;
            doc.DiyName = diyName;
            doc.BaseLevel = System.IO.Path.GetFileName(templatePath);

            int mapId = (int)DiyJson.IntOf(DiyJson.Member(doc.Root, "map"), 0);
            DiyJson.SetInt(DiyJson.EnsureMember(doc.Root, "difficulty"), difficulty);

            // 画板号：模板自己写着就用它（`diy.base_scene`），否则从地图表推
            JNode diyOld = DiyJson.Member(doc.Root, "diy");
            JNode bsOld = DiyJson.Member(diyOld, "base_scene");
            string scene = (bsOld != null && bsOld.Kind == JKind.String) ? bsOld.Str : "";
            if (scene.Length == 0 && mapId > 0)
            {
                MapTemplate t = GameTables.Map(mapId);
                if (t != null)
                {
                    GameTables.EnsureBoardInfo(t);
                    scene = t.SceneSuffix == null ? "" : t.SceneSuffix;
                }
            }
            doc.BoardSuffix = scene;

            JNode diy = DiyJson.EnsureMember(doc.Root, "diy");
            DiyJson.SetKind(diy, JKind.Object);
            DiyJson.SetInt(DiyJson.EnsureMember(diy, "version"), 1);
            DiyJson.SetString(DiyJson.EnsureMember(diy, "name"), diyName);
            DiyJson.SetString(DiyJson.EnsureMember(diy, "template"), System.IO.Path.GetFileName(templatePath));
            DiyJson.SetInt(DiyJson.EnsureMember(diy, "base_map"), mapId);
            DiyJson.SetString(DiyJson.EnsureMember(diy, "base_scene"), scene);
            DiyJson.SetString(DiyJson.EnsureMember(diy, "base_level"), doc.BaseLevel);
            DiyJson.SetString(DiyJson.EnsureMember(diy, "board"), doc.BoardFileName);
            DiyJson.SetString(DiyJson.EnsureMember(diy, "created"),
                DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));

            // ★ 模板自带的内容读成编辑器对象再写回去（顺带补齐 diy.monsters / board_monsters）
            DiyPool.Write(doc, DiyPool.Read(doc));

            // ★★ 2026-10-09 修（用户实报："新建了新的关卡但是棋盘的摆布还是之前旧的"）：
            //   `BoardStore.DiyBoardPath` 是**静态**字段，上一份 DIY 关卡的 `.board.json`
            //   路径还挂在那儿；`LoadBoard` 会优先照它读 ⇒ 把**上一关的改格**当成这份新
            //   文档的棋盘，存盘时再顺手抄进新关卡的 `.board.json`（实测：连开四份新关卡，
            //   四份 board 的改格逐字节相同）。
            //   ⇒ 新建一份文档之前必须把"上一份的棋盘状态"清干净，只留 `BoardSuffix`
            //     指向的那份**官方画板缓存**当底。
            BoardStore.DiyBoardPath = "";
            BoardStore.ExpectedSuffix = doc.BoardSuffix == null ? "" : doc.BoardSuffix;
            BoardStore.ClearCache();

            doc.LoadBoard(null);
            return doc;
        }

        /// <summary>★ 官方 levels json 里"属于内容、不属于地图"的键 —— 新建 DIY 时一个都不留。
        ///
        /// 摘掉 = 让服务端回落到表/代码默认值；写成空容器反而是"明确地一个都不要"。
        /// 以后用户在编辑器里加了事件 / 放了怪，对应的键会由编辑动作自己建出来。
        ///
        /// ★ `victory` 也在里面：官方的胜利条件是"打死某只 Boss"，而那只 Boss 是官方
        ///   配在 `start_monsters` 里的 —— 预填 victory 就会出现"目标怪这份配置里根本不会
        ///   出现"（第⑤页会如实报成红色）。用户 2026-10-03："不要往里面放预设"。
        ///   所以胜利条件也留空，由用户自己填（不写 = 服务端不判胜）。
        /// </summary>
        public static readonly string[] ContentKeys = new string[]
        {
            "rounds", "round_award", "progress", "progress_events", "start_monsters",
            "victory", "settlement", "mastermind",
        };

        /// <summary>把一份官方关卡里"内容"那部分摘干净，只留地图骨架（身份键）。</summary>
        public static void StripOfficialContent(JNode root)
        {
            if (root == null || root.Kind != JKind.Object) return;
            foreach (string key in ContentKeys)
                if (DiyJson.Member(root, key) != null) DiyJson.RemoveMember(root, key);

            // 官方 json 里写给人看的备注：也一并带走（用户："软件内怎么还是一堆注释"）。
            // ★ 判据是"键名里带 note" —— 官方的写法不止一种（note / note_spawn /
            //   progress_events_note / _note_boss_max_hp），按前缀只认得出前两种。
            var notes = new List<string>();
            foreach (JMember m in root.Members)
                if (m.Key.IndexOf("note", StringComparison.OrdinalIgnoreCase) >= 0)
                    notes.Add(m.Key);
            foreach (string key in notes) DiyJson.RemoveMember(root, key);
        }

        /// <summary>官方 levels 目录（只读）。找不到回空串。</summary>
        public static string OfficialLevelDir()
        {
            // ① 服务端登记过的表目录同级的 levels（游戏根里那一份，只读）
            string g = GameLocator.GameDir;
            if (!string.IsNullOrEmpty(g))
            {
                string a = System.IO.Path.Combine(g, System.IO.Path.Combine("server",
                    System.IO.Path.Combine("data", "levels")));
                if (Directory.Exists(a)) return a;
            }
            // ② 工作副本里现成的（开发机路径，只读）
            string dir = AppContext.BaseDirectory.TrimEnd('\\');
            for (int k = 0; k < 10 && !string.IsNullOrEmpty(dir); k++)
            {
                string probe = System.IO.Path.Combine(dir, System.IO.Path.Combine("4-服务端",
                    System.IO.Path.Combine("server", System.IO.Path.Combine("data", "levels"))));
                if (Directory.Exists(probe)) return probe;
                dir = System.IO.Path.GetDirectoryName(dir);
            }
            string direct = @"D:\小小兔趴\4-服务端\server\data\levels";
            return Directory.Exists(direct) ? direct : "";
        }

        /// <summary>给这张模板挑一份官方 json 当底版。一份都没有就回 null。</summary>
        public static string OfficialLevelFor(MapTemplate template)
        {
            string dir = OfficialLevelDir();
            if (dir.Length == 0 || template == null) return null;
            var cands = new List<string>();
            try
            {
                foreach (string f in Directory.GetFiles(dir, template.Id + "_*.json"))
                    cands.Add(f);
                if (cands.Count == 0)
                    foreach (string f in Directory.GetFiles(dir, "*.json"))
                    {
                        string stem = System.IO.Path.GetFileNameWithoutExtension(f);
                        int at = stem.LastIndexOf('_');
                        if (at <= 0) continue;
                        int map;
                        if (int.TryParse(stem.Substring(0, at), out map) && map == template.Id)
                            cands.Add(f);
                    }
            }
            catch (Exception) { return null; }
            if (cands.Count == 0) return null;

            // 优先"这张模板确实有的最低档"
            string best = null;
            int bestDiff = int.MaxValue;
            foreach (string f in cands)
            {
                string stem = System.IO.Path.GetFileNameWithoutExtension(f);
                int at = stem.LastIndexOf('_');
                int d;
                if (at <= 0 || !int.TryParse(stem.Substring(at + 1), out d)) d = 99;
                bool listed = template.Difficulties.Count == 0 || template.Difficulties.Contains(d);
                int weight = listed ? d : d + 1000;      // 表里登记的档优先
                if (weight < bestDiff) { bestDiff = weight; best = f; }
            }
            return best;
        }

        /// <summary>这份 DIY 关卡自己带的棋盘文件（跟关卡文件同目录、同名字）。</summary>
        public string BoardFileName
        {
            get
            {
                string stem = SanitizeName(DiyName);
                if (stem.Length == 0) stem = "未命名";
                return stem + ".board.json";
            }
        }

        /// <summary>★ 关卡/文件名里不能有的字符（Windows 规矩 + 别让服务端难做）。</summary>
        public static string SanitizeName(string name)
        {
            if (name == null) return "";
            var sb = new StringBuilder();
            foreach (char c in name.Trim())
            {
                if (c < 32) continue;
                if ("\\/:*?\"<>|".IndexOf(c) >= 0) continue;
                sb.Append(c);
            }
            string s = sb.ToString().Trim().TrimEnd('.');
            if (s.Length > 60) s = s.Substring(0, 60).TrimEnd();
            return s;
        }

        /// <summary>★ 认出一份 json 是不是 DIY 关卡（有没有 `diy` 块）。</summary>
        public static bool LooksDiy(string path)
        {
            try
            {
                if (!File.Exists(path)) return false;
                JNode root = DiyJson.Parse(File.ReadAllText(path, System.Text.Encoding.UTF8));
                JNode diy = DiyJson.Member(root, "diy");
                return diy != null && diy.Kind == JKind.Object;
            }
            catch (Exception) { return false; }
        }

        /// <summary>打开时把 `diy` 块认出来（`board` 是相对路径，按关卡文件所在目录解）。</summary>
        public void ResolveDiyBoard()
        {
            IsDiy = false;
            DiyName = "";
            BaseLevel = "";
            BoardSuffix = "";
            BoardStore.DiyBoardPath = "";
            _ownBoardPath = "";          // ★ 换文档/新建：这份文档自己的棋盘文件也一并放开
            InvalidateStarts();          // ★ diy.starts 也要跟着换文档重读
            InvalidateBoardOverrides();  // ★ 改过的格子同理
            InvalidateDiy();             // ★ 怪物池 / 进度 同理
            if (Root == null) return;

            JNode diy = DiyJson.Member(Root, "diy");
            if (diy == null || diy.Kind != JKind.Object) return;
            IsDiy = true;

            JNode nm = DiyJson.Member(diy, "name");
            if (nm != null && nm.Kind == JKind.String) DiyName = nm.Str;
            if (DiyName.Length == 0) DiyName = System.IO.Path.GetFileNameWithoutExtension(Path);

            JNode bs = DiyJson.Member(diy, "base_scene");
            if (bs != null && bs.Kind == JKind.String) BoardSuffix = bs.Str;

            JNode bl = DiyJson.Member(diy, "base_level");
            if (bl != null && bl.Kind == JKind.String) BaseLevel = bl.Str;

            JNode bd = DiyJson.Member(diy, "board");
            if (bd != null && bd.Kind == JKind.String && bd.Str.Trim().Length > 0)
            {
                string rel = bd.Str.Trim();
                string dir = System.IO.Path.GetDirectoryName(Path);
                string full = System.IO.Path.IsPathRooted(rel) ? rel : System.IO.Path.Combine(dir, rel);
                if (File.Exists(full)) { BoardStore.DiyBoardPath = full; _ownBoardPath = full; }
            }
        }

        /// <summary>
        /// ★ 起点分配（`diy.starts`）= **四个玩家各自的起始格 id，按起点 1~4 的顺序**。
        ///
        ///   为什么是"格子 id 列表"而不是"给格子加个编号"：官方棋盘里起点格的区分
        ///   本来就是 `serial`（座位号，见 `BoardModel.StartNodes` 的说明），
        ///   而**第几号起点 = 列表里第几个**这件事只有一份权威表达最不容易打架。
        ///   ⇒ 这里一份数组说清"1 号站哪格、2 号站哪格…"，**绝不去动官方 `landType`**。
        ///
        ///   ★ 没写 = 用官方那套（按 `serial` 排的起点格，见 `BoardModel.DefaultStarts`）。
        /// </summary>
        public const string DiyStartsKey = "starts";

        private List<int> _starts;
        private bool _startsRead;

        /// <summary>`diy.starts` 本身（没写回 null）。</summary>
        public List<int> StartsNodes
        {
            get
            {
                if (!_startsRead)
                {
                    _startsRead = true;
                    _starts = null;
                    if (Root != null)
                    {
                        JNode diy = DiyJson.Member(Root, "diy");
                        JNode v = DiyJson.Member(diy, DiyStartsKey);
                        if (v != null && v.Kind == JKind.Array)
                        {
                            _starts = new List<int>();
                            foreach (JNode item in v.Items)
                            {
                                long id;
                                if (DiyJson.IsInt(item, out id)) _starts.Add((int)id);
                            }
                        }
                    }
                }
                return _starts;
            }
        }

        /// <summary>清掉这个缓存（改了 `diy.starts` 之后调）。</summary>
        public void InvalidateStarts() { _startsRead = false; _starts = null; }

        /// <summary>
        /// ★ 真正生效的起点顺序（起点 1~4 各自的格子 id）。
        ///   用户写过 `diy.starts` 就用用户的（哪怕只有 2 条 —— 缺哪个由校验报出来），
        ///   没写过才回落到官方那套（按 `serial` 排的起点格）。
        /// </summary>
        public List<int> EffectiveStarts()
        {
            List<int> mine = StartsNodes;
            if (mine != null) return new List<int>(mine);
            if (Board != null && Board.Ok) return Board.DefaultStarts();
            return new List<int>();
        }

        /// <summary>
        /// 把"某些格子"设成起点、或取消它的起点身份。
        ///
        /// ★ 语义（为的是"编号不重复、取消不留洞"）：
        ///   · 这一格**已经在**列表里 ⇒ 先把它摘掉（后面再按序号插回去）；
        ///   · 指定 N 号 ⇒ 放到列表第 N-1 位；中间空着的用 -1 占位（= "这一号还没指定"）；
        ///   · 取消 ⇒ 摘掉就完了，后面的号**不往前挪**（免得用户点一下"取消 2 号"，
        ///     3 号悄悄变成 2 号 —— 那才是真的"编号乱了"）。
        /// </summary>
        /// <param name="nodeId">棋盘格 id。</param>
        /// <param name="number">1~4 = 设为第几号；0 = 取消这一格的起点。</param>
        public void SetStart(int nodeId, int number)
        {
            if (Root == null) return;
            var list = EffectiveStarts();
            while (list.Count < 4 && number > 0) list.Add(-1);
            for (int k = 0; k < list.Count; k++) if (list[k] == nodeId) list[k] = -1;

            if (number >= 1 && number <= 4)
            {
                while (list.Count < number) list.Add(-1);
                list[number - 1] = nodeId;
            }

            // 去掉尾巴上没用的 -1（前面中间的留着，那代表"这一号空着"）
            while (list.Count > 0 && list[list.Count - 1] == -1) list.RemoveAt(list.Count - 1);
            WriteStarts(list);
        }

        /// <summary>整份替换 `diy.starts`（传空 = 删掉这个键，回到官方的 serial 那套）。</summary>
        public void SetStarts(IList<int> ids)
        {
            if (Root == null) return;
            WriteStarts(ids);
        }

        private void WriteStarts(IList<int> list)
        {
            bool empty = list == null || list.Count == 0;
            if (empty)
            {
                // ★ 全取消了 ⇒ 删掉这个键，回到"用官方那套"
                JNode diy0 = DiyJson.Member(Root, "diy");
                if (diy0 != null && DiyJson.Member(diy0, DiyStartsKey) != null)
                    DiyJson.RemoveMember(diy0, DiyStartsKey);
            }
            else
            {
                JNode diy = DiyJson.EnsureMember(Root, "diy");
                DiyJson.SetKind(diy, JKind.Object);
                JNode arr = DiyJson.SetKind(DiyJson.EnsureMember(diy, DiyStartsKey), JKind.Array);
                arr.Items.Clear();
                foreach (int id in list)
                {
                    var item = new JNode { Kind = JKind.Number, Num = id.ToString(CultureInfo.InvariantCulture), Parent = arr };
                    arr.Items.Add(item);
                }
                DiyJson.MarkDirty(arr);
                DiyJson.MarkDirty(diy);
            }
            InvalidateStarts();
        }

        /// <summary>
        /// ★ 起点自查（自测 / 排查用）：把"缺 / 重"那几种坏情况都摆一遍，确认都报得出来。
        /// 返回一行行给人看的结果。
        /// </summary>
        public List<string> StartSelfCheck()        {
            var outLines = new List<string>();
            if (Root == null || Board == null || !Board.Ok)
            {
                outLines.Add("（没有文档或没有棋盘，跳过起点自查）");
                return outLines;
            }

            List<int> keep = StartsNodes == null ? null : new List<int>(StartsNodes);
            List<int> good = Board.DefaultStarts();
            if (good.Count < 4) { outLines.Add("（这块画板起始点不足 4 个，跳过）"); return outLines; }

            try
            {
                SetStarts(new List<int> { good[0], good[1] });
                int missing = CountBadIn(StartsFindings());
                outLines.Add("只写 2 个 ⇒ 报错 " + missing + " 条");

                SetStarts(new List<int> { good[0], good[0], good[2], good[3] });
                int dup = CountBadIn(StartsFindings());
                outLines.Add("1、2 号指同一格 ⇒ 报错 " + dup + " 条");

                SetStarts(new List<int> { good[0], -1, -1, good[3] });
                int vague = CountBadIn(StartsFindings());
                outLines.Add("2、3 号空着（-1）⇒ 报错 " + vague + " 条");

                SetStarts(good);
                int fine = CountBadIn(StartsFindings());
                outLines.Add("正好 4 个不重复 ⇒ 报错 " + fine + " 条");

                outLines.Add((missing > 0 && dup > 0 && vague > 0 && fine == 0)
                    ? "⇒ 起点校验：缺 / 重 / 空格都报得出来，好的一遍不报 ✓"
                    : "⇒ ★ 起点校验有问题（某一档没报出来）");
            }
            finally
            {
                // ★ 自查完把这份文档恢复成原样，别污染后面的自测
                if (keep == null)
                {
                    JNode d = DiyJson.Member(Root, "diy");
                    if (d != null) DiyJson.RemoveMember(d, DiyStartsKey);
                    InvalidateStarts();
                }
                else SetStarts(keep);
            }
            return outLines;
        }

        /// <summary>"缺 / 重"那几行是 `★` 开头的（第 1 行是结论，不算）。</summary>
        private int CountBadIn(List<string> findings)
        {
            int n = 0;
            for (int k = 1; k < findings.Count; k++) if (findings[k].StartsWith("★")) n++;
            return n;
        }

        // ================================================================ ★ 换格子功能
        /// <summary>
        /// ★ 改过的格子：`格 id → 新的 landType`（写进棋盘文件的 `nodes[i].diy.landType`）。
        ///
        /// ★ 为什么不动 `nodes[i].landType`：那是**官方原始事实**。
        ///   改动一律落到同节点的 `diy.landType` 上 ⇒「还原这一格」永远做得到
        ///   （删掉那个子键就回到官方）。
        /// ★ 为什么不像 `diy.starts` 那样直接读 json：棋盘文件可能有上万行，
        ///   每次画一格都去 parse 一遍太贵。所以这里做一份内存账，
        ///   `LoadBoardOverrides()` 在文档打开时灌一次。
        /// </summary>
        private Dictionary<int, int> _nodeLand;
        private bool _nodeLandRead;

        /// <summary>这一格被改成了什么（没改回 0）。</summary>
        public int NodeLandOverride(int nodeId)
        {
            LoadBoardOverrides();
            int v;
            return _nodeLand.TryGetValue(nodeId, out v) ? v : 0;
        }

        /// <summary>改过的格子有几格（0 格 = 这块棋盘还是官方的）。</summary>
        public int NodeOverrideCount
        {
            get { LoadBoardOverrides(); return _nodeLand.Count; }
        }

        /// <summary>改过的格子账（副本；`格 id → 新 landType`）。</summary>
        public Dictionary<int, int> NodeOverrides
        {
            get { LoadBoardOverrides(); return new Dictionary<int, int>(_nodeLand); }
        }

        /// <summary>把内存里"改过的格子"灌一遍（只读棋盘 json；读不到就空账）。</summary>
        private void LoadBoardOverrides()
        {
            if (_nodeLandRead) return;
            _nodeLandRead = true;
            _nodeLand = new Dictionary<int, int>();

            string src = ExistingBoardPath();
            if (src.Length == 0) return;
            try
            {
                JNode root = DiyJson.Parse(File.ReadAllText(src, System.Text.Encoding.UTF8));
                JNode nodes = DiyJson.Member(root, "nodes");
                if (nodes == null || nodes.Kind != JKind.Array) return;
                foreach (JNode item in nodes.Items)
                {
                    if (item.Kind != JKind.Object) continue;
                    JNode d = DiyJson.Member(item, "diy");
                    if (d == null || d.Kind != JKind.Object) continue;
                    int lt = (int)DiyJson.IntOf(DiyJson.Member(d, "landType"), 0);
                    int id = (int)DiyJson.IntOf(DiyJson.Member(item, "id"), -1);
                    if (id >= 0 && lt > 0) _nodeLand[id] = lt;
                }
            }
            catch (Exception) { }
        }

        public void InvalidateBoardOverrides() { _nodeLandRead = false; _nodeLand = null; }

        /// <summary>
        /// ★★ 2026-10-09：**这一份文档自己的**棋盘文件（`&lt;关卡名&gt;.board.json`；空 = 还没存过 / 新建）。
        ///
        /// 为什么不能只看静态的 `BoardStore.DiyBoardPath`（用户实报"新建了新的关卡但是棋盘的摆布
        /// 还是之前旧的"，就是这一类）：那个字段是**全局唯一**的，"最后存过的那份文档"会把它据为己有；
        /// 于是另一份文档只要触发一次"重读改格账"（`SaveDiy` 末尾的 `RebaseAfterSave` →
        /// `ResolveDiyBoard` 会 `InvalidateBoardOverrides`），就会把**别人**的 `.board.json`
        /// 当成自己的账本读进来。实测（自测 ⑯k 现场）：
        ///   A 改 格3→19 存盘、B 改 格7→12 存盘，**再存一次 A** ⇒ A 的 board 变成 格7→12，
        ///   A 自己那一格反而没了（因为 A 的账是从 B 的 board 重读的、源也是 B 的 board）。
        /// ⇒ 每份文档认领自己的那一份；没认领到（新建 / 还没存过）就只回落「棋盘缓存」里的官方画板。
        /// </summary>
        private string _ownBoardPath = "";

        /// <summary>现在该照着哪份棋盘文件算（**这份文档自己的**优先，否则「棋盘缓存」里的官方那份）。
        ///   ★★ 返回的可能是**内置那份的标记路径**（磁盘上没有时）—— 见 `BoardCache.FindPath`。</summary>
        private string ExistingBoardPath()
        {
            if (!string.IsNullOrEmpty(_ownBoardPath) && File.Exists(_ownBoardPath)) return _ownBoardPath;
            if (BoardSuffix.Length > 0) return BoardCache.FindPath(BoardSuffix);
            return "";
        }

        /// <summary>
        /// ★ 把某一格的功能换成 `landType`（0 = 还原成官方）。
        ///   返回 1 = 真改了；0 = 本来就这样（没动）；-1 = 现在改不了（没棋盘底版）。
        /// </summary>
        public int SetNodeLand(int nodeId, int landType)
        {
            if (Board == null || !Board.Ok) return -1;
            BoardNode n = Board.Get(nodeId);
            if (n == null) return -1;
            if (landType < 0 || landType > 27) return -1;      // 只有 0..27 是登记过的格子类型
            if (landType == n.EffectiveLand) landType = 0;     // 选回了官方值 ⇒ 就当还原

            LoadBoardOverrides();
            if (landType == 0)
            {
                if (!_nodeLand.ContainsKey(nodeId)) return 0;
                _nodeLand.Remove(nodeId);
            }
            else
            {
                int had;
                if (_nodeLand.TryGetValue(nodeId, out had) && had == landType) return 0;
                _nodeLand[nodeId] = landType;
            }

            n.DiyLandType = landType;          // 内存里立刻生效（不用重读文件）
            BoardStore.ClearCache();           // ★ 让所有走 BoardStore 的地方重新读
            return 1;
        }

        /// <summary>棋盘上"改过功能"的格子清单（给人看：格 id → 类型名）。</summary>
        public List<string> NodeOverrideLines()
        {
            var outLines = new List<string>();
            LoadBoardOverrides();
            var keys = new List<int>(_nodeLand.Keys);
            keys.Sort();
            foreach (int id in keys)
            {
                BoardNode n = Board != null && Board.Ok ? Board.Get(id) : null;
                outLines.Add("格 " + id + "：" + (n != null ? Land.Name(n.EffectiveLand) : "?")
                           + " → " + Land.Name(_nodeLand[id]));
            }
            return outLines;
        }

        /// <summary>
        /// ★ 起点校验：**必须恰好 4 个**、**编号不重复**、**格子得真在棋盘上**。
        ///   返回的是给人看的一行行说明（第 1 个元素是结论）。
        /// </summary>
        public List<string> StartsFindings()
        {
            var outLines = new List<string>();
            List<int> list = EffectiveStarts();

            if (Board == null || !Board.Ok)
                return outLines;   // 没棋盘就不下结论（另有"棋盘画不出来"的提示）

            if (list.Count == 0)
            {
                outLines.Add("★ 这块棋盘上一个起点都没有");
                outLines.Add("四个玩家没地方出生。右键格子 →「设为起点1」起，指定 4 个。");
                return outLines;
            }

            // 重复 / 越界 / 空格
            var dup = new List<int>();
            var vague = new List<int>();
            var gone = new List<int>();
            for (int k = 0; k < list.Count; k++)
            {
                int id = list[k];
                if (id < 0) { vague.Add(k + 1); continue; }
                for (int j = 0; j < k; j++) if (list[j] == id) dup.Add(k + 1);
                if (Board.Get(id) == null) gone.Add(k + 1);
            }

            var head = new List<string>();
            if (list.Count == 4) head.Add("起点 " + list.Count + " 个 ✓（要求恰好 4 个）");
            else head.Add("起点 " + list.Count + " 个 ✗（要求恰好 4 个）");

            outLines.Add(string.Join(" · ", head.ToArray()));

            if (dup.Count == 0 && vague.Count == 0 && gone.Count == 0 && list.Count == 4)
            {
                var where = new List<string>();
                for (int k = 0; k < list.Count; k++) where.Add("起点" + (k + 1) + "=格" + list[k]);
                outLines.Add(where.Count + " 个都在：" + string.Join("，", where.ToArray()));
                return outLines;
            }

            // ★ 这几行必须仍以 `★` 开头：`CountBadIn` 就是按这个数"报了几条问题"
            if (list.Count < 4)
                outLines.Add("★ 起点不够 ✗：只有 " + list.Count + " 个，要求恰好 4 个 —— 缺的那几号右键格子补上。");
            else if (list.Count > 4)
                outLines.Add("★ 起点多了 ✗：有 " + list.Count + " 个，要求恰好 4 个 —— 一局只有四个座位。");

            if (dup.Count > 0)
                outLines.Add("★ 编号重复 ✗：起点 " + string.Join("、", ToIntStrings(dup))
                           + " 和前面的号指到了同一格 —— 一个格子只能是一个玩家的起点。");
            if (vague.Count > 0)
                outLines.Add("★ 这几号还没指定 ✗：起点 " + string.Join("、", ToIntStrings(vague))
                           + " 是空的。右键某个格子指定它。");
            if (gone.Count > 0)
                outLines.Add("★ 这几号的格子不在棋盘上 ✗：起点 " + string.Join("、", ToIntStrings(gone))
                           + " —— 可能是换了底版 / 那份棋盘缓存没了。");
            return outLines;
        }

        private static string[] ToIntStrings(List<int> xs)
        {
            var a = new string[xs.Count];
            for (int k = 0; k < xs.Count; k++) a[k] = xs[k].ToString(CultureInfo.InvariantCulture);
            return a;
        }

        /// <summary>★ 存成 &lt;游戏根&gt;\DIY关卡\&lt;名字&gt;.json（+ 同名 .board.json）。</summary>
        public DiySaveResult SaveDiy(string name)
        {
            var result = new DiySaveResult();
            string dir = GameLocator.DiyDir;
            if (string.IsNullOrEmpty(dir))
                throw new InvalidOperationException(
                    "找不到游戏根目录 ⇒ 不知道往哪存。\n\n先在第①页点「指定游戏目录…」。");

            string clean = SanitizeName(name);
            if (clean.Length == 0) throw new InvalidOperationException("关卡名字不能是空的。");

            result.DiyDir = dir;
            result.LevelPath = System.IO.Path.Combine(dir, clean + ".json");
            result.BoardPath = System.IO.Path.Combine(dir, clean + ".board.json");

            // ① 先把 diy 块对齐成"要存的那个名字"
            DiyName = clean;
            JNode diy = DiyJson.EnsureMember(Root, "diy");
            DiyJson.SetString(DiyJson.EnsureMember(diy, "name"), clean);
            DiyJson.SetString(DiyJson.EnsureMember(diy, "board"), System.IO.Path.GetFileName(result.BoardPath));
            if (BoardSuffix.Length > 0) DiyJson.SetString(DiyJson.EnsureMember(diy, "base_scene"), BoardSuffix);
            if (BaseLevel.Length > 0) DiyJson.SetString(DiyJson.EnsureMember(diy, "base_level"), BaseLevel);

            Directory.CreateDirectory(dir);

            // ② 棋盘文件先写（关卡 json 里指着它；先有棋盘再有关卡更稳妥）
            //   ★ 源文件优先用**这份文档自己的**那份（`_ownBoardPath`）—— 绝不拿静态
            //     `BoardStore.DiyBoardPath`：那可能是**别的文档**刚存下去的那一份（见 `_ownBoardPath` 的注释）。
            //   ★★ 底版可能是**exe 内置的那一份**（磁盘上没有「棋盘缓存」时）：那种情况
            //     `boardSrc` 是空的（内置没有磁盘路径），但 `BoardBytes` 照样算得出字节 ⇒
            //     照旧往用户自己的 `DIY关卡\<名字>.board.json` 写一份**真实的**底版快照。
            string boardSrc = !string.IsNullOrEmpty(_ownBoardPath) && File.Exists(_ownBoardPath) ? _ownBoardPath : "";
            if (boardSrc.Length == 0 && BoardSuffix.Length > 0)
            {
                string found = BoardCache.FindPath(BoardSuffix);
                if (found.Length > 0 && File.Exists(found)) boardSrc = found;      // 内置那份没有磁盘路径 ⇒ 留空
            }

            if ((boardSrc.Length > 0 && File.Exists(boardSrc)) || BoardSuffix.Length > 0)
            {
                byte[] boardBytes = BoardBytes(boardSrc);
                result.BoardSource = boardSrc.Length > 0 ? boardSrc : "(exe 内置的 " + BoardSuffix + ".json)";
                result.BoardBytes = boardBytes.Length;
                result.BoardSha = ExportService.Sha256Of(boardBytes);
                result.BoardNodesChanged = _boardChangedCount;
                string boardFull = System.IO.Path.GetFullPath(result.BoardPath);
                bool same = boardSrc.Length > 0 && string.Equals(System.IO.Path.GetFullPath(boardSrc), boardFull,
                    StringComparison.OrdinalIgnoreCase);

                if (boardBytes.Length == 0)
                {
                    result.BoardNote = "「棋盘缓存」里没有 " + BoardSuffix + ".json（内置那份也没读到）⇒ 只写关卡 json";
                }
                else if (same)
                {
                    ExportResult br = ExportService.Overwrite(result.BoardPath, boardBytes, "DIY棋盘");
                    result.BoardBackup = br.BackupPath;
                    result.BoardNote = "棋盘文件原地更新（覆盖前先备份）";
                }
                else if (!File.Exists(boardFull))
                {
                    ExportService.WriteAtomic(result.BoardPath, boardBytes);
                    result.BoardNote = boardSrc.Length > 0
                        ? "棋盘文件是从「" + System.IO.Path.GetFileName(
                            System.IO.Path.GetDirectoryName(boardSrc)) + "」复制过来的"
                        : "棋盘文件是从 exe 内置的 " + BoardSuffix + ".json 落下来的";
                }
                else if (ExportService.Sha256OfFile(boardFull) != result.BoardSha)
                {
                    ExportResult br = ExportService.Overwrite(result.BoardPath, boardBytes, "DIY棋盘");
                    result.BoardBackup = br.BackupPath;
                    result.BoardNote = "棋盘文件已更新（覆盖前先备份）";
                }
                else
                {
                    result.BoardNote = "棋盘文件内容没变（不动它）";
                }
                BoardStore.DiyBoardPath = result.BoardPath;   // 静态那份保持同步（别的调用点还在看它）
                _ownBoardPath = result.BoardPath;             // ★ 这份文档认领自己这一份
                result.BoardWritten = File.Exists(result.BoardPath);
            }
            else
            {
                result.BoardNote = BoardSuffix.Length == 0
                    ? "认不出底版画板号 ⇒ 不写棋盘文件"
                    : "「棋盘缓存」里没有 " + BoardSuffix + ".json ⇒ 只写关卡 json";
            }

            // ③ 关卡 json：覆盖前备份 + 原子替换 + sha256 记账
            byte[] bytes = Bytes();
            result.Sha256 = ExportService.Sha256Of(bytes);
            result.Bytes = bytes.Length;
            string levelFull = System.IO.Path.GetFullPath(result.LevelPath);
            if (File.Exists(levelFull))
            {
                ExportResult r = ExportService.Overwrite(result.LevelPath, bytes, "DIY");
                result.Overwrote = true;
                result.BackupPath = r.BackupPath;
                result.SourceSha256 = r.SourceSha256;
                result.BackupSha256 = r.BackupSha256;
                result.Sha256 = r.NewSha256;
                result.Bytes = r.NewLength;
                result.ByteIdentical = r.ByteIdentical;
            }
            else
            {
                ExportService.WriteAtomic(result.LevelPath, bytes);
            }

            // ④ 基线换成刚写出去的内容（脏标记清掉、Raw 重新对齐）
            RebaseAfterSave(result.LevelPath);
            return result;
        }

        /// <summary>
        /// 棋盘文件要写出去的字节：照官方形状（**1 空格缩进 + 沿用原文换行**）。
        ///
        /// ★ 三件事都在这里做（前两件是 2026-10-03 修的**真 bug**）：
        ///
        ///   ① ★ **顶层的 `diy` 块要"合并"、不是"清空重写"**。
        ///      旧版是 `diy.Members.Clear()` 然后只写 version / base_board / note ——
        ///      结果 `diy.starts`、`diy.progress_limit` 这些**一存就被抹掉**。
        ///      现在只新建缺的那三个键，别的原样留着。
        ///
        ///   ② ★ **改过的格子写 `nodes[i].diy.landType`**（内存账见 `_nodeLand`）。
        ///      `nodes[i].landType` 是**官方原始事实**，一个字节都不动
        ///      ⇒ 所以"还原这一格"永远做得到（删掉那个 `diy` 子键即可）。
        ///      顺便把上一次存留下来的 `diy.landType`（现在账上没有的）清掉，
        ///      不然"还原"会还原不掉。
        ///
        ///   ③ 底版优先用**已经存过的那一份**（`<游戏根>\DIY关卡\&lt;名字&gt;.board.json`），
        ///      没有才拿只读的「棋盘缓存」当底 —— 否则第二次保存会把第一次的
        ///      格子改动覆盖回官方值。
        ///   ★★ 底版还可能来自 **exe 内置的那一份**（磁盘上没有「棋盘缓存」时）：
        ///      那种情况没有磁盘路径，于是从资源里取原文 —— 判据和磁盘那条**完全同一条**。
        /// </summary>
        private byte[] BoardBytes(string cachedSrc)
        {
            string src = ExistingBoardPath();
            if (src.Length == 0) src = cachedSrc;

            // ★★ 磁盘优先、内置兜底（`BoardCache.TryReadSource`）：传进去的可能是
            //    ① 一份真的磁盘路径（DIY 自带 / 用户指定 / 磁盘缓存目录）
            //    ② 内置那份的标记串（磁盘上没有）⇒ 从 exe 资源里取
            string text;
            string diskPath;
            string source = File.Exists(src) ? src : "";
            string suffix = BoardSuffix.Length > 0 ? BoardSuffix : BoardCache.Stem(src);
            if (!BoardCache.TryReadSource(source, suffix, out text, out diskPath))
            {
                // 到这儿说明磁盘和内置都没有这份底版 ⇒ 不写棋盘文件（调用方按 BoardBytes 为空处理）
                _boardChangedCount = 0;
                return new byte[0];
            }

            JNode root = DiyJson.Parse(text);
            string eol = text.IndexOf("\r\n", StringComparison.Ordinal) >= 0 ? "\r\n" : "\n";

            // ---- ① 顶层 diy：只补缺的，别清空
            JNode diy = DiyJson.EnsureMember(root, "diy");
            DiyJson.SetKind(diy, JKind.Object);
            DiyJson.SetInt(DiyJson.EnsureMember(diy, "version"), 1);
            DiyJson.SetString(DiyJson.EnsureMember(diy, "base_board"),
                (BoardSuffix.Length > 0 ? BoardSuffix : "?") + ".json");
            DiyJson.SetString(DiyJson.EnsureMember(diy, "note"),
                "底版快照：官方格子类型的原样副本；改了格子的功能才写 nodes[].diy.landType");

            // ---- ② 逐格把"改过的"写进 nodes[i].diy.landType（官方 landType 不动）
            LoadBoardOverrides();
            int changed = 0;
            JNode nodes = DiyJson.Member(root, "nodes");
            if (nodes != null && nodes.Kind == JKind.Array)
            {
                foreach (JNode item in nodes.Items)
                {
                    if (item.Kind != JKind.Object) continue;
                    int id = (int)DiyJson.IntOf(DiyJson.Member(item, "id"), -1);
                    if (id < 0) continue;

                    int want;
                    bool has = _nodeLand.TryGetValue(id, out want);
                    JNode nd = DiyJson.Member(item, "diy");

                    if (has)
                    {
                        JNode nd0 = DiyJson.EnsureMember(item, "diy");
                        DiyJson.SetKind(nd0, JKind.Object);
                        DiyJson.SetInt(DiyJson.EnsureMember(nd0, "landType"), want);
                        changed++;
                    }
                    else if (nd != null && nd.Kind == JKind.Object
                             && DiyJson.Member(nd, "landType") != null)
                    {
                        // ★ 上一版存下来的覆盖，现在已经还原了 ⇒ 把那个子键摘掉
                        DiyJson.RemoveMember(nd, "landType");
                        if (nd.Count == 0) DiyJson.RemoveMember(item, "diy");
                    }
                }
            }
            _boardChangedCount = changed;

            string body = DiyJson.Write(root, " ", eol);
            return new UTF8Encoding(false).GetBytes(body);
        }

        /// <summary>上一次 `BoardBytes()` 实际写出去几格（给保存回执用）。</summary>
        private int _boardChangedCount;

        /// <summary>
        /// 把"要写出去的棋盘文件字节"拿出来 —— 给自测用（自测要能直接验这份字节，
        /// 而不是绕一大圈去 `SaveDiy` 里落到真的沙盒目录）。
        /// 走的是和 `SaveDiy` **完全相同**的那条 `BoardBytes` 路径。
        /// </summary>
        public byte[] BoardWriteForTest()
        {
            string src = ExistingBoardPath();
            if (src.Length == 0 && BoardSuffix.Length > 0) src = BoardCache.FindPath(BoardSuffix);
            // ★ 内置那份的 src 是标记串（磁盘上没有这个文件）⇒ 交给 `BoardBytes` 从资源里取。
            if (src.Length == 0) return new byte[0];
            return BoardBytes(src);
        }

        public int MapId
        {
            get { return (int)DiyJson.IntOf(DiyJson.Member(Root, "map"), 0); }
        }

        public int Difficulty
        {
            get { return (int)DiyJson.IntOf(DiyJson.Member(Root, "difficulty"), -1); }
        }

        public void LoadBoard(string boardDirHint)
        {
            // ★ 期望的画板号：Map.bin 算出来的那个（82016 -> "023"）。
            //   拿它当判据，才不会把"别的图的棋盘"当成这张图的。
            if (string.IsNullOrEmpty(BoardSuffix))
            {
                MapTemplate t = GameTables.Map(MapId);
                if (t != null) BoardSuffix = t.SceneSuffix;
            }
            BoardStore.ExpectedSuffix = BoardSuffix;
            // ★★ 2026-10-09：棋盘归属**只认这份文档自己的那一份**（`_ownBoardPath`）——
            //   顺手把静态那一份对齐过来，`BoardStore.LoadCore` 的 ① 分支才不会拿到
            //   "上一份文档留下的路径"（那正是"新建了新的关卡但是棋盘的摆布还是之前旧的"）。
            //   空 = 这份文档还没有自己的棋盘（新建 / 没存过）⇒ 只回落「棋盘缓存」里的官方画板。
            BoardStore.DiyBoardPath = _ownBoardPath;
            Board = BoardStore.Load(MapId, boardDirHint);
        }

        /// <summary>当前树状态的字节（没改过就是原始字节）。</summary>
        public byte[] Bytes()
        {
            return ExportService.Encode(Root, Loaded.Eol, Loaded.HasBom, Loaded.Bytes, Changed);
        }

        /// <summary>另存为 / 覆盖。overwrite=true 时走"先备份再原子替换"。</summary>
        public ExportResult Export(string target, bool overwrite, string tag)
        {
            byte[] bytes = Bytes();
            if (!overwrite)
            {
                ExportService.WriteAtomic(target, bytes);
                var r = new ExportResult();
                r.TargetPath = target;
                r.NewSha256 = ExportService.Sha256Of(bytes);
                r.NewLength = bytes.Length;
                r.ByteIdentical = string.Equals(r.NewSha256,
                    ExportService.Sha256OfFile(Path), StringComparison.OrdinalIgnoreCase);
                return r;
            }
            return ExportService.Overwrite(target, bytes, tag);
        }

        /// <summary>保存成功后把"基线"换成刚写出去的内容（脏标记清掉，Raw 重新对齐）。</summary>
        public void RebaseAfterSave(string target)
        {
            Loaded = ExportService.Load(target);
            Root = DiyJson.Parse(Loaded.Text);
            Path = Loaded.Path;
            ReadOnlyDemo = Path.IndexOf(@"\4-服务端\", StringComparison.OrdinalIgnoreCase) >= 0;
            ResolveDiyBoard();
        }

        // ============================================================ 字段实例
        /// <summary>把注册表里的每一项，对应到树里真实存在的节点上。</summary>
        public List<FieldInstance> Fields()
        {
            var outList = new List<FieldInstance>();
            if (Root == null) return outList;

            foreach (FieldSpec spec in LevelSchema.All)
            {
                var hits = new List<FieldInstance>();
                string[] parts = spec.Path.Split('.');
                Walk(Root, parts, 0, "", spec, hits);
                for (int k = 0; k < hits.Count; k++) hits[k].Occurrence = k;
                outList.AddRange(hits);
            }
            outList.Sort(delegate (FieldInstance a, FieldInstance b)
            {
                int c = string.CompareOrdinal(a.Spec.Path, b.Spec.Path);
                if (c != 0) return c;
                return a.Occurrence.CompareTo(b.Occurrence);
            });
            return outList;
        }

        private static readonly JNode[] NoNodes = new JNode[0];

        private static void Walk(JNode node, string[] parts, int index, string trail, FieldSpec spec, List<FieldInstance> into)
        {
            if (node == null || index >= parts.Length) return;
            string head = parts[index];

            if (head == "*")
            {
                // 任意键的对象（progress_events）
                if (node.Kind != JKind.Object) return;
                foreach (JMember m in node.Members)
                {
                    JNode v = m.Value;
                    if (v.Kind == JKind.Object) { }
                    Walk(v, parts, index + 1, trail + "[\"" + m.Key + "\"]", spec, into);
                    if (index + 1 >= parts.Length && v != null)
                        into.Add(Make(spec, v, trail + "[\"" + m.Key + "\"]", null, m.Key + " 号事件"));
                }
                return;
            }

            if (head.EndsWith("[]", StringComparison.Ordinal))
            {
                string key = head.Substring(0, head.Length - 2);
                if (key.Length == 0)
                {
                    if (node.Kind != JKind.Array) return;
                    for (int i = 0; i < node.Count; i++)
                        AddOrDescend(node.Items[i], parts, index + 1, trail + "[" + i + "]", spec, into);
                    return;
                }
                JNode arr = DiyJson.Member(node, key);
                if (arr == null || arr.Kind != JKind.Array) return;
                for (int i = 0; i < arr.Count; i++)
                    AddOrDescend(arr.Items[i], parts, index + 1, trail + (trail.Length > 0 ? "." : "") + key + "[" + i + "]", spec, into);
                return;
            }

            JNode child = DiyJson.Member(node, head);
            if (child == null) return;
            if (index == parts.Length - 1)
            {
                // 顶层键自己也算一个"字段"（例如 rounds）—— 但只对顶层用
                into.Add(Make(spec, child, trail.Length == 0 ? head : trail + "." + head, null, null));
                return;
            }
            Walk(child, parts, index + 1, trail.Length == 0 ? head : trail + "." + head, spec, into);
        }

        private static void AddOrDescend(JNode row, string[] parts, int index, string trail, FieldSpec spec, List<FieldInstance> into)
        {
            if (row == null) return;
            if (index >= parts.Length) return;
            JNode child = DiyJson.Member(row, parts[index]);
            if (child == null) return;
            if (index == parts.Length - 1)
            {
                into.Add(Make(spec, child, trail + "." + parts[index], row, null));
                return;
            }
            Walk(child, parts, index + 1, trail + "." + parts[index], spec, into);
        }

        private static FieldInstance Make(FieldSpec spec, JNode node, string where, JNode row, string owner)
        {
            var f = new FieldInstance();
            f.Spec = spec;
            f.Node = node;
            f.Where = where;
            f.Row = row;
            f.Owner = owner;
            return f;
        }

        // ============================================================ 关键数值（总览页）
        public sealed class Metric
        {
            public string Label;
            public string Value;
            public string Note;
            public int Tone;   // 0 普通 1 好 2 警告 3 坏
        }

        public List<Metric> Metrics()
        {
            var list = new List<Metric>();
            if (Root == null) return list;

            JNode rounds = DiyJson.Member(Root, "rounds");
            long r = DiyJson.IntOf(rounds, 0);
            list.Add(new Metric
            {
                Label = "总回合数",
                Value = rounds == null ? "不写（不按回合判负）" : (r > 0 ? r.ToString(CultureInfo.InvariantCulture) : r + "（无效，会被丢掉）"),
                Note = "",
                Tone = rounds == null ? 0 : (r > 0 ? 1 : 2),
            });

            JNode every = DiyJson.Member(DiyJson.Member(Root, "round_award"), "every");
            long e = DiyJson.IntOf(every, 0);
            list.Add(new Metric
            {
                Label = "轮次奖励",
                Value = e > 0
                    ? "每 " + e + " 回合 → 星币 +" + DiyJson.IntOf(DiyJson.Member(DiyJson.Member(Root, "round_award"), "gold"), 0)
                      + "，手牌 +" + DiyJson.IntOf(DiyJson.Member(DiyJson.Member(Root, "round_award"), "cards"), 0)
                    : "不写（回落 3 回合 / 星币 6 / 手牌 1）",
                Note = "",
                Tone = e > 0 ? 1 : 0,
            });

            JNode st = DiyJson.Member(Root, "start_monsters");
            if (st != null && st.Kind == JKind.Array && st.Count > 0)
            {
                int bad = 0;
                foreach (JNode row in st.Items)
                {
                    long t = DiyJson.IntOf(DiyJson.Member(row, "table"), 0);
                    if (t <= 0) bad++;
                }
                list.Add(new Metric
                {
                    Label = "开局怪",
                    Value = st.Count + " 只" + (bad > 0 ? "（其中 " + bad + " 只没写 table ⇒ 会被丢掉）" : ""),
                    Note = "",
                    Tone = bad > 0 ? 3 : 1,
                });
            }
            else
            {
                list.Add(new Metric { Label = "开局怪", Value = "空", Note = "不写 = 回落到代码默认", Tone = 0 });
            }

            JNode pe = DiyJson.Member(Root, "progress_events");
            if (pe != null && pe.Kind == JKind.Object && pe.Count > 0)
            {
                int spawns = 0;
                foreach (JMember m in pe.Members)
                {
                    JNode sp = DiyJson.Member(m.Value, "spawn");
                    if (sp != null && sp.Kind == JKind.Array) spawns += sp.Count;
                }
                list.Add(new Metric
                {
                    Label = "进度事件",
                    Value = pe.Count + " 条，共 " + spawns + " 次刷怪",
                    Note = "",
                    Tone = 1,
                });
            }
            else if (pe != null && pe.Kind != JKind.Object)
            {
                list.Add(new Metric
                {
                    Label = "进度事件",
                    Value = "写成了「" + (pe.Kind == JKind.Array ? "数组" : "别的形状") + "」⇒ 整块会被丢掉",
                    Note = "要的是「事件 id → 效果」的对象",
                    Tone = 3,
                });
            }
            else
            {
                list.Add(new Metric { Label = "进度事件", Value = "空", Note = "不写 = 不播任何事件", Tone = 0 });
            }

            JNode v = DiyJson.Member(Root, "victory");
            string vk = null;
            JNode kind = DiyJson.Member(v, "kind");
            if (kind != null && kind.Kind == JKind.String) vk = kind.Str;
            JNode targets = DiyJson.Member(v, "targets");
            string tlist = "";
            if (targets != null && targets.Kind == JKind.Array)
            {
                foreach (JNode t in targets.Items) tlist += (tlist.Length > 0 ? "、" : "") + DiyJson.Brief(t, 12);
            }
            list.Add(new Metric
            {
                Label = "胜利条件",
                Value = string.IsNullOrEmpty(vk) ? "没写 / 空 ⇒ 没有胜利条件" : vk + " → 打死 " + (tlist.Length > 0 ? tlist : "（没列目标）"),
                Note = "按怪物表 id，不是格子号",
                Tone = string.IsNullOrEmpty(vk) ? 3 : (tlist.Length > 0 ? 1 : 2),
            });

            // ★ 进度：**写了才显示**。新建的 DIY 关卡这里是空的（官方值不预填），
            //   所以要说"空"，别拿 1/1/1 顶数 —— 那会让空文件看起来像填过。
            JNode prog = DiyJson.Member(DiyJson.Member(Root, "progress"), "start");
            JNode progR = DiyJson.Member(DiyJson.Member(Root, "progress"), "per_round");
            JNode progD = DiyJson.Member(DiyJson.Member(Root, "progress"), "per_death");
            long mdCount = Count(DiyJson.Member(DiyJson.Member(Root, "progress"), "mission_delta"));
            if (prog == null && progR == null && progD == null && mdCount == 0)
            {
                list.Add(new Metric
                {
                    Label = "进度条",
                    Value = "空",
                    Note = "要去「进度 / 事件」页填",
                    Tone = 0,
                });
            }
            else
            {
                list.Add(new Metric
                {
                    Label = "进度条",
                    Value = "开局 " + (prog == null ? "（空）" : DiyJson.Brief(prog, 8))
                        + "，每回合 +" + (progR == null ? "（空）" : DiyJson.Brief(progR, 8))
                        + "，每倒地 +" + (progD == null ? "（空）" : DiyJson.Brief(progD, 8))
                        + "，任务覆盖 " + mdCount + " 条",
                    Note = "上限来自难度行的 ProgressLimit（只读）",
                    Tone = 1,
                });
            }

            list.Add(new Metric
            {
                Label = "真凶机制",
                Value = DiyJson.Member(Root, "mastermind") == null ? "不写（按难度行判是否开）" : "写了（整块原样收下）",
                Note = "",
                Tone = 0,
            });

            list.Add(new Metric
            {
                Label = "结算奖励",
                Value = Count(DiyJson.Member(DiyJson.Member(Root, "settlement"), "awards")) + " 项",
                Note = "",
                Tone = 0,
            });

            list.Add(new Metric
            {
                Label = "棋盘图",
                Value = Board != null && Board.Ok ? Board.Nodes.Count + " 格（" + System.IO.Path.GetFileName(Board.Path) + "）" : "没有",
                Note = Board == null ? "" : Board.Note,
                Tone = Board != null && Board.Ok ? 1 : 2,
            });

            return list;
        }

        private static int Count(JNode n)
        {
            return n != null && n.IsContainer ? n.Count : 0;
        }
    }
}
