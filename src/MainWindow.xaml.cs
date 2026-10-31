using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace DiyEditor
{
    public partial class MainWindow : Window
    {
        /// <summary>★ 整个程序只认这一个文档对象：谁改了它，页面收到通知自己重画。</summary>
        public static LevelDoc Doc = new LevelDoc();

        /// <summary>全局撤销 / 重做栈（覆盖换格 / 放怪 / 删怪 / 改字段 / 效果条目 + 视图）。</summary>
        public static readonly UndoStack Undo = new UndoStack();

        private static List<string> _recent = new List<string>();

        private int _page = 1;

        public MainWindow()
        {
            InitializeComponent();
            LoadRecent();
            // ★ 实时刷新（第 9 波 ③）：DIY 侧数据一变（怪物池 / 进度 / 棋盘放怪），
            //   主窗口把版本号报到状态行上；各页在"变回可见"时自己对版本号重建。
            DiyNotify.Changed += OnDiyChanged;
            // ★ 外观设置窗口改一项 → 这里立刻跟着重画（磨砂 / 背景图 / 压暗都是实时的）。
            Look.Changed += ApplyLook;
            ApplyLook();
            RefreshAll();
        }

        // ============================================================ ★★ 外观（背景图 + 玻璃磨砂）
        /// <summary>
        /// 把「外观」**真画出来**：背景渐变 + 背景图 + 压暗 + 磨砂。
        /// 口径照 `1-首次安装包\启动器`（只读参考）的 `ApplyBackdrop()` / `ApplyFrost()`：
        ///   * 磨砂**只写全局值**，让每块玻璃自己重画（`GlassSurface.GlobalFrost` 的广播）——
        ///     不遍历视觉树，那样会漏掉按钮模板里长出来的药丸；
        ///   * 背景图**不挂 BlurEffect**，图上只有"压暗"那一层。
        /// </summary>
        public void ApplyLook()
        {
            BuildBackdropArt();

            if (BgImage != null)
            {
                ImageSource art = Look.LoadBackground(Look.Background);
                if (art != null)
                {
                    BgImage.Source = art;
                    BgImage.Visibility = Visibility.Visible;
                }
                else
                {
                    // 没有背景图 / 图读不出来 ⇒ 藏起来，露出底下那层主题色渐变。
                    BgImage.Source = null;
                    BgImage.Visibility = Visibility.Collapsed;
                }
            }

            if (DimLayer != null)
                DimLayer.Opacity = Look.Clamp(Look.Dim, 0, Look.DimMax);

            // ★ 背景层是整个窗口最底下那一层 —— 磨砂取样就是它。
            GlassSurface.GlobalFrost = Look.Clamp(Look.Frost, 0, Look.FrostMax);
        }

        /// <summary>
        /// 没自定义背景图（或图读不出来）时，用主题色铺一层 Material You 味的渐变。
        /// ★ 照启动器 `BuildBackdropArt()`：一块底色渐变 + 三团径向光晕，
        ///   「主页的大胶囊」那种 surface 感就是从这儿来的。
        /// </summary>
        private void BuildBackdropArt()
        {
            if (BackdropArt == null) return;
            BackdropArt.Children.Clear();

            Color accent = Look.AccentColor;
            Color warm = Look.WarmOf(accent);
            Color dark = Color.FromRgb(0x07, 0x09, 0x11);

            var baseBrush = new System.Windows.Media.LinearGradientBrush
            {
                StartPoint = new Point(0, 0),
                EndPoint = new Point(1, 1),
            };
            baseBrush.GradientStops.Add(new System.Windows.Media.GradientStop(Look.Mix(dark, accent, 0.22), 0));
            baseBrush.GradientStops.Add(new System.Windows.Media.GradientStop(Look.Mix(dark, accent, 0.06), 0.55));
            baseBrush.GradientStops.Add(new System.Windows.Media.GradientStop(Look.Mix(dark, warm, 0.12), 1));
            BackdropArt.Children.Add(new System.Windows.Shapes.Rectangle { Fill = baseBrush });

            BackdropArt.Children.Add(Blob(accent, 0.14, 0.06, 0.85, 0.34));
            BackdropArt.Children.Add(Blob(warm, 0.92, 0.92, 0.80, 0.26));
            BackdropArt.Children.Add(Blob(accent, 0.78, 0.12, 0.55, 0.18));
        }

        /// <summary>一团径向光晕（中心浓、边缘全透）。</summary>
        private static System.Windows.Shapes.Rectangle Blob(Color color, double cx, double cy,
                                                            double radius, double alpha)
        {
            var brush = new System.Windows.Media.RadialGradientBrush
            {
                Center = new Point(cx, cy),
                GradientOrigin = new Point(cx, cy),
                RadiusX = radius,
                RadiusY = radius,
            };
            brush.GradientStops.Add(new System.Windows.Media.GradientStop(
                Look.Alpha(color, (byte)Math.Round(alpha * 255)), 0));
            brush.GradientStops.Add(new System.Windows.Media.GradientStop(Look.Alpha(color, 0), 1));
            return new System.Windows.Shapes.Rectangle { Fill = brush };
        }

        /// <summary>顶栏那颗「外观」按钮 → 外观设置窗口。</summary>
        private void OnOpenLook(object sender, RoutedEventArgs e)
        {
            var dlg = new LookDialog();
            dlg.Owner = this;
            dlg.ShowDialog();
            ApplyLook();
            if (dlg.Dirty)
            {
                SaveRecent();
                NavDirty.Text = "外观已改：背景 " + Look.DescribeBackground()
                    + " · 磨砂 " + Look.Num(Look.Frost) + " · 压暗 " + Look.Num(Look.Dim);
            }
        }

        /// <summary>数据变更通知的落点（★ 只更新一行字，绝不再回头触发写盘 —— 不做递归）。</summary>
        private void OnDiyChanged()
        {
            try
            {
                if (NavDirty == null) return;
                NavDirty.Text = "怪物池 / 进度 已更新（第 " + DiyNotify.Version + " 次变更）";
            }
            catch (Exception) { }
        }

        // ============================================================ 页面切换
        private void OnNavChanged(object sender, RoutedEventArgs e)
        {
            var rb = sender as System.Windows.Controls.RadioButton;
            if (rb == null) return;
            switch ((string)rb.Tag)
            {
                case "templates": GoPage(1); break;
                case "board": GoPage(2); break;
                case "story": GoPage(3); break;
                case "monsters": GoPage(4); break;
                case "lint": GoPage(5); break;
                case "export": GoPage(6); break;
            }
        }

        // ★ 导航就是设计稿 v2 §① 的那 6 页，一页只干一件事：
        //   ① 地图模板 / 新建   ② 棋盘（主工作区）   ③ 进度 / 事件
        //   ④ 怪物（怪物编辑器） ⑤ 校验 / 保存        ⑥ 导出 / 说明
        public void GoPage(int page)
        {
            _page = page;
            PageTemplates.Visibility = page == 1 ? Visibility.Visible : Visibility.Collapsed;
            PageBoard.Visibility = page == 2 ? Visibility.Visible : Visibility.Collapsed;
            PageStory.Visibility = page == 3 ? Visibility.Visible : Visibility.Collapsed;
            PageMonsters.Visibility = page == 4 ? Visibility.Visible : Visibility.Collapsed;
            PageLint.Visibility = page == 5 ? Visibility.Visible : Visibility.Collapsed;
            PageExport.Visibility = page == 6 ? Visibility.Visible : Visibility.Collapsed;

            SyncNav(page);

            // ★ 切到哪一页就让它自己重读一遍：编辑器改了东西，别的页立刻看得到
            //   （不靠事件通知，简单可靠）。
            try
            {
                if (page == 1) PageTemplates.Reload();
                if (page == 2) PageBoard.Reload();
                if (page == 3) PageStory.Reload();
                if (page == 4) PageMonsters.Reload();
                if (page == 5) PageLint.Reload();
                if (page == 6) PageExport.Reload();
            }
            catch (Exception) { }
        }

        /// <summary>把左边的选中态跟当前页对齐（离屏截图/程序切页时也要对）。</summary>
        private void SyncNav(int page)
        {
            var navs = new System.Windows.Controls.RadioButton[]
            { Nav1, Nav2, Nav3, Nav4, Nav5, Nav6 };
            for (int k = 0; k < navs.Length; k++)
            {
                if (navs[k] == null) continue;
                bool want = (k + 1) == page;
                if (navs[k].IsChecked != want) navs[k].IsChecked = want;
            }
        }

        /// <summary>旧名字，留着给别的页调用（= GoPage）。</summary>
        public void Go(int page) { GoPage(page); }

        /// <summary>打开成功之后由页面调用：刷新所有页 + 侧栏状态 + 最近列表。</summary>
        public static void RefreshAll()
        {
            MainWindow win = Application.Current != null ? Application.Current.MainWindow as MainWindow : null;
            if (win == null) return;
            win.RefreshAllCore();
        }

        private void RefreshAllCore()
        {
            bool open = Doc != null && Doc.Root != null;
            TitleFile.Text = open ? Doc.FileName + (Doc.ReadOnlyDemo ? "（只读样例）" : "") : "";
            NavDocState.Text = open
                ? "已打开：" + Doc.FileName + "\nmap=" + Doc.MapId + " · 难度=" + Doc.Difficulty
                : "还没打开关卡";
            NavDirty.Text = open && Doc.Changed ? "● 有未导出的改动" : "";
            // ★ 上面那几行只是改字（不换控件），当场做没关系；
            //   **整页重建推迟**：见 QueueRebuild 的注释 —— 当场重建会把正被按着的那颗按钮换掉。
            QueueRebuild();
        }

        /// <summary>这一次输入处理里已经排过一次重建了（多次请求合并成一次）。</summary>
        private bool _rebuildQueued;

        /// <summary>
        /// ★★ 把「整页重建」推迟到**这一次输入处理走完之后**再做（并合并多次请求）。
        ///
        /// 病根（用户当场报的"进度页面怎么什么都点不动了"）：
        ///   真人按下鼠标那一刻，WPF 的 `ButtonBase.OnMouseLeftButtonDown` 是
        ///   **先 `Focus(这颗按钮)`、`OnMouseLeftButtonUp` 才发 `Click`**。
        ///   这一个 `Focus()` 会把原来那个输入框的焦点拿走 ⇒ `LostFocus` ⇒
        ///   `OnLimitChanged` / 条词数值框的 `AfterEdit` ⇒ `SaveDiy()` + `RefreshAll()`。
        ///   而 `RefreshAll()` 当场就把整页重建（`ItemList.Children.Clear()` + 重新建一批按钮）
        ///   ⇒ **正被按着的那颗按钮在按下的那一瞬间就被换掉了** ⇒
        ///   鼠标抬起找不到落点（capture 也跟着没了）⇒ `Click` 永远不发 ⇒「点了没反应」。
        ///   键盘 Tab 走同理（焦点元素当场被换掉，输入焦点丢到虚空）。
        ///
        /// 修法（最小改动，**不牺牲"实时刷新"**）：
        ///   `SaveDiy()` 照旧**当场**落地（数据一点不推迟），只有**重建**排到
        ///   `DispatcherPriority.Background` —— 它比输入优先级（`Input`）低，
        ///   所以鼠标抬起 / 这次点击的处理器一定先跑完，重建随后就到，"实时刷新"照样有。
        /// </summary>
        private void QueueRebuild()
        {
            if (_rebuildQueued) return;
            _rebuildQueued = true;
            try
            {
                Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(delegate
                {
                    _rebuildQueued = false;
                    try { GoPage(_page); }
                    catch (Exception) { }
                }));
            }
            catch (Exception)
            {
                // 排不进去（例如消息泵已经在关）就别把标志卡住：下次改动还要能刷新。
                _rebuildQueued = false;
            }
        }

        public void NoteStatus(string text)
        {
            NavDirty.Text = text;
        }

        // ============================================================ 最近打开
        private static string ConfigPath
        {
            get { return Path.Combine(Paths.Base, "编辑器配置.json"); }
        }

        public static List<string> Recent { get { return _recent; } }

        private void LoadRecent()
        {
            _recent = new List<string>();
            try
            {
                if (!File.Exists(ConfigPath)) return;
                JNode root = DiyJson.Parse(File.ReadAllText(ConfigPath, Encoding.UTF8));
                JNode list = DiyJson.Member(root, "recent");
                if (list != null && list.Kind == JKind.Array)
                {
                    foreach (JNode item in list.Items)
                        if (item.Kind == JKind.String && item.Str.Length > 0) _recent.Add(item.Str);
                }
                // ★ 用户手动指定过的游戏目录（只读探测失败时的兜底）
                //   ★★ 2026-10-17：`game_dir` / `table_dir` 两个键**统一由 GameLocator 解析**
                //      （`App.Main` 已经先读过一次；这里再读一次 = "改完配置重开主窗口立刻生效"）。
                GameLocator.LoadEditorConfig(ConfigPath);

                // ★★ 外观（背景图 / 玻璃磨砂 / 压暗 / 主题色）—— 照启动器的
                //    `Settings.Load()`：读不到就用默认，绝不因为配置坏了起不来。
                JNode lk = DiyJson.Member(root, "look");
                if (lk != null && lk.Kind == JKind.Object)
                {
                    JNode v;
                    v = DiyJson.Member(lk, "background");
                    if (v != null && v.Kind == JKind.String) Look.Background = v.Str;
                    v = DiyJson.Member(lk, "frost");
                    if (v != null && v.Kind == JKind.Number)
                        Look.Frost = Look.Clamp(Look.ParseNum(v.Num, Look.FrostDefault), 0, Look.FrostMax);
                    v = DiyJson.Member(lk, "dim");
                    if (v != null && v.Kind == JKind.Number)
                        Look.Dim = Look.Clamp(Look.ParseNum(v.Num, 0), 0, Look.DimMax);
                    v = DiyJson.Member(lk, "accent");
                    if (v != null && v.Kind == JKind.String && Look.IsHexColor(v.Str))
                        Look.Accent = v.Str;
                }
            }
            catch (Exception) { }
        }

        public static void AddRecent(string path)
        {
            _recent.RemoveAll(delegate (string s) { return string.Equals(s, path, StringComparison.OrdinalIgnoreCase); });
            _recent.Insert(0, path);
            while (_recent.Count > 12) _recent.RemoveAt(_recent.Count - 1);
            SaveRecent();
        }

        /// <summary>记下用户手动指的游戏目录（只写我们自己的 编辑器配置.json）。</summary>
        public static void SaveGameDir(string dir)
        {
            GameLocator.Override = dir;
            SaveRecent();
        }

        public static void SaveRecent()
        {
            try
            {
                var sb = new StringBuilder();
                sb.Append("{\n");
                sb.Append("  \"game_dir\": ").Append(DiyJson.EncodeString(GameLocator.Override ?? "")).Append(",\n");
                // ★★ 2026-10-17 新增：表目录（`<游戏根>\server\data\gamedata` 之外的那一份）。
                //   ★ 必须在这里**写回去**：这个函数是整份重写，不写就会被"记一次最近打开"
                //     顺手抹掉（用户会莫名其妙退回旧表）。
                sb.Append("  \"table_dir\": ").Append(DiyJson.EncodeString(GameLocator.TableDirOverride ?? "")).Append(",\n");
                // ★ 外观（背景图 / 玻璃磨砂 / 压暗 / 主题色）—— 跟启动器同一个键径口径
                sb.Append("  \"look\": {\n");
                sb.Append("    \"background\": ").Append(DiyJson.EncodeString(Look.Background ?? "")).Append(",\n");
                sb.Append("    \"frost\": ").Append(Look.Num(Look.Frost)).Append(",\n");
                sb.Append("    \"dim\": ").Append(Look.Num(Look.Dim)).Append(",\n");
                sb.Append("    \"accent\": ").Append(DiyJson.EncodeString(Look.Accent ?? Look.AccentDefault)).Append("\n");
                sb.Append("  },\n");
                sb.Append("  \"recent\": [");
                for (int k = 0; k < _recent.Count; k++)
                {
                    sb.Append(k == 0 ? "\n" : ",\n");
                    sb.Append("    ").Append(DiyJson.EncodeString(_recent[k]));
                }
                sb.Append(_recent.Count > 0 ? "\n  ]\n}\n" : "]\n}\n");
                File.WriteAllText(ConfigPath, sb.ToString(), new UTF8Encoding(false));
            }
            catch (Exception) { }
        }

        // ============================================================ 拖拽
        private void OnDragOver(object sender, DragEventArgs e)
        {
            e.Effects = DragDropEffects.None;
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
                if (files != null && files.Length > 0 && files[0].EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                    e.Effects = DragDropEffects.Copy;
            }
            e.Handled = true;
        }

        private void OnDropFile(object sender, DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
            string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
            if (files == null || files.Length == 0) return;
            try
            {
                LevelDoc doc = LevelDoc.Open(files[0]);
                Doc = doc;
                AddRecent(doc.Path);
                RefreshAll();
                GoPage(2);          // 拖进来的关卡，下一步多半就是看棋盘
            }
            catch (Exception ex)
            {
                MessageBox.Show("打开失败：\n\n" + ex.Message, "小小兔趴 · DIY 关卡编辑器",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            e.Handled = true;
        }

        // ============================================================ 窗口
        private void OnTitleBarDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2) { OnMaximize(sender, e); return; }
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                try { DragMove(); } catch (InvalidOperationException) { }
            }
        }

        private void OnMinimize(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void OnMaximize(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        }

        private void OnClose(object sender, RoutedEventArgs e)
        {
            if (Doc != null && Doc.Changed)
            {
                MessageBoxResult r = MessageBox.Show(
                    "「" + Doc.FileName + "」有还没导出的改动。\n\n关掉就丢了，确定要关吗？",
                    "小小兔趴 · DIY 关卡编辑器", MessageBoxButton.OKCancel, MessageBoxImage.Warning);
                if (r != MessageBoxResult.OK) return;
            }
            Close();
        }
    }
}
