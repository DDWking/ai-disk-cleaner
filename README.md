# AI 磁盘清理

Windows 下的磁盘占用分析工具。直接读 NTFS 的 `$MFT`，秒级扫盘。

> 需要管理员权限（读卷 / MFT）。目前只支持 NTFS。

## 功能

- MFT 秒扫：按 `$MFT` data run 读完整主文件表，不靠递归 `Directory.GetFiles`
- 清理中心（默认首页）：扫描后按用途分层归类；点分类**就地展开**位置，位置再就地展开候选文件；底部「全选」是单个切换（文字恒为「全选」，再点一次取消）；默认一项都不勾，清理前有独立检查页，删除进回收站
- 清理位置**行内直接看候选文件**（文件名 / 大小 / 修改时间 / 勾选），不用先跑 AI；要搜索或分页时再打开右侧明细面板
- 规则明确的批量勾选仍走预览弹层（操作栏不再放单独按钮）：只勾「正式规则判定为安全 + 证据到签名级 + 通过保护检查」的缓存/临时/转储项；大文件、旧文件、下载、压缩包、视频、个人资料和未知对象始终人工选择
- 文件夹整理（辅助入口）：本地识别文件夹用途；顶层（含系统入口）按容量降序；可勾选后把文件夹送进回收站。有子目录才有展开箭头；只有文件的目录给文件入口而不伪造箭头；链接未扫描与空目录分开表达，不会用 0 KB 暗示
- 顶部显示卷容量：总共 / 已用 / 可用；已配置 AI 时在容量右侧显示当前模型（绿点空闲 / 旋转分析中），点开模型设置
- 卸载页：只讲事实（名称 / 安装日期 / 用途 / 占用 / 操作），勾选后走官方卸载程序；卸完可扫残留，勾选后才删。引擎来自 [Bulk Crap Uninstaller](https://github.com/Klocman/Bulk-Crap-Uninstaller)（Apache 2.0）
  - 没有依据的软件保持**中性（未评估）**，不再用「无法确认是否正在运行」这类套话制造建议
  - 占用显式区分**扫描实测 / 安装记录估计 / 未知**；共享安装目录与系统目录不把整棵树算给某一个软件；未测得显示「未知」而不是 0
  - Windows 自带组件与设备驱动（按卸载程序路径 / 安装路径结构判定）不进泛化卸载建议与建议批选

## 下载

[Release v2.12.1](https://github.com/DDWking/ai-disk-cleaner/releases/tag/v2.12.1) 里有 Windows x64 压缩包。解压后右键 `AiDiskCleaner.exe` → 以管理员身份运行。需要已安装 [.NET 8 桌面运行时](https://dotnet.microsoft.com/download/dotnet/8.0)。

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
  Models/              FileEntry
  Native/              NTFS P/Invoke
  Services/            MFT / 递归 / 模拟扫描
```

## 协议

本仓库代码 [MIT](LICENSE)。卸载引擎来自 Bulk Crap Uninstaller，Apache 2.0，见 [THIRD-PARTY.md](THIRD-PARTY.md)。
