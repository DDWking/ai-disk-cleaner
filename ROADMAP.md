# 重构进度与下一步（Jev 化）

这份文件记录 **v3.0.0「围绕 Jev 重构」**做到哪了、哪些是有意留下的，以及下一步的确切设计。
开发分支 `jev-core`（从 `ddw-develop` @ `5e79cdf` 切出）。

## 已完成

| 阶段 | 内容 | 提交 |
|---|---|---|
| 0 | 工作区归位（旧线留档到 `chore/preserve-release-prep-worktree`）+ 基线 + `tools/run-all-checks.ps1`（一条命令跑完 10 项离线检查） | `ce6b507` `ceca62a` |
| 1 | 判断层拆成「域 + 执行器 + 门面」：`Services/Decisions/`（契约 / `IDecisionSet` / `DecisionRunner` / `JevLimits` / `DecisionThresholds` / `JevLog` / `FolderPurposeDecisionSet`）。补上从来没校验过的 255 选项 / 2–10 档 / 字符预算 | `78085c8` |
| 2 | 删掉整套聊天栈（Node 侧车、OpenAI SDK、协议适配、工具循环、陪审团、聊天控件、336 行文案、两条不可达的聊天调用点），净 −5 962 / +518 行 | `59f3a05` |
| 3 | 归类失败把这一批标成「失败」并且下次仍可重试（聊天栈删掉后 `Failed` 状态失去了唯一生产者） | `3ccd5f9` |
| 4 | 卸载页去 AI：删掉没有任何生产调用者的「建议」机制（−388 行），页面只讲事实；`AppRecommendation.cs` → `AppFacts.cs` | `11d7dfd` `4c198f5` |
| 5 | 重复文件「留哪一个」交给判定通道（第二个判定域；入口必须由用户点） | `290202f` |
| 6 | 版本 3.0.0 + 打包 + README/notes；设置迁移断言（老配置不丢） | `b58718e` `f04061e` `581e4e6` |

**回归**：10 项离线检查全绿（`SafetyCheck 1108` / `StartupCheck 191` / 其余见 `tools/run-all-checks.ps1`）；
主工程 0 警告 0 错误。包在 `dist/DashaoHuo-3.0.0-<date>-win-x64`（自包含 189 MB，zip 75 MB，内含 exe 版本 3.0.0.0）。

## 判定层现在的两个域

| 域 | 对象 | 原语 | 写回 | 阈值 |
|---|---|---|---|---|
| `FolderPurposeDecisionSet` | 整理页的文件夹（本地认不出用途的） | `choice`（13 个固定选项） | `OrganizeNode.BatchPurpose` | 按类别 0.60 / 0 / 0.75 |
| `DuplicateKeepDecisionSet` | 清理页的重复组（每组一道题） | `choice`（选项 = 组内成员 `m1..mN`） | 保留项那一行的 `Reason`（**只加一句话**） | 统一 0.75，低于就**不给建议** |

第二个域**没有重写任何一条执行规则**（分批 / 重试 / 折半 / 换代守卫 / 计数遥测全在
`DecisionRunner` 里）—— 这是 Phase 1 那层抽象是否成立的实测证据。

## 有意留下没做（下一步）

### 1. 随聊天通道一起失去归属的两处死代码

- `FolderPurposeRules.BuildAiInput`（4 参重载）与 `Services/SourceSnippet.cs` 的
  `SourceSnippetCollector` / `SnippetSet` / `IsAllowed` / `FormatForAi` —— 它们是
  「把逐目录摘要 + README 白名单片段发出去」那套的出站构造，聊天路径删掉后没有生产调用者了。
  **`SourceSnippetReader.Redact` / `LooksClean` 要留下**（`RecognitionStore` 还在用）。
- 删除时要同时改 `tools/SafetyCheck/LayeredCleanTests.cs` 的片段测试块与
  `tools/UiRegressionCheck/Run.ps1` 里读 `SourceSnippet.cs` 的断言。

### 2. 重复建议要不要落盘（**刻意没做**）

现在建议只活在内存里（这一轮扫描有效，下次扫描重问一次，约 $0.0001）。
要落盘就得先回答「用户怎么清掉一条错建议」—— 一个清不掉的落盘建议比每次重问更糟。
`RecognitionStore` 加 kind `dup-keep`（键 = 现有 `DuplicateGroupKey`）是一行的事，
但清除入口得一起做。

### 3. 顺手可做的小事

- 「测试判定通道」目前报往返毫秒 / 模型 id / 花费；可以考虑把最近 N 次判定花费累计显示出来，
  让用户对「一次分类要多少钱」有直觉。
- Jev 端点是 alpha：服务端回报的模型 id 已经进日志，但**没有**对「回报的模型与配置不一致」出声。
  遇到版本漂移时可以在 `AiClient.ParseDecisions` 里加一条 warning。
- 卸载页的占用归属表（`GenericRoots` 等）是活的，别误删。

## 已知取舍（不是 bug）

- **AI 不产生自由文本**：解释来自本地规则文案（`AppSignatures.PlainNote` 等），
  判定只给标签 + 概率。所以界面上不会出现「模型写的一段解释」——这是有意的。
- **卸载页不进 AI**：用户明确决定（只讲事实）。
- **清理中心不进 AI**：8 条规则每条都声明了用途，没有「认不出来」的堆积。
