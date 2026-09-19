using AiDiskCleaner.Models;
using UninstallTools.Junk.Confidence;
// 别名是必须的：Loc 自己有一个叫 CleanRules 的**字符串属性**（分析步骤文案），
// 在类内部写 CleanRules.AiPurposeKind 会解析到那个属性上。
using AiPurposeKind = AiDiskCleaner.Services.CleanRules.AiPurposeKind;

namespace AiDiskCleaner.Services;

/// <summary>界面文案。Zh / En 两套，设置里切换。</summary>
public static class Loc
{
    public static AppLang Lang { get; set; } = AppLang.Zh;
    public static bool IsEn => Lang == AppLang.En;

    public static string AppName => IsEn ? "Dashao Huo" : "大扫货";
    public static string NavBrowse => IsEn ? "Browse" : "浏览";
    public static string NavAi => IsEn ? "AI analysis" : "AI 分析";
    public static string Scan => IsEn ? "Scan" : "扫描";
    public static string Stop => IsEn ? "Stop" : "停止";
    public static string Settings => IsEn ? "Settings" : "设置";
    public static string About => IsEn ? "About" : "关于";
    public static string AboutDashaoHuo => IsEn ? "About Dashao Huo" : "关于大扫货";
    public static string Ready => IsEn ? "Ready" : "就绪";
    public static string Scanning => IsEn ? "Scanning" : "扫描中";
    public static string Preparing => IsEn ? "Preparing…" : "准备中…";
    public static string ScanningEllipsis => IsEn ? "Scanning…" : "正在扫描…";
    public static string SearchHint => IsEn ? "Search path / name" : "搜索路径 / 文件名";
    public static string Path => IsEn ? "Path" : "路径";
    public static string Pct => IsEn ? "Share" : "占比";
    public static string Size => IsEn ? "Size" : "大小";
    public static string Ext => IsEn ? "Ext" : "扩展名";
    public static string Type => IsEn ? "Type" : "类型";
    public static string Files(int n) => IsEn ? $"{n:N0} files" : $"{n:N0} 个文件";
    public static string FilesWord => IsEn ? "files" : "个文件";
    public static string DirsWord => IsEn ? "folders" : "个文件夹";
    public static string FileDirCount(int files, int dirs) =>
        IsEn ? $"{files:N0} files  {dirs:N0} folders" : $"{files:N0} 个文件  {dirs:N0} 个文件夹";
    public static string Elapsed(double seconds) =>
        IsEn ? $"elapsed {seconds:0.00}s" : $"耗时 {seconds:0.00} 秒";
    public static string Volume(string total, string used, double pct, string free) =>
        IsEn ? $"total {total}  used {used} ({pct:0.0}%)  free {free}"
             : $"总共 {total}  已用 {used} ({pct:0.0}%)  可用 {free}";
    public static string ScanPct(int pct) => IsEn ? $"Scan {pct}%" : $"扫描 {pct}%";
    public static string ScanCount(int n) => IsEn ? $"Scan {n:N0}" : $"扫描 {n:N0}";
    public static string ProgressLine(int pct, string stage, int files) =>
        IsEn ? $"{pct}%  {stage}  {files:N0} files" : $"{pct}%  {stage}  {files:N0} 个文件";
    public static string ProgressIndeterminate(string stage, int files) =>
        IsEn ? $"{stage}  {files:N0} files" : $"{stage}  {files:N0} 个文件";
    /// <summary>分析阶段：第几步 / 共几步 + 当前动作。</summary>
    public static string AnalyzeStep(int step, int total, string action) =>
        IsEn ? $"Analyze {step}/{total}  {action}" : $"分析 {step}/{total}  {action}";
    public static string FilterAll => IsEn ? "whole disk" : "全盘";
    public static string MftFail => IsEn ? "MFT failed, falling back" : "MFT 失败，改用递归扫描";
    public static string MftFallbackStarting(string reason) => IsEn
        ? $"MFT unavailable ({reason}); using compatibility scan…"
        : $"高速 MFT 扫描不可用（{reason}），正在使用兼容递归扫描…";
    public static string ScanFallbackProgress(int pct, string stage, int files, string reason) => IsEn
        ? $"Compatibility scan · {stage} · {files:N0} files · MFT: {reason}"
        : $"兼容递归扫描 · {stage} · {files:N0} 个文件 · MFT 原因：{reason}";
    public static string ScanMftFinished(int files) => IsEn
        ? $"MFT scan complete · {files:N0} files"
        : $"高速 MFT 扫描完成 · {files:N0} 个文件";
    public static string ScanFallbackFinished(int files, string reason) => IsEn
        ? $"Compatibility scan complete · {files:N0} files · MFT: {reason}"
        : $"兼容递归扫描完成 · {files:N0} 个文件 · MFT 原因：{reason}";
    public static string Aborted => IsEn ? "Stopped" : "已停止";

    // ---- 扫描质量报告：不让用户把「跳了一堆目录」当成完整扫描 ----
    public static string ScanSourceMft => IsEn ? "fast MFT scan" : "高速 MFT 扫描";
    public static string ScanSourceRecursive => IsEn ? "compatibility scan" : "兼容递归扫描";
    public static string ScanSourceUnknown => IsEn ? "unknown" : "未知";
    public static string ScanQualityComplete => IsEn ? "complete" : "完整";
    public static string ScanQualityPartial => IsEn ? "INCOMPLETE" : "不完整";
    public static string ScanQualityCanceled => IsEn ? "stopped early" : "中途停止";

    /// <summary>一行摘要：来源 · 完整度 · 数量 · 耗时。</summary>
    public static string ScanQualitySummary(string source, string completeness, int files, int dirs, double seconds)
        => IsEn
            ? $"Scan source: {source} · {completeness} · {files:N0} files / {dirs:N0} folders · {seconds:0.0}s"
            : $"扫描来源：{source} · {completeness} · {files:N0} 个文件 / {dirs:N0} 个文件夹 · 耗时 {seconds:0.0} 秒";

    /// <summary>不完整时补一行明细，说清「哪里没扫到」。</summary>
    public static string ScanQualityProblems(int skippedDirs, int perm, int pathErrors, int read, int reparse)
    {
        var bits = new List<string>();
        if (skippedDirs > 0) bits.Add(IsEn ? $"{skippedDirs:N0} folders skipped" : $"跳过 {skippedDirs:N0} 个目录");
        if (perm > 0) bits.Add(IsEn ? $"{perm:N0} permission errors" : $"{perm:N0} 个权限不足");
        if (pathErrors > 0) bits.Add(IsEn ? $"{pathErrors:N0} path errors" : $"{pathErrors:N0} 个路径问题");
        if (read > 0) bits.Add(IsEn ? $"{read:N0} read failures" : $"{read:N0} 个读取失败");
        if (reparse > 0) bits.Add(IsEn ? $"{reparse:N0} links skipped" : $"{reparse:N0} 个链接未跟随");
        if (bits.Count == 0) return "";
        string head = IsEn ? "Not everything was scanned: " : "有内容没扫到：";
        return head + string.Join(IsEn ? ", " : "、", bits);
    }

    /// <summary>MFT 记录级的问题（解析失败 / 孤儿记录 / 硬链接）。</summary>
    public static string ScanQualityRecords(int unparsed, int orphan, int hardLinks)
    {
        var bits = new List<string>();
        if (unparsed > 0) bits.Add(IsEn ? $"{unparsed:N0} unparsable records" : $"{unparsed:N0} 条记录读不出来");
        if (orphan > 0) bits.Add(IsEn ? $"{orphan:N0} orphan records" : $"{orphan:N0} 条找不到父目录");
        if (hardLinks > 0) bits.Add(IsEn ? $"{hardLinks:N0} hard links" : $"{hardLinks:N0} 个硬链接");
        return bits.Count == 0 ? "" : string.Join(IsEn ? ", " : "、", bits);
    }

    public static string ScanFailed => IsEn ? "Scan failed" : "扫描失败";
    public static string ScanFailedMsg(string msg) => IsEn ? "Scan failed: " + msg : "扫描失败：" + msg;
    public static string AnalyzeAfterScan => IsEn ? "Scan to analyze" : "扫描后分析";
    public static string HintClean => IsEn ? "Hint: disk looks clean" : "建议：磁盘较干净";
    public static string HintTemp(int count, string size) =>
        IsEn ? $"Hint: {count} temp/log files, about {size}"
             : $"建议：{count} 个临时/日志文件，约 {size}";
    public static string Language => IsEn ? "Language" : "语言";
    // ---- 诊断导出 ----
    public static string DiagExport => IsEn ? "Export diagnostics" : "导出诊断信息";
    public static string DiagHint => IsEn
        ? "Writes a redacted log bundle. No API key, no unredacted paths."
        : "导出一份脱敏后的日志包，不含 API 密钥，也不含未脱敏的路径。";
    public static string DiagExportTitle => IsEn ? "Diagnostics" : "诊断信息";
    public static string DiagExportOk(string path) => IsEn
        ? "Saved to:\n" + path
        : "已保存到：\n" + path;
    public static string DiagExportFail => IsEn
        ? "Could not write the diagnostics file."
        : "诊断文件写不出来。";
    public static string AiSection => IsEn ? "Providers" : "提供方";
    public static string AiSectionHint => IsEn
        ? "Add providers, then pick a model in the chat pane."
        : "添加提供方后，在分析栏里选模型。";
    public static string AiEdit => IsEn ? "Edit" : "编辑";
    public static string AiCustomTag => IsEn ? "custom" : "自定义";
    public static string AiAddCustom => IsEn ? "+ Add custom provider" : "+ 添加自定义提供方";
    public static string AiEditTitle => IsEn ? "Edit provider" : "编辑提供方";
    public static string AiName => IsEn ? "Display name" : "显示名称";
    public static string AiNameHint => IsEn ? "My provider" : "显示名称";
    public static string AiUrlHint => "https://api.example.com/v1";
    public static string AiModelHintBox => IsEn ? "model id" : "模型 ID";
    public static string AiBaseUrl => IsEn ? "API URL" : "API 地址";
    public static string AiProtocolTitle => IsEn ? "API protocol" : "API 协议";
    public static string AiModel => IsEn ? "Model catalog" : "模型目录";
    public static string AiApiKey => IsEn ? "API key" : "API 密钥";
    public static string AiKeyStoreHint => IsEn
        ? "Encrypted with Windows DPAPI and kept out of settings.json. Never written to logs."
        : "用 Windows 加密保存，不写进 settings.json，也不会进日志。";
    public static string AiKeyClear => IsEn ? "Clear key" : "清除密钥";
    public static string AiKeyCleared => IsEn ? "Stored API key cleared." : "已清除保存的 API 密钥。";
    public static string AiKeyUnavailable => IsEn
        ? "Windows encryption is unavailable; the key will only last this session."
        : "系统加密不可用，密钥只在本次运行期间有效。";
    public static string AiKeyStored => IsEn ? "Saved (encrypted)" : "已加密保存";
    public static string AiKeyMissing => IsEn ? "Not saved" : "未保存";
    public static string AiSendFullPaths => IsEn ? "Send full local paths to the AI" : "把完整本地路径发给 AI";
    public static string AiSendFullPathsHint => IsEn
        ? "Off by default: paths go out as <UserProfile>\\AppData\\... so usernames stay local."
        : "默认关闭：路径会以 <UserProfile>\\AppData\\… 的形式发出，用户名不出本机。";
    public static string AiTest => IsEn ? "Test" : "测试连接";
    public static string AiFetchModels => IsEn ? "Fetch models" : "获取可用模型";
    public static string AiNeedUrl => IsEn ? "Fill in the API URL first." : "先填 API 地址。";
    public static string AiModelsEmpty => IsEn
        ? "No models yet. Fetch, or type an ID below."
        : "还没有模型。点获取，或在下面手填 ID。";
    public static string AiModelsOk(int n) => IsEn ? $"{n:N0} models" : $"已获取 {n:N0} 个模型";
    public static string AiProtoCompletions => "openai-completions";
    public static string AiProtoResponses => "openai-responses";
    public static string AiProtoAnthropic => "anthropic-messages";
    /// <summary>结构化判定协议（TypeSafe Jev）。和 chat 那三种不兼容，地址也是单独一条。</summary>
    public static string AiProtoDecisions => "structured-decisions";
    public static string AiDecisionsModel => IsEn ? "Model for decisions" : "判定用的模型";
    public static string AiDecisionsHint => IsEn
        ? "Structured decisions (TypeSafe Jev and friends) is a separate channel — its URL is not /v1. "
          + "For OpenRouter use https://openrouter.ai/api/alpha and model typesafe/jev-1.13. "
          + "It powers batch classification: the app asks it what the items it cannot recognise actually are."
        : "「结构化判定」（TypeSafe Jev 这类）是单独一条通道，地址不走 /v1。"
          + "走 OpenRouter 的话填 https://openrouter.ai/api/alpha，模型填 typesafe/jev-1.13。"
          + "它只用于批量归类：把本地规则认不出用途的那批一次性问清楚是什么。";
    public static string AiExplain => IsEn ? "AI explain" : "AI 解释勾选项";
    public static string AiNeedConfig => IsEn ? "Set base URL and model first." : "先填接口地址和模型。";
    public static string AiNeedKey => IsEn ? "API key is empty." : "还没填 API 密钥。";
    public static string AiNeedItems => IsEn ? "Check some items first." : "先勾几项再解释。";
    public static string AiWorking => IsEn ? "Asking the model…" : "正在问模型…";
    public static string AiOk => IsEn ? "Connected." : "连通。";
    public static string AiTitle => IsEn ? "AI" : "AI 建议";
    public static string AiSystem => IsEn
        ? "You help with disk cleanup. Only use the listed items. Be brief. Do not invent files. Do not recommend deleting protected system items. Reply in the user's language."
        : "你是磁盘清理助手。只根据列出的勾选项给简短建议，不要编造没给的文件，不要建议删除系统保护项。用中文。";
    public static string AiPromptHeader => IsEn
        ? "Explain these checked items. Should I delete them? Any risk?"
        : "解释这些已勾选项：能不能删、有没有风险？";
    public static string AiChatTitle => IsEn ? "AI" : "AI";
    public static string AiChatHint => IsEn ? "Ask about this disk…" : "问这张盘…";
    public static string AiSend => IsEn ? "Send" : "发送";
    public static string AiClear => IsEn ? "Clear" : "清空";
    public static string AiNeedScan => IsEn ? "Scan first." : "先扫描再问。";
    public static string AiScanSkip => IsEn ? "Add a provider in Settings, pick a model, then click Analyze." : "在设置里添加提供方，选好模型，再点分析。";
    public static string AiAnalyze => IsEn ? "Analyze" : "分析";
    public static string AiAddProvider => IsEn ? "Add" : "添加";
    public static string AiDelProvider => IsEn ? "Remove" : "删除";
    public static string AiProviderList => IsEn ? "Providers" : "提供方";
    public static string AiPickModel => IsEn ? "Model used for Analyze" : "分析用的模型";
    public static string AiAppsAnalyzing => IsEn ? "AI is reviewing installed apps…" : "AI 正在分析已安装软件…";
    public static string AiAppsDone(int n) => IsEn ? $"AI reviewed {n:N0} apps. Suggestions are for review only." : $"AI 已分析 {n:N0} 个软件。建议仅供核对，不会自动卸载。";
    public static string AiAppsLocal => IsEn ? "Local rules are active. Configure AI for deeper suggestions." : "当前使用本地规则。配置 AI 后可获得更深入的建议。";
    public static string AiAppsFailed(string error) => IsEn ? "AI unavailable: " + error + ". Local rules remain active." : "AI 暂不可用：" + error + "。已保留本地规则建议。";
    public static string AiAppsStale => IsEn ? "The app list changed during analysis. Results were discarded." : "分析期间软件清单已更新，已丢弃本次结果。";
    public static string AiAppsPrivacy => IsEn
        ? "Remote AI receives app name, publisher, version, installed date, size and usage signals. Paths and API keys are not sent.\n\nContinue with remote analysis?"
        : "远程 AI 只接收软件名称、发布者、版本、安装日期、大小和使用状态，不发送路径和密钥。\n\n是否继续发送给远程 AI 分析？";
    public static string AiAppsAnalyze => IsEn ? "Analyze apps" : "分析软件";
    public static string AiAppsSelect => IsEn ? "Select suggestions" : "勾选建议项";
    public static string AppRecommendationHeader => IsEn ? "Recommendation" : "建议";
    public static string AppScannedSize => IsEn ? "Scanned size" : "扫描占用";
    public static string AppSizeEstimated => IsEn ? " est." : "（估算）";

