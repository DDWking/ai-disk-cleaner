using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AiDiskCleaner.Models;
using AiDiskCleaner.Services;
using AiDiskCleaner.Services.Agent;

namespace AiDiskCleaner;

/// <summary>
/// 「AI 分析」入口与报告弹层。
///
/// L1 单发报告：本地选候选 → 证据卡 → 一次/少量批次问模型 → 本地校验 → 可交互报告。
/// 这里**只读**：不改勾选、不碰文件；「加入清理」只是替用户勾上已有的候选项，
/// 「只看这个目录」只是切清理页的范围，两者之后都还要用户自己按确认删除。
/// </summary>
public partial class MainWindow
{
    /// <summary>当前这份报告对应的扫描会话（folderId → FileEntry 要靠它解析）。</summary>
    private ScanSession? _agentSession;

    /// <summary>AI 分析是否在跑：同一时间只允许一次，避免重复扣费。</summary>
    private bool _agentBusy;

    private CancellationTokenSource? _agentCts;

    /// <summary>报告弹层顶部的临时状态行（加入清理后的反馈写这里）。</summary>
    private TextBlock? _reportStatusText;

    private const string AiReportIdleHint = "";

    // ==================== 入口 ====================

    private async void AiReport_Click(object sender, RoutedEventArgs e)
    {
        if (_agentBusy) return;
        if (_root == null || _report == null)
        {
            SetAiStatus(Loc.AiReportNeedScan);
            return;
        }
        if (!AiConfigured())
        {
            // 没配置时直接把设置面板打开：用户不用自己找入口。
            SetAiStatus(Loc.AiReportNotConfigured);
            OpenOverlay(Loc.SettingsTitle, settings: true);
            return;
        }

        _agentBusy = true;
        AiReportBtn.IsEnabled = false;
        SetAiStatus(Loc.AiReportBusy);
        var sw = Stopwatch.StartNew();
        try
        {
            var provider = App.Settings.DecisionProvider();
            var modelId = App.Settings.DecisionModelId();
            if (provider == null || string.IsNullOrWhiteSpace(modelId))
            {
                SetAiStatus(Loc.AiReportNotConfigured);
                return;
            }

            using var cts = new CancellationTokenSource();
            _agentCts = cts;
            _agentSession = ScanSession.Create(
                Path.GetPathRoot(_root.FullPath ?? "") ?? "",
                _root,
                _scanQuality,
                freeBytes: FreeBytesOf(_root));

            var model = ChatModelFactory.Create(provider.BaseUrl, modelId, provider.ApiKey);
            var service = new AgentReportService(model);
            var progress = new Progress<AgentReportProgress>(p =>
            {
                if (p.Total > 0) SetAiStatus($"{Loc.AiReportBusy} {Math.Min(p.Completed, p.Total)}/{p.Total}");
            });

            var result = await service.AnalyzeAsync(_agentSession, progress, cts.Token);
            if (!result.Ok || result.Report == null)
            {
                SetAiStatus(Loc.AiReportFailed(result.Error ?? ""));
                return;
            }

            SetAiStatus(Loc.AiReportMeta(modelId, result.Report.AllItems.Count(),
                FileEntry.FormatSize(result.Report.TotalReclaimableBytes)));
            ShowAgentReport(result.Report);
            AppLog.Write(new LogEntry(DateTime.UtcNow, LogLevel.Debug, "AI", "",
                "agent-report", $"items={result.Report.AllItems.Count()} calls={result.Calls} ms={sw.ElapsedMilliseconds}"));
        }
        catch (OperationCanceledException)
        {
            SetAiStatus(Loc.AiReportNeedScan);
        }
        catch (Exception ex)
        {
            SetAiStatus(Loc.AiReportFailed(AppError.From(ex, "ai-report").UserMessage));
        }
        finally
        {
            _agentBusy = false;
            _agentCts = null;
            AiReportBtn.IsEnabled = true;
        }
    }

    /// <summary>重扫/关窗时取消正在跑的 AI 分析，避免回调打到旧根上。</summary>
    private void CancelAgentReport()
    {
        try { _agentCts?.Cancel(); } catch { /* 已经释放/取消过 */ }
    }

    private static long FreeBytesOf(FileEntry root)
    {
        try
        {
            var path = Path.GetPathRoot(root.FullPath ?? "");
            if (string.IsNullOrEmpty(path)) return 0;
            var info = new DriveInfo(path);
            return info.IsReady ? info.AvailableFreeSpace : 0;
        }
        catch
        {
            return 0;
        }
    }

    // ==================== 弹层 ====================

