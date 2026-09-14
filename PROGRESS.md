# 开发进度

两人看这一份就够。改代码前先看「进行中」，避免撞车；做完一件事在日志最上面加一行，并更新状态表。

## 现在做到哪

| 模块 | 状态 | 说明 |
|---|---|---|
| MFT 秒扫 | 进行中 | 完整 MFT 已读到。正在修文件挂不到目录：硬链接 / 扩展记录上的 `$FILE_NAME` |
| 清理结果分层归类 | 可用 | 主区域**就地展开**：分类 → 位置 → 文件都在同一屏；折叠分区不渲染行；底部固定操作栏是唯一执行入口；「全选」是单个切换（文字恒为「全选」） |
| 主导航 | 可用 | **三页：清理中心 / 文件夹整理 / 卸载**，默认进「清理中心」（扩展名统计已整页移除）；页面切换用枚举而非数字索引 |
| 文件夹整理（辅助入口） | 可用 | **首屏只显示一级（全部收起）**；顶层（含系统入口）**按容量降序**，点表头不能打散树序；后台材料化一、二级并**只做本地识别**；行内只有**单项 AI**；底部「全选」只作用于当前列出的行；勾选后可把文件夹送进回收站；点用途展开行内详情；系统目录有**本地结论且不进 AI**；**有子目录才有展开箭头；只有文件的目录给文件入口而不伪造箭头；链接未扫描与空目录分开表达，不用 0 KB 暗示** |
| 清理前检查 | 可用 | 「清理已选项目」进入独立检查页：位置数/候选项数/预计空间/需确认数 + 会再检查什么；[返回修改][确认清理]；只有确认后才进既有预检与执行链路 |
| AI 分析 | 可用 | **按需挂在具体项目旁**（位置行 / 文件行右侧的「AI 分析」）；范围严格隔离，不自动扩大到整个用途；建议四档含「信息不足」；结果在行内展开（建议/用途/删除影响/判断依据/缺少的信息）；有缓存、有限并发、超时与就地取消；扫描完成不自动发请求 |
| AI 按组挑 | 可用 | 位置分析**先给本地分组**（真实子目录 / 关联应用 / 命中规则 / 类型兜底），每组显示项数、符合清理资格数、候选空间与来源；只有整组都合格且无需确认的组才有「选择本组候选（N 项 · X）」；混合风险组必须先查看；模型只能按**本地序号**给每组一句话；未配置 AI 时本地分组照样可用 |
| 清理规则匹配 | 可用 | 路径签名按**整段目录**匹配（不再裸子串）；npm 全局安装目录与 npm 缓存分成两条（全局=保留）；pip 只认 cache；`\downloads\` 只产生需确认 |
| 清理资格 | 可用 | **唯一判据** `ProtectedPaths.IsCleanupBlocked`（老口径 + 删除时硬拦截）⇒ 显示、候选统计、全选、预检同一口径；Windows Installer / WinSxS / SVP / WindowsApps 等不进候选、不计入空间；仍然每次执行前再验一遍 |
| 位置命名 | 可用 | 标题 = 签名用途名 → **真实文件夹名**；同名靠精简父路径区分；完整路径在悬停与「查看路径」里；保留真实大小写；不再出现「Windows 系统」「用户目录」 |
| 顶部导航 | 可用 | 返回只有一个箭头（Tooltip/可访问名称按层级）；标题只出现一次；第二行统一「N 个位置 · 候选空间 X」；成功扫描不显示状态行，异常才留短警告 |
| 候选行 | 可用 | 紧凑两行：名称+空间+操作 / 父路径+文件数；AI 固定在右侧、结果行内展开；未选择时不显示「已选 0 / N 项」 |
| 资源管理器入口 | 可用 | 位置行与明细面板都是文件夹图标 + 「在资源管理器中打开」；目录打开、文件选中不执行；聚合位置无唯一目录时让用户选；统一走 `ShellReveal`（结构化参数，无 cmd/PowerShell） |
| 弹层 | 可用 | 遮罩与内容**分离**：只有遮罩做淡入淡出，卡片始终不透明（修掉内容透出）；`_overlayEpoch` 防旧回调收掉新弹层；关闭后状态落回确定值、焦点移入对话框 |
| 默认勾选 | 可用 | **新扫描默认一项都不勾**（含原 Safe 项与重复项）；只有用户主动勾选或整组勾选（仅作用于 `CanDelete`）才会选中 |
| 清理候选 | 可用 | 原「建议清理」改为「清理候选」，副标题写明「默认没有勾选，需要你自己选」 |
| 图标体系 | 可用 | 统一 24×24 线性矢量 Geometry + `IconButton`(34)/`IconButtonGear`(36)/`ChipButton`；悬停/按下/焦点/禁用四态；图标按钮均有 ToolTip 与可访问名称，且**禁止被运行时写 Content 覆盖** |
| 查看已选 | 可用 | 胶囊按钮（清单图标 + 已选数量）；结构化面板按位置分组列出用途/位置/候选项数/预计空间；DataGrid 行与列虚拟化；长路径截断+悬停+复制；开关都不改动选择 |
| 文件浏览器侧栏 | 已移除 | 2.11 起不再有目录树 / 搜索 / 侧栏开关；清理范围芯片仍可清除（没有树入口去设置） |
| 风险归属 | 可用 | 签名不得降级「大/旧/长路径」的风险，也不得把用户数据认成缓存；同一用途按真实候选风险拆成两行，各自子集；两个分区各有色带/图标/副标题，不单靠颜色 |
| **非管理员模式清理不可用** | **待决定** | 递归降级扫描不设 `FileEntry.Parent`，导致所有候选被判受保护（`selectable=0`）。修法会**扩大非管理员模式的可删范围**，需你同意后再单独修（详见日志第十二阶段第 12 条） |
| 目录树 + 当前目录列表 | 已移除 | 随 2.11 侧栏一起拿掉；整理页按容量看顶层对象，清理页就地展开位置 |
| 单位显示 | 可用 | 只显示 KB / MB / G |
| 崩溃修复 | 可用 | 递归改迭代、孤儿文件不再堆到根；崩溃处理器有**重入闸 + 启动期致命闩锁**，半初始化窗口不碰控件，启动期致命记录原始原因后**受控非零退出**，第一现场单独存 `first-crash.log`（不被日志风暴轮转掉）；`tools/StartupCheck` 真的加载编译后 BAML + 真实控件树断言启动 |
| 删除前预检（安全） | 可用 | `DeletionPlan` / `DeletionPreflight` / `DeletionExecutor`：存在性、路径身份（file id）、大小/时间、占用、保护路径、父子去重；逐项结局一项不丢 |
| 统一取消 | 可用 | 扫描 / 分析 / 软件清点 / 残留扫描 / AI / 卸载后重扫各有 CTS，顶栏「停止」一键全停；代次守卫保证旧结果不覆盖新结果 |
| 扫描质量报告 | 可用 | 主页面只留一句「扫描完成 · 部分内容未检测」；来源（MFT/递归）· 文件/目录数 · 耗时 · 跳过目录/权限/路径错误/重解析点/解析失败/孤儿记录 · 候选总数/去重/重复命中 全在「扫描详情」里 |
| 统一日志 | 可用 | `%LOCALAPPDATA%/DashaoHuo/app.log`：时间/级别/模块/operation ID/阶段/耗时/数量/异常类型/用户可见错误；滚动 2MB×3；全量脱敏；含诊断导出 |
| AI 说明 | 可用 | 表格一列「说明」：规则原因，分析后被 AI 覆盖（浅青字）。不判风险、不碰勾选。模型在设置里选 |
| 卸载页 | 可用 | 只讲事实：名称 / 安装日期 / 用途 / 占用 / 操作（发布者/版本/状态列保留但隐藏）；占用格只显示数字，来源在悬停；用途 = 类型 + 发布者（无版本）；占用按有效大小降序；勾选后走官方卸载；卸完可扫残留 |
| 软件卸载建议 | 可用 | 规则硬拦截（系统/驱动/运行库/安全/虚拟化/正在运行/状态未知/Windows 自带组件），AI 只建议和排序、不越权，用户逐项确认；**没有依据的软件保持中性（未评估），不再用套话制造建议** |
| 磁盘占用可信度 | 可用 | 显式区分**扫描实测 / 安装记录估计 / 未知**；共享安装目录、Windows 系统目录、跨盘安装目录都不把整棵树算给某一个软件；未测得显示「未知」而不是 0；小值不四舍五入成 0 G；默认按**可信占用**排序 |
| 批量决策（规则明确） | 可用 | 预览弹层与引擎仍在，操作栏不再放单独按钮；只勾 `CanDelete + Risk==Safe + Evidence>=签名级 + 缓存/临时/转储`，先给预览、可逐类排除、写入前复检；大文件/旧文件/下载/压缩包/视频/个人资料/未知对象**始终人工选择**；AI 改不了候选资格 |
| 行内看候选文件 | 可用 | 点清理位置行**就地展开**候选文件（名称/修改时间/大小/勾选），不需要 AI；惰性建表、最多 30 条、**不做嵌套滚动**；超过 30 条显示「另有 N 项」，文件夹图标打开资源管理器 |
| 真实删除/回收站 | 可用 | 单项右键 + 清理页勾选批量，均进回收站，系统项保护；删除结果逐项汇报（成功/不存在/已变化/权限不足/受保护/占用/跳过/失败） |
| 扫描来源与降级提示 | 可用 | 显示 MFT 高速扫描、失败原因、兼容递归扫描及最终来源 |
| 清理候选完整性 | 可用 | 不再硬截断 400 项，摘要区分完整分类数与当前显示数 |
| 诊断日志 | 可用 | 统一写入 %LOCALAPPDATA%/DashaoHuo，不依赖源码目录；设置页可导出脱敏诊断包 |
| 清理规则架构 | 可用 | `ICleanRule` 九条规则独立成模块、可单独测、异常互相隔离；结果按 (列表,路径) 去重；记录每条规则耗时 |
| 重复文件检测 | 可用 | 分阶段哈希：大小 → 首段 → 尾段 → 完整；正确处理硬链接 / 占用 / 云占位 / 网络盘 |
| 扫描内存诊断 | 可用 | `MftScanService.LastMemoryReport`：对象估算、数组占用、nameLinks、平均路径长度，先量再改 |
| 协调模块 | 可用 | `ScanCoordinator` / `DeletionCoordinator` / `AiCoordinator` 从 MainWindow 抽出，依赖注入、可离线测 |
| 软件占用拆分 | 可用 | 程序本体 / 保存的数据 / 缓存 三档，缓存即「不卸载就能清掉」的估计可释放空间；计算结果有缓存 |
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
- 扫描要管理员权限；UAC 拒绝会回退到很慢的递归扫描，界面会显示具体原因和最终扫描来源
- 本机 git 用户曾是占位符，仓库级已改成 `DDWking`

## 日志

新的写在最上面。格式：

```
### YYYY-MM-DD  名字
- 做了什么
- 还差什么 / 下次谁接
```

### 2026-09-14  DDWking（2.12.0 文案去重：一处事实只写一次）

> 叠在已发布的 2.12.0 之上，**不改版本号**。按截图收口重复文案，未提交过的改动现在合进 `main`。

- 卸载占用格只留数字（`34.0 G` / `0 KB` / `未知`），「扫描实测 / 安装记录」进悬停。
- 卸载用途 = 类型 + 发布者（Adobe / Steam），不写版本、不写「已安装软件 · 发布者 …」。
- 清理展开去掉「查看文件 / 完整列表 / 这里只列这一处… / 用途待确认 / 为什么这样建议 / 重新分析」；超过 30 条显示「另有 N 项」。
- 全是待确认时不显示 AI 结果卡；能勾选时只留一行结论 +「选择这些文件」。
- 整理页点过单项 AI 后用途列走 `HasItemAiPurpose`，不再叠「未识别 / 需要确认」。
- 按文件夹删除：无选择时状态栏空着，不占一行教学文案。

### 2026-09-14  体验版 v2.12.0（界面收口：全选 / 整理按容量 / AI 胶囊 / 卸载列精简）

> 叠在 2.11（去目录侧栏、就地展开、按文件夹删除、只讲事实的卸载页、识别结果落盘）之上。
> 版本从 2.10.0 直接到 2.12.0：2.11 界面已落地但未单独打过这个号。

- 清理页操作栏：去掉「选择规则明确的清理项」按钮和「清空选择」；留下单个「全选」切换。文字恒为 `Loc.SelectAll`，已全选时换主按钮外观，再点一次取消。范围是全局 `CanDelete` 项。规则批选引擎 / 预览弹层仍在。
- 整理页：去掉「未识别」筛选胶囊和重复的页面标题；「全选当前列表 + 清空选择」合成一个「全选」（只作用于当前列出的行）。顶层对象（含系统入口）混在一起按容量降序，`OrganizeGrid.CanUserSortColumns=False`，点表头不能打散树。容量列表头与数字右对齐。勾选后仍走回收站删除。
- 顶栏：容量文字和设置齿轮之间加 AI 胶囊。已配置显示模型名 + 7px 绿点 `#3ECF6A`；分析中换成旋转指示；未配置整块 `Collapsed`。点击打开既有模型设置。
- 卸载页：可见列改为 名称 / 安装日期 / 用途 / 占用 / 操作；发布者、版本、状态列 `Visibility=Collapsed` 留在树上。
- 识别落盘接线：`InitRecognitionPersistence` 在第一次 `RebuildOrganize` 之前接上；`TryRestorePurpose` 改用 `FolderId(path, _aiDataGeneration)`（2.11 已无侧栏 `CurrentFolderId` / `DepthOf`）。
- 门禁：StartupCheck 167、SafetyCheck 1067、FolderDeleteCheck 91、UiRegressionCheck 全绿、CleanupPresentationCheck 36、RecognitionPersistenceCheck 66，以及 AiNoteParser / AppRecommendation / AppPurpose / LocalRecognition / ItemAiIsolation / CleanAnalyzer。
- 版本 **2.12.0**（`Version` / `FileVersion` / `InformationalVersion` 同步）。

### 2026-09-13  体验版 v2.10.0（第二十九阶段：按三张实拍截图修四项）

> 需求（已冻结，来自三张真机截图，不推翻既有方向）：
> ① 卸载建议与占用可信度；② 清理页直接看候选文件；③ 整理页展开状态；
> ④ 清理首页支持「规则明确」的批量决策。默认仍是清理中心；仍然本地优先、
> 扫描后不自动批量 AI、AI 不能改候选资格。

**1) 先核实，再动手（都是实测，不是推测）**

- 用一次性控制台程序跑真实的 BCU 清点（206 条），拿到截图里那条
  `Microsoft® Windows® Operating System` 的真身：
  `RegistryKey=mstsc-…`、`UninstallString="C:\Windows\System32\mstsc.exe" /uninstall`、
  `InstallLocation=C:\Windows\System32`，而 BCU 的 `SystemComponent`/`IsProtected` **都是 false**。
  ⇒ 两个独立缺陷同时成立：**它进了「可以考虑」**，而且**System32 整棵树（13.1 G）被算成了它的占用**。
- 同一份数据还查出：**21 条**条目的卸载程序落在 `C:\Windows` 内（多为厂商驱动组件）；
  **16 处**安装目录互为父子（`.NET`/Office/Python 这类共享运行库根）。
- 清理页/整理页的问题直接看代码印证：行内没有文件列表，`查看文件` 只挂在 AI 结果模板里；
  整理页 `CanExpand = CanDescend && ChildDirCount > 0`，而 `SetChildDirCount` 只在建节点时取一次，
  重解析点（WeGameApps 之类）拿到 `Size=0` 又 `CanDescend=false` ⇒ **既没有箭头、又显示 0 KB**。

**2) A 卸载建议与磁盘占用可信度（`AppRecommendationService` / `AppUninstallItem` / `BcuUninstallService`）**

- 新增第四档 `AppRecommendationDecision.Neutral`（**未评估**）。`LocalRule` 改成：
  受保护 / 系统 / 无卸载程序 → 保留；**Windows 自带组件（结构判定）** → 保留；
  运行库 / 驱动 / 安全 / 虚拟化签名 → 保留；**正在运行** → 需要看一下；
  已知捆绑特征 → 建议卸载（运行状态未知时降一档，仍写**真理由**）；**其余一律中性、理由留空**。
- **删掉的套话**：`AppConsiderRunningUnknown` / `AppRunningUnknownWarning` /
  `AppConsiderLarge` / `AppConsiderStartup` / `AppConsiderUnknown` 五个 Loc 串连同分支一起移除。
  以前「无法确认是否正在运行」把 207 行都塞进「可以考虑」，这就是截图里那一列重复文案的来源。
- **系统条目靠结构判定，不靠名字子串**：`IsInboxComponent` 解析卸载命令行的可执行文件路径
  与安装位置，落在 `%SystemRoot%` 内即判定为 Windows 自带组件 / 设备软件（`Environment.SpecialFolder`
  解析，不写死盘符）。分组标题也从「可以考虑」改成「需要看一下」，并新增「未评估（N）」一档。
- **占用归属三道闸**（`CanAttributeLocation` + 现有重叠判定 + 跨盘判定）：
  ① 落在 Windows 目录内的安装路径**不归属**；② 正好是通用父目录（Program Files / ProgramData /
  用户目录 / Temp / 盘根）不归属；③ 与别的软件互为父子（共享父目录）不归属；④ 安装目录不在本次
  扫描的盘里 → 记为未测得。每条都带**具体原因**进悬停。
- **占用显示三态**（`AppFootprintSource`）：`实测` 直接写数；`安装记录` 写「约 X（安装记录）」；
  两样都没有写「未知」。新增 `AppUninstallItem.FormatFootprint`：**绝不把小值四舍五入成 0**
  （1 字节 → `< 1 KB`，400 MB → `400 MB`，不会再出现 `0.0 G`）。列头从「扫描占用」改成「占用」，
  悬停里写明「磁盘占用不等于卸载能释放的量」；默认排序改成**可信占用优先**（实测 → 记录 → 未知）。
- 卸载确认框不再承诺「预计释放 X」，改成「合计占用 X —— 不是承诺」。

**3) D 清理首页的批量决策（`CleanRuleEligibility` + `CleanRuleSelectPreview` + `MainWindow.CleanBatch.cs`）**

- `CleanItem` 新增 `Evidence`（规则的证据等级；以前在工厂里被丢掉），由 `CleanItemFactory` 原样带上。
- **唯一判据** `CleanRuleEligibility.IsRuleClear`：`CanDelete && Risk==Safe && Evidence>=Signature
  && 用途属于缓存/临时/转储`。**只读规则自己产出的字段** —— 不重新判断文件、不看路径里有没有
  "cache"、**完全不看 AI**。大文件 / 旧文件 / 下载安装包 / 重复 / 空目录 / 长路径 / 未知用途**永不入选**。
- `IsStillDeletable` 在**写入前再核一遍**：重新走 `CanOffer` + `ProtectedPaths.IsCleanupBlocked`；
  没有可核对的 `Entry` 一律拦下（宁可不勾也不猜）。
- 底部新增「**选择规则明确的清理项**」按钮：点了**只出预览**（按类别列出项数 / 约占用 / 清掉会怎样 /
  例子，可逐类勾「排除」），取消什么都不发生；确认后才写选择，并在页脚写明「已按规则勾选 N 项，
  删除前仍会走清理前检查」。执行链路**完全没动**：仍然是清理前检查 → 确认 → 预检 → 执行。
- 复用的仍是既有 Overlay 弹层（新增一个 body），没有新窗口、没有新扫描、没有第三套规则引擎。

**4) B 清理位置行内直接看候选文件（`CleanLocationNode` + `MainWindow.xaml`）**

- 点位置行的名称区域或右侧箭头 = **就地展开**这一处的候选文件：文件名 / 修改时间 / 大小 / 勾选，
  **不需要 AI**。
- **惰性 + 有界**：只有真的展开过才建列表（绑定不会为「可能被展开」预建），一次最多 30 条按占用降序，
  超出如实计数并由「完整列表（可搜索）」按钮承接。第一次写测试就抓到 `VisibleFiles` 取值器顺手
  建表的问题（等于每行都排一遍），已改成展开才建。
- **不做嵌套滚动**：行内块最多 30 行、没有自己的 ScrollViewer/ListBox —— 外层列表是虚拟化的，
  内层滚动区会抢滚轮（这个坑以前踩过）。有 StartupCheck 断言盯着这一点。
- 复选框 / AI 按钮 / 文件夹图标各自独立处理点击，不会误触展开；收起时记下并恢复滚动位置，
  选择挂在 `CleanItem` 实例上，收起再展开一项都不丢。
- **入口收敛**：右侧明细面板不再由行点击直接打开，改成行内块里唯一的「完整列表（可搜索）」；
  「在资源管理器中打开」保持独立（应用内查看 vs 资源管理器定位，界面上已经分开）。

**5) C 整理页展开状态（`OrganizeNode` / `MainWindow.Organize.cs`）**

- 新增 `FolderContentKind`：`ChildDirs`（有子目录 ⇒ 才有箭头）/ `FilesOnly` / `Empty` / `NotScanned`。
- **只有文件的目录不伪造箭头**，改成一个文件按钮，就地列出它自己的文件（只读、最多 12 个、不嵌套滚动，
  并说明「打开文件夹看全部」）。整理页仍然**没有任何删除能力**，`OrganizeNode` 上没有
  `Selected`/`CanDelete`/`Risk`/`Verdict`（有断言盯着）。
- **未知 ≠ 空**：重解析点 / 链接本次没有进去 ⇒ 容量写「未知（未扫描）」、状态「未扫描（链接）」；
  扫描结果里没有内容 ⇒ 「扫描无内容」+「空」。**两者文案明确区分，都不再用 0 KB 暗示**。
- 保留既有修复：懒加载首击即展开（材料化与展开同一次完成）、筛选不改展开状态、无孤儿行。

**6) 测试（行为用例优先，全部真跑）**

- `SafetyCheck` **1066 PASS / 0 FAIL**（本轮 +82）：新增 `ReviewFixesTests.cs` 三节 ——
  规则明确批选的资格边界（启发式/需确认/别删/受保护/大文件/旧文件/下载/重复/空目录/长路径/未知
  逐条不许入选）、**AI 改不了候选资格**、预览排除与取消无副作用、写入前复检、
  行内列表惰性/有界/同实例/收起不丢选择、整理四态与「未知 ≠ 0 KB」、整理对象无清理字段。
- `StartupCheck` **148 PASS / 0 FAIL**（本轮 +45）：新增第 10 节，**驱动真 MainWindow 的私有路径** ——
  真按钮 → 真预览弹层 → **点按钮一个都不勾** → 取消无副作用 → 确认后只勾规则明确的 2 项
  （大文件与启发式项没被带走）；点位置行确实就地展开且**没进明细面板**、行内勾选写回同一份状态；
  行内块**没有嵌套滚动容器**；只有文件的目录能真的展开文件且**不动展开状态**；
  链接写「未知（未扫描）」而不是 0 KB；中性档不被自动勾选、按建议勾选只覆盖「建议卸载」。
  另加第 10 节末尾的**真 MainWindow 正常关闭路径**（见第 9 条限制说明）。
- `AppRecommendationCheck`（含新增 40+ 条）、`LocalRecognitionCheck` 52、`ItemAiIsolationCheck` 39、
  `AiNoteParserCheck`、`CleanAnalyzerCheck`、`CleanupPresentationCheck` 36、`UiRegressionCheck` **全过**；
  Release 构建 **0 错 0 警**。
- 顺手修的三个**既有脚本缺陷**（先审后用）：
  ① `StartupCheck` / `CleanupPresentationCheck` / `release.ps1` 里 `$ErrorActionPreference='Stop'` 下
  `2>$null` 重定向原生命令 stderr 会抛 `NativeCommandError`（改成临时放宽 + `2>&1` + 按退出码与真实输出判定）；
  ② `UiRegressionCheck/Run.ps1` 的 UTF-8 BOM 在上一轮被改掉，PS 5.1 按 GBK 读中文注释会把后面的代码吃掉
  （已补回 BOM）；
  ③ `UiRegressionCheck/Run.ps1` 取「最新 dist」用的是**名字排序**，字符串下 `2.9.2 > 2.10.0`，
  2.10.0 一发布门禁反而去查旧包 —— 改成按**版本号**排序（这个缺陷本轮才暴露）。
- `AiNoteParserCheck` 补 `Models/CleanEvidence.cs`；`EvidenceLevel` 从规则契约**移到 Models**
  （它是候选自己的属性），避免为拿一个枚举把整份规则契约拖进解析器检查。

**7) 打包（新目录，不覆盖任何旧包）**

- 版本 **2.10.0**（`Version` / `FileVersion` / `InformationalVersion` 同步）。
- `dist/DashaoHuo-2.10.0-20260913-win-x64/`（546 文件，含 `sidecar/AiSidecar.exe`、
  `SteamHelper.exe`、`StoreAppHelper.exe`、`THIRD-PARTY/`），`AiDiskCleaner.exe` **2.10.0.0**；
  另有 `.zip` 与 `.zip.sha256`。**2.9.x 及以前全部目录与包原样保留。**
- 包内 `AiDiskCleaner.dll` 与本次**验收构建**哈希一致：
  `452C45E83EB5DF04D4BBC1BC77E54BA993DD7D86613A9EE4A3BAC1C6A992181D`
- zip SHA256：`31DB32CEFBFC0651771B181855E86C967D37B92C98A71029DA9CA073C20570DB`

**8) 真实 EXE 冒烟（从本次 dist 起，不是 bin 旧包）**

- `dist/DashaoHuo-2.10.0-20260913-win-x64\AiDiskCleaner.exe`：进程存活、
  **主窗口句柄非 0**（hwnd 5509542、标题「大扫货」）、`Responding=True`、
  **没有 `first-crash.log`**；880×600 ↔ 1400×880 来回改尺寸后仍然响应。
- 日志里能看到它自己真的跑完了启动扫描：
  `op=sca-…-002 stage=done ms=16805 n=1583694 | mft`、
  `layers(first-paint) files=148312 selectable=148246 purposes=15 locations=326`，
  以及 `skipping app inventory: uninstall tab has not been opened yet`（没开卸载页就不清点软件）。
- **没有触发任何真实清理，也没有发任何模型请求。**

**9) 未验收 / 已知限制（如实说明，不粉饰）**

- ❌ **EXE 的优雅关闭没能验证**：`AiDiskCleaner.exe` 带 `requireAdministrator`，本会话 shell 是
  **Medium 完整性、Administrators 只用于 deny 的过滤令牌**（`ConsentPromptBehaviorAdmin=5`），
  于是给那个提权窗口发消息一律被拒：`PostMessage(WM_CLOSE)` 返回 false、
  `SendMessageTimeout` 返回 `ERROR_ACCESS_DENIED(5)`。**连结束这个进程都不行**
  （`Stop-Process` / `taskkill /F` 都是 Access denied）。所以：
  - 已用**进程内**的真 `MainWindow.Close()` 路径替代（StartupCheck 第 10 节 3 条断言：
    关闭不抛异常、关闭后不可见、没有触发崩溃处理器），**这是替代证据，不是 EXE 端到端关闭实测**。
  - 本会话启动的那个实例（PID 166108）**仍在运行**（空闲、CPU 平稳），需要你手动关掉它。
- ❌ **没有真实 UI 实拍**：无法对提权窗口截图；四项改动的排版（行内文件块、预览弹层、
  整理页状态字）只有真实控件树断言与 BAML 加载验证，不是肉眼验收。
- ❌ **没有真实模型调用**：AI 相关断言全部是离线行为；本轮改动本身也没有新增任何 AI 入口。
- 整理页「只有文件」的行内列表最多 12 个文件（不做嵌套滚动），更多的靠「在资源管理器里打开」；
  行内候选文件最多 30 条，更多的靠「完整列表（可搜索）」。
- 「规则明确的清理项」**只是替用户勾选**，范围和以前一样受规则与保护检查限制；
  **缓存不等于删了没影响**，界面上已经写明，最终仍由用户在清理前检查页确认。
- 本轮改动**没有提交**（工作区里还叠着上一轮发布准备审查的未提交改动，
  为了不把遗留改动混成自己的，选择不提交）；包内 `ProductVersion` 因此仍带 HEAD `60f85ff`。

### 2026-09-13  发布准备审查（独立分支 release-prep-review，未推送）
> 独立发布准备审查：只动发布 / 文档 / 检查脚本，不改核心生产逻辑。

- 版本 **2.9.0** 与当前方向（清理为主 + 单项 AI 按需）一致；`ddw-develop` 较 v2.9.0
  多 **3 个未发布提交**（本地用途识别增强 / 清理页分区呈现 / 逐项 AI 来源隔离），
  版本号尚未 bump，留给下次发布时定 2.9.1 还是 2.10.0。
- 验证全绿：Release（win-x64）**0 错 0 警**；`StartupCheck` / `SafetyCheck`(984) /
  `UiRegressionCheck`(244) / `CleanupPresentationCheck` / `CleanAnalyzerCheck` /
  `AiNoteParserCheck` / `AppRecommendationCheck` / `LocalRecognitionCheck`(52) /
  `ItemAiIsolationCheck`(39) 全过。
- 修复（发布门禁 / 过时文档 / 脚本）：
  1. 状态表「现在做到哪」同步 v2.9.0：默认「清理中心」、导航顺序「清理 / 整理 / 卸载」、
     文件夹整理改「辅助入口」、删除已移除的「扩展名统计」行；
  2. README 下载版本 v1.1.0 → v2.9.0，功能列表与运行路径同步当前形态；
  3. `build.yml` 补 `submodules: recursive`（否则 CI 编不过 vendor 子模块）；
  4. `StartupCheck/Run.ps1` 与 `CleanupPresentationCheck/Run.ps1` 的 SDK 定位回退修正
     （原 `--list-sdks` 在「仅运行时、无 SDK」的 dotnet 上仍返回 0，会漏判；改用
     `--version` 退出码判定，本机 `dotnet` 是仅运行时、无 SDK，会失败）；
  5. 新增 `tools/release.ps1`：Release 构建 → self-contained 发布 → zip → SHA256 →
     发布 DLL 与构建 DLL 哈希核验（把散在日志里的手工打包步骤收成脚本）。
- 未做：真实 GUI 启动冒烟（需管理员，本会话非管理员，`requireAdministrator` 挡启动）；
  由 `StartupCheck` 的「真编译 BAML 加载」冒烟覆盖；未推送远端。

### 2026-09-13  DDWking（第二十八阶段：方向改为「清理为主 + 单项 AI 按需」 · v2.9.0）
> 需求（已冻结，覆盖上一轮的两级自动 AI 方向）：
> 清理页是默认首页；扫描后只做**本地**识别；取消整理页一、二级自动 AI 与所有批量入口；
> 模型只允许用户对**某一个**文件/文件夹主动发起；本地识别优先增强。

**1) 默认页与「零后台请求」**

- 构造时默认 `RightTab.Clean`（`_rightTab` 初值也改成 Clean），导航顺序改为
  **清理中心 → 文件夹整理 → 卸载**（清理在前）。
- **删掉 `RunScan()` 里的 `ShowRightTab(...)`**：扫描开始/结束/重建都不再把用户
  从自己选的页面拽走（以前一扫就弹回整理页）。
- 整理页**整块移除**批量 AI：`OrganizeIdentifyAllBtn`（重试全部）、
  `OrganizeWorkBar`+`OrganizeIdentifyCurrentBtn`（识别本层）、
  `OrganizeScopeBar`/`OrganizeStopBtn`/`OrganizeProgressPanel`（范围/预算/进度/取消）、
  右键「识别当前文件夹」，以及 `StartOrganizeAutoIdentify` / `AutoIdentifyTargets` /
  `RunOrganizeIdentifyAsync` / `ProbeUnknownLocally` / `CancelOrganizeWork`
  与任务号 `_organizeTaskId`、`_organizeBusy`、`_organizeStop`、`_organizeCurrent`。
- **侧栏删掉第二套 AI 主入口**：「识别用途」按钮 + 右键项 + `RunPurposeAsync`
  （含"深入识别子目录"）全删；侧栏只剩导航与本地用途文字。
- 新增 `AiGateway.SentCount` / `ResetSentCountForTest()`：在唯一出站咽喉
  `AiGateway.SendAsync` 处 `Interlocked.Increment`，把「后台有没有偷偷发请求」
  变成**可测事实**（不是翻日志）。

**2) 单项 AI（复用清理页那套，没有新提示词、没有第二套结果页）**

- `OrganizeNode.Ai`（`ItemAiView`），语义与清理页逐项分析一致：
  这是什么 / 删除可能影响什么 / 依据 / 缺什么 + 四档建议。
- `DescribeAiTarget` 新增 `OrganizeNode` 分支 → 走**同一个** `RunItemAiAsync`：
  同一份去重（`_itemAiRunning`）、缓存（`ItemAiCacheKey`）、取消、失败重试代码。
- 整理页行内一个 AI 图标按钮（ToolTip + `AutomationProperties.Name`），
  **只分析这一个文件夹**：`BuildFolderSummary(FileEntry, out total)` 只取扫描树里
  已存在的直接子项、条数受 `ItemAiPrompt.MaxFolderSummary` 限制，**不递归、不遍历磁盘**。
