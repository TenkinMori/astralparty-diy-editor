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
    /// ★★ 「挑官方事件 · 这条进度事件什么时候触发」二级窗口（第 11 波）。
    ///
    /// 为什么要有它：触发进度**只能靠事件 id 编码**（MapEvent.bin 的 `field4 ÷ 2`，
    /// 见 `DiyTrigger` 顶上的查证）。所以用户想说"进度 3 触发"，编辑器只能
    /// **反查**"官方哪些事件的 field4÷2 == 3"，把这几条**列出来给他挑**
    /// （用户原话："并把该进度下可用的官方事件列出来给他挑"）。
    ///
    /// 清单 = `MapEvent.bin` 全表（141 条），**按触发进度升序**（同一进度里按 id 升序）；
    /// 上面那个框可以只看某一档进度（"进度 3 下有哪几条、名字是什么"）；
    /// 每条后面挂官方效果摘要，挑的时候心里有数。
    /// </summary>
    public sealed class TriggerPickDialog : Window
    {
        private readonly TextBox _progress = new TextBox();
        private readonly TextBox _search = new TextBox();
        private readonly StackPanel _list = new StackPanel();
        private readonly TextBlock _hint = new TextBlock();
        private readonly TextBlock _pickText = new TextBlock();
        private readonly Button _ok = new Button();

        private MapEventRow _pick;

        /// <summary>挑中的那条官方事件（没挑就是 null）。</summary>
        public MapEventRow Picked { get { return _pick; } }

        /// <param name="progress">当前这条事件的触发进度（-1 = 还没设）—— 用来预筛。</param>
        /// <param name="eventId">当前这条事件用的官方事件 id（-1 = 还没挑）—— 用来预选。</param>
        public TriggerPickDialog(int progress, int eventId)
        {
            Title = "挑官方事件 · 解锁触发进度";
            Width = 1020;
            Height = 720;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = new SolidColorBrush(Color.FromArgb(255, 10, 13, 22));
            Foreground = (Brush)FindResource("Fg");
            FontFamily = (FontFamily)FindResource("AppFont");
            FontSize = 13;

            if (eventId > 0) _pick = MapEventTable.Get(eventId);
            if (progress >= 0) _progress.Text = progress.ToString(CultureInfo.InvariantCulture);

            var root = new Grid();
            DialogSkin.Paint(root);
            root.Width = Width;
            root.Height = Height;
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(42) });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            // ---- 标题条
            var bar = new Border();
            bar.Background = new SolidColorBrush(Color.FromArgb(60, 255, 255, 255));
            bar.MouseLeftButtonDown += delegate (object s, MouseButtonEventArgs e)
            {
                if (e.ClickCount == 1) { try { DragMove(); } catch (Exception) { } }
            };
            var barGrid = new Grid();
            var barText = new TextBlock();
            barText.Text = "挑官方事件 · 决定这条进度事件什么时候触发";
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

            // ---- 主体
            var body = new Grid();
            body.Margin = new Thickness(18, 14, 18, 14);
            body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            body.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            _hint.Style = (Style)FindResource("Note");
            _hint.TextWrapping = TextWrapping.Wrap;
            _hint.MaxWidth = 940;
            Grid.SetRow(_hint, 0);
            body.Children.Add(_hint);

            var head = new DockPanel();
            head.Margin = new Thickness(0, 12, 0, 10);

            var left = new StackPanel();
            left.Orientation = Orientation.Horizontal;
            left.HorizontalAlignment = HorizontalAlignment.Left;
            left.Children.Add(Label("触发进度"));
            _progress.Style = (Style)FindResource("Field");
            _progress.Width = 90;
            _progress.Margin = new Thickness(8, 0, 0, 0);
            _progress.MaxLength = 6;
            _progress.ToolTip = "只看某一档触发进度（留空 = 全部）。★ 填一个官方没有的值 ⇒ 清单会是空的。";
            _progress.TextChanged += delegate { Fill(); };
            left.Children.Add(_progress);
            left.Children.Add(Label("回合", 8));
            DockPanel.SetDock(left, Dock.Left);
            head.Children.Add(left);

            var right = new StackPanel();
            right.Orientation = Orientation.Horizontal;
            right.HorizontalAlignment = HorizontalAlignment.Right;
            right.Children.Add(Label("搜索"));
            _search.Style = (Style)FindResource("Field");
            _search.Width = 220;
            _search.Margin = new Thickness(8, 0, 0, 0);
            _search.ToolTip = "按事件 id 或名字筛（留空 = 全部）";
            _search.TextChanged += delegate { Fill(); };
            right.Children.Add(_search);
            head.Children.Add(right);

            Grid.SetRow(head, 1);
            body.Children.Add(head);

            var scroll = new ScrollViewer();
            scroll.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            scroll.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
            scroll.Focusable = false;
            scroll.Content = _list;
            Grid.SetRow(scroll, 2);
            body.Children.Add(scroll);

            var foot = new Grid();
            foot.Margin = new Thickness(0, 14, 0, 0);
            _pickText.Style = (Style)FindResource("FieldHint");
            _pickText.VerticalAlignment = VerticalAlignment.Center;
            _pickText.TextWrapping = TextWrapping.Wrap;
            _pickText.MaxWidth = 700;
            _pickText.HorizontalAlignment = HorizontalAlignment.Left;
            foot.Children.Add(_pickText);

            var btns = new StackPanel();
            btns.Orientation = Orientation.Horizontal;
            btns.HorizontalAlignment = HorizontalAlignment.Right;
            var cancel = new Button();
            cancel.Style = (Style)FindResource("Ghost");
            cancel.Content = "取消";
            cancel.Click += delegate { DialogResult = false; };
            btns.Children.Add(cancel);
            _ok.Style = (Style)FindResource("Primary");
            _ok.Content = "就用这条";
            _ok.Margin = new Thickness(12, 0, 0, 0);
            _ok.Click += delegate
            {
                if (_pick == null) return;
                DialogResult = true;
            };
            btns.Children.Add(_ok);
            foot.Children.Add(btns);

            Grid.SetRow(foot, 3);
            body.Children.Add(foot);

            Grid.SetRow(body, 1);
            root.Children.Add(body);
            Content = root;

            Fill();
        }

        private TextBlock Label(string text, double left = 0)
        {
            var t = new TextBlock();
            t.Text = text;
            t.Style = (Style)FindResource("FieldLabel");
            t.VerticalAlignment = VerticalAlignment.Center;
            t.Margin = new Thickness(left, 0, 0, 0);
            return t;
        }

        /// <summary>现在这一份筛选结果（进度 + 搜索词都生效之后），★ 按触发进度升序。</summary>
        private List<MapEventRow> Filtered()
        {
            int want = -1;
            string p = (_progress.Text ?? "").Trim();
            if (p.Length > 0)
            {
                int v;
                if (int.TryParse(p, NumberStyles.Integer, CultureInfo.InvariantCulture, out v) && v >= 0) want = v;
            }
            string q = (_search.Text ?? "").Trim();

            var list = new List<MapEventRow>();
            foreach (MapEventRow r in MapEventTable.Rows())
            {
                if (want >= 0 && (!r.HasTrigger || r.Progress != want)) continue;
                if (q.Length > 0
                    && r.Name.IndexOf(q, StringComparison.OrdinalIgnoreCase) < 0
                    && r.Id.ToString(CultureInfo.InvariantCulture).IndexOf(q, StringComparison.Ordinal) < 0)
                    continue;
                list.Add(r);
            }
            list.Sort(delegate (MapEventRow a, MapEventRow b)
            {
                int pa = a.HasTrigger ? a.Progress : int.MaxValue;
                int pb = b.HasTrigger ? b.Progress : int.MaxValue;
                if (pa != pb) return pa.CompareTo(pb);
                return a.Id.CompareTo(b.Id);
            });
            return list;
        }

        private void Fill()
        {
            _list.Children.Clear();
            List<MapEventRow> list = Filtered();
            List<int> values = MapEventTable.ProgressValues();

            _hint.Text = "★ 触发进度只能靠「事件 id」编码：MapEvent.bin 一行的 `field4 ÷ 2` 就是它的触发进度"
                + "（gamedata.map_event_progress 的口径，服务端 battle.progress_events_due 照这个播）。"
                + "所以「进度 3 触发」= 挑一条 field4÷2 == 3 的官方事件。"
                + "　表里一共 " + MapEventTable.Rows().Count + " 条事件，官方给过的触发进度一共 "
                + values.Count + " 档："
                + (values.Count > 0 ? values[0] + " ~ " + values[values.Count - 1] : "(无)") + "。";

            int shown = 0;
            foreach (MapEventRow r in list)
            {
                shown++;
                MapEventRow cap = r;
                var b = new Button();
                bool on = _pick != null && _pick.Id == r.Id;
                b.Style = (Style)FindResource(on ? "Primary" : "Ghost");
                b.HorizontalAlignment = HorizontalAlignment.Stretch;
                b.HorizontalContentAlignment = HorizontalAlignment.Left;
                b.Margin = new Thickness(0, 0, 0, 6);
                b.Padding = new Thickness(12, 8, 12, 8);
                b.Cursor = Cursors.Hand;

                var g = new Grid();
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(96) });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(300) });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                var p1 = new TextBlock();
                p1.Text = r.HasTrigger ? ("进度 " + r.Progress) : "没写触发点";
                p1.FontSize = 12.5;
                p1.FontWeight = FontWeights.SemiBold;
                p1.VerticalAlignment = VerticalAlignment.Center;
                Grid.SetColumn(p1, 0);
                g.Children.Add(p1);

                var p2 = new TextBlock();
                p2.Text = r.Id + " " + (r.Name.Length > 0 ? r.Name : "(官方没给名字)")
                    + "（field4=" + (r.TriggerX2.HasValue ? r.TriggerX2.Value.ToString(CultureInfo.InvariantCulture) : "?") + "）";
                p2.FontSize = 12;
                p2.VerticalAlignment = VerticalAlignment.Center;
                Grid.SetColumn(p2, 1);
                g.Children.Add(p2);

                var p3 = new TextBlock();
                p3.Text = MapEventTable.EffectSummary(r);
                p3.FontSize = 11.5;
                p3.Foreground = UiBits.Br(this, "FgFaint");
                p3.TextWrapping = TextWrapping.Wrap;
                p3.VerticalAlignment = VerticalAlignment.Center;
                Grid.SetColumn(p3, 2);
                g.Children.Add(p3);

                b.Content = g;
                b.Click += delegate
                {
                    _pick = cap;
                    Fill();
                };
                _list.Children.Add(b);
            }

            if (shown == 0)
            {
                var none = new TextBlock();
                none.Text = "这一档一个官方事件都没有"
                    + (values.Count > 0 ? "（官方给过的触发进度：" + JoinValues(values) + "）" : "")
                    + " —— 换个进度值，或者把「触发进度」框清空看全部。";
                none.FontSize = 12;
                none.Foreground = UiBits.Br(this, "BadBrush");
                none.TextWrapping = TextWrapping.Wrap;
                none.Margin = new Thickness(4, 10, 0, 0);
                _list.Children.Add(none);
            }

            _pickText.Text = _pick == null
                ? "还没挑。点一条，触发进度就跟着它定了（列出来 " + shown + " 条）。"
                : "挑中：" + MapEventTable.CandidateText(_pick) + " · 效果：" + MapEventTable.EffectSummary(_pick)
                  + "（列出来 " + shown + " 条）";
            _ok.IsEnabled = _pick != null;
        }

        private static string JoinValues(List<int> xs)
        {
            var a = new string[xs.Count];
            for (int k = 0; k < xs.Count; k++) a[k] = xs[k].ToString(CultureInfo.InvariantCulture);
            return string.Join(" / ", a);
        }
    }
}
