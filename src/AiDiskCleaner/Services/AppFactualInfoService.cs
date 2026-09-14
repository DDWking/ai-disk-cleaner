using AiDiskCleaner.Models;

namespace AiDiskCleaner.Services;

/// <summary>
/// 一次「这个条目按本机记录/结构是什么」的事实描述结果。
/// <para>
/// 刻意**不含**任何「该不该卸载 / 删了会怎样 / 建不建议保留」的字段 ——
/// 那些不属于事实，也不该由本地元数据推断。
/// </para>
/// </summary>
/// <param name="Kind">按结构化证据确认的条目性质（不做任何名字猜测）。</param>
/// <param name="Confidence">本地依据强度：结构化证据 / 只有注册表记录 / 未知。</param>
/// <param name="Summary">一句话事实摘要；没有任何可靠依据时为空。</param>
/// <param name="Evidence">逐条可核对的事实依据（不含免责声明）。</param>
public sealed record AppFactualInfo(
    AppSourceKind Kind,
    AppInfoConfidence Confidence,
    string Summary,
    IReadOnlyList<string> Evidence);

/// <summary>
/// 事实用途服务：只用**本机可核对的元数据与结构证据**说清「这是什么」。
/// <list type="bullet">
/// <item>**不联网、不调模型** —— 客户端信息不出去，描述也不需要生成式猜测；</item>
/// <item>**不碰磁盘** —— 不 Directory.Exists、不遍历目录，只看清单里已有的字段；</item>
/// <item>**不做名字子串判断** —— 名字里出现「浏览器」不会让它变成浏览器，
/// 出现「2345」也不会被贴捆绑标签；性质只看结构化字段（安装位置/清单标记）；</item>
/// <item>**卸载宿主不等于产品归属** —— 第三方 MSI 也走
/// <c>C:\Windows\System32\msiexec.exe /x {guid}</c>，rundll32 同样是宿主工具；
/// 卸载程序位于 Windows 目录内只说明「通过系统工具卸载」，**不能据此断言系统自带**；</item>
/// <item>**不盲信早期标记** —— 不看 <see cref="AppUninstallItem.InboxComponent"/>（它可能正是
/// 由卸载宿主路径算出来的），只认卸载清单的真实系统来源标志与实际安装位置；</item>
/// <item>**没有依据就如实未知** —— 只有名字的条目不会被编出一段用途；</item>
/// <item>**不评价该不该卸载** —— 描述里不出现「建议卸载/可以删/建议保留」这类结论。</item>
/// </list>
/// </summary>
public static class AppFactualInfoService
{
    /// <summary>列表一列的摘要上限：事实要短，悬停里再展开依据。</summary>
    public const int MaxSummaryLength = 160;

    /// <summary>批量补事实信息（纯本地，逐条 O(1)）。</summary>
    public static void Apply(IEnumerable<AppUninstallItem> apps)
    {
        if (apps == null) return;
        foreach (var app in apps)
            if (app != null) Apply(app);
    }

    /// <summary>把事实信息写到条目上。不改变占用、保护标记、勾选或任何执行安全字段。</summary>
    public static void Apply(AppUninstallItem app)
    {
        if (app == null) return;
        var info = Describe(app);
        app.PurposeKind = info.Kind;
        app.PurposeConfidence = info.Confidence;
        app.PurposeSummary = info.Summary;
        app.PurposeDetail = FormatDetail(info.Evidence);
    }

    static string FormatDetail(IReadOnlyList<string> evidence)
    {
        if (evidence.Count == 0) return AppPurposeText.Disclaimer;
        return string.Join(Environment.NewLine, evidence)
            + Environment.NewLine + AppPurposeText.Disclaimer;
    }

