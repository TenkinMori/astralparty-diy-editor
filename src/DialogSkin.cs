using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace DiyEditor
{
    /// <summary>
    /// ★ 三个"二级菜单窗口"（新建关卡 / 新增怪物 / 发牌）共用的一点皮。
    ///
    /// 为什么要它：对话框是**独立的 `Window`**（不是主窗口里的页面），
    /// 它不在主窗口那棵树上取继承的 `Foreground`；离屏渲染（`RenderTargetBitmap`）
    /// 里更明显 —— 没显式给色的 `TextBlock` 会落成**系统主题的深色字**，
    /// 画在深色底上就是"一片黑"。所以这里把底色和"默认字色"都钉在根上
    /// （子元素自己设过的照样覆盖）。
    /// </summary>
    public static class DialogSkin
    {
        public static readonly Color Dark = Color.FromArgb(255, 10, 13, 22);

        /// <summary>给对话框的根打底：深色背景 + 白字默认值（递归铺满整棵树）。</summary>
        public static void Paint(FrameworkElement root)
        {
            if (root == null) return;
            var panel = root as Panel;
            if (panel != null) panel.Background = new SolidColorBrush(Dark);
            var border = root as Border;
            if (border != null) border.Background = new SolidColorBrush(Dark);
            var ctl = root as System.Windows.Controls.Control;
            if (ctl != null) ctl.Background = new SolidColorBrush(Dark);
            Brush fg = Brushes.White;
            object got = root.TryFindResource("Fg");
            if (got is Brush) fg = (Brush)got;
            RootFg(root, fg);
        }

        private static void RootFg(DependencyObject node, Brush fg)
        {
            if (node == null) return;
            TextElement.SetForeground(node, fg);
            int n = VisualTreeHelper.GetChildrenCount(node);
            for (int k = 0; k < n; k++)
                RootFg(VisualTreeHelper.GetChild(node, k), fg);
        }
    }
}
