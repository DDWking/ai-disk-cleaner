namespace AiDiskCleaner.Models;

public enum AppRecommendationDecision
{
    Recommend,
    Consider,
    Keep,
}

public enum AppRunningState
{
    Unknown,
    NotRunning,
    Running,
}

public sealed class AppRecommendation
{
    public string AppId { get; set; } = "";
    public AppRecommendationDecision Decision { get; set; } = AppRecommendationDecision.Consider;
    public double Confidence { get; set; }
    public string Reason { get; set; } = "";
    public string DataWarning { get; set; } = "";
    public bool FromAi { get; set; }
}