- 不碰 `Risk` / `CanDelete` / `Selected`：AI 只填展示态，绝不自动勾选或删除。
- 页头改成分档计数（本地 / AI / 未知 / 失败）；筛选口径从「待确认」改成「未识别」
  —— 没有结论就是没有结论，不是待办压力。

**3) 测试（行为用例为主；过期断言逐条改写，不删掉凑绿）**

- `tools/StartupCheck` 新增第 9 节，**驱动真 `MainWindow` 的私有路径**：
  默认页 = 清理中心（在**刚构造完**时取值，避免被后续用例切页影响）；
  `RunScan()` 里**没有** `ShowRightTab`；导航清理在前；10 项「批量/本层/重试全部」入口
  **不存在**；侧栏无 `PurposeIdentify_Click`/`RunPurposeAsync`/`CtxPurposeIdentify`；
  **扫描重建 + 展开/收起 + 筛选开关 + 切页 = 0 次模型请求**（真实出站计数），
  并配一条「本地识别确实跑了」防止"什么都没做才 0 请求"；
  单项分析：认出整理页对象、`ScopeKey` = 该文件夹完整路径、摘要 ≤ 上限、各对象 AI 状态互不串。
- 结果：**`StartupCheck` 97 PASS / 0 FAIL**、**`SafetyCheck` 984 PASS / 0 FAIL**、
  **`UiRegressionCheck` 244 PASS / 0 FAIL**、`CleanAnalyzerCheck` / `AiNoteParserCheck` /
  `AppRecommendationCheck` 全过、`git diff --check` 干净、Release **0 错 0 警**。
- 旧批量方向的过期断言在 `Run.ps1` 有 33 条、`LayeredCleanTests.cs` 有 7 条，
  **逐条改成新方向断言**（默认页/导航/「这些入口与机制不存在」/「单项 AI 是唯一模型入口」/
  「本地识别照跑」）。**保留**的安全回归：启动加固、行高合法值、首击展开、
  懒加载不自动展开、筛选是纯视图（无孤儿行、不改展开状态）、`Risk/CanDelete/Selected`
  不被 AI 写入、不移动/改名文件、不自提权、走 `ShellReveal` 打开目录、
  用户纠正优先、越权工具已移除。

**4) 真实启动冒烟（不从 dist 起）**

- 从 `src\AiDiskCleaner\bin\Release\...\win-x64\AiDiskCleaner.exe`（**2.9.0.0**）启动：
  进程存活、**主窗口句柄出现**（`hwnd` 非 0，标题「大扫货」，`Responding=True`，约 4.8s），
  `WM_CLOSE` 后 **1 秒内优雅退出**，**没有残留进程**。

**5) 打包**

- 版本 **2.9.0**；self-contained win-x64。
- `dist/DashaoHuo-2.9.0-20260913-win-x64/`，`AiDiskCleaner.exe` **2.9.0.0**，
  含 sidecar 与两个 helper；另有 `.zip` 与 `.zip.sha256`；
  **2.7.0 / 2.8.0 / 2.8.1 / 2.8.2 原样保留。**
- 包内 `AiDiskCleaner.dll` 与本次**验收构建**哈希一致：
  `8370F41CC677225B848D5FDFAAB8421FAF7DF73023F673CA983DD73DE5CA45D9`
- zip SHA256：`523D2092AA80A54E8FACACFECF8591DA9698A848DC2535A16B85684F355A723E`

**6) 未验收 / 已知限制（如实说明）**

- **没有做真实模型调用**：本轮全部验证是离线行为验证 + 真实出站计数，
  **不是**真机截图、也**不是**真实模型链路（单项 AI 的联网路径本身未实跑）。
- **没有实际 UI 实拍**（含窄窗口 / 高 DPI）；只做了真实启动冒烟（窗口句柄 + 优雅退出）。
- **需求 5（本地识别增强）本轮未实现**：真实安装位置（复用卸载清单）、
  系统重定向下载路径（`FOLDERID_Downloads`，而不是猜用户名拼接）、
  规范化边界匹配、共享厂商父目录不被单个软件吞掉、结构证据标为推测 —— 都还没做。
- **需求 6（清理页两类分区）**与需求 4 的「影响信息」文案层未改动。
- 整理页的 `Loc.OrganizeRun*`（旧批量进度/预算文案）与 `OrganizeStateKind.NoModel`
  已成死代码，本轮保留未删（删它要动 Loc 与状态机，收益低）。

### 2026-09-13  DDWking（第二十七阶段：修「一级没识别 + 展开点不动」 · v2.8.2）
> 现象：2.8.1 里**展开目录点不动**，一级目录**仍然大面积未识别**，顶栏永远停在
> 「识别中… · 0/2503 个文件夹有结论」。
> 两个都是**上一轮（v2.8.0/v2.8.1）引入的真 bug**，不是环境问题。

**1) 先复现：把行为用例写出来，按现状跑红（13 FAIL）**

新增 `tools/StartupCheck` 第 8 节，**驱动真 MainWindow 的私有路径**（反射调 `ToggleOrganize` /
`AutoIdentifyTargets` / 筛选 / 代次判据），不是字符串断言。红的时候证据很清楚：

```
FAIL 首击箭头：节点**确实展开**了        ← 第一次点只材料化、不展开
FAIL 再点一次：收起                      ← 于是第二次才展开、第三次才收起（整体错位一格）
FAIL 对象到上限/子项已在首屏：给出明确反馈  ← note 为空 = 点了没反应
FAIL 一级严格排在二级之前   [L2huge:L2,L1big:L1,L1small:L1]   ← 按容量混排
FAIL 打开筛选：**不改变任何节点的展开状态** [D=True E=True F=True]  ← E 被强制展开
FAIL 关闭筛选：回到打开前的展开状态        ← 关了也不收回（行集合 4 → 6）
FAIL 逐项AI代次变化（after-duplicates）不得让整理识别任务失效  [aiGen=1]
```

**2) 根因（都在代码里，逐行可查）**

| # | 根因 | 位置 |
|---|---|---|
| A | **材料化 ≠ 展开**：`SetChildren` 在 v2.8.0 改成默认 `autoExpand:false`（为了首屏只显示一级），但 `ToggleOrganize` 的懒加载分支只调 `Materialize` 而**没有自己展开** ⇒ 首次点箭头毫无反应，第二次才走 `else node.Expand()` | `MainWindow.Organize.cs` `ToggleOrganize` / `Materialize` |
| B | **识别这一遍被误杀**：失效判据用的是逐项 AI 代次 `_aiDataGeneration`，而**重复检测完成后的分层重建**（`RebuildLayersAsync` → `InvalidateItemAiAfterScan`）会把它 +1 ⇒ 识别循环立刻 `break` 并 `return`，**终态 note 不写**，顶栏永远停在开始时那句「识别中…」 | `InvalidateItemAiAfterScan` / `Stale` / `RunOrganizeIdentifyAsync` |
| C | **开始状态写死 0/N**：`SetOrganizeNote(Running + " · " + Covered(0, N))`，而计数只在终态算一次 ⇒ 跑着的这段时间顶栏就是「识别中… · 0/N」 | `RunOrganizeIdentifyAsync` |
| D | **筛选篡改展开状态**：打开筛选时 `foreach (n in _organizeAll) if (n.ChildrenLoaded) n.Expand()`，关掉没人收回 ⇒ 永久改变（也破坏了「首屏只显示一级」）；且筛选下只加命中行、不加祖先 ⇒ **孤儿行** | `OrganizeFilter_Click` / `AddVisibleRows` |
| E | **AI 顺序按容量混排**：`OrderByDescending(Size)` 把一、二级混在一起，大二级目录会插到一级前面 | `AutoIdentifyTargets` |
| F | **预算被悄悄清零 + 缓存被清空**（真机日志抓到的）：`InvalidateItemAiAfterScan` 里还调了 `ResetForScan()`，它每次分层重建都跑 ⇒ 请求计数归零（**60 次预算形同失效**）、用途缓存被清（已识别的要重问）。真机日志里第一条 AI 记录就是 `requests=0`，下一条 `requests=1`，正是被清零的证据 | `InvalidateItemAiAfterScan` |

**3) 修法（只碰这五件事，不动用途识别与删除隔离）**

1. `Materialize` 增加 `autoExpand`（**只有用户点箭头**传 true），并且**只在真的建出子项时**展开；
   后台材料化仍默认不展开 ⇒ 首屏还是只有一级。同时给出**明确反馈**：
   到对象上限 / 子项已在首屏 / 没有子文件夹，各有对应文案，不再"点了没反应"、也不假装展开成功。
2. 失效判据改成**整理数据生命周期 + 扫描代次**：`taskGen != _organizeGeneration || scanGen != _scanGeneration`。
   逐项 AI 代次不再参与 ⇒ after-duplicates 不会误杀；**换扫描 / 重建仍然让旧任务失效**（没删守卫）。
3. 再加一层**任务号** `_organizeTaskId`：只有当前任务能改界面；被换代/被接手的旧任务
   **连 finally 都不许动 UI**，不会覆盖新任务的进度条、终态或统计。
4. 筛选改成**纯视图**：可见集合 = 命中项 ∪ 它们的祖先（保留上下文、无孤儿行），
   **一次都不碰 `IsExpanded`**，所以关掉筛选就回到打开前的样子。
5. `AutoIdentifyTargets` 改成 `.OrderBy(Level).ThenByDescending(Size)`：**一级严格先于二级**；
   本地规则仍先跑**全部**候选，已有结论/缓存命中的对象不进队列 ⇒ 不消耗请求。
6. `ResetForScan()` 从 `InvalidateItemAiAfterScan` **移到** `RebuildOrganize`：只有真换扫描才清缓存/归零计数。
7. 顶栏改成**分档计数**（本地 / AI / 未知 / 失败），终态短句只说页头说不出的事
   （未发送 / 失败），这一遍的明细进 Tooltip；开始态只写「识别中…」，**不再写死 0/N**。
   取消路径只写「识别已取消」。

**4) 测试（行为用例先红后绿）**
- Release 编译 **0 错 0 警**
- **`StartupCheck` 79 PASS / 0 FAIL**（修前同一批 13 FAIL）：
  首击展开 / 收起 / 再展开复用同一批子节点 / 箭头方向通知 / 对象上限与"子项已在首屏"的明确反馈 /
  一级严格先于二级 / 已有结论不进队列 / 筛选不改展开状态 + 无孤儿行 + 关闭恢复 /
  after-duplicates 不失效 + 换扫描与重建仍失效 / **缓存与请求计数只随真换扫描复位**（用本地可判定的
  目录把缓存喂起来，`allowAi:false` 全程不联网）
- `SafetyCheck` **984 PASS / 0 FAIL**；`UiRegressionCheck` **244 PASS / 0 FAIL**；
  `CleanAnalyzerCheck` / `AiNoteParserCheck` / `AppRecommendationCheck` 全过；`git diff --check` 干净

**5) 真实交互验收（真 EXE，从 `bin\...\win-x64` 起，不用 dist）**

用 UIA 的 `InvokePattern` 点真按钮（不合成鼠标坐标），以**应用自己的日志**为准：

- **首击展开**（这条以前是坏的）：
  `13:10:23 op=toggle dir=<一个根目录> level=1 expanded=True loaded=True kids=5 rows=31`
  ⇒ 同一击里既完成材料化（`loaded=True`）又展开（`expanded=True`），行数 26 → 31
- **筛选只读**：点开筛选再关掉后，`'收起' 箭头仍然存在 = True`（原本展开的行没被收起）、
  且 `'展开子文件夹' 箭头仍然存在 = True`（原本收起的行也没被强制展开）
- **运行中状态**：`本地 89 · AI 0 · 未知 2,504 · 失败 0 · 识别中…`（**没有 0/N**）
- **取消终态**：取消后 `… · 识别已取消`，取消按钮消失（进度区收回）
- **完成终态**：`13:10 ` 那次跑到终态：
  `op=identify-done targets=2504 reached=2504 attempted=60 requests=60/60 ai=9 unknown=0 failed=51 notSent=2444 complete=False`
- **after-duplicates 之后识别还活着**（这条是 B 的正面证据）：
  `12:21:35 layers(after-duplicates)` → `12:21:37` 起持续发请求 → `12:26:11 op=identify-done`
- **真实 AI 链路**：真的调用了模型（非模拟），日志里 59 / 9 条带结论回复，`scrubbed=0`
  （出站片段未命中任何密钥模式；报告里不贴任何片段内容）
- 正常关闭：`WM_CLOSE` → 1 秒内退出（本轮多个实例一致）

**6) 数字要读准（别把 reached 当识别成功）**
- `reached=2504` 只表示**遍历到了**这些目标，**不等于**它们都被识别。
- 两次真机运行的差别（同一份代码，真实模型）：早一次 `AI 59 / 失败 1 / 未发送 2443`；
  晚一次 `AI 9 / 失败 51 / 未发送 2444`。**晚一次失败 51 次是真的**（服务端/网络侧不稳定），
  界面如实显示「失败 51」并保留统一重试入口，没有粉饰成成功。
- 结论：**一、二级自动识别仍然受 60 次请求预算限制**，未发送的 2400+ 个保持「未识别」，
  **不能称"全部识别完"**；要更多覆盖只能重试或以后调预算（本轮刻意没加大）。

**7) 打包**
- 版本 **2.8.2**；self-contained win-x64。
- `dist/DashaoHuo-2.8.2-20260913-win-x64/`（546 文件，含 sidecar 与两个 helper），
  `AiDiskCleaner.exe` **2.8.2.0**；另有 `.zip` 与 `.zip.sha256`。
  **2.7.0 / 2.8.0 / 2.8.1 目录与包原样保留。**
- 包内 `AiDiskCleaner.dll` 与本次**验收构建**哈希一致：
  `9079DED8882BCB6693F8ECD2B30DE1805FD2867057FBBECB2657575D5C9EA096`

**8) 旧卡死进程的处理（有身份核验、没有盲杀）**
- 两个 10:09 / 10:10 起的老实例：先用**只读**核验 `name=AiDiskCleaner`、`MainWindowHandle=0`、
  启动日期一致；再用提权辅助程序补上 `path=…\dist\DashaoHuo-2.8.0-20260913-win-x64\AiDiskCleaner.exe`、
  `version=2.8.0.0`，**五项全过**才逐个 `Stop-Process -Id`（停止前再核验一次名称与"无窗口"）。
  两个都 `GONE`，没有触碰任何其它进程；**没有按 PID 盲杀、没有按名字批量结束**。
- 副作用（正面）：它们此前每秒写满 2MB 日志、把 6MB 的日志环一直刷穿，导致上一轮**完全取不到
  用户会话的日志证据**；清掉之后日志才可用（这轮的 AI/生命周期证据就是这么拿到的）。

**9) 未验收 / 已知限制（如实说明）**
- **高 DPI / 150% 缩放下的排版仍然没有实拍**（只按代码推算）。
- **启动期致命路径**（MessageBox + 退出码 70）**没有被真实触发过**（现在不会触发了），
  由 StartupCheck 的判定与提示钩子断言覆盖，**不是端到端实测**。
- `AiSidecar` 未参与本次链路（走的内置通道）。
- 终态短句在**失败数 > 0** 时会与页头的「失败 N」重复一次（同口径、不矛盾）；
  属于文案冗余，本轮未再改动（避免为了文案再重编重验一遍）。
- 早前那次真机验收是在**修显示文案之前**的构建上做的；功能修复（展开/代次/筛选/顺序/缓存）
  与最终产物一致，显示文案（短终态、取消、无 0/N）是在**最终构建**上重新验的。
- 真实交互验收里**没有**做「三次点箭头」那种连点脚本（UIA 每次按名字找第一个箭头，
  展开后名字会变，可能点到下一行）；改用**应用自己的 `op=toggle` 日志**作为行为证据。

### 2026-09-13  DDWking（第二十六阶段：修 2.8.0 启动即死循环 · v2.8.1）
> 现象：**双击 2.8.0 的 EXE 没反应，没有任何可见报错**。
> 结论：不是权限、不是缺依赖、不是单实例锁 —— 是 `MainWindow.xaml` 里一个**编译通过、
> 运行时非法**的 XAML 属性值，加上崩溃处理器自己的死循环把现场证据刷掉了。

**1) 根因（已本地复现，不是推测）**

- `MainWindow.xaml` 第 1372 行：`RowHeight="Auto" MinRowHeight="46"`（2.7.0 是 `RowHeight="46"`）。
- `System.Windows.Controls.DataGrid.RowHeight` 是**普通 `double`，没有 TypeConverter**
  （对比 `FrameworkElement.Height/Width` 有 `TypeConverterAttribute`）。所以 `"Auto"` 在
  **BAML 加载期**抛 `XamlParseException`（内层 `FormatException: The input string 'Auto'
  was not in a correct format.`）。**编译器不检查这个值，所以编译 0 错 0 警照样出包。**
- 复现方式（与 BAML 加载同一条路）：`XamlReader.Parse('<DataGrid RowHeight="Auto"/>')` 抛异常；
  `RowHeight="NaN"` 与 `RowHeight="46"` 都正常。`LengthConverter` 对 `"NaN"` 返回 `double.NaN`。

**2) 为什么用户看到的是「没反应」而不是报错**

1. `App.xaml` 有 `StartupUri="MainWindow.xaml"` → 构造 `MainWindow` → `InitializeComponent()`
   在第 1372 行抛异常，**窗口从未建出来**（实测 `MainWindowHandle=0`）。
2. 因为解析中断，XAML 里**第 1372 行之后**的 `x:Name` 字段全没赋值 —— 其中就有
   `AlertText`（第 2503 行）、`Overlay`（第 2326 行）。
3. `DispatcherUnhandledException`（`OnStartup` 第一件事就注册）捕获 → `Crash(ex)`；
   当时 `Application.MainWindow` 已经是那个**半初始化**的窗口，于是走到
   `w.Dispatcher.BeginInvoke(() => w.ShowCrash(ex.Message))`。
4. `ShowCrash` → `ShowAlert` → `AlertText.Text` → **NullReferenceException**；
   而第 72 行是 `BeginInvoke`，这个 NRE 又回到 `DispatcherUnhandledException` → `Crash()` →
   第 4 步 → **无限递归**。进程驻留、没有窗口、CPU 约 88% 空转。
5. 实测证据：`crash.log` / `app.log`(.1/.2) 共 **6MB 全是同一条 NRE**（一秒上万条），
   崩溃处理器**把自己要用的证据轮转掉了** —— 原始 `XamlParseException` 事后完全查不到。
   最近失败时间：2026-09-13 10:12:20 起（10:00:02 打出的 2.8.0），一直持续到被发现。

**3) 修了什么（只碰启动与异常处理，不动识别产品设计）**

| # | 修法 | 要点 |
|---|---|---|
| 1 | `RowHeight="Auto"` → **`RowHeight="NaN"`** | `NaN` 就是「自动行高」，详情展开时行照样能变高；`MinRowHeight="46"` 保留，行不会被压扁 |
| 2 | 崩溃处理器加**启动期致命闩锁** | 判定过一次启动期致命，后续异常一律丢弃 —— 这是死循环的正面开关 |
| 3 | 崩溃处理器加**重入闸** | 处理器自己在记录/提示时再抛，直接返回；`finally` 才释放闸 |
| 4 | **异步提示回调自身安全** | `BeginInvoke` 里的 `ShowCrash` 再包一层 `try/catch`（它一旦抛就会重新触发 `DispatcherUnhandledException`，光靠"进入前设闸、离开时复位"挡不住异步递归） |
| 5 | **半初始化判定** `MainWindow.IsUiReady` | 构造函数**最后一行**才置 true；崩溃处理只在 `IsUiReady` 为真时才碰窗口控件 |
| 6 | 半初始化/启动期走**独立于 MainWindow 控件**的安全提示 | `MessageBox` + 脱敏后的原始异常；连提示都失败也不抛 |
| 7 | 启动期致命 ⇒ **受控非零退出**（`FatalStartupExitCode = 70`） | 不再"吞掉异常继续跑"，避免留下没有窗口的空转进程 |
| 8 | 新增 `Services/CrashFirstRecord.cs` | **每次运行只记第一条**原始异常；独立文件 `first-crash.log`（自己的 256KB 上限，只滚自己）；过 `LogRedactor` 脱敏；写不了也绝不抛；**不删用户任何旧日志/配置** |
| 9 | 运行期异常提示**有界**（最多 3 次） | 运行期异常风暴也不会刷屏 |

**4) 为什么以前那套测试没拦住（本轮补的运行时防线）**

- `UiRegressionCheck` 是**字符串/AST 断言**：它从不判断 `RowHeight` 的**值合不合法**，
  而 XAML 编译期也不检查 —— 于是"编译通过 + 断言全绿 + 窗口打不开"可以同时成立。
  **字符串测试永远抓不到这一类"运行期才炸的 XAML 值"。**
- 新增 `tools/StartupCheck`（WPF 宿主，**真的加载编译后的 BAML**，就是 `StartupUri` 那条路）：
  1. 编译产物里确实有 BAML 资源；
  2. `new App(); app.InitializeComponent();` 加载 App.xaml 资源；
  3. `new MainWindow()` —— **2.8.0 就是在这一步炸的**；
  4. `IsUiReady` 为真 + 关键命名控件（`AlertText`/`Overlay`/`OrganizeGrid`/…）都已赋值；
  5. `OrganizeGrid.RowHeight` 是 `NaN`、`MinRowHeight >= 46`；
  6. 崩溃处理：启动期致命 / 运行期 / 抑制 / 提示钩子自己抛 / 日志目录不可写；
  7. **真实控件树**上量详情展开：行高确实变大、详情块**完全落在行内（没被裁切）**、收起后回到原高。
- 先保留失败证据再修：改 `RowHeight` 之前跑 StartupCheck 得到
  `FAIL MainWindow 构造 / BAML 解析 [FormatException: The input string 'Auto' was not in a correct format.]`，
  修完同一条用例转绿。

**5) 测试结果（离线部分）**
- Release 编译 **0 错 0 警**（`dotnet build -c Release -r win-x64`）
- **`StartupCheck` 52 PASS / 0 FAIL**（新增，真实 BAML + 真实控件树布局）
- `SafetyCheck` 983 PASS / 0 FAIL；`UiRegressionCheck` **233 PASS / 0 FAIL**（+11 条启动防线断言）
- `CleanAnalyzerCheck` / `AiNoteParserCheck` / `AppRecommendationCheck` 全过；`git diff --check` 干净

**6) 真实启动验收（与离线测试分开记录）**
- 验收对象：**`bin\...\win-x64\AiDiskCleaner.exe`（2.8.1.0）**，不是 dist。
- 11:39:56 启动 → `hasExited=False`、**`MainWindowHandle=657850`、标题「大扫货」**、
  `responding=True`、无 `first-crash.log`。
- **实拍截图**（1000×700，54KB）：真实主窗口，页头「文件夹整理 · 2,592 个文件夹 · 377 G」，
  **首屏只有一级且全部收起**（每行都是右向箭头），系统语义结论正确显示：
  Windows→Windows 操作系统文件、ProgramData→共享程序数据、用户目录→用户文件和应用数据、
  Program Files→程序安装目录、Program Files (x86)→32 位程序安装目录、Downloads→下载文件、
  Local→本地应用数据、Roaming→漫游应用数据；**首屏没有 System32 / WinSxS**。
- 布局：880×600 ↔ 1400×880 来回改尺寸，`responding=True`、进程不退。
- **正常关闭**：`WM_CLOSE` → **1 秒内退出**（这一轮 4 次实例全部如此）。
- 全程没有出现新的崩溃循环（`first-crash.log` 始终不存在）。
- ❌ **没有做真实鼠标点击展开详情**：应用带 `requireAdministrator`，非管理员进程发消息被 UIPI 拦；
  提权辅助进程又**有时落在别的窗口工作站**（`EnumWindows` 看到 0 个窗口）。
  改为用 **StartupCheck 的真实控件树布局断言**覆盖这一项（行高变大 + 详情块完全落在行内），
  并如实标注"不是鼠标实测"。

**7) 打包**
- 版本 **2.8.1**；self-contained win-x64。
- 产物：`dist/DashaoHuo-2.8.1-20260913-win-x64/`（546 个文件，含 `sidecar/AiSidecar.exe`、
  `THIRD-PARTY/`、`SteamHelper.exe`、`StoreAppHelper.exe`），`AiDiskCleaner.exe` 版本 **2.8.1.0**；
  另有 `.zip` 与 `.zip.sha256`。**2.7.0 / 2.8.0 旧目录与旧包都原样保留。**
- 包里的 `AiDiskCleaner.dll` 与本次**验收构建**哈希一致：
  `9AE3916072658A48CA918651FB91A2B904EAF0751A2267D97F8592B73C188467`
  ⇒ 打的就是验收过的那份代码。

**8) 仍未验收 / 已知限制**
- **真实鼠标点击展开详情**未做（见 6，环境限制）；由 StartupCheck 的控件树断言替代。
- **真实 AI 调用**依旧没有；`AI 推测 / 待确认 / 失败 / 取消` 只有离线断言。
- 高 DPI / 150% 缩放下的排版仍未实测（只按代码推算）。
- 启动期致命路径（`MessageBox` + 退出码 70）**没有被真实触发过一次**（因为已经不会触发了），
  它由 StartupCheck 的判定与提示钩子断言覆盖，**不是端到端实测**。
- 两个 2.8.0 的旧实例（现在已不再占用）当时无法从非管理员 shell 结束 —— 这是提权进程的固有限制，
  应用本身没有单实例锁，不影响新版本启动。

**9) 过程中的坑（下次别踩）**
- **XAML 属性值合法性和编译是两码事**：`RowHeight="Auto"` 编译通过、运行期才炸。
  凡是 `double`/`Length` 类属性，值合法性只能用"真的加载一次 BAML"来兜。
- **日志滚动会把第一现场删掉**：崩溃风暴一秒能写几 MB，`app.log`(2MB×3) 与 `crash.log`(512KB)
  瞬间被刷穿。所以第一现场必须**单独文件 + 每次运行只写一条**。
- **`Dispatcher.BeginInvoke` 里的回调必须自己包 `try`**：只在调用前后设/复位闸挡不住异步递归。
- **半初始化窗口不能碰**：构造函数中途抛异常时，异常点之后的 `x:Name` 字段全是 null。
- **提权进程的两个坑**：非管理员 shell 既结束不了它、也发不了窗口消息（UIPI）；
  用 `Start-Process -Verb RunAs` 起的辅助进程**有时在别的窗口工作站**（`EnumWindows` 看到 0 个窗口，
  但 `Process.MainWindowHandle` 还能用）；`-WindowStyle Hidden` 会让子 GUI 进程的窗口**继承隐藏**，
  于是"看起来没窗口"—— 验收 GUI 必须显式 `-WindowStyle Normal`。
- **PS 5.1 老坑**：不支持 `??`；`-File` 读无 BOM 的 UTF-8 会按 GBK 解，中文注释能把后面的代码吃掉
  （临时脚本一律写成纯 ASCII）。

### 2026-09-13  DDWking（第二十五阶段：按 Grok 4.6 第二意见校准首屏与系统语义 · v2.8.0）
> 问题：v2.7.0 虽然做了「两级自动识别」，但**首屏一打开就把 WinSxS / System32 这类二级目录摊出来了**；
> 一级的系统入口（Windows、下载、AppData 本身）没有本地结论、会去问模型；
> 右侧还有一个和左边箭头重复的「展开子文件夹」文字按钮；「当前文件夹」工作条和整表并存导致语义错位；
> 顶栏永远写「自动识别结束」，失败与未覆盖被「已处理」掩盖。本轮**只做校准，不换方向**。

**1) 七个根因逐条修（都先复现再改）**

| # | 根因（复现） | 修法 |
|---|---|---|
| 1 | `OrganizeNode.SetChildren` 一律 `IsExpanded = true`，于是**材料化即展开**，`C:\Windows` 的子项（System32 / WinSxS）首屏就出现 | `SetChildren(..., bool autoExpand = false)` **默认不展开**；材料化与展开彻底分开。首屏可见树 = 根的一级（`AddVisibleRows` 只在 `IsExpanded` 时递归） |
| 2 | `RecognizeLocally` 只对「入口表里的全路径」给 `系统位置 + 待确认`，入口表又缺 `SpecialFolder.Windows`；所以 Windows / 下载 / AppData 都没有结论，全部落到 AI | 新增 `FolderPurposeRules.SystemRoles()` / `SystemRole(path)`：Windows / Program Files / PFx86 / ProgramData / 用户目录 / 用户目录上一级 / AppData / Local / Roaming / 文档 / 下载，**全部由 `Environment.SpecialFolder` 解析**（AppData 与用户目录上一级由解析结果**推导**），命中即返回 `HasConclusion=true, NeedsConfirm=false` 的本地结论；判定**只按全路径相等**，其它盘同名目录不误判；`EntryPoints()` 补上 Windows 与下载 |
| 3 | 入口的直接子项没进后台识别（`ShouldAutoMaterializeChildren` 见入口就 `return false`），用户不点入口就永远没结果 | 规则改为：**系统入口一律铺直接子项**（入口的直接应用目录与入口同为第 1 级）；收纳/混合继续铺；具体对象/未知不铺内部。材料化仍**不展开**，所以「后台识别好、展开即看结果、不重复请求」三件事同时成立 |
| 4 | 右侧「展开子文件夹 / 收起」文字按钮与左侧箭头重复 | 删掉右侧文字按钮；**左侧箭头是唯一展开/收起入口**（方向表达状态），右侧只留「在资源管理器中打开」图标按钮（ToolTip + `AutomationProperties.Name`） |
| 5 | 工作条在任意选中节点上都显示「当前文件夹：xxx」，但下面仍是整表 —— 语义错位 | 工作条**只在三级及更深出现**（`!node.IsDeepLevel` 就收起）；文案改成「识别本层文件夹」+「只识别这个文件夹的直接子文件夹（不分析这个文件夹本身，也不会继续深入更深目录）」；不为它新增页面 |
| 6 | 顶栏永远写「自动识别结束」；`done` 是人数、AI 请求数不可比；出站被拦时返回空结论而不计失败；finally 清排队态让「结束 + 待处理」并存 | 新增 `OrganizeRunRunning / Done / Incomplete / Canceled` 四态，只有「全部跑到 + 没有未覆盖 + 没有失败」才叫完成；顶栏改成 `已识别（其中 AI 推测）/ 未知 / 失败 / 有结论的占比`，**请求预算与「没跑到多少」进 Tooltip**；出站自检不过 ⇒ `FolderPurposeResult.Failure`（计失败、可重试）；finally **只清流程态、保留失败/取消** |
| 7 | 用途只有一行字，看不到判断依据 | 点用途展开**行内详情**：这是什么 / 为什么这么判断（直接子目录数、文件数、代表文件名、判定依据，全部来自真实摘要）/ 判断来源；默认收起，没有结论就照实说未知；`PurposeBasisSignature` 从「命中本地签名」改成「本地认出这是…」，**去掉开发术语** |

**2) 覆盖率与公平性（顺手修掉的隐患）**
- 材料化改成**两趟**：先给**每个根**铺直接子项，再铺第 1 级对象的下一层。
  一趟到底会让第一个大目录（如 Program Files）吃掉全部对象预算，后面的根连一级都建不出来。
- 对象上限 `MaxObjects = 6000` 与可见行上限 `MaxRows = 4000` **分成两条线**；没材料化的条目
  计入「未列出（未识别）」，页头如实报「还有 N 个文件夹没有跑到（不算已识别）」。
- 页头容量改成用**扫描根的真实总量**：顶层对象现在允许「入口与它的祖先同时出现」，
  把根的容量相加会重复计算。
- 同一个真实目录只出现一次：已经是顶层对象的目录不再挂到父节点下面（`_organizeByDir` 去重）。

**3) 真机证据（只读遍历 C:，未删任何文件；用一次性证据程序跑完即删）**
```
系统语义表（全部来自 Environment.SpecialFolder，无硬编码盘符/用户名）:
  C:\Users\<用户>\AppData\Roaming  -> 漫游应用数据
  C:\Users\<用户>\AppData\Local    -> 本地应用数据
  C:\Users\<用户>\Downloads        -> 下载文件
  C:\Program Files (x86)           -> 32 位程序安装目录
  C:\Users\<用户>\AppData          -> 用户应用数据
  C:\Program Files                 -> 程序安装目录
  C:\ProgramData                   -> 共享程序数据
  C:\Users\<用户>                  -> 用户文件和应用数据
  C:\Windows                       -> Windows 操作系统文件
  <另一个盘>\文档\我的文档          -> 文档
  C:\Users                         -> 用户目录
```
- C: 遍历 272,637 目录 / 1,484,982 文件 / 348 G
- **顶层对象 25 个，首屏可见行 = 25（全部收起）**，包含
  `Windows | ProgramData | <用户名> | Program Files | Program Files (x86) | Downloads | Local | Roaming | Users | …`
  ⇒ **`C:\Users` 与 `AppData\Local` / `AppData\Roaming` 同时存在**（祖先不再吞入口，入口也没丢）
- **首屏可见行里没有 System32**；Windows 子项已材料化但默认不展开
- 材料化对象：第 1 级 575 / 第 2 级 2134 / **三级及更深 0** ⇒ 自动识别范围 2709 个对象
- 本地识别：52 个有结论（含全部系统语义），其余保持未知；未列出 2650 条如实计数

