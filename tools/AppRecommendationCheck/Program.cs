// 卸载页的**事实**回归检查。
//
// 目录名是历史遗留：这里以前测的是「卸载建议」（档位 / 理由 / 远程建议降档）。
// 卸载页改成只讲事实（名称 / 安装日期 / 用途 / 占用 / 操作）之后，那套机制在生产里
// 没有任何调用方，已整体删除，相关断言随之删除 —— 见 UiRegressionCheck 里
// 「uninstall page shows no advice」那条不变量。
//
// 现在守的是：真实占用怎么归属（共用目录整棵树不算给单个软件、跨盘不臆造、
// 系统目录不归属）、占用来源三态文案、以及 Windows 自带组件的结构化判定。
using AiDiskCleaner.Models;
using AiDiskCleaner.Services;

int failed = 0;

void Check(string name, bool ok)
{
    Console.WriteLine((ok ? "PASS " : "FAIL ") + name);
    if (!ok) failed++;
}

/// <summary>带诊断信息的一版：失败时把实际值打出来。</summary>
void CheckD(string name, bool ok, string detail)
{
    Console.WriteLine((ok ? "PASS " : "FAIL ") + name + (ok ? "" : "  [" + detail + "]"));
    if (!ok) failed++;
}

AppUninstallItem App(string name, string location, long estimated)
    => new()
    {
        AppId = name,
        Name = name,
        InstallLocation = location,
        SizeBytes = estimated,
        ActualSizeBytes = estimated,
        CanUninstall = true,
    };

