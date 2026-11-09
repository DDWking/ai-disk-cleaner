# 磁盘清理 Agent 工程设计（草案）

> 状态：**v0.3 草案**。P0（工具层 + 证据层）与 P1（聊天客户端 + L1 报告 + 可交互报告页 + 删 Jev）已落地；全量 12 项离线检查绿。
> 评审方式：每篇末尾有带编号的「待确认问题」，按编号回答即可（例如「Q2 同意，Q4 改成 64KB」）。
> 起草时间：2026-11-09 ｜ 基线：`main @ 8c6233f`

## 这是什么

把「大扫货」从「带一次 AI 调用的清理工具」升级为**磁盘清理领域的分析型 agent**：
本地负责扫描与执行，模型负责研判与解释，人负责最终决定。

一句话定位：

> 一个只会「看」和「说」的清理 agent：把整块盘读成一份有证据的建议书，删不删永远由人按下去。

## 文档清单

| 文件 | 内容 | 状态 |
|---|---|---|
| [01-architecture.md](01-architecture.md) | 定位、原则、分层、组件、数据流、与现有代码的映射 | **已落地（P0/P1 基础）** |
| [02-tool-contract.md](02-tool-contract.md) | 工具层契约：命名、信封、错误、预算、工具清单（首版 9 个） | **已落地（P0，4 个只读工具）** |
| [03-evidence-model.md](03-evidence-model.md) | ScanSession / 证据卡 / 候选集 / 模型输出 / 报告卡的数据模型 | **已落地（P0/P1）** |
| [04-agent-loop.md](04-agent-loop.md) | L1/L2 流程、预算、错误恢复、取消、UI 状态机 | **已落地（L1 / P1）** |
| [05-safety-and-eval.md](05-safety-and-eval.md) | 安全边界、提示注入、黄金集回放、稳定性指标 | **待评审**（真机评测未跑） |
| [06-roadmap.md](06-roadmap.md) | 分阶段落地、文件级任务、验收标准 | **P0/P1 已完成** |

## 已落地（P0，2026-11-09）

| 位置 | 内容 |
|---|---|
| `src/AiDiskCleaner/Services/Agent/` | `ScanSession` / `FolderIndex` / `EvidenceCard` / `CandidateSelector` / `ToolEnvelope` / `ToolDto` / `ToolHost` |
| `src/AiDiskCleaner/Services/Agent/Tools/` | `get_overview` / `list_folders` / `get_folder_card` / `query_entries` 四个只读工具 |
| `tools/AgentToolCheck/` | 118 条离线检查（索引、卡片、候选、信封、工具、安全），已接入 `tools/run-all-checks.ps1` |

验证命令：`powershell -NoProfile -ExecutionPolicy Bypass -File tools\run-all-checks.ps1 -Only AgentToolCheck`，期望 `PASS 118  FAIL 0`。

## 已落地（P1，2026-11-09）

| 位置 | 内容 |
|---|---|
| `src/AiDiskCleaner/Services/Agent/Chat/` | `ChatModels` / `OpenAiCompatChatModel` / `ChatModelFactory`（OpenAI 兼容 chat completions，JSON 模式 + SSE） |
| `src/AiDiskCleaner/Services/Agent/` | `PromptBuilder`（`cleanup-report-v1`）/ `ReportParser` / `ReportAssembler` / `AgentReportService`（L1 分批编排） |
| `src/AiDiskCleaner/MainWindow.AgentReport.cs` | 「按文件夹删除」页页头的「AI 分析」入口 + 可交互报告；条目只有「去处理」：切到文件夹页并定位高亮，**不改勾选、不删除** |
| `src/AiDiskCleaner/Services/AiConfigurationService.cs` | 设置页改为聊天配置：DeepSeek / OpenAI 兼容 / Ollama 模板 + 真实连通性测试 |
| `src/AiDiskCleaner/Services/Agent/Chat/ChatModelCatalog.cs` | 「获取模型」从上游 `GET /models` 拉列表（只读、15 秒超时、失败只黄字提示，手动输入始终保留） |
| 顶栏 AI 状态灯 | 红=未接入、黄=接入但未通过测试、绿=通过并显示模型名；点灯直达设置；最近一次测试结果落盘 |
| 设置页供应商列表 | 顶部状态条显示「AI 分析使用」哪家；每行一个单选圆点，一键切换；模型跟着供应商走；状态点如实反映 未测/就绪/失败 |
| 删除 | Jev 判定通道整条移除（`AiClient`/`AiGateway*`/`AiPurposeBatchService`/`DecisionRunner`/`Jev*`/`AiPurposeCriteria` 及对应 UI 与测试） |
| `tools/AgentReportCheck/` | 53 条离线检查（提示词脱敏、解析校验、组装、编排、失败降级），已接入 runner |

