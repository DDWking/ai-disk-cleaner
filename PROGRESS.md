# 开发进度

两人看这一份就够。改代码前先看「进行中」，避免撞车；做完一件事在日志最上面加一行，并更新状态表。

## 现在做到哪

| 模块 | 状态 | 说明 |
|---|---|---|
| MFT 秒扫 | 进行中 | 完整 MFT 已读到。正在修文件挂不到目录：硬链接 / 扩展记录上的 `$FILE_NAME` |
| 目录树 + 当前目录列表 | 可用 | 左树带占比/大小，右表当前目录，按大小降序 |
| 单位显示 | 可用 | 只显示 KB / MB / G |
| 崩溃修复 | 可用 | 递归改迭代、孤儿文件不再堆到根 |
| AI 说明 | 可用 | 表格一列「说明」：规则原因，分析后被 AI 覆盖（浅青字）。不判风险、不碰勾选。模型在设置里选 |
| 卸载页 | 可用 | 右侧第三页，BCU 列已装软件并走官方卸载；含 Steam / Windows 功能；卸完扫残留，勾选后才删 |
| 真实删除/回收站 | 可用 | 单项右键 + 清理页勾选批量，均进回收站，系统项保护 |
| 扩展名统计（WizTree 右侧那种） | 可用 | 右侧 EXT/TYPE，随当前目录变化 |
| 非 NTFS | 未做 | 目前只支持 NTFS |

## 谁在做什么

| 人 | 正在做 | 备注 |
|---|---|---|
| DDWking | MFT 大小/文件数对齐 WizTree | 扫完看 `scan-timing.log` 里 data run 段数 |
| bissensei | — | 开工前在这里占坑 |

**占坑规则：** 准备做某块，先改这张表再写代码。做完把「正在做」改成 `—`，日志里记一笔。

两人在 **`ddw-develop`** 上直接 push。发布才合 `main`。推之前先 `git pull`。步骤见 [CONTRIBUTING.md](CONTRIBUTING.md)。

## 下一步（按优先级）

1. 核对扫描结果是否接近 WizTree
2. ~~规则清理面板~~ 已做
3. ~~卸载页~~ 已做（列已装 + Steam + Windows 功能 + 官方卸载 + 残留勾选删除；驱动 / 更新后做）
4. Treemap（自绘）
4. ~~可选模型解释勾选项~~ 已做（自定义供应商）

想做别的，加到这张表，别闷头开干。

## 已知问题

- `$MFT` 作为普通文件打开会 Access Denied（错误 5），正确路径是读卷 + data run，不要再试 `CreateFile(X:\$MFT)` 当快路径
- 扫描要管理员权限；UAC 拒绝会回退到很慢的递归扫描
- 本机 git 用户曾是占位符，仓库级已改成 `DDWking`

## 日志

新的写在最上面。格式：

```
### YYYY-MM-DD  名字
- 做了什么
- 还差什么 / 下次谁接
```

### 2026-09-04  DDWking
- 清理面板八条一起改：分类改成一行芯片，表格占满；按钮改成「分析」；模型选择挪到设置（沿用上次选的模型）；进度写成「分析 i/N」；点左树会过滤右侧清理列表；风险色只上风险列；全选 / 勾选可安全删除 / 删除勾选项并排；删掉 `AiRun_Click` / `RunJury` / `HarvestNotes` 这套自动勾选死代码。
- 还差：非 NTFS；`Jury.cs` 里评选类型、`ChatBubble` 现在没人用，哪天再清。

### 2026-09-04  DDWking
- 「可清理原因」和「这是什么」合成一列「说明」：没分析显示规则原因，分析后被 AI 覆盖（浅青字）。去掉底部「AI 解释勾选项」，上面「分析」就够了。

### 2026-09-04  DDWking
- 模型选择控件从 `ComboBox + GroupStyle` 换成「按钮 + 弹出列表」（`ModelRow` 在 `Services/Jury.cs`）。
  ComboBox 分组在黑白主题下只渲染供应商标题、模型行整批不显示，别再往回改。
