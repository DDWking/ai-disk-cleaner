namespace AiDiskCleaner.Services;

/// <summary>
/// 组合根：把真实扫描器接上 <see cref="ScanCoordinator"/>。
///
/// 单独放一个文件，是为了让 <see cref="ScanCoordinator"/> 自己不引用
/// <see cref="MftScanService"/> / <see cref="RecursiveScanService"/> ——
/// 那样它才能被离线测试整条换掉。
/// </summary>
public static class ScanServices
{
    public static ScanCoordinator CreateDefault()
        => new(new MftScanService(), new RecursiveScanService());
}
