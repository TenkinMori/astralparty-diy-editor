using System;
using System.IO;

namespace DiyEditor
{
    /// <summary>
    /// 找"工作副本里那几份官方数据"。**只读**，找不到就老实说找不到，不瞎猜。
    ///
    /// ★ 注意分工：**游戏在哪**是 `GameLocator` 的事，这里只管
    ///   "开发机上那套工作副本"（`4-服务端\...`）—— 打开对话框的默认目录、
    ///   官方 levels 的样子，都从这儿来。
    /// </summary>
    public static class LevelFinder
    {
        private static string FromExe(string tail)
        {
            string dir = Paths.Base.TrimEnd('\\');
            for (int k = 0; k < 10 && !string.IsNullOrEmpty(dir); k++)
            {
                string probe = Path.Combine(dir, tail);
                if (Directory.Exists(probe)) return probe;
                dir = Path.GetDirectoryName(dir);
            }
            return null;
        }

        /// <summary>开发机上的官方关卡目录（只读）。</summary>
        public static string LevelsDir()
        {
            string hit = FromExe(Path.Combine("4-服务端", Path.Combine("server", Path.Combine("data", "levels"))));
            if (hit != null) return hit;
            string fallback = @"D:\小小兔趴\4-服务端\server\data\levels";
            return Directory.Exists(fallback) ? fallback : null;
        }

        /// <summary>开发机上的棋盘目录（只读）。</summary>
        public static string BoardsDir()
        {
            string hit = FromExe(Path.Combine("4-服务端", Path.Combine("server", Path.Combine("data", "boards"))));
            if (hit != null) return hit;
            string fallback = @"D:\小小兔趴\4-服务端\server\data\boards";
            return Directory.Exists(fallback) ? fallback : null;
        }

        /// <summary>自己家目录（DIY 关卡自己的地盘）。</summary>
        public static string OwnDir()
        {
            return Paths.Own();
        }

        /// <summary>打开对话框默认先看这里。</summary>
        public static string DefaultOpenDir()
        {
            string mine = Path.Combine(OwnDir(), "导入记录");
            if (Directory.Exists(mine)) return mine;
            return OwnDir();
        }
    }
}
