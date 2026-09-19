using AiDiskCleaner.Models;
using AiDiskCleaner.Services;

namespace AiDiskCleaner;

/// <summary>
/// 识别结果持久化的**界面接线**（新增 partial，不改既有 MainWindow 文件）。
///
/// 目标：**下次打开应用时，上次认出来的文件夹信息还能直接看到，且不再发任何 AI 请求。**
///
/// 落盘本身在 <see cref="RecognitionStore"/>，服务侧读写已在
/// <see cref="FolderPurposeService"/> 内完成：
/// <list type="bullet">
/// <item>识别成功 → 服务内部 <c>Store</c> 写穿到落盘缓存（**无需界面钩子**）；</item>
/// <item>查内存缓存 → 命中落盘缓存就直接返回（**不会发请求**，也不会写盘）。</item>
/// </list>
/// 界面只需要两处接线：
/// <list type="number">
/// <item><b>构造钩子</b>：<c>_folderPurpose</c> 建好、<c>RebuildOrganize()</c> 之前调用一次
///       <see cref="InitRecognitionPersistence"/>；</item>
/// <item><b>可见恢复钩子</b>（可选，纯展示）：整理树重建后再调用
///       <see cref="RestoreRecognizedFolders"/>，把落盘结论贴回尚未有结论的对象；
///       侧栏行若要立刻显示上次结论，用 <see cref="TryRestorePurpose"/>。</item>
/// </list>
/// 配置文件位置跟随 <see cref="AppPaths.ConfigDirectory"/>（档案/配置目录天然隔离）；
/// 建库或读盘失败只会退化成「没有跨重启记忆」，绝不影响启动与本次识别。
/// </summary>
public partial class MainWindow
{
    /// <summary>跨重启识别缓存。构造期建立；建库失败时为 null（功能退化为纯内存）。</summary>
    private RecognitionStore? _recognitionStore;

    /// <summary>识别缓存实际路径（诊断用；未接上时为空串）。</summary>
    private string RecognitionStorePath => _recognitionStore?.Path ?? "";

    /// <summary>
    /// 【构造钩子】建立并接上落盘缓存。
    ///
    /// 位置：`MainWindow()` 里 `_folderPurpose = new FolderPurposeService(...)` 之后、
    /// `RebuildOrganize()` 之前。放在 `RebuildOrganize()` 之前，整理树第一次
    /// <c>LocalRecognize</c> 时就能直接命中落盘结论（**不发请求**）。
    /// </summary>
    private void InitRecognitionPersistence()
    {
        try
        {
            _recognitionStore = new RecognitionStore();
            _folderPurpose.AttachRecognitionStore(_recognitionStore);
            AppLog.Info("Recognition", "op=persist stage=init entries=" + _recognitionStore.Count
                + " purposes=" + _recognitionStore.PurposeCount
                + " ai=" + _recognitionStore.AiPurposeCount);
        }
        catch (Exception ex)
        {
            // 建库失败 ⇒ 本次会话退化为纯内存识别，不抛给启动流程。
            _recognitionStore = null;
            _folderPurpose.AttachRecognitionStore(null);
            AppLog.Record("Recognition", ex, "init persistence");
        }
    }

    /// <summary>
    /// 【重建钩子，可选】整理树重建后，把落盘结论贴回**还没有结论**的对象。
    ///
    /// 说明：`RebuildOrganize` 内部的 <c>LocalRecognize</c> 走 <c>TryGetCached</c>，
    /// 本来就会命中落盘缓存；本方法用于「重建发生在接库之前」或需要显式补一次的场景。
    /// 只读、只贴描述性结论，不发请求、不动任何选择/删除状态。返回补上的条数。
    /// </summary>
    private int RestoreRecognizedFolders()
    {
        if (_recognitionStore == null) return 0;
        int restored = 0;
        string cfg = AiConfigSignature();
        foreach (var node in _organizeAll)
        {
            if (node.HasConclusion) continue;
            var sum = FolderPurposeRules.Summarize(node.Dir, node.Id, node.Depth, node.RelativePath);
            if (_folderPurpose.TryGetCached(node.Id, sum, cfg) is not { HasConclusion: true } hit) continue;
            node.Apply(hit);
            restored++;
        }
        restored += RestoreAiPurposes();
        if (restored > 0)
            AppLog.Info("Recognition", $"op=persist stage=restore objects={restored}");
        return restored;
    }

