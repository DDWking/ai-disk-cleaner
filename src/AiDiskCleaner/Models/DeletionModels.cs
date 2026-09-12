namespace AiDiskCleaner.Models;

/// <summary>
/// 删除前的防护等级。注意这是「删除时」的拦截，不影响清理列表的展示与默认勾选
/// （列表口径仍由 CleanAnalyzer 的 Risk / CanDelete 决定）。
/// </summary>
public enum DeletionGuardLevel
{
    /// <summary>正常可删。</summary>
    Allowed,
    /// <summary>敏感位置（Program Files、用户配置根等），用户已确认整批后仍可删。</summary>
    NeedsConfirm,
    /// <summary>硬拦截：Windows / System32 / WinSxS / 盘符根 / 系统文件 / 重解析点。</summary>
    Blocked,
}

/// <summary>
/// 一个删除项的结局。预检阶段用 Ready 之外的取值给出「预计结局」，
/// 执行阶段再给出真实结局，两边同一套枚举，界面不需要翻译两遍。
/// </summary>
public enum DeletionOutcome
{
    /// <summary>预检通过，可以删。</summary>
    Ready,
    /// <summary>已移入回收站。</summary>
    Recycled,
    /// <summary>文件不存在（扫描后已被别处删掉）。</summary>
    NotFound,
    /// <summary>路径发生变化（不再是同一个对象）。</summary>
    PathChanged,
    /// <summary>文件已被修改（大小或修改时间变了）。</summary>
    Modified,
    /// <summary>权限不足。</summary>
    AccessDenied,
    /// <summary>被系统保护。</summary>
    Protected,
    /// <summary>文件正被占用。</summary>
    InUse,
    /// <summary>用户主动跳过。</summary>
    SkippedByUser,
    /// <summary>父目录已被选中，子项无需重复删除。</summary>
    RedundantChild,
    /// <summary>回收站操作失败。</summary>
    Failed,
}

/// <summary>
/// 文件对象的唯一身份。同一路径可能是不同对象（改了名/被替换），
/// 只比路径不足以证明「还是扫描时那个文件」。
/// </summary>
public readonly record struct FileIdentity(ulong VolumeSerial, ulong FileIdLow, ulong FileIdHigh)
{
    public bool IsKnown => VolumeSerial != 0 || FileIdLow != 0 || FileIdHigh != 0;
}

/// <summary>请求删除的一项：带上扫描时的身份快照，供预检比对。</summary>
public sealed class DeletionTarget
{
    public string Path { get; set; } = "";
    /// <summary>界面显示用的名字（文件名或软件名）。</summary>
    public string Label { get; set; } = "";
    public bool IsDirectory { get; set; }
    /// <summary>扫描时的大小（未分配时为 -1，表示不比对大小）。</summary>
    public long ExpectedSize { get; set; } = -1;
    public DateTime ExpectedModified { get; set; }
    public FileIdentity? ExpectedIdentity { get; set; }
    /// <summary>
    /// 扫描快照的大小/修改时间是否「精确可信」。
    ///
    /// 只有递归扫描（直接读的就是 FileInfo）才算精确。MFT 的 $FILE_NAME 时间戳
    /// 只在目录项变更时才刷新，跟资源管理器显示的 $STANDARD_INFORMATION 时间
    /// 本来就可能差一截，拿它当硬证据会误报「文件被改过」。
    /// 不精确时仍然比对，但只降级成「需要再确认」，不当成硬拦截。
    /// </summary>
    public bool SnapshotIsExact { get; set; }
    /// <summary>回写用：来自清理列表的条目。</summary>
    public CleanItem? Source { get; set; }

    public static DeletionTarget From(string path, string? label = null, CleanItem? source = null)
        => new()
        {
            Path = path ?? "",
            Label = label ?? System.IO.Path.GetFileName(path ?? "") ?? "",
            IsDirectory = source?.IsDirectory ?? false,
            Source = source,
        };
}

