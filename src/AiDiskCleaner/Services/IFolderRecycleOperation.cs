namespace AiDiskCleaner.Services;

/// <summary>一次「送回收站」shell 调用的状态。**只有成功才可能声称回收**。</summary>
public enum RecycleShellStatus
{
    /// <summary>shell 报告删除完成（并且我们用 FOFX_RECYCLEONDELETE 要求了必须回收）。</summary>
    Recycled,
    /// <summary>shell 报告失败。</summary>
    Failed,
    /// <summary>shell 报告操作被中断（fAnyOperationsAborted）。</summary>
    Aborted,
}

/// <summary>shell 调用的原始结果：状态 + 原生错误码 + 简短说明。</summary>
public readonly record struct RecycleShellResult(RecycleShellStatus Status, int HResult, string Message)
{
    public static RecycleShellResult Ok(string message = "") => new(RecycleShellStatus.Recycled, 0, message);
    public static RecycleShellResult Fail(int hr, string message) => new(RecycleShellStatus.Failed, hr, message);
    public static RecycleShellResult Abort(int hr, string message) => new(RecycleShellStatus.Aborted, hr, message);
    public bool Recycled => Status == RecycleShellStatus.Recycled;
}

/// <summary>
/// 「删除必须进回收站」的操作抽象。
///
/// 契约里最重要的一条：**实现不得提供任何永久删除路径**。
/// 做不到回收就返回失败（或抛异常），由上层如实报告，绝不静默退化成永久删除。
/// 抽成接口是为了让文件夹删除的执行/重查/取消能被离线测试，不需要真删任何东西。
/// </summary>
public interface IFolderRecycleOperation
{
    /// <summary>
    /// 该路径所在卷是否具备可用回收站。返回 false 时上层**一律不执行删除**，
    /// 这样连「能不能静默永久删除」的机会都不给。
    /// </summary>
    bool CanRecycle(string path, out string tech);

    /// <summary>
    /// 把单个路径送入回收站。任何失败都通过 <see cref="RecycleShellResult"/> 返回；
    /// 只在调用本身无法完成（COM 激活失败等）时抛异常。
    /// </summary>
    RecycleShellResult Recycle(string path);
}
