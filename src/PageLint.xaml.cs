using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;

namespace DiyEditor
{
    /// <summary>
    /// 第⑤页：校验 / 保存。★ 校验项 = 用户点名的**两条** + 第 11 波追的**第三条**：
    ///
    ///   &gt; 点击校验后会列出问题，但是不是你现在的问题，而是写出，
    ///   &gt; **缺少四个角色的出生点**，**有怪物未配置进度**，列出来
    ///
    ///   &gt; （第 11 波）**若作者没给事件挑 id / 挑了表里没有的 id** ⇒ 校验页列出来
    ///
    /// ⇒ 这一页不再报"哪个字段服务端会丢 / 字段名它不认"那一套（用户明说不要）。
    /// </summary>
    public partial class PageLint : UserControl
    {
        public PageLint()
        {
            InitializeComponent();
        }

        public void Reload()
        {
            Findings.Children.Clear();
            Counts.Children.Clear();
            Whitelist.Children.Clear();
            ExportLog.Text = "";
            LevelDoc doc = MainWindow.Doc;

            if (doc == null || doc.Root == null)
            {
                SubLine.Text = "还没打开关卡。";
                SaveWhere.Text = "";
                FormatNote.Text = "—";
                SourceHash.Text = "—";
                BtnSaveAs.IsEnabled = false;
                BtnOverwrite.IsEnabled = false;
                var none = new TextBlock();
                none.Text = "还没打开关卡，没得校验。";
                none.FontSize = 12;
                none.Foreground = UiBits.Br(this, "FgFaint");
                Findings.Children.Add(none);
                return;
            }
            BtnSaveAs.IsEnabled = true;
            BtnOverwrite.IsEnabled = true;

            SubLine.Text = doc.FileName + " · map " + doc.MapId
                + (doc.IsDiy ? " · DIY 难度 " + doc.Difficulty : " · 难度 " + doc.Difficulty)
                + (doc.ReadOnlyDemo ? " · 只读样例" : "")
                + (doc.Changed ? " · ● 有未保存的改动" : "");

            string diyDir = GameLocator.DiyDir;
            SaveWhere.Text = string.IsNullOrEmpty(diyDir)
                ? "还没找到游戏根目录 ⇒ 保存不了。先去「地图模板 / 新建」页点「指定游戏目录…」。"
                : "保存位置：" + diyDir + "　·　改名 / 保存都在「棋盘」页。";

            SourceHash.Text = doc.Loaded.Sha256 + "（" + doc.Loaded.Bytes.Length + " 字节）";
            FormatNote.Text = "缩进 2 空格 · 换行 " + (doc.Loaded.Eol == "\r\n" ? "CRLF（照原文）" : "LF（照原文）")
                + " · " + (doc.Loaded.HasBom ? "带 BOM（照原文）" : "不带 BOM（照原文）");

            // ★★ 就这三条（前两条是用户 2026-10-03 点名的；第 ③ 条是他第 11 波追的：
            //    "若作者没给事件挑 id / 挑了表里没有的 id ⇒ 校验页列出来"）
            List<string> check1 = DiyPool.CheckSpawns(doc);
            List<string> check2 = DiyPool.CheckMonsters(doc.Diy);
            List<string> check3 = DiyTrigger.Check(doc, doc.Diy);
            bool ok1 = check1.Count > 0 && !check1[0].StartsWith("★");
            bool ok2 = check2.Count > 0 && !check2[0].StartsWith("★");
            bool ok3 = check3.Count > 0 && !check3[0].StartsWith("★");

            Findings.Children.Add(CheckBlock("缺少四个角色的出生点", ok1, check1));
            Findings.Children.Add(CheckBlock("有怪物未配置进度", ok2, check2));
            Findings.Children.Add(CheckBlock("有进度事件没挑事件 id（触发点）", ok3, check3));

            Brush fg = UiBits.Br(this, "Fg");
            Brush faint = UiBits.Br(this, "FgFaint");
            Counts.Children.Add(UiBits.Chip("校验项", "3", UiBits.Br(this, "FgDim"), fg, faint));
            Counts.Children.Add(UiBits.Chip("没通过",
                ((ok1 ? 0 : 1) + (ok2 ? 0 : 1) + (ok3 ? 0 : 1)).ToString(),
                (ok1 && ok2 && ok3) ? UiBits.Br(this, "OkBrush") : UiBits.Br(this, "BadBrush"), fg, faint));
            Counts.Children.Add(UiBits.Chip("怪物池", doc.Diy.Pool.Count.ToString(),
                UiBits.Br(this, "FgDim"), fg, faint));

            BuildWhitelist(fg, faint);
        }