var root = App("root", @"D:\", 100);
var sharedA = App("shared-a", @"C:\Apps\Shared", 200);
var sharedB = App("shared-b", @"C:\Apps\Shared", 300);
var nested = App("nested", @"C:\Apps\Shared\Child", 400);
var unique = App("unique", @"C:\Apps\Unique", 500);

var files = new List<FileEntry>
{
    new() { FullPath = @"D:\huge.bin", Size = 9_000 },
    new() { FullPath = @"C:\Apps\Shared\main.bin", Size = 2_000 },
    new() { FullPath = @"C:\Apps\Shared\Child\child.bin", Size = 3_000 },
    new() { FullPath = @"C:\Apps\Unique\main.bin", Size = 1_234 },
};
var usage = AppRecommendationService.CalculateUsage(
    new[] { root, sharedA, sharedB, nested, unique },
    files);
AppRecommendationService.ApplyUsage(usage);

Check("volume root falls back to registry estimate",
    !root.HasMeasuredSize && root.ActualSizeBytes == root.SizeBytes);
Check("same install location is not double-counted",
    !sharedA.HasMeasuredSize && !sharedB.HasMeasuredSize);
Check("nested install locations are not double-counted",
    !nested.HasMeasuredSize && nested.ActualSizeBytes == nested.SizeBytes);
Check("unique install location uses scanned file usage",
    unique.HasMeasuredSize && unique.ActualSizeBytes == 1_234);

// ---- Windows 自带组件：**结构化判定**，不靠名字子串（事实能力，保留）----
// 实拍依据：本机 `Microsoft® Windows® Operating System` 的卸装命令是
//   "C:\Windows\System32\mstsc.exe" /uninstall，安装位置 C:\Windows\System32，
// 而 BCU 的 SystemComponent / IsProtected 都是 false —— 只看 BCU 标记会漏掉它。
//
// 这里原来还有一组断言，守的是「卸载建议」那套档位：已知捆绑特征的分档、
// 中性档（未评估）、正在运行 ⇒ 需要看一下、体积大/开机自启不算依据、
// 远程建议不得越过运行状态、系统条目落「建议保留」。
// 卸载页改成**只讲事实**之后，那套机制在生产里没有任何调用方（已整体删除），
// 这些断言随之消失 —— 记在 PR 说明里，不在这里假装还测着。
// 「系统条目怎么认出来」这条能力**没有丢**：下面三条就是它的结构化断言。
var windowsDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
var mstsc = App("Microsoft® Windows® Operating System", Path.Combine(windowsDir, "System32"), 0);
mstsc.Entry = new UninstallTools.ApplicationUninstallerEntry
{
    UninstallString = "\"" + Path.Combine(windowsDir, "System32", "mstsc.exe") + "\" /uninstall",
};
Check("结构化判定：卸装程序在 Windows 目录内 ⇒ 系统自带组件",
    AppRecommendationService.IsInboxComponent(mstsc));
Check("只有安装位置在 Windows 目录内也算系统自带组件",
    AppRecommendationService.IsInboxComponent("", Path.Combine(windowsDir, "System32")));
Check("普通 Program Files 软件不算系统自带组件",
    !AppRecommendationService.IsInboxComponent(
        @"C:\Program Files\SomeApp\uninst.exe", @"C:\Program Files\SomeApp"));

// ---- 共享父目录 / 系统目录：整棵树的总量不算给某一个软件 ----
var systemTreeApp = App("System Tree App", Path.Combine(windowsDir, "System32"), 0);
var systemTreeFiles = new List<FileEntry>
{
    new() { FullPath = Path.Combine(windowsDir, "System32", "kernel32.dll"), Size = 13_000_000_000 },
};
AppRecommendationService.InvalidateUsageCache();
var sysUsage = AppRecommendationService.CalculateUsage(new[] { systemTreeApp }, systemTreeFiles);
AppRecommendationService.ApplyUsage(sysUsage);
CheckD("Windows 目录里的整棵树不归属给单个软件",
    !systemTreeApp.HasMeasuredSize, systemTreeApp.ActualSizeBytes.ToString());
CheckD("未归属时如实说明原因（系统目录）",
    systemTreeApp.FootprintNote == Loc.AppFootprintWhySystemDir, systemTreeApp.FootprintNote);
CheckD("未测得 ⇒ 不显示臆造的占用", systemTreeApp.ActualSizeText == Loc.AppSizeUnknown,
    systemTreeApp.ActualSizeText);
CheckD("未测得 ⇒ 占用来源标记为未知",
    systemTreeApp.FootprintSource == AppFootprintSource.Unknown, systemTreeApp.FootprintSource.ToString());

// ---- 占用拆分：程序本体 / 保存的数据 / 缓存（不卸载就能清掉的那块）----
// 范围说明：只统计**安装目录这棵树**。装在 AppData 里的用户数据不在其中（见 ClassifyFootprint 注释）。
var big = App("big-app", @"C:\Apps\Big", 0);
var mixed = new List<FileEntry>
{
    new() { FullPath = @"C:\Apps\Big\app.exe", Size = 1_000 },
    new() { FullPath = @"C:\Apps\Big\Cache\data.bin", Size = 4_000 },
    new() { FullPath = @"C:\Apps\Big\userdata\profile.dat", Size = 2_000 },
    // 不在安装目录下的文件不计入这个软件
    new() { FullPath = @"C:\Users\Someone\AppData\Local\Big\settings.json", Size = 3_000 },
};
AppRecommendationService.InvalidateUsageCache();
AppRecommendationService.ApplyUsage(AppRecommendationService.CalculateUsage(new[] { big }, mixed));

CheckD("程序本体大小单独统计", big.InstallDirBytes == 1_000, big.InstallDirBytes.ToString());
CheckD("缓存大小单独统计", big.CacheBytes == 4_000, big.CacheBytes.ToString());
CheckD("保存的数据单独统计", big.UserDataBytes == 2_000, big.UserDataBytes.ToString());
CheckD("估计可释放 = 缓存部分", big.ReclaimableBytes == 4_000, big.ReclaimableBytes.ToString());
Check("占用拆分有明细文案", big.HasFootprintBreakdown && big.FootprintText.Length > 0);
CheckD("三项之和等于测得的安装目录占用",
    big.InstallDirBytes + big.UserDataBytes + big.CacheBytes == big.ActualSizeBytes,
    $"{big.InstallDirBytes}+{big.UserDataBytes}+{big.CacheBytes} vs {big.ActualSizeBytes}");
CheckD("安装目录外的数据不并进来", big.ActualSizeBytes == 7_000, big.ActualSizeBytes.ToString());

// 分类判定本身（大小写不敏感）
Check("cache 路径归为缓存",
    AppRecommendationService.ClassifyFootprint(@"c:\apps\a\Cache\x.bin") == AppRecommendationService.FootprintKind.Cache);
Check("userdata 路径归为保存的数据",
    AppRecommendationService.ClassifyFootprint(@"C:\Apps\A\UserData\x.bin") == AppRecommendationService.FootprintKind.UserData);
Check("普通文件归为程序本体",
    AppRecommendationService.ClassifyFootprint(@"c:\apps\a\app.exe") == AppRecommendationService.FootprintKind.InstallDir);

// 占用缓存：同一份输入不重复计算；换了输入要重算
AppRecommendationService.InvalidateUsageCache();
var first = AppRecommendationService.CalculateUsage(new[] { big }, mixed);
var second = AppRecommendationService.CalculateUsage(new[] { big }, mixed);
Check("相同输入命中缓存（返回同一实例）", ReferenceEquals(first, second));
AppRecommendationService.InvalidateUsageCache();
var third = AppRecommendationService.CalculateUsage(new[] { big }, mixed);
Check("显式失效后重新计算", !ReferenceEquals(first, third));

// ---- 占用可信度：实测 / 安装记录 / 未知三种文案必须分得开 ----
// 实拍反馈：一整列「0 G（估算）」，既没有信息量又让人以为软件只有 0。
CheckD("小值不会被四舍五入成 0 G",
    AppUninstallItem.FormatFootprint(400L * 1024 * 1024) == "400 MB",
    AppUninstallItem.FormatFootprint(400L * 1024 * 1024));
CheckD("比 1 G 小但比 1 M 大的值落在 MB，不会变成 0 G",
    AppUninstallItem.FormatFootprint(63L * 1024 * 1024) == "63.0 MB",
    AppUninstallItem.FormatFootprint(63L * 1024 * 1024));
CheckD("0 字节就是 0 KB，不写 0 G", AppUninstallItem.FormatFootprint(0) == "0 KB",
    AppUninstallItem.FormatFootprint(0));
CheckD("小于 1 KB 如实写 < 1 KB，不写 0 KB",
    AppUninstallItem.FormatFootprint(512) == "< 1 KB", AppUninstallItem.FormatFootprint(512));
CheckD("刚好 1 GB 不会显示成 0 G",
    AppUninstallItem.FormatFootprint(1024L * 1024 * 1024) == "1.0 G",
    AppUninstallItem.FormatFootprint(1024L * 1024 * 1024));

var measuredApp = App("measured", @"C:\Apps\Measured", 0);
measuredApp.ActualSizeBytes = 1_234_567;
measuredApp.HasMeasuredSize = true;
CheckD("实测就写实测数字，不带「估算」",
    measuredApp.ActualSizeText == AppUninstallItem.FormatFootprint(1_234_567)
    && measuredApp.FootprintSource == AppFootprintSource.Measured,
    measuredApp.ActualSizeText);

var recordApp = App("record-only", @"D:\Elsewhere", 1_000_000);
CheckD("只有安装记录时格子只写数字，来源进悬停",
    recordApp.ActualSizeText == AppUninstallItem.FormatFootprint(1_000_000)
    && recordApp.FootprintSource == AppFootprintSource.InstallRecord
    && recordApp.FootprintHint.Contains(Loc.AppSizeSourceRecord),
    recordApp.ActualSizeText + " | " + recordApp.FootprintHint);

var unknownApp = App("no-number", @"D:\Nowhere", 0);
CheckD("两样都没有 ⇒ 未知，不臆造",
    unknownApp.ActualSizeText == Loc.AppSizeUnknown
    && unknownApp.FootprintSource == AppFootprintSource.Unknown,
    unknownApp.ActualSizeText);

Check("占用悬停说明「磁盘占用不等于卸载可释放量」",
    measuredApp.FootprintHint.Contains(Loc.AppFootprintNotEqualFree));
Check("可信占用排序：实测 < 记录 < 未知",
    measuredApp.SizeConfidenceRank < recordApp.SizeConfidenceRank
    && recordApp.SizeConfidenceRank < unknownApp.SizeConfidenceRank);

// ---- 共享安装目录：不能把父目录总量算给每个软件 ----
var parent = App("parent-app", @"C:\Shared\Vendor", 10);
var childOne = App("child-one", @"C:\Shared\Vendor\One", 20);
var childTwo = App("child-two", @"C:\Shared\Vendor\Two", 30);
var sharedFiles = new List<FileEntry>
{
    new() { FullPath = @"C:\Shared\Vendor\One\a.bin", Size = 111 },
    new() { FullPath = @"C:\Shared\Vendor\Two\b.bin", Size = 222 },
};
AppRecommendationService.InvalidateUsageCache();
var sharedUsage = AppRecommendationService.CalculateUsage(new[] { parent, childOne, childTwo }, sharedFiles);
AppRecommendationService.ApplyUsage(sharedUsage);
CheckD("共享父目录不吞掉子目录的总量", !parent.HasMeasuredSize, parent.ActualSizeBytes.ToString());
CheckD("共享父目录说明原因是「与其他软件共用」",
    parent.FootprintNote == Loc.AppFootprintWhyShared, parent.FootprintNote);
CheckD("共享关系里的每个软件都不重复归属",
    !childOne.HasMeasuredSize && !childTwo.HasMeasuredSize,
    $"{childOne.HasMeasuredSize}/{childTwo.HasMeasuredSize}");

// ---- 跨盘：安装目录不在本次扫描的盘里就如实说没测到 ----
var crossDrive = App("cross-drive", @"Z:\NotScanned\App", 5_000);
var localFiles = new List<FileEntry> { new() { FullPath = @"C:\Only\here.bin", Size = 10 } };
AppRecommendationService.InvalidateUsageCache();
AppRecommendationService.ApplyUsage(AppRecommendationService.CalculateUsage(new[] { crossDrive }, localFiles));
CheckD("跨盘安装目录不算出假数字，也不假装是实测",
    !crossDrive.HasMeasuredSize && crossDrive.ActualSizeBytes == crossDrive.SizeBytes,
    crossDrive.ActualSizeBytes.ToString());
CheckD("跨盘时如实说明「不在本次扫描的盘里」",
    crossDrive.FootprintNote == Loc.AppFootprintWhyNotScanned, crossDrive.FootprintNote);

Console.WriteLine(failed == 0 ? "All checks passed" : $"{failed} checks failed");
return failed == 0 ? 0 : 1;
