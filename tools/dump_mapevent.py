# -*- coding: utf-8 -*-
"""
★ 只读：把 MapEvent.bin 解出来 —— 事件 id / 官方名字（STRMapEvent.bin）/ 触发点 / 效果类型 + 参数。

★ 字段号不是猜的：★ **实测 dump** 出来的形状（`probe_mapevent_raw.py`）+
  服务端 `astralnext/gamedata.py:_load_map_events` 逐条对齐。

   顶层：field 1 (wire2) = 一行 MapEventInfoConfigure
         field 2 (wire2) = **同一批的 map 形态**（entry 里嵌 body，字段一样）—— 要和 1 去重
   行里：
     1 Id            (wire 5 sfixed32)
     2 CardID        (wire 5)
     4 Triggerparams (wire 2，里面 field 1 wire0 = **进度 × 2** 的半格刻度)
     5 TriggerLandUnits (wire 2，packed varint)
     6 MapEventEffectConfigs (repeated，每条 = {1:效果类型, 2:{1: packed fixed32 参数}})
     3 IsHide

效果类型枚举 `MapEventEffectType`（从 AstralParty.Runtime.dll 生成代码常量读出来的）：
    1 CreateMonster / 2 CreateMonsterOneKind / 3 MonsterAttrChange
    4 GainCard / 5 LandUnitsGroupIdTrigger / 6 ActiveBoss

★ 只读；一个字节都不写。
"""
import os
import struct
import sys

TABLE = r"D:\小小兔趴\4-服务端\server\data\gamedata"

EFFECT_NAMES = {
    1: "CreateMonster 刷怪（点名怪物表 id）",
    2: "CreateMonsterOneKind 刷一种怪（[怪id, 数量]）",
    3: "MonsterAttrChange 怪物攻防变更（[攻+N, 防+N]）",
    4: "GainCard 获得卡牌（卡牌 id）",
    5: "LandUnitsGroupIdTrigger 触发地图单位组（参数=组 id）",
    6: "ActiveBoss 激活 Boss（怪物表 id）",
}


def varint(data, i):
    v = 0
    shift = 0
    while i < len(data) and shift < 64:
        b = data[i]
        i += 1
        v |= (b & 0x7F) << shift
        if not (b & 0x80):
            return v, i
        shift += 7
    return v, i


def fields(data):
    out = []
    i = 0
    while i < len(data):
        key, i = varint(data, i)
        num = key >> 3
        wire = key & 7
        if num <= 0:
            break
        if wire == 0:
            v, i = varint(data, i)
            out.append((num, wire, v, None))
        elif wire == 5:
            if i + 4 > len(data):
                break
            out.append((num, wire, struct.unpack_from("<i", data, i)[0], None))
            i += 4
        elif wire == 1:
            if i + 8 > len(data):
                break
            out.append((num, wire, struct.unpack_from("<q", data, i)[0], None))
            i += 8
        elif wire == 2:
            ln, i = varint(data, i)
            if i + ln > len(data):
                break
            out.append((num, wire, None, data[i:i + ln]))
            i += ln
        else:
            break
    return out