    /// <summary>
    /// 把上次 Jev 归类的展示名贴回**还没有结论**的对象。
    /// 本地已经认出来的行不覆盖（本地优先）；贴上的同时标「问过了」，
    /// 所以再点「识别这一屏」不会把同一条再问一遍。
    /// 只读、不发请求、不动勾选。返回贴上的条数。
    /// </summary>
    private int RestoreAiPurposes()
    {
        int restored = 0;
        foreach (var node in _organizeAll)
            if (TryRestoreAiPurpose(node)) restored++;
        if (restored > 0)
            AppLog.Info("Recognition", $"op=persist stage=restore-ai objects={restored}");
        return restored;
    }

    /// <summary>
    /// 单条恢复。材料化子目录时也走这里，所以展开一层不必等整树重建。
    /// </summary>
    private bool TryRestoreAiPurpose(OrganizeNode node)
    {
        if (_recognitionStore == null || node.HasConclusion || node.BatchAsked) return false;
        if (!_recognitionStore.TryGetAiPurpose(node.FullPath, out var name, out var unsure)) return false;
        node.RestoreBatchPurpose(name, unsure);
        return true;
    }

    /// <summary>
    /// 把这一批刚写上的 Jev 标签落盘。空名字不写。
    /// 失败只记日志，不影响这次归类已经贴到界面上的结果。
    /// </summary>
    private void PersistAiPurposes(IEnumerable<OrganizeNode> nodes)
    {
        if (_recognitionStore == null) return;
        int saved = 0;
        try
        {
            foreach (var n in nodes)
            {
                if (n.BatchPurpose.Length == 0) continue;
                _recognitionStore.PutAiPurpose(n.FullPath, n.BatchPurpose, n.BatchPurposeUnsure);
                saved++;
            }
            if (saved > 0)
                AppLog.Info("Recognition", $"op=persist stage=save-ai objects={saved}");
        }
        catch (Exception ex)
        {
            AppLog.Record("Recognition", ex, "save ai-purpose");
        }
    }

    /// <summary>
    /// 「忘掉 AI 标签」：只清 Jev 那一半（落盘 + 当前树上的展示），
    /// 本地规则 / 用户纠正 / 勾选都不动。清完这一屏可以重新问。
    /// </summary>
    private int ForgetAiPurposes()
    {
        int cleared = 0;
        foreach (var node in _organizeAll)
        {
            if (node.BatchPurpose.Length == 0 && !node.BatchAsked) continue;
            node.ClearBatchPurpose();
            cleared++;
        }
        try { _recognitionStore?.ClearAiPurposes(); }
        catch (Exception ex) { AppLog.Record("Recognition", ex, "clear ai-purpose"); }
        if (cleared > 0)
            AppLog.Info("Recognition", $"op=persist stage=forget-ai objects={cleared}");
        return cleared;
    }

    /// <summary>
    /// 【侧栏行钩子，可选】某一目录还没有结论时，用落盘缓存把它先显示出来（不发请求）。
    /// 返回 null 表示没有可用结论（调用方按原来的「未识别」呈现）。
    /// </summary>
    private FolderPurposeResult? TryRestorePurpose(FileEntry dir)
    {
        if (_recognitionStore == null || dir == null) return null;
        // 2.11 起没有侧栏，也不再有 CurrentFolderId / DepthOf；
        // 标识与整理页同一口径：路径 + 扫描代次，深度由相对路径层数得出。
        var id = new FolderId(dir.FullPath, NodeGeneration);
        string rel = RelativeOf(dir);
        int depth = 0;
        if (rel.Length > 0)
            depth = rel.Split('\\', StringSplitOptions.RemoveEmptyEntries).Length;
        var sum = FolderPurposeRules.Summarize(dir, id, depth, rel);
        return _folderPurpose.TryGetCached(id, sum, AiConfigSignature()) is { HasConclusion: true } hit
            ? hit
            : null;
    }
}