**4) 测试结果（全部离线；真实文件只用系统临时目录，真实盘只读）**
- Release 编译 **0 错 0 警**（`dotnet build -c Release -r win-x64`）
- `SafetyCheck` **983 PASS / 0 FAIL**（本阶段 **+57**）：新增「整理页行为」一节，用**行为断言**而不是字符串：
  - 首屏不见 WinSxS / System32 / 入口子项；首屏行数 == 顶层对象数；材料化后**一个都没展开**
  - 展开 Windows 才看到它的直接子项；展开不重建子对象；三级默认收起；收起再展开仍用缓存
  - Windows / 用户目录 / AppData / Local / Roaming / 下载 / 文档**都有本地结论且不需确认**，且不进 AI 队列
  - 其它盘同名目录（`D:\Windows`）**不被误判**；`SystemRole` 只认全路径相等
  - 无模型 ⇒ 没有结论 + 待确认 + **不消耗预算 + 不写缓存**；失败 ⇒ `Failed` 且可重试；
    「判不出来」是未知不是失败；`ClearState` 不会把失败说成已识别
  - 行内详情默认收起、点开含「这是什么 / 为什么 / 来源」、写的是真实证据（子目录数、代表文件名）、
    不出现「命中签名」；没有结论时照实说未知
- `UiRegressionCheck` **222 PASS / 0 FAIL**（本阶段 **+17**）：右侧没有第二个展开按钮、左侧箭头是唯一入口、
  操作列只剩资源管理器图标（含 ToolTip + AutomationProperties.Name）、行高可增长、
  详情块默认收起、工作条只在三级及更深出现、按钮文案是「识别本层文件夹」且说明只处理直接子目录、
  进度四态（完成/进行中/未完成/取消）、计数含 已识别/AI/未知/失败、请求预算进 Tooltip、
  「没跑完不叫完成」、系统语义八项齐全、只按真实路径匹配、首屏不自动展开
- `CleanAnalyzerCheck` / `AiNoteParserCheck` / `AppRecommendationCheck` 全过

**5) 打包**
- 版本 **2.8.0**；self-contained win-x64。
- 产物：`dist/DashaoHuo-2.8.0-20260913-win-x64/`（546 个文件，含 `sidecar/AiSidecar.exe`、
  `THIRD-PARTY/`、`SteamHelper.exe`、`StoreAppHelper.exe`），`AiDiskCleaner.exe` 版本 **2.8.0.0**；
  另有 `.zip` 与 `.zip.sha256`。发布产物里的 `AiDiskCleaner.dll` 与构建输出**哈希一致**（`EA515FE4…`）。

**6) 已知限制 / 未验收（如实说明）**
- **没有接通真实 AI**：`AI 推测 / 待确认 / 失败 / 取消 / 重试` 的真机表现全部只有离线断言。
- **没有 UI 实拍**：`AiDiskCleaner.exe` 带 `requireAdministrator`，非管理员 shell 无法枚举/截图/结束该进程
  （实测 Access Denied，只能在提权子进程里杀）。所以默认窗口 / 窄窗口 / 高 DPI 下的**实际排版、行高增长、
  详情换行、列宽观感本轮没有实拍**。已由断言锁定的是：列优先级（<780 收窄操作列、<620 隐藏容量列）、
  `RowHeight=Auto` + `MinRowHeight=46`、虚拟化与无嵌套滚动、详情默认收起。
  **编译通过不等于 UI 验收。**
- 真机证据是**一次性只读程序**（不在测试里跑真实盘），因为整盘遍历要 ~160s；它验证的是
  层级/入口/首屏规则，不是 WPF 渲染。
- 单个目录最多材料化 400（根）/ 200（子层）个对象；超出如实报「未列出」，本轮**没有「再显示更多」按钮**。
- 非管理员模式下清理仍不可用（`RecursiveScanService` 不设 `FileEntry.Parent`），本轮未动。

**7) 过程中的坑**
- 一次性证据程序最初把 `CleanListSnapshot.NormPath` 抄错了（给盘符补了尾反斜杠），
  导致 `C:\` 的入口全部匹配失败、Top 里看不到 ProgramData/Users —— **应用是对的，是抄的那份错了**。
  教训：证据程序要复用真实实现，别手抄。
- 轨迹里 `Walk()` 原来把「子目录是链接」标到了**父目录**身上，于是 `C:\Users`
  （含 `All Users` 等 junction）被当成链接跳过。真实扫描器按条目设置，不受影响。
- `tools/UiRegressionCheck/Run.ps1` 是 UTF-8 with BOM，`edit` 改写后 BOM 会掉，**每次都要补回**。

### 2026-09-13  DDWking（第二十四阶段：两级自动识别 + 三级手动 · 按 Grok 4.6 第二意见校准 · v2.7.0）
> 问题：v2.6.0 的整理页仍然要用户点「识别这些文件夹」，而且识别深度与**界面行数上限**搅在一起；
> 系统盘上 `C:\Users` 这类祖先目录还会把 `AppData\Local` · `Roaming` 入口**吃掉**。
> 本轮按第二意见做**最小必要修改**，不推翻「扫描后直接列对象」的方向。

**1) 先复核：第二意见指出的 5 个风险都成立，逐条修**

| # | 风险 | 真因（复现） | 修法 |
|---|---|---|---|
| 1 | 系统入口被祖先吞掉 | `BuildRoots` 里盘符子目录用 `IsUnderOrEqual(a,n) \|\| IsUnderOrEqual(n,a)` 双向去重，`C:\Users` 一旦先入列，`C:\Users\<你>\AppData\Local` 就被当成「已覆盖」丢掉 | 改成**只有「自己就是一份入口」才去重**；入口的**祖先**（`Users` / `AppData`）不进顶层对象，只当路径。新增 `IsAncestorOfEntryPoint`，`ShouldAutoMaterializeChildren` 允许顺着祖先铺到入口、入口自身停 |
| 2 | 识别覆盖被 UI 行数上限卡住 | `ChildBudget = 24` 同时当显示上限和材料化上限 | 拆成两条线：显示仍 `ChildBudget = 24`，材料化 `MaterializeBudget = 400` / `MaxMaterializePerNode = 200`；**没材料化的条目如实计数**并标「未列出（未识别）」，不算已识别 |
| 3 | 失败不落 Failed | 服务 catch 里统一返回 `None + NeedsConfirm`，超时/断网看起来和「还没识别」一样 | 新增 `FolderPurposeResult.Failure` + `Failed` 标记；`State` 明确映射 已识别 / 待确认 / 未识别 / **失败**；`OrganizeNode.Apply` 见 `Failed` 就落 `PurposeState.Failed`；页头与提示分别说清「失败 / 预算用完 / 未配模型」 |
| 4 | 集合识别太窄 / 有硬编码例子倾向 | `ClassifyKind` 要求 ≥4 子目录**且**子目录命中签名才算集合，小集合与「有好几个子目录但签名认不出」都会变未知 | 用通用证据重写：目录**自己**命中签名才算具体对象；子目录按**体积**判「像独立对象」；≥4 个独立子目录算集合，≥2 个 + 体积足够也算；新增 `IsKnownObject` / `TrivialChildBytes` / `StrongChildBytes`，**没有任何具体例子** |
| 5 | 串行等待 / 假进度 | 本地那一遍与 AI 那一遍**都** `done++`，进度会超过 100%；NoModel/预算用尽会把前面的提示覆盖掉 | 修正计数（本地算处理、AI 只补剩余，`done = Math.Min(total, done+1)`）；每 32 个让出时间片；失败/预算/未列出**各自补一句**而不是互相覆盖；取消后排队项回落「未识别」 |

**2) 新增的层级语义（这是本轮的核心，起点写清楚）**

- `OrganizeLevelPolicy`：**第 1 级** = 盘符下直接子目录（`D:\Gameklll`）**或系统入口本身**；
  **第 2 级** = 它们的直接子目录（`D:\Gameklll\steam`）；**第 3 级及更深** = `IsManualOnly`。
- 系统入口是**容器**：入口的直接应用目录**与入口同级（第 1 级）**，层级不叠加 ——
  这样 `AppData\Local` 下的 `Google` 一样在第 1 级被识别，而不是被推到三级去。
- 入口的**祖先**（`C:\Users` → `C:\Users\<你>` → `AppData`）照旧可以一路铺到入口（路径例外），
  但入口本身不再自动往下 —— 入口内部由用户展开。
- `ShouldAutoIdentify` 只收一、二级；`AutoIdentifyTargets()` 在整理页过滤 `IsAutoLevel`，
  **三级一个都不会进自动队列**（有断言锁定）。

**3) 界面（只做必要改动，不重做）**

- **删掉行内逐项「识别」按钮**：行里只剩「展开 / 收起」和「在资源管理器中打开」。
  以前每行一个「识别」会让人以为必须手动逐个点。
- 页头左边只留一条**自动识别进度**（`已处理 / 待处理 / 失败 · AI 请求 N/M`），
  右边一个**统一重试入口**（`重试待确认 / 失败`，跑同一批待处理项）。
- 新增**当前文件夹工作区**（`OrganizeWorkBar`）：显示当前文件夹、层级、真实路径，
  一行写明「本次只分析这一层的 N 个直接子文件夹 · 最多发 M 次请求 · 不会自己继续深入更深目录」，
  右边**一个**主操作按钮「识别当前文件夹」。展开 / 双击 / 右键都会把「当前文件夹」切过去。
  右键菜单里也只有这一条识别项（没有逐行识别）。
- 页头如实报「还有 N 个文件夹没有列出（也未识别）」。
- 自动识别在**扫描完成后启动**，范围与预算写在同一句里；`_organizeAutoStarted` 保证同一次扫描只跑一次；
  顶栏在整理页也显示状态；顶栏「停止」现在会一并取消整理页的识别。

**4) 送给模型的东西（安全边界，都是硬约束）**

- 新增 `Services/SourceSnippet.cs`：**白名单**只读 README / LICENSE / CHANGELOG 与
  项目配置 / 清单（`package.json`、`*.csproj`、`*.sln`、`go.mod`、`pyproject.toml`、`Makefile` …），
  **只读直接子级、不递归、不读普通文件正文**；单文件 ≤ 600 字符、最多 3 个文件、合计 ≤ 1200 字符、
  超过 64 KB 的文件直接不读；跳过项如实计数。
- **强制脱敏**（读出来之后、送出之前）：JSON/YAML 敏感键值、`KEY=值` 环境变量、`sk-` / `ghp_` / `AKIA` /
  `eyJ…` JWT / `xox` 等已知前缀、`Bearer` 值、URL 里的 `user:pass@`、`C:\Users\<名>\` 段、
  超长疑似编码串 —— 全部换成 `[已脱敏]`；键名保留，模型仍知道「这里有个配置项」。
- `FolderPurposeRules.BuildAiInput(sum, sendFullPath, maxChars, snippets)`：
  目录名 + 受控结构摘要 + 类型分布 + **少量**代表文件名 + 直接子目录名 + 程序元数据 +
  **经筛选脱敏的片段**；`path:` 在 `AiSendFullPaths=false`（默认）时走 `PathRedactor`；
  **不发完整文件清单**；字数上限提到 1800（含片段）。
- `FolderPurposeRules.IsSafeOutbound`：出站前自检（占位符幂等），不过就**不发**。
- 模型回复也过一遍脱敏再解析 / 缓存，避免回显密钥进缓存和界面。
- 预算：`MaxAiRequests 24 → 60`、超时 `40s → 30s`、并发仍 2，新增**两次请求最小间隔 250 ms**
  （批量时不打爆供应商）；**失败 / 超时 / 空回复一律不缓存**（否则「重试」会命中失败结果）。

**5) 用户纠正的边界（第二意见特别指出）**

- `OrganizeNode.Apply` 现在**只有非用户来源**才写 `Kind` ⇒ 用户纠正**只改用途结论**，
  不会因为纠成一个「具体东西」就把这个集合的子项藏起来 / 停止展开（有断言）。
- `SetUserCorrection` / 落盘读回的 `Kind` 改成 `Unknown`，语义与上面一致；
  纠正仍按**规范化路径**记，重扫 / 重启都优先。

**6) 测试结果（全部离线；真实文件只用系统临时目录）**
- Release 编译 **0 错 0 警**（`dotnet build -c Release -r win-x64`）
- `SafetyCheck` **926 PASS / 0 FAIL**（本阶段 **+92**，新增三节）：
  - 「两级自动识别：起点、三级手动、脱敏片段、失败不缓存」：起点与层级、入口同级不叠加、
    入口祖先可铺而入口停、平台容器不越过两级、当前文件夹范围只含直接子目录、
    计划/预算同源、默认不发完整路径、脱敏（Key/Token/Bearer/URL 账号/用户名）、
    白名单（放行 vs 拒绝 `secrets.json`/`.env`/`id_rsa`）、真实临时目录读片段 + 跳过计数 +
    只读直接子级、缓存指纹、预算/并发/超时/间隔、失败与取消分开、服务源码级断言
  - 「入口覆盖与识别覆盖」：真实入口表、**AppData\Local · Roaming 不被 Users 吞掉**、
    Program Files / (x86) / ProgramData 都在、祖先不占名额、无重复祖先/后代对、
    显示上限与材料化上限两条线、未列出条目如实计数且不算已识别、状态映射五档、
    断网/超时/预算/未配模型如实提示、脱敏再钉一遍
  - 「真实临时目录」：真建 `Game\steam\config\userdata` 与 `Local\app0\config` 等，
    验证两级自动铺开、**三级一个对象都不自动创建**、入口直接应用目录同级、容量统计不变
- `UiRegressionCheck` **205 PASS / 0 FAIL**（本阶段 **+29**）：行里没有逐项识别按钮、
  唯一批量入口是统一重试、扫描后自动开始、自动范围只收一二级、当前文件夹工作区四件套、
  只分析直接子目录、范围与请求数写清、进度含 已处理/待处理/失败、扫描中不启动识别、
  默认不发完整路径/清单、长度上限、脱敏、白名单、不出现「最近使用」、纠正不改 Kind、
  集合检测无具体例子、三级不进自动队列、取消/代次隔离、进度不重复计数、
  入口不被祖先吞、识别覆盖与行数上限解耦、未列出如实报告
- `CleanAnalyzerCheck` / `AiNoteParserCheck` / `AppRecommendationCheck` 全过；`git diff --check` 干净

**6b) 真实盘证据（只读遍历，未删任何文件；用一次性证据程序跑完即删）**

```
解析到的系统入口: 6
   C:\Program Files / C:\Program Files (x86) / C:\ProgramData
   C:\Users\<用户>\AppData\Local / C:\Users\<用户>\AppData\Roaming / C:\Users\<用户>
