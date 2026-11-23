using System.IO;
using System.Text.Json;
using AiDiskCleaner.Models;
using AiDiskCleaner.Native;

namespace AiDiskCleaner.Services;

public sealed record ReorganizeProgress(int Completed, int Total, string CurrentName);

public sealed record ReorganizeItemError(string SourcePath, string Message);

public sealed class ReorganizeExecutionResult
{
    public int MovedCount { get; set; }
    public int SkippedCount { get; set; }
    public int ShortcutsUpdated { get; set; }
    public int JunctionsCreated { get; set; }
    public int CrossDriveCount { get; set; }
    public long MovedBytes { get; set; }
    public ReorganizeUndoBatch? UndoBatch { get; set; }
    public List<ReorganizeItemError> Errors { get; } = new();
}

public sealed class ReorganizeUndoResult
{
    public bool FoundBatch { get; set; }
    public int RestoredCount { get; set; }
    public int ShortcutsRestored { get; set; }
    public int JunctionsRemoved { get; set; }
    public List<ReorganizeItemError> Errors { get; } = new();
}

/// <summary>
/// 文件夹整理的安全执行与事务撤销服务（支持同盘原子归类 + 隐藏 NTFS Junction 软链搬家 + 跨盘校验迁移）。
///
/// 安全铁律：
/// - 移动前强制复检系统/安全红线、重解析点、自身嵌套、目标同名冲突与进程运行占用；
/// - 带注册表的软件/游戏启用软链迁移时，移动后在原位自动建隐藏 NTFS Junction 桥接，失败自动回滚，零损坏注册表；
/// - 跨盘搬家采用「空间预检 → 流式临时区复制 → 文件数与字节数双重校验 → 原位重命名占用验证 → 建隐藏软链 → 释放源盘空间」事务流程；
/// - 每批操作写入本地撤销日志，一键可将同盘/跨盘/隐藏软链与快捷方式 100% 原路退回。
/// </summary>
public sealed class FolderReorganizeService
{
    private const int MaxUndoHistory = 10;
    private const long CrossDriveHeadroomBytes = 256L * 1024 * 1024; // 跨盘搬家至少保留 256MB 目标盘安全余量
    private const int CopyBufferSize = 1024 * 1024; // 1MB 流式复制缓冲区

    private readonly string _undoJournalPath;
    private readonly Func<IReadOnlyList<string>>? _shortcutSearchRootsOverride;
    private bool _undoCacheLoaded;
    private ReorganizeUndoBatch? _cachedLatestUndo;

    public FolderReorganizeService(
        string? undoJournalPath = null,
        Func<IReadOnlyList<string>>? shortcutSearchRootsOverride = null)
    {
        _undoJournalPath = string.IsNullOrWhiteSpace(undoJournalPath)
            ? Path.Combine(AppPaths.ConfigDirectory, "classify-undo.json")
            : undoJournalPath;
        _shortcutSearchRootsOverride = shortcutSearchRootsOverride;
    }