        /// <summary>一条校验 + 它列出来的问题（用户要的"列出来"）。</summary>
        private FrameworkElement CheckBlock(string title, bool ok, List<string> lines)
        {
            var box = new Border();
            box.CornerRadius = new CornerRadius(14);
            box.Padding = new Thickness(16, 13, 16, 13);
            box.Margin = new Thickness(0, 0, 0, 10);
            box.Background = new SolidColorBrush(Color.FromArgb(30, 255, 255, 255));
            box.BorderThickness = new Thickness(3, 0, 0, 0);
            box.BorderBrush = ok ? UiBits.Br(this, "OkBrush") : UiBits.Br(this, "BadBrush");

            var stack = new StackPanel();
            var head = new TextBlock();
            head.Text = (ok ? "【通过】 " : "【有问题】 ") + title;
            head.FontSize = 13;
            head.FontWeight = FontWeights.SemiBold;
            head.Foreground = ok ? UiBits.Br(this, "OkBrush") : UiBits.Br(this, "BadBrush");
            stack.Children.Add(head);

            for (int k = 0; k < lines.Count; k++)
            {
                var t = new TextBlock();
                t.Text = lines[k];
                t.FontSize = 12;
                t.TextWrapping = TextWrapping.Wrap;
                t.Margin = new Thickness(0, k == 0 ? 6 : 3, 0, 0);
                bool bad = lines[k].StartsWith("★") || lines[k].IndexOf("★", StringComparison.Ordinal) > 0;
                t.Foreground = bad ? UiBits.Br(this, "BadBrush")
                    : (k == 0 ? UiBits.Br(this, "FgDim") : UiBits.Br(this, "FgFaint"));
                stack.Children.Add(t);
            }
            box.Child = stack;
            return box;
        }


        private void OnRecheck(object sender, RoutedEventArgs e)
        {
            Reload();
        }

        // ================================================================ 白名单速查
        private void BuildWhitelist(Brush fg, Brush faint)
        {
            var lines = new List<string>();
            lines.Add("顶层只认这 10 个键：map · difficulty · rounds · round_award · start_monsters · progress · progress_events · victory · mastermind · settlement");
            lines.Add("diy：编辑器自己的扩展块（怪物池 / 进度 / 棋盘放怪），服务端不读、也不报错");
            lines.Add("round_award：every · gold · cards（every <= 0 ⇒ 整块丢）");
            lines.Add("progress：start · per_round · per_death · mission_delta");
            lines.Add("progress_events：键必须是 MapEvent 的事件 id；每条只认 spawn · attack · defense · move");
            lines.Add("start_monsters[]：table · node · hp · gold · defense · attack · type · weight（table 是必填，id / monster_id 也认）");
            lines.Add("progress_events.*.spawn[]：table · node · count · hp · gold · defense · attack · type · weight");
            lines.Add("victory：kind · targets（targets 是怪物表 id，<= 0 会被剔掉）");
            lines.Add("settlement：awards（物品 id → 个数）");
            lines.Add("mastermind：整块原样收下，不做字段过滤（拼错的键不会被拦，只是不生效）");
            lines.Add("其它任何键（note / _why / boss / groups …）：不读，也不报错。");

            foreach (string line in lines)
            {
                var t = new TextBlock();
                t.Text = "· " + line;
                t.FontSize = 11.5;
                t.Foreground = line.IndexOf("不会读", StringComparison.Ordinal) >= 0
                    ? UiBits.Br(this, "WaitBrush") : faint;
                t.TextWrapping = TextWrapping.Wrap;
                t.Margin = new Thickness(0, 0, 0, 6);
                Whitelist.Children.Add(t);
            }
        }

        // ================================================================ 导出
        /// <summary>
        /// ★「保存到本地 DIY 关卡」= 页② 那一栏（名字 + 保存按钮都在那儿，一处定义）。
        ///   这里只把用户带过去，不重复一套保存逻辑 —— 免得两个地方各存各的。
        /// </summary>
        private void OnSaveAs(object sender, RoutedEventArgs e)
        {
            LevelDoc doc = MainWindow.Doc;
            if (doc == null || doc.Root == null) return;
            ExportLog.Text = "保存位置固定是 <游戏根>\\DIY关卡\\<名字>.json；"
                + "名字和保存按钮都在「棋盘」页那一栏，已经切过去了。";
            MainWindow win = Application.Current != null ? Application.Current.MainWindow as MainWindow : null;
            if (win != null) win.GoPage(2);
        }

