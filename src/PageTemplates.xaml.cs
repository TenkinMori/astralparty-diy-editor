using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;

namespace DiyEditor
{
    /// <summary>
    /// 第①页：地图模板 / 新建。**侧边栏上就这两块**（用户 2026-10-03 的规格）：
    ///
    ///   &gt; 侧边栏 地图模板/新建 · 新建按钮 · 现有关卡
    ///   &gt; 点击新建按钮后出现一个二级菜单窗口
    ///
    /// ⇒ 这一页上只有一个「新建」按钮 + 一份"现有关卡"清单；
    ///   关卡名字和地图选择都在**二级窗口**（`CreateLevelDialog`）里，
    ///   这一页上**没有难度输入框**（用户："不需要设置难度"）。
    /// </summary>
    public partial class PageTemplates : UserControl
    {
        public PageTemplates()
        {
            InitializeComponent();
        }

        public void Reload()
        {
            if (SubLine == null) return;          // XAML 还没装完
            string game = GameLocator.GameDir;
            string diy = GameLocator.DiyDir;

            if (string.IsNullOrEmpty(game))
            {
                SubLine.Text = "还没找到游戏目录。";
                WhereLine.Text = "点下面的「指定游戏目录…」指一下 AstralParty_CN.exe 所在的那个文件夹。";
            }
            else
            {
                SubLine.Text = GameTables.Status();
                WhereLine.Text = "游戏目录：" + game + "　·　DIY 关卡存到：" + diy;
            }

            if (NewNote != null)
            {
                int tpl = TemplateCatalog.List().Count;
                NewNote.Text = "点「新建」会弹出一个窗口：填关卡名字、选一份**成品模板**。"
                    + "模板放在「" + (TemplateCatalog.Dir.Length > 0 ? TemplateCatalog.Dir : "（先指定游戏目录）")
                    + "」，现在有 " + tpl + " 份 —— 每份都是**内容齐全**的关卡（事件 / 刷怪 / 奖励 / 胜负 / 结算），"
                    + "建好之后直接在它上面改，改完就是一份新的 DIY 关卡。";
            }
            BuildDiyList();
        }

