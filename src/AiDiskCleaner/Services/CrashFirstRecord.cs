using System.IO;
using System.Text;

namespace AiDiskCleaner.Services;

/// <summary>
/// 首个原始异常的**有界**记录。
///
/// 为什么单独一个文件：2026-09-13 的 2.8.0 启动失败时，崩溃处理器自己又抛异常，
/// 形成每秒上万条的死循环，把 <c>app.log</c>（2MB×3）和 <c>crash.log</c>（512KB）
/// 全部刷穿 —— **原始异常被自己的日志风暴轮转掉了**，事后只剩一堆 NRE，
/// 完全无法定位。这个文件就是为此存在的：
/// <list type="bullet">
/// <item>每次进程运行**只写第一条**原始异常（绝不被后续风暴覆盖）；</item>
/// <item>单独文件、单独上限，不参与 app.log / crash.log 的滚动；</item>
/// <item>只滚动它自己；**不删除用户的任何旧日志或配置**；</item>
/// <item>内容过 <see cref="LogRedactor"/> 脱敏（不含密钥、Token、用户名）；</item>
/// <item>任何失败都静默返回 —— 崩溃路径里绝不能再抛。</item>
/// </list>
/// </summary>
public static class CrashFirstRecord
{
    /// <summary>这个文件自己的上限（超了就只删它自己）。</summary>
    public const long MaxFileBytes = 256 * 1024;

    /// <summary>单条记录里堆栈最多保留多少字符（够定位，又不至于把文件撑爆）。</summary>
    public const int MaxTextChars = 8000;

    static readonly object Gate = new();
    static int _saved;

    /// <summary>本进程已经记录过几条（正常只会是 0 或 1）。</summary>
    public static int SaveCount => Volatile.Read(ref _saved);

    /// <summary>
    /// 落地目录。null = 用 <see cref="AppLog.DirectoryPath"/>。
    /// **测试用**：指向不可写路径即可验证「记录失败也不会再次抛异常」。
    /// </summary>
    public static string? OverrideDirectory { get; set; }

    public static string DirectoryPath => OverrideDirectory ?? AppLog.DirectoryPath;

    public static string FilePath => Path.Combine(DirectoryPath, "first-crash.log");

    /// <summary>测试用：清空「已记录」状态并指定目录。</summary>
    public static void ResetForTest(string? directory)
    {
        lock (Gate) { _saved = 0; OverrideDirectory = directory; }
    }

    /// <summary>
    /// 记下**这一次运行的首个**原始异常。已经记过就返回 false（绝不覆盖第一现场）。
    /// 不抛异常。
    /// </summary>
    public static bool Save(Exception? ex)
    {
        if (ex is null) return false;
        lock (Gate)
        {
            if (_saved > 0) return false;      // 第一现场只写一次，风暴盖不掉
            _saved++;
            try
            {
                Directory.CreateDirectory(DirectoryPath);
                string text = LogRedactor.ScrubAll(ex.ToString());
                if (text.Length > MaxTextChars) text = text[..MaxTextChars] + "…（截断）";
                string line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff")
                    + " | pid=" + Environment.ProcessId
                    + " | first unhandled exception" + Environment.NewLine
                    + text + Environment.NewLine + Environment.NewLine;

                // 只滚动这一个文件；用户其它日志与配置一概不动
                try
                {
                    var fi = new FileInfo(FilePath);
                    if (fi.Exists && fi.Length > MaxFileBytes) fi.Delete();
                }
                catch { /* 滚动失败就继续追加，别因为清理而丢掉这次记录 */ }

                File.AppendAllText(FilePath, line, Encoding.UTF8);
                return true;
            }
            catch
            {
                // 磁盘满 / 无权限 / 路径非法：如实放弃，绝不在这里再抛
                return false;
            }
        }
    }
}