```
- **`AppData\Local` 与 `AppData\Roaming` 都在表里** ⇒ 祖先吞入口的问题在真机上确已修掉。
- 脱敏抽样（合成文本）：命中=4，自检通过=True，可出站=True；
  `{"apiKey":"[已脱敏]","password":"[已脱敏]"} TOKEN=[已脱敏]`。
- 预算实测：snippet 文件 ≤3 / 单文件 600 / 合计 1200；maxInput=1800；maxRequests=60；
  并发 2；超时 30s；请求间隔 250ms。
- `C:\ProgramData`：1,589 目录 / 4,538 文件 / 9.6 G，44 个直接子目录 ⇒ 全部落在第 2 级自动识别范围；
  **177 个三级候选一个都不会自动处理**；未列出 0。
- `C:\Program Files`：15,491 目录 / 163,018 文件 / 39.3 G，44 个直接子目录 ⇒ 同上；
  **132 个三级候选不自动处理**；未列出 0。

**7) 打包**
- 版本 **2.7.0**；self-contained win-x64。
- 产物：`dist/DashaoHuo-2.7.0-20260913-win-x64/`（546 个文件，含 `sidecar/AiSidecar.exe`、
  `THIRD-PARTY/`、`SteamHelper.exe`、`StoreAppHelper.exe`），`AiDiskCleaner.exe` 版本 **2.7.0.0**；
  另有 `.zip` 与 `.zip.sha256`。发布产物里的 `AiDiskCleaner.dll`
  与 `bin\...\win-x64` 构建输出**哈希一致**（`2A574313…`），确认打的就是本次代码。

**8) 已知限制 / 未验证（如实说明）**
- **未接通真实 AI**：没有任何真实模型调用，`Failed / 待确认 / AI 推测` 的**真机表现没有实拍**，
  只有离线断言（状态映射、失败分类、不缓存失败、脱敏、输入上限）覆盖。
- **没有 UI 实拍**：本机上 `AiDiskCleaner.exe` 带 `requireAdministrator`，
  非管理员 shell 启动后**无法枚举 / 截图 / 结束**该进程（实测 `Stop-Process`、`taskkill`
  都是 Access Denied，只能在**提权**子进程里才杀得掉；杀之前窗口只报 159×27，截不到内容）。
  所以默认窗口 / 窄窗口 / 高 DPI 下的**实际排版、换行、列宽观感本轮没有实拍**。
  已做的：主内容区宽度驱动的列优先级（<780 收窄操作列、<620 隐藏容量列，名称/用途永不隐藏）、
  虚拟化与无嵌套滚动、工作区条两行固定布局（省略号 + 悬停全文）由断言锁定。
  **编译通过不等于 UI 验收。**
- 单个目录最多材料化 400（顶层）/ 200（子层）个对象；超出的如实标「未列出（未识别）」，
  本轮**没有做「再显示更多」按钮**。
- 非管理员模式下清理仍不可用（`RecursiveScanService` 不设 `FileEntry.Parent`），本轮未动。

**9) 过程中的坑（重要）**
- **提权实例锁住产物目录**：从 `dist` 启动的带 `requireAdministrator` 实例，在非管理员 shell 里
  杀不掉，`dotnet publish` 写 `sidecar\AiSidecar.exe` 会 Access Denied。教训同上一轮：
  **冒烟测试从 `bin\...\win-x64\` 或临时副本启动，不要从 dist 里启动**；本轮的绕法是先杀干净
  （用一次提权 `powershell -Verb RunAs -Wait` 跑 `Stop-Process`）再 publish。
- `tools/UiRegressionCheck/Run.ps1` 是 **UTF-8 with BOM**：`edit` 工具改写后 BOM 会掉，
  PowerShell 5.1 会按 GBK 读，中文注释把引号吃掉、整个脚本报语法错。**每次改完都要补回 BOM**
  （本轮掉了两次）。
- 测试里用 `AppContext.BaseDirectory` 往上找源码时，`Path.Combine(dir, part[0], part[1], part[2], name)`
  遇到 2 段的路径数组会 `IndexOutOfRange` —— 用 `Path.Combine(new[]{dir}.Concat(part).Append(name).ToArray())`。

### 2026-09-12  DDWking（第二十三阶段：文件夹整理成为主工作区 · v2.6.0）
> 问题：上一轮的用途识别只挂在**默认收起的侧栏目录树**上 —— 用户看不到主流程变化，
> 首页主体还是清理候选，仍要逐个点目录。本轮**先把已有实现找出来复用**，再补它缺的那一层。

**1) 先查现状：已有的东西一件没重写**

| 已有 | 位置 | 本轮怎么用 |
|---|---|---|
| 本地摘要（容量/类型/代表文件/程序信息，有上限） | `Services/FolderPurpose.cs` `Summarize` | 直接复用，另加「直接子目录名样本」用于结构判断 |
| 自适应下钻判定（收纳继续 / 具体对象停 / 平台例外） | `ClassifyKind` + `FolderPurposeService.ShouldDescend` | 直接复用，没有复制第二套规则 |
| 用途识别编排（用户纠正→缓存→本地→AI，预算/超时/取消） | `Services/FolderPurposeService.cs` | 直接复用；补了「纠正落盘」与「路径键」 |
| 缓存与状态机 | `TryGetCached` / `PurposeState` | 直接复用 |
| 侧栏树的行内识别按钮 | `MainWindow.xaml.cs`（第 22 阶段） | **保留不动**，只在侧栏当导航用 |

结论：缺的是「主内容区的工作区 + 批量入口 + 状态表达」，不是识别本身。

**2) 改了什么**

- **导航三页**：`RightTab { Organize, Clean, Uninstall }`，默认 `Organize`；侧栏仍默认收起，
  只负责导航；清理与卸载完全保留（清理页的候选空间与执行按钮**一个都没搬过来**）。
  `ShowRightTab(RightTab.Clean)` 只剩「按 AI 建议勾选后回到清单」那处显式跳转。
- **新增 `Models/OrganizeNode.cs`**：对象与**真实目录一一对应**（`FullPath` 就是磁盘路径），
  只有用途与容量字段，**没有** Risk / CanDelete / Selected；缩进只影响展示（不引用 WPF 类型）。
- **新增 `Services/FolderOrganize.cs`**（纯函数、可离线测）：
  - `BuildRoots`：先收**系统解析出来的识别入口**（Program Files / Program Files (x86) /
    ProgramData / AppData Local·Roaming / 用户目录），再收盘符下其它直接子目录，
    互为祖先/后代的**只留最外层**（去重），重解析点跳过并计数；
  - `ShouldAutoExpand`（默认两层，收纳/混合才继续）与 `ShouldAutoFollowEntryPath`
    （**系统入口路径例外**：`…\AppData\Local` 在好几层下面也照样铺到，**不按距盘符固定层数截断**）；
  - `DirectChildDirs`：每层预算 24、按容量降序、截断了如实标注；
  - 预算：`MaxRoots=40`、`ChildBudget=24`、`AutoLevels=2`、`UnknownProbeBudget=8`、
    `MaxEntryDepth=8`、`MaxRows=4000`；**没有**任何删除/移动/提权入口。
- **新增 `MainWindow.Organize.cs`**（工作区，不是说明页）：
  - 扫描完成即建对象并**本地自动认出**（不需要逐项点按钮）；
  - 主列＝名称（缩进+展开箭头+次行真实路径）/ 用途（结论+来源·依据）/ 容量 / 必要操作；
  - 顶部一行页头（**只有一个标题**，统计与操作同行，顶部空白压紧），
    批量入口「识别这些文件夹」旁边写清 **范围 / 数量 / 请求预算**，跑起来有进度条与「取消识别」；
    单项「识别」「深入识别」走**同一条路径**；
  - 「待确认」筛选把满屏「未识别」收成一处；
  - 状态齐全：未扫描（有扫描入口）/ 扫描中 / 空结果 / 未配置模型（给「打开设置」）/ 失败 / 全部识别完；
  - 排队/处理中**只显示状态词，绝不显示成功结论**；预算用完会说明「已到请求上限，剩下保持待确认」。
- **复用同一识别入口**：`RecognizeWithAsync(dir, allowAi, ct)`（原来 `RecognizeOneAsync` 只是它的壳），
  侧栏树与整理页共用；`allowAi:false` 时**一个请求都不发**。
- **系统入口按系统路径解析**：`FolderPurposeRules.EntryPoints()`（`Environment.GetFolderPath`），
  一次解析给整页复用；识别不了就跳过（`skipped` 计数进页头，**不自动提权**）。
- **规则补齐（缺口，不是重写）**：
  - `RecognizeLocally` 增加入口表参数（避免逐行碰磁盘）与「已知内部目录」（`.git` / `node_modules` /
    `winsxs`…）本地结论；`DevProjectMark` 同时看**文件与目录**结构标志；
  - `ParseAi` 把 `PURPOSE: 未知 / unknown` 判为**没有结论**（提示词本来就允许模型这么答，
    以前会被当成识别成功）；
  - 用户纠正改成**路径键**并写盘（`AppPaths.FolderPurposeFile`），换扫描代次、重启都还在。
- **代次守卫**：每次重建 +1（`_organizeGeneration`），请求前后都校验
  `myGen/代次 + _aiDataGeneration`；列表每代只建一次；摘要变化（大小/文件数/时间/配置签名）
  让服务缓存键失效；**展开不重复请求**（子对象材料化过就复用）。

**3) 安全边界（本轮一次都没碰）**
- 识别不改 `Risk` / `CanDelete` / `Selected`，不自动选择、不删除、不移动、不归档；
- 不放宽非管理员清理规则；清理仍是「选择 → 预览/预检 → 确认」；
- 「很久没动」不当作「很久没用」，不据此删除；
- 「在资源管理器中打开」仍只走 `ShellReveal`（只开目录，绝不执行里面的程序）；
- 切换工作区不影响选择与统计口径（整理页没有任何选择字段）。

**4) 测试结果（全部离线 / 真实盘只读）**
- Release 编译 **0 错 0 警**（`dotnet build -c Release -r win-x64`）
- `SafetyCheck` **739 PASS / 0 FAIL**（本阶段 **+49**，新增「文件夹整理」一节）：
  顶层对象去重与上限 / 默认两层与具体对象停止 / 未知有限探索预算 / 平台保留内部游戏入口 /
  工程结构标志 / 容量与分类分离 / **深层系统入口不被层数截断** / 重解析点跳过并计数 /
  缓存指纹 / 用户纠正跨代次与落盘读回 / 「未知」不算结论 / 排队不显示成功结论 /
  行数与子对象预算 / **对象里没有删除·风险·选择字段、服务里没有删除·移动·提权入口**
- `UiRegressionCheck` **176 PASS / 0 FAIL**（本阶段 **+23**）：默认页、三页顺序与枚举、
  整理页是真实工作区、四列表头、真实路径次行、不复制清理按钮、批量入口四件套、
  五种状态、代次守卫、本地先于模型、未配置模型不发请求、纠正落盘、两层与深入口、
  平台游戏入口、预算、链接跳过、虚拟化且无嵌套滚动
- `CleanAnalyzerCheck` / `AiNoteParserCheck` / `AppRecommendationCheck` 全过；`git diff --check` 干净
- **真实盘运行证据**（本机 C: 只读扫描，未删任何文件）：
  `app.log` 里 `[Organize] op=build roots=22 objects=164 rows=164 entries=6 skipped=2`，
  扫描 1,582,899 文件 / 287,433 目录；展开/收起序列的行数自洽（164→140→112→88→64→40→35，再展开 +24→59），
  全程**没有一条 ERROR / 异常**（`quality` 行显示 MFT 源、reparse=650）。
  两次发布产物冒烟（`dist` 里的包 + `bin\...\win-x64` 里的最终构建）都正常起窗、扫描并建出整理页。

**5) 打包**
- 版本 **2.6.0**（`AiDiskCleaner.csproj`）；工作区里 2.3.0→2.5.0 的未提交改动属于上一轮用途识别
  （第 22 阶段），和本轮同一个特性线、编译也依赖它，所以这一次一起提交。self-contained win-x64。
- 产物：`dist/DashaoHuo-2.6.0-20260912-win-x64-r2/`（546 个文件，含 `sidecar/AiSidecar.exe`、
  `THIRD-PARTY/`、`SteamHelper.exe`、`StoreAppHelper.exe`），`AiDiskCleaner.exe` 版本 2.6.0.0；
  另有 `.zip` 与 `.zip.sha256`。`-r2` 后缀的原因见下面第 7 条。
- 发布产物里的 `AiDiskCleaner.dll` 与构建输出**哈希一致**（6B9985E9…），确认打的就是本次代码。

**6) 已知限制 / 未验证（如实说明）**
- **未配置真实 AI 模型**：批量识别里「请求模型 → 解析 → 待确认」这一路只有离线断言覆盖，
  真机上所有结论都会是「本地识别」或「未识别」；`未配置模型` 状态是代码路径 + 断言支撑。
- **没有做视觉验收**：本会话没有截图工具（也没有屏幕/DPI 输入能力），
  所以 1000×700、860×560 DIP 与高 DPI 下的**实际排版、换行、列宽观感没有实拍**。
  已做的：主内容区宽度驱动的列优先级（窄到 780 收窄操作列、窄到 620 隐藏容量列，
  名称/用途两列永不隐藏）、虚拟化与无嵌套滚动由断言锁定。
  **编译通过不等于 UI 验收。**
- 单个目录只列 24 个子对象（可 `ChildBudget` 调），超出的会写明「只列出 24 / N」但没有「再显示更多」按钮。
- 用户纠正按**完整路径**记；目录改名/移动后这条纠正就对不上了（按设计如此，没有做路径跟随）。
- 侧栏目录树那一套（第 22 阶段的实现）保持原样，仍是**按需点击**、不做批量。

**7) 过程中的坑（重要，下次别踩）**
- **两个提权实例把我的产物目录锁死了**：为了做真实盘冒烟测试启动的 app 带
  `requireAdministrator`，之后从**非管理员** shell `Stop-Process` / `taskkill` 全是 Access Denied，
  `Remove-Item`、`Rename-Item`、`dotnet publish` 都被 `AiDiskCleaner.dll` 占用挡住。
  最后改用**新的产物目录名 `-r2`** 才把这一轮代码打出去。教训：冒烟测试要从
  `bin\...\win-x64\`（或临时副本）启动，**不要从 dist 里启动**。
- `tools/UiRegressionCheck/Run.ps1` 是 **UTF-8 with BOM**：`edit` 工具改写后 BOM 会掉，
  PowerShell 5.1 会按 GBK 读，中文注释把引号吃掉、整个脚本报语法错。改完记得补回 BOM。
- PowerShell 里跨 `(` 续行时，**行尾必须落在运算符上**；行尾是字符串/右括号时不会续行。

### 2026-09-12  DDWking（第二十二阶段：文件夹用途识别（本地摘要与边界 + 服务与缓存）· v2.4.0）
> 本阶段目标是「本地摘要与边界 → 识别与缓存」，**UI 接入未完成**，如实标注在下面。

**1) 做了什么**

新增三个东西（都可完全离线测试，不碰磁盘、不调模型）：

- `Services/FolderPurpose.cs`
  - `FolderKind`：**未知 / 收纳(Container) / 具体对象(Concrete) / 混合(Mixed)**。
    这不是「两层或三层硬限制」，而是**要不要继续往下看**的依据 —— 层级由目录性质决定。
  - `PurposeSource`：本地 / AI / 用户 —— **来源如实展示**，不把本地判断说成 AI。
  - `PurposeState`：未识别 / 排队 / 识别中 / 已识别 / 待确认 / 失败；
    **没有结论时不允许显示成「已识别」**（`HasConclusion` 为假 ⇒ `State` 是 `Unrecognized`）。
  - `FolderId` = 完整路径 + 扫描代次（稳定标识，不依赖显示名，不接受 AI 返回的路径）。
  - `FolderSummary`：目录名、相对结构、**容量（整个子树）**、类型分布、代表文件名、
    产品信息、修改时间；样本数与类型数都有上限，超出标 `Truncated`。
    **用途分类与容量统计分开**：停止分类不影响容量。
  - `FolderPurposeRules`：本地识别 + 自适应下钻判定
    - `.git` / `node_modules` / `winsxs` / `obj` / `.venv` … 等 **`KnownLeafDirs`** ⇒ 具体对象，**不再下钻**
      （否则会一头钻进几万个子目录）；
    - **平台容器例外**：`steamapps\common`、`steamlibrary`、`Epic Games`、`XboxGames` …
      认出平台后**仍然允许往里找游戏**（`IsObjectContainer` + `ShouldDescend`）；
    - 开发项目只看**结构标志**（`.git` / `.sln` / `.csproj` / `package.json` / `Cargo.toml` / `go.mod`），
      **不读文件内容**；
    - 系统入口只说明「这是系统位置」，并标 `NeedsConfirm`，**不是清理结论**；
    - 判不出来就返回 `None`（允许未知与混合，不硬给结论）。
  - `EntryPoints()`：Program Files / Program Files (x86) / ProgramData /
    AppData\Local / AppData\Roaming / 用户目录，全部走 `Environment.GetFolderPath`，
    **不硬编码 C 盘或用户名**；解析不到就跳过。
  - `BuildAiInput`：有上限的摘要（`MaxInputChars = 900`），不含整盘清单，相对路径可脱敏。
  - `ParseAi`：只认 `PURPOSE/CATEGORY/BASIS` 三行；**空回复、答非所问、JSON 都判为没有结论**。
- `Services/FolderPurposeService.cs`
  - 预算硬限制：`MaxAiRequests=24`、`MaxConcurrent=2`、单次超时 40s、`MaxDepth=4`；
  - 顺序：用户纠正 → 缓存 → 本地规则 → （有预算且授权时）AI；
  - 缓存键 = 稳定标识 + 摘要指纹 + 配置签名；`ResetForScan()` 清缓存与计数；
  - 取消/超时/异常**一律返回「没有结论 + 待确认」**，不缓存失败结果；
  - `SetUserCorrection` / `TryGetUserCorrection` —— **用户纠正单独存放，重扫不清**。
- `MainWindow`：接入服务实例，并在重建分层结果时 `ResetForScan()`。

**2) 测试结果**
- Release 编译 **0 错 0 警**
- `SafetyCheck` **690 PASS / 0 FAIL**（本阶段 +42），覆盖：
  收纳目录继续下钻 / 具体对象停止 / **平台认出后仍可找内部游戏** /
  开发项目只凭结构标志 / 未知目录不给结论 / 摘要容量仍是整子树且样本与类型有上限 /
  稳定标识含代次且同名不同路径不同 / 缓存命中与重扫清空 /
  **用户纠正重扫后仍保留** / AI 空回复·答非所问·格式错误都不产生结论 /
  来源不伪装 / 输入长度与请求数有上限 / **重解析点不跟随** / 深度到上限即停 /
  **识别不修改 Risk/CanDelete/Selected，且结论对象里没有删除相关字段** /
  系统入口标为待确认

**3) 过程中发现并修掉的两个真 bug（测试逼出来的）**
1. **会钻进 `node_modules`**：原先没有「已知对象内部目录」概念，`.git`／`node_modules`／
   `winsxs` 这类会被判成「未知」并继续下钻 —— 在真实盘上等于几万个子目录。
   已加 `KnownLeafDirs` 判为具体对象并停止。
2. **重扫把用户纠正一起清了**：`ResetForScan` 原来 `_cache.Clear()` 把用户确认的结论也清掉，
   违反「用户纠正优先保留」。已改为用户纠正**单独存放**，重扫不动它。

**4) UI 接入（本轮补齐）**

- 文件浏览区（侧栏 `DirTree`）**每一行名称下方显示用途**，格式为
  `用途 · 依据（来源）`，来源写清是**本地识别 / AI 推测 / 你确认的**。
- 行内一个图标按钮（`IconScan`）：**识别用途 → 深入识别**，再点一次 = 取消；
  有 Tooltip 与可访问名称。**按需触发**，不自动铺开整棵树。
- 深入识别有预算：最多 `PurposeChildBudget = 12` 个直接子目录，
  且只在 `FolderPurposeService.ShouldDescend` 判定「该往下看」时执行
  （具体对象/`.git`/`node_modules` 不会下钻；游戏库容器会）。
- 右键菜单新增「识别用途」与「纠正用途」（九类，点一下即可，不需要输入框）。
- 状态如实表达：未识别 / 排队中 / 识别中 / 已识别 / 待确认 / 识别失败 / 已停止；
  **没有结论时只显示「未识别」**，绝不显示成功结论。
- 树重建时丢弃旧的行引用（`ResetPurposeRows`），已识别结果保留在 `_purposeCache`。
- **实机验证**：截图 `docs/shots/80-browser.png` 可见侧栏每行都有「未识别」+ 识别按钮，
  `SteamLibrary` / `Program Files` / `ProgramData` / `迅雷下载` 等都在列。

**5) 仍未完成 / 未验证（如实说明）**
- ❌ **没有截图证明「点击识别后显示出结论」**：验证脚本在点按钮之后的枚举步骤出错，
  只抓到点击前的状态。**识别后的 `用途 · 依据（来源）` 渲染没有实拍**，
  该路径由断言（`RenderPurpose` 写入 `PurposeSourceText`）与手工代码审查覆盖。
- ❌ 没有在真实盘上跑完整端到端；功能断言基于**内存构造的目录树**。
- ❌ 没有做真实 AI 调用（需授权）⇒ **AI 那一路（超时/空响应/格式错误/取消）只有离线断言**，
  真机上所有结论都会是「本地识别」或「未识别」。
- ❌ 未验证：权限不足目录、真实链接循环、海量文件下的响应、默认/窄窗口与高 DPI 下的显示。
### 2026-09-12  DDWking（第二十一阶段：详情列表改「组头共同说明 + 折叠」 · v2.3.0）
> 问题：文件行反复显示同一句说明 → 说明占了固定列宽 → 文件名被挤到截断，
> 页面标题/数量/空间重复展示。本轮只改展示。

**1) 根因**

- 详情列里有一个**常驻的「说明」列**（`ColCleanWhy`），逐行显示 `CleanItem.NoteText`。
  同一位置里几十上百行的说明**完全相同**（例如「各种程序运行时的临时文件…」），
  既没有增量信息，又固定占走一块宽度 → 文件名只能拿到剩下的。
- 页面标题重复：整页模式下**页头**已有「位置名 + 本位置共 N 项」，**详情面板内**又写了一遍。
- 原有分组是按 `RiskGroupKey`（风险档）分的，组头只有「需要确认（42 项 · 870 KB）」，
  **共同说明没有地方可放**，只能逐行重复。

**2) 改法**

- **分组键**：`CleanItem.DetailGroupKey` = 风险档 + `Risk` + `CanDelete` + `Handling` + `Group` + **完整说明文本**。
  用完整文本参与比较（不是截断文字），风险/可删性/处理方式都进键 ⇒
  不同风险或不同删除影响**不可能被合并**；「截断后看起来一样」也不会误合并。
- **组头**：新增 `DetailGroupHeader(Title, Stat, Note, SelectedText)` 与
  `DetailGroupHeaderConverter`。组头一次性显示 **标题 + 数量·空间 + 共同说明**，
  有选择时再显示 **已选 N · X**。共同说明限制两行（`NoteClamp`），超长给 ToolTip 全文。
- **组标题**：`DetailGroupTitle` = 用途名（`CleanPurposes.Name`）→ 规则分组名 → 「其它文件」。
  **不出现「未标注」这类技术标签。**
- **删掉常驻「说明」列**，文件名因此吃满剩余空间（`Width="*"`）。
  行内只保留：勾选、文件名、大小、AI；类型列仍按宽度阈值（≥620）整列显隐，不压成残缺笔画。
- **折叠**：沿用标准 `Expander`（键盘可用、有箭头）；
  `DetailGroupExpandedConverter` 按**分组键**记忆展开/折叠（`Expanded`/`Collapsed` 两个集合），
  所以重新绑定、搜索、翻页后状态不丢。默认：单组展开、多组只展开第一组
  （由 `MarkFirstGroupExpanded` 写入，不依赖 `CollectionViewGroup.Parent`）。
- **折叠只改展示**：`DetailGroupToggle_Click` 只动状态集合再 `BindDetailPage()`，
  不碰 `Selected`/`Risk`/`CanDelete`。本轮**没有**加组级批量选择按钮。
- **搜索**：`ExpandAll = pager.Search.Length > 0` ⇒ 命中的组一律展开；清空搜索恢复之前的折叠状态。
- **统计口径**：`BuildDetailGroupStats(pager.Matches)` 按**完整过滤结果**算每组的
  数量/空间/已选数，**不是已加载的那一页** ⇒ 折叠组的数字是真全组总量，且一次线性遍历、
  不创建任何 UI 控件。「已加载 X / 命中 Y」仍由 `pager.StatusText` 单独说明，
  三个口径（总数 / 命中 / 已加载）互不冒充。
- **整页标题去重**：`UpdateDetailLayout` 在整页模式下隐藏详情面板内的标题与副标题
  （页头已经有一套），分栏模式保留标题与关闭图标。

**3) 没做的事（守边界）**
- 没有改扫描引擎、清理规则、风险、清理资格、保护规则、删除范围。
- 没有调用 AI 做文本合并，没有修改原始候选数据。
- 没有新增组级批量选择；没有重做窗口。

**4) 测试结果**
- Release 编译 **0 错 0 警**（顺手删掉了不再被引用的 `CleanGroupConverter`）
- `SafetyCheck` **648 PASS / 0 FAIL**
- `UiRegressionCheck` 全过（本轮 **+10**：共同说明只在组头、分组键含完整说明+风险+处理方式、
  组标题不出技术标签、组统计来自完整过滤集而非当前页、单组默认展开、
  折叠只改展示、折叠状态按键记忆、搜索展开+清空恢复、
  行内只保留勾选/名称/大小/AI、整页隐藏重复标题）
- `CleanAnalyzerCheck` / `AiNoteParserCheck` / `AppRecommendationCheck` 全过；`git diff --check` 干净

**5) 界面验证边界（如实说明）**
- ❌ **本轮没有实拍截图。** 环境里的沙箱会在命令结束时回收子进程，
  应用实例多次被提前终止；这次也没有重建出「启动→等扫描→导航→截图」的完整链路。
- ✅ 因此下面这些**只有静态断言与代码路径支撑**，没有界面验收：
  组头两行说明的实际换行效果、折叠动画与键盘操作手感、
  分组数量在数千文件下的实际渲染性能、默认/最小窗口与不同 DPI 下的裁切情况。
- ✅ 可确定的部分：分组键与统计口径由 10 条断言锁定；`DetailGroupKey` 用完整说明文本，
  所以「截断后相同」不会被合并；折叠不改任何数据字段（断言锁定只调 `BindDetailPage`）。
- 未在 100%/150% DPI 下实测。
### 2026-09-12  DDWking（第二十阶段：修文件详情表列布局 · v2.2.1）
> 现象：「类型」列文字被裁切。**先量了实际配置再改**，没有靠缩字体或加 Tooltip 掩盖。

**1) 根因（两条，都是实测出来的）**

1. **表头与单元格内边距不一致**：`DataGridColumnHeader.Padding="14,10"` vs `DataGridCell.Padding="8,0"`
   ⇒ 表头文字比行内容右移 6px，同列上下不对齐，可用宽度也被吃掉。
2. **类型列被自己的「优先级」逻辑整列关掉了**：上一轮我写的
   `showType = width >= 620`、`showWhy = width >= 820` 在窄窗下把类型**整列隐藏**，
   加上列宽写死 88（未含表头/单元格留白），于是要么看不到、要么被压。
   我自己的截图里 `类型` 列根本不出现 —— 这就是「残缺文字」的来源。

**2) 改法（对应 7 条要求）**

| 要求 | 做法 |
|---|---|
| 1 类型列完整显示 | 列宽 **64**（表头「类型」11px ≈24 + 表头留白 16 + 单元格留白 16），加 `MinWidth=64`、`CanUserResize=False` |
| 2 复选框独立占列 | `ColPick` 固定 34 / MinWidth 34 / 不可拖拽，不参与星号分配 |
| 3 表头与行对齐 | 表头 padding 改成 **8,8**，与单元格 8,0 横向一致 |
| 4 稳定宽度 + 分配顺序 | 勾选 34 / 类型 64 / 大小 78 / AI 118 固定；剩余空间 **文件名 `2*`**，其次 **说明 `1*`**（原来是说明 2*，比文件名还宽） |
| 5 右侧空白 + 截断 | 固定列合计降到 294（原 342），星号列拿满剩余；`HorizontalScrollBarVisibility=Disabled` 保持单滚动区 |
| 6 窄窗隐藏类型 | `< 620` 时**整列隐藏**，同时把说明列标题改成「类型 · 说明」、绑定切到 `CleanItem.NoteWithGroup`，**类型信息不丢**；未缩小任何字体 |
| 7 多布局实测 | 见下方「验证」；**未能截到类型列可见的详情截图**，如实标注 |

新增 `CleanItem.NoteWithGroup`（「类型 · 说明」），并在布局时切换 `ColCleanWhy.Binding`。

**3) 未做 / 未改**
- 没有改文件分类、风险、清理资格或选择状态（只动列宽、内边距、绑定与显隐）。
- 没有缩小全局字体，没有用 Tooltip 代替完整显示。

**4) 测试结果**
- Release 编译 **0 错 0 警**
- `SafetyCheck` **648 PASS / 0 FAIL**（沿用上一轮；本轮为纯展示改动，未新增断言）
- `UiRegressionCheck` 全过（本轮 **+8**：表头/单元格内边距一致、复选框列固定且不可拖拽、
  类型列 64+MinWidth、大小/AI 稳定宽度、文件名 2* 优先于说明 1*、
  隐藏类型时把类型折进说明列、类型整列隐藏而非压缩、未缩小字体）
- `CleanAnalyzerCheck` / `AiNoteParserCheck` / `AppRecommendationCheck` 全过；`git diff --check` 干净

**5) 验证到什么程度（如实说明）**
- ✅ 列宽/内边距/绑定/显隐规则**由 8 条断言锁定**，含「表头与单元格内边距必须一致」这条。
- ✅ 代码层面确认：1280 物理（1024 DIP，侧栏收起）时详情为主内容区整页，
  `ApplyDetailColumnPriority(1024)` ⇒ `1024 ≥ 620` ⇒ **类型列可见**且宽 64；860 DIP 时类型整列隐藏、
  说明列改为「类型 · 说明」。
- ❌ **没能截到「类型列可见」的详情截图**。原因：本环境的沙箱会在命令结束时回收子进程，
  应用实例反复被提前终止；用后台任务把实例留住后，我的点击序列又没能走到详情
  （脚本抓到的是首页）。所以**「系统」二字完整显示这一条只有规则+断言支撑，没有本轮截图**。
- ❌ 未在 100%/150% DPI 下实测；未在侧栏展开状态下实测（只按代码推算宽度扣减）。
### 2026-09-12  DDWking（第十九阶段：修详情布局 + AI 空白结果 + 定位失败 · v2.2.0）
> 三个现象，**两个是同一个根因**：文件级 AI 分析从来没有生成结论。

**1) 根因（先复现再改，不是猜的）**

| 现象 | 根因 |
|---|---|
| 右侧详情过窄、列被截断 | 停靠宽度只有 `clamp(340, avail*0.46, 480)`，而固定列 32+88+72+150=**342**，星号列被压到 MinWidth 以下 |
| AI 展开后空白、只有按钮 | `RunItemAiAsync` 用 `ResolveLocationNode(scopeKey)` 取条目，**文件级 scopeKey 是文件路径，永远查不到** → `Verdict` 为 null → `Headline` 空 |
| 「找不到这一组对应的位置」 | 同一个原因：`AiViewBucket_Click` 也只用 `ResolveLocationNode`，文件级必然返回 null |

`crash.log` 没有新异常 —— 不是崩溃，是**数据没算出来**。

**2) 详情布局**

- 判定改成用**主内容区真实宽度**（`CleanMain.ActualWidth`），侧栏停靠时自动扣掉 280+6；
  未排版（ActualWidth=0）时按窗口宽度估算，避免误判成宽屏而把详情压窄。
- 分界从 820 提到 **1080**，停靠宽 `clamp(560, avail*0.54, 760)`：**两侧都够用才分栏**，
  否则详情占满主区域（顶部返回箭头回列表）。
- 新增 `ApplyDetailColumnPriority(width)`：**勾选(34)/文件名(*,min120)/大小(78)/AI(118) 永远保留**；
  类型 ≥620 才显示、说明 ≥820 才显示 —— 次要信息让位，必要操作绝不被裁切。
- 关闭用 × 、返回用箭头（都有 Tooltip 与可访问名称）；保留虚拟化；只挂一个 ScrollViewer。

**3) AI 结果**

- 新增 `ItemsForScope(scopeKey)`：位置级取该位置条目，**文件级就是那一个文件**（`FindItemByPath` 按完整路径）。
  结论、说明、下一步因此全都成立。
- 「请求结束」与「有可用结论」分开：文件级且结果为空/解析失败 → `NoUseful` + 「未能生成分析结果，可以重试」，
  **不再因为请求结束就显示「查看结果」**。
- 依 `HasVerdict`/`IsStale` 决定显示，过期不显示旧结论，也不出现「为什么这样建议？」。

**4) 定位**

- `ResolveLocationForItem(item)` 按**对象身份**（`ReferenceEquals`）反查所属位置，
  不依赖显示名/截断路径/模型给的路径。
- 文件级 → 只定位该文件；分组级 → 只显示该组真实关联的条目（`SetItemFilter` 精确集合过滤）。
- 定位失败**不改页面、不改选择**，只给一句可读原因，并把 `CanViewFiles=false` 让按钮消失（不留必然失败的按钮）。

**5) 重新扫描后过期**

- 新增独立代次 `_aiDataGeneration`（**不复用扫描任务守卫 `_scanGeneration`**，避免破坏扫描的旧任务丢弃逻辑）。
- 每次重建分层结果就 +1，并把**已创建**的逐项 AI 视图标记 `IsStale`（惰性创建的条目视图不受影响，30 万候选也不会批量建对象）。
- 过期后：不显示旧结论、`CanSelect`/`CanViewFiles` 都为 false；用户点分析时先复位再重算。

**6) 措辞按 §五 收紧**

- 「可直接清理」→ **「可考虑清理」**。
- 删掉没有依据的模板句「暂未发现正在使用的内容」→ 改为陈述事实的
  「这些文件命中了本地的缓存/临时规则，程序通常会自己重建」。
- 依据里**如实加上「没有检查文件是否正在被占用」** —— 我们确实没做占用检查，不能拿它当安全依据。

**7) 验证结果**
- Release 编译 **0 错 0 警**
- `SafetyCheck` **648 PASS / 0 FAIL**（本轮 +9：文件级结论非空、文件级受保护仍不给选择、
  「可考虑清理」措辞、说明不写没检查过的结论、依据含「没有做占用检查」、不出现「安全」保证）
- `UiRegressionCheck` 全过；`CleanAnalyzerCheck` / `AiNoteParserCheck` / `AppRecommendationCheck` 全过
- `git diff --check` 干净

**8) 真机验证（实际启动 WPF，走完整链路）**
用 `DASHAOHUO_CONFIG_DIR` 指向临时配置得到干净实例（不动真实配置、不耗额度）：

打开位置 → 看清文件 → 分析一项 → 看到非空结论 → 查看对应文件 → 返回，**全部实测通过**：
- **AI 面板非空**：`这里暂时没有建议清理的内容` + `这些文件属于 node_modules。` +
  `这里没有配置 AI，但你仍然可以自己查看文件并手动选择。` + 按钮 `查看文件` + 折叠 `为什么这样建议？`
- **「查看文件」成功**：详情显示 `只显示这 42 项`（修复前这里是「找不到这一组对应的位置」）
- **详情布局**：860 DIP 下自动切成**整页**（返回箭头 + 关闭），列 `勾选/名称/大小/说明/AI` 全部可读、
  无截断，底部操作栏未被遮挡；1680 物理宽下恢复分栏
- 截图：`docs/shots/53-ai.png`（AI 结论）、`docs/shots/54-view-files.png`（定位成功）、
  `docs/shots/60-detail-default-1000.png`（窄窗整页详情）、`docs/shots/62-detail-wide-1680.png`（宽窗分栏）

> 抓图方法也修了：以前用 `CopyFromScreen` + `SetForegroundWindow`，被别的窗口遮住时会
> **抓到别人的窗口**（这次就抓到了一张游戏画面）。改用 `PrintWindow(..., 2)`，不受 z-order 影响。

**9) 未验证 / 已知限制（如实说明）**
- **没有做真实模型调用**（需授权）⇒ 截图都是「未配置 AI」状态；
  「成功返回模型结论」「信息不足」「空响应」「失败」「取消」「重试」这些**状态转移没有真机截图**，
  只由离线断言 + 代码路径覆盖。
- **非管理员模式 `selectable=0`** ⇒ 真机上「可考虑清理」永远是 0 项，
  因此**没截到三张分组卡**，也**没真机点过「选择这些文件」**（按钮不出现）。
- 「重新扫描后旧结果过期」只有代码 + 断言，**未真机复现**（需要重扫一次并观察同一位置的面板）。
- 侧栏展开/收起 × 三种窗口宽度的**组合**没有逐一截图；只验证了 860 / 1000(钳到860) / 1680 三档。
- 仅验证 125% 缩放；100%/150% 未实测。
- 验证脚本曾误点到「在资源管理器中打开」（`ClickName` 匹配到了文件夹按钮），
  可能弹出过资源管理器窗口 —— **没有执行任何删除**。
### 2026-09-12  DDWking（第十八阶段：AI 结果界面大幅简化 · v2.1.0）
> 本轮**不加信息，只做减法**。上一版 AI 面板要用户读一堆分组和技术词再自己推理，
> 用户看完仍然不知道「该勾哪些」。现在默认只给：**一句结论 + 一句说明 + 两个按钮**。

**1) 默认界面只剩三样东西**

```
node_modules                          空间未知  📁  [查看结果]  ›
  ┌ 建议清理其中 19 项，可释放 2.2 GB        ← 一句结论（带项数与空间）
  │ 这些文件属于 Windows 更新缓存，暂未发现正在使用的内容。  ← 一句说明
  │ [查看文件]  [选择这些文件]                ← 下一步
  │ ⊙ 为什么这样建议？                       ← 默认折叠，需要时才看
  └
```
实现：新增 `Services/AiVerdict.cs`（纯函数，不碰 UI、不调模型），
从位置的条目算出 `Headline / Note / Buckets / SelectableItems / Why / Mixed`。

**2) 删掉的技术性文字（§二）**

从主结果区彻底移除：`[本地规则]`、`[本地拆分]`、`[AI 结论]`、`[符合清理资格]`、
`[候选空间]`、`[按组拆分]`、`[未标注]`，以及 **token / 耗时 / 请求阶段 / 通道**等技术调试信息
（这些仍在 `app.log` 里，不再进界面）。
简短依据改到 **「为什么这样建议？」** 折叠区：文件位于哪、最后修改、匹配到的规则、
没有读取文件内容、还有多少项无法确认。

**3) 分组改成用户能懂的三种，且只有真混合才出现（§三/§七）**

- 名称固定为 **可直接清理 / 建议保留 / 需要确认**，由本地规则归类：
  可删且非保留非需确认 → 可直接清理；受保护或保留 → 建议保留；其余 → 需要确认。
- **`mixed = buckets.Count > 1`**：内容统一的位置只给一个结论，不为了「展示算法结果」硬拆三张卡。
  这也解决验收第 1 条（传递优化这类统一目录只显示一条简洁结论）。
- 每组只显示：名称、`N 项 · X`、一句说明、必要按钮。

**4) 按钮统一（§四）**

- 只保留两个动作：**查看文件** / **选择这些文件**。
  原来的「查看对应文件 / 选择本组候选 / 按组挑选」等措辞全部去掉。
- **「选择这些文件」只出现在「可直接清理」**；建议保留、需要确认、受保护、信息不足
  一律没有快捷选择按钮，`CanSelect` 由本地 `Eligibility` 决定。
- 没有「AI 一键清理」。

**5) 不重复堆叠空间数字（§五）**

- 顶部结论给**一个**总数（可直接清理的项数与空间）；分组各报**自己的** `N 项 · X`。
- 顶部显示的就是「可直接清理」那一档，不再同时出现「总空间/候选空间/符合资格空间」四个相近数字。
- 断言锁定：`SelectableItems.Count == CleanableCount` 且空间相等 ——
  顶部数字与按钮作用范围必须同源。

**6) 展开高度受控（§六）**

整块包在 `ScrollViewer MaxHeight="300"` 里**内部滚动**；
详细依据默认折叠；分组卡片紧凑；实测小窗口（860×560 DIP）下**不遮挡底部操作栏**。

**7) 顺手做的简化（不是新增功能）**

- 「查看文件」改成**精确条目集合过滤**：`CleanItemPager.SetItemFilter(items)`。
  以前靠搜索词过滤，一组条目散在多个子目录时表达不了；现在按对象身份过滤，只换视图不动选择。
- **删除了被取代的两套东西**：`Services/CandidateGroups.cs`、`Models/CandidateGroupView.cs`
  （按子目录/应用分组的旧方案），以及 `ItemAiPrompt.BuildUser/AnalyzeAsync` 的 `groups` 参数与
  `ParseGroupNotes`。留两套分组逻辑只会让以后更难改。
- 未配置 AI 时**本地结论照样生成**（`AiVerdict.Build(items, identity, null)`），
  给一行如实提示，查看与手动选择仍然可用。

**8) 保留的安全边界（§八）—— 全部未动**

`local rules 决定清理资格`、保护路径、`Risk/CanDelete/Selected` 不被 AI 修改、
AI 不扩大范围、不直接删除、最终仍走既有选择 → 预检 → 确认 → 执行链路。
「可直接清理」只是**界面上的建议分类**，`AiVerdict.IsCleanable` 仍要求
`CanDelete && Risk != Keep && Risk != Confirm`，与删除前预检同源。

**9) 验证结果**
- Release 编译 **0 错 0 警**
- `SafetyCheck` **639 PASS / 0 FAIL**（本轮把旧分组测试换成 34 条结论界面测试）
  - 统一目录不分组、混合才分组；三个分组名是普通用户的词且不含技术词
  - 只有「可直接清理」`CanSelect`；需要确认/建议保留均为 false
  - 受保护项永远 `!IsCleanable`；可选集合里没有受保护项
  - 全受保护 / 只有需确认时**不写「建议清理」**、不给一键选择
  - 顶部项数与空间 == 可选集合的项数与空间
  - 说明会补上模型给的删除影响，但**模型不改变可清理项数与可选集合**
  - 空列表不炸；结论区不含「本地规则/符合清理资格/候选空间/未标注/按组/token」
- `UiRegressionCheck` 全过（本轮 +12：默认三件套、三个分组名、混合才分组、
  只有可清理档能批量选、受保护永不可清理、按钮措辞、面板无技术词、
  依据默认折叠、面板限高内滚、走既有刷新链、精确过滤、无 AI 时本地结论可用）
- `CleanAnalyzerCheck` PASS · `AiNoteParserCheck` 全部通过 · `AppRecommendationCheck` All passed
- `git diff --check` 干净

**10) 真机验证（实际启动 WPF 截图）**
用 `DASHAOHUO_CONFIG_DIR` 指向临时配置目录得到**未配置 AI 的干净实例**（不动真实配置、不耗额度）：
- 实测面板文字：`这里暂时没有建议清理的内容` / `这些文件属于 node_modules。` /
  `这里没有配置 AI，但你仍然可以自己查看文件并手动选择。` / 按钮 `查看文件` /
  折叠项 `为什么这样建议？`
- **没有出现「选择这些文件」** —— 本机非管理员模式 `selectable=0`，无可清理项，
  与「只有可直接清理才给快捷选择」一致（真机印证安全边界）
- **没有出现任何技术词**（无 本地规则/按组/符合清理资格/未标注/token）
- **小窗口 860×560 DIP**（1075×700 物理）下底部操作栏未被遮挡
- 截图：`docs/shots/31-ai-narrow.png`

**11) 未验证 / 已知限制（如实说明）**
- **没有做真实模型调用**（用你的付费额度需授权）。因此「结论里补上模型给的删除影响」
  只由离线断言覆盖，**真机截图是「未配置 AI」那一种状态**。
- **没有真机验证「选择这些文件」实际勾选**：本机 `selectable=0`，按钮不出现。
  该路径由 34 条断言覆盖（数量、空间、只作用于可清理项、不碰受保护项），但端到端点击未做。
- **没有真机看到「可直接清理 / 建议保留 / 需要确认」三张分组卡**：
  本机没有可清理项，`mixed` 不成立。分组逻辑由合成数据断言覆盖。
- 未覆盖：取消/超时/重试/重扫失效的真机表现；「为什么这样建议？」展开后的实际排版。
- 仅验证 125% 缩放；100%/150% 未实测。
- 非管理员模式清理仍不可用（`RecursiveScanService` 不设 `FileEntry.Parent`），本轮未动。
### 2026-09-12  DDWking（第十七阶段：AI 从「解释目录」改成「按组挑文件」 · v2.0.0）
> 核心问题：用户看完 AI 分析，**依然不知道应该勾哪些文件**。
> 原因是 AI 只在解释「这个目录是干嘛的」，没有把位置拆成能直接查看、能明确选择的子组。
> 本轮把分析对象从「目录名称」改成「该位置实际包含的候选内容」。

**1) 为什么以前帮不上忙（问题定位）**

- `ItemAiResult` 只有 Purpose / Impact / Basis / Missing 五个**描述性**字段 ——
  回答的是「这是什么」，不是「我该勾哪些」。
- 发给模型的输入只有**目录名 + 有上限的子项摘要**，模型看不到候选是怎么构成的，
  自然只能对整个目录下一个笼统结论。
- 界面上只有一个五段文字块，没有「定位到具体文件」的入口，也没有「按子组选择」的入口。

**2) 新增：本地候选项分组（本轮的核心）**

`Services/CandidateGroups.cs`（纯函数、不碰磁盘、不调模型）：

- **分组依据按可靠度递降**，能区分才用：
  ① 关联应用/用途（组内出现多个标签时）→ ② **真实子目录**（锚点下最多 2 层）→
  ③ 命中的本地清理规则 → ④ 兜底按文件类型（界面会如实标注「按文件类型分」）。
  刻意**不只看扩展名或目录名**判断可删性。
- **划分而非覆盖**：每个候选项只属于一组，所以各组相加恰好等于位置总量 ——
  数量与空间都不会重复计算（有断言锁定）。
- **三套数字严格分开**：`Bytes`（组内总空间）/ `CandidateBytes`（符合清理资格的候选空间）/
  `UnselectedCandidateBytes`（「选择本组候选」会新增的量）。
- **稳定标识**：`Id = 位置键 | 分组来源 | 判别值 | 扫描版本`，全部来自本地数据。
  模型返回的名称或路径**永远不会**成为选择/删除目标。
- 组数上限 40，超出的并进「其它」，计数不丢；`HiddenGroupCount` 如实记录并展示。

**3) AI 的角色收窄为「每组一句话」**

- 发给模型的输入改成**分组汇总**（`#1 名称 | items | eligible | size | by | rule`），
  不发几十万条记录、不发逐条文件路径（有断言：输入里不含具体文件路径）。
- 模型**只能按本地发出的序号**给建议：`ParseGroupNotes` 只认 `#n`，
  **越界序号直接丢弃**，所以它无法凭空造组、也无法把建议贴到别的组上。
- 分组、数量、清理资格**全部由本地规则决定**，AI 只往 `AiNote` 里写一句话。

**4) 让用户方便选择，但不替用户决定**

- 只有 `Eligibility == AllEligible`（整组都是「符合清理资格且无需额外确认」）的组
  才出现 **「选择本组候选（N 项 · X）」** 按钮，文案里的数量与空间**就是这次实际新增的量**。
- 混合风险组**没有**一键选按钮，改为提示「混合风险，请先查看再选」；
  受保护组显示「受保护，不能清理」，也不提供任何快捷选择。
- 「查看对应文件」复用明细已有的搜索：过滤串用组内条目**共同的最长目录前缀**算出，
  一定命中该组；打开明细不改选择，返回位置列表后原搜索状态保留。
- 选择结果走**既有的** `RefreshAfterSelectionChange()` → 汇入「查看已选」与清理确认页，
  没有新建删除入口，也没有「AI 一键清理」。
- AI 请求开始/返回/刷新/失败/读缓存**都不会**自动勾选或取消任何文件
  （回归断言：AI 代码里没有 `Selected = true` / `AiSuggested = true` 这类写入）。

**5) 未配置 AI 也能用（顺手修掉的功能缺口）**

原来的实现里，AI 未配置时 `RunItemAiAsync` **直接 return**，
于是连**不需要 AI 的本地分组**也不会生成 —— 用户什么也看不到。
现在本地分组**先于** AI 配置检查生成并展开，未配置时给一行如实提示：
「没有配置 AI，拿不到模型建议；但下面的本地分组照样能用，可以自己查看和选择文件。」

**6) 验证结果**
- Release 编译 **0 错 0 警**
- `SafetyCheck` **655 PASS / 0 FAIL**（本轮新增 50 条）
  - 分组 39 条：混杂大目录被拆开（cacheA/cacheB）、按真实子目录/应用分组、
    组名可辨认、**各组相加等于位置总量（数量与空间都不重复）**、
    三套数字不混用、混合风险组不给一键选、受保护/保留项不计入合格候选、
    同名不同路径的位置标识与扫描版本都不同、扫描版本随内容变化、
    组数上限与「其它」并组后计数不丢、空位置不炸、组标识以位置键开头
  - 模型建议映射 11 条：按序号映射、**越界序号丢弃且不粘到最后一组**、
    无序号不采纳、空响应、中文冒号容错、重复序号后者覆盖、
    发给模型的输入含序号但**不含逐条文件路径**
- `UiRegressionCheck` 全过（本轮 +12：本地构建分组、划分为划分、三套数字分开、
  只有全合格组暴露一键选、批量选择不碰受保护/需确认项、
  组标识含扫描版本、按本地序号映射、走既有刷新链、无 AI 也能用本地分组、
  查看文件复用明细搜索、面板渲染每组操作、**AI 不会自动勾选**）
- `CleanAnalyzerCheck` PASS · `AiNoteParserCheck` 全部通过 · `AppRecommendationCheck` All passed
- `git diff --check` 干净

**7) 真机验证（实际启动 WPF 截图）**
- 通过新增的 `DASHAOHUO_CONFIG_DIR` 环境变量把配置目录指到临时目录，
  得到一个**未配置 AI 的干净实例**（不动用户真实配置，也不消耗 AI 额度）。
- 实测截到「按组挑（本地拆分）」面板：
  - `2 组 · 符合清理资格 0 项 · 0 KB`
  - `.map 文件`：25 项 · 符合清理资格 0 项 · 候选空间 0 KB · 按文件类型分
    ／ ［本地规则］… · **25 项受保护，不能清理** ／ [查看对应文件] ／ 受保护，不能清理
  - `.js 文件`：17 项 · 同上
  - 顶部提示「没有配置 AI，拿不到模型建议；但下面的本地分组照样能用…」
- **关键安全性在真机上得到印证**：因为本机是非管理员模式（`selectable=0`），
  两项全是受保护状态，界面**没有出现任何「选择本组候选」按钮** ——
  受保护内容拿不到快捷选择入口，与断言一致。
- 顺带修掉一个只有真机能发现的排版缺陷：没有模型结果时，五段建议的空 TextBlock
  仍占位，面板上方留了半屏空白；现在整块按 `Ai.HasResult` 收起。
- 截图：`docs/shots/20-locations.png`、`docs/shots/21-ai-groups.png`

