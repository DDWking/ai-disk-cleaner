using System.IO;

namespace AiDiskCleaner.Services;

/// <summary>
/// 配置文件位置。集中在一处，是为了让「API Key 加密」这类安全行为能被离线测试覆盖 ——
/// 测试可以把目录指到临时目录，绝不碰真实用户配置。
/// </summary>
public static class AppPaths
{
    private static string DefaultDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "DashaoHuo");

    /// <summary>测试用覆盖目录。null = 用默认位置。生产代码不要设置它。</summary>
    public static string? OverrideDirectory { get; set; }

    /// <summary>
    /// 配置目录环境变量（便携 / 诊断用）：设了就用它，**不动用户的真实配置**。
    /// 例如排查问题或做离线验证时，指向一个空目录就得到一个「未配置 AI」的干净实例。
    /// </summary>
    public const string ConfigDirEnvVar = "DASHAOHUO_CONFIG_DIR";

    public static string ConfigDirectory =>
        OverrideDirectory
        ?? NullIfBlank(Environment.GetEnvironmentVariable(ConfigDirEnvVar))
        ?? DefaultDir;

    static string? NullIfBlank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    /// <summary>普通设置（不含任何 API Key）。</summary>
    public static string SettingsFile => Path.Combine(ConfigDirectory, "settings.json");

    /// <summary>DPAPI 加密后的密钥仓库。</summary>
    public static string SecretsFile => Path.Combine(ConfigDirectory, "secrets.dat");

    /// <summary>用户对文件夹用途的手动纠正（明文，不含任何机密：只有路径与用途名）。</summary>
    public static string FolderPurposeFile => Path.Combine(ConfigDirectory, "folder-purpose.json");
}
