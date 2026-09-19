using System.Globalization;

namespace AiDiskCleaner.Services;

/// <summary>
/// 识别缓存的**确定性键**：跨进程稳定哈希。
///
/// 为什么不用 <see cref="object.GetHashCode"/>：字符串哈希在每个进程都会随机化，
/// 落盘的键换个进程就对不上。这里用 FNV-1a，纯函数、跨进程/跨机器一致。
/// 键里**不含扫描代次**：用户关心的是「这个路径是什么」，重扫一代它还是同一个目录；
/// 但**含内容指纹、提示词/语义版本与配置签名** —— 内容、版本、配置任一变化都会让旧结论失效。
///
/// （原来和逐项 AI 的服务挤在同一个文件里；那一套删掉之后，它被
/// <see cref="FolderPurposeService"/> 留着继续用，所以单独搬出来。）
/// </summary>
public static class RecognitionKey
{
    /// <summary>确定性 64 位哈希（FNV-1a，逐段加分隔符，避免拼接歧义）。</summary>
    public static long Stable(params string[] parts)
    {
        unchecked
        {
            ulong h = 14695981039346656037UL;
            foreach (string part in parts)
            {
                string s = part ?? "";
                foreach (char c in s)
                {
                    h ^= (byte)c;
                    h *= 1099511628211UL;
                    h ^= (byte)(c >> 8);
                    h *= 1099511628211UL;
                }
                h ^= 0x1F;              // 段分隔符：("a","bc") 与 ("ab","c") 不会撞
                h *= 1099511628211UL;
            }
            return (long)h;
        }
    }

    /// <summary>哈希的固定 16 位十六进制写法（跨进程稳定，不受区域设置影响）。</summary>
    public static string Hex(long value) => value.ToString("x16", CultureInfo.InvariantCulture);

    /// <summary>规范化路径（大小写不敏感，去除末尾反斜杠与正斜杠差异）。</summary>
    public static string PathKey(string? path)
        => CleanListSnapshot.NormPath(path).ToLowerInvariant();
}
