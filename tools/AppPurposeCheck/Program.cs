using AiDiskCleaner.Models;
using AiDiskCleaner.Services;
using UninstallTools;

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

/// <summary>描述文本里不许出现卸载判断/建议词（免责声明本身除外，只检查摘要）。</summary>
bool HasClaim(string text)
{
    string[] words =
    {
        "建议卸载", "可以卸载", "应该卸载", "建议保留", "可以保留", "安全删除", "放心删",
        "无害", "没用", "should uninstall", "safe to remove", "recommend", "better to",
    };
    foreach (var w in words)
        if (text.Contains(w, StringComparison.OrdinalIgnoreCase)) return true;
    return false;
}

// =====================================================================
// 1) 数值占用排序：实测优先用实测值，否则退回安装记录，未知排最后
//    实拍口径：registry 10GB 应该排在 measured 1GB 前面（按数字排，不按来源排）。
// =====================================================================
var measured1 = App("measured-1gb", @"C:\Apps\Measured", 0);
measured1.ActualSizeBytes = 1L << 30;
measured1.HasMeasuredSize = true;

var record10 = App("record-10gb", @"D:\Elsewhere", 10L << 30);

var measuredEmpty = App("measured-empty", @"C:\Apps\Empty", 0);
measuredEmpty.HasMeasuredSize = true;
measuredEmpty.ActualSizeBytes = 0;

var unknownApp = App("unknown-app", @"D:\Nowhere", 0);

CheckD("数值排序：10GB 安装记录排在 1GB 实测之前",
    record10.EffectiveFootprintBytes > measured1.EffectiveFootprintBytes,
    $"{record10.EffectiveFootprintBytes} vs {measured1.EffectiveFootprintBytes}");
Check("实测时排序键就是实测值", measured1.EffectiveFootprintBytes == 1L << 30);
Check("没有实测时排序键回退到安装记录", record10.EffectiveFootprintBytes == 10L << 30);
CheckD("两样都没有 = 未知哨兵，且标记为未知",
    unknownApp.EffectiveFootprintBytes == AppUninstallItem.UnknownFootprintSortValue
    && !unknownApp.HasKnownFootprint,
    unknownApp.EffectiveFootprintBytes.ToString());

var byFootprint = new[] { measured1, record10, measuredEmpty, unknownApp }
    .OrderByDescending(x => x.EffectiveFootprintBytes)
    .Select(x => x.Name)
    .ToArray();
CheckD("降序 = 10GB 记录 → 1GB 实测 → 0 实测 → 未知",
    byFootprint.SequenceEqual(new[] { "record-10gb", "measured-1gb", "measured-empty", "unknown-app" }),
    string.Join(",", byFootprint));

var both = App("both", @"C:\Apps\Both", 10L << 30);
both.ActualSizeBytes = 1L << 30;
both.HasMeasuredSize = true;
Check("同时有实测与安装记录时，排序键用实测", both.EffectiveFootprintBytes == 1L << 30);

var notifyApp = App("notify", @"C:\Apps\N", 0);
bool notified = false;
notifyApp.PropertyChanged += (_, e) =>
{
    if (e.PropertyName == nameof(AppUninstallItem.EffectiveFootprintBytes)) notified = true;
};
notifyApp.SizeBytes = 5L << 30;
Check("安装记录变化会通知数值排序键（列表实时重排）",
    notified && notifyApp.EffectiveFootprintBytes == 5L << 30);

// =====================================================================
// 2) 占用来源与保护安全仍按 2.10 保留
// =====================================================================
Check("来源仍如实区分：实测 / 安装记录 / 未知",
    measured1.FootprintSource == AppFootprintSource.Measured
    && record10.FootprintSource == AppFootprintSource.InstallRecord
    && unknownApp.FootprintSource == AppFootprintSource.Unknown);
Check("可信度分级保留：实测 < 记录 < 未知",
    measured1.SizeConfidenceRank < record10.SizeConfidenceRank
    && record10.SizeConfidenceRank < unknownApp.SizeConfidenceRank);
CheckD("安装记录来源的文案仍显式标注「安装记录」",
    record10.ActualSizeText == Loc.AppSizeFromRecord(AppUninstallItem.FormatFootprint(10L << 30)),
    record10.ActualSizeText);

var protectedApp = App("Protected App", @"D:\Vendor\Protected", 0);
protectedApp.IsProtected = true;
protectedApp.SystemComponent = true;
protectedApp.CanUninstall = false;
AppFactualInfoService.Apply(protectedApp);
Check("补事实信息不改变保护 / 系统组件 / 可卸载标记",
    protectedApp.IsProtected && protectedApp.SystemComponent && !protectedApp.CanUninstall);
