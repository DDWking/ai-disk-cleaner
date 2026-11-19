# 大扫货（AI 磁盘清理）

Windows 下的磁盘占用分析工具。直接读 NTFS 的 `$MFT`，秒级扫盘。

**一句话定位：本地规则先把能删的算清楚；本地认不出的那部分，发给一个你自己配的模型做研判；最后删什么由人决定。**

> 需要管理员权限（读卷 / MFT）。目前只支持 NTFS。

## 功能

- MFT 秒扫：按 `$MFT` data run 读完整主文件表，不靠递归 `Directory.GetFiles`
- 清理中心（默认首页）：扫描后按用途分层归类，分「清理候选」和「需要你确认」两层；点分类**就地展开**位置，位置再就地展开候选文件；默认一项都不勾；删除前先走独立的「清理前检查」页（会动几个位置、多少项、多大体积、几项需要逐项确认），确认后才执行，落回收站
- 规则明确的批量勾选仍走预览弹层：只勾「正式规则判定为安全 + 证据到签名级 + 通过保护检查」的缓存/临时/转储项；大文件、旧文件、下载、压缩包、视频、个人资料和未知对象始终人工选择
- 按文件夹删除（右侧页签）：本地识别文件夹用途；顶层按容量降序；有子目录才有展开箭头，只有文件的目录给文件入口而不伪造箭头；链接未扫描与空目录分开表达，不会用 0 KB 暗示；勾选后先看确认框（几个文件夹、多大、前 12 个的名称与体积及原因、几个受保护/无回收站会被跳过、都会进回收站可还原），确认才执行
- **AI 分析**：扫描完成后点操作区（亮橙删除胶囊旁）的「AI 分析」，模型读目录摘要，报告直接落在右侧「按文件夹删除」面板里（绿 / 黄 / 红），点卡片定位对应文件夹，**不改勾选、不删文件**（见下）
- 重复文件：分阶段哈希（大小 → 首段 → 尾段 → 全文），硬链接合并成一个代表；每组的保留项与多余项都默认不勾，删哪一份由你定；撞到读取预算会明说「没跑完」，不会假装找全了
- 顶部显示卷容量：总共 / 已用 / 可用；启动后静默预扫 `C:` / `D:`（首屏稳定再跑，低优先级），落一份轻量目录缓存，下次打开直接可用
- 卸载页：只讲事实（名称 / 安装日期 / 用途 / 占用 / 操作），勾选后调程序自己的卸载入口；卸载完成后可顺手扫残留，高置信度项会被预勾选、删前请你核对（文件进回收站，注册表项直接删）。引擎来自 [Bulk Crap Uninstaller](https://github.com/Klocman/Bulk-Crap-Uninstaller)（Apache 2.0）
  - 卸载页**不下「建议卸载」结论**：旧的档位（建议卸载 / 可以考虑 / 建议保留 / 未评估）已经连判定带文案整体删掉，只呈现事实
  - 占用显式区分**扫描实测 / 安装记录估计 / 未知**；共享安装目录与系统目录不把整棵树算给某一个软件；未测得显示「未知」而不是 0
  - Windows 组件按安装路径结构识别（不盲信卸载项里的旧标记），用途一栏写明「Windows 组件」和判定依据

## AI 分析怎么用

接入的是一个**普通 OpenAI 兼容聊天接口**（`/chat/completions`），不是专用判定服务。设置里给一套模板：

| 模板 | 地址 | 模型 |
|---|---|---|
| DeepSeek（推荐） | `https://api.deepseek.com/v1` | `deepseek-chat` |
| OpenAI 官方 | `https://api.openai.com/v1` | `gpt-4o-mini` |
| Ollama（本地） | `http://localhost:11434/v1` | `qwen2.5:7b`，不需要密钥 |
| 自定义 | 任何兼容 `/chat/completions` 的中转、公司网关或私有服务 | 自己填 |

- 地址填到 `/v1` 为止，程序自动补 `/chat/completions`；也可以直接粘完整地址
- 密钥用 Windows DPAPI 加密存放：**不写进 `settings.json`，也不进日志**；本地中转可以留空
- 「测试连接」会真的发一次最小聊天请求。推理模型（如 DeepSeek V4 Flash）常常只回思考、不回正文，这种情况按**连通**算，不会误报成「协议不对」
- 「获取模型」从上游拉模型列表，拉不到不阻塞手动输入
- 顶栏有状态灯：AI 未接入（红）/ 未测试（黄）/ 连接失败并带原因（红）/ 连接通过并显示模型名（绿）

### 一条通道，四步本地流水线

1. **候选**：本地先挑值得问的目录，最多 100 个。
2. **证据卡**：每张卡只装判断需要的事实 —— 脱敏相对路径（用户名替换成 `<UserProfile>`）、大小、文件与目录数、修改时间、距今天数、前 8 个扩展名的数量与体积、最多 6 个样例文件名和 5 个样例目录名、产品线索、隐藏/系统/重解析点属性。
   **只发摘要，不发文件内容。**完整本地路径默认不出站（设置里的开关关着就永远是脱敏路径）。
3. **分批问**：25 张卡一批，最多 4 批（即最多 100 个目录）；要求模型只回一个 JSON 对象；输入累计超过 5 万 token 就停止后续批次，用已有结果出报告。
4. **本地校验**：回来的每一条都要过 `folderId` 是否存在于本次扫描、证据引用能否在卡里对上（`rule:` / `app:` / `age:` / `size:` / `lock:`）、`reclaimableBytes` 是否 ≤ 卡片体积、`verdict` 是否只是 `recommend` / `consider` / `keep` / `unknown`、同一目录是否只出现一次、总条数是否 ≤ 40。不合规的条目直接丢掉并记一条警告，不会悄悄放过。

### 三条硬边界，代码里是约束

1. **只有你点「AI 分析」才会发请求。** 扫描 / 切页 / 展开 / 筛选全程 0 次请求。
2. **报告只写结论，不产生删除目标。** 它不会往清理列表里塞东西，也不会改哪个路径是保留项。
3. **AI 永远不会替你打勾。** 点报告卡只做一件事：切到「按文件夹删除」页并定位、高亮那个文件夹；勾选与删除仍由你操作。

模型给的结论按三色落位：**绿（可清理）/ 黄（待考虑）/ 红（别动）**。系统目录、程序安装目录、用户文档在提示词里被明确禁止判成「可清理」。

### 出问题时你会看到什么

分析失败或部分降级都会写在面板上，不吞掉：

| 降级标记 | 含义 |
|---|---|
| `call_failed` | 上游报错，剩下的批次继续，但报告不全 |
| `call_failed_retryable` | 网络 / 限流一类可重试错误 |
| `reasoning_only` | 模型把输出额度全用在思考上，这批没有正文（换对话模型或调大输出额度） |
| `parse_failed` | 回来的不是可解析的 JSON |
| `budget` | 输入 token 超预算，主动停掉剩余批次 |

## 下载

[Release v3.0.0](https://github.com/DDWking/ai-disk-cleaner/releases/tag/v3.0.0) 里有 Windows x64 压缩包（自包含，不需要预装运行时）。
解压后右键 `AiDiskCleaner.exe` → 以管理员身份运行。

## 运行

环境：Windows 10/11 + [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)

需要卸载引擎 submodule：

```powershell
git clone --recurse-submodules https://github.com/DDWking/ai-disk-cleaner.git
# 已经 clone 过的：
git submodule update --init --recursive
```

```powershell
cd src\AiDiskCleaner
dotnet build
# 以管理员身份运行
.\bin\Debug\net8.0-windows10.0.18362.0\AiDiskCleaner.exe
```

首次启动会弹出 UAC。拒绝的话扫不了 MFT，会回退到很慢的递归扫描。

## 仓库结构

```
src/AiDiskCleaner/          WPF 主程序
  Models/                   文件 / 清理项 / 文件夹树节点
  Native/                   NTFS P/Invoke、回收站、快捷方式
  Services/                 MFT / 递归扫描、本地规则、保护路径、删除预检
    Agent/                  AI 分析：候选筛选、证据卡、报告装配与本地校验
      Chat/                 OpenAI 兼容客户端、模型目录、设置模板
      Tools/                4 个只读工具（列目录 / 取卡片 / 查条目 / 总览）
    CleanRules/             清理规则、重复检测、批选资格
tools/                      12 项离线检查（每项一个 tools/<Name>Check 工程）
                            + run-all-checks.ps1（一条命令跑完）+ release.ps1（出包）
vendor/Bulk-Crap-Uninstaller  卸载引擎（submodule）
```

AI 只在这条链路上：`按文件夹删除` 页 → 候选 → 证据卡 → 模型 → 本地校验 → 报告卡，一次往返、没有多轮工具调用（`Tools/` 那四个只读工具已经实现、也有离线检查覆盖，但还没接进这条链路）。检查、批选、删除预检、回收站链路全部离线可测。

## 一起开发

```powershell
# 一条命令跑完 12 项离线检查（发布门，全绿才继续）
powershell -NoProfile -ExecutionPolicy Bypass -File tools/run-all-checks.ps1
# 出包：构建 → 自包含发布 → zip → sha256 → 哈希一致性校验
powershell -NoProfile -ExecutionPolicy Bypass -File tools/release.ps1
```

12 项：SafetyCheck / StartupCheck / UiRegressionCheck / CleanAnalyzerCheck / CleanupPresentationCheck / AppPurposeCheck / AppRecommendationCheck / LocalRecognitionCheck / RecognitionPersistenceCheck / FolderDeleteCheck / AgentToolCheck / AgentReportCheck。

`release.ps1` 不跑检查，只负责构建、发布和校验哈希；发布前先用 `run-all-checks.ps1`。

## 协议

本仓库代码 [MIT](LICENSE)。卸载引擎来自 Bulk Crap Uninstaller，Apache 2.0，见 [THIRD-PARTY.md](THIRD-PARTY.md)。