/// <summary>预检 + 执行共用的单项记录。</summary>
public sealed class DeletionPreflightItem
{
    public DeletionTarget Target { get; set; } = new();
    public DeletionGuardLevel Guard { get; set; } = DeletionGuardLevel.Allowed;
    public DeletionOutcome Outcome { get; set; } = DeletionOutcome.Ready;
    /// <summary>用户可读原因（界面直接显示）。</summary>
    public string Reason { get; set; } = "";
    /// <summary>技术说明（日志/悬停）。</summary>
    public string Tech { get; set; } = "";
    /// <summary>身份快照，执行时再次比对。</summary>
    public FileIdentity? ObservedIdentity { get; set; }
    public long ObservedSize { get; set; }
    public DateTime ObservedModified { get; set; }

    public string Path => Target.Path;
    public string Label => string.IsNullOrWhiteSpace(Target.Label) ? Target.Path : Target.Label;
    public bool Ready => Outcome == DeletionOutcome.Ready;
    /// <summary>能否进入执行阶段。</summary>
    public bool CanExecute => Ready && Guard != DeletionGuardLevel.Blocked;
    public bool NeedsUserConfirm => Guard == DeletionGuardLevel.NeedsConfirm;
}

/// <summary>预检汇总。任何一项失败都能从 <see cref="Items"/> 里查到细节。</summary>
public sealed class DeletionPreflightResult
{
    public List<DeletionPreflightItem> Items { get; } = new();
    public DateTime StartedAt { get; set; } = DateTime.Now;
    public TimeSpan Elapsed { get; set; }
    /// <summary>是否因为用户在确认框里点了「继续」而放行敏感项。</summary>
    public bool SensitiveConfirmed { get; set; }

    public int TotalCount => Items.Count;
    public int ReadyCount => Items.Count(x => x.CanExecute);
    public int BlockedCount => Items.Count(x => x.Guard == DeletionGuardLevel.Blocked
                                                || x.Outcome is DeletionOutcome.Protected or DeletionOutcome.RedundantChild);
    public int RedundantCount => Items.Count(x => x.Outcome == DeletionOutcome.RedundantChild);
    public int MissingCount => Items.Count(x => x.Outcome == DeletionOutcome.NotFound);
    public int ChangedCount => Items.Count(x => x.Outcome is DeletionOutcome.PathChanged or DeletionOutcome.Modified);
    public int InUseCount => Items.Count(x => x.Outcome == DeletionOutcome.InUse);
    public int DeniedCount => Items.Count(x => x.Outcome == DeletionOutcome.AccessDenied);
    public int InvalidCount => Items.Count(x => x.Outcome == DeletionOutcome.Failed);
    public int SensitiveCount => Items.Count(x => x.NeedsUserConfirm);
    public long ReadyBytes => Items.Where(x => x.CanExecute).Sum(x => Math.Max(0, x.Target.ExpectedSize));

    public List<DeletionPreflightItem> Executable => Items.Where(x => x.CanExecute).ToList();
    public List<DeletionPreflightItem> Refused => Items.Where(x => !x.CanExecute).ToList();
}

