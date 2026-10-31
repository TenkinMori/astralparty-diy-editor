using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;

namespace DiyEditor
{
    /// <summary>
    /// ★★ 真交互自查（`--clicktest [关卡.json]`）—— 专门抓"**看着能点、点了没反应**"这一类毛病。
    ///
    /// 为什么 `--selftest` 抓不到：那条路不建窗口、不进布局、不碰视觉树；
    ///   "按下的那一瞬间页面被整棵重建 ⇒ 正被按着的那颗按钮被换掉 ⇒ 鼠标抬起没了落点"
    ///   这种毛病在纯函数层根本不存在。
    ///
    /// 这条路做的事：
    ///   ① **真窗口**：主窗口 `Show()` 出来，但摆到**屏幕外面**（`ShowActivated=false`、
    ///      不进任务栏）⇒ 屏幕上什么都看不到，可 `IsVisible` 是真的，
    ///      命中测试、布局、模板全都是真的（不 Show 的话 WPF 把所有元素当不可见，命中测试一律落空）；
    ///   ② **真命中测试**：控件正中间那一点，最上层是不是它自己
    ///      （证明没被盖住 / 没被禁用 / 没被裁掉 / 没被 `IsHitTestVisible=false`）；
    ///   ③ **真按一下**：先让焦点离开输入框（= WPF `ButtonBase.OnMouseLeftButtonDown` 里
    ///      `Focus(这颗按钮)` 造成的那一下 `LostFocus`；本自查不抢键盘焦点，
    ///      所以这一步用**同一个 LostFocus 事件**喂进去，处理器链是真的），再发 `Click`；
    ///   ④ **断言"点了有反应"**：数据变了 / 编辑区出来了 / 二级窗口真被建出来了。
    /// </summary>
    public static class ClickTest
    {
        private const double W = 1440;
        private const double H = 1000;

        private static readonly StringBuilder Log = new StringBuilder();
        private static int _pass;
        private static int _fail;

        /// <summary>模拟"键盘焦点现在在哪个输入框里"（本自查不激活窗口，拿不到真键盘焦点）。</summary>
        private static TextBox _focused;

        // ================================================================ ★★ 看门狗（别让自查永远挂着）
        /// <summary>最近一次"有进展"的时刻（`Say` / `Check` 都会刷新它）。</summary>
        private static DateTime _progress = DateTime.Now;

        /// <summary>多久没进展就判定"卡住了"（秒）。整条自查正常跑完只要几秒。</summary>
        private const double StaleSeconds = 240;

        /// <summary>看门狗（后台线程）：卡住的时候**自己了断**并写出日志，别让 `--clicktest` 永远挂着。</summary>
        private static System.Threading.Timer _dog;

        /// <summary>
        /// ★★ 为什么要看门狗：`--clicktest` 曾经卡死过一次（用户当场报的"跑了 7 分钟没输出，只能杀进程"）——
        ///   根因是点了会**真弹模态框**的按钮（`PageStory.Ask()` 里 `PickDialog == null` ⇒ `ShowDialog()`），
        ///   而离屏自查没有真人去点它 ⇒ 永远等下去。
        ///   ⇒ 这次两头都堵：① 凡是要弹窗口的点击，一律先挂 `PageStory.PickDialog`（见 `TapDialog`）；
        ///     ② 万一还有漏网的，看门狗在 `StaleSeconds` 秒后写日志 + **退出码 3**（= 卡住了，不是断言失败），
        ///        不再让人干等 / 手工杀进程。
        /// </summary>
        private static void StartWatchdog()
        {
            _progress = DateTime.Now;
            _dog = new System.Threading.Timer(delegate
            {
                if ((DateTime.Now - _progress).TotalSeconds < StaleSeconds) return;
                try
                {
                    Log.AppendLine("");
                    Log.AppendLine("★ 看门狗：超过 " + StaleSeconds.ToString("0") + " 秒没有任何进展 ⇒ 判定**卡住**了"
                        + "（多半是弹了个真模态框没人点）。退出码 3。");
                }
                catch (Exception) { }
                try { WriteLog(); } catch (Exception) { }
                try { Environment.Exit(3); } catch (Exception) { }
            }, null, 5000, 5000);
        }

        private static void StopWatchdog()
        {
            try { if (_dog != null) _dog.Dispose(); } catch (Exception) { }
            _dog = null;
        }

        private static void Say(string s)
        {
            _progress = DateTime.Now;
            Console.WriteLine(s);
            Log.AppendLine(s);
        }

        private static void Check(string what, bool ok, string detail)
        {
            _progress = DateTime.Now;
            if (ok) { _pass++; Say("  【通过】 " + what + (detail.Length > 0 ? " —— " + detail : "")); }
            else { _fail++; Say("  【失败】 " + what + " —— " + detail); }
        }

        private static void Title(string s) { Say(""); Say("──────── " + s); }

        /// <summary>从探针输出里挑出以某个序号开头的那一行（找不到回空串）。</summary>
        private static string Find(List<string> lines, string prefix)
        {
            foreach (string one in lines)
                if (one.TrimStart().StartsWith(prefix, StringComparison.Ordinal)) return one.Trim();
            return "";
        }

        public static int Run(string docPath)
        {
            StartWatchdog();
            try { return RunInner(docPath); }
            finally { StopWatchdog(); }
        }

        private static int RunInner(string docPath)
        {
            try { Console.OutputEncoding = Encoding.UTF8; } catch (Exception) { }
            Say("★★ 真交互自查 --clicktest");
            Say("   真窗口 + 真布局 + 真命中测试；窗口摆到屏幕外、不激活、不进任务栏 ⇒ 屏幕上不会冒窗口；");
            Say("   只读你自己那份关卡的**副本**，不写任何关卡文件。");
            Say("   ★ 会弹二级窗口的点击一律先挂 PageStory.PickDialog（钩子），点完立刻恢复 null ⇒ 绝不会卡在模态框上；");
            Say("     万一还是卡住了，看门狗 " + StaleSeconds.ToString("0") + " 秒后写日志并以退出码 3 收场。");

            string copy = Path.Combine(Paths.Under("_自测"), "点击自测关卡.json");
            try
            {
                // ★★ 默认夹具 = `_截图\页面演示\DIY关卡\演示-开过进度.json`
                //   —— 它是 **新扁平格式**（`progress_events.<事件键>` + `diy.board_monsters` + 顶层
                //      `victory` / `progress.limit`），由 `--demo-edit progress` 跑出来的，不是手抄的：
                //      生成器在 `DemoEdit.Progress`（改那份再跑一次就能重生成）。
                //   ★ 旁边那份 `演示-新建后.json` 是 **历史留档**（2026-10-03「刚新建出来」的样子，
                //     只当 `--demo-edit` 的底稿和截图底版用）：别拿它当夹具、也别为了它改断言。
                string src = !string.IsNullOrEmpty(docPath) && File.Exists(docPath)
                    ? docPath
                    : Path.Combine(Paths.Own(), @"_截图\页面演示\DIY关卡\演示-开过进度.json");
                if (!File.Exists(src)) { Say("★ 找不到要打开的关卡：" + src); return 2; }
                File.Copy(src, copy, true);
                Say("   关卡副本：" + copy);
            }
            catch (Exception ex) { Say("★ 准备副本失败：" + ex.Message); return 2; }

            try
            {
                return Body(copy);
            }
            catch (Exception ex)
            {
                Say("★ 自查本身抛异常：" + ex);
                WriteLog();
                return 1;
            }
        }

