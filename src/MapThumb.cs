using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DiyEditor
{
    /// <summary>
    /// ★★ 「地图选择」页那些**缺失的缩略图** —— 从编辑器自己手上的棋盘数据**现画一张**。
    ///
    /// 为什么要有它（用户这轮的第 4 件事）：
    ///   「地图选择」页的缩略图原来只认官方的
    ///   `UT_MapPreview_&lt;画板号&gt;` / `UT_MapScene_&lt;画板号&gt;`。
    ///   实测游戏自己的 Addressables 目录里 **只登记了 15 张 UT_MapPreview**
    ///   （003..016 + 019/020，**没有 017/021/022/023**）——
    ///   而「异变图书馆」(82016) 的画板号正是 **023** ⇒ 它的三档卡片全是
    ///   「（没有缩略图）」。82017(022) 同理。
    ///
    /// 所以这里换一条**稳定、不依赖游戏资源**的路：棋盘数据
    ///   （`棋盘缓存\&lt;画板号&gt;.json`，编辑器本来就能读、也本来就会画）
    ///   直接渲染成一张缩略图 —— 格子贴图 / 连线 / 起点描边都是编辑器已经会画的那一套，
    ///   所以缩略图和棋盘页看到的**是同一张图**，不会"图和实际对不上"。
    ///
    /// 三级取材顺序（前一级没有才退后一级）：
    ///   ① 已经有缓存 `贴图缓存\map_&lt;画板号&gt;.png` ⇒ 直接用（不重画）；
    ///   ② 有棋盘数据 ⇒ **渲染**一张，并**落盘缓存**（下次不重画）；
    ///   ③ 没有棋盘数据 ⇒ 退官方的 `UT_MapPreview_*` / `UT_MapScene_*`（可能也没有 ⇒ null）。
    ///
    /// ★ 只写 `5-DIY关卡\贴图缓存\`（编辑器自己的缓存目录，README 里它就是可写的）；
    ///   游戏目录一个字节都不碰；**exe 内置的那份贴图也一个字节都不写**（只读来源）。
    ///
    /// ★★ 棋盘数据本身走 `BoardStore` ⇒ 磁盘优先、**exe 内置兜底**（用户 2026-10-12
    ///   的「要内置」）：只有 exe 一个文件时也照样渲染得出缩略图。
    /// </summary>
    public static class MapThumb
    {
        /// <summary>缩略图渲染出来的像素尺寸（卡片那块是 92 px 高、宽度更大，给足余量）。</summary>
        public const int Width = 480;
        public const int Height = 300;

        private static readonly Dictionary<string, ImageSource> _cache =
            new Dictionary<string, ImageSource>(StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> _tried = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, string> _source = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>调试用：把内存里那份忘了（`--map-thumbs --fresh` 会让它重画一遍）。</summary>
        public static void ForgetMemory()
        {
            _cache.Clear();
            _tried.Clear();
            _source.Clear();
        }

        /// <summary>
        /// 渲染出来的缓存文件路径（`贴图缓存\map_&lt;画板号&gt;.png`）。
        /// ★ 这是**渲染图**的缓存，按**画板号**存（和官方预览按"地图"取不是一回事）。
        /// </summary>
        public static string CachePath(string suffix)
        {
            string dir = GameLocator.ArtCacheDir;
            if (string.IsNullOrEmpty(dir)) return null;
            return Path.Combine(dir, "map_" + suffix + ".png");
        }

        /// <summary>
        /// 这张地图模板的缩略图。找不到任何来源就回 null（调用方照旧落「（没有缩略图）」）。
        /// </summary>
        public static ImageSource Find(MapTemplate t)
        {
            if (t == null || string.IsNullOrEmpty(t.SceneSuffix)) return null;
            string key = t.Id + "|" + t.SceneSuffix;

            ImageSource hit;
            if (_cache.TryGetValue(key, out hit)) return hit;
            if (_tried.Contains(key)) return null;      // 这一档试过了、没有，别反复试
            _tried.Add(key);

            ImageSource got;
            string src;
            got = FindCore(t, out src);
            _cache[key] = got;
            _source[key] = src;
            return got;
        }

        /// <summary>
        /// ★★ **这一张卡片到底该用哪张图** —— 全工程只在这里判一次（`CreateLevelDialog` 与
        /// `--map-thumbs` 都走它，不在两处各判一遍）。
        ///
        /// 素材顺序（用户 2026-10-10 拍板）：
        ///   ① **`Map.bin` f1 登记的美术图**（f8 `MapImage`，本地有就用）
        ///      —— ★ 名字来自**表里登记**，不是拿画板号拼的（拼是错的：82016 画板 023、美术 016）；
        ///   ①b 那一行没登记时，**借同一画板号的邻图**（82017 与 82007 都是画板 007）；
        ///   ①c 才退横版大图（f9 `MapSceneImage`）；
        ///   ② 我们自己缓存的渲染图 `贴图缓存\map_&lt;画板号&gt;.png`；
        ///   ③ 从棋盘数据**现渲染一张**并落盘缓存（只有这一路会写盘）。
        ///
        /// ★ 两把 key 不是一回事：**美术图按"地图 id"登记**（`Map.bin` f1），
        ///   渲染缓存按**画板号**存 —— 所以判断身份要用 `MapTemplate` 整个对象，不能只看画板号。
        /// </summary>
        public static string SourceOf(MapTemplate t)
        {
            if (t == null || string.IsNullOrEmpty(t.SceneSuffix)) return "没有画板号";
            string key = t.Id + "|" + t.SceneSuffix;
            string got;
            if (_source.TryGetValue(key, out got)) return got ?? "没有";
            Find(t);                                     // 没查过就先查一遍（结果记进 _source）
            return _source.TryGetValue(key, out got) ? (got ?? "没有") : "没有";
        }

        private static ImageSource FindCore(MapTemplate t, out string src)
        {
            src = "没有";
            string suffix = t.SceneSuffix;

            // ① ★ 表里登记的美术图（`Map.bin` f1 的 f8 = MapImage）—— 本地有就用。
            //    这一族是**正方形的地图卡片美术**，正是选图卡片设计上要的那张。
            if (!string.IsNullOrEmpty(t.MapImage))
            {
                ImageSource art = TryLoad(t.MapImage);
                if (art != null) { src = "官方美术 " + t.MapImage + "（Map.bin f8）"; return art; }
            }
            // ①b **同画板号的邻图借图**：少数地图（82017「锥尖的炽热挑战」）在 Map.bin 的
            //     登记表里**根本没有行**，但它的画板号和 82007 相同（007）—— 同一张棋盘换了个关卡皮。
            //     借"它自己那张棋盘所属的美术图"是有依据的，比空白强。
            if (t.MapImage.Length == 0)
            {
                string sib = GameTables.SiblingArtName(t);
                if (!string.IsNullOrEmpty(sib) && !string.Equals(sib, t.MapImage, StringComparison.Ordinal))
                {
                    ImageSource art = TryLoad(sib);
                    if (art != null)
                    {
                        src = "官方美术 " + sib + "（同画板号 " + suffix + " 的邻图借用；本条 Map.bin 没登记）";
                        return art;
                    }
                }
            }
            // ①c 卡片美术本地没有才退横版大图（f9 = MapSceneImage，800x220 超宽）。
            if (!string.IsNullOrEmpty(t.MapSceneImage))
            {
                ImageSource art = TryLoad(t.MapSceneImage);
                if (art != null)
                {
                    src = "官方大图 " + t.MapSceneImage + "（Map.bin f9；f8 本地没有）";
                    return art;
                }
            }

            // ② 我们自己缓存的渲染图（按画板号）
            try
            {
                string cp = CachePath(suffix);
                if (cp != null && File.Exists(cp))
                {
                    ImageSource cached = Art.Load(cp);
                    if (cached != null) { src = "渲染缓存 map_" + suffix + ".png"; return cached; }
                }
            }
            catch (Exception) { }

            // ③ 有棋盘数据 ⇒ 现渲染一张 + 落盘
            try
            {
                BoardModel board = BoardStore.Load(t.Id, null);
                if (board != null && board.Ok && board.Nodes.Count > 0)
                {
                    ImageSource made = Render(board);
                    if (made != null)
                    {
                        SavePng(made, CachePath(suffix));
                        src = "现渲染（已落盘 map_" + suffix + ".png）";
                        return made;
                    }
                }
            }
            catch (Exception) { }

            return null;
        }

        /// <summary>读一张贴图，读不出来回 null（不抛）。</summary>
        private static ImageSource TryLoad(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            try { return Art.LoadName(name); }
            catch (Exception) { return null; }
        }

        // ==================================================== 渲染
        /// <summary>
        /// 把一张棋盘渲染成缩略图：格子贴图 + 连线 + 起点绿环，
        /// 底色用主题色的深色渐变（和界面同一个观感）。
        /// </summary>
        public static ImageSource Render(BoardModel board)
        {
            if (board == null || !board.Ok || board.Nodes.Count == 0) return null;

            // 棋盘坐标 → 像素：先算包围盒，再等比铺满（留边）
            double minX = double.MaxValue, maxX = double.MinValue;
            double minZ = double.MaxValue, maxZ = double.MinValue;
            foreach (BoardNode n in board.Nodes)
            {
                if (n.X < minX) minX = n.X;
                if (n.X > maxX) maxX = n.X;
                if (n.Z < minZ) minZ = n.Z;
                if (n.Z > maxZ) maxZ = n.Z;
            }
            double spanX = Math.Max(1.0, maxX - minX);
            double spanZ = Math.Max(1.0, maxZ - minZ);

            const double pad = 16.0;
            double usableW = Width - pad * 2;
            double usableH = Height - pad * 2;

            // 相邻格子的实际间距（用来定格子直径：别让格子互相压住）
            double pitch = MedianPitch(board.Nodes);
            if (pitch <= 0.01) pitch = Math.Min(spanX, spanZ);

            // 一个"格距"该占多少像素 = min(横着铺得下, 竖着铺得下)
            double pxPerUnit = Math.Min(usableW / (spanX + pitch), usableH / (spanZ + pitch));
            double node = Math.Max(5.0, pitch * pxPerUnit);

            double usedW = spanX * pxPerUnit;
            double usedH = spanZ * pxPerUnit;
            double offX = (Width - usedW) / 2.0;
            double offY = (Height - usedH) / 2.0;

            Func<BoardNode, Point> at = delegate (BoardNode n)
            {
                return new Point(offX + (n.X - minX) * pxPerUnit,
                                 offY + (n.Z - minZ) * pxPerUnit);
            };

            var visual = new DrawingVisual();
            using (DrawingContext dc = visual.RenderOpen())
            {
                // 底：主题色深渐变（和主界面背景一个口径）
                Color accent = Look.AccentColor;
                Color dark = Color.FromRgb(0x07, 0x09, 0x11);
                var bg = new LinearGradientBrush(Look.Mix(dark, accent, 0.20),
                                                 Look.Mix(dark, accent, 0.04),
                                                 new Point(0, 0), new Point(1, 1));
                dc.DrawRectangle(bg, null, new Rect(0, 0, Width, Height));

                // ① 连线（先画，压在格子底下）
                var pen = new Pen(new SolidColorBrush(Color.FromArgb(150, 226, 236, 250)),
                                  Math.Max(1.2, node * 0.14));
                pen.Freeze();
                var drawn = new HashSet<string>(StringComparer.Ordinal);
                foreach (BoardNode n in board.Nodes)
                {
                    Point a = at(n);
                    foreach (int nb in n.Neighbors)
                    {
                        BoardNode m = null;
                        foreach (BoardNode c in board.Nodes) if (c.Id == nb) { m = c; break; }
                        if (m == null) continue;
                        string key = Math.Min(n.Id, m.Id) + "-" + Math.Max(n.Id, m.Id);
                        if (!drawn.Add(key)) continue;
                        dc.DrawLine(pen, a, at(m));
                    }
                }

                // ② 每一格：**裁成正圆**的色块 + 官方平台贴图
                //    ★ 和棋盘页 `ClipToDisc` 同一个口径 —— 不裁的话缩略图里
                //      也会出现"圆形贴图外面四个角露色块"，正是用户指出的那个毛病。
                foreach (BoardNode n in board.Nodes)
                {
                    Point p = at(n);
                    var rect = new Rect(p.X - node / 2, p.Y - node / 2, node, node);

                    Color fill;
                    try { fill = (Color)ColorConverter.ConvertFromString(Land.Hex(n.ShownLand)); }
                    catch (Exception) { fill = Color.FromRgb(0x3A, 0x43, 0x54); }

                    var disc = new EllipseGeometry(p, node / 2, node / 2);
                    disc.Freeze();
                    dc.PushClip(disc);
                    dc.DrawRectangle(new SolidColorBrush(fill), null, rect);

                    string stem = Art.PlatformTexture(n.ShownLand);
                    ImageSource art = stem == null ? null : Art.LoadName(stem);
                    if (art != null) dc.DrawImage(art, rect);
                    else
                        dc.DrawEllipse(null,
                                       new Pen(new SolidColorBrush(Color.FromArgb(140, 255, 255, 255)),
                                               Math.Max(0.8, node * 0.07)),
                                       p, node / 2, node / 2);

                    dc.Pop();
                }
            }

            var rtb = new RenderTargetBitmap(Width, Height, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(visual);
            rtb.Freeze();
            return rtb;
        }

        /// <summary>相邻格子的中位间距（棋盘坐标是"格距 = 一个格子"的量纲）。</summary>
        private static double MedianPitch(List<BoardNode> nodes)
        {
            var dists = new List<double>();
            foreach (BoardNode n in nodes)
                foreach (int nb in n.Neighbors)
                    foreach (BoardNode m in nodes)
                        if (m.Id == nb)
                        {
                            double dx = n.X - m.X, dz = n.Z - m.Z;
                            double d = Math.Sqrt(dx * dx + dz * dz);
                            if (d > 0.01) dists.Add(d);
                            break;
                        }
            if (dists.Count == 0) return 0;
            dists.Sort();
            return dists[dists.Count / 2];
        }

        /// <summary>把渲染结果落成 PNG（失败就当没缓存，别把界面搞崩）。</summary>
        private static void SavePng(ImageSource src, string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            try
            {
                var bmp = src as BitmapSource;
                if (bmp == null) return;
                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
                var enc = new PngBitmapEncoder();
                enc.Frames.Add(BitmapFrame.Create(bmp));
                using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write))
                    enc.Save(fs);
            }
            catch (Exception) { }
        }
    }
}
