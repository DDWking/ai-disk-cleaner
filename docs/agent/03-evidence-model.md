# 03 · 证据模型（Evidence Model）

> 状态：待评审 ｜ 关联决策：D1、D3、D6 ｜ 依赖：[02-tool-contract.md](02-tool-contract.md)

## 0. 一句话

**模型不接触文件系统，只接触「证据卡」；报告不接触路径，只接触「引用」。**

## 1. 数据流全景

```text
扫描树（本地内存，全量）
   │  本地筛选（规则 + 阈值 + 黑名单）
   ▼
CandidateSet（≤200 个 folderId）
   │  取卡（确定性、可缓存）
   ▼
EvidenceCard[]（每张 ≤300 token）
   │  分批打包 → 模型
   ▼
AnalystOutput（JSON，含 folderId + evidenceRefs）
   │  本地校验/合并/阈值
   ▼
ReportCard[]（UI 展示，每条可点开证据）
   │  用户勾选
   ▼
DeletionPlan（预检后，仍不执行）
   │  用户确认
   ▼
DeletionCoordinator.Execute → 回收站
```

## 2. ScanSession

```json
{
  "scanId": "s_20261109_1a2b3c",
  "drive": "C:",
  "scannedAt": "2026-11-09T12:00:00+08:00",
  "source": "mft",
  "quality": {
    "complete": true,
    "files": 812345,
    "dirs": 91234,
    "hardLinks": 3211,
    "reparsePoints": 57,
    "unparsedRecords": 12,
    "orphanRecords": 3,
    "note": ""
  },
  "rootFolderId": "f_root",
  "index": { "folderCount": 91234, "fileCount": 812345, "cardCacheHit": true }
}
```

规则：
- `scanId` 由本地生成，形如 `s_YYYYMMDD_xxxxxx`，**全局唯一**；
- 会话默认 30 分钟内视为新鲜（`stale=false`），超过仍可用但标记 `stale=true`；
- 会话过期时间 7 天，过期后 `SCAN_NOT_FOUND`；
- 扫描质量 `complete=false` 时，报告必须带「扫描不完整，建议以管理员身份重扫」的提示。

## 3. EvidenceCard（证据卡）

这是**唯一**允许出站的目录事实。字段故意做少：够判断，不够泄露。

| 字段 | 类型 | 说明 | 出站 |
|---|---|---|---|
| `folderId` | string | 不透明 ID | ✅ |
| `relPath` | string | 相对盘根；用户名替换为 `%USER%` | ✅ |
| `depth` | int | 层级 | ✅ |
| `sizeBytes` | long | 逻辑大小（含子项） | ✅ |
| `allocatedBytes` | long | 占用（含稀疏/压缩差异） | ✅ |
| `fileCount` / `folderCount` / `directFolderCount` | int | 数量 | ✅ |
| `modified` | ISO8601 | 最后修改（子项最大值） | ✅ |
| `ageDays` | int | 距今天数 | ✅ |
| `typeMix` | array | 扩展名 × 数量 × 大小，≤8 项 | ✅ |
| `samples` | array | 文件名样本，≤6 个 | ✅ |
| `sampleDirs` | array | 子目录名样本，≤5 个 | ✅ |
| `productHint` | string? | 本地推断的软件名（如 `Chrome`） | ✅ |
| `attrs` | object | `hidden` / `system` / `reparse` | ✅ |
| `rules` | array | 本地规则命中：`id` + `confidence` | ✅ |
| `lock` | object | `locked` / `reason`（仅缓存命中时） | ✅ |
| `fullPath` | string | 完整路径 | ❌ 默认不出站 |
| `ownerSid` / ACL | — | 权限信息 | ❌ 永不出站 |
| `sampleContent` | string | 文件片段 | ⚠️ 仅 `peek_files` 且显式开启 |

### 3.1 JSON 示例

