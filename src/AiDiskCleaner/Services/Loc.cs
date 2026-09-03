using AiDiskCleaner.Models;
using UninstallTools.Junk.Confidence;

namespace AiDiskCleaner.Services;

/// <summary>界面文案。Zh / En 两套，设置里切换。</summary>
public static class Loc
{
    public static AppLang Lang { get; set; } = AppLang.Zh;
    public static bool IsEn => Lang == AppLang.En;

    public static string AppName => IsEn ? "Dashao Huo" : "大扫货";
    public static string Scan => IsEn ? "Scan" : "扫描";
    public static string Stop => IsEn ? "Stop" : "停止";
    public static string Settings => IsEn ? "Settings" : "设置";
    public static string About => IsEn ? "About" : "关于";
    public static string Ready => IsEn ? "Ready" : "就绪";
    public static string Scanning => IsEn ? "Scanning" : "扫描中";
    public static string Preparing => IsEn ? "Preparing…" : "准备中…";
    public static string ScanningEllipsis => IsEn ? "Scanning…" : "正在扫描…";
    public static string SearchHint => IsEn ? "Search path / name" : "搜索路径 / 文件名";
    public static string Path => IsEn ? "Path" : "路径";
    public static string Pct => IsEn ? "Share" : "占比";
    public static string Size => IsEn ? "Size" : "大小";
    public static string ExtType => IsEn ? "Extension / Type" : "扩展名 / 类型";
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
    public static string MftFail => IsEn ? "MFT failed, falling back" : "MFT 失败，改用递归扫描";
    public static string Aborted => IsEn ? "Stopped" : "已停止";
    public static string ScanFailed => IsEn ? "Scan failed" : "扫描失败";
    public static string ScanFailedMsg(string msg) => IsEn ? "Scan failed: " + msg : "扫描失败：" + msg;
    public static string AnalyzeAfterScan => IsEn ? "Scan to analyze" : "扫描后分析";
    public static string HintClean => IsEn ? "Hint: disk looks clean" : "建议：磁盘较干净";
    public static string HintTemp(int count, string size) =>
        IsEn ? $"Hint: {count} temp/log files, about {size}"
             : $"建议：{count} 个临时/日志文件，约 {size}";
    public static string Language => IsEn ? "Language" : "语言";
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
    public static string AiPickModel => IsEn ? "Model" : "模型";
    public static string AiNeedScanFirst => IsEn ? "Scan the disk first." : "先扫描磁盘。";
    public static string AiRound(int n) => IsEn ? $"round {n}" : $"第 {n} 轮";
    public static string AiToolResult(string name, string preview) =>
        IsEn ? $"{name} → {preview}" : $"{name} → {preview}";
    public static string AiYou => IsEn ? "You" : "你";
    public static string AiBot => ModelLabel();
    public static string ModelLabel()
    {
        string name = (App.Settings.AiModel ?? "").Trim();
        return string.IsNullOrEmpty(name) ? (IsEn ? "AI" : "AI") : name;
    }
    public static string AiLampOff => IsEn ? "not configured" : "未配置";
    public static string AiReady => IsEn ? "ready" : "已配置";
    public static string AiMark => IsEn ? "AI suggested" : "AI 建议";
    public static string AiInside(string note) => IsEn ? "inside: " + note : "内有 · " + note;
    public static string AiLampOn => IsEn ? "connected" : "已连接";
    public static string AiLampBusy => IsEn ? "reading…" : "正在分析…";
    public static string AiLampFail => IsEn ? "failed" : "失败";
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
    public static string JuryToggleOn => IsEn ? "Multi-model jury: on" : "多模型评选：开";
    public static string JuryToggleOff => IsEn ? "Multi-model jury: off" : "多模型评选：关";
    public static string JuryLabel => IsEn ? "Models in the jury (max 4)" : "参与评选的模型（最多 4 个）";
    public static string JuryHint => IsEn
        ? "Pick models under each provider. They run together after you say what to clean. Nothing is checked until you confirm."
        : "按提供方勾选模型。说清需求后一起跑、投票打分。没经你确认不会勾选删除项。";
    public static string JuryChipMore(int n) => IsEn ? $"+{n}" : $"+{n}";
    public static string JuryNeedAsk => IsEn
        ? "What should I focus on first?\n• caches / temp / dumps\n• Downloads installers\n• dev caches (npm, pip, Gradle, Docker)\n• video downloads\n• all cleanable items\nSay it in one line. I will not check anything until you confirm the scores."
        : "想先清哪块？说一句就行：\n• 缓存 / 临时 / 转储\n• 下载里的安装包\n• 开发缓存（npm、pip、Gradle、Docker）\n• 视频下载\n• 全部可清项\n说完我再让模型一起打分。没经你确认不会勾选。";
    public static string JuryWorking(int n) => IsEn ? $"jury: {n} models…" : $"评审：{n} 个模型并行…";
    public static string JurySeatOk(string name, int n) => IsEn ? $"{name} · {n} items" : $"{name} · {n} 条";
    public static string JurySeatFail(string name, string err) => IsEn ? $"{name} failed: {err}" : $"{name} 失败：{err}";
    public static string JurySummary(int models, int high) => IsEn
        ? $"{models} models voted. {high} high-confidence items. Nothing is checked yet."
        : $"{models} 个模型已投票。高把握 {high} 项。还没勾选，等你确认。";
    public static string JuryAsk => IsEn
        ? "Reply 确认 to check high-confidence items. Or change the goal."
        : "回「确认」就勾上高把握项。想改范围直接说。";
    public static string JuryChecked => IsEn ? "High-confidence items are now checked on the right." : "高把握项已在右边勾上。等你点删除。";
    public static string JuryNone => IsEn ? "No overlapping suggestions. Try a clearer goal." : "没有重叠建议。需求再说具体一点。";
    public static string JurySystem => IsEn
        ? """
          You score cleanup candidates. Never delete. Never invent paths. No markdown.
          Only list items that match the USER NEED. Copy full paths from the scan.
          Never list Windows / Program Files / Users / WinSxS as a whole.
          Reply exactly:

          SUMMARY
          one sentence.

          DELETABLE
          GOTO C:\full\path	12.4G	why it matches the need

          KEEP
          GOTO C:\full\path	24G	why to leave it
          """
        : """
          你给清理项打分。不要删除，不要编造路径，不要 markdown。
          只列符合「用户需求」的项。路径从扫描结果原样复制。
          不要写整个 Windows / Program Files / Users / WinSxS。
          只按这个格式回复：

          SUMMARY
          一句话。

          DELETABLE
          GOTO C:\完整路径	12.4G	为什么符合需求

          KEEP
          GOTO C:\完整路径	24G	为什么别动
          """;
    public static string AiScanHeader => IsEn
        ? "Scan finished. Reply in the exact format below. Do not invent files."
        : "扫描结束。必须按下述格式回复。不要编造文件。";
    public static string AiAnalystSystem => IsEn
        ? """
          You are a file analyst in a disk cleaner. Never delete. Never invent paths.
          FINAL reply MUST use this exact shape. No markdown, no extra sections, no prose mixed into item lines:

          SUMMARY
          one or two sentences.

          FOLDERS
          GOTO C:\full\path	12.4G	what it is
          (3-8 root folders only)

          DELETABLE
          GOTO C:\full\path	2.4G	why it can be deleted
          (concrete files/caches only; never Windows / Program Files / Users / WinSxS as a whole)

          KEEP
          GOTO C:\full\path	24.4G	why not delete

          QUESTION
          one question.

          Each GOTO line: GOTO, then the full path, then size, then one-line reason. Copy paths from the scan. Ask the goal first. Do not call set_checked in this round. Safe cache may be suggested; keep/migrate only. WeChat/QQ: cache only.
          """
        : """
          你是磁盘清理软件里的文件分析师。不要删除，不要编造路径。
          最终回复必须用下面这个格式。不要 markdown，不要加别的章节，条目行里不要夹长文：

          SUMMARY
          一两句话总览。

          FOLDERS
          GOTO C:\完整路径	12.4G	这是什么
          （只写 3～8 个大根目录）

          DELETABLE
          GOTO C:\完整路径	2.4G	为什么能删
          （只写具体文件/缓存，不要写整个 Windows / Program Files / Users / WinSxS）

          KEEP
          GOTO C:\完整路径	24.4G	为什么不能删

          QUESTION
          问一句用户想先清哪块。

          每条 GOTO：GOTO、完整路径、大小、一句原因。路径从扫描结果原样复制。先问清需求再建议。这一轮不要调用 set_checked。safe cache 可建议删；keep 只能迁移。微信/QQ 只动缓存。
          """;
    public static string AiTool(string name) => IsEn ? $"tool: {name}" : $"工具：{name}";
    public static string AiKindName(AiProtocol p) => p switch
    {
        AiProtocol.Responses => AiProtoResponses,
        AiProtocol.Anthropic => AiProtoAnthropic,
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
    public static string NoExt => IsEn ? "(no extension)" : "(无扩展名)";
    public static string Folder => IsEn ? "Folder" : "文件夹";
    public static string Allocated => IsEn ? "Allocated" : "分配";
    public static string Items => IsEn ? "Items" : "项目";
    public static string FilesCol => IsEn ? "Files" : "文件";
    public static string FoldersCol => IsEn ? "Folders" : "文件夹";
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

    public static string TypeName(string ext)
    {
        ext = ext.ToLowerInvariant();
        if (IsEn)
        {
            return ext switch
            {
                ".dll" => "App extension",
                ".exe" => "Application",
                ".sys" or ".mui" => "System file",
                ".log" or ".etl" => "Log",
                ".txt" => "Text",
                ".tmp" or ".temp" or ".cache" or ".bak" or ".old" => "Temporary",
                ".jpg" or ".jpeg" or ".png" or ".gif" or ".bmp" or ".webp" => "Image",
                ".mp4" or ".mkv" or ".mov" or ".avi" or ".wmv" => "Video",
                ".mp3" or ".wav" or ".flac" or ".aac" => "Audio",
                ".doc" or ".docx" or ".xls" or ".xlsx" or ".pdf" or ".ppt" or ".pptx" => "Document",
                ".py" or ".js" or ".ts" or ".json" or ".cs" or ".cpp" or ".c" or ".h"
                    or ".java" or ".go" or ".rs" or ".html" or ".css" => "Code",
                ".zip" or ".rar" or ".7z" or ".tar" or ".gz" or ".tgz" => "Archive",
                ".msi" or ".iso" => "Installer",
                ".db" or ".sqlite" or ".dat" or ".bin" => "Data",
                ".vhd" or ".vhdx" => "Disk image",
                ".img" => "Optical image",
                ".jar" => "Java archive",
                ".pak" => "Game pack",
                ".dmp" => "Dump",
                ".nvph" => "NVIDIA cache",
                ".bundl" => "Bundle",
                ".ppkg" => "Provisioning",
                ".ress" => "Unity resource",
                ".body" => "Resource body",
                ".assets" => "Assets",
                "" or "(无扩展名)" or "(no extension)" => NoExt,
                "$mft" => "Master file table",
                "$mftmirr" => "MFT mirror",
                "$logfile" => "Log file",
                "$volume" => "Volume",
                "$attrdef" => "Attribute defs",
                "$bitmap" => "Cluster bitmap",
                "$boot" => "Boot",
                "$badclus" => "Bad clusters",
                "$secure" => "Security",
                "$upcase" => "Upcase table",
                "$extend" => "Extend",
                _ => "Other",
            };
        }
        return ext switch
        {
            ".dll" => "应用程序扩展",
            ".exe" => "应用程序",
            ".sys" or ".mui" => "系统文件",
            ".log" or ".etl" => "日志",
            ".txt" => "文本",
            ".tmp" or ".temp" or ".cache" or ".bak" or ".old" => "临时",
            ".jpg" or ".jpeg" or ".png" or ".gif" or ".bmp" or ".webp" => "图片",
            ".mp4" or ".mkv" or ".mov" or ".avi" or ".wmv" => "视频",
            ".mp3" or ".wav" or ".flac" or ".aac" => "音频",
            ".doc" or ".docx" or ".xls" or ".xlsx" or ".pdf" or ".ppt" or ".pptx" => "文档",
            ".py" or ".js" or ".ts" or ".json" or ".cs" or ".cpp" or ".c" or ".h"
                or ".java" or ".go" or ".rs" or ".html" or ".css" => "代码",
            ".zip" or ".rar" or ".7z" or ".tar" or ".gz" or ".tgz" => "压缩包",
            ".msi" or ".iso" => "安装包",
            ".db" or ".sqlite" or ".dat" or ".bin" => "数据",
            ".vhd" or ".vhdx" => "硬盘映像",
            ".img" => "光盘映像",
            ".jar" => "Java 压缩包",
            ".pak" => "游戏资源包",
            ".dmp" => "转储文件",
            ".nvph" => "NVIDIA 缓存",
            ".bundl" => "资源包",
            ".ppkg" => "配置包",
            ".ress" => "Unity 资源",
            ".body" => "资源体",
            ".assets" => "资源文件",
            "" or "(无扩展名)" or "(no extension)" => NoExt,
            "$mft" => "主文件表",
            "$mftmirr" => "主文件表镜像",
            "$logfile" => "日志文件",
            "$volume" => "卷",
            "$attrdef" => "属性定义",
            "$bitmap" => "簇位图",
            "$boot" => "引导",
            "$badclus" => "坏簇",
            "$secure" => "安全描述符",
            "$upcase" => "大小写表",
            "$extend" => "扩展",
            _ => "其他",
        };
    }

    public static string TabExt => IsEn ? "Types" : "扩展名";
    public static string TabClean => IsEn ? "Clean" : "清理";
    public static string TabUninstall => IsEn ? "Uninstall" : "卸载";
    public static string UninstallRefresh => IsEn ? "Refresh" : "刷新";
    public static string UninstallRun => IsEn ? "Uninstall selected" : "卸载勾选项";
    public static string UninstallListing => IsEn ? "Listing installed apps…" : "正在列出已装软件…";
    public static string UninstallHint => IsEn
        ? "Refresh to list installed apps. Uninstall uses each app's own uninstaller (BCU engine)."
        : "点刷新列出已装软件。卸载走各软件自己的卸载程序（BCU 引擎）。";
    public static string UninstallSearchHint => IsEn ? "Search apps…" : "搜索软件…";
    public static string UninstallFiltered(int shown, int total) =>
        IsEn ? $"{shown:N0} / {total:N0} apps" : $"{shown:N0} / {total:N0} 个软件";
    public static string UninstallCount(int n) => IsEn ? $"{n:N0} apps" : $"{n:N0} 个软件";
    public static string UninstallConfirm(int n) =>
        IsEn ? $"Run the official uninstaller for {n:N0} apps? Each may show its own window."
             : $"对 {n:N0} 个软件运行官方卸载程序？每个都可能弹出自己的窗口。";
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
    public static string UninstallRunning => IsEn ? "Uninstalling…" : "正在卸载…";
    public static string UninstallDone => IsEn ? "Done" : "完成";
    public static string UninstallFailed => IsEn ? "Failed" : "失败";
    public static string UninstallWaiting => IsEn ? "Waiting" : "等待";
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
    public static string SelectSafe => IsEn ? "Select safe" : "勾选安全项";
    public static string SelectNone => IsEn ? "Clear checks" : "取消勾选";
    public static string CleanHintReady(int n, string size) =>
        IsEn ? $"Cleanable: {n:N0} items, about {size}" : $"可清理：{n:N0} 项，约 {size}";
    public static string RecycleManyConfirm(int n, string size) =>
        IsEn ? $"Move {n:N0} items ({size}) to Recycle Bin?" : $"把 {n:N0} 项（{size}）删到回收站？";
    public static string RecycleManyOk(int n) => IsEn ? $"Moved {n:N0} items" : $"已移到回收站 {n:N0} 项";
    public static string NothingSelected => IsEn ? "Nothing selected." : "没有勾选项。";
    public static string ColReason => IsEn ? "Why" : "原因";
    public static string ColName => IsEn ? "Name" : "名称";

    public static string CatCleanable => IsEn ? "Safe to clean" : "可清理";
    public static string CatLarge => IsEn ? "Largest files" : "大文件";
    public static string CatOld => IsEn ? "Old files" : "老文件";
    public static string CatDup => IsEn ? "Duplicates" : "重复文件";
    public static string CatEmpty => IsEn ? "Empty folders" : "空文件夹";
    public static string CatShortcut => IsEn ? "Broken shortcuts" : "失效快捷方式";
    public static string CatLong => IsEn ? "Long paths" : "超长路径";
    public static string CatCompare => IsEn ? "Since last scan" : "和上次比";

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

    public static string ReasonTempDir => IsEn ? "In a temp/cache folder" : "在临时/缓存目录里";
    public static string ReasonTempExt => IsEn ? "Temp / log leftover" : "临时或日志残留";
    public static string ReasonDump => IsEn ? "Crash dump" : "崩溃转储";
    public static string ReasonWinUpdate => IsEn ? "Windows update leftover" : "Windows 更新残留";
    public static string ReasonInstaller => IsEn ? "Installer in Downloads, likely safe" : "下载里的安装包，可考虑删";
    public static string ReasonOldInstaller => IsEn ? "Old disk image / installer, unused for months" : "很久没动的镜像/安装包";
    public static string ReasonVmDisk => IsEn ? "VM / WSL / emulator disk image" : "虚拟机 / WSL / 模拟器磁盘";
    public static string AskAiFolder => IsEn ? "Ask AI what this is" : "问 AI 这是什么";
    public static string ReasonRecycle => IsEn ? "Already in Recycle Bin" : "已在回收站";
    public static string ReasonLarge => IsEn ? "Among the largest files" : "占用最大的文件之一";
    public static string ReasonOld(string age) => IsEn ? $"Not modified for {age}" : $"已 {age} 未改";
    public static string ReasonEmpty => IsEn ? "Folder has no files" : "空文件夹";
    public static string ReasonBroken(string target) =>
        IsEn ? "Target missing: " + target : "目标不存在：" + target;
    public static string ReasonLong(int n) => IsEn ? $"Path {n} chars" : $"路径 {n} 字";
    public static string ReasonDupKeep => IsEn ? "Keep (shortest path)" : "保留（路径最短）";
    public static string ReasonDupExtra(string keep) =>
        IsEn ? "Same content as " + keep : "与此项相同：" + keep;
    public static string ReasonGrew(string size) => IsEn ? "Grew " + size : "多了 " + size;
    public static string ReasonShrunk(string size) => IsEn ? "Shrank " + size : "少了 " + size;
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

    public static string Category(string fileName)
    {
        if (fileName.StartsWith('$')) return IsEn ? "System" : "系统";
        var t = TypeName(System.IO.Path.GetExtension(fileName));
        if (t is "日志" or "Log") return IsEn ? "Log" : "日志";
        if (t is "临时" or "Temporary") return IsEn ? "Temporary" : "临时";
        return t;
    }
}