    // ---- 软件占用拆分（都在「扫描占用」这棵树里）----
    public static string FootprintInstall(string size) => IsEn ? $"Program files: {size}" : $"程序本体：{size}";
    public static string FootprintUserData(string size) => IsEn ? $"Saved data: {size}" : $"保存的数据：{size}";
    public static string FootprintCache(string size) => IsEn ? $"Caches: {size}" : $"缓存：{size}";
    public static string FootprintReclaimable(string size) => IsEn
        ? $"Cleanable without uninstalling: {size}"
        : $"不卸载就能清掉的：{size}";
    public static string AiAppsSystem => IsEn
        ? """
          You analyze an installed-app inventory. Give advice only; never uninstall anything and never invent facts.
          Return JSON only, with this shape: {"items":[{"appId":"...", "decision":"recommend|consider|keep", "confidence":0.0, "reason":"short reason", "dataWarning":"short warning"}]}.
          Use only appId values from the input. Include at most one item per appId. Recommend only apps that are plausibly unwanted,
          redundant, obsolete, or known bloatware. Keep Windows features, system components, protected items, security software,
          drivers, runtimes, SDKs, virtualization tools, and apps with unclear ownership. runningState is running, not-running,
          or unknown. Apps that are running or whose running state is unknown must not be recommended.
          Do not infer personal preference. Keep reasons short and written in the user's language.
          """
        : """
          你是安装软件清单分析员，只提供建议和解释，绝不卸载软件，也不要编造事实。
          只能回复 JSON，格式必须是：{"items":[{"appId":"...", "decision":"recommend|consider|keep", "confidence":0.0, "reason":"简短原因", "dataWarning":"简短提醒"}]}。
          只能使用输入里的 appId，每个 appId 最多输出一次。只有明显可能是不需要、重复、过时或常见捆绑软件时才建议卸载。
          Windows 功能、系统组件、受保护项、安全软件、驱动、运行库、SDK、虚拟化工具以及归属不清的软件必须保留。
          runningState 的值是 running、not-running 或 unknown。正在运行或运行状态无法确认的软件都不能建议卸载。
          不要猜测用户偏好。原因简短，用用户的语言。
          """;
    public static string AiAppsPrompt(string json) =>
        IsEn ? "Installed app inventory:\n" + json : "已安装软件清单：\n" + json;
    /// <summary>模型选择按钮上，还没选任何模型时的占位。</summary>
    public static string AiNoModel => IsEn ? "no model selected" : "未选模型";
    public static string AiNeedScanFirst => IsEn ? "Scan the disk first." : "先扫描磁盘。";
    public static string AiRound(int n) => IsEn ? $"round {n}" : $"第 {n} 轮";
    public static string AiToolResult(string name, string preview) =>
        IsEn ? $"{name} → {preview}" : $"{name} → {preview}";
    public static string AiYou => IsEn ? "You" : "你";
    public static string AiBot => ModelLabel();
    public static string AiJuryName => IsEn ? "Jury" : "评审";
    public static string ModelLabel()
    {
        string name = (App.Settings.AiModel ?? "").Trim();
        return string.IsNullOrEmpty(name) ? "AI" : name;
    }
    public static string AiLampOff => IsEn ? "AI not configured" : "AI 未配置";
    public static string AiReady => IsEn ? "AI ready" : "AI 已配置";
    public static string AiMark => IsEn ? "AI suggested" : "AI 建议";
    public static string AiInside(string note) => IsEn ? "inside: " + note : "内有 · " + note;
    public static string AiLampOn => IsEn ? "AI connected" : "AI 已连接";
    public static string AiLampBusy => IsEn ? "AI reading…" : "AI 正在分析…";
    /// <summary>顶栏 AI 胶囊的空闲 / 分析中状态（可访问名称里要说清是哪一种）。</summary>
    public static string AiChipIdle => IsEn ? "idle" : "空闲";
    public static string AiChipAnalyzing => IsEn ? "analysing" : "分析中";
    public static string AiChipName(string model, bool busy)
        => IsEn
            ? $"AI model {model} — {(busy ? AiChipAnalyzing : AiChipIdle)}"
            : $"AI 模型 {model}（{(busy ? AiChipAnalyzing : AiChipIdle)}）";
    public static string AiLampFail => IsEn ? "AI failed" : "AI 失败";
    public static string AiExtraPrompt => IsEn ? "Extra instructions (optional)" : "额外提示词（可选）";
    public static string AiExtraHint => IsEn
        ? "e.g. Always ask what I want before suggesting deletes."
        : "例如：先问我想清什么，再给建议。";
    public static string SecSummary => IsEn ? "Overview" : "总览";
    public static string SecFolders => IsEn ? "Folders" : "大文件夹";
    public static string SecDeletable => IsEn ? "Likely deletable" : "可能能删";
    public static string SecKeep => IsEn ? "Keep" : "别动";
    public static string SecQuestion => IsEn ? "Question" : "问你一句";
    public static string SecNeed => IsEn ? "Your goal" : "你的需求";
    public static string GradeHigh => IsEn ? "High" : "高把握";
    public static string GradeMid => IsEn ? "Medium" : "中把握";
    public static string GradeLow => IsEn ? "Low" : "低把握";
    public static string JuryToggleOn => IsEn ? "Multi-model jury: paused" : "多模型评选：暂关";
    public static string JuryToggleOff => IsEn ? "Multi-model jury: off" : "多模型评选：关";
    public static string JuryPaused => IsEn
        ? "Jury is paused. Analyze uses one model until chat is stable."
        : "评选先关掉。分析只用当前模型，对话稳了再开。";
    public static string JuryChipMore(int n) => IsEn ? $"+{n}" : $"+{n}";
    public static string JuryDefaultNeed => IsEn
        ? "Clean everything that is safe: temp, caches, dumps, leftover installers, dev caches. Do not touch Windows / Program Files / Users as a whole."
        : "能清的都清：临时、缓存、转储、装完的安装包、开发缓存。Windows / Program Files / Users 整夹别动。";
    public static string JuryWorking(int n) => IsEn ? $"jury: {n} models…" : $"评审：{n} 个模型并行…";
    public static string JuryThinking => IsEn ? "connecting…" : "正在连接…";
    public static string JuryWaiting => IsEn ? "request sent, waiting for reply…" : "请求已发出，等回复…";
    public static string AiWaiting(int s) => IsEn ? $"waiting… {s}s" : $"等回复中… {s} 秒";
    public static string JuryRetry(string err) => IsEn ? "retrying: " + err : "改整段请求：" + err;
    public static string AiPartial(string err) => IsEn ? "Stopped: " + err + " Using what we already have." : "中断：" + err + " 先用已经找到的项。";
    public static string AiHttp520 => IsEn
        ? "The API proxy returned 520. Too many tool rounds. Try Analyze again."
        : "接口中转返回 520，多半是工具轮次太多。再点一次分析。";
    public static string AiTimeout => IsEn
        ? "Timed out. Check URL / key, or the model is too slow."
        : "连接超时。检查地址和密钥，或模型太慢。";
    public static string AiHostFail(string err) => IsEn
        ? "Cannot reach host: " + err
        : "连不上主机：" + err;
    public static string AiNetFail(string err) => IsEn ? "Network error: " + err : "网络错误：" + err;
    public static string AiHttpFail(int code, string err) => IsEn ? $"HTTP {code}: {err}" : $"HTTP {code}：{err}";
    public static string JuryMerge => IsEn ? "Merged score" : "汇总";
    public static string JurySeatOk(string name, int n) => IsEn ? $"{name} voted {n} items" : $"{name} 投了 {n} 条";
    public static string JurySeatFail(string name, string err) => IsEn ? $"{name} failed, no vote: {err}" : $"{name} 失败没投：{err}";
    public static string JurySeatEmpty(string name) => IsEn ? $"{name} returned nothing, no vote" : $"{name} 没有给出条目，没投";
    public static string JuryVotedYes(IEnumerable<string> names) => IsEn ? "yes: " + string.Join(", ", names) : "投了：" + string.Join("、", names);
    public static string JuryVotedNo(IEnumerable<string> names) => IsEn ? "no: " + string.Join(", ", names) : "没投：" + string.Join("、", names);
    public static string JurySummary(int models, int high) => IsEn
        ? $"{models} models voted. {high} high-confidence items are checked on the right."
        : $"{models} 个模型已投票。高把握 {high} 项已在右边勾上。";
    public static string JuryAsk => IsEn
        ? "High-confidence items are checked. Press Recycle when ready."
        : "高把握项已勾上。确认后点删除。";
    public static string JuryChecked => IsEn ? "High-confidence items are now checked on the right." : "高把握项已在右边勾上。等你点删除。";
    public static string JuryNone => IsEn ? "No overlapping suggestions. Try a clearer goal." : "没有重叠建议。需求再说具体一点。";
    public static string JurySystem => IsEn
        ? """
          Score cleanup candidates. Never delete. Never invent paths. Copy full paths from the scan.
          Never list Windows / Program Files / Users / WinSxS as a whole.
          List at most 8 DELETABLE items:

          SUMMARY
          one sentence

          DELETABLE
          GOTO C:\full\path	12.4G	why
          """
        : """
          给清理项打分。不要删除，不要编造路径。只从扫描结果复制完整路径。
          不要写整个 Windows / Program Files / Users / WinSxS。
          只列最值得清的 8 条。格式：

          SUMMARY
          一句话

          DELETABLE
          GOTO C:\完整路径	12.4G	原因
          """;
    public static string AiScanHeader => IsEn
        ? "Scan finished. Reply in the exact format below. Do not invent files."
        : "扫描结束。必须按下述格式回复。不要编造文件。";
    /// <summary>
    /// AI 只做一件事：告诉用户「这是什么」。风险档位由规则（AppSignatures / CleanAnalyzer）判定，
    /// 可审计、可复现，不让模型来回改，也省得它一本正经地把系统文件标成可删。
    /// </summary>
    public static string AiExplainBtn => IsEn ? "Ask AI what these are" : "让 AI 看看这些是什么";
    public static string AiCatEmpty => IsEn ? "Nothing to analyze in this category." : "这个分类没有可分析的条目。";
    /// <summary>右键「问 AI 这是什么」用的提示词：只解释，不判风险。</summary>
    public static string AiFolderAskSystem => IsEn
        ? """
          You explain a folder to someone who knows nothing about computers.
          First sentence: what happens if they delete it. Then, in everyday words, what this folder is and which app made it.
          BANNED words: command names (pip, npm, yarn), package/file formats (wheel, source package), and tech terms
          (hash, response body, P2P, content-addressed, dependency, index, cache directory).
          You MAY name well-known apps (WeChat, NetEase Cloud Music, Chrome, Steam) but say what they do.
          Do NOT judge deletion risk and do NOT tell the user to delete or keep it — the app rates risk by rules.
          No markdown, no preamble, no bullet list. Two or three plain sentences, no jargon.
          """
        : """
          你要向一个完全不懂电脑的人解释一个文件夹。
          第一句先说「删了会怎样」，然后用大白话说清这是什么、哪个软件弄出来的。
          禁止出现的词：命令行名（pip、npm、yarn）、格式名（wheel、源码包）、技术词（哈希、响应体、P2P、内容寻址、依赖、索引、缓存区）。
          可以提大众软件名（微信、网易云音乐、Chrome、Steam），但要顺带说清它是干嘛的。
          不要判断能不能删，不要劝用户删或留——风险由软件按规则判定。
          不要 markdown，不要开场白，不要列点。两三句白话，不要术语。
          """;
    /// <summary>问 AI 单个文件夹时的用户消息模板。</summary>
    public static string AiFolderAskUser(string path, string listing) => IsEn
        ? $"Folder: {path}\nTop items:\n{listing}"
        : $"文件夹：{path}\n里面的条目：\n{listing}";
    public static string AiFolderAskFail(string err) => IsEn
        ? "AI did not answer: " + err
        : "AI 没答上来：" + err;
    public static string AiCatDone(int n) => IsEn ? $"AI explained {n} items" : $"AI 说明了 {n} 条";
    public static string AiCatStopped => IsEn ? "Stopped." : "已停止。";
    public static string AiNoItems => IsEn ? "AI returned nothing usable." : "AI 没给出可用的说明。";
    /// <summary>清理表格里 AI 说明列的表头。</summary>
    public static string AiColNote => IsEn ? "What it is" : "这是什么";

    public static string AiAnalystSystem => IsEn
        ? """
          You are a file analyst in a disk cleaner. Never delete. Never invent paths.
          The user wants the disk as clean as it can safely be. Do not ask what to clean. List every safe candidate.
          FINAL reply MUST use this exact shape. No markdown, no extra sections, no QUESTION section:

          SUMMARY
          one or two sentences.

          DELETABLE
          GOTO C:\full\path	2.4G	why it can be deleted
          (concrete files/caches/dumps/leftover installers/dev caches; never Windows / Program Files / Users / WinSxS as a whole)

          KEEP
          GOTO C:\full\path	24.4G	why not delete

          Each GOTO line: GOTO, then the full path, then size, then one-line reason. Copy paths from the scan list. Suggest safe cache. WeChat/QQ: cache only. Do not invent XML/DSML/tool tags. Do not call tools. One shot.
          """
        : """
          你是磁盘清理软件里的文件分析师。不要删除，不要编造路径。
          用户想尽量清干净。不要问清哪块，把能安全清的都列出来。
          最终回复必须用下面这个格式。不要 markdown，不要 QUESTION 章节：

          SUMMARY
          一两句话总览。

          DELETABLE
          GOTO C:\完整路径	2.4G	为什么能删
          （只写具体文件/缓存/转储/装完的安装包/开发缓存，不要写整个 Windows / Program Files / Users / WinSxS）

          KEEP
          GOTO C:\完整路径	24.4G	为什么不能删

          每条 GOTO：GOTO、完整路径、大小、一句原因。路径从扫描清单原样复制。safe cache 可建议删。微信/QQ 只动缓存。不要写 XML/DSML/工具标签，不要调工具，一轮答完。
          """;
    public static string AiTool(string name) => IsEn ? $"tool: {name}" : $"工具：{name}";
    public static string AiKindName(AiProtocol p) => p switch
    {
        AiProtocol.Responses => AiProtoResponses,
        AiProtocol.Anthropic => AiProtoAnthropic,
        AiProtocol.Decisions => AiProtoDecisions,
        _ => AiProtoCompletions,
    };
    public static string LangZh => "中文";
    public static string LangEn => "English";
    public static string Close => IsEn ? "Close" : "关闭";
    public static string Yes => IsEn ? "Yes" : "确定";
    public static string No => IsEn ? "No" : "取消";
    public static string AboutTitle => IsEn ? "About" : "关于";
    public static string SettingsTitle => IsEn ? "Settings" : "设置";
    public static string AboutBody => IsEn
        ? "A fast NTFS disk scanner. MIT. Uninstall list uses Bulk Crap Uninstaller (Apache 2.0, Marcin Szeniak)."
        : "NTFS 磁盘秒扫。MIT。卸载列表使用 Bulk Crap Uninstaller（Apache 2.0，Marcin Szeniak）。";
    public static string Repo => "https://github.com/DDWking/ai-disk-cleaner";
    public static string Folder => IsEn ? "Folder" : "文件夹";
    public static string Items => IsEn ? "Items" : "项目";
    public static string FilesCol => IsEn ? "Files" : "文件";
    public static string OpenInExplorer => IsEn ? "Open in Explorer" : "在资源管理器中打开";
    public static string CopyPath => IsEn ? "Copy path" : "复制路径";
    public static string CopyName => IsEn ? "Copy name" : "复制名称";
    public static string Properties => IsEn ? "Properties" : "属性";
    public static string DeleteToRecycle => IsEn ? "Delete to Recycle Bin" : "删除到回收站";
    public static string DeleteBlocked => IsEn ? "Protected system item, won't delete." : "系统保护项，不能删。";
    public static string DeleteConfirm(string name, string size) =>
        IsEn ? $"Move “{name}” ({size}) to Recycle Bin?" : $"把「{name}」（{size}）删到回收站？";
    public static string DeleteFailed(string msg) => IsEn ? "Delete failed: " + msg : "删除失败：" + msg;
    public static string DeleteOk => IsEn ? "Moved to Recycle Bin" : "已移到回收站";

    // ---- 删除逐项结果报告 ----
    public static string DeleteResultTitle => IsEn ? "Delete result" : "删除结果";

    /// <summary>首行：成功几项、释放多少、总共处理多少项。</summary>
    public static string DeleteResultHead(int recycled, string freed, int total)
        => IsEn
            ? $"{recycled:N0} of {total:N0} moved to Recycle Bin · freed about {freed}"
            : $"已移入回收站 {recycled:N0} / {total:N0} 项 · 释放约 {freed}";

    /// <summary>按结局分组的明细行：原因 · 条数 · 前几个名字。</summary>
    public static string DeleteResultGroup(DeletionOutcome outcome, int count, string names)
    {
        string label = outcome switch
        {
            DeletionOutcome.NotFound => IsEn ? "already gone" : "已经不在了",
            DeletionOutcome.PathChanged => IsEn ? "path changed" : "路径已变化",
            DeletionOutcome.Modified => IsEn ? "changed since scan" : "扫描后被改过",
            DeletionOutcome.AccessDenied => IsEn ? "no permission" : "权限不足",
            DeletionOutcome.Protected => IsEn ? "protected" : "受保护",
            DeletionOutcome.InUse => IsEn ? "in use" : "正在使用",
            DeletionOutcome.SkippedByUser => IsEn ? "skipped" : "已跳过",
            DeletionOutcome.RedundantChild => IsEn ? "already covered by parent" : "父目录已包含",
            DeletionOutcome.Failed => IsEn ? "failed" : "失败",
            DeletionOutcome.Recycled => IsEn ? "deleted" : "已删除",
            _ => DeletionText.Outcome(outcome),
        };
        return IsEn
            ? $"{label} ({count:N0}): {names}"
            : $"{label}（{count:N0}）：{names}";
    }

    /// <summary>预检摘要：确认框里告诉用户「这一批里有多少项其实删不了/要小心」。</summary>
    public static string DeletePreflightNote(int ready, int blocked, int missing, int changed, int inUse, int sensitive)
    {
        var bits = new List<string>();
        if (blocked > 0) bits.Add(IsEn ? $"{blocked:N0} protected" : $"{blocked:N0} 项受保护");
        if (missing > 0) bits.Add(IsEn ? $"{missing:N0} already gone" : $"{missing:N0} 项已不存在");
        if (changed > 0) bits.Add(IsEn ? $"{changed:N0} changed" : $"{changed:N0} 项已变化");
        if (inUse > 0) bits.Add(IsEn ? $"{inUse:N0} in use" : $"{inUse:N0} 项正在使用");
        if (sensitive > 0) bits.Add(IsEn ? $"{sensitive:N0} in sensitive folders" : $"{sensitive:N0} 项在敏感位置");
        if (bits.Count == 0)
            return IsEn ? $"\nPre-check: all {ready:N0} items ready." : $"\n删前检查：{ready:N0} 项都可以删。";
        string head = IsEn ? $"\nPre-check: {ready:N0} ready, " : $"\n删前检查：{ready:N0} 项可删，";
        return head + string.Join(IsEn ? ", " : "、", bits) + (IsEn ? "." : "。");
    }

    public static string SortBySize => IsEn ? "Sort by size" : "按大小排序";
    public static string SortByName => IsEn ? "Sort by name" : "按名称排序";
    public static string SortByModified => IsEn ? "Sort by date" : "按修改时间排序";
    public static string FilterOff => IsEn ? "Show all types" : "显示全部类型";
    public static string FilterExt(string ext) => IsEn ? $"Filter: {ext}" : $"筛选：{ext}";
    public static string MoreFiles(int n) => IsEn ? $"+ {n:N0} more files" : $"还有 {n:N0} 个文件";
    public static string FilesIn(int n, string path) =>
        IsEn ? $"{n:N0} files in {path}" : $"{n:N0} 个文件在 {path}";
    public static string PropBody(FileEntry e)
    {
        var lines = new[]
        {
            e.FullPath,
            "",
            (IsEn ? "Size: " : "大小：") + FileEntry.FormatSize(e.Size),
            (IsEn ? "Allocated: " : "分配：") + FileEntry.FormatSize(e.Allocated),
            e.IsDirectory
                ? FileDirCount(e.FileCount, e.FolderCount)
                : (IsEn ? "Type: " : "类型：") + e.Category,
            e.Modified == DateTime.MinValue ? "" : (IsEn ? "Modified: " : "修改：") + e.ModifiedText,
        };
        return string.Join(Environment.NewLine, lines.Where(s => s != null));
    }