**8) 未验证 / 已知限制（如实说明）**
- **没有做真实模型调用。** 因此「AI 给每组一句话」在真机上**没有截图**：
  分组界面是本地跑出来的，模型那一路只由离线断言覆盖
  （序号映射、越界丢弃、输入不含逐条路径、空响应、非法建议降级）。
- **「选择本组候选」没有真机点过。** 本机非管理员模式 `selectable=0`，
  全部分组都是「符合清理资格 0 项」，按钮根本不出现。
  该路径的正确性由 39 条分组断言 + 12 条 UI 断言覆盖，但**端到端点击未验证**。
- **本次截到的分组是「按文件类型分」这一档兜底**，不是「按子目录分」。
  按子目录分组由合成数据的断言覆盖（cacheA/cacheB 场景），
  **未在真机大目录上验证**（本机候选都不可删，样本也偏小）。
- 未覆盖：取消 / 超时 / 重试 / 重新扫描后旧结果失效的真机表现；
  大列表 + 小窗口下分组面板的溢出表现（只做了默认窗口）。
- 分组是按位置的 **`Items` 全量**在本地算的（不读文件内容、不发记录），
  超大单位置（十万级）下的分组耗时**没有单独测量**。
- **非管理员模式清理仍不可用**（`RecursiveScanService` 不设 `FileEntry.Parent`），
  本轮未动；它同时导致上面两条「无法端到端验证」。

### 2026-09-12  DDWking（第十六阶段：箭头崩溃根因 + 资源管理器入口 + 弹层透明度 · v1.9.0）
> 范围刻意收窄：只修「点箭头崩溃/闪烁」、「查看路径 → 在资源管理器中打开」、「弹层透出」三件事，
> 没有动布局骨架。用户报的 `StaticResourceHolder` 异常找到了确凿根因。

**1) 箭头异常的真正根因（不是「某个资源缺失」，是引用了一个已删除的键）**

`%APPDATA%\DashaoHuo\crash.log` 里的完整异常（含 InnerException 与调用栈）：

```
System.Windows.Markup.XamlParseException: 在"System.Windows.Markup.StaticResourceHolder"上提供值时引发了异常。
 ---> System.Exception: 无法找到名为"ItemAiBlock"的资源。资源名称区分大小写。
   at System.Windows.StaticResourceExtension.ProvideValue(IServiceProvider serviceProvider)
   at System.Windows.FrameworkTemplate.LoadTemplateXaml(XamlReader templateReader, XamlObjectWriter currentWriter)
   at System.Windows.FrameworkTemplate.LoadContent(DependencyObject container, ...)
   at System.Windows.Controls.DataGridCell.MeasureOverride(Size constraint)
```

链路：**点箭头 → OpenDetail → 明细 DataGrid 建行 → `DataGridCell.MeasureOverride` →
单元格模板 `LoadTemplateXaml` → 解析 `{StaticResource ItemAiBlock}` → 键不存在 → 抛异常**。

上一阶段把 `ItemAiBlock` 拆成 `ItemAiInline`（按钮）+ `ItemAiResultOnly`（结果）时，
**漏改了文件明细 `ColItemAi` 列里的那一处引用**。为什么编译通过、只有点箭头才炸：
那个 DataGrid 在详情页里，**只在打开详情时才实例化**，启动时不解析。

**为什么还闪**：异常发生在 `MeasureOverride` 里，布局会反复重试 ⇒ 同一秒里刷出十几条同样的异常
（日志时间戳 12:56:05.132/.172/.178/.198/.253/.258… 就是证据），画面看起来就是闪。

修复：
- `ColItemAi` 改用 `ItemAiInline`；
- AI 结果改走 DataGrid **原生 `RowDetailsTemplate`**（`VisibleWhenSelected`）在行下方展开，
  不再往单元格里塞整块面板（这也顺带避免了逐行创建大模板）；
- **加了真正的回归测试**：`UiRegressionCheck` 现在会解析 `MainWindow.xaml` + `App.xaml` 的
  全部 `x:Key`，并断言**每个 `{StaticResource X}` 的键都有定义**。这条测试本来就能拦住本次事故。

顺带核查了事件链：箭头（`LocationViewFiles_Click`）、行名称（`LocationRowOpen_Click`）、
复选框（`LayerCheck_Click`）、AI 按钮（`ItemAi_Click`）、扫描信息（`ScanDetails_Click`）
**各自独立绑定**，不存在同一处理器被重复绑到多个元素上；一次点击只走一条路径。
重复点击由既有的 `_locationScrollOffset` 恢复 + 导航状态判断处理，未新增吞异常逻辑。

**2) 「查看路径」→「在资源管理器中打开」**

- 位置行右侧 `IconLink`「查看路径」文字/开关按钮 → **文件夹图标** `IconFolderOpen`，
  Tooltip 与可访问名称都是「在资源管理器中打开」。
- 明细面板顶部的文字按钮「查看路径 / 复制路径」也一起换成图标（文件夹 / 复制），Tooltip 说清行为。
- 完整路径不再靠展开面板：标题悬停显示，右键菜单给「复制完整路径 / 在资源管理器中打开」。

**新建 `Services/ShellReveal.cs` 作为唯一实现**（原来这套逻辑在三处各写了一遍，都有毛病）：
| 旧实现的问题 | 现在 |
|---|---|
| `OpenExplorer` 里 `Directory.Exists(path) \|\| directory` 会去打开**并不存在**的目录 | 只按真实存在性判断 |
| 三处都不区分「路径不存在」，失败静默无反应 | 返回 `NotFound`/`Failed` + 可读提示 |
| 都拼字符串 `"/select,\"" + path + "\""`，**路径带逗号时 Explorer 解析歪** | 用 `ProcessStartInfo.ArgumentList` 结构化参数，`/select,` 与路径合成一个带引号的参数 |
| 聚合位置静默打开「第一个样本项」 | 先列**真实目录**去重：唯一才打开；多个则弹出让用户**自己选**；一个都没有就如实说 |

- 目录 → 打开它本身；文件 → 打开所在文件夹并**选中**（`/select,`），**绝不执行文件**。
- 全部从**数据模型的完整路径**（`CleanItem.FullPath` / `CleanLocationNode.Path`）取，
  不用界面上的省略路径、展示名或脱敏路径。
- 不经过 cmd / PowerShell；空格、中文、逗号、长路径由参数转义与引号规则处理（有测试覆盖）。
- 失败只写一行状态 + 日志，**不弹模态框**、不改选择、不改 AI 状态、不改导航。

**3) 弹层透出与状态残留**

三处真实缺陷（都验证过，不是只看静态截图）：

1. **`Opacity` 加在了包含对话框内容的父容器上** —— `DialogCard` 是 `Overlay` 的子元素，
   而淡入淡出动画改的是 `Overlay.Opacity`；WPF 的 `Opacity` 会**乘到子元素**，
   于是动画期间弹层内容本身半透明，后面的列表直接从弹层里透出来（正是截图现象）。
   → 重构成 **遮罩与内容两层兄弟节点**：遮罩单独做不透明度动画，**卡片始终不透明**，只做轻微缩放。
2. **`HideOverlay` 的 `Completed` 回调无守卫** —— 淡出未结束时若又打开弹层，
   上一次的回调会把**刚打开的弹层** Collapse 掉（弹层莫名消失/残留）。
   → 加 `_overlayEpoch` 代次：回调只在「还是同一次关闭」时才收起。
3. 关闭后不清 `DialogScale` 动画、`Overlay.Opacity` 停在动画时钟上。
   → 关闭与淡入结束时都落回确定值（`Opacity=1/0`、`Scale=0.97`）并 `BeginAnimation(null)`。

另外：弹出时把焦点移进对话框（`DialogCard.Focus()` + `Focusable` + `TabNavigation=Cycle`），
遮罩已经挡住鼠标，键盘焦点也不再留在底层列表上。**没有另造一套弹层**，沿用原有 `Overlay`/`DialogCard`。

**4) 验证结果**
- Release 编译 **0 错 0 警**
- `SafetyCheck` **604 PASS / 0 FAIL**（本轮 +16：`ShellReveal` 全链路）
  - 目录只发 1 个结构化参数、参数是**完整真实路径**、保留中文/空格/逗号
  - 文件用 `/select,`、路径带引号、**启动的是 explorer.exe 而不是执行文件自身**
  - 路径不存在 → 不启动任何进程 + `NotFound` + 可读提示；空路径 → `NoPath`
  - 启动失败 → `Failed` 而不是抛异常；带多余引号的路径会被规范化
- `UiRegressionCheck` 全过（本轮 +3：**每个 StaticResource 键都有定义**、
  不再引用已删除的 `ItemAiBlock`、明细 AI 结果走原生 `RowDetailsTemplate`）
- `CleanAnalyzerCheck` PASS · `AiNoteParserCheck` 全部通过 · `AppRecommendationCheck` All passed
- `git diff --check` 干净

**5) 真机验证（实际启动 WPF 做的，不是只看编译）**
- **点箭头进详情成功**：右侧正确显示该位置「本位置共 42 项」+ 搜索框 + 分组；
  对照基线，`crash.log` **新增 0 字节**、`app.log` 无任何 `Crash`/`XamlParse`/`StaticResource` 记录
  （修复前每点一次箭头就会刷出十几条 `ItemAiBlock` 异常）
- **快速连点箭头 4 次**：无异常、页面状态正常
- **位置行按钮**：`在资源管理器中打开`（文件夹图标）/ `AI 分析` / 进入箭头，三者独立存在
- **弹层**：打开后 `关闭` 按钮数 = 1；**「关闭」后 120ms 内立刻重开 → 仍 = 1**（旧回调抢收已修）；
  关闭后 = 0 且返回按钮仍在。截图确认**对话框完全不透明**，没有列表透出
- 顺带确认本机仍是非管理员模式（弹层里写明「高速 MFT 扫描不可用…错误码 5，正在使用兼容递归扫描」）
- 默认窗口 1250×875 物理 = 1000×700 DIP

**6) 未验证 / 已知限制（如实说明）**
- **真的点了「在资源管理器中打开」按钮吗？没有。** 本轮只验证到「按钮渲染正确、改名正确、
  启动参数由 16 条断言覆盖」；为了避免在验证过程中弹出真实资源管理器窗口，
  **没有真机点这个按钮**。`ShellReveal` 的参数正确性由测试保证，但「Explorer 收到这些参数后
  确实打开/选中」这一步**没有实测**。
- **闪烁只是「不再由异常引起」**。反复测量阶段（Measure 重试）导致的闪已经随异常消失，
  但我没有做逐帧对比，**不能声称所有视觉闪动都已彻底消除**（例如滚动回收时的重建闪动未专门测）。
- 弹层「关闭 → 立刻重开」验证过；**未覆盖**：弹层打开时按 Esc、弹层打开时切分类、
  设置页内「编辑提供方」子视图的 `return` 分支与关闭动画的交错。
- 快速点击用的是 220ms 间隔，**没有做极限连点**。
- 仅验证 125% 缩放；**100% / 150% 未实测**。
- 明细的 `RowDetailsTemplate` 在 42 行的规模上验证；**未在超长列表上验证 AI 结果展开的滚动表现**。
- 非管理员模式清理仍不可用（`RecursiveScanService` 不设 `FileEntry.Parent`），本轮未动，需你决定。

### 2026-09-12  DDWking（第十五阶段：导航与排版精简 + 修复「别删却可勾选」 · v1.8.0）
> 起因两条：一是页头把「清理中心 / 临时文件」和「临时文件」并排重复、返回符号也重复；
> 二是位置名里写着「别删」，东西却照样出现在可勾选的清理列表里。

**1) 「别删却可勾选」的根因（不是名字问题，是口径不一致）**

线索属实，但真正的原因是**两套保护判定各管一半**：

| 路径 | 名字里的警告 | 能进候选列表？ | 删除时被拦？ |
|---|---|---|---|
| `C:\Windows\Installer` | 「别乱删」 | **能**（`CanOffer` 不查它） | **不拦**（`BlockedSegments` 没有它） |
| `C:\System Volume Information` | 「别删」 | **能** | **拦**（删除时才报错） |
| `C:\Windows\Logs\...` | 「Windows 系统（别删）」 | 能（合法日志） | 不拦 |

- 清理列表用的是 `ProtectedPaths.IsProtectedEntry`（老口径）；
- 删除时用的是 `ProtectedPaths.Classify`（`BlockedSegments` 一套）；
- 位置名走 `KnownPaths.Describe`，而那张表**把「身份」和「保护警告」写在同一串文字里**
  （`"Windows 系统（别删）"`），于是警告字样直接变成了候选行的标题。

所以这不是「删掉名字里的别删」能解决的 —— 那样只会把真实保护提示也一起抹掉。

**修法（按真实状态，不按名字）**

1. **统一清理资格判据** `ProtectedPaths.IsCleanupBlocked(e)` =
   `IsProtectedEntry(e) || Classify(path).Guard == Blocked`。
   `CleanRuleHelpers.CanOffer` 改用它 ⇒ 显示、候选统计、全选、预检**同一个口径**。
   于是「界面能勾、执行被拦」和「名字说别删、实际能删」同时消失。
   **方向是收窄，不会让原本不能删的变得能删。**
2. **把名字里承诺的保护真的补上**：新增 `BlockedWindowsSubdirs`
   = `installer / winsxs / servicing / system32 / syswow64 / assembly`，
   只作用于 `C:\Windows\` 下（不误伤任意位置的同名目录）；
   `BlockedAnywhereSegments` 收 `windowsapps`。
   Windows Installer 缓存被删会导致程序无法修复/卸载，属于该保护的位置。
3. **身份与风险分开表达**：新增 `KnownPaths.StripWarning` / `HasWarning`。
   **没有全局删除 KnownPaths 里的警告文案**（其他地方还在用），只是清理列表不再拿它当标题。
4. 受保护项不进入候选、不计入候选数量与空间；它们仍然能在**文件浏览器**里看到，
   保护原因由 `ProtectedPaths.CleanupBlockReason` 给出。没有解锁入口，预检也照常再验一次。

**2) 位置命名（§四）**

- 清理列表的位置标题改成：**签名给出的具体用途名 → 真实文件夹名**。
  不再出现「Windows 系统」「用户目录」这种笼统标签，
  两个不同位置也不会再看起来一模一样。
- 同名位置用**精简父路径**区分（最多 3 段，前面 `…`）：
  `…\v6\npm-ag-grid-…-integrity\node_modules · 30 个文件`。
- 完整路径在悬停里，另有「查看路径」图标 + 复制/打开两个图标操作。
- 「在临时目录里·看不出用途」简化为必要时才出现的短标记 **「用途待确认」**。
- **顺带修掉一个真机才暴露的显示 bug**：`LocationKey` 会把锚点 `ToUpperInvariant()`
  作为稳定键，而新的命名逻辑从键取名字，于是位置名显示成
  `LOGS` / `D:\PROJ1\BUILD\CACHE`。新增 `RealCaseAnchor` 从真实路径取回原始大小写，
  键保持大写、显示用真实大小写。

**3) 顶部导航精简（§二）**

- 返回只剩**一个箭头按钮**（`IconBack`），Tooltip 与可访问名称按层级给
  （「返回清理中心」/「返回位置列表」），不再「箭头 + 面包屑文字 + 大标题」三份重复。
- 首页标题留空（导航项已经写着「清理中心」）；位置页只写用途名一次。
- 位置页第二行与页头统一为 **「N 个位置 · 候选空间 X」**；
  原来页面内 `LocationPageSub` 还重复一遍「预计处理」，现在只补页头没有的信息
  （被截断未显示的位置数）。
- **成功扫描不再显示任何状态行**；只有异常才留一句短警告
  「部分内容未检测 · 点这里看原因」（去掉了冗余的「扫描完成 ·」前缀），可展开看原因。
- 底部仍然只对用户**实际选中**的内容显示预计处理空间；没选择时说「尚未选择清理内容」。

**4) 位置行改成紧凑两行（§五）**

```
[复选框] node_modules                          空间未知   [查看路径] [AI 分析] [›]
         …\v6\npm-ag-grid-…-integrity\node_modules · 30 个文件
