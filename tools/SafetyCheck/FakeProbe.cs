using AiDiskCleaner.Models;
using AiDiskCleaner.Services;

namespace SafetyCheck;

/// <summary>
/// 假的文件系统探针：让删除预检/执行器能在没有真实磁盘的情况下被完整测试，
/// 包括「预检之后文件被改动」「删一半失败」「用户取消」这些真盘上很难复现的情况。
/// </summary>
public sealed class FakeProbe : IFileSystemProbe
{
    public sealed class Node
    {
        public bool Exists = true;
        public bool IsDirectory;
        public bool ReparsePoint;
        public bool System;
        public bool InUse;
        public bool AccessDenied;
        public long Size;
        public DateTime Modified = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        public FileIdentity Identity = new(1, 1, 0);
        /// <summary>探针被调用几次（用于验证「执行前重新探测」）。</summary>
        public int Probes;
    }

    private readonly Dictionary<string, Node> _nodes = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>删除时抛异常，用来模拟回收站失败。</summary>
    public readonly Dictionary<string, Exception> ThrowOnDelete = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>删除时把该路径标记为「仍然存在」，用来模拟系统没删掉。</summary>
    public readonly HashSet<string> StickAround = new(StringComparer.OrdinalIgnoreCase);

    public List<string> DeletedPaths { get; } = new();

    public Node Set(string path, Action<Node>? configure = null)
    {
        var n = new Node { IsDirectory = false };
        configure?.Invoke(n);
        _nodes[Norm(path)] = n;
        return n;
    }

    public Node? Get(string path) => _nodes.TryGetValue(Norm(path), out var n) ? n : null;

    public static string Norm(string path) => path.Replace('/', '\\').TrimEnd('\\').ToUpperInvariant();

    public FileProbeInfo Probe(string path)
    {
        if (!_nodes.TryGetValue(Norm(path), out var n))
            return FileProbeInfo.Missing("not found");
        n.Probes++;
        if (!n.Exists) return FileProbeInfo.Missing("not found");
        return new FileProbeInfo
        {
            Exists = true,
            IsDirectory = n.IsDirectory,
            IsReparsePoint = n.ReparsePoint,
            IsSystem = n.System,
            Size = n.Size,
            AllocatedSize = n.Size,
            Modified = n.Modified,
            Identity = n.Identity,
            InUse = n.InUse,
            AccessDenied = n.AccessDenied,
        };
    }

    /// <summary>轻量身份查询：只认已知节点，不产生 Probe 计数（硬链接合并只该问这个）。</summary>
    public FileIdentity? TryGetIdentity(string path)
    {
        if (!_nodes.TryGetValue(Norm(path), out var n)) return null;
        if (!n.Exists) return null;
        return n.Identity.IsKnown ? n.Identity : null;
    }

    public void SendToRecycle(IReadOnlyList<string> paths)
    {
        foreach (var p in paths)
        {
            if (ThrowOnDelete.TryGetValue(Norm(p), out var ex)) throw ex;
            DeletedPaths.Add(p);
            if (_nodes.TryGetValue(Norm(p), out var n) && !StickAround.Contains(Norm(p)))
                n.Exists = false;
        }
    }
}
