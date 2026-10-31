using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DiyEditor
{
    /// <summary>
    /// ★★ **内置缓存**：把「棋盘缓存\」（26 份官方画板 json）与「贴图缓存\」（159 张 PNG
    ///   + 2 份清单 txt）**编进 exe 自己**，于是"只有 exe 一个文件、放在任何空目录里"
    ///   也能画出棋盘和图标（用户 2026-10-12 的要求：**要内置**）。
    ///
    /// 为什么要有它（用户实报的痛点）：
    ///   以前这两份缓存是**磁盘目录**（`Paths.Under("棋盘缓存")` / `Paths.Under("贴图缓存")`），
    ///   交付包只放了 exe ⇒ 新建关卡时**棋盘一片空白**（`BoardStore.LoadCore` 找不到
    ///   `棋盘缓存\023.json`，只回一句"把 boards\023.json 拷进「棋盘缓存」"）。
    ///
    /// ★★ **读取顺序：磁盘优先，内置兜底**（用户明确要求）：
    ///   ① 磁盘上有 `棋盘缓存\xxx.json` / `贴图缓存\xxx.png` ⇒ **用磁盘那一份**
    ///      （用户可以覆盖 / 更新 / 删掉重来，行为跟以前一模一样）；
    ///   ② 磁盘上没有 ⇒ 从 exe 的资源里取内置的那一份。
    ///   ⇒ **一个字节都不往用户目录里落**（不解包、不生成"缓存副本"）——
    ///     内置的那份永远只是"读"的来源，磁盘才是唯一的可写事实。
    ///
    /// ★ 体积：内置的是**原样字节**（PNG 已经是压缩格式，再套一层 zip/deflate 只省几个百分点，
    ///   却要额外的解压代码 + 内存峰值）⇒ 直接 `EmbeddedResource` 嵌进 exe，
    ///   代价就是 exe 长大 ~34 MB（实测见交付报告）。
    ///
    /// ★★ **资源名的形状**（踩过一次的坑，写在这儿别再踩）：
    ///   csproj 里的 `LogicalName` 写的是 `DiyEditor.Cache.棋盘缓存.%(Filename)%(Extension)`
    ///   —— MSBuild 用的是 **`.` 当分隔**（跟 `RootNamespace.子目录.文件` 的老规矩一样），
    ///   **不是 `/`**。所以本类里的"逻辑名"一律用 <see cref="Sep"/> = `.`：
    ///   `棋盘缓存.023.json` / `贴图缓存.UT_Platform_Gift.png`。
    ///   （一开始按 `/` 拼，结果 `GetManifestResourceNames()` 里明明有 187 条、
    ///     这里却一条都匹配不上 ⇒ 内置等于没装。）
    ///
    /// ★ 找不到 / 读不出**绝不静默**：见 <see cref="Warnings"/>（自测里当判据用）。
    /// </summary>
    public static class EmbeddedCache
    {
        /// <summary>资源前缀（`&lt;LogicalName&gt;` 的公共头；见 csproj 里那两条 `EmbeddedResource`）。</summary>
        public const string Prefix = "DiyEditor.Cache.";

        /// <summary>逻辑名里的分隔符（★ MSBuild 用 `.`，不是路径分隔符）。</summary>
        public const char Sep = '.';

        /// <summary>内置棋盘缓存的逻辑目录名（和磁盘上的同名）。</summary>
        public const string BoardFolder = "棋盘缓存";

        /// <summary>内置贴图缓存的逻辑目录名（和磁盘上的同名）。</summary>
        public const string ArtFolder = "贴图缓存";

        /// <summary>
        /// ★★ 「这一张贴图来自内置缓存」的**内部标记前缀**。
        ///
        /// `Art.ImagePath(name)` 以前一律回"磁盘上的绝对路径"（调用方 `Art.Load` 用
        /// `new Uri(path)` 读）。内置的图**没有磁盘路径**，所以这里约定一个**绝不会和真路径撞**
        /// 的标记串：`@内置贴图\&lt;贴图名&gt;.png`。
        ///   · 判据别自己拼字符串，用 <see cref="IsBuiltInPath"/> / <see cref="BuiltInName"/>；
        ///   · 对外表现（`Path.GetFileName` 拿得到名字、`File.Exists` 一律 false）⇒ 不会误导别的代码当真文件用。
        /// </summary>
        public const string BuiltInMarker = "@内置贴图\\";

        // ==================================================================== 资源清单（懒加载一次）
        private static Dictionary<string, string> _index;                  // 逻辑名（"贴图缓存.UT_x.png"）→ 真资源名
        private static string[] _boards;                                    // 棋盘缓存里的后缀（"023"；已排序）
        private static readonly object _lock = new object();

        private static void EnsureIndex()
        {
            if (_index != null) return;
            lock (_lock)
            {
                if (_index != null) return;
                var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                string[] all = new string[0];
                try
                {
                    Assembly asm = typeof(EmbeddedCache).Assembly;
                    all = asm.GetManifestResourceNames();
                    string head = Prefix + BoardFolder + Sep;
                    string head2 = Prefix + ArtFolder + Sep;
                    foreach (string full in all)
                    {
                        if (full == null || !full.StartsWith(Prefix, StringComparison.Ordinal)) continue;
                        string logical = full.Substring(Prefix.Length);
                        // ★ 只收「棋盘缓存」「贴图缓存」这两族的（别的资源——例如 WPF 的
                        //   `*.g.resources` / 字体——不进索引，免得跟贴图名撞）。
                        if (!full.StartsWith(head, StringComparison.Ordinal)
                            && !full.StartsWith(head2, StringComparison.Ordinal)) continue;
                        if (logical.Length == 0) continue;
                        map[logical] = full;
                    }
                }
                catch (Exception ex) { Warn("枚举 exe 资源失败：" + ex.GetType().Name + " " + ex.Message); }
                _index = map;
                string boardHead = BoardFolder + Sep;
                var boards = new List<string>();
                foreach (string logical in map.Keys)
                    if (logical.StartsWith(boardHead, StringComparison.OrdinalIgnoreCase)
                        && logical.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                        boards.Add(logical.Substring(boardHead.Length, logical.Length - boardHead.Length - 5));
                boards.Sort(StringComparer.OrdinalIgnoreCase);
                _boards = boards.ToArray();
            }
        }

        /// <summary>拼一个逻辑名（`folder` + `file`）；非法回 null。</summary>
        private static string Logical(string folder, string file)
        {
            if (string.IsNullOrEmpty(folder) || string.IsNullOrEmpty(file)) return null;
            string f = file.Replace('\\', '/').TrimStart('/');
            int cut = f.LastIndexOf('/');
            if (cut >= 0) f = f.Substring(cut + 1);          // 只取文件名那一段
            if (f.Length == 0 || f.IndexOf("..", StringComparison.Ordinal) >= 0) return null;
            return folder + Sep + f;
        }

        /// <summary>这一份逻辑名在 exe 里有没有内置（`Warn` 时记一笔，供自测/报告用）。</summary>
        public static bool Has(string folder, string file)
        {
            string logical = Logical(folder, file);
            if (logical == null) { Warn("逻辑名不合法：" + folder + " / " + file); return false; }
            EnsureIndex();
            string full;
            if (!_index.TryGetValue(logical, out full)) { Warn("内置缓存里没有 " + logical); return false; }
            return true;
        }

        /// <summary>这个逻辑名（形如 `棋盘缓存.023.json`）在不在内置里（不记账，纯查）。</summary>
        public static bool HasLogical(string logical)
        {
            if (string.IsNullOrEmpty(logical)) return false;
            EnsureIndex();
            return _index.ContainsKey(logical);
        }

        /// <summary>打开内置资源的流（没有回 null；流由调用方释放）。</summary>
        public static Stream Open(string folder, string file)
        {
            string logical = Logical(folder, file);
            if (logical == null) { Warn("逻辑名不合法：" + folder + " / " + file); return null; }
            EnsureIndex();
            string full;
            if (!_index.TryGetValue(logical, out full)) { Warn("内置缓存里没有 " + logical); return null; }
            return OpenLogical(full, logical);
        }

        private static Stream OpenLogical(string fullName, string logicalForWarn)
        {
            try
            {
                Stream s = typeof(EmbeddedCache).Assembly.GetManifestResourceStream(fullName);
                if (s == null) { Warn("内置资源打不开：" + logicalForWarn); return null; }
                return s;
            }
            catch (Exception ex) { Warn("读内置资源失败：" + logicalForWarn + "（" + ex.GetType().Name + "）"); return null; }
        }

        /// <summary>内置文本（棋盘 json / 清单 txt）；没有回 null。</summary>
        public static string Text(string folder, string file)
        {
            using (Stream s = Open(folder, file))
            {
                if (s == null) return null;
                try
                {
                    using (var r = new StreamReader(s, new UTF8Encoding(false), true))
                        return r.ReadToEnd();
                }
                catch (Exception ex) { Warn("读内置文本失败：" + folder + Sep + file + "（" + ex.GetType().Name + "）"); return null; }
            }
        }

        /// <summary>把逻辑名的尾段解析成字节（棋盘 json 要按原样拷给用户那份 `.board.json` 用）。</summary>
        public static byte[] Bytes(string folder, string file)
        {
            using (Stream s = Open(folder, file))
            {
                if (s == null) return null;
                try
                {
                    var ms = new MemoryStream();
                    s.CopyTo(ms);
                    return ms.ToArray();
                }
                catch (Exception ex) { Warn("读内置字节失败：" + folder + Sep + file + "（" + ex.GetType().Name + "）"); return null; }
            }
        }

        /// <summary>内置棋盘缓存里的全部画板后缀（`"023"` 这种；已排序）。</summary>
        public static string[] BoardNames
        {
            get { EnsureIndex(); return (string[])_boards.Clone(); }
        }

        // ==================================================================== 内置贴图
        /// <summary>内置贴图缓存里有 `name + ".png"` 吗（`name` 就是 `Art.ImagePath` 那个贴图名）。</summary>
        public static bool HasTexture(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            return Has(ArtFolder, name + ".png");
        }

        /// <summary>「这一张来自内置」的标记串（`Art.ImagePath` 用；见 <see cref="BuiltInMarker"/>）。</summary>
        public static string BuiltInPath(string name)
        {
            return BuiltInMarker + name + ".png";
        }

        /// <summary>这个路径是不是"内置标记串"（不是真文件路径）。</summary>
        public static bool IsBuiltInPath(string path)
        {
            return !string.IsNullOrEmpty(path)
                && path.StartsWith(BuiltInMarker, StringComparison.Ordinal);
        }

        /// <summary>从内置标记串里取回贴图名（不是标记串就回 null）。</summary>
        public static string BuiltInName(string path)
        {
            if (!IsBuiltInPath(path)) return null;
            string s = path.Substring(BuiltInMarker.Length);
            int cut = s.LastIndexOf('\\');
            if (cut >= 0) s = s.Substring(cut + 1);
            if (s.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) s = s.Substring(0, s.Length - 4);
            return s.Length == 0 ? null : s;
        }

        /// <summary>
        /// ★ 从内置资源里解一张图（`OnLoad` 语义：读完就脱手，不占文件句柄）。
        ///   读不出回 null（并记 <see cref="Warnings"/>），调用方照旧回落色块。
        ///
        /// ★ 为什么先拷进 `MemoryStream` 再解（踩过的坑）：直接把
        ///   `GetManifestResourceStream` 那个**程序集资源流**喂给 `BitmapImage.StreamSource`
        ///   会抛 `ArgumentNullException`（WPF 解 PNG 时的那条链要一个可 Seek 的托管流，
        ///   程序集资源流不满足）。拷一份进内存就稳了 —— 一张图几十上百 KB，
        ///   解完 `MemoryStream` 就释放（`OnLoad` 已经把像素读进内存了）。
        /// </summary>
        public static ImageSource LoadTexture(string name)
        {
            using (Stream s = Open(ArtFolder, name + ".png"))
            {
                if (s == null) return null;
                try
                {
                    byte[] bytes;
                    using (var ms = new MemoryStream())
                    {
                        s.CopyTo(ms);
                        bytes = ms.ToArray();
                    }
                    // ★ 用 `BitmapFrame`（解码器那条链）而不是 `BitmapImage.StreamSource`：
                    //   后者在"程序集资源流/内存流"上会抛 `ArgumentNullException(key)`
                    //   （WPF 内部那次字典查找的键是空的），换成 `BitmapFrame.Create` 就没事。
                    var mem = new MemoryStream(bytes, false);
                    try
                    {
                        BitmapSource frame = BitmapFrame.Create(mem,
                            BitmapCreateOptions.IgnoreImageCache, BitmapCacheOption.OnLoad);
                        frame.Freeze();
                        return frame;
                    }
                    finally { mem.Dispose(); }
                }
                catch (Exception ex)
                {
                    Warn("内置贴图解不开：" + name + "（" + ex.GetType().Name + " " + ex.Message + "）");
                    return null;
                }
            }
        }

        // ==================================================================== 提示 / 计数
        private static readonly List<string> _warnings = new List<string>();
        private static readonly HashSet<string> _warnedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// ★★ **绝不静默**：内置缓存"该有却没有 / 读不出"时的记账（去重，同一件事只记一次）。
        ///   自测把它当判据（见 `TestRunner` 的"内置缓存"那一段），本类也会打一行到控制台。
        /// </summary>
        public static List<string> Warnings
        {
            get { lock (_warnedKeys) return new List<string>(_warnings); }
        }

        /// <summary>记一笔"内置缓存这里不对"（去重）。</summary>
        public static void Warn(string what)
        {
            if (string.IsNullOrEmpty(what)) return;
            lock (_warnedKeys)
            {
                if (!_warnedKeys.Add(what)) return;
                if (_warnings.Count < 200) _warnings.Add(what);
            }
            try { Console.WriteLine("   [内置缓存] " + what); } catch (Exception) { }
        }

        /// <summary>自测换目录 / 复核用：把"记过的账"清掉。</summary>
        public static void ClearWarnings()
        {
            lock (_warnedKeys) { _warnings.Clear(); _warnedKeys.Clear(); }
        }

        /// <summary>报告用：内置缓存到底装了多少东西（份数 + 解出来的总字节）。</summary>
        public static string Describe()
        {
            EnsureIndex();
            int png = 0, other = 0;
            long artBytes = 0, boardBytes = 0;
            string artHead = ArtFolder + Sep;
            string boardHead = BoardFolder + Sep;
            foreach (KeyValuePair<string, string> kv in _index)
            {
                long len = 0;
                try
                {
                    using (Stream s = typeof(EmbeddedCache).Assembly.GetManifestResourceStream(kv.Value))
                        if (s != null) len = s.Length;
                }
                catch (Exception) { }
                if (kv.Key.StartsWith(boardHead, StringComparison.OrdinalIgnoreCase)) { boardBytes += len; continue; }
                if (kv.Key.StartsWith(artHead, StringComparison.OrdinalIgnoreCase))
                {
                    if (kv.Key.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) png++; else other++;
                    artBytes += len;
                }
            }
            return "内置：棋盘 " + (_boards == null ? 0 : _boards.Length) + " 份 json（" + Mb(boardBytes) + "）· 贴图 "
                 + png + " 张 png（" + Mb(artBytes) + "）" + (other > 0 ? (" + " + other + " 份清单") : "")
                 + " ⇒ 合计 " + Mb(boardBytes + artBytes);
        }

        /// <summary>字节数 → "33.6 MB" 这种给人看的形式。</summary>
        public static string Mb(long bytes)
        {
            double mb = bytes / 1048576.0;
            return mb.ToString("0.0", CultureInfo.InvariantCulture) + " MB";
        }

        // ==================================================================== 小工具

        /// <summary>棋盘缓存里"这份后缀"对应的逻辑名（`023` → `棋盘缓存.023.json`）。</summary>
        public static string BoardLogicalName(string suffix)
        {
            if (string.IsNullOrEmpty(suffix)) return null;
            return BoardFolder + Sep + suffix + ".json";
        }
    }

    /// <summary>
    /// ★★ **棋盘缓存**（`5-DIY关卡\棋盘缓存\*.json` 那 26 份官方画板）—— **磁盘优先 / 内置兜底**。
    ///
    /// 为什么要有这一层（而不是各处自己 `File.Exists(CacheDir + ...)`）：
    ///   "去哪儿找棋盘"这件事原来散在 `BoardModel.LoadCore` / `BoardStore.CacheDir`
    ///   / `LevelDoc` 的三处 / `GameTables.EnsureBoardInfo` / 自测的两处循环里，
    ///   每处都只认磁盘 ⇒ 内置之后**很容易漏一处**（漏掉的那处就是"一片空白"）。
    ///   ⇒ 统一收口到这里：**枚举**（<see cref="EnumerateJson"/>）与**读文本**
    ///   （<see cref="TextOf"/> / <see cref="BytesOf"/> / <see cref="FindPath"/> / <see cref="FindText"/>）
    ///   都先查磁盘、再回内置。
    ///
    /// ★ 落点（磁盘）＝ `BoardStore.CacheDir`：跟着 exe 走，开发时从 `bin\...` 往上找到
    ///   `5-DIY关卡\棋盘缓存`。磁盘上没有这个目录**不算错**（内置兜着）。
    /// </summary>
    public static class BoardCache
    {
        /// <summary>磁盘上的棋盘缓存目录（可能不存在；不存在时各处不再当"环境缺失"报错）。</summary>
        public static string Dir { get { return BoardStore.CacheDir; } }

        /// <summary>磁盘上有没有这个目录（报告/提示用）。</summary>
        public static bool DirExists
        {
            get
            {
                try { return Directory.Exists(Dir); }
                catch (Exception) { return false; }
            }
        }

        /// <summary>
        /// ★ 枚举"现在手上能用的全部棋盘 json"：**磁盘那份（若在）在前，内置那些在后**，按名字去重。
        ///   自测 / 模板扫描走它 —— 于是"只有 exe"的形态下也扫得到那 26 块官方画板。
        ///   返回值可能是**真路径**，也可能是**内置标记路径**（见 <see cref="Marker"/>）。
        /// </summary>
        public static List<string> EnumerateJson()
        {
            var list = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            try
            {
                string dir = Dir;
                if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                    foreach (string f in Directory.GetFiles(dir, "*.json"))
                    {
                        string stem = Stem(f);
                        if (stem.Length > 0 && seen.Add(stem)) list.Add(f);
                    }
            }
            catch (Exception) { }

            foreach (string suffix in EmbeddedCache.BoardNames)
            {
                if (suffix.Length == 0) continue;
                if (seen.Add(suffix)) list.Add(Marker(suffix));
            }
            return list;
        }

        /// <summary>
        /// ★ 枚举 + **每一份到底是"磁盘那份"还是"内置那份"**：先按后缀问 <see cref="FindPath"/>
        ///   （它就是"磁盘优先 / 内置兜底"那条判据），再交给 <see cref="EnumerateJson"/>
        ///   把"文件名对不上画板号"的磁盘 json 也带上（那种只有磁盘才有，一律算磁盘）。
        ///   自测/界面要**如实报来源**时用它（别用 `EnumerateJson` 的返回值猜）。
        /// </summary>
        public static List<KeyValuePair<string, bool>> EnumerateWithOrigin()
        {
            var list = new List<KeyValuePair<string, bool>>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            try
            {
                string dir = Dir;
                if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                    foreach (string f in Directory.GetFiles(dir, "*.json"))
                    {
                        string stem = Stem(f);
                        if (stem.Length == 0 || !seen.Add(stem)) continue;
                        // 磁盘上有这个文件 ⇒ 就是磁盘那份（`FindPath` 也会说磁盘）
                        list.Add(new KeyValuePair<string, bool>(f, false));
                    }
            }
            catch (Exception) { }

            foreach (string suffix in EmbeddedCache.BoardNames)
            {
                if (suffix.Length == 0 || !seen.Add(suffix)) continue;
                string p = FindPath(suffix);                       // 磁盘优先 / 内置兜底
                if (p.Length == 0) p = Marker(suffix);
                list.Add(new KeyValuePair<string, bool>(p, IsBuiltIn(p)));
            }
            return list;
        }

        /// <summary>内置那份的标记路径（磁盘上没有时用它当"身份"，别当真文件路径用）。</summary>
        public static string Marker(string suffix)
        {
            if (string.IsNullOrEmpty(suffix)) return "";
            return EmbeddedCache.BoardFolder + Path.DirectorySeparatorChar + suffix + ".json";
        }

        /// <summary>这份路径 / 标记串是不是内置的那一份（磁盘上真有这个文件就不算）。</summary>
        public static bool IsBuiltIn(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            bool exists = false;
            try { exists = File.Exists(path); }
            catch (Exception) { }
            if (exists) return false;
            return path.StartsWith(EmbeddedCache.BoardFolder + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>给人看的"在哪"标签（界面上报"棋盘图：023.json（内置）"）。</summary>
        public static string Label(string path)
        {
            if (string.IsNullOrEmpty(path)) return "(没有)";
            return IsBuiltIn(path) ? "内置" : "磁盘";
        }

        /// <summary>
        /// ★★ 读一份棋盘的原文：先看是不是"磁盘上真有这个文件"的路径，再按后缀查磁盘缓存目录，
        ///   最后回内置。都没有回 null（调用方自己给提示）。
        /// </summary>
        public static string TextOf(string pathOrSuffix)
        {
            if (string.IsNullOrEmpty(pathOrSuffix)) return null;

            // ① 传进来的就是一份真路径（DIY 自带棋盘 / 用户指定的目录）—— 磁盘优先
            try { if (File.Exists(pathOrSuffix)) return File.ReadAllText(pathOrSuffix, Encoding.UTF8); }
            catch (Exception) { }

            string suffix = Stem(pathOrSuffix);

            // ② 磁盘缓存目录里按后缀找
            if (suffix.Length > 0)
            {
                string cached = TargetPath(suffix);
                if (cached != null)
                {
                    try { if (File.Exists(cached)) return File.ReadAllText(cached, Encoding.UTF8); }
                    catch (Exception) { }
                }
            }

            // ③ 内置兜底
            return suffix.Length == 0 ? null : EmbeddedCache.Text(EmbeddedCache.BoardFolder, suffix + ".json");
        }

        /// <summary>同上，但要**字节**（保存 `.board.json` 时按原样拷）。</summary>
        public static byte[] BytesOf(string pathOrSuffix)
        {
            if (string.IsNullOrEmpty(pathOrSuffix)) return null;
            try { if (File.Exists(pathOrSuffix)) return File.ReadAllBytes(pathOrSuffix); }
            catch (Exception) { }

            string suffix = Stem(pathOrSuffix);
            if (suffix.Length == 0) return null;
            string cached = TargetPath(suffix);
            if (cached != null)
            {
                try { if (File.Exists(cached)) return File.ReadAllBytes(cached); }
                catch (Exception) { }
            }
            return EmbeddedCache.Bytes(EmbeddedCache.BoardFolder, suffix + ".json");
        }

        /// <summary>磁盘上这份后缀对应的路径（`&lt;棋盘缓存&gt;\023.json`；目录算不出来回 null）。</summary>
        public static string TargetPath(string suffix)
        {
            if (string.IsNullOrEmpty(suffix)) return null;
            string dir = Dir;
            if (string.IsNullOrEmpty(dir)) return null;
            return Path.Combine(dir, suffix + ".json");
        }

        /// <summary>
        /// ① 磁盘上有就回磁盘路径；② 否则若内置里有就回**内置标记路径**；③ 都没有回空串。
        ///   （`LevelDoc` 那几处"该照着哪份棋盘算"用它。）
        /// </summary>
        public static string FindPath(string suffix)
        {
            if (string.IsNullOrEmpty(suffix)) return "";
            string p = TargetPath(suffix);
            if (p != null)
            {
                try { if (File.Exists(p)) return p; }
                catch (Exception) { }
            }
            if (EmbeddedCache.HasLogical(EmbeddedCache.BoardLogicalName(suffix))) return Marker(suffix);
            return "";
        }

        /// <summary>
        /// ★★ **合成一份"底版"的原文**（`LevelDoc.BoardBytes` 用）：磁盘 / 内置都行，
        ///   而且**除了"磁盘上就是目标那份文件"之外一律走内存** ——
        ///   内置的那份没有磁盘路径，落盘必须用 `ExportService.WriteAtomic` 写字节。
        ///
        /// 返回 true 时 <paramref name="text"/> 一定有值，<paramref name="diskPath"/> 只有
        /// "来源是磁盘文件"时才有值（内置来源时是空串，调用方据此选落盘方式）。
        /// </summary>
        public static bool TryReadSource(string ownPath, string suffix, out string text, out string diskPath)
        {
            text = null;
            diskPath = "";

            if (!string.IsNullOrEmpty(ownPath))
            {
                try
                {
                    if (File.Exists(ownPath))
                    {
                        text = File.ReadAllText(ownPath, Encoding.UTF8);
                        diskPath = ownPath;
                        return true;
                    }
                }
                catch (Exception) { }
            }

            if (string.IsNullOrEmpty(suffix)) return false;

            string cached = TargetPath(suffix);
            if (cached != null)
            {
                try
                {
                    if (File.Exists(cached))
                    {
                        text = File.ReadAllText(cached, Encoding.UTF8);
                        diskPath = cached;
                        return true;
                    }
                }
                catch (Exception) { }
            }

            text = EmbeddedCache.Text(EmbeddedCache.BoardFolder, suffix + ".json");
            return text != null;
        }

        /// <summary>`棋盘缓存\023.json` / `棋盘缓存.023.json` / `023` / `23` → `023` 这种"文件名主体"。</summary>
        public static string Stem(string pathOrSuffix)
        {
            if (string.IsNullOrEmpty(pathOrSuffix)) return "";
            string s = pathOrSuffix.Replace('/', Path.DirectorySeparatorChar);
            int cut = s.LastIndexOf(Path.DirectorySeparatorChar);
            if (cut >= 0) s = s.Substring(cut + 1);
            if (s.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) s = s.Substring(0, s.Length - 5);
            return s.Trim();
        }
    }
}
