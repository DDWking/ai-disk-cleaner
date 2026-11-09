# 01 · 总体架构

> 状态：待评审 ｜ 关联决策：D1–D7

## 0. 一句话

**扫描树留在本地当数据库，模型只拿有界视图；AI 的自主性在「看」，不在「删」。**

## 1. 问题与目标

### 1.1 现状

- 扫描层是好的：[MftScanService.cs](../../src/AiDiskCleaner/Services/MftScanService.cs:18) 秒级建全盘树，[ScanCoordinator.cs](../../src/AiDiskCleaner/Services/ScanCoordinator.cs:47) 有降级和质量报告。
- 判断层是窄的：[AiClient.cs](../../src/AiDiskCleaner/Services/AiClient.cs:122) 只有 `/decisions` 与 `/v1/systemone` 两种一问一答，无解释、无循环。
- v3.0 明确删掉了整套聊天栈（[ROADMAP.md](../../ROADMAP.md:12)，提交 `59f3a05`），所以「agent 运行时、工具循环、流式输出」目前为零。

### 1.2 目标（按优先级）

1. 让 AI 能在**整块盘**上给出有证据、可解释、可追问的清理建议；
2. 建议质量可验证（黄金集回放、稳定性指标）；
3. 成本与延迟可控（每次分析有硬预算）；
4. 执行仍然不可逆风险为零（预检 + 人确认 + 回收站）。

### 1.3 非目标

- 不做通用助手（不写代码、不查天气）；
- 不做自动清理；
- 不接管卸载、注册表、服务、计划任务；
- 不在首版做多模态（截图/图像分析）。

## 2. 核心原则

| # | 原则 | 说明 |
|---|---|---|
| P1 | **索引与推理分离** | 扫描产物是本地索引；模型输入是索引的「视图」，不是原始枚举 |
| P2 | **工具窄而快** | 每个工具回答一类问题，本地毫秒级，返回有硬上限 |
| P3 | **路径只能引用** | 模型只能引用 `folderId`；任何自由路径一律判无效 |
| P4 | **执行在人手里** | 模型没有 `execute` 工具；执行入口只在 UI |
| P5 | **证据必须可回链** | 每条建议都带 `evidenceRefs`，点得开、对得上 |
| P6 | **降级要诚实** | 预算耗尽/模型失败时，退回本地报告并明确标注「未经 AI 研判」 |
| P7 | **确定性可缓存** | 同一 `scanId + folderId` 的证据卡必须稳定，才能缓存与回放 |

## 3. 分层

```text
┌──────────────────────────────────────────────────────────────┐
│ UI（WPF）                                                     │
│  · 分析页：报告卡 / 进度 / 追问                                │
│  · 清理页：原有列表 + 勾选（保留）                             │
│  · 执行按钮（唯一能触发删除的地方）                             │
└───────────────▲──────────────────────────────┬───────────────┘
                │ 报告 / 计划                    │ 用户确认
┌───────────────┴──────────────────────────────▼───────────────┐
│ Agent Runtime（新增）                                         │
│  · AgentLoop：轮次、预算、取消、错误恢复                       │
│  · Analyst：prompt 组装、输出解析、校验                         │
│  · ReportAssembler：合并/排序/阈值/去重                        │
└───────────────▲──────────────────────────────┬───────────────┘
                │ 工具调用                       │ 结果（有界 JSON）
┌───────────────┴──────────────────────────────▼───────────────┐
│ ToolHost（新增）                                              │
│  · 10 个 typed tools（见 02）                                 │
│  · 参数校验、权限级别、token 记账、截断                          │
└───────────────▲──────────────────────────────────────────────┘
                │ 只读查询
┌───────────────┴──────────────────────────────────────────────┐
│ ScanSession / EvidenceStore（新增）                           │
│  · scanId → 树快照 + 证据卡缓存 + 候选集                        │
│  · 复用：FileEntry 树、FolderSummary、CleanItem、DuplicateGroup │
└───────────────▲──────────────────────────────────────────────┘
                │
┌───────────────┴──────────────────────────────────────────────┐
│ 本地能力（已有）                                               │
│  MftScanService / ScanCoordinator / 8 条清理规则 /             │
│  DuplicateScanService / AppRecommendationService /             │
│  RecognitionStore / DeletionCoordinator                       │
└──────────────────────────────────────────────────────────────┘

模型通道（新增 IChatModel）：
  · L1：一次调用（结构化 JSON 输出）
  · L2：多轮（tool_calls）
  · 现有 AiClient 判定通道保留，作为可选快速通道（D8）
```

## 4. 组件清单

| 组件 | 职责 | 现状 | 落点建议 |
|---|---|---|---|
| `ScanSession` | 一次扫描的会话状态：scanId、扫描质量、树根、索引统计 | **已实现** | `Services/Agent/ScanSession.cs` |
| `FolderIndex` / `EvidenceCard` | folderId ↔ FileEntry 映射；证据卡生成（键 = scanId + folderId + cardVersion） | **已实现** | `Services/Agent/FolderIndex.cs` / `EvidenceCard.cs` |
| `ToolHost` | 工具注册、参数校验、权限、token 记账、截断 | **已实现** | `Services/Agent/ToolHost.cs` |
| `IAgentTool` | 单个工具契约（名称、执行、上限） | **已实现** | `Services/Agent/ToolEnvelope.cs` |
| `AgentLoop` | 轮次控制、预算、取消、错误恢复 | 新增（L2，未做） | `Services/Agent/AgentLoop.cs` |
| `IChatModel` | 模型通道抽象（流式、tool_calls） | **已实现** | `Services/Agent/Chat/ChatModels.cs` |
| `OpenAiCompatChatModel` | OpenAI 兼容实现（DeepSeek / Ollama / 中转） | **已实现** | `Services/Agent/Chat/OpenAiCompatChatModel.cs` |
| `ReportAssembler` / `ReportParser` | 校验模型输出、合并、排序、阈值、生成报告卡 | **已实现** | `Services/Agent/ReportAssembler.cs` / `ReportParser.cs` |
| `DeletionCoordinator` | 预检 + 执行 + 回收站 | **已有** | 不动 |
| `AiConfigurationService` | 聊天预设/连通性测试/诊断（Jev 已删） | **已有，已改** | `Services/AiConfigurationService.cs` |
| `RecognitionStore` | 本地目录用途落盘缓存（purpose 槽） | **已有，已精简** | 复用缓存键思路 |