        private void OnSaveAsElsewhere(object sender, RoutedEventArgs e)
        {
            LevelDoc doc = MainWindow.Doc;
            if (doc == null || doc.Root == null) return;

            var dlg = new SaveFileDialog();
            dlg.Title = "另存为到别处（不进 DIY 目录）";
            dlg.Filter = "关卡 JSON (*.json)|*.json";
            dlg.FileName = SuggestName(doc);
            string own = Path.Combine(LevelFinder.OwnDir(), "导入记录");
            dlg.InitialDirectory = Directory.Exists(own) ? own : LevelFinder.OwnDir();
            if (dlg.ShowDialog() != true) return;

            try
            {
                var r = doc.Export(dlg.FileName, false, "");
                ExportLog.Text = "已导出：" + r.TargetPath + "\n"
                    + "字节数 " + r.NewLength + " · sha256 " + r.NewSha256 + "\n"
                    + (r.ByteIdentical
                        ? "与原来的文件逐字节相同（这次导出没改动任何内容）。"
                        : "与原文不同（这次导出带着你的改动）。");
                MessageBox.Show("导出成功：\n" + r.TargetPath
                    + (doc.Changed ? "\n\n（这份还带着未保存的改动 —— 如果你想让它接着编辑这份新文件，请用「打开」再打开它。）" : ""),
                    "小小兔趴 · DIY 关卡编辑器", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                ExportLog.Text = "导出失败：" + ex.Message;
                MessageBox.Show("导出失败：\n\n" + ex.Message, "小小兔趴 · DIY 关卡编辑器",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private static string SuggestName(LevelDoc doc)
        {
            string mine = Path.GetFileName(doc.Path);
            if (doc.ReadOnlyDemo) return Path.GetFileNameWithoutExtension(mine) + "_DIY.json";
            return mine;
        }

        private void OnOverwrite(object sender, RoutedEventArgs e)
        {
            LevelDoc doc = MainWindow.Doc;
            if (doc == null || doc.Root == null) return;
            if (doc.ReadOnlyDemo)
            {
                MessageBox.Show("这一份是从服务端只读样例目录打开的，编辑器不覆盖它。请用「另存为」。",
                    "小小兔趴 · DIY 关卡编辑器", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            byte[] now = doc.Bytes();
            var cur = ExportService.Load(doc.Path);
            bool same = string.Equals(cur.Sha256, ExportService.Sha256Of(now), StringComparison.OrdinalIgnoreCase);

            MessageBoxResult go = MessageBox.Show(
                "要覆盖这个文件：\n" + doc.Path + "\n\n"
                + "· 会先把它完整备份到「导出备份\\<文件名>_<时间>_覆盖前\\」并记下 sha256（可逐字节回滚）；\n"
                + "· 然后用原子替换换上新的内容（不会留半截文件）。\n"
                + (same ? "\n注意：当前内容和磁盘上的一模一样，覆盖等于什么都没改。\n" : "")
                + "\n确定吗？",
                "小小兔趴 · DIY 关卡编辑器", MessageBoxButton.OKCancel, MessageBoxImage.Warning);
            if (go != MessageBoxResult.OK) return;

            try
            {
                var r = doc.Export(doc.Path, true, "覆盖前");
                doc.RebaseAfterSave(doc.Path);
                ExportLog.Text =
                    "覆盖完成。\n"
                    + "覆盖前 sha256：" + r.SourceSha256 + "\n"
                    + "备份 sha256：  " + r.BackupSha256 + "（与原文件一致 ⇒ 可逐字节回滚）\n"
                    + "备份位置：     " + r.BackupPath + "\n"
                    + "覆盖后 sha256：" + r.NewSha256 + "\n"
                    + "字节数：" + r.NewLength + (r.ByteIdentical ? "（与覆盖前逐字节相同）" : "");
                MessageBox.Show("覆盖成功，备份在：\n" + r.BackupPath,
                    "小小兔趴 · DIY 关卡编辑器", MessageBoxButton.OK, MessageBoxImage.Information);
                MainWindow.RefreshAll();
            }
            catch (Exception ex)
            {
                ExportLog.Text = "覆盖失败：" + ex.Message;
                MessageBox.Show("覆盖失败（原文件没有被破坏）：\n\n" + ex.Message,
                    "小小兔趴 · DIY 关卡编辑器", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
