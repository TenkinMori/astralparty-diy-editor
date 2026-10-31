# -*- coding: utf-8 -*-
"""★ 只读侦察（第四步）：一个包里到底装了什么（对象类型 + 名字 + 尺寸）。

用在"catalog 说这张图在这个包里，但抠不出来"的时候 —— 看是**名字不一样**、
**类型是 Sprite 不是 Texture2D**，还是**根本不是这个包**。

只读：只 open(path,"rb")。用法：
    python probe_bundle_content.py <bundle 名或全路径> [bundle2 ...]
"""
import io
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(HERE, "pylibs"))
sys.path.insert(0, r"D:\小小兔趴\4-服务端\tools")

BUNDLES = r"D:\steam\steamapps\common\Astral Party\8vJXn6CN\AstralParty_CN_Data\StreamingAssets\aa\StandaloneWindows64"
OUT = os.path.join(HERE, "probe_bundle_content.txt")


def main(argv):
    import UnityPy
    out = []

    def say(s):
        out.append(s)
        print(s)

    for arg in argv:
        path = arg if os.path.isabs(arg) else os.path.join(BUNDLES, arg)
        if not path.endswith(".bundle"):
            path += ".bundle"
        say("=" * 78)
        say("%s（%d 字节）" % (path, os.path.getsize(path) if os.path.exists(path) else -1))
        if not os.path.exists(path):
            continue
        env = UnityPy.load(path)
        kinds = {}
        for obj in env.objects:
            kinds[obj.type.name] = kinds.get(obj.type.name, 0) + 1
        say("  对象类型：" + ", ".join("%s×%d" % (k, v) for k, v in sorted(kinds.items())))
        for obj in env.objects:
            if obj.type.name not in ("Texture2D", "Sprite"):
                continue
            try:
                data = obj.read()
            except Exception as ex:
                say("  %-12s path_id=%s 读不出来：%s" % (obj.type.name, obj.path_id, ex))
                continue
            name = getattr(data, "m_Name", "?")
            extra = ""
            if obj.type.name == "Texture2D":
                w = getattr(data, "m_Width", "?")
                h = getattr(data, "m_Height", "?")
                fmt = getattr(data, "m_TextureFormat", "?")
                extra = " %sx%s fmt=%s" % (w, h, fmt)
            say("  %-12s %-40s%s" % (obj.type.name, name, extra))
    io.open(OUT, "w", encoding="utf-8").write("\n".join(out))
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
