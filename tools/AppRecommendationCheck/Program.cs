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
Check("unknown running state does not get a local uninstall recommendation",
    AppRecommendationService.LocalRule(unknownBloat).Decision == AppRecommendationDecision.Consider);

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
Check("unknown running state downgrades remote uninstall recommendation",
    aiBloat.Recommendation == AppRecommendationDecision.Consider
    && aiBloat.RecommendationWarning.Contains("verify", StringComparison.OrdinalIgnoreCase));

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

Console.WriteLine(failed == 0 ? "All checks passed" : $"{failed} checks failed");
return failed == 0 ? 0 : 1;