    private void ShowAgentReport(AgentReport report)
    {
        DialogTitle.Text = Loc.AiReportTitle;
        SettingsBody.Visibility = Visibility.Collapsed;
        ProvEditBody.Visibility = Visibility.Collapsed;
        AboutBody.Visibility = Visibility.Collapsed;
        SelectionBody.Visibility = Visibility.Collapsed;
        RuleSelectBody.Visibility = Visibility.Collapsed;
        AlertBody.Visibility = Visibility.Collapsed;
        ConfirmButtons.Visibility = Visibility.Collapsed;
        ReportBody.Visibility = Visibility.Visible;
        DialogCard.Width = 940;
        DialogCard.MaxHeight = 760;
        DialogClose.Visibility = Visibility.Visible;
        DialogClose.Content = Loc.Close;

        BuildReportContent(report);
        ShowOverlay();
    }

    private void BuildReportContent(AgentReport report)
    {
        ReportContent.Children.Clear();

        // 一行总结 + 一行元信息
        ReportContent.Children.Add(AddText(report.Summary, 13.5, "Text", FontWeights.SemiBold, new Thickness(0, 0, 0, 4)));
        ReportContent.Children.Add(AddText(Loc.AiReportMeta(report.Model, report.AllItems.Count(), FileEntry.FormatSize(report.TotalReclaimableBytes)),
            11, "TextDim", FontWeights.Normal, new Thickness(0, 0, 0, 8)));

        _reportStatusText = AddText(AiReportIdleHint, 11, "Accent", FontWeights.Normal, new Thickness(0, 0, 0, 8));
        _reportStatusText.Visibility = Visibility.Collapsed;
        ReportContent.Children.Add(_reportStatusText);

        if (!string.IsNullOrEmpty(report.Degraded))
        {
            var reason = report.Warnings.Count > 0 ? report.Warnings[0] : "degraded";
            ReportContent.Children.Add(AddText(Loc.AiReportDegraded(reason), 11, "TextMuted", FontWeights.Normal, new Thickness(0, 0, 0, 8)));
        }

        if (report.AllItems.Count() == 0)
        {
            ReportContent.Children.Add(AddText(Loc.AiReportEmpty, 12, "TextDim", FontWeights.Normal, new Thickness(0, 6, 0, 6)));
        }

        foreach (var group in report.Groups)
        {
            if (group.Items.Count == 0) continue;
            var header = new DockPanel { Margin = new Thickness(0, 10, 0, 6) };
            var title = new TextBlock
            {
                Text = group.Group,
                FontSize = 12.5,
                FontWeight = FontWeights.SemiBold,
                Foreground = ThemeService.Brush("Accent"),
            };
            var bytes = new TextBlock
            {
                Text = FileEntry.FormatSize(group.TotalBytes),
                FontSize = 11,
                Foreground = ThemeService.Brush("TextDim"),
                HorizontalAlignment = HorizontalAlignment.Right,
            };
            DockPanel.SetDock(bytes, Dock.Right);
            DockPanel.SetDock(title, Dock.Left);
            header.Children.Add(bytes);
            header.Children.Add(title);
            ReportContent.Children.Add(header);

            foreach (var item in group.Items)
                ReportContent.Children.Add(BuildReportItem(item));
        }

        ReportContent.Children.Add(AddText(Loc.AiReportFooter, 11, "TextMuted", FontWeights.Normal, new Thickness(0, 12, 0, 0)));
    }

