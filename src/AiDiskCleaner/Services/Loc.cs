using AiDiskCleaner.Models;

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
    public static string Theme => IsEn ? "Theme" : "主题";
    public static string Language => IsEn ? "Language" : "语言";
    public static string ThemeTerminal => IsEn ? "Terminal" : "终端";
    public static string ThemeMono => IsEn ? "Black & White" : "黑白";
    public static string ThemeCyber => IsEn ? "Cyberpunk" : "赛博朋克";
    public static string LangZh => "中文";
    public static string LangEn => "English";
    public static string Close => IsEn ? "Close" : "关闭";
    public static string AboutTitle => IsEn ? "About" : "关于";
    public static string SettingsTitle => IsEn ? "Settings" : "设置";
    public static string AboutBody => IsEn
        ? "A fast NTFS disk scanner. Open source under MIT."
        : "NTFS 磁盘秒扫工具，MIT 开源。";
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
    public static string Analyze => IsEn ? "Analyze" : "分析";
    public static string Analyzing => IsEn ? "Analyzing…" : "正在分析…";
    public static string RecycleSelected => IsEn ? "Recycle selected" : "删除勾选项";
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
    public static string ReasonInstaller => IsEn ? "Installer in Downloads" : "下载里的安装包";
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
