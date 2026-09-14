namespace AiDiskCleaner.Models;

public enum AppRecommendationDecision
{
    /// <summary>有明确依据支持卸载（例如命中已知捆绑软件特征）。</summary>
    Recommend,
    /// <summary>有一件具体的事需要用户看一眼（例如当前正在运行）。**不是**「什么都不知道」。</summary>
    Consider,
    /// <summary>系统 / 驱动 / 运行库 / 受保护：本页不给卸载建议。</summary>
    Keep,
    /// <summary>
    /// 没有找到任何卸载依据 —— **中性**，既不说可以卸也不说别卸。
    /// 以前的实现把这一档塞进「可以考虑」，于是 200+ 行都写着
    /// 「无法确认是否正在运行」，等于用套话制造建议，本轮取消。
    /// </summary>
    Neutral,
}

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

public sealed class AppRecommendation
{
    public string AppId { get; set; } = "";
    public AppRecommendationDecision Decision { get; set; } = AppRecommendationDecision.Neutral;
    public double Confidence { get; set; }
    public string Reason { get; set; } = "";
    public string DataWarning { get; set; } = "";
    public bool FromAi { get; set; }
}
