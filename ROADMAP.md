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
| 4 | 卸载页去 AI：删掉没有任何生产调用者的「建议」机制，页面只讲事实 | （见 git log 最后一条） |
| 6 | 版本 3.0.0 + 打包 + README/notes | `b58718e` |

**回归**：10 项离线检查全绿（`SafetyCheck 1059` / `StartupCheck 197` / 其余见 `tools/run-all-checks.ps1`）；
主工程 0 警告 0 错误。包在 `dist/DashaoHuo-3.0.0-<date>-win-x64`。

## 有意留下没做（下一步）

### 1. 重复文件「留哪一个」交给判定模型（用户已选，设计已定）

**为什么没做**：它需要一个新的用户入口 —— 而入口的位置决定了对不对，不能顺手塞。

**关键设计决定：必须是用户点的，不能跟着扫描自动跑。**
软件对外承诺「扫描 / 切页 / 展开 / 筛选 = 0 次请求」，而且这条是用**真实出站计数**断言的
（`StartupCheck` §9.4）。重复检测属于扫描阶段，如果在里面顺手问一轮模型，这条承诺就破了 ——
用户会在不知情的情况下花钱。所以入口应当放在清理页的「重复」区（一个明确写清条数的按钮，
例如「问一下留哪个（3 组）」），而不是扫描流程里。

**引擎已经就位，只差接线**（`DecisionRunner` + `IDecisionSet<T>`）：

```csharp
sealed class DuplicateKeepDecisionSet : IDecisionSet<DuplicateGroup>
{
    // 每组一道 choice：选项 = 组内成员（键 m1..mN），说明含**脱敏**路径 + 大小 + 修改时间 + 所在目录
    // state = 组摘要（大小 / 成员数 / 内容指纹）
    // 成员 > 12 的组直接 Skipped（不猜）
}
```

落地时要守住的四条（都要有断言）：

1. **只写展示**：建议只落在「保留项」那一行的说明文字上，
   `Selected` / `CanDelete` / `Risk` 一个都不许动（重复项本来就是「永久人工选择」）。
2. **把握不够就不出建议**：阈值 `DecisionThresholds.DuplicateKeep = 0.75`，低于它写「AI 没把握，未给建议」，
   绝不硬挑一个。
3. **不改 keep/extra 归属**：现有「路径最短者为保留项」的基线不动，AI 只是多一句提示
   （如果它建议留另一个，就明说「AI 建议改留 …」）。
4. **脱敏**：走 `PathRedactor.Outbound`。

**落盘（可选）**：可以用 `RecognitionStore` 新增 kind `dup-keep`（键 = 现有 `DuplicateGroupKey`）。
没做是因为「怎么让用户清掉一条错建议」还没有入口 —— 一个清不掉的落盘建议比每次重问一遍更糟
（重问一次约 $0.0001）。要做就连同清除入口一起做。

### 2. 随聊天通道一起失去归属的两处死代码

- `FolderPurposeRules.BuildAiInput`（4 参重载）与 `Services/SourceSnippet.cs` 的
  `SourceSnippetCollector` / `SnippetSet` / `IsAllowed` / `FormatForAi` —— 它们是
  「把逐目录摘要 + README 白名单片段发出去」那套的出站构造，聊天路径删掉后没有生产调用者了。
  **`SourceSnippetReader.Redact` / `LooksClean` 要留下**（`RecognitionStore` 还在用）。
- 删除时要同时改 `tools/SafetyCheck/LayeredCleanTests.cs` 的片段测试块与
  `tools/UiRegressionCheck/Run.ps1` 里读 `SourceSnippet.cs` 的断言。

### 3. 顺手可做的小事

- `AppRecommendationService` 里仍有几张只为事实服务的表（`GenericRoots` 等），
  它们**是活的**（占用归属要用），别误删。
- 「测试判定通道」目前报往返毫秒 / 模型 id / 花费；可以考虑把最近 N 次判定花费累计显示出来，
  让用户对「一次分类要多少钱」有直觉。
- Jev 端点是 alpha：模型 id 已经锁在设置里并在日志里记录服务端回报值，但**没有**对
  「回报的模型与配置不一致」出声。如果以后遇到版本漂移，可以在 `AiClient.ParseDecisions`
  里加一条 warning。

## 已知取舍（不是 bug）

- **AI 不产生自由文本**：解释来自本地规则文案（`AppSignatures.PlainNote` 等），
  判定只给标签 + 概率。所以界面上不会出现「模型写的一段解释」——这是有意的。
- **卸载页不进 AI**：用户明确决定（只讲事实）。
- **清理中心不进 AI**：8 条规则每条都声明了用途，没有「认不出来」的堆积。
