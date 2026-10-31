# 交接说明（STATUS）

> 给下一次对话/接手的人：先读 `README.md`，再读本文件。

## 项目一句话

**吉星派对 · DIY 关卡编辑器**：本地 WPF 单文件工具，编辑关卡 JSON、导出给游戏/私服使用。
纯本地，不联网、不改游戏。

## 仓库与本地

| 仓库路径 | 本地原目录 |
|---|---|
| `src/` | `D:\小小兔趴\5-DIY关卡\src` |
| `棋盘缓存/` `贴图缓存/` `图标缓存/` | `D:\小小兔趴\5-DIY关卡\` 同名目录 |
| `tools/` | `D:\小小兔趴\5-DIY关卡\tools`（不含 `pylibs/`） |
| `docs/` | `D:\小小兔趴\5-DIY关卡\文档` |
| 根目录 md/json | 同目录 |

> 本仓库是整理后的**源码快照**，只保留最新；`dist/`、`bin/`、`obj/`、旧版 exe 不入库。

## 当前状态（2026-10-31）

- ✅ 编辑器可用：棋盘编辑、怪物放置（一格多怪）、事件/触发器、模板、撤销、导出、自检
- ✅ 内置缓存：`棋盘缓存` + `贴图缓存` 嵌进 exe，单文件可独立运行
- ✅ 2026-10-17 版本随「森秋幻境 · 极限」模板一起发布（exe 约 190MB）
- ✅ 84 只怪物立绘在 `图标缓存/`，导出脚本 `tools/export_monster_portraits.py`
- ⏳ 关卡任务（DIY）可行性已评估，见 `docs/关卡任务(DIY)可行性-20261010.md`

## 构建 / 运行

```powershell
dotnet build   src\DIY关卡编辑器.csproj -c Release
dotnet publish src\DIY关卡编辑器.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o dist
```

运行需要 `dist\编辑器配置.json`（`table_dir` 指向游戏配置表）。用法见
`森秋幻境极限-编辑器用法.md`。

## 已知问题 / 待办

- [ ] 单文件 exe 约 190MB，启动偏慢（内置棋盘 + 贴图缓存导致）
- [ ] 游戏更新后 `table_dir` 指向的新表要同步
- [ ] DIY 关卡上线需服务端在 `data/diylevels.json` 登记
- [ ] `tools/pylibs`（约 40MB）未入库，跑导出脚本需自行装 UnityPy

## 给新会话的流程

1. 读 `README.md` → 本文件 → `森秋幻境极限-编辑器用法.md`
2. 改代码在本地 `D:\小小兔趴\5-DIY关卡` 改，不要直接改仓库
3. 改完同步：`powershell -NoProfile -File D:\AstralPartyGit\diy-sync.ps1 -Message "改了什么"`
4. 导出/构建产物（exe、缓存）不要提交
