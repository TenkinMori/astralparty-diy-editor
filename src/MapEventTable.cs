using System;
using System.Collections.Generic;
using System.Globalization;

namespace DiyEditor
{
    /// <summary>MapEvent.bin 的一行（只读）。</summary>
    public sealed class MapEventRow
    {
        public int Id;
        public string Name = "";

        /// <summary>field 4 原值 = 进度 × 2。</summary>
        public int? TriggerX2;

        public int Progress { get { return TriggerX2.HasValue ? TriggerX2.Value / 2 : -1; } }
        public bool HasTrigger { get { return TriggerX2.HasValue; } }

        public bool Hide;
        public List<int> LandUnits = new List<int>();
        public Dictionary<int, List<int>> Effects = new Dictionary<int, List<int>>();

        public string Display
        {
            get { return Id.ToString(CultureInfo.InvariantCulture) + " " + (Name.Length > 0 ? Name : "(官方没给名字)"); }
        }

        public string TriggerText
        {
            get { return HasTrigger ? ("进度到 " + Progress + " 触发") : "表里没写触发点"; }
        }
    }

    /// <summary>MapEventEffectType。</summary>
    public static class MapEventEffectType
    {
        public const int None = 0;
        public const int CreateMonster = 1;
        public const int CreateMonsterOneKind = 2;
        public const int MonsterAttrChange = 3;
        public const int GainCard = 4;
        public const int LandUnitsGroupIdTrigger = 5;
        public const int ActiveBoss = 6;

        public static string Name(int kind)
        {
            switch (kind)
            {
                case CreateMonster: return "刷怪 CreateMonster";
                case CreateMonsterOneKind: return "刷一种怪 CreateMonsterOneKind";
                case MonsterAttrChange: return "怪物攻防变更 MonsterAttrChange";
                case GainCard: return "获得卡牌 GainCard";
                case LandUnitsGroupIdTrigger: return "触发地图单位组 LandUnitsGroupIdTrigger";
                case ActiveBoss: return "激活 Boss ActiveBoss";
                default: return "效果 " + kind;
            }
        }

        public static string Note(int kind)
        {
            switch (kind)
            {
                case CreateMonster: return "参数 = 怪物表 id 列表，各刷 1 只";
                case CreateMonsterOneKind: return "参数 = [怪物表 id, 数量]";
                case MonsterAttrChange: return "参数 = [攻击+N, 防御+N]，作用于场上所有怪";
                case GainCard: return "参数 = 卡牌 id 列表";
                case LandUnitsGroupIdTrigger: return "参数 = 地图单位组 id（组表在服务端）";
                case ActiveBoss: return "参数 = 怪物表 id 列表";
                default: return "";
            }
        }
    }

    /// <summary>MapEvent.bin / STRMapEvent.bin 的只读入口。字段号照抄服务端 gamedata._load_map_events。</summary>
    public static class MapEventTable
    {
        private static List<MapEventRow> _rows;
        private static Dictionary<int, MapEventRow> _byId;

        /// <summary>触发进度 → 官方事件行（升序）。★ "进度 N 下有哪些官方事件"靠它。</summary>
        private static Dictionary<int, List<MapEventRow>> _byProgress;

        private static string _note = "";

        public static string Note { get { Rows(); return _note; } }

        public static List<MapEventRow> Rows()
        {
            if (_rows != null) return _rows;
            _rows = new List<MapEventRow>();
            _byId = new Dictionary<int, MapEventRow>();
            _byProgress = null;

            byte[] data = GameTables.TableBytes("MapEvent.bin");
            if (data == null)
            {
                _note = "读不到 MapEvent.bin（表目录：" + (GameLocator.TableDir ?? "没找到") + "）⇒ 事件 id 只能手填。";
                return _rows;
            }

            Dictionary<int, string> names = GameTables.StrTable("STRMapEvent");
            int named = 0, withTrigger = 0, withEffect = 0;
            // 只有顶层 field 1 是真行；2/3/4 是同一批的 map 形态
            foreach (Proto.Field top in Proto.Parse(data))
            {
                if (top.Number != 1 || top.Wire != 2 || top.Bytes == null) continue;
                MapEventRow row = ParseRow(top.Bytes);
                if (row == null) continue;
                _rows.Add(row);
                _byId[row.Id] = row;

                string nm;
                if (names.TryGetValue(row.Id, out nm) && !string.IsNullOrEmpty(nm)) { row.Name = nm; named++; }
                if (row.HasTrigger) withTrigger++;
                if (row.Effects.Count > 0) withEffect++;
            }
            _rows.Sort(delegate (MapEventRow a, MapEventRow b) { return a.Id.CompareTo(b.Id); });
            _note = "MapEvent.bin 读到 " + _rows.Count + " 条事件：有官方名字 " + named
                  + " 条、有触发点 " + withTrigger + " 条、带效果 " + withEffect + " 条（只读）。";
            return _rows;
        }

