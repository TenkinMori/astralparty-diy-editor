using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace DiyEditor
{
    /// <summary>一份"成品模板"（用户 2026-10-08：「由咱们给提供已经完成的关卡模板」）。</summary>
    public sealed class LevelTemplate
    {
        public string Path = "";
        public string FileName = "";
        public string Name = "";
        public int MapId;
        public int Difficulty;
        public string SceneSuffix = "";
        public int Events;          // 模板里带几条进度事件
        public int StartMonsters;   // 模板里带几只开局怪
        public string Note = "";

        public string Title { get { return Name.Length > 0 ? Name : FileName; } }

        public string Sub
        {
            get
            {
                var parts = new List<string>();
                if (MapId > 0) parts.Add("地图 " + MapId.ToString(CultureInfo.InvariantCulture));
                if (SceneSuffix.Length > 0) parts.Add("画板 " + SceneSuffix);
                parts.Add("事件 " + Events.ToString(CultureInfo.InvariantCulture));
                parts.Add("开局怪 " + StartMonsters.ToString(CultureInfo.InvariantCulture));
                return string.Join(" · ", parts.ToArray());
            }
        }
    }

    /// <summary>
    /// ★★ 成品模板目录（2026-10-08 大改）。
    ///
    /// 位置：**`&lt;游戏根&gt;\DIY关卡\_模板\*.json`** —— 和玩家自己存的 DIY 关卡同一个
    /// 目录下的**子目录**，这样：
    ///   · 编辑器第①页列的是"我们提供的模板"，玩家自建的那些不会被混进来；
    ///   · 客户端那边"扫 `DIY关卡\*.json` 当关卡列表"也不会把模板列成可玩的关卡（只扫文件、不进子目录）。
    ///
    /// ★ 模板就是**一份完整的关卡 json**（服务端真读的那 10 个顶层键都在），
    ///   新建时整份复制、只换身份键 —— 见 `LevelDoc.CreateFromOwnTemplate`。
    /// ★ 目录空着就从官方 levels 拷一份进来当种子（"咱们提供"的落点是这个目录，不是仓库）。
    /// </summary>
    public static class TemplateCatalog
    {
        public const string DirName = "_模板";

        /// <summary>模板目录（`&lt;游戏根&gt;\DIY关卡\_模板`）。算不出游戏根就回空串。</summary>
        public static string Dir
        {
            get
            {
                string diy = GameLocator.DiyDir;
                if (string.IsNullOrEmpty(diy)) return "";
                return System.IO.Path.Combine(diy, DirName);
            }
        }

        /// <summary>目录里现有的模板（按文件名排序；目录不在就回空表）。</summary>
        public static List<LevelTemplate> List()
        {
            var outList = new List<LevelTemplate>();
            string dir = Dir;
            if (dir.Length == 0 || !Directory.Exists(dir)) return outList;
            string[] files;
            try { files = Directory.GetFiles(dir, "*.json"); }
            catch (Exception) { return outList; }
            Array.Sort(files, StringComparer.OrdinalIgnoreCase);
            foreach (string f in files)
            {
                // 模板自己可能带一份同名 board.json —— 那不是模板
                if (f.EndsWith(".board.json", StringComparison.OrdinalIgnoreCase)) continue;
                LevelTemplate t = Read(f);
                if (t != null) outList.Add(t);
            }
            return outList;
        }

        /// <summary>读一份模板的"身份信息"（不整份解析成编辑器文档，列表页要快）。</summary>
        public static LevelTemplate Read(string path)
        {
            try
            {
                var t = new LevelTemplate();
                t.Path = path;
                t.FileName = System.IO.Path.GetFileName(path);
                ExportService.Loaded text = ExportService.Load(path);
                JNode root = DiyJson.Parse(text.Text);
                t.MapId = (int)DiyJson.IntOf(DiyJson.Member(root, "map"), 0);
                t.Difficulty = (int)DiyJson.IntOf(DiyJson.Member(root, "difficulty"), -1);

                JNode diy = DiyJson.Member(root, "diy");
                JNode nm = DiyJson.Member(diy, "name");
                if (nm != null && nm.Kind == JKind.String) t.Name = nm.Str;
                // ★ 没写名字（从官方关卡拷来的种子就是这样）⇒ 拿地图表里的官方名当显示名
                if (t.Name.Length == 0 && t.MapId > 0)
                {
                    MapTemplate m = GameTables.Map(t.MapId);
                    if (m != null && !string.IsNullOrEmpty(m.Name))
                        t.Name = m.Name + (t.Difficulty >= 0 ? ("（" + t.Difficulty.ToString(CultureInfo.InvariantCulture) + " 档）") : "");
                }
                JNode bs = DiyJson.Member(diy, "base_scene");
                if (bs != null && bs.Kind == JKind.String) t.SceneSuffix = bs.Str;
                JNode nt = DiyJson.Member(diy, "note");
                if (nt != null && nt.Kind == JKind.String) t.Note = nt.Str;

                JNode pe = DiyJson.Member(root, "progress_events");
                if (pe != null && pe.Kind == JKind.Object && pe.Members != null) t.Events = pe.Members.Count;
                JNode sm = DiyJson.Member(root, "start_monsters");
                if (sm != null && sm.Kind == JKind.Array && sm.Items != null) t.StartMonsters = sm.Items.Count;
                return t;
            }
            catch (Exception)
            {
                return null;   // 读不动的就当没有这一份（列表页别因为一个坏文件整页空掉）
            }
        }

        /// <summary>
        /// ★★ 给"**有官方棋盘、但本机没有现成模板**"的图补一份**空白玩法模板**
        ///   （用户 2026-10-10：「所有有官方棋盘的图都能新建关卡」—— 没有官方玩法数据的那几张
        ///   就是"**空白玩法 + 官方棋盘**"，事件 / 怪 / 奖励由作者自己填）。
        ///
        /// 为什么落在 `_模板\` 里：第①页的「地图选择」清单**就是** `TemplateCatalog.List()`
        ///   （见 `CreateLevelDialog.BuildTemplates`），新建 = 整份复制模板
        ///   （`LevelDoc.CreateFromOwnTemplate`）。所以"让某张图能新建"的落点就是"在这儿放一份模板"。
        ///
        /// 口径（**幂等**：只看"这张图有没有模板"，有就一份都不加、不覆盖）：
        ///   * 覆盖范围 = `GameTables.Maps()` 里 **认得出画板号 + 模式属于 PVE 系（4 PVE / 12 词条PVE）
        ///     或练习（9）** 的图 —— 判据直接用 `GameTables.CanBuildDiyFrom` / `ModesOfMap`，
        ///     **不硬编码任何地图号**；
        ///   * 文件名 `&lt;地图id&gt;_&lt;底版档&gt;.json`（例如 `82010_0.json`），与已有的官方模板同一命名法；
        ///   * `difficulty` = **0**（普通档）：与**所有**已有的官方种子模板一致（`82016_0` / `82008_0` /
        ///     `82016_1`…），也是 `LevelDoc.OfficialLevelFor` 挑底版时"最低档优先"的那一档；
        ///   * `diy.base_level` 写**这份模板自己的文件名**：新建走 `CreateFromOwnTemplate`，
        ///     底版就是模板自己（`doc.BaseLevel = 模板文件名`），**不再去官方 levels 找底版**
        ///     —— 所以哪怕这张图官方一份 levels 都没有，也能新建（这正是 82010/82012/… 的处境）。
        ///
        /// ★ **不凭空造内容**：`progress_events` / `start_monsters` / `victory` / `progress` 一律**不写**
        ///   （官方语义是"没写的字段回落表/代码默认值"；写空容器反而会变成"明确地一个都不要"）。
        ///   校验页会给一条 Info 提示"这份还没写事件"，不报错。
        /// </summary>
        public static int EnsureBlankTemplates()
        {
            string dir = Dir;
            if (dir.Length == 0) return 0;

            // 现有模板覆盖了哪些地图（模板里 `map` 就是地图 id）
            var covered = new Dictionary<int, bool>();
            foreach (LevelTemplate t in List())
                if (t.MapId > 0) covered[t.MapId] = true;

            var todo = new List<MapTemplate>();
            foreach (MapTemplate m in GameTables.Maps())
            {
                if (m == null || string.IsNullOrEmpty(m.SceneSuffix)) continue;   // 没画板号 ⇒ 定不了棋盘
                if (covered.ContainsKey(m.Id)) continue;                          // 已经有模板 ⇒ 不动
                if (!GameTables.CanBuildDiyFrom(m)) continue;                     // 不是 PVE/词条PVE 系 ⇒ 不硬塞
                todo.Add(m);
            }
            if (todo.Count == 0) return 0;

            try { Directory.CreateDirectory(dir); }
            catch (Exception) { return 0; }

            int made = 0;
            foreach (MapTemplate m in todo)
            {
                string file = m.Id.ToString(CultureInfo.InvariantCulture) + "_0.json";
                string path = System.IO.Path.Combine(dir, file);
                if (File.Exists(path)) continue;                                  // 有就不覆盖（幂等）
                try
                {
                    File.WriteAllText(path, BlankTemplateJson(m), new System.Text.UTF8Encoding(false));
                    made++;
                }
                catch (Exception) { }
            }
            return made;
        }

        // ============================================================ ★★ 空白玩法模板的进度事件骨架
        //
        // 用户 2026-10-10：「给地图模板也给一些进度事件的模板，显示的文案啊什么的可以跟原版一样
        //   但是表现为 diy 的就可以了」。
        //
        // ★★ 骨架从哪来（2026-10-10 修正 —— 这一版最重要的认知）：
        //   官方**本来就登记了"这张图这一档用哪几条进度事件"**：
        //     `Map.bin` 顶层 field 6（难度表）→ 难度行 field 2 → **行里 field 3**（packed fixed32）
        //     = 这张图这一档的事件 id 列表
        //     （`GameTables.ProgressEventsByDifficulty` / `MapEventTable.EventsForMapDifficulty`）。
        //   拿这批 id 回 `MapEvent.bin`（触发进度 = field 4 ÷ 2）与 `STRMapEvent.bin`（文案）取数，
        //   就是"**文案跟原版一样、但表现为 diy 的**"最忠实的落法。
        //
        //   ★ 上一版（同一天更早）拿的是"**全局**触发进度 = 0 的前 3 条"（31001/31002/31003），
        //     于是 8 份模板**每张图都被塞进同样那 3 条跟这张图无关的事件**；它写在 docstring 里的
        //     依据（"82016_0.json 里是 \"32121\": {}"）**是错的** —— 82016_0.json 实际写的是
        //     32122/32123/32124，而且它的 `progress_events_note` 明说 32121 **故意不写**。
        //     真正的病根是 `GameTables.ProgressEventsByDifficulty` 的缓存口径（只给第一次问的那张图
        //     建表 ⇒ 其余每张图都查到 0 条），上一版因此误以为"每张图的官方登记读不出来"。
        //     那个 bug 2026-10-10 已修（见 `GameTables.ProgressEventsByDifficulty` 的注释）。
        //
        // 口径（**幂等，只补空缺**）：
        //   * 只动**我们自己生成的空白玩法模板**（文件名 `<map>_0.json` 且 `diy.name` 含"（空白玩法）"）
        //     —— 官方那 6 份、以及作者自己放进来的模板**一律不碰**；
        //   * 文件里**没有 `progress_events` 键、或它是空对象**时才补；
        //   * **有内容就一个字节不动** —— 唯一的例外见 `LooksLikeLegacyOpeningSkeleton`：
        //     文件如果**逐项**等于上一版那套"全局开局波骨架"（note 上留着上一版的记号、键恰好是
        //     上一版那几条、每行只有 `trigger_progress: 0`、`diy.events` 只有官方名字），
        //     说明那是**我们自己上一版的产物、作者一个字节都没动过** ⇒ 就地升级成这张图的官方事件。
        //     ★ 作者加过 spawn / 改过进度 / 增删过事件 ⇒ 判据不成立 ⇒ 一个字节都不动。
        //   * 每条事件：键 = **官方事件 id**、`trigger_progress` = **官方那条的触发进度**、
        //     `diy.events.<id>.name` = **官方文案**（编辑器那一页显示的正是这个键，
        //     见 `DiyModel.ReadFlatEvent`）。官方没给名字（`STRMapEvent` 里没有）就不写 name ——
        //     宁可留空让那一页显示"事件 &lt;id&gt;"，也不自己编一句文案（例如 83001/83002 的 35001~35004）。
        //   * `MapEvent.bin` 那一行**没写触发点**（field 4 缺）的事件**不收**：那种 id 根本编码不了
        //     触发进度（`DiyTrigger.ApplyRow` 同样拒收），收进来只会在校验页变成一条"没挑触发点"。
        //   * 这张图官方一档都没登记 ⇒ **整块 `progress_events` 不写**。
        //
        // ★★ 2026-10-10 追加（用户要"**一个不刷怪的空白模板**"；与服务端对称）：
        //   空白模板要写成**显式空数组**，而不是"不写"：
        //     · 顶层 `"start_monsters": []` ⇒ 开局**明确不刷**（不是"回落官方 `battle.INIT_MONSTERS`"）；
        //     · 每条事件 `"spawn": []` ⇒ 这一波**明确不刷**（不是"回落官方那条事件的效果"）。
        //   有怪的时候照旧一行不少（`spawn` 有行就写行）——见 `DiyModel.SyncStartMonsters` /
        //   `SyncProgressEvents` 那一对同口径的改动。
        //   ⇒ 效果 = **空白玩法 + 官方棋盘 + 官方事件骨架（id / 文案 / 触发进度都在），但一只怪都不刷**；
        //     作者往里加才会刷（「怪物」页还有「用官方开局怪」一键填回官方那批，见 `OpeningMonsters`）。
        //
        // ============================================================

        /// <summary>
        /// ★★ 给"空白玩法"模板补/刷新成 —— **这张图官方登记的进度事件骨架 + 显式空刷怪**
        ///   （`start_monsters: []`、每条事件 `spawn: []`，官方 id / 文案 / 触发进度全部保留）。
        /// 返回这次**新补**了几份；就地升级（上一版骨架 → 这一版带显式空数组）的份数走 <paramref name="upgraded"/>。
        /// </summary>
        public static int EnsureTemplateEvents() { int upgraded; return EnsureTemplateEvents(out upgraded); }

        /// <summary>同上，另外报出"就地升级"的份数。</summary>
        public static int EnsureTemplateEvents(out int upgraded)
        {
            upgraded = 0;
            string dir = Dir;
            if (dir.Length == 0 || !Directory.Exists(dir)) return 0;
            if (MapEventTable.Rows().Count == 0) return 0;   // 读不到 MapEvent.bin ⇒ 不瞎编，什么都不做

            int made = 0;
            foreach (LevelTemplate t in List())
            {
                if (!IsBlankTemplate(t)) continue;
                // ★ 官方一条都没登记的图（例如 82017）**也要**处理：它同样是"不刷怪的空白模板"，
                //   只是事件表是空的（`seeds.Count == 0` ⇒ 不写 `progress_events`，但 `start_monsters: []` 要写）。
                List<MapEventRow> seeds = OfficialEventSeeds(t.MapId, t.Difficulty);
                try
                {
                    bool wasUpgrade;
                    if (!RefreshBlankTemplate(t.Path, seeds, out wasUpgrade)) continue;
                    if (wasUpgrade) upgraded++; else made++;
                }
                catch (Exception) { }
            }
            return made;
        }

        /// <summary>
        /// ★★ 一张图**这一档**的官方进度事件骨架：`Map.bin` 难度行 field 3 登记的 id → `MapEvent.bin` 那一行。
        ///   * 顺序**照官方登记的那一串**（不重排 —— 这份清单本身就是官方的播放顺序）；
        ///   * 表里没有那一行、或那一行**没写触发点**的，跳过（编码不了触发进度）；
        ///   * 同一 id 只收一次。
        /// </summary>
        public static List<MapEventRow> OfficialEventSeeds(int mapId, int difficulty)
        {
            var outList = new List<MapEventRow>();
            if (mapId <= 0) return outList;
            foreach (int id in MapEventTable.EventsForMapDifficulty(mapId, difficulty))
            {
                MapEventRow r = MapEventTable.Get(id);
                if (r == null || !r.HasTrigger) continue;
                bool dup = false;
                foreach (MapEventRow x in outList) if (x.Id == r.Id) { dup = true; break; }
                if (!dup) outList.Add(r);
            }
            return outList;
        }

        /// <summary>
        /// 它是不是"我们生成的空白玩法模板"：文件名 `<数字>_0.json` 且 `diy.name` 带"（空白玩法）"。
        /// 用来把"官方模板 / 作者自己的模板"排除在自动补事件之外。
        /// </summary>
        private static bool IsBlankTemplate(LevelTemplate t)
        {
            if (t == null || t.Path.Length == 0) return false;
            string stem = System.IO.Path.GetFileNameWithoutExtension(t.Path);
            if (!stem.EndsWith("_0", StringComparison.Ordinal)) return false;
            int map;
            if (!int.TryParse(stem.Substring(0, stem.Length - 2),
                    NumberStyles.Integer, CultureInfo.InvariantCulture, out map)) return false;
            return t.Name.IndexOf("（空白玩法）", StringComparison.Ordinal) >= 0;
        }

        /// <summary>
        /// 给一份模板补事件：**只在 `progress_events` 缺失 / 空对象时**动手；
        /// 有内容时**只有**"逐项等于上一版开局波骨架"那一情形才就地升级（`upgraded` 报出来）。
        /// 用文本方式判断 + 重新拼，避免引入 JSON 写回器（模板本身很小、结构固定）。
        /// </summary>
        private static bool RefreshBlankTemplate(string path, List<MapEventRow> seeds, out bool upgraded)
        {
            upgraded = false;
            string text = File.ReadAllText(path, Encoding.UTF8);
            JNode root = DiyJson.Parse(text);
            if (root == null || root.Kind != JKind.Object) return false;
            int mapId = (int)DiyJson.IntOf(DiyJson.Member(root, "map"), 0);
            if (mapId <= 0) return false;

            JNode pe = DiyJson.Member(root, "progress_events");
            bool empty = pe == null
                || (pe.Kind == JKind.Object && (pe.Members == null || pe.Members.Count == 0))
                || (pe.Kind == JKind.Array && (pe.Items == null || pe.Items.Count == 0));
            if (!empty)
            {
                // ★★ 有内容：**只有**"那是我们自己写进去、作者一个字节都没动过"才刷新。
                //    判据写在 LooksLikeOurs（逐项核对，宁可不动）。
                if (!LooksLikeOurs(root, mapId, seeds)) return false;
                upgraded = true;
            }

            // 把 `diy` 块里已有内容留着，只把 note / start_monsters / progress_events / diy.events 补上。
            // 模板是我们自己生成的、结构固定 ⇒ 用"文本重排"最稳（不引入 json 写回器）。
            string rebuilt = RebuildBlankTemplate(root, mapId, seeds);
            if (rebuilt == null) { upgraded = false; return false; }
            if (string.Equals(rebuilt, text, StringComparison.Ordinal)) { upgraded = false; return false; }
            File.WriteAllText(path, rebuilt, new System.Text.UTF8Encoding(false));
            return true;
        }

        // ============================================================ "这是我们的产物 + 作者没动过"的指纹
        //
        // 就地刷新（上一版骨架 → 这一版"带显式空数组"）**只**在下面两种情形动手，
        // 任何一条不成立就当作者动过 ⇒ 一个字节都不改：
        //
        //   ① 上一版（同一天更早）那套"**全局**开局波骨架"：`note` 上留着上一版的记号、
        //      `progress_events` 的键**恰好**是全局进度 0 的前 3 条（31001/31002/31003）、
        //      每行只有 `trigger_progress: 0`、`diy.events` 每项只有官方名字
        //      —— 见 `LooksLikeLegacyOpeningSkeleton`。
        //   ② 这一版/上一版的"**这张图自己的**官方事件骨架"：`note` 还是我们生成的那句开头、
        //      `progress_events` 的键**恰好**等于 `Map.bin` 难度行登记的那串（顺序也一致）、
        //      每行只有 `trigger_progress`（= 官方那条的进度）与**空** `spawn`、
        //      `diy.events` 每项只有 `name`（= 官方文案）—— 见 `LooksLikeOurs`。
        //      ★ 作者加过 spawn 行 / 改过触发进度 / 增删过事件 / 加过奖励 ⇒ 判据不成立 ⇒ 不碰。
        private const int LegacySeedCount = 3;
        private const string LegacyNoteMark = "★ 已带上**开局进度事件骨架**";

        private static List<int> _legacySeedIds;

        /// <summary>上一版那几条"开局波"id（`MapEvent.bin` 里触发进度 = 0 的前 3 条，逐次一致）。</summary>
        private static List<int> LegacyOpeningIds()
        {
            if (_legacySeedIds != null) return _legacySeedIds;
            _legacySeedIds = new List<int>();
            foreach (MapEventRow r in MapEventTable.RowsAtProgress(0))
            {
                if (r == null) continue;
                _legacySeedIds.Add(r.Id);
                if (_legacySeedIds.Count >= LegacySeedCount) break;
            }
            return _legacySeedIds;
        }

        /// <summary>
        /// 文件是不是"**我们生成的**空白玩法模板"（note / 骨架都对得上），且**作者没动过内容**。
        /// 两种产物都认：上一版的全局开局波骨架、这一版"这张图自己的骨架"。
        /// </summary>
        private static bool LooksLikeOurs(JNode root, int mapId, List<MapEventRow> seeds)
        {
            if (LooksLikeLegacyOpeningSkeleton(root)) return true;
            if (!NoteIsOurs(root, mapId)) return false;

            JNode diy = DiyJson.Member(root, "diy");
            if (diy == null || diy.Kind != JKind.Object) return false;

            JNode pe = DiyJson.Member(root, "progress_events");
            int rows = pe == null ? 0
                : (pe.Kind == JKind.Object && pe.Members != null ? pe.Members.Count : -1);
            if (rows < 0 || rows != seeds.Count) return false;      // 数组形态 / 多一条少一条 ⇒ 不是我们的

            for (int k = 0; k < seeds.Count; k++)
            {
                JMember m = pe.Members[k];
                if (m.Key != seeds[k].Id.ToString(CultureInfo.InvariantCulture)) return false;
                if (m.Value == null || m.Value.Kind != JKind.Object || m.Value.Members == null) return false;
                foreach (JMember sub in m.Value.Members)
                {
                    if (string.Equals(sub.Key, DiyTrigger.KeyProgress, StringComparison.Ordinal))
                    {
                        if (DiyJson.IntOf(sub.Value, -1) != seeds[k].Progress) return false;
                    }
                    else if (string.Equals(sub.Key, "spawn", StringComparison.Ordinal))
                    {
                        // 只认**空**数组（作者加过怪 ⇒ 不是空数组 ⇒ 不动）
                        if (sub.Value == null || sub.Value.Kind != JKind.Array || sub.Value.Count != 0) return false;
                    }
                    else return false;                              // 奖励 / 攻防 / 别的键 ⇒ 作者动过
                }
            }

            JNode evs = DiyJson.Member(diy, "events");
            if (evs != null && evs.Kind == JKind.Object && evs.Members != null)
                foreach (JMember m in evs.Members)
                {
                    int id;
                    if (!int.TryParse(m.Key, NumberStyles.Integer, CultureInfo.InvariantCulture, out id)) return false;
                    bool known = false;
                    string official = MapEventTable.NameOf(id);
                    foreach (MapEventRow r in seeds) if (r.Id == id && official.Length > 0) { known = true; break; }
                    if (!known) return false;
                    if (m.Value == null || m.Value.Kind != JKind.Object || m.Value.Members == null) return false;
                    foreach (JMember sub in m.Value.Members)
                    {
                        if (!string.Equals(sub.Key, "name", StringComparison.Ordinal)) return false;
                        if (sub.Value == null || sub.Value.Kind != JKind.String) return false;
                        if (!string.Equals(sub.Value.Str, official, StringComparison.Ordinal)) return false;
                    }
                }
            return true;
        }

        /// <summary>`note` 是不是我们生成的那句话（两种版本的产物都认）。</summary>
        private static bool NoteIsOurs(JNode root, int mapId)
        {
            string note = Str(root, "note");
            if (note.IndexOf(LegacyNoteMark, StringComparison.Ordinal) >= 0) return true;
            return note.StartsWith(MapNameOf(mapId) + " " + mapId.ToString(CultureInfo.InvariantCulture)
                + " 的空白玩法模板：官方棋盘 + ", StringComparison.Ordinal);
        }

        /// <summary>文件是不是"上一版开局波骨架、且作者一个字节都没动过"（判据见上面那段注释）。</summary>
        private static bool LooksLikeLegacyOpeningSkeleton(JNode root)
        {
            JNode diy = DiyJson.Member(root, "diy");
            if (diy == null || diy.Kind != JKind.Object) return false;
            if (Str(root, "note").IndexOf(LegacyNoteMark, StringComparison.Ordinal) < 0) return false;

            List<int> legacy = LegacyOpeningIds();
            if (legacy.Count == 0) return false;

            JNode pe = DiyJson.Member(root, "progress_events");
            if (pe == null || pe.Kind != JKind.Object || pe.Members == null) return false;
            if (pe.Members.Count != legacy.Count) return false;
            for (int k = 0; k < legacy.Count; k++)
            {
                JMember m = pe.Members[k];
                int id;
                if (!int.TryParse(m.Key, NumberStyles.Integer, CultureInfo.InvariantCulture, out id)) return false;
                if (id != legacy[k]) return false;
                if (m.Value == null || m.Value.Kind != JKind.Object || m.Value.Members == null) return false;
                foreach (JMember sub in m.Value.Members)
                {
                    if (!string.Equals(sub.Key, DiyTrigger.KeyProgress, StringComparison.Ordinal)) return false;
                    if (DiyJson.IntOf(sub.Value, -1) != 0) return false;
                }
            }

            JNode evs = DiyJson.Member(diy, "events");
            if (evs != null && evs.Kind == JKind.Object && evs.Members != null)
                foreach (JMember m in evs.Members)
                {
                    int id;
                    if (!int.TryParse(m.Key, NumberStyles.Integer, CultureInfo.InvariantCulture, out id)) return false;
                    if (!legacy.Contains(id)) return false;
                    if (m.Value == null || m.Value.Kind != JKind.Object || m.Value.Members == null) return false;
                    foreach (JMember sub in m.Value.Members)
                    {
                        if (!string.Equals(sub.Key, "name", StringComparison.Ordinal)) return false;
                        string official = MapEventTable.NameOf(id);
                        string want = official.Length > 0 ? official : ("事件 " + id);
                        if (sub.Value == null || sub.Value.Kind != JKind.String) return false;
                        if (!string.Equals(sub.Value.Str, want, StringComparison.Ordinal)) return false;
                    }
                }
            return true;
        }

        /// <summary>
        /// 把一份空白玩法模板重排成"带**这张图官方**进度事件骨架"的文本。
        /// 只认我们自己生成的形状（note / map / difficulty / diy{version,name,base_map,base_scene,base_level}），
        /// 认不出来就回 null（**宁可不动，也不乱改别人的文件**）。
        /// </summary>
        private static string RebuildBlankTemplate(JNode root, int mapId, List<MapEventRow> seeds)
        {
            JNode diy = DiyJson.Member(root, "diy");
            if (diy == null || diy.Kind != JKind.Object) return null;
            if (mapId <= 0) return null;
            string diyName = Str(diy, "name");
            if (diyName.IndexOf("（空白玩法）", StringComparison.Ordinal) < 0)
                diyName = MapNameOf(mapId) + "（空白玩法）";
            string baseScene = Str(diy, "base_scene");
            string baseLevel = Str(diy, "base_level");
            if (baseLevel.Length == 0) return null;

            var sb = new System.Text.StringBuilder();
            sb.Append("{\n");
            sb.Append("  \"note\": \"").Append(Esc(NoteText(mapId, seeds.Count))).Append("\",\n");
            sb.Append("  \"map\": ").Append(mapId.ToString(CultureInfo.InvariantCulture)).Append(",\n");
            sb.Append("  \"difficulty\": ").Append(((int)DiyJson.IntOf(DiyJson.Member(root, "difficulty"), 0))
                .ToString(CultureInfo.InvariantCulture)).Append(",\n");
            // ★★ 「一个不刷怪的空白模板」：开局**显式**空数组（键在、空数组 = 服务端明确不刷）
            sb.Append("  \"start_monsters\": [],\n");
            sb.Append("\n");
            // ★ 事件表：**这张图官方一条都没登记**（例如 82017）就整块不写 ——
            //   写个空对象 `{}` 是"明确地一个都不要"，那不是在说"这图没登记"。
            if (seeds.Count > 0)
            {
                sb.Append("  \"progress_events\": {\n");
                for (int k = 0; k < seeds.Count; k++)
                {
                    MapEventRow r = seeds[k];
                    sb.Append("    \"").Append(r.Id.ToString(CultureInfo.InvariantCulture)).Append("\": { ")
                      .Append("\"trigger_progress\": ").Append(r.Progress.ToString(CultureInfo.InvariantCulture))
                      .Append(", \"spawn\": [] }").Append(k + 1 < seeds.Count ? ",\n" : "\n");
                }
                sb.Append("  },\n");
                sb.Append("\n");
            }
            sb.Append("  \"diy\": {\n");
            sb.Append("    \"version\": ").Append(((int)DiyJson.IntOf(DiyJson.Member(diy, "version"), 1))
                .ToString(CultureInfo.InvariantCulture)).Append(",\n");
            sb.Append("    \"name\": \"").Append(Esc(diyName)).Append("\",\n");
            sb.Append("    \"base_map\": ").Append(mapId.ToString(CultureInfo.InvariantCulture)).Append(",\n");
            sb.Append("    \"base_scene\": \"").Append(Esc(baseScene)).Append("\",\n");
            sb.Append("    \"base_level\": \"").Append(Esc(baseLevel)).Append("\"");
            AppendEventNames(sb, "    ", seeds);
            sb.Append("  }\n");
            sb.Append("}\n");
            return sb.ToString();
        }

        /// <summary>
        /// `diy.events` 那一块：**只给官方给了名字的**事件写 `name`（名字一律照抄 STRMapEvent.bin）。
        /// ★ 一条名字都没有（例如 83001/83002 的 35001~35004 在表里就是没名字）⇒ **整块不写**，
        ///   宁可让那一页显示"事件 35001"，也不自己编一句文案。
        /// </summary>
        private static void AppendEventNames(System.Text.StringBuilder sb, string indent, List<MapEventRow> seeds)
        {
            var named = new List<MapEventRow>();
            foreach (MapEventRow r in seeds) if (r.Name.Length > 0) named.Add(r);
            if (named.Count == 0) { sb.Append("\n"); return; }
            sb.Append(",\n");
            sb.Append(indent).Append("\"events\": {\n");
            for (int k = 0; k < named.Count; k++)
            {
                MapEventRow r = named[k];
                sb.Append(indent).Append("  \"").Append(r.Id.ToString(CultureInfo.InvariantCulture))
                  .Append("\": { \"name\": \"").Append(Esc(r.Name)).Append("\" }")
                  .Append(k + 1 < named.Count ? ",\n" : "\n");
            }
            sb.Append(indent).Append("}\n");
        }

        /// <summary>
        /// 模板里那句 `note`（生成与就地刷新共用一份文案，别两处各写一句）。
        /// ★★ 2026-10-10 追加"不刷怪"那半句：现在**开局与每条事件都是显式空数组**
        ///   （`start_monsters: []` / `progress_events.&lt;事件id&gt;.spawn: []`）⇒ 是"**明确不刷**"，
        ///   不再是原来那句会误导人的"刷怪…回落默认"（回落官方那批才是用户踩到的坑）。
        ///   ★ 这句里**不能出现双引号**（它本身就是 JSON 字符串），键名一律不加引号写。
        /// </summary>
        private static string NoteText(int mapId, int seedCount)
        {
            var sb = new System.Text.StringBuilder();
            sb.Append(MapNameOf(mapId)).Append(' ').Append(mapId.ToString(CultureInfo.InvariantCulture))
              .Append(" 的空白玩法模板：官方棋盘 + ");
            if (seedCount > 0)
                sb.Append("**这张图官方登记的进度事件骨架**（").Append(seedCount.ToString(CultureInfo.InvariantCulture))
                  .Append(" 条：事件 id / 触发进度 / 文案都照官方那份，可改可删）");
            else
                sb.Append("**空的事件表**（这张图官方一档都没登记进度事件，事件由作者自己加）");
            sb.Append("。★ 开局与每条事件都写成了**显式空数组**（start_monsters: [] 与 progress_events.")
              .Append("<事件id>.spawn: []）⇒ **一只怪都不刷**（不是回落官方那批；想让开局就用官方那批，")
              .Append("在「怪物」页点「用官方开局怪」）。奖励 / 胜负 / 主谋还没写 ⇒ 回落默认，由作者自己填。")
              .Append("由编辑器 TemplateCatalog 生成；改这份模板不影响已新建的关卡（新建是整份复制）。");
            return sb.ToString();
        }

        /// <summary>地图表里的官方名（读不到就"地图 &lt;id&gt;"）。</summary>
        private static string MapNameOf(int mapId)
        {
            MapTemplate m = GameTables.Map(mapId);
            return m != null && !string.IsNullOrEmpty(m.Name)
                ? m.Name : ("地图 " + mapId.ToString(CultureInfo.InvariantCulture));
        }

        private static string Str(JNode obj, string key)
        {
            JNode n = DiyJson.Member(obj, key);
            return n != null && n.Kind == JKind.String ? n.Str : "";
        }

        /// <summary>
        /// 一份空白玩法模板的 JSON 文本（键序与既有模板一致，手改起来顺手）。
        /// ★ 事件骨架取自**这张图自己的官方登记**（`Map.bin` 难度行 field 3，见 `OfficialEventSeeds`）；
        ///   这张图官方一档都没登记 ⇒ **整块 `progress_events` 不写**。
        /// ★★ 但 `start_monsters: []` **一定写**，事件行也一定带 `spawn: []`
        ///   —— 这份模板的语义是"**一个不刷怪的空白玩法**"（显式空数组 = 明确不刷）。
        /// </summary>
        private static string BlankTemplateJson(MapTemplate m)
        {
            string file = m.Id.ToString(CultureInfo.InvariantCulture) + "_0.json";
            List<MapEventRow> seeds = OfficialEventSeeds(m.Id, 0);
            var sb = new System.Text.StringBuilder();
            sb.Append("{\n");
            sb.Append("  \"note\": \"").Append(Esc(NoteText(m.Id, seeds.Count))).Append("\",\n");
            sb.Append("  \"map\": ").Append(m.Id.ToString(CultureInfo.InvariantCulture)).Append(",\n");
            sb.Append("  \"difficulty\": 0,\n");
            sb.Append("  \"start_monsters\": [],\n");
            sb.Append("\n");

            // ★ 这张图的官方事件骨架（键 = 官方事件 id；触发进度 / 文案都照官方那份；`spawn: []` = 明确不刷）
            if (seeds.Count > 0)
            {
                sb.Append("  \"progress_events\": {\n");
                for (int k = 0; k < seeds.Count; k++)
                {
                    sb.Append("    \"").Append(seeds[k].Id.ToString(CultureInfo.InvariantCulture))
                      .Append("\": { \"trigger_progress\": ")
                      .Append(seeds[k].Progress.ToString(CultureInfo.InvariantCulture))
                      .Append(", \"spawn\": [] }").Append(k + 1 < seeds.Count ? ",\n" : "\n");
                }
                sb.Append("  },\n");
                sb.Append("\n");
            }

            sb.Append("  \"diy\": {\n");
            sb.Append("    \"version\": 1,\n");
            sb.Append("    \"name\": \"").Append(Esc(m.Name)).Append("（空白玩法）\",\n");
            sb.Append("    \"base_map\": ").Append(m.Id.ToString(CultureInfo.InvariantCulture)).Append(",\n");
            sb.Append("    \"base_scene\": \"").Append(Esc(m.SceneSuffix)).Append("\",\n");
            sb.Append("    \"base_level\": \"").Append(Esc(file)).Append("\"");
            AppendEventNames(sb, "    ", seeds);
            sb.Append("  }\n");
            sb.Append("}\n");
            return sb.ToString();
        }

        private static string Esc(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }

        /// <summary>
        /// 目录空着就从官方 levels 拷一份种子模板进来（**只拷一次**；已经有模板就不动）。
        /// 返回这次新拷了几份。官方目录也找不到就回 0。
        /// </summary>
        public static int EnsureSeeded()
        {
            string dir = Dir;
            if (dir.Length == 0) return 0;
            if (List().Count > 0) return 0;

            string official = LevelDoc.OfficialLevelDir();
            if (string.IsNullOrEmpty(official) || !Directory.Exists(official)) return 0;

            var copied = 0;
            try { Directory.CreateDirectory(dir); }
            catch (Exception) { return 0; }

            string[] files;
            try { files = Directory.GetFiles(official, "*.json"); }
            catch (Exception) { return 0; }
            Array.Sort(files, StringComparer.OrdinalIgnoreCase);
            foreach (string f in files)
            {
                string name = System.IO.Path.GetFileName(f);
                string to = System.IO.Path.Combine(dir, name);
                if (File.Exists(to)) continue;
                try
                {
                    File.Copy(f, to, false);
                    copied++;
                }
                catch (Exception) { }
            }
            return copied;
        }
    }
}
