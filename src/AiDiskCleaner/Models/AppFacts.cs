namespace AiDiskCleaner.Models;

// 这个文件以前还装着「卸载建议」那两个类型（AppRecommendationDecision / AppRecommendation）。
// 卸载页改成**只讲事实**（名称 / 安装日期 / 用途 / 占用 / 操作）之后，那套档位在生产里
// 没有任何调用方，已整体删除。剩下的是卸载页真正在用的两个事实枚举。

public enum AppRunningState
{
    Unknown,
    NotRunning,
    Running,
}

/// <summary>
/// 占用数字的来源。**必须显式区分**：磁盘占用不是内存，未测得也不能显示成 0。
/// </summary>
public enum AppFootprintSource
{
    /// <summary>用扫描结果按安装目录实测出来的。</summary>
    Measured = 0,
    /// <summary>安装记录里软件自己写的估计值（EstimatedSize），不是实测。</summary>
    InstallRecord = 1,
    /// <summary>两样都没有 —— 照实说未知，不臆造。</summary>
    Unknown = 2,
}