        public static MapEventRow Get(int eventId)
        {
            Rows();
            MapEventRow hit;
            return _byId.TryGetValue(eventId, out hit) ? hit : null;
        }

        public static bool Known(int eventId) { return Get(eventId) != null; }

        public static string NameOf(int eventId)
        {
            MapEventRow r = Get(eventId);
            return r == null || r.Name.Length == 0 ? "" : r.Name;
        }

        public static int ProgressOf(int eventId)
        {
            MapEventRow r = Get(eventId);
            return r == null ? -1 : r.Progress;
        }

        // ============================================================ ★★ 触发进度索引
        //
        // 为什么要有这一段（用户 2026-10-03 第 11 波："要能给每个进度事件写出哪个回合触发"）：
        //   官方**没有**"触发进度"这个独立字段 —— 触发点被**事件 id** 那一行编码
        //   （MapEvent.bin field 4 = 进度 × 2，见 gamedata.map_event_progress）。
        //   所以"我想让它在进度 3 触发"这件事，只能落成"挑一条 field4÷2 == 3 的官方事件 id"。
        //   ⇒ 编辑器的做法是：把想要的进度值**反查**成候选 id 列表给他挑，一个都不硬编。

        /// <summary>★ 触发进度 = progress 的官方事件行（**按 id 升序**）。没有就是空表。</summary>
        public static List<MapEventRow> RowsAtProgress(int progress)
        {
            Rows();
            BuildProgressIndex();
            List<MapEventRow> hit;
            if (_byProgress != null && _byProgress.TryGetValue(progress, out hit) && hit != null)
                return new List<MapEventRow>(hit);
            return new List<MapEventRow>();
        }

        /// <summary>表里出现过的触发进度（升序）——"官方一共给了哪几档触发点"。</summary>
        public static List<int> ProgressValues()
        {
            Rows();
            BuildProgressIndex();
            var keys = new List<int>(_byProgress == null ? new List<int>() : new List<int>(_byProgress.Keys));
            keys.Sort();
            return keys;
        }

        /// <summary>表里最大的触发进度（"官方没有比这个更大的触发点"用得上）。</summary>
        public static int MaxProgress()
        {
            List<int> keys = ProgressValues();
            return keys.Count == 0 ? -1 : keys[keys.Count - 1];
        }

        /// <summary>官方效果的一行摘要（挑事件 / 写报告时给人看）。</summary>
        public static string EffectSummary(MapEventRow r)
        {
            if (r == null) return "(没有这一行)";
            if (r.Effects.Count == 0) return "(官方那一行没写效果)";
            var eff = new List<string>();
            foreach (int k in SortedKeys(r.Effects))
                eff.Add(MapEventEffectType.Name(k) + "=[" + Join(r.Effects[k]) + "]");
            return string.Join(" ", eff.ToArray());
        }

        /// <summary>候选列表里怎么称呼一条（"32001 小怪兽登陆（field4=6 ⇒ 进度 3）"）。</summary>
        public static string CandidateText(MapEventRow r)
        {
            if (r == null) return "(空)";
            return r.Id + " " + (r.Name.Length > 0 ? r.Name : "(官方没给名字)")
                 + "（field4=" + (r.TriggerX2.HasValue ? r.TriggerX2.Value.ToString(CultureInfo.InvariantCulture) : "?")
                 + " ⇒ 进度 " + r.Progress + "）";
        }