        // ============================================================ 新建
        private void OnNew(object sender, RoutedEventArgs e)
        {
            string diy = GameLocator.DiyDir;
            if (string.IsNullOrEmpty(diy))
            {
                MessageBox.Show("还没找到游戏目录，没法确定 DIY 关卡存哪儿。\n\n先点「指定游戏目录…」。",
                    "小小兔趴 · DIY 关卡编辑器", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var dlg = new CreateLevelDialog(SuggestName());
            dlg.Owner = Window.GetWindow(this);
            if (dlg.ShowDialog() != true) return;

            LevelTemplate t = dlg.PickedTemplate;
            string name = dlg.PickedName;
            if (t == null || name.Length == 0) return;

            string path = Path.Combine(diy, name + ".json");
            if (File.Exists(path))
            {
                MessageBox.Show("已经有同名的 DIY 关卡了：\n" + path
                    + "\n\n换个名字，或者在下面「现有关卡」里直接打开它。",
                    "小小兔趴 · DIY 关卡编辑器", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            // ★ 2026-10-08：模板本身就是一份完整关卡，不再去官方 levels 找"底版"；
            //   只提醒一句"这份模板要的画板本机有没有"（没有的话棋盘页画不出格子，但关卡照样能编辑/保存）。
            MapTemplate map = t.MapId > 0 ? GameTables.Map(t.MapId) : null;
            if (map == null || !GameTables.EnsureBoardInfo(map))
            {
                MessageBoxResult go = MessageBox.Show(
                    "本机没有画板 " + (t.SceneSuffix.Length > 0 ? t.SceneSuffix : "?")
                    + " 的棋盘数据 ⇒ 棋盘页画不出格子。\n\n还是新建吗？",
                    "小小兔趴 · DIY 关卡编辑器", MessageBoxButton.OKCancel, MessageBoxImage.Warning);
                if (go != MessageBoxResult.OK) return;
            }

            try
            {
                Directory.CreateDirectory(diy);
                // ★ 难度界面上不给选/不给看：新建一律写 DIY 难度号段（1001 起）。
                LevelDoc doc = LevelDoc.CreateFromOwnTemplate(t.Path, name, LevelDoc.DiyDifficulty);
                DiySaveResult saved = doc.SaveDiy(name);

                MainWindow.Doc = doc;
                MainWindow.AddRecent(doc.Path);
                MainWindow.RefreshAll();
                GoBoard();      // ★ 用户："点击新建后自动打开棋盘页面"

                try
                {
                    MainWindow win = Application.Current != null
                        ? Application.Current.MainWindow as MainWindow : null;
                    if (win != null)
                        win.NoteStatus("已新建：" + Path.GetFileName(saved.LevelPath)
                            + "（sha256 " + saved.Sha256.Substring(0, 12) + "…）");
                }
                catch (Exception) { }
            }
            catch (Exception ex)
            {
                MessageBox.Show("新建失败：\n\n" + ex.Message, "小小兔趴 · DIY 关卡编辑器",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>跳到「棋盘」页（★ 用实例，不写死 MainWindow 的静态入口）。</summary>
        private void GoBoard()
        {
            MainWindow win = Application.Current != null ? Application.Current.MainWindow as MainWindow : null;
            if (win != null) win.GoPage(2);
        }

        private string SuggestName()
        {
            string[] existing = ListDiyFiles();
            for (int k = 1; k < 999; k++)
            {
                string want = "我的关卡" + (k == 1 ? "" : k.ToString(CultureInfo.InvariantCulture));
                bool taken = false;
                foreach (string f in existing)
                    if (string.Equals(Path.GetFileNameWithoutExtension(f), want, StringComparison.OrdinalIgnoreCase))
                    { taken = true; break; }
                if (!taken) return want;
            }
            return "我的关卡";
        }

        // ============================================================ 现有关卡
        private static string[] ListDiyFiles()
        {
            string diy = GameLocator.DiyDir;
            if (string.IsNullOrEmpty(diy) || !Directory.Exists(diy)) return new string[0];
            var outList = new List<string>();
            try
            {
                foreach (string f in Directory.GetFiles(diy, "*.json"))
                {
                    string n = Path.GetFileName(f);
                    if (n.EndsWith(".board.json", StringComparison.OrdinalIgnoreCase)) continue;
                    outList.Add(f);
                }
            }
            catch (Exception) { }
            outList.Sort(StringComparer.OrdinalIgnoreCase);
            return outList.ToArray();
        }

        private void BuildDiyList()
        {
            if (DiyList == null) return;
            DiyList.Children.Clear();
            string diy = GameLocator.DiyDir;
            if (string.IsNullOrEmpty(diy))
            {
                if (DiyWhere != null) DiyWhere.Text = "还没找到游戏目录。";
                return;
            }
            DiyWhere.Text = "目录：" + diy;
            string[] files = ListDiyFiles();
            if (files.Length == 0)
            {
                var none = new TextBlock();
                none.Text = "还没有 DIY 关卡。点上面的「新建」。";
                none.FontSize = 12;
                none.Foreground = UiBits.Br(this, "FgFaint");
                none.TextWrapping = TextWrapping.Wrap;
                DiyList.Children.Add(none);
                return;
            }
            foreach (string f in files)
            {
                string captured = f;
                var row = new Grid { Margin = new Thickness(0, 3, 0, 3) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(240) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var name = new TextBlock();
                name.Text = Path.GetFileName(f);
                name.FontSize = 12.5;
                name.Foreground = UiBits.Br(this, "Fg");
                name.TextTrimming = TextTrimming.CharacterEllipsis;
                name.VerticalAlignment = VerticalAlignment.Center;
                Grid.SetColumn(name, 0);
                row.Children.Add(name);

                var meta = new TextBlock();
                meta.FontSize = 11;
                meta.Foreground = UiBits.Br(this, "FgFaint");
                meta.VerticalAlignment = VerticalAlignment.Center;
                meta.Margin = new Thickness(12, 0, 12, 0);
                meta.Text = Describe(f);
                Grid.SetColumn(meta, 1);
                row.Children.Add(meta);

                var open = new Button { Content = "打开", Style = (Style)FindResource("Ghost") };
                open.Click += delegate
                {
                    try
                    {
                        LevelDoc doc = LevelDoc.Open(captured);
                        MainWindow.Doc = doc;
                        MainWindow.AddRecent(doc.Path);
                        MainWindow.RefreshAll();
                        GoBoard();
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show("打开失败：\n\n" + ex.Message, "小小兔趴 · DIY 关卡编辑器",
                            MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                };
                Grid.SetColumn(open, 2);
                row.Children.Add(open);
                DiyList.Children.Add(row);
            }
        }

        private static string Describe(string path)
        {
            try
            {
                var info = new FileInfo(path);
                string extra;
                try
                {
                    LevelDoc d = LevelDoc.Open(path);
                    DiyData diy = d.Diy;
                    extra = "map " + d.MapId
                        + " · 怪物池 " + diy.Pool.Count + " 只"
                        + " · 进度事件 " + diy.Events.Count + " 条";
                }
                catch (Exception) { extra = "(读不出来)"; }
                string board = "";
                string stem = Path.GetFileNameWithoutExtension(path);
                string bp = Path.Combine(Path.GetDirectoryName(path), stem + ".board.json");
                if (File.Exists(bp)) board = " · 有自己的棋盘";
                return info.LastWriteTime.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)
                    + " · " + extra + board;
            }
            catch (Exception) { return ""; }
        }

        private void OnRefresh(object sender, RoutedEventArgs e) { Reload(); }

        private void OnOpenOther(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog();
            dlg.Title = "打开一份关卡 JSON";
            dlg.Filter = "关卡 JSON (*.json)|*.json|全部文件 (*.*)|*.*";
            string start = GameLocator.DiyDir;
            if (string.IsNullOrEmpty(start)) start = LevelFinder.DefaultOpenDir();
            dlg.InitialDirectory = start;
            if (dlg.ShowDialog() != true) return;
            try
            {
                LevelDoc doc = LevelDoc.Open(dlg.FileName);
                MainWindow.Doc = doc;
                MainWindow.AddRecent(doc.Path);
                MainWindow.RefreshAll();
                GoBoard();
            }
            catch (Exception ex)
            {
                MessageBox.Show("打开失败：\n\n" + ex.Message, "小小兔趴 · DIY 关卡编辑器",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OnPickGame(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog();
            dlg.Title = "选 AstralParty_CN.exe（用它所在的那个文件夹当游戏根）";
            dlg.Filter = "游戏主程序 (AstralParty_CN.exe)|AstralParty_CN.exe|全部文件 (*.*)|*.*";
            string g = GameLocator.GameDir;
            if (!string.IsNullOrEmpty(g)) dlg.InitialDirectory = g;
            if (dlg.ShowDialog() != true) return;
            string dir = Path.GetDirectoryName(dlg.FileName);
            if (!GameLocator.LooksLikeGame(dir))
            {
                MessageBox.Show("这个文件夹看着不像游戏根（里面没有 GameRes 或 AstralParty_CN_Data）。\n\n"
                    + "选的是：" + dir, "小小兔趴 · DIY 关卡编辑器",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            GameLocator.Override = dir;
            GameLocator.Reset();
            MainWindow.SaveGameDir(dir);
            Reload();
        }
    }
}
