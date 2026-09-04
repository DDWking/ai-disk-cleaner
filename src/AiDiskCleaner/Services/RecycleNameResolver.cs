using System.IO;
using System.Text;

namespace AiDiskCleaner.Services;

/// <summary>
/// 回收站里的文件在磁盘上不叫原名：$Rxxxxx 是内容，$Ixxxxx 是元数据。
/// 用户看到 "$R3F2A.docx" 根本认不出是自己删的哪个文件，
/// 这里解析配对的 $I 元数据把原始路径取回来。
/// $I 格式：版本(8) 大小(8) 删除时间(8) [路径长度(4)] 路径(UTF-16LE)
/// </summary>
public static class RecycleNameResolver
{
    static readonly Dictionary<string, string?> Cache = new(StringComparer.OrdinalIgnoreCase);
    static readonly object Gate = new();

    public static bool IsRecycleItem(string? path)
        => !string.IsNullOrEmpty(path)
           && path!.Contains(@"$Recycle.Bin", StringComparison.OrdinalIgnoreCase);

    /// <summary>$I 元数据文件本身，不该当独立条目列出来。</summary>
    public static bool IsMetaFile(string? path)
    {
        string name = Path.GetFileName(path ?? "");
        return name.StartsWith("$I", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>给 $R/$I 文件取原始完整路径，取不到返回 null。</summary>
    public static string? OriginalPath(string? recyclePath)
    {
        if (string.IsNullOrEmpty(recyclePath)) return null;
        string name = Path.GetFileName(recyclePath);
        if (name.Length < 3) return null;
        if (name[0] != '$' || (name[1] != 'R' && name[1] != 'I')) return null;

        lock (Gate)
        {
            if (Cache.TryGetValue(recyclePath, out var hit)) return hit;
        }

        string? result = null;
        try
        {
            string? dir = Path.GetDirectoryName(recyclePath);
            if (!string.IsNullOrEmpty(dir))
            {
                // 配对：把 $R/$I 前缀换成 $I 就是元数据文件
                string infoPath = Path.Combine(dir, "$I" + name[2..]);
                if (File.Exists(infoPath)) result = ParseInfo(infoPath);
            }
        }
        catch { }

        lock (Gate)
        {
            Cache[recyclePath] = result;
        }
        return result;
    }

    static string? ParseInfo(string infoPath)
    {
        try
        {
            using var fs = new FileStream(infoPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var br = new BinaryReader(fs);
            if (fs.Length < 32) return null;

            long version = br.ReadInt64();
            _ = br.ReadInt64();          // 文件大小
            _ = br.ReadInt64();          // 删除时间

            string raw;
            if (version >= 2 && fs.Length >= 32)
            {
                int len = br.ReadInt32();
                if (len <= 0 || len > 32767) return null;
                if (fs.Length - fs.Position < len * 2L) return null;
                byte[] bytes = br.ReadBytes(len * 2);
                raw = Encoding.Unicode.GetString(bytes);
            }
            else
            {
                // 老格式：固定 520 字节的路径区
                int take = (int)Math.Min(520, fs.Length - fs.Position);
                byte[] bytes = br.ReadBytes(take);
                raw = Encoding.Unicode.GetString(bytes);
            }

            string path = raw.TrimEnd('\0').Trim();
            return string.IsNullOrEmpty(path) ? null : path;
        }
        catch
        {
            return null;
        }
    }
}