    /// <summary>2.11：这一页的语义是「按文件夹删除」，不再是「整理」。</summary>
    public static string TabOrganize => IsEn ? "Delete by folder" : "按文件夹删除";
    public static string TabClean => IsEn ? "Clean center" : "清理中心";
    public static string TabUninstall => IsEn ? "Uninstall" : "卸载";
    public static string UninstallRefresh => IsEn ? "Refresh" : "刷新";
    public static string UninstallRun => IsEn ? "Uninstall selected" : "卸载勾选项";
    // ---- 2.11 卸载页：只讲事实的单一表格 ----
    /// <summary>页头唯一一句事实说明：这里只列「装了什么」。</summary>
    public static string UninstallFactsOnly => IsEn
        ? "Installed software, largest footprint first."
        : "已安装软件，按占用从大到小。";
    public static string ColVersion => IsEn ? "Version" : "版本";
    public static string ColInstallDate => IsEn ? "Installed" : "安装日期";
    /// <summary>用途列：只说「这是什么软件」，不带建议。</summary>
    public static string AppPurposeHeader => IsEn ? "What it is" : "用途";
    public static string UninstallEmpty => IsEn ? "No installed software was found." : "没有列出任何已安装软件。";
    public static string UninstallSortNote => IsEn
        ? "Sorted by footprint: measured scan first, install record next, unknown last."
        : "按占用降序：扫描实测优先，其次安装记录，未知的排在最后。";
    public static string UninstallRowActions => IsEn ? "Actions" : "操作";
    public static string UninstallReveal => IsEn ? "Open install folder" : "打开安装目录";
    public static string UninstallRevealTip => IsEn
        ? "Open the install folder in Explorer (does not run anything)"
        : "在资源管理器中打开安装目录（不会执行里面的程序）";
    public static string UninstallProtectedNote => IsEn
        ? "This item cannot be uninstalled from here."
        : "这一项无法从这里卸载。";
    // ---- 2.11 清理首页：分类就地展开 ----
    public static string ExpandLocations => IsEn ? "Show cleanup locations" : "展开这一类的清理位置";
    public static string CollapseLocations => IsEn ? "Hide cleanup locations" : "收起这一类的清理位置";
    public static string UninstallListing => IsEn ? "Listing installed apps…" : "正在列出已装软件…";
    public static string UninstallHint => IsEn
        ? "Suggestions are analysis only. Review and check apps yourself; uninstall runs each app's own uninstaller."
        : "建议仅供分析。请自行核对并勾选，确认后才会调用软件自己的卸载程序。";
    public static string UninstallSearchHint => IsEn ? "Search apps…" : "搜索软件…";
    public static string UninstallFiltered(int shown, int total) =>
        IsEn ? $"{shown:N0} / {total:N0} apps" : $"{shown:N0} / {total:N0} 个软件";
    public static string UninstallCount(int n) => IsEn ? $"{n:N0} apps" : $"{n:N0} 个软件";
    public static string UninstallConfirm(int n) =>
        IsEn ? $"Run the official uninstaller for {n:N0} apps? Each may show its own window."
             : $"对 {n:N0} 个软件运行官方卸载程序？每个都可能弹出自己的窗口。";
    /// <summary>
    /// 2.11 确认框：**只讲事实** —— 要跑几个软件的官方卸载程序、它们合计占用多少。
    /// 不再出现任何「有提醒 / 建议核对」这类基于建议档位的说法。
    /// </summary>
    public static string UninstallConfirmDetails(IEnumerable<string> names, int n, string size) =>
        IsEn
            ? $"You are about to run the official uninstaller for {n:N0} apps.\n"
              + $"Combined disk footprint: {size} — the best available number (measured scan or the "
              + "installer's own record), not a promise of freed space.\n\n" + string.Join("\n", names)
            : $"即将对 {n:N0} 个软件运行官方卸载程序。\n"
              + $"所选项目的磁盘占用合计 {size} —— 这是目前最可信的一个数（扫描实测或安装记录写的），"
              + "不等于卸载后一定能释放这么多。\n\n" + string.Join("\n", names);
    public static string UninstallConfirmDetails(IEnumerable<string> names, int n, string size, bool warning) =>
        IsEn
            ? $"You are about to run official uninstallers for {n:N0} apps.\n"
              + $"Combined disk footprint: {size} — the best available number (measured scan or the "
              + "installer's own record), not a promise of freed space.\n\n" + string.Join("\n", names)
              + (warning ? "\n\nSome selected apps have warnings. Review them before confirming." : "")
            : $"即将对 {n:N0} 个软件运行官方卸载程序。\n"
              + $"所选项目的磁盘占用合计 {size} —— 这是目前最可信的一个数（扫描实测或安装记录写的），"
              + "不等于卸载后一定能释放这么多。\n\n" + string.Join("\n", names)
              + (warning ? "\n\n部分软件有提醒，请确认后再继续。" : "");
    public static string UninstallProtected => IsEn ? "Protected" : "受保护";
    public static string UninstallGroupOk => IsEn ? "Can uninstall" : "可卸载";
    public static string UninstallGroupSteam(int n) => IsEn ? $"Steam ({n:N0})" : $"Steam（{n:N0}）";
    public static string UninstallGroupFeatures(int n) =>
        IsEn ? $"Windows features ({n:N0})" : $"Windows 功能（{n:N0}）";
    public static string UninstallGroupProtected(int n) =>
        IsEn ? $"Protected ({n:N0})" : $"受保护（{n:N0}）";
    public static string UninstallWinFeature => IsEn ? "Windows feature" : "Windows 功能";
    public static string UninstallConfirmFeatures(int n, int features) =>
        IsEn ? $"Run uninstallers for {n:N0} items, including {features:N0} Windows features? Features use DISM and may need a reboot."
             : $"对 {n:N0} 项运行卸载（含 {features:N0} 个 Windows 功能）？功能走 DISM，可能要重启。";
    public static string UninstallNoWay => IsEn ? "No uninstaller" : "无法卸载";
    public static string UninstallGroupRecommend(int n) => IsEn ? $"Suggested to uninstall ({n:N0})" : $"建议卸载（{n:N0}）";
    public static string UninstallGroupConsider(int n) => IsEn ? $"Needs a look ({n:N0})" : $"需要看一下（{n:N0}）";
    public static string UninstallGroupKeep(int n) => IsEn ? $"Suggested to keep ({n:N0})" : $"建议保留（{n:N0}）";
    /// <summary>没有足够证据的一档。**中性**：不叫「可以考虑」，也不假装有结论。</summary>
    public static string UninstallGroupNeutral(int n) => IsEn ? $"Not assessed ({n:N0})" : $"未评估（{n:N0}）";
    public static string AppRecommendationLabel(AppRecommendationDecision decision) => decision switch
    {
        AppRecommendationDecision.Recommend => IsEn ? "Recommend uninstall" : "建议卸载",
        AppRecommendationDecision.Keep => IsEn ? "Keep" : "建议保留",
        AppRecommendationDecision.Neutral => IsEn ? "Not assessed" : "未评估",
        _ => IsEn ? "Worth a look" : "需要看一下",
    };
    public static string AppKeepSystem => IsEn ? "System or protected component" : "系统或受保护组件";
    public static string AppKeepNoUninstaller => IsEn ? "No usable uninstaller was found" : "没有可用的卸载程序";
    public static string AppKeepCritical => IsEn ? "Runtime, driver, security, or virtualization component" : "运行库、驱动、安全或虚拟化组件";
    /// <summary>Windows 自带组件 / 厂商驱动：本页不给卸载建议（也没有可信占用）。</summary>
    public static string AppKeepInboxComponent =>
        IsEn ? "Windows component or device software; this page does not suggest removing it"
             : "Windows 自带组件或设备软件，本页不提供卸载建议";
    public static string AppConsiderRunning => IsEn ? "Currently running" : "当前正在运行";
    public static string AppRunningWarning => IsEn ? "Close it before uninstalling" : "卸载前请先退出软件";
    /// <summary>运行状态只作为事实说明，不当成卸载依据。</summary>
    public static string RunningStateText(AppRunningState state) => state switch
    {
        AppRunningState.Running => IsEn ? "Running: yes" : "运行状态：正在运行",
        AppRunningState.NotRunning => IsEn ? "Running: no" : "运行状态：未在运行",
        _ => IsEn ? "Running: could not be verified" : "运行状态：未能确认",
    };
    public static string AppRecommendBloat => IsEn ? "Known bundled or unwanted software pattern" : "符合常见捆绑或不需要软件特征";
    /// <summary>中性档不给理由 —— 理由栏只写有证据的话，不写套话。</summary>
    public static string AppNeutralReason => "";

    // ---- 占用可信度：扫描实测 / 安装记录估计 / 未测得，三者必须一眼分清 ----
    /// <summary>安装记录里的体积（不是实测）。</summary>
    public static string AppSizeFromRecord(string size) =>
        IsEn ? $"~{size} (install record)" : $"约 {size}（安装记录）";
    /// <summary>没有实测、也没有可信记录时照实说未知。</summary>
    public static string AppSizeUnknown => IsEn ? "unknown" : "未知";
    /// <summary>实测到 0 字节：格子里只写 0 KB，原因进悬停。</summary>
    public static string AppSizeMeasuredEmpty => IsEn ? "0 KB" : "0 KB";
    public static string AppSizeSourceMeasured => IsEn ? "Source: measured by scan" : "来源：扫描实测";
    public static string AppSizeSourceRecord => IsEn ? "Source: install record" : "来源：安装记录";
    /// <summary>占用为什么没测到 —— 悬停里说清，不让用户以为是软件真的只有这么大。</summary>
    public static string AppFootprintNotMeasured(string why) =>
        IsEn ? $"Footprint not attributable to this app: {why}" : $"占用未归属到这个软件：{why}";
    public static string AppFootprintWhyShared =>
        IsEn ? "the install path is shared with other apps" : "安装目录与其他软件共用";
    public static string AppFootprintWhySystemDir =>
        IsEn ? "the install path is inside the Windows directory" : "安装路径在 Windows 系统目录里";
    public static string AppFootprintWhyGenericRoot =>
        IsEn ? "the recorded path is a generic parent folder, not this app's own folder" : "记录的是通用父目录，不是这个软件自己的目录";
    public static string AppFootprintWhyNotScanned =>
        IsEn ? "the folder is not covered by the scanned drive(s)" : "这个目录不在本次扫描的盘里";
    public static string AppFootprintWhyMissing =>
        IsEn ? "the recorded folder does not exist" : "记录的目录不存在";
    public static string AppFootprintWhyUnreadable =>
        IsEn ? "the running state could not be verified, so size was not measured" : "运行状态未能确认，因此没有实测";
    /// <summary>磁盘占用 ≠ 卸载可释放量。这句话必须写在确认框里。</summary>
    public static string AppFootprintNotEqualFree =>
        IsEn ? "Disk footprint is not the same as what an uninstall actually frees."
             : "磁盘占用不等于卸载实际能释放的空间。";
    public static string UninstallConfirmSizeNote =>
        IsEn ? "size shown is the best available number (measured scan or install record), not a promise"
             : "上面这个数是目前最可信的一个（扫描实测或安装记录），不是承诺";
    public static string AppSizeColumnHeader => IsEn ? "Footprint" : "占用";
    public static string AppSizeColumnTip => IsEn
        ? "Measured from the scan when possible; otherwise the installer's own record. Unknown stays unknown."
        : "能实测就用扫描结果；否则用安装记录。两者都没有就写「未知」。";
    /// <summary>建议列不是证据就不要往上写东西。</summary>
    public static string AppNeutralHint => IsEn
        ? "No uninstall signal was found. Search, sort by footprint, or uninstall it manually."
        : "没有找到卸载依据。可以搜索、按占用排序，或自己直接卸载。";
    public static string UninstallAiNotConfigured => IsEn
        ? "Local rules are being used. AI is optional and never controls uninstall."
        : "当前使用本地规则。AI 可选，且永远不会直接控制卸载。";
    public static string UninstallRunning => IsEn ? "Uninstalling…" : "正在卸载…";
    public static string UninstallDone => IsEn ? "Done" : "完成";
    public static string UninstallFailed => IsEn ? "Failed" : "失败";
    public static string UninstallSkipped => IsEn ? "Skipped" : "跳过";
    public static string UninstallWaiting => IsEn ? "Waiting" : "等待";
    public static string UninstallRetry => IsEn ? "Retry uninstall" : "重试卸载";
    public static string UninstallOpenOfficial => IsEn ? "Open official uninstaller" : "打开官方卸载程序";
    public static string UninstallResultSummary(int ok, int fail, int skip, string freed) =>
        IsEn ? $"Uninstall finished: {ok} done, {fail} failed, {skip} skipped · freed ≈ {freed}"
             : $"卸载完成：成功 {ok}，失败 {fail}，跳过 {skip} · 释放约 {freed}";
    public static string UninstallResultPending => IsEn ? "Still left:" : "未完成：";
    /// <summary>只按建议勾选并展示，绝不代替用户删除——AI 只负责分析，删不删由用户确认。</summary>
    public static string ReviewSuggestions => IsEn ? "Review suggestions" : "看 AI 建议";
    public static string ReviewSuggestionsShort => IsEn ? "Pre-check by suggestion" : "按 AI 建议勾选";
    public static string SelectAllTip => IsEn ? "Select all / clear" : "全选 / 取消全选";
    public static string ConfirmDelete(int n) => n > 0
        ? (IsEn ? $"Delete {n:N0} item(s)" : $"确认删除 {n:N0} 项")
        : (IsEn ? "Delete selected" : "确认删除");
    public static string ReviewScanFirst =>
        IsEn ? "Scan the disk first, then review suggestions." : "请先扫描磁盘，再看建议。";
    public static string ReviewScanning =>
        IsEn ? "Scanning in progress, please wait." : "正在扫描，请稍候。";
    public static string ReviewNothing =>
        IsEn ? "No suggestions right now." : "暂时没有可以建议清理的内容。";
    public static string ReviewHint(int junkCount, string junkSize, int appCount, string appSize) =>
        IsEn
            ? $"Pre-checked by suggestion: {junkCount:N0} item(s) ({junkSize}) and {appCount:N0} app(s) (≈{appSize}). Nothing has been deleted — review each group, then confirm."
            : $"已按建议勾选：可清理 {junkCount:N0} 项（{junkSize}），软件 {appCount:N0} 个（约 {appSize}）。还没有删除任何东西——请逐组核对后再确认。";
    public static string JunkScanning => IsEn ? "Scanning leftovers…" : "正在扫描残留…";
    public static string JunkNone => IsEn ? "No leftovers found." : "没有发现残留。";
    public static string JunkHint(int n, int safe) =>
        IsEn ? $"{n:N0} leftover items. High-confidence ones are checked ({safe:N0}). Review before deleting."
             : $"发现 {n:N0} 项残留。高置信度已勾选（{safe:N0}）。删前请核对。";
    public static string JunkDelete => IsEn ? "Delete leftovers" : "删除残留";
    public static string JunkSafe => IsEn ? "Select safe leftovers" : "勾选安全残留";
    public static string JunkConfirm(int n) =>
        IsEn ? $"Permanently remove {n:N0} leftover items? Files go to Recycle Bin; registry keys are deleted."
             : $"删除 {n:N0} 项残留？文件进回收站，注册表项会直接删。";
    public static string JunkDeleted(int ok, int fail) =>
        IsEn ? $"Removed {ok:N0} leftovers" + (fail > 0 ? $", {fail:N0} failed" : "")
             : $"已删残留 {ok:N0} 项" + (fail > 0 ? $"，失败 {fail:N0}" : "");
    public static string ColCategory => IsEn ? "Kind" : "类型";
    public static string ColConfidence => IsEn ? "Confidence" : "把握";
    public static string JunkLevel(ConfidenceLevel level) => level switch
    {
        ConfidenceLevel.VeryGood => IsEn ? "Very likely" : "很有把握",
        ConfidenceLevel.Good => IsEn ? "Likely" : "较有把握",
        ConfidenceLevel.Questionable => IsEn ? "Unsure" : "不确定",
        ConfidenceLevel.Bad => IsEn ? "Risky" : "风险高",
        _ => IsEn ? "Unknown" : "未知",
    };
    public static string Publisher => IsEn ? "Publisher" : "发布者";
    public static string Status => IsEn ? "Status" : "状态";
    public static string Refresh => IsEn ? "Refresh" : "刷新";
    public static string Analyze => IsEn ? "Analyze" : "分析";
    public static string Analyzing => IsEn ? "Analyzing…" : "正在分析…";
    public static string RecycleSelected => IsEn ? "Recycle selected" : "删除勾选项";
    public static string SelectAll => IsEn ? "Select all" : "全选";
    public static string SelectSafe => IsEn ? "Select safe to delete" : "勾选可安全删除";
    public static string SelectNone => IsEn ? "Clear checks" : "取消勾选";
    public static string CleanHintReady(int n, string size) =>
        IsEn ? $"Cleanable: {n:N0} items, about {size}" : $"可清理：{n:N0} 项，约 {size}";
    public static string RecycleManyConfirm(int n, string size) =>
        IsEn ? $"Move {n:N0} items ({size}) to Recycle Bin?" : $"把 {n:N0} 项（{size}）删到回收站？";
    public static string RecycleManyOk(int n) => IsEn ? $"Moved {n:N0} items" : $"已移到回收站 {n:N0} 项";
    public static string RecycleManySummary(int ok, int failed, int skipped, string freed) => IsEn
        ? $"Recycle Bin: {ok:N0} moved, {failed:N0} failed, {skipped:N0} protected/skipped, freed {freed}"
        : $"回收站操作：成功 {ok:N0} 项，失败 {failed:N0} 项，保护/跳过 {skipped:N0} 项，释放 {freed}";
    public static string NothingSelected => IsEn ? "Nothing selected." : "没有勾选项。";
    /// <summary>清理表格说明列：规则原因，分析后被 AI 覆盖。</summary>
    public static string ColReason => IsEn ? "Note" : "说明";
    /// <summary>只说「这是什么」，不下安全结论——安全与否由分组标题承担。</summary>
    public static string ColType => IsEn ? "Type" : "类型";
    public static string FilterLabel => IsEn ? "Filter" : "筛选";
    public static string ColName => IsEn ? "Name" : "名称";

    public static string CatAi => IsEn ? "AI suggested" : "AI 建议";
    public static string CatCleanable => IsEn ? "Safe to clean" : "可清理";
    public static string CatLarge => IsEn ? "Largest files" : "大文件";
    public static string CatOld => IsEn ? "Old files" : "老文件";
    public static string CatDup => IsEn ? "Duplicates" : "重复文件";
    public static string CatEmpty => IsEn ? "Empty folders" : "空文件夹";
    public static string CatShortcut => IsEn ? "Broken shortcuts" : "失效快捷方式";
    public static string CatLong => IsEn ? "Long paths" : "超长路径";
    public static string CatCompare => IsEn ? "Since last scan" : "和上次比";

    // ===== 按用途分类（来自 AppSignatures.Category）=====
    public static string CatSystem => IsEn ? "System" : "系统";
    public static string CatBrowser => IsEn ? "Browsers" : "浏览器";
    public static string CatDev => IsEn ? "Dev tools" : "开发工具";
    public static string CatChat => IsEn ? "Chat apps" : "聊天软件";
    public static string CatGame => IsEn ? "Games" : "游戏";
    public static string CatMedia => IsEn ? "Media" : "影音";
    public static string CatCloud => IsEn ? "Cloud drives" : "网盘";
    public static string CatVm => IsEn ? "Virtual machines" : "虚拟机";
    public static string CatIde => IsEn ? "IDE / editors" : "IDE / 编辑器";
    public static string CatAiTool => IsEn ? "AI tools" : "AI 工具";
    public static string CatOffice => IsEn ? "Office" : "办公";
    public static string CatSecurity => IsEn ? "Security" : "安全软件";
    public static string CatBloat => IsEn ? "Bloatware" : "卸载残留";
    public static string CatIme => IsEn ? "Input methods" : "输入法";
    public static string CatOther => IsEn ? "Other" : "其他";
    public static string CatAll => IsEn ? "All" : "全部";

    // ===== 风险三档 =====