/// <summary>一次删除的完整计划。预检结果 + 用户是否已确认 + 是否允许敏感项。</summary>
public sealed class DeletionPlan
{
    public DeletionPreflightResult Preflight { get; set; } = new();
    /// <summary>用户在确认框点过「继续」。</summary>
    public bool UserConfirmed { get; set; }
    /// <summary>把敏感项（Program Files 等）也纳入执行。</summary>
    public bool AllowSensitive { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    /// <summary>真正会送去回收站的项：预检通过，且敏感项已获用户确认。</summary>
    public List<DeletionPreflightItem> ToExecute => Preflight.Items
        .Where(x => x.CanExecute && (AllowSensitive || !x.NeedsUserConfirm))
        .ToList();

    /// <summary>预检通过但因为没确认敏感项而被挡下的（执行时记成「用户跳过」）。</summary>
    public List<DeletionPreflightItem> HeldBack => Preflight.Items
        .Where(x => x.CanExecute && !AllowSensitive && x.NeedsUserConfirm)
        .ToList();

    public bool HasAnythingToDo => ToExecute.Count > 0;
    /// <summary>确认框要用：需要额外提醒的项。</summary>
    public List<DeletionPreflightItem> NeedsAttention => Preflight.Items
        .Where(x => x.Outcome != DeletionOutcome.Ready && x.Outcome != DeletionOutcome.RedundantChild)
        .ToList();
}

/// <summary>单项执行结果。失败不会吞掉其他项的细节。</summary>
public sealed class DeletionItemResult
{
    public string Path { get; set; } = "";
    public string Label { get; set; } = "";
    public DeletionOutcome Outcome { get; set; } = DeletionOutcome.Failed;
    /// <summary>用户可读原因。</summary>
    public string Message { get; set; } = "";
    /// <summary>技术细节：异常类型 + 原生错误码。</summary>
    public string Detail { get; set; } = "";
    public long FreedBytes { get; set; }
    public TimeSpan Elapsed { get; set; }

    public bool Succeeded => Outcome == DeletionOutcome.Recycled;
    public string OutcomeText => DeletionText.Outcome(Outcome);
}

/// <summary>一批删除的汇总结果。</summary>
public sealed class DeletionBatchResult
{
    public List<DeletionItemResult> Results { get; } = new();
    public DateTime StartedAt { get; set; } = DateTime.Now;
    public TimeSpan Elapsed { get; set; }
    /// <summary>安全取消：已处理的项仍有完整结果。</summary>
    public bool Canceled { get; set; }

    public int Recycled => Count(DeletionOutcome.Recycled);
    public int NotFound => Count(DeletionOutcome.NotFound);
    public int PathChanged => Count(DeletionOutcome.PathChanged);
    public int Modified => Count(DeletionOutcome.Modified);
    public int AccessDenied => Count(DeletionOutcome.AccessDenied);
    public int Protected => Count(DeletionOutcome.Protected);
    public int InUse => Count(DeletionOutcome.InUse);
    public int Skipped => Count(DeletionOutcome.SkippedByUser);
    public int Redundant => Count(DeletionOutcome.RedundantChild);
    public int Failed => Count(DeletionOutcome.Failed);
    public int Total => Results.Count;
    public long FreedBytes => Results.Sum(x => Math.Max(0, x.FreedBytes));

    public int Count(DeletionOutcome outcome) => Results.Count(x => x.Outcome == outcome);

    /// <summary>真正没做成、需要告诉用户原因的项（不含用户主动跳过和父子去重）。</summary>
    public List<DeletionItemResult> Problems => Results
        .Where(x => x.Outcome is DeletionOutcome.Failed or DeletionOutcome.AccessDenied
            or DeletionOutcome.InUse or DeletionOutcome.Protected or DeletionOutcome.PathChanged
            or DeletionOutcome.Modified)
        .ToList();

    public List<string> RecycledPaths => Results.Where(x => x.Succeeded).Select(x => x.Path).ToList();
}

/// <summary>结局 → 用户可读文案。放在模型层，方便离线测试覆盖全部取值。</summary>
public static class DeletionText
{
    public static string Outcome(DeletionOutcome o) => o switch
    {
        DeletionOutcome.Ready => "可删除",
        DeletionOutcome.Recycled => "已移入回收站",
        DeletionOutcome.NotFound => "文件已不存在",
        DeletionOutcome.PathChanged => "路径已变化",
        DeletionOutcome.Modified => "文件已被修改",
        DeletionOutcome.AccessDenied => "权限不足",
        DeletionOutcome.Protected => "受系统保护",
        DeletionOutcome.InUse => "文件正在使用",
        DeletionOutcome.SkippedByUser => "用户跳过",
        DeletionOutcome.RedundantChild => "父目录已选中",
        DeletionOutcome.Failed => "删除失败",
        _ => "未知",
    };
}
