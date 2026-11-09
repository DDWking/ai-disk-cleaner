# 06 · 落地路线

> 状态：**P0 已完成 · P1 已完成（2026-11-09）** ｜ 关联决策：C1–C6 ｜ 依赖：[01](01-architecture.md)–[05](05-safety-and-eval.md)

## 0. 一句话

**先做不联网、可离线测的「工具层 + 证据层」，再接模型做「一次报告」，最后才加循环和追问；Jev 在接模型那一步一起删。**

## 1. 阶段总览

| 阶段 | 目标 | 产出 | 估算 | 可回滚 |
|---|---|---|---|---|
| P0 | 工具层 + 证据层（无模型、无 UI） | 5 个新服务 + 离线检查全绿 | 2–3 天 | 是 |
| P1 | 聊天客户端 + L1 报告 + 可交互报告页 | 一次分析出报告，能跳转勾选 | 5–7 天 | 是（开关关闭则不出现） |
| P2 | L2 循环 + 追问下钻 | 最多 6 轮的有界循环 | 5–7 天 | 是（默认 L1） |
| P3 | 评测 + 打磨 + 发布 | 黄金集指标达标、安装包 | 3–5 天 | — |

总计约 2–3 周（一个人、按每天有效 4–6 小时）。

## 2. P0：工具层 + 证据层（✅ 已完成 2026-11-09）

**原则：这一阶段不写网络代码、不碰 UI，全部能被离线检查覆盖。**

**落地状态：** 10 个源文件 + 1 个离线检查工程已合入工作区；`dotnet build` 0 警告 0 错误，`tools/AgentToolCheck` 118 条检查全绿（`PASS 118  FAIL 0`），`SafetyCheck` 1185 条无回归。下一阶段 P1 尚未开始。

### 2.1 新增文件

```text
src/AiDiskCleaner/Services/Agent/
  ScanSession.cs          // scanId、盘符、根节点、质量、创建时间、索引入口
  FolderIndex.cs          // folderId ↔ FileEntry；确定性 ID；卡片缓存
  EvidenceCard.cs         // 卡片 DTO + 构建器（只读、纯函数）
  CandidateSelector.cs    // 候选入口 + 打分 + 硬排除
  ToolEnvelope.cs         // 统一返回信封 + 错误码 + JSON 序列化
  ToolDto.cs             // 工具返回 DTO（概览 / 列表 / 条目）
  ToolHost.cs            // 白名单、参数校验、预算记账、错误转换
  Tools/GetOverviewTool.cs
  Tools/ListFoldersTool.cs
  Tools/GetFolderCardTool.cs
  Tools/QueryEntriesTool.cs
```

首版 4 个只读工具；`find_duplicates` / `list_installed_apps` / `check_locks` / `plan_delete` 在 P1 接（它们依赖现有服务，接口先留好）。

### 2.2 关键设计点

- `folderId`：`"f_" + SHA256(规范化相对路径)[..12]`，同一台机器跨扫描稳定，便于缓存与用户偏好；不把哈希当路径用，任何操作都回索引取 `FileEntry`。
- `ScanSession` 不复制整棵树，只持有根 `FileEntry` 与索引；索引按需构建（第一次用到时 DFS 一次，之后 O(1)）。
- `CandidateSelector` 纯本地、确定性：输入根节点 + 规则命中 + 保护路径，输出排序后的候选列表；不依赖 AI。
- `ToolHost` 是唯一出口：模型/上层只能通过它拿数据；它负责给每个返回加信封、截断、预算记账。
- 所有服务不引用 `MainWindow`、不引用 WPF 类型。

### 2.3 离线检查（新增 `tools/AgentToolCheck`）

必须覆盖：

- 索引：10 万节点的树，构建 <500ms；ID 稳定（两次构建相同）；未知 folderId → `FOLDER_NOT_FOUND`；
- 卡片：字段齐全、样本截断、用户名替换、同 scanId+folderId 两次结果字节一致；
- 候选：保护路径 100% 排除；同父 ≤8；分数排序单调；
- 工具：信封字段齐全；非法参数 → `INVALID_ARGUMENT`；超上限 → `truncated=true`；预算耗尽 → `BUDGET_EXCEEDED`；
- 安全：`ToolHost` 没有 execute/delete/shell（反射扫描工具清单断言）。

