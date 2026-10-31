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
    /// ★★ 「给所有人发一张牌」的**二级菜单窗口** —— 用户 2026-10-03 的规格：
    ///
    ///   &gt; 给所有人发一张牌（点击后二级菜单窗口，可分页搜索，在里面要求列出所有的手牌和贴图）
    ///
    /// ⇒ 清单 = `Card.bin` 全表（所有手牌）；贴图 = `UT_HandCard_&lt;卡牌id&gt;`
    ///   （本机 44 张，没有那几张就落"色块 + 名字"，如实说）。
    ///   **分页**（每页 24 张）+ **搜索**（名字 / id）。
    /// </summary>
    public sealed class CardPickDialog : Window
    {
        private const int PageSize = 24;

        private readonly TextBox _search = new TextBox();
        private readonly WrapPanel _grid = new WrapPanel();
        private readonly TextBlock _hint = new TextBlock();
        private readonly TextBlock _pageText = new TextBlock();
        private readonly Button _prev = new Button();
        private readonly Button _next = new Button();
        private readonly Button _ok = new Button();

        private CardRow _pick;
        private int _page;

        public CardRow Picked { get { return _pick; } }

        public CardPickDialog(int current)
        {
            Title = "给所有人发一张牌";
            Width = 1000;
            Height = 700;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = new SolidColorBrush(Color.FromArgb(255, 10, 13, 22));
            Foreground = (Brush)FindResource("Fg");
            FontFamily = (FontFamily)FindResource("AppFont");
            FontSize = 13;

            List<CardRow> all = GameTables.Cards();
            if (current > 0)
                foreach (CardRow c in all) if (c.Id == current) { _pick = c; break; }

            var root = new Grid();
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
            barText.Text = "给所有人发一张牌";
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
            Grid.SetRow(_hint, 0);
            body.Children.Add(_hint);

            var head = new DockPanel();
            head.Margin = new Thickness(0, 12, 0, 10);
            var pageBox = new StackPanel();
            pageBox.Orientation = Orientation.Horizontal;
            pageBox.HorizontalAlignment = HorizontalAlignment.Left;
            _prev.Style = (Style)FindResource("Ghost");
            _prev.Content = "上一页";
            _prev.Click += delegate { if (_page > 0) { _page--; Fill(); } };
            pageBox.Children.Add(_prev);
            _next.Style = (Style)FindResource("Ghost");
            _next.Content = "下一页";
            _next.Margin = new Thickness(8, 0, 0, 0);
            _next.Click += delegate { _page++; Fill(); };
            pageBox.Children.Add(_next);
            _pageText.Style = (Style)FindResource("FieldHint");
            _pageText.VerticalAlignment = VerticalAlignment.Center;
            _pageText.Margin = new Thickness(12, 0, 0, 0);
            pageBox.Children.Add(_pageText);
            DockPanel.SetDock(pageBox, Dock.Left);
            head.Children.Add(pageBox);

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
            _search.ToolTip = "按卡牌名字或 id 筛（留空 = 全部手牌）";
            _search.TextChanged += delegate { _page = 0; Fill(); };
            searchBox.Children.Add(_search);
            head.Children.Add(searchBox);

            Grid.SetRow(head, 1);
            body.Children.Add(head);

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
            _ok.Content = "确定";
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

            // 选中那张牌在第几页
            if (_pick != null)
            {
                List<CardRow> list = Filtered();
                for (int k = 0; k < list.Count; k++)
                    if (list[k].Id == _pick.Id) { _page = k / PageSize; break; }
            }
            Fill();
        }

        /// <summary>现在这一份筛选结果（搜索词生效之后）。</summary>
        private List<CardRow> Filtered()
        {
            string q = (_search.Text ?? "").Trim();
            var list = new List<CardRow>();
            foreach (CardRow c in GameTables.Cards())
            {
                if (q.Length > 0
                    && c.Name.IndexOf(q, StringComparison.OrdinalIgnoreCase) < 0
                    && c.Id.ToString(CultureInfo.InvariantCulture).IndexOf(q, StringComparison.Ordinal) < 0)
                    continue;
                list.Add(c);
            }
            return list;
        }

        private void Fill()
        {
            _grid.Children.Clear();
            List<CardRow> list = Filtered();

            int pages = (list.Count + PageSize - 1) / PageSize;
            if (pages < 1) pages = 1;
            if (_page >= pages) _page = pages - 1;
            if (_page < 0) _page = 0;

            int withArt = 0;
            for (int k = 0; k < list.Count; k++)
                if (Art.ImagePath(HandName(list[k].Id)) != null) withArt++;

            _hint.Text = "手牌一共 " + GameTables.Cards().Count + " 张，其中 " + withArt
                + " 张本机有手牌贴图（UT_HandCard_<id>）；没有贴图的落「色块 + 名字」。"
                + "　每页 " + PageSize + " 张。";

            int from = _page * PageSize;
            int to = Math.Min(list.Count, from + PageSize);
            for (int k = from; k < to; k++) _grid.Children.Add(Tile(list[k]));

            if (list.Count == 0)
            {
                var none = new TextBlock();
                none.Text = "没有匹配的手牌。";
                none.FontSize = 12;
                none.Foreground = (Brush)FindResource("FgFaint");
                none.Margin = new Thickness(4, 10, 0, 0);
                _grid.Children.Add(none);
            }

            _pageText.Text = "第 " + (_page + 1) + " / " + pages + " 页"
                + "（筛出 " + list.Count + " 张）";
            _prev.IsEnabled = _page > 0;
            _next.IsEnabled = _page + 1 < pages;
        }

        /// <summary>卡牌的手牌贴图名（本机 44 张；没有的落色块）。</summary>
        public static string HandName(int cardId)
        {
            return "UT_HandCard_" + cardId.ToString(CultureInfo.InvariantCulture);
        }

        private FrameworkElement Tile(CardRow c)
        {
            var item = new TileItem();
            item.ImageName = HandName(c.Id);
            item.Block = new SolidColorBrush(CardBlock(c.Id));
            item.Title = c.Name.Length > 0 ? c.Name : ("卡 " + c.Id);
            item.Sub = c.Id.ToString(CultureInfo.InvariantCulture);
            item.Tooltip = (c.Name.Length > 0 ? c.Name : "(没名字)") + "　卡牌 id " + c.Id
                + (Art.ImagePath(HandName(c.Id)) == null ? "\n（本机没有 UT_HandCard_" + c.Id + ".png）" : "");
            item.Tag = c;
            item.Marked = _pick != null && _pick.Id == c.Id;
            return Tiles.Make(this, item, delegate (TileItem t)
            {
                _pick = (CardRow)t.Tag;
                Fill();
            });
        }

        /// <summary>按卡牌号段给个稳定的色块（没贴图时看着不至于一片灰）。</summary>
        private static Color CardBlock(int id)
        {
            switch (id / 10000)
            {
                case 1: return Color.FromArgb(255, 62, 92, 140);
                case 2: return Color.FromArgb(255, 122, 82, 60);
                default: return Color.FromArgb(255, 92, 72, 122);
            }
        }
    }
}