```json
{
  "folderId": "f_7c3a",
  "relPath": "Users\\%USER%\\AppData\\Local\\Temp",
  "depth": 5,
  "sizeBytes": 3221225472,
  "allocatedBytes": 3388997632,
  "fileCount": 1284,
  "folderCount": 12,
  "directFolderCount": 5,
  "modified": "2026-10-02T09:31:00+08:00",
  "ageDays": 38,
  "typeMix": [
    { "ext": "tmp", "count": 812, "bytes": 2147483648 },
    { "ext": "log", "count": 301, "bytes": 734003200 },
    { "ext": "dmp", "count": 4, "bytes": 339738624 }
  ],
  "samples": ["chrome_1234.tmp", "setup.log", "app.dmp"],
  "sampleDirs": ["CrashDumps", "Chrome"],
  "productHint": "Chrome",
  "attrs": { "hidden": false, "system": false, "reparse": false },
  "rules": [
    { "id": "temp-files", "confidence": 0.95 },
    { "id": "browser-cache", "confidence": 0.85 }
  ],
  "lock": { "locked": false },
  "truncated": false
}
```

### 3.2 Token 预算

| 部分 | 估算 token |
|---|---|
| 固定字段 | ≈90 |
| `typeMix`（8 项） | ≈60 |
| `samples` + `sampleDirs` | ≈40 |
| `rules` + `lock` | ≈20 |
| 合计 | **≈210–300** |

所以 L1 的一批 = 25 张卡 ≈ 6–7.5K token；3 批 ≈ 2 万 token；加上系统提示与输出，单次分析落在 3–5 万 token 区间，符合预算。

### 3.3 确定性（I7）

卡片生成只依赖：`scanId` 的树快照 + 规则版本 + 卡片版本号（`cardVersion`）。
同一 `scanId`、同一 `folderId`、相同 `cardVersion` → **字节级相同**。
这样卡片可缓存、可回放、可在评测里当固定输入。

### 3.4 隐私

| 模式 | 行为 | 默认 |
|---|---|---|
| `relative` | 只出 `relPath`，用户名替换 `%USER%` | ✅ |
| `full` | 出完整路径 | 需用户显式开启 |
| `off` | 不出任何路径（只出 folderId + 文件名样本） | 可选 |

注意：即使 `relative`，**文件名样本**也可能泄露内容（如 `报价单-客户A.xlsx`）。首版接受这一点，但在 UI 里明确告知「文件名样本会发送给模型」。

## 4. CandidateSet 与候选打分（纯本地）

候选集由本地规则产生，模型**看不到**未入候选的节点。

### 4.1 入口

1. 8 条清理规则的命中项（`CleanItem` 等价物）；
2. 全局 Top-N 大目录（按 `sizeBytes`）；
3. 高年龄目录（`ageDays ≥ 180` 且 `sizeBytes ≥ 500MB`）；
4. 重复文件组所在目录；
5. 已安装软件的缓存/日志目录（`AppRecommendationService`）；
6. 上一轮 `RecognitionStore` 里用户确认/否决过的目录（作为上下文，不自动入选）。

### 4.2 打分（确定性）

```text
score = w1·log10(sizeBytes)
      + w2·min(ageDays, 730)/365
      + w3·max(ruleConfidence)
      + w4·appResidualHint
      - p1·protectedPenalty
      - p2·systemPenalty
```

默认：`w1=1.0, w2=0.8, w3=1.2, w4=0.6, p1=∞（保护路径直接排除）, p2=3.0`。

### 4.3 硬排除（永不入候选）

- `C:\Windows`、`C:\Program Files` 根、`C:\ProgramData\Microsoft`、`System Volume Information`、`$Recycle.Bin`；
- 回收站、系统还原点、引导分区文件；
- 当前进程/服务正在使用的路径（由 `check_locks` 结果或句柄探测标记）；
- 用户显式加入「永不建议」名单的路径（`RecognitionStore` 持久化）。

### 4.4 候选上限

