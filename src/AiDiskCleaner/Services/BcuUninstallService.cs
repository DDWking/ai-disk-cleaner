using System.Diagnostics;
using Klocman.Forms.Tools;
using UninstallTools;
using UninstallTools.Factory;
using UninstallTools.Junk;
using UninstallTools.Junk.Confidence;
using UninstallTools.Junk.Containers;
using UninstallTools.Uninstaller;
using AiDiskCleaner.Models;

namespace AiDiskCleaner.Services;

/// <summary>
/// 薄封装 BCU UninstallTools。列出已装软件，勾选后走官方卸载程序。
/// Copyright 2017 Marcin Szeniak, Apache 2.0.
/// </summary>
public static class BcuUninstallService
{
    public static List<AppUninstallItem> ListApps(IProgress<ScanProgress>? progress, CancellationToken ct)
    {
        PremadeDialogs.SendErrorAction ??= ex => Trace.WriteLine("BCU: " + ex);
        UninstallToolsGlobalConfig.ScanRegistry = true;
        UninstallToolsGlobalConfig.ScanStoreApps = true;
        UninstallToolsGlobalConfig.ScanDrives = false;
        UninstallToolsGlobalConfig.ScanPreDefined = false;
        UninstallToolsGlobalConfig.ScanSteam = false;
        UninstallToolsGlobalConfig.ScanOculus = false;
        UninstallToolsGlobalConfig.ScanWinFeatures = false;
        UninstallToolsGlobalConfig.ScanWinUpdates = false;
        UninstallToolsGlobalConfig.ScanChocolatey = false;
        UninstallToolsGlobalConfig.ScanScoop = false;
        UninstallToolsGlobalConfig.UseQuietUninstallDaemon = false;

        var raw = ApplicationUninstallerFactory.GetUninstallerEntries(p =>
        {
            ct.ThrowIfCancellationRequested();
            string msg = p.Inner?.Message ?? p.Message ?? Loc.UninstallListing;
            int total = p.TotalCount > 0 ? p.TotalCount : 8;
            int pct = Math.Clamp(p.CurrentCount * 100 / Math.Max(total, 1), 0, 99);
            progress?.Report(new ScanProgress(p.CurrentCount, msg, pct));
        });

        return raw
            .Where(e => !string.IsNullOrWhiteSpace(e.DisplayName))
            .OrderBy(e => e.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .Select(Wrap)
            .ToList();
    }

    public static AppUninstallItem Wrap(ApplicationUninstallerEntry e)
    {
        long bytes = 0;
        try { bytes = e.EstimatedSize.GetKbSize() * 1024L; } catch { }
        return new AppUninstallItem
        {
            Name = e.DisplayName,
            Publisher = e.PublisherTrimmed ?? "",
            Version = e.DisplayVersion ?? "",
            SizeBytes = bytes,
            InstallLocation = e.InstallLocation ?? "",
            CanUninstall = e.UninstallPossible && !e.IsProtected,
            IsProtected = e.IsProtected,
            Entry = e,
            Status = e.IsProtected ? Loc.UninstallProtected : (e.UninstallPossible ? "" : Loc.UninstallNoWay),
        };
    }

    public static BulkUninstallTask StartUninstall(IEnumerable<AppUninstallItem> items)
    {
        var targets = items
            .Where(x => x.CanUninstall && x.Entry != null)
            .Select(x => new BulkUninstallEntry(x.Entry!, false, UninstallStatus.Waiting))
            .ToList();
        if (targets.Count == 0)
            throw new InvalidOperationException(Loc.NothingSelected);

        var cfg = new BulkUninstallConfiguration(
            ignoreProtection: false,
            preferQuiet: false,
            simulate: false,
            autoKillStuckQuiet: false,
            retryFailedQuiet: false);
        var task = UninstallManager.CreateBulkUninstallTask(targets, cfg);
        task.Start();
        return task;
    }

    public static List<JunkItem> FindLeftovers(
        IEnumerable<ApplicationUninstallerEntry> targets,
        ICollection<ApplicationUninstallerEntry> all,
        IProgress<ScanProgress>? progress,
        CancellationToken ct)
    {
        PremadeDialogs.SendErrorAction ??= ex => Trace.WriteLine("BCU: " + ex);
        var list = JunkManager.FindJunk(targets, all, p =>
        {
            ct.ThrowIfCancellationRequested();
            string msg = p.Inner?.Message ?? p.Message ?? Loc.JunkScanning;
            int total = p.TotalCount > 0 ? p.TotalCount : 1;
            int pct = Math.Clamp(p.CurrentCount * 100 / Math.Max(total, 1), 0, 99);
            progress?.Report(new ScanProgress(p.CurrentCount, msg, pct));
        }).ToList();

        return list
            .Select(WrapJunk)
            .OrderByDescending(x => x.ConfidenceScore)
            .ThenBy(x => x.AppName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public static JunkItem WrapJunk(IJunkResult j)
    {
        var level = j.Confidence.GetConfidence();
        bool safe = level is ConfidenceLevel.Good or ConfidenceLevel.VeryGood;
        return new JunkItem
        {
            AppName = j.Application?.DisplayName ?? "",
            Category = j.Source?.CategoryName ?? "",
            Path = j.GetDisplayName(),
            ConfidenceScore = j.Confidence.GetRawConfidence(),
            ConfidenceText = Loc.JunkLevel(level),
            Safe = safe,
            Selected = safe,
            Result = j,
        };
    }

    public static (int Ok, int Fail) DeleteLeftovers(IEnumerable<JunkItem> items)
    {
        var picked = items.Where(x => x.Selected && x.Result != null).Select(x => x.Result!).ToList();
        var sorted = picked
            .OrderByDescending(x => x is RunProcessJunk)
            .ThenByDescending(x => x is StartupJunkNode)
            .ToList();
        int ok = 0, fail = 0;
        foreach (var j in sorted)
        {
            try
            {
                j.Delete();
                ok++;
            }
            catch (Exception ex)
            {
                Trace.WriteLine("junk delete: " + ex);
                fail++;
            }
        }
        return (ok, fail);
    }
}