## 5. 三条数据流

### L1 · 单发报告（先做）

```text
scan(drive)
  → ScanSession（树 + 质量）
  → CandidateSelector（本地规则，产出候选集）
  → CardPacker（每批 20–30 张卡，打包成 prompt）
  → 模型（一次调用，结构化 JSON）
  → ReportAssembler（校验 + 合并 + 阈值 + 证据回链）
  → 报告卡 UI
```

### L2 · 有界循环（后做）

```text
L1 的前半段不变
  → 模型返回 tool_calls（例如「看一下 D:\Games 下面」）
  → ToolHost 本地执行（毫秒级）
  → 结果回填 → 下一轮
  → 达到「结论足够」或预算上限 → ReportAssembler
```

### L3 · 外部工具化（最后做）

```text
外部 agent（DSH / Claude Code）
  → MCP server（复用 ToolHost）
  → 只读工具 + plan_delete
  → 执行仍在「大扫货」UI 里由人完成
```

## 6. 关键不变量（必须写进离线断言）

| ID | 不变量 |
|---|---|
| I1 | 模型可用工具集中**不存在**任何执行/删除/shell 工具 |
| I2 | 工具参数只接受不透明 ID，不接受任意路径（`peek_files` 也只接受 folderId） |
| I3 | 每条报告卡至少 1 个可解析的 `evidenceRef`，否则整条丢弃 |
| I4 | 报告卡引用的 folderId 必须属于本 `scanId` 的候选集，不允许「AI 新增删除目标」 |
| I5 | 执行只能由 UI 发起，且必须经过 `DeletionCoordinator` 预检 |
| I6 | 每个工具返回 ≤ 预算上限；超限必须 `truncated=true` 且提供游标 |
| I7 | 同一 `scanId + folderId` 的证据卡内容确定性（同输入同输出） |
| I8 | 预算耗尽/模型失败 → 降级为本地报告，不得输出空结果 |

## 7. 与现有代码的复用关系

| 现有资产 | 在新架构里的角色 | 注意 |
|---|---|---|
| `FileEntry` 树 | ScanSession 的数据源 | 只读遍历用 `ChildList`，别用 `Children` |
| `FolderSummary`（[FolderPurpose.cs:38](../../src/AiDiskCleaner/Services/FolderPurpose.cs:38)） | 证据卡的雏形 | 需补：路径隐私模式、规则命中、软件归属、锁定状态 |
| `FolderPurposeRules.BuildAiInput` | 已死代码，可改写为 CardPacker | ROADMAP 已标记 |
| `SourceSnippet`（白名单片段收集） | `peek_files` 的工具雏形 | `Redact` / `LooksClean` 保留，出站构造需重写 |
| `CleanItem` + 8 条规则 | 候选集来源 | 规则置信度直接进候选打分 |
| `DuplicateScanService` | `find_duplicates` 工具 | 现成的组/成员结构 |
| `AppRecommendationService` | `list_installed_apps` / 软件归属 | 只读，别动卸载逻辑 |
| `DeletionCoordinator` | `plan_delete` 的落地实现 | `Plan` 与 `Execute` 分离要保住 |
| `RecognitionStore` | 结论缓存 | 键要加 `scanId`/`promptVersion` |
| `AiConfigurationService` | 配置/探测/诊断 | 增加 chat 协议与模型列表探测 |

## 8. 一次分析的完整时序（L1）

```text
UI：用户点「AI 分析 D 盘」
  → 若已有新鲜 scanId 则复用，否则先扫描
  → 本地：top-N 目录 → 候选 ≤200 个 → 排除保护路径/系统目录
  → 分批（每批 20–30 张卡）
  → 对每批：模型调用 → 校验 → 收集
  → 本地合并：去重、按可回收空间排序、置信度阈值
  → 生成报告：总览 + 分组建议 + 每条的证据
  → UI 展示；用户可勾选 → 进入原有预检/删除流程
```

## 9. 待确认问题

- **Q1**：`scan_drive` 是否允许模型主动触发重新扫描？（我建议：只允许复用新鲜会话，重新扫描必须由 UI 发起）
- **Q2**：一次分析的候选集上限定多少？（我建议 200 个目录；超出按「空间 × 年龄 × 规则置信度」截断）
- **Q3**：分批策略按「每批固定 25 张」还是「按 token 预算动态装填」？（我建议动态装填，硬上限 8K token/批）
- **Q4**：报告的语言与长度上限？（我建议中文，每条 why ≤60 字、risk ≤40 字，总览 ≤200 字）
- **Q5**：模型失败时，UI 是「显示本地报告 + 提示未研判」还是「提示重试」？（我建议前者，后者作为按钮）
- **Q6**：L1 是否也要保留「追问」入口？（我建议 L1 不做，避免半成品对话；追问留给 L2）
