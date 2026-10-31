using System.Windows;
using System.Windows.Controls;

namespace DiyEditor
{
    /// <summary>
    /// ★ 第⑥页：导出 / 说明 —— 纯说明页（设计稿 v2 §2.1 / §2.4 / §③）。
    ///
    /// ★ 这一页**不写任何文件**：它只回答"这份 JSON 将来谁读、怎么读"。
    ///   本轮（用户要求先看 UI）它是静态版面；内容都来自已经核实过的事实：
    ///   · 服务端白名单在 `levels.py:117-193`；`_normalize` 只挑认识的顶层键
    ///   · 房间难度要落进 `Map.GameDifficultyDict` 的 key（`gamedata.py:5`）
    ///   · 事件效果现在只认 spawn / attack / defense / move（levels.py:167）
    ///
    /// ★ 本轮新增「⓪ 外观」那一块：入口照启动器的「设置 → 外观」，
    ///   点开就是 `LookDialog`（背景图 / 玻璃磨砂 / 压暗 / 主题色）。
    /// </summary>
    public partial class PageExport : UserControl
    {
        public PageExport()
        {
            InitializeComponent();
        }

        public void Reload()
        {
            // 纯说明页，没有要刷新的数据 —— 只把"现在的外观"如实写一行。
            if (LookNow != null)
                LookNow.Text = "现在：背景 = " + Look.DescribeBackground()
                    + " · 磨砂 " + Look.Num(Look.Frost)
                    + " · 压暗 " + Look.Num(Look.Dim)
                    + " · 主题色 " + Look.Accent;
        }

        private void OnOpenLook(object sender, RoutedEventArgs e)
        {
            LookDialog.OpenFrom(this);
        }
    }
}
