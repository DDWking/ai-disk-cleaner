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
        /// <summary>卸载命令行（结构化判定「Windows 自带组件」要用它，不是靠名字猜）。</summary>
        public string UninstallString { get; set; } = "";
        public string RegistryPath { get; set; } = "";
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
        public static string AppSizeFromRecord(string size) => "~" + size + " (record)";
        public static string AppSizeUnknown => "unknown";
        public static string AppSizeMeasuredEmpty => "0 KB";
        public static string AppSizeSourceMeasured => "Source: measured by scan";
        public static string AppSizeSourceRecord => "Source: install record";
        public static string AppFootprintNotMeasured(string why) => "not measured: " + why;
        public static string AppFootprintWhyShared => "shared";
        public static string AppFootprintWhySystemDir => "system dir";
        public static string AppFootprintWhyGenericRoot => "generic root";
        public static string AppFootprintWhyNotScanned => "not scanned";
        public static string AppFootprintWhyMissing => "missing";
        public static string AppFootprintWhyUnreadable => "unreadable";
        public static string AppFootprintNotEqualFree => "footprint != freed";
        public static string UninstallConfirmSizeNote => "size is not a promise";
        public static string AppSizeColumnHeader => "Footprint";
        public static string AppSizeColumnTip => "measured / record / unknown";
        public static string AppSizeEstimated => " est.";
        public static string FootprintInstall(string size) => "install: " + size;
        public static string FootprintUserData(string size) => "userdata: " + size;
        public static string FootprintCache(string size) => "cache: " + size;
        public static string FootprintReclaimable(string size) => "reclaimable: " + size;
    }
}
