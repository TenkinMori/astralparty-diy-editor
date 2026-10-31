using System;
using System.IO;

namespace DiyEditor
{
    /// <summary>
    /// ★ 「这个编辑器自己的目录」在哪 —— **只管工具自己的地盘**，
    /// 游戏在哪是 `GameLocator` 的事（一个概念只有一个所有者，别两边都算）。
    ///
    /// 单文件发布（PublishSingleFile）时 `Assembly.Location` **返回空串** ——
    /// 用它的地方会把自己的目录算成当前工作目录，表现就是"备份跑到别处去了"。
    /// 所以一律用 `AppContext.BaseDirectory`（单文件下就是 exe 所在目录）。
    ///
    /// 规矩：**一切都跟着 exe 走**（配置文件 / 导出备份 / 棋盘缓存），
    /// 不碰 %LocalLow%、不碰服务端目录、不写任何别的地方。
    /// </summary>
    public static class Paths
    {
        /// <summary>exe 所在目录（单文件下就是它自己那儿）。</summary>
        public static string Base
        {
            get { return AppContext.BaseDirectory; }
        }

        /// <summary>本站自己的根目录（5-DIY关卡）。开发时从 bin\Release\... 往上找。</summary>
        public static string Own()
        {
            string dir = Base.TrimEnd('\\');
            for (int k = 0; k < 10 && !string.IsNullOrEmpty(dir); k++)
            {
                if (string.Equals(Path.GetFileName(dir), "5-DIY关卡", StringComparison.OrdinalIgnoreCase))
                    return dir;
                dir = Path.GetDirectoryName(dir);
            }
            return Base;
        }

        /// <summary>在 exe 旁边（或往上找得到的 5-DIY关卡 里）找一个子目录。</summary>
        public static string Under(string name)
        {
            string beside = Path.Combine(Base, name);
            if (Directory.Exists(beside)) return beside;
            string own = Path.Combine(Own(), name);
            if (Directory.Exists(own)) return own;
            return beside;
        }
    }
}