using System;
using System.Collections.Generic;

namespace DiyEditor
{
    /// <summary>
    /// ★ 怪物的分类（`Monster.bin` 的 `monster_type`：1 普通小怪 / 2 Boss / 3 精英）。
    ///   界面上"支持分类"那一栏就是按它分的（新增怪物窗口的三个页签 + 怪物池页的分栏）。
    /// </summary>
    public static class MonsterClasses
    {
        public static readonly MonsterClass[] Order = new MonsterClass[]
        {
            MonsterClass.Minion, MonsterClass.Elite, MonsterClass.Boss, MonsterClass.Unknown,
        };

        /// <summary>分类的短名字（页签 / 分栏标题用）。</summary>
        public static string Tab(MonsterClass c)
        {
            switch (c)
            {
                case MonsterClass.Minion: return "普通小怪";
                case MonsterClass.Boss: return "Boss 怪";
                case MonsterClass.Elite: return "精英怪";
                default: return "未分类";
            }
        }

        /// <summary>分类的完整说明（带 monster_type 依据）。</summary>
        public static string Title(MonsterClass c)
        {
            switch (c)
            {
                case MonsterClass.Minion: return "普通小怪（monster_type = 1）";
                case MonsterClass.Boss: return "首领 Boss（monster_type = 2）";
                case MonsterClass.Elite: return "精英怪（monster_type = 3）";
                default: return "未分类（monster_type 不是 1/2/3，或不在 Monster.bin 里）";
            }
        }
    }

    /// <summary>
    /// ★ 兼容壳：`BoardMonsters.ClassOrder` 这个名字在别处还在用（列表顺序只有一份权威表达）。
    /// </summary>
    public static class BoardMonsters
    {
        public static readonly MonsterClass[] ClassOrder = MonsterClasses.Order;
    }

    /// <summary>
    /// ★★ 棋盘上一格画多个怪物头像时的**几何**（一格多只 · 第 9 波）。
    ///
    /// 规矩（用户第 9 波原话："同一格多只怪要都画出来" + "小头像错开叠放，例如 2~3 只按 45° 小偏移"
    /// + "多于 3 只 ⇒ 显示 '+N' 角标"）：
    ///
    ///   ① **居中**：1 只时头像中心 = **格子的内容层中心**（不是外层画布、不是格子左上角）；
    ///   ② **错开**：2~3 只沿 45°（右上 / 左下）等距摊开，步长 = 头像直径 × 0.42（"小偏移"）；
    ///   ③ **封顶**：最多画 3 个，多的用 "+N" 角标（N = 没画出来的只数）。
    ///
    /// ★ 为什么单独写成静态类：这套算术要能被自测**直接断言**（四档格子大小都验居中），
    ///   而不是藏在 `Draw()` 里靠看截图。
    /// </summary>
    public static class MonsterStack
    {
        /// <summary>一格上最多画几个头像。</summary>
        public const int MaxDrawn = 3;

        /// <summary>错开的方向：45°（单位向量 (1,-1)/√2，屏幕 y 向下 ⇒ 往上偏）。</summary>
        private static readonly double Cos45 = Math.Sqrt(0.5);

        /// <summary>错开的步长 = 头像直径 × 这个比例（小偏移，不是排成一行）。</summary>
        public const double StepRatio = 0.42;

        /// <summary>头像直径（相对格子的边长）。只数越多画得越小一点，叠起来才看得清。</summary>
        public static double Diameter(int count, double node)
        {
            double r = count <= 1 ? 0.62 : (count == 2 ? 0.54 : 0.48);
            return node * r;
        }

        /// <summary>超过上限时"没画出来"的只数（不超回 0）。</summary>
        public static int HiddenCount(int count)
        {
            return count > MaxDrawn ? count - MaxDrawn : 0;
        }

        /// <summary>
        /// 第 `index` 个头像（共 `count` 个）相对**格子中心**的偏移量（画布像素）。
        /// 1 只 ⇒ (0,0)；2 只 ⇒ 沿 45° 一上一下各半格；3 只 ⇒ 再往两头各一格。
        /// </summary>
        public static void Offset(int index, int count, double diameter, out double dx, out double dy)
        {
            dx = 0; dy = 0;
            if (count <= 1) return;
            double step = diameter * StepRatio;
            double off = (index - (count - 1) / 2.0) * step;
            dx = off * Cos45;
            dy = -off * Cos45;      // 屏幕 y 向下：负 = 往上
        }

        /// <summary>
        /// 头像在画布上的矩形。★ 参考系 = **格子的内容层** —— `centerX/centerY` 是
        /// `BoardCanvas` 里那一格的中心（格子自己摆在 `center - node/2`），
        /// 所以 1 只时 `left = centerX - size/2`、`top = centerY - size/2`（正正好居中）。
        /// </summary>
        public static void Rect(double centerX, double centerY, double node, int index, int count,
                                out double left, out double top, out double size)
        {
            size = Diameter(count, node);
            double dx, dy;
            Offset(index, count, size, out dx, out dy);
            left = centerX + dx - size / 2;
            top = centerY + dy - size / 2;
        }
    }
}
