using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Path = System.IO.Path;

namespace DiyEditor
{
    /// <summary>
    /// 第②页：棋盘。用户 2026-10-03 的规格：
    ///
    ///   &gt; 点击新建后自动打开棋盘页面
    ///   &gt; 棋盘页面是已经加载好的地图预设，上面新增**是否显示怪物的按钮**
    ///   &gt; **左键点击棋盘格子会在右侧出现各种功能格子，点击后即可更换（要可视化图标）**
    ///   &gt; **右键出现右键菜单，里面有新增怪物，还原原版**
    ///   &gt; 点击新增怪物后会出现二级菜单窗口，里面有各种怪物，支持分类，
    ///   &gt; 点击添加后在棋盘指定格子显示怪物头像，**怪物添加到怪物池里面**
    /// </summary>
    public partial class PageBoard : UserControl
    {
        private BoardModel _board;

        // ── 视图状态：世界坐标 → 画布坐标   p' = p * _scale + _offset
        private double _scale = 1.0;
        private double _offsetX, _offsetY;

        private UndoStep _pendingView;
        private readonly ViewLink _viewLink;

        // ── 左键拖拽
        private bool _dragging;
        private Point _dragFrom;
        private double _dragOx, _dragOy;
        private bool _moved;

        private bool _drawn;
        private bool _needRefit;

        /// <summary>这块棋盘的格距（世界单位）—— 节点大小按它算。</summary>
        private double _pitchUnits = DefaultPitchUnits;

        /// <summary>生效的起点顺序（起点 1~4 各自的格子 id）。</summary>
        private List<int> _starts = new List<int>();

        private int _ctxNode = -1;
        private ContextMenu _ctxMenu;

        /// <summary>选中的格子（右侧「格子」编的就是它）；-1 = 没选。</summary>
        private int _selNode = -1;

        /// <summary>鼠标现在压在哪一格（悬停高亮用；-1 = 没有）。</summary>
        private int _hoverNode = -1;

        /// <summary>
        /// ★★ 每一格的"悬停环"（格 id → 那个 `Ellipse`）。
        ///
        /// ★★★ 为什么要有这张表（2026-10-10 P1 回归：**用户更新后棋盘点不动了**）：
        ///   上一版 `SetHover` 是「改 `_hoverNode` → `Draw()`」。而 `Draw()` 第一件事就是
        ///   `BoardCanvas.Children.Clear()` —— **把鼠标底下那一格拆了** ⇒ WPF 立刻对那颗
        ///   已经消失的控件发 `MouseLeave` ⇒ `SetHover(-1)` ⇒ 又 `Draw()` …… 无限循环，
        ///   UI 线程一直忙着重建整盘 ⇒ **真鼠标的点击根本轮不到**（所以
        ///   `--clicktest` 那种**程序化派发**照样 68/0 全绿，它是绕开真实命中/进出链路的）。
        ///
        ///   ⇒ 现在悬停**只切这一个环的 `Visibility`**，一个 `Draw()` 都不调。
        /// </summary>
        private readonly Dictionary<int, Ellipse> _hoverRings = new Dictionary<int, Ellipse>();

        /// <summary>悬停环 / 重建次数的自检计数（`--board-hover-count` 打印；也给自己核对有没有重建风暴）。</summary>
        public static int HoverSetCount;
        public static int DrawCount;

        /// <summary>截图 / 自检用（`--board-hover-count`）：把悬停次数与重建次数打出来。</summary>
        public static bool ShotsCountHover;

        /// <summary>自检用（`--board-hit-probe`）：跑"真命中测试 + 三条点击路径"的探针。</summary>
        public static bool ShotsHitProbe;

        /// <summary>
        /// ★★ 2026-10-08 大改：棋盘现在"按组看" —— 这一眼只看 / 只放哪一组（哪只怪归谁）的怪。
        ///   组的口径：`0` = 开局就在场上（写进 `start_monsters`）；
        ///   其它 = 某个事件的**落盘键**（`DiyPool.EventKeyOf`）—— 写盘 / 读盘都按它认
        ///   （`DiyPool.Write` 里就是拿这个键跟 `BoardMonsterRef.Event` 对账的，两边必须同一个口径）。
        ///
        /// 用户原话："在棋盘那边加个切换进度的功能，切换到这个进度了就显示这个进度刷的怪物，
        /// 然后放置怪物就是在这个回合放置的"。
        /// ★ 这是**视图状态**（切换不改任何 json），但必须活过"整页重建" ⇒ 存成字段，
        ///   Reload 时按它把下拉重新选回来（见 `SyncGroupPick`）。
        /// </summary>
        private int _groupEvent;

        /// <summary>正在往「刷怪归属」下拉里铺选项 —— 别把回填当成用户切换。</summary>
        private bool _fillingGroup;

        /// <summary>截图用（`--board-pick`）：把这一格设成"选中"，离屏截图才拍得到选中态。</summary>
        public static int ShotsPickNode = -1;

        /// <summary>截图用（`--board-hover`）：把这一格设成"悬停"，离屏截图才拍得到悬停态。</summary>
        public static int ShotsHoverNode = -1;

        /// <summary>截图 / 自检用（`--dump-cells`）：把每格的画布坐标打出来，方便精确裁对照图。</summary>
        public static bool ShotsDumpCells;

        /// <summary>
        /// ★★ 自检用：**最后一次 Draw 里每个怪物头像实际落在哪**（画布坐标）。
        ///   一行一只，形如 `格 1 第 1/4 只 中心(321.5,654.3) 格子中心(321.5,654.3) 偏差 0.00`
        ///   —— "头像居中于格子"这件事在**真渲染那一层**也说得清（不只是算术）。
        /// </summary>
        public static readonly List<string> LastPlacement = new List<string>();

        /// <summary>
        /// ★ 自检 / 对照截图用：最后一次 `Draw` 里**每一格落在画布哪儿**：
        ///   `格 18 中心(342.3,541.2) 尺寸 44 landType 1 起始点 改过=否`（画布坐标）。
        ///   出"选中/悬停/起点/改过"四种状态的放大对照图时，靠它精确定位要裁哪一格。
        /// </summary>
        public static readonly List<string> LastCellRect = new List<string>();

        // ── 截图定档（真人用时全是默认值）
        public static bool? ShotsOverrideIds;
        public static bool? ShotsOverrideLinks;
        public static bool ShotsHideHint;
        public static bool? ShotsOverrideMonsters;
        /// <summary>兼容旧开关（这一版的右键菜单只剩两项，不再平铺预览）。</summary>
        public static bool ShotsShowCtxMenu;
        public static double? ShotsNodeRatio;

        public PageBoard()
        {
            InitializeComponent();
            _viewLink = new ViewLink();
            _viewLink.Get = delegate { return new double[] { _scale, _offsetX, _offsetY }; };
            _viewLink.Set = delegate (double[] v) { _scale = v[0]; _offsetX = v[1]; _offsetY = v[2]; };
            Loaded += delegate
            {
                if (_needRefit) { _needRefit = false; FitToWindow(true); }
                Draw();
            };
            // ★ 实时刷新（第 9 波 ③）：别的页改了怪物池 / 进度，本页变回可见时自己重建。
            IsVisibleChanged += delegate
            {
                if (!IsVisible) return;
                if (_seenDiyVersion == DiyNotify.Version) return;
                Reload();
            };
        }

        /// <summary>上一次画的时候 DIY 侧数据的版本号（对不上就说明该重建了）。</summary>
        private int _seenDiyVersion = -1;

        // ================================================================ 生命周期
        public void Reload()
        {
            ApplyShotFlags();
            _seenDiyVersion = DiyNotify.Version;
            LevelDoc doc = MainWindow.Doc;
            if (doc == null || doc.Root == null)
            {
                SubLine.Text = "还没打开关卡。先去「地图模板 / 新建」建一份。";
                TexLine.Text = "";
                BoardNote.Text = "";
                LegendNote.Text = "";
                Legend.Children.Clear();
                SpawnList.Children.Clear();
                SpawnNote.Text = "";
                BoardCanvas.Children.Clear();
                NameBox.Text = "";
                SaveLine.Text = "";
                ZoomLine.Text = "";
                StartsNote.Text = "";
                NodeNote.Text = "左键点棋盘上任意一格：这一格的信息和下面那排「功能格子」就出来了。";
                if (ChangedNote != null) ChangedNote.Text = "";
                if (LandTiles != null) LandTiles.Children.Clear();
                if (NodeEditor != null) NodeEditor.Visibility = Visibility.Collapsed;
                _starts = new List<int>();
                _selNode = -1;
                SyncGroupPick();          // 没关卡时下拉里只剩「开局就在场上（0 只）」
                SyncButtons();
                return;
            }

            _board = doc.Board;
            MeasurePitch();
            DiyData diy = doc.Diy;
            SubLine.Text = doc.FileName + " · map " + doc.MapId
                + (doc.IsDiy ? " · DIY 难度 " + doc.Difficulty : " · 难度 " + doc.Difficulty)
                + (doc.IsDiy ? " · 底版 " + (doc.BaseLevel.Length > 0 ? doc.BaseLevel : "?") : "")
                + " · 怪物池 " + diy.Pool.Count + " 只"
                + (doc.Changed ? " · ● 有未保存的改动" : "");

            DescribeTextures();
            BoardNote.Text = _board == null
                ? "（没有棋盘对象）"
                : (_board.Ok ? _board.Note : _board.Note + "\n\n棋盘画不出来时，这一页只列落点号。");

            if (string.IsNullOrEmpty(NameBox.Text) || NameBox.Text != doc.DiyName)
                NameBox.Text = (doc.IsDiy && doc.DiyName.Length > 0) ? doc.DiyName : "";

            BuildLegend();
            BuildSpawnList(doc);
            BuildStarts(doc);
            SyncGroupPick();              // ★ 按当前数据重铺「刷怪归属」，并把选中项落回 _groupEvent
            if (ShotsPickNode >= 0 && _board != null && _board.Ok && _board.Get(ShotsPickNode) != null)
                _selNode = ShotsPickNode;
            // ★ 截图用：`--board-hover <格id>` 让离屏截图也能拍到"悬停高亮"那圈（真人用是鼠标进出）。
            if (ShotsHoverNode >= 0 && _board != null && _board.Ok && _board.Get(ShotsHoverNode) != null)
                _hoverNode = ShotsHoverNode;
            if (_selNode >= 0 && (_board == null || !_board.Ok || _board.Get(_selNode) == null))
                _selNode = -1;
            UpdateNodeEditor();

            _pendingView = null;
            _scale = 1.0;
            FitToWindow(true);
            _needRefit = true;
            Draw();
        }

        private void DescribeTextures()
        {
            if (_board == null || !_board.Ok)
            {
                TexLine.Text = GameLocator.ImageDir == null
                    ? "找不到游戏目录 ⇒ 读不到贴图，格子画成「色块 + 中文名」。"
                    : "没有棋盘数据，没有格子可画。";
                return;
            }
            var kinds = new List<int>();
            foreach (BoardNode n in _board.Nodes)
                if (!kinds.Contains(n.ShownLand)) kinds.Add(n.ShownLand);
            kinds.Sort();

            int tex = 0;
            var noTex = new List<string>();
            foreach (int k in kinds)
            {
                if (Art.PlatformTexture(k) != null) tex++;
                else noTex.Add(k + " " + Land.Name(k));
            }
            TexLine.Text = "这张棋盘 " + kinds.Count + " 种格子：" + tex + " 种用官方平台贴图"
                + (noTex.Count > 0 ? "；" + noTex.Count + " 种没图 ⇒ 画成「色块 + 中文名」" : "")
                + "。贴图目录：" + (GameLocator.ImageDir ?? "（没找到）")
                // ★ 2026-10-17：表目录也打印出来（`编辑器配置.json` 的 `table_dir` 可改）。
                //   为什么放这一行：这块牌子本来就是"这一页的数据是从哪儿读的"，
                //   怪的名字/头像来自表、格子的贴图来自贴图目录，两件事放一起最省事。
                + "\n" + GameLocator.TableDirLine;
        }

        private void OnHostResize(object sender, SizeChangedEventArgs e)
        {
            if (!_drawn) { FitToWindow(true); Draw(); return; }
            if (_needRefit) { _needRefit = false; FitToWindow(true); }
            Draw();
        }

        // ================================================================ ★ 起始点改用补给站贴图
        /// <summary>
        /// ★★ 起始点那一格改画哪张平台贴图（用户 2026-10-10：「起始点可以改成补给站的贴图
        ///   （但是也要跟现在一样标注好起点1234）」）。
        ///
        /// **为什么是 `UT_Platform_Start`（= `landType 2` 保障点/补给站 的官方贴图）**：
        ///   ① `Land.bin` 给每个 landType 点的贴图（`LandTable.cs:77-80` 那张表，实测 dump 一致）：
        ///        landType **1 起始点** → `UT_Platform_108`
        ///        landType **2 保障点** → `UT_Platform_Start`
        ///   ② 但 `UT_Platform_108` 是**编号角色头像**（一个兔耳角色，260×260），
        ///      不是功能图标 —— `LandTable.StemIfUsable` 专门把这一族**拒掉**
        ///      （见 `LandTable.cs:30` 的说明），所以**起始点原来根本没有贴图**，
        ///      回落成「绿色色块 + 中文名『起始点』」—— 那就是用户看到的那个绿圆。
        ///   ③ 陪跑那条 dump 也印证了「landType 2 保障点退到了 `UT_Platform_Start`（和起始点长得一样）」
        ///      （`LandTable.cs:22`）。
        ///   ⇒ **"补给站那张" = `UT_Platform_Start`**（260×260 的圆底 + 兔耳角色 + 星星）。
        ///
        /// ★ 取不到就回 null ⇒ 起始点照旧走"色块 + 中文名"，不会变成空白。
        /// </summary>
        private static readonly string StartCellTexture = Art.PlatformTexture(2);

