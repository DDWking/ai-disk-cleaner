using AiDiskCleaner.Models;
using AiDiskCleaner.Services;

namespace FolderDeleteCheck;

/// <summary>
/// 离线假探针：让「预览 → 重查 → 执行」的每一条分支都能在没有真实磁盘的情况下验证，
/// 包括「执行前身份被换掉」「路径突然消失」「重解析祖先」这些真盘上很难复现的情况。
/// </summary>
public sealed class FakeProbe : IFileSystemProbe
{
    public sealed class Node
    {
        public bool Exists = true;
        public bool IsDirectory = true;
        public bool ReparsePoint;
        public bool System;
        public bool InUse;
        public bool AccessDenied;
        public long Size;
        public DateTime Modified = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        public FileIdentity Identity = new(1, 1, 0);
    }

    private readonly Dictionary<string, Node> _nodes = new(StringComparer.OrdinalIgnoreCase);

    public static string Norm(string path) => path.Replace('/', '\\').TrimEnd('\\').ToUpperInvariant();

    public Node Set(string path, Action<Node>? configure = null)
    {
        var n = new Node();
        configure?.Invoke(n);
        _nodes[Norm(path)] = n;
        return n;
    }

    public Node? Get(string path) => _nodes.TryGetValue(Norm(path), out var n) ? n : null;

    public void MarkDeleted(string path)
    {
        if (_nodes.TryGetValue(Norm(path), out var n)) n.Exists = false;
    }

    public FileProbeInfo Probe(string path)
    {
        if (!_nodes.TryGetValue(Norm(path), out var n)) return FileProbeInfo.Missing("not found");
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

    public FileIdentity? TryGetIdentity(string path)
        => _nodes.TryGetValue(Norm(path), out var n) && n.Exists ? n.Identity : null;

    public void SendToRecycle(IReadOnlyList<string> paths)
    {
        foreach (var p in paths) MarkDeleted(p);
    }
}

/// <summary>
/// 测试用的「一行整理对象」投影。只携带选择状态；<see cref="AiSaysDelete"/> 是故意加的
/// 「AI 说可以删」字段，用来断言选择逻辑完全无视 AI。
/// </summary>
public sealed class Pick
{
    public bool IsSelected { get; set; }
    public string FullPath { get; set; } = "";
    public string Name { get; set; } = "";
    public long Size { get; set; }
    public bool AiSaysDelete { get; set; }

    public FolderPick ToPick() => new(IsSelected, FullPath, Name, Size);
}

/// <summary>
/// 假的「必须进回收站」操作。可以按路径控制「回收站是否可用」与 shell 返回的状态，
/// 并记录到底被调用了几次 —— 用来断言「确认前取消 = 零后端调用」与「回收站不可用 = 不调用」。
/// </summary>
public sealed class FakeRecycle : IFolderRecycleOperation
{
    public FakeProbe? Probe { get; set; }
    public bool DefaultCanRecycle { get; set; } = true;
    public bool RemovePathOnCall { get; set; } = true;
    public Exception? ThrowOnRecycle { get; set; }

    public readonly Dictionary<string, bool> CanRecycleMap = new(StringComparer.OrdinalIgnoreCase);
    public readonly Dictionary<string, RecycleShellResult> Results = new(StringComparer.OrdinalIgnoreCase);
    public readonly List<string> Calls = new();

    public bool CanRecycle(string path, out string tech)
    {
        if (CanRecycleMap.TryGetValue(FakeProbe.Norm(path), out var ok))
        {
            tech = ok ? "" : "fake: no usable Recycle Bin on this volume";
            return ok;
        }
        tech = DefaultCanRecycle ? "" : "fake: no usable Recycle Bin on this volume";
        return DefaultCanRecycle;
    }

    public RecycleShellResult Recycle(string path)
    {
        Calls.Add(path);
        if (ThrowOnRecycle != null) throw ThrowOnRecycle;
        if (RemovePathOnCall) Probe?.MarkDeleted(path);
        return Results.TryGetValue(FakeProbe.Norm(path), out var r)
            ? r
            : RecycleShellResult.Ok("fake IFileOperation + FOFX_RECYCLEONDELETE");
    }
}