    private Border BuildReportItem(ReportItem item)
    {        var card = new Border
        {
            BorderBrush = ThemeService.Brush("Border"),
            BorderThickness = new Thickness(1),
            Background = ThemeService.Brush("Bg"),
            Padding = new Thickness(12, 10, 12, 10),
            Margin = new Thickness(0, 0, 0, 8),
        };

        var body = new StackPanel();

        // 第一行：标题 + 结论 + 可回收大小
        var top = new DockPanel();
        var sizeText = new TextBlock
        {
            Text = FileEntry.FormatSize(item.ReclaimableBytes),
            FontSize = 12.5,
            FontWeight = FontWeights.SemiBold,
            Foreground = ThemeService.Brush("Accent"),
            VerticalAlignment = VerticalAlignment.Center,
        };
        DockPanel.SetDock(sizeText, Dock.Right);
        top.Children.Add(sizeText);

        var title = new TextBlock
        {
            Text = item.Title,
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = ThemeService.Brush(item.Verdict == ReportVerdict.Keep ? "TextDim" : "Text"),
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
        };
        top.Children.Add(title);
        body.Children.Add(top);

        // 相对路径 + 目录总大小
        var pathLine = new TextBlock
        {
            Text = $"{item.RelPath}  ·  {FileEntry.FormatSize(item.SizeBytes)}",
            FontSize = 11,
            Foreground = ThemeService.Brush("TextDim"),
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(0, 2, 0, 4),
        };
        body.Children.Add(pathLine);

        // 结论 + 依据 + 风险
        var verdictLine = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
        verdictLine.Children.Add(MakeBadge(item.Verdict));
        if (!string.IsNullOrWhiteSpace(item.Why))
        {
            verdictLine.Children.Add(new TextBlock
            {
                Text = item.Why,
                FontSize = 11.5,
                Foreground = ThemeService.Brush("Text"),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(8, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
            });
        }
        body.Children.Add(verdictLine);

        if (item.Evidence.Count > 0)
        {
            var labels = string.Join(" · ", item.Evidence.Select(x => x.Label));
            AddText(Loc.AiReportEvidence + labels, 10.5, "TextMuted", FontWeights.Normal,
                new Thickness(0, 0, 0, 2), body);
        }
        if (!string.IsNullOrWhiteSpace(item.Risk))
            AddText(Loc.AiReportRisk(item.Risk), 10.5, "TextMuted", FontWeights.Normal,
                new Thickness(0, 0, 0, 4), body);

        // 按钮：加入清理 / 只看这个目录（都不执行删除）
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
        var addBtn = new Button
        {
            Content = Loc.AiReportAdd,
            Style = FindResource("GhostButton") as Style,
            Padding = new Thickness(10, 3, 10, 3),
            FontSize = 11,
            Margin = new Thickness(0, 0, 8, 0),
        };
        addBtn.Click += (_, _) =>
        {
            var (count, bytes) = AddReportItemToCleanup(item);
            if (count <= 0)
            {
                SetReportStatus(Loc.AiReportAddNothing, muted: true);
                return;
            }
            SetReportStatus(Loc.AiReportAdded(count, FileEntry.FormatSize(bytes)));
            addBtn.Content = Loc.AiReportAdded(count, "");
            addBtn.IsEnabled = false;
        };
        buttons.Children.Add(addBtn);

        var jumpBtn = new Button
        {
            Content = Loc.AiReportJump,
            Style = FindResource("GhostButton") as Style,
            Padding = new Thickness(10, 3, 10, 3),
            FontSize = 11,
            ToolTip = Loc.AiReportJumpTip,
        };
        jumpBtn.Click += (_, _) => JumpToReportFolder(item);
        buttons.Children.Add(jumpBtn);
        body.Children.Add(buttons);

        card.Child = body;
        return card;
    }

    private Border MakeBadge(ReportVerdict verdict)
    {
        var (text, key) = verdict switch
        {
            ReportVerdict.Recommend => (Loc.AiReportRecommend, "Accent"),
            ReportVerdict.Consider => (Loc.AiReportConsider, "TextDim"),
            ReportVerdict.Keep => (Loc.AiReportKeep, "TextMuted"),
            _ => (Loc.AiReportConsider, "TextMuted"),
        };
        return new Border
        {
            BorderBrush = ThemeService.Brush(key),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(6, 1, 6, 1),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock
            {
                Text = text,
                FontSize = 10.5,
                Foreground = ThemeService.Brush(key),
            },
        };
    }

    private static TextBlock AddText(string text, double size, string brush, FontWeight weight,
        Thickness margin, Panel? parent = null)
    {
        var block = new TextBlock
        {
            Text = text,
            FontSize = size,
            FontWeight = weight,
            Foreground = ThemeService.Brush(brush),
            TextWrapping = TextWrapping.Wrap,
            Margin = margin,
        };
        if (parent != null) parent.Children.Add(block);
        return block;
    }

    private void SetReportStatus(string text, bool muted = false)
    {
        if (_reportStatusText == null) return;
        _reportStatusText.Text = text;
        _reportStatusText.Foreground = ThemeService.Brush(muted ? "TextMuted" : "Accent");
        _reportStatusText.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
    }

    // ==================== 动作（都只改「显示/勾选」，不执行删除） ====================

    /// <summary>把报告项对应目录下的已有候选项勾上；返回本次新勾选的数量与字节。</summary>
    private (int Count, long Bytes) AddReportItemToCleanup(ReportItem item)
    {
        var folder = ResolveReportFolder(item.FolderId);
        if (folder == null) return (0, 0);

        var prefix = CleanListSnapshot.FolderPrefix(folder.FullPath ?? "");
        int count = 0;
        long bytes = 0;
        foreach (var candidate in _layered.AllItems)
        {
            if (!candidate.CanDelete || candidate.Selected) continue;
            if (!CleanListSnapshot.UnderPrefix(candidate.FullPath, prefix)) continue;
            candidate.Selected = true;
            count++;
            bytes += candidate.Size;
        }
        if (count > 0) RefreshAfterSelectionChange();
        return (count, bytes);
    }

    /// <summary>切到清理页，并把范围限定到这个目录（不改任何勾选）。</summary>
    private void JumpToReportFolder(ReportItem item)
    {
        var folder = ResolveReportFolder(item.FolderId);
        if (folder == null) return;

        HideOverlay();
        ShowRightTab(RightTab.Clean);
        _cleanScopeRoot = folder;
        ApplyCleanScope();
    }

    private FileEntry? ResolveReportFolder(string folderId)
    {
        if (_agentSession == null || string.IsNullOrWhiteSpace(folderId)) return null;
        return _agentSession.Index.TryGet(folderId, out var folder) ? folder : null;
    }
}