```
- 第一行：名称 + 空间 + 操作；第二行：精简父路径 · 文件数 ·（必要时）用途待确认。
- AI 按钮**固定在右侧**，不再单独占一行；结果仍在行下方展开，
  已拆成 `ItemAiInline`（按钮）与 `ItemAiResultOnly`（结果）两个模板。
- 「查看路径」变成图标操作（`IconLink`），与复制（`IconCopy`）、
  在资源管理器打开（`IconFolderOpen`）Tooltip 各自说清行为，不再混淆。
- **未选择时不再显示「已选 0 / 385 项」**（`SelectionBadge` 为空），有选择后才出现。
- 复选框 / AI / 路径 / 进入箭头各自绑定处理器，互不触发行导航；
  点名称区域才进明细（`LocationRowOpen_Click`）。

**5) 验证结果**
- Release 编译 **0 错 0 警**（顺手清掉一个 `CS0108` 遮蔽警告）
- `SafetyCheck` **588 PASS / 0 FAIL**（本轮新增 34 条）
  - 受保护项排除 17 条（Installer/WinSxS/servicing/SVI/WindowsApps/$Recycle.Bin 都不可候选；
    Windows\Temp、Windows\Logs、用户 Temp、npm 缓存仍可候选；**逐个断言「资格 == 删除口径」**）
  - 命名 12 条（StripWarning/HasWarning、不用笼统标签、不含保护措辞、
    **名字取自真实路径且保留真实大小写**、同名文件夹靠父路径区分、
    父路径长度上限、选择标记空/非空语义）
  - 混合目录 2 条（受保护项不混进候选；合格候选照常产出）
- `UiRegressionCheck` 全过（本轮 +4：返回只有一个箭头、标题只出现一次、
  成功扫描不重复状态行、页头用候选空间而底栏用预计处理）
- `CleanAnalyzerCheck` PASS · `AiNoteParserCheck` 全部通过 · `AppRecommendationCheck` All passed
- 大数据：**300k 候选** snapshot 335 ms / grouping 1669 ms；100k 单位置展开 89 ms、仅 100 行
- `git diff --check` 干净

**6) 真机验证（做了，125% 缩放）**
- `XamlParseException` 计数 **0**；默认窗口 1250×875 物理 = **1000×700 DIP**，最小窗口可达 860×560 DIP
- 首页第二行：`82 个位置 · 候选空间 空间未知`（不再写「预计处理」）
- 位置页第二行：`5 个位置 · 候选空间 空间未知` —— **只出现一次**（修复前页头与页内各一遍）
- 返回按钮可访问名称 = **「返回清理中心」**
- 每行第二行显示 `…\v6\npm-ag-grid-enterprise-…-integrity\node_modules · 30 个文件`，
  同名 `node_modules` 靠父路径区分，**大小写正确（不再全大写）**
- 每行按钮：`查看路径` / `AI 分析` / `‹名称›`（进入）三者独立存在

**7) 未验证 / 已知限制（如实说明）**
- 「受保护项在文件浏览器里以锁图标 + 保护原因展示」**没有真机截图**：本机非管理员模式
  `selectable=0`（见下），文件浏览器路径未逐个点验。保护原因由
  `ProtectedPaths.CleanupBlockReason` 提供，逻辑由 588 条断言覆盖。
- 「混合目录只统计合格候选」由 `SafetyCheck` 断言覆盖，**未在真机混合目录上点验**。
- **没有做真实模型调用**（沿用上一轮结论：用你的付费额度需授权），
  逐项 AI 的按钮与结果展开仍是「结构 + 状态逻辑」验证。
- 仅验证 125% 缩放；**100% / 150% 未实测**。
- 本轮**收窄了可删范围**（Installer / SVI / WindowsApps 等不再进候选）。
  这是安全方向的改动，但它确实改变了旧行为，如果你认为某个位置应当可清理，需要单独讨论。
- **非管理员模式清理仍不可用**（`RecursiveScanService` 不设 `FileEntry.Parent` →
  全部判受保护 → `selectable=0`）。本轮仍未修改：修法会扩大非管理员模式的可删范围，
  需要你单独决定。注意本轮的统一资格判据**依赖** `Parent` 正确，所以这个 bug 在非管理员模式
  下的影响面比之前更大，建议尽快决定怎么处理。
- 首页「82 个位置」与分区「84 个位置」口径不同（前者按位置键去重、后者按风险分区累加），
  沿用既有行为未改。

### 2026-09-12  DDWking（第十四阶段：AI 改为按需逐项 + 收紧清理规则 + 取消默认勾选 · v1.7.0）
> 起因两条：一是 AI 是「首页全局批量分析」，用户感受不到它和具体哪一项有关；
> 二是清理规则与默认勾选太激进 —— 默认替用户勾上一大批，而且路径匹配是裸子串。

**1) 移除首页全局 AI 面板**
- 删掉清理中心顶部那块 `AiPanel`（标题/说明/优先建议/谨慎判断/开始分析/重新分析/按 AI 建议选择）
  以及配套的整段状态机（`AiState`、`_aiLast`、`_aiScopeRoot`、`_aiRequestId`、
  `AnalyzeCurrentCategory`、`ExplainItems`、`ShowAiAnalysis`、`UpdateAiPanel`）。
- **扫描完成后不再自动发起任何模型请求**；「更多」菜单也不再提供整盘/整类分析入口。
- AI 配置、`AiCoordinator`、`AiGateway`、`AiClient`、sidecar 通道**全部保留**，只是入口换了位置。
- 扫描状态与逐项 AI 状态**彻底分开**：分析某一项不会让整个页面进入忙碌。

**2) AI 改为挂在具体项目旁的按需分析**
- 清理位置行、文件明细行右侧都有固定位置的 **「AI 分析」按钮**（统一图标 + 文字，走既有 `ChipButton` 样式）。
- **范围严格隔离**：文件分析只发那一个文件；位置分析只发那一个位置，
  不会静默扩大到整个用途（`DescribeAiTarget` 只认 `CleanItem` / `CleanLocationNode`）。
- 结果**在行下方紧凑展开**，五段固定内容：建议 / 用途 / 删除影响 / 判断依据 / 缺少的信息。
- **建议允许「信息不足」**（`ItemAiSuggestion` 四档：可考虑清理 / 需要确认 / 建议保留 / 信息不足）。
  模型若说「安全」「可以删」这类越权词，`MapSuggestion` 一律降级为「信息不足」，绝不变成可删结论。
- 已有结果时按钮变「查看结果」并**只切换展开、不再请求模型**；另给「重新分析」。
- 按钮有 ToolTip、可访问名称、键盘焦点；状态文案由状态机给出（排队/分析中/查看结果/重试）。

**3) 降低单项延迟（按需 + 有界 + 可缓存）**
- 输入只有**元数据**：脱敏路径、大小、修改时间、类型、命中的本地规则。**不读、不上传文件内容。**
- 文件夹用**有上限的目录摘要**（最大 12 个直接子项，取自已经扫出来的数据，**不做任何遍历**），
  并在提示词里如实写明「摘要只覆盖 N 个里的前 M 个 —— 不是整个文件夹」。
- 单项只走**一轮**请求（`maxTurns: 1`）：这是纯文本判定，不需要多轮工具调查。
- **有限并发** `MaxConcurrent=2` + **单项超时 45 秒** + 可取消（取消一路传到 HTTP/sidecar）。
- **缓存** `ItemAiCacheKey(ScopeKey, Version)`：版本由路径/大小/修改时间/类型/本地规则/
  配置签名/提示词版本共同决定，任一项变了自动过期；`DropExpired` 清理同项的旧版本。
- 日志记录 queue / send / parse 三段耗时与通道，便于**实测后再优化**（本轮没有承诺秒回）。

**4) 逐项状态与结果归属**
- `ItemAiStatus` 覆盖：未分析 / 排队 / 分析中 / 完成 / 有回复但无可用 / 失败 / 超时 / 取消。
- 状态挂在**项目自身**（位置用稳定键 `Key`、文件用 `FullPath`），不依赖虚拟化出来的行控件，
  所以滚动回收、切页、重扫都不会把结果串到别的行。
- `RequestId` 守卫：旧请求晚回来**不许覆盖**新状态；`_itemAiRunning` 既做**去重**（同一项不重复提交）
  又提供**就地取消**。
- 「请求成功」与「结果有效」分开：解析为空、答非所问、建议不合法 → `NoUseful`，不显示成「分析完成」。

**5) 清理规则收紧（本轮最重要的安全改动）**

**缺陷 A：路径签名是裸子串匹配。** `Match` 用 `IndexOf` 且**不检查目录段边界**，于是：

| 误匹配 | 后果 |
|---|---|
| `\trae` 命中 `C:\tools\traefik\`、`D:\docs\trae-notes\` | 毫不相干的目录被当成编辑器缓存 |
| `\npm-cache` 命中 `D:\backup\npm-cache-old\` | 备份目录被当成 npm 缓存 |
| `\go\pkg\mod` 命中 `\go\pkg\models` | 源码目录被当成模块缓存 |
| `\node_modules` 命中 `node_modules_backup` | — |

修法：新增 `SegmentIndexOf` —— needle 必须以**完整目录段序列**出现（needle 自带前导 `\`，
所以只需守右边界：匹配结束处必须是串尾或另一个 `\`）。`SubHit` 同样改成整段匹配
（`\cache` 不再命中 `\mycache\`）。90 个 needle 全部带前导 `\`，只有 2 个带尾随 `\`，已按整段语义处理。

**缺陷 B：`\appdata\roaming\npm` 被当成安全缓存。** 这是 npm 的**全局安装目录**
（`npm i -g` 装出来的命令行工具在这儿），**不是缓存**。原来它和 `\npm-cache` 混在同一条
`SigRisk.Safe` 签名里，会把用户的全局工具标成「删了会自动重建」——**会误删用户工具**。
修法：拆成两条 ——
- `npm` / `Safe` / needle 只剩 `\npm-cache`（整段匹配同时覆盖 `Local\npm-cache` 与 `Roaming\npm-cache`）；
- 新增 `npm 全局工具` / `SigRisk.Keep` / needle `\appdata\roaming\npm` → 映射为 `CleanRisk.Keep`（不可删）。

同类过宽项一并修掉：`pip` 去掉 `\appdata\local\pip`（整个 pip 目录），只保留 `\pip\cache`；
`360` 补上真实目录名 `\360se6`（整段匹配后 `\360se` 不再命中它）。

**已核实但无需改动**：`SafeDirBits` 里虽然含 `\downloads\`，但用到它的两条分支都只产生
`Confirm`，不会把下载目录标成安全 —— 已加测试锁定。

**缺陷 C：AI 有工具能直接改勾选。** `DiskAnalyst` 的 `set_checked` / `suggest` 会让模型
直接改 `CleanItem.Selected`；sidecar 的 `agent.js` 也把这两个工具提供给模型。这违反
「AI 只解释和建议，选择由用户做」。修法：
- C# 侧 `Run()` 拒绝这两个名字，`SetChecked`/`Suggest` 处理器删除，
  `IAnalystHost` 去掉 `OnChecksChanged`/`OnSuggest`，工具清单换成只读的 `report_finding`；
- **重建 sidecar**（bun），新 exe 已不含 `set_checked`（已核对二进制内容），启动冒烟通过。

**6) 取消默认大批量勾选**
- 「建议清理」→ **「清理候选」**，副标题也从「规则明确，通常可以优先处理」改成
  **「规则命中的候选 · 默认没有勾选，需要你自己选」**。改名字不等于规则没问题，
  但至少界面不再替用户下「可以优先处理」的结论。
- `TempCacheRule` 的 `Selected = risk == CleanRisk.Safe` → **`Selected = false`**；
  重复项的 `Selected = canDelete && !incomplete` → **`Selected = false`**。
  现在**任何扫描结果默认一项都不勾**，包括原来是 Safe 的项，也不会沿用上一轮的选择。
- 用户主动勾选、`ApplySelection`（整组勾选，只作用于 `CanDelete`）保持不变。

**7) 验证结果**
- Release 编译 **0 错 0 警**
- `SafetyCheck` **554 PASS / 0 FAIL**（本轮新增 60 条）
  - 路径段边界 17 条（误匹配不再命中 + 正常路径仍命中 + npm 全局/缓存区分）
  - 默认不勾选 8 条（**直接跑真实 `TempCacheRule`**，断言所有命中 `Selected == false`）
  - AI 不得改选择 7 条（工具被拒绝、工具清单不含这两个名字、宿主接口不再暴露写选择方法）
  - 逐项 AI 25 条（范围隔离、缓存失效、摘要上限与如实说明、脱敏、解析、
    非法建议降级、字段截断、状态机语义）
- `UiRegressionCheck` 全过（本轮新增 21 条：首页面板已移除、不再自动请求、
  配置与网关保留、逐项按钮接线、文件/位置范围隔离、摘要上限不用遍历、
  并发/超时、状态覆盖、取消与去重、缓存键、建议四档、五段结果、本地规则标注、
  sidecar 不再提供改选择工具、清理候选改名、规则不预勾选、整段匹配、npm 全局非缓存）
- `CleanAnalyzerCheck` PASS · `AiNoteParserCheck` 全部通过 · `AppRecommendationCheck` All passed
- 大数据：**300k 候选** snapshot 337 ms / grouping 1567 ms；100k 单位置展开 83 ms、仅 100 行
- `git diff --check` 干净

**8) 真机验证（做了，125% 缩放）**
- 界面结构确认：首页**已无** AI 面板；「更多」菜单不再有批量分析入口；
  位置页每行右侧有 **「AI 分析」按钮**（截图可见），箭头面包屑 `‹ 清理中心 / 超长路径` 正常。
- **真机抓出一个只在运行时才暴露的 bug**：`ItemAiBlock` 模板用 `StaticResource ChipButton`，
  但 `ChipButton` 定义在它**后面** —— WPF 的 `StaticResource` 只向前解析，
  于是每个位置行渲染时抛 `XamlParseException`（编译和静态断言都发现不了）。
  已把 `ChipButton` 移到模板之前，重启后 `XamlParseException` 计数为 0。
- **规则修正的实机证据**：位置页出现名为 **「npm 全局工具」** 的位置
  （修复前它会以 npm 缓存的身份混在候选里），说明新签名在真实扫描数据上生效。
- 未点击 AI 按钮：本机已配置 `ai98pro.xyz` 供应商，点击会**用你的额度发真实请求**，
  未经授权不执行（见下方未验证项）。

**9) 未验证 / 已知限制（如实说明）**
- **没有做真实模型调用**。因此逐项分析的「排队 / 分析中 / 完成 / 无可用 / 失败 / 超时 /
  取消」在真机上**没有截图**；这些状态由 `SafetyCheck`（真实 `ItemAiPrompt` /
  `ItemAiView` / 缓存与解析逻辑）与 `UiRegressionCheck`（结构接线）覆盖。
- **逐项分析的端到端点击未验证**（同上原因）：点按钮 → 请求 → 解析 → 行内展开这条链
  只验证到「按钮已渲染且接线正确」。
- **111 秒 → 单项延迟没有实测数据**。`maxTurns: 1` 从代码路径上移除了多轮 agent 循环，
  但**未在真机计时**；`ItemAiService` 已经记录 queue/send/parse 三段耗时，下次实测可直接读日志。
- **文件明细行的 AI 按钮未截图**（位置行已验证）；文件详情需要先进入某个位置再打开明细。
- 仅验证 125% 缩放；**100% / 150% 未实测**。
- **非管理员模式清理仍不可用**（`RecursiveScanService` 不设 `FileEntry.Parent` →
  全部判受保护 → `selectable=0`）。本轮仍未修改：修法会扩大非管理员模式的可删范围，
  需要你单独决定。顺带发现它也影响测试夹具 —— 手写 `FileEntry` 必须挂 `Parent`，
  否则规则会把它们全判为受保护（已在测试里注明）。

### 2026-09-12  DDWking（第十三阶段：图标体系 + AI 真实结果可见性 · v1.6.0）
> 起因两条：一是纯文字入口太多、「查看已选」没有按钮感、设置还是文字；二是 AI 分析
> 「看起来跑了但用户感受不到作用」。先查 AI 链路，再改 UI，不重写窗口。

**1) AI 根因：查到了确凿证据（不是猜模型或 sidecar 慢）**

本机日志：
```
[2026-09-11 00:31:58] [Ai] | channel=sidecar ok=True attempts=1 ms=111152
[2026-09-11 00:31:58] op=ai-003007-006 stage=done ms=111203 n=15 | explained=15 sent=60 redacted=True
```

逐条回答调查问题：

| 问题 | 结论 | 证据 |
|---|---|---|
| 点击是否真的发出请求？ | **是**，成功返回 | `ok=True`，`explained=15` |
| 分析范围是什么？ | 当前用途/位置优先，否则全盘候选（最多 60 条） | `AnalyzeCurrentCategory` 的三级回退；`MaxBatch=60` |
| 返回多少、匹配多少、应用多少？ | 提交 60、**可用 15** | `sent=60 explained=15` |
| 15/60 是模型没回、解析失败还是匹配失败？ | **模型没回满**（单次只回约 15 行） | 新增解析计数后可区分：`cand/hits/noise` 各自计数；用假响应确定性复现 |
| AiNote 更新后 UI 收到通知了吗？ | 收到，但**只有文件详情面板的「说明」列**绑定它 | 全仓库 `AiNote` 只有 1 处 XAML 引用（`ColCleanWhy`）|
| 首页把本地规则摘要当成 AI 结论了吗？ | **是**，这是核心问题 | `AiRecommendLine/AiCautionLine` 读的是 `RiskTier`（本地规则），却写在 AI 面板里、没有标注 |
| 页面切换/重扫/重复点击有旧任务覆盖吗？ | **有**，此前无请求代次守卫 | `_aiBusy` 是共享布尔，晚返回的旧任务会覆盖新状态 |
| 111 秒花在哪？ | 4 轮 agent 循环 + 超轮数后的**额外兜底总结请求** | `AiClient.StreamAsync` 硬编码 `MaxTurns=4` → `SidecarClient` 透传 → `sidecar/src/agent.js` 在 `sawTool` 时再发一次 `completeSimple` |

**根因归纳（三条，都不是「模型慢」）：**
1. **AI 产物展示位置错了**：AI 唯一真实产物 `AiNote` 只在「文件详情 → 说明列」，用户点完
   「开始 AI 分析」在首页看不到任何 AI 内容；而首页 AI 面板显示的却是**本地规则**的
   「优先建议/谨慎判断」，且没有标注来源 → 用户以为 AI 在复述本地结果。
2. **一次文本批注走了多轮工具 agent**：`MaxTurns=4` 让 sidecar 用工具调查最多 4 轮，
   结束后再补一次总结请求；预算被调查吃掉，最终只产出约 15 条，耗时 111 秒。
3. **状态机有竞争**：`_aiBusy` 布尔 + 面板枚举两处状态，旧请求晚返回会覆盖新状态；
   进行中主按钮被禁用，用户没有就近的取消入口。

**2) AI 修复**

- **批注任务不再走工具 agent**：`AiClient.StreamAsync` 的 `maxTurns` 默认改为 **1**
  （这纯属一次文本转换，不需要多轮调查）。`AiGateway`/`SidecarClient`/`agent.js` 本来就
  消费这个参数，改动只有一处、最小。
- **解析可诊断**：`AiNoteParser.ApplyWithStats` 返回 `Lines / CandidateLines / PathHits /
  NoiseSkipped / Applied`。现在能直接区分「模型没回」「路径对不上」「全是噪音」三种 15/60。
  `Apply` 旧签名保留为 `=> ApplyWithStats(...).Applied`，14 处既有测试不受影响。
- **阶段耗时落日志**：组装 / 请求 / 解析三段分别计时，并把解析计数、通道、尝试次数
  一起写进 op 日志。以后再遇到类似问题不必靠猜。
- **状态机收敛为单一来源** `_aiState`（NotConfigured / Idle / Running / Done / NoUseful /
  Failed / Stopped）。**「请求成功」与「结果有效」分开**：一条都没解析出来是 `NoUseful`，
  不再显示成「AI 已完成分析」。
- **请求代次守卫**：`_aiRequestId`，旧请求晚返回只记日志、不碰界面；`_aiInFlightId` 防重复提交；
  结果记住 `_aiScopeRoot`，重新扫描后旧结果不再算当前范围。
- **取消入口就近可用**：进行中主按钮变成「停止分析」（原来是禁用，只能去顶栏找停止）。
- **不伪造进度**：没有真实百分比就显示「已等待 N 秒」，不编百分比。

**3) AI 真实结果可见（首页 + 详情）**

- 首页 AI 面板新增 `AiScopeText`：`提交 60 项 · 生成可用说明 15 条 · 无说明 45 项` ——
  **提交数与可用数分开写**，绝不把「发送 60 条」说成「分析了 60 条」。
- 「优先建议 / 谨慎判断」前面加 **［本地规则］** 标签，并在详情里单列一节
  「以下来自本地规则，不是 AI 结论」。不再把本地规则包装成 AI 成果。
- **AI 详情改为真实内容优先**：先列分析范围/覆盖/计数/三段耗时/解析明细/通道，
  再列**真实逐项 AI 说明**（带名称），最后才是标注清楚的本地规则摘要。
  没有任何可用说明时直接说事实，不虚构总结。
- 没有为了「展示摘要」额外增加模型调用（协议未变，仍是逐项 AiNote）。

**4) 图标体系**

- 新增一套统一的 **24×24 线性矢量 `Geometry`**（`IconGear / IconChecklist / IconInfo /
  IconMore / IconBack / IconClose / IconPanel / IconClearSelection / IconAi / IconDoc /
  IconChevron* / IconSearch / IconScan`），颜色一律 `Fill` 绑定按钮前景色、跟随主题。
  不混用 emoji 或随机 Unicode，也没有引入新依赖。
- 新增三个统一样式：`IconButton`（34×34，悬停/按下/键盘焦点/禁用四态齐全）、
  `IconButtonGear`（齿轮 **36×36**）、`ChipButton`（有按钮感的胶囊，用于「查看已选」）。
- 实际改造：**设置→齿轮**、扫描详情→ⓘ、更多→⋯、返回→箭头、展开/收起→矢量箭头、
  AI 面板→★ 矢量、查看已选→清单图标+数量、底部主按钮保持图标+文字。
  核心操作「开始 AI 分析」「清理已选项目」保留**图标+文字**，不裸图标。

**5) 真机抓出的两个「Content 覆盖图标」bug（本轮最实在的收获）**

只有真跑界面才看得出来，编译和静态断言都发现不了：
1. `ApplyUi()` 里残留 `SettingsButton.Content = Loc.Settings` → 齿轮被文字顶掉。
   **第一次修完仍然是文字**，因为 `ApplyUi()` 开头还有**第二处** `SettingsButton.Content`
   （另一段文案设置块）—— 截图放大后才定位到。
2. `ScanDetailsBtn.Content = Loc.ScanDetails` / `CleanMoreBtn.Content = Loc.MoreMenu`
   → ⓘ 和 ⋯ 同样被文字顶掉，界面上出现被裁剪的「扫描详」「更多」。

现在这三处都只设 `ToolTip` 与 `AutomationProperties.Name`，**再加回归断言禁止任何
图标按钮被写 Content**。

**6) 查看已选：结构化清单（替换一大段文字）**

- 入口换成**胶囊按钮 + 清单图标 + 已选数量**（`查看已选（12）`），空选择时禁用；
  数量口径与底部摘要一致（候选项数）。
- 点击打开结构化面板：顶部是带量词的统计（位置数 / 候选项数 / 预计空间），
  下面用 `DataGrid` 按**位置**分组列出 用途 / 位置名 / 候选项数 / 预计空间。
- 网格**开了行与列虚拟化 + Recycling**，大选择量不会一次性建行。
- 长路径列截断显示、悬停给完整路径，并有「复制路径」按钮。
- 没归到任何已建位置的已选项单独归一行（`（其它已选项）`），避免清单漏数。
- 统计/说明明确写「候选项」而不是「文件数」；打开与关闭**都不改动选择**。

**7) 视觉层级**

- 顶部只留导航与少量全局工具（齿轮、容量）；辅助操作图标化；内容区保持
  「建议清理 / 需要你确认」两块独立区域（色带 + 图标 + 副标题）；底部只留一个主操作。
- 字号/字重分层：页面标题 15 SemiBold、分组标题 13.5 SemiBold、条目 14、辅助 11~11.5。
- 上一阶段已删的常驻教学句与重复统计保持移除状态。

**8) 验证结果**

- Release 编译 **0 错 0 警**
- `SafetyCheck` **494 PASS / 0 FAIL**（本轮新增 39 条：AI 解析诊断 13 条 +
  AI 端到端 22 条 + 其它）——其中 AI 端到端用假响应驱动**真实的** coordinator + parser +
  计数链路，**确定性复现了 15/60 症状**，并验证「空回复」「答非所问」「超上限」
  「取消」「脱敏」「AI 不碰风险/勾选」各条路径
- `UiRegressionCheck` 全过（本轮新增 30 条：AI 单一状态来源、成功与有效分离、取消入口、
  去重提交、代次守卫、范围失效、日志字段、maxTurns=1、矢量图标、四态样式、
  可访问名称、齿轮不被覆盖、矢量箭头、查看已选结构化+虚拟化、无空名称按钮）
- `CleanAnalyzerCheck` PASS · `AiNoteParserCheck` 全部通过 · `AppRecommendationCheck` All passed
- 大数据：**300k 候选** snapshot 344 ms / grouping 1634 ms；100k 单位置展开 98 ms、仅 100 行
- `git diff --check` 干净

**9) 真实 WPF UI 验证（做了）**

本机 **125% 缩放**（120 DPI，已核实），非提权实例 + UI Automation + 真实鼠标点击：
- 默认窗口 1250×875 物理 = **1000×700 DIP**；最小钳到 1075×700 物理 = **860×560 DIP**
- **齿轮**实际渲染（45×45 物理 = **36×36 DIP**），可访问名称「打开设置」，ToolTip「设置」，
  点击**真的打开设置对话框**；「把完整本地路径发给 AI」保持未勾选（默认脱敏未被改动）
- 页头 **ⓘ + ⋯** 图标实际渲染；AI 面板 **★** 矢量图标渲染
- **空名称按钮数量 = 0**（修掉了我自己引入的可访问名称回归）
- 窄窗 860 DIP 时侧栏为**覆盖抽屉 + 遮罩**：主内容未被压缩、底部按钮可达、无黑块
- 首屏 AI 面板显示「［本地规则］ 谨慎判断：超长路径、很久没动的文件、大文件」

**10) 未验证 / 已知限制（如实说明）**

- **没有做真实模型调用**。供应商已配置（`ai98pro.xyz`，密钥在 `secrets.dat`），
  但用你的付费额度真实调用需要你明确授权，本轮**没有**发出任何真实请求。
  因此：AI 面板的「分析中 / 已完成 / NoUseful / 失败」四态**没有真机截图**，
  只有 `SafetyCheck`（假响应端到端）与 `UiRegressionCheck`（结构断言）覆盖。
- **111 秒 → 快多少没有实测数据**。`maxTurns=1` 从代码路径上移除了 3 轮工具循环和一次
  兜底总结请求，但**未在真机计时对比**。
- **「查看已选」结构化面板未截到真实截图**：本机非管理员模式下 `selectable=0`
  （见下方已知问题），没有可勾选的候选，按钮处于禁用态。面板结构由回归断言覆盖。
- 「按 AI 建议选择」同理，未在真机点验。
- 仅验证 125% 缩放；**100% 与 150% 未实测**。
- 未覆盖：AI 请求真实超时、真实网络中断、真实 sidecar 冷启动耗时。

**11) 顺带确认的既有问题（仍未修改，需你决定）**

上一阶段记录的**非管理员模式清理不可用**依旧存在：`RecursiveScanService` 不设
`FileEntry.Parent` → `ProtectedPaths.IsProtectedEntry` 把所有条目判为受保护 →
`selectable=0`。本轮日志再次证实（`layers(...) selectable=0`）。
修法会**扩大非管理员模式的可删范围**，超出本轮范围，**未擅自修改**。

### 2026-09-12  DDWking（第十二阶段：AI 核心化 + 清理语义 + 响应式侧栏 · v1.5.0）
> 起因：AI 被塞在「更多」菜单里，看不出是这个软件的核心能力；底部按钮叫「检查并清理」，
> 说不清点了会发生什么；侧栏往左拖会出现黑块，窗口缩小时主内容被压到逐字换行。
> 这一版把 AI 提到清理中心顶部成为核心区域，把执行入口改成语义明确的「清理已选项目」+
> 独立的清理前检查页，并把侧栏重做成宽屏停靠 / 窄屏覆盖的响应式布局。

**1) 顶部导航与按钮语义**
- 「清理」→「**清理中心**」（导航项与页面标题统一）。
- 底部主按钮「检查并清理」→「**清理已选项目**」。全仓库已无「检查并清理」字样。
- 点「清理已选项目」**不再直接进确认框**，而是打开**清理前检查页**。
- 未选内容时显示「尚未选择清理内容」且按钮禁用。
- 执行中主按钮变「正在清理……」并禁用（停止入口仍是顶栏「停止」）；
  完成后变「已完成 N 项，M 项未处理」+「查看结果」。
- 卸载页保留自己的操作栏与按钮状态，不共用清理页的状态机。

**2) 新增「清理前检查页」（新的一层主内容）**
显示：将处理的位置数 / 将处理的候选项数 / 预计处理空间（标注估算）/ 需要你确认的项目数，
并用一段文字说明程序在真正处理每一项之前会**再检查**：文件是否仍存在、扫描后是否被改动过、
是否受系统保护或正在被占用；没通过的项留在原处并在结果里写明原因。
按钮：[返回修改] [确认清理]。只有点「确认清理」才进入**既有**的删除预检 → 确认框 → 执行链路。
- 数字由一个纯函数 `CleanPreflight.Build` 计算（可测）：只统计 `CanDelete` 且有路径的项，
  位置数按稳定键去重，「需确认」= 风险非 Safe 的项，如实报出。
- 底部摘要的位置数与检查页共用同一个函数，避免两处口径跑偏。
- 「返回修改」回到来源层（位置页或首页），滚动、筛选、搜索、勾选都不动；
  返回不会重新扫描或重新分析。

**3) AI 分析提升为清理中心的核心区域**
- 在页头状态下方新增 `AiPanel`：✦ 图标 + 标题 + 说明 + 优先建议/谨慎判断 + 主按钮。
- 四种状态各有明确文案与按钮：
  - **未配置**：「AI 分析尚未配置」+「配置 AI」（打开设置，不动隐私开关）；
  - **分析中**：「AI 正在分析当前扫描结果……」，主按钮禁用，**不阻塞**清理列表；
  - **已完成**：「AI 已完成分析」，显示 `优先建议：…` / `谨慎判断：…`，按钮变「查看 AI 分析」；
    另给「重新分析」与「按 AI 建议选择」两个轻量文字入口；
  - **已停止 / 失败**：各自标题与重试入口。
- 「优先建议 / 谨慎判断」**直接取本地风险档**（RiskTier 0 / 1）的用途名，
  不是让 AI 改风险后再显示 —— 保证同一份盘的结果可复现、可审计。
- 「查看 AI 分析」详情含：为什么推荐、清理后可能的影响、不建议处理的、需要确认的、
  **分析依据范围（送出 N 项 / 共 M 项）**、以及「受上限影响只分析了部分候选」的如实提示，
  最后写明 AI 的边界：只解释和建议，风险等级/可删范围/最终选择仍归本地规则和用户。
- **AI 安全边界保持不变（并加测试锁死）**：不写 Risk / Selected / CanDelete；
  不把「需要你确认」改成「建议清理」；不扩大可删除范围；不删文件；不卸载软件；不绕过删除预检。
- 「按 AI 建议选择」文案明确为「按 AI 建议选择」，**只勾选本地规则已允许且属于「建议清理」档**的候选
  （`RiskTier != 0` 直接跳过、`!CanDelete || Risk==Keep` 跳过），且执行前仍由用户确认。
- 已确认全仓库不存在误导文案：AI 自动清理 / AI 安全清理 / AI 决定清理 / AI 已替你选择全部内容。
- `UpdateAiPanel` 只读分层结果里的用途名与影响，**不重设列表 ItemsSource**，因此不会重建几十万条候选行。

**4) 精简清理中心顶部**
- 删掉常驻教学长句「先选用途，再选具体位置。要看具体文件时再点『查看文件』。」
- 删掉首页里重复的「清理」标题（导航项已经写着「清理中心」）。
- 首页不再显示候选总数、完整扫描文件/目录数、耗时、重复项统计 ——
  全部移入「扫描详情」，其中新增了候选总数与重复组数、去重/重复命中、重复检测是否跑完。
  `UpdateCleanHintAfterDuplicates` 也不再往主页面写「找到 N 个重复项」。
- 「预计是估算值……」不再单独占一行，改成底部摘要的 **悬停提示**。
- 「部分内容未检测」等重要警示仍保留在主页面；不完整绝不显示成扫描成功。

**5) 风险分区改成真正独立的两块视觉区域**
- 每个分区是一个带**左侧色带**（3px）的容器，两块区域各有不同的背景层级与上下留白。
- 分区头：`✓ / !` 图标 + 标题 + **副标题**（说清这一组是什么性质）+ 右侧统计。
  - 建议清理：绿松石色带，副标题「规则明确，通常可以优先处理」；
  - 需要你确认：暗橙色带，副标题「这些内容可能仍有价值，请查看后决定」。
- **不单靠颜色**：图标形状（✓ / !）、副标题文字、背景层级、色带位置都能区分。
- 去掉了看起来像全宽按钮的厚重边框；标题左对齐、统计右对齐。
- 「需要你确认」默认折叠；折叠组里若有已选项，标题必须显示「已选 N 项，分布在 M 个位置」。
- 风险归属沿用上一阶段的修正：大文件/旧文件/重复文件**不会因为名字或体积**进「建议清理」，
  风险仍由本地规则决定，没有为了视觉效果降低任何风险。

**6) 返回改成箭头面包屑**
- 下级页面的返回入口从大按钮改成 `‹ 清理中心 / 当前层` 的面包屑，整块可点、可键盘聚焦，
  ToolTip 与可访问名称都是「返回清理中心」。
- 支持 **Esc 逐层返回**：清理前检查页 → 文件明细 → 位置页 → 首页，其次才清文件浏览器搜索。
- 返回保留滚动位置、筛选、搜索与勾选；不重新扫描、不重新分析。
- 文件明细仍用右上角关闭；关闭后回到原来的位置列表。

**7) 侧栏改成响应式抽屉（修掉黑块与窄窗错乱）**
- **黑块的根因**：主内容列写了 `MaxWidth="1200"`。star 列一旦有上限，窗口比上限宽时
  右侧就空出一条没人绘制的暗色区域。已删除该上限（并加回归断言禁止再加回来）。
- 两种模式：
  - **宽屏停靠**（≥1040 DIP）：左列占宽 280 DIP，可拖，范围卡在 **240–360 DIP**；
    拖动结束用 `Math.Clamp` 收敛，拖到极限也不会出现负宽度或黑块。
  - **窄屏覆盖**（<1040 DIP）：左列宽 0，侧栏变成固定 300 DIP 的浮层抽屉 + 半透明遮罩，
    **不再挤压主内容**；点遮罩收起。
- 从覆盖模式切回停靠模式时，把浮层专用属性（`Width`、`ZIndex`、`ColumnSpan`、对齐）全部复位，
  避免残留造成下次错乱。
- 侧栏开关仍是左上角小型矢量图标，位置固定不跳动。
- 默认收起，且**每次启动都收起**（不记忆上次展开状态）。
- 浏览与清理范围仍然彻底分离：点目录不改变清理筛选，只有「只看此文件夹的清理项」才改范围，
  且不改动已有勾选。侧栏展开/收起不重建候选。

**8) 默认窗口尺寸**
- 默认 **1000×700 DIP**（原来 1280×800），最小 **860×560 DIP**。
- 默认尺寸下顶部工具栏、导航、AI 区域、风险分区、底部操作栏都在视口内。

**9) 实机验证（真的驱动了界面）**
非提权实例 + UI Automation + 真实鼠标点击，本机 **125% 缩放**（120 DPI，已核实），
C 盘递归扫描 **1,515,437 个文件 / 271,654 个文件夹 → 239 候选 / 3 用途 / 83 位置**。
- **默认尺寸**：窗口 1250×875 物理 = **1000×700 DIP**，符合设计值。
- **导航**：只剩「清理中心 / 卸载」；扩展名入口与页面确认已移除。
- **AI 区域**：出现在页头下方，显示「✦ AI 分析 · 尚未进行 AI 分析」+
  「运行后会为候选项补充说明，帮助你判断。」+「谨慎判断：超长路径、很久没动的文件、大文件」+
  「开始 AI 分析」按钮。UI 树确认文案与按钮都存在。
- **底部操作栏**：唯一主按钮为「清理已选项目」，未选时禁用并显示「尚未选择清理内容」。
- **风险分区**：`! 需要你确认` 带暗橙色带与副标题，右侧统计「3 类 · 85 个位置 · 空间未知」；
  建议清理区为空时整段不显示。
- **侧栏停靠**（1500×950）：左列出现，搜索框/浏览摘要/「只看此文件夹的清理项」都在。
- **侧栏覆盖**（收窄到最小 860 DIP）：抽屉浮在主内容上 + 遮罩，**主内容没有被压缩**，
  不存在逐字换行、底部按钮被遮挡或空白列。
- **最大化 / 恢复**后布局正常，与停靠态画面一致。
- **性能**：`purpose-home rows created=0 candidates=3` —— 首页只建 3 行，239 条候选未进可视树。

**10) 测试**
- Release 编译 0 错 0 警
- `SafetyCheck` **455 PASS / 0 FAIL**（本轮新增：分区独立视觉属性与语义、
  大文件不进建议清理、折叠组已选提示、清理前检查范围/需确认数/执行集合一致、
  保留项即使被硬设选中也不进检查范围）
- `UiRegressionCheck` 全过（本轮新增：按钮语义、清理前检查页四类数字与两个按钮、
  确认走既有预检链路、执行中/完成态、AI 四态与文案、AI 不写风险、AI 建议选择范围、
  无误导文案、面包屑+Esc、教学句已删、估算改悬停、默认/最小窗口尺寸、
  侧栏无 MaxWidth、240–360 限制、覆盖抽屉与遮罩、模式切换复位）
- `CleanAnalyzerCheck` PASS · `AiNoteParserCheck` 全部通过 · `AppRecommendationCheck` All passed
- 大数据：**300k 候选** snapshot 342 ms / grouping 1665 ms（14 用途 10000 位置）；
  100k 单位置展开 103 ms、只建 100 行（虚拟化有效）
- `git diff --check` 干净

**11) 发布 v1.5.0**
- 唯一推荐启动：`dist/DashaoHuo-1.5.0-20260912-win-x64/AiDiskCleaner.exe`（FileVersion **1.5.0.0**）
- 唯一推荐压缩包：`dist/DashaoHuo-1.5.0-20260912-win-x64.zip`（114.5 MB）
- SHA256：`32E51A6D441A9C4F01BA7EEA45F6019A892ED37BDA5649B56E6B8F0948C40C18`
- self-contained；19 项依赖核对通过（sidecar 哈希一致、SteamHelper / StoreAppHelper 等齐全）
- 1.1.x / 1.2.0 / 1.3.0 / 1.4.0 旧目录**原样保留未删**

**12) 本轮发现但\*\*没有擅自修改\*\*的问题（需要你决定）**
**非管理员（递归降级）模式下清理功能整体不可用。**
- 现象：非提权实例扫描后日志是 `layers(...) files=239 selectable=0`，
  所有位置行都显示「无 可选项」，用户**一个都勾不上**，因此也进不了清理前检查页。
- 根因：`ProtectedPaths.IsProtectedEntry` 里有一句 `if (e.Parent == null) return true; // 盘符根`。
  `MftScanService` 会设 `c.Parent = node`（已确认），但 **`RecursiveScanService` 从不设置 `Parent`**，
  于是递归模式下每个条目的 `Parent` 都是 null → 全部被判为受保护 → `CanOffer` 全 false →
  `CanDelete` 全 false → `selectable=0`。
- 这是**上一版就存在**的问题（v1.4.0 的非提权日志同样 `selectable=0`），不是本轮引入；
  提权走 MFT 时正常（该实例日志为 `selectable=265753`）。
- 修法（未执行）：在 `RecursiveScanService` 建子节点时补 `Parent` 指向，与 MFT 路径口径一致。
- **为什么不顺手改**：这一改会让非管理员模式下**大量内容变成可删**，
  属于「扩大可删除范围」的行为变更，超出本轮 UI 范围，也触碰你反复强调的安全红线。
  需要你明确同意后再单独作为一次修复来做。
- 影响：本轮「清理前检查页」的**点击链路无法在真机走通**（没有可勾选的候选），
  该页面的数字逻辑改用 `SafetyCheck` 单测覆盖（9 条断言），页面结构与接线由
  `UiRegressionCheck` 覆盖，但**没有截到该页面的真实截图**。

**13) 仍未完成 / 已知限制**
- **清理前检查页未做真机视觉验证**（原因见上一条）。
- **AI 面板的「分析中 / 已完成 / 失败」三态未做真机视觉验证**：
  本机没有配置可用的 AI provider，无法真实触发分析；三态由 `UiRegressionCheck` 断言覆盖。
- **100% / 150% 缩放下未逐项检查**：本机固定 125%（已核实 120 DPI），
  只验证了 125% 下的默认尺寸换算、停靠/覆盖切换、最大化和窄窗口。100% 与 150% 未实测。
- **实盘删除仍未执行**：按约定不在真实用户文件上做删除验收；
  「确认清理」之后走的是既有链路（预检 → 确认框 → 执行），本轮只验证到「进入检查页」这一层。
- **窗口拖动分隔条改宽度未做真机拖动**：UI Automation 不驱动 `GridSplitter` 拖拽；
  上下限逻辑（`Math.Clamp` + Min/Max）由回归断言覆盖，未手动拖验。
- 未覆盖：真实勾选→确认→执行→部分失败的完整闭环；键盘 Tab 顺序逐项走查；
  长名称/长路径/大数字在极端数据下的逐项目视。

### 2026-09-11  DDWking（第十一阶段：导航精简 + 侧栏化 + 风险归属修正 · v1.4.0）
> 起因：顶部四个入口（清理/扩展名/卸载）里，扩展名统计没人要；「文件夹」是个大文字按钮；
> 顶部塞了候选总数、MFT 长句、百万文件数；底部还有第二条常驻统计栏。
> 更严重的是**风险归属**：截图里「大文件」「很久没动的文件」出现在「建议清理」里，
> 但它们的说明又写着「需要你自己一个个看」—— 这是把需确认内容混进了建议清理。

**1) 移除「扩展名」功能（不是隐藏，是连计算一起删）**
- 删掉顶部导航项、整个扩展名页（`ExtPane`/`ExtGrid`/`ExtTitle` + 4 个列定义）、
  `ExtStat` 模型、`ShareWidthConverter`、`ShowExtStats`/`BuildExtStats`/`CollectExt`、
  缓存字段（`_extCache`/`_extCacheKey`/`_extGeneration`）、`_extFilter` 及其子树筛选
  （`SubtreeHasExt`/`ExtOf`）、`ExtGrid` 的两个事件、以及只服务它的文案
  （`Loc.ExtType`/`TabExt`/`NoExt`/`TypeName`/`Loc.Category`/`Ext`/`Pct`）。
- **导航索引改成枚举**：`private enum RightTab { Clean, Uninstall }`。
  原来是 0 清理 / 1 扩展名 / 2 卸载，删掉中间那页后「卸载」会错位到别的页面。
  另外两处 `ShowRightTab(0)` 也一并改成枚举，回归脚本加断言禁止数字索引再出现。
- **保留**与扩展名无关的能力：`FileClassifier`（文件类型识别）、`Path.GetExtension`（规则判断）、
  `AppSignatures`（清理规则）、文件打开/图标、文件搜索都没动。

**2) 「文件夹」改成小型矢量侧栏开关**
- 左上角、紧贴应用名左侧，开/关位置不变；点击区 30×28，支持键盘焦点与可访问名称。
- 图标是 **XAML 矢量图**（`Viewbox` + `Canvas` + 圆角矩形 + 左侧竖栏），
  不用可能缺字的特殊 Unicode 字符。
- 悬停/可访问名称随状态切换：「显示文件浏览器」/「隐藏文件浏览器」。
- 展开宽度约 280 DIP，可拖动；窄窗口自动收窄（`Math.Min(280, avail - 420 - 12)`），
  保证主内容至少保住 420 DIP。
- 收起时左列与分隔条都置 0 并隐藏面板 —— **不残留空列、空白、分隔条**。
- 默认收起，且**每次启动都收起**（不记忆上次展开状态）。

**3) 文件搜索移入侧栏，浏览与清理范围彻底解耦**
- 「搜索路径 / 文件名」从顶部移进侧栏顶部，只服务文件浏览器；清理页的范围/搜索不共用。
- 侧栏新增浏览摘要（文件数/目录数/占用）—— 原先在全局底栏。
- **关键：点目录不再联动清理页**。同时补上一直缺失的显式操作
  「只看此文件夹的清理项」，执行它才把当前浏览目录设为清理范围。
- 范围状态与浏览状态是**两个独立变量**（`_cleanScopeRoot` vs `_current`），
  不再共用一个字段造成隐式联动。
- 范围生效后清理页顶部出现提示条：「当前范围：<路径> ｜ 清除范围」。
- 范围提示会**额外说明范围外仍有已选项**（「范围外还有 N 个已选项，仍会被处理」）——
  设置范围只影响显示，不清除也不扩大选择。
- 在盘符根目录时该按钮禁用（范围等于全盘，没有意义）。

**4) 顶部精简为三层**
- 第一层工具栏：侧栏开关 · 应用名 · 磁盘 · 扫描 · 简洁容量信息 · 设置。
  容量信息改成右对齐 + 省略号收尾，空间不足时不挤压扫描/设置按钮。
- 第二层：清理 / 卸载（只剩两个）。
- 第三层：`操作状态 · 扫描完成 · 部分内容未检测`（右侧「扫描详情」/「更多」）。
  操作状态与覆盖完整度合成一行，不再各占一段。
- 删掉重复的「清理」页标题（已选中的导航下方不再重复一遍）。
  保留位置页/明细页真正有用的标题、面包屑与返回入口。
- 删掉常驻教学长句「先选用途，再选具体位置…」。
- 顶部不再出现 MFT 长句与百万文件数；候选总数、父子目录去重、重复路径合并、
  扫描耗时**全部移入「扫描详情」**（数据一条没删，只是改了默认呈现层级）。
- 覆盖完整度用词从「检测未跑完」（像还在跑）改成**「扫描完成 · 部分内容未检测」**。

**5) 风险归属修正（本轮最重要的安全改动）**
两个真实缺陷，都会把**需确认**的内容推进「建议清理」：
- **缺陷 A：签名分类没校验子目录命中。** 像「Chrome 缓存」的 Needle 只到
  `\google\chrome\user data`，真正的缓存还得落在 `\cache` / `\code cache` / `\gpucache`。
  以前 `Classify` 不看 `SubHit`，于是 `User Data\Default\History`（书签/历史——**用户数据**）
  也会被认成「Chrome 缓存」并标成 **Safe**。已修成：有 Sub 要求但没命中 ⇒ 一律按「认不出来」处理，
  把风险交回规则判定（与 `IsSafeCache` 同一口径）。
- **缺陷 B：签名会把「大/旧/长路径」的风险降级。** 一个 8GB 的普通文件只要路径挨着某个缓存目录，
  就被 `Classify` 的 Safe 覆盖，掉进「建议清理」。新增 `ICleanRule.RiskIsAuthoritative`：
  `LargeFileRule` / `OldFileRule` / `LongPathRule` / `ScanCompareRule` 置 true，
  签名只能补充分类与说明、**不得降级风险**，也不再把它归进缓存类用途。
  小目录缓存规则（临时/缓存）保持 false —— 那才是它们的主要证据。
- 顺带修掉一个文案自相矛盾：重复项的说明必须写清「和哪个文件重复」「本轮检测有没有跑完」，
  不能被签名的大白话顶掉（新增 `CleanRuleHit.ReasonIsEssential`）。
- **分组拆分本来就按真实候选风险**（`(用途, 风险档)`），同一用途同时有 Safe 与 Confirm 会拆成两行，
  各自子集；「大/旧」单独存在时永远进「需要你确认」且默认不勾选。已补测试锁死这些行为。
- 若后续发现某条底层规则的安全性有争议，应记录并单独讨论，**不擅自扩大可删除范围**。

**6) 分区标题与列表行**
- 分区标题改成**左标题 / 右统计**：`▸ 需要你确认` … `3 类 · 85 个位置 · 空间未知`。
  去掉整圈按钮式边框，改用留白 + 一条细分隔线。
- 展开箭头 `▾`/`▸`，折叠/展开不只靠颜色；整行可点，带键盘焦点样式与可访问名称。
- **折叠分区里有已选项时标题必须写明**：「（已选 N 项，分布在 M 个位置）」。
- 分组空间标为「约」，未知时不硬套「约」；空分区（建议清理没有候选时）整段不显示。
- 列表行：名称/箭头负责进详情，**复选框只负责选择**，空白点击不改选择（防误选）；
  行高约 64–72 DIP；每行的描边按钮改成轻量 `›` 箭头（带悬停提示 + 可访问名称）；
  右侧数字形成稳定列并右对齐；影响说明用可读的 `TextDim`，不用最灰的色。
- 清理文案去掉无证据的绝对保证（「收藏夹和密码不受影响」「你的文档不在里面」
  「聊天记录不受影响」等 14 处），改成按实际规则覆盖范围的有限说明
  （例如「缓存子目录里的网页缓存…；书签和密码不在这个范围内」）。

**7) 底部只留一个操作栏**
- 删掉第二条常驻统计栏（文件数 | 大小 | 耗时 | 提示），内容移入扫描详情与侧栏。
- 唯一操作栏：主行「已选 N 个位置 · 预计处理 X」（加粗突出），
  次行「包含 M 个候选项」（含目录时称「项」不称「文件数」）+「查看已选」文字入口；
  右侧「清空选择」+「检查并清理」（唯一主按钮，未选则禁用）。
- 全盘不同层级（首页/位置页/明细）共用同一个全局选择摘要。
- 卸载页有自己的操作栏，切页时第三层状态不串台。

