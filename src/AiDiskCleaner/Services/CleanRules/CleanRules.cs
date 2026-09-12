using System.IO;
using AiDiskCleaner.Models;
using AiDiskCleaner.Native;

namespace AiDiskCleaner.Services.CleanRules;

/// <summary>
/// 临时文件 / 缓存 / 回收站 / 崩溃转储 / 安装包缓存。
/// 就是从原来 <c>CleanAnalyzer.FillCleanable</c> 原样搬过来的判定顺序 ——
/// 顺序不能动，因为先命中的规则决定分类和风险，改了默认勾选就会变。
/// </summary>
public sealed class TempCacheRule : ICleanRule
{
    public string Name => "temp-cache";
    public string Category => "temp";
    public CleanPurpose Purpose => CleanPurpose.Temp;
    public CleanRisk DefaultRisk => CleanRisk.Confirm;
    public EvidenceLevel Evidence => EvidenceLevel.Heuristic;
    public bool CanDelete => true;
    public bool SupportsCancellation => false;

    public void Evaluate(CleanRuleContext ctx, ICleanRuleSink sink)
    {
        foreach (var f in ctx.Files)
        {
            if (!CleanRuleHelpers.CanOffer(f)) continue;
            string path = CleanRuleHelpers.Lower(f.FullPath);
            // 回收站归 RecycleBinRule 管（它要解析 $I 元数据）。这里直接跳过，
            // 保证「回收站分支优先」的判定顺序和重构前完全一致。
            if (path.Contains(@"$recycle.bin", StringComparison.OrdinalIgnoreCase)) continue;
            string ext = Path.GetExtension(f.Name);
            string? reason = null;
            string group;
            // 收紧后的默认档：没有明确证据就不说"可安全删除"。
            // 误标安全是清理工具最贵的错误，宁可让用户自己看一眼。
            var risk = CleanRisk.Confirm;
            string? displayName = null;
            EvidenceLevel evidence = EvidenceLevel.Heuristic;
            // 用途跟着判定分支走：每一支都是不同的证据。
            var purpose = CleanPurpose.Temp;

            if (ext.Equals(".dmp", StringComparison.OrdinalIgnoreCase) || path.Contains(@"\minidump\") || path.Contains(@"\crashdumps\"))
            {
                // 崩溃转储：明确可删
                reason = Loc.ReasonDump;
                group = Loc.GroupDump;
                risk = CleanRisk.Safe;
                evidence = EvidenceLevel.Signature;
                purpose = CleanPurpose.Dump;
            }
            else if (AppSignatures.IsSafeCache(path))
            {
                // 认得出来的应用缓存（60+ 条签名，带 Safe 标记）
                reason = AppSignatures.PlainNote(path) ?? Loc.ReasonTempDir;
                group = Loc.GroupTemp;
                risk = CleanRisk.Safe;
                evidence = EvidenceLevel.Signature;
                // 具体是浏览器缓存还是应用缓存，交给签名分类细化（CleanItemFactory）
                purpose = CleanPurpose.AppCache;
            }
            else if (path.Contains(@"\windows\softwaredistribution\download\") || path.Contains(@"\windows\temp\"))
            {
                // Windows 更新缓存 / 系统临时目录
                reason = Loc.ReasonWinUpdate;
                group = Loc.GroupTemp;
                risk = CleanRisk.Safe;
                evidence = EvidenceLevel.Signature;
                purpose = CleanPurpose.Temp;
            }
            else if (CleanRuleHelpers.LooksLikeTempDir(path) && (CleanRuleHelpers.TempExt.Contains(ext) || f.Name.StartsWith('~') || f.Size == 0))
            {
                // 只是"路径像临时目录"，没匹配到已知签名 -> 需确认
                reason = Loc.ReasonTempDir;
                group = Loc.GroupTemp;
                purpose = CleanPurpose.Temp;
            }
            else if (CleanRuleHelpers.TempExt.Contains(ext) && CleanRuleHelpers.LooksLikeTempDir(path))
            {
                reason = Loc.ReasonTempExt;
                group = Loc.GroupTemp;
                purpose = CleanPurpose.Temp;
            }
            else if (CleanRuleHelpers.InstallExt.Contains(ext) && path.Contains(@"\downloads\") && f.Size >= 20L * 1024 * 1024)
            {
                reason = Loc.ReasonInstaller;
                group = Loc.GroupInstaller;
                purpose = CleanPurpose.Installer;
            }
            else if ((ext.Equals(".tmp", StringComparison.OrdinalIgnoreCase) || ext.Equals(".temp", StringComparison.OrdinalIgnoreCase)
                      || ext.Equals(".log", StringComparison.OrdinalIgnoreCase))
                     && f.Size >= 8L * 1024 * 1024 && !CleanRuleHelpers.LooksUnsafe(path))
            {
                // 单个大临时文件，没匹配到签名 -> 需确认
                reason = Loc.ReasonTempExt;
                group = Loc.GroupTemp;
                // 大日志归「应用日志」，其余仍算临时文件
                purpose = ext.Equals(".log", StringComparison.OrdinalIgnoreCase)
                    ? CleanPurpose.AppLog
                    : CleanPurpose.Temp;
            }
            else continue;

            sink.Add(new CleanRuleHit
            {
                Entry = f,
                Target = CleanRuleTarget.Cleanable,
                Reason = reason!,
                Group = group,
                Purpose = purpose,
                Risk = risk,
                CanDelete = true,
                // **默认一律不勾选**（包括原来标成 Safe 的那些）。
                // 「允许进入清理流程」不等于「用户已经同意删」；由用户自己勾，
                // 也避免新扫描沿用上一轮的大批量选择。
                Selected = false,
                Evidence = evidence,
                DisplayName = displayName,
            });
        }

        foreach (var d in ctx.Dirs)
        {
            if (!CleanRuleHelpers.CanOffer(d)) continue;
            string path = CleanRuleHelpers.Lower(d.FullPath);
            if (path.EndsWith(@"\windows\temp") || path.EndsWith(@"\appdata\local\temp")
                || AppSignatures.IsSafeCache(path))
            {
                sink.Add(new CleanRuleHit
                {
                    Entry = d,
                    Target = CleanRuleTarget.Cleanable,
                    Reason = AppSignatures.PlainNote(path) ?? Loc.ReasonTempDir,
                    Group = Loc.GroupTemp,
                    Purpose = CleanPurpose.Temp,
                    Risk = CleanRisk.Confirm,
                    CanDelete = true,
                    Selected = false,
                    Evidence = EvidenceLevel.Signature,
                });
            }
        }
    }
}

/// <summary>回收站单独成规则：它有自己的元数据解析，行为也最需要单独测。</summary>
public sealed class RecycleBinRule : ICleanRule
{
    public string Name => "recycle-bin";
    public string Category => "recycle";
    public CleanPurpose Purpose => CleanPurpose.Recycle;
    public CleanRisk DefaultRisk => CleanRisk.Confirm;
    public EvidenceLevel Evidence => EvidenceLevel.Verified;
    public bool CanDelete => true;
    public bool SupportsCancellation => false;

    public void Evaluate(CleanRuleContext ctx, ICleanRuleSink sink)
    {
        foreach (var f in ctx.Files)
        {
            string path = CleanRuleHelpers.Lower(f.FullPath);
            if (!path.Contains(@"$recycle.bin", StringComparison.OrdinalIgnoreCase)) continue;
            if (!CleanRuleHelpers.CanOffer(f)) continue;
            // $I 是元数据（每个被删文件一个，几十字节），不当条目列出来
            if (RecycleNameResolver.IsMetaFile(f.FullPath)) continue;

            string? original = RecycleNameResolver.OriginalPath(f.FullPath);
            string? displayName = null;
            string reason;
            if (!string.IsNullOrEmpty(original))
            {
                displayName = Path.GetFileName(original);
                reason = Loc.ReasonRecycleNamed(displayName!);
            }
            else
            {
                reason = Loc.ReasonRecycle;
            }

            sink.Add(new CleanRuleHit
            {
                Entry = f,
                Target = CleanRuleTarget.Cleanable,
                Reason = reason,
                Group = Loc.GroupRecycle,
                Purpose = CleanPurpose.Recycle,
                Risk = CleanRisk.Confirm,
                CanDelete = true,
                Selected = false,
                Evidence = EvidenceLevel.Verified,
                DisplayName = displayName,
            });
        }
    }
}

/// <summary>大文件：只列不勾，让用户自己判断。</summary>
public sealed class LargeFileRule : ICleanRule
{
    private const int MaxHits = 80;

    public string Name => "large-files";
    public string Category => "large";
    public CleanPurpose Purpose => CleanPurpose.Large;
    public CleanRisk DefaultRisk => CleanRisk.Confirm;
    public EvidenceLevel Evidence => EvidenceLevel.Heuristic;
    public bool CanDelete => true;
    /// <summary>「大」本身不是可删理由：签名不得把它降级到「建议清理」。</summary>
    public bool RiskIsAuthoritative => true;
    public bool SupportsCancellation => false;

    public void Evaluate(CleanRuleContext ctx, ICleanRuleSink sink)
    {
        foreach (var f in ctx.Files.Where(CleanRuleHelpers.CanList).OrderByDescending(x => x.Size).Take(MaxHits))
        {
            var hint = KnownPaths.LargeHint(f);
            sink.Add(new CleanRuleHit
            {
                Entry = f,
                Target = CleanRuleTarget.Large,
                Reason = hint?.Reason ?? Loc.ReasonLarge,
                Group = hint?.Group ?? Loc.GroupLarge,
                // 大文件永远是「大文件」用途，不因为签名命中就变成缓存类
                Purpose = CleanPurpose.Large,
                Risk = CleanRisk.Confirm,
                CanDelete = true,
                Selected = false,
                Evidence = hint != null ? EvidenceLevel.Signature : EvidenceLevel.Heuristic,
            });
        }
    }
}

/// <summary>一年没动过的大文件。</summary>
public sealed class OldFileRule : ICleanRule
{
    private const int MaxHits = 80;

    public string Name => "old-files";
    public string Category => "old";
    public CleanPurpose Purpose => CleanPurpose.Old;
    public CleanRisk DefaultRisk => CleanRisk.Confirm;
    public EvidenceLevel Evidence => EvidenceLevel.Heuristic;
    public bool CanDelete => true;
    /// <summary>「久未改动」本身不是可删理由：签名不得把它降级到「建议清理」。</summary>
    public bool RiskIsAuthoritative => true;
    public bool SupportsCancellation => false;

    public void Evaluate(CleanRuleContext ctx, ICleanRuleSink sink)
    {
        var cutoff = DateTime.Now.AddYears(-1);
        foreach (var f in ctx.Files
                     .Where(x => CleanRuleHelpers.CanList(x) && x.Modified != DateTime.MinValue
                                 && x.Modified < cutoff && x.Size >= 8L * 1024 * 1024)
                     .OrderBy(x => x.Modified)
                     .Take(MaxHits))
        {
            sink.Add(new CleanRuleHit
            {
                Entry = f,
                Target = CleanRuleTarget.Old,
                Reason = Loc.ReasonOld(f.AgeText),
                Group = Loc.GroupOld,
                Purpose = CleanPurpose.Old,
                Risk = CleanRisk.Confirm,
                CanDelete = true,
                Selected = false,
            });
        }
    }
}

/// <summary>空目录。隐藏目录（.git 之类）不动。</summary>
public sealed class EmptyFolderRule : ICleanRule
{
    private const int MaxHits = 120;

    public string Name => "empty-folders";
    public string Category => "empty";
    public CleanPurpose Purpose => CleanPurpose.EmptyFolder;
    public CleanRisk DefaultRisk => CleanRisk.Confirm;
    public EvidenceLevel Evidence => EvidenceLevel.Verified;
    public bool CanDelete => true;
    public bool SupportsCancellation => false;

    public void Evaluate(CleanRuleContext ctx, ICleanRuleSink sink)
    {
        foreach (var d in ctx.Dirs)
        {
            if (sink.Count(CleanRuleTarget.EmptyFolders) >= MaxHits) return;
            if (!CleanRuleHelpers.CanOffer(d)) continue;
            if (d.FileCount != 0 || d.FolderCount != 0) continue;
            if (d.HasChildren) continue;
            if (CleanRuleHelpers.LooksUnsafe(CleanRuleHelpers.Lower(d.FullPath))) continue;
            if (d.Name.StartsWith('.')) continue;
            sink.Add(new CleanRuleHit
            {
                Entry = d,
                Target = CleanRuleTarget.EmptyFolders,
                Reason = Loc.ReasonEmpty,
                Group = Loc.GroupEmpty,
                Purpose = CleanPurpose.EmptyFolder,
                Risk = CleanRisk.Confirm,
                CanDelete = true,
                Selected = false,
                Evidence = EvidenceLevel.Verified,
            });
        }
    }
}

/// <summary>路径过长：多数是把文件挪个地方就能解决，所以默认不允许删除。</summary>
public sealed class LongPathRule : ICleanRule
{
    private const int MaxHits = 80;
    private const int Threshold = 240;

    public string Name => "long-paths";
    public string Category => "longpath";
    public CleanPurpose Purpose => CleanPurpose.LongPath;
    public CleanRisk DefaultRisk => CleanRisk.Confirm;
    public EvidenceLevel Evidence => EvidenceLevel.Verified;
    public bool CanDelete => false;
    /// <summary>长路径的处置方式是「挪位置」而不是删，风险固定为需确认。</summary>
    public bool RiskIsAuthoritative => true;
    public bool SupportsCancellation => false;

    public void Evaluate(CleanRuleContext ctx, ICleanRuleSink sink)
    {
        foreach (var e in ctx.Files.Concat(ctx.Dirs))
        {
            if (sink.Count(CleanRuleTarget.LongPaths) >= MaxHits) return;
            if (string.IsNullOrEmpty(e.FullPath) || e.IsFilesGroup) continue;
            if (e.FullPath.Length < Threshold) continue;
            sink.Add(new CleanRuleHit
            {
                Entry = e,
                Target = CleanRuleTarget.LongPaths,
                Reason = Loc.ReasonLong(e.FullPath.Length),
                Group = Loc.GroupLong,
                Purpose = CleanPurpose.LongPath,
                Risk = CleanRisk.Confirm,
                CanDelete = CleanRuleHelpers.CanOffer(e),
                Selected = false,
                Evidence = EvidenceLevel.Verified,
            });
        }
    }
}

/// <summary>坏了 的快捷方式：解析出目标，目标不在了才算坏。</summary>
public sealed class BrokenShortcutRule : ICleanRule
{
    private const int MaxHits = 80;
    private const int MaxChecked = 2500;

    public string Name => "broken-shortcuts";
    public string Category => "shortcut";
    public CleanPurpose Purpose => CleanPurpose.BrokenShortcut;
    public CleanRisk DefaultRisk => CleanRisk.Safe;
    public EvidenceLevel Evidence => EvidenceLevel.Verified;
    public bool CanDelete => true;
    public bool SupportsCancellation => true;

    public void Evaluate(CleanRuleContext ctx, ICleanRuleSink sink)
    {
        int checkedN = 0;
        foreach (var f in ctx.Files)
        {
            if (checkedN > MaxChecked) break;
            if (sink.Count(CleanRuleTarget.BrokenShortcuts) >= MaxHits) break;
            if (!f.Name.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase)) continue;
            if (string.IsNullOrEmpty(f.FullPath) || !File.Exists(f.FullPath)) continue;
            if (!CleanRuleHelpers.CanOffer(f)) continue;
            checkedN++;
            ctx.Ct.ThrowIfCancellationRequested();
            string? target = ShortcutNative.ResolveTarget(f.FullPath);
            if (string.IsNullOrEmpty(target)) continue;
            bool exists = File.Exists(target) || Directory.Exists(target);
            if (exists) continue;
            sink.Add(new CleanRuleHit
            {
                Entry = f,
                Target = CleanRuleTarget.BrokenShortcuts,
                Reason = Loc.ReasonBroken(target),
                Group = Loc.GroupShortcut,
                Purpose = CleanPurpose.BrokenShortcut,
                Risk = CleanRisk.Safe,
                CanDelete = true,
                Selected = false,
                Evidence = EvidenceLevel.Verified,
                Tech = target,
            });
        }
    }
}

/// <summary>和上次扫描对比：只报告变化，永远不可删。</summary>
public sealed class ScanCompareRule : ICleanRule
{
    private const long MinDelta = 8L * 1024 * 1024;

    public string Name => "scan-compare";
    public string Category => "compare";
    public CleanPurpose Purpose => CleanPurpose.Delta;
    public CleanRisk DefaultRisk => CleanRisk.Confirm;
    public EvidenceLevel Evidence => EvidenceLevel.Verified;
    public bool CanDelete => false;
    /// <summary>只是变化信息，永远需确认。</summary>
    public bool RiskIsAuthoritative => true;
    public bool SupportsCancellation => false;

    public void Evaluate(CleanRuleContext ctx, ICleanRuleSink sink)
    {
        var previous = ctx.Previous;
        if (previous == null) return;

        var now = new Dictionary<string, FileEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in ctx.Root.ChildList)
        {
            if (c.IsFilesGroup) continue;
            now[c.Name] = c;
        }

        foreach (var kv in now)
        {
            previous.Folders.TryGetValue(kv.Key, out long old);
            long delta = kv.Value.Size - old;
            if (Math.Abs(delta) < MinDelta) continue;
            string reason = delta >= 0
                ? Loc.ReasonGrew(FileEntry.FormatSize(delta))
                : Loc.ReasonShrunk(FileEntry.FormatSize(-delta));
            sink.Add(new CleanRuleHit
            {
                Entry = kv.Value,
                Target = CleanRuleTarget.Compare,
                Reason = reason,
                Group = Loc.GroupCompare,
                Purpose = CleanPurpose.Delta,
                CanDelete = false,
                Selected = false,
                Evidence = EvidenceLevel.Verified,
            });
        }

        foreach (var name in previous.Folders.Keys)
        {
            if (now.ContainsKey(name)) continue;
            long old = previous.Folders[name];
            if (old < MinDelta) continue;
            // 已经不存在的目录：没有 FileEntry 可用，单独造一个只用于展示的占位
            sink.Add(new CleanRuleHit
            {
                Entry = new FileEntry { Name = name, FullPath = name, Size = old, Kind = EntryKind.Directory },
                Target = CleanRuleTarget.Compare,
                Reason = Loc.ReasonGone,
                Group = Loc.GroupCompare,
                Purpose = CleanPurpose.Delta,
                CanDelete = false,
                Selected = false,
                Evidence = EvidenceLevel.Verified,
            });
        }
    }
}
