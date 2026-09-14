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
CheckD("安装记录占用格只写数字，来源进悬停",
    record10.ActualSizeText == AppUninstallItem.FormatFootprint(10L << 30)
    && record10.FootprintHint.Contains(Loc.AppSizeSourceRecord),
    record10.ActualSizeText + " | " + record10.FootprintHint);

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
CheckD("事实摘要是类型 + 发布者，不含版本",
    known.PurposeText.Contains("Example Corp")
    && !known.PurposeText.Contains("3.1.4")
    && !known.PurposeText.Contains("版本"),
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
CheckD("自带组件摘要如实写「Windows 组件」",
    mstsc.PurposeText.Contains("Windows 组件")
    && !mstsc.PurposeText.Contains("Windows 自带组件"), mstsc.PurposeText);
Check("自带组件描述里没有「建议保留」这类结论", !HasClaim(mstsc.PurposeText));
Check("自带组件的依据写清是结构判定（Windows 系统目录）",
    mstsc.PurposeHint.Contains("Windows 系统目录"));

// ---- 卸载宿主不等于系统自带（真实误识别修正）----
// 第三方 MSI 的卸载命令是 C:\Windows\System32\msiexec.exe /x {guid}，rundll32 同样是宿主工具；
// 它们只说明「怎么卸」，不能证明产品属于 Windows。
// 只有**实际安装位置**在 Windows 内才是结构化证据；早期的 InboxComponent 标记也不能盲信。

// (a) 普通第三方 MSI：装在 Program Files，卸载走 msiexec 全路径
var thirdPartyMsi = App("Third Party MSI", @"C:\Program Files\Vendor\App", 3L << 30);
thirdPartyMsi.Publisher = "Vendor Inc";
thirdPartyMsi.Version = "2.0";
thirdPartyMsi.Entry = new ApplicationUninstallerEntry
{
    UninstallString = "\"C:\\Windows\\System32\\msiexec.exe\" /x {11111111-2222-3333-4444-555555555555}",
};
AppFactualInfoService.Apply(thirdPartyMsi);
CheckD("msiexec 卸载的第三方软件不得断言系统自带",
    thirdPartyMsi.PurposeKind == AppSourceKind.InstalledApplication,
    thirdPartyMsi.PurposeKind.ToString());
CheckD("msiexec 只保守表述「通过系统工具卸载」",
    thirdPartyMsi.PurposeHint.Contains("通过系统工具卸载")
    && thirdPartyMsi.PurposeHint.Contains("msiexec.exe"),
    thirdPartyMsi.PurposeHint);
Check("msiexec 第三方软件描述不含「Windows 组件」",
    !thirdPartyMsi.PurposeText.Contains("Windows 组件")
    && !thirdPartyMsi.PurposeText.Contains("Windows 自带组件"));

// (b) 大小写 / 正反斜杠变体同样识别为系统工具（仍不当自带）
var msiVariant = App("MSI Variant", @"D:\Apps\Variant", 1L << 30);
msiVariant.Entry = new ApplicationUninstallerEntry
{
    UninstallString = "C:/WINDOWS/SYSTEM32/MSIEXEC.EXE /x {ABCDEFAB-0000-0000-0000-000000000000}",
};
AppFactualInfoService.Apply(msiVariant);
CheckD("大小写/斜杠变体的 msiexec 也不当自带，且仍写系统工具",
    msiVariant.PurposeKind != AppSourceKind.WindowsInboxComponent
    && msiVariant.PurposeHint.Contains("通过系统工具卸载")
    && msiVariant.PurposeHint.Contains("msiexec.exe", StringComparison.OrdinalIgnoreCase),
    msiVariant.PurposeText + " | " + msiVariant.PurposeHint);

// (c) rundll32 宿主
var rundll = App("Rundll Hosted", @"C:\Program Files\Vendor\Rundll", 2L << 30);
rundll.Entry = new ApplicationUninstallerEntry
{
    UninstallString = "\"C:\\Windows\\System32\\rundll32.exe\" "
        + "\"C:\\Program Files\\Vendor\\Rundll\\setup.dll\",Uninstall",
};
AppFactualInfoService.Apply(rundll);
CheckD("rundll32 宿主也只算系统工具卸载",
    rundll.PurposeKind != AppSourceKind.WindowsInboxComponent
    && rundll.PurposeHint.Contains("通过系统工具卸载")
    && rundll.PurposeHint.Contains("rundll32.exe"),
    rundll.PurposeKind.ToString());

// (d) 普通第三方安装位置 vs 早期 InboxComponent 标记冲突：不盲信标记
var wronglyFlagged = App("Wrongly Flagged", @"C:\Program Files\Vendor\Flagged", 1L << 30);
wronglyFlagged.InboxComponent = true;
wronglyFlagged.Entry = new ApplicationUninstallerEntry
{
    UninstallString = "\"C:\\Windows\\System32\\msiexec.exe\" /x {99999999-0000-0000-0000-000000000000}",
};
AppFactualInfoService.Apply(wronglyFlagged);
CheckD("早期 InboxComponent=true 不被盲信（第三方安装位置冲突）",
    wronglyFlagged.PurposeKind != AppSourceKind.WindowsInboxComponent,
    wronglyFlagged.PurposeKind.ToString());

// (e) 只有卸载程序在 Windows 内、没有 Windows 安装位置：保守，不断言自带
var windowsHostedOnly = App("Windows Hosted Only", "", 0);
windowsHostedOnly.Entry = new ApplicationUninstallerEntry
{
    UninstallString = "\"" + Path.Combine(windowsDir, "System32", "mstsc.exe") + "\" /uninstall",
};
AppFactualInfoService.Apply(windowsHostedOnly);
CheckD("只有卸载程序在系统目录内 ⇒ 不断言系统自带",
    windowsHostedOnly.PurposeKind != AppSourceKind.WindowsInboxComponent,
    windowsHostedOnly.PurposeKind.ToString());
CheckD("只有卸载程序在系统目录内 ⇒ 保守表述卸载方式",
    windowsHostedOnly.PurposeHint.Contains("通过系统目录内的卸载程序卸载")
    && windowsHostedOnly.PurposeHint.Contains("mstsc.exe"),
    windowsHostedOnly.PurposeHint);

// (f) 真正 Windows 安装位置仍然是自带组件（原有正确判定保留）
var realInbox = App("Windows Real", Path.Combine(windowsDir, "System32"), 0);
realInbox.Entry = new ApplicationUninstallerEntry
{
    UninstallString = "\"" + Path.Combine(windowsDir, "System32", "mstsc.exe") + "\" /uninstall",
};
AppFactualInfoService.Apply(realInbox);
CheckD("真正 Windows 安装位置 ⇒ 自带组件（结构化证据）",
    realInbox.PurposeKind == AppSourceKind.WindowsInboxComponent
    && realInbox.PurposeConfidence == AppInfoConfidence.LocalEvidence,
    realInbox.PurposeKind.ToString());

// (g) 清单真实系统来源标志（SystemComponent）仍作为系统证据，与卸载宿主无关
var sysFlag = App("Flagged System", @"D:\Vendor\SysFlag", 0);
sysFlag.SystemComponent = true;
AppFactualInfoService.Apply(sysFlag);
CheckD("清单 SystemComponent 标志仍作为系统来源证据",
    sysFlag.PurposeKind == AppSourceKind.ProtectedSystemEntry
    && sysFlag.PurposeText.Contains("系统组件"),
    sysFlag.PurposeText);

var feature = App("Some Feature", "", 0);
feature.GroupKey = 2;
feature.CanUninstall = false;
AppFactualInfoService.Apply(feature);
CheckD("Windows 功能条目如实标功能",
    feature.PurposeKind == AppSourceKind.WindowsFeature
    && feature.PurposeText.Contains("Windows 功能"),
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
AppFactualInfoService.Apply(new[]
{
    known, mstsc, windowsHostedOnly, thirdPartyMsi, rundll, realInbox, feature, steam, bare, bloatNamed,
});
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
