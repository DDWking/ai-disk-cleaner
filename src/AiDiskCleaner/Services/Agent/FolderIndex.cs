using System.Security.Cryptography;
using System.Text;
using AiDiskCleaner.Models;

namespace AiDiskCleaner.Services.Agent;

/// <summary>
/// folderId ↔ FileEntry 的映射。
///
/// folderId = "f_" + SHA256(规范化相对路径) 的前 12 位十六进制，
/// 同一台机器上跨扫描稳定（相对路径不变，ID 就不变），便于缓存和用户偏好；
/// 但它**不是路径**：任何操作都必须回到这里取 <see cref="FileEntry"/>。
/// </summary>
public sealed class FolderIndex
{
    public const string RootId = "f_root";

    private const int IdHexLength = 12;

    private readonly Dictionary<string, FileEntry> _byId = new(StringComparer.Ordinal);
    private readonly Dictionary<FileEntry, string> _relPath = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<FileEntry, string> _idByEntry = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<FileEntry, int> _depth = new(ReferenceEqualityComparer.Instance);

    public FileEntry Root { get; }

    public int FolderCount => _byId.Count;

    public FolderIndex(FileEntry root)
    {
        Root = root ?? throw new ArgumentNullException(nameof(root));
        Build();
    }

    /// <summary>按 ID 取目录；根目录固定为 f_root。</summary>
    public bool TryGet(string? folderId, out FileEntry entry)
    {
        entry = null!;
        if (string.IsNullOrWhiteSpace(folderId)) return false;
        return _byId.TryGetValue(folderId, out entry!);
    }

    /// <summary>目录的 folderId；不在索引里（文件、FilesGroup、重解析点子树）返回 null。</summary>
    public string? GetFolderId(FileEntry entry)
        => _idByEntry.TryGetValue(entry, out string? id) ? id : null;

    /// <summary>相对盘根的路径，如 Users\%USER%\AppData\Local（根为空串）。</summary>
    public string GetRelPath(FileEntry entry)
    {
        if (_relPath.TryGetValue(entry, out string? rel)) return rel;

        // 索引里没有的节点（文件/被排除的子树）按父链临时算，不写缓存。
        var segments = new List<string>();
        var cursor = entry;
        while (cursor is not null && !ReferenceEquals(cursor, Root))
        {
            segments.Add(cursor.Name);
            cursor = cursor.Parent;
        }

        if (!ReferenceEquals(cursor, Root)) return "";
        segments.Reverse();
        return string.Join('\\', segments);
    }

    /// <summary>相对盘根的层级；根为 0。</summary>
    public int GetDepth(FileEntry entry)
        => _depth.TryGetValue(entry, out int depth) ? depth : 0;

    /// <summary>规范化相对路径 → 稳定 ID。空路径返回 f_root。</summary>
    public static string MakeFolderId(string relativePath)
    {
        string normalized = Normalize(relativePath);
        if (normalized.Length == 0) return RootId;

        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        string hex = Convert.ToHexString(hash).ToLowerInvariant();
        return "f_" + hex[..IdHexLength];
    }

    /// <summary>ID 是否可能是本索引发出的（格式校验，不保证存在）。</summary>
    public static bool LooksLikeFolderId(string? value)
    {
        if (string.IsNullOrEmpty(value)) return false;
        if (value == RootId) return true;
        if (value.Length != 2 + IdHexLength || value[0] != 'f' || value[1] != '_') return false;

        for (int i = 2; i < value.Length; i++)
        {
            char c = value[i];
            if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f'))) return false;
        }

        return true;
    }

    internal static string Normalize(string? relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath)) return "";
        return relativePath.Replace('/', '\\').Trim('\\').ToLowerInvariant();
    }

    private void Build()
    {
        var stack = new Stack<(FileEntry Node, string RelPath, int Depth)>();
        stack.Push((Root, "", 0));
        Add(Root, RootId, "", 0);

        // 迭代 DFS：十万级目录也不会爆栈。
        while (stack.Count > 0)
        {
            var (node, relPath, depth) = stack.Pop();
            var children = node.ChildList;
            for (int i = 0; i < children.Count; i++)
            {
                var child = children[i];
                if (!IsRealFolder(child)) continue;

                string childRel = relPath.Length == 0 ? child.Name : relPath + "\\" + child.Name;
                string id = MakeFolderId(childRel);
                Add(child, id, childRel, depth + 1);
                stack.Push((child, childRel, depth + 1));
            }
        }
    }

    private void Add(FileEntry node, string id, string relPath, int depth)
    {
        // 相对路径理论上唯一；真撞了就保留先到的，避免重复 ID 覆盖。
        _byId.TryAdd(id, node);
        _relPath[node] = relPath;
        _idByEntry[node] = id;
        _depth[node] = depth;
    }

    internal static bool IsRealFolder(FileEntry entry)
        => entry.IsDirectory && !entry.IsFilesGroup && !entry.IsReparsePoint;
}