**实际结果：** 118 条检查覆盖上述全部条目，另加 `get_folder_card` 对保护路径返回 `OUT_OF_SCOPE`、候选 `relPath` 已做 `%USER%` 脱敏、候选不把容器根（Program Files/Windows/Users 等）当目标；已接入 `tools/run-all-checks.ps1`（`Kind='exe'`），单跑与全量 runner 均为 PASS。

### 2.4 验收（已完成）

- [x] 新增检查全绿，原有 10 套检查不回归（AgentToolCheck 118 / SafetyCheck 1185，Jev 删除后 SafetyCheck 943）；
- [x] 不引入新 NuGet 依赖（System.Text.Json 随 .NET 8 自带）；
- [x] 构建 `dotnet build` 0 警告 0 错误。

## 3. P1：聊天客户端 + L1 报告（✅ 已完成 2026-11-09）

**落地状态：** 聊天客户端（`Services/Agent/Chat/ChatModels.cs`、`OpenAiCompatChatModel.cs`、`ChatModelFactory.cs`）、L1 编排（`PromptBuilder.cs`/`ReportParser.cs`/`ReportAssembler.cs`/`AgentReportService.cs`）、可交互报告（`MainWindow.AgentReport.cs` + XAML 的 `AiReportBtn`/`ReportBody`）、设置页聊天化（DeepSeek / OpenAI 兼容 / Ollama 模板）全部完成；Jev 判定通道整条删除；`tools/AgentReportCheck` 53 条检查全绿；全量 12 项离线检查绿。

**与计划的差异：** 报告没有做独立窗口，直接复用 `DialogCard` 弹层并加宽到 940px（`ReportBody`/`ReportContent`）；入口在「按文件夹删除」页页头，报告动作只有「去处理」= 切页 + 定位/高亮文件夹（不改勾选、不删除）；`find_duplicates` 等其余 5 个工具仍未实现（工具层首版只读 4 个）。

### 3.1 新增文件

```text
src/AiDiskCleaner/Services/Agent/
  ChatMessage.cs / ChatRequest.cs / ChatResponse.cs   // 最小协议模型
  IChatModel.cs
  OpenAiCompatChatModel.cs     // POST {base}/chat/completions，SSE
  ChatModelFactory.cs          // 从 AiProviderCfg 读配置（复用 AiConfigurationService）
  AgentReportService.cs        // L1 编排：候选→卡片→批→校验→合并→ReportCard
  ReportParser.cs              // 剥离围栏、解析 JSON、走 03 校验流水线
  ReportAssembler.cs           // 分组、脱敏回填、生成 ReportCard
src/AiDiskCleaner/
  AgentReportWindow.xaml(.cs)  // 或 MainWindow 内嵌页；报告卡片 + 按钮
```

### 3.2 删除/停用 Jev

- 设置页去掉 Jev 判定温度/启用开关；`AiProviderCfg` 只保留 BaseUrl/Model/Protocol/Key；
- 删除：JevContracts、JevLimits、JevLog、DecisionRunner、DecisionThresholds、FolderPurposeDecisionSet、AiPurposeBatchService、AiGateway（若只服务 Jev）；
- 保留：AiClient 的 HTTP/SSE 基础设施可改成 ChatModel 复用；`AiConfigurationService` 的预设/诊断保留；
- 迁移：旧配置里的 Jev 字段读出来丢弃，不报错；
- 验收：全仓搜索 `Jev` 只剩文档/迁移说明；全量检查绿。

### 3.3 报告页交互（C5）

每条建议卡片：

```text
[可回收 1.2 GB]  浏览器缓存 · 2 个目录
原因：Chrome/Edge 缓存目录，可安全重建          置信度 高
[查看证据]  [去处理]
```

- 入口在「按文件夹删除」页页头统计行右侧（AI 只服务这个页面，清理中心不再放入口）；
- 「查看证据」→ 卡片内联展开该目录的字段 + 命中的本地规则；
- 「去处理」→ 切到「按文件夹删除」页，展开祖先并滚动定位/高亮该文件夹；**不改勾选、不删除**（见 04 Q5），勾选始终由用户自己点。