def fixed32s(blob):
    if not blob:
        return []
    if len(blob) % 4 == 0:
        vals = list(struct.unpack("<%di" % (len(blob) // 4), blob))
        if all(0 <= v <= 10 ** 7 for v in vals):
            return vals
    out = []
    i = 0
    while i < len(blob):
        raw, i = varint(blob, i)
        out.append((raw >> 1) ^ -(raw & 1))
    return out


def strtable(name):
    path = os.path.join(TABLE, name)
    table = {}
    if not os.path.exists(path):
        return table
    with open(path, "rb") as fh:
        data = fh.read()
    for num, wire, _v, blob in fields(data):
        if num != 1 or wire != 2 or blob is None:
            continue
        key, text = None, None
        for k, w, v, _b in fields(blob):
            if k == 1 and w in (0, 5):
                key = v
            elif k == 2 and w == 2 and _b is not None:
                try:
                    text = _b.decode("utf-8")
                except Exception:
                    text = None
        if key is None or not text:
            continue
        if any(ord(c) < 32 for c in text):
            continue
        table.setdefault(key, text)
    return table


def parse_row(blob):
    row = fields(blob)
    rid = next((v for k, w, v, _b in row if k == 1 and w == 5), None)
    if rid is None:
        return None
    # ★ 实测（probe_mapevent_trigger.py）：field 4 是**裸 varint** 的 "进度 × 2"
    #   （32121 → 0x02 = 2；32122 → 0x08 = 8），**不是**嵌套的 packed 数组。
    #   服务端 gamedata.map_event_progress 就是 `field4 // 2`，这里照抄。
    trig = None
    for k, w, v, b in row:
        if k == 4 and w == 2 and b:
            trig = b[0] if len(b) == 1 else None
            if trig is None:
                got = fixed32s(b)
                trig = got[0] if got else None
    land = None
    for k, w, _v, b in row:
        if k == 5 and w == 2 and b:
            land = fixed32s(b)
    hide = next((v for k, w, v, _b in row if k == 3 and w in (0, 5)), None)
    effects = {}
    for k, w, _v, b in row:
        if k != 6 or w != 2 or not b:
            continue
        kind, values = None, []
        for sk, sw, sv, sb in fields(b):
            if sk == 1 and sw == 5:
                kind = sv
            elif sk == 2 and sw == 2 and sb is not None:
                for q, s, _y, y in fields(sb):
                    if q == 1 and s == 2 and y is not None:
                        values = fixed32s(y)
        if kind is not None:
            effects[int(kind)] = values
    return {
        "id": int(rid),
        "trigger_x2": trig,
        "progress": None if trig is None else trig / 2.0,
        "hide": hide,
        "land_units": land or [],
        "effects": effects,
    }


def load_events():
    path = os.path.join(TABLE, "MapEvent.bin")
    with open(path, "rb") as fh:
        data = fh.read()
    rows = {}
    for num, wire, _v, blob in fields(data):
        if wire != 2 or blob is None or num not in (1, 2):
            continue
        got = parse_row(blob)
        if got is None:
            continue
        # field 1 的真行优先（field 2 那份是 map 形态，字段一样；只有 1 缺了才用它兜底）
        if got["id"] not in rows or num == 1:
            rows[got["id"]] = got
    return rows


def main():
    names = strtable("STRMapEvent.bin")
    rows = load_events()
    out = ["MapEvent.bin 行数 = %d ；STRMapEvent.bin 条目 = %d" % (len(rows), len(names))]
    named = 0
    with_trigger = 0
    with_effect = 0
    for eid in sorted(rows):
        r = rows[eid]
        nm = names.get(eid, "")
        if nm:
            named += 1
        if r["trigger_x2"] is not None:
            with_trigger += 1
        if r["effects"]:
            with_effect += 1
        eff = "；".join("%d:%s=%s" % (k, EFFECT_NAMES.get(k, "?"), r["effects"][k])
                        for k in sorted(r["effects"]))
        out.append("id=%d  名=%s  触发_x2=%s(进度%s)  hide=%s  组=%s  效果：%s"
                   % (eid, nm or "(无名字)", r["trigger_x2"], r["progress"], r["hide"],
                      r["land_units"], eff or "(没有效果)"))
    out.append("有名字 %d / %d ；有触发点 %d ；有效果 %d" % (named, len(rows), with_trigger, with_effect))
    print("\n".join(out))
    dst = os.path.join(os.path.dirname(os.path.abspath(__file__)), "mapevent_dump.txt")
    with open(dst, "w", encoding="utf-8") as fh:
        fh.write("\n".join(out) + "\n")
    print("（已写入 %s）" % dst)


if __name__ == "__main__":
    sys.stdout.reconfigure(encoding="utf-8")
    main()
