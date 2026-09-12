using System.Diagnostics;
using System.IO;

namespace AiDiskCleaner.Services;

/// <summary>在资源管理器中定位的结果。界面据此给出**简短**提示，不弹模态框。</summary>
public enum RevealKind
{
    /// <summary>打开了一个文件夹。</summary>
    OpenedDirectory,
    /// <summary>打开了所在文件夹并选中了文件。</summary>
    SelectedFile,
    /// <summary>路径不存在（可能刚被删或已移动）。</summary>
    NotFound,
    /// <summary>启动资源管理器失败（被策略拦等）。</summary>
    Failed,
    /// <summary>路径为空。</summary>
    NoPath,
}

public readonly record struct RevealResult(RevealKind Kind, string Target, string Message)
{
    public bool Ok => Kind is RevealKind.OpenedDirectory or RevealKind.SelectedFile;
    public static RevealResult NoPath() => new(RevealKind.NoPath, "", "");
}

/// <summary>
/// 「在资源管理器中打开」的**唯一**实现。
///
/// 以前这段逻辑在三处各写了一遍，且都有毛病：
/// <list type="bullet">
/// <item><c>OpenExplorer(path, directory)</c> 里的 <c>|| directory</c> 会去打开一个**并不存在**的目录；</item>
/// <item>三处都不区分「路径不存在」，失败了只是静默无反应；</item>
/// <item>都用字符串拼 <c>"/select,\"" + path + "\""</c>，路径里有逗号时 Explorer 会解析歪。</item>
/// </list>
///
/// 这里统一成一处，并做到：
/// <list type="bullet">
/// <item>只用**结构化参数**（<c>ArgumentList</c>），不拼命令行、不经过 cmd/PowerShell；</item>
/// <item>目录 → 打开它；文件 → 打开所在文件夹并**选中**该文件（绝不执行文件）；</item>
/// <item>路径必须真实存在才启动，否则返回可读原因；</item>
/// <item>空格/中文/逗号/长路径都由参数转义与引号规则处理。</item>
/// </list>
/// 纯逻辑，可离线测试：测试通过 <see cref="LaunchOverride"/> 换掉启动动作。
/// </summary>
public static class ShellReveal
{
    /// <summary>
    /// 测试缝：参数是 (可执行文件, 结构化参数)。返回是否启动成功。
    /// 生产环境走 <see cref="Process.Start(ProcessStartInfo)"/>。
    /// </summary>
    public static Func<string, IReadOnlyList<string>, bool>? LaunchOverride { get; set; }

    /// <summary>explorer.exe 定位文件用的开关。单独一个参数，避免逗号路径被误解。</summary>
    internal const string SelectSwitch = "/select,";

    /// <summary>在资源管理器中定位一条路径。目录就打开它，文件就选中它。</summary>
    public static RevealResult Reveal(string? path)
    {
        string target = (path ?? "").Trim().Trim('"');
        if (target.Length == 0) return RevealResult.NoPath();

        try
        {
            if (Directory.Exists(target))
            {
                return Launch(target, new[] { target })
                    ? new RevealResult(RevealKind.OpenedDirectory, target, "")
                    : Fail(target, "无法启动资源管理器");
            }

            if (File.Exists(target))
            {
                // 文件：打开所在文件夹并选中它。**不执行**文件本身。
                // 把 "/select," 和路径合成一个参数，这样 .NET 的转义会给出
                // `/select,"C:\a,b.txt"` —— 带逗号的路径也能被 Explorer 正确解析。
                string arg = SelectSwitch + "\"" + target + "\"";
                return Launch(target, new[] { arg })
                    ? new RevealResult(RevealKind.SelectedFile, target, "")
                    : Fail(target, "无法启动资源管理器");
            }

            // 路径不在了：如实说，不去猜一个父目录悄悄打开。
            return Fail(target, "路径不存在，可能已被移动或删除", RevealKind.NotFound);
        }
        catch (Exception ex)
        {
            Log(target, ex);
            return new RevealResult(RevealKind.Failed, target, "打不开资源管理器：" + ex.GetType().Name);
        }
    }

    static bool Launch(string target, string[] args)
    {
        if (LaunchOverride != null) return LaunchOverride("explorer.exe", args);
        try
        {
            var psi = new ProcessStartInfo("explorer.exe") { UseShellExecute = false };
            foreach (var a in args) psi.ArgumentList.Add(a);
            using var p = Process.Start(psi);
            return true;
        }
        catch (Exception ex)
        {
            Log(target, ex);
            return false;
        }
    }

    static RevealResult Fail(string target, string message, RevealKind kind = RevealKind.Failed)
    {
        AppLog.Write(new LogEntry(DateTime.UtcNow, LogLevel.Debug, "UI", "", "reveal",
            $"{kind} | {LogRedactor.ScrubPath(target)}"));
        return new RevealResult(kind, target, message);
    }

    static void Log(string target, Exception ex)
        => AppLog.Write(new LogEntry(DateTime.UtcNow, LogLevel.Debug, "UI", "", "reveal-failed",
            $"{ex.GetType().Name}: {ex.Message} | {LogRedactor.ScrubPath(target)}"));
}