### 3.4 验收

- 断网 → 明确失败提示，本地清理功能不受影响；
- 报告里的每条建议都能点开证据，且证据字段与本地卡片一致；
- 全量检查绿；打包 exe 能启动并完成一次分析（用真实 key 手测）。

## 4. P2：L2 循环 + 追问

- 循环、工具调用累积、停止条件按 04 §5；
- UI 增加追问输入框（仅报告页可用），回答仍落成报告条目；
- 工具扩展到 9 个（find_duplicates/list_installed_apps/check_locks）；
- 验收：6 轮/24 调用/90s 硬上限生效；连续 3 次工具错误能收尾；取消无残留。

## 5. P3：评测与发布

- 黄金集 ≥20 条，指标达到 05 §8.2；
- 红队清单全过；
- 发布：`tools/release.ps1` 出 zip + sha256，版本号 4.0.0（去掉 Jev 属于大版本变更）；
- 文档：README/ROADMAP 更新，docs/agent 状态改「已定稿」。

## 6. 任务拆分（可直接开工）

| # | 任务 | 文件 | 依赖 |
|---|---|---|---|
| 1 | ScanSession + FolderIndex | Services/Agent/* | 无 | ✅ |
| 2 | EvidenceCard + 构建器 | 同上 | 1 | ✅ |
| 3 | CandidateSelector | 同上 | 1 | ✅ |
| 4 | ToolEnvelope + ToolHost | 同上 | 1 | ✅ |
| 5 | 4 个只读工具 | Tools/* | 3、4 | ✅ |
| 6 | AgentToolCheck 项目 + 接入 run-all-checks | tools/AgentToolCheck/* | 1–5 | ✅ |
| 7 | ChatModel + Factory | Services/Agent/* | 无 | ✅ |
| 8 | ReportParser + Assembler | 同上 | 2 | ✅ |
| 9 | AgentReportService（L1） | 同上 | 6、7、8 | ✅ |
| 10 | 报告页 UI + 三个按钮 | AgentReportWindow / MainWindow | 9 | ✅（复用 DialogCard + MainWindow.AgentReport.cs） |
| 11 | 删 Jev | 多个 | 7 | ✅ |
| 12 | L2 循环 | Services/Agent/* | 9、10 | 未开始 |

## 7. 风险与对策

| 风险 | 概率 | 影响 | 对策 |
|---|---|---|---|
| 模型输出质量不稳 | 中 | 高 | 严格校验 + 置信度 + 评测集；不足以 keep 兜底 |
| 中转/官方限流 | 中 | 中 | 串行批、退避重试、降级本地报告 |
| 用户不信任 AI 建议 | 中 | 中 | 证据可点开、明确不确定、永不自动删 |
| 删 Jev 引入回归 | 低 | 中 | 分步删、全量检查、保留迁移说明 |
| UI 改动过大 | 中 | 中 | 报告页独立窗口/独立页，先不动主流程 |
| 成本超预期 | 低 | 中 | 硬预算 + 本地候选筛选（只发 100 张卡） |

## 8. 明确不做

- 不做通用聊天助手（只服务"这次扫描"）；
- 不做自动删除/无人值守；
- 不做文件内容分析（`peek_files` 首版不做）；
- 不恢复 Node sidecar；
- 不换语言/不整体重构；
- 不做 MCP 外部工具（P2 之后再看）。

## 9. 完成定义（DoD）

- [x] 任一失败路径都不会导致删除或数据丢失（AI 侧无删除 API，删除仍走 `DeletionCoordinator` 预检 + 回收站）；
- [x] 报告每条建议都有可点开的本地证据（`ReportEvidence` + evidence chips，校验不过的条目不展示）；
- [x] 全量离线检查绿（12 项：SafetyCheck 943 / StartupCheck 176 / UiRegressionCheck / AgentToolCheck 118 / AgentReportCheck 53 等）；
- [ ] 真实 key 手测：一次分析 ≤90s，报告可跳转勾选（需用户环境与 key；离线部分已由 `AgentReportCheck` 覆盖）；
- [ ] 打包 exe 可启动、可分析、可删除（走原有确认流程）（待发布时手测）；
- [x] 文档与代码一致（docs/agent 状态更新）。
