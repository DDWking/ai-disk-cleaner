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
| 卸载页 | 可用 | 扫描后自动列已装软件 + 匹配真实占用；本地规则先过滤，AI 可选只做建议；按「建议卸载/可以考虑/建议保留」分组；确认框列名称+释放空间；卸完自动复扫 |
| 软件卸载建议 | 可用 | 规则硬拦截（系统/驱动/运行库/安全/虚拟化/正在运行/状态未知），AI 只建议和排序、不越权，用户逐项确认 |
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