        private static int Body(string copy)
        {
            var win = new MainWindow();
            Application.Current.MainWindow = win;
            MainWindow.Doc = LevelDoc.Open(copy);

            // ★ 真窗口，但摆到屏幕外：屏幕上不会看到它，也不抢键盘焦点。
            win.WindowStartupLocation = WindowStartupLocation.Manual;
            win.Left = SystemParameters.VirtualScreenLeft - 3000;
            win.Top = SystemParameters.VirtualScreenTop - 3000;
            win.Width = W;
            win.Height = H;
            win.ShowActivated = false;
            win.ShowInTaskbar = false;
            win.Show();
            Pump(win.Content as FrameworkElement);

            var root = win.Content as FrameworkElement;
            // ★★ 预热：把"窗口刚 Show / 冷启动"那一轮推迟的重建**全部跑完**再开始断言。
            //   不计分、不缩短任何断言，只是把"上一帧"变成"这一帧"——
            //   `Settle` 会一直跑到"尺寸 + 控件数"连续两轮都不变为止。
            var warm = System.Diagnostics.Stopwatch.StartNew();
            Settle(root, 12);
            int warmElements = VisualCount(root);
            win.GoPage(3);
            Settle(root, 12);
            Say("   预热：布局收敛用时 " + warm.ElapsedMilliseconds + " ms · 收敛时视觉树 "
                + warmElements + " 个元素 ⇒ " + VisualCount(root) + " 个（进度页）");

            PageStory story = win.PageStory;
            LevelDoc doc = MainWindow.Doc;
            DiyData d = doc.Diy;
            if (story == null || d.Events.Count == 0) { Say("★ 进度页 / 进度事件没拿到"); WriteLog(); return 1; }

            Say("   窗口：Left=" + win.Left.ToString("0") + "（屏幕外）· IsVisible=" + win.IsVisible
                + " · IsActive=" + win.IsActive);
            Say("   关卡：" + doc.FileName + " · 事件 " + d.Events.Count + " 个（" + EventIds(d) + "）· 怪物池 "
                + d.Pool.Count + " 只");

            // ================================================================ §B ★★ 棋盘：真命中 + 三条点击路径
            // ★★ 2026-10-10 两次 P1 回归（"棋盘点不动了"）都出在这一页，而**旧的自测一条都没抓到**：
            //   ① 悬停触发重建 ⇒ 死循环 ⇒ 真鼠标点击送不到；
            //   ② 去掉"色块底"后格子上没有可命中表面 ⇒ 左键落到容器 ⇒ 变成拖动平移。
            //   两次都是"程序化调用绕开了真实命中/进出链路"才漏掉的。
            //   ⇒ 这一段专门**先真命中测试、再派事件**，把这两条钉死。
            Title("§B ★★ 棋盘：每一格真命中测试 + 左键/右键/空白处平移三条路径");
            try
            {
                win.GoPage(2);
                Settle(root, 12);
                var pb = win.PageBoard;
                // ★ 棋盘页要等它自己"适应窗口"那一趟跑完才有格子
                //   （`Draw()` 在宿主还没量出尺寸时直接 return）。
                for (int k = 0; k < 8 && pb != null && pb.CellCount == 0; k++)
                {
                    Pump(root);
                    pb.Reload();
                    Settle(root, 12);
                }
                if (pb == null)
                {
                    Check("ⓑ0 棋盘页拿得到", false, "PageBoard == null");
                }
                else if (pb.CellCount == 0)
                {
                    // ★ 这份夹具**没有棋盘数据**（格子一个都没画）⇒ 命中/点击无从验起。
                    //   要点名报失败，绝不能悄悄放过 —— 这几条正是为棋盘页 P1 回归专设的闸门。
                    //   在**项目目录**里跑就正常（夹具旁边有 `棋盘缓存\<画板号>.json`）；
                    //   只有"把 exe 拷到够不到棋盘缓存的地方"才会走到这一支。
                    Check("ⓑ1 ★★ 棋盘真命中/点击三条路径（需要带棋盘的关卡；这份夹具没有棋盘数据）",
                        false, "格 0 个 ⇒ 无法验证。请用带棋盘的关卡跑 --clicktest。");
                }
                else
                {
                    Say("   棋盘就绪：格 " + pb.CellCount + " 个");
                    List<string> pr = pb.RunHitProbe();
                    foreach (string one in pr) Say("    " + one.Trim());

                    string l1 = Find(pr, "①");
                    int slash = l1.IndexOf('/');
                    bool hitAll = l1.IndexOf("★不合格", StringComparison.Ordinal) < 0 && l1.IndexOf("42/42") >= 0;
                    Check("ⓑ1 ★★ 每一格中心都真命中得到（42/42；命中元素能上溯到那一格的 Grid）"
                        + " —— 上一版格子上没有可命中表面，这条会红", hitAll, l1);

                    string l2 = Find(pr, "②");
                    bool clickOk = l2.IndexOf("★不合格", StringComparison.Ordinal) < 0
                        && l2.IndexOf("选中成功 42/42") >= 0 && l2.IndexOf("误启动平移 0") >= 0;
                    Check("ⓑ2 ★★ 左键点格子：42/42 都选中、且**一次都没误启动拖动平移**"
                        + "（用户看到的「拖动光标」就是这里坏的）", clickOk, l2);

                    string l3 = Find(pr, "③");
                    bool panOk = l3.IndexOf("启动平移 = True", StringComparison.Ordinal) >= 0;
                    Check("ⓑ3 ★ 空白处左键**仍然**能拖动平移（别把平移修坏了）", panOk, l3);

                    string l4 = Find(pr, "④");
                    bool ctxOk = l4.IndexOf("★不合格", StringComparison.Ordinal) < 0
                        && l4.IndexOf("菜单就绪 42/42") >= 0;
                    Check("ⓑ4 ★ 右键点格子：42/42 都能把右键菜单准备好", ctxOk, l4);

                    // ★ 悬停绝不重建（当年那个死循环）
                    int b = PageBoard.DrawCount;
                    int acts = pb.RunHoverProbe(false);
                    int dnew = PageBoard.DrawCount - b;
                    Check("ⓑ5 ★★ 悬停（" + acts + " 次进/出真事件）**一次重建都不许有**"
                        + " —— 上一版就是「悬停→重建→MouseLeave→再重建」的死循环把点击卡死的",
                        dnew == 0, "探针期间 Draw 新增 " + dnew + " 次");
                }
            }
            catch (Exception ex)
            {
                Check("ⓑ 棋盘命中/点击探针不抛异常", false, ex.GetType().Name + "：" + ex.Message);
            }
            win.GoPage(3);
            Settle(root, 12);

            // ================================================================ §0 页面本身健康
            Title("§0 页面本身：画出来了没有 / 有没有被盖住 / 重建会不会抛异常");
            try
            {
                story.Reload();
                Check("① 进度页重建（Reload）不抛异常", true,
                    "（GoPage 里那层 catch 会把它咽掉，这里单独量一次）");
            }
            catch (Exception ex)
            {
                Check("① 进度页重建（Reload）不抛异常", false, ex.GetType().Name + "：" + ex.Message);
            }
            Layout(root);

            Check("② 主窗口没被禁用（不是「被卡住的模态窗」把整窗按住）",
                win.IsEnabled && story.IsEnabled,
                "win.IsEnabled = " + win.IsEnabled + " · story.IsEnabled = " + story.IsEnabled);
            Check("③ 进度这一页真的可见（不是被藏起来 / 高度塌成 0）",
                story.IsVisible && story.ActualHeight > 100 && story.ActualWidth > 400,
                "IsVisible = " + story.IsVisible + " · 尺寸 "
                + story.ActualWidth.ToString("0") + "x" + story.ActualHeight.ToString("0"));

            string why2;
            Check("④ 界面跟数据对得上（② 的事件数 / ③ 的条词行数 / 输入框数）",
                LooksRight(story, d, out why2), why2);

            // ★ 能点的地方：① 上限框 / 事件下拉 · ③「＋ 新增事件」/ 触发进度框 / 挑官方事件
            //   · ② 的事件行 · ③ 的 7 颗「＋…」· 每条条词上的按钮 · ④ 的勾选框 + 目标按钮
            var targets = new List<FrameworkElement>();
            targets.Add(story.LimitBox);                       // ① 上限输入框
            targets.Add(story.EventPick);                      // 进度事件下拉框
            targets.Add(story.BtnAddItem);                     // 「＋ 新增事件」
            targets.Add(story.TriggerBox);                     // 触发进度输入框
            targets.Add(story.BtnTriggerPick);                 // 「挑官方事件…」
            foreach (UIElement u in story.ItemList.Children) if (u is FrameworkElement) targets.Add((FrameworkElement)u);
            foreach (Button b in Buttons(story.AddEffRow)) targets.Add(b);
            foreach (Button b in Buttons(story.EffList)) targets.Add(b);
            int greyed = 0;
            foreach (UIElement u in story.VictoryBox.Children)
            {
                // ★ 只收"真能点的控件"（按钮 / 勾选框）：
                //   空白面板正中间本来就是什么都没有，点到它不算"点不动"；
                //   ④ 里那些**按规格该是灰的**目标按钮也不收（没勾「击败指定怪物」时它们本来就点不动，
                //   见下面第 ⑥ 条 —— 勾上之后 §10④ 会专门验它们能点）。
                foreach (Button b in Buttons(u)) { if (b.IsEnabled) targets.Add(b); else greyed++; }
                foreach (CheckBox c in Checks(u)) if (c.IsEnabled) targets.Add(c);
            }
            var uniq = new List<FrameworkElement>();
            foreach (FrameworkElement el in targets)
                if (el != null && el.ActualWidth >= 0.5 && !uniq.Contains(el)) uniq.Add(el);

            int hit = 0;
            var missed = new List<string>();
            foreach (FrameworkElement el in uniq)
            {
                string how = HitAtCenter(root, el);
                if (how.StartsWith("★")) missed.Add(Brief(el) + " ⇒ " + how);
                else hit++;
            }
            Check("⑤ 每个能点的地方，正中间命中的都是它自己（没被盖住 / 没被禁用 / 没被裁掉）",
                missed.Count == 0 && hit >= 10,
                hit + " / " + uniq.Count + " 个命中（① 上限框 · 事件下拉 · ＋新增事件 · 触发进度框 · 挑官方事件 · "
                + "② 的事件行 · ③ 的 7 颗「＋…」· 条词按钮 · ④ 勾选框/目标按钮）"
                + (missed.Count == 0 ? "" : "；★ 落空的：" + string.Join(" | ", missed.ToArray())));

            DiyEvent curEv0 = CurEvent(story, d);
            bool wantTgt = curEv0 != null && curEv0.VictoryKinds.Contains(DiyVictory.TypeKillMonsters);
            Check("⑥ ★ ④ 里的目标按钮：没勾「击败指定怪物」时按规格就是灰的（所以不参与上面那次命中测试）",
                wantTgt ? greyed == 0 : (greyed > 0 || Buttons(story.VictoryBox).Count == 0),
                "勾着「击败指定怪物」= " + wantTgt + " · 灰掉的 " + greyed + " 颗 / 目标按钮 "
                + Buttons(story.VictoryBox).Count + " 颗 / 池子 " + d.Pool.Count + " 只");

            // ================================================================ §1 病根
            Title("§1 病根：按下的那一瞬间，页面会不会被整棵重建（正被按着的那颗按钮被换掉）");
            Say("   真人按下鼠标，WPF 的 ButtonBase 先 Focus(这颗按钮) 再发 Click；");
            Say("   这一下会把「① 上限」输入框的焦点拿走 ⇒ LostFocus ⇒ OnLimitChanged ⇒ SaveDiy + RefreshAll。");

            UIElement probeList = story.ItemList.Children.Count > 0 ? story.ItemList.Children[0] : null;
            List<Button> effBtns = Buttons(story.EffList);
            Button probeEff = effBtns.Count > 0 ? effBtns[0] : null;
            List<Button> addBtns = Buttons(story.AddEffRow);
            Button probeAdd = addBtns.Count > 0 ? addBtns[0] : null;

            _focused = story.LimitBox;      // 模拟"用户刚在「① 上限」里点过一下"，焦点还在框里
            long limitBefore = curEv0 == null ? -1 : curEv0.Limit;
            // ★ 没写上限时（-1）框里填 5 —— 断言要跟着这个"填进去的那个值"走，不能一律按 +5 算
            long wantLimit = limitBefore >= 0 ? limitBefore + 5 : 5;
            story.LimitBox.Text = wantLimit.ToString(CultureInfo.InvariantCulture);

            MoveFocusAway();                // ← 焦点离开输入框，就是真人按键钮时 WPF 干的那一下

            bool listAlive = probeList != null && story.ItemList.Children.Count > 0
                && ReferenceEquals(probeList, story.ItemList.Children[0]);
            bool effAlive = probeEff != null && IsInside(probeEff, story);
            bool addAlive = probeAdd != null && IsInside(probeAdd, story);
            Check("① 失焦那一刷**不在当场**把正被按着的清单按钮换掉", listAlive,
                listAlive ? "清单第 1 颗还是原来那个对象（按下去它不会凭空消失）"
                          : "★ 按下的瞬间它已被换掉 ⇒ 抬起没落点 ⇒ 点了没反应");
            Check("② 条词行里的按钮同样当场还活着", effAlive,
                effAlive ? "「" + Brief(probeEff) + "」还在树上" : "★ 当场被换掉");
            Check("③ 「＋ 添加条词」那排按钮同样当场还活着", addAlive,
                addAlive ? "「" + Brief(probeAdd) + "」还在树上" : "★ 当场被换掉");
            Check("④ 数据是当场就落地的（只有「重建」被推迟，改动本身不推迟）",
                curEv0 != null && curEv0.Limit == wantLimit,
                "进度上限 " + limitBefore + " ⇒ " + (curEv0 == null ? -1 : curEv0.Limit)
                + "（框里填的是 " + wantLimit + "）");

            Pump(root);
            bool rebuilt = !ReferenceEquals(probeList, story.ItemList.Children.Count > 0 ? story.ItemList.Children[0] : null);
            Check("⑤ 推迟之后**确实**重建了（「实时刷新」这个功能没被牺牲）", rebuilt,
                rebuilt ? "消息泵一空，清单已经换成新画的那一批（对象身份变了）"
                        : "★ 一直没重建 ⇒ 刷新被吞了，那就成了为了好点把刷新删掉");

            // ================================================================ §2 ③ 「＋ 新增事件」
            Title("§2 ③ 点「＋ 新增事件」⇒ 挑一条官方事件当触发点"
                + "（★ 这里一定会弹二级窗口，钩子必须先挂好，否则自查就卡死在这儿）");
            int events0 = d.Events.Count;
            Window dlgAdd = null;
            string dlgAddText = "";
            // ① 先"取消"一次：窗口真造出来了、但清单里一个事件都不该多
            TapDialog(story, root, story.BtnAddItem, "① 点「＋ 新增事件」（这次点取消）", "21",
                delegate (Window dlg)
                {
                    dlgAdd = dlg;
                    dlgAddText = TextOfLogical((DependencyObject)dlg.Content);
                    return false;
                });
            Layout(root);
            Check("② 真造出「挑官方事件」二级窗口（不是卡在那儿等人点）",
                dlgAdd is TriggerPickDialog && LogicalCount(dlgAdd.Content as DependencyObject) >= 40,
                dlgAdd == null ? "★ 处理器没走到弹窗那一步"
                               : (dlgAdd.GetType().Name + "「" + dlgAdd.Title + "」· 逻辑元素 "
                                  + LogicalCount(dlgAdd.Content as DependencyObject) + " 个"));
            Check("③ 窗口里列的是官方事件（标签「触发进度」+ 事件 id 都在）",
                dlgAddText.IndexOf("触发进度", StringComparison.Ordinal) >= 0
                && dlgAddText.IndexOf("32001", StringComparison.Ordinal) >= 0,
                Clip(dlgAddText, 140));
            Check("④ 点了取消 ⇒ 一个事件都没多加（不写半条进去）",
                d.Events.Count == events0, events0 + " ⇒ " + d.Events.Count);

            // ② 再点一次，这次真挑一条（挑「触发进度 4」那一档，好跟后面 §12 填 3 区分开）
            List<MapEventRow> p4 = MapEventTable.RowsAtProgress(4);
            MapEventRow wantRow = p4.Count > 0 ? p4[0]
                : (MapEventTable.Rows().Count > 0 ? MapEventTable.Rows()[0] : null);
            TapDialog(story, root, story.BtnAddItem, "⑤ 再点一次，这次挑一条官方事件", "22",
                delegate (Window dlg) { ForceField(dlg, "_pick", wantRow); return true; });
            Layout(root);
            DiyEvent added = wantRow == null ? null
                : d.Events.Find(delegate (DiyEvent e) { return e.Id == wantRow.Id; });
            Check("⑥ 事件 +1，且新事件的 id = 挑的那条官方事件 id（扁平模型：事件 id 就是触发点）",
                wantRow != null && d.Events.Count == events0 + 1 && added != null,
                events0 + " ⇒ " + d.Events.Count
                + (added == null ? "（★ 没找到 " + (wantRow == null ? -1 : wantRow.Id) + "）"
                                 : "（新事件 " + added.Id + "「" + added.Name + "」）"));
            Check("⑦ 新事件自带一条进度，触发点 = 那条官方事件的触发进度",
                added != null && added.Items.Count == 1 && added.Items[0].TriggerEvent == added.Id
                && added.Items[0].TriggerProgress == (wantRow == null ? -2 : wantRow.Progress),
                added == null || added.Items.Count == 0 ? "（没条目）"
                    : ("条目 n=" + added.Items[0].N + " · TriggerProgress=" + added.Items[0].TriggerProgress
                       + " · TriggerEvent=" + added.Items[0].TriggerEvent
                       + "（要的是 " + (wantRow == null ? -2 : wantRow.Progress) + "）"));
            Check("⑧ ② 的清单跟着多一行（清单 = 整关的事件，不再是「这个事件的进度条目」）",
                story.ItemList.Children.Count == d.Events.Count,
                "清单 " + story.ItemList.Children.Count + " 颗 / 事件 " + d.Events.Count + " 个");
            Check("⑨ 提示语说了「新建事件」", TextOf(story.EditNote).Contains("新建事件"),
                "EditNote = " + Clip(TextOf(story.EditNote), 130));

            // ================================================================ §3 ② 点清单里某一条（切事件）
            Title("§3 ② 点清单里某一条 ⇒ 上面下拉 + 下面 ③ 都换成它（清单 = 整关的事件）");
            DiyEvent demoEv = d.Events[0];             // 列表按 id 升序，点第一个最稳
            Tap(story, root, ButtonWithText(story.ItemList, "事件 " + demoEv.Id), "① 点清单里那个事件", "23");
            Layout(root);
            DiyEvent sel3 = CurEvent(story, d);
            Check("② 选中的事件真的切过去了（下拉 + ③ 都跟着换）",
                sel3 != null && sel3.Id == demoEv.Id,
                "选中 = " + (sel3 == null ? -1 : sel3.Id) + "（点的是事件 " + demoEv.Id + "）");
            Check("③ 上面那行说明也换了（「事件 <id> · 名字」）",
                TextOf(story.EventNote).IndexOf("事件 " + demoEv.Id, StringComparison.Ordinal) >= 0,
                "EventNote = " + Clip(TextOf(story.EventNote), 120));
            Check("④ ③ 的编辑区展开着，编的就是这个事件的那一条进度",
                story.EffPanel.Visibility == Visibility.Visible
                && PickN(story) == (demoEv.Items.Count > 0 ? demoEv.Items[0].N : 0),
                "EffPanel = " + story.EffPanel.Visibility + " · _pickN = " + PickN(story)
                + "（这个事件的第一条是 n=" + (demoEv.Items.Count > 0 ? demoEv.Items[0].N : 0) + "）");
            Button row3now = ButtonWithText(story.ItemList, "事件 " + demoEv.Id);
            Check("⑤ 选中态跟着走（那颗按钮换成 Primary 样式）",
                row3now != null && row3now.Style == (Style)story.FindResource("Primary"),
                "样式 = " + (row3now == null ? "(没找到)" : StyleName(row3now, story)));

            // ★★ 2026-10-10 修的可读性 bug + 它的守卫：
            //   选中那行用的是 `Primary` 样式 ⇒ **底色就是 Accent**，而"触发进度 N"原来前景色
            //   也写死 `Accent` ⇒ 浅青写在浅青上，选中行的那个数字**根本看不见**（实测截图里
            //   选中行右边是空的）。修法：选中行前景改用 `Primary` 自己的前景色（AccentDeep），
            //   未选中行**一个字都不改**（仍然 Accent / 没设触发点时 FgFaint）。
            Color selFg, otherFg;
            bool gotSel = RowTrigForeground(row3now, out selFg);
            DiyEvent otherEv = null;
            foreach (DiyEvent e2 in d.Events) if (e2.Id != demoEv.Id) { otherEv = e2; break; }
            Button rowOther = otherEv == null ? null : ButtonWithText(story.ItemList, "事件 " + otherEv.Id);
            bool gotOther = RowTrigForeground(rowOther, out otherFg);
            var accentBrush = (SolidColorBrush)story.FindResource("Accent");
            Color accentC = accentBrush.Color;
            Color deepC = ((SolidColorBrush)story.FindResource("AccentDeep")).Color;
            double selRatio = Contrast(selFg, accentC);
            Check("⑥ ★★ 选中那一行的「触发进度 N」看得见（行底色是 Accent ⇒ 前景不许再用 Accent，对比度要够）",
                gotSel && selFg != accentC && selRatio >= 4.5,
                "选中行前景 " + Hex(selFg) + " / 行底色 " + Hex(accentC)
                + " ⇒ 对比度 " + selRatio.ToString("0.0") + ":1（AccentDeep = " + Hex(deepC) + "）");
            bool otherHasTrig = otherEv != null && otherEv.Items.Count > 0 && otherEv.Items[0].TriggerProgress >= 0;
            Color wantOther = ((SolidColorBrush)story.FindResource(otherHasTrig ? "Accent" : "FgFaint")).Color;
            Check("⑦ ★ 未选中行的观感没变（有触发点 ⇒ Accent；没设触发点 ⇒ FgFaint，一个字都没改）",
                gotOther && otherFg == wantOther,
                "未选中行（事件 " + (otherEv == null ? -1 : otherEv.Id) + "，"
                + (otherHasTrig ? "有触发点" : "没设触发点") + "）前景 " + Hex(otherFg)
                + " / 要的是 " + Hex(wantOther));

            // ================================================================ §4 ③ ＋ 加条词（不用二级窗口的那几类）
            Title("§4 ③ 点「＋…」加条词：加扣血 / 发一次筹码（★ 第七类筹码是这次大改补的）");
            DiyProgressItem cur1 = Item(CurEvent(story, d), PickN(story));
            if (cur1 == null) { Check("① 当前选中条找得到", false, "PickN=" + PickN(story)); WriteLog(); return 1; }
            int eff0 = cur1.Effects.Count;
            Tap(story, root, ButtonWithText(story.AddEffRow, DiyEffect.KindName(EffectKind.Hp)), "① ＋ 加扣血", "24");
            Check("② 这一条的条词 +1", cur1.Effects.Count == eff0 + 1, eff0 + " ⇒ " + cur1.Effects.Count);
            Check("③ 编辑区跟着多出来一行", BorderRows(story.EffList) == cur1.Effects.Count,
                "行 " + BorderRows(story.EffList) + " / 条词 " + cur1.Effects.Count);
            Check("④ 标题里的条数也变了", TextOf(story.EditHead).Contains("（" + cur1.Effects.Count + " 条）"),
                "EditHead = " + TextOf(story.EditHead));
            Check("⑤ 提示语说了「加了一条条词」", TextOf(story.EditNote).Contains("加了一条条词"),
                "EditNote = " + Clip(TextOf(story.EditNote), 130));

            // ★ 第七类「发一次筹码」（2026-10-08 补的）：Kind = Relic、张数默认 3、默认给全队
            int effR = cur1.Effects.Count;
            Tap(story, root, ButtonWithText(story.AddEffRow, DiyEffect.KindName(EffectKind.Relic)), "⑥ ＋ 发一次筹码", "25");
            DiyEffect rel0 = cur1.Effects.Count > effR ? cur1.Effects[effR] : null;
            Check("⑦ 加出来的就是第七类「发一次筹码」（默认 张数 3 / 给全队）",
                rel0 != null && rel0.Kind == EffectKind.Relic && rel0.Choice == 3 && rel0.To == DiyEffect.ToTeam,
                rel0 == null ? "★ 条词没加上"
                             : ("Kind=" + rel0.Kind + " · Choice=" + rel0.Choice + " · To=" + rel0.To));

            // ================================================================ §5 ③ 二级窗口：发牌 / 挑怪物
            Title("§5 ③ 点「＋ 给所有人发一张牌」/「＋ 新增怪物」⇒ 二级窗口真造出来 + 挑完真落地");
            // ① 发牌：先"取消"一次 —— 窗口造出来了，但条词不该加
            int effC = cur1.Effects.Count;
            Window cardDlg = null;
            TapDialog(story, root, ButtonWithText(story.AddEffRow, DiyEffect.KindName(EffectKind.Card)),
                "① ＋ 发一张牌（这次点取消）", "26",
                delegate (Window dlg) { cardDlg = dlg; return false; });
            Check("② 二级窗口（选手牌）真被建出来了", cardDlg is CardPickDialog,
                cardDlg == null ? "★ 处理器没走到弹窗那一步"
                                : (cardDlg.GetType().Name + "「" + cardDlg.Title + "」· 逻辑元素 "
                                   + LogicalCount(cardDlg) + " 个 · 内容 "
                                   + (cardDlg.Content != null ? "有" : "★ 没有")));
            Check("③ 点了取消 ⇒ 条词没加（不写半条进去）", cur1.Effects.Count == effC,
                effC + " ⇒ " + cur1.Effects.Count);

            // ② 再点一次，这次真挑一张牌
            List<CardRow> allCards = GameTables.Cards();
            CardRow wantCard = allCards.Count > 0 ? allCards[0] : null;
            TapDialog(story, root, ButtonWithText(story.AddEffRow, DiyEffect.KindName(EffectKind.Card)),
                "④ ＋ 发一张牌（这次挑一张）", "27",
                delegate (Window dlg) { ForceField(dlg, "_pick", wantCard); return true; });
            int cardIdx = -1;
            for (int k = 0; k < cur1.Effects.Count; k++) if (cur1.Effects[k].Kind == EffectKind.Card) cardIdx = k;
            Check("⑤ 挑中的那张牌真进了条词（二级窗口 → 数据，整条链路通）",
                wantCard != null && cardIdx >= 0 && cur1.Effects[cardIdx].Card == wantCard.Id,
                wantCard == null ? "（表里一张牌都没有）"
                                 : ("要的是 " + wantCard.Id + " " + wantCard.Name + " ⇒ "
                                    + (cardIdx < 0 ? "★ 条词没加上"
                                                   : cur1.Effects[cardIdx].Card.ToString(CultureInfo.InvariantCulture))));

            // ③ 改一条**已经加进去**的发牌条词（「选手牌…」那条链路）—— 换成另一张
            // ★ 要点的必须是**被测的那一行**：夹具本身就是新扁平格式、里面自带一条发牌条词，
            //   所以 `EffList` 里可能有两颗「选手牌…」按钮（夹具那条 + 刚挑进来的那条）——
            //   不按行找就会点到夹具那一行，测出来的东西和断言的不是同一条条词。
            CardRow otherCard = null;
            int cardBefore = cardIdx < 0 ? -1 : cur1.Effects[cardIdx].Card;
            if (cardIdx >= 0)
                foreach (CardRow c in allCards) if (c.Id != cardBefore) { otherCard = c; break; }
            Border rowCard = RowAt(story.EffList, cardIdx);
            TapDialog(story, root, ButtonWithText(rowCard, "选手牌"),
                "⑥ 点条词里的「选手牌…」换一张", "28",
                delegate (Window dlg) { ForceField(dlg, "_pick", otherCard); return true; });
            Check("⑦ 换牌真落地（改一条已经加进去的条词走的是同一条链路）",
                otherCard != null && cardIdx >= 0 && cur1.Effects[cardIdx].Card == otherCard.Id,
                cardBefore + " ⇒ " + (cardIdx < 0 ? -1 : cur1.Effects[cardIdx].Card)
                + (otherCard == null ? "（表里只有一张牌，没法换）" : "（要的是 " + otherCard.Id + "）"));

            // ④ 挑怪物：真从池子里挑一只（§6 还要用它验「数量」框）
            // ★ 2026-10-08 大改之后：**每个事件对每只怪只留一条**刷怪条词 —— 再挑同一只走的是
            //   「数量 +1」那条路（条数不变），验不到"挑完真落地"。所以这里挑一只**这一条里还没有的**
            //   怪，才量得到"挑中的那只真进了条词"（挑不到没被用过的就退回第一只，行为跟老版一样）。
            int effS = cur1.Effects.Count;
            PoolMonster wantMon = null;
            foreach (PoolMonster p in d.Pool)
            {
                bool used = false;
                foreach (DiyEffect ex in cur1.Effects)
                    if (ex.Kind == EffectKind.Spawn && ex.Pool == p.Id) used = true;
                if (!used) { wantMon = p; break; }
            }
            if (wantMon == null && d.Pool.Count > 0) wantMon = d.Pool[0];
            Window monDlg = null;
            TapDialog(story, root, ButtonWithText(story.AddEffRow, DiyEffect.KindName(EffectKind.Spawn)),
                "⑧ ＋ 新增怪物（从池子里挑一只）", "29",
                delegate (Window dlg)
                {
                    monDlg = dlg;
                    ForceField(dlg, "_pick", wantMon == null ? null : wantMon.TableRow);
                    return true;
                });
            DiyEffect spNew = cur1.Effects.Count > effS ? cur1.Effects[effS] : null;
            Check("⑨ 挑中的那只怪真进了条词（Pool 指对了池内序号）",
                monDlg is MonsterPickDialog && spNew != null && spNew.Kind == EffectKind.Spawn
                && wantMon != null && spNew.Pool == wantMon.Id,
                "窗口 = " + (monDlg == null ? "★ 没造出来" : monDlg.GetType().Name)
                + " · " + (spNew == null ? "★ 条词没加上"
                                         : ("Pool " + spNew.Pool + "（要的是 #"
                                            + (wantMon == null ? -1 : wantMon.Id) + " "
                                            + (wantMon == null ? "" : wantMon.Name) + "）")));

            // ================================================================ §6 ★ 新控件：刷怪「数量」
            Title("§6 ★ 刷怪那条的「数量」框：改一下 ⇒ 这个事件刷几只跟着变");
            int spIdx = -1;
            for (int k = 0; k < cur1.Effects.Count; k++) if (cur1.Effects[k].Kind == EffectKind.Spawn) spIdx = k;
            if (spIdx < 0) { Check("① 编辑区里有一条「新增怪物」条词", false, "没找到"); }
            else
            {
                Border rowSp = RowAt(story.EffList, spIdx);
                TextBox boxSp = FirstOf(Boxes(rowSp));
                Check("① 那条条词上有「数量」框（这次大改新增的控件）",
                    boxSp != null && TextOf(rowSp).IndexOf("数量", StringComparison.Ordinal) >= 0,
                    rowSp == null ? "★ 没找到那一行"
                                  : ("框 = " + (boxSp == null ? "没有" : "有") + " · 行文字 = "
                                     + Clip(TextOf(rowSp), 90)));
                if (boxSp != null)
                {
                    int c0 = cur1.Effects[spIdx].Count;
                    _focused = boxSp;
                    boxSp.Text = "5";
                    MoveFocusAway();
                    Pump(root);
                    Check("② 改成 5 ⇒ 数据里的 Count 跟着变", cur1.Effects[spIdx].Count == 5,
                        c0 + " ⇒ " + cur1.Effects[spIdx].Count);
                    Border rowSp2 = RowAt(story.EffList, spIdx);
                    TextBox boxSp2 = FirstOf(Boxes(rowSp2));
                    Check("③ 重画之后框里写的还是 5（没被旧值盖回去）",
                        boxSp2 != null && (boxSp2.Text ?? "").Trim() == "5",
                        "框里 = " + (boxSp2 == null ? "(没有)" : boxSp2.Text)
                        + " / 数据 = " + cur1.Effects[spIdx].Count);
                }
            }

            // ================================================================ §7 ★ 新控件：筹码「张数」+ 奖励「给谁」
            Title("§7 ★ 「张数」框 + 「给谁」下拉（这次大改新增的两个控件）");
            int relIdx = -1;
            for (int k = 0; k < cur1.Effects.Count; k++) if (cur1.Effects[k].Kind == EffectKind.Relic) relIdx = k;
            if (relIdx < 0) { Check("① 编辑区里有一条「发一次筹码」条词", false, "没找到"); }
            else
            {
                Border rowRel = RowAt(story.EffList, relIdx);
                TextBox boxRel = FirstOf(Boxes(rowRel));
                ComboBox cbRel = FirstOf(Combos(rowRel));
                Check("① 筹码那条上「张数」框和「给谁」下拉都在",
                    boxRel != null && cbRel != null
                    && TextOf(rowRel).IndexOf("张数", StringComparison.Ordinal) >= 0
                    && TextOf(rowRel).IndexOf("给谁", StringComparison.Ordinal) >= 0,
                    rowRel == null ? "★ 没找到那一行"
                                   : ("张数框 = " + (boxRel == null ? "没有" : "有") + " · 给谁下拉 = "
                                      + (cbRel == null ? "没有" : "有") + " · 行文字 = " + Clip(TextOf(rowRel), 90)));
                if (boxRel != null)
                {
                    _focused = boxRel;
                    boxRel.Text = "2";
                    MoveFocusAway();
                    Pump(root);
                    Check("② 张数改成 2 ⇒ 数据里的 Choice 跟着变（三选一给几张）",
                        cur1.Effects[relIdx].Choice == 2, "Choice = " + cur1.Effects[relIdx].Choice);
                }
                if (cbRel != null)
                {
                    string to0 = cur1.Effects[relIdx].To;
                    Check("③ 「给谁」下拉里就是「全队 / 触发者」两项，选中项跟数据一致",
                        cbRel.Items.Count == 2
                        && string.Equals(cbRel.Items[0] as string, "全队", StringComparison.Ordinal)
                        && string.Equals(cbRel.Items[1] as string, "触发者", StringComparison.Ordinal)
                        && cbRel.SelectedIndex == (cur1.Effects[relIdx].To == DiyEffect.ToActor ? 1 : 0),
                        "项 = " + (cbRel.Items.Count > 1 ? (cbRel.Items[0] + "/" + cbRel.Items[1]) : "?")
                        + " · SelectedIndex = " + cbRel.SelectedIndex + " · To = " + to0);
                    cbRel.SelectedIndex = 1;                       // = 选「触发者」
                    Pump(root);
                    bool toActor = cur1.Effects[relIdx].To == DiyEffect.ToActor;
                    Border rowRel2 = RowAt(story.EffList, relIdx);
                    ComboBox cbRel2 = FirstOf(Combos(rowRel2));
                    if (cbRel2 == null) cbRel2 = cbRel;
                    cbRel2.SelectedIndex = 0;                      // = 选回「全队」
                    Pump(root);
                    Check("④ 选「触发者」⇒ To = actor；再选回「全队」⇒ To = team",
                        toActor && cur1.Effects[relIdx].To == DiyEffect.ToTeam,
                        "选触发者之后 = " + (toActor ? "actor ✓" : "★ 没变（" + cur1.Effects[relIdx].To + "）")
                        + " · 选回全队之后 = " + cur1.Effects[relIdx].To);
                }
            }

            // ================================================================ §8 ⑤ 改输入框再 Tab 走
            Title("§8 ⑤ 把数值框改一下再 Tab 走（焦点一走不能当场被拆掉）");
            DiyProgressItem curNow = Item(CurEvent(story, d), PickN(story));
            int numIdx = -1;
            if (curNow != null)
                for (int k = 0; k < curNow.Effects.Count; k++)
                {
                    EffectKind kk = curNow.Effects[k].Kind;
                    // ★ 「数量」（Spawn）和「张数」（Relic）是另外两颗框，§6/§7 单独验；这里挑数值型那颗
                    if (kk != EffectKind.Card && kk != EffectKind.Spawn && kk != EffectKind.Relic) { numIdx = k; break; }
                }
            TextBox box0 = numIdx < 0 ? null : FirstOf(Boxes(RowAt(story.EffList, numIdx)));
            if (box0 == null || curNow == null || numIdx < 0)
            {
                Check("① 数值条词那一行有一个数值输入框", false,
                    "框 = " + (box0 == null ? "没找到" : "有") + " · 选中条 = "
                    + (curNow == null ? "没找到" : "第 " + curNow.N + " 条") + " · 数值条词下标 = " + numIdx);
            }
            else
            {
                long old = curNow.Effects[numIdx].N;
                _focused = box0;
                box0.Text = "7";
                MoveFocusAway();                      // ← 真人按 Tab：焦点离开这个框
                bool boxAlive = IsInside(box0, story);
                Pump(root);
                Check("① Tab 走的那一刻，这个输入框还没被拆掉（当场不重建）", boxAlive,
                    boxAlive ? "焦点离开时它还在树上" : "★ 焦点一走它就被换掉（键盘改完值会被整页重建冲掉）");
                Check("② 改的值真进了数据", curNow.Effects[numIdx].N == 7,
                    "N " + old + " ⇒ " + curNow.Effects[numIdx].N);
                TextBox box1 = FirstOf(Boxes(RowAt(story.EffList, numIdx)));
                Check("③ 重新画出来之后，框里写的还是新值（没被旧值盖回去）",
                    box1 != null && (box1.Text ?? "").Trim() == "7",
                    "框里 = " + (box1 == null ? "(没有)" : box1.Text) + " / 数据 = " + curNow.Effects[numIdx].N);
            }

            // ================================================================ §9 ④ 获胜条件
            Title("§9 ④ 获胜条件：勾选框点了有没有反应");
            CheckBox cb0 = FirstCheck(story.VictoryBox);
            if (cb0 == null) { Check("① 获胜条件里有勾选框", false, "没找到"); }
            else
            {
                string kind = (cb0.Content as string) ?? "";
                bool was = cb0.IsChecked == true;
                _focused = story.LimitBox;
                story.LimitBox.Text = "28";
                MoveFocusAway();
                cb0.IsChecked = !was;                       // 真人点勾选框：ToggleButton 先翻转，再发 Click
                cb0.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, cb0));
                Pump(root);
                DiyEvent ev9 = CurEvent(story, d);
                bool nowIn = ev9 != null && ev9.VictoryKinds.Contains(kind);
                Check("① 勾选框点下去有反应（数据里的获胜条件跟着变）", was != nowIn,
                    "「" + kind + "」勾选 " + was + " ⇒ " + cb0.IsChecked + " · 数据里 "
                    + (nowIn ? "在" : "不在"));
            }

