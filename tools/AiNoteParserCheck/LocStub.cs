namespace AiDiskCleaner.Services;

/// <summary>
/// 只为编译 CleanItem.RiskText 而存在的最小桩。
/// 真正的 Loc 在界面工程里，依赖 App.Settings，测试进程里不需要。
/// </summary>
public static class Loc
{
    public static string RiskSafe => "可安全删除";
    public static string RiskConfirm => "需确认";
    public static string RiskKeep => "别删";
}