        // ================================================================ ★ 格子画成圆的
        /// <summary>
        /// ★★ 格子的**状态描边 = 一个正圆**（用户 2026-10-10：「那边框为什么还是方的」）。
        ///
        /// 病根：描边原来是那个 `Border` 自己画的（`BorderBrush` + `BorderThickness`），
        ///   而它的 `CornerRadius` 是 `node * 0.30`（= **圆角方块**）⇒ 画出来是方框；
        ///   上一轮加的 `EllipseGeometry` 的 `Clip` 只裁**内容**，**裁不掉描边**
        ///   （裁完只是把方框四个角切掉，看四条直边还在，仍然不是圆）。
        ///
        /// 修法（用户给的方案 2，推荐那种）：描边**不再由 `Border` 画**，改成**独立的 `Ellipse`**
        ///   叠在格子上面 —— 椭圆天生就是圆的，`Stretch` 跟着格子尺寸走，
        ///   **不依赖 `SizeChanged`/`Loaded` 的时序**（比"把 CornerRadius 设成半径一半"稳）。
        /// </summary>
        private static Ellipse DiscStroke(double d, Brush brush, double weight)
        {
            var el = new Ellipse();
            el.Width = d;
            el.Height = d;
            el.Stroke = brush;
            el.StrokeThickness = weight;
            el.Fill = null;                 // 只要描边，不要填充（不然会盖住贴图）
            el.HorizontalAlignment = HorizontalAlignment.Center;
            el.VerticalAlignment = VerticalAlignment.Center;
            el.IsHitTestVisible = false;
            return el;
        }

        /// <summary>
        /// 记下鼠标压在哪一格并**只切换悬停环的可见性**。
        ///
        /// ★★★ 绝对不许在这里调 `Draw()` / `RefreshAll()` / 重建格子 —— 见 `_hoverRings` 的长注释：
        ///   重建会把鼠标底下那一格拆掉 ⇒ 立刻收到 `MouseLeave` ⇒ 再重建 …… 死循环，
        ///   整盘一直在重建 ⇒ **真鼠标点不动**（程序化派发的自测发现不了）。
        ///   这里只改 `Visibility`，不碰布局、不碰视觉树结构、不触发任何重画。
        /// </summary>
        private void SetHover(int node)
        {
            if (_hoverNode == node) return;          // 同节点直接 return（防抖）
            HoverSetCount++;

            Ellipse prev;
            if (_hoverNode >= 0 && _hoverRings.TryGetValue(_hoverNode, out prev) && prev != null)
                prev.Visibility = Visibility.Collapsed;

            _hoverNode = node;

            Ellipse now;
            if (node >= 0 && _hoverRings.TryGetValue(node, out now) && now != null)
                now.Visibility = Visibility.Visible;
        }

        /// <summary>把上一次画的悬停环登记清掉（`Draw()` 开头调；环本身随 Children.Clear 一起没了）。</summary>
        private void ClearHoverRings()
        {
            _hoverRings.Clear();
        }