    /// <summary>纯函数：算出一个条目的事实描述，不改动条目。</summary>
    public static AppFactualInfo Describe(AppUninstallItem app)
    {
        if (app == null)
            return new(AppSourceKind.Unknown, AppInfoConfidence.Unknown, "", Array.Empty<string>());

        string publisher = Clean(app.Publisher);
        string version = Clean(app.Version);
        string location = Clean(app.InstallLocation);
        bool hasInstallDate = app.InstallDate != DateTime.MinValue;
        bool hasSizeRecord = app.SizeBytes > 0 || app.HasMeasuredSize;

        // 卸载命令行只当「怎么卸」的事实，**不当产品归属证据**：msiexec / rundll32 是系统宿主。
        var uninstall = InspectUninstall(app);
        bool hasUninstall = uninstall.Exe.Length > 0;

        var kind = DetectKind(app, hasUninstall);
        bool structural = kind is AppSourceKind.WindowsInboxComponent
            or AppSourceKind.WindowsFeature
            or AppSourceKind.ProtectedSystemEntry
            or AppSourceKind.SteamItem;
        bool hasRecords = publisher.Length > 0 || version.Length > 0 || location.Length > 0
            || hasInstallDate || hasSizeRecord || hasUninstall;

        // 只有名字：没有任何可核对的东西。如实未知，不编用途、不贴标签。
        if (!structural && !hasRecords)
            return new(AppSourceKind.Unknown, AppInfoConfidence.Unknown,
                AppPurposeText.UnknownSummary, Array.Empty<string>());

        var evidence = new List<string>();
        string basis = Basis(kind);
        if (basis.Length > 0) evidence.Add(basis);
        if (publisher.Length > 0) evidence.Add(AppPurposeText.PublisherRecord(publisher));
        if (version.Length > 0) evidence.Add(AppPurposeText.VersionRecord(version));
        if (location.Length > 0) evidence.Add(AppPurposeText.LocationRecord(location));
        if (hasInstallDate) evidence.Add(AppPurposeText.InstallDateRecord(app.InstallDateText));
        AddUninstallEvidence(uninstall, evidence);
        AddFootprint(app, evidence);
        if (app.IsProtected) evidence.Add(AppPurposeText.MarkProtected);
        if (app.SystemComponent) evidence.Add(AppPurposeText.MarkSystemComponent);
        if (!app.CanUninstall && kind != AppSourceKind.NoUninstaller)
            evidence.Add(AppPurposeText.RecordNoUninstaller);
        if (app.HasStartup) evidence.Add(AppPurposeText.StartupRecord);
        // 只写「检测到在运行」这一件确认过的事；未知不写成「没在运行」。
        if (app.RunningState == AppRunningState.Running) evidence.Add(AppPurposeText.RunningNow);

        string summary = BuildSummary(app, kind, publisher, version, location);
        var confidence = structural ? AppInfoConfidence.LocalEvidence : AppInfoConfidence.RecordOnly;
        return new(kind, confidence, summary, evidence);
    }

    /// <summary>
    /// 性质判定：只看**卸载清单的真实系统来源标志与实际安装位置**，绝不看名字，
    /// 也不看卸载宿主程序在不在 Windows 目录里。
    ///
    /// 顺序 = 证据强度：实际安装位置在系统目录 → Windows 功能清单 → 系统/受保护标志
    /// → Steam 清单 → 普通清单。
    /// </summary>
    static AppSourceKind DetectKind(AppUninstallItem app, bool hasUninstall)
    {
        if (IsWindowsInstallLocation(app)) return AppSourceKind.WindowsInboxComponent;
        if (app.GroupKey == 2) return AppSourceKind.WindowsFeature;
        if (app.IsProtected || app.SystemComponent || app.GroupKey == 3)
            return AppSourceKind.ProtectedSystemEntry;
        if (app.GroupKey == 1) return AppSourceKind.SteamItem;
        return app.CanUninstall || hasUninstall
            ? AppSourceKind.InstalledApplication
            : AppSourceKind.NoUninstaller;
    }

    static void AddUninstallEvidence(UninstallHost host, List<string> evidence)
    {
        if (!host.InWindows) return;
        // 系统宿主工具（msiexec / rundll32）只说明卸载方式，不说明产品归属。
        evidence.Add(host.IsSystemTool
            ? AppPurposeText.ViaSystemTool(host.Name)
            : AppPurposeText.ViaWindowsUninstaller(host.Name));
    }

