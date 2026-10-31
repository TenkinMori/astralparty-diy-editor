using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DiyEditor
{
    public partial class App : Application
    {
        /// <summary>
        /// ★★ 自测台能不能开跑 —— 不能就把**原因**回出来（能跑回 null）。
        ///
        /// 为什么要它（用户 2026-10-10 报的"构建后首次跑 --clicktest 假红 54/12"）：
        ///   自测台会往**自己目录里固定的几个文件**写东西（`_自测\点击自测关卡.json` /
        ///   `自测输出.txt` / `_自测\输出\**`）。如果**另一个编辑器实例**（最常见的就是用户
        ///   自己开着的那个界面，或者上一次还没退干净的自测）也在写同一批文件，两边会互相
        ///   打断 —— 表现就是"一次挂一大片、复跑又全绿"，跟真回归长得一模一样。
        ///   ⇒ 与其让人去猜"这是不是抖动"，不如**开局就拒绝跑**并把话说清楚。
        ///
        /// 两道检查：
        ///   ① 另有一个实例**会写同一个 `_自测\` 目录** ⇒ 报错。
        ///      ★ 判据是"会不会写同一份 `_自测\`"，**不是**"有没有同名进程"：
        ///        自测台是"读关卡 + 写自测产物"的独立进程，两个实例写**不同**目录各跑各的并不冲突；
        ///        会打架的只有"同一份 `_自测\` 被两个进程同时写"。所以先算出对方 exe 的 `_自测`
        ///        落在哪，与自己的比对，只有相同才拦；不同的**不拦**但会打印出来让人看见。
        ///   ② 在 `_自测\.selftest.lock` 上抢**独占文件锁** —— 抢不到说明另一次自测正在写，
        ///      报错。这个锁活到进程结束（不删文件，文件在不算"占用"）。
        ///
        /// ★ 为什么不能只看"有没有同名进程"就一刀拦：那会让"开着编辑器就没法跑自测"，
        ///   而用户最常见的用法正是**开着 dist 版界面 + 跑 src 的自测** —— 这两者若解析到
        ///   同一份 `_自测`（`dist\` 与 `src\bin\Release\` 都往上找到同一个 `5-DIY关卡\`）
        ///   确实会打架，所以那种情况**照拦**；但把 exe 拷到别处跑（`_自测` 落在 exe 旁边）
        ///   就不会被误拦。
        /// </summary>
        /// ★★ 两层判据（2026-10-10 用户要求收窄 —— 旧判据"有同名进程就拦"太宽，
        ///   用户经常开着 `dist\` 干活，那个进程**并不跑自测**，却每次都把自测拦掉）：
        ///
        ///   **① 硬拦（exit 4）只留给"真的会打架"的情形** —— `_自测\.selftest.lock`
        ///      **被别人占着**（`FileShare.None` 抢不到 = **另一个自测正在跑**）。
        ///      只有这时才拦，并指名道姓说是谁在跑。
        ///   **② 同名进程只做提示、不拦** —— 打一行 `[提示]`（还算出"它会不会写同一份 `_自测\`"
        ///      写进文案里），然后**照常继续跑**。
        ///
        ///   为什么 ① 就够了：真正会互相打断的**只有两个自测同时写同一批 `_自测\` 文件**；
        ///   而"开着编辑器界面"根本不碰那些文件。锁是**精确**的判据，进程名是**粗**的判据 ——
        ///   用粗判据拦人，代价是"每次都得先关窗口"。
        /// </summary>
        private static string SelftestBlockedReason()
        {
            // ============================================================ ① 硬拦：锁被占 = 另一个自测在跑
            _selftestDir = Paths.Under("_自测");
            string lockPath = Path.Combine(_selftestDir, ".selftest.lock");
            try
            {
                if (!Directory.Exists(_selftestDir)) Directory.CreateDirectory(_selftestDir);
                _selftestLock = new FileStream(lockPath, FileMode.OpenOrCreate,
                    FileAccess.ReadWrite, FileShare.None);
                // ★ 把自己的 PID 写到**旁边那个小文件**里 —— 别人被锁挡住时就能"指名道姓"。
                //   为什么不写进锁文件本身：锁是 `FileShare.None`，**别人连读都读不到**
                //   （`FileShare.None` 连共享读都不给）⇒ 只能另放一个文件。
                try
                {
                    File.WriteAllText(LockOwnerPath,
                        System.Diagnostics.Process.GetCurrentProcess().Id.ToString(CultureInfo.InvariantCulture),
                        new UTF8Encoding(false));
                }
                catch (Exception) { }
            }
            catch (Exception ex)
            {
                string holder = DescribeLockHolder();
                return "有**另一个自测正在跑**（抢不到 `_自测\\.selftest.lock` 的独占锁）"
                    + (holder.Length > 0 ? ("：" + holder) : "")
                    + "。\n        两个自测写同一批 `_自测\\` 文件必然互相打断"
                    + "（看起来就是「一次挂一大片」）。\n        等它跑完再跑。"
                    + "\n        （锁文件：" + lockPath + " — " + ex.GetType().Name + "）";
            }

            // ============================================================ ② 同名进程：只提示，不拦
            try
            {
                string me = System.Diagnostics.Process.GetCurrentProcess().ProcessName;
                var same = new List<string>();
                var other = new List<string>();
                foreach (System.Diagnostics.Process p in
                         System.Diagnostics.Process.GetProcessesByName(me))
                {
                    if (p.Id == System.Diagnostics.Process.GetCurrentProcess().Id) continue;
                    string when = "";
                    try { when = "（" + p.StartTime.ToString("HH:mm:ss", CultureInfo.InvariantCulture) + " 启动）"; }
                    catch (Exception) { }
                    string label = "PID " + p.Id + when;

                    // 对方 exe 在哪 → 它的 `_自测` 会落在哪（**只用来写提示文案**）
                    string theirs = null;
                    try
                    {
                        string exe = p.MainModule == null ? null : p.MainModule.FileName;
                        if (!string.IsNullOrEmpty(exe))
                        {
                            string d = Path.GetDirectoryName(exe);
                            for (int k = 0; k < 10 && !string.IsNullOrEmpty(d); k++)
                            {
                                if (string.Equals(Path.GetFileName(d.TrimEnd('\\')), "5-DIY关卡",
                                        StringComparison.OrdinalIgnoreCase))
                                { theirs = Path.Combine(d, "_自测"); break; }
                                d = Path.GetDirectoryName(d);
                            }
                            if (theirs == null) theirs = Path.Combine(Path.GetDirectoryName(exe), "_自测");
                        }
                    }
                    catch (Exception) { }

                    if (theirs != null && string.Equals(
                            Path.GetFullPath(theirs).TrimEnd('\\'),
                            Path.GetFullPath(_selftestDir).TrimEnd('\\'),
                            StringComparison.OrdinalIgnoreCase))
                        same.Add(label + "（exe " + theirs + "）");
                    else
                        other.Add(label + (theirs == null ? "" : "（它写 " + theirs + "）"));
                }
                Console.WriteLine("   [护栏] 本进程 exe=" + Paths.Base + " ⇒ _自测=" + _selftestDir
                    + " · 已拿到 .selftest.lock（没有别的自测在跑）");
                if (same.Count + other.Count > 0)
                    Console.WriteLine("   [提示] 另有 " + (same.Count + other.Count)
                        + " 个编辑器实例在跑"
                        + (same.Count > 0 ? ("（其中 " + same.Count + " 个写同一份 `_自测\\`：" 
                            + string.Join("、", same.ToArray()) + "）") : "")
                        + " —— 若它们也在跑自测才会真冲突；"
                        + "既然锁在我手上，本次照常跑。");
            }
            catch (Exception) { }

            return null;   // ← 拿到锁就放行
        }

        /// <summary>`_自测` 目录（护栏/锁用；`SelftestBlockedReason` 里设）。</summary>
        private static string _selftestDir = "";

        /// <summary>
        /// 锁被谁占着 —— 读 `_自测\.selftest.lock.owner` 里写的那个 PID
        /// （抢到锁的那个自测会把自己的 PID 写进去，见 `SelftestBlockedReason` ①）。
        /// 读不到就回空串（调用方照旧把"有另一个自测在跑"说清楚）。
        /// </summary>
        private static string DescribeLockHolder()
        {
            try
            {
                if (!File.Exists(LockOwnerPath)) return "";
                string txt = File.ReadAllText(LockOwnerPath, Encoding.UTF8).Trim();
                int pid;
                if (!int.TryParse(txt, out pid) || pid <= 0) return "";
                string when = "";
                try
                {
                    using (var p = System.Diagnostics.Process.GetProcessById(pid))
                        when = "（" + p.StartTime.ToString("HH:mm:ss", CultureInfo.InvariantCulture) + " 启动）";
                }
                catch (Exception) { }
                return "PID " + pid + when;
            }
            catch (Exception) { }
            return "";
        }

        /// <summary>锁持有者的 PID 记在这儿（锁文件本身 `FileShare.None`，读不了）。</summary>
        private static string LockOwnerPath
        {
            get { return Path.Combine(_selftestDir, ".selftest.lock.owner"); }
        }

        /// <summary>自测期间一直握着的锁（活到进程结束；文件本身不删，占着不算"被占用"）。</summary>
        private static FileStream _selftestLock;

        /// <summary>退出时把"锁持有者 PID"那个小文件删掉（免得留个过期的 PID 误导下一次）。</summary>
        private static void CleanupLockOwner()
        {
            try
            {
                if (_selftestLock == null) return;      // 本次没拿到锁 ⇒ 别删别人的
                string p = Path.Combine(Paths.Under("_自测"), ".selftest.lock.owner");
                if (File.Exists(p)) File.Delete(p);
            }
            catch (Exception) { }
        }

        /// <summary>开跑前打印一行"我开始了"，让人一眼看出是不是并发跑的两个。</summary>
        private static void SayRunHeader(string which)
        {
            try
            {
                Console.WriteLine("── " + which + " · PID "
                    + System.Diagnostics.Process.GetCurrentProcess().Id
                    + " · " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)
                    + " · exe " + Paths.Base);
            }
            catch (Exception) { }
        }

        /// <summary>
        /// 入口点。--selftest 不开窗口，直接跑自测并按退出码返回；否则正常起界面。
        /// 签名不能改：WPF 生成的 Main 就是这一份。
        /// </summary>
        [STAThread]
        public static void Main()
        {
            string[] args = Environment.GetCommandLineArgs();

            // ★★ 2026-10-17：`编辑器配置.json` 的 `game_dir` / `table_dir` **最先读**。
            //   为什么在入口这一层：`--selftest` / `--celltest` / `--shots` 这些**不开主窗口**的
            //   入口原来根本读不到这两个键（只有 `MainWindow.LoadRecent` 读），于是
            //   "配了表目录"只对界面生效、对自测/截图不生效 —— 验证起来像灵异事件。
            //   ★ 读不到 / 文件坏了什么都不改（`LoadEditorConfig` 自己吞异常）。
            GameLocator.LoadEditorConfig(Path.Combine(Paths.Base, "编辑器配置.json"));
            try
            {
                // 一眼可查：表目录用的是哪一份、来源是什么（配置 / 默认 / 配置失效回落默认）。
                Console.WriteLine("  " + GameLocator.TableDirLine);
            }
            catch (Exception) { }

            bool selfTest = false;
            foreach (string a in args)
                if (string.Equals(a, "--selftest", StringComparison.OrdinalIgnoreCase)) selfTest = true;

            if (selfTest)
            {
                try { Console.OutputEncoding = Encoding.UTF8; } catch (Exception) { }
                SayRunHeader("--selftest");
                string blocked = SelftestBlockedReason();
                if (blocked != null)
                {
                    Console.WriteLine("★ 自测**没开跑**：" + blocked);
                    Console.WriteLine("（这是**护栏**，不是断言失败 —— 退出码 4）");
                    // ★ 自己把"我因为被锁挡而退出、退出码 4"记一笔（自证用；
                    //   并发时父进程 shell 的 %ERRORLEVEL% 未必拿得准，这只是多留一份凭证）。
                    try
                    {
                        File.AppendAllText(Path.Combine(Paths.Under("_自测"), "护栏记录.txt"),
                            DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)
                            + "  PID " + System.Diagnostics.Process.GetCurrentProcess().Id
                            + "  被锁挡 ⇒ exit 4  " + blocked.Replace('\n', ' ') + "\n",
                            new UTF8Encoding(false));
                    }
                    catch (Exception) { }
                    Environment.Exit(4);
                    return;
                }
                string log = Path.Combine(Paths.Under("_自测"), "自测输出.txt");
                int code = TestRunner.Run(log);
                CleanupLockOwner();
                Environment.Exit(code);
                return;
            }

            // ★★ 真交互自查：`--clicktest [一份关卡 json]`
            //   离屏窗口（**不 Show**）+ 程序化"按一下"，逐条断言"点了有反应 / 没被盖住"。
            //   专治 --selftest 抓不到的"看着能点、点了没反应"。见 ClickTest。
            for (int i = 0; i < args.Length; i++)
            {
                if (!string.Equals(args[i], "--clicktest", StringComparison.OrdinalIgnoreCase)) continue;
                try { Console.OutputEncoding = Encoding.UTF8; } catch (Exception) { }
                SayRunHeader("--clicktest");
                // ★ 同一道护栏（见 SelftestBlockedReason）：并发跑自测会互相打断，看起来像假红。
                string blocked = SelftestBlockedReason();
                if (blocked != null)
                {
                    Console.WriteLine("★ 点击自测**没开跑**：" + blocked);
                    Console.WriteLine("（这是**护栏**，不是断言失败 —— 退出码 4）");
                    try
                    {
                        File.AppendAllText(Path.Combine(Paths.Under("_自测"), "护栏记录.txt"),
                            DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)
                            + "  PID " + System.Diagnostics.Process.GetCurrentProcess().Id
                            + "  被锁挡 ⇒ exit 4  " + blocked.Replace('\n', ' ') + "\n",
                            new UTF8Encoding(false));
                    }
                    catch (Exception) { }
                    Environment.Exit(4);
                    return;
                }
                var clickApp = new App();
                clickApp.InitializeComponent();
                string clickDoc = (i + 1 < args.Length
                    && args[i + 1].EndsWith(".json", StringComparison.OrdinalIgnoreCase)) ? args[i + 1] : "";
                int clickCode = ClickTest.Run(clickDoc);
                CleanupLockOwner();
                Environment.Exit(clickCode);
                return;
            }

            var app = new App();
            app.InitializeComponent();

            // 截图自查：把 6 页各渲一张 PNG（不写任何关卡文件）。见 ShotRunner。
            //   --shots <页码|all> [关卡 json] [--board-ids|-off] [--board-links|-off]
            //   [--board-pick 格id] [--land-pvp] [--ctx-menu] [--board-hint-off]
            //   [--node 45|60|72|85] [--shot-suffix 后缀] [--scroll-to 名] [--top-align 名] [--top-offset px]
            //   [--event-pick 事件id] [--demo-edit cellland|monster|events] [--demo-undo]
            // 先把带值的开关连值一起标记掉，后面认页码 / 关卡 json 时才不会认错。
            bool shots = false;
            string shotWhich = "all";
            string shotDoc = "";
            string shotSuffix = "";
            var isValueOfFlag = new bool[args.Length];
            for (int i = 0; i < args.Length; i++)
            {
                if (string.Equals(args[i], "--board-ids", StringComparison.OrdinalIgnoreCase))
                { PageBoard.ShotsOverrideIds = true; continue; }
                if (string.Equals(args[i], "--board-ids-off", StringComparison.OrdinalIgnoreCase))
                { PageBoard.ShotsOverrideIds = false; continue; }
                if (string.Equals(args[i], "--board-links", StringComparison.OrdinalIgnoreCase))
                { PageBoard.ShotsOverrideLinks = true; continue; }
                if (string.Equals(args[i], "--board-links-off", StringComparison.OrdinalIgnoreCase))
                { PageBoard.ShotsOverrideLinks = false; continue; }
                if (string.Equals(args[i], "--board-hint-off", StringComparison.OrdinalIgnoreCase))
                { PageBoard.ShotsHideHint = true; continue; }
                if (string.Equals(args[i], "--land-pvp", StringComparison.OrdinalIgnoreCase))
                { PageBoard.ShotsOverrideMonsters = true; continue; }
                if (string.Equals(args[i], "--ctx-menu", StringComparison.OrdinalIgnoreCase))
                { PageBoard.ShotsShowCtxMenu = true; continue; }
                if (string.Equals(args[i], "--demo-edit", StringComparison.OrdinalIgnoreCase))
                {
                    if (i + 1 < args.Length)
                    {
                        isValueOfFlag[i + 1] = true;
                        ShotRunner.DemoEditName = args[i + 1];
                    }
                    continue;
                }
                if (string.Equals(args[i], "--demo-undo", StringComparison.OrdinalIgnoreCase))
                { ShotRunner.DemoUndo = true; continue; }
                if (string.Equals(args[i], "--scroll-to", StringComparison.OrdinalIgnoreCase))
                {
                    if (i + 1 < args.Length)
                    {
                        isValueOfFlag[i + 1] = true;
                        ShotRunner.ScrollTo = args[i + 1];
                    }
                    continue;
                }
                if (string.Equals(args[i], "--top-align", StringComparison.OrdinalIgnoreCase))
                {
                    if (i + 1 < args.Length)
                    {
                        isValueOfFlag[i + 1] = true;
                        ShotRunner.TopAlign = args[i + 1];
                    }
                    continue;
                }
                if (string.Equals(args[i], "--top-offset", StringComparison.OrdinalIgnoreCase))
                {
                    double off;
                    if (i + 1 < args.Length)
                    {
                        isValueOfFlag[i + 1] = true;
                        if (double.TryParse(args[i + 1], System.Globalization.NumberStyles.Float,
                                System.Globalization.CultureInfo.InvariantCulture, out off))
                            ShotRunner.TopAlignOffset = off;
                    }
                    continue;
                }
                if (string.Equals(args[i], "--event-pick", StringComparison.OrdinalIgnoreCase))
                {
                    int pick;
                    if (i + 1 < args.Length)
                    {
                        isValueOfFlag[i + 1] = true;
                        if (int.TryParse(args[i + 1], out pick)) PageStory.ShotsPickEvent = pick;
                    }
                    continue;
                }
                if (string.Equals(args[i], "--node", StringComparison.OrdinalIgnoreCase))
                {
                    // 截图定档：格子大小（百分比；45/60/72/85 就是下拉框那四档）
                    isValueOfFlag[i < args.Length - 1 ? i + 1 : i] = true;
                    double pct;
                    if (i + 1 < args.Length &&
                        double.TryParse(args[i + 1], System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out pct) && pct > 0)
                        PageBoard.ShotsNodeRatio = pct / 100.0;
                    continue;
                }
                if (string.Equals(args[i], "--shot-suffix", StringComparison.OrdinalIgnoreCase))
                {
                    if (i + 1 < args.Length) { shotSuffix = args[i + 1]; isValueOfFlag[i + 1] = true; }
                    continue;
                }
                if (string.Equals(args[i], "--win", StringComparison.OrdinalIgnoreCase))
                {
                    // ★ 拍一个"二级窗口"（新建关卡 / 新增怪物 / 发牌）
                    if (i + 1 < args.Length)
                    {
                        isValueOfFlag[i + 1] = true;
                        ShotRunner.WindowShot = args[i + 1];
                    }
                    continue;
                }
                if (string.Equals(args[i], "--shot-h", StringComparison.OrdinalIgnoreCase))
                {
                    // ★ 渲染高度（进度页四块摞起来比 900 px 高）
                    double hh;
                    if (i + 1 < args.Length)
                    {
                        isValueOfFlag[i + 1] = true;
                        if (double.TryParse(args[i + 1], System.Globalization.NumberStyles.Float,
                                System.Globalization.CultureInfo.InvariantCulture, out hh) && hh >= 400)
                            ShotRunner.ShotHeight = hh;
                    }
                    continue;
                }
                if (string.Equals(args[i], "--board-pick", StringComparison.OrdinalIgnoreCase))
                {
                    int pick;
                    if (i + 1 < args.Length)
                    {
                        isValueOfFlag[i + 1] = true;
                        if (int.TryParse(args[i + 1], out pick)) PageBoard.ShotsPickNode = pick;
                    }
                    continue;
                }
                if (string.Equals(args[i], "--board-hover", StringComparison.OrdinalIgnoreCase))
                {
                    // ★ 截图用：把某一格设成"悬停态"（真人用是鼠标进出，离屏截图拍不到）。
                    int hv;
                    if (i + 1 < args.Length)
                    {
                        isValueOfFlag[i + 1] = true;
                        if (int.TryParse(args[i + 1], out hv)) PageBoard.ShotsHoverNode = hv;
                    }
                    continue;
                }
                if (string.Equals(args[i], "--dump-cells", StringComparison.OrdinalIgnoreCase))
                {
                    // ★ 对照截图用：把每格的画布坐标 + landType 打出来，方便精确裁图。
                    PageBoard.ShotsDumpCells = true;
                    continue;
                }
                if (string.Equals(args[i], "--board-hover-count", StringComparison.OrdinalIgnoreCase))
                {
                    // ★★ P1 回归自检（走**真实路由事件**，不像 `--clicktest` 那样程序化派发）：
                    //   对每一格真派发 MouseEnter → MouseLeftButtonDown → MouseLeave，
                    //   再数 `SetHover` / `Draw` 各被调了几次。
                    //   ★ 判据：**悬停绝不许引起重建** —— `Draw` 次数必须停在"打开页面那几次"，
                    //     不能随进出次数增长（上一版就是 悬停→重建→MouseLeave→再重建 的死循环）。
                    PageBoard.ShotsCountHover = true;
                    PageBoard.ShotsDumpCells = true;
                    continue;
                }
                if (string.Equals(args[i], "--board-hit-probe", StringComparison.OrdinalIgnoreCase))
                {
                    // ★★ P1 回归探针 2：**先真命中测试、再派事件** —— 验
                    //   ① 每格中心可命中 ② 左键点格子不启动平移且能选中
                    //   ③ 空白处左键仍能平移 ④ 右键能出菜单。
                    //   ★ 上一版只"对格子直接派事件"是错的：那绕开了命中测试，
                    //     所以"格子上没有可命中表面 ⇒ 左键落到容器 ⇒ 变成拖动光标"这个真机症状一条都测不出来。
                    PageBoard.ShotsHitProbe = true;
                    continue;
                }
            }
            for (int i = 0; i < args.Length; i++)
            {
                if (isValueOfFlag[i]) continue;
                if (!string.Equals(args[i], "--shots", StringComparison.OrdinalIgnoreCase)) continue;
                shots = true;
                if (i + 1 < args.Length && !isValueOfFlag[i + 1]
                    && !args[i + 1].StartsWith("-", StringComparison.Ordinal))
                {
                    string next = args[i + 1];
                    if (next.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) shotDoc = next;
                    else shotWhich = next;
                }
                if (i + 2 < args.Length && !isValueOfFlag[i + 2]
                    && args[i + 2].EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                    shotDoc = args[i + 2];
            }
            // ★ 排查用：`--look [frost] [dim] [背景]` —— 把外观按参数临时摆好再拍/再看，
            //   用来核"磨砂滑杆到底有没有生效"。**不写盘**（只改这一次进程的内存）。
            //   ★ 必须排在 `--shots` **之前**：两个一起给时（`--shots 1 --look 50`）
            //     要先把外观摆好，`ShotRunner` 起界面时 `ApplyLook()` 才会用上。
            bool lookOnly = false;
            for (int i = 0; i < args.Length; i++)
            {
                if (!string.Equals(args[i], "--look", StringComparison.OrdinalIgnoreCase)) continue;
                lookOnly = true;
                try { Console.OutputEncoding = Encoding.UTF8; } catch (Exception) { }
                if (i + 1 < args.Length && !args[i + 1].StartsWith("--"))
                    Look.Frost = Look.Clamp(Look.ParseNum(args[i + 1], Look.Frost), 0, Look.FrostMax);
                if (i + 2 < args.Length && !args[i + 2].StartsWith("--"))
                    Look.Dim = Look.Clamp(Look.ParseNum(args[i + 2], Look.Dim), 0, Look.DimMax);
                if (i + 3 < args.Length && !args[i + 3].StartsWith("--"))
                    Look.Background = args[i + 3];
                Console.WriteLine("背景   = " + Look.DescribeBackground());
                Console.WriteLine("磨砂   = " + Look.Num(Look.Frost) + "（0.." + Look.Num(Look.FrostMax) + "）");
                Console.WriteLine("压暗   = " + Look.Num(Look.Dim) + "（0.." + Look.Num(Look.DimMax) + "）");
                Console.WriteLine("主题色 = " + Look.Accent);
                string rp = Look.ResolveBackgroundPath(Look.Background);
                Console.WriteLine("背景图解析到 = " + (rp.Length > 0 ? rp : "（没有真图 ⇒ 现画渐变）"));
            }

            // ★ 排查/复核用：`--map-events` —— 每张"能新建"的图，**按官方登记**能列出哪几条进度事件
            //   （`Map.bin` 难度行 field 3 那一串事件 id），以及每条在 `MapEvent.bin` 里的触发进度 / 名字 / 效果。
            //   给空白模板预填 `progress_events` 就靠这份清单。
            for (int i = 0; i < args.Length; i++)
            {
                if (!string.Equals(args[i], "--map-events", StringComparison.OrdinalIgnoreCase)) continue;
                try { Console.OutputEncoding = Encoding.UTF8; } catch (Exception) { }
                var eb = new StringBuilder();
                Action<string> esay = delegate (string s) { eb.Append(s).Append('\n'); Console.WriteLine(s); };
                esay("MapEvent.bin：" + MapEventTable.Note);
                esay("表里出现过的触发进度：" + string.Join(", ",
                    MapEventTable.ProgressValues().ConvertAll(x => x.ToString()).ToArray()));
                esay("");
                esay("── 每个触发进度下有哪几条官方事件（**挑事件的实际候选池**）──");
                foreach (int p in MapEventTable.ProgressValues())
                {
                    List<MapEventRow> rows = MapEventTable.RowsAtProgress(p);
                    if (p < 0 || p > 20) continue;      // 只列"像回合数"的那些
                    esay("   进度 " + p.ToString().PadLeft(2) + " ⇒ " + rows.Count + " 条："
                        + string.Join("、", rows.ConvertAll(x => x.Id + " " + (x.Name.Length > 0 ? x.Name : "(没名字)")).ToArray()));
                }
                esay("");
                esay("── 带「刷怪」效果的官方事件（进度 0~14）—— spawn 的合法来源 ──");
                foreach (int p in MapEventTable.ProgressValues())
                {
                    if (p < 0 || p > 14) continue;
                    foreach (MapEventRow r in MapEventTable.RowsAtProgress(p))
                    {
                        List<int> ids;
                        if (r.Effects.TryGetValue(MapEventEffectType.CreateMonster, out ids))
                            esay("   进度 " + p + " · " + r.Id + " " + r.Name
                                + " ⇒ CreateMonster " + string.Join(",", ids.ConvertAll(x => x.ToString()).ToArray()));
                        if (r.Effects.TryGetValue(MapEventEffectType.CreateMonsterOneKind, out ids))
                            esay("   进度 " + p + " · " + r.Id + " " + r.Name
                                + " ⇒ CreateMonsterOneKind " + string.Join(",", ids.ConvertAll(x => x.ToString()).ToArray()));
                    }
                }
                esay("");
                esay("── 每张「能新建」的图官方登记了哪几条（Map.bin 难度行 field 3）──");
                esay("   ★ 档次用 **GameDifficultyType 序号**（0 普通 / 1 困难 / …）查；"
                    + "括号里那个 `难度表 id`（82016 / 8201201 这种）是 `Map.bin` 的字典键，**不是**档次，别拿去查。");
                foreach (MapTemplate t in GameTables.Maps())
                {
                    if (string.IsNullOrEmpty(t.SceneSuffix) || !GameTables.CanBuildDiyFrom(t)) continue;
                    esay("═══ 地图 " + t.Id + " " + t.Name + "（画板 " + t.SceneSuffix
                        + "）难度表 id [" + string.Join(",", t.Difficulties.ConvertAll(x => x.ToString()).ToArray()) + "] ═══");
                    bool any = false;
                    List<int> idxWithEvents = GameTables.DifficultyIndexesWithEvents(t.Id);
                    for (int diff = 0; diff <= 4; diff++)
                    {
                        List<int> ids = MapEventTable.EventsForMapDifficulty(t.Id, diff);
                        bool has = idxWithEvents.Contains(diff);
                        if (ids.Count == 0 && !has) continue;           // 这一档官方什么都没登记 ⇒ 不刷屏
                        esay("   档次 " + diff + "（" + GameTables.DifficultyName(diff) + "）：官方登记 " + ids.Count + " 条"
                            + (ids.Count == 0 ? "" : " ⇒ " + string.Join(", ", ids.ConvertAll(x => x.ToString()).ToArray())));
                        foreach (int id in ids)
                        {
                            MapEventRow r = MapEventTable.Get(id);
                            esay("        " + (r == null ? (id + " ★MapEvent.bin 里没有这一行")
                                : ("进度 " + r.Progress + " · " + r.Name + " · " + MapEventTable.EffectSummary(r))));
                            any = true;
                        }
                    }
                    if (!any) esay("   ⇒ ★ 这张图官方一档都没登记进度事件（空白模板只能是空的事件表）");
                    esay("");
                }
                for (int k = 0; k < args.Length; k++)
                {
                    if (!string.Equals(args[k], "--out", StringComparison.OrdinalIgnoreCase)) continue;
                    if (k + 1 >= args.Length) break;
                    try { File.WriteAllText(args[k + 1], eb.ToString(), new UTF8Encoding(false)); }
                    catch (Exception) { }
                }
                Environment.Exit(0);
                return;
            }

            // ★ 截图用：`--new-from <模板文件名>` —— 先走**真正的新建那条路**
            //   （`LevelDoc.CreateFromOwnTemplate`）把文档造出来，后面 `--shots` 就拍这一份。
            //   用来证明"从空白玩法模板新建出来的关卡，棋盘页画的是官方棋盘"。**不写盘**。
            //   ★ 必须排在 `if (shots)` **之前**。
            for (int i = 0; i < args.Length; i++)
            {
                if (!string.Equals(args[i], "--new-from", StringComparison.OrdinalIgnoreCase)) continue;
                if (i + 1 >= args.Length) { Environment.Exit(2); return; }
                string want = args[i + 1];
                LevelDoc made = null;
                foreach (LevelTemplate t in TemplateCatalog.List())
                    if (string.Equals(t.FileName, want, StringComparison.OrdinalIgnoreCase))
                    {
                        made = LevelDoc.CreateFromOwnTemplate(t.Path, "空白试建", LevelDoc.DiyDifficulty);
                        break;
                    }
                if (made == null)
                {
                    Console.WriteLine("★ --new-from 找不到模板：" + want);
                    Environment.Exit(2);
                    return;
                }
                DiyEditor.MainWindow.Doc = made;
                DiyEditor.MainWindow.RefreshAll();
                try { Console.OutputEncoding = Encoding.UTF8; } catch (Exception) { }
                Console.WriteLine("  --new-from " + want + " ⇒ map=" + made.MapId
                    + " 画板=" + made.BoardSuffix
                    + " 棋盘格=" + (made.Board != null && made.Board.Ok ? made.Board.Nodes.Count : 0)
                    + " 起点=" + made.EffectiveStarts().Count
                    + " 事件=" + made.Diy.Events.Count);
                // ★ 复核用（2026-10-12 内置缓存那一波加的）：这块棋盘的底版**到底是从哪儿读来的**
                //   ——「磁盘」（`棋盘缓存\<画板>.json` / 这份 DIY 自带的那份）还是「内置」（exe 里那份），
                //   外加首格的官方 landType。
                //   ★ 判据就是这两行：把磁盘上 `棋盘缓存\<画板>.json` 改一格再跑，这两行就该跟着变；
                //     把磁盘那份删掉再跑，来源变成"内置"。
                if (made.Board != null)
                {
                    Console.WriteLine("  棋盘底版来源 = " + BoardCache.Label(made.Board.Path)
                        + "（" + made.Board.Path + "）· 提示：" + made.Board.Note);
                    BoardNode b0 = null;
                    foreach (BoardNode x in made.Board.Nodes) { b0 = x; break; }
                    if (b0 != null)
                        Console.WriteLine("  棋盘首格 = 格 " + b0.Id + " 官方 landType " + b0.LandType
                            + "（" + Land.Name(b0.LandType) + "）");
                }
                else
                {
                    Console.WriteLine("  ★ 这块棋盘没出来：" + (made.Board == null ? "(null)" : made.Board.Note));
                }
                continue;
            }

            if (shots)
            {
                // ★ 演示编辑（`--demo-edit <名字>` / `--demo-undo`）已经交给
                //   `ShotRunner.Run` 在**文档打开之后**做（那边的顺序才对：
                //   先 `LevelDoc.Open` 再动手）。开关就存在 `ShotRunner.DemoEdit` 上。
                int code = string.IsNullOrEmpty(ShotRunner.WindowShot)
                    ? ShotRunner.Run(shotWhich, shotDoc, Paths.Under("_截图"), shotSuffix)
                    : ShotRunner.RunWindow(ShotRunner.WindowShot, shotDoc, Paths.Under("_截图"));
                Environment.Exit(code);
                return;
            }
            if (lookOnly) { Environment.Exit(0); return; }

            // ★ 排查用：`--celltest <关卡.json> <格 id> <新 landType>` ——
            //   走一遍"换格 + 拿要写出去的棋盘字节"，把顶层 diy 和改过的节点打出来。
            //   **纯内存，不写任何文件**（就是拿来核"保存时 diy.starts 会不会被清掉"的）。
            for (int i = 0; i < args.Length; i++)
            {
                if (!string.Equals(args[i], "--celltest", StringComparison.OrdinalIgnoreCase)) continue;
                try { Console.OutputEncoding = Encoding.UTF8; } catch (Exception) { }
                if (i + 3 >= args.Length)
                {
                    Console.WriteLine("用法：--celltest <关卡.json> <格 id> <新 landType>");
                    Environment.Exit(2);
                    return;
                }
                int nodeId, newLand;
                int.TryParse(args[i + 2], out nodeId);
                int.TryParse(args[i + 3], out newLand);

                LevelDoc d = LevelDoc.Open(args[i + 1]);
                Console.WriteLine("打开：" + args[i + 1] + "  isDiy=" + d.IsDiy);
                Console.WriteLine("棋盘：" + (d.Board == null ? "(null)" : d.Board.Path));
                Console.WriteLine("换格前：改过 " + d.NodeOverrideCount + " 格 -> "
                    + string.Join("；", d.NodeOverrideLines().ToArray()));
                Console.WriteLine("SetNodeLand(" + nodeId + ", " + newLand + ") = " + d.SetNodeLand(nodeId, newLand));

                byte[] outBytes = d.BoardWriteForTest();
                Console.WriteLine("要写出去的棋盘字节 = " + outBytes.Length);
                JNode r = DiyJson.Parse(Encoding.UTF8.GetString(outBytes));
                JNode diy = DiyJson.Member(r, "diy");
                Console.WriteLine("顶层 diy = " + (diy == null ? "★ 没了！" : "在"));
                if (diy != null)
                    foreach (JMember m in diy.Members)
                        Console.WriteLine("   diy." + m.Key + " = " + DiyJson.Brief(m.Value, 60));
                int n = 0;
                foreach (JNode item in DiyJson.Member(r, "nodes").Items)
                {
                    JNode nd = DiyJson.Member(item, "diy");
                    if (nd == null || DiyJson.Member(nd, "landType") == null) continue;
                    n++;
                    Console.WriteLine("   节点 " + DiyJson.Brief(DiyJson.Member(item, "id"), 8)
                        + "：官方 landType " + DiyJson.Brief(DiyJson.Member(item, "landType"), 8)
                        + " -> diy.landType " + DiyJson.Brief(DiyJson.Member(nd, "landType"), 8));
                }
                Console.WriteLine("带 diy.landType 的节点 = " + n);
                Environment.Exit(0);
                return;
            }

            // ★ 排查 / 出货用：`--newdemo <输出目录>` —— 走**和界面完全相同**的那条
            //   "新建一份 DIY 关卡"的路（`LevelDoc.CreateFromTemplate` + `SaveDiy`），
            //   把产物写出来并把它打屏。纯本地，不碰任何只读目录。
            for (int i = 0; i < args.Length; i++)
            {
                if (!string.Equals(args[i], "--newdemo", StringComparison.OrdinalIgnoreCase)) continue;
                try { Console.OutputEncoding = Encoding.UTF8; } catch (Exception) { }
                if (i + 1 >= args.Length)
                {
                    Console.WriteLine("用法：--newdemo <输出目录> [地图 id]");
                    Environment.Exit(2);
                    return;
                }
                string outDir = args[i + 1];
                int mapId = 82016;
                if (i + 2 < args.Length) int.TryParse(args[i + 2], out mapId);

                // ★★ 红线：这条路**绝不写真的游戏目录**。要求传进来的就是一个"像游戏根"的目录
                //    （沙盒：`_截图\页面演示`），不然直接拒跑。
                if (!GameLocator.LooksLikeGame(outDir))
                {
                    Console.WriteLine("★ 拒跑：--newdemo 的第一个参数必须是「像游戏根」的目录（沙盒），现在给的是：" + outDir);
                    Environment.Exit(4);
                    return;
                }
                GameLocator.Override = outDir;
                GameLocator.Reset();

                MapTemplate tpl = GameTables.Map(mapId);
                if (tpl == null) { Console.WriteLine("Map.bin 里没有地图 " + mapId); Environment.Exit(3); return; }

                LevelDoc made = LevelDoc.CreateFromTemplate(tpl, "演示-新建后", LevelDoc.DiyDifficulty);
                DiySaveResult saved = made.SaveDiy("演示-新建后");
                Console.WriteLine("游戏根（沙盒）：" + GameLocator.GameDir);
                Console.WriteLine("已写出：" + saved.LevelPath + "（" + saved.Bytes + " 字节）");
                Console.WriteLine("棋盘：" + (saved.BoardWritten ? saved.BoardPath : "（没写）"));
                Console.WriteLine("—— 文件原文 ——");
                Console.WriteLine(File.ReadAllText(saved.LevelPath, new UTF8Encoding(false)));
                Environment.Exit(0);
                return;
            }

            // ★ 排查 / 出货用：`--art-list <输出txt>` —— 把编辑器**要用的全部贴图名**列出来
            //   （格子功能 / 怪头像 / 手牌 / 地图预览），交给 `tools\export_art_cache.py`
            //   只读地从游戏自己的 Addressables 包里导到 `5-DIY关卡\贴图缓存\`。
            //   ★ 纯只读 + 只写我们自己的目录，绝不碰游戏目录。
            for (int i = 0; i < args.Length; i++)
            {
                if (!string.Equals(args[i], "--art-list", StringComparison.OrdinalIgnoreCase)) continue;
                try { Console.OutputEncoding = Encoding.UTF8; } catch (Exception) { }
                if (i + 1 >= args.Length)
                {
                    Console.WriteLine("用法：--art-list <输出txt>");
                    Environment.Exit(2);
                    return;
                }
                var rows = ArtList();
                var sb = new StringBuilder();
                sb.Append("# 编辑器要用的贴图名（一行一个，制表符后面是「这是干什么的 / 中文名」）\n");
                sb.Append("# 由 DIY关卡编辑器.exe --art-list 生成；给 tools\\export_art_cache.py 用\n");
                foreach (string[] r in rows)
                    sb.Append(r[0]).Append('\t').Append(r[1]).Append('\t').Append(r[2]).Append('\n');
                File.WriteAllText(args[i + 1], sb.ToString(), new UTF8Encoding(false));
                Console.WriteLine("已写出 " + rows.Count + " 个贴图名 → " + args[i + 1]);
                int have = 0;
                foreach (string[] r in rows) if (GameLocator.Image(r[0]) != null) have++;
                Console.WriteLine("其中现在就能读到的：" + have + " / " + rows.Count);
                Environment.Exit(0);
                return;
            }

            // ★ 排查用：`--map-thumbs [输出目录] [--fresh] [--tidy] [--out 报告文件]` —— 逐张地图模板报
            //   "Map.bin 登记的 f8/f9 是什么、本地有没有、最终画哪张"。这一条就是为了
            //   「部分地图的预览图丢失」这件事能一条命令查清楚。
            //   ★ 纯只读游戏目录 + 只写编辑器自己的缓存目录 / 你指定的报告文件。
            for (int i = 0; i < args.Length; i++)
            {
                if (!string.Equals(args[i], "--map-thumbs", StringComparison.OrdinalIgnoreCase)) continue;
                try { Console.OutputEncoding = Encoding.UTF8; } catch (Exception) { }
                string dumpDir = i + 1 < args.Length && !args[i + 1].StartsWith("--") ? args[i + 1] : null;
                bool fresh = false;
                foreach (string a in args)
                    if (string.Equals(a, "--fresh", StringComparison.OrdinalIgnoreCase)) fresh = true;
                if (fresh)
                {
                    // 重画一遍：先忘掉内存那份，再把落盘的那几张删掉（只删我们自己的缓存文件）
                    MapThumb.ForgetMemory();
                    foreach (MapTemplate t2 in GameTables.Maps())
                    {
                        if (string.IsNullOrEmpty(t2.SceneSuffix)) continue;
                        try
                        {
                            string cp = MapThumb.CachePath(t2.SceneSuffix);
                            if (cp != null && File.Exists(cp)) File.Delete(cp);
                        }
                        catch (Exception) { }
                    }
                }
                // ★ `--tidy`：清掉**已经多余**的渲染图缓存。
                //   判据 = "这一档现在能拿到 `Map.bin` 登记的那张官方美术图了"
                //   ⇒ 渲染图按取材顺序已经用不上，留着只是占地方（~100 KB/张）。
                //   ★ 判据必须走 `MapImage`/`MapSceneImage`（表里登记的），**不能**再拿画板号拼
                //     名字 —— 那正是 82016 当初被误判成"没有官方图"的原因。
                //   只删我们自己缓存目录里的 map_*.png。
                if (Array.Exists(args, delegate (string a) {
                        return string.Equals(a, "--tidy", StringComparison.OrdinalIgnoreCase); }))
                {
                    int removed = 0;
                    foreach (MapTemplate t2 in GameTables.Maps())
                    {
                        if (string.IsNullOrEmpty(t2.SceneSuffix)) continue;
                        try
                        {
                            bool hasOfficial =
                                (t2.MapImage.Length > 0 && Art.ImagePath(t2.MapImage) != null)
                                || (t2.MapSceneImage.Length > 0 && Art.ImagePath(t2.MapSceneImage) != null);
                            if (!hasOfficial) continue;                        // 还要靠它，留着
                            string cp = MapThumb.CachePath(t2.SceneSuffix);
                            if (cp != null && File.Exists(cp)) { File.Delete(cp); removed++; }
                        }
                        catch (Exception) { }
                    }
                    MapThumb.ForgetMemory();
                    Console.WriteLine("已清掉多余的渲染图缓存 " + removed + " 张（这些档现在有 Map.bin 登记的官方美术图）。");
                }
                int made = 0, missing = 0, offi = 0, offiBig = 0, rcache = 0, rnow = 0;
                var tl = new StringBuilder();
                Action<string> emit = delegate (string s)
                {
                    tl.Append(s).Append('\n');
                    Console.WriteLine(s);
                };
                foreach (MapTemplate t in GameTables.Maps())
                {
                    if (string.IsNullOrEmpty(t.SceneSuffix)) continue;
                    ImageSource art = MapThumb.Find(t);
                    string src = MapThumb.SourceOf(t);                    if (art == null) missing++;
                    else if (src.StartsWith("官方美术", StringComparison.Ordinal)) offi++;
                    else if (src.StartsWith("官方大图", StringComparison.Ordinal)) { offi++; offiBig++; }
                    else if (src.StartsWith("渲染缓存", StringComparison.Ordinal)) rcache++;
                    else if (src.StartsWith("现渲染", StringComparison.Ordinal)) { rnow++; made++; }
                    emit(string.Format(
                        "地图 {0,-6} 画板 {1,-6} {2,-14} f8={3,-22}(本地{4}) f9={5,-22}(本地{6}) → {7}",
                        t.Id, t.SceneSuffix, t.Name,
                        t.MapImage.Length > 0 ? t.MapImage : "（表里没登记）",
                        t.MapImage.Length > 0 && Art.ImagePath(t.MapImage) != null ? "有" : "无",
                        t.MapSceneImage.Length > 0 ? t.MapSceneImage : "（表里没登记）",
                        t.MapSceneImage.Length > 0 && Art.ImagePath(t.MapSceneImage) != null ? "有" : "无",
                        art == null ? "（没有缩略图）" : src));
                    if (dumpDir != null && art != null)
                    {
                        try
                        {
                            Directory.CreateDirectory(dumpDir);
                            var bmp = art as BitmapSource;
                            if (bmp != null)
                            {
                                var enc = new PngBitmapEncoder();
                                enc.Frames.Add(BitmapFrame.Create(bmp));
                                using (var fs = new FileStream(
                                    Path.Combine(dumpDir, t.SceneSuffix + "_" + t.Id + ".png"),
                                    FileMode.Create, FileAccess.Write))
                                    enc.Save(fs);
                            }
                        }
                        catch (Exception) { }
                    }
                }
                emit("");
                emit("取材计数：官方美术(f8) " + (offi - offiBig) + " 张 · 官方大图(f9兜底) " + offiBig
                    + " 张 · 渲染缓存 " + rcache + " 张 · 这一趟现渲染 " + rnow + " 张 · 仍然没有 " + missing + " 张。");
                for (int k = 0; k < args.Length; k++)
                {
                    if (!string.Equals(args[k], "--out", StringComparison.OrdinalIgnoreCase)) continue;
                    if (k + 1 >= args.Length) break;
                    try { File.WriteAllText(args[k + 1], tl.ToString(), new UTF8Encoding(false)); }
                    catch (Exception) { }
                }
                Environment.Exit(0);
                return;
            }

            // ★ 排查用：`--monster-art` —— 逐只怪报"最终画哪张图、是哪一路来的"，
            //   重点看那 3 只表里登记了像素小人但本地没下到的（1068/1069/1070）。
            //   ★ 纯只读，不写任何文件。
            for (int i = 0; i < args.Length; i++)
            {
                if (!string.Equals(args[i], "--monster-art", StringComparison.OrdinalIgnoreCase)) continue;
                try { Console.OutputEncoding = Encoding.UTF8; } catch (Exception) { }
                var ml = new StringBuilder();
                Action<string> emit = delegate (string s)
                {
                    ml.Append(s).Append('\n');
                    Console.WriteLine(s);
                };
                int pixel = 0, bust = 0, card = 0, none = 0, missingPixel = 0, unregistered = 0;
                foreach (MonsterRow m in GameTables.Monsters())
                {
                    if (m.AvatarAsset.Length == 0) unregistered++;
                    if (m.AvatarIsPixel) pixel++;
                    else if (m.PixelArtMissing) missingPixel++;
                    else if (Art.ImagePath(m.BustName) != null) bust++;
                    else if (Art.ImagePath(m.CardArtName) != null) card++;
                    else none++;
                    emit(string.Format(
                        "怪 {0,-5} {1,-14} f19={2,-32} 画={3,-32} 来源={4}  立绘={5}",
                        m.Id, m.Name,
                        m.AvatarAsset.Length > 0 ? m.AvatarAsset : "（表里没登记）",
                        Art.ImagePath(m.AvatarName) != null ? m.AvatarName : "（没有图）",
                        m.AvatarSourceNote,
                        // ★ 2026-10-17：图标缓存里那张立绘有没有（`5-DIY关卡\图标缓存\<id>.png`）
                        m.PortraitPath.Length > 0 ? ("图标缓存\\" + m.Id + ".png") : "（没有）"));
                }
                emit("");
                emit("怪共 " + GameTables.Monsters().Count + " 只：像素小人 " + pixel
                    + " · 表里登记了但本地缺图(回落) " + missingPixel
                    + " · 回落半身像 " + bust + " · 回落手牌画 " + card + " · 完全没图 " + none
                    + " · 表里没登记像素图 " + unregistered + "。");
                emit("Monster.bin 里登记了像素头像的：" + (GameTables.Monsters().Count - unregistered) + " 只。");
                // `--out <file>`：把同一份报告写 UTF-8 文件（这台机器控制台是 GBK，中文会花）
                for (int k = 0; k < args.Length; k++)
                {
                    if (!string.Equals(args[k], "--out", StringComparison.OrdinalIgnoreCase)) continue;
                    if (k + 1 >= args.Length) break;
                    try { File.WriteAllText(args[k + 1], ml.ToString(), new UTF8Encoding(false)); }
                    catch (Exception) { }
                }
                Environment.Exit(0);
                return;
            }

            // ★ 排查用：`--land-tex` —— 打印每个 landType 官方点名的平台贴图 + 本地有没有。
            //   用来核"起始点 / 补给站到底画哪张"。
            for (int i = 0; i < args.Length; i++)
            {
                if (!string.Equals(args[i], "--land-tex", StringComparison.OrdinalIgnoreCase)) continue;
                try { Console.OutputEncoding = Encoding.UTF8; } catch (Exception) { }
                var lb = new StringBuilder();
                for (int land = 0; land <= LandTable.MaxLand; land++)
                {
                    string official = Art.OfficialTextureName(land);
                    string stem = Art.PlatformTexture(land);
                    lb.Append(string.Format("landType {0,-3} {1,-12} 官方点名={2,-28} 可用={3,-28} 本地={4}\n",
                        land, Land.Name(land),
                        official ?? "（没有）", stem ?? "（没有）",
                        stem != null && Art.ImagePath(stem) != null ? "有" : "无"));
                }
                Console.WriteLine(lb.ToString());
                for (int k = 0; k < args.Length; k++)
                {
                    if (!string.Equals(args[k], "--out", StringComparison.OrdinalIgnoreCase)) continue;
                    if (k + 1 >= args.Length) break;
                    try { File.WriteAllText(args[k + 1], lb.ToString(), new UTF8Encoding(false)); }
                    catch (Exception) { }
                }
                Environment.Exit(0);
                return;
            }

            // ★ 排查用：`--map-modes` —— 每张地图：画板号 / 属于哪些模式 / 有没有棋盘数据 /
            //   能不能拿来新建（模式 4 PVE 或 12 词条PVE）。定"哪些图能当 DIY 底版"时靠它。
            for (int i = 0; i < args.Length; i++)
            {
                if (!string.Equals(args[i], "--map-modes", StringComparison.OrdinalIgnoreCase)) continue;
                try { Console.OutputEncoding = Encoding.UTF8; } catch (Exception) { }
                var mb = new StringBuilder();
                foreach (MapTemplate t in GameTables.Maps())
                {
                    var modes = GameTables.ModesOfMap(t.Id);
                    bool hasBoard = GameTables.EnsureBoardInfo(t);
                    bool canDiy = GameTables.CanBuildDiyFrom(t);
                    mb.Append(string.Format(
                        "地图 {0,-7} 画板 {1,-6} {2,-16} 模式[{3,-10}] 有棋盘={4} 可新建={5} 难度档[{6}] 难度中文[{7}] 棋盘文件={8}\n",
                        t.Id, string.IsNullOrEmpty(t.SceneSuffix) ? "-" : t.SceneSuffix, t.Name,
                        string.Join(",", modes.ConvertAll(x => x.ToString()).ToArray()),
                        hasBoard ? "是" : "否", canDiy ? "是" : "否",
                        string.Join(",", t.Difficulties.ConvertAll(x => x.ToString()).ToArray()),
                        GameTables.DifficultyTextOf(t),
                        hasBoard ? Path.GetFileName(t.BoardFile) : "-"));
                }
                Console.WriteLine(mb.ToString());
                for (int k = 0; k < args.Length; k++)
                {
                    if (!string.Equals(args[k], "--out", StringComparison.OrdinalIgnoreCase)) continue;
                    if (k + 1 >= args.Length) break;
                    try { File.WriteAllText(args[k + 1], mb.ToString(), new UTF8Encoding(false)); }
                    catch (Exception) { }
                }
                Environment.Exit(0);
                return;
            }

            // ★ 复核用：`--seed-templates` —— 幂等地补两种模板：
            //   ① 目录空时从官方 levels 拷种子（`EnsureSeeded`）；
            //   ② 有官方棋盘但没模板的图，补「空白玩法 + 官方棋盘」模板（`EnsureBlankTemplates`）；
            //   ③ 空白玩法模板补**这张图官方登记的**进度事件骨架（`EnsureTemplateEvents`：
            //      键 = `Map.bin` 难度行 field 3 的事件 id，触发进度与文案照 `MapEvent.bin` / `STRMapEvent.bin`），
            //      并把**上一版**那套"全局开局波骨架"（31001/31002/31003）就地升级过来 ——
            //      只在"逐项等于上一版形状、作者一个字节没动过"时才升级。
            //   只在**缺**的时候补，已存在的**一份都不覆盖**。可反复跑。
            for (int i = 0; i < args.Length; i++)
            {
                if (!string.Equals(args[i], "--seed-templates", StringComparison.OrdinalIgnoreCase)) continue;
                try { Console.OutputEncoding = Encoding.UTF8; } catch (Exception) { }
                var sb2 = new StringBuilder();
                Action<string> say2 = delegate (string s) { sb2.Append(s).Append('\n'); Console.WriteLine(s); };
                say2("模板目录：" + TemplateCatalog.Dir);
                int a = TemplateCatalog.EnsureSeeded();
                say2("① 官方 levels 种子：新拷 " + a + " 份");
                int b = TemplateCatalog.EnsureBlankTemplates();
                say2("② 空白玩法模板：新补 " + b + " 份（有官方棋盘、之前没模板的图）");
                int e2 = 0, up2 = 0;
                e2 = TemplateCatalog.EnsureTemplateEvents(out up2);
                say2("③ 空白玩法模板：新补 " + e2 + " 份 / 就地刷新 " + up2 + " 份"
                    + "（骨架 = 这张图官方登记的进度事件 + **显式空刷怪** start_monsters:[] / spawn:[]；"
                    + "就地刷新只认「我们生成的骨架且作者没动过」那一情形）");
                say2("");
                say2("现在目录里共 " + TemplateCatalog.List().Count + " 份：");
                foreach (LevelTemplate t in TemplateCatalog.List())
                    say2("   " + t.FileName.PadRight(20) + " 地图 " + t.MapId
                        + " · 画板 " + (t.SceneSuffix.Length > 0 ? t.SceneSuffix : "-")
                        + " · 事件 " + t.Events + " · 开局怪 " + t.StartMonsters
                        + " · " + t.Name);
                for (int k = 0; k < args.Length; k++)
                {
                    if (!string.Equals(args[k], "--out", StringComparison.OrdinalIgnoreCase)) continue;
                    if (k + 1 >= args.Length) break;
                    try { File.WriteAllText(args[k + 1], sb2.ToString(), new UTF8Encoding(false)); }
                    catch (Exception) { }
                }
                Environment.Exit(0);
                return;
            }

            // ★ 复核用：`--create-from <模板文件名> [名字]` —— 走**真正的新建那条路**
            //   （`LevelDoc.CreateFromOwnTemplate`），把结果报出来：
            //   地图 / 画板号 / 棋盘格数 / 起点数 / 事件数 / 池子数。**不写任何文件**。
            for (int i = 0; i < args.Length; i++)
            {
                if (!string.Equals(args[i], "--create-from", StringComparison.OrdinalIgnoreCase)) continue;
                try { Console.OutputEncoding = Encoding.UTF8; } catch (Exception) { }
                string dir = TemplateCatalog.Dir;
                var cb = new StringBuilder();
                Action<string> csay = delegate (string s) { cb.Append(s).Append('\n'); Console.WriteLine(s); };
                string want = i + 1 < args.Length ? args[i + 1] : "";
                // ★ 诊断：`--create-from --dbg <模板>` 直接把解析出来的节点打出来
                for (int k = 0; k < args.Length; k++)
                {
                    if (!string.Equals(args[k], "--dbg", StringComparison.OrdinalIgnoreCase)) continue;
                    if (k + 1 >= args.Length || dir.Length == 0) continue;
                    string p2 = Path.Combine(dir, args[k + 1]);
                    if (!File.Exists(p2)) { csay("dbg: 没这个文件 " + p2); continue; }
                    string txt2 = File.ReadAllText(p2, Encoding.UTF8);
                    JNode r2 = DiyJson.Parse(txt2);
                    JNode pe2 = DiyJson.Member(r2, "progress_events");
                    csay("dbg: progress_events kind=" + (pe2 == null ? "null" : pe2.Kind.ToString())
                        + " members=" + (pe2 == null || pe2.Members == null ? -1 : pe2.Members.Count));
                    if (pe2 != null && pe2.Members != null)
                        foreach (JMember m2 in pe2.Members)
                        {
                            JNode tp = DiyJson.Member(m2.Value, "trigger_progress");
                            csay("dbg:   键 " + m2.Key + " 值 kind=" + (m2.Value == null ? "null" : m2.Value.Kind.ToString())
                                + " trigger_progress 节点=" + (tp == null ? "null" : (tp.Kind + " num='" + tp.Num + "'"))
                                + " IntOf=" + DiyJson.IntOf(tp, -1)
                                + " Known(" + m2.Key + ")=" + MapEventTable.Known(int.Parse(m2.Key)));
                        }
                }
                string nm = i + 2 < args.Length && !args[i + 2].StartsWith("--") ? args[i + 2] : "空白试建";
                csay("模板目录：" + dir);
                foreach (LevelTemplate t in TemplateCatalog.List())
                {
                    if (want.Length > 0 && !string.Equals(t.FileName, want, StringComparison.OrdinalIgnoreCase)) continue;
                    try
                    {
                        LevelDoc doc = LevelDoc.CreateFromOwnTemplate(t.Path, nm, LevelDoc.DiyDifficulty);
                        BoardModel bm = doc.Board;
                        var trig = new StringBuilder();
                        foreach (DiyEvent de in doc.Diy.Events)
                            trig.Append(" [").Append(de.Id).Append(" ").Append(de.Name)
                                .Append(" 条目").Append(de.Items.Count);
                        foreach (DiyEvent de in doc.Diy.Events)
                            foreach (DiyProgressItem pi in de.Items)
                                trig.Append(" {ev").Append(de.Id).Append(" p=").Append(pi.TriggerProgress)
                                    .Append(" e=").Append(pi.TriggerEvent)
                                    .Append(" eff=").Append(pi.Effects.Count).Append("}");
                        csay(string.Format(
                            "  {0,-18} 地图 {1,-7} 画板 {2,-6} 棋盘格 {3,-4} 起点 {4}  事件 {5}  怪池 {6}  落点 {7}{8}",
                            t.FileName, doc.MapId, doc.BoardSuffix,
                            bm != null && bm.Ok ? bm.Nodes.Count : 0,
                            doc.EffectiveStarts().Count,
                            doc.Diy.Events.Count, doc.Diy.Pool.Count, doc.Diy.BoardMonsters.Count,
                            trig.ToString()));
                    }
                    catch (Exception ex)
                    {
                        csay("  " + t.FileName + "  ★新建失败：" + ex.Message.Replace('\n', ' '));
                    }
                }
                for (int k = 0; k < args.Length; k++)
                {
                    if (!string.Equals(args[k], "--out", StringComparison.OrdinalIgnoreCase)) continue;
                    if (k + 1 >= args.Length) break;
                    try { File.WriteAllText(args[k + 1], cb.ToString(), new UTF8Encoding(false)); }
                    catch (Exception) { }
                }
                Environment.Exit(0);
                return;
            }

            app.StartupUri = new Uri("MainWindow.xaml", UriKind.Relative);
            app.Run();
        }

        /// <summary>编辑器要用的全部贴图名：{名字, 在哪用, 中文名}。</summary>
        public static List<string[]> ArtList()
        {
            var rows = new List<string[]>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            Action<string, string, string> add = delegate (string n, string where, string cn)
            {
                if (string.IsNullOrEmpty(n) || !seen.Add(n)) return;
                rows.Add(new string[] { n, where, cn ?? "" });
            };

            // ① 格子功能（Land.bin 给每个 landType 点名的平台贴图）
            for (int land = 1; land <= LandTable.MaxLand; land++)
            {
                string n = LandTable.TextureName(land);
                if (n == null) continue;
                add(n, "格子 landType " + land, Land.Name(land));
            }
            // ② 怪头像
            foreach (MonsterRow m in GameTables.Monsters())
                add(m.BustName, "怪头像 monster " + m.Id, m.Name);
            // ③ 怪的手牌画（Monster_Card）
            foreach (MonsterRow m in GameTables.Monsters())
                add("UT_Monster_Card_" + m.Id, "怪手牌画 monster " + m.Id, m.Name);
            // ④ 手牌
            foreach (CardRow c in GameTables.Cards())
                add(CardPickDialog.HandName(c.Id), "手牌 card " + c.Id, c.Name);
            // ⑤ 地图预览（新建关卡窗口里的图）
            foreach (MapTemplate t in GameTables.Maps())
            {
                add(t.ThumbName, "地图缩略图 " + t.SceneSuffix, t.Name);
                add(t.BigName, "地图大图 " + t.SceneSuffix, t.Name);
            }
            return rows;
        }

        protected override void OnExit(ExitEventArgs e)
        {
            CleanupLockOwner();
            try { DiyEditor.MainWindow.SaveRecent(); } catch (Exception) { }
            base.OnExit(e);
        }
    }
}