    public static string NoteCache => IsEn ? "cache data" : "缓存数据";
    /// <summary>
    /// 候选组的标题。**不写「删了会自动重建」** —— 组里只要混进一个不会重建的东西，
    /// 这就是在替用户打包票。后果由每条用途自己的「影响」一句话说明，标题只报数量和空间。
    /// </summary>
    public static string CleanGroupSafe(int n, string size) => IsEn
        ? $"Cleanup candidates ({n:N0} · {size})"
        : $"清理候选（{n:N0} 项 · {size}）";
    public static string CleanGroupConfirm(int n, string size) => IsEn
        ? $"Needs your review ({n:N0} · {size})"
        : $"需要你确认（{n:N0} 项 · {size}）";
    public static string CleanScopeTotal(int total, int shown, string size) => IsEn
        ? $"{total:N0} total · {shown:N0} shown · {size}"
        : $"共 {total:N0} 项 · 当前显示 {shown:N0} 项 · {size}";
    public static string SelectedHint(int n, string size) => IsEn
        ? $"{n:N0} selected · about {size}"
        : $"已勾选 {n:N0} 项 · 可释放约 {size}";

    public static string GroupTemp => IsEn ? "Temp / cache" : "临时/缓存";
    public static string GroupDump => IsEn ? "Crash dumps" : "崩溃转储";
    public static string GroupInstaller => IsEn ? "Installers" : "安装包";
    public static string GroupRecycle => IsEn ? "Recycle Bin" : "回收站";
    public static string GroupLarge => IsEn ? "Large" : "大文件";
    public static string GroupOld => IsEn ? "Old" : "老文件";
    public static string GroupDup => IsEn ? "Duplicate" : "重复";
    public static string GroupEmpty => IsEn ? "Empty" : "空文件夹";
    public static string GroupShortcut => IsEn ? "Shortcut" : "快捷方式";
    public static string GroupLong => IsEn ? "Long path" : "超长路径";
    public static string GroupCompare => IsEn ? "Delta" : "变化";

    /// <summary>位置行第二行的短标记：只在认不出用途时出现，不再每行挂一句长解释。</summary>
    public static string PurposeUnclear => IsEn ? "purpose unclear" : "用途待确认";
    /// <summary>聚合位置找不到唯一真实目录时的提示。</summary>
    public static string RevealNoFolder => IsEn
        ? "This entry has no single real folder to open"
        : "这一项没有对应的真实文件夹可以打开";

    // ---- 本地候选项分组（按真实子目录/应用/规则把位置拆成可选子组） ----
    public static string GroupBySubdir => IsEn ? "by folder" : "按子目录分";
    public static string GroupByApp => IsEn ? "by app" : "按应用分";
    public static string GroupByRule => IsEn ? "by rule" : "按规则分";
    public static string GroupByKind => IsEn ? "by file type" : "按文件类型分";
    public static string GroupUnspecified => IsEn ? "(no rule label)" : "（未标注）";
    public static string GroupOthers(int hidden) => IsEn
        ? $"Others ({hidden} more groups merged here)"
        : $"其它（{hidden} 个小组并到这里）";
    public static string GroupFolders => IsEn ? "folders" : "文件夹";
    public static string GroupFiles => IsEn ? "files" : "文件";
    public static string GroupNoExtension => IsEn ? "(no extension)" : "（无扩展名）";

    public static string GroupRiskMix(int candidates, int confirm) => IsEn
        ? $"{candidates} eligible · {confirm} need confirmation"
        : $"{candidates} 项符合清理资格 · {confirm} 项需确认";
    public static string GroupProtectedCount(int n) => IsEn
        ? $"{n} protected item(s) — cannot be cleaned"
        : $"{n} 项受保护，不能清理";

    /// <summary>「选择本组候选」按钮：必须写明会新增多少项、多少空间。</summary>
    public static string SelectGroupCandidates(int count, string size) => IsEn
        ? $"Select this group ({count:N0} · {size})"
        : $"选择本组候选（{count:N0} 项 · {size}）";
    public static string GroupAllSelected => IsEn ? "all already selected" : "本组已全部选中";
    public static string GroupNeedsReview => IsEn
        ? "mixed — review the files first"
        : "混合风险，请先查看再选";
    public static string GroupViewFiles => IsEn ? "View its files" : "查看对应文件";
    public static string GroupLocalOnlyTag => IsEn ? "[local rules] " : "［本地规则］";
    public static string GroupsHead(int groups, int candidates, string size) => IsEn
        ? $"{groups} group(s) · {candidates:N0} eligible · {size}"
        : $"{groups} 组 · 符合清理资格 {candidates:N0} 项 · {size}";
    public static string GroupsTruncated(int hidden) => IsEn
        ? $"{hidden} more group(s) merged into Others"
        : $"另有 {hidden} 个小组已并入「其它」";
    /// <summary>发给模型的分组说明：只发汇总数字，不发逐条记录。</summary>
    public static string AiGroupsUserHeader(int groups) => IsEn
        ? $"local groups ({groups}). For EACH group write one line: '#N <one short sentence>'. "
          + "Judge groups separately; never say the whole folder is safe."
        : $"本地分组（{groups} 组）。**每一组**写一行：'#N <一句短建议>'。"
          + "分组要分开判断，绝不能说「整个文件夹都安全」。";
    public static string GroupScopeSampled(int shown, int total) => IsEn
        ? $"analysis covered {shown} of {total} items"
        : $"分析范围：{total} 项中的 {shown} 项";

    /// <summary>组的一行本地摘要。</summary>
    public static string GroupSummaryLine(int items, int candidates, string size, string source) => IsEn
        ? $"{items:N0} item(s) · {candidates:N0} eligible · {size} · {source}"
        : $"{items:N0} 项 · 符合清理资格 {candidates:N0} 项 · 候选空间 {size} · {source}";
    public static string GroupAiTag => IsEn ? "[AI] " : "［AI］";
    public static string GroupProtectedOnly => IsEn
        ? "protected — cannot be cleaned"
        : "受保护，不能清理";
    public static string GroupFilterHint(string what) => IsEn
        ? "will filter the file list to " + what
        : "会在文件列表里过滤到 " + what;

    // ---- AI 结果界面：一句结论 + 一句说明 + 下一步（面向普通用户，不出现技术词） ----
    public static string AiHeadlineClean(int count, string size) => IsEn
        ? $"Suggest cleaning {count:N0} item(s) — about {size} can be freed"
        : $"建议清理其中 {count:N0} 项，可释放 {size}";
    public static string AiHeadlineReview(int count, string size) => IsEn
        ? $"{count:N0} item(s) need your confirmation ({size}) — can't be called safe yet"
        : $"有 {count:N0} 项需要你确认（{size}），暂时不能确定是否安全";



    public static string AiBucketStat(int count, string size) => IsEn
        ? $"{count:N0} item(s) · {size}"
        : $"{count:N0} 项 · {size}";

    public static string AiViewFiles => IsEn ? "View files" : "查看文件";
    public static string AiWhyToggle => IsEn ? "Why this suggestion?" : "为什么这样建议？";
    /// <summary>重新扫描后旧结果过期：不给旧结论，也不给操作。</summary>
    // ---- 详情列表的分组表头 ----
    public static string GroupStatLine(int count, string size) => IsEn
        ? $"{count:N0} item(s) · {size}"
        : $"{count:N0} 项 · {size}";
    public static string GroupSelectedLine(int count, string size) => IsEn
        ? $"selected {count:N0} · {size}"
        : $"已选 {count:N0} · {size}";
    /// <summary>分组标题兜底：中性说法，不用「未标注」这类技术标签。</summary>
    // ---- 文件夹用途识别 ----
    public static string PurposeFromLocal => IsEn ? "local rules" : "本地识别";
    public static string PurposeFromAi => IsEn ? "AI guess" : "AI 推测";
    public static string PurposeFromUser => IsEn ? "you confirmed" : "你确认的";
    public static string PurposeBasisUser => IsEn ? "you set this yourself" : "你自己改的";
    public static string PurposeUnknownCategory => IsEn ? "uncategorised" : "未分类";
    public static string PurposeNoExt => IsEn ? "(no ext)" : "（无扩展名）";
    public static string PurposeGameLibrary => IsEn ? "game library" : "游戏库";
    public static string PurposeCatGame => IsEn ? "games" : "游戏";
    public static string PurposeCatDev => IsEn ? "dev" : "开发";
    public static string PurposeCatSystem => IsEn ? "system" : "系统";
    public static string PurposeDevProject => IsEn ? "dev project" : "开发项目";
    public static string PurposeSystemArea => IsEn ? "system location" : "系统位置";
    public static string PurposeBasisSignature(string what) => IsEn
        ? $"recognised locally as: {what}" : $"本地认出这是：{what}";
    public static string PurposeBasisPath(string what) => IsEn
        ? $"the folder name is {what}" : $"目录名就是 {what}";
    public static string PurposeBasisPlatform => IsEn
        ? "this is a platform game folder — games inside are listed separately"
        : "这是平台的游戏目录，里面的游戏会单独列出";
    public static string PurposeBasisDevMark(string mark) => IsEn
        ? $"found project marker {mark}" : $"发现工程标志 {mark}";
    public static string PurposeBasisSystemEntry => IsEn
        ? "a system folder entry point — not a cleanup suggestion"
        : "系统目录入口，不是清理建议";
    public static string PurposeNeedsConfirm => IsEn ? "needs confirmation" : "待确认";
    public static string PurposeUnrecognized => IsEn ? "not recognised yet" : "未识别";
    public static string PurposeQueued => IsEn ? "queued…" : "排队中…";
    public static string PurposeRunning => IsEn ? "identifying…" : "识别中…";
    public static string PurposeFailed => IsEn ? "identification failed" : "识别失败";
    public static string PurposeCancelled => IsEn ? "stopped" : "已停止";
    public static string PurposeCorrected(string what) => IsEn
        ? $"Purpose set to {what}" : $"用途已改为「{what}」";
    /// <summary>纠正用途的备选类别（点一下即可，不需要输入框）。</summary>
    public static readonly string[] PurposeCorrections =
        { "游戏", "开发", "系统", "影音", "文档", "应用", "资料", "混合", "其它" };
    public static string PurposeIdentify => IsEn ? "Identify purpose" : "识别用途";
    public static string PurposeDeepen => IsEn ? "Look deeper" : "深入识别";
    public static string PurposeCorrect => IsEn ? "Correct" : "纠正";
    public static string PurposeAiHeader => IsEn
        ? "Identify ONE folder's purpose from this bounded summary. Do not read file contents."
        : "根据下面这份有上限的摘要判断**一个**目录的用途。不要读取文件内容。";
    public static string PurposeAiSystem => IsEn
        ? """
          You name what a folder is for, for a non-technical Windows user.

          Reply with exactly these three lines, nothing else. Keep each short (<= 12 words):
          PURPOSE: <what this folder is, e.g. "Steam game library">
          CATEGORY: <one of: games | dev | system | media | documents | apps | mixed | unknown>
          BASIS: <which facts above you used>

          Rules:
          - If the summary is not enough, write PURPOSE: unknown. Do not guess.
          - Never say anything is safe to delete.
          - No markdown, no tool calls, no extra lines.
          """
        : """
          你要为一个不懂电脑的 Windows 用户判断**一个**目录是做什么用的。

          只回下面三行，不要有别的内容。每行尽量短（不超过 12 个字）：
          PURPOSE: <这个目录是什么，例如「Steam 游戏库」>
          CATEGORY: <从这些里选一个：游戏 | 开发 | 系统 | 影音 | 文档 | 应用 | 混合 | 未知>
          BASIS: <你用了上面哪些信息>

          规则：
          - 摘要不够就写 PURPOSE: 未知，不要猜。
          - 绝不要说某样东西可以安全删除。
          - 不要 markdown、不要工具调用、不要多余的行。
          """;

    /// <summary>
    /// Jev 批量用途判定的选项（键 → 说明）。
    ///
    /// 只有 10 类，而且刻意**只留一个「别删」出口**：实测把「用户数据 / 系统文件 /
    /// 开发项目」拆成三类时，边界立刻模糊——视频文件被判成「项目」，Windows 更新
    /// 缓存的置信度从 0.43 掉到「系统 0.84」。合并成一个出口之后，同类测试里
    /// 用户视频 / 程序本体 / 游戏存档全部正确落进 keep，且置信度显著更高。
    ///
    /// 键是稳定标识（进缓存键），**不要改**；要改说明就改文本。
    /// </summary>
    public static (string Key, string Text)[] AiPurposeOptions => IsEn
        ? new[]
        {
            ("temp",        "Temporary files — apps throw these away when done"),
            ("browsercache","Browser cache"),
            ("appcache",    "App cache — the app rebuilds it by itself"),
            ("devcache",    "Component cache downloaded by a developer tool"),
            ("applog",      "Application logs"),
            ("dump",        "Crash dumps"),
            ("installer",   "Installer you downloaded"),
            ("model",       "Large asset such as an AI model or game data — deleting means downloading it again"),
            // keep 是**兜底**，不是「用户数据」这一个窄类。写窄了模型就会往缓存类上猜 ——
            // 实测：窄 keep 时 LocalLow 被猜成 temp 0.29、VS Code 的 Roaming\Code 被猜成
            // devcache 0.27（都是错的），而 AppData\Local\Programs 猜 keep 0.39 被阈值挡掉、
            // 显示成「未识别」——那可是装软件的地方。放宽之后 24 条里采纳数 16 → 22。
            ("keep",        "别删：你自己的数据（文档/照片/视频/存档/聊天记录/密钥），或者某个软件、系统自己的数据目录（它自己的设置、账号、插件、数据库、模型），再或者你判断不出它是谁的数据、但明显不是临时垃圾"),
            ("unknown",     "只有在连「这是谁的数据」都判断不出来时才选这个"),
        }
        : new[]
        {
            ("temp",        "临时文件，程序用完就丢"),
            ("browsercache","浏览器缓存"),
            ("appcache",    "软件缓存，软件会自己重建"),
            ("devcache",    "开发工具下载的组件缓存"),
            ("applog",      "软件日志"),
            ("dump",        "崩溃转储"),
            ("installer",   "安装包"),
            ("model",       "AI 模型或游戏素材这种大文件，删了要重新下载"),
            // 同上：keep 必须是兜底，不能只是「用户数据」这一个窄类
            ("keep",        "别删：你自己的数据（文档/照片/视频/存档/聊天记录/密钥），或者某个软件、系统自己的数据目录（它自己的设置、账号、插件、数据库、模型），再或者你判断不出它是谁的数据、但明显不是临时垃圾"),
            ("unknown",     "只有在连「这是谁的数据」都判断不出来时才选这个"),
        };

    /// <summary>
    /// 每道题都把路径写进问题本身。
    /// <b>不要改成「第 N 行」</b> —— 实测那会让模型去数行号，48 项时 8/16 自相矛盾，
    /// 而且后面整片塌成同一个答案。
    /// </summary>
    public static string AiPurposeQuestion(string path)
        => IsEn ? $"What is this path: {path}" : $"这个路径是什么：{path}";
    /// <summary>批量判定的结果写进「说明」列时，必须一眼看出这是 AI 推测、不是规则结论。</summary>
    public static string AiPurposeNote(string name, string impact)
        => IsEn ? $"{PurposeFromAi}: {name} · {impact}" : $"{PurposeFromAi}：{name} · {impact}";
    public static string AiPurposeFromAi(string text)
        => IsEn ? $"{PurposeFromAi}: {text}" : $"{PurposeFromAi}：{text}";
    /// <summary>
    /// 类别短名。整理页的「用途」是自由文本（不是一个枚举），所以判定结果要落成一句能看的名词。
    /// 返回空串表示这一类不该写进用途（只有 Unknown）。
    /// </summary>
    public static string AiPurposeDisplayName(AiPurposeKind kind) => kind switch
    {
        AiPurposeKind.Temporary => IsEn ? "Temporary files" : "临时文件",
        AiPurposeKind.BrowserCache => IsEn ? "Browser cache" : "浏览器缓存",
        AiPurposeKind.AppCache => IsEn ? "App cache" : "软件缓存",
        AiPurposeKind.DevCache => IsEn ? "Dev tool cache" : "开发工具缓存",
        AiPurposeKind.AppLog => IsEn ? "App logs" : "软件日志",
        AiPurposeKind.Dump => IsEn ? "Crash dumps" : "崩溃转储",
        AiPurposeKind.Installer => IsEn ? "Installers" : "安装包",
        AiPurposeKind.Model => IsEn ? "Models / game assets" : "模型 / 游戏素材",
        // 说得肯定一点：这个功能本来就是在帮用户判断，一句「像是…」等于没主见。
        // 不确定性交给「AI 推测」那个前缀去表达，不重复在名称里泄气。
        AiPurposeKind.Keep => IsEn ? "Your data or the app's own data" : "你的数据或软件自己的数据",
        _ => "",
    };
    /// <summary>模型认出这是「别删」那一类时给的提示。只是提示，不阻挡用户删。</summary>
    public static string AiPurposeKeepHint => IsEn
        ? "Looks like your own data or a Windows file — check before deleting"
        : "看着像你自己的数据或系统文件，删之前先看一眼";
    /// <summary>模型认出这是模型 / 素材这类大件时给的提示。</summary>
    public static string AiPurposeModelHint => IsEn
        ? "Looks like a model or game asset — deleting means downloading it again"
        : "看着像模型或游戏素材这类大件，删了要重新下载";
    public static string AiPurposeBatchRunning => IsEn ? "Classifying…" : "正在归类…";
    public static string AiPurposeBatchProgress(int done, int total)
        => IsEn ? $"Classified {done:N0} / {total:N0}" : $"已归类 {done:N0} / {total:N0}";
    /// <summary>
    /// 结果短句。**必须把「模型说不出」和「模型说了但没把握」分开报** ——
    /// 两者在界面上都显示成「未识别」，混成一句用户根本没法判断问题出在哪：
    /// 前者要改提示词，后者只要放宽阈值。
    /// </summary>
    public static string AiPurposeBatchSummary(int applied, int unsure, int unknown, int calls, double cost)
        => IsEn
            ? $"Classified {applied:N0} folder(s); {unsure:N0} guessed but not confident; {unknown:N0} it couldn't tell. {calls} request(s), about ${cost:0.0000}."
            : $"认出了 {applied:N0} 个；{unsure:N0} 个有猜测但没把握；{unknown:N0} 个它说不出来。共 {calls} 次请求，约 ${cost:0.0000}。";
    public static string AiPurposeBatchNotConfigured => IsEn
        ? "Batch classification needs a provider with the “structured decision” protocol. Add one in Settings."
        : "批量归类需要一条「结构化判定」协议的供应商，请先在设置里添加。";
    public static string AiPurposeBatchNothing => IsEn
        ? "Nothing left to classify."
        : "没有需要归类的条目了。";
    public static string AiPurposeBatchFailed(string why)
        => IsEn ? "Classification failed: " + why : "归类失败：" + why;
    /// <summary>Jev 判定用途时的系统提示词。整批一次问完，不要串行追问。</summary>
    public static string AiPurposeBatchSystem => IsEn
        ? """
          You are given a list of Windows paths, one per line, numbered from the top.
          Each question asks what ONE numbered path is.

          Pick the single best option. Do not explain. Do not say anything is safe to delete.
          If the path is not enough to tell, pick "unknown" instead of guessing.
          """
        : """
          下面给你一串 Windows 路径，一行一个，从上往下编号。
          每个问题问的是其中某一个编号的路径是什么。

          从选项里挑最合适的那一个。不要解释，不要判断能不能删。
          光看路径判断不出来就选 unknown，不要猜。
          """;
    public static string OtherFilesTitle => IsEn ? "Other files" : "其它文件";

