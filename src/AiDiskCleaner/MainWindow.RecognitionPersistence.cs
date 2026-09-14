using AiDiskCleaner.Models;
using AiDiskCleaner.Services;

namespace AiDiskCleaner;

/// <summary>
/// 识别结果持久化的**界面接线**（新增 partial，不改既有 MainWindow 文件）。
///
/// 目标：**下次打开应用时，上次认出来的文件夹信息还能直接看到，且不再发任何 AI 请求。**
///
/// 落盘本身在 <see cref="RecognitionStore"/>，服务侧读写已在
/// <see cref="ItemAiService"/> / <see cref="FolderPurposeService"/> 内完成：
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
///       侧栏/逐项行若要立刻显示上次结论，用
///       <see cref="TryRestorePurpose"/> / <see cref="TryRestoreItemAi"/>。</item>
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
            _itemAi.AttachRecognitionStore(_recognitionStore);
            _folderPurpose.AttachRecognitionStore(_recognitionStore);
            AppLog.Info("Recognition", "op=persist stage=init entries=" + _recognitionStore.Count
                + " items=" + _recognitionStore.ItemCount
                + " purposes=" + _recognitionStore.PurposeCount);
        }
        catch (Exception ex)
        {
            // 建库失败 ⇒ 本次会话退化为纯内存识别，不抛给启动流程。
            _recognitionStore = null;
            _itemAi.AttachRecognitionStore(null);
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
        if (restored > 0)
            AppLog.Info("Recognition", $"op=persist stage=restore objects={restored}");
        return restored;
    }

    /// <summary>
    /// 【逐项行钩子，可选】某一项还没有结果时，先把落盘的描述性结论贴出来（不发请求、不写盘）。
    /// 返回是否恢复成功。整理来源的对象经此恢复后**依然没有**勾选 / 定位清理明细的能力
    /// （能力只看 <see cref="ItemAiView.Source"/>，落盘内容里也根本没有判定/选择字段）。
    /// </summary>
    private bool TryRestoreItemAi(ItemAiView view, ItemAiRequest request)
    {
        if (_recognitionStore == null || view == null || request == null) return false;
        if (view.HasResult || view.IsBusy) return false;

        var restored = _itemAi.TryGetRestored(request, AiConfigSignature());
        if (restored == null || !ItemAiPrompt.IsUsable(restored)) return false;

        view.Result = restored;                  // FromCache=true，界面会如实标「缓存」
        view.Status = ItemAiStatus.Done;
        view.IsExpanded = true;
        return true;
    }

    /// <summary>
    /// 【侧栏行钩子，可选】某一目录还没有结论时，用落盘缓存把它先显示出来（不发请求）。
    /// 返回 null 表示没有可用结论（调用方按原来的「未识别」呈现）。
    /// </summary>
    private FolderPurposeResult? TryRestorePurpose(FileEntry dir)
    {
        if (_recognitionStore == null || dir == null) return null;
        var id = CurrentFolderId(dir);
        var sum = FolderPurposeRules.Summarize(dir, id, DepthOf(dir), RelativeOf(dir));
        return _folderPurpose.TryGetCached(id, sum, AiConfigSignature()) is { HasConclusion: true } hit
            ? hit
            : null;
    }
}
