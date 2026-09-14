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

var unknownBloat = App("2345 helper", @"D:\", 1);
unknownBloat.RunningState = AppRunningState.Unknown;
var unknownBloatRule = AppRecommendationService.LocalRule(unknownBloat);
CheckD("已知捆绑特征不会因为「运行状态未知」被丢掉，但也不升到「建议卸载」",
    unknownBloatRule.Decision == AppRecommendationDecision.Consider
    && unknownBloatRule.Reason == Loc.AppRecommendBloat,
    unknownBloatRule.Decision + " / " + unknownBloatRule.Reason);
Check("这一档的理由是真信号，不是「无法确认是否正在运行」那句套话",
    unknownBloatRule.Reason != Loc.AppConsiderRunning && unknownBloatRule.DataWarning.Length == 0);

// ---- 中性档：没有依据就是没有依据，不许用套话凑出「可以考虑」----
// 实拍反馈：207 行都写着「可以考虑 · 无法确认是否正在运行 · 卸载前请确认软件已退出」。
// 那句话对每一行都一样，等于没有信息。
var ordinary = App("Some Ordinary App", @"C:\Apps\Ordinary", 2_000);
ordinary.RunningState = AppRunningState.Unknown;
ordinary.HasStartup = true;
var ordinaryRule = AppRecommendationService.LocalRule(ordinary);
CheckD("普通未知应用落中性档（不是「可以考虑」）",
    ordinaryRule.Decision == AppRecommendationDecision.Neutral, ordinaryRule.Decision.ToString());
CheckD("中性档不带套话理由", ordinaryRule.Reason.Length == 0, ordinaryRule.Reason);
AppRecommendationService.ApplyLocalRules(new[] { ordinary });
Check("中性档只显示「未评估」，不带任何理由/警告",
    ordinary.RecommendationText == Loc.AppRecommendationLabel(AppRecommendationDecision.Neutral)
    && ordinary.RecommendationReason.Length == 0 && ordinary.RecommendationWarning.Length == 0);
Check("中性档的悬停里说明了可以去搜索/排序/自己卸",
    ordinary.RecommendationHint.Contains(Loc.AppNeutralHint));
Check("中性档不被自动勾选", !ordinary.Selected);

// 运行状态未知**不再**制造建议；正在运行才是「需要看一下」。
var runningApp = App("Running App", @"C:\Apps\Running", 2_000);
runningApp.RunningState = AppRunningState.Running;
var runningRule = AppRecommendationService.LocalRule(runningApp);
CheckD("正在运行 ⇒ 需要看一下（而不是中性）",
    runningRule.Decision == AppRecommendationDecision.Consider, runningRule.Decision.ToString());
Check("正在运行的提醒只在真的在跑时出现",
    runningRule.DataWarning == Loc.AppRunningWarning && runningRule.Reason == Loc.AppConsiderRunning);

// 「大」「会随系统启动」都不是卸载依据，不能进「可以考虑」。
var bigOnly = App("Big Neutral App", @"C:\Apps\BigNeutral", 9L * 1024 * 1024 * 1024);
CheckD("只是体积大不再产生建议", AppRecommendationService.LocalRule(bigOnly).Decision == AppRecommendationDecision.Neutral,
    AppRecommendationService.LocalRule(bigOnly).Decision.ToString());
CheckD("只是开机自启不再产生建议",
    AppRecommendationService.LocalRule(ordinary).Decision == AppRecommendationDecision.Neutral,
    AppRecommendationService.LocalRule(ordinary).Decision.ToString());

var aiBloat = App("2345 browser", @"D:\", 1);
aiBloat.RunningState = AppRunningState.Unknown;
AppRecommendationService.ApplyAiResults(
    new[] { aiBloat },
    new[]
    {
        new AppRecommendation
        {
            AppId = aiBloat.AppId,
            Decision = AppRecommendationDecision.Recommend,
            Reason = "remote result",
        },
    });
Check("运行的软件不得被远程建议抬成「建议卸载」",
    aiBloat.Recommendation == AppRecommendationDecision.Consider
    && aiBloat.RecommendationWarning.Length > 0);

// ---- 系统 / 驱动 / 运行库：结构化判定，不靠名字子串，也不进泛化建议 ----
// 实拍依据：本机 `Microsoft® Windows® Operating System` 的卸装命令是
//   "C:\Windows\System32\mstsc.exe" /uninstall，安装位置 C:\Windows\System32，
// 而 BCU 的 SystemComponent / IsProtected 都是 false —— 只看 BCU 标记会漏掉它。
var windowsDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
var mstsc = App("Microsoft® Windows® Operating System", Path.Combine(windowsDir, "System32"), 0);
mstsc.Entry = new UninstallTools.ApplicationUninstallerEntry
{
    UninstallString = "\"" + Path.Combine(windowsDir, "System32", "mstsc.exe") + "\" /uninstall",
};
var mstscRule = AppRecommendationService.LocalRule(mstsc);
CheckD("Windows 系统条目不进「可以考虑」",
    mstscRule.Decision != AppRecommendationDecision.Consider, mstscRule.Decision.ToString());
CheckD("Windows 系统条目落「建议保留」并说明原因",
    mstscRule.Decision == AppRecommendationDecision.Keep && mstscRule.Reason == Loc.AppKeepInboxComponent,
    mstscRule.Reason);
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