    // ---- 文件夹整理（工作区，不是说明页） ----
    public static string OrganizeColName => IsEn ? "Folder" : "名称";
    public static string OrganizeColPurpose => IsEn ? "What it is" : "用途";
    public static string OrganizeColSize => IsEn ? "Size" : "容量";
    public static string OrganizeColAction => IsEn ? "Actions" : "操作";

    public static string OrganizeIntro => IsEn
        ? "Locally decidable folders are already named. Only what stays unknown needs AI."
        : "本地能判断的已经认出来了；剩下看不出来的才需要 AI。";
    public static string OrganizeTotals(int objects, long bytes) => IsEn
        ? $"{objects:N0} folders · {FileEntry.FormatSize(bytes)}"
        : $"{objects:N0} 个文件夹 · {FileEntry.FormatSize(bytes)}";

    public static string OrganizeIdentifyAll => IsEn ? "Identify these folders" : "识别这些文件夹";
    public static string OrganizeIdentifyScope(int count) => IsEn
        ? $"scope: the {count:N0} folders still unknown on this page"
        : $"范围：本页还没认出来的 {count:N0} 个文件夹";
    public static string OrganizeBudget(int requests) => IsEn
        ? $"at most {requests:N0} AI requests"
        : $"最多发 {requests:N0} 次 AI 请求";
    public static string OrganizeProgress(int done, int total, int used, int budget) => IsEn
        ? $"{done:N0}/{total:N0} · AI requests {used:N0}/{budget:N0}"
        : $"{done:N0}/{total:N0} · AI 请求 {used:N0}/{budget:N0}";
    public static string OrganizeStop => IsEn ? "Stop identifying" : "取消识别";
    public static string OrganizeRetryHint => IsEn
        ? "failed — you can identify it again"
        : "识别失败，可以重试";
    public static string OrganizeWaitNoResult => IsEn
        ? "waiting — no result yet"
        : "处理中，还没有结论";

    public static string OrganizeFilterAll => IsEn ? "All" : "全部";
    // 「未识别」筛选胶囊已从页头移除：用户看不到这个入口，所以这两个文案不再需要。
    // 视图筛选本身还在（只是没有可见入口），它的统计行文案仍由 OrganizeFilterActive 提供。
    public static string OrganizeFilterActive(int shown, int total) => IsEn
        ? $"showing {shown:N0} of {total:N0}"
        : $"只显示 {shown:N0} / {total:N0} 个";

    // 「全选」按钮的悬停说明：说清作用范围（当前列出来的行）与再点一次会发生什么。
    // **不写进按钮文案** —— 按钮永远只叫「全选」，不改名成「取消全选」。
    public static string OrganizeSelectAllTip => IsEn
        ? "Tick every folder currently listed (click again to clear those ticks)"
        : "勾选当前列出来的全部文件夹（再点一次清空这些勾选）";

    public static string OrganizeNoScan => IsEn ? "Not scanned yet" : "还没有扫描结果";
    public static string OrganizeNoScanBody => IsEn
        ? "Scan the disk first — folder objects are organised from the scan result. Nothing is moved, renamed or deleted."
        : "先扫描磁盘，整理结果直接从扫描结果里来。整理只读取结构，不移动、不改名、不删除任何文件。";
    public static string OrganizeScanning => IsEn ? "Scanning…" : "正在扫描…";
    public static string OrganizeScanningBody => IsEn
        ? "As soon as the scan finishes the folder objects show up here."
        : "扫描一结束，这里的文件夹对象就会直接出现。";
    public static string OrganizeEmpty => IsEn ? "Nothing to organise" : "没有可整理的文件夹";
    public static string OrganizeEmptyBody => IsEn
        ? "The scan found no sub-folders under this drive."
        : "这次扫描在这个盘下没有找到子文件夹。";
    public static string OrganizeFailed => IsEn ? "Organising failed" : "整理结果生成失败";
    public static string OrganizeFailedBody => IsEn
        ? "The scan result could not be turned into folder objects. Rescan to try again."
        : "扫描结果没能整理成文件夹对象。重新扫描一次即可。";
    public static string OrganizeNoModel => IsEn ? "No AI model configured" : "还没配置 AI 模型";
    public static string OrganizeNoModelBody => IsEn
        ? "These folders cannot be decided locally. Configure a model in Settings and then identify them — nothing is sent until you ask."
        : "这些文件夹本地判断不了。到设置里配好模型再点识别；你不点，就不会发出任何数据。";
    public static string OrganizeOpenSettings => IsEn ? "Open settings" : "打开设置";
    public static string OrganizeAllDone => IsEn ? "Everything on this page is identified" : "本页都识别过了";
    public static string OrganizeAllDoneBody => IsEn
        ? "Nothing left to identify. Expand a folder if you want to look inside."
        : "没有待识别的对象了。想往下看就展开某个文件夹。";

    public static string OrganizeExpand => IsEn ? "Show sub-folders" : "展开子文件夹";
    public static string OrganizeCollapse => IsEn ? "Collapse" : "收起";
    public static string OrganizeDeepen => IsEn ? "Identify inside" : "深入识别";
    public static string OrganizeIdentifyOne => IsEn ? "Identify" : "识别";
    public static string OrganizeOpen => IsEn ? "Open in Explorer" : "在资源管理器中打开";
    public static string OrganizeCorrect => IsEn ? "Correct purpose" : "纠正用途";
    public static string OrganizeCopyPath => IsEn ? "Copy full path" : "复制完整路径";
    public static string OrganizePaused => IsEn ? "Stopped the inside-by-inside pass" : "已停止逐个分类";
    public static string OrganizeChildBudgetNote(int shown, int total) => IsEn
        ? $"{shown:N0} of {total:N0} sub-folders shown"
        : $"只列出 {shown:N0} / {total:N0} 个子文件夹";
    public static string OrganizeEntryPointTag => IsEn ? "system entry" : "系统入口";
    public static string OrganizePlatformTag => IsEn ? "games live inside" : "里面有游戏";

    // ---- 展开状态：为什么这一行没有箭头 / 为什么写未知（不是 0 KB） ----
    /// <summary>链接 / 重解析点：本次扫描没有进去，占用与内容都未知。</summary>
    public static string OrganizeSizeNotScanned => IsEn ? "unknown (not scanned)" : "未知（未扫描）";
    /// <summary>空目录：只敢说「这次扫描里没有内容」，不拿 0 KB 暗示。</summary>
    public static string OrganizeSizeEmpty => IsEn ? "no content found" : "扫描无内容";
    public static string OrganizeStateNotScanned => IsEn ? "not scanned (link)" : "未扫描（链接）";
    public static string OrganizeStateFilesOnly(int files) => IsEn
        ? $"files only · {files:N0}"
        : $"只有文件 · {files:N0}";
    public static string OrganizeStateEmpty => IsEn ? "empty" : "空";
    public static string OrganizeTipNotScanned => IsEn
        ? "This is a link or reparse point. The scan did not follow it, so its size and contents are unknown — not zero."
        : "这是链接或系统重解析点。本次扫描没有跟进去，所以内容和占用都是未知，不是 0。";
    public static string OrganizeTipFilesOnly => IsEn
        ? "No sub-folders here, so there is nothing to expand. Use the file button to list its files."
        : "这一层没有子文件夹，所以没有可展开的箭头。用文件按钮可以看它里面的文件。";
    public static string OrganizeTipEmpty => IsEn
        ? "Nothing was found inside this folder in this scan. That is not the same as a link that was skipped."
        : "本次扫描在这个文件夹里没有找到内容。这和「链接被跳过」不是一回事。";
    public static string OrganizeFilesCount(int shown, int total) => IsEn
        ? $"{shown:N0} of {total:N0} file(s), largest first"
        : $"共 {total:N0} 个文件，按占用列出前 {shown:N0} 个";
    public static string OrganizeFilesMore(int hidden) => IsEn
        ? $"{hidden:N0} more — open the folder to see all"
        : $"另有 {hidden:N0} 个，打开文件夹看全部";
    public static string OrganizeFilesScope => IsEn
        ? "Read-only list of this folder's own files. The organize page never deletes anything."
        : "这里只列这个文件夹自己的文件，只读。整理页不做任何删除。";
    public static string OrganizeSkippedLinks(int n) => IsEn
        ? $"{n:N0} links / groups skipped (not followed)"
        : $"跳过 {n:N0} 个链接或散文件组（不跟随）";
    public static string OrganizeSkippedNoAccess(int n) => IsEn
        ? $"{n:N0} folders skipped: no permission or not scanned"
        : $"权限不足或没扫到的目录 {n:N0} 个已跳过（不自动提权）";
    public static string OrganizeNotElevated => IsEn
        ? "permission problems are recorded and skipped — the app never asks for elevation on its own"
        : "权限不足只记录并跳过，不会自动提权";

    public static string OrganizeResultNote(int ai, int local) => IsEn
        ? $"named {local:N0} locally, {ai:N0} by AI"
        : $"本地认出 {local:N0} 个，AI 推测 {ai:N0} 个";
    public static string OrganizeBudgetUsed(int used) => IsEn
        ? $"stopped at the request budget ({used:N0}) — the rest stays to confirm"
        : $"已到本次请求上限（{used:N0} 次），剩下的保持待确认";
    public static string OrganizePurposeUnknownAi => IsEn
        ? "the model did not give a usable answer"
        : "模型没有给出可用的结论";

    // ---- 自动识别（两级）与「识别当前文件夹」（三级及更深） ----

    /// <summary>片段说明也写进提示词里，让模型知道自己看的是什么。</summary>
    public static string PurposeAiSnippetHeader => IsEn
        ? "Filtered snippets (README / project config / manifests only; secrets, tokens and accounts are masked):"
        : "经过筛选的片段（只有 README / 项目配置 / 清单文件；密钥、Token、账号已脱敏）：";

    /// <summary>范围声明：送出前给用户看的口径，和实际发出去的内容同源。</summary>
    public static string OrganizeSendNote => IsEn
        ? "Only folder name, bounded structure summary, file-type mix, a few sample names and filtered README/config snippets are sent. No full path and no full file list. Secrets, tokens and account names are masked."
        : "只发送目录名、受控的结构摘要、文件类型分布、少量代表文件名，以及经过筛选的 README/配置片段；不发送完整路径，也不上传完整文件清单。密钥、Token、账号等已脱敏。";

    public static string OrganizeAutoStart(int count, int budget) => IsEn
        ? $"Scan finished — identifying the {count:N0} folders on levels 1-2 automatically (at most {budget:N0} AI requests)"
        : $"扫描完成，正在自动识别一、二级共 {count:N0} 个文件夹（最多发 {budget:N0} 次 AI 请求）";

    public static string OrganizeAutoDone(int done, int pending, int failed, int used, int budget) => IsEn
        ? $"auto pass finished · handled {done:N0} · waiting {pending:N0} · failed {failed:N0} · AI requests {used:N0}/{budget:N0}"
        : $"自动识别结束 · 已处理 {done:N0} · 待处理 {pending:N0} · 失败 {failed:N0} · AI 请求 {used:N0}/{budget:N0}";

    public static string OrganizeCounts(int resolved, int pending, int failed) => IsEn
        ? $"named {resolved:N0} · to confirm {pending:N0} · failed {failed:N0}"
        : $"已认出 {resolved:N0} · 待确认 {pending:N0} · 失败 {failed:N0}";

    public static string OrganizeRetryPending => IsEn ? "Retry the rest" : "重试待确认 / 失败";
    public static string OrganizeRetryPendingTip => IsEn
        ? "Run the whole pass again for folders that are still unknown or failed (same request budget)"
        : "对还没认出来或失败的文件夹再跑一遍（仍然受同一个请求上限约束）";
    public static string OrganizeDeepOnly => IsEn
        ? "level 3 and deeper are never identified automatically — open a folder and use the button below"
        : "三级及更深不会自动识别；进入具体文件夹后用下面的按钮";

    /// <summary>层级标注（只进悬停，不占主视觉）。</summary>
    public static string OrganizeLevelAuto(int level) => IsEn
        ? $"level {level} · identified automatically"
        : $"第 {level} 级 · 自动识别范围";
    public static string OrganizeLevelDeep(int level) => IsEn
        ? $"level {level} · identify on demand only"
        : $"第 {level} 级 · 只在点「识别当前文件夹」时处理";

    /// <summary>没列出来的子目录：如实说出「已列出 X / 共 Y / 还有 N 个未列出（未识别）」。</summary>
    public static string OrganizeUnlistedNote(int shown, int total, int unlisted) => IsEn
        ? $"listed {shown:N0} of {total:N0} · {unlisted:N0} not listed (not identified)"
        : $"已列出 {shown:N0} / 共 {total:N0} · 还有 {unlisted:N0} 个未列出（未识别）";

    /// <summary>页头汇总：还有多少条目根本没列出来。</summary>
    public static string OrganizeUnlistedTotal(int unlisted) => IsEn
        ? $"{unlisted:N0} folders are not listed (and not identified) — expand a folder or identify that level"
        : $"还有 {unlisted:N0} 个文件夹没有列出（也未识别）——展开或点「识别当前文件夹」再处理";

    /// <summary>失败原因分开报：网络不通 / 超时 都不能说成成功。</summary>

    public static string OrganizeWorkTitleFixed(string name, int level) => IsEn
        ? $"Working folder: {name} · level {level}"
        : $"当前文件夹：{name} · 第 {level} 级";
    public static string OrganizeWorkScope(int children, int budget) => IsEn
        ? $"this run only looks at the {children:N0} direct sub-folders of this folder · up to {budget:N0} AI requests · it does not go deeper on its own"
        : $"本次只分析这一层的 {children:N0} 个直接子文件夹 · 最多发 {budget:N0} 次请求 · 不会自己继续深入更深目录";
    public static string OrganizeIdentifyCurrent => IsEn ? "Identify this folder" : "识别当前文件夹";
    public static string OrganizeIdentifyCurrentTip => IsEn
        ? "Analyse only the direct sub-folders of the selected folder. Nothing else is sent."
        : "只分析选中文件夹的直接子文件夹，不会扩大到别的地方。";
    public static string OrganizeWorkNone => IsEn
        ? "Expand or click a folder to work on that level"
        : "展开或点选一个文件夹，就能在这一层做识别";
    public static string OrganizeWorkPathHidden => IsEn
        ? "(full path shown on hover)"
        : "（完整路径在悬停里看）";

    /// <summary>已知对象的内部目录（名字判定，不读内容）。</summary>
    public static string PurposeKnownInternal => IsEn ? "inside an app/project" : "程序内部目录";
    public static string PurposeBasisKnownLeaf(string name) => IsEn
        ? $"known internal folder name: {name}" : $"已知的内部目录名：{name}";

    // ---- 系统语义（由 Environment.SpecialFolder 解析，不硬编码盘符/用户名） ----

    public static string PurposeUserFiles => IsEn ? "user files" : "用户文件";
    public static string SysWindows => IsEn ? "Windows operating system files" : "Windows 操作系统文件";
    public static string SysProgramFiles => IsEn ? "program installation folder" : "程序安装目录";
    public static string SysProgramFilesX86 => IsEn ? "32-bit program installation folder" : "32 位程序安装目录";
    public static string SysProgramData => IsEn ? "shared program data" : "共享程序数据";
    public static string SysUserProfile => IsEn ? "user files and app data" : "用户文件和应用数据";
    public static string SysUsersContainer => IsEn ? "user folders" : "用户目录";
    public static string SysAppData => IsEn ? "user app data" : "用户应用数据";
    public static string SysLocalAppData => IsEn ? "local app data" : "本地应用数据";
    public static string SysRoamingAppData => IsEn ? "roaming app data" : "漫游应用数据";
    public static string SysDocuments => IsEn ? "documents" : "文档";
    public static string SysDownloads => IsEn ? "downloaded files" : "下载文件";

    public static string SysBasisWindows => IsEn
        ? "this is the system folder resolved by Windows itself" : "这是系统自己解析出来的 Windows 目录";
    public static string SysBasisProgramFiles => IsEn
        ? "this is the program installation folder resolved by Windows itself"
        : "这是系统自己解析出来的程序安装目录";
    public static string SysBasisProgramData => IsEn
        ? "this is the shared program data folder resolved by Windows itself"
        : "这是系统自己解析出来的共享程序数据目录";
    public static string SysBasisUserProfile => IsEn
        ? "this is your user folder resolved by Windows itself"
        : "这是系统自己解析出来的用户目录";
    public static string SysBasisUsersContainer => IsEn
        ? "this is the folder where Windows keeps every user's home folder"
        : "这是系统存放各用户主目录的位置";
    public static string SysBasisAppData => IsEn
        ? "this is the per-user app data folder (parent of Local and Roaming)"
        : "这是用户应用数据目录（Local 与 Roaming 的上一级）";
    public static string SysBasisLocalAppData => IsEn
        ? "this is the per-user local app data folder resolved by Windows itself"
        : "这是系统自己解析出来的本地应用数据目录";
    public static string SysBasisRoamingAppData => IsEn
        ? "this is the per-user roaming app data folder resolved by Windows itself"
        : "这是系统自己解析出来的漫游应用数据目录";
    public static string SysBasisDocuments => IsEn
        ? "this is the documents folder resolved by Windows itself"
        : "这是系统自己解析出来的文档目录";
    public static string SysBasisDownloads => IsEn
        ? "this is your download folder (user folder + Downloads)" : "这是用户目录下的下载文件夹";

    // ---- 行内详情（点击用途展开；只用真实摘要证据，不编造） ----

    public static string OrganizeDetailWhat => IsEn ? "What it is: " : "这是什么：";
    public static string OrganizeDetailWhy => IsEn ? "Why: " : "为什么这么判断：";
    public static string OrganizeDetailKind(int directFolders, int files, string types) => IsEn
        ? $"{directFolders:N0} sub-folders · {files:N0} files"
          + (types.Length > 0 ? $" · types: {types}" : "")
        : $"{directFolders:N0} 个子文件夹 · {files:N0} 个文件"
          + (types.Length > 0 ? $" · 类型：{types}" : "");
    public static string OrganizeDetailSamples(string names) => IsEn
        ? $"largest files: {names}" : $"最大的文件：{names}";
    public static string OrganizeDetailSource(string source) => IsEn
        ? $"Judged by: {source}" : $"判断来源：{source}";
    public static string OrganizeDetailNoEvidence => IsEn
        ? "not enough evidence — this folder stays unknown" : "证据不足，这个文件夹保持未知";
    public static string OrganizeDetailTip => IsEn
        ? "Click to see what this is and why" : "点一下看它是什么、为什么这么判断";
    public static string OrganizeDetailHide => IsEn
        ? "Click to hide the explanation" : "点一下收起说明";

    // ---- 进度 / 终态（顶栏短句 + 详情分开，不把请求数和文件夹数混在一行） ----

    public static string OrganizeRunRunning => IsEn ? "identifying…" : "识别中…";
    public static string OrganizeRunDone => IsEn ? "identification finished" : "识别完成";
    public static string OrganizeRunIncomplete => IsEn ? "identification not finished" : "识别未完成";
    public static string OrganizeRunCanceled => IsEn ? "identification stopped" : "识别已取消";
    public static string OrganizeRunSuperseded => IsEn
        ? "identification not finished (the list was rebuilt)"
        : "识别未完成（列表已重建）";

