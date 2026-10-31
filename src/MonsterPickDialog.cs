using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace DiyEditor
{
    /// <summary>
    /// ★★ 「新增怪物」的**二级菜单窗口** —— 用户 2026-10-03 的规格：
    ///
    ///   &gt; 点击新增怪物后会出现二级菜单窗口，里面有各种怪物，支持分类，
    ///   &gt; 点击添加后在棋盘指定格子显示怪物头像，怪物添加到怪物池里面
    ///
    /// ⇒ 各种怪物 = `Monster.bin` 全表；**分类** = 普通小怪 / 精英怪 / Boss 怪；
    ///   头像是 `UT_Monster_Bust_&lt;id&gt;_0`（跟棋盘上画的是同一张）。
    /// </summary>
    public sealed class MonsterPickDialog : Window
    {
        private readonly TextBox _search = new TextBox();
        private readonly WrapPanel _grid = new WrapPanel();
        private readonly TextBlock _hint = new TextBlock();
        private readonly Button _ok = new Button();
        private readonly List<Button> _tabs = new List<Button>();

        private MonsterRow _pick;
        private MonsterClass _tab = MonsterClass.Minion;

        /// <summary>非 null ⇒ 只列这批怪（"从怪物池里挑"用；分类页签这时不拦）。</summary>
        private Func<List<MonsterRow>> _fromPool;

        /// <summary>用户点了「添加」之后挑中的那只怪（null = 取消了）。</summary>
        public MonsterRow Picked { get { return _pick; } }

        /// <summary>
        /// ★ 只列**怪物池里现有的那些怪**（用户："新增什么怪物（在怪物池里面挑选）"）。
        ///   池子里的每只怪也会在这里出现（同一只可能会重复出现，那正是池子里的几条）。
        /// </summary>
        public static MonsterPickDialog ForPool(List<PoolMonster> pool)
        {
            var dlg = new MonsterPickDialog("新增怪物（从怪物池里挑）",
                "怪物池现在有 " + pool.Count + " 只 —— 这里挑一只挂到这条进度上。");
            var rows = new List<MonsterRow>();
            foreach (PoolMonster m in pool)
            {
                MonsterRow r = m.TableRow;
                if (r != null) rows.Add(r);
            }
            dlg._fromPool = delegate { return rows; };
            dlg.Fill();
            return dlg;
        }

        public MonsterPickDialog(string title, string sub)
        {
            Title = title;
            Width = 1000;
            Height = 720;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = new SolidColorBrush(Color.FromArgb(255, 10, 13, 22));
            Foreground = (Brush)FindResource("Fg");
            FontFamily = (FontFamily)FindResource("AppFont");
            FontSize = 13;

            var root = new Grid();
            // ★ 离屏渲染算得上 + 不给系统主题留机会：底色和文字色都写死在根上
            DialogSkin.Paint(root);       // 深底 + 白字（对话框是独立 Window，没有继承来的 Foreground）
            root.Width = Width;
            root.Height = Height;
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(42) });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            var bar = new Border();
            bar.Background = new SolidColorBrush(Color.FromArgb(60, 255, 255, 255));
            bar.MouseLeftButtonDown += delegate (object s, MouseButtonEventArgs e)
            {
                if (e.ClickCount == 1) { try { DragMove(); } catch (Exception) { } }
            };
            var barGrid = new Grid();
            var barText = new TextBlock();
            barText.Text = title;
            barText.FontSize = 13.5;
            barText.VerticalAlignment = VerticalAlignment.Center;
            barText.Margin = new Thickness(16, 0, 0, 0);
            barGrid.Children.Add(barText);
            var close = new Button();
            close.Style = (Style)FindResource("ChromeClose");
            close.Content = "\uE8BB";
            close.HorizontalAlignment = HorizontalAlignment.Right;
            close.Click += delegate { DialogResult = false; };
            barGrid.Children.Add(close);
            bar.Child = barGrid;
            Grid.SetRow(bar, 0);
            root.Children.Add(bar);

            var body = new Grid();
            body.Margin = new Thickness(18, 14, 18, 14);
            body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            body.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            _hint.Style = (Style)FindResource("Note");
            _hint.TextWrapping = TextWrapping.Wrap;
            _hint.MaxWidth = 900;
            _hint.Text = sub;
            Grid.SetRow(_hint, 0);
            body.Children.Add(_hint);

            // 搜索 + 分类（两个都是"分类"这一件事：按类分栏 + 按名字筛）
            var headRow = new DockPanel();
            headRow.Margin = new Thickness(0, 12, 0, 10);
            var tabPanel = new StackPanel();
            tabPanel.Orientation = Orientation.Horizontal;
            tabPanel.HorizontalAlignment = HorizontalAlignment.Left;
            foreach (MonsterClass c in BoardMonsters.ClassOrder)
            {
                MonsterClass cap = c;
                var b = new Button();
                b.Style = (Style)FindResource("Ghost");
                b.Padding = new Thickness(14, 6, 14, 6);
                b.Margin = new Thickness(0, 0, 8, 0);
                b.Content = ClassTabName(cap);
                b.Tag = cap;
                b.Click += delegate { _tab = cap; SyncTabs(); Fill(); };
                _tabs.Add(b);
                tabPanel.Children.Add(b);
            }
            DockPanel.SetDock(tabPanel, Dock.Left);
            headRow.Children.Add(tabPanel);

            var searchBox = new StackPanel();
            searchBox.Orientation = Orientation.Horizontal;
            searchBox.HorizontalAlignment = HorizontalAlignment.Right;
            var searchLabel = new TextBlock();
            searchLabel.Text = "搜索";
            searchLabel.Style = (Style)FindResource("FieldLabel");
            searchLabel.VerticalAlignment = VerticalAlignment.Center;
            searchBox.Children.Add(searchLabel);
            _search.Style = (Style)FindResource("Field");
            _search.Width = 220;
            _search.Margin = new Thickness(8, 0, 0, 0);
            _search.ToolTip = "按怪物名字或表 id 筛（留空 = 这一类全列）";
            _search.TextChanged += delegate { Fill(); };
            searchBox.Children.Add(_search);
            headRow.Children.Add(searchBox);

            Grid.SetRow(headRow, 1);
            body.Children.Add(headRow);

            var scroll = new ScrollViewer();
            scroll.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            scroll.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
            scroll.Focusable = false;
            scroll.Content = _grid;
            Grid.SetRow(scroll, 2);
            body.Children.Add(scroll);

            var foot = new StackPanel();
            foot.Orientation = Orientation.Horizontal;
            foot.HorizontalAlignment = HorizontalAlignment.Right;
            foot.Margin = new Thickness(0, 14, 0, 0);
            var cancel = new Button();
            cancel.Style = (Style)FindResource("Ghost");
            cancel.Content = "取消";
            cancel.Click += delegate { DialogResult = false; };
            foot.Children.Add(cancel);
            _ok.Style = (Style)FindResource("Primary");
            _ok.Content = "添加";
            _ok.IsEnabled = false;
            _ok.Margin = new Thickness(12, 0, 0, 0);
            _ok.Click += delegate
            {
                if (_pick == null) return;
                DialogResult = true;
            };
            foot.Children.Add(_ok);
            Grid.SetRow(foot, 3);
            body.Children.Add(foot);

            Grid.SetRow(body, 1);
            root.Children.Add(body);
            Content = root;

            SyncTabs();
            Fill();
        }

        private static string ClassTabName(MonsterClass c)
        {
            switch (c)
            {
                case MonsterClass.Minion: return "普通小怪";
                case MonsterClass.Elite: return "精英怪";
                case MonsterClass.Boss: return "Boss 怪";
                default: return "未分类";
            }
        }

        private void SyncTabs()
        {
            foreach (Button b in _tabs)
            {
                var c = (MonsterClass)b.Tag;
                bool on = c == _tab;
                b.Style = (Style)FindResource(on ? "Primary" : "Ghost");
            }
        }

        private void Fill()
        {
            _grid.Children.Clear();
            string q = (_search.Text ?? "").Trim();
            int total = 0, hit = 0;
            IEnumerable<MonsterRow> source = _fromPool != null
                ? (IEnumerable<MonsterRow>)_fromPool() : GameTables.MonstersWithAvatar();
            foreach (MonsterRow m in source)
            {
                if (_fromPool == null && m.Class != _tab) continue;
                total++;
                if (q.Length > 0
                    && m.Name.IndexOf(q, StringComparison.OrdinalIgnoreCase) < 0
                    && m.Id.ToString(System.Globalization.CultureInfo.InvariantCulture).IndexOf(q, StringComparison.Ordinal) < 0)
                    continue;
                hit++;
                _grid.Children.Add(Tile(m));
            }
            if (hit == 0)
            {
                var none = new TextBlock();
                none.Text = total == 0 ? "这里没有可挑的怪。" : "没有匹配「" + q + "」的怪。";
                none.FontSize = 12;
                none.Foreground = (Brush)FindResource("FgFaint");
                none.Margin = new Thickness(4, 10, 0, 0);
                _grid.Children.Add(none);
            }
        }

        private FrameworkElement Tile(MonsterRow m)
        {
            var item = new TileItem();
            item.ImageName = m.AvatarName;      // ★ 图鉴像素小人 → 半身像 → 手牌画（见 MonsterRow.AvatarName）
            // ★ 像素小人（200×200 小图）**不裁**且要最近邻放大；半身像那档才需要"内容居中裁方图"。
            item.IsPixelArt = m.AvatarIsPixel;
            item.CenterSquare = !m.AvatarIsPixel;
            // ★ 2026-10-10 用户："怪物底部怎么有个黑底…包括在怪物列表那边也是不需要黑底"。
            //   像素小人是**带透明通道的不规则形状**（四角 alpha=0、最外一圈 alpha 均值 0.0），
            //   所以这里**不给色块底**：`Tiles` 只在"没有图"时才拿 `Block` 当兜底，
            //   给了深色就变成一块黑方块压在透明图后面。
            item.Block = null;
            item.Title = m.Name;
            item.Sub = m.Id.ToString(System.Globalization.CultureInfo.InvariantCulture);
            item.Tooltip = m.Name + "（表 " + m.Id + "）\n" + m.ClassName
                + "\n血 " + m.Blood + " · 防 " + m.Defense + " · 攻 " + m.Attack + " · 币 " + m.Gold
                + "\n图：" + MonsterArt.SourceNote(m);
            item.Tag = m;
            item.Marked = ReferenceEquals(_pick, m)
                || (_pick != null && _pick.Id == m.Id);
            return Tiles.Make(this, item, delegate (TileItem t)
            {
                _pick = (MonsterRow)t.Tag;
                _ok.IsEnabled = true;
                Fill();
            });
        }
    }
}