            // ================================================================ §10 实时刷新还在
            Title("§10 「实时刷新」还在：进度页配的「新增怪物 → 池 #N」⇒ 怪物页分栏跟着变");
            DiyProgressItem it2 = Item(CurEvent(story, d), PickN(story));
            int refPool = -1;
            if (it2 != null) foreach (DiyEffect x in it2.Effects) if (x.Kind == EffectKind.Spawn) refPool = x.Pool;
            PoolMonster mm = DiyPool.Find(d.Pool, refPool);
            Check("① 数据层：这个事件刷的那只怪，归属重算成了这条进度的编号",
                mm != null && mm.ProgressN.HasValue && it2 != null && mm.ProgressN.Value == it2.N,
                mm == null ? "（池里没找到那只怪）"
                           : ("池 #" + mm.Id + " " + mm.Name + " ⇒ ProgressN = "
                              + (mm.ProgressN.HasValue
                                 ? mm.ProgressN.Value.ToString(CultureInfo.InvariantCulture) : "未配置")
                              + "（这条进度 N=" + (it2 == null ? -1 : it2.N) + "）"));

            win.GoPage(4);
            Layout(root);
            string monText = TextOf(win.PageMonsters.ListHost) + " " + TextOfLogical(win.PageMonsters.ListHost);
            // ★ 2026-10-08：怪物页的分栏口径从"条目编号（进度 N）"改成**事件**了
            //   （扁平模型下一个事件只有一条条目、编号恒为 1 ⇒ 以前所有事件挤在"进度 1"）。
            //   所以这里找的是「事件 <落盘键>」那一栏。
            int curKey = CurEvent(story, d) == null ? -1 : DiyPool.EventKeyOf(CurEvent(story, d));
            Check("② 切到怪物页：分栏里真的出现了「事件 <键>」这一栏（为了好点也**没**把刷新删掉）",
                curKey > 0 && monText.IndexOf("事件 " + curKey, StringComparison.Ordinal) >= 0,
                "怪物页分栏：" + Clip(monText, 140));
            win.GoPage(3);
            Layout(root);