验证命令：`powershell -NoProfile -ExecutionPolicy Bypass -File tools\run-all-checks.ps1 -Only AgentReportCheck`，期望 `PASS 53  FAIL 0`；全量 `tools\run-all-checks.ps1` 12 项全绿。

## 已提出的决策（状态≠已定）

| ID | 决策 | 建议 | 状态 |
|---|---|---|---|
| D1 | AI 读操作完全自主，写操作只能生成计划，执行必须人工确认 | 采纳 | 已确认 |
| D2 | 不给模型 shell / 任意命令，只给 typed tools | 采纳 | 已确认 |
| D3 | 工具参数只接受 `scanId` / `folderId`，模型不得生成路径 | 采纳 | 已确认 |
| D4 | 扫描一次建索引，所有工具都是本地索引查询（毫秒级） | 采纳 | 已确认 |
| D5 | 单次分析默认预算：≤6 轮、≤5 万输入 token、≤90 秒 | 采纳 | 已确认 |
| D6 | 交互形态：报告优先，对话为辅 | 采纳 | 已确认 |
| D7 | 外部集成（MCP）留接口，Phase 3 再做 | 采纳 | 已确认 |
| D8 | Jev 保留为可选的「快速打标」通道，不再是必需项 | ~~采纳~~ | **已否决：Jev 整条删除** |
| D9 | 是否允许模型读少量文件内容（`peek_files`） | 默认关闭，需显式开启 | 已确认：首版不做 |

### 已确认（2026-11-09 用户答复）

| # | 结论 | 影响 |
|---|---|---|
| C1 | 模型接入同时支持三类：DeepSeek 官方 / OpenAI 兼容中转 / 本地 Ollama | 统一走 OpenAI 兼容 chat completions，配置里只留 baseUrl + key + model |
| C2 | 隐私默认接受「相对路径 + 用户名脱敏 + 文件名样本出站」 | 03 号文档 §3.4 的 `relative` 模式即默认 |
| C3 | Jev 整条删除 | 删除范围约 1900 行：`AiClient` 判定通道、`AiConfigurationService` 预设、`AiGateway`/`AiGateways`、`AiPurposeBatchService`、`DecisionRunner`/`DecisionThresholds`/`Jev*`、设置页 AI 区块；`AiClient` 由新的 chat 客户端替代 |
| C4 | 运行时底座与语言选型 | **确认走 A：C# 内进程**。不引入 Node/Pi sidecar，不换语言，不做整体重构 |
| C5 | 报告形态 | **可交互**：报告只有「去处理」按钮 —— 切到「按文件夹删除」页并定位/高亮该文件夹；勾选由用户自己点，再走原有预检与删除流程 |
| C6 | `peek_files` | **首版不实现**，只留接口位；等评测证明需要再开 |

## 三个阶段的边界

- **L1 单发报告**：扫 → 本地筛候选 → 批量证据卡 → 一次模型调用 → 报告。不是 agent，先验证建议质量。
- **L2 有界循环**：允许下钻与追问，默认 ≤6 轮。本文档 02/03 的契约同时服务 L1 和 L2。
- **L3 外部工具化**：同一套工具走 MCP，供 DSH / Claude Code 这类外部 agent 调用；`execute` 永远不暴露。

## 术语

| 术语 | 含义 |
|---|---|
| `scanId` | 一次扫描的会话 ID。所有证据、卡片、建议都挂在它下面 |
| `folderId` | 扫描树里一个目录节点的不透明 ID。模型只能引用，不能构造 |
| 证据卡（EvidenceCard） | 一个目录的有界摘要，是送给模型的唯一事实来源 |
| 候选集（CandidateSet） | 本地规则从树里筛出的「值得看」的目录集合 |
| 报告卡（ReportCard） | 模型输出经本地校验后的可展示建议，带证据引用 |
| 计划（DeletionPlan） | 预检后的待删清单，不等于执行 |

## 不做什么（提前划线）

- 不做通用聊天机器人；
- 不做「AI 自动清理」；
- 不读注册表写操作、不删服务/计划任务；
- 不让模型决定扫描范围之外的事情。
