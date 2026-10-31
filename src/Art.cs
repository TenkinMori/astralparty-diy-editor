using System;
using System.Collections.Generic;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DiyEditor
{
    /// <summary>
    /// ★ 客户端贴图只读入口（带缓存）。
    ///
    /// 图片一律**只读**引用（实测 2901 张拆好的 PNG）——
    /// 不拷贝、不占空间，游戏更新换了图编辑器自动跟着变。
    ///
    /// ★★ 取材顺序（用户 2026-10-12 定的口径）：**磁盘优先、内置兜底** ——
    ///   ① `5-DIY关卡\贴图缓存\`（磁盘）② `<游戏根>\GameRes\Img`（磁盘）
    ///   ③ **exe 里内置的那份**（`EmbeddedCache`；"只拿到一个 exe"也能出图标）。
    ///   `ImagePath` 对内置那一份回的是 <see cref="EmbeddedCache.BuiltInMarker"/> 标记串，
    ///   由 <see cref="Load"/> 认出来、从资源流里解图。
    ///
    /// ★ **一个字节都不写**：`Load` 用 `BitmapCacheOption.OnLoad` + 从流读，
    ///   读完就关流 —— 不持有文件句柄，也不会去写它。
    /// </summary>
    public static class Art
    {
        private static readonly Dictionary<string, ImageSource> _cache = new Dictionary<string, ImageSource>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, string> _pathCache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> _missing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>贴图文件的完整路径；没有就返回 null（调用方回落色块）。
        ///   ★ 内置的那一份回的是**标记串**（不是真路径），见 <see cref="EmbeddedCache.BuiltInMarker"/>。</summary>
        public static string ImagePath(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            string hit;
            if (_pathCache.TryGetValue(name, out hit)) return hit;    // 命中缓存（可能是 null）
            string p = GameLocator.Image(name);
            _pathCache[name] = p;
            return p;
        }

        public static ImageSource Load(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            ImageSource hit;
            if (_cache.TryGetValue(path, out hit)) return hit;

            // ★★ 内置兜底：标记串 ⇒ 从 exe 的资源里解（不是磁盘文件）。
            //    内置里也没有就记账（别静默），调用方照旧回落色块。
            if (EmbeddedCache.IsBuiltInPath(path))
            {
                string builtInName = EmbeddedCache.BuiltInName(path);
                ImageSource got = EmbeddedCache.LoadTexture(builtInName);
                if (got == null)
                    EmbeddedCache.Warn("内置贴图里取不出 " + builtInName + "（看看 csproj 的 EmbeddedResource 有没有漏）");
                _cache[path] = got;
                if (got == null) _missing.Add(path);
                return got;
            }

            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;   // ★ 读完就脱手，不占文件
                bmp.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
                bmp.UriSource = new Uri(path, UriKind.Absolute);
                bmp.EndInit();
                bmp.Freeze();
                _cache[path] = bmp;
                return bmp;
            }
            catch (Exception)
            {
                _missing.Add(path);
                return null;
            }
        }

        public static ImageSource LoadName(string name)
        {
            string p = ImagePath(name);
            return p == null ? null : Load(p);
        }

        // ==================================================== ★★ 居中裁切（第 9 波 ④）
        /// <summary>
        /// ★★ 贴图里"非透明内容"的外接框（像素坐标）。读不到 / 整张全透明回 null；
        ///   整张都不透明时回整张的框。
        ///
        /// 为什么要它：官方怪头像是 **宽幅半身像**（1568×900 / 1880×1080），
        ///   角色在画布上**基本不居中** —— 实测 53 张里 **47 张** 的内容中心
        ///   偏离画布中心 8 px 以上（最多 180 px）。直接 UniformToFill 塞进圆里，
        ///   人眼看到的就是"怪物图标偏了"（用户第 9 波 ④ 的另一半原因）。
        /// </summary>
        public static int[] ContentBox(string name)
        {
            string key = name ?? "";
            int[] hit;
            if (_boxCache.TryGetValue(key, out hit)) return hit;
            hit = ComputeContentBox(name);
            _boxCache[key] = hit;
            return hit;
        }

        private static int[] ComputeContentBox(string name)
        {
            string path = ImagePath(name);
            if (path == null) return null;
            try
            {
                var src = Load(path) as BitmapSource;
                if (src == null) return null;
                int w = src.PixelWidth, h = src.PixelHeight;
                if (w <= 0 || h <= 0) return null;

                // 统一转成 Bgra32 再扫 alpha（源可能是 Indexed / Bgr24 / 带预乘）
                BitmapSource conv = src;
                if (src.Format != PixelFormats.Bgra32)
                {
                    var c = new FormatConvertedBitmap();
                    c.BeginInit();
                    c.Source = src;
                    c.DestinationFormat = PixelFormats.Bgra32;
                    c.EndInit();
                    c.Freeze();
                    conv = c;
                }
                int stride = w * 4;
                var buf = new byte[(long)stride * h <= int.MaxValue ? stride * h : 0];
                if (buf.Length == 0) return null;
                conv.CopyPixels(buf, stride, 0);

                int minX = w, minY = h, maxX = -1, maxY = -1;
                for (int y = 0; y < h; y++)
                {
                    int row = y * stride;
                    for (int x = 0; x < w; x++)
                        if (buf[row + x * 4 + 3] > 8)          // alpha 阈值：几乎全透的边不算内容
                        {
                            if (x < minX) minX = x;
                            if (x > maxX) maxX = x;
                            if (y < minY) minY = y;
                            if (y > maxY) maxY = y;
                        }
                }
                if (maxX < minX || maxY < minY) return null;       // 整张全透明
                return new int[] { minX, minY, maxX - minX + 1, maxY - minY + 1 };
            }
            catch (Exception) { return null; }
        }

        /// <summary>
        /// ★★ **内容居中的正方形**：把"非透明内容"的外接框居中裁成正方形再返回。
        ///
        /// 用途 = 怪头像（棋盘上的怪 / 怪物池 / 新增怪物窗口）—— 圆里塞的就是这一块，
        ///   所以角色一定**正正好在正中**，不再"偏一角"。
        /// ★ 找不到内容框（没图 / 读不出）就原样回整张，调用方行为不变。
        /// </summary>
        public static ImageSource CenteredSquare(string name)
        {
            string key = "□" + (name ?? "");
            ImageSource hit;
            if (_squareCache.TryGetValue(key, out hit)) return hit;

            string path = ImagePath(name);
            if (path == null) { _squareCache[key] = null; return null; }
            try
            {
                var src = Load(path) as BitmapSource;
                if (src == null) { _squareCache[key] = null; return null; }
                int[] box = ContentBox(name);
                int w = src.PixelWidth, h = src.PixelHeight;

                int side, cx, cy;
                if (box == null)
                {
                    side = Math.Min(w, h);
                    cx = w / 2; cy = h / 2;
                }
                else
                {
                    // 内容外扩 4%（留一点边，别顶到圆的边上）
                    side = (int)Math.Ceiling(Math.Max(box[2], box[3]) * 1.08);
                    cx = box[0] + box[2] / 2;
                    cy = box[1] + box[3] / 2;
                }
                if (side > Math.Min(w, h)) side = Math.Min(w, h);
                if (side < 1) side = 1;

                int x = cx - side / 2, y = cy - side / 2;
                if (x < 0) x = 0;
                if (y < 0) y = 0;
                if (x + side > w) x = w - side;
                if (y + side > h) y = h - side;
                if (x < 0) x = 0;
                if (y < 0) y = 0;

                var crop = new CroppedBitmap(src, new System.Windows.Int32Rect(x, y, side, side));
                crop.Freeze();
                _squareCache[key] = crop;
                return crop;
            }
            catch (Exception)
            {
                _squareCache[key] = null;
                return null;
            }
        }

        /// <summary>这张贴图的内容框（报告用；没有就 null）。</summary>
        public static int[] ContentBoxOf(string name) { return ContentBox(name); }

        private static readonly Dictionary<string, int[]> _boxCache = new Dictionary<string, int[]>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, ImageSource> _squareCache = new Dictionary<string, ImageSource>(StringComparer.Ordinal);

        // ==================================================== 格子贴图
        /// <summary>
        /// ★★ `landType` → 客户端贴图名。判据**全部来自游戏自己的 `Land.bin`**
        ///   （不是照英文名猜的，见 `LandTable` 的说明：旧版那张猜表就是用户说的「图标对不上」的根因）。
        ///
        /// ★ 只返回**官方点名 + 文件确实在 + 不是角色头像编号贴图**的那一张；
        ///   否则回 null，由调用方落下"色块 + 官方中文名"。
        /// </summary>
        public static string PlatformTexture(int landType)
        {
            return LandTable.StemIfUsable(landType);
        }

        /// <summary>同上，但把"是哪一张"也带出来（页面上要如实报"认得出几张"）。</summary>
        public static bool TryPlatformTexture(int landType, out string stem)
        {
            stem = LandTable.StemIfUsable(landType);
            return stem != null;
        }

        /// <summary>官方给这一格点名的贴图名（不管文件在不在 / 是不是头像）—— 报告用。</summary>
        public static string OfficialTextureName(int landType)
        {
            return LandTable.TextureName(landType);
        }

        /// <summary>这张表一共登记了几种格子功能（= Land.bin 里的行数）。</summary>
        public static int PlatformTableSize { get { return LandTable.TableSize; } }
    }
}