    /// <summary>
    /// 分档计数：本地认出 / AI 有结论 / 未知 / 失败。
    /// **不把这几类混成一句「待确认」**。
    /// </summary>
    /// 最后一个标签必须是「**还没识别**」而不是「未知」：它数的是**从没被识别过**的，
    /// 不是「AI 看了但看不出来」。用「未知」会让用户以为 AI 大面积失灵 ——
    /// 真机上就发生过：页头写「未知 2,517」，而实际只是还没轮到它们。
    /// <summary>分类结果面板：没有结论的那一桶。**没有勾选框** —— 它没有结论，不是一类东西。</summary>
    public static string OrganizeBucketUnnamed => IsEn ? "Not identified yet" : "还没认出来";


    /// <summary>
    /// 分类结果面板的抬头。**说是「AI 认出来的」** —— 本地已经认出来的那些不进这个面板
    /// （它们本来就在列表里有自己的标签），不写清楚会让人以为面板漏了东西。
    /// 同时说清楚「勾一类就整片处理」，用户才知道这一行能点。
    /// </summary>
    public static string OrganizeResultTitle(int classes, int identified) => IsEn
        ? $"AI identified {identified:N0} item(s) in {classes:N0} group(s) — tick a group to handle it as a whole"
        : $"AI 认出了 {identified:N0} 项，分成 {classes:N0} 类 · 勾一类就整片处理";

    public static string OrganizeCountsLine(int local, int ai, int unknown, int failed) => IsEn
        ? $"local {local:N0} · AI {ai:N0} · not looked at yet {unknown:N0} · failed {failed:N0}"
        : $"本地认出 {local:N0} · AI 认了 {ai:N0} · 还没识别 {unknown:N0} · 失败 {failed:N0}";

    /// <summary>
    /// 终态短句。**只说页头说不出的事**（未发送 / 失败），
    /// 不再重复「本地/AI/未知」——页头已经用同一批标签报了全局口径，
    /// 两套不同口径的同名标签拼在一行会让人读成矛盾。
    /// </summary>
    public static string OrganizeRunSummary(string head, int failed, int notSent)
    {
        var parts = new List<string>();
        if (failed > 0) parts.Add(IsEn ? $"failed {failed:N0}" : $"失败 {failed:N0}");
        if (notSent > 0) parts.Add(IsEn ? $"not sent {notSent:N0}" : $"未发送 {notSent:N0}");
        string tail = parts.Count > 0
            ? string.Join(" · ", parts)
            : (IsEn ? "everything was answered" : "都跑完了");
        return head + " · " + tail;
    }

    /// <summary>这一遍识别的明细（放 Tooltip，不占顶栏那一行）。</summary>
    public static string OrganizeRunPassDetail(int aiNamed, int unknown, int failed) => IsEn
        ? $"this pass: AI answered {aiNamed:N0} · unknown {unknown:N0} · failed {failed:N0}"
        : $"这一遍：AI 有结论 {aiNamed:N0} · 未知 {unknown:N0} · 失败 {failed:N0}";

    /// <summary>没配模型时如实说明：保持「未识别」，不算识别过。</summary>
    public static string OrganizeNoModelHonest(int pending) => IsEn
        ? $"{pending:N0} folders need a model and none is configured — they stay unidentified."
        : $"有 {pending:N0} 个文件夹需要模型判断，但还没配置模型；它们保持「未识别」，不算识别过。";
    /// <summary>请求预算用完：剩下的叫「未发送」，不是「待确认」。</summary>
    public static string OrganizeBudgetLeft(int notSent) => IsEn
        ? $"{notSent:N0} folders were never sent (this pass ran out of its AI request budget)"
        : $"还有 {notSent:N0} 个文件夹没有发送（本次 AI 请求预算用完，它们没有被问过）";

    /// <summary>问过多少次（≠ 成功数）。</summary>
    public static string OrganizeRunAttempts(int attempted, int total) => IsEn
        ? $"{attempted:N0} of {total:N0} folders were sent to the model (a request is not a success)"
        : $"实际问过模型 {attempted:N0} / {total:N0} 个（发过请求 ≠ 识别成功）";

    /// <summary>请求预算单独一行（放 Tooltip / 详情，不和文件夹数混在一行）。</summary>
    public static string OrganizeRunRequests(int used, int budget) => IsEn
        ? $"AI requests {used:N0}/{budget:N0}" : $"AI 请求 {used:N0}/{budget:N0}";

    /// <summary>深层工作条：明确「只识别本层的直接子目录」，不是分析这个目录本身。</summary>
    public static string OrganizeIdentifyThisLevel => IsEn ? "Identify folders on this level" : "识别本层文件夹";

    // ---- 展开反馈（点箭头不能没有反应，也不能假装成功） ----
    public static string OrganizeExpandBudgetReached => IsEn
        ? "not expanded: too many folders in this pass — rescan to try again"
        : "没能展开：这次整理的对象数量已达上限，重新扫描后再试";
    public static string OrganizeExpandAlreadyListed => IsEn
        ? "nothing to expand: its sub-folders are already listed on the first level"
        : "没什么可展开的：它的子文件夹已经在首屏那一级列出来了";
    public static string OrganizeExpandNoChildren => IsEn
        ? "nothing to expand: this folder has no sub-folders"
        : "没什么可展开的：这个文件夹里没有子文件夹";


    public static string OrganizeThisLevelOnly => IsEn
        ? "only the direct sub-folders of this folder are identified — not the folder itself, and it does not go deeper"
        : "只识别这个文件夹的直接子文件夹（不分析这个文件夹本身，也不会继续深入更深目录）";
    public static string OrganizeWorkBarScope(int children, int budget) => IsEn
        ? $"direct sub-folders: {children:N0} · at most {budget:N0} AI requests this time"
        : $"直接子文件夹 {children:N0} 个 · 本次最多发 {budget:N0} 次请求";

    public static string AiFilteredToCount(int n) => IsEn
        ? $"Showing only these {n:N0} item(s)"
        : $"只显示这 {n:N0} 项";
    public static string DetailShowingAll => IsEn ? "Showing all items" : "显示全部";
    public static string AiEverythingSelected => IsEn ? "All of these are already selected" : "这些都已经选好了";

    public static string AiNoteBelongs(string what) => IsEn ? $"These are {what}" : $"这些文件属于{what}";

    public static string AiWhyLocation(string where) => IsEn ? $"Located in {where}" : $"文件位于 {where}";
    public static string AiWhyModified(string age) => IsEn ? $"Last changed {age}" : $"最后修改于{age}";
    public static string AiWhyMatched(string tech) => IsEn ? $"Matched rule: {tech}" : $"匹配到的规则：{tech}";
    public static string AiWhySomeUncertain(int n) => IsEn
        ? $"{n:N0} item(s) still cannot be confirmed"
        : $"仍有 {n:N0} 项无法确认";
    public static string AiWhyProtected(int n) => IsEn
        ? $"{n:N0} item(s) are protected and never offered"
        : $"{n:N0} 项受保护，不会进入清理";

    public static string AiAgeDays(int d) => IsEn ? $"{d} day(s) ago" : $"{d} 天前";
    public static string AiAgeMonths(int m) => IsEn ? $"{m} month(s) ago" : $"{m} 个月前";
    public static string AiAgeYears(int y) => IsEn ? $"{y} year(s) ago" : $"{y} 年前";

    public static string GroupViewNeedsLocation => IsEn
        ? "Cannot locate the folder for this group"
        : "找不到这一组对应的位置";
    public static string GroupFilterApplied(string what, int n) => IsEn
        ? $"Filtered to {n:N0} item(s) under {what}"
        : $"已过滤到 {what} 下的 {n:N0} 项";
    public static string GroupsPanelTitle => IsEn
        ? "Pick by group (split locally)"
        : "按组挑（本地拆分）";
    public static string GroupsPanelHint => IsEn
        ? "Counts and eligibility come from local rules. AI only adds a note."
        : "数量和清理资格来自本地规则，AI 只补充说明。";
    public static string SelectAdded(int count, string size) => IsEn
        ? $"Added {count:N0} item(s) · {size}"
        : $"已新增选择 {count:N0} 项 · {size}";
    public static string SelectNothingNew => IsEn ? "Nothing new to select in this group" : "这一组没有新的可选项";
    public static string SelectBlockedByRule => IsEn
        ? "This group needs your review — open its files first"
        : "这一组需要你先看过文件才能选";

    // ---- 页头导航 ----
    public static string BackToCleanCenter => IsEn ? "Back to clean center" : "返回清理中心";
    public static string BackToLocations => IsEn ? "Back to locations" : "返回位置列表";
    /// <summary>
    /// 首页第二行：这一页的候选规模。
    /// 用「候选空间」而不是「预计处理空间」—— 用户还没选任何东西，
    /// 说「预计处理」会让人以为程序要动这些文件。
    /// </summary>
    public static string CandidateScopeLine(int locations, string size) => IsEn
        ? $"{locations:N0} location(s) · {size} of candidates"
        : $"{locations:N0} 个位置 · 候选空间 {size}";
    /// <summary>「看不出用途」是刻意写的：启发式只认得出体积/位置，认不出这是什么。</summary>
    public static string ReasonUnknown => IsEn ? "can't tell what it is" : "看不出用途";
    public static string ReasonTempDir => IsEn ? $"In a temp folder · {ReasonUnknown}" : $"在临时目录里 · {ReasonUnknown}";
    public static string ReasonTempExt => IsEn ? $"Temp / log leftover · {ReasonUnknown}" : $"临时或日志残留 · {ReasonUnknown}";
    public static string ReasonDump => IsEn
        ? "Leftover log from a program crash"
        : "程序崩溃时留下的记录文件";
    public static string ReasonWinUpdate => IsEn
        ? "Temp files for Windows updates; updates already installed are unaffected"
        : "Windows 更新下载留下的临时文件；已安装的更新文件不在这个目录里";
    public static string ReasonInstaller => IsEn
        ? "Installer you downloaded, probably already used"
        : "你下载的安装包，装完通常就没用了";
    public static string ReasonOldInstaller => IsEn
        ? $"Untouched for months · installer or disk image, {ReasonUnknown}"
        : $"很久没动过 · 安装包或镜像文件，{ReasonUnknown}";
    public static string ReasonVmDisk => IsEn
        ? "Careful · this is a virtual machine's disk, real data lives inside"
        : "要小心 · 这是虚拟机的磁盘，里面的数据是真的";
    public static string AskAiFolder => IsEn ? "Ask AI what this is" : "问 AI 这是什么";
    public static string ReasonRecycle => IsEn
        ? "Things you deleted earlier — clearing these loses them for good"
        : "你之前删掉的东西，清掉就真没了";
    public static string ReasonRecycleNamed(string name) => IsEn
        ? $"“{name}”, which you deleted earlier — clearing it loses it for good"
        : $"你之前删掉的「{name}」，清掉就真没了";
    public static string ReasonLarge => IsEn ? $"Large file · {ReasonUnknown}" : $"大文件 · {ReasonUnknown}";
    public static string ReasonOld(string age) => IsEn ? $"Untouched for {age} · {ReasonUnknown}" : $"已 {age} 未改 · {ReasonUnknown}";
    public static string ReasonEmpty => IsEn ? "Empty folder" : "里面什么都没有的空文件夹";
    public static string ReasonBroken(string target) =>
        IsEn ? "Shortcut points at something that no longer exists: " + target
             : "快捷方式指向的东西已经不存在了：" + target;
    public static string ReasonLong(int n) => IsEn
        ? $"Path is {n} characters · some programs can't open it"
        : $"路径有 {n} 个字 · 有些程序打不开它";
    public static string ReasonDupKeep => IsEn
        ? "Keep this one · identical to another file, but has the shortest path"
        : "和另一个文件内容完全一样，这个路径最短，留它";
    public static string ReasonDupExtra(string keep) =>
        IsEn ? "Byte-for-byte identical to " + keep
             : "和这个文件内容一模一样：" + keep;

    // ---- 重复检测没跑完时的明确标注（不能让用户以为已经查全了）----
    public static string DupIncompleteBudget => IsEn
        ? "Duplicate check stopped early (read budget used up) — only verified groups are listed, nothing is pre-ticked"
        : "重复检测没跑完（读盘预算用完）· 只列出已核实的组，没有预先勾选";
    public static string DupIncompletePartial => IsEn
        ? "Duplicate check was cut short — only verified groups are listed, nothing is pre-ticked"
        : "重复检测中途收手 · 只列出已核实的组，没有预先勾选";
    public static string DupStage(string phase, int done, int total, string bytes) => IsEn
        ? $"Duplicate check · {phase} · {done:N0}/{total:N0} · read {bytes}"
        : $"重复检测 · {phase} · {done:N0}/{total:N0} · 已读 {bytes}";
    public static string DupDone(int groups, int files, string bytes, double seconds) => IsEn
        ? $"Duplicates: {groups:N0} groups / {files:N0} files · read {bytes} · {seconds:0.0}s"
        : $"重复文件：{groups:N0} 组 / {files:N0} 个 · 已读 {bytes} · {seconds:0.0} 秒";
    public static string DupFound(int n, string size) => IsEn
        ? $"Found {n:N0} duplicate items · about {size}"
        : $"找到 {n:N0} 个重复项 · 约 {size}";
    public static string DupNone => IsEn ? "No duplicates found." : "没有找到重复文件。";
    public static string Canceling => IsEn ? "Canceling…" : "正在取消…";

    // ================= 清理结果分层归类（首页 / 位置 / 明细） =================

    // ---- 用途分类名 ----
    public static string PurposeTemp => IsEn ? "Temporary files" : "临时文件";
    public static string PurposeBrowserCache => IsEn ? "Browser cache" : "浏览器缓存";
    public static string PurposeAppCache => IsEn ? "App cache" : "应用缓存";
    public static string PurposeDevCache => IsEn ? "Dev tool cache" : "开发工具缓存";
    public static string PurposeAppLog => IsEn ? "App logs" : "应用日志";
    public static string PurposeRecycle => IsEn ? "Recycle Bin" : "回收站";
    public static string PurposeDump => IsEn ? "Crash dumps" : "崩溃转储";
    public static string PurposeInstaller => IsEn ? "Installers" : "安装包";
    public static string PurposeDuplicate => IsEn ? "Duplicate files" : "重复文件";
    public static string PurposeLarge => IsEn ? "Large files" : "大文件";
    public static string PurposeOld => IsEn ? "Old files" : "很久没动的文件";
    public static string PurposeEmpty => IsEn ? "Empty folders" : "空文件夹";
    public static string PurposeShortcut => IsEn ? "Broken shortcuts" : "失效快捷方式";
    public static string PurposeLongPath => IsEn ? "Very long paths" : "超长路径";
    public static string PurposeDelta => IsEn ? "Changed since last scan" : "和上次相比的变化";
    public static string PurposeOther => IsEn ? "Other" : "其他";

    // ---- 用途影响：回答「清掉会怎样」 ----
    public static string ImpactTemp => IsEn
        ? "Apps recreate these as needed. Nothing you saved is in here."
        : "程序下次用的时候会自己重建，你自己存的东西不在里面。";
    public static string ImpactBrowserCache => IsEn
        ? "Pages reload a bit slower next time. Bookmarks and passwords are untouched."
        : "只清理缓存子目录；网页下次打开会重新加载，书签和密码不在这个范围内。";
    public static string ImpactAppCache => IsEn
        ? "Apps rebuild these. Your documents, chats and settings are untouched."
        : "只清理缓存子目录；软件会自己重建，文档、聊天记录和设置不在这个范围内。";
    public static string ImpactDevCache => IsEn
        ? "Next build downloads them again (needs network)."
        : "下次构建时会重新下载，需要联网。";
    public static string ImpactAppLog => IsEn
        ? "Only past log records. Useful only when troubleshooting."
        : "只是过去留下的日志记录，排查问题时才有用。";
    public static string ImpactRecycle => IsEn
        ? "Gone for good. Check what is inside before clearing."
        : "清掉就真没了。清之前先看一眼里面是什么。";
    public static string ImpactDump => IsEn
        ? "Crash records. Only useful if you are debugging a crash."
        : "程序崩溃时留下的记录，不排查崩溃就用不上。";
    public static string ImpactInstaller => IsEn
        ? "You would need to download them again to reinstall."
        : "以后要重装得重新下载。";
    public static string ImpactDuplicate => IsEn
        ? "One copy of each duplicate group is kept; the rest are removed."
        : "每组重复文件里留一份，其余删掉。";
    public static string ImpactLarge => IsEn
        ? "Large, not necessarily unused."
        : "体积大，不代表没用";
    public static string ImpactOld => IsEn
        ? "Not modified in a long time."
        : "很久没改过";
    public static string ImpactEmpty => IsEn
        ? "These folders hold nothing at all."
        : "这些文件夹里面什么都没有。";
    public static string ImpactShortcut => IsEn
        ? "The shortcut target no longer exists, so the shortcut does nothing."
        : "快捷方式指向的东西已经不在了，点了也没反应。";
    public static string ImpactLongPath => IsEn
        ? "Some programs cannot open these. Moving them usually fixes it."
        : "有些程序打不开它们，通常是挪个位置就好了。";
    public static string ImpactDelta => IsEn
        ? "Information only. Nothing here can be deleted."
        : "只是告诉你变化，这里的东西不能删。";

    // ---- 首页摘要：先说「几处、多少空间」 ----
    public static string SummaryLocations(int locations, string size) => IsEn
        ? $"Found {locations:N0} cleanable location(s), about {size} to reclaim"
        : $"找到 {locations:N0} 处可清理位置，预计可清理 {size}";

    // ---- 位置行 / 明细 ----
    public static string LocationsIn(string purpose) => IsEn ? $"Locations · {purpose}" : $"清理位置 · {purpose}";
    public static string LocationFiles(int files) => IsEn ? $"{files:N0} files" : $"{files:N0} 个文件";
    public static string ViewFiles => IsEn ? "View files" : "查看文件";
    public static string Collapse => IsEn ? "Collapse" : "收起";
    public static string UnknownLocation => IsEn ? "(unrecognised location)" : "（认不出的位置）";
    public static string NoSoftwareName => IsEn ? "Folder (app not identified)" : "文件夹（没认出是哪个软件）";
    public static string DetailShowing(int shown, int total) => IsEn
        ? $"Showing {shown:N0} / {total:N0} files"
        : $"当前显示 {shown:N0} / {total:N0} 个文件";
    public static string LoadMore => IsEn ? "Load more" : "加载更多";
    public static string SearchInScope => IsEn ? "Search name / path" : "搜索文件名 / 路径";
    public static string SearchScopeHint(string scope) => IsEn
        ? $"Search covers all {scope} in this selection, not just loaded rows."
        : $"搜索针对{scope}的全部候选，不只是已显示的部分。";
    public static string SelectGroupHint(int count, int total) => IsEn
        ? $"Tick this group = {count:N0} of {total:N0} candidates (only ones the rules already allow)"
        : $"勾选本组 = 选中 {count:N0} / {total:N0} 个候选（只含规则本来就允许删的）";
    public static string DeselectAll => IsEn ? "Clear selection" : "全部取消勾选";
    public static string OverlapNotice(int n) => IsEn
        ? $"{n:N0} item(s) sit inside a folder that is also selected — counted once."
        : $"{n:N0} 项在已被选中的文件夹里，空间只算了一次。";
    public static string DupHitsNotice(int n) => IsEn
        ? $"{n:N0} duplicate path hit(s) merged."
        : $"{n:N0} 条重复路径已合并。";
    public static string SelectAllInGroup => IsEn ? "Select the whole group" : "选中整组";
    public static string SelectAllInSearch => IsEn ? "Select everything matching this search" : "选中搜索结果全部";
    public static string SelectCurrentPage => IsEn ? "Select this page only" : "只选中当前页";
    public static string GroupSelectionTitle => IsEn ? "How much to select" : "选择范围";
    public static string KeepCandidate => IsEn ? "keep" : "保留";
    public static string ExtraCandidate => IsEn ? "duplicate" : "多余副本";
    public static string DuplicateKeepPath => IsEn
        ? "Shortest path in this group — kept, cannot be selected"
        : "本组里路径最短的那个，留着不动，也选不中";
    public static string DuplicateExtraPath(int copies, int folders) => IsEn
        ? $"{copies:N0} copies of the same content across {folders:N0} folder(s)"
        : $"同一份内容共 {copies:N0} 个，分散在 {folders:N0} 个文件夹";
    public static string EstUnknown => IsEn ? "size unknown" : "空间未知";

