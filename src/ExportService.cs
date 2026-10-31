using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace DiyEditor
{
    /// <summary>一次导出的结果（给人看的凭据）。</summary>
    public sealed class ExportResult
    {
        public string TargetPath = "";
        public bool Overwrote;
        public string BackupPath = "";       // 覆盖模式一定有
        public string SourceSha256 = "";     // 导出前：目标文件（覆盖时）的 sha256
        public string BackupSha256 = "";     // 备份副本的 sha256（必须与 SourceSha256 相同）
        public string NewSha256 = "";        // 导出后目标文件的 sha256
        public long NewLength;
        public bool ByteIdentical;           // 新内容与原文逐字节相同
        public string Note = "";
    }

    /// <summary>
    /// 导出：**保序 / 保风格回写** + ★ 覆盖前先备份（sha256 可回滚）+ **原子替换** ★。
    ///
    /// 为什么非要这一套（红线里的第 4 条自测）：
    ///   * 直接 File.WriteAllText 到目标路径 = 写一半断电/被杀 ⇒ 原文件**直接没了**；
    ///   * 服务端是**按 mtime 热重载**的（levels.py:44）⇒ 它可能正好读到一个半截文件；
    ///   * 所以：写同目录临时文件 → **刷盘** → 先备份 → File.Replace（带 ReplaceBackup 也行，
    ///     这里用自己的备份目录 + 原子 Move）→ 全程 sha256 对得上才算成功。
    /// </summary>
    public static class ExportService
    {
        // ================================================================ 读
        public sealed class Loaded
        {
            public string Path;
            public byte[] Bytes;
            public string Text;
            public string Sha256;
            public string Eol = "\n";
            public bool HasBom;
        }

        public static Loaded Load(string path)
        {
            var data = new Loaded();
            data.Path = path;
            data.Bytes = File.ReadAllBytes(path);
            data.Sha256 = Sha256Of(data.Bytes);

            int offset = 0;
            if (data.Bytes.Length >= 3 && data.Bytes[0] == 0xEF && data.Bytes[1] == 0xBB && data.Bytes[2] == 0xBF)
            {
                data.HasBom = true;
                offset = 3;
            }
            data.Text = new UTF8Encoding(false).GetString(data.Bytes, offset, data.Bytes.Length - offset);

            // ★ 换行风格：官方 levels\*.json 全是 LF，boards\*.json 全是 CRLF。
            //   回写时沿用**原文**的风格，不动它的形状。
            int crlf = 0, lf = 0;
            for (int i = 0; i < data.Text.Length; i++)
            {
                if (data.Text[i] != '\n') continue;
                if (i > 0 && data.Text[i - 1] == '\r') crlf++; else lf++;
            }
            data.Eol = crlf > lf ? "\r\n" : "\n";
            return data;
        }

        public static string Sha256Of(byte[] bytes)
        {
            using (var sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(bytes);
                var sb = new StringBuilder(hash.Length * 2);
                foreach (byte b in hash) sb.Append(b.ToString("x2", CultureInfo.InvariantCulture));
                return sb.ToString();
            }
        }

        public static string Sha256OfFile(string path)
        {
            return Sha256Of(File.ReadAllBytes(path));
        }

        /// <summary>把整棵树的当前状态变成要写出去的字节。</summary>
        public static byte[] Encode(JNode root, string eol, bool bom, byte[] originalBytes, bool changed)
        {
            // ★ 一个字节都没改 ⇒ **原样吐出原始字节**。
            //   这是"零写入 / 格式一致"能做到的最强保证：连你自己都没动过的东西，
            //   不存在"我重新排版了一下"这种事。
            if (!changed && originalBytes != null) return originalBytes;

            string body = DiyJson.Write(root, "  ", eol);
            var enc = new UTF8Encoding(false);
            byte[] text = enc.GetBytes(body);
            if (!bom) return text;
            var withBom = new byte[text.Length + 3];
            withBom[0] = 0xEF; withBom[1] = 0xBB; withBom[2] = 0xBF;
            Array.Copy(text, 0, withBom, 3, text.Length);
            return withBom;
        }

        /// <summary>★ 自己家目录里跟 exe 走的备份目录。</summary>
        public static string BackupRoot
        {
            get { return _backupRootOverride ?? Path.Combine(Paths.Base, "导出备份"); }
        }

        /// <summary>自测时把它指到 _自测 目录，免得在交付目录里堆备份。</summary>
        private static string _backupRootOverride;

        public static void OverrideBackupRoot(string dir) { _backupRootOverride = dir; }

        /// <summary>★ 覆盖前先备份 —— 给棋盘文件这类"官方形状的 json"用。</summary>
        public static ExportResult OverwriteWithBackupRoot(string target, byte[] bytes, string tag, string backupRoot)
        {
            string keep = _backupRootOverride;
            _backupRootOverride = backupRoot;
            try { return Overwrite(target, bytes, tag); }
            finally { _backupRootOverride = keep; }
        }

        private static void EnsureDir(string dir)
        {
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
        }

        /// <summary>原子写一个全新文件（不覆盖任何东西）。</summary>
        public static void WriteAtomic(string target, byte[] bytes)
        {
            string dir = Path.GetDirectoryName(Path.GetFullPath(target));
            EnsureDir(dir);
            string tmp = Path.Combine(dir, "." + Path.GetFileName(target) + ".tmp-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            using (var fs = new FileStream(tmp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                fs.Write(bytes, 0, bytes.Length);
                fs.Flush(true);              // ★ 刷到盘，不是只刷到系统缓存
            }
            if (File.Exists(target))
            {
                // 目标已存在（另存为同名）：走 Replace，别先删再写
                string spare = Path.Combine(dir, "." + Path.GetFileName(target) + ".old-" + Guid.NewGuid().ToString("N").Substring(0, 8));
                File.Replace(tmp, target, spare, true);
                TryDelete(spare);
            }
            else
            {
                File.Move(tmp, target);      // 同一分区上的原子改名
            }
        }

        /// <summary>
        /// ★ 覆盖原文件：**先备份（sha256 记账）→ 再原子替换**。
        /// 任何一步对不上就抛异常，绝不留下半截文件。
        /// </summary>
        public static ExportResult Overwrite(string target, byte[] newBytes, string tag)
        {
            var result = new ExportResult();
            result.TargetPath = target;
            result.Overwrote = true;

            string full = Path.GetFullPath(target);
            if (!File.Exists(full)) throw new FileNotFoundException("要覆盖的文件不在了", full);

            byte[] before = File.ReadAllBytes(full);
            result.SourceSha256 = Sha256Of(before);

            // ① 备份（按 sha256 命名 ⇒ 同一个内容不会存两遍，回滚时一眼能认出是哪一版）
            string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
            string stem = Path.GetFileNameWithoutExtension(full);
            string ext = Path.GetExtension(full);
            string dir = Path.Combine(BackupRoot, stem + "_" + stamp + (string.IsNullOrEmpty(tag) ? "" : "_" + tag));
            EnsureDir(dir);
            string backup = Path.Combine(dir, Path.GetFileName(full));
            WriteAtomic(backup, before);

            result.BackupPath = backup;
            result.BackupSha256 = Sha256OfFile(backup);
            if (!string.Equals(result.BackupSha256, result.SourceSha256, StringComparison.OrdinalIgnoreCase))
                throw new IOException("备份的 sha256 和原文件对不上，已中止（原文件没动）：" + backup);

            // ② 原子替换
            string parent = Path.GetDirectoryName(full);
            string tmp = Path.Combine(parent, "." + Path.GetFileName(full) + ".new-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            using (var fs = new FileStream(tmp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                fs.Write(newBytes, 0, newBytes.Length);
                fs.Flush(true);
            }
            string spare = Path.Combine(parent, "." + Path.GetFileName(full) + ".old-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            File.Replace(tmp, full, spare, true);
            TryDelete(spare);

            byte[] after = File.ReadAllBytes(full);
            result.NewSha256 = Sha256Of(after);
            result.NewLength = after.Length;
            if (!string.Equals(result.NewSha256, Sha256Of(newBytes), StringComparison.OrdinalIgnoreCase))
                throw new IOException("替换后的内容和要写的字节对不上（备份在 " + backup + "，可以用它回滚）");
            result.ByteIdentical = string.Equals(result.NewSha256, result.SourceSha256, StringComparison.OrdinalIgnoreCase);
            return result;
        }

        private static void TryDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); } catch (Exception) { }
        }
    }
}
