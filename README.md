# 大扫货（AI 磁盘清理）

Windows 下的磁盘占用分析工具。直接读 NTFS 的 `$MFT`，秒级扫盘。

**一句话定位：本地规则先说清楚，说不清的那部分交给结构化判定模型（Jev），最后删什么由人决定。**

> 需要管理员权限（读卷 / MFT）。目前只支持 NTFS。

## 功能

- MFT 秒扫：按 `$MFT` data run 读完整主文件表，不靠递归 `Directory.GetFiles`
- 清理中心（默认首页）：扫描后按用途分层归类；点分类**就地展开**位置，位置再就地展开候选文件；底部「全选」是单个切换（文字恒为「全选」，再点一次取消）；默认一项都不勾，清理前有独立检查页，删除进回收站
- 清理位置**行内直接看候选文件**（文件名 / 大小 / 修改时间 / 勾选）；要搜索或分页时再打开右侧明细面板
- 规则明确的批量勾选仍走预览弹层：只勾「正式规则判定为安全 + 证据到签名级 + 通过保护检查」的缓存/临时/转储项；大文件、旧文件、下载、压缩包、视频、个人资料和未知对象始终人工选择
- 文件夹删除（辅助入口）：本地识别文件夹用途；顶层（含系统入口）按容量降序；可勾选后把文件夹送进回收站。有子目录才有展开箭头；只有文件的目录给文件入口而不伪造箭头；链接未扫描与空目录分开表达，不会用 0 KB 暗示
  - 本地认不出来的那一屏，点一次「识别这一屏」交给**判定模型**批量归类（见下）
- 重复文件：检测到内容相同的组之后，**每组可以问一次「留哪一份」**。它只加一句建议在你的保留项那一行上，不替你勾选、不改哪份是保留项；没把握时什么都不说（见下）
- 顶部显示卷容量：总共 / 已用 / 可用
- 卸载页：只讲事实（名称 / 安装日期 / 用途 / 占用 / 操作），勾选后走官方卸载程序；卸完可扫残留，勾选后才删。引擎来自 [Bulk Crap Uninstaller](https://github.com/Klocman/Bulk-Crap-Uninstaller)（Apache 2.0）
  - 没有依据的软件保持**中性（未评估）**，不用套话制造建议
  - 占用显式区分**扫描实测 / 安装记录估计 / 未知**；共享安装目录与系统目录不把整棵树算给某一个软件；未测得显示「未知」而不是 0
  - Windows 自带组件与设备驱动（按卸载程序路径 / 安装路径结构判定）不进泛化卸载建议与建议批选

## 判定模型（Jev）怎么用

设置里只填四项：**地址 + 密钥 + 判定模型 + 是否允许发完整路径**（默认只发脱敏路径）。

- 地址填 OpenRouter 的判定端点前缀（例如 `https://openrouter.ai/api/alpha`，程序会接上 `/decisions`），
  模型填 `typesafe/jev-1.13`。也可以直接粘完整地址（以 `/decisions` 结尾时原样使用）。
- 「测试判定通道」会真的发一道判断题，回报往返毫秒、**服务端回报的模型 id**（alpha 端点会悄悄换版本，
  只有这里能看见）和花费。

三条边界，代码里是硬约束：

1. **只有你点按钮才会发请求。** 扫描 / 切页 / 展开 / 筛选全程 0 次请求（回归用真实出站计数断言）。
   重复文件的建议也一样：它不在扫描流程里，得你点那一行上的按钮。
2. **只有本地认不出来的才问。** 本地规则认得出的绝不上行；一次最多 80 条，超了自动分批，
   一批撑爆上下文会拆两半重试。重复组里成员超过 12 个的**连问都不问** ——
   十几个差不多的路径摆一起，模型也只能瞎猜，猜错的代价是你删错文件。
3. **AI 永远不会替你打勾。** 判定结果只写「用途」这一个展示字段，
   重复建议只在你保留项那一行的说明后面加一句话 —— 整理树和重复项的模型里根本没有
   风险 / 可删除 / 勾选这些槽位（重复建议的阈值也更严：低于 0.75 一个字都不说）。

本地规则、签名表、保护路径、删除预检、回收站链路全部离线可测（`tools/run-all-checks.ps1`）。

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
src/AiDiskCleaner/     WPF 主程序
  Models/              文件 / 清理项 / 文件夹树节点
  Native/              NTFS P/Invoke、回收站、快捷方式
  Services/            MFT / 递归扫描、本地规则、保护路径、删除预检
    Decisions/         判定层：契约、分批执行器、硬限制、阈值、判定域
tools/                 10 个离线检查 + run-all-checks.ps1（一条命令跑完）+ release.ps1
```

## 一起开发

```powershell
# 一条命令跑完 10 项离线检查（发布门）
powershell -NoProfile -ExecutionPolicy Bypass -File tools/run-all-checks.ps1
# 出包：构建 → 自包含发布 → zip → sha256 → 哈希一致性校验
powershell -NoProfile -ExecutionPolicy Bypass -File tools/release.ps1
```

## 协议

本仓库代码 [MIT](LICENSE)。卸载引擎来自 Bulk Crap Uninstaller，Apache 2.0，见 [THIRD-PARTY.md](THIRD-PARTY.md)。
