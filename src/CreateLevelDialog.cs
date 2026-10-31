using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace DiyEditor
{
    /// <summary>
    /// ★★ 「新建关卡」的**二级菜单窗口** —— 用户 2026-10-03 的规格：
    ///
    ///   &gt; 点击新建按钮后出现一个二级菜单窗口
    ///   &gt; 关卡名字
    ///   &gt; 不需要设置难度（未来客户端那边默认就是 DIY 难度）
    ///   &gt; 地图选择，列出所有的 PVE 图，**不要 PVP 和练习图**，把**词条 PVE** 也加里面
    ///
    /// ⇒ 这一页上**只有两样东西**：关卡名字 + 地图选择。**没有难度输入框**
    ///   （写进文件的难度号恒为 `LevelDoc.DiyDifficulty` = 1001，界面上一个字都不提）。
    /// </summary>
    public sealed class CreateLevelDialog : Window
    {
        private readonly TextBox _name = new TextBox();
        private readonly WrapPanel _maps = new WrapPanel();
        private readonly TextBlock _hint = new TextBlock();
        private readonly Button _ok = new Button();

        private LevelTemplate _pick;

        /// <summary>用户点了「新建」之后的结果（null = 取消了）。</summary>
        public LevelTemplate PickedTemplate { get { return _pick; } }
        public string PickedName { get { return LevelDoc.SanitizeName(_name.Text ?? ""); } }

        /// <summary>
        /// ★ 只给离屏截图用（`--win create`）：**选中第一份模板**并把名字填上，
        ///   这样那张图里能同时看见"名字 + 选中的模板 + 新建按钮"。
        ///   真人用是手点（自己不调它）。
        /// </summary>
        public void ShotPickFirstUsable()
        {
            foreach (UIElement child in _maps.Children)
            {
                var b = child as Button;
                if (b == null || !b.IsEnabled) continue;
                Pick((LevelTemplate)b.Tag);
                break;
            }
        }

        public CreateLevelDialog(string suggestName)
        {
            Title = "新建 DIY 关卡";
            Width = 960;
            Height = 700;
            WindowStyle = WindowStyle.None;
            AllowsTransparency = false;
            ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = new SolidColorBrush(Color.FromArgb(255, 10, 13, 22));
            Foreground = (Brush)FindResource("Fg");
            FontFamily = (FontFamily)FindResource("AppFont");
            FontSize = 13;

            var root = new Grid();
            DialogSkin.Paint(root);       // 深底 + 白字（对话框是独立 Window，没有继承来的 Foreground）
            root.Width = Width;
            root.Height = Height;
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(42) });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            // ── 标题条（把窗口拖着走 / 关掉）
            var bar = new Border();
            bar.Background = new SolidColorBrush(Color.FromArgb(60, 255, 255, 255));
            bar.MouseLeftButtonDown += delegate (object s, MouseButtonEventArgs e)
            {
                if (e.ClickCount == 1) { try { DragMove(); } catch (Exception) { } }
            };
            var barGrid = new Grid();
            var barText = new TextBlock();
            barText.Text = "新建 DIY 关卡";
            barText.FontSize = 13.5;
            barText.Foreground = (Brush)FindResource("Fg");
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

            // ── 正文
            var body = new Grid();
            body.Margin = new Thickness(18, 14, 18, 14);
            body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            body.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // ① 关卡名字
            var nameRow = new Grid();
            nameRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(84) });
            nameRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var nameLabel = new TextBlock();
            nameLabel.Text = "关卡名字";
            nameLabel.Style = (Style)FindResource("FieldLabel");
            nameLabel.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(nameLabel, 0);
            nameRow.Children.Add(nameLabel);
            _name.Style = (Style)FindResource("Field");
            _name.MaxLength = 40;
            _name.Text = suggestName ?? "";
            _name.ToolTip = "存成 <游戏根>\\DIY关卡\\<名字>.json。重名会被挡下来。";
            Grid.SetColumn(_name, 1);
            nameRow.Children.Add(_name);
            Grid.SetRow(nameRow, 0);
            body.Children.Add(nameRow);

            // ② 地图选择
            var mapBox = new Border();
            mapBox.Margin = new Thickness(0, 14, 0, 0);
            mapBox.CornerRadius = new CornerRadius(14);
            mapBox.Background = new SolidColorBrush(Color.FromArgb(26, 255, 255, 255));
            mapBox.BorderThickness = new Thickness(1);
            mapBox.BorderBrush = new SolidColorBrush(Color.FromArgb(38, 255, 255, 255));
            mapBox.Padding = new Thickness(14, 12, 10, 12);
            var mapStack = new DockPanel();
            var mapHead = new TextBlock();
            mapHead.Text = "地图选择";
            mapHead.Style = (Style)FindResource("H1");
            DockPanel.SetDock(mapHead, Dock.Top);
            mapStack.Children.Add(mapHead);
            _hint.Style = (Style)FindResource("Note");
            _hint.Margin = new Thickness(0, 6, 0, 8);
            _hint.MaxWidth = 860;
            _hint.TextWrapping = TextWrapping.Wrap;
            DockPanel.SetDock(_hint, Dock.Top);
            mapStack.Children.Add(_hint);

            var scroll = new ScrollViewer();
            scroll.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            scroll.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
            scroll.Focusable = false;
            scroll.Content = _maps;
            mapStack.Children.Add(scroll);

            mapBox.Child = mapStack;
            Grid.SetRow(mapBox, 1);
            body.Children.Add(mapBox);

            // ③ 底下那一排
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
            _ok.Content = "新建";
            _ok.Margin = new Thickness(12, 0, 0, 0);
            _ok.Click += OnCreate;
            foot.Children.Add(_ok);
            Grid.SetRow(foot, 2);
            body.Children.Add(foot);

            Grid.SetRow(body, 1);
            root.Children.Add(body);
            Content = root;

            BuildTemplates();
        }

        /// <summary>
        /// ★ 2026-10-08 大改：清单 = **我们提供的成品模板**
        ///   （`&lt;游戏根&gt;\DIY关卡\_模板\*.json`），不再是"官方的 PvE 地图列表"。
        ///   模板里已经写着完整的关卡内容（事件 / 刷怪 / 奖励 / 胜负 / 结算），
        ///   新建 = **整份复制模板**，然后在上面改。
        /// </summary>
        private void BuildTemplates()
        {
            int seeded = TemplateCatalog.EnsureSeeded();      // 目录空 ⇒ 拷一份官方关卡当种子
            int blanks = TemplateCatalog.EnsureBlankTemplates();  // ★ 有官方棋盘但没模板的图 ⇒ 补空白玩法模板
            int evs = TemplateCatalog.EnsureTemplateEvents();     // ★ 空白玩法模板 ⇒ 补**这张图官方登记的**事件骨架（只补空缺 / 升级上一版骨架）
            _maps.Children.Clear();
            List<LevelTemplate> list = TemplateCatalog.List();
            if (list.Count == 0)
            {
                _hint.Text = "还没有成品模板。模板目录："
                    + (TemplateCatalog.Dir.Length > 0 ? TemplateCatalog.Dir : "(先指定游戏目录)")
                    + "　——　把做好的关卡 json 放进这个目录，这里就会列出来。";
                _ok.IsEnabled = false;
                return;
            }
            foreach (LevelTemplate t in list)
            {
                MapTemplate map = t.MapId > 0 ? GameTables.Map(t.MapId) : null;
                if (map != null) GameTables.EnsureBoardInfo(map);
                _maps.Children.Add(MapCard(t, map));
            }
            _hint.Text = "成品模板 " + list.Count + " 份"
                + (seeded > 0 ? ("（刚从官方关卡拷了 " + seeded + " 份当种子）") : "")
                + (blanks > 0 ? ("（新补了 " + blanks + " 份「空白玩法 + 官方棋盘」模板）") : "")
                + (evs > 0 ? ("（补了 " + evs + " 份的开局事件骨架）") : "")
                + "　·　目录：" + TemplateCatalog.Dir
                + "　·　新建后直接进棋盘页。";
        }

        private FrameworkElement MapCard(LevelTemplate t, MapTemplate map)
        {
            var btn = new Button();
            btn.Width = 180;
            btn.Height = 172;
            btn.Margin = new Thickness(0, 0, 12, 12);
            btn.Cursor = Cursors.Hand;
            btn.Tag = t;
            btn.ToolTip = t.Title + "\n模板文件 " + t.FileName
                + (map != null ? ("\n地图 " + map.Id + "（" + map.Name + "）\n所属模式：" + ModeTextOf(map)) : "")
                + "\n" + t.Sub;

            var shell = new Border();
            shell.CornerRadius = new CornerRadius(16);
            shell.BorderThickness = new Thickness(1);
            shell.BorderBrush = new SolidColorBrush(Color.FromArgb(60, 255, 255, 255));
            shell.Background = new SolidColorBrush(Color.FromArgb(26, 255, 255, 255));
            shell.Padding = new Thickness(9);
            var stack = new StackPanel();

            var shot = new Border();
            shot.Height = 92;
            shot.CornerRadius = new CornerRadius(11);
            shot.Background = new SolidColorBrush(Color.FromArgb(70, 20, 28, 42));
            shot.ClipToBounds = true;
            // ★★ 缩略图取材顺序（用户这轮第 4 件事：部分地图预览图丢失）：
            //   ① 已有 `贴图缓存\map_<画板号>.png`；② 从编辑器手上的棋盘数据**现画一张**
            //   （并落盘缓存）；③ 退官方的 UT_MapPreview_* / UT_MapScene_*。
            //   —— 异变图书馆(82016, 画板 023) 官方压根没有 preview（只有 003..016+019/020），
            //      以前所以是「（没有缩略图）」；现在走 ② 就有了。
            ImageSource thumb = map == null ? null : MapThumb.Find(map);
            if (thumb != null)
            {
                var img = new Image();
                img.Source = thumb;
                img.Stretch = Stretch.UniformToFill;
                shot.Child = img;
            }
            else
            {
                var none = new TextBlock();
                none.Text = "（没有缩略图）";
                none.FontSize = 11;
                none.Foreground = (Brush)FindResource("FgFaint");
                none.HorizontalAlignment = HorizontalAlignment.Center;
                none.VerticalAlignment = VerticalAlignment.Center;
                shot.Child = none;
            }
            stack.Children.Add(shot);

            var name = new TextBlock();
            name.Text = t.Title;
            name.FontSize = 12.5;
            name.Foreground = (Brush)FindResource("Fg");
            name.TextTrimming = TextTrimming.CharacterEllipsis;
            name.Margin = new Thickness(2, 7, 2, 0);
            stack.Children.Add(name);

            var meta = new TextBlock();
            meta.Text = t.Sub;
            meta.FontSize = 10.5;
            meta.Foreground = (Brush)FindResource("FgFaint");
            meta.TextTrimming = TextTrimming.CharacterEllipsis;
            meta.Margin = new Thickness(2, 3, 2, 0);
            stack.Children.Add(meta);

            var mode = new TextBlock();
            mode.Text = t.FileName;
            mode.FontSize = 10.5;
            mode.Foreground = (Brush)FindResource("FgFaint");
            mode.TextTrimming = TextTrimming.CharacterEllipsis;
            mode.Margin = new Thickness(2, 3, 2, 0);
            stack.Children.Add(mode);

            shell.Child = stack;
            btn.Content = shell;
            btn.Click += delegate { Pick(t); };
            if (ReferenceEquals(t, _pick)) shell.BorderBrush = (Brush)FindResource("Accent");
            return btn;
        }

        private static string ModeTextOf(MapTemplate t)
        {
            var parts = new List<string>();
            foreach (int m in GameTables.ModesOfMap(t.Id))
                if (m == 4 || m == 12)
                    parts.Add(GameTables.ModeName(m));
            return parts.Count == 0 ? "(表里没挂模式)" : string.Join(" · ", parts.ToArray());
        }

        private void Pick(LevelTemplate t)
        {
            _pick = t;
            foreach (UIElement child in _maps.Children)
            {
                var b = child as Button;
                if (b == null) continue;
                var shell = b.Content as Border;
                if (shell == null) continue;
                bool on = ReferenceEquals(b.Tag, t);
                shell.BorderBrush = on ? (Brush)FindResource("Accent")
                                       : new SolidColorBrush(Color.FromArgb(60, 255, 255, 255));
                shell.BorderThickness = new Thickness(on ? 2.5 : 1);
            }
            _ok.IsEnabled = true;
        }

        private void OnCreate(object sender, RoutedEventArgs e)
        {
            string name = PickedName;
            if (name.Length == 0)
            {
                MessageBox.Show(this, "先给这个 DIY 关卡起个名字。", "小小兔趴 · DIY 关卡编辑器",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (_pick == null)
            {
                MessageBox.Show(this, "先选一份成品模板。", "小小兔趴 · DIY 关卡编辑器",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            DialogResult = true;
        }
    }
}