            // ================================================================ §11 页面上剩下的按钮
            Title("§11 页面上剩下的按钮：条词里的「删除」、获胜条件里的目标按钮");
            DiyProgressItem curX = Item(CurEvent(story, d), PickN(story));
            if (curX == null) { Check("① 当前选中条找得到", false, "PickN=" + PickN(story)); }
            else
            {
                int n0 = curX.Effects.Count;
                Tap(story, root, ButtonWithText(story.EffList, "删除"), "① 点条词里的「删除」", "30");
                Check("② 那条条词真被删掉了", curX.Effects.Count == n0 - 1, n0 + " ⇒ " + curX.Effects.Count);
                Check("③ 编辑区跟着少一行、标题里的条数也变了",
                    BorderRows(story.EffList) == curX.Effects.Count
                    && TextOf(story.EditHead).Contains("（" + curX.Effects.Count + " 条）"),
                    "行 " + BorderRows(story.EffList) + " / 条词 " + curX.Effects.Count
                    + " · EditHead = " + TextOf(story.EditHead));
            }

            // 获胜条件里的目标按钮：先确认「击败指定怪物」是勾上的（不然按规格它们就该是灰的）
            CheckBox cbV = FirstCheck(story.VictoryBox);
            if (cbV == null) { Check("④ 获胜条件里有勾选框", false, "没找到"); }
            else
            {
                if (cbV.IsChecked != true)
                {
                    cbV.IsChecked = true;
                    cbV.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, cbV));
                    Pump(root);
                }
                Button tgt = null;
                foreach (Button b in Buttons(story.VictoryBox))
                    if (TextOf(b).Contains("#")) { tgt = b; break; }
                Check("④ 勾上「击败指定怪物」之后，池子里的目标按钮是可点的（没被灰掉）",
                    tgt != null && tgt.IsEnabled,
                    tgt == null ? "（没找到目标按钮）" : "「" + TextOf(tgt) + "」IsEnabled = " + tgt.IsEnabled);
                if (tgt != null)
                {
                    DiyEvent evT = CurEvent(story, d);
                    int beforeN = evT == null ? -1 : evT.VictoryTargets.Count;
                    Tap(story, root, tgt, "⑤ 点一个目标按钮", "31");
                    DiyEvent evT2 = CurEvent(story, d);
                    Check("⑥ 目标的选中数变了（点一下真算数）",
                        evT2 != null && evT2.VictoryTargets.Count != beforeN,
                        "目标 " + beforeN + " ⇒ " + (evT2 == null ? -1 : evT2.VictoryTargets.Count)
                        + "（" + string.Join(",", (evT2 == null ? new List<int>() : evT2.VictoryTargets)
                            .ConvertAll(delegate (int v) { return v.ToString(CultureInfo.InvariantCulture); }).ToArray()) + "）");
                }
            }

            // ================================================================ §12 ★★ 触发点
            Title("§12 ★★「这个事件在第几回合触发」：清单显示 · 编辑区可填可挑 · 真写进配置（★ 新契约：键 = 官方事件 id）");
            {
                // 作者写了"每回合 +1 进度"（换算成回合要用它；没写就只显示进度值）
                // ★ 注意 EnsureMember 建出来的是 Null 节点，要让 Member() 找得着子键必须先 SetKind(Object)
                JNode prog = DiyJson.SetKind(DiyJson.EnsureMember(doc.Root, "progress"), JKind.Object);
                DiyJson.SetInt(DiyJson.EnsureMember(prog, "start"), 0);
                DiyJson.SetInt(DiyJson.EnsureMember(prog, "per_round"), 1);

                // ★ 挑一个"还没设触发点、而且已经挂着一条「新增怪物」"的事件来填：
                //   有刷怪条词，progress_events 那一行才会有 spawn 可以验
                DiyEvent target = null;
                foreach (DiyEvent e in d.Events)
                    if (e.Items.Count > 0 && e.Items[0].TriggerEvent <= 0 && HasSpawn(e)) { target = e; break; }
                if (target == null)
                    foreach (DiyEvent e in d.Events)
                        if (e.Items.Count > 0 && HasSpawn(e)) { target = e; break; }
                if (target == null && d.Events.Count > 0) target = d.Events[0];

                Tap(story, root, ButtonWithText(story.ItemList, "事件 " + target.Id), "① 点清单里那个事件", "32");
                Layout(root);
                DiyProgressItem curT = Item(CurEvent(story, d), PickN(story));
                string list0 = ListText(story);
                Check("① 清单里每个事件都显示了触发点（还没设的写「触发点没设」）",
                    curT != null && list0.IndexOf("触发点没设", StringComparison.Ordinal) >= 0,
                    "清单：" + Clip(list0, 170));

                // ---- ② 在「触发进度 / 回合」里填 3（= 真人敲完 Tab 走那一下）
                story.TriggerBox.Text = "3";
                _focused = story.TriggerBox;
                MoveFocusAway();
                Pump(root);
                // ★ `MainWindow.RefreshAll()` 的整页重建是**推迟到 Background 优先级**做的
                //   （见 MainWindow.QueueRebuild）：多走一遍 Layout（含 Pump）保证清单已经重画完，
                //   再读清单那一行 —— 不然量到的是"上一版"的清单，会误报成"清单没跟着变"。
                Layout(root);
                DiyProgressItem after = Item(CurEvent(story, d), PickN(story));
                string note1 = NoteText(story.TriggerNote);
                // ★ 断言钉在**那个事件自己的那一行**上（不拿整页清单的文字碰运气）：
                //   清单第三格 = `DiyTrigger.Short(doc, 事件的第一条)`，扁平模型下一个事件就一条
                //   ⇒ 它必然写的就是刚填进去的那个触发进度。
                Button rowT = ButtonWithText(story.ItemList, "事件 " + target.Id);
                string rowText1 = rowT == null ? "（清单里没有事件 " + target.Id + " 那一行）" : TextOf(rowT);
                Check("② 填 3 ⇒ 反查官方事件并落定；编辑区那句 + 清单那一条都写着「触发进度 3（约第 3 回合）」",
                    after != null && after.TriggerProgress == 3 && after.TriggerEvent > 0
                    && note1.IndexOf("触发进度 3", StringComparison.Ordinal) >= 0
                    && note1.IndexOf("约第 3 回合", StringComparison.Ordinal) >= 0
                    && rowText1.IndexOf("触发进度 3（约第 3 回合）", StringComparison.Ordinal) >= 0,
                    "n=" + (after == null ? -1 : after.N) + " ⇒ 触发进度 " + (after == null ? -1 : after.TriggerProgress)
                    + " · 事件 id " + (after == null ? -1 : after.TriggerEvent)
                    + " ｜编辑区：" + Clip(note1, 100) + " ｜清单那一行：" + Clip(rowText1, 110));

                // ---- ③ ★ 落盘 json 里写的是什么（★ 2026-10-08 新契约）
                JNode pe = DiyJson.Member(doc.Root, "progress_events");
                JNode peRow = pe == null || after == null ? null
                    : DiyJson.Member(pe, after.TriggerEvent.ToString(CultureInfo.InvariantCulture));
                JNode peSpawn = peRow == null ? null : DiyJson.Member(peRow, "spawn");
                long tp = peRow == null ? -1 : DiyJson.IntOf(DiyJson.Member(peRow, DiyTrigger.KeyProgress), -1);
                JNode evBlock = DiyJson.Member(DiyJson.Member(DiyJson.Member(doc.Root, "diy"), "events"),
                    target.Id.ToString(CultureInfo.InvariantCulture));
                bool keyOk = peRow != null && peSpawn != null && peSpawn.Kind == JKind.Array && peSpawn.Items.Count > 0;
                bool oldGone = evBlock != null && DiyJson.Member(evBlock, "items") == null
                    && DiyJson.Member(evBlock, "progress_limit") == null
                    && DiyJson.Member(evBlock, "victory") == null;
                Check("③ ★ 落盘（新契约）：progress_events 的键 = 那条官方事件 id，行里有 trigger_progress=3 + spawn；"
                    + "diy.events.<id> 里**不再写**老结构（items / progress_limit / victory）",
                    tp == 3 && after != null && keyOk && oldGone,
                    "progress_events[" + (after == null ? -1 : after.TriggerEvent) + "] = " + DiyJson.Compact(peRow)
                    + " ｜diy.events." + target.Id + " = " + DiyJson.Compact(evBlock));

                // ---- ④ 填一个官方没有的触发点 ⇒ 明说 + 不改
                int noEv = -1;
                for (int p = 0; p <= 200; p++)
                    if (MapEventTable.RowsAtProgress(p).Count == 0) { noEv = p; break; }
                // ★ 用**此刻**正在编的那一条（跟页面同一个查法：当前事件 + `_pickN`）当基准 ——
                //   ② 里读到的那只 `after` 是上一次重建之后的快照；拿它跟"框里显示什么"比，
                //   一旦重建把选择/条目换了对象，就会误报成"框回退成空"。
                //   这里钉的是：**框里回退到的值 = 这一条自己写的值**，且数据一个字节没改。
                DiyProgressItem now4 = Item(CurEvent(story, d), PickN(story));
                int keepP = now4 == null ? -9 : now4.TriggerProgress;
                int keepE = now4 == null ? -9 : now4.TriggerEvent;
                story.TriggerBox.Text = noEv.ToString(CultureInfo.InvariantCulture);
                _focused = story.TriggerBox;
                MoveFocusAway();
                Pump(root);
                Layout(root);                      // 同上：先让推迟的重建跑完，再读框里写了什么
                // ★ 那条提示语是**推迟重建**里刷的（`DispatcherPriority.Background`），
                //   单跑一次 `Layout` 偶尔还停在上一帧 ⇒ 会偶发假红（实测 3 次里红 1 次）。
                //   这里**等它真的刷出来**再读（等到就立刻返回，不多等；等不到则由下面的断言判红）——
                //   ★ 判据一个字节没放宽，只是不再"抢跑"。
                int noEvWait = noEv;
                WaitFor(delegate { return NoteText(story.TriggerNote)
                    .IndexOf("官方没有触发进度 = " + noEvWait, StringComparison.Ordinal) >= 0; }, root, 25);
                DiyProgressItem after4 = Item(CurEvent(story, d), PickN(story));
                string noteNo = NoteText(story.TriggerNote);
                string wantBox = keepP >= 0 ? keepP.ToString(CultureInfo.InvariantCulture) : "";
                Check("④ ★ 填一个官方没有的触发点 ⇒ 界面上明说「官方没有触发进度 = N 的事件」，"
                    + "数据一个字节不改（框回退到原值）",
                    noEv >= 0 && noteNo.IndexOf("官方没有触发进度 = " + noEv, StringComparison.Ordinal) >= 0
                    && now4 != null && after4 != null && after4.N == now4.N
                    && after4.TriggerProgress == keepP && after4.TriggerEvent == keepE
                    && (story.TriggerBox.Text ?? "").Trim() == wantBox,
                    "填 " + noEv + " ｜" + Clip(noteNo, 120) + " ｜条目还是 触发进度 "
                    + (after4 == null ? -1 : after4.TriggerProgress) + " / 事件 " + (after4 == null ? -1 : after4.TriggerEvent)
                    + "（n=" + (after4 == null ? -1 : after4.N) + "，同一条=" + (now4 != null && after4 != null && after4.N == now4.N)
                    + "）｜框里回到「" + story.TriggerBox.Text + "」，这一条自己写的是「" + wantBox + "」");

                // ---- ⑤ 「挑官方事件…」真造二级窗口，列的就是官方那一档
                Window seen = null;
                string dlgText = "";
                TapDialog(story, root, story.BtnTriggerPick, "⑤ 点「挑官方事件…」", "33",
                    delegate (Window dlg)
                    {
                        seen = dlg;
                        dlgText = TextOfLogical((DependencyObject)dlg.Content);
                        return false;                                 // = 用户点了取消
                    });
                Check("⑤ ★ 点「挑官方事件…」真造出二级窗口，里面列的是官方事件（那一档、带 id 和名字）",
                    seen is TriggerPickDialog && LogicalCount(seen.Content as DependencyObject) >= 40
                    && dlgText.IndexOf("触发进度", StringComparison.Ordinal) >= 0
                    && dlgText.IndexOf("32001", StringComparison.Ordinal) >= 0
                    && dlgText.IndexOf("进度 3", StringComparison.Ordinal) >= 0,
                    seen == null ? "★ 窗口没造出来"
                                 : (seen.GetType().Name + "「" + seen.Title + "」· 逻辑元素 "
                                    + LogicalCount(seen.Content as DependencyObject) + " 个 · 文字里含 32001 = "
                                    + (dlgText.IndexOf("32001", StringComparison.Ordinal) >= 0)
                                    + " · 含「进度 3」= "
                                    + (dlgText.IndexOf("进度 3", StringComparison.Ordinal) >= 0)));
            }

            // ================================================================ 汇总
            Say("");
            // ★ 收尾自查：钩子必须已经还原（真人用时 PickDialog 必须是 null，否则走不到真 ShowDialog），
            //   主窗口也不能被"卡住的模态框"按住 —— 这两条就是"没有卡在模态框上"的证据。
            Check("★ 全程没卡在模态框上：二级窗口的钩子已还原 null，主窗口也没被禁用",
                PageStory.PickDialog == null && win.IsEnabled && story.IsEnabled,
                "PageStory.PickDialog = " + (PageStory.PickDialog == null ? "null ✓" : "★ 还挂着")
                + " · win.IsEnabled = " + win.IsEnabled + " · story.IsEnabled = " + story.IsEnabled);
            Say("════════════════════════════════════════");
            Say("真交互自查：通过 " + _pass + " 条，失败 " + _fail + " 条 ⇒ " + (_fail == 0 ? "全部通过" : "★ 有失败"));
            string logPath = Path.Combine(Paths.Under("_自测"), "点击自测输出.txt");
            WriteLog();
            Say("（明细已写出：" + logPath + "）");
            try { win.Close(); } catch (Exception) { }
            return _fail == 0 ? 0 : 1;
        }

        // ================================================================ 二级窗口的钩子
        /// <summary>
        /// ★★ 点"会弹二级窗口"的东西：**先挂钩子 → 按一下 → 无论如何都恢复 null**。
        ///
        ///   为什么非这样不可：`PageStory.Ask()` 在 `PickDialog == null` 时走的是**真 `ShowDialog()`**，
        ///   而离屏自查没有真人去点它 ⇒ **永远卡住**（用户 2026-10-08 报的就是这个：
        ///   `＋ 新增事件` 弹「挑官方事件」模态框，`--clicktest` 在那儿等了 7 分钟）。
        ///   ⇒ 凡是会弹窗口的点击必须走这个口子；`finally` 里恢复 null 是硬要求
        ///     （留着钩子的话，真人那条路就再也弹不出窗口了）。
        /// </summary>
        private static bool TapDialog(PageStory story, FrameworkElement root, Button b, string what, string seq,
                                      Func<Window, bool?> hook)
        {
            PageStory.PickDialog = hook;
            try { return Tap(story, root, b, what, seq); }
            finally { PageStory.PickDialog = null; }
        }

        // ================================================================ "按一下"
        /// <summary>
        /// 真人按一下鼠标：WPF 的 `ButtonBase.OnMouseLeftButtonDown` 先 `Focus(这颗按钮)`
        /// （这一下会让原来那个输入框吃到 `LostFocus`），`OnMouseLeftButtonUp` 才发 `Click`。
        /// 本自查为了不抢用户键盘焦点，不激活窗口 ⇒ 拿不到真键盘焦点，
        /// 所以"焦点离开输入框"这一步是用**同一个 `LostFocus` 事件**喂进去的（处理器链是真的）。
        /// ★ 关键断言：焦点一离开，这颗按钮**还在不在树上** ——
        ///   不在，就等于真人按下的那一瞬间它被换掉，鼠标抬起不会落到它身上，`Click` 永远不会发。
        /// 每次点之前都把「① 上限」改一个值 —— 这是真人用这一页时的常态（刚动过输入框）。
        /// </summary>
        private static bool Tap(PageStory story, FrameworkElement root, Button b, string what, string seq)
        {
            if (b == null)
            {
                Check(what + "：找得到那颗按钮", false, "★ 按钮没找到");
                return false;
            }
            _focused = story.LimitBox;
            story.LimitBox.Text = seq;
            MoveFocusAway();
            if (!IsInside(b, story))
            {
                Check(what + "：按得下去", false,
                    "★ 焦点一离开输入框它就被当场换掉 ⇒ 鼠标抬起没落点 ⇒ 点了没反应");
                Pump(root);
                return false;
            }
            b.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, b));
            Pump(root);
            Check(what + "：按得下去", true, "「" + Brief(b) + "」按下去时还在树上，Click 已送到处理器");
            return true;
        }

        /// <summary>焦点离开当前那个输入框 —— 就是真人按 Tab / 点别处时 WPF 发的那一下。</summary>
        private static void MoveFocusAway()
        {
            TextBox box = _focused;
            _focused = null;
            if (box != null) box.RaiseEvent(new RoutedEventArgs(UIElement.LostFocusEvent, box));
        }

        // ================================================================ 工具
        /// <summary>
        /// ★★ 2026-10-08 大改之后：② 列的是**整关的事件**（不是"这个事件的进度条目"），
        ///   ③ 编辑的是**当前选中那个事件**的那一条进度。
        ///   ⇒ 对账口径：清单条数 = 事件数；条词行数 = 选中事件的条词数；
        ///     输入框数 = 除「发牌」以外每一类各一颗（数值型一颗 / 刷怪「数量」一颗 / 筹码「张数」一颗）。
        /// </summary>
        private static bool LooksRight(PageStory story, DiyData d, out string why)
        {
            DiyEvent ev = CurEvent(story, d);
            DiyProgressItem it = ev == null ? null : Item(ev, PickN(story));
            int rows = BorderRows(story.EffList);
            int boxes = Boxes(story.EffList).Count;
            int wantBoxes = 0, wantRows = it == null ? 0 : it.Effects.Count;
            if (it != null)
                foreach (DiyEffect x in it.Effects)
                    if (x.Kind != EffectKind.Card) wantBoxes++;   // 发牌那颗是「选手牌…」按钮，不是输入框
            why = "清单 " + story.ItemList.Children.Count + "/" + d.Events.Count + " 个事件 · 条词行 "
                + rows + "/" + wantRows + " · 输入框 " + boxes + "/" + wantBoxes
                + " · 选中事件 " + (ev == null ? -1 : ev.Id) + " 的条目 N=" + PickN(story);
            return story.ItemList.Children.Count == d.Events.Count && rows == wantRows && boxes == wantBoxes;
        }

        /// <summary>当前选中的那个事件（③ 编辑区正在编辑的）。下拉里塞的是私有的 `EventPickItem`，用反射读它的 Id。</summary>
        private static DiyEvent CurEvent(PageStory story, DiyData d)
        {
            if (d == null || d.Events.Count == 0) return null;
            int id = PickEventId(story);
            DiyEvent ev = id > 0 ? d.Event(id) : null;
            return ev ?? d.Events[0];
        }

        private static int PickEventId(PageStory story)
        {
            try
            {
                object it = story == null || story.EventPick == null ? null : story.EventPick.SelectedItem;
                if (it == null) return -1;
                FieldInfo f = it.GetType().GetField("Id", BindingFlags.Public | BindingFlags.Instance);
                return f == null ? -1 : (int)f.GetValue(it);
            }
            catch (Exception) { return -1; }
        }

        /// <summary>这一关所有事件的 id（日志用）。</summary>
        private static string EventIds(DiyData d)
        {
            var a = new List<string>();
            foreach (DiyEvent e in d.Events) a.Add(e.Id.ToString(CultureInfo.InvariantCulture));
            return string.Join("/", a.ToArray());
        }

        /// <summary>第 k 条条词那一行（`EffList` 里的第 k 个 `Border`，顺序跟 `it.Effects` 一一对应）。</summary>
        private static Border RowAt(Panel host, int index)
        {
            if (host == null || index < 0) return null;
            int k = -1;
            foreach (UIElement u in host.Children)
            {
                var b = u as Border;
                if (b == null) continue;
                k++;
                if (k == index) return b;
            }
            return null;
        }

        private static TextBox FirstOf(List<TextBox> xs)
        {
            return xs != null && xs.Count > 0 ? xs[0] : null;
        }

        private static ComboBox FirstOf(List<ComboBox> xs)
        {
            return xs != null && xs.Count > 0 ? xs[0] : null;
        }

        /// <summary>一块面板里的下拉框（★「给谁」那颗是这次大改新增的控件）。</summary>
        private static List<ComboBox> Combos(DependencyObject host)
        {
            var got = new List<ComboBox>();
            CollectCombos(host, got);
            return got;
        }

        private static void CollectCombos(DependencyObject node, List<ComboBox> into)
        {
            if (node == null) return;
            var c = node as ComboBox;
            if (c != null && !into.Contains(c)) into.Add(c);
            int n = 0;
            try { n = VisualTreeHelper.GetChildrenCount(node); } catch (Exception) { return; }
            for (int k = 0; k < n; k++) CollectCombos(VisualTreeHelper.GetChild(node, k), into);
        }

        /// <summary>这个事件里有没有「新增怪物」条词（§12 要拿有刷怪的那条验 progress_events 的 spawn 行）。</summary>
        private static bool HasSpawn(DiyEvent e)
        {
            if (e == null) return false;
            foreach (DiyProgressItem it in e.Items)
                foreach (DiyEffect x in it.Effects)
                    if (x.Kind == EffectKind.Spawn) return true;
            return false;
        }

        private static DiyProgressItem Item(DiyEvent ev, int n)
        {
            foreach (DiyProgressItem it in ev.Items) if (it.N == n) return it;
            return null;
        }

        private static int PickN(PageStory story)
        {
            try
            {
                FieldInfo f = typeof(PageStory).GetField("_pickN", BindingFlags.NonPublic | BindingFlags.Instance);
                if (f != null) return (int)f.GetValue(story);
            }
            catch (Exception) { }
            return -1;
        }

        private static void ForceField(object target, string field, object value)
        {
            try
            {
                if (target == null) return;
                FieldInfo f = target.GetType().GetField(field, BindingFlags.NonPublic | BindingFlags.Instance);
                if (f != null) f.SetValue(target, value);
            }
            catch (Exception) { }
        }

        /// <summary>
        /// ★ 等到某个条件成立为止（**等到就立刻返回**；等不到也不抛，让调用方的断言去判红）。
        ///
        /// 为什么需要它：页面里有些文字是**推迟重建**（`DispatcherPriority.Background`）里刷的，
        ///   单跑一次 `Layout` 偶尔还停在上一帧 —— 断言就会偶发假红（实测 3 次里红 1 次）。
        ///   这不是"放宽断言"：判据一个字没改，只是**别抢在刷新之前读**。
        /// </summary>
        private static bool WaitFor(Func<bool> cond, FrameworkElement root, int maxRounds)
        {
            for (int k = 0; k < maxRounds; k++)
            {
                try { if (cond()) return true; }
                catch (Exception) { }
                Pump(root);
                Layout(root);
                System.Threading.Thread.Sleep(15);      // 让 Dispatcher 真的有机会跑那档 Background
            }
            try { return cond(); }
            catch (Exception) { return false; }
        }

        /// <summary>
        /// ★★ 把待办**真正排干净**：反复跑布局 + 清各优先级队列，直到
        ///   ① 根的尺寸连续两轮不变，**且** ② 整棵视觉树的控件数连续两轮不变。
        ///
        /// 为什么要"控件数不变"这一条：`MainWindow.QueueRebuild()` 把整页重建排在
        ///   `DispatcherPriority.Background`（见那边的长注释），重建会**换掉一批控件** ——
        ///   尺寸可能早就稳了，但控件数还在变。只等尺寸就会出现"读到的还是上一帧"：
        ///   断言拿到的清单 / 输入框是重建前那一批 ⇒ 一次挂一大片，复跑又全绿。
        ///   （`tools` 那套离屏截图 `--shots` 也踩过同一个坑，所以它一口气 Drain 三档优先级。）
        /// </summary>
        private static void Settle(FrameworkElement root, int maxRounds)
        {
            double lastW = -1, lastH = -1;
            int lastCount = -1;
            for (int k = 0; k < maxRounds; k++)
            {
                Layout(root);                 // 内部已经 Measure/Arrange/UpdateLayout + Pump
                double w = root.ActualWidth, h = root.ActualHeight;
                int count = VisualCount(root);
                bool still = Math.Abs(w - lastW) < 0.01 && Math.Abs(h - lastH) < 0.01
                             && count == lastCount;
                if (still && k >= 1) return;
                lastW = w; lastH = h; lastCount = count;
            }
        }

        /// <summary>整棵视觉树的元素个数（"控件数还在变"就是还没重建完）。</summary>
        private static int VisualCount(DependencyObject node)
        {
            if (node == null) return 0;
            int n = 0;
            try
            {
                int kids = VisualTreeHelper.GetChildrenCount(node);
                n = 1;
                for (int k = 0; k < kids; k++)
                    n += VisualCount(VisualTreeHelper.GetChild(node, k));
            }
            catch (Exception) { }
            return n;
        }

        private static void Layout(FrameworkElement root)
        {
            // ★ 从固定的 4 轮改成"到稳为止"：重建是在 Background 优先级补上来的，
            //   固定轮数在慢机器 / 冷启动上可能不够（那正是"构建后第一次跑"最容易中的场景）。
            for (int k = 0; k < 12; k++)
            {
                double bw = root.ActualWidth, bh = root.ActualHeight;
                try
                {
                    root.Measure(new Size(W, H));
                    root.Arrange(new Rect(0, 0, W, H));
                    root.UpdateLayout();
                }
                catch (Exception) { }
                Pump(root);
                if (k >= 1 && Math.Abs(root.ActualWidth - bw) < 0.01
                           && Math.Abs(root.ActualHeight - bh) < 0.01) return;
            }
        }

        private static void Pump(FrameworkElement root)
        {
            Dispatcher dis = Dispatcher.CurrentDispatcher;
            try { dis.Invoke(new Action(delegate { }), DispatcherPriority.Render); } catch (Exception) { }
            try { dis.Invoke(new Action(delegate { }), DispatcherPriority.Loaded); } catch (Exception) { }
            try { dis.Invoke(new Action(delegate { }), DispatcherPriority.Background); } catch (Exception) { }
            if (root != null) { try { root.UpdateLayout(); } catch (Exception) { } }
        }

        /// <summary>控件正中间那一点，最上层是谁 —— ★ 这就是"能不能点到它"的判据。</summary>
        private static string HitAtCenter(FrameworkElement root, FrameworkElement el)
        {
            try { el.BringIntoView(); } catch (Exception) { }
            try { root.UpdateLayout(); } catch (Exception) { }
            if (!el.IsVisible) return "★ 它是不可见态（IsVisible=false）";
            if (!el.IsEnabled) return "★ 它被禁用了（IsEnabled=false）";
            if (!el.IsHitTestVisible) return "★ 它自己关掉了命中测试（IsHitTestVisible=false）";
            if (el.ActualWidth < 0.5 || el.ActualHeight < 0.5)
                return "★ 尺寸是 0（" + el.ActualWidth.ToString("0.#") + "x" + el.ActualHeight.ToString("0.#") + "）";
            Point p;
            try { p = el.TransformToAncestor(root).Transform(new Point(el.ActualWidth / 2, el.ActualHeight / 2)); }
            catch (Exception ex) { return "★ 算不出坐标（" + ex.GetType().Name + "）"; }
            IInputElement got;
            try { got = root.InputHitTest(p); }
            catch (Exception ex) { return "★ 命中测试抛异常（" + ex.GetType().Name + "）"; }
            var node = got as DependencyObject;
            if (node == null) return "★ 这一点上什么都没有（落空；多半是被裁到视口外了）";
            if (ReferenceEquals(node, el) || IsInside(node, el)) return "自己（" + Brief(node) + "）";
            return "★ 被别的元素挡住：" + Brief(node);
        }

        private static bool IsInside(DependencyObject node, DependencyObject ancestor)
        {
            DependencyObject cur = node;
            int guard = 0;
            while (cur != null && guard++ < 200)
            {
                if (ReferenceEquals(cur, ancestor)) return true;
                cur = VisualTreeHelper.GetParent(cur);
            }
            return false;
        }

        private static List<Button> Buttons(DependencyObject host)
        {
            var got = new List<Button>();
            Collect(host, got);
            return got;
        }

        /// <summary>逻辑树里的元素个数（对话框没 Show 过时视觉树还没长出来，只能数逻辑树）。</summary>
        private static int LogicalCount(DependencyObject node, int depth = 0)
        {
            if (node == null || depth > 30) return 0;
            int n = 1;
            var panel = node as Panel;
            if (panel != null)
            {
                foreach (UIElement u in panel.Children) n += LogicalCount(u, depth + 1);
                return n;
            }
            var dec = node as Decorator;
            if (dec != null) return n + LogicalCount(dec.Child, depth + 1);
            var cp = node as ContentControl;
            if (cp != null && cp.Content is DependencyObject)
                return n + LogicalCount((DependencyObject)cp.Content, depth + 1);
            return n;
        }

        private static void Collect(DependencyObject node, List<Button> into)
        {
            if (node == null) return;
            var b = node as Button;
            if (b != null && !into.Contains(b)) into.Add(b);
            int n = 0;
            try { n = VisualTreeHelper.GetChildrenCount(node); } catch (Exception) { return; }
            for (int k = 0; k < n; k++) Collect(VisualTreeHelper.GetChild(node, k), into);
        }

        private static List<CheckBox> Checks(DependencyObject host)
        {
            var got = new List<CheckBox>();
            Checks(host, got);
            return got;
        }

        private static void Checks(DependencyObject node, List<CheckBox> into)
        {
            if (node == null) return;
            var c = node as CheckBox;
            if (c != null && !into.Contains(c)) into.Add(c);
            int n = 0;
            try { n = VisualTreeHelper.GetChildrenCount(node); } catch (Exception) { return; }
            for (int k = 0; k < n; k++) Checks(VisualTreeHelper.GetChild(node, k), into);
        }

        private static List<TextBox> Boxes(DependencyObject host)
        {
            var got = new List<TextBox>();
            Boxes(host, got);
            return got;
        }

        private static void Boxes(DependencyObject node, List<TextBox> into)
        {
            if (node == null) return;
            var t = node as TextBox;
            if (t != null && !into.Contains(t)) into.Add(t);
            int n = 0;
            try { n = VisualTreeHelper.GetChildrenCount(node); } catch (Exception) { return; }
            for (int k = 0; k < n; k++) Boxes(VisualTreeHelper.GetChild(node, k), into);
        }

        private static TextBox FirstTextBox(Panel host)
        {
            foreach (UIElement u in host.Children)
            {
                List<TextBox> got = Boxes(u);
                if (got.Count > 0) return got[0];
            }
            return null;
        }

        private static CheckBox FirstCheck(Panel host)
        {
            List<CheckBox> got = Checks(host);
            return got.Count > 0 ? got[0] : null;
        }

        private static int BorderRows(Panel host)
        {
            int n = 0;
            foreach (UIElement u in host.Children) if (u is Border) n++;
            return n;
        }

        private static Button ButtonWithText(DependencyObject host, string contains)
        {
            foreach (Button b in Buttons(host))
                if (TextOf(b).IndexOf(contains, StringComparison.Ordinal) >= 0) return b;
            return null;
        }

        private static string StyleName(Button b, FrameworkElement res)
        {
            try
            {
                if (b.Style == (Style)res.FindResource("Primary")) return "Primary（选中）";
                if (b.Style == (Style)res.FindResource("Ghost")) return "Ghost（没选中）";
            }
            catch (Exception) { }
            return "(别的样式)";
        }

        /// <summary>
        /// ★ 一颗事件行里**第 3 列那个"触发进度 N" TextBlock** 的前景色（§3⑥/⑦ 用它验可读性）。
        /// 内容树是我们自己搭的（`BuildList`），所以直接走 `Button.Content as Grid` 最稳，
        /// 不必等控件模板实例化。
        /// </summary>
        private static bool RowTrigForeground(Button row, out Color color)
        {
            color = Colors.Transparent;
            if (row == null) return false;
            var g = row.Content as Grid;
            if (g == null) return false;
            foreach (UIElement u in g.Children)
            {
                var tb = u as TextBlock;
                if (tb == null || Grid.GetColumn(tb) != 2) continue;
                var sb = tb.Foreground as SolidColorBrush;
                if (sb == null) return false;
                color = sb.Color;
                return true;
            }
            return false;
        }

        /// <summary>两个颜色的 WCAG 对比度（1.0 ~ 21.0）。</summary>
        private static double Contrast(Color a, Color b)
        {
            double la = Luminance(a), lb = Luminance(b);
            double hi = Math.Max(la, lb), lo = Math.Min(la, lb);
            return (hi + 0.05) / (lo + 0.05);
        }

        private static double Luminance(Color c)
        {
            return 0.2126 * Channel(c.R) + 0.7152 * Channel(c.G) + 0.0722 * Channel(c.B);
        }

        private static double Channel(byte v)
        {
            double s = v / 255.0;
            return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
        }

        private static string Hex(Color c)
        {
            return "#" + c.R.ToString("X2") + c.G.ToString("X2") + c.B.ToString("X2");
        }

        /// <summary>逻辑树里的文字（有些内容还没走模板时只存在于逻辑树里）。</summary>
        private static string TextOfLogical(DependencyObject node)
        {
            var sb = new StringBuilder();
            LogicalText(node, sb, 0);
            return sb.ToString().Trim();
        }

        private static void LogicalText(DependencyObject node, StringBuilder sb, int depth)
        {
            if (node == null || depth > 30) return;
            var tb = node as TextBlock;
            if (tb != null) sb.Append(tb.Text).Append(' ');
            var panel = node as Panel;
            if (panel != null)
            {
                foreach (UIElement u in panel.Children) LogicalText(u, sb, depth + 1);
                return;
            }
            var dec = node as Decorator;
            if (dec != null) { LogicalText(dec.Child, sb, depth + 1); return; }
            var cp = node as ContentControl;
            if (cp != null && cp.Content is DependencyObject)
                LogicalText((DependencyObject)cp.Content, sb, depth + 1);
        }

        private static string TextOf(DependencyObject node)
        {
            var sb = new StringBuilder();
            TextOf(node, sb, 0);
            return sb.ToString().Trim();
        }

        private static void TextOf(DependencyObject node, StringBuilder sb, int depth)
        {
            if (node == null || depth > 40) return;
            var b = node as Button;
            if (b != null && b.Content is string) sb.Append((string)b.Content).Append(' ');
            var tb = node as TextBlock;
            if (tb != null) sb.Append(tb.Text).Append(' ');
            var cb = node as CheckBox;
            if (cb != null && cb.Content is string) sb.Append((string)cb.Content).Append(' ');
            int n = 0;
            try { n = VisualTreeHelper.GetChildrenCount(node); } catch (Exception) { return; }
            for (int k = 0; k < n; k++) TextOf(VisualTreeHelper.GetChild(node, k), sb, depth + 1);
        }

        private static string Brief(DependencyObject node)
        {
            if (node == null) return "(null)";
            var fe = node as FrameworkElement;
            string name = fe != null && !string.IsNullOrEmpty(fe.Name) ? "#" + fe.Name : "";
            string text = TextOf(node);
            if (text.Length > 20) text = text.Substring(0, 20) + "…";
            return node.GetType().Name + name + (text.Length > 0 ? "「" + text + "」" : "");
        }

        /// <summary>清单那一块上的文字（视觉树长出来了就用它，没有就退回逻辑树）。</summary>
        private static string ListText(PageStory story)
        {
            return TextOf(story.ItemList) + " " + TextOfLogical(story.ItemList);
        }

        /// <summary>一块面板上的文字（同上；§11 用它读"编辑区那句话"）。</summary>
        private static string NoteText(DependencyObject host)
        {
            return TextOf(host) + " " + TextOfLogical(host);
        }

        /// <summary>日志里一行太长不好看：压成一行、超过就截断。</summary>
        private static string Clip(string s, int max)
        {
            if (string.IsNullOrEmpty(s)) return "";
            s = s.Replace("\r", " ").Replace("\n", " ").Trim();
            while (s.IndexOf("  ", StringComparison.Ordinal) >= 0) s = s.Replace("  ", " ");
            return s.Length <= max ? s : s.Substring(0, max) + "…";
        }

        private static void WriteLog()
        {
            try
            {
                File.WriteAllText(Path.Combine(Paths.Under("_自测"), "点击自测输出.txt"),
                    Log.ToString(), new UTF8Encoding(false));
            }
            catch (Exception) { }
        }
    }
}