        /// <summary>
        /// ★★ P1 回归探针（`--board-hover-count`）：对画布上每一格**真派发路由事件**
        ///   `MouseEnter → MouseLeftButtonDown → MouseRightButtonDown → MouseLeave`，
        ///   返回派发次数。
        ///
        /// 为什么要"真路由事件"：`--clicktest` 是**程序化调用/派发**，它不经过
        ///   "鼠标进入离开"这条链路，所以上一版"悬停→重建→MouseLeave→再重建"的**死循环它一条都测不出来**。
        ///   这个探针专门走那条链路，并且由外面数 `Draw` 次数 —— 只要悬停引起重建，数字就会涨。
        /// </summary>
        public int RunHoverProbe(bool withClick)
        {
            if (BoardCanvas == null) return 0;
            int acts = 0;
            var cells = new List<Grid>();
            foreach (UIElement u in BoardCanvas.Children)
            {
                var g = u as Grid;
                if (g != null && g.Tag is int) cells.Add(g);
            }
            foreach (Grid g in cells)
            {
                // ① 只走"鼠标进出"这条链路（悬停的嫌疑就在这儿）
                g.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0)
                { RoutedEvent = Mouse.MouseEnterEvent });
                acts++;
                g.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0)
                { RoutedEvent = Mouse.MouseLeaveEvent });
                acts++;

                if (!withClick) continue;

                // ② 再走左键 / 右键（左键会"选中"⇒ 本来就该重画一次；右键只弹菜单）
                g.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
                { RoutedEvent = Mouse.MouseDownEvent });
                acts++;
                g.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Right)
                { RoutedEvent = UIElement.MouseRightButtonDownEvent });
                acts++;
            }
            return acts;
        }

        /// <summary>内容裁圆几何（半径 d/2 的正圆）。</summary>
        private static EllipseGeometry DiscClip(double d)
        {
            return new EllipseGeometry(new Point(d / 2, d / 2), d / 2, d / 2);
        }

        /// <summary>画好就裁圆（`Border` / `Image` 都能用）。★ 必须等 `Loaded` —— `Draw()` 里还没布局，`ActualWidth` 是 0。</summary>
        private static void ClipToDisc(FrameworkElement el)
        {
            // ★ 窗口缩放也要跟着重算（原来只在 Loaded 里裁一次，缩放后几何会过时）。
            el.SizeChanged += delegate
            {
                double w = el.ActualWidth, h = el.ActualHeight;
                if (w <= 0.5 || h <= 0.5) return;
                el.Clip = DiscClip(Math.Min(w, h));
            };
        }

        // ================================================================ ★ 图面开关
        private bool WantMonsters
        {
            get { return ShotsOverrideMonsters ?? (ChkMonsters == null || ChkMonsters.IsChecked == true); }
        }

        private bool WantIds
        {
            get { return ShotsOverrideIds ?? (ChkIds != null && ChkIds.IsChecked == true); }
        }

        private bool WantLinks
        {
            get { return ShotsOverrideLinks ?? (ChkLinks != null && ChkLinks.IsChecked == true); }
        }

        private void OnToggleDraw(object sender, RoutedEventArgs e)
        {
            Draw();
        }

        private void OnNodeSizeChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_fillingSize || NodeSizePick == null) return;
            var item = NodeSizePick.SelectedItem as ComboBoxItem;
            if (item == null) return;
            double ratio;
            if (!double.TryParse((string)item.Tag, NumberStyles.Float, CultureInfo.InvariantCulture, out ratio))
                return;
            if (Math.Abs(ratio - _nodeRatio) < 1e-9) return;
            _nodeRatio = ratio;
            if (SaveLine != null)
                SaveLine.Text = "格子大小改成「" + item.Content + "」（只是画面上的大小，不改任何数据）";
            Draw();
        }

        private void SyncNodeSizePick()
        {
            if (NodeSizePick == null) return;
            _fillingSize = true;
            try
            {
                for (int k = 0; k < NodeSizePick.Items.Count; k++)
                {
                    var item = NodeSizePick.Items[k] as ComboBoxItem;
                    if (item == null) continue;
                    double ratio;
                    if (!double.TryParse((string)item.Tag, NumberStyles.Float, CultureInfo.InvariantCulture, out ratio))
                        continue;
                    if (Math.Abs(ratio - _nodeRatio) < 1e-9) { NodeSizePick.SelectedIndex = k; break; }
                }
            }
            finally { _fillingSize = false; }
        }

        public void ApplyShotFlags()
        {
            if (HintBox != null && ShotsHideHint) HintBox.Visibility = Visibility.Collapsed;
            if (ChkIds != null && ShotsOverrideIds.HasValue && ChkIds.IsChecked != ShotsOverrideIds.Value)
                ChkIds.IsChecked = ShotsOverrideIds.Value;
            if (ChkLinks != null && ShotsOverrideLinks.HasValue && ChkLinks.IsChecked != ShotsOverrideLinks.Value)
                ChkLinks.IsChecked = ShotsOverrideLinks.Value;
            if (ShotsNodeRatio.HasValue) _nodeRatio = ShotsNodeRatio.Value;
            SyncNodeSizePick();
        }

        // ================================================================ ★ 刷怪归属（这一眼看哪一组）
        /// <summary>当前组在界面上叫什么。</summary>
        private string GroupName()
        {
            return GroupNameOf(MainWindow.Doc == null ? null : MainWindow.Doc.Diy, _groupEvent);
        }

        /// <summary>`0` = 开局组；其它 = 那个事件（名字跟着事件走）。</summary>
        internal static string GroupNameOf(DiyData d, int group)
        {
            if (group == 0) return "开局就在场上";
            DiyEvent ev = EventOfGroup(d, group);
            return ev == null ? ("事件 " + group + "（已经没了）")
                              : ("事件 " + group + (ev.Name.Length > 0 ? " " + ev.Name : ""));
        }

        /// <summary>
        /// 组号 → 事件。★ 比的是 `DiyPool.EventKeyOf`（**落盘键**）而不是 `ev.Id`：
        ///   设了触发点的事件，键 = 那条官方事件 id；`BoardMonsterRef.Event` 记的也是这个键
        ///   （`DiyPool.Write` 里拿键跟 `BoardMonsterRef.Event` 对账）。拿 `ev.Id` 来比就会找不到。
        /// </summary>
        internal static DiyEvent EventOfGroup(DiyData d, int group)
        {
            if (d == null || group == 0) return null;
            foreach (DiyEvent ev in d.Events) if (DiyPool.EventKeyOf(ev) == group) return ev;
            return null;
        }

        /// <summary>这一组在棋盘上一共放了几只（= 下拉里那个 N）。</summary>
        internal static int GroupCount(DiyData d, int group)
        {
            if (d == null) return 0;
            int n = 0;
            foreach (BoardMonsterRef r in d.BoardMonsters) if (r.Event == group) n++;
            return n;
        }

        /// <summary>
        /// 重铺「刷怪归属」：选项 = `开局就在场上（N 只）` + 每个事件一条 `事件 &lt;id&gt; &lt;名字&gt;（N 只）`。
        /// ★ 当前组已经不存在了（换了关卡 / 那个事件被删了）就落回开局组 —— 不能让棋盘停在一个
        ///   已经没有的组上（那会变成"画面上什么都没有、放怪也放不进任何事件"）。
        /// </summary>
        private void SyncGroupPick()
        {
            if (GroupPick == null) return;
            DiyData d = MainWindow.Doc == null ? null : MainWindow.Doc.Diy;

            _fillingGroup = true;
            try
            {
                GroupPick.Items.Clear();
                GroupPick.Items.Add(GroupItem(d, 0));
                var keys = new List<int>();
                if (d != null)
                    foreach (DiyEvent ev in d.Events)
                    {
                        int key = DiyPool.EventKeyOf(ev);
                        // ★ 两条事件落到同一个键上时只列一条：写盘那边也是"一个键只留一条"，
                        //   列两条会让用户以为有两组（点了却是同一组）。
                        if (key <= 0 || keys.Contains(key)) continue;
                        keys.Add(key);
                        GroupPick.Items.Add(GroupItem(d, key));
                    }

                bool exists = false;
                foreach (object o in GroupPick.Items)
                {
                    var it = o as ComboBoxItem;
                    if (it != null && it.Tag is int && (int)it.Tag == _groupEvent) { exists = true; break; }
                }
                if (!exists) _groupEvent = 0;
                foreach (object o in GroupPick.Items)
                {
                    var it = o as ComboBoxItem;
                    if (it != null && it.Tag is int && (int)it.Tag == _groupEvent)
                    { GroupPick.SelectedItem = it; break; }
                }
            }
            finally { _fillingGroup = false; }

            UpdateGroupNote();
        }

        private static ComboBoxItem GroupItem(DiyData d, int group)
        {
            var it = new ComboBoxItem();
            it.Content = GroupNameOf(d, group) + "（" + GroupCount(d, group) + " 只）";
            it.Tag = group;
            it.ToolTip = group == 0
                ? "开局就在场上的那一组：写进服务端读的 start_monsters，一开局就站在棋盘上。"
                : "这一组挂在事件 " + group + " 上：写进 progress_events." + group + ".spawn。";
            return it;
        }

        private void UpdateGroupNote()
        {
            if (GroupNote == null) return;
            GroupNote.Text = _groupEvent == 0
                ? "现在只看 / 只放「开局就在场上」这一组（写进 start_monsters）。"
                : ("现在只看 / 只放「" + GroupName() + "」这一组（写进它所辖事件的 spawn）；"
                   + "别的组放在棋盘上的怪这一眼不画。");
        }

        /// <summary>用户真的换了组（不是回填）—— 只重画，不动任何数据。</summary>
        private void OnGroupChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_fillingGroup || GroupPick == null) return;
            var it = GroupPick.SelectedItem as ComboBoxItem;
            if (it == null || !(it.Tag is int)) return;
            int g = (int)it.Tag;
            if (g == _groupEvent) return;
            _groupEvent = g;
            UpdateGroupNote();
            UpdateNodeEditor();          // 右侧「这一格的怪」也跟着换成这一组
            if (SaveLine != null)
                SaveLine.Text = "棋盘改成只看「" + GroupName() + "」这一组（只是画面上的切换，一个字节都没改）。";
            Draw();
        }

        /// <summary>
        /// 截图用：把这一格的**右键菜单那几项**平铺到画布上（跟 `BuildCellMenu` 同一份语义）。
        /// 真人用时 `CtxMenuPreviewBox` 是 `Collapsed`，一个像素都不占。
        /// </summary>
        private void RenderCtxMenuPreview(BoardNode n)
        {
            if (CtxMenuPreviewBox == null || CtxMenuPreview == null) return;
            if (!ShotsShowCtxMenu || ShotsPickNode < 0 || n == null)
            {
                CtxMenuPreviewBox.Visibility = Visibility.Collapsed;
                return;
            }
            DiyData d = MainWindow.Doc == null ? null : MainWindow.Doc.Diy;
            // ★ 和 `BuildCellMenu` 同一个口径：只有**当前组**在这一格上的怪才算"这一格的怪"。
            List<BoardMonsterRef> herd = d == null ? new List<BoardMonsterRef>() : DiyPool.RefsAtNode(d, n.Id, _groupEvent);

            CtxMenuPreview.Children.Clear();
            if (CtxMenuPreviewHead != null)
            {
                CtxMenuPreviewHead.Text = "右键格 " + n.Id + " 弹出的菜单（当前组：" + GroupName()
                    + "）—— 新增怪物 / 这一格上的怪 / 拿下 / 编辑 / 还原原版：";
            }
            CtxMenuPreview.Children.Add(MenuLine(
                herd.Count == 0
                    ? "新增怪物"
                    : ("新增怪物（这一格这一组现在 " + herd.Count + " 只 ⇒ 再追加一只）"), true));
            if (herd.Count > 0)
            {
                CtxMenuPreview.Children.Add(MenuLine(
                    "—— 这一格上的怪（当前组 " + herd.Count + " 只，点就是拿掉这一只）——", false));
                for (int k = 0; k < herd.Count; k++)
                {
                    PoolMonster mm = DiyPool.Find(d.Pool, herd[k].Pool);
                    CtxMenuPreview.Children.Add(MenuLine(
                        "拿掉 " + (k + 1) + "/" + herd.Count + "  "
                        + (mm == null ? ("池内 #" + herd[k].Pool) : mm.Display)
                        + "（血 " + herd[k].Hp + "）", true));
                }
            }
            CtxMenuPreview.Children.Add(MenuLine(
                "编辑这一格的怪…" + (herd.Count == 0 ? "（这一格没怪 ⇒ 灰的）" : ""), herd.Count > 0));
            CtxMenuPreview.Children.Add(MenuLine(
                "拿下这一格的怪（只撤这一组）" + (herd.Count == 0 ? "（没怪 ⇒ 灰的）" : ""), herd.Count > 0));
            CtxMenuPreview.Children.Add(MenuLine(
                "还原原版" + (n.HasDiy ? "" : "（这一格本来就是官方值 ⇒ 灰的）"), n.HasDiy));
            CtxMenuPreviewBox.Visibility = Visibility.Visible;
        }

        private TextBlock MenuLine(string text, bool enabled)
        {
            var t = new TextBlock();
            t.Text = text;
            t.FontSize = 12.5;
            t.Foreground = enabled ? UiBits.Br(this, "Fg") : UiBits.Br(this, "FgFaint");
            t.Margin = new Thickness(10, 6, 14, 6);
            return t;
        }

        private void OnToggleSide(object sender, RoutedEventArgs e)
        {
            if (SideCol == null || BtnSide == null) return;
            bool hide = SideCol.Width.Value > 0;
            SideCol.Width = new GridLength(hide ? 0 : 376);
            if (SideScroll != null) SideScroll.Visibility = hide ? Visibility.Collapsed : Visibility.Visible;
            BtnSide.Content = hide ? "展开侧栏" : "收起侧栏";
            FitToWindow(true);
            Draw();
        }

        // ================================================================ ★ 右键：新增怪物 / 这一格上的怪 / 还原原版
        /// <summary>
        /// ★★ 右键菜单（用户第 9 波的四条里的 ①，2026-10-08 大改又加了两项）：
        ///   · **新增怪物** → 二级窗口（`MonsterPickDialog`）→ 挑一只 → **追加**到这一格（不换掉原来的）
        ///   · **这一格上的怪** → 逐只列出来，每条右键项就是一个「拿掉 #N 名字」（★ 单独删）
        ///   · **编辑这一格的怪…** → 小窗口里改这一格上每只怪的血量 / 防御 / 攻击 / 掉落星币 / 是否反击，
        ///     顺带能「删掉这只」（用户原话："在棋盘里面右键有怪物的格子可以编辑这格里面的怪物"）
        ///   · **拿下这一格的怪** → 整格清掉（★ 只清**当前组**的，别组的一只不动）
        ///   · **还原原版** → 把这一格的功能还原成官方（删掉 `diy.landType`）
        ///   ★★ 「这一格上的怪」= **当前组**在这一格上的怪（`RefsAtNode(d, node, _groupEvent)`）——
        ///      切换进度之后，别的组放在同一格上的怪不出现在这份菜单里，也不会被菜单上的动作碰到。
        ///   ★ 一格上没怪时「编辑这一格的怪…」「拿下这一格的怪」都灰掉（别弹出个空窗口）。
        ///   ★ 「拿掉」只摘棋盘上的这一条引用，**池子里的那一条留着**（会在怪物页「未配置」里出现）。
        /// </summary>
        private ContextMenu BuildCellMenu(int nodeId, BoardNode n)
        {
            var menu = new ContextMenu();

            var add = new MenuItem();
            DiyData d = MainWindow.Doc == null ? null : MainWindow.Doc.Diy;
            List<BoardMonsterRef> here = d == null
                ? new List<BoardMonsterRef>() : DiyPool.RefsAtNode(d, nodeId, _groupEvent);
            add.Header = here.Count == 0
                ? ("新增怪物（进「" + GroupName() + "」）")
                : ("新增怪物（这一格这一组现在 " + here.Count + " 只 ⇒ 再追加一只）");
            add.ToolTip = "挑一只怪，追加到格 " + nodeId + " 的「" + GroupName() + "」这一组"
                + "（这一格这一组原来那几只不动）；新加的这一处自己一份数值，改它不影响别处。";
            add.Click += delegate { AddMonsterHere(nodeId); };
            menu.Items.Add(add);

            menu.Items.Add(new Separator());

            var head = new MenuItem();
            head.Header = "这一格上的怪（" + GroupName() + "）：" + here.Count + " 只";
            head.IsEnabled = false;
            menu.Items.Add(head);

            if (here.Count > 0)
            {
                List<BoardMonsterRef> refs = DiyPool.RefsAtNode(d, nodeId, _groupEvent);
                for (int k = 0; k < refs.Count; k++)
                {
                    BoardMonsterRef capRef = refs[k];
                    PoolMonster m = DiyPool.Find(d.Pool, capRef.Pool);
                    var one = new MenuItem();
                    one.Header = "拿掉 " + (k + 1) + "/" + refs.Count + "  "
                        + (m == null ? ("池内 #" + capRef.Pool) : m.Display)
                        + "（血 " + capRef.Hp + " · 币 " + capRef.Gold + "）";
                    one.ToolTip = "只把这一处从格 " + nodeId + " 的「" + GroupName() + "」里摘掉；"
                        + "同一只怪放在别处的那些、别的组在这一格上的怪都不动，怪种也留着。";
                    one.Click += delegate { RemoveMonsterHere(nodeId, capRef); };
                    menu.Items.Add(one);
                }
            }

            var edit = new MenuItem();
            edit.Header = "编辑这一格的怪…";
            edit.IsEnabled = here.Count > 0;
            edit.ToolTip = here.Count > 0
                ? ("这一格上「" + GroupName() + "」的 " + here.Count
                   + " 只怪：改血量 / 防御 / 攻击 / 掉落星币 / 是否反击，也能删掉其中某一只。")
                : "这一格上（当前组「" + GroupName() + "」）没有怪 ⇒ 没得编辑。";
            edit.Click += delegate { EditMonstersHere(nodeId); };
            menu.Items.Add(edit);

            var clear = new MenuItem();
            clear.Header = "拿下这一格的怪（只撤「" + GroupName() + "」）";
            clear.IsEnabled = here.Count > 0;
            clear.ToolTip = here.Count > 0
                ? ("把「" + GroupName() + "」放在格 " + nodeId + " 上的 " + here.Count
                   + " 只怪全撤掉；别的组的怪一只不动，池子里的几条也留着。")
                : "这一格上（当前组「" + GroupName() + "」）没有怪。";
            clear.Click += delegate { ClearMonstersHere(nodeId); };
            menu.Items.Add(clear);

            menu.Items.Add(new Separator());

            var revert = new MenuItem();
            revert.Header = "还原原版";
            revert.IsEnabled = n != null && n.HasDiy;
            revert.ToolTip = n == null ? "" : ("把「" + Land.Name(n.ShownLand) + "」还原成官方的「"
                + Land.Name(n.EffectiveLand) + "」");
            revert.Click += delegate { ApplyCellLand(nodeId, 0); };
            menu.Items.Add(revert);
            return menu;
        }

        /// <summary>★ 新增怪物的二级窗口 → 挑一只 → **追加**到这一格的当前组（一格多只）。</summary>
        private void AddMonsterHere(int nodeId)
        {
            LevelDoc doc = MainWindow.Doc;
            if (doc == null || doc.Root == null) { SaveLine.Text = "还没打开关卡，放不了怪。"; return; }

            var dlg = new MonsterPickDialog("新增怪物",
                "点一只怪，然后「添加」：它会**追加**到格 " + nodeId + " 的「" + GroupName()
                + "」这一组里（这一格这一组原来那几只不动），并进怪物池。");
            dlg.Owner = Window.GetWindow(this);
            if (dlg.ShowDialog() != true) return;

            MainWindow.Undo.Capture(doc, "新增怪物（格 " + nodeId + "）");
            DiyData d = doc.Diy;
            int had = DiyPool.RefsAtNode(d, nodeId, _groupEvent).Count;
            PoolMonster m = DiyPool.MakePool(d, dlg.Picked.Id);
            DiyPool.AddAt(d, nodeId, m.Id, _groupEvent);      // ★ 追加到**当前组**，不是"哪儿都算"

            // ★★ 当前组是某个**事件**时，顺手保证那个事件里有这只怪的一条「新增怪物」条词
            //    —— 用户口径是"在棋盘上放了就一定会刷"。开局组不用：它本来就写进 start_monsters。
            string tie = "";
            if (_groupEvent != 0)
            {
                DiyEvent ev = EventOfGroup(d, _groupEvent);
                if (ev != null && EnsureSpawnEffect(ev, m.Id))
                    tie = "；顺手在「" + GroupName() + "」里补了一条「新增怪物」（数量 1），这样放了就一定会刷";
            }
            doc.SaveDiy();

            _selNode = nodeId;
            MainWindow.RefreshAll();
            Reload();
            SaveLine.Text = "格 " + nodeId + " 的「" + GroupName() + "」追加了 " + dlg.Picked.Id + " " + dlg.Picked.Name
                + " ⇒ 进怪物池 #" + m.Id + "；这一格这一组现在 "
                + DiyPool.RefsAtNode(d, nodeId, _groupEvent).Count + " 只"
                + (had > 0 ? "（原来 " + had + " 只）" : "") + tie + "。";
        }

        /// <summary>
        /// ★ 保证这个事件里有「这只怪」的刷怪条词（用户口径：棋盘上放了就一定会刷）。
        ///   ★★ 每个事件对每只怪**只留一条** `EffectKind.Spawn`（`Count` = 计划只数）——
        ///      这是 2026-10-08 大改定下的表示法：这只怪在本事件下棋盘上放的每一格由写盘时
        ///      各出一行 `node`，计划只数只在这里记一笔。所以"已经有了"就一个字节都不动。
        ///   返回 true = 这条是这次新加的。
        /// </summary>
        internal static bool EnsureSpawnEffect(DiyEvent ev, int poolId)
        {
            if (ev == null || poolId <= 0) return false;
            foreach (DiyProgressItem it in ev.Items)
                foreach (DiyEffect x in it.Effects)
                    if (x.Kind == EffectKind.Spawn && x.Pool == poolId) return false;

            // 挂到第一条进度上（这一关的第一条 = 玩家最常编的那条）；一条都没有就建一条 N=1 的。
            DiyProgressItem host = ev.Items.Count > 0 ? ev.Items[0] : null;
            if (host == null)
            {
                host = new DiyProgressItem();
                host.N = 1;
                ev.Items.Add(host);
            }
            var made = new DiyEffect();
            made.Kind = EffectKind.Spawn;
            made.Pool = poolId;
            made.Count = 1;
            host.Effects.Add(made);
            return true;
        }

        /// <summary>
        /// ★ 把这一格上的**某一处放置**拿掉。
        /// ★★ 2026-10-09：按**对象**删（`remove` 那一处），不是"按池内序号删第一条" ——
        ///   同一格上可能放了两条同种怪、数值还不一样，按序号删会删错那一条。
        /// ★★ 只在**当前组**里找（`r.Event == _groupEvent`）—— 别的组放在同一格上的怪一只不动。
        /// ★★ 怪种（池子里那一条）**留着** —— 用户规格：删格上的怪 ≠ 从池子里拿掉。
        /// </summary>
        private void RemoveMonsterHere(int nodeId, BoardMonsterRef remove)
        {
            LevelDoc doc = MainWindow.Doc;
            if (doc == null || doc.Root == null || remove == null) return;
            DiyData d = doc.Diy;
            PoolMonster m = DiyPool.Find(d.Pool, remove.Pool);
            string who = (m == null ? ("池内 #" + remove.Pool) : m.Display)
                + "（血 " + remove.Hp + " · 币 " + remove.Gold + "）";

            if (remove.Event != _groupEvent)
            {
                SaveLine.Text = "这一处不属于当前组「" + GroupName() + "」，没动它。";
                return;
            }
            MainWindow.Undo.Capture(doc, "拿掉格 " + nodeId + " 上的一处怪");
            if (!d.BoardMonsters.Remove(remove))
            {
                SaveLine.Text = "格 " + nodeId + " 的「" + GroupName() + "」上没有这一处了。";
                return;
            }
            doc.SaveDiy();
            int left = DiyPool.RefsAtNode(d, nodeId, _groupEvent).Count;
            MainWindow.RefreshAll();
            Reload();
            SaveLine.Text = "已从格 " + nodeId + " 的「" + GroupName() + "」拿掉 " + who + "；这一格这一组还剩 "
                + left + " 只（别的组在这一格上的怪没动）。池子里那一只留着（怪物页「未配置」能看见）。";
        }

        /// <summary>
        /// ★ 拿下这一格上**当前组**的全部怪（整格清掉）—— 用户原话："拿下这一格的怪"。
        ///   ★ 只撤当前组：`ClearNode(d, node, _groupEvent)`（不传组号的那种"整格清空"在这里绝对不能用，
        ///     那会把别组放在同一格上的怪一起抹掉）。
        /// </summary>
        private void ClearMonstersHere(int nodeId)
        {
            LevelDoc doc = MainWindow.Doc;
            if (doc == null || doc.Root == null) return;
            DiyData d = doc.Diy;
            int had = DiyPool.RefsAtNode(d, nodeId, _groupEvent).Count;
            if (had == 0)
            {
                SaveLine.Text = "格 " + nodeId + " 的「" + GroupName() + "」上没有怪。";
                return;
            }

            MainWindow.Undo.Capture(doc, "拿下格 " + nodeId + " 上的怪（" + GroupName() + "）");
            int n = DiyPool.ClearNode(d, nodeId, _groupEvent);
            doc.SaveDiy();
            MainWindow.RefreshAll();
            Reload();
            SaveLine.Text = "已从格 " + nodeId + " 拿下「" + GroupName() + "」的 " + n + " 只怪"
                + "（别的组放在这一格上的怪一只没动；池子里那几条也留着）。";
        }

        /// <summary>
        /// ★ 「编辑这一格的怪…」：弹一个小窗口，把这一格上**当前组**的怪逐处列出来，
        ///   点一处就改**它自己**的五项（血量 / 防御 / 攻击 / 掉落星币 / 是否反击）+「删掉这一处」。
        ///   ★★ 2026-10-09：窗口里改的是**这一处放置**（`BoardMonsterRef`）自己的数值 ——
        ///     同一只怪放在别格 / 别事件的那几处一点都不动（用户："各改各的，互不影响"）。
        ///     怪种（池子里那一条）只当默认值，界面上照实写明。
        ///   ★ 改完 `SaveDiy()` + `RefreshAll()`，所以还开着窗口时服务端那份 json 就已经变了；
        ///     关掉之后本页再 Reload 一遍。
        /// </summary>
        private void EditMonstersHere(int nodeId)
        {
            LevelDoc doc = MainWindow.Doc;
            if (doc == null || doc.Root == null) return;
            DiyData d = doc.Diy;
            if (DiyPool.RefsAtNode(d, nodeId, _groupEvent).Count == 0)
            {
                // 菜单项本来就是灰的；万一走到这儿也别弹一个空窗口骗人。
                SaveLine.Text = "格 " + nodeId + " 的「" + GroupName() + "」上没有怪，没什么可编辑的。";
                return;
            }

            var dlg = new CellMonsterDialog(doc, nodeId, _groupEvent, GroupName());
            dlg.Owner = Window.GetWindow(this);
            dlg.ShowDialog();

            _selNode = nodeId;
            MainWindow.RefreshAll();
            Reload();
            if (dlg.Dirty)
            {
                DiyData now = MainWindow.Doc == null ? d : MainWindow.Doc.Diy;
                SaveLine.Text = "格 " + nodeId + " 的「" + GroupName() + "」改过了，现在这一格这一组有 "
                    + DiyPool.RefsAtNode(now, nodeId, _groupEvent).Count + " 处放置（各是各的数值）。";
            }
        }

        // ================================================================ 画棋盘
        private void Draw()
        {
            if (BoardCanvas == null) return;
            _drawn = true;
            DrawCount++;
            BoardCanvas.Children.Clear();
            ClearHoverRings();
            LastCellRect.Clear();
            if (_board == null || !_board.Ok) return;
            if (CanvasHost == null || CanvasHost.ActualWidth <= 2 || CanvasHost.ActualHeight <= 2) return;

            double pitch = PitchPixels();
            double node = NodeSize(pitch);
            double half = node / 2;

            var pos = new Dictionary<int, Point>();
            foreach (BoardNode n in _board.Nodes)
                pos[n.Id] = new Point(n.X * _scale + _offsetX, n.Z * _scale + _offsetY);

            // ① 邻接连线
            if (WantLinks) DrawLinks(pos, pitch);

            // ①b 起点的绿色光晕
            if (node >= 8)
            {
                foreach (BoardNode n in _board.Nodes)
                {
                    if (BoardModel.StartNumber(_starts, n.Id) <= 0) continue;
                    Point sp = pos[n.Id];
                    double d = node * 1.22;
                    var halo = new Ellipse();
                    halo.Width = d;
                    halo.Height = d;
                    halo.Fill = new SolidColorBrush(Color.FromArgb(44, 99, 230, 168));
                    halo.Stroke = new SolidColorBrush(Color.FromArgb(135, 99, 230, 168));
                    halo.StrokeThickness = Math.Max(1.0, node * 0.035);
                    halo.IsHitTestVisible = false;
                    Canvas.SetLeft(halo, sp.X - d / 2);
                    Canvas.SetTop(halo, sp.Y - d / 2);
                    BoardCanvas.Children.Add(halo);
                }
            }

            // ② 怪物落点（棋盘上的怪 + 池子里有没有落空的）
            // ★ 一格可以有多只 ⇒ 这里存"每格一串"，不是"每格一只"。
            // ★★ 2026-10-08：**只收当前组**（`r.Event == _groupEvent`）—— 用户口径就是
            //    "切换到这个进度了就显示这个进度刷的怪物"，所以别的组放在同一格上的怪这一眼不画。
            DiyData diy = MainWindow.Doc == null ? new DiyData() : MainWindow.Doc.Diy;
            //   ★ 2026-10-09：存的是**放置条目**（不是怪种）—— 每一处自己带一份数值，
            //     画出来的那些角标/悬停数值要按**它自己**那份说。
            var here = new Dictionary<int, List<BoardMonsterRef>>();
            int boardMonsterCount = 0;
            foreach (BoardMonsterRef r in diy.BoardMonsters)
            {
                if (r.Event != _groupEvent) continue;
                PoolMonster m = DiyPool.Find(diy.Pool, r.Pool);
                if (m == null) continue;
                List<BoardMonsterRef> slot;
                if (!here.TryGetValue(r.Node, out slot)) { slot = new List<BoardMonsterRef>(); here[r.Node] = slot; }
                slot.Add(r);
                boardMonsterCount++;
            }

            // ③ 节点
            var startBadges = new List<int>();
            foreach (BoardNode n in _board.Nodes)
            {
                Point p = pos[n.Id];
                int land = n.ShownLand;
                string stem = Art.PlatformTexture(land);
                ImageSource art = stem == null ? null : Art.LoadName(stem);

                var cell = new Grid();
                cell.Width = node;
                cell.Height = node;
                // ★★ 2026-10-10 P1：给格子一个**看不见但能接鼠标**的表面。
                //
                // 为什么必须有：`Brushes.Transparent` 参与命中测试，`null` **不参与**。
                //   上一轮去掉"色块底"（有贴图时 `box.Background` 不铺）之后，
                //   格子上就没有任何可命中元素了 ⇒ 左键穿过去落到外层容器
                //   ⇒ 启动拖动平移、光标变 `SizeAll`，**格子永远选不中**（用户："点上去直接变成拖动的那个鼠标图标了"）。
                //   ★ 透明 ≠ 有底色：视觉上依旧**一丁点底色都没有**，它只是"接点击"的。
                //   ★ 也让贴图透明的**四角**照样可点（整格方形区域都能点），比只点圆内更好按。
                cell.Background = System.Windows.Media.Brushes.Transparent;

                int startNo = BoardModel.StartNumber(_starts, n.Id);
                if (startNo > 0) startBadges.Add(n.Id);

                // ★★ 2026-10-10 用户：「起始点可以改成补给站的贴图（但是也要跟现在一样标注好起点1234）」
                //   起始点那一档（landType 1 = `Land_Born`，官方贴图 `UT_Platform_Start`）改画
                //   **补给站**的贴图（landType 2 = `Land_FillingStation`，官方贴图 `UT_Platform_TeleportPoint`）。
                //   ★ 只改"这一格画哪张贴图"：landType 本身、`起点N` 绿徽标、绿色描边、
                //     起点光晕、右键换格 / 还原语义**一个字都没动**（那些都按 `land`/`startNo` 走）。
                string drawStem = stem;
                if (startNo > 0) drawStem = StartCellTexture ?? stem;
                if (!string.IsNullOrEmpty(drawStem))
                {
                    ImageSource ov = string.Equals(drawStem, stem, StringComparison.Ordinal)
                        ? art : Art.LoadName(drawStem);
                    if (ov != null) art = ov;
                }

                var box = new Border();
                // ★ 内容层仍然裁圆（贴图/色块是方的，不裁就四角溢色）。
                box.CornerRadius = new CornerRadius(Math.Max(3, node * 0.30));
                box.ClipToBounds = true;
                // ★★ 2026-10-10 用户：「那边框为什么还是方的，选择的边框还是方的」
                //   ⇒ **描边不再由这个 Border 画**（它的 CornerRadius 是圆角方块，画出来就是方框，
                //     而 `Clip` 只裁内容、裁不掉描边）。改成下面那两个 `Ellipse` 叠在格子上，
                //     椭圆天生是正圆、跟着格子尺寸走，不依赖 `SizeChanged`/`Loaded` 时序。
                //   这一格的状态描边（底：landType 色 / 改过 / 选中）最终由圆环决定。
                double strokeW = Math.Max(1.2, node * 0.055);
                Brush strokeBrush = new SolidColorBrush(ParseHex(Land.Hex(land)));

                if (art != null)
                {
                    // ★★ 用户：「既然改成圆形的了怎么四边还是有色块」—— 有贴图时
                    //   **一丁点底色都不许有**（贴图是圆形内容 + 四角透明，垫色块就会漏出那圈）。
                    var img = new Image();
                    img.Source = art;
                    img.Stretch = Stretch.Uniform;
                    img.IsHitTestVisible = false;
                    // ★ 内容裁圆放在**图片自己**身上（不再裁 `box`）：这样 `box` 不再 `ClipToBounds`
                    //   ⇒ 悬停那圈 1.20× 的环不会被裁掉（原来裁在 box 上会把外圈切没）。
                    ClipToDisc(img);
                    box.Child = img;
                }
                else
                {
                    // 没贴图才回落「色块 + 中文名」（色块此时是唯一的信息载体，必须留）。
                    box.Background = new SolidColorBrush(ParseHex(Land.Hex(land)));
                    var txt = new TextBlock();
                    txt.Text = Land.Name(land);
                    txt.FontSize = Math.Max(8.5, Math.Min(22.0, node * 0.24));
                    txt.Foreground = Brushes.White;
                    txt.TextAlignment = TextAlignment.Center;
                    txt.TextWrapping = TextWrapping.Wrap;
                    txt.HorizontalAlignment = HorizontalAlignment.Center;
                    txt.VerticalAlignment = VerticalAlignment.Center;
                    txt.Margin = new Thickness(node * 0.04);
                    txt.IsHitTestVisible = false;
                    box.Child = txt;
                }
                if (n.HasDiy)
                {
                    // 「这一格改过（和官方不同）」—— 圆环加粗换色
                    strokeBrush = UiBits.Br(this, "WaitBrush");
                    strokeW = Math.Max(2.0, node * 0.10);
                }
                if (n.Id == _selNode)
                {
                    // 选中
                    strokeBrush = UiBits.Br(this, "Accent");
                    strokeW = Math.Max(2.5, node * 0.12);
                }
                // ★ 内容层不再整体裁圆（原先裁 `box` 会把悬停外圈一起切掉）：
                //   有贴图时裁在图片身上（见上面），兜底色块时裁在 `box` 身上（下面）。
                if (art == null) ClipToDisc(box);
                cell.Children.Add(box);
                // ★ 状态描边（正圆）
                cell.Children.Add(DiscStroke(node, strokeBrush, strokeW));

                // ★★ 悬停环：**每格常驻一个**（默认 `Collapsed`），进出时只切 `Visibility`。
                //    ★ 不在这里"按需加/删" —— 那是改视觉树结构，而且切可见性最省、绝不触发重建。
                var hoverRing = DiscStroke(node * 1.20, UiBits.Br(this, "Accent"),
                                           Math.Max(2.0, node * 0.09));
                hoverRing.Opacity = 0.75;
                hoverRing.IsHitTestVisible = false;      // ★ 装饰环绝不吃点击
                hoverRing.Visibility = n.Id == _hoverNode ? Visibility.Visible : Visibility.Collapsed;
                cell.Children.Add(hoverRing);
                _hoverRings[n.Id] = hoverRing;

                if (node >= 10)
                {
                    int nodeId = n.Id;
                    BoardNode cap = n;
                    cell.ContextMenu = BuildCellMenu(nodeId, cap);
                    cell.Tag = nodeId;
                    cell.MouseRightButtonDown += Cell_RightDown;
                    cell.Cursor = Cursors.Hand;
                    cell.MouseLeftButtonDown += Cell_LeftDown;
                    // ★ 悬停：**只切那一圈环的可见性**，绝不重建（见 `_hoverRings` 的长注释 ——
                    //   上一版就是"悬停→重建→MouseLeave→再重建"的死循环，害得真鼠标点不动）。
                    cell.MouseEnter += delegate { SetHover(nodeId); };
                    cell.MouseLeave += delegate { SetHover(-1); };
                }

                if (WantIds && node >= 13)
                {
                    double fs = Math.Max(8.5, Math.Min(14.0, node * 0.30));
                    var idText = new TextBlock();
                    idText.Text = n.Id.ToString(CultureInfo.InvariantCulture);
                    idText.FontSize = fs;
                    idText.FontWeight = FontWeights.SemiBold;
                    idText.Foreground = new SolidColorBrush(Color.FromArgb(232, 255, 255, 255));
                    idText.IsHitTestVisible = false;

                    var idBox = new Border();
                    idBox.Background = new SolidColorBrush(Color.FromArgb(150, 8, 12, 20));
                    idBox.BorderBrush = new SolidColorBrush(Color.FromArgb(70, 255, 255, 255));
                    idBox.BorderThickness = new Thickness(1);
                    idBox.CornerRadius = new CornerRadius(Math.Max(4, fs * 0.5));
                    idBox.Padding = new Thickness(fs * 0.42, fs * 0.06, fs * 0.42, fs * 0.10);
                    idBox.Child = idText;
                    idBox.HorizontalAlignment = HorizontalAlignment.Right;
                    idBox.VerticalAlignment = VerticalAlignment.Top;
                    idBox.Margin = new Thickness(0, Math.Max(1.0, node * 0.02),
                                                 Math.Max(1.0, node * 0.02), 0);
                    idBox.IsHitTestVisible = false;
                    cell.Children.Add(idBox);
                }

                Canvas.SetLeft(cell, p.X - half);
                Canvas.SetTop(cell, p.Y - half);
                BoardCanvas.Children.Add(cell);

                if (ShotsDumpCells)
                    LastCellRect.Add(string.Format(CultureInfo.InvariantCulture,
                        "格 {0} 中心({1:0.#},{2:0.#}) 尺寸 {3:0.#} landType {4} {5} 改过={6}{7}{8}",
                        n.Id, p.X, p.Y, node, land, Land.Name(land), n.HasDiy ? "是" : "否",
                        n.Id == _selNode ? " 【选中】" : "", n.Id == _hoverNode ? " 【悬停】" : ""));
            }

            // ③b 起点徽标（最后一趟画，不被邻居节点盖住）
            if (node >= 12)
            {
                foreach (int id in startBadges)
                {
                    Point sp = pos[id];
                    int no = BoardModel.StartNumber(_starts, id);
                    if (no <= 0) continue;

                    double fs = Math.Max(9.5, Math.Min(15.0, node * 0.34));
                    var sn = new TextBlock();
                    sn.Text = "起点" + no;
                    sn.FontSize = fs;
                    sn.FontWeight = FontWeights.Bold;
                    sn.Foreground = new SolidColorBrush(Color.FromArgb(255, 6, 18, 12));
                    sn.IsHitTestVisible = false;

                    var sb = new Border();
                    sb.Background = new SolidColorBrush(Color.FromArgb(244, 99, 230, 168));
                    sb.BorderBrush = new SolidColorBrush(Color.FromArgb(210, 255, 255, 255));
                    sb.BorderThickness = new Thickness(1.4);
                    sb.CornerRadius = new CornerRadius(Math.Max(5, fs * 0.55));
                    sb.Padding = new Thickness(fs * 0.46, fs * 0.08, fs * 0.46, fs * 0.16);
                    sb.Child = sn;
                    sb.IsHitTestVisible = false;
                    sb.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                    double bw = sb.DesiredSize.Width, bh = sb.DesiredSize.Height;
                    Canvas.SetLeft(sb, sp.X - bw / 2);
                    Canvas.SetTop(sb, sp.Y - half - bh - Math.Max(1.0, node * 0.05));
                    BoardCanvas.Children.Add(sb);
                }
            }

            // ④ ★ 棋子：看板上的怪物头像（「显示怪物」勾着才画）
            //   ★★ 一格多只：2~3 只按 45° 小偏移错开叠放；超过 3 只只画 3 只 + "+N" 角标。
            //   ★★ 定位参考系 = **格子的内容层**：格子摆在 (p - half)，所以格子的内容层中心
            //      就是 `p` 本身。`MonsterStack.Rect` 给的 left/top 直接就是那个中心减半个头像。
            if (WantMonsters && node >= 20)
            {
                LastPlacement.Clear();
                foreach (var kv in here)
                {
                    Point pp;
                    if (!pos.TryGetValue(kv.Key, out pp)) continue;
                    List<BoardMonsterRef> list = kv.Value;
                    int count = list.Count;
                    int drawn = Math.Min(count, MonsterStack.MaxDrawn);
                    // ★ 摊开的"排面"就按**画出来的个数**算：4 只时画 3 只 + 「+N」角标，
                    //   这 3 只仍然以格子中心为对称中心（不然会整体偏半个步长）。
                    int layout = drawn;

                    for (int k = 0; k < drawn; k++)
                    {
                        BoardMonsterRef r = list[k];
                        PoolMonster m = DiyPool.Find(diy.Pool, r.Pool);
                        if (m == null) continue;
                        double size, left, top;
                        MonsterStack.Rect(pp.X, pp.Y, node, k, layout, out left, out top, out size);
                        BoardCanvas.Children.Add(MonsterChip(r, m, size, left, top,
                            count == 1 ? null : (k + 1) + "/" + count));
                        double mx = left + size / 2, my = top + size / 2;
                        LastPlacement.Add("格 " + kv.Key + " 第 " + (k + 1) + "/" + count + " 只（"
                            + m.Display + "） 中心(" + mx.ToString("0.0", CultureInfo.InvariantCulture)
                            + "," + my.ToString("0.0", CultureInfo.InvariantCulture)
                            + ") 格子中心(" + pp.X.ToString("0.0", CultureInfo.InvariantCulture)
                            + "," + pp.Y.ToString("0.0", CultureInfo.InvariantCulture)
                            + ") 偏差 " + Math.Sqrt((mx - pp.X) * (mx - pp.X) + (my - pp.Y) * (my - pp.Y))
                                .ToString("0.00", CultureInfo.InvariantCulture)
                            + " 头像 " + size.ToString("0.0", CultureInfo.InvariantCulture)
                            + "px / 格子 " + node.ToString("0.0", CultureInfo.InvariantCulture) + "px");
                    }

                    int hidden = MonsterStack.HiddenCount(count);
                    if (hidden > 0)
                    {
                        Border badge = MoreBadge(pp, node, hidden, count);
                        BoardCanvas.Children.Add(badge);
                        LastPlacement.Add("格 " + kv.Key + " ★ 没画出来的 " + hidden + " 只 ⇒ 角标「+"
                            + hidden + "」贴在格子右上角："
                            + Canvas.GetLeft(badge).ToString("0.0", CultureInfo.InvariantCulture) + ","
                            + Canvas.GetTop(badge).ToString("0.0", CultureInfo.InvariantCulture));
                    }
                }
            }
            else LastPlacement.Clear();

            // ⑤ 池子里指不到的落点（棋盘上没这个格子）
            //   ★ 同样只看当前组：这一眼没画出来的组，它的落点也轮不到这里喊。
            var missing = new List<string>();
            foreach (BoardMonsterRef r in diy.BoardMonsters)
            {
                if (r.Event != _groupEvent) continue;
                if (_board.Get(r.Node) == null) missing.Add("格 " + r.Node + "（池内 #" + r.Pool + "）");
            }
            if (missing.Count > 0)
            {
                var box = new Border();
                box.Background = new SolidColorBrush(Color.FromArgb(226, 40, 12, 16));
                box.BorderBrush = UiBits.Br(this, "BadBrush");
                box.BorderThickness = new Thickness(2);
                box.CornerRadius = new CornerRadius(10);
                box.Padding = new Thickness(12, 10, 12, 10);
                box.HorizontalAlignment = HorizontalAlignment.Right;
                box.VerticalAlignment = VerticalAlignment.Top;
                box.Margin = new Thickness(0, 12, 12, 0);
                box.MaxWidth = 380;
                var text = new TextBlock();
                text.Foreground = UiBits.Br(this, "BadBrush");
                text.FontSize = 12;
                text.TextWrapping = TextWrapping.Wrap;
                text.Text = "这些落点在棋盘上不存在（怪不会出现）：" + string.Join("、", missing.ToArray());
                box.Child = text;
                BoardCanvas.Children.Add(box);
            }

            double pitchNow = PitchPixels();
            ZoomLine.Text = "缩放 " + (_scale * 100).ToString("0", CultureInfo.InvariantCulture) + "%"
                          + " · 格距 " + pitchNow.ToString("0", CultureInfo.InvariantCulture)
                          + " px · 格子 " + NodeSize(pitchNow).ToString("0", CultureInfo.InvariantCulture)
                          + " px（" + NodeRatioText + "）"
                          + " · 怪物 " + (WantMonsters
                                ? (boardMonsterCount + " 只画着（" + GroupName() + "）· 占 " + here.Count + " 格"
                                   + (boardMonsterCount > here.Count ? "（有格子放了多只）" : ""))
                                : "不显示")
                          + " · 可撤销 " + MainWindow.Undo.UndoCount + " 步";
            SyncButtons();
        }

        /// <summary>
        /// 一格上的一个怪物头像（圆形裁切 + 池内序号角标）。
        /// ★ `left/top/size` 由 `MonsterStack.Rect` 算好 —— 那套算术是"居中于格子内容层"的唯一出处。
        /// ★★ 2026-10-09：悬停里的数值取的是**这一处放置自己**那份（`BoardMonsterRef`），
        ///    不是怪种那份 —— 同一只怪放在两格上可以是两个血量，悬停要说得清是哪一个。
        /// </summary>
        private Border MonsterChip(BoardMonsterRef r, PoolMonster m, double size,
                                   double left, double top, string corner)
        {
            var frame = new Border();
            frame.Width = size;
            frame.Height = size;
            frame.CornerRadius = new CornerRadius(size / 2);
            // ★ 2026-10-10 用户："怪物底部怎么有个黑底，不需要这个" —— 那层黑底是**我们加的深色容器**
            //   （原来这里是 `FromArgb(220,12,16,26)`），不是图自带的：像素小人 PNG 四角 alpha 全 0、
            //   最外一圈 alpha 均值 0.0（实测见 `tools\_alpha_report.txt`）。
            //   棋盘上格子本来就小，铺深色圆底会显得每只怪都顶着一块黑饼 ⇒ 有图时不给底色。
            frame.BorderBrush = UiBits.Br(this, "BadBrush");
            frame.BorderThickness = new Thickness(Math.Max(1.2, size * 0.05));
            frame.IsHitTestVisible = false;
            frame.ToolTip = m.Display + "（" + m.ClassName + "）· 格 " + r.Node
                + "\n血 " + r.Hp + " · 防 " + r.Defense + " · 攻 " + r.Attack
                + " · 币 " + r.Gold + (r.Counter ? " · 会反击" : " · 不反击")
                + "\n（这是「这一处放置」自己的数值；同一只怪放在别处的那几条不受影响）"
                + "\n图：" + MonsterArt.SourceNote(m);

            // ★★ 统一取图（图鉴像素小人 → 半身像 → 手牌画）；像素小人自动走最近邻放大。
            //   半身像是宽幅、角色不居中，所以那一档会按内容居中裁成方图（实测 53 张里 47 张偏 >8 px）。
            var im = MonsterArt.Make(m);
            if (im != null)
            {
                im.Stretch = Stretch.UniformToFill;
                im.Clip = new EllipseGeometry(new Point(size / 2, size / 2), size / 2, size / 2);
                frame.Child = im;
            }
            else
            {
                var t = new TextBlock();
                t.Text = "#" + m.Id;
                t.FontSize = Math.Max(9, size * 0.22);
                t.Foreground = Brushes.White;
                t.HorizontalAlignment = HorizontalAlignment.Center;
                t.VerticalAlignment = VerticalAlignment.Center;
                frame.Child = t;
            }
            Canvas.SetLeft(frame, left);
            Canvas.SetTop(frame, top);
            return frame;
        }

        /// <summary>
        /// 一格上超过 3 只时角落的 "+N"（N = 没画出来的只数；悬停给总数）。
        /// ★ 贴**右下角**：右上角是"显示编号"的编号徽标、正上方是错开叠放的头像，
        ///   右下角是唯一不和它们打架的地方。
        /// </summary>
        private Border MoreBadge(Point center, double node, int hidden, int total)
        {
            double fs = Math.Max(9.5, Math.Min(15.0, node * 0.32));
            var t = new TextBlock();
            t.Text = "+" + hidden;
            t.FontSize = fs;
            t.FontWeight = FontWeights.Bold;
            t.Foreground = new SolidColorBrush(Color.FromArgb(255, 255, 255, 255));
            t.IsHitTestVisible = false;

            var b = new Border();
            b.Background = new SolidColorBrush(Color.FromArgb(238, 176, 40, 56));
            b.BorderBrush = new SolidColorBrush(Color.FromArgb(220, 255, 255, 255));
            b.BorderThickness = new Thickness(1.4);
            b.CornerRadius = new CornerRadius(Math.Max(5, fs * 0.55));
            b.Padding = new Thickness(fs * 0.42, fs * 0.02, fs * 0.42, fs * 0.12);
            b.Child = t;
            b.IsHitTestVisible = false;
            b.ToolTip = "这一格一共 " + total + " 只怪，画面只画了前 " + MonsterStack.MaxDrawn
                + " 只 ⇒ 还有 " + hidden + " 只（右键这一格能逐只看到 / 拿掉）。";
            b.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            double bw = b.DesiredSize.Width, bh = b.DesiredSize.Height;
            double half = node / 2;
            double m = Math.Max(1.0, node * 0.02);
            Canvas.SetLeft(b, center.X + half - bw - m);
            Canvas.SetTop(b, center.Y + half - bh - m);
            return b;
        }

        private void DrawLinks(Dictionary<int, Point> pos, double pitch)
        {
            if (_board == null) return;

            var degree = new Dictionary<int, int>();
            foreach (BoardNode n in _board.Nodes)
            {
                int d = 0;
                foreach (int nb in n.Neighbors) if (pos.ContainsKey(nb)) d++;
                degree[n.Id] = d;
            }

            double thin = Math.Max(0.7, Math.Min(2.4, pitch * LinkThickRatio));
            var drawn = new HashSet<string>(StringComparer.Ordinal);
            foreach (BoardNode n in _board.Nodes)
            {
                foreach (int nb in n.Neighbors)
                {
                    int a = Math.Min(n.Id, nb), b = Math.Max(n.Id, nb);
                    if (!drawn.Add(a + "-" + b)) continue;
                    Point pa, pb;
                    if (!pos.TryGetValue(n.Id, out pa) || !pos.TryGetValue(nb, out pb)) continue;

                    bool touchStart = BoardModel.StartNumber(_starts, n.Id) > 0
                                   || BoardModel.StartNumber(_starts, nb) > 0;
                    int da, db;
                    degree.TryGetValue(n.Id, out da);
                    degree.TryGetValue(nb, out db);
                    bool branch = da >= 3 || db >= 3;

                    Color c = Color.FromArgb(88, 150, 172, 200);
                    double thick = thin;
                    if (branch) { c = Color.FromArgb(132, 196, 212, 236); thick = thin * 1.5; }
                    if (touchStart) { c = Color.FromArgb(152, 99, 230, 168); thick = thin * 1.7; }

                    var line = new Line();
                    line.X1 = pa.X; line.Y1 = pa.Y;
                    line.X2 = pb.X; line.Y2 = pb.Y;
                    line.Stroke = new SolidColorBrush(c);
                    line.StrokeThickness = thick;
                    line.StrokeStartLineCap = PenLineCap.Round;
                    line.StrokeEndLineCap = PenLineCap.Round;
                    line.IsHitTestVisible = false;
                    BoardCanvas.Children.Add(line);
                }
            }
        }

        /// <summary>量一下这块棋盘一格有多远（世界单位）：相邻格距离的中位数。</summary>
        private void MeasurePitch()
        {
            _pitchUnits = DefaultPitchUnits;
            if (_board == null || !_board.Ok) return;

            var d = new List<double>();
            foreach (BoardNode n in _board.Nodes)
            {
                foreach (int nb in n.Neighbors)
                {
                    BoardNode m = _board.Get(nb);
                    if (m == null) continue;
                    double dx = m.X - n.X, dz = m.Z - n.Z;
                    double len = Math.Sqrt(dx * dx + dz * dz);
                    if (len > 1e-6) d.Add(len);
                }
            }
            if (d.Count == 0) return;
            d.Sort();
            double mid = d[d.Count / 2];
            if (mid > 1e-6) _pitchUnits = mid;
        }

        private double PitchPixels()
        {
            double units = _pitchUnits > 1e-6 ? _pitchUnits : DefaultPitchUnits;
            return Math.Max(6.0, units * _scale);
        }

        public static double NodeSize(double pitchPixels)
        {
            return NodeSizeFor(pitchPixels, _nodeRatio);
        }

        public static double NodeSizeFor(double pitchPixels, double ratio)
        {
            if (ratio < 0.20) ratio = 0.20;
            if (ratio > 0.95) ratio = 0.95;
            return Math.Max(7.0, Math.Min(260.0, pitchPixels * ratio));
        }

        private static double _nodeRatio = DefaultNodeRatio;
        public const double DefaultNodeRatio = 0.60;
        public static readonly double[] NodeRatioOptions = new double[] { 0.45, 0.60, 0.72, 0.85 };
        public static double NodeRatioNow { get { return _nodeRatio; } }

        private bool _fillingSize;

        public static string NodeRatioText
        {
            get { return NodeRatioTextFor(_nodeRatio); }
        }

        /// <summary>某一档格子大小怎么念（自测四档都验的时候用，别在测试里现拼字符串）。</summary>
        public static string NodeRatioTextFor(double ratio)
        {
            return "格子 = 格距的 " + (ratio * 100).ToString("0", CultureInfo.InvariantCulture) + "%";
        }

        private const double BasePixelsPerUnit = 30.0;
        private const double LinkThickRatio = 0.05;
        private const double DefaultPitchUnits = 29.5;
        private const double FitPadding = 90.0;

        private static double BoardBase() { return BasePixelsPerUnit; }

        private static Color ParseHex(string hex)
        {
            try { return (Color)ColorConverter.ConvertFromString(hex); }
            catch (Exception) { return Color.FromRgb(0x3A, 0x43, 0x54); }
        }

        // ================================================================ 视图
        private void BoardBounds(out double minX, out double maxX, out double minZ, out double maxZ)
        {
            minX = double.MaxValue; maxX = double.MinValue;
            minZ = double.MaxValue; maxZ = double.MinValue;
            if (_board == null) { minX = maxX = minZ = maxZ = 0; return; }
            foreach (BoardNode n in _board.Nodes)
            {
                if (n.X < minX) minX = n.X;
                if (n.X > maxX) maxX = n.X;
                if (n.Z < minZ) minZ = n.Z;
                if (n.Z > maxZ) maxZ = n.Z;
            }
            if (_board.Nodes.Count == 0) { minX = maxX = minZ = maxZ = 0; }
        }

        private void FitToWindow(bool resetScale)
        {
            if (_board == null || !_board.Ok) return;
            if (CanvasHost == null || CanvasHost.ActualWidth <= 2) return;

            double minX, maxX, minZ, maxZ;
            BoardBounds(out minX, out maxX, out minZ, out maxZ);
            double spanX = Math.Max(BasePixelsPerUnit, maxX - minX);
            double spanZ = Math.Max(BasePixelsPerUnit, maxZ - minZ);

            double fit = Math.Min((CanvasHost.ActualWidth - FitPadding) / spanX,
                                  (CanvasHost.ActualHeight - FitPadding) / spanZ);
            fit = Math.Max(0.05, Math.Min(fit, 4.0));
            if (resetScale) _scale = fit;

            _offsetX = (CanvasHost.ActualWidth - spanX * _scale) / 2 - minX * _scale;
            _offsetY = (CanvasHost.ActualHeight - spanZ * _scale) / 2 - minZ * _scale;
        }

        private void OnFit(object sender, RoutedEventArgs e)
        {
            PushView();
            FitToWindow(true);
            Draw();
        }

        private void OnReset(object sender, RoutedEventArgs e)
        {
            PushView();
            _scale = 1.0;
            FitToWindow(false);
            Draw();
        }

        private void OnWheel(object sender, MouseWheelEventArgs e)
        {
            if (CanvasHost == null) return;
            Point m = e.GetPosition(CanvasHost);
            double before = _scale;
            double factor = e.Delta > 0 ? 1.15 : (1.0 / 1.15);
            double want = ClampScale(_scale * factor);
            if (Math.Abs(want - before) < 1e-9) return;

            PushView();
            double wx = (m.X - _offsetX) / before;
            double wz = (m.Y - _offsetY) / before;
            _scale = want;
            _offsetX = m.X - wx * _scale;
            _offsetY = m.Y - wz * _scale;
            Draw();
            e.Handled = true;
        }

        private static double ClampScale(double s)
        {
            if (s < 0.2) return 0.2;
            if (s > 4.0) return 4.0;
            return s;
        }

        private void OnMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (CanvasHost == null) return;
            // ★★ 2026-10-10 P1：**左键落在格子上时绝不许启动拖动平移**。
            //
            // 病根（用户："点上去直接变成拖动的那个鼠标图标了"）：
            //   这个 `OnMouseDown` 原来**无条件**启动平移；而上一轮去掉"色块底"之后，
            //   格子上**没有任何可接住鼠标的元素**了（`box.Background` 有贴图时不铺、
            //   图片又是 `IsHitTestVisible=false`）⇒ 左键直接冒泡到这一层
            //   ⇒ 光标变 `SizeAll` + 开始拖盘，**格子永远选不中**。
            //   （更早能点，是因为那层方形色块底**恰好当了可点表面** —— 一直埋着的坑被挖出来了。）
            //
            // 现在：点在格子上 ⇒ 直接 return，把这次点击**留给格子**；
            //   平移只在"点在空白棋盘区域"时启动（右键/中键也照旧可平移）。
            if (e.ChangedButton == MouseButton.Left && HitTestCell(e.OriginalSource) != null)
                return;
            Focus();
            _dragging = true;
            _moved = false;
            _dragFrom = e.GetPosition(CanvasHost);
            _dragOx = _offsetX;
            _dragOy = _offsetY;
            PushView();
            CanvasHost.CaptureMouse();
            CanvasHost.Cursor = Cursors.SizeAll;
        }

        /// <summary>
        /// ★★ P1 回归探针 2（`--board-hit-probe`）：**走真实命中测试**验三条路径。
        ///
        /// 上一版我犯的错就是"只对格子直接派事件" —— 那绕开了命中测试，
        /// 所以"格子上没有可命中表面 ⇒ 左键落到容器 ⇒ 启动拖动平移 ⇒ 点不中"
        /// 这个真机症状一条都测不出来。这个探针**先 `HitTest` 再派事件**。
        ///
        /// 返回一行行结果；外面按行断言。
        /// </summary>
        public List<string> RunHitProbe()
        {
            var log = new List<string>();
            if (CanvasHost == null || BoardCanvas == null) { log.Add("★ 画布没准备好"); return log; }

            // ── ① 每一格的中心都真的命中得了（命中元素能上溯到那一格的 Grid）
            var cells = new List<Grid>();
            foreach (UIElement u in BoardCanvas.Children)
            {
                var g = u as Grid;
                if (g != null && g.Tag is int) cells.Add(g);
            }
            int hitOk = 0;
            var bad = new List<string>();
            int outside = 0;
            foreach (Grid g in cells)
            {
                int id = (int)g.Tag;
                Point pc;
                try { pc = g.TransformToAncestor(BoardCanvas).Transform(new Point(g.ActualWidth / 2, g.ActualHeight / 2)); }
                catch (Exception) { bad.Add("格 " + id + "（算不出坐标）"); continue; }
                // ★ 顶点落在**可视区以外**的格子（盘比视口大、画布 ClipToBounds）本来就不该被点到，
                //   跳过它们，别把它算成"命中失败"。
                if (pc.X < 0 || pc.Y < 0 || pc.X > BoardCanvas.ActualWidth || pc.Y > BoardCanvas.ActualHeight)
                { outside++; continue; }

                // ★ 从 **BoardCanvas** 起测（不要从 CanvasHost —— 那上面还压着左上角提示 `HintBox`，
                //   会把落在那块底下的格子误判成"命中了提示框"）。
                Grid hitCell = HitTestCellAt(pc);
                if (hitCell != null && (int)hitCell.Tag == id) hitOk++;
                else bad.Add("格 " + id + " 归属=" + (hitCell == null ? "找不到格子" : ("格 " + hitCell.Tag)));
            }
            log.Add("① 格中心命中： " + hitOk + "/" + (cells.Count - outside) + " 格可命中（另有 "
                + outside + " 格中心在可视区外，不计）"
                + (bad.Count == 0 ? " ✓" : " ★不合格：" + string.Join("、", bad.ToArray())));

            // ── ② 左键点格子：不许启动平移、不许变 SizeAll，且要真的选中
            //   ★ 注意：`SelectCell` 会 `Draw()` 重建整盘 ⇒ 我手里那份 `cells` 会失效，
            //     所以**每点一次都重新收集一遍格子**（这也是真机上的实际顺序）。
            int selOk = 0, panBad = 0;
            var ids = new List<int>();
            foreach (Grid g in cells) ids.Add((int)g.Tag);
            foreach (int id in ids)
            {
                Grid cur = FindCellById(id);
                if (cur == null) continue;
                Point pc;
                try { pc = cur.TransformToAncestor(BoardCanvas).Transform(new Point(cur.ActualWidth / 2, cur.ActualHeight / 2)); }
                catch (Exception) { continue; }

                _dragging = false;
                _selNode = -1;
                CanvasHost.Cursor = Cursors.Arrow;
                // ★ 用命中到的那一格当派发起点（等价于真鼠标在格内按下、事件冒泡到格子 Grid 那一刻）
                var args = new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
                { RoutedEvent = UIElement.MouseLeftButtonDownEvent, Source = cur };
                cur.RaiseEvent(args);

                bool selChanged = _selNode == id;
                bool panned = _dragging || (CanvasHost.Cursor == Cursors.SizeAll);
                if (selChanged && !panned) selOk++;
                else
                {
                    panBad++;
                    if (panBad <= 3)
                        log.Add("      · 格 " + id + " 选中=" + selChanged + " 启动平移=" + panned);
                }
            }
            try { CanvasHost.ReleaseMouseCapture(); } catch (Exception) { }
            CanvasHost.Cursor = Cursors.Arrow;
            log.Add("② 左键点格子： 选中成功 " + selOk + "/" + ids.Count + " · 误启动平移 " + panBad
                + (panBad == 0 && selOk == ids.Count ? " ✓" : " ★不合格"));

            // ── ③ 空白处左键：平移**必须**还能用（别把平移修坏了）
            _dragging = false;
            var blankArgs = new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
            { RoutedEvent = UIElement.MouseLeftButtonDownEvent, Source = CanvasHost };
            CanvasHost.RaiseEvent(blankArgs);
            bool blankPans = _dragging;
            _dragging = false;
            try { CanvasHost.ReleaseMouseCapture(); } catch (Exception) { }
            CanvasHost.Cursor = Cursors.Arrow;
            log.Add("③ 空白处左键： 启动平移 = " + blankPans + (blankPans ? " ✓" : " ★不合格（平移被修坏了）"));

            // ── ④ 右键点格子：菜单要能弹出（`Cell_RightDown` 不重画，所以格子不会失效）
            int ctxOk = 0, ctxTried = 0;
            foreach (int id in ids)
            {
                Grid cur = FindCellById(id);
                if (cur == null) continue;
                ctxTried++;
                _ctxMenu = null;
                _ctxNode = -1;
                var args = new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Right)
                { RoutedEvent = UIElement.MouseRightButtonDownEvent, Source = cur };
                cur.RaiseEvent(args);
                if (_ctxMenu != null && _ctxNode == id) ctxOk++;
            }
            log.Add("④ 右键点格子： 菜单就绪 " + ctxOk + "/" + ctxTried
                + (ctxTried > 0 && ctxOk == ctxTried ? " ✓" : " ★不合格"));

            return log;
        }

        /// <summary>棋盘上现在画了多少格（自测用：等"适应窗口"那趟跑完才有格子）。</summary>
        public int CellCount
        {
            get
            {
                if (BoardCanvas == null) return 0;
                int n = 0;
                foreach (UIElement u in BoardCanvas.Children)
                {
                    var g = u as Grid;
                    if (g != null && g.Tag is int) n++;
                }
                return n;
            }
        }

        /// <summary>按格 id 在当前的 `BoardCanvas` 里把那一格的 `Grid` 找回来（`Draw()` 之后要用它重新定位）。</summary>
        private Grid FindCellById(int id)
        {
            if (BoardCanvas == null) return null;
            foreach (UIElement u in BoardCanvas.Children)
            {
                var g = u as Grid;
                if (g != null && g.Tag is int && (int)g.Tag == id) return g;
            }
            return null;
        }

        /// <summary>
        /// ★ 在棋盘上做一次"真命中测试"，只认**归属于某一格**的结果。
        ///
        /// 为什么要带 `HitTestResultCallback` 过滤：`VisualTreeHelper.HitTest` 在某个元素
        ///   `IsHitTestVisible = false` 时**会继续往上找一个能命中的祖先**返回 ——
        ///   于是它会返回 `Canvas` 之类**不属于任何格子**的东西（实测就这么被骗过）。
        ///   真鼠标路由落到的却是**最深的那个可见元素**（Image / TextBlock / Grid…），
        ///   所以这里显式把"上溯能到某个格子 `Grid`"作为唯一判据，拿到就 `Stop`。
        /// </summary>
        private Grid HitTestCellAt(Point p)
        {
            if (BoardCanvas == null) return null;
            Grid found = null;
            try
            {
                VisualTreeHelper.HitTest(BoardCanvas, null,
                    delegate (HitTestResult r)
                    {
                        if (r == null || r.VisualHit == null) return HitTestResultBehavior.Continue;
                        Grid g = HitTestCell(r.VisualHit);
                        if (g != null) { found = g; return HitTestResultBehavior.Stop; }
                        return HitTestResultBehavior.Continue;
                    },
                    new PointHitTestParameters(p));
            }
            catch (Exception) { }
            return found;
        }

        /// <summary>
        /// 从这个"被点到的元素"往上找，看看它是不是落在某一格里；
        /// 是就回那个格子的 `Grid`（`Tag is int`），不是就回 null。
        /// ★ 判据和 `Draw` 里挂处理函数时一致：**格子 = `Tag is int` 的那个 `Grid`**。
        /// </summary>
        private Grid HitTestCell(object source)
        {
            var d = source as DependencyObject;
            while (d != null)
            {
                var g = d as Grid;
                if (g != null && g.Tag is int) return g;
                d = VisualTreeHelper.GetParent(d);
            }
            return null;
        }

        private void OnMouseMove(object sender, MouseEventArgs e)
        {
            if (!_dragging) return;
            Point now = e.GetPosition(CanvasHost);
            double dx = now.X - _dragFrom.X;
            double dy = now.Y - _dragFrom.Y;
            if (!_moved && Math.Abs(dx) + Math.Abs(dy) < 3) return;
            _moved = true;
            _offsetX = _dragOx + dx;
            _offsetY = _dragOy + dy;
            Draw();
        }

        private void OnMouseUp(object sender, MouseButtonEventArgs e)
        {
            if (!_dragging) return;
            _dragging = false;
            try { CanvasHost.ReleaseMouseCapture(); } catch (Exception) { }
            CanvasHost.Cursor = Cursors.Arrow;
            if (_moved) FlushPending();
            else DropPending();
        }

        private void OnRightDown(object sender, MouseButtonEventArgs e)
        {
            HintText.Text = "右键某一格：新增怪物（进当前「刷怪归属」那一组）/ 编辑这一格的怪… / 拿下这一格的怪 / 还原原版。";
            e.Handled = true;
        }

        private void OnKeyDown(object sender, KeyEventArgs e)
        {
            bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;

            if (ctrl && e.Key == Key.Z) { OnUndo(sender, e); e.Handled = true; return; }
            if (ctrl && e.Key == Key.Y) { OnRedo(sender, e); e.Handled = true; return; }
            if (ctrl && e.Key == Key.S) { OnSave(sender, e); e.Handled = true; return; }
            if (ctrl) return;

            if (IsTextTarget()) return;

            double step = (Keyboard.Modifiers & ModifierKeys.Shift) != 0 ? 140 : 56;
            bool hit = true;
            switch (e.Key)
            {
                case Key.W: case Key.Up: _offsetY += step; break;
                case Key.S: case Key.Down: _offsetY -= step; break;
                case Key.A: case Key.Left: _offsetX += step; break;
                case Key.D: case Key.Right: _offsetX -= step; break;
                default: hit = false; break;
            }
            if (!hit) return;
            PushView();
            Draw();
            FlushPending();
            e.Handled = true;
        }

        private bool IsTextTarget()
        {
            return Keyboard.FocusedElement is TextBox;
        }

        // ================================================================ 撤销 / 重做
        private void PushView()
        {
            FlushPending();
            if (_pendingView == null)
                _pendingView = new UndoStep { IsView = true, Scale = _scale, Ox = _offsetX, Oy = _offsetY };
        }

        private void FlushPending()
        {
            if (_pendingView == null) return;
            UndoStep before = _pendingView;
            _pendingView = null;
            if (Math.Abs(before.Scale - _scale) < 1e-9 &&
                Math.Abs(before.Ox - _offsetX) < 1e-9 &&
                Math.Abs(before.Oy - _offsetY) < 1e-9) return;
            MainWindow.Undo.CaptureView(before, "视图（缩放 / 平移）");
        }

        private void DropPending() { _pendingView = null; }

        private void OnUndo(object sender, RoutedEventArgs e) { Step("撤销", true); }
        private void OnRedo(object sender, RoutedEventArgs e) { Step("重做", false); }

        private void Step(string what, bool undo)
        {
            FlushPending();
            LevelDoc doc = MainWindow.Doc;
            string label;
            int rc = undo ? MainWindow.Undo.Undo(doc, _viewLink, out label)
                          : MainWindow.Undo.Redo(doc, _viewLink, out label);
            if (rc == 0)
            {
                SaveLine.Text = what + "：没有更多可" + (undo ? "撤销" : "重做") + "的了。";
                SyncButtons();
                return;
            }

            if (rc == 2)
            {
                _selNode = -1;
                SaveLine.Text = what + "了编辑动作「" + label + "」 —— 文档已跟着回滚（保存前不落盘）。";
                MainWindow.RefreshAll();
                SyncButtons();
                return;
            }

            _scale = ClampScale(_scale);
            Draw();
            SaveLine.Text = what + "了视图（还能"
                + (undo ? "撤销 " + MainWindow.Undo.UndoCount : "重做 " + MainWindow.Undo.RedoCount) + " 步）。";
            SyncButtons();
        }

        private void SyncButtons()
        {
            if (BtnUndo != null) BtnUndo.IsEnabled = MainWindow.Undo.CanUndo || _pendingView != null;
            if (BtnRedo != null) BtnRedo.IsEnabled = MainWindow.Undo.CanRedo;
        }

        // ================================================================ 存 / 读本地
        private void OnSave(object sender, RoutedEventArgs e)
        {
            LevelDoc doc = MainWindow.Doc;
            if (doc == null || doc.Root == null)
            {
                MessageBox.Show("还没打开关卡，没东西可存。", "小小兔趴 · DIY 关卡编辑器",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            string name = LevelDoc.SanitizeName(NameBox.Text);
            if (name.Length == 0)
            {
                MessageBox.Show("先给这个 DIY 关卡起个名字。\n（名字会变成文件名，所以不能有 \\ / : * ? \" < > |）",
                    "小小兔趴 · DIY 关卡编辑器", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            string dir = GameLocator.DiyDir;
            string target = string.IsNullOrEmpty(dir) ? "" : Path.Combine(dir, name + ".json");
            if (File.Exists(target))
            {
                MessageBoxResult ok = MessageBox.Show(
                    "这个文件名已经有了：\n" + target
                    + "\n\n覆盖它吗？（覆盖前会先备份到「导出备份」，并按 sha256 记账）",
                    "小小兔趴 · DIY 关卡编辑器", MessageBoxButton.OKCancel, MessageBoxImage.Warning);
                if (ok != MessageBoxResult.OK) return;
            }

            try
            {
                doc.SaveDiy();
                DiySaveResult r = doc.SaveDiy(name);
                NameBox.Text = doc.DiyName;
                SaveLine.Text = "已保存：" + Path.GetFileName(r.LevelPath)
                    + "  sha256 " + r.Sha256.Substring(0, 12) + "…";
                string msg = "已保存到本地 DIY 关卡：\n\n"
                    + "关卡 json： " + r.LevelPath + "\n"
                    + "  " + r.Bytes + " 字节 · sha256 " + r.Sha256 + "\n\n"
                    + "棋盘 json： " + (r.BoardWritten ? r.BoardPath : "（这次没写）") + "\n"
                    + "  " + r.BoardNote
                    + (r.BoardBytes > 0 ? " · " + r.BoardBytes + " 字节 · sha256 " + r.BoardSha : "") + "\n\n";
                if (r.Overwrote)
                    msg += "这次是覆盖：\n"
                         + "  覆盖前 sha256：" + r.SourceSha256 + "\n"
                         + "  备份路径：      " + r.BackupPath + "\n"
                         + "  备份 sha256：   " + r.BackupSha256
                         + (r.ByteIdentical ? "\n  （内容没变，逐字节相同）" : "") + "\n";
                else
                    msg += "这是新文件（原子写入）。\n";
                MessageBox.Show(msg, "小小兔趴 · DIY 关卡编辑器",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                MainWindow.RefreshAll();
                Reload();
            }
            catch (Exception ex)
            {
                MessageBox.Show("保存失败：\n\n" + ex.Message, "小小兔趴 · DIY 关卡编辑器",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OnReload(object sender, RoutedEventArgs e)
        {
            LevelDoc doc = MainWindow.Doc;
            if (doc == null || doc.Path.Length == 0) { Reload(); return; }
            try
            {
                MainWindow.Doc = LevelDoc.Open(doc.Path);
                SaveLine.Text = "已重新读取：" + Path.GetFileName(doc.Path);
            }
            catch (Exception ex)
            {
                SaveLine.Text = "重新读取失败：" + ex.Message;
            }
            MainWindow.RefreshAll();
            Reload();
        }

        // ================================================================ ★ 起点 1~4
        private void BuildStarts(LevelDoc doc)
        {
            _starts = doc == null || doc.Root == null ? new List<int>() : doc.EffectiveStarts();

            if (_board == null || !_board.Ok)
            {
                StartsNote.Text = "没有棋盘，起点无从谈起。";
                return;
            }
            List<string> lines = doc == null ? null : doc.StartsFindings();
            if (lines == null || lines.Count == 0)
            {
                StartsNote.Text = "棋盘上没有起点格（landType=1）。";
                return;
            }
            var head = new List<string>();
            head.Add(lines[0]);
            head.Add("来源：" + (doc.StartsNodes != null ? "diy.starts（你指定过）" : "官方 serial（底版那四个）"));
            StartsNote.Text = string.Join("\n", head.ToArray())
                            + (lines.Count > 1 ? "\n" + string.Join("\n", lines.GetRange(1, lines.Count - 1).ToArray()) : "");
        }

        private void Cell_RightDown(object sender, MouseButtonEventArgs e)
        {
            if (_ctxMenu != null) { try { _ctxMenu.IsOpen = false; } catch (Exception) { } }
            var fe = sender as FrameworkElement;
            if (fe == null) return;
            _ctxNode = fe.Tag is int ? (int)fe.Tag : -1;
            _ctxMenu = fe.ContextMenu;
            HintText.Text = "右键这一格：新增怪物（进当前「刷怪归属」那一组）/ 编辑这一格的怪… / 拿下这一格的怪 / 还原原版。";
        }

        private void Cell_LeftDown(object sender, MouseButtonEventArgs e)
        {
            var fe = sender as FrameworkElement;
            if (fe == null || !(fe.Tag is int)) return;
            SelectCell((int)fe.Tag);
            // ★ 标成已处理：别让这次左键再往上冒泡到棋盘容器（那边会启动拖动平移）。
            //   （`OnMouseDown` 里也有一道 `HitTestCell` 判据兜着，两道一起才稳。）
            e.Handled = true;
        }

        private void SelectCell(int nodeId)
        {
            _selNode = nodeId;
            UpdateNodeEditor();
            Draw();
        }

        // ================================================================ 右侧「格子」
        private void UpdateNodeEditor()
        {
            if (NodeNote == null) return;
            LevelDoc doc = MainWindow.Doc;
            BoardNode n = (_board != null && _board.Ok && _selNode >= 0) ? _board.Get(_selNode) : null;

            if (n == null)
            {
                if (NodeEditor != null) NodeEditor.Visibility = Visibility.Collapsed;
                NodeNote.Text = "左键点棋盘上任意一格：这一格的信息和下面那排「功能格子」就出来了。";
                if (LandTiles != null) LandTiles.Children.Clear();
                if (ChangedNote != null) ChangedNote.Text = "";
                RenderCtxMenuPreview(null);
                return;
            }

            NodeNote.Text = "选中的格子：格 " + n.Id
                + "\n官方功能：" + n.EffectiveLand + " " + Land.Name(n.EffectiveLand)
                + "\n现在功能：" + n.ShownLand + " " + Land.Name(n.ShownLand)
                + (n.HasDiy ? "（改过）" : "（= 官方值）")
                + "\n邻接：" + (n.Neighbors.Count == 0 ? "（没有）" : string.Join("、", NeighborText(n)))
                + (n.Serial != 0 ? "\nserial " + n.Serial : "");

            DiyData diy = doc == null ? null : doc.Diy;
            // ★ 也按当前组：棋盘上画的是哪一组，这里就列哪一组（两处口径必须一致，
            //   不然"画面上没有、右侧却说有"会让人以为丢怪了）。
            // ★ 2026-10-09：列的是**放置条目**，每条的数值是它自己那份。
            List<BoardMonsterRef> here = diy == null
                ? new List<BoardMonsterRef>() : DiyPool.RefsAtNode(diy, n.Id, _groupEvent);
            if (here.Count == 1)
            {
                PoolMonster only = DiyPool.Find(diy.Pool, here[0].Pool);
                NodeNote.Text += "\n这一格的怪（" + GroupName() + "，1 只）："
                    + (only == null ? ("池内 #" + here[0].Pool) : only.Display)
                    + "\n  血 " + here[0].Hp + " · 防 " + here[0].Defense + " · 攻 " + here[0].Attack
                    + " · 币 " + here[0].Gold + (here[0].Counter ? " · 会反击" : " · 不反击");
            }
            else if (here.Count > 1)
            {
                var names = new List<string>();
                for (int k = 0; k < here.Count; k++)
                {
                    PoolMonster mm = DiyPool.Find(diy.Pool, here[k].Pool);
                    names.Add((k + 1) + ". " + (mm == null ? ("池内 #" + here[k].Pool) : mm.Display)
                        + "（血 " + here[k].Hp + " · 防 " + here[k].Defense + " · 攻 " + here[k].Attack
                        + " · 币 " + here[k].Gold + (here[k].Counter ? " · 反击" : "") + "）");
                }
                NodeNote.Text += "\n★ 这一格有 " + here.Count + " 只怪（" + GroupName() + "，各是各的数值）：\n  "
                    + string.Join("\n  ", names.ToArray())
                    + "\n（右键这一格能逐只拿掉 /「编辑这一格的怪…」改这一只的数值；画面最多画 "
                    + MonsterStack.MaxDrawn + " 个，多的显示 +N）";
            }

            if (NodeEditor != null) NodeEditor.Visibility = Visibility.Visible;
            FillLandTiles(n);
            RenderCtxMenuPreview(n);
            if (BtnRevert != null) BtnRevert.IsEnabled = n.HasDiy;

            if (ChangedNote != null)
            {
                List<string> lines = doc == null ? null : doc.NodeOverrideLines();
                ChangedNote.Text = (lines == null || lines.Count == 0)
                    ? "这块棋盘还是官方原样。"
                    : ("改了 " + lines.Count + " 格：" + string.Join("；", lines.ToArray()));
            }
        }

        private static string[] NeighborText(BoardNode n)
        {
            var a = new string[n.Neighbors.Count];
            for (int k = 0; k < n.Neighbors.Count; k++)
                a[k] = n.Neighbors[k].ToString(CultureInfo.InvariantCulture);
            return a;
        }

        /// <summary>
        /// ★★ 右侧那一排「各种功能格子」—— **可视化图标**，点一下就换（用户点名要的）。
        ///   默认只列 PvE 用得到的（同一份 `Land.PickIds`）；第一块是「还原原版」。
        /// </summary>
        private void FillLandTiles(BoardNode n)
        {
            if (LandTiles == null) return;
            LandTiles.Children.Clear();

            var revert = new TileItem();
            revert.ImageName = null;
            revert.Block = new SolidColorBrush(Color.FromArgb(255, 58, 67, 84));
            revert.Title = "还原原版";
            revert.Sub = n.EffectiveLand + " " + Land.Name(n.EffectiveLand);
            revert.Tooltip = "把这一格还原成官方原本的功能（删掉 diy.landType）。";
            revert.Tag = 0;
            revert.Marked = !n.HasDiy;
            LandTiles.Children.Add(Tiles.Make(this, revert, delegate (TileItem t)
            {
                ApplyCellLand(n.Id, 0);
            }));

            List<int> want = Land.PickIds(false);
            foreach (int land in want)
            {
                var item = new TileItem();
                item.ImageName = Art.PlatformTexture(land);
                item.Block = new SolidColorBrush(ParseHex(Land.Hex(land)));
                item.Title = land + " " + Land.Name(land);
                item.Sub = item.ImageName == null ? "（色块）" : "";
                item.Tooltip = Land.Name(land) + "（landType " + land + "）\n" + LandTable.Reason(land);
                item.Tag = land;
                item.Marked = land == n.ShownLand && n.HasDiy;
                int cap = land;
                LandTiles.Children.Add(Tiles.Make(this, item, delegate (TileItem t)
                {
                    int id = (int)t.Tag;
                    ApplyCellLand(n.Id, id == n.EffectiveLand ? 0 : id);
                }));
            }

            if (LandGroupNote != null)
                LandGroupNote.Text = "列 PvE 用得到的 " + want.Count + " 种（PvP 专用的不列）。";
        }

        private void OnRevertCell(object sender, RoutedEventArgs e)
        {
            if (_selNode >= 0) ApplyCellLand(_selNode, 0);
        }

        /// <summary>把一格换成别的功能（0 = 还原成官方）。落 nodes[i].diy.landType。</summary>
        private void ApplyCellLand(int nodeId, int landType)
        {
            LevelDoc doc = MainWindow.Doc;
            if (doc == null || doc.Root == null)
            {
                SaveLine.Text = "还没打开关卡，改不了格子。";
                return;
            }
            BoardNode before = _board != null && _board.Ok ? _board.Get(nodeId) : null;
            string wasName = before != null ? Land.Name(before.ShownLand) : "?";

            int rc = doc.SetNodeLand(nodeId, landType);
            if (rc < 0)
            {
                SaveLine.Text = "格 " + nodeId + " 改不了（没有棋盘底版，或这个功能号不在 0~27 里）。";
                return;
            }

            MainWindow.RefreshAll();
            Reload();
            SelectCell(nodeId);
            if (rc == 0) SaveLine.Text = "格 " + nodeId + " 本来就是这个，没动。";
            else if (landType == 0) SaveLine.Text = "已把格 " + nodeId + " 还原成官方（原来改成了「" + wasName + "」）。";
            else SaveLine.Text = "已把格 " + nodeId + " 从「" + wasName + "」换成「" + Land.Name(landType) + "」。";
        }

        private void ApplyStart(int nodeId, int number)
        {
            LevelDoc doc = MainWindow.Doc;
            if (doc == null || doc.Root == null) { SaveLine.Text = "还没打开关卡，改不了起点。"; return; }
            doc.SetStart(nodeId, number);
            MainWindow.RefreshAll();
            Reload();
            SaveLine.Text = number >= 1
                ? "已把格 " + nodeId + " 设为起点" + number + "（落 diy.starts；官方 landType 没动）"
                : "已取消格 " + nodeId + " 的起点（diy.starts）";
        }

        // ================================================================ 图例 / 落点
        private void BuildLegend()
        {
            Legend.Children.Clear();
            if (_board == null || !_board.Ok) { LegendNote.Text = ""; return; }
            var kinds = new List<int>();
            foreach (BoardNode n in _board.Nodes)
                if (!kinds.Contains(n.ShownLand)) kinds.Add(n.ShownLand);
            kinds.Sort();

            int tex = 0, block = 0;
            foreach (int kind in kinds)
            {
                string stem = Art.PlatformTexture(kind);
                ImageSource art = stem == null ? null : Art.LoadName(stem);

                var box = new Border();
                box.CornerRadius = new CornerRadius(10);
                box.Background = new SolidColorBrush(Color.FromArgb(30, 255, 255, 255));
                box.Padding = new Thickness(8, 6, 12, 6);
                box.Margin = new Thickness(0, 0, 8, 8);
                var line = new StackPanel { Orientation = Orientation.Horizontal };

                if (art != null)
                {
                    tex++;
                    var img = new Image();
                    img.Source = art;
                    img.Width = 22; img.Height = 22;
                    img.Stretch = Stretch.UniformToFill;
                    img.VerticalAlignment = VerticalAlignment.Center;
                    // ★ 和棋盘上的格子**同一个口径**：图例也裁成正圆，别一处圆一处方。
                    img.Clip = DiscClip(22);
                    line.Children.Add(img);
                }
                else
                {
                    block++;
                    var dot = new Ellipse();
                    dot.Width = 16; dot.Height = 16;
                    dot.Fill = new SolidColorBrush(ParseHex(Land.Hex(kind)));
                    dot.VerticalAlignment = VerticalAlignment.Center;
                    line.Children.Add(dot);
                }

                int count = 0;
                foreach (BoardNode n in _board.Nodes) if (n.ShownLand == kind) count++;

                var text = new TextBlock();
                text.Text = kind + " " + Land.Name(kind) + " ×" + count + (art == null ? "（色块）" : "");
                text.FontSize = 11.5;
                text.Foreground = UiBits.Br(this, "FgDim");
                text.VerticalAlignment = VerticalAlignment.Center;
                text.Margin = new Thickness(8, 0, 0, 0);
                line.Children.Add(text);
                box.Child = line;
                box.ToolTip = LandTable.Reason(kind);
                Legend.Children.Add(box);
            }
            LegendNote.Text = "这张棋盘 " + tex + " 种有官方贴图、" + block + " 种回落色块 + 中文名。";
        }

        /// <summary>落点清单 = 怪物池里"放到棋盘上了"的那些（哪只怪在哪几格）。</summary>
        private void BuildSpawnList(LevelDoc doc)
        {
            SpawnList.Children.Clear();
            if (doc == null || doc.Root == null) { SpawnNote.Text = ""; return; }
            DiyData diy = doc.Diy;
            SpawnNote.Text = _board != null && _board.Ok
                ? "棋盘：" + Path.GetFileName(_board.Path) + "（" + _board.Nodes.Count + " 格）"
                : (_board == null ? "" : _board.Note);

            int bad = 0;
            foreach (BoardMonsterRef r in diy.BoardMonsters) if (_board == null || !_board.Ok || _board.Get(r.Node) == null) bad++;

            var head = new TextBlock();
            head.Text = "棋盘上放着 " + diy.BoardMonsters.Count + " 处（怪种共 " + diy.Pool.Count
                + " 条）；落点不存在 " + bad + " 个";
            head.FontSize = 12.5;
            head.Foreground = bad > 0 ? UiBits.Br(this, "BadBrush") : UiBits.Br(this, "OkBrush");
            head.Margin = new Thickness(0, 0, 0, 8);
            head.TextWrapping = TextWrapping.Wrap;
            SpawnList.Children.Add(head);

            var ids = new List<int>();
            foreach (BoardMonsterRef r in diy.BoardMonsters) if (!ids.Contains(r.Node)) ids.Add(r.Node);
            ids.Sort();
            foreach (int id in ids)
            {
                var names = new List<string>();
                foreach (BoardMonsterRef r in diy.BoardMonsters)
                {
                    if (r.Node != id) continue;
                    PoolMonster m = DiyPool.Find(diy.Pool, r.Pool);
                    names.Add((m == null ? ("#" + r.Pool) : m.Display)
                        + "（血 " + r.Hp + " · 币 " + r.Gold
                        + (r.Event == 0 ? " · 开局组" : "") + "）");
                }
                BoardNode node = _board != null && _board.Ok ? _board.Get(id) : null;
                var line = new TextBlock();
                line.Text = "格 " + id + (node != null ? "（" + Land.Name(node.ShownLand) + "）" : "（★ 棋盘上没有这个格子）")
                    + " ← " + string.Join("，", names.ToArray());
                line.FontSize = 11.5;
                line.Foreground = node == null ? UiBits.Br(this, "BadBrush") : UiBits.Br(this, "FgDim");
                line.TextWrapping = TextWrapping.Wrap;
                line.Margin = new Thickness(0, 0, 0, 5);
                SpawnList.Children.Add(line);
            }
            if (diy.BoardMonsters.Count == 0)
            {
                var none = new TextBlock();
                none.Text = "棋盘上还没有怪。右键某一格 →「新增怪物」。";
                none.FontSize = 11.5;
                none.Foreground = UiBits.Br(this, "FgFaint");
                none.TextWrapping = TextWrapping.Wrap;
                SpawnList.Children.Add(none);
            }
        }
    }
}