- 默认 200 个；按 `score desc` 截断；
- 同一父目录下最多 8 个候选（避免整棵子树刷屏）；
- 截断时记录 `candidatesTruncated=true`，报告里注明。

## 5. EvidenceRef（证据引用）

`evidenceRefs` 是「模型说的话」和「本地事实」之间的桥。

### 5.1 格式

```text
card:f_7c3a          证据卡（必须有）
rule:temp-files      规则命中（必须在卡片的 rules 里）
app:chrome           软件归属（必须在卡片的 apps/productHint 里）
dup:g_12             重复组（必须由 find_duplicates 返回过）
age:384d             年龄事实（必须与卡片一致，±1 天）
size:3.0GB           大小事实（必须与卡片一致，±1%）
lock:none            占用状态（必须与卡片一致）
```

### 5.2 校验规则（关键）

1. `card:` 引用必须能解析到本批输入里的卡片；
2. 其他引用必须是**该卡片已携带**的事实，模型不得自造；
3. 每条报告卡 ≥1 个 `card:` 引用，否则整条丢弃；
4. 引用解析失败 → 丢弃该条（不静默修正）。

> 这条规则是「AI 不能新增删除目标」的技术落点：模型只能引用它见过的 folderId，且必须能对回事实。

## 6. AnalystOutput（模型输出）

### 6.1 Schema

```json
{
  "reportVersion": 1,
  "scanId": "s_20261109_1a2b3c",
  "summary": "C 盘可回收约 21GB，主要集中在浏览器缓存、旧安装包和日志。",
  "items": [
    {
      "folderId": "f_7c3a",
      "verdict": "recommend",
      "confidence": 0.86,
      "why": "Chrome 缓存与崩溃转储，最近 38 天未清理，规则置信度高。",
      "risk": "清理后首次打开网页会稍慢。",
      "reclaimableBytes": 3221225472,
      "evidenceRefs": ["card:f_7c3a", "rule:browser-cache", "age:38d"]
    }
  ],
  "questions": [
    { "text": "需要我列出 D 盘的游戏安装残留吗？", "suggestedTool": "list_folders" }
  ],
  "degraded": null
}
```

### 6.2 约束

| 字段 | 约束 |
|---|---|
| `reportVersion` | 必须为 1；不认识就整份拒绝 |
| `scanId` | 必须与输入一致，否则整份拒绝 |
| `summary` | ≤200 字，不得含完整路径 |
| `items` | ≤40 条；`folderId` 必须在候选集内 |
| `verdict` | 枚举：`recommend` / `consider` / `keep` / `unknown` |
| `confidence` | [0,1]，超出则 clamp |
| `why` | ≤60 字，必须与证据一致 |
| `risk` | ≤40 字，可为空 |
| `reclaimableBytes` | 必须 ≤ 卡片 `sizeBytes`（超过则 clamp 并在日志里记异常） |
| `evidenceRefs` | 1–6 个；按 §5.2 校验 |
| `questions` | ≤3 条，L1 只展示不执行；L2 才允许带 `suggestedTool` |

### 6.3 校验流水线（本地，顺序固定）

```text
1. JSON 解析（失败 → 重试一次，仍失败 → 降级本地报告）
2. reportVersion / scanId 校验（不符 → 整份拒绝）
3. 逐条：
   a. folderId 是否在候选集 → 否则丢弃
   b. verdict 枚举 → 否则丢弃
   c. evidenceRefs 解析 → 任一失败即丢弃该条
   d. confidence clamp；reclaimableBytes clamp
   e. 长度截断（why/risk）
4. 去重：同 folderId 保留 confidence 最高的一条
5. 合并：同父目录的 consider 合并为一条「父级建议」
6. 阈值：confidence < 0.55 的 recommend 降级为 consider
7. 排序：reclaimableBytes desc → confidence desc → relPath asc
8. 总量：reclaimableBytes 之和超过实际可回收空间时，按比例下调并标注
9. 生成 ReportCard[]
```