CheckD("受保护条目的性质是 ProtectedSystemEntry",
    protectedApp.PurposeKind == AppSourceKind.ProtectedSystemEntry,
    protectedApp.PurposeKind.ToString());
Check("受保护描述里没有「建议保留」这类结论", !HasClaim(protectedApp.PurposeText));

// =====================================================================
// 3) 系统根 / 系统目录：误导性归属仍被拒绝（占用不整棵树算给单个软件）
// =====================================================================
var rootApp = App("root-app", @"D:\", 100);
AppRecommendationService.InvalidateUsageCache();
AppRecommendationService.ApplyUsage(AppRecommendationService.CalculateUsage(
    new[] { rootApp },
    new List<FileEntry> { new() { FullPath = @"D:\huge.bin", Size = 9_000_000_000L } }));
CheckD("盘根安装位置不把整盘算给单个软件（误导性归属被拒）",
    !rootApp.HasMeasuredSize && rootApp.ActualSizeBytes == rootApp.SizeBytes,
    rootApp.ActualSizeBytes.ToString());
CheckD("未归属时如实说明原因（通用根目录）",
    rootApp.FootprintNote == Loc.AppFootprintWhyGenericRoot, rootApp.FootprintNote);

var windowsDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
var systemTree = App("system-tree", Path.Combine(windowsDir, "System32"), 0);
AppRecommendationService.InvalidateUsageCache();
AppRecommendationService.ApplyUsage(AppRecommendationService.CalculateUsage(
    new[] { systemTree },
    new List<FileEntry>
    {
        new() { FullPath = Path.Combine(windowsDir, "System32", "kernel32.dll"), Size = 13_000_000_000L },
    }));
CheckD("Windows 系统目录的整棵树不归属给单个软件",
    !systemTree.HasMeasuredSize, systemTree.ActualSizeBytes.ToString());
CheckD("未归属时如实说明原因（系统目录）",
    systemTree.FootprintNote == Loc.AppFootprintWhySystemDir, systemTree.FootprintNote);
CheckD("系统目录条目在事实描述里是 Windows 自带组件",
    AppFactualInfoService.Describe(systemTree).Kind == AppSourceKind.WindowsInboxComponent,
    AppFactualInfoService.Describe(systemTree).Kind.ToString());

// =====================================================================
// 4) 未知就是未知：不猜标签、不编事实、不做卸载判断
// =====================================================================
var bare = App("Google Chrome", "", 0);
AppFactualInfoService.Apply(bare);
CheckD("只有名字 ⇒ 未知（不因为名字像 Chrome 就贴浏览器标签）",
    bare.PurposeKind == AppSourceKind.Unknown && bare.PurposeConfidence == AppInfoConfidence.Unknown,
    bare.PurposeKind + "/" + bare.PurposeConfidence);
CheckD("未知时如实写未知文案", bare.PurposeText == AppPurposeText.UnknownSummary, bare.PurposeText);
CheckD("未知描述不编发布者/版本",
    !bare.PurposeText.Contains("发布者") && !bare.PurposeText.Contains("版本"), bare.PurposeText);
Check("未知描述不含任何卸载判断词", !HasClaim(bare.PurposeText));
Check("未知悬停只有声明，没有结论", bare.PurposeHint.Contains(AppPurposeText.Disclaimer));

var bloatNamed = App("2345 安全卫士 优化大师", "", 0);
AppFactualInfoService.Apply(bloatNamed);
CheckD("名字像捆绑软件也不据此贴标签",
    bloatNamed.PurposeKind == AppSourceKind.Unknown
    && bloatNamed.PurposeText == AppPurposeText.UnknownSummary,
    bloatNamed.PurposeText);
Check("未知摘要里没有浏览器/捆绑等猜测词",
    !bloatNamed.PurposeText.Contains("浏览器") && !bloatNamed.PurposeText.Contains("捆绑")
    && !bloatNamed.PurposeText.Contains("browser") && !bloatNamed.PurposeText.Contains("bloat"));

// =====================================================================
// 5) 有本机记录时：事实齐全、简短、可核对，且不带判断
// =====================================================================
var known = App("Example App", @"C:\Program Files\Example", 2L << 30);
known.Publisher = "Example Corp";
known.Version = "3.1.4";
known.InstallDate = new DateTime(2024, 5, 6);
known.HasStartup = true;
AppFactualInfoService.Apply(known);
CheckD("事实摘要包含发布者与版本",
    known.PurposeText.Contains("Example Corp") && known.PurposeText.Contains("3.1.4"),
    known.PurposeText);
Check("事实摘要短且不含判断词",
    known.PurposeText.Length <= AppFactualInfoService.MaxSummaryLength && !HasClaim(known.PurposeText));
CheckD("有注册表记录 ⇒ 依据强度 RecordOnly",
    known.PurposeConfidence == AppInfoConfidence.RecordOnly,
    known.PurposeConfidence.ToString());
Check("悬停逐条列出可核对依据（位置/安装日期/启动项）",
    known.PurposeHint.Contains(@"C:\Program Files\Example")
    && known.PurposeHint.Contains("2024-05-06")
    && known.PurposeHint.Contains("启动项"));
Check("悬停带「不构成卸载建议」声明", known.PurposeHint.Contains(AppPurposeText.Disclaimer));

// =====================================================================
// 6) 系统自带组件 / Windows 功能 / Steam：结构化事实，不作保留建议
// =====================================================================
var mstsc = App("Microsoft® Windows® Operating System", Path.Combine(windowsDir, "System32"), 0);
mstsc.CanUninstall = false;
AppFactualInfoService.Apply(mstsc);
CheckD("Windows 目录内 ⇒ 自带组件，依据强度 = 本地结构化证据",
    mstsc.PurposeKind == AppSourceKind.WindowsInboxComponent
    && mstsc.PurposeConfidence == AppInfoConfidence.LocalEvidence,
    mstsc.PurposeKind + "/" + mstsc.PurposeConfidence);
CheckD("自带组件摘要如实写「Windows 自带组件」",
    mstsc.PurposeText.Contains("Windows 自带组件"), mstsc.PurposeText);
Check("自带组件描述里没有「建议保留」这类结论", !HasClaim(mstsc.PurposeText));
Check("自带组件的依据写清是结构判定（Windows 系统目录）",
    mstsc.PurposeHint.Contains("Windows 系统目录"));

var byCommand = App("Some OS Thing", "", 0);
byCommand.Entry = new ApplicationUninstallerEntry
{
    UninstallString = "\"" + Path.Combine(windowsDir, "System32", "mstsc.exe") + "\" /uninstall",
};
AppFactualInfoService.Apply(byCommand);
CheckD("卸载程序位于系统目录内也算自带组件（不靠名字）",
    byCommand.PurposeKind == AppSourceKind.WindowsInboxComponent,
    byCommand.PurposeKind.ToString());

var feature = App("Some Feature", "", 0);
feature.GroupKey = 2;
feature.CanUninstall = false;
AppFactualInfoService.Apply(feature);
CheckD("Windows 功能条目如实标功能",
    feature.PurposeKind == AppSourceKind.WindowsFeature
    && feature.PurposeText.Contains("Windows 可选功能"),
    feature.PurposeText);

var steam = App("Some Game", @"C:\Games\Steam\steamapps\common\SomeGame", 5L << 30);
steam.GroupKey = 1;
AppFactualInfoService.Apply(steam);
CheckD("Steam 条目按清单标明",
    steam.PurposeKind == AppSourceKind.SteamItem && steam.PurposeText.Contains("Steam"),
    steam.PurposeText);

// =====================================================================
// 7) Describe 是纯函数；事实补全全程不调模型 / 网络
// =====================================================================
int callsBefore = AiClient.CallCount;
AppFactualInfoService.Apply(new[] { known, mstsc, byCommand, feature, steam, bare, bloatNamed });
Check("事实描述不触发任何模型 / 网络调用", AiClient.CallCount == callsBefore);

var probe = App("probe", @"C:\Apps\Probe", 1234);
probe.Publisher = "Probe Inc";
long sizeBefore = probe.SizeBytes;
bool measuredBefore = probe.HasMeasuredSize;
bool selectedBefore = probe.Selected;
bool canBefore = probe.CanUninstall;
var described = AppFactualInfoService.Describe(probe);
Check("Describe 是纯函数：不改动条目任何字段",
    probe.SizeBytes == sizeBefore && probe.HasMeasuredSize == measuredBefore
    && probe.Selected == selectedBefore && probe.CanUninstall == canBefore
    && probe.PurposeText.Length == 0);
Check("Describe 给出摘要与依据",
    described.Summary.Length > 0 && described.Evidence.Count > 0);
Check("Describe 的摘要不带卸载判断", !HasClaim(described.Summary));

Console.WriteLine(failed == 0 ? "All checks passed" : $"{failed} checks failed");
return failed == 0 ? 0 : 1;