        private static void BuildProgressIndex()
        {
            if (_byProgress != null) return;
            _byProgress = new Dictionary<int, List<MapEventRow>>();
            foreach (MapEventRow r in _rows)
            {
                if (!r.HasTrigger) continue;
                List<MapEventRow> got;
                if (!_byProgress.TryGetValue(r.Progress, out got))
                {
                    got = new List<MapEventRow>();
                    _byProgress[r.Progress] = got;
                }
                got.Add(r);
            }
            foreach (var kv in _byProgress)
                kv.Value.Sort(delegate (MapEventRow a, MapEventRow b) { return a.Id.CompareTo(b.Id); });
        }

        public static string Describe(int eventId)
        {
            MapEventRow r = Get(eventId);
            if (r == null)
                return "事件 " + eventId + " 不在 MapEvent.bin 里（官方没有这个号）—— 服务端按 id 触发，这个号不会有任何事发生。";
            var parts = new List<string>();
            parts.Add(r.Name.Length > 0 ? r.Name : "(官方没给名字)");
            parts.Add(r.TriggerText);
            if (r.Hide) parts.Add("IsHide");
            if (r.LandUnits.Count > 0) parts.Add("地图单位组 " + Join(r.LandUnits));
            if (r.Effects.Count == 0)
            {
                parts.Add("官方那一行没写效果（要靠关卡 json 的 progress_events 补 spawn）");
            }
            else
            {
                var eff = new List<string>();
                foreach (int k in SortedKeys(r.Effects))
                    eff.Add(MapEventEffectType.Name(k) + " = [" + Join(r.Effects[k]) + "]");
                parts.Add("官方效果：" + string.Join("；", eff.ToArray()));
            }
            return string.Join(" · ", parts.ToArray());
        }

        /// <summary>这张图这一档官方登记的进度事件 id（Map.bin 难度行 field 3）。</summary>
        public static List<int> EventsForMapDifficulty(int mapId, int difficulty)
        {
            var outList = new List<int>();
            Dictionary<int, List<int>> rows = GameTables.ProgressEventsByDifficulty(mapId);
            List<int> got;
            if (rows != null && rows.TryGetValue(difficulty, out got)) outList.AddRange(got);
            return outList;
        }

        private static List<int> SortedKeys(Dictionary<int, List<int>> d)
        {
            var keys = new List<int>(d.Keys);
            keys.Sort();
            return keys;
        }

        private static string Join(List<int> xs)
        {
            if (xs == null || xs.Count == 0) return "";
            var a = new string[xs.Count];
            for (int k = 0; k < xs.Count; k++) a[k] = xs[k].ToString(CultureInfo.InvariantCulture);
            return string.Join(", ", a);
        }

        private static MapEventRow ParseRow(byte[] blob)
        {
            var row = new MapEventRow();
            bool hasId = false;
            foreach (Proto.Field f in Proto.Parse(blob))
            {
                if (f.Number == 1 && (f.Wire == 0 || f.Wire == 5)) { row.Id = (int)f.Int; hasId = true; }
                else if (f.Number == 3 && (f.Wire == 0 || f.Wire == 5)) row.Hide = f.Int != 0;
                else if (f.Number == 4 && f.Wire == 2 && f.Bytes != null)
                {
                    int i = 0;
                    long v;
                    if (Proto.ReadVarint(f.Bytes, ref i, out v)) row.TriggerX2 = (int)v;
                }
                else if (f.Number == 5 && f.Wire == 2 && f.Bytes != null)
                {
                    row.LandUnits.AddRange(Proto.Zigzag32List(f.Bytes));
                }
                else if (f.Number == 6 && f.Wire == 2 && f.Bytes != null)
                {
                    int kind = 0; var values = new List<int>();
                    foreach (Proto.Field sub in Proto.Parse(f.Bytes))
                    {
                        if (sub.Number == 1 && (sub.Wire == 0 || sub.Wire == 5)) kind = (int)sub.Int;
                        else if (sub.Number == 2 && sub.Wire == 2 && sub.Bytes != null)
                        {
                            foreach (Proto.Field inner in Proto.Parse(sub.Bytes))
                                if (inner.Number == 1 && inner.Wire == 2 && inner.Bytes != null)
                                    values = Proto.Fixed32List(inner.Bytes);
                        }
                    }
                    if (kind != 0) row.Effects[kind] = values;
                }
            }
            return hasId ? row : null;
        }
    }
}
