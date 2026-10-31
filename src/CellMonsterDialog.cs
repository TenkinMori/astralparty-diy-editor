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
    /// ★★ 「编辑这一格的怪…」—— 用户 2026-10-08 原话：
    ///
    ///   &gt; **在棋盘里面右键有怪物的格子可以编辑这格里面的怪物**
    ///
    /// ⇒ 这一格上**当前组**（开局组 / 某个事件）有几处放置就列几处（头像 + 名字 + 它自己的数值摘要），
    ///   点一处就在这里改**它自己**的五项（血量 / 防御 / 攻击 / 掉落星币 / 是否反击），
    ///   外加一个「删掉这一处」。
    ///
    /// ★★ 2026-10-09 用户原话："**我要每个独立的怪物都分开，数值也要分开 diy 而不是现在这样**"
    ///   ⇒ 这里改的是**这一处放置**（`BoardMonsterRef`）自己的那份数值：
    ///     同一只怪放在格 17 和格 25 = 两条互不影响的记录，改一条绝不动另一条。
    ///   怪种（`diy.monsters[]` 里那一条）那份只当**默认值**（新建放置 / 随机补那一行用它），
    ///   界面上照实说明，别让用户以为改的是同一份。
    ///
    /// ★★ 「删掉这一处」按**对象**删（不是"按池内序号删第一条"）：同一格上可能放了两条同种怪、
    ///   数值还不一样，按序号删会删错那一条。怪种那一条**留着**（用户规格：删格上的怪 ≠ 从池子里拿掉）。
    ///
    /// ★★ 每次改动都 `SaveDiy()` + `MainWindow.RefreshAll()`：窗口还开着的时候，
    ///   服务端读的那份 json（`progress_events.*.spawn` / `start_monsters` 里那几行数值）
    ///   就已经跟着变了，不存在"关了窗口才算数"的中间态。
    /// </summary>
    public sealed class CellMonsterDialog : Window
    {
        private readonly LevelDoc _doc;
        private readonly int _node;
        private readonly int _group;
        private readonly string _groupName;

        private readonly StackPanel _list = new StackPanel();
        private readonly StackPanel _editor = new StackPanel();
        private readonly TextBlock _head = new TextBlock();
        private readonly TextBlock _note = new TextBlock();

        /// <summary>现在选中的是**哪一处放置**（对象身份；它被删了就落回第一处）。</summary>
        private BoardMonsterRef _pick;

        /// <summary>这个窗口里**有没有改过数据**（棋盘页靠它决定要不要写那句提示）。</summary>
        public bool Dirty { get; private set; }

        public CellMonsterDialog(LevelDoc doc, int node, int group, string groupName)
        {
            _doc = doc;
            _node = node;
            _group = group;
            _groupName = groupName ?? "";

            Title = "编辑这一格的怪";
            Width = 900;
            Height = 640;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = new SolidColorBrush(Color.FromArgb(255, 10, 13, 22));
            Foreground = (Brush)FindResource("Fg");
            FontFamily = (FontFamily)FindResource("AppFont");
            FontSize = 13;

            var root = new Grid();
            DialogSkin.Paint(root);      // 深底 + 白字（对话框是独立 Window，没有继承来的 Foreground）
            root.Width = Width;
            root.Height = Height;
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(42) });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            // ── 标题栏（跟别的二级窗口一个样子：能拖、右上角关）
            var bar = new Border();
            bar.Background = new SolidColorBrush(Color.FromArgb(60, 255, 255, 255));
            bar.MouseLeftButtonDown += delegate (object s, MouseButtonEventArgs e)
            {
                if (e.ClickCount == 1) { try { DragMove(); } catch (Exception) { } }
            };
            var barGrid = new Grid();
            var barText = new TextBlock();
            barText.Text = "编辑这一格的怪";
            barText.FontSize = 13.5;
            barText.VerticalAlignment = VerticalAlignment.Center;
            barText.Margin = new Thickness(16, 0, 0, 0);
            barGrid.Children.Add(barText);
            var close = new Button();
            close.Style = (Style)FindResource("ChromeClose");
            close.Content = "\uE8BB";
            close.HorizontalAlignment = HorizontalAlignment.Right;
            close.Click += delegate { Finish(); };
            barGrid.Children.Add(close);
            bar.Child = barGrid;
            Grid.SetRow(bar, 0);
            root.Children.Add(bar);

            // ── 主体：左边"这一格上有哪几处"，右边"选中那一处的五项"
            var body = new Grid();
            body.Margin = new Thickness(18, 14, 18, 14);
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(386) });
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var left = new StackPanel();
            _head.Style = (Style)FindResource("H1");
            left.Children.Add(_head);

            _note.Style = (Style)FindResource("FieldHint");
            _note.Margin = new Thickness(0, 8, 0, 10);
            _note.TextWrapping = TextWrapping.Wrap;
            _note.MaxWidth = 356;
            left.Children.Add(_note);

            var scroll = new ScrollViewer();
            scroll.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            scroll.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
            scroll.Focusable = false;
            scroll.Content = _list;
            left.Children.Add(scroll);
            Grid.SetColumn(left, 0);
            body.Children.Add(left);

            var right = new ScrollViewer();
            right.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            right.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
            right.Focusable = false;
            right.Margin = new Thickness(18, 0, 0, 0);
            right.Content = _editor;
            Grid.SetColumn(right, 1);
            body.Children.Add(right);

            Grid.SetRow(body, 1);
            root.Children.Add(body);
            Content = root;

            Rebuild();
        }

        /// <summary>关闭窗口：`ShowDialog` 那条路上不回写 DialogResult（改用 `Dirty` 说话）。</summary>
        private void Finish()
        {
            try { Close(); }
            catch (Exception) { }
        }

        // ================================================================ 画面
        /// <summary>重铺左右两边（数据一变就整块重来，跟别的页一个套路）。</summary>
        private void Rebuild()
        {
            List<BoardMonsterRef> refs = Refs();
            if (_pick == null || !refs.Contains(_pick))
                _pick = refs.Count > 0 ? refs[0] : null;

            _head.Text = "格 " + _node + " · " + _groupName;
            _note.Text = "这一格上（" + _groupName + "）有 " + refs.Count + " 处放置。"
                + "点一处就能改「它自己」的血量 / 防御 / 攻击 / 掉落星币 / 是否反击 ——"
                + "同一只怪放在别格 / 别事件的那几处一点都不动。";

            BuildList(refs);
            BuildEditor();
        }

        private void BuildList(List<BoardMonsterRef> refs)
        {
            _list.Children.Clear();
            if (refs.Count == 0)
            {
                var none = new TextBlock();
                none.Text = "这一格上（" + _groupName + "）已经没有怪了。\n"
                    + "怪种（怪物池里那几条）还留着 —— 怪物页那一栏能看到它们。";
                none.FontSize = 12.5;
                none.Foreground = UiBits.Br(this, "FgFaint");
                none.TextWrapping = TextWrapping.Wrap;
                none.Margin = new Thickness(0, 6, 0, 0);
                _list.Children.Add(none);
                return;
            }

            for (int k = 0; k < refs.Count; k++) _list.Children.Add(Row(k, refs.Count, refs[k]));
        }

        /// <summary>一处放置一行：头像 + 名字 + **它自己**的数值摘要（点了就选中它）。</summary>
        private FrameworkElement Row(int index, int total, BoardMonsterRef r)
        {
            BoardMonsterRef cap = r;
            bool on = ReferenceEquals(cap, _pick);
            PoolMonster m = PoolOf(cap);

            var box = new Border();
            box.CornerRadius = new CornerRadius(12);
            box.Padding = new Thickness(10, 8, 12, 8);
            box.Margin = new Thickness(0, 0, 0, 8);
            int alpha = on ? 46 : 20;
            box.Background = new SolidColorBrush(Color.FromArgb((byte)alpha, 255, 255, 255));
            box.BorderBrush = on
                ? UiBits.Br(this, "Accent")
                : new SolidColorBrush(Color.FromArgb(40, 255, 255, 255));
            box.BorderThickness = new Thickness(on ? 2.4 : 1.0);
            box.Cursor = Cursors.Hand;
            box.ToolTip = Summary(cap, m);
            box.MouseLeftButtonDown += delegate { _pick = cap; Rebuild(); };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(56) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var frame = new Border();
            frame.Width = 48;
            frame.Height = 48;
            frame.CornerRadius = new CornerRadius(24);
            // ★ 同上（用户："怪物底部怎么有个黑底"）：黑底是我们加的深色容器，不是图自带的
            //   （像素小人 PNG 四角 alpha=0、最外一圈 alpha 均值 0.0）。有图时不铺底色。
            frame.BorderBrush = UiBits.Br(this, "BadBrush");
            frame.BorderThickness = new Thickness(1.4);
            frame.VerticalAlignment = VerticalAlignment.Top;
            // ★ 跟棋盘上、怪物页**同一个取图函数**（图鉴像素小人 → 半身像 → 手牌画）
            var im = MonsterArt.Make(m);
            if (im != null)
            {
                im.Stretch = Stretch.UniformToFill;
                im.Clip = new EllipseGeometry(new Point(24, 24), 24, 24);
                frame.Child = im;
            }
            else
            {
                frame.Background = new SolidColorBrush(Color.FromArgb(140, 12, 16, 26));
                var t = new TextBlock();
                t.Text = "#" + cap.Pool;
                t.FontSize = 11;
                t.Foreground = Brushes.White;
                t.HorizontalAlignment = HorizontalAlignment.Center;
                t.VerticalAlignment = VerticalAlignment.Center;
                frame.Child = t;
            }
            Grid.SetColumn(frame, 0);
            grid.Children.Add(frame);

            var text = new StackPanel();
            text.Margin = new Thickness(10, 0, 0, 0);

            var title = new TextBlock();
            title.Text = (index + 1) + "/" + total + "  " + (m == null ? ("池内 #" + cap.Pool) : m.Display);
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

        private void BuildEditor()
        {
            _editor.Children.Clear();
            BoardMonsterRef r = _pick;
            if (r == null)
            {
                var none = new TextBlock();
                none.Text = "左边这一格上（" + _groupName + "）没有怪可编辑了。\n"
                    + "要再放怪：关掉这个窗口，右键那一格 →「新增怪物」。";
                none.Style = (Style)FindResource("FieldHint");
                none.TextWrapping = TextWrapping.Wrap;
                none.MaxWidth = 420;
                _editor.Children.Add(none);
                return;
            }
            BoardMonsterRef cap = r;
            PoolMonster m = PoolOf(cap);

            var head = new TextBlock();
            head.Text = "格 " + cap.Node + " 上的" + (m == null ? ("池内 #" + cap.Pool) : (" #" + cap.Pool + " " + m.Name));
            head.Style = (Style)FindResource("H1");
            _editor.Children.Add(head);

            var meta = new TextBlock();
            meta.Text = (m == null ? "" : ("表 " + m.Table + " · " + m.ClassName + " · "))
                + _groupName + "\n★ 改的是「这一处放置」：同一只怪放在别格 / 别事件的那几处不受影响。";
            meta.Style = (Style)FindResource("FieldHint");
            meta.Margin = new Thickness(0, 8, 0, 0);
            meta.TextWrapping = TextWrapping.Wrap;
            meta.MaxWidth = 420;
            _editor.Children.Add(meta);

            // ★ 跟棋盘上、怪物页**同一个取图函数**（图鉴像素小人 → 半身像 → 手牌画）
            var img = MonsterArt.Make(m);
            if (img != null)
            {
                img.Width = 88;
                img.Height = 88;
                img.Stretch = Stretch.Uniform;
                img.HorizontalAlignment = HorizontalAlignment.Left;
                img.Margin = new Thickness(0, 10, 0, 0);
                img.ToolTip = MonsterArt.SourceNote(m);   // ★ 如实说这张图哪来的
                _editor.Children.Add(img);
            }

            var hpBox = new TextBox();
            var defBox = new TextBox();
            var atkBox = new TextBox();
            var goldBox = new TextBox();
            AddNum(_editor, hpBox, "血量", cap.Hp);
            AddNum(_editor, defBox, "防御", cap.Defense);
            AddNum(_editor, atkBox, "攻击", cap.Attack);
            AddNum(_editor, goldBox, "掉落星币", cap.Gold);

            var counter = new CheckBox();
            counter.Style = (Style)FindResource("Check");
            counter.Content = "是否反击";
            counter.IsChecked = cap.Counter;
            counter.Margin = new Thickness(0, 12, 0, 0);
            counter.ToolTip = "勾上 = 这一处的怪挨打时会反击。";
            _editor.Children.Add(counter);

            var line = new TextBlock();
            line.Style = (Style)FindResource("FieldHint");
            line.Margin = new Thickness(0, 10, 0, 0);
            line.TextWrapping = TextWrapping.Wrap;
            line.MaxWidth = 420;
            line.Text = PoolCompareText(cap, m);
            _editor.Children.Add(line);

            var ops = new StackPanel();
            ops.Orientation = Orientation.Horizontal;
            ops.Margin = new Thickness(0, 12, 0, 0);

            var save = new Button();
            save.Style = (Style)FindResource("Primary");
            save.Content = "保存这一处";
            save.ToolTip = "只改这一处放置的数值（同一只怪放在别处的那几处一点都不动）。";
            save.Click += delegate
            {
                long v;
                if (!ReadBox(hpBox, out v)) { line.Text = "血量那一格不是整数，没保存。"; return; }
                long hp = v;
                if (!ReadBox(defBox, out v)) { line.Text = "防御那一格不是整数，没保存。"; return; }
                long df = v;
                if (!ReadBox(atkBox, out v)) { line.Text = "攻击那一格不是整数，没保存。"; return; }
                long at = v;
                if (!ReadBox(goldBox, out v)) { line.Text = "掉落星币那一格不是整数，没保存。"; return; }
                long gd = v;
                SaveOne(cap, hp, df, at, gd, counter.IsChecked == true);
            };
            ops.Children.Add(save);

            var del = new Button();
            del.Style = (Style)FindResource("Ghost");
            del.Content = "删掉这一处";
            del.Margin = new Thickness(10, 0, 0, 0);
            del.ToolTip = "只把这一处从格 " + cap.Node + "（" + _groupName + "）上拿掉；"
                + "同一只怪放在别处的那几处、怪种都留着。";
            del.Click += delegate { DeleteOne(cap); };
            ops.Children.Add(del);
            _editor.Children.Add(ops);

            // ★★ 2026-10-09 用户要的第三个按钮：「把这套数值应用到全体相同怪（N 处）」——
            //   把这一处的四项 + 是否反击写进**这个怪种在棋盘上的每一处放置**（跨格 / 跨事件 /
            //   开局组那批），并同步怪种默认值（新建放置 / 随机补那一行也跟着变）。
            //   N = 同怪种在棋盘上的放置数（不含随机那一行）；N <= 1 时灰掉（只有它自己，没意义）。
            int siblings = 0;
            DiyData dd = Pool();
            if (m != null && dd != null)
                foreach (BoardMonsterRef x in dd.BoardMonsters) if (x.Pool == m.Id) siblings++;
            var applyAll = new Button();
            applyAll.Style = (Style)FindResource("Ghost");
            applyAll.Content = "把这套数值应用到全体相同怪（" + siblings + " 处）";
            applyAll.Margin = new Thickness(0, 10, 0, 0);
            applyAll.HorizontalAlignment = HorizontalAlignment.Left;
            applyAll.IsEnabled = m != null && siblings > 1;
            applyAll.ToolTip = m == null ? "" :
                ("把这一处的四项数值 + 是否反击写进「" + m.Name + "」在棋盘上的 " + siblings
                 + " 处放置（跨格 / 跨事件 / 开局组那批），并把怪种默认值也改成这一套"
                 + "（新建放置、随机补那一行跟着变）。撤消可以整步退回。");
            applyAll.Click += delegate
            {
                long v;
                if (!ReadBox(hpBox, out v)) { line.Text = "血量那一格不是整数，没应用。"; return; }
                long hp = v;
                if (!ReadBox(defBox, out v)) { line.Text = "防御那一格不是整数，没应用。"; return; }
                long df = v;
                if (!ReadBox(atkBox, out v)) { line.Text = "攻击那一格不是整数，没应用。"; return; }
                long at = v;
                if (!ReadBox(goldBox, out v)) { line.Text = "掉落星币那一格不是整数，没应用。"; return; }
                long gd = v;
                bool cnt = counter.IsChecked == true;
                MessageBoxResult ok = MessageBox.Show(Window.GetWindow(this),
                    "把这一套数值应用到「" + (m == null ? "这一只" : m.Name) + "」在棋盘上的 "
                    + siblings + " 处放置？\n\n"
                    + "血 " + hp + " · 防 " + df + " · 攻 " + at + " · 币 " + gd
                    + (cnt ? " · 会反击" : " · 不反击") + "\n\n"
                    + "这 " + siblings + " 处（跨格 / 跨事件 / 开局组）会全部改成这一套，"
                    + "怪种默认值也跟着改；撤消可以整步退回。",
                    "小小兔趴 · DIY 关卡编辑器", MessageBoxButton.OKCancel, MessageBoxImage.Warning);
                if (ok != MessageBoxResult.OK) return;
                ApplyAll(hp, df, at, gd, cnt);
            };
            _editor.Children.Add(applyAll);

            var done = new Button();
            done.Style = (Style)FindResource("Ghost");
            done.Content = "改完了，关掉";
            done.Margin = new Thickness(0, 14, 0, 0);
            done.HorizontalAlignment = HorizontalAlignment.Left;
            done.Click += delegate { Finish(); };
            _editor.Children.Add(done);
        }

        /// <summary>
        /// ★★ 2026-10-09（用户问"图里那行是不是重复了"）：当前这一处与怪种**完全一致**时，
        ///   只写一句短话（一眼就知道"没别的东西"）；**不一致**时才把怪种那份列出来，
        ///   并标出差在哪（例如「⟵ 当前：血 33、反击已改」）。
        ///   ★ 这一行不删：它承载"随机补那一行取怪种那份"的口径（见 `DiyPool.SyncProgressEvents`）。
        /// </summary>
        private static string PoolCompareText(BoardMonsterRef r, PoolMonster m)
        {
            if (m == null || r == null) return "";
            bool same = r.Hp == m.Hp && r.Defense == m.Defense && r.Attack == m.Attack
                        && r.Gold == m.Gold && r.Counter == m.Counter;
            if (same)
                return "与怪种 #" + m.Id + " 一致 —— 新建放置 / 随机补那一行用的就是这一套值。";

            var diff = new List<string>();
            if (r.Hp != m.Hp) diff.Add("血 " + r.Hp);
            if (r.Defense != m.Defense) diff.Add("防 " + r.Defense);
            if (r.Attack != m.Attack) diff.Add("攻 " + r.Attack);
            if (r.Gold != m.Gold) diff.Add("币 " + r.Gold);
            if (r.Counter != m.Counter) diff.Add(r.Counter ? "反击已改" : "反击改回不反击");
            return "怪种 #" + m.Id + "（默认值，随机补那一行取它）：血 " + m.Hp + " · 防 " + m.Defense
                 + " · 攻 " + m.Attack + " · 币 " + m.Gold + (m.Counter ? " · 会反击" : " · 不反击")
                 + "　⟵ 当前：" + string.Join("、", diff.ToArray());
        }

        private void AddNum(StackPanel host, TextBox box, string label, long value)
        {
            var g = new Grid();
            g.Margin = new Thickness(0, 12, 0, 0);
            g.MaxWidth = 380;
            g.HorizontalAlignment = HorizontalAlignment.Left;
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(92) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(240) });

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

        // ================================================================ 数据
        private DiyData Pool()
        {
            return _doc == null ? null : _doc.Diy;
        }

        /// <summary>这一格上**当前组**的那几条放置（别的组放这儿的怪不在这份清单里）。</summary>
        private List<BoardMonsterRef> Refs()
        {
            DiyData d = Pool();
            return d == null ? new List<BoardMonsterRef>() : DiyPool.RefsAtNode(d, _node, _group);
        }

        /// <summary>这一处放置的怪种（池子里那一条；只用来取名字 / 头像 / 默认值）。</summary>
        private PoolMonster PoolOf(BoardMonsterRef r)
        {
            DiyData d = Pool();
            return (d == null || r == null) ? null : DiyPool.Find(d.Pool, r.Pool);
        }

        private string Summary(BoardMonsterRef r, PoolMonster m)
        {
            return (m == null ? ("池内 #" + r.Pool) : m.Display)
                + (m == null ? "" : ("（" + m.ClassName + "）"))
                + "\n血 " + r.Hp + " · 防 " + r.Defense + " · 攻 " + r.Attack + " · 币 " + r.Gold
                + (r.Counter ? " · 会反击" : " · 不反击")
                + "\n放在格 " + _node + "（" + _groupName + "）";
        }

        /// <summary>
        /// 把五项写进**这一处放置**，然后 `SaveDiy()` + `RefreshAll()`。
        /// ★ 为什么先 `Undo.Capture`：撤销栈存的是"动手之前整棵树的字节"，跟页面上的编辑一个规矩。
        /// </summary>
        internal bool SaveOne(BoardMonsterRef r, long hp, long defense, long attack, long gold, bool counter)
        {
            if (_doc == null || r == null) return false;
            MainWindow.Undo.Capture(_doc, "改格 " + _node + " 上那一处怪的数值");
            r.Hp = hp;
            r.Defense = defense;
            r.Attack = attack;
            r.Gold = gold;
            r.Counter = counter;
            _doc.SaveDiy();
            Dirty = true;
            MainWindow.RefreshAll();
            _pick = r;
            Rebuild();
            return true;
        }

        /// <summary>
        /// ★★ 2026-10-09（用户要的「把这套数值应用到全体相同怪」）：把这一套五项写进
        /// **这个怪种在棋盘上的每一处放置**（跨格 / 跨事件 / 含开局组那批），并同步怪种默认值。
        /// 返回改了几处（UI 的确认框由调用方先弹；自查直接调它）。
        /// </summary>
        internal int ApplyAll(long hp, long defense, long attack, long gold, bool counter)
        {
            if (_doc == null || _pick == null) return 0;
            DiyData d = _doc.Diy;
            if (d == null) return 0;
            MainWindow.Undo.Capture(_doc, "把这套数值应用到全体相同怪");
            int n = DiyPool.ApplyToSamePool(d, _pick.Pool, hp, defense, attack, gold, counter);
            _doc.SaveDiy();
            Dirty = true;
            MainWindow.RefreshAll();
            Rebuild();
            return n;
        }

        /// <summary>
        /// 把**这一处放置**从这一格上（当前组）拿掉。
        /// ★ 按对象删（同一格上两条同种怪也能删对那一条）；怪种那一条**留着**
        ///   （用户规格：删格上的怪 ≠ 从池子里拿掉）。
        /// </summary>
        internal bool DeleteOne(BoardMonsterRef r)
        {
            if (_doc == null || r == null) return false;
            DiyData d = _doc.Diy;
            if (d == null || !d.BoardMonsters.Contains(r)) return false;
            MainWindow.Undo.Capture(_doc, "删掉格 " + _node + " 上那一处怪");
            if (!d.BoardMonsters.Remove(r)) return false;
            _doc.SaveDiy();
            Dirty = true;
            MainWindow.RefreshAll();
            _pick = null;
            Rebuild();
            return true;
        }
    }
}
