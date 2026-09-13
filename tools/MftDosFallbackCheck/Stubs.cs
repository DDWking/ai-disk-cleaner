namespace AiDiskCleaner.Services;

/// <summary>
/// 界面文案桩：本检查只验证 MFT 记录解析，不验证文案，也不引入真实 Loc
/// （真实 Loc 依赖 vendor 卸载模块，离线检查工程不引用）。
/// </summary>
public static class Loc
{
    public static bool IsEn => false;
    public static string FilesIn(int n, string path) => $"{n:N0} 个文件在 {path}";
}

/// <summary>诊断日志桩：解析行为与日志无关，不落盘。</summary>
public static class AppLog
{
    public static string DirectoryPath => "";
    public static string LogPath => "";
    public static string ScanTimingPath => "";
    public static void EnsureDirectory() { }
    public static void Info(string module, string message, int? count = null) { }
    public static void Warn(string module, string message) { }
    public static void Error(string module, string message, Exception? ex = null) { }
}
