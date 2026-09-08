using AiDiskCleaner.Models;
using AiDiskCleaner.Services;

int failed = 0;

void Check(string name, bool ok)
{
    Console.WriteLine((ok ? "PASS " : "FAIL ") + name);
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

Console.WriteLine(failed == 0 ? "All checks passed" : $"{failed} checks failed");
return failed == 0 ? 0 : 1;