    static void AddFootprint(AppUninstallItem app, List<string> evidence)
    {
        switch (app.FootprintSource)
        {
            case AppFootprintSource.Measured:
                evidence.Add(AppPurposeText.FootprintMeasured(app.ActualSizeText));
                break;
            case AppFootprintSource.InstallRecord:
                evidence.Add(AppPurposeText.FootprintRecordOnly(app.ActualSizeText));
                break;
            default:
                evidence.Add(AppPurposeText.FootprintUnknown);
                break;
        }
        // 没实测时把「为什么没测到」也如实写出来（共享目录 / 系统目录 / 不在扫描范围…）。
        if (!app.HasMeasuredSize && !string.IsNullOrWhiteSpace(app.FootprintNote))
            evidence.Add(AppPurposeText.FootprintWhy(app.FootprintNote));
    }

    static string BuildSummary(AppUninstallItem app, AppSourceKind kind,
        string publisher, string version, string location)
    {
        var parts = new List<string>(2);
        string label = KindLabel(app, kind);
        if (label.Length > 0) parts.Add(label);
        // 列表只留「类型 + 发布者」。版本有自己的列（已隐藏），进悬停，不占用途格。
        if (publisher.Length > 0 && !DuplicatesLabel(label, publisher))
            parts.Add(publisher);
        if (parts.Count == 1 && location.Length > 0)
            parts.Add(AppPurposeText.SummaryLocation(location));

        string text = string.Join(" · ", parts);
        if (text.Length > MaxSummaryLength) text = text[..(MaxSummaryLength - 1)] + "…";
        return text;
    }

    /// <summary>发布者已经写在类型里时不再重复（「Steam · Steam」）。</summary>
    static bool DuplicatesLabel(string label, string publisher)
        => label.Length > 0 && publisher.Length > 0
           && (label.Equals(publisher, StringComparison.OrdinalIgnoreCase)
               || label.Contains(publisher, StringComparison.OrdinalIgnoreCase)
               || publisher.Contains(label, StringComparison.OrdinalIgnoreCase));

    static string KindLabel(AppUninstallItem app, AppSourceKind kind) => kind switch
    {
        AppSourceKind.InstalledApplication => AppPurposeText.KindInstalled,
        AppSourceKind.NoUninstaller => AppPurposeText.KindNoUninstaller,
        AppSourceKind.SteamItem => AppPurposeText.KindSteam,
        AppSourceKind.WindowsFeature => AppPurposeText.KindWindowsFeature,
        AppSourceKind.WindowsInboxComponent => AppPurposeText.KindInbox,
        // 「受保护」和「系统组件」是两件不同的事实，分别如实写，不合并成一句笼统的话。
        AppSourceKind.ProtectedSystemEntry => (app.IsProtected, app.SystemComponent) switch
        {
            (true, true) => AppPurposeText.KindProtectedSystem,
            (false, true) => AppPurposeText.KindSystemComponent,
            _ => AppPurposeText.KindProtected,
        },
        _ => "",
    };

    static string Basis(AppSourceKind kind) => kind switch
    {
        AppSourceKind.WindowsInboxComponent => AppPurposeText.BasisInbox,
        AppSourceKind.WindowsFeature => AppPurposeText.BasisWindowsFeature,
        AppSourceKind.ProtectedSystemEntry => AppPurposeText.BasisProtected,
        AppSourceKind.SteamItem => AppPurposeText.BasisSteam,
        AppSourceKind.NoUninstaller => AppPurposeText.BasisNoUninstaller,
        AppSourceKind.InstalledApplication => AppPurposeText.BasisInstalled,
        _ => "",
    };

    // ---- 结构化判定（与「建议」无关）----

