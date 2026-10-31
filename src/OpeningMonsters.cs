using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace DiyEditor
{
    /// <summary>官方那批开局怪里的一只（= 服务端 `battle.INIT_MONSTERS` 的 8 元组）。</summary>
    public sealed class OpeningMonster
    {
        public int Table;
        public int Node;        // -1 = "让服务端自己挑一格"（同 progress 事件刷怪的写法）
        public int Hp;
        public int Gold;
        public int Defense;
        public int Attack;
        public int Type;        // Hero.MonsterType（12 = PveEliteMonster …）
        public int Weight;      // 行动权重

        public string One()
        {
            return Table.ToString(CultureInfo.InvariantCulture)
                 + (Node >= 0 ? ("@" + Node.ToString(CultureInfo.InvariantCulture)) : "@随机")
                 + "（血 " + Hp.ToString(CultureInfo.InvariantCulture)
                 + " / 星币 " + Gold.ToString(CultureInfo.InvariantCulture)
                 + " / 防 " + Defense.ToString(CultureInfo.InvariantCulture)
                 + " / 攻 " + Attack.ToString(CultureInfo.InvariantCulture) + "）";
        }
    }

    /// <summary>
    /// ★★ 官方那批**开局怪**（"服务端没读到 `start_monsters` 时会刷什么"）—— 只读。
    ///
    /// 为什么要有它（用户 2026-10-10 原话：「但是我编辑器这里写的是开局刷怪是 0 啊」）：
    ///   服务端 `battle.spawn_init_monsters` 的口径是
    ///     `entry = levels.start_monsters(room)`；**`None` 才** `INIT_MONSTERS.get(map_id) or ()`
    ///     （`4-服务端\server\astralnext\battle.py:3203-3207`）。
    ///   也就是说"配置里没写"= 刷**这张图官方那批**；编辑器要能把这句话说清楚、
    ///   还要能一键把那批填进"开局就在场上"那一组 —— 数据就在这里。
    ///
    /// 数据从哪读（**只读**，三处依次找；都没有就老实说"查不到"，绝不硬编）：
    ///   ① `<游戏根>\server\astralnext\battle.py`
    ///   ② 从 exe 往上找得到的 `4-服务端\server\astralnext\battle.py`（开发机 / 用户那份 dist 都在这个树下）
    ///   ③ `D:\小小兔趴\4-服务端\server\astralnext\battle.py`（兜底，和 `GameLocator.TableDir` 一个套路）
    ///
    /// 解析口径：只认 `INIT_MONSTERS = { … }` 这个**顶层块**，块里
    ///   · `    <地图id>: [` 开一段；
    ///   · 每行一条 8 元组 `(table, node, hp, gold, defense, attack, type, weight)`；
    ///   · `    ],` 收一段。
    /// 别的行（空行 / `#` 注释）跳过；**出现任何不认识的形状就把那段整段丢掉**并记进 `Note`
    ///   —— 宁可说"这张图读不到"，也不猜。
    /// </summary>
    public static class OpeningMonsters
    {
        private static Dictionary<int, List<OpeningMonster>> _byMap;
        private static string _note = "";
        private static bool _tried;

        /// <summary>数据从哪来 / 为什么没有（界面与报告如实说明用）。</summary>
        public static string Note { get { Load(); return _note; } }

        /// <summary>这张图官方那批开局怪（没有就空表）。</summary>
        public static List<OpeningMonster> ForMap(int mapId)
        {
            Load();
            List<OpeningMonster> hit;
            if (_byMap != null && _byMap.TryGetValue(mapId, out hit) && hit != null)
                return new List<OpeningMonster>(hit);
            return new List<OpeningMonster>();
        }

        public static bool Known(int mapId) { return ForMap(mapId).Count > 0; }

        /// <summary>给 lint / 界面用的一句话（"（这张图官方那批 6 只：1016@3 / …）"）；查不到回空串。</summary>
        public static string Hint(int mapId)
        {
            string brief = Brief(mapId);
            if (brief.Length == 0) return "";
            return "（这张图官方那批 " + ForMap(mapId).Count + " 只：" + brief + "）";
        }

        /// <summary>只要那串清单（"1016@3 / 1016@13 / …"）；查不到回空串。</summary>
        public static string Brief(int mapId)
        {
            List<OpeningMonster> got = ForMap(mapId);
            if (got.Count == 0) return "";
            var parts = new List<string>();
            foreach (OpeningMonster m in got)
            {
                parts.Add(m.Table.ToString(CultureInfo.InvariantCulture)
                    + (m.Node >= 0 ? ("@" + m.Node.ToString(CultureInfo.InvariantCulture)) : "@随机"));
            }
            return string.Join(" / ", parts.ToArray());
        }

        /// <summary>`battle.py` 在哪（找不到回空串）。</summary>
        public static string FilePath()
        {
            var cand = new List<string>();
            string game = GameLocator.GameDir;
            if (!string.IsNullOrEmpty(game))
                cand.Add(Path.Combine(game, Path.Combine("server", Path.Combine("astralnext", "battle.py"))));
            string dir = Paths.Base.TrimEnd('\\');
            for (int k = 0; k < 10 && !string.IsNullOrEmpty(dir); k++)
            {
                cand.Add(Path.Combine(dir, Path.Combine("4-服务端",
                    Path.Combine("server", Path.Combine("astralnext", "battle.py")))));
                dir = Path.GetDirectoryName(dir);
            }
            cand.Add(@"D:\小小兔趴\4-服务端\server\astralnext\battle.py");
            foreach (string p in cand)
            {
                try { if (File.Exists(p)) return p; }
                catch (Exception) { }
            }
            return "";
        }

        private static void Load()
        {
            if (_tried) return;
            _tried = true;
            _byMap = new Dictionary<int, List<OpeningMonster>>();

            string path = FilePath();
            if (path.Length == 0)
            {
                _note = "找不到服务端的 battle.py（这三个地方都没有：游戏根\\server\\astralnext\\、"
                      + "exe 往上找的 4-服务端\\server\\astralnext\\、D:\\小小兔趴\\4-服务端\\server\\astralnext\\）"
                      + " ⇒ 拿不到「官方那批开局怪」，只说「按官方那批来」，不硬编。";
                return;
            }

            string text;
            try { text = File.ReadAllText(path, Encoding.UTF8); }
            catch (Exception ex) { _note = "读不动 " + path + "：" + ex.Message; return; }

            Parsed p = Parse(text);
            _byMap = p.Maps;
            _note = "官方开局怪表 `battle.INIT_MONSTERS` 读自 " + path + "："
                  + _byMap.Count + " 张图" + (p.Skipped.Count > 0 ? ("；跳过了读不动的 " + p.Skipped.Count + " 段：" + string.Join("、", p.Skipped.ToArray())) : "")
                  + "。";
        }

        private sealed class Parsed
        {
            public Dictionary<int, List<OpeningMonster>> Maps = new Dictionary<int, List<OpeningMonster>>();
            public List<string> Skipped = new List<string>();
        }

        /// <summary>只认 `INIT_MONSTERS = { … }` 这一个块，块里只认那三种行（见类注释）。</summary>
        private static Parsed Parse(string text)
        {
            var outP = new Parsed();
            int at = text.IndexOf("INIT_MONSTERS", StringComparison.Ordinal);
            if (at < 0) return outP;
            int braceAt = text.IndexOf('{', at);
            if (braceAt < 0) return outP;
            int endAt = text.IndexOf("\n}", braceAt, StringComparison.Ordinal);
            if (endAt < 0) return outP;

            string block = text.Substring(braceAt + 1, endAt - braceAt - 1);
            string[] lines = block.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

            int curMap = 0;
            List<OpeningMonster> cur = null;
            bool broken = false;
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                string trim = line.Trim();
                if (trim.Length == 0 || trim.StartsWith("#", StringComparison.Ordinal)) continue;

                int mapId;
                if (trim.EndsWith(": [", StringComparison.Ordinal)
                    && int.TryParse(trim.Substring(0, trim.Length - 3).Trim(),
                        NumberStyles.Integer, CultureInfo.InvariantCulture, out mapId))
                {
                    if (cur != null) Finish(outP, curMap, cur, broken);
                    curMap = mapId;
                    cur = new List<OpeningMonster>();
                    broken = false;
                    continue;
                }
                if (trim == "]," || trim == "]")
                {
                    if (cur != null) Finish(outP, curMap, cur, broken);
                    cur = null; curMap = 0; broken = false;
                    continue;
                }
                if (trim.StartsWith("(", StringComparison.Ordinal))
                {
                    OpeningMonster m = ParseTuple(trim);
                    if (m == null) broken = true;              // ★ 形状不认 ⇒ 这一段整段丢掉
                    else if (cur != null) cur.Add(m);
                    continue;
                }
                broken = true;                                  // ★ 别的行也不认
            }
            if (cur != null) Finish(outP, curMap, cur, broken);
            return outP;
        }

        private static void Finish(Parsed p, int mapId, List<OpeningMonster> rows, bool broken)
        {
            if (mapId <= 0) return;
            if (broken || rows.Count == 0)
            {
                p.Skipped.Add(mapId.ToString(CultureInfo.InvariantCulture));
                return;
            }
            p.Maps[mapId] = rows;
        }

        /// <summary>`(1067, 29, 10, 6, 0, 0, 10, 60),` → 8 个数；形状不对回 null。</summary>
        private static OpeningMonster ParseTuple(string trim)
        {
            int open = trim.IndexOf('('), close = trim.LastIndexOf(')');
            if (open < 0 || close <= open) return null;
            string body = trim.Substring(open + 1, close - open - 1);
            string[] parts = body.Split(',');
            if (parts.Length != 8) return null;
            var v = new int[8];
            for (int k = 0; k < 8; k++)
            {
                string s = parts[k].Trim();
                int val;
                if (!int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out val)) return null;
                v[k] = val;
            }
            var m = new OpeningMonster();
            m.Table = v[0]; m.Node = v[1]; m.Hp = v[2]; m.Gold = v[3];
            m.Defense = v[4]; m.Attack = v[5]; m.Type = v[6]; m.Weight = v[7];
            if (m.Table <= 0) return null;
            return m;
        }

        /// <summary>
        /// ★ 把那批官方开局怪填进"开局就在场上"那一组（`Event == 0`）。
        ///   · 同一种怪（`table`）复用池子里已有那一条，没有才新建（数值取**官方这一行**）；
        ///   · `node >= 0` ⇒ 落到那一格；`node &lt; 0` ⇒ 记成"随机空地补 1 只"（`StartRandom`）。
        /// 返回放进去了几只（0 = 没数据 / 没放成）。
        /// </summary>
        public static int FillInto(DiyData d, int mapId)
        {
            if (d == null) return 0;
            List<OpeningMonster> got = ForMap(mapId);
            if (got.Count == 0) return 0;

            int added = 0;
            foreach (OpeningMonster o in got)
            {
                PoolMonster m = null;
                foreach (PoolMonster x in d.Pool) if (x.Table == o.Table) { m = x; break; }
                if (m == null)
                {
                    m = DiyPool.MakePool(d, o.Table);
                    if (m == null) continue;
                    // ★ 官方那一行自己的四项（不是怪物表那份）—— 这一批的数据就是官方回放观测值
                    m.Hp = o.Hp; m.Defense = o.Defense; m.Attack = o.Attack; m.Gold = o.Gold;
                }
                if (o.Node >= 0)
                {
                    DiyPool.AddAt(d, o.Node, m.Id, 0);
                    added++;
                }
                else
                {
                    bool merged = false;
                    foreach (StartSpawn s in d.StartRandom)
                        if (s.Pool == m.Id) { s.Count++; merged = true; break; }
                    if (!merged) d.StartRandom.Add(new StartSpawn { Pool = m.Id, Count = 1 });
                    added++;
                }
            }
            DiyNotify.Touch();
            return added;
        }
    }
}
