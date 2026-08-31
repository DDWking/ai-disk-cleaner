# AI 磁盘清理

Windows 下的磁盘占用分析工具。直接读 NTFS 的 `$MFT`，秒级扫盘；界面参考 [WizTree](https://diskanalyzer.com/)，左边文件夹树、右边当前目录。

> 需要管理员权限（读卷 / MFT）。目前只支持 NTFS。

## 功能

- MFT 秒扫：按 `$MFT` data run 读完整主文件表，不靠递归 `Directory.GetFiles`
- 左边目录树：按占用排序，显示占比和大小
- 右边当前目录：文件夹 + 文件，默认按大小降序
- 顶部显示卷容量：总共 / 已用 / 可用
- 文件名搜索（当前目录）
- 临时 / 日志文件的清理建议（还很初级）

## 运行

环境：Windows 10/11 + [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)

```powershell
cd src\AiDiskCleaner
dotnet build
# 以管理员身份运行
.\bin\Debug\net8.0-windows\AiDiskCleaner.exe
```

首次启动会弹出 UAC。拒绝的话扫不了 MFT，会回退到很慢的递归扫描。

## 仓库结构

```
src/AiDiskCleaner/     WPF 主程序
  Models/              FileEntry
  Native/              NTFS P/Invoke
  Services/            MFT / 递归 / 模拟扫描
```

## 一起开发

进度和开发日志在 [PROGRESS.md](PROGRESS.md)，开工前先看谁在做什么。协作方式见 [CONTRIBUTING.md](CONTRIBUTING.md)。

简单说（朋友被加成协作者之后，直接推 `main`）：

```powershell
git clone https://github.com/DDWking/ai-disk-cleaner.git
cd ai-disk-cleaner
git pull origin main
# 改代码
git add .
git commit -m "feat: 一句话说清楚改了什么"
git pull origin main
git push origin main
```

## 协议

[MIT](LICENSE)