    /// <summary>
    /// 只有**实际安装位置**落在 Windows 目录内才算系统自带组件 / 设备软件。
    ///
    /// 刻意**不看卸载程序路径**，也**不看清单里早期算好的 <see cref="AppUninstallItem.InboxComponent"/>**：
    /// 第三方 MSI 的卸载命令同样位于 <c>C:\Windows\System32\msiexec.exe</c>，
    /// 只看卸载路径会把普通软件误判成系统自带（该标记本身可能正是这样生成的）。
    /// </summary>
    static bool IsWindowsInstallLocation(AppUninstallItem app)
    {
        string win = WindowsDirectory;
        if (win.Length == 0) return false;
        string location = NormalizePath(app.InstallLocation);
        return location.Length > 0 && IsPathWithin(location, win);
    }

    /// <summary>
    /// 卸载命令行里的事实：可执行文件是谁、在不在 Windows 目录内、是不是系统宿主工具。
    /// 这些只说明「怎么卸」，**不说明产品属于谁**。
    /// </summary>
    readonly record struct UninstallHost(string Exe, string Name, bool InWindows, bool IsSystemTool);

    static UninstallHost InspectUninstall(AppUninstallItem app)
    {
        string exe = NormalizePath(UninstallExecutable(app.Entry?.UninstallString));
        if (exe.Length == 0) return new("", "", false, false);
        string win = WindowsDirectory;
        bool inWindows = win.Length > 0 && IsPathWithin(exe, win);
        string name = FileName(exe);
        return new(exe, name, inWindows, inWindows && IsSystemUninstallHost(name));
    }

    /// <summary>
    /// Windows 自带的卸载宿主工具：普通第三方软件的卸载也会调用它们，
    /// 所以「通过 msiexec 卸载」不能证明软件是系统自带。
    /// </summary>
    static bool IsSystemUninstallHost(string name)
        => name.Equals("msiexec.exe", StringComparison.OrdinalIgnoreCase)
            || name.Equals("rundll32.exe", StringComparison.OrdinalIgnoreCase);

    /// <summary>取路径最后一段（纯字符串，不碰磁盘）。</summary>
    static string FileName(string path)
    {
        int i = path.LastIndexOf('\\');
        return i >= 0 ? path[(i + 1)..] : path;
    }

    /// <summary>Windows 目录由系统 API 解析，不硬编码盘符。</summary>
    static string WindowsDirectory
        => Environment.GetFolderPath(Environment.SpecialFolder.Windows).TrimEnd('\\');

    /// <summary>从卸载命令行里取出可执行文件路径（带引号或不带引号都认）。</summary>
    static string UninstallExecutable(string? command)
    {
        string s = (command ?? "").Trim();
        if (s.Length == 0) return "";
        if (s[0] == '"')
        {
            int end = s.IndexOf('"', 1);
            return end > 1 ? s[1..end].Trim() : "";
        }
        int sp = s.IndexOf(' ');
        return (sp > 0 ? s[..sp] : s).Trim();
    }

    static string NormalizePath(string? path)
        => (path ?? "").Trim().Trim('"').Replace('/', '\\').TrimEnd('\\');

    /// <summary>整段边界比较：`C:\Windows` 不会命中 `C:\WindowsApps`。</summary>
    static bool IsPathWithin(string path, string location)
        => path.Equals(location, StringComparison.OrdinalIgnoreCase)
            || (path.Length > location.Length
                && path.StartsWith(location, StringComparison.OrdinalIgnoreCase)
                && path[location.Length] == '\\');

    static string Clean(string? value) => (value ?? "").Trim();
}

/// <summary>
/// 事实用途的新文案（中/英）。集中放在这里，避免在共享的 <c>Loc</c> 里和别的改动打架；
/// 集成稳定后再并进 <c>Loc</c>。措辞只陈述事实，不出现卸载建议。
/// </summary>
internal static class AppPurposeText
{
    static bool En => Loc.IsEn;

    public static string UnknownSummary => En
        ? "No local record describes this app"
        : "本机记录不足以说明这是什么";

    public static string Disclaimer => En
        ? "Facts from local records and structure only — not a recommendation to uninstall."
        : "以上仅为本机记录与结构事实，不构成是否卸载的建议。";

