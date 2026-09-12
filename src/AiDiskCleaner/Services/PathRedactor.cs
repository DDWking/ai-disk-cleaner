using System.Text.RegularExpressions;

namespace AiDiskCleaner.Services;

/// <summary>
/// 发给外部 AI 之前的路径脱敏。
///
/// 默认只送：文件类型、文件大小、脱敏后的路径、本地规则给的原因。
/// 完整路径必须用户明确允许（设置里 <c>AiSendFullPaths</c>）才会发出去。
///
/// 例子：
/// <code>
/// C:\Users\User\AppData\Local\Temp\x.tmp   →   &lt;UserProfile&gt;\AppData\Local\Temp\x.tmp
/// C:\Users\Bob\Documents\a.docx            →   C:\Users\&lt;User&gt;\Documents\a.docx
/// </code>
///
/// 注意：这只管「发出去」，本地判定（保护路径、删除预检）永远用真实路径。
/// </summary>
public static class PathRedactor
{
    public const string UserProfileToken = "<UserProfile>";
    public const string UserToken = "<User>";
    public const string MachineToken = "<PC>";

    private static readonly string[] ProfilePrefixes = BuildProfilePrefixes();

    private static string[] BuildProfilePrefixes()
    {
        var list = new List<string>();
        void Add(Environment.SpecialFolder folder)
        {
            try
            {
                string p = Environment.GetFolderPath(folder);
                if (!string.IsNullOrWhiteSpace(p)) list.Add(p);
            }
            catch { }
        }
        Add(Environment.SpecialFolder.UserProfile);
        Add(Environment.SpecialFolder.LocalApplicationData);
        Add(Environment.SpecialFolder.ApplicationData);
        Add(Environment.SpecialFolder.CommonApplicationData);
        return list.Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(x => x.Length)
            .ToArray();
    }

    /// <summary>任意用户的目录段：C:\Users\Bob\… / C:\Documents and Settings\Bob\…</summary>
    private static readonly Regex UserSegment = new(
        @"(?<head>\\Users\\|\\Documents and Settings\\)(?<name>[^\\/:*?""<>|]+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>把一条本地路径脱敏。null / 空串原样返回。</summary>
    public static string Redact(string? path)
    {
        if (string.IsNullOrEmpty(path)) return path ?? "";
        string s = path;

        // 1) 当前用户目录（长的先替换，避免 AppData 被 UserProfile 吃掉）
        foreach (var prefix in ProfilePrefixes)
        {
            if (prefix.Length > 0 && s.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                s = UserProfileToken + s[prefix.Length..];
                break;
            }
        }

        // 2) 其他用户的 \Users\<名字>\ 段
        try
        {
            s = UserSegment.Replace(s, m => m.Groups["head"].Value + UserToken);
        }
        catch { /* 正则不该失败；真失败就保留原样，由调用方决定要不要发 */ }

        // 3) 机器名
        string machine = SafeMachineName();
        if (machine.Length > 3)
        {
            try { s = Regex.Replace(s, Regex.Escape(machine), MachineToken, RegexOptions.IgnoreCase); }
            catch { }
        }

        // 4) 当前用户名单独出现（比如路径里用做父目录名但不在 Users 下）
        string user = SafeUserName();
        if (user.Length > 2)
        {
            try { s = Regex.Replace(s, Regex.Escape(user), UserToken, RegexOptions.IgnoreCase); }
            catch { }
        }

        return s;
    }

    /// <summary>批量脱敏，保持顺序。</summary>
    public static IReadOnlyList<string> RedactAll(IEnumerable<string> paths)
        => paths.Select(Redact).ToList();

    /// <summary>
    /// 自检：这串文本里还有没有没脱干净的个人信息。
    /// 用于「发给 AI 之前」和「写日志之前」的兜底断言。
    /// </summary>
    public static bool IsRedacted(string? text)
    {
        if (string.IsNullOrEmpty(text)) return true;
        string user = SafeUserName();
        if (user.Length > 2 && text.Contains(user, StringComparison.OrdinalIgnoreCase)) return false;
        string machine = SafeMachineName();
        if (machine.Length > 3 && text.Contains(machine, StringComparison.OrdinalIgnoreCase)) return false;
        string home = SafeUserProfile();
        if (home.Length > 3 && text.Contains(home, StringComparison.OrdinalIgnoreCase)) return false;
        return true;
    }

    /// <summary>
    /// 按设置决定送真实路径还是脱敏路径。
    /// 这是唯一应该被业务代码调用的入口，避免有人忘了看设置。
    /// </summary>
    public static string Outbound(string? path)
        => App.Settings.AiSendFullPaths ? (path ?? "") : Redact(path);

    private static string SafeUserName()
    {
        try { return Environment.UserName ?? ""; } catch { return ""; }
    }

    private static string SafeMachineName()
    {
        try { return Environment.MachineName ?? ""; } catch { return ""; }
    }

    private static string SafeUserProfile()
    {
        try { return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) ?? ""; } catch { return ""; }
    }
}