- AI 职责收窄：**只解释「这是什么」，不再判风险**。新增 `CleanItem.AiNote` + 表格「这是什么」列，
  和规则给的「可清理原因」分列显示；`Risk` / `Selected` / `Reason` 由解析器保证碰不到。
- 解析逻辑抽到 `Services/AiNoteParser.cs`，配了个手工检查：`dotnet run --project tools/AiNoteParserCheck`
  （18 条断言，覆盖模型漏写 GOTO、塞风险词、照抄大小、markdown 包裹、编造路径等情况）。
- 「AI 解释勾选项」和右键「问 AI 这是什么」改走同一条只说明路径；前者原来会**自动帮你勾选**，已去掉。
- 还差：`AiRun_Click` / `RunJury` / `HarvestNotes` 这套多模型投票是死代码（AI 分析页已删，没有按钮能触发），
  哪天顺手清掉；非 NTFS 仍未做。

### 2026-04-08  DDWking
- 微动效：弹窗淡入、不确定进度条呼吸；设置里自定义 AI 供应商（OpenAI 兼容 / Anthropic / Gemini / Ollama），只把勾选项发给模型
- 还差：Treemap

### 2026-04-08  DDWking
- 合进 main，发 v1.1.0：清理页 + 卸载页（注册表/商店/Steam/Windows 功能 + 残留勾选删除）
- 还差：Treemap、Windows 更新 / 驱动

### 2026-04-08  DDWking
- 卸载页接上 BCU Steam 游戏和 Windows 功能；Steam 单独成组，功能默认折叠；卸功能会再确认一次（DISM，可能重启）
- 还差：Windows 更新 / 驱动 / Chocolatey / Scoop

### 2026-04-07  DDWking
- 卸载完成后扫 BCU 残留，高置信度默认勾选，低置信度要自己勾，确认后才删（文件进回收站，注册表直接删）
- 还差：Steam / 驱动

### 2026-04-07  DDWking
- 右侧第三页「卸载」：submodule 引用 BCU UninstallTools，列出注册表 + 商店应用，勾选后走官方卸载
- 还差：残留扫描、孤儿软件

### 2026-04-07  DDWking
- 右侧加「清理」页：临时缓存、大文件、一年未改、重复哈希、空文件夹、失效快捷方式、超长路径、和上次扫描对比
- 勾选后批量进回收站，系统目录默认保护
- 还差：Treemap

### 2026-03-31  DDWking
- C 盘根显示 `$MFT` / `$LogFile` / `pagefile.sys` 等系统文件（之前跳过记录 0–15）
- 右侧加扩展名占用分析，随当前目录变化
- 还差：AI 分析

### 2026-03-31  DDWking
- 建树改成按每条 `$FILE_NAME` 挂目录（硬链接、扩展记录上的名字之前会丢）
- 还差：用户核对「文件夹在、文件不在」的目录是否回来了

### 2026-03-31  DDWking
- 用 `OpenFileById($MFT)` + retrieval pointers 读完整碎片（记录 0 的 data run 经常只有第一段，所以 Users 只有 7.8G）
- 窗口标题改成「AI 磁盘清理 · MFT」，方便确认跑的是新 exe
- 还差：和 WizTree 对总大小/文件数

### 2026-03-31  DDWking
- 修文件大小：`$MFT` 碎片按逻辑 VCN 读（之前按磁盘 LCN 排序会把记录号打乱）
- 非驻留 `$DATA` 同时看 DataSize / InitializedSize
- 扫盘加顶部进度条（百分比 + 阶段 + 文件数），避免看起来像卡死
- 还差：和 WizTree 对总大小/文件数

### 2026-03-31  DDWking
- 仓库开源：https://github.com/DDWking/ai-disk-cleaner （MIT）
- 删掉 `docs/`
- MFT 改为按 data run 读完整表；界面改成左树右表（WizTree 布局）
- 文件大小从 `$DATA` 读，不再用 `$FILE_NAME` 里的垃圾值
- 还差：和 WizTree 对一下总大小/文件数；AI 分析还没做

### 更早（压缩）
- WPF 界面、真实递归扫描、MFT 秒扫、栈溢出修复、去掉 TreeMap、按大小排序和占用条
