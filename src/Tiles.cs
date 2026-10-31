using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace DiyEditor
{
    /// <summary>图块上要显示的一件东西（格子功能 / 怪物 / 手牌都用它）。</summary>
    public sealed class TileItem
    {
        public string ImageName;      // 贴图名（GameRes\Img\<名字>.png）；null 就走色块 + 名字
        public Brush Block;           // ImageName 为 null 时的色块
        public string Title = "";     // 图块上的主标题（一行）
        public string Sub = "";       // 图块上的副标题（小字，可空）
        public string Tooltip = "";   // 悬停说明
        public object Tag;
        public bool Marked;           // 打个"当前"的圈
        public bool Dim;              // 灰掉（暂不支持 / 读不到）
        /// <summary>
        /// ★ 这一张要不要**按内容居中裁成正方形**再显示。
        ///   怪半身像（宽幅，角色不居中）要；格子贴图 / 手牌 / **图鉴像素小人**（本来就居中的方图）不用。
        /// </summary>
        public bool CenterSquare;

        /// <summary>
        /// ★★ 这一张是**像素画**（图鉴那套 200×200 怪小人）⇒ 放大时用 `NearestNeighbor`，别插值。
        ///   默认的双线性会把像素画糊成一团色浆，硬边才是像素画的观感。
        /// </summary>
        public bool IsPixelArt;

        public static TileItem Make(string image, Brush block, string title, string sub, object tag)
        {
            var t = new TileItem();
            t.ImageName = image;
            t.Block = block;
            t.Title = title;
            t.Sub = sub;
            t.Tag = tag;
            return t;
        }
    }

    /// <summary>
    /// ★★ 共用的"可视化图块列表" —— 用户要的**图标**就长在这里：
    ///   · 棋盘页左键点一格 ⇒ 右侧列出各种功能格子（带图标）⇒ 点一下就换；
    ///   · 新增怪物二级窗口 ⇒ 各种怪物（带头像、按分类）；
    ///   · 发牌二级窗口 ⇒ 所有手牌（带手牌贴图）。
    ///
    /// ★ 为什么做成"真控件"而不是右键菜单里的项：用户明确要"可视化图标"，
    ///   而 WPF 的 `ContextMenu` 活在另一个 Popup 里，离屏截图（RenderTargetBitmap）
    ///   根本渲不进去 —— 做成页面/窗口里的真控件，截图和真人都看得到。
    /// </summary>
    public static class Tiles
    {
        public const double TileW = 96;
        public const double TileH = 116;

        /// <summary>把一件东西铺成一块可点的图块。</summary>
        public static FrameworkElement Make(FrameworkElement owner, TileItem item, Action<TileItem> click)
        {
            var btn = new Button();
            btn.Style = owner.TryFindResource("CapsuleTile") as Style;
            btn.Width = TileW;
            btn.Height = TileH;
            btn.Margin = new Thickness(0, 0, 10, 10);
            btn.Tag = item.Tag;
            btn.ToolTip = item.Tooltip;
            btn.Cursor = Cursors.Hand;
            btn.Opacity = item.Dim ? 0.55 : 1.0;

            var stack = new StackPanel();

            var frame = new Border();
            frame.Width = 60;
            frame.Height = 60;
            frame.CornerRadius = new CornerRadius(12);
            frame.BorderThickness = new Thickness(item.Marked ? 3 : 1.4);
            frame.BorderBrush = item.Marked
                ? UiBits.Br(owner, "Accent")
                : new SolidColorBrush(Color.FromArgb(72, 255, 255, 255));
            frame.Background = item.Block ?? new SolidColorBrush(Color.FromArgb(255, 46, 54, 70));
            frame.HorizontalAlignment = HorizontalAlignment.Center;

            ImageSource art = item.CenterSquare
                ? (Art.CenteredSquare(item.ImageName) ?? Art.LoadName(item.ImageName))
                : Art.LoadName(item.ImageName);
            if (art != null)
            {
                var img = new Image();
                img.Source = art;
                img.Stretch = Stretch.Uniform;
                img.Margin = new Thickness(3);
                img.IsHitTestVisible = false;
                // ★ 像素画（图鉴怪小人）最近邻放大，保持硬边；别的照旧高质量插值。
                RenderOptions.SetBitmapScalingMode(img, MonsterArt.ModeFor(item.IsPixelArt));
                frame.Child = img;
            }
            else
            {
                var fb = new TextBlock();
                fb.Text = FirstTwo(item.Title);
                fb.FontSize = 13;
                fb.Foreground = Brushes.White;
                fb.TextAlignment = TextAlignment.Center;
                fb.TextWrapping = TextWrapping.Wrap;
                fb.VerticalAlignment = VerticalAlignment.Center;
                fb.HorizontalAlignment = HorizontalAlignment.Center;
                fb.IsHitTestVisible = false;
                frame.Child = fb;
            }
            stack.Children.Add(frame);

            var title = new TextBlock();
            title.Text = item.Title;
            title.FontSize = 11.5;
            title.Foreground = item.Dim ? UiBits.Br(owner, "FgFaint") : UiBits.Br(owner, "Fg");
            title.TextAlignment = TextAlignment.Center;
            title.TextWrapping = TextWrapping.Wrap;
            title.TextTrimming = TextTrimming.CharacterEllipsis;
            title.MaxHeight = 30;
            title.Margin = new Thickness(0, 6, 0, 0);
            title.IsHitTestVisible = false;
            stack.Children.Add(title);

            if (!string.IsNullOrEmpty(item.Sub))
            {
                var sub = new TextBlock();
                sub.Text = item.Sub;
                sub.FontSize = 10.5;
                sub.Foreground = UiBits.Br(owner, "FgFaint");
                sub.TextAlignment = TextAlignment.Center;
                sub.TextTrimming = TextTrimming.CharacterEllipsis;
                sub.IsHitTestVisible = false;
                stack.Children.Add(sub);
            }

            btn.Content = stack;
            if (click != null)
            {
                TileItem it = item;
                btn.Click += delegate { click(it); };
            }
            return btn;
        }

        private static string FirstTwo(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Length <= 2 ? s : s.Substring(0, 2);
        }

        /// <summary>把一批东西铺进一个 WrapPanel。</summary>
        public static void Fill(FrameworkElement owner, Panel host, List<TileItem> items, Action<TileItem> click)
        {
            host.Children.Clear();
            foreach (TileItem it in items) host.Children.Add(Make(owner, it, click));
        }
    }
}
