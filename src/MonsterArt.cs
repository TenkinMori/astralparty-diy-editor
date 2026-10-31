using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace DiyEditor
{
    /// <summary>
    /// ★★ 怪物头像的**统一取图入口**（2026-10-10 用户要求：怪物图换成图鉴那套像素小人）。
    ///
    /// 为什么要有它：以前"画哪张怪图"这件事散在四处 —— 怪物页列表 / 怪物页右侧详情 /
    /// `CellMonsterDialog` / 棋盘上的怪 —— 各写一遍 `Art.CenteredSquare(m.AvatarName)`。
    /// 现在**取图与缩放口径只在这里定一次**，调用方只管把结果塞进 `Image`。
    ///
    /// 取图顺序（见 `MonsterRow.AvatarName`）：① f19 登记的像素小人 → ② 官方半身像 →
    /// ③ 手牌画 → ④ 色块 + 中文名。
    ///
    /// ★ **像素图必须用 NearestNeighbor 放大**：那套图是 200×200（有的 160×160）的小图，
    ///   卡片/棋盘上要放大到 40~120 px 显示，默认的双线性插值会把它糊成一团色浆 ——
    ///   像素画的观感全靠"硬边"。
    /// </summary>
    public static class MonsterArt
    {
        /// <summary>头像贴图的放大方式：像素小人用最近邻（硬边），别的用默认高质量插值。</summary>
        public static BitmapScalingMode ModeFor(bool isPixel)
        {
            return isPixel ? BitmapScalingMode.NearestNeighbor : BitmapScalingMode.HighQuality;
        }

        /// <summary>
        /// 这只怪该画的那张图（已经按需做了"内容居中裁正方形"）。
        /// 像素小人**不裁** —— 它本来就是居中的方形小图，`CenteredSquare` 反而白算一遍 alpha 扫描。
        ///
        /// ★★ 2026-10-17：加了**立绘**这一档（`5-DIY关卡\图标缓存\&lt;id&gt;.png`，见
        ///   `MonsterRow.PortraitPath`）。口径：
        ///     · 配置 `monster_art: "portrait"` ⇒ **立绘优先**（怪物页/对话框那种大图更好看）；
        ///     · 默认 `"pixel"` ⇒ 还是图鉴像素小人优先（棋盘上的怪只有 15 px，立绘缩下去认不清），
        ///       立绘只当"像素小人缺图"时的第一顺位。
        ///   ⇒ 两种设置下**都有图**，只是大的那张不一样。
        /// </summary>
        public static ImageSource ImageFor(MonsterRow m)
        {
            if (m == null) return null;
            if (UsesPortrait(m)) return Art.Load(m.PortraitPath);
            if (m.AvatarIsPixel) return Art.LoadName(m.AvatarAsset);
            if (m.PortraitPath.Length > 0) return Art.Load(m.PortraitPath);
            return Art.CenteredSquare(m.AvatarName);
        }

        /// <summary>这次画的是不是"图标缓存里的立绘"（决定缩放方式与提示文案）。</summary>
        public static bool UsesPortrait(MonsterRow m)
        {
            if (m == null || m.PortraitPath.Length == 0) return false;
            return GameLocator.PreferPortrait || !m.AvatarIsPixel;
        }

        /// <summary>池子里的怪（`PoolMonster`）版。</summary>
        public static ImageSource ImageFor(PoolMonster p)
        {
            return p == null ? null : ImageFor(p.TableRow);
        }

        /// <summary>
        /// 造一个已经配好 `Source` 与缩放方式的 `Image`（调用方再设尺寸/对齐/裁圆）。
        /// 取不到图就回 null，调用方照旧落「色块 + 中文名」。
        /// </summary>
        public static Image Make(MonsterRow m)
        {
            ImageSource src = ImageFor(m);
            if (src == null) return null;
            var img = new Image();
            img.Source = src;
            img.IsHitTestVisible = false;
            // ★ 只有"真在画像素小人"时才用最近邻；画立绘要用高质量插值。
            bool pixelLook = m != null && m.AvatarIsPixel && !UsesPortrait(m);
            RenderOptions.SetBitmapScalingMode(img, ModeFor(pixelLook));
            return img;
        }

        /// <summary>池子里的怪（`PoolMonster`）版。</summary>
        public static Image Make(PoolMonster p)
        {
            return p == null ? null : Make(p.TableRow);
        }

        /// <summary>
        /// 「这张图是哪来的」一行字（悬停 / 详情用）。★ 那几只"官方登记了像素图但本地没下到"
        /// 的怪，就靠这一行把话说清楚 —— 不弹窗、不打扰用户。
        /// </summary>
        public static string SourceNote(MonsterRow m)
        {
            return m == null ? "" : m.AvatarSourceNote;
        }

        /// <summary>同上，池子版。</summary>
        public static string SourceNote(PoolMonster p)
        {
            return p == null || p.TableRow == null ? "" : p.TableRow.AvatarSourceNote;
        }
    }
}
