using AiDiskCleaner.Models;
using AiDiskCleaner.Services;

namespace System.Windows.Media
{
    public class ImageSource
    {
    }
}

namespace UninstallTools
{
    public class ApplicationUninstallerEntry
    {
    }
}

namespace AiDiskCleaner
{
    public static class App
    {
        public static TestSettings Settings { get; } = new();
    }

    public sealed class TestSettings
    {
        public AiProviderCfg? CurrentProvider() => null;
        public string AiModel { get; set; } = "";
    }
}

namespace AiDiskCleaner.Services
{
    public sealed class AiProviderCfg
    {
        public string BaseUrl { get; set; } = "";
    }

    public sealed class AiMsg
    {
        public string Role { get; set; } = "";
        public string Text { get; set; } = "";
    }

    public static class AiClient
    {
        public static Task<string> ChatAsync(string system, IReadOnlyList<AiMsg> turns, CancellationToken ct)
            => Task.FromResult("");
    }

    public static class Loc
    {
        public static string AiAppsSystem => "";
        public static string AiAppsPrompt(string json) => json;
        public static string AppKeepSystem => "system";
        public static string AppKeepNoUninstaller => "no uninstaller";
        public static string AppKeepCritical => "critical";
        public static string AppConsiderRunning => "running";
        public static string AppRunningWarning => "close first";
        public static string AppConsiderRunningUnknown => "state unknown";
        public static string AppRunningUnknownWarning => "verify closed";
        public static string AppRecommendBloat => "bloat";
        public static string AppConsiderLarge => "large";
        public static string AppConsiderStartup => "startup";
        public static string AppConsiderUnknown => "unknown";
        public static string AppSizeEstimated => " est.";
        public static string FootprintInstall(string size) => "install: " + size;
        public static string FootprintUserData(string size) => "userdata: " + size;
        public static string FootprintCache(string size) => "cache: " + size;
        public static string FootprintReclaimable(string size) => "reclaimable: " + size;
        public static string AiMark => "AI";
        public static string AppRecommendationLabel(AppRecommendationDecision decision) => decision.ToString();
    }
}
