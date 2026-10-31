using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace DiyEditor
{
    /// <summary>
    /// ★★ 「外观」设置窗口 —— 用户这轮的原话就是「旧项目的背景图和玻璃磨砂设置」。
    ///
    /// 照 `D:\小小兔趴\1-首次安装包\启动器\`（**只读参考，原件一个字节都没动**）
    /// 的「设置 → 外观」那一栏搬过来的四项：
    ///   ① 背景图  —— 6 个预设（有真图用真图，没有就现画渐变）+ 「挑一张本机图片…」+ 「清空」；
    ///   ② 玻璃磨砂 —— 0..60，**只糊每一块玻璃面板，背景图本身保持清晰**；
    ///   ③ 背景压暗 —— 0..0.85，盖在背景图上、不糊；
    ///   ④ 主题色  —— #RRGGBB（背景渐变与强调色都吃它）。
    ///
    /// ★ **改一项立刻生效**：调 `Look.NotifyChanged()` 广播，主窗口的 `ApplyLook()`
    ///   当场重画背景 + 重设 `GlassSurface.GlobalFrost`。所以这个窗口自己就能当预览。
    /// </summary>
    public sealed class LookDialog : Window
    {
        private readonly WrapPanel _presets = new WrapPanel();
        private readonly List<Button> _presetButtons = new List<Button>();
        private readonly Slider _frost = new Slider();
        private readonly Slider _dim = new Slider();
        private readonly TextBox _hex = new TextBox();
        private readonly TextBlock _frostVal = new TextBlock();
        private readonly TextBlock _dimVal = new TextBlock();
        private readonly TextBlock _bgNow = new TextBlock();
        private readonly Border _swatchNow = new Border();

        /// <summary>用户到底改过没有（主窗口按它决定要不要落盘 + 报状态）。</summary>
        public bool Dirty { get { return Look.Dirty; } }

        /// <summary>
        /// 「导出 / 说明」页那个按钮走这儿：从任意宿主元素开出来，Owner 自动认它所在的窗口。
        /// 关掉之后把新的外观落盘（`编辑器配置.json` 的 `look` 块）。
        /// </summary>
        public static void OpenFrom(FrameworkElement host)
        {
            var dlg = new LookDialog();
            Window owner = host == null ? null : Window.GetWindow(host);
            if (owner != null) dlg.Owner = owner;
            dlg.ShowDialog();
            if (dlg.Dirty) MainWindow.SaveRecent();
        }

        public LookDialog()
        {
            Title = "外观";
            Width = 720;
            Height = 620;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = new SolidColorBrush(Color.FromArgb(255, 10, 13, 22));
            Foreground = (Brush)FindResource("Fg");
            FontFamily = (FontFamily)FindResource("AppFont");
            FontSize = 13;

            var root = new Grid();
            DialogSkin.Paint(root);
            root.Width = Width;
            root.Height = Height;
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(42) });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            root.Children.Add(BuildTitleBar());

            var scroll = new ScrollViewer();
            scroll.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            scroll.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
            Grid.SetRow(scroll, 1);
            var body = new StackPanel();
            body.Margin = new Thickness(20, 8, 20, 8);
            body.Children.Add(PresetSection());
            body.Children.Add(FrostSection());
            body.Children.Add(DimSection());
            body.Children.Add(AccentSection());
            body.Children.Add(NoteSection());
            scroll.Content = body;
            root.Children.Add(scroll);

            root.Children.Add(BuildFooter());

            Content = root;
            SyncFromLook();
        }

        // ============================================================ 标题条
        private UIElement BuildTitleBar()
        {
            var bar = new Border();
            bar.Background = new SolidColorBrush(Color.FromArgb(60, 255, 255, 255));
            bar.MouseLeftButtonDown += delegate (object s, MouseButtonEventArgs e)
            {
                if (e.ClickCount == 1) { try { DragMove(); } catch (Exception) { } }
            };
            var g = new Grid();
            var t = new TextBlock();
            t.Text = "外观 · 背景图 / 玻璃磨砂";
            t.FontSize = 13.5;
            t.VerticalAlignment = VerticalAlignment.Center;
            t.Margin = new Thickness(18, 0, 0, 0);
            g.Children.Add(t);

            var close = new Button();
            close.Content = "\uE8BB";
            close.FontFamily = new FontFamily("Segoe Fluent Icons,Segoe MDL2 Assets");
            close.Width = 46;
            close.Height = 42;
            close.HorizontalAlignment = HorizontalAlignment.Right;
            close.Background = Brushes.Transparent;
            close.Foreground = (Brush)FindResource("Fg");
            close.BorderThickness = new Thickness(0);
            close.Cursor = Cursors.Hand;
            close.ToolTip = "关掉";
            close.Click += delegate { Close(); };
            g.Children.Add(close);

            bar.Child = g;
            return bar;
        }

        // ============================================================ ① 背景图
        private UIElement PresetSection()
        {
            var card = Card("① 背景图");
            var panel = (StackPanel)card.Child;

            var row = new StackPanel { Orientation = Orientation.Horizontal };
            _swatchNow.Width = 92;
            _swatchNow.Height = 54;
            _swatchNow.CornerRadius = new CornerRadius(10);
            _swatchNow.BorderThickness = new Thickness(1);
            _swatchNow.BorderBrush = new SolidColorBrush(Color.FromArgb(60, 255, 255, 255));
            row.Children.Add(_swatchNow);

            var col = new StackPanel { Margin = new Thickness(14, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            _bgNow.FontSize = 12.5;
            col.Children.Add(_bgNow);
            var hint = new TextBlock();
            hint.Text = "预设放在 exe 旁边的 背景图\\ 里就用真图；没放就按同色系现画。";
            hint.FontSize = 11.5;
            hint.TextWrapping = TextWrapping.Wrap;
            hint.MaxWidth = 460;
            hint.Opacity = 0.66;
            hint.Margin = new Thickness(0, 5, 0, 0);
            col.Children.Add(hint);
            row.Children.Add(col);
            panel.Children.Add(row);

            _presets.Margin = new Thickness(0, 14, 0, 0);
            panel.Children.Add(_presets);
            foreach (Look.Preset p in Look.Presets()) _presets.Children.Add(PresetButton(p));

            var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 0) };
            actions.Children.Add(Action("挑一张本机图片…", OnBrowse));
            actions.Children.Add(Action("清空（用主题色渐变）", OnClear));
            panel.Children.Add(actions);
            return card;
        }

        private Button PresetButton(Look.Preset p)
        {
            var b = new Button();
            b.Width = 96;
            b.Height = 62;
            b.Margin = new Thickness(0, 0, 10, 10);
            b.Cursor = Cursors.Hand;
            b.Tag = p;
            b.ToolTip = p.Title + "（" + p.Tag + "）"
                + (p.File.Length > 0 ? "\n用 背景图\\ 里的真图" : "\n没放真图 ⇒ 现画同色系渐变");
            b.BorderThickness = new Thickness(1);
            b.BorderBrush = new SolidColorBrush(Color.FromArgb(70, 255, 255, 255));

            var wrap = new Grid();
            // 缩略图：有真图就取真图中间一块，没有就画渐变（照启动器 PresetThumb）
            var thumb = new Border { CornerRadius = new CornerRadius(0) };
            thumb.Background = PresetThumb(p);
            wrap.Children.Add(thumb);
            var label = new TextBlock();
            label.Text = p.Title;
            label.FontSize = 11;
            label.Foreground = Brushes.White;
            label.HorizontalAlignment = HorizontalAlignment.Center;
            label.VerticalAlignment = VerticalAlignment.Bottom;
            label.Margin = new Thickness(2, 0, 2, 4);
            wrap.Children.Add(label);
            b.Content = wrap;

            b.Click += delegate
            {
                Look.Background = p.Tag;
                Look.NotifyChanged();
                SyncFromLook();
            };
            _presetButtons.Add(b);
            return b;
        }

        /// <summary>预设色块的缩略图：有真图就用真图的中间一小块，没有就画渐变。</summary>
        private static Brush PresetThumb(Look.Preset p)
        {
            if (p.File.Length > 0)
            {
                try
                {
                    var img = new System.Windows.Media.Imaging.BitmapImage();
                    img.BeginInit();
                    img.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                    img.UriSource = new Uri(p.File, UriKind.Absolute);
                    img.EndInit();
                    img.Freeze();
                    var brush = new ImageBrush(img);
                    brush.Stretch = Stretch.UniformToFill;
                    return brush;
                }
                catch (Exception) { }
            }
            var g = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 1) };
            g.GradientStops.Add(new GradientStop(p.A, 0));
            g.GradientStops.Add(new GradientStop(p.B, 1));
            return g;
        }

        private void OnBrowse(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title = "挑一张背景图",
                Filter = "图片|*.jpg;*.jpeg;*.png;*.bmp;*.gif|所有文件|*.*",
                CheckFileExists = true,
            };
            if (dlg.ShowDialog(this) != true) return;
            Look.Background = dlg.FileName;
            Look.NotifyChanged();
            SyncFromLook();
        }

        private void OnClear(object sender, RoutedEventArgs e)
        {
            Look.Background = "";
            Look.NotifyChanged();
            SyncFromLook();
        }

        // ============================================================ ② 磨砂 / ③ 压暗
        private UIElement FrostSection()
        {
            var card = Card("② 玻璃磨砂半径");
            var panel = (StackPanel)card.Child;

            _frost.Minimum = 0;
            _frost.Maximum = Look.FrostMax;
            _frost.TickFrequency = 5;
            _frost.IsSnapToTickEnabled = false;
            _frost.ValueChanged += delegate
            {
                Look.Frost = Look.Clamp(_frost.Value, 0, Look.FrostMax);
                _frostVal.Text = ((int)Math.Round(Look.Frost)).ToString();
                Look.NotifyChanged();
            };
            panel.Children.Add(SliderRow(_frost, _frostVal, "越大越糊；只糊面板，背景图本身保持清晰。"));

            var quick = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
            foreach (double v in new double[] { 0, 14, 28, 44, 60 })
            {
                double val = v;
                quick.Children.Add(Action(val.ToString(), delegate
                {
                    _frost.Value = val;   // ValueChanged 会负责 NotifyChanged + 刷新读数
                }));
            }
            quick.Children.Add(Action("恢复默认 28", delegate { _frost.Value = Look.FrostDefault; }));
            panel.Children.Add(quick);
            return card;
        }

        private UIElement DimSection()
        {
            var card = Card("③ 背景压暗");
            var panel = (StackPanel)card.Child;

            _dim.Minimum = 0;
            _dim.Maximum = Look.DimMax;
            _dim.ValueChanged += delegate
            {
                Look.Dim = Look.Clamp(_dim.Value, 0, Look.DimMax);
                _dimVal.Text = Look.Dim.ToString("0.00");
                Look.NotifyChanged();
            };
            panel.Children.Add(SliderRow(_dim, _dimVal, "盖在背景图上的一层暗色，不糊 —— 图太亮时用它压。"));
            return card;
        }

        private UIElement SliderRow(Slider s, TextBlock val, string hint)
        {
            var wrap = new StackPanel();
            var line = new Grid();
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            line.Children.Add(s);
            val.FontFamily = new FontFamily("Consolas");
            val.FontSize = 13;
            val.VerticalAlignment = VerticalAlignment.Center;
            val.MinWidth = 46;
            val.TextAlignment = TextAlignment.Right;
            Grid.SetColumn(val, 1);
            line.Children.Add(val);
            wrap.Children.Add(line);

            var t = new TextBlock();
            t.Text = hint;
            t.FontSize = 11.5;
            t.Opacity = 0.66;
            t.TextWrapping = TextWrapping.Wrap;
            t.Margin = new Thickness(0, 5, 0, 0);
            wrap.Children.Add(t);
            return wrap;
        }

        // ============================================================ ④ 主题色
        private UIElement AccentSection()
        {
            var card = Card("④ 主题色");
            var panel = (StackPanel)card.Child;

            var row = new StackPanel { Orientation = Orientation.Horizontal };
            _hex.Width = 130;
            _hex.FontFamily = new FontFamily("Consolas");
            _hex.Padding = new Thickness(8, 5, 8, 5);
            row.Children.Add(_hex);
            row.Children.Add(Action("用这个色", OnApplyHex));
            row.Children.Add(Action("恢复默认 " + Look.AccentDefault, delegate
            {
                _hex.Text = Look.AccentDefault;
                OnApplyHex(null, null);
            }));
            panel.Children.Add(row);

            var t = new TextBlock();
            t.Text = "写成 #RRGGBB。背景渐变与强调色都吃它。";
            t.FontSize = 11.5;
            t.Opacity = 0.66;
            t.Margin = new Thickness(0, 8, 0, 0);
            panel.Children.Add(t);
            return card;
        }

        private void OnApplyHex(object sender, RoutedEventArgs e)
        {
            string text = (_hex.Text ?? "").Trim();
            if (text.Length > 0 && text[0] != '#') text = "#" + text;
            if (!Look.IsHexColor(text))
            {
                MessageBox.Show("主题色要写成 #RRGGBB（例：#8FD8FF）。", "外观",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            Look.Accent = text;
            Look.NotifyChanged();
            SyncFromLook();
        }

        // ============================================================ 说明 + 底栏
        private UIElement NoteSection()
        {
            var card = Card("说明");
            var panel = (StackPanel)card.Child;
            var t = new TextBlock();
            t.Text = "外观只写编辑器自己的 编辑器配置.json（exe 旁边那一份），"
                + "不碰游戏目录、不碰服务端。磨砂是「把背后那块背景截一块、高斯模糊之后当面板的底」，"
                + "所以背景图本身始终是清晰的。";
            t.FontSize = 11.5;
            t.TextWrapping = TextWrapping.Wrap;
            t.Opacity = 0.7;
            panel.Children.Add(t);
            return card;
        }

        private UIElement BuildFooter()
        {
            var bar = new Border();
            bar.Background = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255));
            bar.Padding = new Thickness(20, 12, 20, 12);
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var note = new TextBlock();
            note.Text = "改一项立刻生效（这个窗口背后就是主窗口）。";
            note.FontSize = 11.5;
            note.Opacity = 0.66;
            note.VerticalAlignment = VerticalAlignment.Center;
            g.Children.Add(note);

            var ok = new Button();
            ok.Content = "完成";
            ok.Padding = new Thickness(22, 8, 22, 8);
            ok.Cursor = Cursors.Hand;
            ok.Background = new SolidColorBrush(Color.FromArgb(60, 143, 216, 255));
            ok.Foreground = Brushes.White;
            ok.BorderThickness = new Thickness(0);
            ok.Click += delegate { Close(); };
            Grid.SetColumn(ok, 1);
            g.Children.Add(ok);

            bar.Child = g;
            Grid.SetRow(bar, 2);
            return bar;
        }

        // ============================================================ 小零件
        private Border Card(string title)
        {
            var outer = new Border();
            outer.CornerRadius = new CornerRadius(16);
            outer.BorderThickness = new Thickness(1);
            outer.BorderBrush = new SolidColorBrush(Color.FromArgb(56, 255, 255, 255));
            outer.Background = new SolidColorBrush(Color.FromArgb(26, 255, 255, 255));
            outer.Padding = new Thickness(16);
            outer.Margin = new Thickness(0, 0, 0, 14);

            var stack = new StackPanel();
            var h = new TextBlock();
            h.Text = title;
            h.FontSize = 14;
            h.FontWeight = FontWeights.SemiBold;
            h.Margin = new Thickness(0, 0, 0, 12);
            stack.Children.Add(h);
            outer.Child = stack;
            return outer;
        }

        private Button Action(string text, RoutedEventHandler click)
        {
            var b = new Button();
            b.Content = text;
            b.Padding = new Thickness(14, 7, 14, 7);
            b.Margin = new Thickness(0, 0, 10, 0);
            b.Cursor = Cursors.Hand;
            b.Background = new SolidColorBrush(Color.FromArgb(36, 255, 255, 255));
            b.Foreground = (Brush)FindResource("Fg");
            b.BorderThickness = new Thickness(0);
            b.Click += click;
            return b;
        }

        /// <summary>把界面拉回当前 `Look.*`（读盘 / 点完预设之后都要同步一次）。</summary>
        private void SyncFromLook()
        {
            _frost.Value = Look.Clamp(Look.Frost, 0, Look.FrostMax);
            _dim.Value = Look.Clamp(Look.Dim, 0, Look.DimMax);
            _frostVal.Text = ((int)Math.Round(Look.Frost)).ToString();
            _dimVal.Text = Look.Dim.ToString("0.00");
            _hex.Text = Look.Accent;

            _bgNow.Text = "现在：背景 = " + Look.DescribeBackground();

            // ★ 选中的那个预设描一圈强调色（不然"点了哪个"看不出来）
            foreach (Button b in _presetButtons)
            {
                var p = b.Tag as Look.Preset;
                bool on = p != null && string.Equals(p.Tag, Look.Background, StringComparison.OrdinalIgnoreCase);
                b.BorderThickness = new Thickness(on ? 2 : 1);
                b.BorderBrush = on
                    ? new SolidColorBrush(Look.AccentColor)
                    : new SolidColorBrush(Color.FromArgb(70, 255, 255, 255));
            }

            var g = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 1) };
            Color a = Look.AccentColor;
            Color dark = Color.FromRgb(0x07, 0x09, 0x11);
            g.GradientStops.Add(new GradientStop(Look.Mix(dark, a, 0.22), 0));
            g.GradientStops.Add(new GradientStop(Look.Mix(dark, Look.WarmOf(a), 0.12), 1));
            _swatchNow.Background = g;
        }
    }
}
