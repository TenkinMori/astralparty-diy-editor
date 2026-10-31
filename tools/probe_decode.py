# -*- coding: utf-8 -*-
"""★ 只读侦察（第五步）：① 为什么 Texture2D 存不出来  ② catalog 里 UT_Monster* 到底有哪些名字。

只读游戏目录 + LocalLow catalog。结论写 5-DIY关卡\\tools\\probe_decode.txt。
"""
import base64
import io
import json
import os
import struct
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(HERE, "pylibs"))
sys.path.insert(0, r"D:\小小兔趴\4-服务端\tools")

LL = os.path.join(os.environ.get("USERPROFILE", ""), "AppData", "LocalLow",
                  "feimo", "AstralParty_CN")
ADDR = os.path.join(LL, "com.unity.addressables")
BUNDLES = r"D:\steam\steamapps\common\Astral Party\8vJXn6CN\AstralParty_CN_Data\StreamingAssets\aa\StandaloneWindows64"
OUT = os.path.join(HERE, "probe_decode.txt")


def _b64(t):
    return base64.b64decode(t)


def _read_key(blob, offset):
    if offset < 0 or offset >= len(blob):
        return None
    tag = blob[offset]
    if tag not in (0, 1):
        return None
    length = struct.unpack_from("<i", blob, offset + 1)[0]
    try:
        return blob[offset + 5:offset + 5 + length].decode("utf-8")
    except Exception:
        return None


def catalog_keys(path):
    raw = json.load(io.open(path, encoding="utf-8"))
    kd = _b64(raw["m_KeyDataString"])
    count = struct.unpack_from("<i", kd, 0)[0]
    keys = []
    i = 4
    for _ in range(count):
        tag = kd[i]
        if tag == 0:
            length = struct.unpack_from("<i", kd, i + 1)[0]
            keys.append(kd[i + 5:i + 5 + length].decode("utf-8", "replace"))
            i += 5 + length
        elif tag == 1:
            keys.append(struct.unpack_from("<i", kd, i + 1)[0])
            i += 5
        else:
            break
    return keys


def main():
    out = []

    def say(s):
        out.append(s)
        print(s)

    # ---------- ① 解码为什么失败 ----------
    say("=" * 78)
    say("① 直接解一张 BC7 贴图")
    import UnityPy
    p = os.path.join(BUNDLES, "c80067d0f82fc45dc11ca5035d343cbe.bundle")
    env = UnityPy.load(p)
    for obj in env.objects:
        if obj.type.name != "Texture2D":
            continue
        data = obj.read()
        say("  m_Name = %s  %sx%s  format=%s mip=%s"
            % (getattr(data, "m_Name", None), getattr(data, "m_Width", None),
               getattr(data, "m_Height", None), getattr(data, "m_TextureFormat", None),
               getattr(data, "m_MipCount", None)))
        try:
            img = data.image
            say("  data.image -> %r" % (img,))
        except Exception as ex:
            say("  data.image 抛异常：%s: %s" % (type(ex).__name__, ex))
        for attr in ("image_data", "get_image", "m_TextureFormat"):
            say("  有 %-14s = %s" % (attr, hasattr(data, attr)))
        try:
            raw = data.image_data
            say("  image_data -> %d 字节" % len(raw))
        except Exception as ex:
            say("  image_data 抛异常：%s: %s" % (type(ex).__name__, ex))
        break

    say("")
    say("  UnityPy 版本 = %s" % getattr(UnityPy, "__version__", "?"))
    for mod in ("texture2ddecoder", "etcpak", "astc_encoder", "PIL"):
        try:
            m = __import__(mod)
            say("  %-18s 在（%s）" % (mod, getattr(m, "__file__", "?")))
        except Exception as ex:
            say("  %-18s 缺：%s" % (mod, ex))

    # ---------- ② catalog 里的 UT_Monster* ----------
    say("")
    say("=" * 78)
    say("② catalog 里 UT_Monster* 的全部名字")
    cat = os.path.join(ADDR, "catalog_3.2.0.json")
    keys = catalog_keys(cat)
    mon = sorted(k for k in keys if isinstance(k, str) and k.startswith("UT_Monster"))
    say("  共 %d 条" % len(mon))
    from collections import Counter
    c = Counter("_".join(k.split("_")[:3]) for k in mon)
    for k, v in c.most_common():
        say("    %-30s %d" % (k, v))
    say("")
    for k in mon:
        say("    " + k)

    io.open(OUT, "w", encoding="utf-8").write("\n".join(out))
    print("\n报告：" + OUT)
    return 0


if __name__ == "__main__":
    sys.exit(main())