    // ---- 性质标签（列表摘要用）----
    public static string KindInstalled => En ? "Installed application" : "已安装软件";
    public static string KindNoUninstaller => En ? "No usable uninstaller" : "未找到可用的卸载程序";
    public static string KindSteam => En ? "Steam" : "Steam";
    public static string KindWindowsFeature => En ? "Windows feature" : "Windows 功能";
    public static string KindInbox => En ? "Windows component" : "Windows 组件";
    public static string KindProtectedSystem => En ? "Protected system component" : "受保护的系统组件";
    public static string KindSystemComponent => En ? "System component" : "系统组件";
    public static string KindProtected => En ? "Protected entry" : "受保护的条目";

    // ---- 依据（悬停用，说明这个结论凭什么）----
    public static string BasisInbox => En
        ? "Basis: the actual install location is inside the Windows directory (structural check)"
        : "依据：实际安装位置位于 Windows 系统目录内（结构化判定）";
    public static string BasisWindowsFeature => En
        ? "Source: Windows feature list"
        : "来源：Windows 功能清单";
    public static string BasisProtected => En
        ? "Basis: the installed list marks this protected or a system component"
        : "依据：已安装清单标记为受保护或系统组件";
    public static string BasisSteam => En
        ? "Source: Steam library list"
        : "来源：Steam 清单";
    public static string BasisNoUninstaller => En
        ? "Record: the list has no usable uninstaller for this entry"
        : "记录：清单里没有可用的卸载程序";
    public static string BasisInstalled => En
        ? "Source: local installed-software list"
        : "来源：本机已安装软件清单";

    // ---- 单条事实 ----
    public static string PublisherRecord(string value) => En
        ? "Publisher (registry record): " + value
        : "发布者（注册表记录）：" + value;
    public static string VersionRecord(string value) => En
        ? "Version (registry record): " + value
        : "版本（注册表记录）：" + value;
    public static string LocationRecord(string value) => En
        ? "Install location (registry record): " + value
        : "安装位置（注册表记录）：" + value;
    public static string InstallDateRecord(string value) => En
        ? "Install date (registry record): " + value
        : "安装日期（注册表记录）：" + value;

    // 卸载方式：宿主工具只说明怎么卸，不说明产品归属。
    public static string ViaSystemTool(string name) => En
        ? "Uninstalled via a Windows system tool: " + name
        : "通过系统工具卸载：" + name;
    public static string ViaWindowsUninstaller(string name) => En
        ? "Uninstalled via a program inside the Windows directory: " + name
        : "通过系统目录内的卸载程序卸载：" + name;

    public static string FootprintMeasured(string size) => En
        ? "Footprint: " + size + " (measured by scan)"
        : "占用：" + size + "（扫描实测）";
    public static string FootprintRecordOnly(string size) => En
        ? "Footprint: " + size + " (installer estimate, not measured)"
        : "占用：" + size + "（安装记录估计，非实测）";
    public static string FootprintUnknown => En
        ? "Footprint: no usable number on this machine"
        : "占用：本机没有可用数字";
    public static string FootprintWhy(string why) => En
        ? "Not measured because: " + why
        : "未实测原因：" + why;

    public static string MarkProtected => En
        ? "Flag: marked protected by the installed list"
        : "标记：已安装清单标记为受保护";
    public static string MarkSystemComponent => En
        ? "Flag: marked as a system component by the installed list"
        : "标记：已安装清单标记为系统组件";
    public static string RecordNoUninstaller => En
        ? "Record: no usable uninstaller found"
        : "记录：没有找到可用的卸载程序";
    public static string StartupRecord => En
        ? "Startup: listed in the startup entries"
        : "启动项：清单里有开机自启记录";
    public static string RunningNow => En
        ? "Running: detected as currently running"
        : "运行状态：检测到正在运行";

    // ---- 摘要片段 ----
    public static string SummaryPublisher(string value) => En ? "by " + value : "发布者 " + value;
    public static string SummaryVersion(string value) => En ? "version " + value : "版本 " + value;
    public static string SummaryLocation(string value) => En ? "at " + value : "安装位置 " + value;
}