    // ============ 位置行内展开候选文件（不用先跑 AI） ============
    public static string ViewFilesAction => IsEn ? "View its files" : "查看文件";
    public static string CollapseFilesAction => IsEn ? "Hide its files" : "收起文件";
    public static string InlineFilesCount(int shown, int total) => IsEn
        ? $"{shown:N0} of {total:N0} candidate item(s), largest first"
        : $"共 {total:N0} 项候选，按占用从大到小列出 {shown:N0} 项";
    public static string InlineFilesHidden(int hidden) => IsEn
        ? $"{hidden:N0} more"
        : $"另有 {hidden:N0} 项";
    /// <summary>行内列表与「在资源管理器中打开」是两件事，必须说清。</summary>
    public static string InlineFilesScope => IsEn
        ? "These are this app's cleanup candidates only — not every file in the folder. "
          + "Use the folder icon to open the real folder in Explorer."
        : "这里只列这一处的清理候选，不是文件夹里的全部文件。要看真实目录请用文件夹图标。";
    public static string OpenFullListAction => IsEn ? "Full list (searchable)" : "完整列表（可搜索）";
    public static string OpenFullListTip => IsEn
        ? "Open the side panel with search, paging and per-item reasons"
        : "打开右侧明细面板：可搜索、分页，并逐项看理由";
    public static string KeptItemBadge => IsEn ? "kept" : "保留";
    public static string FileModifiedHeader => IsEn ? "Modified" : "修改时间";
    public static string FileNameHeader => IsEn ? "File" : "文件名";
    public static string FileSizeHeader => IsEn ? "Size" : "大小";
    public static string FilesOpenFor(string name) => IsEn ? $"Files · {name}" : $"文件 · {name}";

    // ============ 「选择规则明确的清理项」（批量决策，但只走正式规则） ============
    /// <summary>动作名必须说清它选的是**规则明确**的那部分，不是「推荐清理」。</summary>
    public static string RuleSelectAction => IsEn ? "Select rule-clear items" : "选择规则明确的清理项";
    public static string RuleSelectActionTip => IsEn
        ? "Ticks only items the formal rules marked safe with signature-level evidence and that pass the protection check. You see the list first."
        : "只勾选正式规则判定为安全、证据达到签名级、且通过保护检查的项。会先给你看清单。";
    public static string RuleSelectTitle => IsEn ? "Rule-clear items" : "规则明确的清理项";
    public static string RuleSelectSummary(int files, string size, int kinds) => IsEn
        ? $"{files:N0} item(s) · about {size} · {kinds:N0} kind(s)"
        : $"{files:N0} 项 · 约 {size} · {kinds:N0} 类";
    public static string RuleSelectNoEligible => IsEn
        ? "Nothing in this scan matches the formal rules. Pick items yourself."
        : "本次扫描里没有符合正式规则的项，请自己挑。";
    /// <summary>把「选了什么」和「没选什么」同时说清，避免用户以为已经全清了。</summary>
    public static string RuleSelectNote => IsEn
        ? "Only cache / temp / dump items that the rules themselves identified with signature-level "
          + "evidence are ticked. Large files, old files, downloads, archives, videos, personal data and "
          + "anything unknown are NOT included — those stay manual. Caches are not all safe to delete "
          + "without impact, so glance at this list first."
        : "这里只会勾上「规则自己认出、证据到签名级」的缓存 / 临时 / 转储项。"
          + "大文件、旧文件、下载内容、压缩包、视频、个人资料和未知对象都不在其中，仍要你自己挑。"
          + "缓存也不是全都删了没影响，勾完请先看一眼这个清单。";
    public static string RuleSelectConfirm => IsEn ? "Tick these items" : "确认勾选这些";
    public static string RuleSelectCancel => IsEn ? "Cancel" : "取消";
    public static string RuleSelectColExclude => IsEn ? "Exclude" : "排除";
    public static string RuleSelectColPurpose => IsEn ? "Kind" : "类别";
    public static string RuleSelectColImpact => IsEn ? "What removing it does" : "清掉会怎样";
    public static string RuleSelectColFiles => IsEn ? "Items" : "项数";
    public static string RuleSelectColSize => IsEn ? "About" : "约占用";
    public static string RuleSelectColSample => IsEn ? "Examples" : "例子";
    public static string RuleSelectExcludeTip => IsEn
        ? "Tick to leave this kind out of the batch selection"
        : "勾上表示这一类不参与本次批量选择";
    public static string RuleSelectExcludedNote(int files, int kinds, string size) => IsEn
        ? $"Excluded: {kinds:N0} kind(s) · {files:N0} item(s) · about {size}"
        : $"已排除：{kinds:N0} 类 · {files:N0} 项 · 约 {size}";
    public static string RuleSelectApplied(int files, string size) => IsEn
        ? $"Ticked {files:N0} rule-clear item(s) (about {size}). The cleanup check still runs before anything is deleted."
        : $"已按规则勾选 {files:N0} 项（约 {size}）。删除前仍会走清理前检查。";
    public static string RuleSelectNothingNew => IsEn
        ? "Those items were already ticked."
        : "这些项本来就已经勾上了。";
    /// <summary>写入前重新核对时被拦下的条数（保护路径 / 资格变化）。</summary>
    public static string RuleSelectRecheckSkipped(int n) => IsEn
        ? $"{n:N0} item(s) were skipped by the pre-write protection re-check"
        : $"有 {n:N0} 项在写入前复检时被保护规则拦下，未勾选";
    /// <summary>风险分区标题。**叫「清理候选」而不是「建议清理」** —— 改个名字不等于规则没问题，
    /// 但至少不要再让界面替用户下「可以删」的结论。</summary>
    public static string LayerSafe => IsEn ? "Cleanup candidates" : "清理候选";
    public static string LayerConfirm => IsEn ? "Needs your review" : "需要你确认";
    public static string PurposeHeader => IsEn
        ? "Pick a purpose to see where it lives."
        : "先选用途，再看具体位置。";
    public static string LocationHeaderFormat(string purpose, int locations) => IsEn
        ? $"{purpose} · {locations:N0} location(s)"
        : $"{purpose} · {locations:N0} 处位置";
    public static string DetailHeaderFormat(string name, int files) => IsEn
        ? $"Files · {name} · {files:N0} in this selection"
        : $"文件明细 · {name} · 本次范围共 {files:N0} 个";

    // ============ 页面层级 / 导航（一次只显示一层） ============
    public static string PagePurposeTitle => IsEn ? "Clean center" : "清理中心";
    public static string ViewLocations => IsEn ? "View locations ›" : "查看位置 ›";
    public static string ViewFilesArrow => IsEn ? "View files ›" : "查看文件 ›";
    public static string ShowPathAction => IsEn ? "Show path" : "查看路径";
    public static string HidePathAction => IsEn ? "Hide path" : "收起路径";
    public static string PathCopied => IsEn ? "Path copied." : "路径已复制。";
    public static string OpenHere => IsEn ? "Open in Explorer" : "在资源管理器中打开";
    public static string CloseDetail => IsEn ? "Close" : "关闭";
    public static string MoreMenu => IsEn ? "More" : "更多";
    public static string AiExplainHere => IsEn ? "AI: explain what these are" : "AI 解释这些是什么";
    public static string ScanDetails => IsEn ? "Scan details" : "扫描详情";
    public static string ScanDetailsTitle => IsEn ? "Scan details" : "扫描详情";

    /// <summary>
    /// 分区标题的右侧统计：「8 类 · 82 个位置 · 约 131 GB」。
    /// 空间按「约」表达 —— 分组空间是估算，不代表整组都能放心删。
    /// 量词必须写出来，别把「类」和「个位置」混成一个数字。
    /// 空间未知时不硬套「约」，直接照实说未知。
    /// </summary>
    public static string SectionStats(int kinds, int locations, string size) => IsEn
        ? $"{kinds:N0} kinds · {locations:N0} location(s) · {(size == EstUnknown ? size : "about " + size)}"
        : $"{kinds:N0} 类 · {locations:N0} 个位置 · {(size == EstUnknown ? size : "约 " + size)}";

    /// <summary>折叠的分区里仍有已选项时的提示。</summary>
    public static string CollapsedSelected(int selected, int locations) => IsEn
        ? $"({selected:N0} item(s) in {locations:N0} location(s) are already selected)"
        : $"（已选 {selected:N0} 项，分布在 {locations:N0} 个位置）";

    // ---- 风险分区的副标题（说清这一组是什么性质，不只靠颜色） ----
    /// <summary>候选组：**不下「可以优先处理」的结论**，也不把「命中规则」说成「垃圾」，
    /// 只说明它是规则命中的候选、默认没有勾选。用户仍然要自己决定。</summary>
    public static string SectionSubtitleSafe => IsEn
        ? "Matched by rules — matched does not mean useless. Nothing is ticked; you pick."
        : "规则命中的候选，不代表没用 · 默认没有勾选，需要你自己选";
    /// <summary>确认组：说清这里常出现的就是大文件 / 旧文件 / 下载内容这类「还有用」的东西，
    /// 并再次写明默认没有勾选。</summary>
    public static string SectionSubtitleConfirm => IsEn
        ? "Large, old or downloaded items may still be useful. Nothing is ticked — look, then decide."
        : "大文件、旧文件、下载内容等可能还有用 · 默认没有勾选，请查看后决定";
    /// <summary>分区图标：用文字符号也能读出状态，不依赖颜色。</summary>
    public static string SectionIconSafe => "✓";
    public static string SectionIconConfirm => "!";

    // ---- 清理前检查页 ----
    public static string PreflightTitle => IsEn ? "Before cleaning" : "清理前检查";
    public static string PreflightSub => IsEn
        ? "Nothing has been deleted yet. Review, then confirm."
        : "还没有删除任何东西。确认下面的范围后再执行。";
    public static string PreflightLocations(int n) => IsEn
        ? $"{n:N0} location(s) will be handled" : $"将处理 {n:N0} 个位置";
    public static string PreflightItems(int n) => IsEn
        ? $"{n:N0} candidate item(s)" : $"将处理 {n:N0} 个候选项";
    public static string PreflightSize(string size) => IsEn
        ? $"About {size} to handle (estimate)" : $"预计处理空间 约 {size}（估算）";
    public static string PreflightNeedsConfirm(int n) => IsEn
        ? $"{n:N0} item(s) are in “review first” and will be handled too"
        : $"其中 {n:N0} 项属于「需要你确认」，也会一并处理";
    public static string PreflightNoConfirm => IsEn
        ? "All selected items are in “cleanup candidates”."
        : "所选内容都在「清理候选」里。";
    /// <summary>程序会再做一遍的检查。说清是「再检查」，不是保证。</summary>
    public static string PreflightRecheck => IsEn
        ? "Before each item is handled, the app checks again: whether the file still exists, "
          + "whether it changed since the scan, and whether it is protected or in use. "
          + "Items that fail stay where they are and are listed with a reason."
        : "每一项在真正处理前，程序会再检查一遍：文件是否还存在、扫描后是否被改动过、"
          + "是否受系统保护或正在被占用。没通过的项会留在原处，并在结果里写明原因。";
    public static string BackToEdit => IsEn ? "Back and adjust" : "返回修改";
    public static string ConfirmClean => IsEn ? "Confirm and clean" : "确认清理";
    public static string CleaningNow => IsEn ? "Cleaning…" : "正在清理……";
    public static string StopNow => IsEn ? "Stop" : "停止";
    public static string DoneItems(int ok, int failed) => IsEn
        ? $"Done {ok:N0}, {failed:N0} not handled" : $"已完成 {ok:N0} 项，{failed:N0} 项未处理";
    public static string ViewResults => IsEn ? "View results" : "查看结果";
    public static string NothingToPreflight => IsEn
        ? "Nothing is selected, so there is nothing to check."
        : "没有选中内容，无需检查。";

    // ---- AI 分析面板（清理中心的核心区域） ----
    public static string AiPanelTitle => IsEn ? "AI analysis" : "AI 分析";
    public static string AiPanelIntro => IsEn
        ? "Helps you understand the scan and see what is worth handling first."
        : "帮你理解扫描结果，并判断哪些内容值得优先处理。";
    public static string AiNotConfiguredTitle => IsEn ? "AI analysis is not configured" : "AI 分析尚未配置";
    public static string AiNotConfiguredSub => IsEn
        ? "Configure it to get explanations and priority suggestions for the cleanup list."
        : "配置后可获得清理内容的解释和优先级建议。";
    public static string AiConfigure => IsEn ? "Configure AI" : "配置 AI";
    public static string AiWorkingTitle => IsEn ? "AI is analysing the scan…" : "AI 正在分析当前扫描结果……";
    public static string AiDoneTitle => IsEn ? "AI analysis finished" : "AI 已完成分析";
    public static string AiIdleTitle => IsEn ? "AI analysis has not run yet" : "尚未进行 AI 分析";
    public static string AiIdleSub => IsEn
        ? "Run it to get per-item explanations for the candidates."
        : "运行后会为候选项补充说明，帮助你判断。";
    public static string AiStoppedTitle => IsEn ? "AI analysis stopped" : "AI 分析已停止";
    public static string AiFailedTitle => IsEn ? "AI analysis failed" : "AI 分析失败";
    public static string AiRecommendLabel => IsEn ? "Handle first: " : "优先建议：";
    public static string AiCautionLabel => IsEn ? "Decide carefully: " : "谨慎判断：";
    public static string ViewAiAnalysis => IsEn ? "View AI analysis" : "查看 AI 分析";
    public static string Reanalyze => IsEn ? "Analyse again" : "重新分析";
    public static string AiPanelNoLists => IsEn ? "no recognizable groups" : "没有可归类的分组";
    /// <summary>AI 建议选择：文案必须说清「只是建议」且仍要用户确认。</summary>
    public static string AiSelectByAdvice => IsEn ? "Select by AI advice" : "按 AI 建议选择";
    public static string AiAdviceHint => IsEn
        ? "Only ticks items local rules already allow. You still confirm before anything is cleaned."
        : "只勾选本地规则已经允许的项；执行前仍然由你确认。";
    public static string AiAnalysisTitle => IsEn ? "AI analysis" : "AI 分析详情";
    public static string AiAnalysisNoResult => IsEn
        ? "AI has not produced a result yet. Run the analysis first."
        : "AI 还没有产出结果，请先运行一次分析。";
    public static string RunAiAnalysis => IsEn ? "Run AI analysis" : "开始 AI 分析";
    public static string AiAnalyzingShort => IsEn ? "Analysing…" : "分析中…";
    public static string AiNoKeepGroup => IsEn ? "nothing is marked keep-only" : "没有被标为「保留」的分组";
    public static string AiAdviceApplied(int n) => IsEn
        ? $"Selected {n:N0} item(s) that local rules already allow. Review, then confirm."
        : $"已按建议勾选 {n:N0} 项（都在本地规则允许范围内）。请核对后再确认执行。";
    public static string AiAdviceNothing => IsEn
        ? "Nothing new to select by advice." : "按建议没有可新增勾选的内容。";
    public static string AiScopeLine(int analysed, int total) => IsEn
        ? $"Scope: {analysed:N0} of {total:N0} candidate item(s) were sent for analysis"
        : $"分析依据范围：本次送出 {analysed:N0} 项，共 {total:N0} 个候选项";
    public static string AiScopePartial => IsEn
        ? "Note: only part of the candidates was analysed (batch limit). The rest keep their local rule descriptions."
        : "注意：受单次上限影响，只分析了部分候选项；其余保留本地规则给出的说明。";
    public static string AiDetailWhy => IsEn ? "Why these are suggested:" : "为什么推荐这些：";
    public static string AiDetailImpact => IsEn ? "What cleaning may affect:" : "清理后可能产生的影响：";
    public static string AiDetailAvoid => IsEn ? "What is NOT suggested for handling:" : "哪些内容不建议处理：";
    public static string AiDetailAsk => IsEn ? "What needs your decision:" : "哪些内容需要你确认：";
    public static string AiDetailBoundary => IsEn
        ? "AI only explains and suggests. Risk levels, deletable scope and the final choice all stay with the local rules and with you."
        : "AI 只做解释和建议。风险等级、可删除范围与最终选择仍由本地规则和你决定。";

    // ---- 逐项 AI（按需分析单个文件 / 单个清理位置） ----
    public static string ItemAiStopped => IsEn ? "Stopped" : "已停止";



    public static string AiItemSummaryScope(int shown, int total) => IsEn
        ? $"summary covers the {shown} largest of {total} direct child item(s) — NOT the whole folder"
        : $"摘要只覆盖 {total} 个直接子项里最大的 {shown} 个 —— 不是整个文件夹";

    public static string AiBtnTip => IsEn ? "Analyse this item with AI" : "用 AI 分析这一项";
    /// <summary>「更多」菜单里的说明：AI 入口已经挪到每一项旁边。</summary>
    public static string AiPerItemHint => IsEn
        ? "Single-item AI analysis lives on the row itself (the ✦ button)"
        : "单项 AI 分析在每一行自己的按钮上（✦）";
    /// <summary>「更多」菜单里的批量归类入口。</summary>
    public static string AiBatchClassify => IsEn ? "Classify items AI can't name" : "批量识别用途";
    public static string AiBatchClassifyWithCount(int n) => IsEn
        ? $"Classify {n:N0} item(s) AI can't name"
        : $"批量识别用途（还有 {n:N0} 项没认出来）";
    /// <summary>页头按钮上的短文案（长的放 ToolTip 和菜单）。</summary>
    public static string AiBatchClassifyShort(int n) => IsEn
        ? $"Classify {n:N0} unnamed"
        : $"识别用途（{n:N0} 项）";
    /// <summary>
    /// 页头按钮：数字必须是**这一屏真正会问的条数**。
    /// 曾经写成「全局还没识别的条数」，于是按钮显示 (2,517 项)、实际只问了当前可见的 83 条 ——
    /// 标签和动作对不上，用户会以为卡住了。
    /// </summary>
    public static string AiBatchClassifyScreen(int n) => IsEn
        ? $"Classify this screen ({n:N0})"
        : $"识别这一屏（{n:N0} 项）";

