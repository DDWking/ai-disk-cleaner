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
/// 「AI 分析」入口与报告弹层（只服务「按文件夹删除」页）。
///
/// L1 单发报告：本地选候选 → 证据卡 → 一次/少量批次问模型 → 本地校验 → 可交互报告。
/// 这里**只读**：不改勾选、不碰文件。报告里的「去处理」只切到文件夹页并把目标文件夹
/// 定位/高亮出来，勾选与删除仍然全部由用户自己操作。
/// </summary>
public partial class MainWindow
{
    /// <summary>当前这份报告对应的扫描会话（folderId → FileEntry 要靠它解析）。</summary>
    private ScanSession? _agentSession;

    /// <summary>AI 分析是否在跑：同一时间只允许一次，避免重复扣费。</summary>
    private bool _agentBusy;

    private CancellationTokenSource? _agentCts;

    /// <summary>报告弹层顶部的临时状态行。</summary>
    private TextBlock? _reportStatusText;

    // ==================== 入口 ====================

    /// <summary>
    /// 入口按钮在「按文件夹删除」页页头：有扫描结果才可点；没配置模型时先打开设置。
    /// </summary>
    private async void AiReport_Click(object sender, RoutedEventArgs e)
    {
        if (_agentBusy) return;
        if (_root == null)
        {
            SetOrganizeNote(Loc.AiReportNeedScan);
            return;
        }
        if (!AiConfigured())
        {
            SetOrganizeNote(Loc.AiReportNotConfigured);
            OpenOverlay(Loc.SettingsTitle, settings: true);
            return;
        }

        _agentBusy = true;
        UpdateAiReportButton();
        SetOrganizeNote(Loc.AiReportBusy);
        var sw = Stopwatch.StartNew();
        try
        {
            var provider = App.Settings.DecisionProvider();
            var modelId = App.Settings.DecisionModelId();
            if (provider == null || string.IsNullOrWhiteSpace(modelId))
            {
                SetOrganizeNote(Loc.AiReportNotConfigured);
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
                if (p.Total > 0)
                    AiReportBtnText.Text = Loc.AiReportProgress(Math.Min(p.Completed, p.Total), p.Total);
            });

            var result = await service.AnalyzeAsync(_agentSession, progress, cts.Token);
            if (!result.Ok || result.Report == null)
            {
                SetOrganizeNote(Loc.AiReportFailed(result.Error ?? ""));
                return;
            }

            SetOrganizeNote(Loc.AiReportMeta(modelId, result.Report.AllItems.Count(),
                FileEntry.FormatSize(result.Report.TotalReclaimableBytes)));
            ShowAgentReport(result.Report);
            AppLog.Write(new LogEntry(DateTime.UtcNow, LogLevel.Debug, "AI", "",
                "agent-report", $"items={result.Report.AllItems.Count()} calls={result.Calls} ms={sw.ElapsedMilliseconds}"));
        }
        catch (OperationCanceledException)
        {
            SetOrganizeNote(Loc.AiReportNeedScan);
        }
        catch (Exception ex)
        {
            SetOrganizeNote(Loc.AiReportFailed(AppError.From(ex, "ai-report").UserMessage));
        }
        finally
        {
            _agentBusy = false;
            _agentCts = null;
            if (AiReportBtnText != null) AiReportBtnText.Text = Loc.AiReportBtn;
            UpdateAiReportButton();
        }
    }

    /// <summary>按钮可用性只由「有没有扫描结果 + 是否在扫描/分析」决定，与清理列表无关。</summary>
    private void UpdateAiReportButton()
    {
        if (AiReportBtn == null) return;
        AiReportBtn.IsEnabled = _root != null && !_scanning && !_agentBusy;
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

        _reportStatusText = AddText("", 11, "TextMuted", FontWeights.Normal, new Thickness(0, 0, 0, 8));
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
    {
        var card = new Border
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

        // 唯一的动作：切到文件夹页并定位高亮 —— 不勾选、不删除。
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
        var jumpBtn = new Button
        {
            Content = Loc.AiReportJump,
            Style = FindResource("GhostButton") as Style,
            Padding = new Thickness(10, 3, 10, 3),
            FontSize = 11,
            ToolTip = Loc.AiReportJumpTip,
        };
        jumpBtn.Click += (_, _) => RevealReportFolder(item);
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

    // ==================== 动作（只切页 + 定位，不勾选、不删除） ====================

    /// <summary>关掉报告，切到「按文件夹删除」页，定位并高亮这个文件夹行。</summary>
    private void RevealReportFolder(ReportItem item)
    {
        var folder = ResolveReportFolder(item.FolderId);
        if (folder == null)
        {
            SetReportStatus(Loc.AiReportNotInTree, muted: true);
            return;
        }

        HideOverlay();
        ShowRightTab(RightTab.Organize);
        var result = RevealOrganizeFolder(folder);
        string note = result.Kind switch
        {
            OrganizeRevealKind.Located when result.Node != null => Loc.AiReportJumped(result.Node.Name),
            OrganizeRevealKind.Partial => Loc.AiReportJumpPartial(result.Node?.Name ?? folder.Name),
            _ => Loc.AiReportNotInTree,
        };
        SetOrganizeNote(note);
    }

    private FileEntry? ResolveReportFolder(string folderId)
    {
        if (_agentSession == null || string.IsNullOrWhiteSpace(folderId)) return null;
        return _agentSession.Index.TryGet(folderId, out var folder) ? folder : null;
    }
}