    public static void Preflight(IReadOnlyList<ClassifyItem> items)
    {
        var claimedDestinations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in items)
        {
            if (item.Executed || item.IsBlocked || item.IsKeepInPlace)
            {
                item.ConflictNote = "";
                continue;
            }

            string src = CleanListSnapshot.NormPath(item.SourcePath);
            string dst = CleanListSnapshot.NormPath(item.DestinationPath);
            string reason = ValidateMovePaths(src, dst, allowCrossDrive: item.IsCrossDrive, useJunction: item.UseJunction, expectedSizeBytes: item.SizeBytes);
            if (reason.Length == 0 && (Directory.Exists(dst) || File.Exists(dst)))
                reason = "目标目录已存在同名项";
            if (reason.Length == 0 && !claimedDestinations.Add(dst))
                reason = "同批存在重名冲突";

            item.ConflictNote = reason;
        }
    }

    /// <summary>在后台线程执行磁盘冲突与空间预检，再回到调用线程更新 ConflictNote，彻底避免卡顿 UI。</summary>
    public static async Task PreflightAsync(IReadOnlyList<ClassifyItem> items, CancellationToken ct = default)
    {
        if (items.Count == 0) return;

        var snapshots = new (int Index, bool Skip, string Src, string Dst, bool CrossDrive, bool UseJunction, long SizeBytes)[items.Count];
        for (int i = 0; i < items.Count; i++)
        {
            var it = items[i];
            bool skip = it.Executed || it.IsBlocked || it.IsKeepInPlace;
            snapshots[i] = (i, skip, skip ? "" : it.SourcePath, skip ? "" : it.DestinationPath, it.IsCrossDrive, it.UseJunction, it.SizeBytes);
        }

        var notes = await Task.Run(() =>
        {
            var results = new string[snapshots.Length];
            var claimed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var s in snapshots)
            {
                ct.ThrowIfCancellationRequested();
                if (s.Skip) { results[s.Index] = ""; continue; }
                string src = CleanListSnapshot.NormPath(s.Src);
                string dst = CleanListSnapshot.NormPath(s.Dst);
                string reason = ValidateMovePaths(src, dst, allowCrossDrive: s.CrossDrive, useJunction: s.UseJunction, expectedSizeBytes: s.SizeBytes);
                if (reason.Length == 0 && (Directory.Exists(dst) || File.Exists(dst)))
                    reason = "目标目录已存在同名项";
                if (reason.Length == 0 && !claimed.Add(dst))
                    reason = "同批存在重名冲突";
                results[s.Index] = reason;
            }
            return results;
        }, ct);

        for (int i = 0; i < items.Count && i < notes.Length; i++)
            items[i].ConflictNote = notes[i];
    }

    /// <summary>基础同盘路径校验（默认拒绝跨盘裸移动，保持既有安全契约）。</summary>
    public static string ValidateMovePaths(string sourcePath, string destinationPath)
        => ValidateMovePaths(sourcePath, destinationPath, allowCrossDrive: false, useJunction: false, expectedSizeBytes: 0);

    public static string ValidateMovePaths(
        string sourcePath, string destinationPath, bool allowCrossDrive, bool useJunction, long expectedSizeBytes = 0)
    {
        if (string.IsNullOrWhiteSpace(sourcePath) || string.IsNullOrWhiteSpace(destinationPath))
            return "路径无效";

        string src = CleanListSnapshot.NormPath(sourcePath);
        string dst = CleanListSnapshot.NormPath(destinationPath);
        if (ProtectedPaths.IsDriveRoot(src) || ProtectedPaths.IsDriveRoot(dst))
            return "盘符根目录不可移动";

        string? srcRoot = Path.GetPathRoot(src);
        string? dstRoot = Path.GetPathRoot(dst);
        if (string.IsNullOrWhiteSpace(srcRoot) || string.IsNullOrWhiteSpace(dstRoot))
            return "无法识别所在盘符";

        bool sameDrive = string.Equals(srcRoot.TrimEnd('\\'), dstRoot.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
        if (!sameDrive && !allowCrossDrive)
            return "第一期仅支持同一盘符内整理";

        if (string.Equals(src, dst, StringComparison.OrdinalIgnoreCase))
            return "已经在目标位置";

        if (FolderOrganize.IsUnderOrEqual(dst, src))
            return "不能把目录移入自身内部";

        if (ProtectedPaths.Classify(src).Guard == PathGuard.Blocked || FolderClassificationService.IsHardNoJunctionPath(src))
            return "系统核心或安全红线路径，永久禁止移动";

        if (useJunction && !JunctionNative.IsNtfsVolume(srcRoot))
            return $"原磁盘 {srcRoot.TrimEnd('\\')} 非 NTFS 格式，不支持创建透明软链";

        if (!sameDrive)
        {
            try
            {
                var dstDrive = new DriveInfo(dstRoot);
                if (!dstDrive.IsReady)
                    return $"目标盘 {dstRoot.TrimEnd('\\')} 未就绪";
                long needed = Math.Max(0, expectedSizeBytes) + CrossDriveHeadroomBytes;
                if (expectedSizeBytes > 0 && dstDrive.AvailableFreeSpace < needed)
                {
                    return $"目标盘 {dstRoot.TrimEnd('\\')} 空间不足（需约 {FileEntry.FormatSize(needed)}，当前余 {FileEntry.FormatSize(dstDrive.AvailableFreeSpace)}）";
                }
            }
            catch
            {
                return $"无法读取目标盘 {dstRoot.TrimEnd('\\')} 状态";
            }
        }

        return "";
    }

    public async Task<ReorganizeExecutionResult> ExecuteAsync(
        IReadOnlyList<ClassifyItem> items,
        bool updateShortcuts,
        string driveRoot,
        IProgress<ReorganizeProgress>? progress = null,
        CancellationToken ct = default)
    {
        Preflight(items);

        var targets = items
            .Where(i => i.IsChecked && i.CanCheck)
            .ToList();

        var result = new ReorganizeExecutionResult();
        if (targets.Count == 0) return result;

        var batch = new ReorganizeUndoBatch
        {
            BatchId = DateTime.UtcNow.ToString("yyyyMMddHHmmss") + "-" + Guid.NewGuid().ToString("N")[..6],
            ExecutedAtUtc = DateTime.UtcNow,
            DriveRoot = driveRoot,
        };

        var shortcutFiles = updateShortcuts ? DiscoverShortcutFiles() : Array.Empty<string>();
        var createdRootsThisBatch = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        await Task.Run(() =>
        {
            for (int i = 0; i < targets.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                var item = targets[i];
                progress?.Report(new ReorganizeProgress(i + 1, targets.Count, item.SourceName));

                string src = CleanListSnapshot.NormPath(item.SourcePath);
                string dst = CleanListSnapshot.NormPath(item.DestinationPath);
                bool crossDrive = item.IsCrossDrive;
                bool useJunction = item.UseJunction;

                string err = ValidateMovePaths(src, dst, allowCrossDrive: crossDrive, useJunction: useJunction, expectedSizeBytes: item.SizeBytes);
                if (err.Length > 0)
                {
                    result.SkippedCount++;
                    result.Errors.Add(new ReorganizeItemError(src, err));
                    continue;
                }

                if (!Directory.Exists(src))
                {
                    result.SkippedCount++;
                    result.Errors.Add(new ReorganizeItemError(src, "源目录已不存在"));
                    continue;
                }

                try
                {
                    var attrs = File.GetAttributes(src);
                    if ((attrs & FileAttributes.ReparsePoint) != 0)
                    {
                        result.SkippedCount++;
                        result.Errors.Add(new ReorganizeItemError(src, "重解析点 / 符号链接，已跳过"));
                        continue;
                    }
                }
                catch (Exception ex)
                {
                    result.SkippedCount++;
                    result.Errors.Add(new ReorganizeItemError(src, ex.Message));
                    continue;
                }

                // 进程占用预检：只要目录内有程序正在运行就先拦住提示关闭，绝不强行拆半套
                var runningProcs = JunctionNative.FindRunningProcessesUnder(src);
                if (runningProcs.Count > 0)
                {
                    string procList = string.Join("、", runningProcs.Take(3));
                    result.SkippedCount++;
                    result.Errors.Add(new ReorganizeItemError(src, $"检测到运行中的程序（{procList}），请先完全退出后再迁移"));
                    continue;
                }

                if (Directory.Exists(dst) || File.Exists(dst))
                {
                    result.SkippedCount++;
                    result.Errors.Add(new ReorganizeItemError(src, "目标目录已存在同名项"));
                    continue;
                }

                string? dstParent = Path.GetDirectoryName(dst);
                if (string.IsNullOrWhiteSpace(dstParent))
                {
                    result.SkippedCount++;
                    result.Errors.Add(new ReorganizeItemError(src, "无法确定目标父目录"));
                    continue;
                }

                bool createdParent = false;
                long actualMovedBytes = item.SizeBytes;
                try
                {
                    if (!Directory.Exists(dstParent))
                    {
                        Directory.CreateDirectory(dstParent);
                        createdParent = createdRootsThisBatch.Add(CleanListSnapshot.NormPath(dstParent));
                    }

                    if (!crossDrive)
                    {
                        // 1) 同盘毫秒级原子搬移（+ 可选原位创建隐藏 NTFS Junction 桥接）
                        Directory.Move(src, dst);
                        if (useJunction)
                        {
                            try
                            {
                                JunctionNative.CreateJunction(src, dst, setHidden: true);
                            }
                            catch
                            {
                                // 建软链失败立即原子回退目录，绝不留下破损安装路径
                                try { Directory.Move(dst, src); } catch { }
                                throw;
                            }
                        }
                    }
                    else
                    {
                        // 2) 跨盘安全事务搬家：量测 -> 余量校验 -> 临时区流式复制 -> 双重一致性校验 -> 原位改名锁检 -> 建隐藏软链 -> 删旧释放原盘空间
                        actualMovedBytes = ExecuteCrossDriveTransfer(
                            src, dst, useJunction,
                            copiedBytes =>
                            {
                                string totalStr = FileEntry.FormatSize(Math.Max(item.SizeBytes, copiedBytes));
                                progress?.Report(new ReorganizeProgress(
                                    i + 1, targets.Count,
                                    $"跨盘迁移 {item.SourceName}（{FileEntry.FormatSize(copiedBytes)} / {totalStr}）"));
                            },
                            ct);
                    }
                }
                catch (OperationCanceledException)
                {
                    CleanupEmptyCreatedParent(createdParent, dstParent);
                    throw;
                }
                catch (Exception ex)
                {
                    CleanupEmptyCreatedParent(createdParent, dstParent);
                    result.SkippedCount++;
                    result.Errors.Add(new ReorganizeItemError(src, ex.Message));
                    AppLog.Record("Classify", ex, "move " + LogRedactor.ScrubPath(src));
                    continue;
                }

                var undoEntry = new ReorganizeUndoEntry
                {
                    OriginalPath = src,
                    MovedPath = dst,
                    TargetCategoryRoot = CleanListSnapshot.NormPath(dstParent),
                    CreatedCategoryRoot = createdParent,
                    CreatedJunction = useJunction,
                    JunctionHidden = useJunction,
                    CrossDrive = crossDrive,
                    MovedBytes = actualMovedBytes,
                };

                // 若未使用软链（纯数据/绿软裸搬移）才更新桌面快捷方式；若已建原位软链，旧路径原生透明生效，无需破坏原快捷方式
                if (!useJunction && shortcutFiles.Count > 0)
                {
                    foreach (var lnk in shortcutFiles)
                    {
                        if (ShortcutNative.TryRewritePrefix(lnk, src, dst, out var before) && before != null)
                        {
                            undoEntry.UpdatedShortcuts.Add(before);
                            result.ShortcutsUpdated++;
                        }
                    }
                }

                batch.Entries.Add(undoEntry);
                item.Executed = true;
                item.ExecutionStatus = useJunction
                    ? (crossDrive ? $"已跨盘迁至 {dst}（原位已留隐藏软链）" : $"已迁至 {dst}（原位已留隐藏软链）")
                    : (crossDrive ? $"已跨盘整理到 {dst}" : $"已整理到 {dst}");
                result.MovedCount++;
                if (useJunction) result.JunctionsCreated++;
                if (crossDrive) result.CrossDriveCount++;
                result.MovedBytes += actualMovedBytes;
            }
        }, ct).ConfigureAwait(false);

        if (batch.Entries.Count > 0)
        {
            result.UndoBatch = batch;
            PushUndoBatch(batch);
        }

        return result;
    }

    private static long ExecuteCrossDriveTransfer(
        string src,
        string dst,
        bool createHiddenJunction,
        Action<long>? onBytesCopied,
        CancellationToken ct)
    {
        var (srcFiles, srcBytes) = MeasureDirectoryTree(src, ct);

        string dstRoot = Path.GetPathRoot(dst) ?? "";
        var dstDrive = new DriveInfo(dstRoot);
        long needed = srcBytes + CrossDriveHeadroomBytes;
        if (!dstDrive.IsReady || dstDrive.AvailableFreeSpace < needed)
        {
            throw new IOException($"目标盘 {dstRoot.TrimEnd('\\')} 空间不足（需 {FileEntry.FormatSize(needed)}，当前余 {FileEntry.FormatSize(dstDrive.AvailableFreeSpace)}）");
        }

        string stagingDst = dst + ".__dsh_staging__";
        string srcOld = src + ".__dsh_old__";
        if (Directory.Exists(stagingDst)) DeleteDirectoryForce(stagingDst);

        try
        {
            long copiedSoFar = 0;
            long lastReported = 0;
            CopyDirectoryTree(src, stagingDst, delta =>
            {
                copiedSoFar += delta;
                if (copiedSoFar - lastReported >= 4 * 1024 * 1024)
                {
                    lastReported = copiedSoFar;
                    onBytesCopied?.Invoke(copiedSoFar);
                }
            }, ct);
            onBytesCopied?.Invoke(copiedSoFar);

            // 严格一致性校验：逐项核对文件总数与总字节数
            var (dstFiles, dstBytes) = MeasureDirectoryTree(stagingDst, ct);
            if (dstFiles != srcFiles || dstBytes != srcBytes)
            {
                throw new IOException($"跨盘复制校验不一致（源 {srcFiles} 文件/{srcBytes} B，目标 {dstFiles} 文件/{dstBytes} B），已自动回滚未触碰源目录");
            }

            // 原子落位目标路径
            Directory.Move(stagingDst, dst);
        }
        catch
        {
            if (Directory.Exists(stagingDst))
                try { DeleteDirectoryForce(stagingDst); } catch { }
            throw;
        }

        // 先对源目录做原子改名锁定测试：若有任何隐藏句柄占用，此处立刻失败并清理 dst，100% 保护原目录完好
        try
        {
            Directory.Move(src, srcOld);
        }
        catch (Exception ex)
        {
            try { if (Directory.Exists(dst)) DeleteDirectoryForce(dst); } catch { }
            throw new IOException("源目录正被占用或无权限修改，已安全取消且原目录完好：" + ex.Message, ex);
        }

        if (createHiddenJunction)
        {
            try
            {
                JunctionNative.CreateJunction(src, dst, setHidden: true);
            }
            catch (Exception ex)
            {
                // 建软链失败：立刻恢复源目录，再删目标副本
                try { Directory.Move(srcOld, src); } catch { }
                try { if (Directory.Exists(dst)) DeleteDirectoryForce(dst); } catch { }
                throw new IOException("创建原位隐藏软链失败，已完整回退：" + ex.Message, ex);
            }
        }

        // 仅在目标已校验落位且隐藏软链已接管后，才删除源目录备份以真正释放原盘空间
        try
        {
            DeleteDirectoryForce(srcOld);
        }
        catch (Exception ex)
        {
            AppLog.Record("Classify", ex, "cleanup old cross-drive source " + LogRedactor.ScrubPath(srcOld));
        }

        return srcBytes;
    }

    public ReorganizeUndoBatch? LoadLatestUndoBatch()
    {
        if (_undoCacheLoaded) return _cachedLatestUndo;
        var list = LoadUndoHistory();
        _cachedLatestUndo = list.Count > 0 ? list[^1] : null;
        _undoCacheLoaded = true;
        return _cachedLatestUndo;
    }

    public async Task<ReorganizeUndoResult> UndoLatestAsync(
        IProgress<ReorganizeProgress>? progress = null,
        CancellationToken ct = default)
    {
        var history = LoadUndoHistory();
        var result = new ReorganizeUndoResult();
        if (history.Count == 0) return result;

        var batch = history[^1];
        result.FoundBatch = true;
        var remaining = new List<ReorganizeUndoEntry>();

        await Task.Run(() =>
        {
            for (int i = batch.Entries.Count - 1; i >= 0; i--)
            {
                ct.ThrowIfCancellationRequested();
                var entry = batch.Entries[i];
                string baseName = Path.GetFileName(entry.OriginalPath);
                progress?.Report(new ReorganizeProgress(batch.Entries.Count - i, batch.Entries.Count, baseName));

                if (!Directory.Exists(entry.MovedPath))
                {
                    result.Errors.Add(new ReorganizeItemError(entry.MovedPath, "待撤销目录已不在目标位置"));
                    continue;
                }

                // 进程占用预检（原软链路径与真身新路径同时检查）
                var busy = JunctionNative.FindRunningProcessesUnder(entry.MovedPath);
                busy.AddRange(JunctionNative.FindRunningProcessesUnder(entry.OriginalPath));
                if (busy.Count > 0)
                {
                    string procList = string.Join("、", busy.Distinct(StringComparer.OrdinalIgnoreCase).Take(3));
                    result.Errors.Add(new ReorganizeItemError(entry.MovedPath, $"程序正在运行（{procList}），请先退出后再撤销"));
                    remaining.Add(entry);
                    continue;
                }

                bool hasOriginalJunction = entry.CreatedJunction && JunctionNative.IsJunction(entry.OriginalPath, out _);
                if (!hasOriginalJunction && (Directory.Exists(entry.OriginalPath) || File.Exists(entry.OriginalPath)))
                {
                    result.Errors.Add(new ReorganizeItemError(entry.OriginalPath, "原位置已有非软链同名项，无法覆盖还原"));
                    remaining.Add(entry);
                    continue;
                }

                string? origParent = Path.GetDirectoryName(entry.OriginalPath);
                string? srcRoot = Path.GetPathRoot(entry.OriginalPath);
                string? dstRoot = Path.GetPathRoot(entry.MovedPath);
                bool isCrossDrive = entry.CrossDrive
                    || (!string.IsNullOrWhiteSpace(srcRoot) && !string.IsNullOrWhiteSpace(dstRoot)
                        && !string.Equals(srcRoot.TrimEnd('\\'), dstRoot.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase));

                try
                {
                    if (!string.IsNullOrWhiteSpace(origParent) && !Directory.Exists(origParent))
                        Directory.CreateDirectory(origParent);

                    if (!isCrossDrive)
                    {
                        if (hasOriginalJunction)
                            JunctionNative.DeleteJunctionOnly(entry.OriginalPath);

                        try
                        {
                            Directory.Move(entry.MovedPath, entry.OriginalPath);
                            if (hasOriginalJunction) result.JunctionsRemoved++;
                        }
                        catch
                        {
                            // 若还原移动失败，立刻补回原软链保持程序可用
                            if (hasOriginalJunction)
                            {
                                try { JunctionNative.CreateJunction(entry.OriginalPath, entry.MovedPath, setHidden: entry.JunctionHidden); } catch { }
                            }
                            throw;
                        }
                    }
                    else
                    {
                        // 跨盘撤销：先复制回原盘临时目录校验一致，再移除软链并原子复位
                        var (mFiles, mBytes) = MeasureDirectoryTree(entry.MovedPath, ct);
                        var origDrive = new DriveInfo(srcRoot!);
                        if (!origDrive.IsReady || origDrive.AvailableFreeSpace < mBytes + CrossDriveHeadroomBytes)
                            throw new IOException($"原盘 {srcRoot!.TrimEnd('\\')} 空间不足，暂无法搬回");

                        string undoStaging = entry.OriginalPath + ".__dsh_undo__";
                        if (Directory.Exists(undoStaging)) DeleteDirectoryForce(undoStaging);
                        try
                        {
                            CopyDirectoryTree(entry.MovedPath, undoStaging, null, ct);
                            var (uFiles, uBytes) = MeasureDirectoryTree(undoStaging, ct);
                            if (uFiles != mFiles || uBytes != mBytes)
                                throw new IOException("跨盘撤销校验不一致，已终止保护数据");

                            if (hasOriginalJunction)
                                JunctionNative.DeleteJunctionOnly(entry.OriginalPath);

                            try
                            {
                                Directory.Move(undoStaging, entry.OriginalPath);
                                if (hasOriginalJunction) result.JunctionsRemoved++;
                            }
                            catch
                            {
                                if (hasOriginalJunction)
                                {
                                    try { JunctionNative.CreateJunction(entry.OriginalPath, entry.MovedPath, setHidden: entry.JunctionHidden); } catch { }
                                }
                                throw;
                            }
                        }
                        catch
                        {
                            if (Directory.Exists(undoStaging))
                                try { DeleteDirectoryForce(undoStaging); } catch { }
                            throw;
                        }

                        try { DeleteDirectoryForce(entry.MovedPath); } catch { }
                    }
                }
                catch (Exception ex)
                {
                    result.Errors.Add(new ReorganizeItemError(entry.MovedPath, "还原失败：" + ex.Message));
                    remaining.Add(entry);
                    continue;
                }

                foreach (var sc in entry.UpdatedShortcuts)
                {
                    if (ShortcutNative.TryRestore(sc))
                        result.ShortcutsRestored++;
                }

                if (entry.CreatedCategoryRoot && !string.IsNullOrWhiteSpace(entry.TargetCategoryRoot)
                    && Directory.Exists(entry.TargetCategoryRoot))
                {
                    try
                    {
                        if (Directory.GetFileSystemEntries(entry.TargetCategoryRoot).Length == 0)
                            Directory.Delete(entry.TargetCategoryRoot);
                    }
                    catch { }
                }

                result.RestoredCount++;
            }
        }, ct).ConfigureAwait(false);

        if (remaining.Count == 0)
        {
            history.RemoveAt(history.Count - 1);
        }
        else
        {
            remaining.Reverse();
            batch.Entries = remaining;
        }
        SaveUndoHistory(history);
        return result;
    }

    private static (long FileCount, long TotalBytes) MeasureDirectoryTree(string rootDir, CancellationToken ct)
    {
        long files = 0;
        long bytes = 0;
        var stack = new Stack<string>();
        stack.Push(rootDir);

        while (stack.Count > 0)
        {
            ct.ThrowIfCancellationRequested();
            string dir = stack.Pop();
            foreach (var sub in Directory.EnumerateDirectories(dir))
            {
                var attrs = File.GetAttributes(sub);
                if ((attrs & FileAttributes.ReparsePoint) != 0) continue; // 不穿透嵌套软链防死循环
                stack.Push(sub);
            }
            foreach (var f in Directory.EnumerateFiles(dir))
            {
                var fi = new FileInfo(f);
                files++;
                bytes += fi.Length;
            }
        }
        return (files, bytes);
    }

    private static void CopyDirectoryTree(string srcRoot, string dstRoot, Action<long>? onBytesDelta, CancellationToken ct)
    {
        Directory.CreateDirectory(dstRoot);
        byte[] buffer = new byte[CopyBufferSize];

        var stack = new Stack<(string Src, string Dst)>();
        stack.Push((srcRoot, dstRoot));

        while (stack.Count > 0)
        {
            ct.ThrowIfCancellationRequested();
            var (curSrc, curDst) = stack.Pop();
            Directory.CreateDirectory(curDst);

            foreach (var sub in Directory.EnumerateDirectories(curSrc))
            {
                var attrs = File.GetAttributes(sub);
                if ((attrs & FileAttributes.ReparsePoint) != 0) continue;
                string name = Path.GetFileName(sub);
                stack.Push((sub, Path.Combine(curDst, name)));
            }

            foreach (var file in Directory.EnumerateFiles(curSrc))
            {
                ct.ThrowIfCancellationRequested();
                string name = Path.GetFileName(file);
                string dstFile = Path.Combine(curDst, name);

                using (var srcStream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read, CopyBufferSize, FileOptions.SequentialScan))
                using (var dstStream = new FileStream(dstFile, FileMode.CreateNew, FileAccess.Write, FileShare.None, CopyBufferSize, FileOptions.SequentialScan))
                {
                    int read;
                    while ((read = srcStream.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        ct.ThrowIfCancellationRequested();
                        dstStream.Write(buffer, 0, read);
                        onBytesDelta?.Invoke(read);
                    }
                }

                try
                {
                    File.SetLastWriteTimeUtc(dstFile, File.GetLastWriteTimeUtc(file));
                    var fAttrs = File.GetAttributes(file) & ~FileAttributes.ReadOnly;
                    if (fAttrs != FileAttributes.Normal)
                        File.SetAttributes(dstFile, fAttrs);
                }
                catch { }
            }
        }
    }

    private static void DeleteDirectoryForce(string path)
    {
        if (!Directory.Exists(path)) return;
        // 清理只读属性（如 .git pack 只读文件），防止清理旧副本时抛 UnauthorizedAccessException
        var stack = new Stack<string>();
        stack.Push(path);
        while (stack.Count > 0)
        {
            string dir = stack.Pop();
            try
            {
                var da = File.GetAttributes(dir);
                if ((da & FileAttributes.ReparsePoint) != 0)
                {
                    if (JunctionNative.IsJunction(dir, out _))
                        JunctionNative.DeleteJunctionOnly(dir);
                    continue;
                }
                if ((da & FileAttributes.ReadOnly) != 0)
                    File.SetAttributes(dir, da & ~FileAttributes.ReadOnly);
            }
            catch { }

            foreach (var sub in Directory.EnumerateDirectories(dir))
                stack.Push(sub);

            foreach (var f in Directory.EnumerateFiles(dir))
            {
                try
                {
                    var fa = File.GetAttributes(f);
                    if ((fa & FileAttributes.ReadOnly) != 0)
                        File.SetAttributes(f, fa & ~FileAttributes.ReadOnly);
                }
                catch { }
            }
        }
        Directory.Delete(path, recursive: true);
    }

    private static void CleanupEmptyCreatedParent(bool createdParent, string dstParent)
    {
        if (!createdParent || !Directory.Exists(dstParent)) return;
        try
        {
            if (Directory.GetFileSystemEntries(dstParent).Length == 0)
                Directory.Delete(dstParent);
        }
        catch { }
    }

    private IReadOnlyList<string> DiscoverShortcutFiles()
    {
        var roots = _shortcutSearchRootsOverride != null
            ? _shortcutSearchRootsOverride()
            : DefaultShortcutSearchRoots();

        var found = new List<string>();
        foreach (var root in roots)
        {
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) continue;
            try
            {
                foreach (var lnk in Directory.EnumerateFiles(root, "*.lnk", SearchOption.AllDirectories))
                    found.Add(lnk);
            }
            catch { }
        }
        return found;
    }

    private static IReadOnlyList<string> DefaultShortcutSearchRoots()
    {
        var list = new List<string>(4);
        void Add(Environment.SpecialFolder sf)
        {
            try
            {
                string p = Environment.GetFolderPath(sf);
                if (!string.IsNullOrWhiteSpace(p) && Directory.Exists(p)) list.Add(p);
            }
            catch { }
        }
        Add(Environment.SpecialFolder.DesktopDirectory);
        Add(Environment.SpecialFolder.CommonDesktopDirectory);
        Add(Environment.SpecialFolder.Programs);
        Add(Environment.SpecialFolder.CommonPrograms);
        return list;
    }

    private List<ReorganizeUndoBatch> LoadUndoHistory()
    {
        try
        {
            if (!File.Exists(_undoJournalPath)) return new();
            string json = File.ReadAllText(_undoJournalPath);
            return JsonSerializer.Deserialize<List<ReorganizeUndoBatch>>(json) ?? new();
        }
        catch
        {
            return new();
        }
    }

    private void PushUndoBatch(ReorganizeUndoBatch batch)
    {
        var history = LoadUndoHistory();
        history.Add(batch);
        while (history.Count > MaxUndoHistory)
            history.RemoveAt(0);
        SaveUndoHistory(history);
    }

    private void SaveUndoHistory(List<ReorganizeUndoBatch> history)
    {
        try
        {
            string? dir = Path.GetDirectoryName(_undoJournalPath);
            if (!string.IsNullOrWhiteSpace(dir)) Directory.CreateDirectory(dir);
            string tmp = _undoJournalPath + ".tmp";
            string json = JsonSerializer.Serialize(history, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(tmp, json);
            File.Move(tmp, _undoJournalPath, overwrite: true);
            _cachedLatestUndo = history.Count > 0 ? history[^1] : null;
            _undoCacheLoaded = true;
        }
        catch (Exception ex)
        {
            AppLog.Record("Classify", ex, "save undo history");
        }
    }
}
