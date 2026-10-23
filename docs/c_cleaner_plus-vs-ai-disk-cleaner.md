# c_cleaner_plus 对比分析报告

> 对照对象：本仓库 **ai-disk-cleaner（大扫货）** vs 开源项目 [Kiowx/c_cleaner_plus](https://github.com/Kiowx/c_cleaner_plus)
> 分析时间：2026-09-18 ｜ 分析方式：GitHub API + 远端文件 + **main.py 全量代码级核对（14,978 行）** + 本地仓库实测
> **修订说明**：v2。v1 有两处说法被代码核对推翻——(1) 把 winapp2/BleachBit 当成"在用的规则库"，实际 main.py **从未引用**这两个本地文件；(2) 把"删除逻辑"整体说成没测，实际**保护路径实现相当细**，缺的是"真删"这一步的测试覆盖。两处已在正文更正。
> 一句话结论：**你的项目"看得懂盘、删得更稳、日志更干净"；他的项目"功能更全、发得出去"。1806 : 1 的 star 差，主要差在发行与运营，不是代码水平。**

---

## 0. 结论速览

| 维度 | 谁更强 | 说明 |
|---|---|---|
| 扫盘与可视化 | **你** | 你用 MFT 建整棵文件树（秒级、目录树、占比、文件夹整理）；他的 MFT 只用于"挑盘根大文件"，且是可选外部 exe |
| 删除安全 | **你** | 你只走回收站；他**默认勾选"永久删除"**，定时任务默认永久删，但保护路径实现本身不差 |
| 隐私/日志 | **你** | 你有 `PathRedactor` / `LogRedactor`；他日志**零脱敏**，导出会带真实用户名和路径 |
| 规则生态与可扩展性 | **他** | 8 类规则包 + 规则商店远程下载；但包**无版本、无哈希、来源是第三方 Gitee 仓库** |
| 功能覆盖 | **他** | 定时任务、开机自启、托盘、右键菜单清理、强力卸载（注册表/服务/计划任务） |
| AI 能力 | **你** | 你是真 LLM（只读解释、不改勾选）；他完全没有 LLM（0 处模型 API） |
| 安装与发版 | **他** | CI 自动出安装包 + 自动发 Release，自带运行时；你手动打 zip 且要用户先装 .NET 8 |
| 供应链安全 | **你** | 你没有远程规则下载，也就没有"未签名规则包"风险面 |
| 社区与文档 | **他** | 1806 star / 69 fork / 文档站 / 交流群 / Android 版 |

---

## 1. 两个项目的客观事实卡

| 项 | ai-disk-cleaner（本仓库） | c_cleaner_plus |
|---|---|---|
| 定位 | Windows NTFS 磁盘分析 + 清理（WizTree 风格） | Windows 全盘清理工具箱 |
| 技术栈 | C# / .NET 8 WPF + Node/pi-ai 侧车 + BCUninstaller（第三方库） | Python 3.9+ / PySide6 / Fluent Widgets + 可选 Rust 辅助程序 |
| 许可证 | MIT（卸载引擎 Apache 2.0） | Apache-2.0 |
| 代码体量 | 112 个源文件 / 1.86 MB<br>`MainWindow.xaml.cs` 4920 行（234 KB）<br>`MainWindow.xaml` 2727 行<br>`Loc.cs` 1997 行<br>`MftScanService.cs` 976 行<br>安全测试 `tools/SafetyCheck` 196 KB | **`main.py` 单文件 14,978 行 / 654 KB / 34 个类 / 226 个顶层函数**<br>最大类 `ToolboxPage` 1676 行、`MainWindow` 1659 行<br>测试约 53 个（46 + 7） |
| 扫描实现 | 主程序内 C# 直读 `$MFT`（`CreateFile(\\.\C:)` + `DeviceIoControl` + `SeBackupPrivilege`），建**全盘文件树**；失败降级递归 | `main.py` **零 MFT 代码**（`DeviceIoControl`/`$MFT`/`\\.\` 命中 0 次），用 `os.scandir`；盘根大文件才调外部 `fast_large_files.exe`（Rust，读 MFT，只读） |
| 规则来源 | 8 条 C# 规则（顺序有语义）+ 证据等级/风险归属/可删性/用途分层 | 8 类规则包（位置数组）+ 规则商店（从 Gitee 拉）；`config/winapp2.json`、`config/bleachbit.json` **随包但未被代码引用** |
| 删除 | 只回收站（`SHFileOperation` + `FOF_ALLOWUNDO`）+ 预检 + 保护闸 + 执行前身份复核 | 回收站（`SHFileOperationW` + `FOF_ALLOWUNDO`，同款 API）**或永久删除**（`os.remove`/`shutil.rmtree`），**永久删除默认勾选**；占用文件用 `MoveFileExW` 延迟到重启删 |
| 日志 | 有脱敏（`PathRedactor` / `LogRedactor`，密钥与路径都抹） | 内存 1200 行 + 导出 `.txt`，**零脱敏**（0 处 redact/mask） |
| AI | 接 LLM 只读解释（不改勾选/风险），密钥 DPAPI 加密 | **无 LLM**（0 处 openai/anthropic/deepseek/api_key） |
| 自动化 | 无（无托盘/定时/自启/计时器） | 计划任务（分/时/日/周/登录，`schtasks /RL HIGHEST`）+ 登录自启 + 托盘 |
| 卸载 | BCU UninstallTools（官方卸载程序 + 残留扫描） | 标准 + 强力卸载（`sc stop`/`sc delete` + `taskkill /T /F` + `reg delete /f`） |
| 提权 | `app.manifest` 写死 `requireAdministrator` | `main()` 里检测非管理员则用 `ShellExecuteW(..., "runas", ...)` 自我重启 |
| 打包发版 | 手动 zip；需预装 .NET 8；CI 仅 `dotnet build` | CI 自动 PyInstaller + Inno Setup → 自动 Release；setup 28 MB / zip 39 MB |
| 社区 | 1 star / 0 fork / MIT | **1806 star / 69 fork / Apache-2.0** + [文档站](https://docs.cus.cc.cd/) + 群 + [Android 版](https://github.com/Kiowx/Deep_Cleaner) |
| 创建 / 最后提交 | 2026-08-31 / 2026-09-14 | 2026-02-05 / 2026-09-08 |

---

## 2. 相同点（你们在解决同一类问题）

- 都是 Windows 全盘清理：临时文件、缓存、日志、大文件、重复文件、空文件夹、无效快捷方式。
- 都要管理员权限干重活，都选择"启动即提权"。
- 回收站删除用的是**同一个 API**：`SHFileOperation` + `FOF_ALLOWUNDO`。
- 都有深浅色主题、中英界面（他 `i18n/en_us.json`，你 `Loc.cs` 1997 行）。
- 都做"卸载 + 扫残留"。
- **都有安全回归测试**：你 `tools/SafetyCheck`（`LayeredCleanTests.cs` 196 KB），他 `tests/test_safety_regressions.py` 46 KB。
- 都跨语言借力：你借 Node 侧车适配模型协议，他借 Rust 做 MFT 大文件快扫。

---

## 3. 关键不同点（带证据）

### 3.1 扫盘：都用 MFT，但一个是"建树"，一个是"挑大文件"

| | 你 | 他 |
|---|---|---|
| MFT 代码位置 | 主程序内 C#（`MftScanService` 976 行 + `NtfsNative`） | **`main.py` 里 0 处**；在外部 Rust 程序 `tools/fast_large_files` |
| 用途 | 全盘文件 + 目录 + 大小 → 目录树/占比/文件夹整理 | 只列**盘根**的大文件（默认 ≥300 MB、最多 300 条，JSON Lines），只读不删 |
| 调用方式 | 始终可用 | `subprocess.Popen`，只在盘根扫描时启用，**60 秒墙钟超时**，可用环境变量 `C_CLEANER_PLUS_DISABLE_FAST_MFT` 关掉，exe 缺失则回退 `os.scandir` |
| 兜底 | MFT 失败 → 递归扫描降级，降级原因带到界面 | 静默回退 Python 扫描，README 无速度数字 |
| 并行 | — | 线程 + 队列，4 个 worker，无 QThread/asyncio |

**结论**：MFT 不是你的独占技术，但"用 MFT 建整棵树 + 可视化 + 不依赖外部 exe"仍是你的强项。

### 3.2 规则：他是"可扩展清单 + 远程商店"，你是"有语义的对象模型"

- **他**（`parse_rule_entry`）：位置数组 `[标题, 路径, kind(dir/file/glob), 启用, 说明, 是否自定义, glob模式]`，支持 `%LOCALAPPDATA%` 等变量；glob 用 `fnmatch` 只匹配直接子项。
  **没有风险等级、没有保护标记、没有排除项、没有回收站/永久标记**（回收站是全局运行选项）。
- **重要更正**：他的 `config/winapp2.json`（841 KB）和 `config/bleachbit.json`（113 KB）**在 main.py 里 0 次引用**——也就是说这两个文件在当前提交里是**死数据**。"兼容 winapp2 / BleachBit"体现在规则商店里作为**可下载的规则包**（远程拉取），不是本地直接消费。
- **你**：`CleanItem` 带风险归属、证据等级（Heuristic/Signature/Verified）、可删性、用途分层、分组；8 条规则顺序有语义。

### 3.3 删除：你保守，他"默认更狠"（但保护路径实现不差）

- **你**：只走回收站；删前预检 + 保护闸（硬拦截/敏感确认）+ 执行前重新探盘比对文件身份（FileId/大小）。**用户没有"永久删除"这个选项。**
- **他**（代码级核对）：
  - 回收站 = `ctypes` 调 `SHFileOperationW` + `FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_SILENT | FOF_NOERRORUI`（和你同一套）。
  - 永久删除 = `os.remove` / `shutil.rmtree` / `os.rmdir`；占用中的文件走 `MoveFileExW(path, None, 4)` → **重启后删除（等于静默永久删）**。
  - **UI 上"永久删除"复选框默认是勾上的**；**定时任务默认也是永久删除**，除非加 `--scheduled-recycle`。
  - 全部 12 处删除入口都收敛到一个 `delete_path`，再过 `is_protected_system_path`：盘符根、`Windows`/`System32`/`SysWOW64`/`ProgramData`/`Program Files`/`Program Files (x86)`/`Users`/`Public`/`USERPROFILE`/`LOCALAPPDATA` 精确拦截，系统目录后代只放行白名单缓存；再做 `realpath + normcase`、跳过符号链接/重解析点；空目录删除前复查；重复文件删除前按 sha256 + stat 签名复验。
  - **测试覆盖的真实缺口**：`delete_path` / `force_delete_registry` 在测试里是 **mock**；没有测"回收站 vs 永久删除"、`C:\` 根、`pagefile.sys`、用户目录根、重解析点、文件占用、glob 展开。也就是说**保护逻辑写得不差，但"真删"这条路上没有自动化测试守着**。

### 3.4 AI：你是真 AI，他一个模型 API 都没有

- **你**：Node/pi-ai 侧车或内置 HTTP，**只解释、只写说明，不改勾选/风险**；工具只有 `list_folder` / `search_clean` / `report_finding`（只读）；Key 走 DPAPI。
- **他**：`main.py` 里 `openai` / `anthropic` / `deepseek` / `api_key` / `Authorization` **命中 0 次**；对外只有 3 个 `urllib` 地址——Gitee 更新清单、Gitee 规则商店、GitHub i18n 清单。"AI 工具"只是规则分类（含 HuggingFace 缓存等），规则说明里明确写"会话记录、记忆文件和登录凭据不在清理范围内"。
- **"AI 磁盘清理"这个名字，目前只有你名副其实。**

### 3.5 自动化：他有一整块你没有的（也是风险最大的一块）

- 他有：计划任务（`schtasks /Create /RL HIGHEST` + `/SC DAILY|WEEKLY|HOURLY|MINUTE|ONLOGON`）、登录自启（就是一条 ONLOGON 任务，**不写 Run 注册表**）、托盘。
- **定时任务是静默无人值守的**：`main()` 在创建 `QApplication` **之前**就跑 `run_scheduled_job`，没有确认框，**默认永久删除**，只写 `scheduled_logs/scheduled_clean_*.log`。
- 另有一处值得注意：**定时分支跑在提权检查之前**（`is_admin` 检查在 GUI 分支），也就是说定时路径没有自我提权那一步。
- 你（已 grep 核实）：**没有任何托盘、定时、自启、计时器代码**，纯手动交互。

### 3.6 工程化与发行：差距最大的一块

| | 你 | 他 |
|---|---|---|
| CI | `build.yml` 19 行，只 `dotnet build`（不跑测试、不打产物） | 自动打包 + 自动发 Release（机器人发布，资产带 sha256） |
| 安装 | 解压 zip，需预装 .NET 8 桌面运行时 | Inno Setup（`PrivilegesRequired=admin`）+ 自带运行时；**卸载时用 PowerShell 注销计划任务** |
| 打包 | `tools/release.ps1` 手动 | PyInstaller onedir + `_internal`、`uac_admin=True`、UPX 关、排除约 40 个 Qt 模块、依赖全部钉版本 |
| 代码签名 | 未见 | 无 |
| 更新通道 | 无 | 有（i18n 语言包走远程，只校验 version；规则商店走 Gitee） |

### 3.7 你比他强、但容易被忽略的一项：日志脱敏

- 你：`PathRedactor` / `LogRedactor`——日志与异常里的密钥、真实路径都抹掉。
- 他：**0 处脱敏**。日志直接写 `[回收站] {path}` 这种原始绝对路径，导出 `.txt` 给别人排查时**会连用户名、目录结构一起带走**。

---

## 4. 他比你强的地方（按对你价值排序）

1. **发行与运营**：CI 自动出包发版、有安装包、有文档站、有社群、1806 star；release notes 修的是"安装后权限不足无法启动"这类真实用户坑。
2. **开箱即用**：自带运行时，下载即用；你没有安装/卸载闭环，用户还要先装 .NET 8。
3. **功能覆盖**：定时任务 + 自启 + 托盘 + 右键菜单清理 + 强力卸载（`sc delete` / `taskkill /T /F` / `reg delete /f`）。
4. **规则可分发给用户**：规则商店能远程下载导入，用户不用等你发版就能加规则（代价见第 6 节：无签名）。
5. **AI 软件缓存覆盖细且诚实**：细到 `Cache` / `Code Cache` / `GPUCache` / `Service Worker` / `Crashpad`，并在描述里写明"清理后无法恢复""会话/记忆/凭据不在清理范围"。
6. **删除给用户选择（也是双刃剑）**：回收站 + 永久删除双选项，清大文件、清模型缓存更彻底。
7. **性能上肯下本**：为大文件扫描专门写零依赖 Rust MFT 扫描器（读 `\\.\C:`、校验 `NTFS    ` 签名、解析 MFT 记录 5 + data runs + USA fixup，1 MiB 分块）。
8. **界面打磨**：Fluent 2、深浅色/跟随系统、侧边栏横竖切换、可拖列宽、Shift 批量勾选、日志导出。

---

## 5. 你比他强的地方（该守住的）

1. **MFT 全盘建树**：秒级出目录树 + 占比 + 文件夹整理；主程序内实现，不依赖外部 exe，行为稳定。
2. **删除更保守**：只回收站、无"永久删除"选项、删除预检 + 保护闸 + 执行前身份复核。
3. **隐私与密钥**：日志/异常脱敏 + API Key DPAPI 加密落盘。
4. **AI 只读克制设计**：AI 只写说明、不改勾选/风险——这是真差异点。
5. **语义化清理模型**：风险 + 证据等级 + 可删性 + 用途分层 + 分组，比"纯路径清单"细。
6. **供应链更干净**：没有远程规则下载，也就没有未签名规则包的风险面。
7. **分层代码结构 + 离线校验工具链**（SafetyCheck / StartupCheck / CleanAnalyzerCheck 等 9 个），最大文件 4920 行；他是 14,978 行单文件、最大类 1676 行。

---

## 6. 他的缺点与风险（不要学的部分）

1. **规则包无完整性校验**：商店索引只有 `title/filename/source/summary/detail`，**没有 version / URL / sha256**；包体来自**第三方 Gitee 仓库**，只有"文件名消毒 + 4 MB 上限"两道防线。
2. **规则是通配符**：`fnmatch` 匹配，远程内容一旦变宽就扩大删除范围，唯一兜底是 `delete_path` 的保护路径判断。
3. **永久删除默认开启**：UI 默认勾选、定时任务默认永久、占用文件延迟到重启静默删——对清理工具来说这是最大的行为风险。
4. **定时任务静默无人值守**，且**定时分支跑在提权检查之前**。
5. **真删路径无自动化测试**：删除相关断言全是 mock；未覆盖盘符根、`pagefile.sys`、用户目录根、重解析点、占用文件、glob 展开。
6. **日志零脱敏**：导出的诊断日志会带走真实用户名与路径。
7. **本地规则库是死数据**：`winapp2.json`（841 KB）+ `bleachbit.json`（113 KB）随包但 main.py 未引用。
8. **脆弱测试**：部分测试用 `inspect.getsource` 检查源码文本，测的是"代码长什么样"而不是行为。
9. **两套保护实现可能漂移**：Rust 工具里另有一套硬编码保护前缀（`PAGEFILE`、`*.sys`、`$RECYCLE.BIN`、`WINDOWS\SYSTEM32\CONFIG` 等），与 Python 侧 `is_protected_system_path` 不共享代码。
10. **可选组件导致打包行为不一致**：Rust exe "有就用、没有就降级"。
11. **单文件 14,978 行 / 65 万字节**：改动风险高，53 个测试密度偏低。
12. **无代码签名**；**无 QThread**（线程 + 信号），强力卸载单步超时 1200 秒、还原点创建在 worker 上跑，存在界面卡顿/长阻塞面。

---

## 7. 建议路线（追平并保持差异）

| 优先级 | 做什么 | 为什么 |
|---|---|---|
| P0 | **CI 跑测试 + 自动打 zip + 自动发 Release** | 纯工程活、回报最大；你现在测试不跑、包手打 |
| P1 | **8 条规则抽成外部 JSON 规则包**（可导入导出/远程更新） | 解决"改规则要重新发版"；保留你的对象模型（风险/证据/保护标记），并加 **sha256 + 版本号 + 固定 tag（不要可变分支）** |
| P2 | **定时扫描 + 托盘**，但做成"定时只扫 + 出报告，删除仍需用户确认" | 补功能空白，并把他"静默永久删除"的风险反过来变成你的安全卖点 |
| P3 | **安装包 + 卸载闭环**（自包含发布或引导装 .NET 8） | 降低"先装 .NET 8"的流失 |

---

## 8. 证据与核实状态

**已核实（有代码/文件/API 证据）**
- `main.py` 全量核对：本地副本 `D:\_inspect\c_cleaner_plus_main.py`（654,154 B / 14,978 行，sha256 `0A0B138E…`，对应 GitHub blob `eb808620…`）。
- 扫描：main.py 内 MFT 相关命中 0；Rust 扫描器源码见仓库 `tools/fast_large_files/src/main.rs`（编译产物未入库），调用点、60 秒超时、环境变量开关、盘根限定、Python 回退均已核对。
- 删除：`send_to_recycle_bin` / `delete_path` / `is_protected_system_path` / `MoveFileExW` 延迟删除 / 12 处删除入口收敛，均已核对。
- 规则与商店：`config_store.json`、`config/rules_ai_tools.json`、`parse_rule_entry`、Gitee 商店地址与 4 MB 上限。
- 测试覆盖：`tests/test_safety_regressions.py`（46 个方法）+ `tests/test_clean_table_interaction.py`（7 个）。
- 打包：`requirements.txt`、`c_cleaner_plus.spec`、`installer/c_cleaner_plus.iss`。
- 社区/发版数据：GitHub API（repo / releases）。
- 本仓库数据：本地实测（文件数、体积、行数、`build.yml`、grep 托盘/定时/自启/脱敏）。

**未核实（诚实标注）**
- 规则商店实际下载到的包内容与校验行为（索引本身无哈希，包的远端内容未取）。
- `docs.cus.cc.cd` 文档站不可达（网络原因）。
- `.github/workflows/build.yml` 具体步骤（由产物与测试断言推断）。
- 发行物是否代码签名（`.spec` 显示 `codesign_identity=None`，未查证书）。
- 未做任何运行时测试（纯静态分析）。

**过程材料**
- 原始核对报告：`c_cleaner_plus_review.md`（仓库根目录，未纳入 git）。
- main.py 本地副本：`D:\_inspect\c_cleaner_plus_main.py`（临时分析用，可删）。

---

## 9. 参考链接

- 对方仓库：<https://github.com/Kiowx/c_cleaner_plus>
- 对方发版：<https://github.com/Kiowx/c_cleaner_plus/releases>
- 对方规则目录：<https://github.com/Kiowx/c_cleaner_plus/blob/main/config_store.json>
- 对方 Android 版：<https://github.com/Kiowx/Deep_Cleaner>
- 本仓库架构图：`docs/dashuohuo-runtime-architecture.html`
