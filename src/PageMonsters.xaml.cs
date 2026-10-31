using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace DiyEditor
{
    /// <summary>
    /// 第④页：怪物。
    ///
    /// 用户 2026-10-03 的规格：
    ///   &gt; 怪物栏里是现在怪物池的所有怪物，**按照设置好的进度进行分类**，
    ///   &gt; 如果没配置则**另外分一栏**列出未配置的，点怪物支持编辑
    ///   &gt; **血量，防御，攻击，掉落星币，是否反击**，要把每个怪都列出来，支持单独设置属性
    ///
    /// ★ 2026-10-09 用户原话："**我要每个独立的怪物都分开，数值也要分开 diy 而不是现在这样**"
    ///   ⇒ 这一页列的**不再是"怪种"**，而是**每一处放置**（`BoardMonsterRef`）：
    ///     同一只怪放在格 17 两只、格 25 两只，这里就是**四条**，各改各的血/防/攻/星币/反击。
    ///   · 分栏仍按**事件**（用户上一轮拍过："只显示了一个事件1，不应该是显示每个进度刷的怪吗"），
    ///     最后一栏 = "开局就在场上"（`Event == 0`，写进 `start_monsters`）。
    ///   · 右边编辑框改的就是**选中那一处**；"怪种"（`diy.monsters[]` 里那一条）那份数值只是
    ///     新建放置 / 随机补那一行用的**默认值**（界面上照实说明，别让用户以为是两套数据）。
    /// </summary>
    public partial class PageMonsters : UserControl
    {
        /// <summary>现在选中的是**哪一处放置**（按对象身份认；它被删了就落回第一处）。</summary>
        private BoardMonsterRef _pick;

        /// <summary>上一次画的时候 DIY 侧数据的版本号（★ 对不上就说明别的页改过东西）。</summary>
        private int _seenDiyVersion = -1;

        public PageMonsters()
        {
            InitializeComponent();
            // ★★ 实时刷新（第 9 波 ③，用户原话："怪物页面要实时变化的，我在事件那边配置了还是没显示"）：
            //    别的页（进度/事件、棋盘）改了放置 ⇒ 本页变回可见时自己重建。
            IsVisibleChanged += delegate
            {
                if (!IsVisible) return;
                if (_seenDiyVersion == DiyNotify.Version) return;
                Reload();
            };
        }

        public void Reload()
        {
            if (SubLine == null) return;
            _seenDiyVersion = DiyNotify.Version;
            LevelDoc doc = MainWindow.Doc;
            if (doc == null || doc.Root == null)
            {
                SubLine.Text = "还没打开关卡。";
                ListHost.Children.Clear();
                EditorHost.Children.Clear();
                return;
            }
            DiyData d = doc.Diy;
            // ★★ 每次重建都重算派生账（哪只怪挂在哪个事件下）—— 界面上的编辑是原地改内存对象的，
            //    不重算就会拿着旧归属。
            DiyPool.BindProgress(d);
            int byEvent = 0, byStart = 0;
            foreach (BoardMonsterRef r in d.BoardMonsters)
            {
                if (r.Event == 0) byStart++;
                else byEvent++;
            }
            SubLine.Text = "怪物池 " + d.BoardMonsters.Count + " 处（怪种 " + d.Pool.Count + " 条）："
                + "事件刷的 " + byEvent + " 处、「开局就在场上」的 " + byStart + " 处。"
                + "★ 每一处放置自己一份数值，改一处不影响别处。"
                + (d.BoardMonsters.Count == 0 ? "（棋盘上右键格子 →「新增怪物」，怪就会进池子。）" : "");

            // ★★ 2026-10-10（用户原话：「但是我编辑器这里写的是开局刷怪是 0 啊」）：
            //   把"开局那一组 0 只"的**服务端语义**写在明面上 —— 这份关卡落盘时写的是
            //   `start_monsters: []`（键在、空数组）= **明确不刷**，不是"没写 ⇒ 回落官方那批"。
            //   有官方那批数据（`battle.INIT_MONSTERS`）就把那批也列出来 + 给一键填回的按钮。
            if (byStart == 0)
            {
                var official = OpeningMonsters.ForMap(doc.MapId);
                SubLine.Text += "\n★ 开局那一组现在是空的 ⇒ 这份关卡**开局不刷怪**"
                    + "（落盘写的是 start_monsters: []，服务端看到空数组就不再刷）。";
                if (official.Count > 0)
                    SubLine.Text += "官方那张图开局本来是 " + official.Count + " 只："
                        + OpeningMonsters.Brief(doc.MapId)
                        + " —— 想照官方开局就点下面的「用官方开局怪」。";
                else
                    SubLine.Text += "（拿不到这张图「官方那批开局怪」的数据 ⇒ 不放按钮、也不硬编；"
                        + "想开局就刷就在「开局就在场上」那一组自己放。）";
            }

            if (d.BoardMonsters.Count == 0)
            {
                ListHost.Children.Clear();
                var none = new TextBlock();
                none.Text = d.Pool.Count == 0
                    ? "怪物池是空的。去棋盘页右键某一格 →「新增怪物」放一只。"
                    : ("怪物池里有 " + d.Pool.Count + " 条怪种，但棋盘上还没有任何放置。"
                       + "去棋盘页右键某一格 →「新增怪物」放一只。");
                none.FontSize = 12.5;
                none.Foreground = UiBits.Br(this, "FgFaint");
                none.TextWrapping = TextWrapping.Wrap;
                ListHost.Children.Add(none);
                EditorHost.Children.Clear();
                return;
            }

            Build(d, doc);
        }

        private void Build(DiyData d, LevelDoc doc)
        {
            ListHost.Children.Clear();

            // 分栏 = **事件**（升序），最后是"开局就在场上"那一批
            var keys = new List<int>();
            foreach (BoardMonsterRef r in d.BoardMonsters) if (r.Event != 0 && !keys.Contains(r.Event)) keys.Add(r.Event);
            keys.Sort();

            foreach (int n in keys)
            {
                var got = new List<BoardMonsterRef>();
                foreach (BoardMonsterRef r in d.BoardMonsters) if (r.Event == n) got.Add(r);
                DiyEvent e = PageBoard.EventOfGroup(d, n);
                string title = PageBoard.GroupNameOf(d, n);
                ListHost.Children.Add(Group(title, EventSub(d, e, got.Count), got, n));
            }

            var start = new List<BoardMonsterRef>();
            foreach (BoardMonsterRef r in d.BoardMonsters) if (r.Event == 0) start.Add(r);
            // ★ 开局那一栏：有怪、或者这一页只有它、或者**能提供"用官方开局怪"** ⇒ 都画出来
            //   （最后那种是用户 2026-10-10 要的入口：开局组为空时给个一键填官方那批的按钮）
            bool canOfferOfficial = start.Count == 0 && OpeningMonsters.Known(doc.MapId);
            if (start.Count > 0 || keys.Count == 0 || canOfferOfficial)
            {
                var card = (GlassSurface)Group("开局就在场上",
                    "写进 start_monsters：一开局就站在棋盘上（" + start.Count + " 处）。"
                    + (start.Count == 0 ? "现在是空的 ⇒ 落盘写的是 start_monsters: []，开局**明确不刷**。" : ""),
                    start, 0);
                if (canOfferOfficial) AddOfficialButton(card, d, doc);
                ListHost.Children.Add(card);
            }

            BuildEditor(d, doc);
        }

        /// <summary>
        /// ★★ 2026-10-10：把「开局」那一栏的按钮补上 —— 一键把**这张图官方那批开局怪**
        ///   （服务端 `battle.INIT_MONSTERS`，见 `OpeningMonsters`）填进"开局就在场上"那一组。
        ///   只在"开局组为空 + 有那批数据"时才出现；点完是真写进模型的（同 `SaveDiy` 一条路）。
        /// </summary>
        private void AddOfficialButton(GlassSurface card, DiyData d, LevelDoc doc)
        {
            var stack = card.Content as StackPanel;
            if (stack == null) return;
            var official = OpeningMonsters.ForMap(doc.MapId);

            var row = new StackPanel();
            row.Orientation = Orientation.Horizontal;
            row.Margin = new Thickness(0, 10, 0, 0);
            var btn = new Button();
            btn.Style = (Style)FindResource("Primary");
            btn.Content = "用官方开局怪（" + official.Count + " 只）";
            btn.ToolTip = "把这张图官方那批开局怪照原样填进「开局就在场上」："
                + OpeningMonsters.Brief(doc.MapId)
                + "\n数据来源：" + OpeningMonsters.Note;
            btn.Click += delegate
            {
                MainWindow.Undo.Capture(doc, "用官方开局怪（" + official.Count + " 只）");
                int n = OpeningMonsters.FillInto(d, doc.MapId);
                doc.SaveDiy();
                MainWindow.RefreshAll();
                Reload();
                SubLine.Text += "\n★ 刚填进 " + n + " 只官方开局怪（已写进 start_monsters）。"
                    + "每一只的数值都能在下面单独改。";
            };
            row.Children.Add(btn);
            stack.Children.Add(row);
        }

        /// <summary>事件那一栏的副标题：放了几处 + 计划几只（差额就是随机补的）。</summary>
        private static string EventSub(DiyData d, DiyEvent e, int placed)
        {
            int plan = 0;
            if (e != null)
                foreach (DiyProgressItem it in e.Items)
                    foreach (DiyEffect x in it.Effects)
                        if (x.Kind == EffectKind.Spawn) plan += x.Count < 1 ? 1 : x.Count;
            return "这个事件刷出来的怪（棋盘上放了 " + placed + " 处"
                + (plan > placed ? "；条词的计划只数 " + plan + " ⇒ 差额 " + (plan - placed) + " 只按随机空地补" : "")
                + "）";
        }

        private FrameworkElement Group(string title, string sub, List<BoardMonsterRef> got, int eventKey)
        {
            var card = new GlassSurface();
            card.Style = (Style)FindResource("CapBig");
            card.Margin = new Thickness(0, 0, 0, 14);
            var stack = new StackPanel();

            var head = new TextBlock();
            head.Text = title;
            head.Style = (Style)FindResource("H1");
            stack.Children.Add(head);

            var note = new TextBlock();
            note.Text = sub;
            note.Style = (Style)FindResource("FieldHint");
            note.Margin = new Thickness(0, 6, 0, 0);
            note.TextWrapping = TextWrapping.Wrap;
            stack.Children.Add(note);

            var host = new StackPanel();
            host.Margin = new Thickness(0, 10, 0, 0);
            foreach (BoardMonsterRef r in got) host.Children.Add(Row(r));
            foreach (FrameworkElement extra in RandomRows(eventKey))
                host.Children.Add(extra);
            if (got.Count == 0 && host.Children.Count == 0)
            {
                var none = new TextBlock();
                none.Text = "这一栏现在是空的。";
                none.FontSize = 11.5;
                none.Foreground = UiBits.Br(this, "FgFaint");
                host.Children.Add(none);
            }
            stack.Children.Add(host);
            card.Content = stack;
            return card;
        }

        /// <summary>
        /// 这一组里"没有格子"的那几只（随机补）—— 它们**不是放置**，所以只作说明：
        ///   · 开局组：`StartRandom` 里那几条；
        ///   · 事件组：条词的计划只数 − 实际放了几处 = 写盘时补成 `node:-1` 那一行。
        ///   数值口径：**取怪种那份**（见 `DiyPool.SyncProgressMonsters/SyncProgressEvents` 的注释）。
        /// </summary>
        private List<FrameworkElement> RandomRows(int eventKey)
        {
            var got = new List<FrameworkElement>();
            LevelDoc doc = MainWindow.Doc;
            DiyData d = doc == null ? null : doc.Diy;
            if (d == null) return got;

            if (eventKey == 0)
            {
                foreach (StartSpawn s in d.StartRandom)
                {
                    PoolMonster m = DiyPool.Find(d.Pool, s.Pool);
                    if (m == null || s.Count < 1) continue;
                    got.Add(RandomLine("随机格 ×" + s.Count, m));
                }
                return got;
            }

            DiyEvent e = PageBoard.EventOfGroup(d, eventKey);
            if (e == null) return got;
            var pools = new List<int>();
            var wants = new List<int>();
            foreach (DiyProgressItem it in e.Items)
                foreach (DiyEffect x in it.Effects)
                {
                    if (x.Kind != EffectKind.Spawn) continue;
                    if (DiyPool.Find(d.Pool, x.Pool) == null) continue;
                    int w = x.Count < 1 ? 1 : x.Count;
                    int idx = pools.IndexOf(x.Pool);
                    if (idx < 0) { pools.Add(x.Pool); wants.Add(w); }
                    else if (w > wants[idx]) wants[idx] = w;
                }
            for (int k = 0; k < pools.Count; k++)
            {
                int placed = 0;
                foreach (BoardMonsterRef r in d.BoardMonsters)
                    if (r.Event == eventKey && r.Pool == pools[k]) placed++;
                int rest = placed == 0 ? wants[k] : wants[k] - placed;
                if (rest <= 0) continue;
                PoolMonster m = DiyPool.Find(d.Pool, pools[k]);
                if (m != null) got.Add(RandomLine("随机格 ×" + rest, m));
            }
            return got;
        }

        private FrameworkElement RandomLine(string where, PoolMonster m)
        {
            var t = new TextBlock();
            t.Text = where + "  " + m.Display + " —— 数值取怪种：血 " + m.Hp + " · 防 " + m.Defense
                + " · 攻 " + m.Attack + " · 币 " + m.Gold + (m.Counter ? " · 会反击" : " · 不反击")
                + "（没有格子 ⇒ 没有「这一只」的数值可改）";
            t.FontSize = 11.5;
            t.Foreground = UiBits.Br(this, "FgFaint");
            t.TextWrapping = TextWrapping.Wrap;
            t.Margin = new Thickness(2, 4, 0, 0);
            return t;
        }

        /// <summary>一处放置一行：怪名 + 表 id + 它**自己**的四项 + 反击 + 放在哪一格。</summary>
        private FrameworkElement Row(BoardMonsterRef r)
        {
            BoardMonsterRef cap = r;
            bool on = ReferenceEquals(cap, _pick);
            PoolMonster m = PoolOf(cap);

            var box = new Border();
            box.CornerRadius = new CornerRadius(12);
            box.Padding = new Thickness(10, 8, 12, 8);
            box.Margin = new Thickness(0, 0, 0, 6);
            int alpha = on ? 46 : 20;
            box.Background = new SolidColorBrush(Color.FromArgb((byte)alpha, 255, 255, 255));
            box.BorderBrush = on ? UiBits.Br(this, "Accent")
                                 : new SolidColorBrush(Color.FromArgb(40, 255, 255, 255));
            box.BorderThickness = new Thickness(on ? 2.4 : 1.0);
            box.Cursor = Cursors.Hand;
            box.MouseLeftButtonDown += delegate { _pick = cap; Reload(); };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(48) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var frame = new Border();
            frame.Width = 42;
            frame.Height = 42;
            frame.CornerRadius = new CornerRadius(21);
            // ★ 2026-10-10 用户："怪物底部怎么有个黑底，不需要这个" ——
            //   那层黑底**是我们自己给头像加的深色容器**（原来这里 `Background=FromArgb(220,12,16,26)`），
            //   不是图自带的：`UT_AccountHeadShot_Monster_*.png` 四角 alpha 全是 0、最外一圈 alpha 均值 0.0，
            //   标准透明 PNG（实测见 `tools\_alpha_probe.py` 的 `_alpha_report.txt`）。
            //   ⇒ 像素小人是**带透明通道的不规则形状**，铺深色底就成了一块黑方块。
            //   有图时**不给任何底色**，直接让透明图落在玻璃面板上；只有"没图"那种回落才给一层淡底垫名字。
            frame.BorderBrush = UiBits.Br(this, "BadBrush");
            frame.BorderThickness = new Thickness(1.4);
            frame.VerticalAlignment = VerticalAlignment.Top;
            // ★ 统一取图（图鉴像素小人 → 半身像 → 手牌画），像素小人自动走最近邻放大。
            var im = MonsterArt.Make(m);
            if (im != null)
            {
                im.Stretch = Stretch.UniformToFill;
                im.Clip = new EllipseGeometry(new Point(21, 21), 21, 21);
                frame.Child = im;
            }
            else
            {
                frame.Background = new SolidColorBrush(Color.FromArgb(140, 12, 16, 26));
                var t2 = new TextBlock();
                t2.Text = "#" + cap.Pool;
                t2.FontSize = 10.5;
                t2.Foreground = Brushes.White;
                t2.HorizontalAlignment = HorizontalAlignment.Center;
                t2.VerticalAlignment = VerticalAlignment.Center;
                frame.Child = t2;
            }
            Grid.SetColumn(frame, 0);
            grid.Children.Add(frame);

            var text = new StackPanel();
            text.Margin = new Thickness(10, 0, 0, 0);

            var title = new TextBlock();
            title.Text = "格 " + cap.Node + "　" + (m == null ? ("池内 #" + cap.Pool) : (m.Name + "（表 " + m.Table + "）"));
            title.FontSize = 12.5;
            title.FontWeight = FontWeights.SemiBold;
            title.Foreground = UiBits.Br(this, on ? "Accent" : "Fg");
            title.TextWrapping = TextWrapping.Wrap;
            text.Children.Add(title);

            var sub = new TextBlock();
            sub.Text = "血 " + cap.Hp + " · 防 " + cap.Defense + " · 攻 " + cap.Attack
                + " · 币 " + cap.Gold + (cap.Counter ? " · 会反击" : " · 不反击");
            sub.FontSize = 11.5;
            sub.Foreground = UiBits.Br(this, "FgDim");
            sub.TextWrapping = TextWrapping.Wrap;
            sub.Margin = new Thickness(0, 4, 0, 0);
            text.Children.Add(sub);

            Grid.SetColumn(text, 1);
            grid.Children.Add(text);
            box.Child = grid;
            return box;
        }

        // ============================================================ 右边：这一处的五项
        private void BuildEditor(DiyData d, LevelDoc doc)
        {
            EditorHost.Children.Clear();
            if (_pick == null || !d.BoardMonsters.Contains(_pick))
                _pick = d.BoardMonsters.Count > 0 ? d.BoardMonsters[0] : null;
            BoardMonsterRef r = _pick;
            if (r == null) return;
            BoardMonsterRef cap = r;
            PoolMonster m = PoolOf(cap);

            var card = new GlassSurface();
            card.Style = (Style)FindResource("Cap");
            var stack = new StackPanel();

            var head = new TextBlock();
            head.Text = "格 " + cap.Node + " 上的" + (m == null ? ("池内 #" + cap.Pool) : (" #" + cap.Pool + " " + m.Name));
            head.Style = (Style)FindResource("H1");
            stack.Children.Add(head);

            var meta = new TextBlock();
            meta.Text = "组：" + PageBoard.GroupNameOf(d, cap.Event)
                + (m == null ? "" : (" · 表 " + m.Table + " · " + m.ClassName))
                + "\n★ 改的是「这一处放置」：同一只怪放在别格 / 别事件的那几条不受影响。";
            meta.Style = (Style)FindResource("FieldHint");
            meta.Margin = new Thickness(0, 8, 0, 0);
            meta.TextWrapping = TextWrapping.Wrap;
            stack.Children.Add(meta);

            // ★ 统一取图（图鉴像素小人 → 半身像 → 手牌画），像素小人自动走最近邻放大。
            var img = MonsterArt.Make(m);
            if (img != null)
            {
                img.Width = 84;
                img.Height = 84;
                img.Stretch = Stretch.Uniform;
                img.HorizontalAlignment = HorizontalAlignment.Left;
                img.Margin = new Thickness(0, 10, 0, 0);
                img.ToolTip = MonsterArt.SourceNote(m);   // ★ 如实说这张图哪来的
                stack.Children.Add(img);
            }

            var hp = new TextBox();
            var def = new TextBox();
            var atk = new TextBox();
            var gold = new TextBox();
            AddNum(stack, hp, "血量", cap.Hp);
            AddNum(stack, def, "防御", cap.Defense);
            AddNum(stack, atk, "攻击", cap.Attack);
            AddNum(stack, gold, "掉落星币", cap.Gold);

            var counter = new CheckBox();
            counter.Style = (Style)FindResource("Check");
            counter.Content = "是否反击";
            counter.IsChecked = cap.Counter;
            counter.Margin = new Thickness(0, 12, 0, 0);
            counter.ToolTip = "勾上 = 这只怪挨打时会反击。";
            stack.Children.Add(counter);

            var line = new TextBlock();
            line.Style = (Style)FindResource("FieldHint");
            line.Margin = new Thickness(0, 10, 0, 0);
            line.TextWrapping = TextWrapping.Wrap;
            line.Text = m == null ? "" :
                ("怪种 #" + m.Id + "（默认值，新建放置 / 随机补那一行用它）：血 " + m.Hp + " · 防 " + m.Defense
                 + " · 攻 " + m.Attack + " · 币 " + m.Gold + (m.Counter ? " · 会反击" : " · 不反击"));
            stack.Children.Add(line);

            var ops = new StackPanel();
            ops.Orientation = Orientation.Horizontal;
            ops.Margin = new Thickness(0, 12, 0, 0);
            var save = new Button();
            save.Style = (Style)FindResource("Primary");
            save.Content = "保存这一处";
            save.ToolTip = "只改这一处放置的数值（同一只怪放在别处的那些一点都不动）。";
            save.Click += delegate
            {
                long v;
                if (!ReadBox(hp, out v)) { line.Text = "血量那一格不是整数，没保存。"; return; }
                long nhp = v;
                if (!ReadBox(def, out v)) { line.Text = "防御那一格不是整数，没保存。"; return; }
                long ndf = v;
                if (!ReadBox(atk, out v)) { line.Text = "攻击那一格不是整数，没保存。"; return; }
                long nat = v;
                if (!ReadBox(gold, out v)) { line.Text = "掉落星币那一格不是整数，没保存。"; return; }
                long ngd = v;
                MainWindow.Undo.Capture(doc, "改格 " + cap.Node + " 上那一处怪的数值");
                cap.Hp = nhp; cap.Defense = ndf; cap.Attack = nat; cap.Gold = ngd;
                cap.Counter = counter.IsChecked == true;
                doc.SaveDiy();
                MainWindow.RefreshAll();
                Reload();
            };
            ops.Children.Add(save);

            var one = new Button();
            one.Style = (Style)FindResource("Ghost");
            one.Content = "把这一处拿掉";
            one.Margin = new Thickness(10, 0, 0, 0);
            one.ToolTip = "只把格 " + cap.Node + " 上的这一条摘掉；同一只怪放在别处的那些、池子里那条都留着。";
            one.Click += delegate
            {
                MainWindow.Undo.Capture(doc, "拿掉格 " + cap.Node + " 上那一处怪");
                d.BoardMonsters.Remove(cap);
                doc.SaveDiy();
                _pick = null;
                MainWindow.RefreshAll();
                Reload();
            };
            ops.Children.Add(one);
            stack.Children.Add(ops);

            if (m != null)
            {
                int shared = 0;
                foreach (BoardMonsterRef x in d.BoardMonsters) if (x.Pool == m.Id) shared++;
                var drop = new Button();
                drop.Style = (Style)FindResource("Ghost");
                drop.Content = "从池子里拿掉这种怪";
                drop.Margin = new Thickness(0, 10, 0, 0);
                drop.HorizontalAlignment = HorizontalAlignment.Left;
                drop.ToolTip = "连同棋盘上放着它的每一处一起清掉（进度条词、获胜条件里指着它的地方也清）。";
                drop.Click += delegate
                {
                    MessageBoxResult ok = MessageBox.Show(Window.GetWindow(this),
                        "把怪种 #" + m.Id + " " + m.Name + " 从池子里拿掉？\n\n"
                        + "棋盘上放着它的 " + shared + " 处放置、进度里指着它的条词、获胜条件里的目标都会一起清掉。",
                        "小小兔趴 · DIY 关卡编辑器", MessageBoxButton.OKCancel, MessageBoxImage.Warning);
                    if (ok != MessageBoxResult.OK) return;
                    DiyPool.DropFromPool(doc.Diy, m.Id);
                    doc.SaveDiy();
                    _pick = null;
                    MainWindow.RefreshAll();
                };
                stack.Children.Add(drop);
            }

            card.Content = stack;
            EditorHost.Children.Add(card);
        }

        private PoolMonster PoolOf(BoardMonsterRef r)
        {
            LevelDoc doc = MainWindow.Doc;
            DiyData d = doc == null ? null : doc.Diy;
            return d == null ? null : DiyPool.Find(d.Pool, r.Pool);
        }

        private void AddNum(StackPanel host, TextBox box, string label, long value)
        {
            var g = new Grid();
            g.Margin = new Thickness(0, 12, 0, 0);
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(92) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var lb = new TextBlock();
            lb.Text = label;
            lb.Style = (Style)FindResource("FieldLabel");
            lb.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(lb, 0);
            g.Children.Add(lb);
            box.Style = (Style)FindResource("Field");
            box.Text = value.ToString(CultureInfo.InvariantCulture);
            box.MaxLength = 9;
            Grid.SetColumn(box, 1);
            g.Children.Add(box);
            host.Children.Add(g);
        }

        private static bool ReadBox(TextBox box, out long value)
        {
            value = 0;
            string t = (box.Text ?? "").Trim();
            if (t.Length == 0) return true;
            return long.TryParse(t, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
        }
    }
}
