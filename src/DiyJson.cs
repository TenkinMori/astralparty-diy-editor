using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace DiyEditor
{
    /// <summary>
    /// 极小的**保序 + 保原文**JSON 模型。
    ///
    /// ★ 为什么不用 System.Text.Json：
    ///   ① 它写出来的是"标准缩进"，会把官方文件里那些手写的**单行短数组/单行对象**
    ///      （`"victory": { "kind": "kill_boss", "targets": [1067] }`、
    ///        `"1067": [106711, 106712, 106713]`）**展开成很多行** ⇒ 导出结果与官方
    ///      文件不是一个形状，人一眼就看得出"被工具动过"。
    ///   ② 我们要保证**键序原样**：官方文件里 key 的顺序是有意义的可读性顺序
    ///      （`note` → `map` → `difficulty` → `rounds` → …），字典类容器会把它打乱。
    ///   ③ 我们要能保证"**没编辑过 ⇒ 导出与原文逐字节一致**"。
    ///
    /// 所以模型是"**原文片段 + 脏标记**"：
    ///   * 每个节点都记着自己那一段**原始文本**（Raw）；
    ///   * 没被改过的节点，回写时**原样照抄** Raw（连注释键里的换行、单行数组都不动）；
    ///   * 只有被改过的那条路径（以及它的祖先容器）才重新排版，且**沿用原文风格**
    ///     （原来是单行就还单行、原来逗号后带空格就还带空格）。
    ///
    /// ★ 这是"格式一致"这件事能做到的最强保证：改动越小，导出与原文越像。
    /// </summary>
    public enum JKind { Object, Array, String, Number, Bool, Null }

    public sealed class JMember
    {
        public string Key;
        public JNode Value;
    }

    public sealed class JNode
    {
        public JKind Kind;
        public JNode Parent;

        /// <summary>原文片段（未编辑时原样回写）。</summary>
        public string Raw = "";

        /// <summary>自己被改过，或某个后代被改过。</summary>
        public bool Dirty;

        public List<JMember> Members;     // Object
        public List<JNode> Items;         // Array

        public string Str;                // String 的解码值
        public string Num;                // Number 的原样文本
        public bool BoolVal;              // Bool

        /// <summary>原文里这个容器没有换行（单行）。</summary>
        public bool WasInline;

        /// <summary>单行对象 `{ … }` 里侧是否留空格。</summary>
        public bool BraceSpace = true;

        /// <summary>单行数组 `[ … ]` 里侧是否留空格。</summary>
        public bool BracketSpace = false;

        /// <summary>分隔符是 ", " 还是 ","。</summary>
        public bool CommaSpace = true;

        public bool IsContainer { get { return Kind == JKind.Object || Kind == JKind.Array; } }

        public int Count { get { return Kind == JKind.Object ? (Members == null ? 0 : Members.Count) : (Items == null ? 0 : Items.Count); } }
    }

    public static class DiyJson
    {
        // =============================================================== 解析
        public static JNode Parse(string text)
        {
            if (text == null) throw new FormatException("内容是空的");
            string body = text;
            int i = 0;
            SkipWs(body, ref i);
            if (i >= body.Length) throw new FormatException("文件里没有 JSON 内容");
            JNode root = ParseValue(body, ref i, null);
            SkipWs(body, ref i);
            if (i != body.Length)
                throw new FormatException("JSON 后面还有多余内容（第 " + (i + 1) + " 个字符起）");
            return root;
        }

        private static JNode ParseValue(string s, ref int i, JNode parent)
        {
            SkipWs(s, ref i);
            if (i >= s.Length) throw new FormatException("JSON 提前结束（这里应该有一个值）");
            int start = i;
            char c = s[i];
            JNode n;
            if (c == '{') n = ParseObject(s, ref i);
            else if (c == '[') n = ParseArray(s, ref i);
            else if (c == '"') { string v = ParseString(s, ref i); n = new JNode { Kind = JKind.String, Str = v }; }
            else if (c == 't') { Expect(s, ref i, "true"); n = new JNode { Kind = JKind.Bool, BoolVal = true }; }
            else if (c == 'f') { Expect(s, ref i, "false"); n = new JNode { Kind = JKind.Bool, BoolVal = false }; }
            else if (c == 'n') { Expect(s, ref i, "null"); n = new JNode { Kind = JKind.Null }; }
            else n = ParseNumber(s, ref i);

            n.Parent = parent;
            n.Raw = s.Substring(start, i - start);
            n.WasInline = n.Raw.IndexOf('\n') < 0;
            if (n.Kind == JKind.Object) n.BraceSpace = n.Raw.Length > 1 && char.IsWhiteSpace(n.Raw[1]);
            if (n.Kind == JKind.Array) n.BracketSpace = n.Raw.Length > 1 && char.IsWhiteSpace(n.Raw[1]);
            if (n.IsContainer) n.CommaSpace = ScanCommaSpace(n.Raw);
            return n;
        }

        private static JNode ParseObject(string s, ref int i)
        {
            var n = new JNode { Kind = JKind.Object, Members = new List<JMember>() };
            i++;                                   // {
            SkipWs(s, ref i);
            if (i < s.Length && s[i] == '}') { i++; return n; }
            while (true)
            {
                SkipWs(s, ref i);
                if (i >= s.Length) throw new FormatException("对象没有收尾（缺 '}'）");
                if (s[i] != '"') throw new FormatException("对象里的键必须是字符串（第 " + (i + 1) + " 个字符）");
                string key = ParseString(s, ref i);
                SkipWs(s, ref i);
                if (i >= s.Length || s[i] != ':') throw new FormatException("键 \"" + key + "\" 后面缺 ':'");
                i++;
                JNode value = ParseValue(s, ref i, n);
                n.Members.Add(new JMember { Key = key, Value = value });
                SkipWs(s, ref i);
                if (i >= s.Length) throw new FormatException("对象没有收尾（缺 '}'）");
                if (s[i] == ',') { i++; continue; }
                if (s[i] == '}') { i++; return n; }
                throw new FormatException("对象里出现意外字符 '" + s[i] + "'（第 " + (i + 1) + " 个字符）");
            }
        }

        private static JNode ParseArray(string s, ref int i)
        {
            var n = new JNode { Kind = JKind.Array, Items = new List<JNode>() };
            i++;                                   // [
            SkipWs(s, ref i);
            if (i < s.Length && s[i] == ']') { i++; return n; }
            while (true)
            {
                JNode item = ParseValue(s, ref i, n);
                n.Items.Add(item);
                SkipWs(s, ref i);
                if (i >= s.Length) throw new FormatException("数组没有收尾（缺 ']'）");
                if (s[i] == ',') { i++; continue; }
                if (s[i] == ']') { i++; return n; }
                throw new FormatException("数组里出现意外字符 '" + s[i] + "'（第 " + (i + 1) + " 个字符）");
            }
        }

        private static string ParseString(string s, ref int i)
        {
            if (s[i] != '"') throw new FormatException("这里应该是一个字符串（第 " + (i + 1) + " 个字符）");
            i++;
            var sb = new StringBuilder();
            while (i < s.Length)
            {
                char c = s[i++];
                if (c == '"') return sb.ToString();
                if (c != '\\') { sb.Append(c); continue; }
                if (i >= s.Length) break;
                char e = s[i++];
                switch (e)
                {
                    case '"': sb.Append('"'); break;
                    case '\\': sb.Append('\\'); break;
                    case '/': sb.Append('/'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case 'u':
                        if (i + 4 > s.Length) throw new FormatException("\\u 后面不足 4 位十六进制");
                        sb.Append((char)Convert.ToInt32(s.Substring(i, 4), 16));
                        i += 4;
                        break;
                    default: throw new FormatException("不认识的转义 \\" + e);
                }
            }
            throw new FormatException("字符串没有收尾（缺 '\"'）");
        }

        private static JNode ParseNumber(string s, ref int i)
        {
            int start = i;
            while (i < s.Length && ("+-0123456789.eE".IndexOf(s[i]) >= 0)) i++;
            string t = s.Substring(start, i - start);
            // ★ 一个数字字符都没有 ⇒ 这里本来就该是 0（`EnsureMember` 建出来的占位节点
            //   被别处改成 Number 而没填值时会出现）。**不能**抛错：那样整份 json 都读不回来。
            if (t.Length == 0) return new JNode { Kind = JKind.Number, Num = "0" };
            double d;
            if (!double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out d))
                throw new FormatException("不是合法的数字：" + t);
            return new JNode { Kind = JKind.Number, Num = t };
        }

        private static void Expect(string s, ref int i, string word)
        {
            if (i + word.Length > s.Length || string.CompareOrdinal(s, i, word, 0, word.Length) != 0)
                throw new FormatException("这里应该是 " + word + "（第 " + (i + 1) + " 个字符）");
            i += word.Length;
        }

        private static void SkipWs(string s, ref int i)
        {
            while (i < s.Length && (s[i] == ' ' || s[i] == '\t' || s[i] == '\r' || s[i] == '\n')) i++;
        }

        /// <summary>单行容器里用的分隔符是 ", " 还是 ","（跳过字符串内部）。</summary>
        private static bool ScanCommaSpace(string raw)
        {
            bool inStr = false;
            for (int k = 0; k < raw.Length; k++)
            {
                char c = raw[k];
                if (inStr)
                {
                    if (c == '\\') { k++; continue; }
                    if (c == '"') inStr = false;
                    continue;
                }
                if (c == '"') { inStr = true; continue; }
                if (c == ',') return k + 1 < raw.Length && raw[k + 1] == ' ';
            }
            return true;
        }

        // =============================================================== 序列化
        public static string EncodeString(string s)
        {
            var sb = new StringBuilder((s == null ? 0 : s.Length) + 2);
            sb.Append('"');
            if (s != null)
            {
                foreach (char c in s)
                {
                    switch (c)
                    {
                        case '"': sb.Append("\\\""); break;
                        case '\\': sb.Append("\\\\"); break;
                        case '\n': sb.Append("\\n"); break;
                        case '\r': sb.Append("\\r"); break;
                        case '\t': sb.Append("\\t"); break;
                        case '\b': sb.Append("\\b"); break;
                        case '\f': sb.Append("\\f"); break;
                        default:
                            if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                            else sb.Append(c);          // ★ 中文照原样写出去，不转成 \uXXXX
                            break;
                    }
                }
            }
            sb.Append('"');
            return sb.ToString();
        }

        /// <summary>把整棵树写出来（未编辑的子树原样照抄 Raw）。</summary>
        public static string Write(JNode root, string indentUnit, string eol)
        {
            var sb = new StringBuilder();
            WriteNode(root, sb, indentUnit, 0, eol);
            return sb.ToString();
        }

        private static void Indent(StringBuilder sb, string unit, int depth)
        {
            for (int k = 0; k < depth; k++) sb.Append(unit);
        }

        private static void WriteNode(JNode n, StringBuilder sb, string unit, int depth, string eol)
        {
            if (n == null) { sb.Append("null"); return; }
            if (!n.Dirty) { sb.Append(n.Raw); return; }

            switch (n.Kind)
            {
                case JKind.Object:
                    {
                        int count = n.Count;
                        if (count == 0) { sb.Append("{}"); return; }
                        if (n.WasInline)
                        {
                            sb.Append(n.BraceSpace ? "{ " : "{");
                            for (int k = 0; k < count; k++)
                            {
                                if (k > 0) sb.Append(n.CommaSpace ? ", " : ",");
                                sb.Append(EncodeString(n.Members[k].Key)).Append(": ");
                                WriteNode(n.Members[k].Value, sb, unit, depth, eol);
                            }
                            sb.Append(n.BraceSpace ? " }" : "}");
                            return;
                        }
                        sb.Append('{').Append(eol);
                        for (int k = 0; k < count; k++)
                        {
                            Indent(sb, unit, depth + 1);
                            sb.Append(EncodeString(n.Members[k].Key)).Append(": ");
                            WriteNode(n.Members[k].Value, sb, unit, depth + 1, eol);
                            if (k < count - 1) sb.Append(',');
                            sb.Append(eol);
                        }
                        Indent(sb, unit, depth);
                        sb.Append('}');
                        return;
                    }

                case JKind.Array:
                    {
                        int count = n.Count;
                        if (count == 0) { sb.Append("[]"); return; }
                        if (n.WasInline)
                        {
                            sb.Append(n.BracketSpace ? "[ " : "[");
                            for (int k = 0; k < count; k++)
                            {
                                if (k > 0) sb.Append(n.CommaSpace ? ", " : ",");
                                WriteNode(n.Items[k], sb, unit, depth, eol);
                            }
                            sb.Append(n.BracketSpace ? " ]" : "]");
                            return;
                        }
                        sb.Append('[').Append(eol);
                        for (int k = 0; k < count; k++)
                        {
                            Indent(sb, unit, depth + 1);
                            WriteNode(n.Items[k], sb, unit, depth + 1, eol);
                            if (k < count - 1) sb.Append(',');
                            sb.Append(eol);
                        }
                        Indent(sb, unit, depth);
                        sb.Append(']');
                        return;
                    }

                case JKind.String: sb.Append(EncodeString(n.Str)); return;
                case JKind.Number: sb.Append(n.Num); return;
                case JKind.Bool: sb.Append(n.BoolVal ? "true" : "false"); return;
                default: sb.Append("null"); return;
            }
        }

        /// <summary>编辑器自己用的紧凑写法（只给人看，不往关卡文件里写）。</summary>
        public static string Compact(JNode n)
        {
            var sb = new StringBuilder();
            CompactNode(n, sb);
            return sb.ToString();
        }

        private static void CompactNode(JNode n, StringBuilder sb)
        {
            if (n == null) { sb.Append("null"); return; }
            switch (n.Kind)
            {
                case JKind.Object:
                    sb.Append('{');
                    for (int k = 0; k < n.Count; k++)
                    {
                        if (k > 0) sb.Append(',');
                        sb.Append(EncodeString(n.Members[k].Key)).Append(':');
                        CompactNode(n.Members[k].Value, sb);
                    }
                    sb.Append('}');
                    return;
                case JKind.Array:
                    sb.Append('[');
                    for (int k = 0; k < n.Count; k++)
                    {
                        if (k > 0) sb.Append(',');
                        CompactNode(n.Items[k], sb);
                    }
                    sb.Append(']');
                    return;
                case JKind.String: sb.Append(EncodeString(n.Str)); return;
                case JKind.Number: sb.Append(NumText(n)); return;
                case JKind.Bool: sb.Append(n.BoolVal ? "true" : "false"); return;
                default: sb.Append("null"); return;
            }
        }

        /// <summary>Num 为空就写 0，绝不写 null（写 null 会让整份 json 下次解析失败）。</summary>
        private static string NumText(JNode n)
        {
            if (n == null) return "0";
            return string.IsNullOrEmpty(n.Num) ? "0" : n.Num;
        }

        // =============================================================== 改
        public static void MarkDirty(JNode n)
        {
            JNode cur = n;
            while (cur != null && !cur.Dirty) { cur.Dirty = true; cur = cur.Parent; }
        }

        /// <summary>整棵树里有没有被改过（等价于根节点的 Dirty，留个显式的名字好读）。</summary>
        public static bool IsDirty(JNode root) { return root != null && root.Dirty; }

        /// <summary>把这一段的原文当作"新的基线"（保存成功后调用，免得又原样照抄旧 Raw）。</summary>
        public static JNode Rebase(JNode n, string newRaw, JNode parent)
        {
            JNode fresh = Parse(newRaw);
            fresh.Parent = parent;
            fresh.Raw = newRaw;
            return fresh;
        }

        // =============================================================== 取值
        public static JNode Member(JNode obj, string key)
        {
            if (obj == null || obj.Kind != JKind.Object || obj.Members == null) return null;
            foreach (JMember m in obj.Members)
                if (string.Equals(m.Key, key, StringComparison.Ordinal)) return m.Value;
            return null;
        }

        public static string KeyAt(JNode obj, int index)
        {
            if (obj == null || obj.Kind != JKind.Object || obj.Members == null) return null;
            if (index < 0 || index >= obj.Members.Count) return null;
            return obj.Members[index].Key;
        }

        public static bool IsInt(JNode n, out long value)
        {
            value = 0;
            if (n == null || n.Kind != JKind.Number) return false;
            return long.TryParse(n.Num, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
        }

        public static long IntOf(JNode n, long fallback)
        {
            long v;
            return IsInt(n, out v) ? v : fallback;
        }

        public static double DoubleOf(JNode n, double fallback)
        {
            if (n == null || n.Kind != JKind.Number) return fallback;
            double v;
            return double.TryParse(n.Num, NumberStyles.Float, CultureInfo.InvariantCulture, out v) ? v : fallback;
        }

        /// <summary>给"显示用"的一行短摘要（长字符串截断）。</summary>
        public static string Brief(JNode n, int max)
        {
            if (n == null) return "—";
            string s;
            switch (n.Kind)
            {
                case JKind.Object: s = "{…" + n.Count + " 项}"; break;
                case JKind.Array: s = "[…" + n.Count + " 项]"; break;
                case JKind.String: s = "\"" + n.Str + "\""; break;
                case JKind.Number: s = n.Num; break;
                case JKind.Bool: s = n.BoolVal ? "true" : "false"; break;
                default: s = "null"; break;
            }
            s = s.Replace("\r", " ").Replace("\n", " ");
            if (max > 3 && s.Length > max) s = s.Substring(0, max - 1) + "…";
            return s;
        }

        // =============================================================== 增删
        /// <summary>取一个成员；没有就新建一个 null 并挂上去（父容器会被标脏）。</summary>
        public static JNode EnsureMember(JNode obj, string key)
        {
            JNode found = Member(obj, key);
            if (found != null) return found;
            if (obj.Members == null) obj.Members = new List<JMember>();
            var value = new JNode { Kind = JKind.Null, Parent = obj };
            obj.Members.Add(new JMember { Key = key, Value = value });
            MarkDirty(obj);
            return value;
        }

        public static void SetNumber(JNode n, string numText)
        {
            n.Kind = JKind.Number; n.Num = numText; n.Str = null;
            MarkDirty(n);
        }

        public static void SetInt(JNode n, long value)
        {
            SetNumber(n, value.ToString(CultureInfo.InvariantCulture));
        }

        public static void SetString(JNode n, string text)
        {
            n.Kind = JKind.String; n.Str = text; n.Num = null;
            MarkDirty(n);
        }

        /// <summary>
        /// 改节点的种类，并把该种类要用的容器准备好。
        /// EnsureMember 建出来的是 Null（Members / Items 都是 null），
        /// 直接赋 Kind = Array 之后 Items.Add 就是 NullReferenceException ⇒ 一律走这里。
        /// </summary>
        public static JNode SetKind(JNode n, JKind kind)
        {
            if (n == null) return null;
            n.Kind = kind;
            if (kind == JKind.Object && n.Members == null) n.Members = new List<JMember>();
            if (kind == JKind.Array && n.Items == null) n.Items = new List<JNode>();
            MarkDirty(n);
            return n;
        }

        public static void RemoveMember(JNode obj, string key)
        {
            if (obj == null || obj.Members == null) return;
            for (int k = 0; k < obj.Members.Count; k++)
            {
                if (string.Equals(obj.Members[k].Key, key, StringComparison.Ordinal))
                {
                    obj.Members.RemoveAt(k);
                    MarkDirty(obj);
                    return;
                }
            }
        }

        /// <summary>按现有元素的样子复制一个（数组加行用；标量给 0，对象给同键的空壳）。</summary>
        public static JNode CloneShape(JNode src, JNode parent)
        {
            var n = new JNode { Kind = src.Kind, Parent = parent, Dirty = true, WasInline = src.WasInline, CommaSpace = src.CommaSpace };
            if (src.Kind == JKind.Object)
            {
                n.Members = new List<JMember>();
                foreach (JMember m in src.Members)
                    n.Members.Add(new JMember { Key = m.Key, Value = CloneShape(m.Value, n) });
            }
            else if (src.Kind == JKind.Array)
            {
                n.Items = new List<JNode>();
            }
            else if (src.Kind == JKind.Number) n.Num = "0";
            else if (src.Kind == JKind.String) n.Str = "";
            else if (src.Kind == JKind.Bool) n.BoolVal = false;
            return n;
        }
    }
}