**8) 顺带修掉的两个真 bug（只有真跑界面才看得出来）**
- `UpdateScanStateLine()` 里被我误插了 `UpdateScanStateLine()` 自调用，导致**无限递归崩溃**
  （扫描完成后进程直接退出）。已移除。
- `ApplyUi()` 里残留 `TreeToggleBtn.Content = Loc.Folder`，把矢量图标覆盖回「文件夹」文字。
  已改为只设悬停/可访问名称。

**9) 实机验证（真的驱动了界面）**
非提权实例（`dotnet AiDiskCleaner.dll`，因此走递归降级扫描）＋ UI Automation ＋ 真实鼠标点击，
本机 C 盘实测 **1,515,352 个文件 / 271,591 个文件夹 → 239 候选 / 3 用途 / 83 位置**。
逐张核对截图确认：
- **导航**：只剩「清理 / 卸载」两个按钮，扩展名入口与页面全部消失。
- **侧栏**：默认收起；点图标展开（左列 + 分隔条出现，搜索框/浏览摘要/「只看此文件夹的清理项」都在），
  再点收起后画面与展开前**逐字节一致**（哈希相同）—— 无空列/空白/分隔条残留。
- **侧栏图标**：放大后确认是矢量分栏图标（圆角矩形 + 左侧填充竖栏），不是文字。
- **浏览不联动范围**：先把范围设为 `C:\迅雷下载`，再浏览到 `Users`，
  范围提示仍是 `当前范围：C:\迅雷下载` —— 四.4/四.9 得到验证。
- **范围生效**：分区统计从 `3 类 · 85 个位置` 变成 `1 类 · 2 个位置`，提示条出现「清除范围」。
- **风险归属**：修复后首页「建议清理」为空，`超长路径 / 很久没动的文件 / 大文件` 全部落在
  「需要你确认」；大文件各位置显示「无 可选项」（`CanDelete=false`），不会被整组带去删。
- **位置页**：`返回` + 面包屑「全部清理」+ 第三层状态行；行内显示可识别的软件/文件夹名
  （迅雷下载 / NVIDIA 缓存 / Python venv / Steam 游戏库 / 用户目录…）、路径默认收起、
  「查看路径」「无 可选项」、`›` 箭头、行高约 75px。
- **文件明细**：右侧面板打开，含位置名、`本位置共 12 项`、查看路径/复制路径、搜索框、选择范围、
  `当前显示 12 / 12 个文件`、`需要你确认（12 项 · 7.4 G）`、`已全部加载`、`关闭`；
  面板内**没有**删除按钮；打开时左侧位置列表保留并高亮当前行。
- **进行中状态**：重新扫描时第三层显示「正在扫描磁盘…」、主按钮禁用、顶部出现「停止」。

**10) 验证结果**
- Release 编译 **0 错 0 警**
- `SafetyCheck` **432 PASS / 0 FAIL**（新增风险归属 19 条：SubHit 口径、真规则 `RiskIsAuthoritative`、
  用户数据不得标 Safe、同用途按风险拆行、折叠分区已选提示等）
- `UiRegressionCheck` 全过（新增两页导航、枚举索引、扩展名页连计算一起消失、
  侧栏矢量图标/可访问名称/无残留、搜索在侧栏、浏览不联动范围、单一操作栏等断言）
- `CleanAnalyzerCheck` PASS · `AiNoteParserCheck` 全部通过 · `AppRecommendationCheck` All passed
- `git diff --check` 干净

**11) 发布 v1.4.0**
- 唯一推荐启动：`dist/DashaoHuo-1.4.0-20260911-win-x64/AiDiskCleaner.exe`（FileVersion **1.4.0.0**）
- 唯一推荐压缩包：`dist/DashaoHuo-1.4.0-20260911-win-x64.zip`（114.5 MB）
- SHA256：`49BAC81DF9AE8B55AA2ECFF036AA5A870A679E3DAE9E937E3895C1CEB9CD88BC`
- **self-contained**；依赖 19 项核对通过（sidecar 哈希一致、SteamHelper / StoreAppHelper /
  UninstallTools / KlocTools / ObjectListView / OpenAI / BCU 许可）
- 1.1.x / 1.2.0 / 1.3.0 旧目录**原样保留未删**；版本号递增，不会误开旧包。

**12) 仍未验证 / 已知限制**
- **实盘删除未执行**：按约定不在真实用户文件上做删除验收。三个风险档的预检/确认/执行集合一致
  由 `SafetyCheck` 覆盖，但**没有在真盘上端到端跑过一次删除**。
- **提权实例的界面未驱动验证**：提权窗口受 UIPI 限制，输入事件与非提权 UIA 都进不去；
  截图验证是在非提权实例上完成的，因此走的是**递归降级扫描**，
  提权下的 MFT 完整扫描路径本轮没跑到。
- **整组勾选/三态未在真机点中**：UI Automation 的 `TogglePattern` 不触发 WPF 的 `Click`
  （`LayerCheck_Click` 收不到），而按坐标点击整行会命中「进详情」而不是复选框。
  这两个行为由 `SafetyCheck`（组勾选只作用于 `CanDelete`、三态、折叠提示）覆盖，**未在真机点验**。
- 未覆盖：真实勾选→确认框→执行→部分失败的完整闭环；高 DPI（125%/150%）缩放下逐项目视；
  键盘 Tab 顺序逐项走查。
- 已知取舍：位置行数上限 4000/用途（超出标注「另有 N 处未逐条列出」，总数已含）；
  认不出软件按文件夹回退不猜名；空间为估算值。

### 2026-09-11  DDWking（第十阶段：清理界面信息层级与交互 · v1.3.0）
> 起因：上一版把用途 / 位置 / 文件做成**三张表同屏纵向堆叠**，一屏里三个滚动区，用户不知道自己在看哪一层。
> 这一版改成**逐层进入、单一主操作**：主区域同一时刻只有一个信息主角。

**1) 取消三表堆叠，改为主区域分层导航**
- 删掉 `CleanLayers` 那个三行 Grid（`PurposeRow` / `LocationRow` / `DetailRow`）以及配套的
  `PurposeGrid` / `LocationPanel` / `LocationGrid` / `CloseLocationLayer` / `CloseDetailLayer` /
  `UpdateCleanSelHint` / `BindPurposeGridFiltered`。
- 现在主区域是 `Grid` 两列：**第 0 列是页面**（清理首页 / 位置页，互斥可见，切换时替换主内容，
  不在上级下面再插一张表），**第 1 列是文件详情面板**（默认宽度 0，按需打开）。
- 导航：首页点「查看位置 ›」→ 位置页；位置页「返回」→ 首页；「查看文件 ›」→ 打开右侧明细；
  明细「关闭」→ 回位置页。返回时**恢复滚动位置**（`_purposeScrollOffset` / `_locationScrollOffset`），
  勾选状态挂在条目实例上本来就不会丢。
- 窄窗口（< 820px）自动把明细切成**独立整页**（隐藏列表、明细占满主区域），
  返回键此时充当「关闭」，不硬把主列表压到读不了。

**2) 清理首页**
- 上半页只有两个风险分区：**建议清理（默认展开）/ 需要你确认（默认折叠）**，
  分区标题就承担风险表达（`▾ 建议清理 · 3 类 · 7 处 · 18.1 GB`），
  行里**不再重复绿色「建议清理」**，也**没有单独的类型列**、不显示实际路径。
- 一行 = 一个用途：左侧勾选框 / 中间用途名 + 一行清理影响 / 右侧预计空间 + 位置数 + 「查看位置 ›」。
- **折叠的分区不渲染行**（`Visibility` 绑 `IsExpanded`）—— 这条是实机截图抓出来的 bug：
  默认折叠的「需要你确认」当时仍然把行画了出来。
- **勾选与导航彻底分离**：点勾选框只改选择（`LayerCheck_Click`），点「查看位置 ›」只进详情
  （`PurposeOpen_Click`），两个 handler 互不调用。

**3) 清理位置页**
- 标题区：`‹ 返回` + 用途名 + 面包屑「全部清理」+ `N 个位置 · 预计处理 X`。
- 一行 = 一个清理位置（某个软件或某个实际文件夹）：软件名 / 清理原因 / 已选比例 / 预计空间 / 文件数 /
  「查看文件 ›」。**只有一个滚动区域**（`LocationList`，已开虚拟化）。
- 路径**默认不显示**：点「查看路径」才展开完整路径（等宽字体），并可「复制路径」/
  「在资源管理器中打开」。
- **技术标签从标题里拿掉**：以前位置标题直接用了 `AppSignatures.Describe()`，会显示
  「npm · safe cache」。新增 `FriendlyName()` 只返回软件名，`safe cache` / 风险词这类术语
  降级到悬停（`CleanLocationNode.Tech` + `HintText`），**数据没删，只是换了呈现层级**。
  认不出软件就回退真实文件夹名并标注「未识别」，不猜。

**4) 文件详情面板**
- 显示：位置名 / 本位置候选项数 + 预计空间 / 查看路径 + 复制路径 / 搜索框 / 选择范围 /
  明细表 / `当前显示 X / Y 个文件` / 「关闭」。
- 沿用上一版的按需加载：默认 100 条 + 「加载更多」；搜索跑**完整候选集**（`ApplySource`），
  能命中从未加载的页；勾选状态只有一套（挂在 `CleanItem` 上），不存在主界面与明细两套状态。
- **面板内不放醒目的删除按钮** —— 执行统一走底部全局主操作。
- 不明细里重复整套用途标题、风险分区标题与操作栏（明细自己的风险分组是表格内分组，不是页头）。

**5) 统一数字口径与单一执行入口**
- 底部固定操作栏：左侧「已选 N 个位置，包含 M 个候选项 · 预计处理 X」，右侧
  「清空选择」（次要）+「检查并清理」（**唯一主要执行入口**，没选就禁用）。
- 全盘「预计可清理 205 GB」这类**包含需确认候选**的总量不再当醒目主标题；
  它退到次级信息行（候选文件数 + 检测完成情况 + 重叠/重复命中去重说明 + 「预计」口径说明）。
- 口径用词区分：候选项数 / 位置数 / 选中的是「项」；比例写「已选 0 / 21 项」不裸写。
- 「位置数」按**去重后的稳定键**算，风险拆组不重复计同一实际位置。
- 跨页面保留选择时底部明确写「选择是全局的，包含不在当前页面的已选项」，并可「查看已选」
  核对（按用途汇总 + 占比最大的 10 项）。「清空选择」清的是全局选择。
- 选择范围菜单三种范围各自带条数：只选当前页 / 搜索结果全部 / 整个位置；
  组勾选框悬停提示「将选中整组（N 项），不是只选当前页」。
- 需确认分区默认折叠 + 组勾选只作用于 `CanDelete` ⇒ 不会被低风险整组选择带入。

**6) AI 与技术信息降级**
- 从首页移除「按 AI 建议勾选」、常驻「AI 已配置」状态灯、与主按钮并列的 AI 大按钮。
- AI 解释收进位置/明细的「更多」菜单（`CleanMore_Click` → `AiExplainHere`）；
  隐私设置与发送范围完全没动（仍默认脱敏路径）。
- 长长的 MFT 诊断条收敛成页头一句 `扫描完成 · 部分内容未检测` + 「扫描详情」按钮，
  MFT 来源 / 耗时 / 读写计数 / 权限问题 / 重解析点 / 硬链接 / 孤儿记录全在详情里，
  **一条数据都没删**。不完整绝不显示成完整（`ScanPartialShort` + `DetectedPartial`）。

**7) 视觉**
- 正文换成可读界面字体（`Microsoft YaHei UI / Segoe UI`），等宽只留给路径与技术信息
  （新增 `MonoFont` 资源）。
- 清理影响用 `TextDim`（可读）而不是最灰的 `TextMuted`；数字右对齐；
  减少成排按钮与重复分隔线；风险色只在分区标题与类型列出现一次。

**8) 实机验证（这次真的跑了界面）**
本机桌面可交互（1920x1200），用 PowerShell + Win32 输入事件驱动真实窗口，并逐张核对截图：

- **首页**：两个风险分区；「建议清理」展开显示 3 个用途行；「需要你确认」折叠且**不渲染行**；
  勾选后底部立刻变成「已选 46 个位置，包含 213,062 个候选项 · 预计处理 27.0 G」，
  「检查并清理」由禁用变可用；未选时显示「尚未选择清理内容」且按钮禁用。
- **位置页**：「超长路径」→ 「1 个位置 · 预计处理 空间未知」，行内显示 `npm` /
  「你装的开发工具留下的安装包备份」/「查看路径」「无 可选项」/「3 个文件」/「查看文件 ›」，
  顶部有 `返回` + 面包屑「全部清理」。
- **文件详情**：右侧面板打开，含位置名、本位置共 3 项、查看路径 / 复制路径、搜索框、
  选择范围、`当前显示 3 / 3 个文件`、表格（类型/名称/大小/说明）、建议清理分组、`已全部加载`、`关闭`；
  面板内**没有**删除按钮。
- **窄窗口 660x900**：明细切成整页；`620x880` 页头不重叠（清理 / 先选用途… / 扫描完成·部分内容未检测）；
  `620x620`、`520x900` 无遮挡裁切。
- **真实数据规模**：本机 C 盘 **265,797 个候选 → 19 个用途 / 328 处位置，分组耗时 935 ms**（后台）；
  非提权实例跑出 1,515,167 个文件、199 候选 / 6 用途 / 75 位置。
- 日志佐证：`purpose-home rows created=0 candidates=19` —— 首页**只建 19 行**，
  265,797 条候选一个都没进可视树。

**9) 过程中修掉的 3 个真问题**
1. 折叠分区仍然渲染行（截图发现）→ 行容器 `Visibility` 绑 `IsExpanded`。
2. 窗口变窄时页头标题与扫描状态文字**重叠**（截图发现）→ 右侧 Auto 列改为只放按钮，
   文本全部并入星号列并允许换行。
3. 位置标题泄漏技术标签「npm · safe cache」（截图发现）→ 新增 `FriendlyName()`，术语降级到悬停。

**10) 顺带修掉的工具链隐患**
- 仓库里的 `.ps1` 都没有 UTF-8 BOM。PowerShell 5.1 无 BOM 时按 ANSI 读，
  中文一旦进**字符串字面量**就会静默损坏（这次实际踩到）。已给
  `tools/CleanAnalyzerCheck/Run.ps1`、`tools/UiRegressionCheck/Run.ps1` 加 BOM。
- `UiRegressionCheck` 里一条断言写成 `Assert-True '...' (expr).Count -eq 0`，
  PowerShell 把 `.Count` 当成 `$ok` 实参导致恒 FAIL。已改成先算变量再断言。

**11) 验证结果**
- Release 编译 0 错 0 警
- `SafetyCheck` **404 PASS / 0 FAIL**（新增分区/单层页面/标题无技术标签 26 条）
- `UiRegressionCheck` 全过（新增单层导航、分区默认折叠、唯一主按钮、AI 降级、诊断收敛等断言）
- `CleanAnalyzerCheck` PASS · `AiNoteParserCheck` 全部通过 · `AppRecommendationCheck` All passed
- `git diff --check` 干净

**12) 发布 v1.3.0**
- 唯一推荐启动：`dist/DashaoHuo-1.3.0-20260911-win-x64/AiDiskCleaner.exe`（FileVersion **1.3.0.0**）
- 唯一推荐压缩包：`dist/DashaoHuo-1.3.0-20260911-win-x64.zip`（114.5 MB）
- SHA256：`AB80F1DEFE42053A6E6F41C3C85B2FE489EEDDC12367E47608734DED3446B753`
- **self-contained**（自带 .NET 8 桌面运行时）；依赖 19 项核对通过，含 sidecar（哈希一致）、
  SteamHelper / StoreAppHelper / UninstallTools / KlocTools / ObjectListView / OpenAI / BCU 许可。
- 1.1.x / 1.2.0 旧目录**原样保留未删**；版本号递增，不会误开旧包。

**13) 仍未验证 / 已知限制**
- **实盘删除未执行**：按约定不在真实用户文件上做删除验收。删除预检、逐项结果、部分失败保留
  这些逻辑没改，但**没有在真盘上端到端跑过一次删除**。
- **管理员（提权）实例的界面未驱动验证**：提权窗口受 UIPI 限制，非提权 shell 无法用输入事件驱动，
  截图验证是在非提权实例（`dotnet AiDiskCleaner.dll`，因此走递归降级扫描）上完成的。
  提权下的 MFT 完整扫描路径本次没跑到。
- 未覆盖：真实勾选→确认框→执行→部分失败的完整闭环；高 DPI（125%/150%）缩放下的目视检查；
  键盘 Tab 顺序与可访问名称的逐项走查。
- 已知取舍：位置行数上限 4000/用途（超出会标注「另有 N 处未逐条列出」，总数已包含）；
  认不出软件按文件夹回退不猜名；空间为估算值。

### 2026-09-11  DDWking（第九阶段：清理结果分层归类与按需展示 · v1.2.0）
> 把清理面板从「一行一个文件」改成 **用途分类 → 软件/实际清理位置 → 文件明细**。
> 首页不再绑几十万条记录；完整候选一条不丢；**归类没有扩大任何删除权限**。

**1) 用途分类改由规则声明（证据驱动）**
- `CleanPurpose` 枚举 + `ICleanRule.Purpose` / `CleanRuleHit.Purpose`：
  分类跟着「这条规则凭什么认为它可清理」一起产出，**不在展示层按路径名猜**。
- `CleanItemFactory` 用签名分类做**受限细化**：只在缓存类用途内部细化
  （临时文件/应用缓存/开发缓存/浏览器缓存/应用日志）。
  **大文件 / 旧文件 / 重复文件留在原用途** —— 一个 8GB 视频即使躺在浏览器缓存目录里，
  也仍然是「大文件·需确认」，不会因为签名命中就看起来像默认可清理。风险等级完全没动。

**2) 分组服务（`Services/CleanGroupingService.cs`，纯计算可后台跑）**
- **位置锚点只收窄不放大**：签名根 → 已知缓存目录 → 条目自己的父目录。
  `ForbiddenAnchors` 明确禁止把 AppData、用户主目录、Program Files、Windows、盘符根
  这类宽泛位置当锚点；落到那些位置就退回更窄的父目录。
  「临时目录整棵子树汇总成一个位置」靠已知缓存目录表实现，不做通用推断。
- **位置键稳定**：`dir|<归一化锚点路径>` 或 `dup|<组标识+保留/多余>`，**不含显示名**
  （显示名会随语言/文案漂移）。同目录不同文件共用键。
- **风险拆组**：同一用途里 Safe / Confirm 拆成两行；同一位置里不同风险也拆开，
  低风险勾选带不动需确认项。
- **位置计数口径**：用**去重后的稳定键**。同一文件夹在「建议清理/需要你确认」各出现一次仍算**一处位置**，
  不因风险拆组翻倍；也不是分类数。
- **空间口径**：只算可删的、且不被同用途内目录候选覆盖的（父子重叠不重复计），
  按**完整候选**算 —— 位置行被显示上限截断时，总数与预计空间照样不少算。
- **位置行数上限** `MaxLocationsPerPurpose = 4000`，超出部分用 `LocationsNotShown` 如实标注
  「另有 N 处未逐条列出（总数已包含它们）」，候选与空间不受影响。
- 重复文件保持**「重复组」结构**：不拆成互不关联的文件夹，保留项与多余项分成两个位置，
  保留项 `CanDelete=false`，整组勾选也动不了它。
- 纯函数、可取消、不碰 WPF；不产生任何 `DeletionTarget`。

**3) 首页 / 位置 / 明细三层界面**
- 首页 `PurposeGrid`：一行 = (用途 × 风险档)，显示 用途 / 类型 / **位置数** / 文件数（次要）/ 预计可清理 / 清理影响。
  默认视图就是它，**不绑文件明细**。
- 点用途展开 `LocationGrid`：软件名或文件夹名 / 实际路径 / 已选·可删 / 预计可清理 / 清理原因，
  带「查看文件」「打开」入口。认不出软件就照实显示文件夹名并标记「未识别」，不猜名字。
- 「查看文件」才打开 `DetailPanel`（复用原有文件表格与列、右键菜单）。
- 顶部摘要改成三层文案：第一行「找到 N 处可清理位置，预计可清理 X」（位置数是去重口径），
  第二行「涉及 N 个候选文件 · 检测已完成 / **检测未跑完 —— 数字只是下限**」，
  第三行口径说明「预计是估算值，实际可用空间会随回收站清空而变化」+ 重叠/重复命中去重说明。
- 布局未回退：启动仍进清理面板并铺满，文件浏览器/目录树仍默认隐藏，顶栏「文件夹」仍可展开。

**4) 文件明细按需加载（`Services/CleanItemPager.cs`）**
- 默认 100 条 + 「加载更多」；显示「当前显示 X / Y 个文件」。
- **搜索针对该范围的完整候选集**（`ApplySearch(_source, q)`），能命中从未加载过的页。
- 勾选挂在同一个 `CleanItem` 实例上，翻页 / 搜索 / 折叠都不丢。
- 三态勾选（全选/半选/未选）；范围明确分三种且**文案与范围一致**：
  「只选中当前页」「选中搜索结果全部」「选中整组」，各自带条数。

**5) 选择与删除安全（最高优先级，逐条对齐）**
- 组勾选**只改已有候选的 `Selected`**，且 `if (!item.CanDelete) continue;`
  —— 保留项、受保护项、规则本就不允许删的（长路径等）永远选不中。
- **不把分组路径变成递归删除目标**：分组是索引，删除目标是候选自己的路径，逻辑未变。
- 现有目录候选继续走原有目录安全判定与删除预检，**没有因为汇总产生新的目录删除权限**。
- 「实际执行集合」= `_layered.SelectedItems`（完整候选里筛 `Selected && CanDelete`）；
  确认框按同一集合算**位置数 / 候选数 / 预计空间 / 需确认风险数**，按钮上的数字、确认框、执行三者一致。
- 删除仍走原 `DeletionCoordinator` 预检 + 逐项结果；
  删除后 `RemoveDeletedFromLayers` **只摘掉真正成功的项**，失败项留在原地并保留原因，然后重建分层刷新计数与总计。
- 父子目录重叠、同路径重复命中沿用并补强原有口径（重叠不重复计空间、大小写不敏感去重），都有断言。

**6) 测试与性能**
- `SafetyCheck` **379 PASS / 0 FAIL**（新增分层归类 **106 条**：基本分组 14、锚点 12、风险拆组 6、
  选择安全 16、重复文件 13、分页搜索 20、规模 12、取消 4，以及位置计数/截断口径）。
- 大数据实测（在测试进程里量）：
  - **30 万条** 分层归集约 **850 ms**（后台），产出 **14 个首页行 / 10,000 处位置**，
    首页行数与候选数完全解耦；
  - 单个位置含 **10 万条** 时，展开只渲染 **100** 行，耗时 **约 90 ms**，不创建全量行容器；
  - 6,000 个散落目录触发位置上限时，候选数与预计空间仍按全量算。
- `UiRegressionCheck` 62 条全过（新增分层视图 12 条 + 选择安全 8 条：首页绑用途、明细只走分页器、
  搜索覆盖未加载页、三态、组勾选跳过不可删、确认用全选集合、分组不改风险、锚点不收窄到宽泛位置、
  只摘成功项、检测未完成会标注）。
- 其余：`CleanAnalyzerCheck` PASS · `AiNoteParserCheck` 全部通过 · `AppRecommendationCheck` All passed ·
  `git diff --check` 干净 · Release 编译 0 错 0 警。

**7) 发布 v1.2.0**（版本递增，未回退旧版本）
- 目录：`dist/DashaoHuo-1.2.0-20260911-win-x64/`（279.5 MB，**self-contained**，自带 .NET 8 桌面运行时）
- **唯一推荐启动**：`dist/DashaoHuo-1.2.0-20260911-win-x64/AiDiskCleaner.exe`（FileVersion 1.2.0.0）
- **唯一推荐压缩包**：`dist/DashaoHuo-1.2.0-20260911-win-x64.zip`（114.5 MB）
- SHA256：`B312F4DCC27D4109F2E4BA5D06BE27AFEE34F6B3D7133305C92A0C42D615DB04`
- 依赖完整性 19 项核对通过，含 **sidecar**（与 `sidecar/AiSidecar.exe` 哈希一致）、
  SteamHelper / StoreAppHelper（exe+dll+deps+runtimeconfig）、UninstallTools / KlocTools / ObjectListView / OpenAI、
  自包含运行时、BCU 第三方许可。
- `dist/DashaoHuo-1.1.0-*` 与 `1.1.1-*` 旧目录**原样保留未删**；新包版本号不同，不会误开旧包。

**8) 尚未完成的验证（必须实机补）**
- **WPF 界面实际运行未验证**：以上是编译 + 模型层断言 + 静态检查。
  「点用途展开位置」「查看文件翻页/搜索」「三态勾选」「确认框数字与执行一致」都还没有在真实窗口里点过。
- 30 万条下的**实际行容器数与交互响应**要看 `%LOCALAPPDATA%\DashaoHuo\app.log`：
  `purpose-grid rows created=…` / `location-grid …` / `clean-grid …` 三行；
  行数接近候选数会额外打 WARN。
- 实盘删除**未执行**（按要求不在用户真实文件上做删除测试）。
- 已知限制：位置行数上限 4000/用途（超出会标注但不逐条列出）；认不出的位置按文件夹回退，
  不会猜软件名；空间是估算值。

### 2026-09-11  DDWking（第八阶段：清理列表大数据展示 + 取消链路）
> 起因：清理列表在 26.5 万条候选下卡顿，分析期间停止按钮消失。按「先量、再修展示、再移走同步活、再修取消、最后补重复检测预算」的顺序做。

**1) 先补分段计时（不先猜）**
- 新增 `Services/PerfTrace.cs`：按段计时并写进 `app.log`（级别 Perf），`Summary()` 输出
  「阶段=毫秒」一行，能直接区分排序 / 布局 / 读盘 / 内存压力。
- 已埋点：`finish-scan`（树构建）、`clean-pipeline`（整条流水线）、`snapshot-build:first-paint`
  与 `snapshot-build:after-duplicates`、`duplicates`（重复检测）、`clean-bind`（绑定）。
- 新增**行容器审计**：绑定后 `ScheduleRowContainerAudit` 数一遍实际创建的 `DataGridRow`，
  写 `rows created=N candidates=M virtualizingWhenGrouping=… mode=…`。
  当 N 接近 M（>50%）时直接报 **WARN**，不用人去猜虚拟化到底生效没有。

**2) 清理列表虚拟化（用户指出的真正根因）**
- `CleanGrid` 补上分组场景真正管用的那几个开关：
  `VirtualizingPanel.IsVirtualizingWhenGrouping="True"`（**默认 false，只开 EnableRowVirtualization 没用**）、
  `VirtualizationMode="Recycling"`、`ScrollUnit="Pixel"`、`ScrollViewer.CanContentScroll="True"`、
  `CacheLength="1,1"`（按页）。`UninstallGrid` 同样是分组表格，一并补上。
- 自定义 `GroupStyle`（`Expander` + `ItemsPresenter`）保留 —— 这是官方分组虚拟化认得的写法。

**3) 分类 / 统计 / 预排序全部移出界面线程，并去掉重复排序**
- 新增 `Services/CleanListSnapshot.cs`：去重 → 分类 → 统计 → 预排序的**纯计算**产物，可在后台跑。
  - 以前是对同一批数据**排两次序**（每个分类各排一次 + 拼「全部」又排一次）；
    现在只排一次，按 `(风险分组, 大小降序)`，分组建「全部」直接复用同一份列表（零拷贝、零再排序）。
  - 界面那个 `ListCollectionView` **只加分组、不加 `SortDescriptions`** ——
    源顺序已经是最终顺序，26 万条不会再在 UI 线程上被完整排一遍。
    （卸载页/残留页的排序量级只有几十条，保持原样。）
- `BuildCategories()` 删除，`RefreshCleanUi` / `ShowCleanCat` / `CurrentCleanList` 改成读快照。
- 目录过滤结果**带缓存**，同一 (分类, 目录) 不重复全量过滤。
- 分组标题求和的 `CleanGroupConverter` 也加了缓存 —— 它要对组内全量求和，之前每次重绑都重算。
- 实测：30 万条快照构建 **≈300 ms**（在后台），且断言候选总数一条不丢（`SafetyCheck` 覆盖）。

**4) 界面线程上的附加工作搬走**
- `RebuildSnapshotAsync`：快照构建在 `Task.Run`，完成后回 UI 线程一次性换掉；
  后台**不碰任何控件或绑定视图**。
- 快照保存：`ScanSnapshot.Capture` + `Save` 都进后台，**不挡首屏**；
  `Save` 改成 **先写 .tmp 再 `File.Move(overwrite)` 原子替换**，中断不会留下半截损坏 json；
  写盘前再确认一次扫描代次，旧扫描不会覆盖新扫描的快照。
- 扩展名统计（`BuildExtStats` 要遍历整棵子树，根目录下就是全盘）改为后台 + 按目录缓存 + 代次守卫。
- 隐藏面板按需初始化：卸载页**第一次被打开**时才去扫软件清单（`EnsureAppsLoaded` / `_uninstallTabVisited`）。
- `FinishScanAsync` 一开始 `await Task.Yield()`，让进度条先画出来再干重活。

**5) 停止按钮 / 取消链路 / 任务代次**
- 新增 `Services/WorkState.cs`：可取消阶段登记簿（`Scan / Analyze / Duplicates / Snapshot`）。
  **只要还有阶段在册，停止按钮就可用**。
- 修掉那个明确的 bug：`RunScan` 的 `finally` 以前无条件
  `StopButton.Visibility = Collapsed`，而 `FinishScan` 是 fire-and-forget 启动分析 ——
  扫描结束、分析还在跑的那段时间根本没有停止入口。现在 finally 只调 `UpdateStopButton()`。
- 状态拆开：扫描中 → 清理分析中 → 重复检测中 → （取消中 → 已取消）/ 完成。
  点停止后按钮立刻切成「正在取消…」并不可再点，不等任务真正退出。
- 新增 `InvalidateFollowUpStages()`：新扫描开始时取消并作废上一轮的重复检测/快照/分析，
  避免它们往已经清空的报告上写、或把旧结果盖到新界面。
- 所有进度回调都加了代次守卫（`myGeneration != _xxxGeneration` ⇒ 直接丢弃），
  旧任务不能动新任务的进度条，也不会隐藏新任务的进度 UI。
- 新扫描开始会清掉上一轮的 `_report` / 快照 / 列表绑定 —— 不能让用户照着过期清单去删除。
- 取消后已有结果**继续可看**，并明确标注「重复检测没有跑完」。

**6) 重复检测的取消、预算与进度**（`Services/CleanRules/DuplicateDetector.cs` 重写）
- 取消检查加到：大小分组循环、**硬链接身份探测循环**（每 64 个）、每个文件、**每个读取块**。
- 计数统一 `long`：`bytesRead` / `BytesRead` / `BudgetBytes` 全是 long，单文件与总量都不再溢出。
- 预算**按块扣减**：单块读取前先判断是否超预算，超了就置 `BudgetExhausted` 并停下 ——
  以前是在处理文件**之前**检查，单个超大文件能一口气冲过 4GB 预算。
- 明确返回 `DuplicateScanResult { Complete / BudgetExhausted / Truncated / Note }`，
  **撞预算就如实说「没跑完」，绝不假装检测完毕**。
- 细分进度 `DuplicateProgress`（阶段 / 已处理 / 总数 / 已读字节 / 找到组数 / 百分比），
  **每 300 ms 合并一次**，收尾强制报一条。
- 卷类型查询加缓存（`ConcurrentDictionary`），不再逐条文件问盘。
- 身份探测改用新增的 `IFileSystemProbe.TryGetIdentity`（只开一次 `CreateFile` 读 file id），
  不再走完整的删除预检式探测。
- **读到一半长度对不上**（文件被截断）单独记 `SkippedChanged`，**不算已验证重复**。
- **重复检测拆成独立可取消阶段**（`Services/CleanRules/DuplicateScanService.cs`）：
  `CleanAnalyzer.Analyze(includeDuplicates: false)` 先出普通清理列表，重复项随后单独跑完再并进来。
  未验证完成的候选不会变成可删除结果；检测没跑完时产出条目一律**不预先勾选**并标注原因。
- 顺带把「命中 → 界面条目」抽成 `CleanRules/CleanItemFactory.cs`，规则流水线与重复检测共用，
  保证同一个文件不会因为走哪条路而显示不同颜色。

- 验证：Release 编译 0 错 0 警；**`SafetyCheck` 277 PASS / 0 FAIL**（新增工作状态 15 条、列表快照 24 条、
  重复检测预算/进度/截断 8 条）；`UiRegressionCheck` 45 条全过（新增虚拟化、后台化、停止按钮 18 条）；
  `CleanAnalyzerCheck` / `AiNoteParserCheck` / `AppRecommendationCheck` 全过；`git diff --check` 干净。
- 已知取舍（**没做，说明理由**）：
  - **没有加分页/按需加载兜底**。用户给的兜底方案是「如果分组模板仍无法稳定虚拟化」才启用；
    现在正确的虚拟化开关已经补上，并且运行时审计会直接告诉我们到底生效没有，
    在没有实机数据之前加一层分页只会引入「显示的和能删的不一致」的新风险。
  - 自定义 `GroupStyle` 保留未改。**虚拟化是否真在 26.5 万条下生效，必须在实机上看 `app.log` 里
    那行 `rows created=…`**（几十 = 生效；接近候选总数 = 没生效，日志会同时给 WARN）。