## 7. ReportCard（UI 展示）

```json
{
  "reportId": "r_...",
  "scanId": "s_...",
  "generatedAt": "...",
  "summary": "…",
  "totalReclaimableBytes": 21000000000,
  "groups": [
    {
      "group": "浏览器缓存",
      "totalBytes": 8200000000,
      "items": [
        {
          "folderId": "f_7c3a",
          "title": "Chrome 缓存",
          "relPath": "Users\\%USER%\\AppData\\Local\\Temp",
          "sizeBytes": 3221225472,
          "verdict": "recommend",
          "confidence": 0.86,
          "why": "…",
          "risk": "…",
          "evidence": [
            { "ref": "card:f_7c3a", "label": "证据卡", "target": "folder" },
            { "ref": "rule:browser-cache", "label": "规则：浏览器缓存", "target": "rule" }
          ],
          "action": "select"
        }
      ]
    }
  ],
  "degraded": null,
  "provenance": {
    "model": "deepseek-chat",
    "promptVersion": "cleanup-report-v1",
    "rounds": 1,
    "inputTokens": 18200,
    "outputTokens": 2100
  }
}
```

`action` 只是给 UI 的展示建议（`select` / `keep` / `ask`），**不直接写勾选**；报告里的动作是「去处理」= 切到「按文件夹删除」页并定位/高亮该文件夹，勾选始终由用户点。
点击证据 → 打开对应目录卡片或规则说明，**不直接触发删除**。

## 8. 与现有类型的映射

| 现有类型 | 新模型里的角色 | 注意 |
|---|---|---|
| `FileEntry` | 树节点（本地） | 不直接出站 |
| `FolderSummary`（[FolderPurpose.cs:38](../../src/AiDiskCleaner/Services/FolderPurpose.cs:38)） | EvidenceCard 的前身 | 需补 `rules` / `apps` / `lock` / 隐私处理 |
| `CleanItem` | 候选来源 | 规则置信度要保留 |
| `ScanSnapshot` | 上一版快照 | 可用于「增长最快目录」类证据 |
| `RecognitionStore` | 用户偏好（确认/否决） | 键需含 `scanId` 与规则版本 |
| `DeletionPlan` / `DeletionTarget` | `plan_delete` 的输出类型 | 保留 `ExpectedSize` / `ExpectedModified` / `SnapshotIsExact` |

## 9. 缓存与持久化

```text
%LocalAppData%\DashaoHuo\agent\
  sessions\s_20261109_1a2b3c\
    session.json       # ScanSession + 质量
    cards.jsonl        # 证据卡（按需生成、追加）
    candidates.json    # 候选集与打分
    report.json        # 最近一次 AnalystOutput（原始文本 + 校验后）
```

- 保留 7 天，启动时清理过期会话；
- 缓存键：`scanId + folderId + cardVersion`；
- `report.json` 保留原始输出，便于回放与问题定位（不含文件内容）。

## 10. 待确认问题

- **Q1**：证据卡目标体积 250 token（上限 300），可以吗？（更小会丢判断依据，更大会挤压批量）
- **Q2**：`verdict` 用 4 个枚举够吗？还是加 `strong-recommend`？（我建议 4 个，UI 上用置信度区分强弱）
- **Q3**：置信度阈值 0.55，可调吗？（我建议首版固定，评测后再调）
- **Q4**：报告只按「目录」出，还是允许少量「文件级」建议（如单个 20GB 的 vhdx）？（我建议目录为主，文件级只作为目录建议里的样本展示）
- **Q5**：`evidenceRefs` 严格子集校验是否会导致大量丢弃？（我建议保留严格模式，用日志统计丢弃率，若 >15% 再放宽到「允许 size/age 的事实性引用但必须与卡片一致」）
- **Q6**：会话目录放 `%LocalAppData%` 还是放扫描盘旁的 `DashaoHuoData`？（我建议 LocalAppData，避免污染被分析的盘）
