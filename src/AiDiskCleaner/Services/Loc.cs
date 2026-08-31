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

    public static string Category(string fileName)
    {
        if (fileName.StartsWith('$')) return IsEn ? "System" : "系统";
        var t = TypeName(System.IO.Path.GetExtension(fileName));
        if (t is "日志" or "Log") return IsEn ? "Log" : "日志";
        if (t is "临时" or "Temporary") return IsEn ? "Temporary" : "临时";
        return t;
    }
}
