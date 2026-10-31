using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace DiyEditor
{
    /// <summary>
    /// 离屏截图：RenderTargetBitmap 直接渲视觉树，不弹窗、不截屏。
    /// 布局用 Measure/Arrange/UpdateLayout 同步跑完，**不进消息泵**（旧版 PushFrame 卡死过）。
    /// 用法：--shots &lt;页码|all&gt; [关卡 json]
    /// </summary>
    public static class ShotRunner
    {
        public const int PageCount = 6;

        private static readonly string[] PageNames =
        {
            "页1-地图模板与新建",
            "页2-棋盘",
            "页3-进度与事件",
            "页4-怪物",
            "页5-校验与保存",
            "页6-导出与说明",
        };

        public static string PageName(int page)
        {
            if (page < 1 || page > PageCount) return "页" + page;
            return PageNames[page - 1];
        }

        private const double W = 1440;
        private const double H = 900;

        /// <summary>截图前先做一步真编辑（--demo-edit 的名字），并记进 MainWindow.Undo。</summary>
        public static string DemoEditName = "";

        /// <summary>做完演示编辑之后再撤销一步（用来拍撤销前后对比）。</summary>
        public static bool DemoUndo;

        /// <summary>把某个 x:Name 的元素滚进视野（--scroll-to）。</summary>
        public static string ScrollTo = "";

        /// <summary>把某个 x:Name 的元素顶到最上面（--top-align）：BringIntoView 滚不到时用平移取景。</summary>
        public static string TopAlign = "";

        /// <summary>--top-align 之后再往上推多少像素。</summary>
        public static double TopAlignOffset;

        /// <summary>
        /// ★ 要拍的**二级窗口**（`--win create|monster|card|monster-pool`）。
        ///   WPF 的模态窗口在离屏渲染里没法"弹出来再拍"，所以这里**只构造不 Show**，
        ///   直接 Measure/Arrange/Render 那棵视觉树 —— 拍到的是同一份真实界面。
        /// </summary>
        public static string WindowShot = "";

        /// <summary>
        /// ★ 渲染高度（`--shot-h 1400`）：进度那一页四块摞起来比 900 px 高，
        ///   用高一点的画布才拍得全（真人用时窗口滚动，内容是一样的）。
        /// </summary>
        public static double ShotHeight = H;

        /// <summary>拍一个离屏窗口（不进消息泵、不 Show）。</summary>
        public static int RunWindow(string which, string docPath, string outDir)
        {
            try
            {
                Directory.CreateDirectory(outDir);
                if (!string.IsNullOrEmpty(docPath) && File.Exists(docPath))
                    MainWindow.Doc = LevelDoc.Open(docPath);

                Window win = MakeWindow(which, docPath);
                if (win == null) { Console.WriteLine("不认识的 --win：" + which); return 2; }

                var root = win.Content as FrameworkElement;
                if (root == null) { Console.WriteLine("窗口没有内容"); return 1; }
                double w = win.Width, h = win.Height;
                for (int round = 0; round < 4; round++)
                {
                    root.Measure(new Size(w, h));
                    root.Arrange(new Rect(0, 0, w, h));
                    root.UpdateLayout();
                    Drain(DispatcherPriority.Render);
                    Drain(DispatcherPriority.Loaded);
                }
                string path = Path.Combine(outDir, WindowName(which) + ".png");
                if (!SaveSized(root, path, w, h)) { Console.WriteLine("★ " + which + " 渲染失败"); return 1; }
                Console.WriteLine("已渲染：" + path);
                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine("渲染失败：" + ex);
                return 1;
            }
        }

        private static string WindowName(string which)
        {
            switch (which)
            {
                case "create": return "窗口-新建关卡";
                case "monster": return "窗口-新增怪物";
                case "monster-pool": return "窗口-新增怪物_从怪物池挑";
                case "card": return "窗口-给所有人发一张牌";
                case "look": return "窗口-外观设置";
                case "cell": return "窗口-编辑这一格的怪";
                default: return "窗口-" + which;
            }
        }

        private static Window MakeWindow(string which, string docPath)
        {
            switch (which)
            {
                case "create":
                    {
                        var d = new CreateLevelDialog("演示关卡");
                        d.ShotPickFirstUsable();
                        return d;
                    }
                case "monster":
                    return new MonsterPickDialog("新增怪物",
                        "点一只怪，然后「添加」：它会出现在指定格子上（显示怪物头像），并进怪物池。");
                case "monster-pool":
                    {
                        DiyData dd = MainWindow.Doc == null ? null : MainWindow.Doc.Diy;
                        List<PoolMonster> pool = dd == null ? new List<PoolMonster>() : dd.Pool;
                        return MonsterPickDialog.ForPool(pool);
                    }
                case "card":
                    {
                        List<CardRow> cs = GameTables.Cards();
                        return new CardPickDialog(cs.Count > 0 ? cs[0].Id : 0);
                    }
                case "look":
                    // ★ 外观设置（背景图 / 玻璃磨砂 / 压暗 / 主题色）。
                    //   拍的时候临时选中一个预设，这样图里能看见"选中的背景 + 读数"。
                    Look.Background = "preset:星海";
                    return new LookDialog();
                case "cell":
                    {
                        // ★ 「编辑这一格的怪」——挑**这一格里放置最多**的那一格拍，
                        //   这样左侧列表 + 右侧像素小人详情都在同一张图里。
                        //   ★ 必须走 `DiyPool.Read(doc)`（权威读法），`doc.Diy` 那份未必填了落点。
                        var doc = MainWindow.Doc;
                        DiyData dd = doc == null ? null : DiyPool.Read(doc);
                        if (dd == null || dd.BoardMonsters.Count == 0)
                        {
                            Console.WriteLine("  --win cell：这份文档没有棋盘放怪记录"
                                + "（doc=" + (doc == null ? "null" : "ok")
                                + " 放置=" + (dd == null ? -1 : dd.BoardMonsters.Count) + "）");
                            return null;
                        }
                        // ★ 挑"某一格上放得最多"的那一处来拍：先找放置总数最高的
                        //   (格, 事件组) 组合，再用**同一个组**去开对话框 ——
                        //   `CellMonsterDialog` 只画"当前组"落在这一格上的怪。
                        var count = new Dictionary<string, int>();
                        var parts = new List<int[]>();
                        foreach (BoardMonsterRef r in dd.BoardMonsters)
                        {
                            string k = r.Node + "|" + r.Event;
                            int c;
                            count.TryGetValue(k, out c);
                            count[k] = c + 1;
                            parts.Add(new int[] { r.Node, r.Event });
                        }
                        int bestNode = -1, bestGrp = 0, bestN = 0;
                        foreach (int[] p in parts)
                        {
                            string k = p[0] + "|" + p[1];
                            int c = count[k];
                            if (c > bestN) { bestN = c; bestNode = p[0]; bestGrp = p[1]; }
                        }
                        if (bestNode < 0) return null;
                        return new CellMonsterDialog(doc, bestNode, bestGrp, "事件 " + bestGrp);
                    }
                default:
                    return null;
            }
        }

        /// <summary>渲一页或全部。返回 0 = 全成功。</summary>
        public static int Run(string which, string docPath, string outDir)
        {
            return Run(which, docPath, outDir, "");
        }

        /// <summary>同上，可以给文件名加后缀（对比图用）。</summary>
        public static int Run(string which, string docPath, string outDir, string suffix)
        {
            int failed = 0;
            try
            {
                Directory.CreateDirectory(outDir);

                if (!string.IsNullOrEmpty(docPath) && File.Exists(docPath))
                {
                    MainWindow.Doc = LevelDoc.Open(docPath);
                    Console.WriteLine("已打开（只读）：" + docPath);
                }
                else if (MainWindow.Doc != null && MainWindow.Doc.Root != null)
                {
                    // ★ `--new-from <模板>` 之类**已经先把文档造好了**（走的是真正的新建那条路），
                    //   这里就别再清空成"还没打开"的空状态了。
                    Console.WriteLine("沿用已造好的文档：map=" + MainWindow.Doc.MapId
                        + " 画板=" + MainWindow.Doc.BoardSuffix);
                }
                else
                {
                    Console.WriteLine("（没给关卡文件：页面显示「还没打开」的空状态）");
                }

                // ★★ 演示编辑：**必须**在文档打开之后（`DemoEdit.Apply` 要 `MainWindow.Doc`）
                if (!string.IsNullOrEmpty(DemoEditName))
                {
                    string said = DemoEdit.Apply(MainWindow.Doc, DemoEditName);
                    Console.WriteLine(said == null
                        ? ("★ --demo-edit 认不出这个名字。" + DemoEdit.Usage())
                        : said);
                }
                if (DemoUndo)
                {
                    string what;
                    int step = MainWindow.Undo.Undo(MainWindow.Doc, null, out what);
                    Console.WriteLine("演示：撤销一步（rc=" + step + "，这一步是「" + what + "」）");
                }

                // ★ 先看有哪些页要渲
                var pages = new List<int>();
                if (string.Equals(which, "all", StringComparison.OrdinalIgnoreCase) || string.IsNullOrEmpty(which))
                {
                    for (int p = 1; p <= PageCount; p++) pages.Add(p);
                }
                else
                {
                    int only;
                    if (int.TryParse(which, out only) && only >= 1 && only <= PageCount) pages.Add(only);
                    else { Console.WriteLine("页码不认：" + which); return 2; }
                }

                // ★ 不 Show 窗口：自己 Measure/Arrange 一块固定尺寸的画布。
                var win = new MainWindow();
                win.Width = W;
                win.Height = ShotHeight;

                var root = win.Content as FrameworkElement;
                if (root == null) { Console.WriteLine("窗口没有内容"); return 1; }

                foreach (int page in pages)
                {
                    try
                    {
                        win.GoPage(page);
                        // ★ 画完第一遍布局之后，有些页会自己再"适应窗口"一次
                        //   （棋盘页就是这样：它的视口宽度要等右栏量完才定）。
                        //   所以第一遍布局完**再清一轮队列 + 再布局一次**，让那次自适应落定。
                        Layout(root);
                        Drain(DispatcherPriority.Render);
                        Drain(DispatcherPriority.Loaded);
                        Drain(DispatcherPriority.Background);
                        if (!string.IsNullOrEmpty(ScrollTo) || !string.IsNullOrEmpty(TopAlign))
                            ShiftIntoView(root);
                        Layout(root);

                        string path = Path.Combine(outDir, PageName(page) + suffix + ".png");
                        if (!Save(root, path)) { failed++; Console.WriteLine("★ 第 " + page + " 页渲染失败"); }
                        else Console.WriteLine("已渲染：" + path);

                        // ★ `--dump-cells`：每一格落在画布哪儿（出"选中/悬停/起点/改过"对照图时靠它精确裁）
                        if (page == 2 && PageBoard.ShotsDumpCells && PageBoard.LastCellRect.Count > 0)
                        {
                            Console.WriteLine("  —— 每一格的画布坐标（--dump-cells）——");
                            foreach (string one in PageBoard.LastCellRect)
                                Console.WriteLine("    " + one);
                        }

                        // ★★ `--board-hit-probe`：先真命中测试、再派事件，验三条点击路径。
                        //   ★ 必须**排在 `--board-hover-count` 前面**：那个探针会反复派发
                        //     进出/点击事件并触发重画，跑完再来做命中测试会拿到过期坐标（实测点变成 (0,0)）。
                        if (page == 2 && PageBoard.ShotsHitProbe && win.PageBoard != null)
                        {
                            Console.WriteLine("  —— 真命中测试 + 点击三路径探针 ——");
                            foreach (string one in win.PageBoard.RunHitProbe())
                                Console.WriteLine("    " + one);
                        }

                        // ★★ `--board-hover-count`：**真路由事件**跑一遍"进格 / 点格 / 离格"，
                        //   数 `SetHover` 与 `Draw` 的次数 —— 这就是那个死循环的守卫。
                        if (page == 2 && PageBoard.ShotsCountHover && win.PageBoard != null)
                        {
                            // ① 只走"鼠标进出"（悬停的嫌疑就在这条链路）——**这里必须 0 次重建**
                            int b1 = PageBoard.DrawCount;
                            int a1 = win.PageBoard.RunHoverProbe(false);
                            int d1 = PageBoard.DrawCount - b1;
                            // ② 再带上左/右键（左键"选中" ⇒ 本来就该重画一次/格，是预期行为）
                            int b2 = PageBoard.DrawCount;
                            int a2 = win.PageBoard.RunHoverProbe(true);
                            int d2 = PageBoard.DrawCount - b2;

                            Console.WriteLine("  —— 悬停/点击 真路由事件自检 ——");
                            Console.WriteLine("    ① 只进/出（" + a1 + " 次操作）：Draw 新增 " + d1
                                + " 次 " + (d1 == 0 ? "✓ 悬停不重建（正确）"
                                                   : "★★ 不合格：悬停引发了重建 = 死循环病根！"));
                            Console.WriteLine("    ② 进/出 + 左键 + 右键（" + a2 + " 次操作）：Draw 新增 " + d2
                                + " 次（左键「选中」每格重画一次是**预期**的；右键不该重画）");
                            Console.WriteLine("    SetHover 累计调用 = " + PageBoard.HoverSetCount
                                + "（" + a1 + " + " + a2 + " 次进出操作对应，量级应相当、不随次数增长）");
                        }

                        // ★ 棋盘页：把"每个怪物头像最后落在哪"打出来（自检用，不靠看截图）
                        if (page == 2 && PageBoard.LastPlacement.Count > 0)
                        {
                            Console.WriteLine("  —— 棋盘上怪物头像的实际落点（画布坐标）——");
                            foreach (string one in PageBoard.LastPlacement)
                                Console.WriteLine("    " + one);
                        }
                    }
                    catch (Exception ex)
                    {
                        failed++;
                        Console.WriteLine("★ 第 " + page + " 页渲染抛异常：" + ex.Message);
                    }
                }
                return failed == 0 ? 0 : 1;
            }
            catch (Exception ex)
            {
                Console.WriteLine("渲染失败：" + ex);
                return 1;
            }
        }

        /// <summary>取景：--scroll-to 滚进视野；--top-align 顶到最上面（BringIntoView 滚不到时用平移）。</summary>
        private static void ShiftIntoView(FrameworkElement root)
        {
            if (!string.IsNullOrEmpty(ScrollTo))
            {
                FrameworkElement hit = Find(root, ScrollTo);
                if (hit == null) Console.WriteLine("  （--scroll-to 找不到 " + ScrollTo + "，跳过）");
                else { try { hit.BringIntoView(); } catch (Exception) { } }
            }
            if (!string.IsNullOrEmpty(TopAlign))
            {
                FrameworkElement hit = Find(root, TopAlign);
                if (hit == null) Console.WriteLine("  （--top-align 找不到 " + TopAlign + "，跳过）");
                else
                {
                    try
                    {
                        // 参考系必须是内容那一层；平移 ScrollViewer 会把视口一起挪走
                        var sv = FindScrollHost(hit) as ScrollViewer;
                        FrameworkElement target = sv != null
                            ? (sv.Content as FrameworkElement) : null;
                        if (target == null) target = (FindScrollHost(hit) ?? root);
                        Point p = hit.TransformToAncestor(target).Transform(new Point(0, 0));
                        double dy = -(p.Y - TopAlignOffset);
                        target.RenderTransform = new System.Windows.Media.TranslateTransform(0, dy);
                        Console.WriteLine("  （--top-align " + TopAlign + "：内容上移 "
                            + dy.ToString("0", System.Globalization.CultureInfo.InvariantCulture) + " px）");
                    }
                    catch (Exception ex) { Console.WriteLine("  （--top-align 失败：" + ex.Message + "）"); }
                }
            }
            for (int k = 0; k < 3; k++)
            {
                try { root.UpdateLayout(); } catch (Exception) { }
                Drain(DispatcherPriority.Loaded);
            }
        }

        /// <summary>往上找一个 ScrollViewer（平移取景时当坐标参考）。</summary>
        private static FrameworkElement FindScrollHost(DependencyObject node)
        {
            DependencyObject cur = node;
            while (cur != null)
            {
                var sv = cur as ScrollViewer;
                if (sv != null) return sv;
                cur = System.Windows.Media.VisualTreeHelper.GetParent(cur);
            }
            return null;
        }

        private static FrameworkElement Find(DependencyObject node, string name)
        {
            if (node == null) return null;
            var fe = node as FrameworkElement;
            if (fe != null && string.Equals(fe.Name, name, StringComparison.Ordinal)) return fe;
            int n = System.Windows.Media.VisualTreeHelper.GetChildrenCount(node);
            for (int k = 0; k < n; k++)
            {
                FrameworkElement got = Find(System.Windows.Media.VisualTreeHelper.GetChild(node, k), name);
                if (got != null) return got;
            }
            return null;
        }

        /// <summary>
        /// <summary>同步把布局跑完（不进消息泵）。跑到宽高不再变为止，最多 6 轮。</summary>
        private static void Layout(FrameworkElement root)
        {
            double lastW = -1, lastH = -1;
            for (int round = 0; round < 6; round++)
            {
                root.Measure(new Size(W, ShotHeight));
                root.Arrange(new Rect(0, 0, W, ShotHeight));
                root.UpdateLayout();

                Drain(DispatcherPriority.Render);
                Drain(DispatcherPriority.Loaded);

                root.Measure(new Size(W, ShotHeight));
                root.Arrange(new Rect(0, 0, W, ShotHeight));
                root.UpdateLayout();

                if (Math.Abs(root.ActualWidth - lastW) < 0.5 && Math.Abs(root.ActualHeight - lastH) < 0.5
                    && round >= 1)
                    break;
                lastW = root.ActualWidth;
                lastH = root.ActualHeight;
            }
        }

        /// <summary>把"渲染优先级及更高"排队的活儿清一遍。用 Invoke，不用 PushFrame（后者卡死过）。</summary>
        private static void Drain(DispatcherPriority priority)
        {
            try
            {
                Dispatcher.CurrentDispatcher.Invoke(new Action(delegate { }), priority);
            }
            catch (Exception) { }
        }

        private static bool Save(FrameworkElement root, string path)
        {
            return SaveSized(root, path, W, ShotHeight);
        }

        private static bool SaveSized(FrameworkElement root, string path, double w, double h)
        {
            try
            {
                var bmp = new RenderTargetBitmap((int)Math.Round(w), (int)Math.Round(h),
                    96, 96, PixelFormats.Pbgra32);
                bmp.Render(root);
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bmp));
                using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write))
                    encoder.Save(fs);
                return new FileInfo(path).Length > 1000;      // 太小说明渲出了空白
            }
            catch (Exception ex)
            {
                Console.WriteLine("  写 PNG 失败：" + ex.Message);
                return false;
            }
        }
    }
}
