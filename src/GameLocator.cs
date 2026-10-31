using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace DiyEditor
{
    /// <summary>
    /// ★ 游戏在哪 / 客户端资源在哪 —— **只读探测，并且都允许手动覆盖**。
    ///
    /// 探测顺序（先找到先用）：
    ///   ① 用户手动指定的（`编辑器配置.json` 里存 game_dir，或页面上"指定游戏目录…"）；
    ///   ② 工作副本里现成的登记：`4-服务端\client\launcher.json` 的 `game_dir`
    ///      —— ★ 这是"一处定义两边共用"的那一份（启动器/客户端也认它）；
    ///   ③ 从 exe 往上找 `4-服务端\client\launcher.json` 再读同一个键。
    ///
    /// ★ 一律**只读**：GameRes\Img 只拿来当贴图源；`DIY关卡` 是**要写**的那个目录，
    ///   但它属于"用户自己的 DIY 数据"，不是客户端资源，也不是服务端数据。
    /// </summary>
    public static class GameLocator
    {
        private static string _gameDir;
        private static bool _looked;

        /// <summary>用户手动指定（会存进 编辑器配置.json）。</summary>
        public static string Override { get; set; }

        public static string GameDir
        {
            get
            {
                if (!_looked) { _looked = true; _gameDir = Find(); }
                return _gameDir;
            }
        }

        public static void Reset()
        {
            _looked = false;
            _gameDir = null;
            InvalidateTableDir();     // ★ 表目录跟着游戏根走（配置那一支不受影响，它在 FindTableDir 里最优先）
        }

        private static string Find()
        {
            if (!string.IsNullOrEmpty(Override) && LooksLikeGame(Override)) return Override;

            // ② / ③ 从 exe 往上找 4-服务端\client\launcher.json
            string dir = Paths.Base.TrimEnd('\\');
            for (int k = 0; k < 10 && !string.IsNullOrEmpty(dir); k++)
            {
                string cfg = Path.Combine(dir, Path.Combine("4-服务端", Path.Combine("client", "launcher.json")));
                string got = ReadGameDir(cfg);
                if (got != null) return got;
                dir = Path.GetDirectoryName(dir);
            }
            // 兜底：工作副本根（开发机常见）
            string direct = @"D:\小小兔趴\4-服务端\client\launcher.json";
            return ReadGameDir(direct);
        }

        private static string ReadGameDir(string launcherJson)
        {
            try
            {
                if (!File.Exists(launcherJson)) return null;
                JNode root = DiyJson.Parse(File.ReadAllText(launcherJson, new UTF8Encoding(false)));
                JNode g = DiyJson.Member(root, "game_dir");
                if (g != null && g.Kind == JKind.String && LooksLikeGame(g.Str)) return g.Str;
            }
            catch (Exception) { }
            return null;
        }

        /// <summary>"看着像游戏根"：有 exe 或者有 GameRes 或者有 _Data 目录。</summary>
        public static bool LooksLikeGame(string dir)
        {
            try
            {
                if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return false;
                return File.Exists(Path.Combine(dir, "AstralParty_CN.exe"))
                    || Directory.Exists(Path.Combine(dir, "GameRes"))
                    || Directory.Exists(Path.Combine(dir, "AstralParty_CN_Data"));
            }
            catch (Exception) { return false; }
        }

        // ================================================== 客户端贴图（只读）
        /// <summary>`<游戏根>\GameRes\Img` —— 实测有 2901 张拆好的 PNG。</summary>
        public static string ImageDir
        {
            get
            {
                string g = GameDir;
                if (string.IsNullOrEmpty(g)) return null;
                string dir = Path.Combine(g, Path.Combine("GameRes", "Img"));
                return Directory.Exists(dir) ? dir : null;
            }
        }

        /// <summary>
        /// ★★ 本站自己的**贴图缓存**（`5-DIY关卡\贴图缓存\`）—— 第 9 波 ② 加的。
        ///
        /// 为什么要有它：游戏目录里散着的那份 `GameRes\Img` 并不全（格子功能 11 种、
        ///   怪头像 80 只里 27 只、手牌 71 张里 27 张都没有 PNG）。官方那几张其实在
        ///   游戏自己的 Addressables 包里（`AstralParty_CN_Data\StreamingAssets\aa\StandaloneWindows64\*.bundle`），
        ///   由 `tools\export_art_cache.py` **只读**导出来存到这里。
        ///
        /// ★ 读盘顺序：**先磁盘上的缓存、再游戏目录，最后 exe 里内置的那一份**
        ///   （这样没装游戏 / 换了机器 / 只拿到一个 exe 也还认得出图标）。
        /// ★ 这个属性本身只报**磁盘目录**（`MapThumb` 要往里写渲染缓存）；
        ///   "内置兜底"在 <see cref="Image"/> / <see cref="ImageSource"/> 里做。
        /// </summary>
        public static string ArtCacheDir
        {
            get
            {
                string dir = Paths.Under("贴图缓存");
                return dir;
            }
        }

        /// <summary>
        /// ★★ 2026-10-17：**怪物立绘/图标缓存**（`5-DIY关卡\图标缓存\<怪物id>.png`）。
        ///
        /// 为什么单独一个目录：`贴图缓存` 是按**贴图名**存的（`UT_Monster_Bust_1005_0.png`），
        /// 而"每只怪一张立绘"这件事按**怪 id** 认最省事 —— 作者可以自己换图（把
        /// `图标缓存\1005.png` 换成任意一张），编辑器不用重编就跟着变。
        /// 这些图由 `tools\export_monster_portraits.py` 从游戏自己的 Addressables 包里
        /// **只读**抠出来（官方半身立绘），**不联网**。
        /// </summary>
        public static string IconCacheDir
        {
            get { return Paths.Under("图标缓存"); }
        }

        /// <summary>`图标缓存\<id>.png` 的绝对路径；没有回 null。</summary>
        public static string MonsterIconPath(int monsterId)
        {
            if (monsterId <= 0) return null;
            string dir = IconCacheDir;
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return null;
            string p = Path.Combine(dir, monsterId.ToString() + ".png");
            return File.Exists(p) ? p : null;
        }

        /// <summary>
        /// 怪物图标画哪一套：`pixel`（默认，图鉴像素小人）/ `portrait`（图标缓存里的立绘）。
        /// ★ 为什么默认像素小人：棋盘上的怪只有 **15 px**，立绘那种大图缩到 15 px 就是一坨色块；
        ///   像素画放大（NearestNeighbor）才认得清。想换成立绘在 `编辑器配置.json` 里写
        ///   `"monster_art": "portrait"` 即可，**不用重编 exe**。
        /// </summary>
        public static string MonsterArtMode = "pixel";

        public static bool PreferPortrait
        {
            get { return string.Equals(MonsterArtMode, "portrait", StringComparison.OrdinalIgnoreCase); }
        }

        /// <summary>磁盘上那张贴图的路径（先 `5-DIY关卡\贴图缓存\`，再 `<游戏根>\GameRes\Img`）；没有回 null。</summary>
        public static string ImageOnDisk(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;

            string cache = ArtCacheDir;
            if (!string.IsNullOrEmpty(cache) && Directory.Exists(cache))
            {
                string hit = Path.Combine(cache, name + ".png");
                if (File.Exists(hit)) return hit;
            }

            string dir = ImageDir;
            if (string.IsNullOrEmpty(dir)) return null;
            string path = Path.Combine(dir, name + ".png");
            return File.Exists(path) ? path : null;
        }

        /// <summary>
        /// 拼一张贴图的完整路径；都没有返回 null（调用方回落到色块 + 中文名）。
        /// ★★ 读取顺序（用户 2026-10-12 定的口径）：**磁盘优先、内置兜底** ——
        ///   ① `5-DIY关卡\贴图缓存\`（磁盘，用户可覆盖 / 更新 / 删掉）
        ///   ② `<游戏根>\GameRes\Img`（磁盘，游戏自己的那份）
        ///   ③ **exe 里内置的那份** ⇒ 返回 <see cref="EmbeddedCache.BuiltInPath"/> 标记串
        ///      （不是真文件路径；`Art.Load` 认得它，会从资源流里解图）。
        ///   ★ 内置的那份**绝不落盘**（不解包、不生成缓存副本）——磁盘才是唯一可写的事实。
        /// </summary>
        public static string Image(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;

            string disk = ImageOnDisk(name);
            if (disk != null) return disk;

            if (EmbeddedCache.HasTexture(name)) return EmbeddedCache.BuiltInPath(name);
            return null;
        }

        /// <summary>这张贴图是从哪来的（报告 / 界面如实说用）。</summary>
        public static string ImageSource(string name)
        {
            if (string.IsNullOrEmpty(name)) return "（没给名字）";
            string cache = ArtCacheDir;
            if (!string.IsNullOrEmpty(cache) && Directory.Exists(cache)
                && File.Exists(Path.Combine(cache, name + ".png"))) return "贴图缓存";
            string dir = ImageDir;
            if (!string.IsNullOrEmpty(dir) && File.Exists(Path.Combine(dir, name + ".png"))) return "游戏目录";
            if (EmbeddedCache.HasTexture(name)) return "内置贴图";
            return "没有";
        }

        // ================================================== 配置表（只读）
        /// <summary>
        /// ★★ 2026-10-17：表目录允许**配置** —— `编辑器配置.json` 的 `table_dir`。
        ///
        /// 为什么要它（用户拍板走这条）：编辑器原来只认两份写死的路径
        ///   `<游戏根>\server\data\gamedata` 与「从 exe 往上找 `4-服务端\server\data\gamedata`」。
        ///   那两份可能**不是最新的**：`4-服务端` 里那份是 2026-10-01 的，**Monster.bin 里
        ///   根本没有 1071~1074（森秋幻境那四只）** ⇒ 编辑器显示「不在 Monster.bin 里」、
        ///   没有名字/头像，而且「＋ 新增怪物（从怪物池里挑）」**列不出它们**
        ///   （`MonsterPickDialog.ForPool` 会把 `TableRow == null` 的过滤掉）。
        ///   与其去覆盖那份表（那会动到自测夹具、190/0 要重新校准），不如让编辑器**指得动**。
        ///
        /// ★ 口径（重要）：**配置优先；没配 / 配的路径不存在 ⇒ 一律回落到下面那套写死逻辑**
        ///   —— 自测、别人的机器、老配置的行为**一个字节都不变**。
        /// ★ 路径写法：绝对路径、相对路径（按 exe 所在目录算）、`%VAR%` 环境变量都认。
        /// </summary>
        public static string TableDirOverride { get; set; }

        private static string _tableDir;
        private static bool _tableDirLooked;
        private static string _tableDirSource = "";

        /// <summary>表目录是哪儿来的（"配置" / "默认" / "配置里的路径不存在 ⇒ 回落默认"）。</summary>
        public static string TableDirSource
        {
            get { string unused = TableDir; return _tableDirSource; }
        }

        /// <summary>给人看的一句话：`表目录：<路径>（来源：配置）`。界面与启动日志都用它，一眼可查。</summary>
        public static string TableDirLine
        {
            get
            {
                string p = TableDir;
                return "表目录：" + (string.IsNullOrEmpty(p) ? "（没找到）" : p)
                     + "（来源：" + (TableDirSource.Length == 0 ? "默认" : TableDirSource) + "）";
            }
        }

        /// <summary>把配置里的路径变成绝对路径（展开 `%VAR%`；相对路径按 exe 目录算）。</summary>
        public static string ExpandPath(string raw)
        {
            if (raw == null) return "";
            string s = raw.Trim().Trim('"');
            if (s.Length == 0) return "";
            try { s = Environment.ExpandEnvironmentVariables(s).Trim(); }
            catch (Exception) { }
            if (s.Length == 0) return "";
            if (!Path.IsPathRooted(s)) s = Path.Combine(Paths.Base, s);
            try { return Path.GetFullPath(s); }
            catch (Exception) { return s; }
        }

        public static string TableDir
        {
            get
            {
                if (!_tableDirLooked)
                {
                    _tableDirLooked = true;
                    _tableDir = FindTableDir(out _tableDirSource);
                }
                return _tableDir;
            }
        }

        /// <summary>表目录缓存作废（配置改了之后要叫一下，见 `LoadEditorConfig`）。</summary>
        public static void InvalidateTableDir()
        {
            _tableDirLooked = false;
            _tableDir = null;
        }

        private static string FindTableDir(out string source)
        {
            // ① 配置里的 table_dir（★ 用户自己指的，最优先）
            string want = ExpandPath(TableDirOverride);
            if (want.Length > 0)
            {
                if (Directory.Exists(want)) { source = "配置"; return want; }
                source = "配置里的路径不存在 ⇒ 回落默认";
            }
            else
            {
                source = "默认";
            }

            // ② `<游戏根>\server\data\gamedata`
            string g = GameDir;
            if (!string.IsNullOrEmpty(g))
            {
                string a = Path.Combine(g, Path.Combine("server", Path.Combine("data", "gamedata")));
                if (Directory.Exists(a)) return a;
            }
            // ③ 兜底：工作副本（只读）
            string dir = Paths.Base.TrimEnd('\\');
            for (int k = 0; k < 10 && !string.IsNullOrEmpty(dir); k++)
            {
                string probe = Path.Combine(dir, Path.Combine("4-服务端", Path.Combine("server", Path.Combine("data", "gamedata"))));
                if (Directory.Exists(probe)) return probe;
                dir = Path.GetDirectoryName(dir);
            }
            string direct = @"D:\小小兔趴\4-服务端\server\data\gamedata";
            return Directory.Exists(direct) ? direct : null;
        }

        /// <summary>
        /// ★ 从 `编辑器配置.json` 读 `game_dir` / `table_dir`（两个键只在这一处解析）。
        ///
        /// 为什么放在 `GameLocator` 而不是主窗口：`--selftest` / `--celltest` / `--shots`
        /// 这些**不开主窗口**的入口也要认这两个键，所以由 `App.Main` 第一件事调它。
        /// ★ 读不到 / 文件坏了 = 什么都不改（绝不因为配置坏了起不来）。
        /// </summary>
        public static void LoadEditorConfig(string configPath)
        {
            try
            {
                if (string.IsNullOrEmpty(configPath) || !File.Exists(configPath)) return;
                JNode root = DiyJson.Parse(File.ReadAllText(configPath, new UTF8Encoding(false)));
                if (root == null) return;

                JNode g = DiyJson.Member(root, "game_dir");
                if (g != null && g.Kind == JKind.String && g.Str.Trim().Length > 0)
                {
                    Override = g.Str.Trim();
                    Reset();                 // 游戏根变了 ⇒ GameDir 也要重算
                }

                JNode t = DiyJson.Member(root, "table_dir");
                if (t != null && t.Kind == JKind.String)
                {
                    TableDirOverride = t.Str.Trim();
                    InvalidateTableDir();    // ★ 配了（哪怕配成空串 = 回到默认）就作废缓存
                }

                // ★ 2026-10-17：怪物图标画哪一套（`pixel` 默认 / `portrait` = 图标缓存里的立绘）。
                JNode ma = DiyJson.Member(root, "monster_art");
                if (ma != null && ma.Kind == JKind.String && ma.Str.Trim().Length > 0)
                    MonsterArtMode = ma.Str.Trim().ToLowerInvariant();
            }
            catch (Exception) { }
        }

        public static string BoardSourceDir
        {
            get
            {
                string g = GameDir;
                if (!string.IsNullOrEmpty(g))
                {
                    string a = Path.Combine(g, Path.Combine("server", Path.Combine("data", "boards")));
                    if (Directory.Exists(a)) return a;
                }
                string dir = Paths.Base.TrimEnd('\\');
                for (int k = 0; k < 10 && !string.IsNullOrEmpty(dir); k++)
                {
                    string probe = Path.Combine(dir, Path.Combine("4-服务端", Path.Combine("server", Path.Combine("data", "boards"))));
                    if (Directory.Exists(probe)) return probe;
                    dir = Path.GetDirectoryName(dir);
                }
                string direct = @"D:\小小兔趴\4-服务端\server\data\boards";
                return Directory.Exists(direct) ? direct : null;
            }
        }

        // ================================================== ★ DIY 关卡目录（要写）
        /// <summary>
        /// ★★ 保存目录：`<游戏根>\DIY关卡\`。
        ///
        /// 为什么是这里（用户拍板 §②）：客户端要"动态列文件名"，它本来就在游戏根里跑；
        /// 服务端在同一台机器上，读同一个路径；而且这不在 Steam 校验范围、
        /// 也不会被更新包的清理逻辑删掉。
        ///
        /// ★ **不会写 `server\data\levels\`**（那是线上数据目录）。
        /// </summary>
        public static string DiyDir
        {
            get
            {
                string g = GameDir;
                if (string.IsNullOrEmpty(g)) return null;
                return Path.Combine(g, "DIY关卡");
            }
        }
    }
}
