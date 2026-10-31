using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace DiyEditor
{
    /// <summary>几块共用的显示小件（页面②和页面⑤都要用）。</summary>
    public static class UiBits
    {
        public static Brush Br(FrameworkElement who, string key)
        {
            object found = who.TryFindResource(key);
            return found as Brush ?? Brushes.White;
        }

        /// <summary>一个"数值/状态"小药丸。</summary>
        public static Border Chip(string label, string value, Brush accent, Brush fg, Brush faint)
        {
            var box = new Border();
            box.CornerRadius = new CornerRadius(16);
            box.Background = new SolidColorBrush(Color.FromArgb(38, 255, 255, 255));
            box.BorderBrush = new SolidColorBrush(Color.FromArgb(56, 255, 255, 255));
            box.BorderThickness = new Thickness(1);
            box.Padding = new Thickness(14, 10, 16, 10);
            box.Margin = new Thickness(0, 0, 10, 10);

            var stack = new StackPanel();
            var top = new TextBlock();
            top.Text = label;
            top.FontSize = 11.5;
            top.Foreground = faint;
            stack.Children.Add(top);

            var bottom = new TextBlock();
            bottom.Text = value;
            bottom.FontSize = 14;
            bottom.Foreground = accent;
            bottom.Margin = new Thickness(0, 4, 0, 0);
            bottom.TextWrapping = TextWrapping.NoWrap;
            stack.Children.Add(bottom);

            box.Child = stack;
            return box;
        }

        public static Brush ToneBrush(FrameworkElement who, int tone)
        {
            switch (tone)
            {
                case 1: return Br(who, "OkBrush");
                case 2: return Br(who, "WaitBrush");
                case 3: return Br(who, "BadBrush");
                default: return Br(who, "FgDim");
            }
        }

        /// <summary>一条校验报告。★ 每一条都把"服务端会怎么处理"写在明面上。</summary>
        public static Border FindingRow(FrameworkElement who, LintFinding f)
        {
            var row = new Border();
            row.CornerRadius = new CornerRadius(14);
            row.Padding = new Thickness(14, 12, 16, 12);
            row.Margin = new Thickness(0, 0, 0, 8);
            row.Background = new SolidColorBrush(Color.FromArgb(30, 255, 255, 255));
            row.BorderThickness = new Thickness(3, 0, 0, 0);

            switch (f.Level)
            {
                case LintLevel.Bad: row.BorderBrush = Br(who, "BadBrush"); break;
                case LintLevel.Warn: row.BorderBrush = Br(who, "WaitBrush"); break;
                case LintLevel.Ok: row.BorderBrush = Br(who, "OkBrush"); break;
                default: row.BorderBrush = Br(who, "FgFaint"); break;
            }

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(118) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var tag = new TextBlock();
            tag.Text = f.Tag;
            tag.FontSize = 11.5;
            tag.Foreground = row.BorderBrush;
            tag.TextWrapping = TextWrapping.Wrap;
            tag.Margin = new Thickness(0, 2, 12, 0);
            Grid.SetColumn(tag, 0);
            grid.Children.Add(tag);

            var body = new StackPanel();
            var head = new TextBlock();
            head.Text = f.Where;
            head.FontSize = 12.5;
            head.FontWeight = FontWeights.SemiBold;
            head.Foreground = Br(who, "Fg");
            head.TextWrapping = TextWrapping.Wrap;
            body.Children.Add(head);

            var what = new TextBlock();
            what.Text = f.What;
            what.FontSize = 12.5;
            what.Foreground = Br(who, "FgDim");
            what.TextWrapping = TextWrapping.Wrap;
            what.Margin = new Thickness(0, 5, 0, 0);
            body.Children.Add(what);

            if (!string.IsNullOrEmpty(f.Why))
            {
                var why = new TextBlock();
                why.Text = "为什么：" + f.Why;
                why.FontSize = 11.5;
                why.Foreground = Br(who, "FgFaint");
                why.TextWrapping = TextWrapping.Wrap;
                why.Margin = new Thickness(0, 5, 0, 0);
                body.Children.Add(why);
            }

            if (!string.IsNullOrEmpty(f.Fix))
            {
                var fix = new TextBlock();
                fix.Text = "怎么办：" + f.Fix;
                fix.FontSize = 11.5;
                fix.Foreground = Br(who, "Accent");
                fix.TextWrapping = TextWrapping.Wrap;
                fix.Margin = new Thickness(0, 4, 0, 0);
                body.Children.Add(fix);
            }

            Grid.SetColumn(body, 1);
            grid.Children.Add(body);
            row.Child = grid;
            return row;
        }

        /// <summary>★ 每个白名单键的说明（"服务端读到的形状"），页面②的注释列用。</summary>
        public static readonly Dictionary<string, string> ShapeNote = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            { "map", "只用来决定文件名该叫 <map>_<diff>.json；文件里的值本身不参与归一化" },
            { "difficulty", "同上：文件名里那一档说了算，文件里的值服务端不读" },
            { "rounds", "要 > 0 才收；写 0 / null 等于没写" },
            { "round_award", "every / gold / cards 三个整数；every <= 0 ⇒ 整块丢" },
            { "start_monsters", "数组；每行 table 必填（id / monster_id 也认），缺的字段补 0" },
            { "progress", "start / per_round / per_death / mission_delta" },
            { "progress_events", "对象：事件 id → { spawn / attack / defense / move }（不是数组！）" },
            { "victory", "kind 必须非空（现在只实现 kill_boss）；targets 是怪物表 id" },
            { "mastermind", "整块原样收下，不做字段过滤" },
            { "settlement", "awards：{ 物品 id → 个数 }" },
        };
    }
}
