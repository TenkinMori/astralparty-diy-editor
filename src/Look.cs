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
    /// ★★ 「外观」设置：背景图 + 玻璃磨砂半径 + 背景压暗 + 主题色。
    ///
    /// 这是照 `D:\小小兔趴\1-首次安装包\启动器\`（**只读参考，一个字节都没动**）
    /// 那套「设置 → 外观」搬过来的 —— 用户这轮的原话就是
    /// 「旧项目的背景图和玻璃磨砂设置」。
    ///
    /// 口径与启动器一致：
    ///   * `Background` —— 背景图路径（空串 = 用主题色渐变）；`preset:xxx` 是预设名；
    ///   * `Frost`      —— 玻璃磨砂半径（0..60），**只糊面板，背景图本身保持清晰**；
    ///   * `Dim`        —— 背景压暗（0..0.85），盖在图上、不糊。
    ///
    /// ★ 只写编辑器自己的 `编辑器配置.json`（`Paths.Base` 下），不碰游戏目录、不碰服务端。
    /// </summary>
    public static class Look
    {
        public const double FrostMax = 60.0;
        public const double DimMax = 0.85;
        /// <summary>启动器 `Settings.Blur` 的默认值就是 28。</summary>
        public const double FrostDefault = 28.0;
        public const string AccentDefault = "#8FD8FF";

        /// <summary>自定义背景图；空串＝用主题色渐变。可以是绝对路径，也可以是 `preset:名字`。</summary>
        public static string Background = "";

        /// <summary>玻璃面板的磨砂半径（背景图本身不糊）。</summary>
        public static double Frost = FrostDefault;

        /// <summary>背景压暗（0..DimMax）。</summary>
        public static double Dim = 0.0;

        /// <summary>主题色（#RRGGBB）。</summary>
        public static string Accent = AccentDefault;

        /// <summary>本轮有没有改过 —— 界面在关掉设置时按它决定要不要落盘。</summary>
        public static bool Dirty;

        /// <summary>
        /// ★★ 外观任何一项改了 —— **广播**一声，主窗口立刻重画背景 / 重设磨砂。
        ///
        /// 为什么要广播而不是让设置窗口自己去摸主窗口：磨砂那一项不是"改一个属性"，
        /// 而是要写 `GlassSurface.GlobalFrost`（它内部再广播给每一块活着的玻璃）。
        /// 从设置窗口直接摸主窗口会把两个窗口的引用搅在一起；广播只传"变了"这个事实。
        /// </summary>
        public static event Action Changed;

        /// <summary>改完一项之后调它：置脏 + 广播。</summary>
        public static void NotifyChanged()
        {
            Dirty = true;
            Action handler = Changed;
            if (handler != null) handler();
        }

        public static double Clamp(double v, double lo, double hi)
        {
            if (v < lo) return lo;
            if (v > hi) return hi;
            return v;
        }

        public static bool IsHexColor(string s)
        {
            if (string.IsNullOrEmpty(s) || s[0] != '#') return false;
            string body = s.Substring(1);
            if (body.Length != 6) return false;
            for (int k = 0; k < body.Length; k++)
                if (!Uri.IsHexDigit(body[k])) return false;
            return true;
        }

        public static Color AccentColor
        {
            get
            {
                try { return (Color)ColorConverter.ConvertFromString(Accent); }
                catch (Exception) { return (Color)ColorConverter.ConvertFromString(AccentDefault); }
            }
        }

        // ==================================================== 预设背景（照启动器）
        public sealed class Preset
        {
            public string Tag;      // 存进配置的名字
            public string Title;    // 界面上给人看的
            public Color A;
            public Color B;
            public string File;     // exe 旁边 `背景图\` 里的可选真图
        }

        /// <summary>exe 旁边那个可选素材目录（放真图就用真图，没放就按同色系现画）。</summary>
        public static string PresetDir
        {
            get { return Path.Combine(Paths.Base, "背景图"); }
        }

        /// <summary>
        /// 预设背景。★ 与启动器一致：**素材可选** —— exe 旁边 `背景图\` 里放了
        /// `预设1-夜色.jpg` 之类就用真图；没放就按同色系现画一张渐变，界面照样能用。
        /// </summary>
        public static List<Preset> Presets()
        {
            var list = new List<Preset>();
            string dir = PresetDir;
            string[,] defs =
            {
                { "preset:夜色", "夜色（深蓝）", "#FF0B1424", "#FF0A0D16" },
                { "preset:星海", "星海（青紫）", "#FF12203A", "#FF1B1030" },
                { "preset:暖阳", "暖阳（橘粉）", "#FF3A1F1B", "#FF231018" },
                { "preset:森绿", "森绿（青绿）", "#FF0C2420", "#FF0A1418" },
                { "preset:素白", "素白（浅灰）", "#FF2A2F3A", "#FF14171E" },
                { "preset:墨黑", "墨黑（纯深）", "#FF0A0A0C", "#FF05060A" },
            };
            for (int i = 0; i < defs.GetLength(0); i++)
            {
                string tag = defs[i, 0];
                string name = tag.Substring("preset:".Length);
                string file = Path.Combine(dir, "预设" + (i + 1) + "-" + name + ".jpg");
                if (!File.Exists(file)) file = Path.Combine(dir, name + ".jpg");
                Color a, b;
                try { a = (Color)ColorConverter.ConvertFromString(defs[i, 2]); }
                catch (Exception) { a = Colors.Black; }
                try { b = (Color)ColorConverter.ConvertFromString(defs[i, 3]); }
                catch (Exception) { b = Colors.Black; }
                list.Add(new Preset
                {
                    Tag = tag,
                    Title = defs[i, 1],
                    A = a,
                    B = b,
                    File = File.Exists(file) ? file : "",
                });
            }
            return list;
        }

        /// <summary>把 `preset:xxx` 解析成 `背景图\` 里的真图；本地路径原样返回。</summary>
        public static string ResolveBackgroundPath(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "";
            if (!value.StartsWith("preset:", StringComparison.Ordinal)) return value;
            foreach (Preset p in Presets())
                if (string.Equals(p.Tag, value, StringComparison.OrdinalIgnoreCase))
                    return p.File;
            return "";
        }

        /// <summary>界面上如实说"现在用的是哪张 / 哪套"。</summary>
        public static string DescribeBackground()
        {
            if (string.IsNullOrWhiteSpace(Background)) return "主题色渐变（默认）";
            if (Background.StartsWith("preset:", StringComparison.Ordinal))
            {
                foreach (Preset p in Presets())
                    if (string.Equals(p.Tag, Background, StringComparison.OrdinalIgnoreCase))
                        return p.File.Length > 0 ? (p.Title + "（用 背景图\\ 里的真图）") : (p.Title + "（现画渐变）");
                return Background;
            }
            return Background;
        }

        /// <summary>读一张背景图（读不出来回 null，调用方回落渐变）。</summary>
        public static ImageSource LoadBackground(string value)
        {
            string path = ResolveBackgroundPath(value);
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
            try
            {
                var img = new BitmapImage();
                img.BeginInit();
                img.CacheOption = BitmapCacheOption.OnLoad;
                img.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
                img.UriSource = new Uri(path, UriKind.Absolute);
                img.EndInit();
                img.Freeze();
                return img;
            }
            catch (Exception)
            {
                return null;
            }
        }

        // ==================================================== 主题色工具（照启动器）
        /// <summary>两色混合。</summary>
        public static Color Mix(Color a, Color b, double t)
        {
            return Color.FromRgb(
                (byte)Math.Round(a.R + (b.R - a.R) * t),
                (byte)Math.Round(a.G + (b.G - a.G) * t),
                (byte)Math.Round(a.B + (b.B - a.B) * t));
        }

        /// <summary>主题色的暖色搭配（色相转 +150°、提亮成粉彩）—— 照启动器 `WarmOf`。</summary>
        public static Color WarmOf(Color color)
        {
            double r = color.R / 255.0, g = color.G / 255.0, b = color.B / 255.0;
            double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
            double l = (max + min) / 2.0;
            double h, s = 0;
            if (Math.Abs(max - min) < 0.0001) { h = 330; }
            else
            {
                double d = max - min;
                s = l > 0.5 ? d / (2 - max - min) : d / (max + min);
                if (Math.Abs(max - r) < 0.0001) h = (g - b) / d + (g < b ? 6 : 0);
                else if (Math.Abs(max - g) < 0.0001) h = (b - r) / d + 2;
                else h = (r - g) / d + 4;
                h *= 60;
            }
            h = (h + 150) % 360;
            s = Math.Min(1.0, s * 0.85 + 0.15);
            l = Math.Min(0.86, Math.Max(0.70, l * 0.5 + 0.45));
            return FromHsl(h, s, l);
        }

        public static Color FromHsl(double h, double s, double l)
        {
            double c = (1 - Math.Abs(2 * l - 1)) * s;
            double x = c * (1 - Math.Abs((h / 60.0) % 2 - 1));
            double m = l - c / 2;
            double r = 0, g = 0, b = 0;
            if (h < 60) { r = c; g = x; }
            else if (h < 120) { r = x; g = c; }
            else if (h < 180) { g = c; b = x; }
            else if (h < 240) { g = x; b = c; }
            else if (h < 300) { r = x; b = c; }
            else { r = c; b = x; }
            return Color.FromRgb(
                (byte)Math.Round((r + m) * 255),
                (byte)Math.Round((g + m) * 255),
                (byte)Math.Round((b + m) * 255));
        }

        /// <summary>透明版（做光晕用）。</summary>
        public static Color Alpha(Color c, byte a)
        {
            return Color.FromArgb(a, c.R, c.G, c.B);
        }

        /// <summary>数值转字符串（配置里写稳定的小数）。</summary>
        public static string Num(double v)
        {
            return v.ToString("0.##", CultureInfo.InvariantCulture);
        }

        public static double ParseNum(string s, double fallback)
        {
            double v;
            if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v))
                return v;
            return fallback;
        }
    }
}
