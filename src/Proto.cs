using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace DiyEditor
{
    /// <summary>
    /// ★ protobuf **线格式**读取器（只要够读客户端配置表就行，不生成代码、不带 .proto）。
    ///
    /// 为什么自己写：客户端那 157 张表是裸 protobuf（`GameRes` 里没有 .proto），
    /// 服务端 `astralnext/codec.py` 也是自己解线格式的。字段号一律**照抄**
    /// `server\astralnext\gamedata.py` 里已经逐条核对过的读法 —— 不凭印象猜。
    ///
    /// 线格式（wire type）：
    ///   0 = varint（int32/int64/bool/enum）
    ///   1 = 64 位固定
    ///   2 = 长度前缀（字符串 / 字节 / 嵌套消息 / packed 数组）
    ///   5 = 32 位固定（float / fixed32 / sfixed32）
    /// ★ 3/4（已废弃的 group）直接判错 —— 这些表里没有，出现就是定位错了。
    /// </summary>
    public static class Proto
    {
        public sealed class Field
        {
            public int Number;
            public int Wire;
            public byte[] Bytes;      // wire 2
            public long Int;          // wire 0 / 1 / 5 解出来的整数
            public bool IntIsSigned;  // wire 5 时按 int32 解释（< 0 表示负数）
        }

        // ================================================================ 顶层
        public static List<Field> Parse(byte[] data)
        {
            var list = new List<Field>();
            if (data == null || data.Length == 0) return list;
            int i = 0;
            while (i < data.Length)
            {
                long key;
                if (!ReadVarint(data, ref i, out key)) break;
                int number = (int)(key >> 3);
                int wire = (int)(key & 7);
                if (number <= 0) break;

                var f = new Field { Number = number, Wire = wire };
                switch (wire)
                {
                    case 0:
                        long v;
                        if (!ReadVarint(data, ref i, out v)) return list;
                        f.Int = v;
                        break;
                    case 1:
                        if (i + 8 > data.Length) return list;
                        f.Int = BitConverter.ToInt64(data, i);
                        i += 8;
                        break;
                    case 5:
                        if (i + 4 > data.Length) return list;
                        int i32 = BitConverter.ToInt32(data, i);
                        f.Int = i32;
                        f.IntIsSigned = true;
                        i += 4;
                        break;
                    case 2:
                        long len;
                        if (!ReadVarint(data, ref i, out len)) return list;
                        if (len < 0 || i + len > data.Length) return list;
                        f.Bytes = new byte[len];
                        Array.Copy(data, i, f.Bytes, 0, (int)len);
                        i += (int)len;
                        break;
                    default:
                        // group（3/4）或未知线型 ⇒ 后面已经没法对齐了，就此打住
                        return list;
                }
                list.Add(f);
            }
            return list;
        }

        // ================================================================ 基础
        public static bool ReadVarint(byte[] data, ref int i, out long value)
        {
            value = 0;
            int shift = 0;
            while (i < data.Length && shift < 64)
            {
                byte b = data[i++];
                value |= (long)(b & 0x7F) << shift;
                if ((b & 0x80) == 0) return true;
                shift += 7;
            }
            return i <= data.Length;
        }

        public static string Utf8(byte[] blob)
        {
            if (blob == null) return "";
            try { return new UTF8Encoding(false).GetString(blob); }
            catch (Exception) { return ""; }
        }

        // ================================================================ packed 数组
        /// <summary>
        /// packed 的 32 位整数数组。★ 两种编码都认（表里两种都有）：
        ///   · 纯 varint（`Map.bin` 的 Mids / `GameMode.bin` 的 MapID —— **zigzag** 编码）
        ///   · 定长 4 字节小端（`Map.bin` 的 DifficultyIds —— `fixed32`）
        /// 判据：长度能被 4 整除 **且** 按定长读出来的值看着像 id（都为正、都小于 1e7）
        /// ⇒ 当定长；否则当 varint + zigzag。
        /// </summary>
        public static List<int> Fixed32List(byte[] blob)
        {
            var outList = new List<int>();
            if (blob == null || blob.Length == 0) return outList;

            if (blob.Length % 4 == 0 && LooksLikeFixed32(blob))
            {
                for (int k = 0; k < blob.Length; k += 4)
                    outList.Add(BitConverter.ToInt32(blob, k));
                return outList;
            }

            int i = 0;
            while (i < blob.Length)
            {
                long raw;
                if (!ReadVarint(blob, ref i, out raw)) break;
                outList.Add((int)((raw >> 1) ^ -(raw & 1)));      // zigzag 解回来
            }
            return outList;
        }

        /// <summary>packed sint32（明确按 varint + zigzag 解）。</summary>
        public static List<int> Zigzag32List(byte[] blob)
        {
            var outList = new List<int>();
            if (blob == null) return outList;
            int i = 0;
            while (i < blob.Length)
            {
                long raw;
                if (!ReadVarint(blob, ref i, out raw)) break;
                outList.Add((int)((raw >> 1) ^ -(raw & 1)));
            }
            return outList;
        }

        private static bool LooksLikeFixed32(byte[] blob)
        {
            for (int k = 0; k < blob.Length; k += 4)
            {
                int v = BitConverter.ToInt32(blob, k);
                if (v < 0 || v > 10000000) return false;
            }
            return true;
        }

        // ================================================================ 便捷
        /// <summary>一行的某个字段（第一个 wire 0/5 的）。找不到回 null。</summary>
        public static Field Num(List<Field> row, int number)
        {
            foreach (Field f in row)
                if (f.Number == number && (f.Wire == 0 || f.Wire == 5)) return f;
            return null;
        }

        /// <summary>顶层 repeated 消息（`_rows` 那个读法）。</summary>
        public static List<byte[]> Rows(byte[] data, int topField)
        {
            var outList = new List<byte[]>();
            foreach (Field f in Parse(data))
                if (f.Number == topField && f.Wire == 2 && f.Bytes != null) outList.Add(f.Bytes);
            return outList;
        }

        /// <summary>
        /// 顶层 map 形态。★ 客户端这些表是**两层 MapEntry**（逐层 dump 实证）：
        ///
        ///     顶层 field N (wire 2)              ← 一条 entry
        ///       └ MapEntry.field 1 (wire 2)      ← 里面又一层的 key/value
        ///           ├ inner.field 1 (wire 0/5)   ← 真正的 key
        ///           └ inner.field 2 (wire 2)     ← 真正的 body
        ///
        /// 所以这里**要往里钻一层**。只认外层 field 1 的话会一条都取不到
        /// （外层 field 1 是 wire 2，不是 wire 5），
        /// `Map.bin` / `GameMode.bin` 都是这个形状。
        /// </summary>
        public sealed class Entry
        {
            public int Key;
            public byte[] Body;
        }

        public static List<Entry> Entries(byte[] data, int topField)
        {
            var outList = new List<Entry>();
            foreach (Field top in Parse(data))
            {
                if (top.Number != topField || top.Wire != 2 || top.Bytes == null) continue;
                Entry e = Unwrap(top.Bytes);
                if (e != null) outList.Add(e);
            }
            return outList;
        }

        private static Entry Unwrap(byte[] blob)
        {
            int key = 0;
            bool hasKey = false;
            byte[] body = null;

            foreach (Field f in Parse(blob))
            {
                if (f.Number == 1 && (f.Wire == 0 || f.Wire == 5)) { key = (int)f.Int; hasKey = true; }
                else if (f.Number == 2 && f.Wire == 2) body = f.Bytes;
                else if (f.Number == 1 && f.Wire == 2 && f.Bytes != null)
                {
                    // ★ 钻进去一层：MapEntry 里再放 key/value
                    foreach (Field g in Parse(f.Bytes))
                    {
                        if (g.Number == 1 && (g.Wire == 0 || g.Wire == 5)) { key = (int)g.Int; hasKey = true; }
                        else if (g.Number == 2 && g.Wire == 2) body = g.Bytes;
                    }
                }
            }
            if (!hasKey || body == null) return null;
            return new Entry { Key = key, Body = body };
        }
    }
}