    /// <summary>进行中的诚实提示：没有真实百分比就不编，只报已等待时间。</summary>
    public static string AiRunningHint(TimeSpan waited) => IsEn
        ? $"Waiting for the model… {waited.TotalSeconds:0}s elapsed. It can take a while; you can stop at any time."
        : $"正在等待模型返回……已等待 {waited.TotalSeconds:0} 秒。可能较慢，随时可以停止。";
    public static string AiStopShort => IsEn ? "Stop analysis" : "停止分析";
    public static string AiStopping => IsEn ? "Stopping AI analysis…" : "正在停止 AI 分析…";
    /// <summary>「提交 60 项 · 生成 15 条可用说明 · 45 项无说明」—— 三个数分开说。</summary>
    public static string AiCountLine(int sent, int applied, int without) => IsEn
        ? $"Submitted {sent:N0} · usable notes {applied:N0} · no note {without:N0}"
        : $"提交 {sent:N0} 项 · 生成可用说明 {applied:N0} 条 · 无说明 {without:N0} 项";
    public static string AiDoneSub(int applied, int sent, int without) => IsEn
        ? $"Generated {applied:N0} usable note(s) from {sent:N0} submitted item(s)"
          + (without > 0 ? $"; {without:N0} item(s) got no note." : ".")
        : $"从提交的 {sent:N0} 项中生成 {applied:N0} 条可用说明"
          + (without > 0 ? $"；另有 {without:N0} 项没有拿到说明。" : "。");
    public static string AiNoUsefulTitle => IsEn ? "AI replied, but no usable notes" : "AI 有回复，但没有可用说明";
    public static string AiNoUsefulSub(int sent) => IsEn
        ? $"{sent:N0} item(s) were submitted but none came back in a usable format. Local rule descriptions are unchanged."
        : $"提交了 {sent:N0} 项，但没有一条能按格式解析成说明。本地规则的说明保持不变。";
    public static string AiResultUnusable => IsEn
        ? "AI replied but produced no usable note; local rules still apply."
        : "AI 有回复但没有产出可用说明；本地规则仍然有效。";
    public static string AiNoNotesBody => IsEn
        ? "No usable per-item AI note was produced. Nothing here is an AI conclusion."
        : "本次没有产出可用的逐项 AI 说明。下面显示的都是本地规则结果，不是 AI 结论。";
    public static string AiNotesHead(int n) => IsEn
        ? $"AI notes for {n:N0} item(s) (real model output):"
        : $"AI 逐项说明（真实模型输出，共 {n:N0} 条）：";
    public static string AiNotesMore(int n) => IsEn
        ? $"…plus {n:N0} more (open the file detail to read them all)."
        : $"……另有 {n:N0} 条（在文件详情里可以逐条查看）。";
    /// <summary>本地规则摘要必须打标签，不能冒充 AI 结论。</summary>
    public static string AiLocalTag => IsEn ? "[local rules]" : "［本地规则］";
    public static string AiLocalHead => IsEn
        ? "From local rules (not an AI conclusion):"
        : "以下来自本地规则，不是 AI 结论：";
    public static string AiTimingLine(double buildMs, double sendMs, double parseMs) => IsEn
        ? $"Timing: prepare {buildMs:0}ms · request {sendMs:0}ms · parse {parseMs:0}ms"
        : $"耗时：组装 {buildMs:0} 毫秒 · 请求 {sendMs:0} 毫秒 · 解析 {parseMs:0} 毫秒";
    public static string AiParseLine(int lines, int candidates, int hits, int noise) => IsEn
        ? $"Response: {lines:N0} line(s) · {candidates:N0} list-like · {hits:N0} matched a submitted path · {noise:N0} noise cell(s) skipped"
        : $"响应解析：{lines:N0} 行 · 像清单的 {candidates:N0} 行 · 命中提交路径 {hits:N0} 行 · 跳过噪音列 {noise:N0} 次";
    public static string AiChannelLine(string channel, int attempts) => IsEn
        ? $"Channel: {channel} · attempt(s): {attempts}"
        : $"通道：{channel} · 尝试次数：{attempts}";

    /// <summary>
    /// 估算口径改成悬停提示（不再单独占一整行）。
    /// 措辞不能承诺「立刻多出等量空间」—— 回收站清空后空间才真正回来。
    /// </summary>
    public static string EstimateTip => IsEn
        ? "“About” is an estimate, not a promise. Actual free space grows after the Recycle Bin is emptied."
        : "「预计」是估算值，不是承诺。实际可用空间会在回收站清空后才增加。";
    public static string NeedsConfirmTip => IsEn
        ? "Items in “review first” are included and are not auto-chosen for you."
        : "「需要你确认」的项目也会被处理，程序不会替你自动勾选它们。";

    /// <summary>位置页标题区：「临时文件 / 23 个位置 · 预计处理 18.1 GB」。</summary>
    public static string LocationPageHead(string purpose, int locations, string size) => IsEn
        ? $"{locations:N0} location(s) · about {size} to handle"
        : $"{locations:N0} 个位置 · 预计处理 {size}";

    // ---- 底部固定操作栏（唯一主要执行入口） ----
    public static string NothingSelectedYet => IsEn ? "Nothing selected yet." : "尚未选择清理内容";
    /// <summary>「已选 2 个位置，包含 N 个候选项 · 预计处理 2.6 GB」。</summary>
    public static string SelectionSummary(int locations, int items, string size) => IsEn
        ? $"{locations:N0} location(s) selected · {items:N0} candidate item(s) · about {size} to handle"
        : $"已选 {locations:N0} 个位置，包含 {items:N0} 个候选项 · 预计处理 {size}";
    public static string ClearSelection => IsEn ? "Clear selection" : "清空选择";
    /// <summary>
    /// 底部「全选」切换按钮的提示（未全选时）：点一下把**全局**所有可清理项都勾上。
    /// 文字恒为 <see cref="SelectAll"/>，不随状态改名。
    /// </summary>
    public static string SelectAllActionTip => IsEn
        ? "Select every cleanable item (the whole result, not just this page)"
        : "全选所有可清理项（整个结果，不只是当前页）";
    /// <summary>底部「全选」切换按钮的提示（已全选时）：文字不变，再点一下是取消全部勾选。</summary>
    public static string SelectAllClearTip => IsEn
        ? "All cleanable items are selected — click again to clear"
        : "已全选，再点一次取消全选";
    /// <summary>
    /// 底部唯一主按钮。**不再叫「检查并清理」** —— 那个词说不清点了会发生什么。
    /// 点它进入清理前检查页，不是直接删。
    /// </summary>
    public static string CleanSelectedItems => IsEn ? "Clean selected items" : "清理已选项目";
    public static string GlobalSelectionNote => IsEn
        ? "Selection is global and includes items not on this page."
        : "选择是全局的，包含不在当前页面的已选项。";
    public static string ViewSelected => IsEn ? "View selected" : "查看已选";
    public static string ViewSelectedTip(int n) => IsEn
        ? $"Review the {n:N0} selected candidate(s) — this only opens a list, it does not clear the selection."
        : $"核对已选的 {n:N0} 个候选项 —— 只是打开清单，不会清空选择。";
    /// <summary>清单口径说明：明确「候选项」与「位置」是两种计数，不与文件数混用。</summary>
    public static string ViewSelectedSub => IsEn
        ? "Grouped by cleanup location. “Items” counts candidate items, not files. "
          + "Sizes are estimates. Closing this list does not change the selection."
        : "按清理位置分组。表格里的「候选项」计的是候选项数量，不是文件数；空间为估算值。"
          + "关闭本清单不会改动选择。";
    /// <summary>没归到任何已建位置的已选项，单独归一行，避免清单漏数。</summary>
    public static string OtherSelected => IsEn ? "(other selected items)" : "（其它已选项）";
    public static string ViewSelectedTitle => IsEn ? "Selected for cleaning" : "已选中的清理内容";
    public static string SelectedRatio(int selected, int total) => IsEn
        ? $"{selected:N0} / {total:N0} selected"
        : $"已选 {selected:N0} / {total:N0} 项";
    public static string NothingSelectable => IsEn ? "nothing selectable" : "无可选项";
    public static string SelectSectionAll => IsEn ? "Select this section" : "选中本分区";
    public static string SelectLocationAll => IsEn ? "Select this location" : "选中本位置";
    /// <summary>组选择范围提示：说清是整组而不是当前页。</summary>
    public static string SelectWholeGroup(int n) => IsEn
        ? $"This selects the whole group ({n:N0} items), not just this page."
        : $"将选中整组（{n:N0} 项），不是只选当前页。";

    // ---- 空 / 进行中 / 部分完成 / 取消 / 失败 ----
    public static string EmptyAfterScan => IsEn
        ? "Nothing worth cleaning was found on this drive."
        : "这个盘没找到值得清理的内容。";
    public static string StateScanning => IsEn ? "Scanning the drive…" : "正在扫描磁盘…";
    public static string StateAnalyzing => IsEn ? "Checking what can be cleaned…" : "正在分析可清理内容…";
    public static string StateDup => IsEn ? "Comparing duplicate files…" : "正在核对重复文件…";
    public static string StateCanceledTitle => IsEn ? "Stopped" : "已停止";
    public static string StateCanceledBody => IsEn
        ? "Partial results are kept below. Nothing was deleted."
        : "下面是已经算出来的部分结果，没有删除任何东西。";
    public static string StateFailedTitle => IsEn ? "Scan failed" : "扫描失败";
    public static string StatePartialTitle => IsEn ? "Not everything was checked" : "有内容没检测完";
    public static string PartialBadge => IsEn ? "incomplete" : "未跑完";

    // ---- 扫描诊断收敛（细节进「扫描详情」） ----
    public static string ScanOkShort => IsEn ? "Scan finished" : "扫描完成";
    /// <summary>
    /// 异常路径的短警告。**不带「扫描完成」前缀** —— 那一句在成功时已经不显示了，
    /// 异常时再说一遍只是噪音；这里只讲真正有问题的那半句。
    /// </summary>
    public static string ScanPartialShort => IsEn
        ? "Some content was not checked"
        : "部分内容未检测";
    public static string ScanStoppedShort => IsEn ? "Scan stopped early" : "扫描中途停止";
    public static string ScanDone => IsEn ? "Scan finished" : "扫描完成";
    /// <summary>兼容模式（没走 MFT）：不重复「扫描完成」，因为状态行里还有覆盖度那句。</summary>
    public static string ScanDoneFallback => IsEn ? "Compatibility mode" : "兼容模式扫描";
    public static string ScanElapsedLine(double seconds) => IsEn
        ? $"Scan took {seconds:0.0}s" : $"扫描耗时 {seconds:0.0} 秒";

    // ---- 清理范围（与文件浏览器状态分离） ----
    public static string ScopeToFolder => IsEn ? "Only clean in this folder" : "只看此文件夹的清理项";
    public static string ScopeToFolderHint(string path) => IsEn
        ? "Limit the cleanup view to:\n" + path
        : "把清理视图限制在：\n" + path;
    public static string ScopeNeedFolder => IsEn
        ? "Pick a folder in the file browser first."
        : "先在文件浏览器里选一个文件夹。";
    public static string ClearScope => IsEn ? "Stop limiting to this folder" : "取消只看此文件夹";
    public static string ClearScopeAction => IsEn ? "Clear range" : "清除范围";

    // ---- 侧栏开关 ----
    public static string ShowSidebar => IsEn ? "Show file browser" : "显示文件浏览器";
    public static string HideSidebar => IsEn ? "Hide file browser" : "隐藏文件浏览器";
    /// <summary>齿轮按钮的可访问名称（比「设置」更清楚点了会发生什么）。</summary>
    public static string OpenSettings => IsEn ? "Open settings" : "打开设置";

    // ---- 候选统计（进扫描详情，不在主页面刷屏） ----
    public static string CandidateTotalLine(int files, int dupGroups) => IsEn
        ? $"Candidates: {files:N0} · duplicate groups: {dupGroups:N0}"
        : $"候选总数：{files:N0} · 重复组：{dupGroups:N0}";
    public static string CandidateDedupLine(int overlap, int dupHits) => IsEn
        ? $"Inside an already-selected folder: {overlap:N0} · duplicate path hits merged: {dupHits:N0}"
        : $"已被选中文件夹包含：{overlap:N0} · 重复路径合并：{dupHits:N0}";
    public static string DupFinished => IsEn ? "Duplicate check: finished." : "重复检测：已跑完。";
    public static string DupNotFinished => IsEn
        ? "Duplicate check: NOT finished (budget/limit) — some duplicates may be missing."
        : "重复检测：没跑完（预算/上限）——可能还有重复文件没找出来。";

    /// <summary>范围提示。范围外仍有已选项时必须说明，否则会被静默执行。</summary>
    public static string ScopeChip(string path, int selectedOutside) => IsEn
        ? $"Range: {path}"
          + (selectedOutside > 0
              ? $"   ·   {selectedOutside:N0} selected item(s) are OUTSIDE this range and will still be handled"
              : "")
        : $"当前范围：{path}"
          + (selectedOutside > 0
              ? $"   ·   范围外还有 {selectedOutside:N0} 个已选项，仍会被处理"
              : "");

    // ---- 分层视图补充 ----
    public static string DetailScopeCount(int n, string size) => IsEn
        ? $"{n:N0} item(s) in this scope · about {size}"
        : $"本位置共 {n:N0} 项 · 预计 {size}";
    public static string AllLoaded => IsEn ? "all loaded" : "已全部加载";
    public static string LargestSelected => IsEn ? "Largest selected:" : "占用最大的已选项：";
    public static string NoScanYet => IsEn ? "No scan yet." : "还没扫描。";
    public static string CleanStateFailedBody => IsEn
        ? "Nothing was changed. Try again, or check the log for details."
        : "没有改动任何东西。可以重试，细节见诊断日志。";
    public static string CleanStatePartialBody => IsEn
        ? "Counts below are a lower bound. See scan details for what was skipped."
        : "下面的数字只是下限。哪些内容没检测到，见「扫描详情」。";

    // 选择范围菜单：文案必须带范围与条数
    public static string SelectCurrentPageCount(int n) => IsEn
        ? $"This page only ({n:N0} items)" : $"只选当前页（{n:N0} 项）";
    public static string SelectAllInSearchCount(int n) => IsEn
        ? $"Everything matching the search ({n:N0} items)" : $"搜索结果全部（{n:N0} 项）";
    public static string SelectWholeGroupCount(int n) => IsEn
        ? $"Whole location ({n:N0} items)" : $"整个位置（{n:N0} 项）";
    public static string DeselectAllCount(int n) => IsEn
        ? $"Clear selection ({n:N0} items)" : $"全部取消勾选（{n:N0} 项）";

    // ---- 扫描详情（细节从页头收进来，数据一条不删） ----
    public static string ScanSourceLine(string source) => IsEn ? "Scan source: " + source : "扫描来源：" + source;
    public static string ScanDurationLine(double seconds) => IsEn
        ? $"Duration: {seconds:0.0}s" : $"耗时：{seconds:0.0} 秒";
    public static string ScanCountLine(int files, int dirs) => IsEn
        ? $"Read: {files:N0} files / {dirs:N0} folders" : $"读到：{files:N0} 个文件 / {dirs:N0} 个文件夹";
    public static string ScanSkipLine(int skipped, int perm, int path, int read) => IsEn
        ? $"Skipped: {skipped:N0} folders · permission errors {perm:N0} · path errors {path:N0} · read failures {read:N0}"
        : $"跳过：{skipped:N0} 个目录 · 权限不足 {perm:N0} · 路径问题 {path:N0} · 读取失败 {read:N0}";
    public static string ScanReparseLine(int reparse) => IsEn
        ? $"Links not followed: {reparse:N0} (junctions / symlinks / cloud placeholders)"
        : $"未跟随的链接：{reparse:N0}（快捷方式/符号链接/云占位）";
    public static string ScanRecordLine(int unparsed, int orphan, int hardLinks) => IsEn
        ? $"MFT records: unparsable {unparsed:N0} · orphan {orphan:N0} · hard links {hardLinks:N0}"
        : $"MFT 记录：读不出 {unparsed:N0} · 找不到父目录 {orphan:N0} · 硬链接 {hardLinks:N0}";
    public static string ScanCompleteLine => IsEn
        ? "Result: this scan is complete." : "结论：这次扫描是完整的。";
    public static string ScanIncompleteLine => IsEn
        ? "Result: this scan is INCOMPLETE — some content was not checked."
        : "结论：这次扫描不完整 —— 有内容没检测到。";
    public static string LocationsNotShown(int n) => IsEn
        ? $"+{n:N0} more location(s) not listed (totals above still include them)"
        : $"另有 {n:N0} 处位置未逐条列出（上面的总数已包含它们）";

    /// <summary>确认框：说清位置数 / 候选数 / 预计空间 / 需要确认的风险。</summary>
    public static string DeleteScopeConfirm(int locations, int files, string size, int sensitive)
    {
        string head = IsEn
            ? $"Delete from {locations:N0} location(s): {files:N0} files, about {size}."
            : $"将从 {locations:N0} 处位置删除：{files:N0} 个文件，预计 {size}。";
        if (sensitive <= 0) return head;
        string tail = IsEn
            ? $"\n{sensitive:N0} of them picked from the “needs your review” group."
            : $"\n其中 {sensitive:N0} 个来自「需要你确认」那一组。";
        return head + tail;
    }    public static string CanceledPartial => IsEn
        ? "Stopped. Results so far are still shown; duplicate check did not finish."
        : "已停止。已有结果仍可查看，重复检测没有跑完。";
    public static string AnalyzingPartial => IsEn
        ? "Stopped. Partial results are shown below."
        : "已停止。下面是已经算出来的部分结果。";
    public static string ReasonGrew(string size) => IsEn ? "Grew " + size : "多了 " + size;    public static string ReasonShrunk(string size) => IsEn ? "Shrank " + size : "少了 " + size;
    public static string ReasonGone => IsEn ? "Gone since last scan" : "上次有，这次没了";
    public static string CompareFirst => IsEn ? "First scan of this drive — next scan can compare." : "这盘第一次扫，下次才能对比。";
    public static string CompareSince(DateTime when, string delta) =>
        IsEn ? $"Last scan {when:yyyy-MM-dd HH:mm}, root {delta}"
             : $"上次 {when:yyyy-MM-dd HH:mm}，根目录 {delta}";
    public static string CatCount(int n, string size) => $"{n:N0} · {size}";
    public static string HashingDups => IsEn ? "Checking duplicates…" : "正在核对重复文件…";
    public static string CleanScan => IsEn ? "Scanning disk…" : "正在扫描磁盘…";
    public static string CleanWalk => IsEn ? "Walking files…" : "正在遍历文件…";
    public static string CleanRules => IsEn ? "Matching clean rules…" : "正在套清理规则…";
    public static string CleanShortcuts => IsEn ? "Checking shortcuts…" : "正在检查快捷方式…";
    public static string CleanDups => IsEn ? "Checking duplicates…" : "正在核对重复文件…";
    public static string CleanCompare => IsEn ? "Comparing with last scan…" : "正在和上次扫描对比…";
    public const int AnalyzeSteps = 6;
}
