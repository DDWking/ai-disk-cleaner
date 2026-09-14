using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
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
        UninstallToolsGlobalConfig.ScanSteam = true;
        UninstallToolsGlobalConfig.ScanWinFeatures = true;
        UninstallToolsGlobalConfig.ScanDrives = false;
        UninstallToolsGlobalConfig.ScanPreDefined = false;
        UninstallToolsGlobalConfig.ScanOculus = false;
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
        try { bytes = e.EstimatedSize.GetKbSize() * 1024L; }
        catch (Exception ex)
        {
            // 体积估不出来不阻断列表，但要让日志里查得到是哪个软件。
            AppLog.Write(new LogEntry(DateTime.UtcNow, LogLevel.Debug, "Uninstall", "", "size-estimate",
                (e.DisplayName ?? "") + " | " + ex.GetType().Name));
        }
        bool steam = e.UninstallerKind == UninstallerType.Steam;
        bool feature = e.UninstallerKind == UninstallerType.WindowsFeature;
        // Windows 自带组件 / 设备软件：结构化判定（卸载程序路径或安装路径落在 Windows 目录内）。
        // 实测依据：本机 mstsc 那条 `Microsoft® Windows® Operating System` 的
        // BCU SystemComponent/IsProtected 都是 false，但卸装命令与安装位置都在 C:\Windows 下。
        bool inbox = false;
        try { inbox = AppRecommendationService.IsInboxComponent(e.UninstallString, e.InstallLocation); }
        catch (Exception ex) { AppLog.Record("Uninstall", ex, "inbox component check"); }
        int group = e.IsProtected ? 3 : feature ? 2 : steam ? 1 : 0;
        string pub = e.PublisherTrimmed ?? "";
        if (pub.Length == 0 && steam) pub = "Steam";
        string status = e.IsProtected ? Loc.UninstallProtected
            : feature ? Loc.UninstallWinFeature
            : steam ? "Steam"
            : e.UninstallPossible ? "" : Loc.UninstallNoWay;
        return new AppUninstallItem
        {
            AppId = Guid.NewGuid().ToString("N"),
            Name = e.DisplayName ?? "",
            Publisher = pub,
            Version = e.DisplayVersion ?? "",
            SizeBytes = bytes,
            ActualSizeBytes = bytes,
            InstallDate = e.InstallDate,
            InstallLocation = e.InstallLocation ?? "",
            CanUninstall = e.UninstallPossible && !e.IsProtected && !e.SystemComponent && !feature,
            IsProtected = e.IsProtected,
            SystemComponent = e.SystemComponent,
            InboxComponent = inbox,
            HasStartup = e.HasStartups,
            GroupKey = group,
            IconBytes = TryIconBytes(e),
            Entry = e,
            Status = status,
        };
    }

    static byte[]? TryIconBytes(ApplicationUninstallerEntry e)
    {
        try
        {
            var icon = e.GetIcon();
            if (icon != null) return ToPng(icon);
        }
        catch (Exception ex)
        {
            AppLog.Write(new LogEntry(DateTime.UtcNow, LogLevel.Debug, "Uninstall", "", "icon",
                (e.DisplayName ?? "") + " | " + ex.GetType().Name));
        }
        try
        {
            string? path = e.DisplayIcon;
            if (string.IsNullOrWhiteSpace(path)) return null;
            int comma = path.LastIndexOf(',');
            if (comma > 0 && int.TryParse(path[(comma + 1)..], out _))
                path = path[..comma].Trim('"', ' ');
            path = path.Trim('"', ' ');
            if (!File.Exists(path)) return null;
            using var extracted = Icon.ExtractAssociatedIcon(path);
            return extracted == null ? null : ToPng(extracted);
        }
        catch { return null; }
    }

    static byte[]? ToPng(Icon icon)
    {
        using var bmp = icon.ToBitmap();
        using var ms = new MemoryStream();
        bmp.Save(ms, ImageFormat.Png);
        return ms.ToArray();
    }

    public static ImageSource? ToImage(byte[]? png)
    {
        if (png == null || png.Length == 0) return null;
        using var ms = new MemoryStream(png);
        var img = new BitmapImage();
        img.BeginInit();
        img.CacheOption = BitmapCacheOption.OnLoad;
        img.StreamSource = ms;
        img.EndInit();
        img.Freeze();
        return img;
    }

    public static BulkUninstallTask StartUninstall(IEnumerable<AppUninstallItem> items)
    {
        var targets = items
            .Where(x => x.CanUninstall
                && x.Entry != null
                && x.Entry.UninstallPossible
                && !x.Entry.IsProtected
                && !x.Entry.SystemComponent
                && x.Entry.UninstallerKind != UninstallerType.WindowsFeature)
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