- 还差（实机验证清单）：
  1. 30 万条候选下扫一次 C 盘，看 `app.log` 的 `Perf` 段与 `rows created` 那行；
  2. 窗口在分析 / 首次列表 / 展开分组时能否拖动点击；
  3. 停止按钮是否立刻给「正在取消…」并在约 1 秒内收尾；
  4. 连续扫多次，确认不残留旧树/旧列表；
  5. 默认可清理面板、隐藏文件浏览器的布局不变。

### 2026-09-11  DDWking（发布 v1.1.1）
- 版本从 1.1.0 提到 **1.1.1**（安全、隐私、架构三块都是实质变化），目的是让新包和旧的
  `dist/DashaoHuo-1.1.0-*` 目录不可能混淆。旧目录**原样保留**，没有动。
- 发布产物（**self-contained**，自带 .NET 8.0.30 运行时 + Windows Desktop 运行时，目标机不用装 .NET）：
  - 目录：`dist/DashaoHuo-1.1.1-20260911-win-x64/`（279.4 MB）
  - 压缩包：`dist/DashaoHuo-1.1.1-20260911-win-x64.zip`（114.4 MB）
  - 校验：`dist/DashaoHuo-1.1.1-20260911-win-x64.zip.sha256`
    → `594884D32120E738049979D84E2F665D763C605F5C53E2219BD0FFFD75EB1F3B`
    （第八阶段改完后重新发布并重算了校验值）
- 发布包依赖完整性已逐项核对：主程序 4 件 + `sidecar/AiSidecar.exe` + SteamHelper / StoreAppHelper
  各自的 exe/dll/deps/runtimeconfig + UninstallTools / KlocTools / ObjectListView / OpenAI.dll +
  自包含运行时（coreclr/hostfxr/hostpolicy/PresentationFramework）+ BCU 第三方许可文件，全部在位。
  **AI sidecar 保留**，且与仓库里的 `sidecar/AiSidecar.exe` SHA256 完全一致（`7254C147…F13597`）。
- 新增 `tools/UiRegressionCheck/Run.ps1`：24 条静态回归断言，守住 2026-09-10 定下的界面行为 ——
  **默认显示可清理面板**、**文件浏览器默认隐藏**、右列三 pane 共用 Grid（不被回退成旧的 DockPanel bug）、
  AI 只能写说明不能改 Risk/CanDelete/Selected、不能执行卸载、窗口代码里 `CancellationToken.None` 为 0、
  删除一律走预检、发布包完整性与版本号。
- 全部检查一次过：
  `dotnet build -c Release` 0 错 0 警 ·
  `CleanAnalyzerCheck` PASS ·
  `UiRegressionCheck` 24/24 PASS ·
  `AiNoteParserCheck` 全部通过 ·
  `AppRecommendationCheck` All checks passed（含新增 18 条占用拆分/缓存）·
  `SafetyCheck` **214 PASS / 0 FAIL** ·
  `git diff --check` 干净。
- **没有实盘验证的部分（下次接手必须先补）**：
  1. 完整 UI 走查：启动、扫描、勾选、删到回收站、卸载、设置页新按钮（清除密钥 / 完整路径开关 / 导出诊断）；
  2. 管理员权限下的真实 MFT 扫描与 WizTree 对账（文件数 / 总大小）；
  3. 真实删除预检在盘上的表现（尤其"文件被修改""正在使用"两档会不会误报）；
  4. DPAPI 在真实用户配置下的存取与旧版明文迁移；
  5. sidecar 冷启动到 fallback 的实际链路；
  6. 卸载逐项结果与 BCU 真实状态的对应关系。

### 2026-09-11  DDWking（第五~七阶段：性能、结构、卸载、诊断）
- **MFT 内存先量再改**：`MftScanService.LastMemoryReport` 在扫描结束时统计并写日志
  （托管堆、已用记录数、FileEntry 对象估算总量与均值、平均路径长度、entries/parents/bases 数组占用、
  nameLinks 条数）。抽样每 512 条估平均再乘回去，不为量内存再跑一遍全表。
  `FileEntry.EstimatedBytes` 提供单条估算，诊断包里可查。
  结论方向很明确：**FullPath 字符串是大头**，数组和 nameLinks 是小头 —— 但把 FullPath 改成按需生成
  会动到树构建的核心路径，**这一版没做**（见「还差」）。
- **`FileEntry.Children` 改成懒分配**：新增 `ChildList`（只读遍历，空时返回共享空数组，零分配）、
  `ChildCount`、`HasChildren`。磁盘上绝大多数条目是文件，不再给每个叶子节点挂一个空 List。
  所有只读遍历点（MainWindow / DiskAnalyst / KnownPaths / CleanAnalyzer / CleanRules / MftScanService /
  ScanSnapshot / ReconcileUp）已切到 `ChildList`；写路径（`.Add` / `.RemoveAll` / `.Remove`）继续用 `Children`。
- **软件占用计算加缓存 + 占用拆分**（`AppRecommendationService`）：
  - 同一份 (软件清单, 扫描结果) 只算一次，缓存键含软件数 + 安装路径 + 文件数 + 总分配字节；
    `InvalidateUsageCache()` 在每次扫描结束时调用（新扫描 = 新口径）。
  - 每个软件的占用拆成 **程序本体 / 保存的数据 / 缓存**，缓存那部分就是「不卸载也能先清掉」的估计可释放空间
    （`AppUninstallItem.FootprintText`，卸载列表「扫描占用」列悬停可见）。
  - 范围说清楚：只统计**安装目录这棵树**。装在 AppData 里的用户数据没有并入（按软件名去用户目录里找
    很容易算错，宁可不算），这一点写进了 `ClassifyFootprint` 的注释和界面文案。
- **从 MainWindow 抽出协调模块**（窗口只负责 UI 绑定与事件转发）：
  - `ScanCoordinator`：主扫描 → 失败降级递归 → 质量报告，`onFallback` 回调让界面立刻显示
    「MFT 不可用，正在用兼容扫描」，而不是等扫完才知道。依赖构造函数注入，自己不 new 扫描器；
    真实接线在 `ScanServices.CreateDefault()`。
  - `DeletionCoordinator`：计划 → 确认说明 → 执行 → 汇总文案，删除决策全在这里，可独立测试。
  - `AiCoordinator`：组装批次 → 脱敏 → 发请求 → 强校验解析。通道细节交给 `AiGateway`。
  - `UninstallCoordinator` 职责由 `BcuUninstallService` + `AppRecommendationService` + 逐项结果承担。
- **删除逐项结果补齐一个真问题**：以前预检就挡下的项（不存在 / 受保护 / 已变化 / 占用 / 父目录已包含）
  **不会进批次结果**，界面只说得出「删了几项」，说不出「另外几项为什么没删」。
  现在按请求顺序逐项产出，**每个请求项恰好一条记录**，`DeletionBatchResult` 计数才完整。
- **卸载逐项结果**（`Models/UninstallResult.cs`）：`UninstallItemResult` / `UninstallBatchResult`，
  结局分 已卸载 / 失败 / 用户跳过 / 本地硬拦截 / 保留 / 已停止。每项一行写日志，含原因和技术细节；
  卸载完成时输出一条汇总（ok/fail/skip/protected/kept/total/freed）。
- **诊断导出入口**：设置页「导出诊断信息」写一份 `%LOCALAPPDATA%/DashaoHuo/dashuohuo-diag-时间戳.txt`
  （环境 + 最近日志），**先脱敏再落地**：不含 API Key，不含未脱敏的用户路径。
- 验证：Release 编译 0 错 0 警；`SafetyCheck` **214 条断言全过**（新增协调模块 26 条）；
  `AppRecommendationCheck` 新增通过 18 条（占用拆分 + 缓存）；
  `CleanAnalyzerCheck` / `AiNoteParserCheck` 全过；`git diff --check` 干净。
- 还差（明确不做，避免盲目改核心）：
  - `FileEntry.FullPath` 仍是每个条目一份字符串，这是扫描内存的最大头。改成按需生成 / 目录懒加载需要
    实测数据支撑，且会动到 MFT 建树和 UI 展开逻辑，本版只在日志里把数字量出来，没有下手。
  - 规则级测试目前只覆盖重复文件规则，其他规则靠端到端实盘回归。
  - 全部改动**没有在真机上跑过完整 UI / 管理员实盘扫描与删除**（见最终报告的「未实盘验证」清单）。

### 2026-09-11  DDWking（第四阶段：清理规则架构）
- **抽出 `ICleanRule`**（`Services/CleanRules/`）。每条规则声明：规则名、分类、默认风险、**证据等级**
  （`None / Heuristic / Signature / Verified`）、是否允许删除、是否支持取消。运行时记录**规则耗时**和产出条数。
- **规则拆成独立模块**（每条可单独测）：
  `TempCacheRule`（临时文件 + 缓存 + 安装包缓存）、`RecycleBinRule`（回收站，解析 $I 元数据）、
  `LargeFileRule`、`OldFileRule`、`EmptyFolderRule`、`LongPathRule`、`BrokenShortcutRule`、
  `DuplicateFileRule`、`ScanCompareRule`（前后扫描对比）。
  判定逻辑是从原 `CleanAnalyzer` **逐字搬**过来的，顺序没动 —— 先命中的规则决定分类和风险，
  改了顺序就会改默认勾选。
- **`CleanAnalyzer` 变成纯协调器**：走一遍目录树 → 逐条跑规则 → 汇总。
  重点是**规则异常互相隔离**：某条规则抛异常只记日志（`AppLog`）并继续跑下一条，取消则原样往上抛，
  不会被当成规则故障吞掉。`LastRuleTimings` 暴露每条规则的耗时/条数/错误，日志里逐条可查。
- **规则结果去重**：`CleanRuleSink` 按 `(目标列表, 路径)` 去重，不同规则重复命中同一个文件时**先命中的说了算**。
  应用签名（`AppSignatures`）统一在汇总口生效，所以「规则结论优先、签名覆盖分类」的老行为不变。
- **重复文件改成分阶段哈希**（`Services/CleanRules/DuplicateDetector.cs`）：
  按大小分组 → 首段 256KB 哈希 → 尾段 256KB 哈希 → 完整哈希（流式 SHA256 + 长度也进哈希）。
  只有活过上一阶段的候选才进入下一阶段，另有完整哈希条数上限（200）和字节预算（4GB）。
  边界情况明确处理：
  - **硬链接**：同一 (卷序列号, file id) 只留一个代表 —— 删了不释放空间，不算重复；
  - **正在使用**：以 `FILE_SHARE_READ|WRITE|DELETE` 打开，打不开就跳过并计数，不报错；
  - **云占位文件**：带重解析点属性的一律不读（读会把文件从云端拉下来）；
  - **网络盘**：整盘跳过（按内容哈希要过 SMB，慢且不稳），UNC 也按网络处理；
  - **相同内容不同路径**：这就是要的结果，保留**路径最短**的那个，其余标为「多余」且默认可勾。
  检测统计（`DuplicateStats`）会记下各阶段哈希次数、字节数、各类跳过数量，便于解释「为什么这次没找到重复」。
- 验证：Release 编译 0 错 0 警；**`SafetyCheck` 185 条断言全过**（新增重复文件分阶段哈希 19 条）；
  `CleanAnalyzerCheck` / `AiNoteParserCheck` / `AppRecommendationCheck` 全过；`git diff --check` 干净。
- 还差：规则级测试目前只覆盖了重复文件规则；其他规则的行为靠端到端实盘扫描回归，还没在真机上跑过。

### 2026-09-11  DDWking（第三阶段：隐私与 AI 安全）
- **API Key 加密保存**（`Services/SecretProtector.cs` + `Services/AppPaths.cs`）：
  - 明文密钥只活在内存里（`AiProviderCfg.ApiKey` 标了 `JsonIgnore`），**永远不进 settings.json**；
  - 落盘走 Windows DPAPI（`CryptProtectData` / `CryptUnprotectData`，当前用户范围 + 自定义熵），
    单独存 `%APPDATA%/DashaoHuo/secrets.dat`；
  - 解不开就当没有（换机器/换账户/文件被改），提示重填，**绝不退回明文存储**；
  - 旧版把明文写在 settings.json 的 `ApiKey` / `AiApiKey` 里：`Load()` 会用 `JsonNode` 读原始 json
    把它们搬进加密仓库，然后立刻重存一次把明文抹掉（一次性迁移）；
  - 设置页加了「清除密钥」按钮和「已加密保存 / 未保存」提示；系统加密不可用时界面明确说明密钥只在本次运行有效。
  - 配置目录集中到 `AppPaths`，测试可以把目录指到临时目录，绝不碰真实用户配置。
- **统一 AI 通道**（`Services/AiGateway.cs` + `AiGateways.cs`）：
  - `IAiGateway` / `IAiGatewayProvider` 抽象，`SidecarAiGateway` 与 `DirectHttpAiGateway` 两个实现；
  - `AiGateway` 负责选路（sidecar → 内置 HTTP fallback）、整体超时、瞬时故障重试（3 次，400/1200ms 退避）、
    请求限流（默认 250ms 间隔）、状态记录（`AiGatewayStatus`：通道/成功/次数/耗时/消息）；
  - 只有 408/429/5xx/连接层失败才算瞬时；401/403/400 不重试；用户取消原样抛出，不当故障；
  - `AiGateway` 本体不引用 SidecarClient / AiClient（组合根 `AiGateways.Register()` 接线），所以能被离线测试整条换掉；
  - sidecar 冷启动（最长等 20 秒）挪到线程池，**不会卡住 UI**；`AiClient` 的公开方法全部收口到网关。
- **AI 输出强约束**（`Services/AiNoteParser.cs`）：说明长度上限 160、单次最多 60 条、响应体上限 20000 字符、
  最多解析 2000 行、流式增量也在 MainWindow 侧封顶；路径必须命中清单（编不出来）；风险词/纯大小当噪音丢掉；
  `Risk` / `Selected` / `CanDelete` 由结构保证碰不到；解析失败保留本地规则结果。
- **路径脱敏**（`Services/PathRedactor.cs`）：默认只发脱敏路径 —— 当前用户目录 → `<UserProfile>`，
  其他用户 → `<User>`，主机名 → `<PC>`，系统路径（`C:\Windows\…`）结构保留（AI 得靠它判断这是什么）。
  完整路径必须用户在设置里明确打开「把完整本地路径发给 AI」（`AppSettings.AiSendFullPaths`，默认关），
  开关动作会记日志。日志脱敏和对外脱敏现在共用同一套规则（`LogRedactor.ScrubPath` → `PathRedactor.Redact`）。
  发出去是脱敏路径，所以 AI 回的也是脱敏路径 —— `AiNoteParser.Apply` 支持传入同一套映射规则还原到条目。
- 顺带把 AI 的 DTO（`AiProtocol` / `AiMsg` / `AiReply` / `AiToolCall`）从 `AiClient.cs` 挪到 `Models/AiContracts.cs`，
  让它们不依赖 OpenAI 包就能被测试引用。
- 验证：Release 编译 0 错 0 警；**`SafetyCheck` 166 条断言全过**（新增 API Key 加密 18 条、AI 网关 14 条、路径脱敏 13 条）；
  `CleanAnalyzerCheck` / `AiNoteParserCheck` / `AppRecommendationCheck` 全过；`git diff --check` 干净。
- 踩过的坑：`CryptProtectData` 的 `pOptionalEntropy` 要的是 `DATA_BLOB*`，传裸字节缓冲会让 DPAPI 把
  前 4 字节当长度读，直接失败。已改成 `ref DATA_BLOB`。

### 2026-09-11  DDWking（第二阶段：安全与流程）
- **删除安全模型落地**。新增 `Models/DeletionModels.cs`：`DeletionTarget` / `DeletionPlan` /
  `DeletionPreflightItem` / `DeletionPreflightResult` / `DeletionItemResult` / `DeletionBatchResult`，
  结局枚举覆盖：`Recycled / NotFound / PathChanged / Modified / AccessDenied / Protected / InUse /
  SkippedByUser / RedundantChild / Failed`。一项失败不会丢掉其他项的细节。
- **删除前预检**（`Services/DeletionPreflight.cs` + `DeletionExecutor.cs`）逐项检查：文件是否还在、
  路径身份（`GetFileInformationByHandle` 的 volume serial + file id，同一路径换了文件也能认出来）、
  大小/修改时间变化、是否目录、是否重解析点、是否系统文件、是否盘符根、是否在 Windows / System32 /
  WinSxS / Program Files / ProgramData / Users 等保护路径、父子路径重复选择、是否正在被占用、权限是否够。
  执行前会**再探一次盘**（预检到执行之间文件可能被改），删完还复查路径确实没了，没删掉就报失败而不是谎报成功。
- **时间戳口径的取舍**：MFT 的 `$FILE_NAME` 时间只有目录项变更时才刷新，和资源管理器显示的
  `$STANDARD_INFORMATION` 时间本来就可能不一致。所以 `DeletionTarget.SnapshotIsExact` 只有递归扫描
  （直接读 `FileInfo`）才为真；MFT 快照的时间/大小差异降级成「需要再确认」，不当硬拦截，避免误报「文件被改过」。
- **保护路径分两档**：`Blocked`（盘符根 / Windows 浅层 / System32 / WinSxS / $ 元数据 / pagefile 等，永不删）和
  `NeedsConfirm`（Program Files / ProgramData / Users 的**一级**子项、链接目录、系统标记目录）。AppData 里的
  缓存不降级，所以普通清理不会被误打成敏感。链接目录不再硬拦（删的只是链接本身），云占位文件放行。
  `ProtectedPaths.IsProtectedEntry` 保留了**老口径**给 CleanAnalyzer 用，清理列表的展示与默认勾选不变。
- **统一取消**：扫描 `_scanCts` / 清理规则分析 `_analyzeCts` / 软件清点与残留扫描 `_uninstallCts` /
  AI `_aiStop`+`_aiAppsStop`+`_aiConfigCts` 各自独立，顶栏「停止」调 `StopEverything()` 全部取消。
  所有 `CancellationToken.None` 已清零（原 8 处）。每个操作有**代次守卫**（`_scanGeneration` /
  `_analyzeGeneration` / `_uninstallGeneration`），旧任务跑完发现代次不是自己那代就丢弃结果，不覆盖新结果。
  取消走 `OperationCanceledException` 单独分支，只清理状态，不再报成「扫描失败」。
- **扫描质量报告**：`Models/ScanQuality.cs` + `IScanService.LastQuality`。MFT 侧统计解析失败 / 孤儿记录 /
  硬链接 / 重解析点，递归侧统计跳过目录 / 权限错误 / 路径错误 / 读取失败 / 重解析点。界面顶栏下加了一条
  常驻细线（`ScanQualityPanel`，首次扫描后才出现，启动布局不变），完整=暗灰、不完整=高亮，
  让「跳了一堆目录的扫描」不可能看起来像完整扫描。
- **统一日志**（`Services/AppLog.cs` 重写）：结构化一行 = 时间 / 级别 / 模块 / operation ID / 阶段 / 耗时 /
  数量 / 异常类型 / 用户可见错误；2MB 滚动、保留 3 份；`LogOperation` 作用域负责 ID 与计时。
  `BuildDiagnostics()` / `ExportDiagnostics()` 出诊断包，**先脱敏再落地**。
- **错误分类与脱敏**：`Services/AppError.cs` 把异常分成 权限 / 路径 / 网络 / 取消 / 解析 / IO / 未知，
  用户可见文案与技术细节分开；`Services/LogRedactor.cs` 抹掉 OpenAI/Anthropic/Google/GitHub/HF 密钥字面量、
  `Bearer`、`apiKey` 等字段、查询串里的 key，并把用户目录换成 `<UserProfile>`、主机名换成 `<PC>`。
  空 `catch {}` 在关键路径（设置读写、扫描快照、回收站名解析、卸载体积/图标、sidecar 启停、
  资源管理器/剪贴板、崩溃兜底）全部改成带分类的日志，日志失败仍然不影响主流程。
- 新增回归检查 `tools/SafetyCheck`（`dotnet run --project tools/SafetyCheck/SafetyCheck.csproj`）：
  **116 条断言全过**，覆盖删除预检、路径身份变化、父子路径重复选择、批量部分失败、用户取消、
  递归扫描跳过统计、扫描权限/路径错误、AI 格式错误与越权字段、脱敏、错误分类、诊断包。
- 验证：Release 编译 0 错 0 警；`CleanAnalyzerCheck` / `AiNoteParserCheck` / `AppRecommendationCheck` /
  `SafetyCheck` 全过；`git diff --check` 干净。
- **踩过的坑（重要）**：`pwsh` 下用 `Get-Content -Raw` + `Set-Content -Encoding UTF8` 改这个仓库的中文
  `.cs` 文件会把 UTF-8 当 GBK 读，把整个文件打成乱码且**有损**（每个损坏点丢 2 字节）。批量改文件
  一律走 `edit`/`write` 工具，不要用 PowerShell 文本管道。

### 2026-09-11  DDWking
- 完成一次开发进度审计并直接实施优化：
  - 删除 CleanAnalyzer 对清理候选的 400 项硬截断，保留完整候选并按大小排序。
  - 扫描界面明确区分 MFT 高速扫描与兼容递归扫描；MFT 失败原因会贯穿降级过程，并在完成后显示最终来源。
  - 扫描/UI 诊断日志迁移到 %LOCALAPPDATA%/DashaoHuo，目录创建失败不会阻断主流程。
  - 批量回收站增加成功、失败、保护/跳过、释放空间统计；失败项保留异常原因，保护项不再误报为失败。
  - 清理摘要显示分类完整候选数与当前目录实际显示数；新增 tools/CleanAnalyzerCheck/Run.ps1，离线覆盖 401 项边界。
- 发布：找到用户目录下未加入 PATH 的 .NET SDK 8.0.424，已用它重新构建 Windows x64 自包含版本（附带 .NET 桌面运行时和现有 AI sidecar）。产物目录：dist/DashaoHuo-1.1.0-20260911-win-x64。
- 修复发布遗漏：ReferenceOutputAssembly=false 的 SteamHelper / StoreAppHelper 仍需要入口 DLL，现通过 MSBuild GetTargetPath 将其加入构建与发布输出。
- 验证：Release publish 成功；重新编译运行 AiNoteParserCheck、AppRecommendationCheck 全部通过；CleanAnalyzerCheck 静态/虚拟数据检查通过；两个辅助程序的托管入口无参数启动均返回预期的 160；sidecar 校验一致。
- 还差：完整 UI / 管理员实盘扫描与删除回归未执行。直接启动辅助 EXE 需要 UAC 提权，托管入口检查不等于完整管理员功能验证。

### 2026-09-04  DDWking
- 去掉每行开头的「删了没事，/删了要重新下载，」——545 行都这么开头，人眼看到第三行就不读了，等于没写；而且和分组标题重复。
  - 规则：**默认后果不写，特殊后果才写**。`Safe` 那批签名只留「这是什么」；`Cautious / Keep` 那批没有默认前提，后果该说就说。
  - 默认前提挪进分组标题：`建议清理 · 删了会自动重建（545 项 · 123 G）`。
  - AI 提示词：从「第一句必须说删了会怎样」改成「只说这是什么，只有删了会让人意外时才补后果」，并明确禁止对普通缓存写「删了没事」。
  - `Loc.Reason*` 同步：崩溃转储→「程序崩溃时留下的记录文件」，空文件夹→「里面什么都没有的空文件夹」，重复文件→「和这个文件内容一模一样：xxx」。

### 2026-09-04  DDWking
- 全部文案改大白话（起因：AI 写出来的是「pip 存放已下载 wheel、源码包……按哈希目录存放响应内容」，普通人一个词都看不懂）：
  - AI 提示词（`Loc.AiCatSystem` / `AiFolderAskSystem`）重写：**第一句必须回答「删了会怎样」**，明令禁止命令行名（pip/npm/yarn）、格式名（wheel/源码包）、技术词（哈希/响应体/P2P/依赖/索引），限 30 字，给了「删了没事，下次装东西时自动重下」这种示例。
  - `AppSignatures` 的 `Note` 改名 `Plain`，65 条全部重写成「后果优先」的大白话。
  - `Loc.Reason*` 同步改：崩溃转储→「删了没事 · 程序崩溃时留下的记录文件」，回收站→「清掉就真没了」等。
  - **术语挪到悬停**：`AppSig.Name`（pip / WinSxS）只出现在 tooltip；`Describe()` 改成纯技术信息，新增 `PlainNote()` 出大白话；`CleanItem` 加 `Tech` / `HintText`（完整路径 + 技术细节），两个列的 tooltip 都绑 `HintText`。
  - `CleanAnalyzer.Item()`：签名命中时**不再拼接**调用方那句「大文件 · 看不出用途」——否则「看不出用途」和「删了没事」自相矛盾。

### 2026-09-04  DDWking
- 撤回「说明列默认收起」——把理由藏起来只会让人更没底。说明列恢复常驻，文案改「诚实版」：签名命中写依据（`开发缓存 · 删了会自动重建`），启发式命中直接写 `大文件 · 看不出用途` / `已 N 天未改 · 看不出用途`（新增 `Loc.ReasonUnknown`）。分组名暂不动。
- AI 按钮文案改成「让 AI 看看这些是什么」（`Loc.AiExplainBtn`），明确它是可选第二意见、只解释那批"看不出用途"的项。

### 2026-09-04  DDWking
- **右列铺不满的真正原因**（上次诊断错了）：`RightPanel` 里三个 pane 的外层是 `DockPanel`，`CleanPane` 没写 `DockPanel.Dock`，作为非最后一个子元素默认按 `Dock=Left` 停靠，宽度取内容宽度；`UninstallPane` 才是 `LastChildFill` 的那个。右列固定 440 时被夹住看不出来，左树收起、右列变宽才露馅。改成 `Grid`（tabs 一行 Auto，三个 pane 同处 `Grid.Row="1"`，谁可见谁占满）。

### 2026-09-04  DDWking
- 修右列铺不满：`UpdateRightColLimit` 在构造函数里跑时窗口还没尺寸，算出 `MaxWidth=320`，而右列是 `Star` 宽——Star 列被 MaxWidth 卡住，右边就空一块。改成左树收起时 `MaxWidth=Infinity`，并在 `Window_Loaded` 用真实尺寸重算一次。
- 风险列改成「类型」列（绑定 `Group`，不再下安全结论）：行只说「这是什么」（开发缓存 / 大文件 / 老文件…），安全与否由分组标题承担。起因：截图里 `weights.bin` 4 GB 被标成「可安全删除」但理由是「占用最大的文件之一」——`CleanAnalyzer.Item()` 让路径签名覆盖了风险档位，标签和理由自相矛盾。
- 分类下拉从主视图移走，降级成「筛选」按钮 + 动态 `ContextMenu`（`FilterBtn_Click` / `SelectCategory`），`CatList` 和 `_catLock` 一起删掉。分类不再和风险分组叠成两层。
- 清死代码：`CleanItem.RiskText`、`Loc.RiskSafe/RiskConfirm/RiskKeep`、`Loc.FilterHere`、`Loc.ColRisk`。

### 2026-09-04  DDWking
- 清理列表按风险分两组：`CleanItem.RiskGroupKey`（安全=0 / 需确认=1），`ShowCleanCat` 把 `CleanGrid.ItemsSource` 换成带 `GroupDescriptions` 的视图，配 `CleanGroupConverter`（组标题带条数+大小）和 `CleanGroupExpandedConverter`（安全组展开、需确认组折叠）。复用卸载页那套 `GroupStyle`+`Expander`。
- 顶部摘要去重：「建议清理 554 项 · 需要你判断 225 项」→「共 779 项 · 254 GB」（分类计数挪到组标题里），`Loc.RiskSummary` 删掉。
- 注意：`Jury.cs` 里那条「本主题下 GroupStyle 只渲染标题、行不显示」的坑只针对 ComboBox + GroupStyle，DataGrid 的 GroupStyle 正常。

### 2026-09-04  DDWking
- 继续减法：顶栏「关于」移进设置对话框（`AboutLinkBtn`，在 `SettingsBody` 滚动区里）；目录树 6 列砍到 3 列（去掉 分配 / 文件 / 文件夹，表头保留 16px 缩进列，行网格起点在树 padding 内，对齐不变）；清理面板「确认删除」挪到按钮行最前。
- 顺手清死代码：`SortAlloc_Click` / `SortFiles_Click` / `SortFolders_Click` 和 `SortKey` 里已无列的 Allocated/Files/Folders/Modified 一起删掉；`Loc.Allocated` / `Loc.FoldersCol` 删（`Loc.FilesCol` 还被 AI 提示词用着，保留）。

### 2026-09-04  DDWking
- 按钮层级重排（一屏只留一个实心按钮）：顶栏去掉「看 AI 建议」、扫描降级为幽灵按钮、停止只在扫描时出现；「看 AI 建议」降级成清理面板里的一行「按 AI 建议勾选」；清理面板去掉「全选 / 勾选可安全删除」，全选改表头复选框，主按钮变成动态的「确认删除 N 项」（无勾选时禁用）。
- 左树默认收起：顶栏「文件夹」按钮切换，收起时右列铺满、分隔条隐藏（`ApplyTreeVisibility` / `_treeVisible`），`UpdateRightColLimit` 跟着算可用宽度。
- 表头复选框没用 `x:Name`——DataGrid 列里的命名元素在 WPF 里可能取不到字段，改成从 `Click` 的 sender 拿，并在 `ApplyUi` 里从 `ColPick.Header` 回填 `_pickAllBox`。

### 2026-09-04  DDWking
- 界面往「傻瓜式」收：**「一键处理」改成「看 AI 建议」——只按建议勾选 + 展示，绝不代替用户删除**。AI/规则只负责分析，删不删由用户看过清单后确认（产品原则，不能有暗示自动动手的文案）。
- 清理列表不再显示「别删」项（`BuildCategories` 过滤 `Keep`），底部「可清理」计数也同步排除；风险摘要去掉「别删 0」。
- 文案：勾选提示「97 · 17.5 G」→「已勾选 97 项 · 可释放约 17.5 GB」；AI 状态灯加前缀（`已连接`→`AI 已连接`），避免和勾选数连读成一句；摘要「安全/需确认/别删」→「建议清理 N 项 · 需要你判断 M 项」。
- 自动扫描早就有（`Window_Loaded` → `RunScan`），无需改动。

### 2026-09-04  DDWking
- 「一键处理」：顶部新按钮。没扫过就先扫；扫完把全盘「安全」垃圾（不限当前分类/目录）一起勾上，同时把本地规则标出的「建议卸载」软件勾上，确认后回收垃圾并自动切到卸载页，用户核对再点卸载。软件永不自动卸载（卸载器是交互式的），AI 也不是必需。
- 驱动/更新清理补两条签名：`NVIDIA 驱动缓存`（ProgramData 下载缓存，Safe）、`软件安装包缓存`（`\ProgramData\Package Cache`，Cautious）。Windows 更新下载缓存、驱动精灵/鲁大师/360 这些早就在规则里了。没做删除已装驱动（pnputil）和卸载 KB 补丁——风险太高，产品定位是只建议不自动动手。

### 2026-09-04  DDWking
- 卸载后自动重扫磁盘：卸完先立即刷顶部可用空间（`DriveInfo`），再后台重扫整盘（MFT/递归），目录树、清理面板、软件占用一起跟着刷新，不用手动再点「扫描」。`_scanning` 防重入，未扫过盘则不触发。

### 2026-09-04  DDWking
- 卸载闭环补齐：卸载结束在摘要区显示「成功/失败/跳过 + 释放约多少空间 + 未完成清单（失败/跳过名称）」，结果持续显示到下次卸载；卸载网格加右键菜单「重试卸载 / 打开官方卸载程序」（`UninstallManager.RunUninstaller`）。跳过不再被误标成「失败」。

### 2026-09-04  DDWking
- 「扫完 AI 告诉你哪些软件能删、傻瓜式操作」落地：扫描结束自动加载已装软件并用 `_allFiles` 匹配真实占用（重叠/根目录不计，避免重复统计）；本地规则先硬拦截系统组件、驱动、运行库、安全、虚拟化、正在运行/状态未知项；AI 可选，只在用户确认隐私提示后发送（匿名 appId、名称、发布者、版本、安装日期、大小、运行状态，不发路径和密钥），返回严格 JSON 只做「建议卸载/可以考虑/建议保留」排序。
- 卸载页按建议分三组，勾选框对不可卸载项禁用；确认框列出软件名 + 预计释放空间 + 提醒；卸载后自动复扫。
- 新增 `tools/AppRecommendationCheck` 回归检查（6 条：根目录/重叠目录不计占用、运行状态未知降级远程建议等），`dotnet run --project tools/AppRecommendationCheck`。
- 修掉 Codex 遗留的两处编译错（`AppRecommendationService.cs` 缺 `using System.IO`、`Stubs.cs` 缺 `using AiDiskCleaner.Models`），全量编译 0 错 0 警，两套检查全过。

### 2026-09-04  DDWking
- 分类芯片改成一个下拉：完整名称 + 条数 + 大小，不再挤一行裁切。第一项「全部」。

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
