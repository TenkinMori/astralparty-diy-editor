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
    /// 第③页：进度 / 事件。**四块，按用户 2026-10-03 给的顺序**：
    ///
    ///   &gt; 然后就是进度页面，最上面第一个是**关卡总进度上限**
    ///   &gt; 第二个是**现在的进度清单**，支持左键点击后在下面编辑
    ///   &gt; 第三个是**编辑进度**，上面是**新增进度的按钮**，记得**编号作区分**
    ///   &gt; 然后编辑进度在下面显示条词，比如给所有人发一张牌 / 给所有人加多少血 /
    ///   &gt; 新增什么怪物（在怪物池里面挑选）/ 所有人加多少星币
    ///   &gt; 第四个是**获胜条件**，预留：击败指定怪物（在怪物池挑选，支持设置多个）、击败所有 BOSS 单位
    ///
    /// ★ 上面那一条「进度事件」下拉框只是**切换编辑哪一个进度事件**（新建时自带 `DIY进度` 那一条）
    ///   —— 用户把这一页叫"进度/事件"，事件就是这个意思。
    /// </summary>
    public partial class PageStory : UserControl
    {
        private bool _filling;
        private int _pickN;

        /// <summary>
        /// ★ 一条"刚做完什么"的提示（"加了第 N 条进度" / "加了一条条词：…"）。
        ///
        /// 为什么要留着一份：整页重建已经**推迟到这一次输入处理之后**（见 `MainWindow.QueueRebuild`），
        /// 而重建里的 `BuildEdit` 会把 `EditNote` 清空 —— 不留这一份的话，
        /// 用户点完按钮看到的那句提示会在几毫秒后被抹掉（功能没坏，但反馈没了）。
        /// 所以：做完事**先**记一句（当场显示），下一次重建再把它画一遍，取过就作废。
        /// </summary>
        private string _flash = "";

        /// <summary>
        /// ★★ 第 11 波：触发点那一句"刚做完什么"（同一个道理，但它挂在 `TriggerNote` 上）。
        ///
        /// 非留不可的理由比 `_flash` 更硬：**失败**那些话（"官方没有触发进度 = 5 的事件"）
        /// 是唯一告诉用户"为什么没改"的地方 —— 被重建冲掉就等于静默失败，
        /// 而用户明确要求"别静默失败、别硬编一个错的 id"。
        /// </summary>
        private string _trigFlash = "";

        /// <summary>上一次画的时候 DIY 侧数据的版本号（★ 棋盘那边改了怪 ⇒ 本页变回可见时重建）。</summary>
        private int _seenDiyVersion = -1;

        /// <summary>离屏截图用：一上来就选中某条进度事件（真人用是下拉框选）。</summary>
        public static int ShotsPickEvent = -1;

        /// <summary>
        /// ★ 交互自查（`--clicktest`）用：非 null 时代替 `ShowDialog()` ——
        ///   离屏自查**不能真弹模态窗口**（那是真窗口，会冒到屏幕上、还会把消息泵卡住），
        ///   所以留这么一个口子：二级窗口照样**真造出来**（造完交给这个口子），
        ///   只是"显示 / 等用户点"这一步换成自查自己决定。真人用时它是 null，走的还是原来的 `ShowDialog()`。
        /// </summary>
        public static Func<Window, bool?> PickDialog = null;

        /// <summary>弹二级窗口：自查接管时走 PickDialog，否则真弹。</summary>
        private bool? Ask(Window dlg)
        {
            if (PickDialog != null) return PickDialog(dlg);
            dlg.Owner = Window.GetWindow(this);
            return dlg.ShowDialog();
        }

        public PageStory()
        {
            InitializeComponent();
            // ★ 实时刷新（第 9 波 ③）：棋盘上放了 / 拿掉了怪 ⇒ 本页的怪物池按钮跟着变。
            IsVisibleChanged += delegate
            {
                if (!IsVisible) return;
                if (_seenDiyVersion == DiyNotify.Version) return;
                Reload();
            };
        }

        private DiyEvent Cur
        {
            get
            {
                LevelDoc doc = MainWindow.Doc;
                if (doc == null || doc.Root == null) return null;
                DiyData d = doc.Diy;
                if (d.Events.Count == 0) return null;
                var sel = EventPick == null ? null : EventPick.SelectedItem as EventPickItem;
                int id = sel != null ? sel.Id : (ShotsPickEvent > 0 ? ShotsPickEvent : d.Events[0].Id);
                DiyEvent ev = d.Event(id);
                return ev ?? d.Events[0];
            }
        }

        private sealed class EventPickItem
        {
            public int Id;
            public string Text = "";
            public override string ToString() { return Text; }
        }

        public void Reload()
        {
            if (SubLine == null) return;
            _seenDiyVersion = DiyNotify.Version;
            LevelDoc doc = MainWindow.Doc;
            if (doc == null || doc.Root == null)
            {
                SubLine.Text = "还没打开关卡。先去「地图模板 / 新建」建一份。";
                EventNote.Text = "";
                if (EventPick != null) EventPick.Items.Clear();
                ItemList.Children.Clear();
                EffPanel.Visibility = Visibility.Collapsed;
                VictoryBox.Children.Clear();
                ListNote.Text = "";
                VictoryNote.Text = "";
                LimitBox.Text = "";
                LimitNote.Text = "";
                return;
            }

            DiyData d = doc.Diy;
            SubLine.Text = doc.FileName + " · 怪物池 " + d.Pool.Count + " 只 · 进度事件 " + d.Events.Count + " 条"
                + (d.Pool.Count == 0 ? "（池子是空的：棋盘上右键「新增怪物」放几只，进度里才有的挑）" : "");

            FillEventPick(d);
            DiyEvent ev = Cur;
            if (ev == null)
            {
                EventNote.Text = "这份关卡里一条进度事件都没有。";
                ItemList.Children.Clear();
                EffPanel.Visibility = Visibility.Collapsed;
                VictoryBox.Children.Clear();
                return;
            }
            if (_pickN <= 0 || ev.Items.Find(delegate (DiyProgressItem x) { return x.N == _pickN; }) == null)
                _pickN = ev.Items.Count > 0 ? ev.Items[0].N : 0;

            EventNote.Text = "事件 " + ev.Id + " · " + (ev.Name.Length > 0 ? ev.Name : "(没名字)")
                + (ev.Description.Length > 0 ? " · " + ev.Description : "");

            _filling = true;
            try { LimitBox.Text = ev.Limit >= 0 ? ev.Limit.ToString(CultureInfo.InvariantCulture) : ""; }
            finally { _filling = false; }
            LimitNote.Text = ev.Limit >= 0
                ? "现在写的是 " + ev.Limit + "。"
                : "现在没写这个键（留空 = 不写）。";
            LimitNote.ToolTip = "写进顶层 `progress.limit` —— DIY 关卡按自己这份走"
                + "（服务端第二批会优先认它；现在服务端读的是地图难度行的 ProgressLimit）。";

            BuildList(ev);
            BuildEdit(ev, d);
            BuildVictory(ev, d);
        }

        private void FillEventPick(DiyData d)
        {
            bool same = EventPick.Items.Count == d.Events.Count;
            if (same)
            {
                for (int k = 0; k < d.Events.Count && same; k++)
                {
                    var it = EventPick.Items[k] as EventPickItem;
                    if (it == null || it.Id != d.Events[k].Id) same = false;
                }
            }
            _filling = true;
            try
            {
                if (!same)
                {
                    EventPick.Items.Clear();
                    foreach (DiyEvent e in d.Events)
                    {
                        var it = new EventPickItem();
                        it.Id = e.Id;
                        // ★ 扁平模型：一个事件 = 一个触发点（那条进度条目就带着它）
                        string trig = "";
                        if (e.Items.Count > 0 && e.Items[0].TriggerProgress >= 0)
                            trig = "· 触发进度 " + e.Items[0].TriggerProgress;
                        else trig = "· ★ 还没设触发点";
                        it.Text = e.Id + " " + (e.Name.Length > 0 ? e.Name : "(没名字)") + " " + trig;
                        EventPick.Items.Add(it);
                    }
                }
                DiyEvent want = Cur;
                for (int k = 0; k < EventPick.Items.Count; k++)
                {
                    var it = EventPick.Items[k] as EventPickItem;
                    if (it != null && want != null && it.Id == want.Id) { EventPick.SelectedIndex = k; break; }
                }
            }
            finally { _filling = false; }
        }

        private void OnEventPicked(object sender, SelectionChangedEventArgs e)
        {
            if (_filling) return;
            _pickN = 0;
            Reload();
        }

        // ============================================================ ① 上限
        private void OnLimitChanged(object sender, RoutedEventArgs e)
        {
            if (_filling) return;
            LevelDoc doc = MainWindow.Doc;
            DiyEvent ev = Cur;
            if (doc == null || ev == null) return;
            string t = (LimitBox.Text ?? "").Trim();
            if (t.Length == 0)
            {
                ev.Limit = -1;
                LimitNote.Text = "现在没写这个键（留空 = 不写）。";
            }
            else
            {
                long v;
                if (!long.TryParse(t, NumberStyles.Integer, CultureInfo.InvariantCulture, out v) || v < 0)
                {
                    LimitNote.Text = "要写一个 0 或更大的整数。";
                    return;
                }
                ev.Limit = v;
                LimitNote.Text = "现在写的是 " + v + "。";
            }
            doc.SaveDiy();
            MainWindow.RefreshAll();
        }

        // ============================================================ ② 清单
        /// <summary>
        /// ★★ 2026-10-08 大改：② 这一栏改成**列整关的事件**（一个事件 = 一个触发点
        ///   = 一波"刷多少怪 + 发什么奖励"）。点一条 ⇒ 上面的事件下拉切到它、下面 ③ 编辑它。
        /// </summary>
        private void BuildList(DiyEvent ev)
        {
            ItemList.Children.Clear();
            LevelDoc doc = MainWindow.Doc;
            DiyData d = doc == null ? null : doc.Diy;
            if (d == null) return;
            d.Events.Sort(delegate (DiyEvent a, DiyEvent b) { return a.Id.CompareTo(b.Id); });

            ListNote.Text = d.Events.Count == 0
                ? "这一关一个事件都没有。点下面的「＋ 新增事件」挑一条官方事件当触发点。"
                : (d.Events.Count + " 个事件。点一条就切到它（上面那个下拉也会跟着跳）。"
                   + "★ 事件的 id 就是官方事件 id，它决定这一波在第几点进度播。");

            foreach (DiyEvent e in d.Events)
            {
                DiyEvent cap = e;
                bool on = ev != null && cap.Id == ev.Id;
                var btn = new Button();
                btn.Style = (Style)FindResource(on ? "Primary" : "Ghost");
                btn.Margin = new Thickness(0, 0, 0, 6);
                btn.HorizontalAlignment = HorizontalAlignment.Stretch;
                btn.HorizontalContentAlignment = HorizontalAlignment.Left;
                btn.Cursor = Cursors.Hand;

                var g = new Grid();
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(180) });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var no = new TextBlock();
                no.Text = "事件 " + cap.Id;
                no.FontSize = 12.5;
                no.FontWeight = FontWeights.SemiBold;
                no.VerticalAlignment = VerticalAlignment.Center;
                Grid.SetColumn(no, 0);
                g.Children.Add(no);

                var sub = new TextBlock();
                sub.Text = (cap.Name.Length > 0 ? cap.Name + "　·　" : "") + EventSummary(cap);
                sub.FontSize = 11.5;
                sub.Foreground = UiBits.Br(this, "FgFaint");
                sub.VerticalAlignment = VerticalAlignment.Center;
                Grid.SetColumn(sub, 1);
                g.Children.Add(sub);

                DiyProgressItem first = cap.Items.Count > 0 ? cap.Items[0] : null;
                var trig = new TextBlock();
                trig.Text = DiyTrigger.Short(doc, first);
                trig.FontSize = 11.5;
                trig.FontWeight = FontWeights.SemiBold;
                // ★ 选中那一行用的是 `Primary` 样式 ⇒ **底色就是 Accent**，而这里原来写死
                //   `Accent` 前景色 ⇒ 选中行的"触发进度 N"变成浅青写在浅青上，**看不见**
                //   （2026-10-10 实测截图里选中行右边是空的，对比度只有 1.0:1）。
                //   口径：**只改可读性、不动配色体系** —— 选中行一律改用 `Primary` 样式自己
                //   那个前景色（`AccentDeep`，同一套主题色资源，压在 Accent 上是 12.1:1）；
                //   **未选中行一个字都不改**（有触发点 ⇒ `Accent`，没设触发点 ⇒ `FgFaint`）。
                //   ★ 选中行的"触发点没设"也一起用 AccentDeep：`FgFaint` 压在 Accent 上只有
                //     2.0:1，是同一类读不清（这一档在未选中行里照旧是 FgFaint）。
                trig.Foreground = UiBits.Br(this, on ? "AccentDeep"
                    : (first != null && first.TriggerProgress >= 0 ? "Accent" : "FgFaint"));
                trig.VerticalAlignment = VerticalAlignment.Center;
                trig.Margin = new Thickness(12, 0, 0, 0);
                if (first != null) trig.ToolTip = DiyTrigger.Detail(doc, first);
                Grid.SetColumn(trig, 2);
                g.Children.Add(trig);

                btn.Content = g;
                btn.Click += delegate
                {
                    _pickN = cap.Items.Count > 0 ? cap.Items[0].N : 0;
                    SelectEvent(cap.Id);
                    Reload();
                };
                ItemList.Children.Add(btn);
            }
        }

        /// <summary>一个事件的一行摘要（刷几只怪 / 几条奖励 / 有没有全场攻防）。</summary>
        private static string EventSummary(DiyEvent e)
        {
            int monsters = 0, rewards = 0;
            bool atk = false, def = false;
            foreach (DiyProgressItem it in e.Items)
                foreach (DiyEffect x in it.Effects)
                {
                    if (x.Kind == EffectKind.Spawn) monsters += x.Count < 1 ? 1 : x.Count;
                    else if (x.IsReward) rewards++;
                    else if (x.Kind == EffectKind.MonsterAttack) atk = true;
                    else if (x.Kind == EffectKind.MonsterDefense) def = true;
                }
            var parts = new List<string>();
            parts.Add("刷怪 " + monsters + " 只");
            parts.Add("奖励 " + rewards + " 条");
            if (atk) parts.Add("全场怪 +攻");
            if (def) parts.Add("全场怪 +防");
            return string.Join(" · ", parts.ToArray());
        }

        // ============================================================ ③ 编辑进度
        /// <summary>
        /// ★★ 2026-10-08 大改：**新建一个事件**。
        ///
        /// 扁平模型下"一个事件 = 一个触发点"，所以新建事件 = 挑一条**官方事件 id**
        /// （就是它决定这一波在第几点进度播）：事件的 `Id` 直接取那条官方事件 id ——
        /// 这样 `diy.events.&lt;id&gt;` 的键和 `progress_events.&lt;id&gt;` 的键是同一个，不会再有两套号。
        /// 新建出来的事件自带一条进度条目（触发点 = 那条官方事件在表里的触发进度）。
        /// </summary>
        private void OnAddEvent(object sender, RoutedEventArgs e)
        {
            LevelDoc doc = MainWindow.Doc;
            if (doc == null || doc.Root == null) return;
            if (MapEventTable.Rows().Count == 0)
            {
                Flash("★ 读不到 MapEvent.bin（" + MapEventTable.Note + "）⇒ 没法新建事件。");
                return;
            }

            var dlg = new TriggerPickDialog(-1, -1);
            if (Ask(dlg) != true || dlg.Picked == null) return;
            MapEventRow row = dlg.Picked;

            DiyData d = doc.Diy;
            if (d.Event(row.Id) != null)
            {
                Flash("事件 " + row.Id + " 已经在清单里了 —— 下拉里选中它直接改就行。");
                SelectEvent(row.Id);
                MainWindow.RefreshAll();
                return;
            }

            var ev = new DiyEvent();
            ev.Id = row.Id;
            ev.Name = row.Name != null && row.Name.Length > 0 ? row.Name : ("事件 " + row.Id);
            var it = new DiyProgressItem();
            it.N = 1;
            it.TriggerEvent = row.Id;
            it.TriggerProgress = row.Progress;
            ev.Items.Add(it);
            d.Events.Add(ev);
            d.Events.Sort(delegate (DiyEvent a, DiyEvent b) { return a.Id.CompareTo(b.Id); });

            _pickN = 1;
            SelectEvent(row.Id);
            doc.SaveDiy();
            Flash("新建事件 " + row.Id + "「" + ev.Name + "」（触发进度 " + row.Progress + "）——"
                + "现在给它加刷怪和奖励。");
            MainWindow.RefreshAll();
        }

        /// <summary>把下拉选中到某一个事件（找不到就不动）。</summary>
        private void SelectEvent(int id)
        {
            if (EventPick == null) return;
            _filling = true;
            try
            {
                for (int k = 0; k < EventPick.Items.Count; k++)
                {
                    var it = EventPick.Items[k] as EventPickItem;
                    if (it != null && it.Id == id) { EventPick.SelectedIndex = k; break; }
                }
            }
            finally { _filling = false; }
        }

        // ============================================================ ★★ 第 11 波：触发点
        /// <summary>当前选中的那条进度（③ 编辑区里正在编辑的那一条）。</summary>
        private DiyProgressItem CurItem
        {
            get
            {
                DiyEvent ev = Cur;
                if (ev == null || _pickN <= 0) return null;
                return ev.Items.Find(delegate (DiyProgressItem x) { return x.N == _pickN; });
            }
        }

        /// <summary>
        /// ★ 用户直接在「触发进度 / 回合」里填一个数 ⇒ 反查官方事件 → 挑一条 → 写进配置。
        ///   官方没有那一档 ⇒ **明说没改**（`DiyTrigger.Apply` 一个字节都不动），并把框恢复原值。
        /// </summary>
        private void OnTriggerChanged(object sender, RoutedEventArgs e)
        {
            if (_filling) return;
            LevelDoc doc = MainWindow.Doc;
            DiyEvent ev = Cur;
            DiyProgressItem it = CurItem;
            if (doc == null || ev == null) return;
            if (it == null) { TrigShow("先在上面点一条进度。"); return; }

            string t = (TriggerBox.Text ?? "").Trim();
            int oldP = it.TriggerProgress;
            if (t.Length == 0)
            {
                if (oldP < 0) { TrigShow(""); return; }
                DiyTrigger.Clear(it);
                doc.SaveDiy();
                TrigShow("取消了这一条的触发点（配置里那两个键一起摘掉）。");
                MainWindow.RefreshAll();
                return;
            }

            int want;
            if (!int.TryParse(t, NumberStyles.Integer, CultureInfo.InvariantCulture, out want) || want < 0 || want > 99999)
            {
                TrigShow("触发进度要填 0 或更大的整数（0 = 开局那一刻）。");
                TriggerBox.Text = oldP >= 0 ? oldP.ToString(CultureInfo.InvariantCulture) : "";
                return;
            }
            if (want == oldP && it.TriggerEvent > 0)
            {
                TrigShow(DiyTrigger.Detail(doc, it));
                return;
            }

            string note;
            bool ok = DiyTrigger.Apply(doc, doc.Diy, it, want, out note);
            if (!ok)
            {
                // ★ 失败：一个字节都没改 —— 把框恢复到原来的值，别让界面上留着一个没生效的数
                TrigShow(note);
                TriggerBox.Text = oldP >= 0 ? oldP.ToString(CultureInfo.InvariantCulture) : "";
                return;
            }
            doc.SaveDiy();
            TrigShow(note);
            MainWindow.RefreshAll();
        }

        /// <summary>★ 「挑官方事件…」= 把官方那一档的事件列出来给他挑。</summary>
        private void OnTriggerPick(object sender, RoutedEventArgs e)
        {
            LevelDoc doc = MainWindow.Doc;
            DiyProgressItem it = CurItem;
            if (doc == null || it == null) { TrigShow("先在上面点一条进度。"); return; }
            if (MapEventTable.Rows().Count == 0)
            {
                TrigShow("★ 读不到 MapEvent.bin（" + MapEventTable.Note + "）⇒ 挑不了官方事件。");
                return;
            }

            var dlg = new TriggerPickDialog(it.TriggerProgress, it.TriggerEvent);
            if (Ask(dlg) != true) return;

            string note;
            if (!DiyTrigger.ApplyRow(doc, it, dlg.Picked, out note)) { TrigShow(note); return; }
            doc.SaveDiy();
            TrigShow(note);
            MainWindow.RefreshAll();
        }

        /// <summary>把触发点那一句提示记下来并当场显示（下一次重建再画一遍，取过就作废）。</summary>
        private void TrigShow(string note)
        {
            _trigFlash = note ?? "";
            TriggerNote.Text = _trigFlash;
        }

        /// <summary>取走那句提示（一次性）。</summary>
        private string TakeTrigFlash()
        {
            string s = _trigFlash;
            _trigFlash = "";
            return s;
        }

        private void BuildEdit(DiyEvent ev, DiyData d)
        {
            LevelDoc doc = MainWindow.Doc;
            DiyProgressItem it = null;
            if (_pickN > 0) it = ev.Items.Find(delegate (DiyProgressItem x) { return x.N == _pickN; });
            if (it == null)
            {
                EffPanel.Visibility = Visibility.Collapsed;
                EditNote.Text = ev.Items.Count == 0 ? "先「新增进度」。" : "左键点上面清单里的一条。";
                return;
            }
            EffPanel.Visibility = Visibility.Visible;
            EditHead.Text = "第 " + it.N + " 条进度的条词（" + it.Effects.Count + " 条）";
            EditNote.Text = TakeFlash();

            // ---- ★★ 第 11 波：触发点（这条进度事件在第几点进度 / 约第几回合触发）
            _filling = true;
            try { TriggerBox.Text = it.TriggerProgress >= 0 ? it.TriggerProgress.ToString(CultureInfo.InvariantCulture) : ""; }
            finally { _filling = false; }
            TriggerRound.Text = it.TriggerProgress >= 0 ? DiyTrigger.RoundText(doc, it.TriggerProgress) : "";
            string tf = TakeTrigFlash();
            TriggerNote.Text = tf.Length > 0 ? tf : DiyTrigger.Detail(doc, it);

            EffList.Children.Clear();
            for (int k = 0; k < it.Effects.Count; k++)
            {
                DiyProgressItem capItem = it;
                int idx = k;
                DiyEffect x = it.Effects[k];
                var row = new Border();
                row.CornerRadius = new CornerRadius(12);
                row.Background = new SolidColorBrush(Color.FromArgb(28, 255, 255, 255));
                row.Padding = new Thickness(12, 9, 10, 9);
                row.Margin = new Thickness(0, 0, 0, 6);

                var g = new Grid();
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(124) });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var tag = new TextBlock();
                tag.Text = (idx + 1) + ". " + DiyEffect.KindShort(x.Kind);
                tag.FontSize = 12;
                tag.Foreground = UiBits.Br(this, "Accent");
                tag.VerticalAlignment = VerticalAlignment.Center;
                Grid.SetColumn(tag, 0);
                g.Children.Add(tag);

                var body = new StackPanel();
                body.VerticalAlignment = VerticalAlignment.Center;
                var bodyText = new TextBlock();
                bodyText.Text = x.Summary(d.Pool);
                bodyText.FontSize = 12.5;
                bodyText.Foreground = UiBits.Br(this, "Fg");
                bodyText.TextWrapping = TextWrapping.Wrap;
                body.Children.Add(bodyText);
                // ★★ 2026-10-09：刷怪这条只写"计划只数 + 挑哪只怪种"；**每一只自己的数值**
                //   在「怪物」页按格改（用户："我要每个独立的怪物都分开，数值也要分开 diy"）。
                //   这句提示是为了别让人以为是两套数据：条词说数量，怪物页说每一处放置的数值。
                if (x.Kind == EffectKind.Spawn)
                {
                    var tie = new TextBlock();
                    tie.Text = "具体每一只的数值在「怪物」页按格改（同一只怪放几处就有几份，各改各的）。";
                    tie.FontSize = 11;
                    tie.Foreground = UiBits.Br(this, "FgFaint");
                    tie.TextWrapping = TextWrapping.Wrap;
                    tie.Margin = new Thickness(0, 3, 0, 0);
                    body.Children.Add(tie);
                }
                Grid.SetColumn(body, 1);
                g.Children.Add(body);

                var ops = new StackPanel();
                ops.Orientation = Orientation.Horizontal;

                // ★ 数值型（加扣血 / 加星币 / 怪物攻 / 怪物防）就地一个框改 N
                if (x.Kind != EffectKind.Card && x.Kind != EffectKind.Spawn
                    && x.Kind != EffectKind.Relic)
                {
                    var lb = new TextBlock();
                    lb.Text = x.Kind == EffectKind.Hp ? "血量" : (x.Kind == EffectKind.Gold ? "星币" : "数值");
                    lb.Style = (Style)FindResource("FieldLabel");
                    lb.VerticalAlignment = VerticalAlignment.Center;
                    lb.Margin = new Thickness(0, 0, 6, 0);
                    ops.Children.Add(lb);

                    var box = new TextBox();
                    box.Style = (Style)FindResource("Field");
                    box.Width = 76;
                    box.Text = x.N.ToString(CultureInfo.InvariantCulture);
                    box.MaxLength = 8;
                    box.ToolTip = x.Kind == EffectKind.Hp
                        ? "正数 = 加血，负数 = 扣血。"
                        : "正数 = 加，负数 = 减。";
                    DiyEffect capX = x;
                    box.LostFocus += delegate
                    {
                        long v;
                        if (!long.TryParse((box.Text ?? "").Trim(), NumberStyles.Integer,
                                CultureInfo.InvariantCulture, out v)) { box.Text = capX.N.ToString(CultureInfo.InvariantCulture); return; }
                        if (v == capX.N) return;
                        capX.N = v;
                        AfterEdit("改成：" + capX.Summary(d.Pool));
                    };
                    ops.Children.Add(box);
                }

                if (x.Kind == EffectKind.Card || x.Kind == EffectKind.Spawn)
                {
                    var pick = new Button();
                    pick.Style = (Style)FindResource("Ghost");
                    pick.Content = x.Kind == EffectKind.Card ? "选手牌…" : "挑怪物…";
                    pick.Padding = new Thickness(10, 3, 10, 3);
                    pick.Click += delegate { Configure(capItem, idx); };
                    ops.Children.Add(pick);
                }

                // ★ 2026-10-08：刷怪数量（这个事件刷几只）
                if (x.Kind == EffectKind.Spawn)
                {
                    var lb = new TextBlock();
                    lb.Text = "数量";
                    lb.Style = (Style)FindResource("FieldLabel");
                    lb.VerticalAlignment = VerticalAlignment.Center;
                    lb.Margin = new Thickness(10, 0, 6, 0);
                    ops.Children.Add(lb);

                    var box = new TextBox();
                    box.Style = (Style)FindResource("Field");
                    box.Width = 60;
                    box.Text = x.Count.ToString(CultureInfo.InvariantCulture);
                    box.MaxLength = 4;
                    box.ToolTip = "这一次刷几只。棋盘上放了 N 格就按格刷 N 只，多出来的按随机空地补。";
                    DiyEffect capS = x;
                    box.LostFocus += delegate
                    {
                        long v;
                        if (!long.TryParse((box.Text ?? "").Trim(), NumberStyles.Integer,
                                CultureInfo.InvariantCulture, out v) || v < 1)
                        { box.Text = capS.Count.ToString(CultureInfo.InvariantCulture); return; }
                        if (v == capS.Count) return;
                        capS.Count = (int)v;
                        AfterEdit("改成：" + capS.Summary(d.Pool));
                    };
                    ops.Children.Add(box);
                }

                // ★ 2026-10-08：筹码张数（三选一给几张）
                if (x.Kind == EffectKind.Relic)
                {
                    var lb = new TextBlock();
                    lb.Text = "张数";
                    lb.Style = (Style)FindResource("FieldLabel");
                    lb.VerticalAlignment = VerticalAlignment.Center;
                    lb.Margin = new Thickness(0, 0, 6, 0);
                    ops.Children.Add(lb);

                    var box = new TextBox();
                    box.Style = (Style)FindResource("Field");
                    box.Width = 60;
                    box.Text = x.Choice.ToString(CultureInfo.InvariantCulture);
                    box.MaxLength = 3;
                    box.ToolTip = "弹出几张让它挑一张（官方那套是三选一）。";
                    DiyEffect capR = x;
                    box.LostFocus += delegate
                    {
                        long v;
                        if (!long.TryParse((box.Text ?? "").Trim(), NumberStyles.Integer,
                                CultureInfo.InvariantCulture, out v) || v < 1)
                        { box.Text = capR.Choice.ToString(CultureInfo.InvariantCulture); return; }
                        if (v == capR.Choice) return;
                        capR.Choice = (int)v;
                        AfterEdit("改成：" + capR.Summary(d.Pool));
                    };
                    ops.Children.Add(box);
                }

                // ★ 2026-10-08：奖励发给谁（全队每人 / 触发这条事件的那个人）
                if (x.IsReward)
                {
                    var lb = new TextBlock();
                    lb.Text = "给谁";
                    lb.Style = (Style)FindResource("FieldLabel");
                    lb.VerticalAlignment = VerticalAlignment.Center;
                    lb.Margin = new Thickness(10, 0, 6, 0);
                    ops.Children.Add(lb);

                    var cb = new ComboBox();
                    cb.Style = (Style)FindResource("Drop");
                    cb.Width = 96;
                    cb.Items.Add("全队");
                    cb.Items.Add("触发者");
                    cb.SelectedIndex = x.To == DiyEffect.ToActor ? 1 : 0;
                    DiyEffect capW = x;
                    cb.SelectionChanged += delegate
                    {
                        string to = cb.SelectedIndex == 1 ? DiyEffect.ToActor : DiyEffect.ToTeam;
                        if (to == capW.To) return;
                        capW.To = to;
                        AfterEdit("改成：" + capW.Summary(d.Pool));
                    };
                    ops.Children.Add(cb);
                }
                var del = new Button();
                del.Style = (Style)FindResource("Ghost");
                del.Content = "删除";
                del.Padding = new Thickness(10, 3, 10, 3);
                del.Margin = new Thickness(6, 0, 0, 0);
                del.Click += delegate
                {
                    DiyEffect gone = capItem.Effects[idx];
                    capItem.Effects.RemoveAt(idx);
                    // ★ 2026-10-08：删掉一条「新增怪物」= **连它在棋盘上放的那几格一起撤**。
                    //   不撤的话就成了"条词删了、棋盘上的怪还在继续刷"，用户会看不懂。
                    if (gone.Kind == EffectKind.Spawn && d != null)
                    {
                        int evKey = DiyPool.EventKeyOf(ev);
                        d.BoardMonsters.RemoveAll(delegate (BoardMonsterRef r)
                        { return r.Pool == gone.Pool && r.Event == evKey; });
                    }
                    DiyPool.BindProgress(d);
                    AfterEdit("删掉了一条条词。");
                };
                ops.Children.Add(del);
                Grid.SetColumn(ops, 2);
                g.Children.Add(ops);

                row.Child = g;
                EffList.Children.Add(row);
            }
            if (it.Effects.Count == 0)
            {
                var none = new TextBlock();
                none.Text = "这一条还没有条词。用下面那排按钮加。";
                none.FontSize = 11.5;
                none.Foreground = UiBits.Br(this, "FgFaint");
                none.Margin = new Thickness(0, 0, 0, 6);
                EffList.Children.Add(none);
            }

            BuildAddRow(it, d);
        }

        /// <summary>一条条词一个按钮（★ 2026-10-08：补上第七类「发一次筹码」）。</summary>
        private void BuildAddRow(DiyProgressItem it, DiyData d)
        {
            AddEffRow.Children.Clear();
            foreach (EffectKind k in new EffectKind[]
            {
                EffectKind.Card, EffectKind.Hp, EffectKind.Spawn, EffectKind.Gold,
                EffectKind.MonsterAttack, EffectKind.MonsterDefense, EffectKind.Relic,
            })
            {
                EffectKind cap = k;
                var b = new Button();
                b.Style = (Style)FindResource("Ghost");
                b.Content = "＋ " + DiyEffect.KindName(cap);
                b.Margin = new Thickness(0, 0, 8, 8);
                b.Padding = new Thickness(12, 6, 12, 6);
                b.Click += delegate { AddEffect(it, d, cap); };
                AddEffRow.Children.Add(b);
            }
        }

        private void AddEffect(DiyProgressItem it, DiyData d, EffectKind k)
        {
            LevelDoc doc = MainWindow.Doc;
            if (doc == null) return;
            var x = new DiyEffect();
            x.Kind = k;
            switch (k)
            {
                case EffectKind.Card:
                    {
                        List<CardRow> cs = GameTables.Cards();
                        x.Card = cs.Count > 0 ? cs[0].Id : 10001;
                        // ★ 用户要的"二级菜单窗口，可分页搜索，在里面要求列出所有的手牌和贴图"
                        var dlg = new CardPickDialog(x.Card);
                        if (Ask(dlg) != true) return;
                        x.Card = dlg.Picked.Id;
                        break;
                    }
                case EffectKind.Hp:
                    x.N = 2;
                    break;
                case EffectKind.Gold:
                    x.N = 10;
                    break;
                case EffectKind.MonsterAttack:
                case EffectKind.MonsterDefense:
                    x.N = 1;
                    break;
                case EffectKind.Relic:
                    // ★ 2026-10-08：发一次筹码 —— 走官方那套三选一（张数默认 3）
                    x.Choice = 3;
                    break;
                case EffectKind.Spawn:
                    {
                        if (d.Pool.Count == 0)
                        {
                            MessageBox.Show(Window.GetWindow(this),
                                "怪物池还是空的。\n\n先在棋盘页右键格子 →「新增怪物」放几只，再回来加这条条词。",
                                "小小兔趴 · DIY 关卡编辑器", MessageBoxButton.OK, MessageBoxImage.Information);
                            return;
                        }
                        var md = MonsterPickDialog.ForPool(d.Pool);
                        if (Ask(md) != true) return;
                        PoolMonster hit = FindInPoolByTable(d, md.Picked.Id);
                        if (hit == null) hit = DiyPool.MakePool(d, md.Picked.Id);
                        // ★ 2026-10-08：同一个事件里同一只怪**只留一条**（"有几个怪物就显示几行"）；
                        //   本来就有它 ⇒ 数量 +1，不再加一行。
                        foreach (DiyEffect ex in it.Effects)
                            if (ex.Kind == EffectKind.Spawn && ex.Pool == hit.Id)
                            {
                                ex.Count = (ex.Count < 1 ? 1 : ex.Count) + 1;
                                DiyPool.BindProgress(d);
                                AfterEdit("这一只本来就有 ⇒ 数量改成 " + ex.Count + "。");
                                return;
                            }
                        x.Pool = hit.Id;
                        break;
                    }
            }
            it.Effects.Add(x);
            AfterEdit("加了一条条词：" + x.Summary(d.Pool));
        }

        private static PoolMonster FindInPoolByTable(DiyData d, int table)
        {
            foreach (PoolMonster m in d.Pool) if (m.Table == table) return m;
            return null;
        }

        /// <summary>改一条已经加进去的条词（发牌 / 挑怪物）。</summary>
        private void Configure(DiyProgressItem it, int index)
        {
            LevelDoc doc = MainWindow.Doc;
            if (doc == null || index < 0 || index >= it.Effects.Count) return;
            DiyEffect x = it.Effects[index];
            if (x.Kind == EffectKind.Card)
            {
                var dlg = new CardPickDialog(x.Card);
                if (Ask(dlg) != true) return;
                x.Card = dlg.Picked.Id;
                AfterEdit("换成手牌 " + x.Card + "（" + GameTables.CardName(x.Card) + "）。");
            }
            else if (x.Kind == EffectKind.Spawn)
            {
                DiyData d = doc.Diy;
                if (d.Pool.Count == 0)
                {
                    MessageBox.Show(Window.GetWindow(this), "怪物池是空的。先去棋盘页放几只怪。",
                        "小小兔趴 · DIY 关卡编辑器", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
                var md = MonsterPickDialog.ForPool(d.Pool);
                if (Ask(md) != true) return;
                PoolMonster hit = FindInPoolByTable(d, md.Picked.Id);
                if (hit == null) hit = DiyPool.MakePool(d, md.Picked.Id);
                x.Pool = hit.Id;
                AfterEdit("换成池子里的 #" + hit.Id + "（" + hit.Name + "）。");
            }
        }

        private void AfterEdit(string note)
        {
            LevelDoc doc = MainWindow.Doc;
            if (doc != null) doc.SaveDiy();
            Flash(note);                 // ★ 先记着：重建被推迟到这次输入之后，提示要活到那时候
            MainWindow.RefreshAll();
        }

        /// <summary>记一句"刚做完什么"，并且当场显示（下一次重建再画一遍 ⇒ 不会被推迟的重建抹掉）。</summary>
        private void Flash(string note)
        {
            _flash = note ?? "";
            EditNote.Text = _flash;
        }

        /// <summary>取走那句提示（一次性：取过就没了，免得一直挂着骗人）。</summary>
        private string TakeFlash()
        {
            string s = _flash;
            _flash = "";
            return s;
        }

        // ============================================================ ④ 获胜条件
        private void BuildVictory(DiyEvent ev, DiyData d)
        {
            VictoryBox.Children.Clear();
            VictoryNote.Text = "预留两种（用户指定）：击败指定怪物（可多选，从怪物池里挑）、击败所有 BOSS 单位。";

            foreach (string kind in DiyVictory.All)
            {
                string cap = kind;
                var cb = new CheckBox();
                cb.Style = (Style)FindResource("Check");
                cb.Content = kind;
                cb.IsChecked = ev.VictoryKinds.Contains(kind);
                cb.Margin = new Thickness(0, 6, 0, 0);
                cb.Click += delegate
                {
                    if (cb.IsChecked == true) { if (!ev.VictoryKinds.Contains(cap)) ev.VictoryKinds.Add(cap); }
                    else ev.VictoryKinds.Remove(cap);
                    AfterEdit("获胜条件：" + (cb.IsChecked == true ? "勾上" : "取消") + "「" + cap + "」。");
                };
                VictoryBox.Children.Add(cb);
            }

            // 击败指定怪物：从池子里多选
            bool wantTargets = ev.VictoryKinds.Contains(DiyVictory.TypeKillMonsters);
            var head = new TextBlock();
            head.Text = "击败指定怪物 —— 挑哪几只（可多选）：";
            head.Style = (Style)FindResource("FieldLabel");
            head.Margin = new Thickness(0, 12, 0, 6);
            head.IsEnabled = wantTargets;
            VictoryBox.Children.Add(head);

            if (d.Pool.Count == 0)
            {
                var none = new TextBlock();
                none.Text = "怪物池是空的（棋盘页右键「新增怪物」放几只，这里才有的挑）。";
                none.FontSize = 11.5;
                none.Foreground = UiBits.Br(this, "FgFaint");
                VictoryBox.Children.Add(none);
                return;
            }
            var wrap = new WrapPanel();
            foreach (PoolMonster m in d.Pool)
            {
                PoolMonster cap = m;
                var btn = new Button();
                bool on = ev.VictoryTargets.Contains(cap.Id);
                btn.Style = (Style)FindResource(on ? "Primary" : "Ghost");
                btn.Content = cap.Display;
                btn.Margin = new Thickness(0, 0, 8, 8);
                btn.Padding = new Thickness(12, 6, 12, 6);
                btn.IsEnabled = wantTargets;
                btn.ToolTip = cap.Name + " · " + cap.ClassName + " · 血 " + cap.Hp + " / 防 " + cap.Defense
                    + " / 攻 " + cap.Attack + " / 币 " + cap.Gold + (cap.Counter ? " · 会反击" : "");
                btn.Click += delegate
                {
                    if (ev.VictoryTargets.Contains(cap.Id)) ev.VictoryTargets.Remove(cap.Id);
                    else ev.VictoryTargets.Add(cap.Id);
                    AfterEdit("击败指定怪物：" + (ev.VictoryTargets.Contains(cap.Id) ? "加上" : "去掉") + " " + cap.Display);
                };
                wrap.Children.Add(btn);
            }
            VictoryBox.Children.Add(wrap);
        }
    }
}
