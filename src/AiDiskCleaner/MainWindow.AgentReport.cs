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
/// 「AI 分析」入口与报告面板（服务「按文件夹删除」页）。
///
/// L1 单发报告：本地选候选 → 证据卡 → 一次/少量批次问模型 → 本地校验 → 可交互报告。
/// 默认以右侧分屏面板停靠在「按文件夹删除」页右侧，点「去处理」直接展开/定位左侧目标文件夹，
/// 不关闭报告面板，方便边看报告边勾选；同时保留「大窗查看」弹层模式。
/// 这里**只读**：不改勾选、不碰文件，勾选与删除仍然全部由用户自己操作。
/// </summary>
public partial class MainWindow
{
    /// <summary>当前这份报告对应的扫描会话（folderId → FileEntry 要靠它解析）。</summary>
    private ScanSession? _agentSession;

    /// <summary>最近一次生成的 AI 报告（供右侧分屏与大窗弹层复用）。</summary>
    private AgentReport? _lastAgentReport;

    /// <summary>AI 分析是否在跑：同一时间只允许一次，避免重复扣费。</summary>
    private bool _agentBusy;

    private CancellationTokenSource? _agentCts;

    /// <summary>大窗报告弹层顶部的临时状态行。</summary>
    private TextBlock? _reportStatusText;

    /// <summary>右侧分屏报告顶部的临时状态行。</summary>
    private TextBlock? _dockedReportStatusText;

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

            _lastAgentReport = result.Report;
            SetOrganizeNote(Loc.AiReportMeta(modelId, result.Report.AllItems.Count(),
                FileEntry.FormatSize(result.Report.TotalReclaimableBytes)));
            ShowDockedAgentReport(result.Report);
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
        UpdateAiReportDockToggle();
    }

    /// <summary>重扫/关窗时取消正在跑的 AI 分析，并收起旧根对应的分屏报告。</summary>
    private void CancelAgentReport()
    {
        try { _agentCts?.Cancel(); } catch { /* 已经释放/取消过 */ }
        _lastAgentReport = null;
        SetDockedReportVisible(false);
        UpdateAiReportDockToggle();
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

    // ==================== 右侧分屏面板 & 大窗弹层 ====================

    /// <summary>默认在「按文件夹删除」页右侧展开分屏报告，同时把内容同步到弹层容器。</summary>
    private void ShowDockedAgentReport(AgentReport report)
    {
        _lastAgentReport = report;
        if (OrganizeReportHeaderTitle != null)
            OrganizeReportHeaderTitle.Text = Loc.AiReportTitle;
        if (OrganizeReportExpandBtn != null)
            OrganizeReportExpandBtn.Content = Loc.AiReportExpandModal;
        if (OrganizeReportCloseBtn != null)
        {
            OrganizeReportCloseBtn.ToolTip = Loc.AiReportCollapsePanel;
            System.Windows.Automation.AutomationProperties.SetName(OrganizeReportCloseBtn, Loc.AiReportCollapsePanel);
        }

        if (OrganizeReportContent != null)
            PopulateReportPanel(OrganizeReportContent, report, isDocked: true);
        BuildReportContent(report);
        SetDockedReportVisible(true);
        UpdateAiReportDockToggle();
    }

    private void SetDockedReportVisible(bool visible)
    {
        if (OrganizeReportPanel == null || OrganizeReportCol == null) return;
        if (visible && _lastAgentReport != null)
        {
            OrganizeReportPanel.Visibility = Visibility.Visible;
            OrganizeReportCol.Width = new GridLength(390);
        }
        else
        {
            OrganizeReportPanel.Visibility = Visibility.Collapsed;
            OrganizeReportCol.Width = new GridLength(0);
        }
        UpdateAiReportDockToggle();
    }

    private void UpdateAiReportDockToggle()
    {
        if (AiReportToggleBtn == null) return;
        if (_lastAgentReport == null || _root == null)
        {
            AiReportToggleBtn.Visibility = Visibility.Collapsed;
            return;
        }
        bool isOpen = OrganizeReportPanel?.Visibility == Visibility.Visible;
        AiReportToggleBtn.Visibility = Visibility.Visible;
        AiReportToggleBtn.Content = isOpen ? Loc.AiReportDockHide : Loc.AiReportDockShow;
    }

    private void AiReportToggle_Click(object sender, RoutedEventArgs e)
    {
        if (_lastAgentReport == null) return;
        bool isOpen = OrganizeReportPanel?.Visibility == Visibility.Visible;
        SetDockedReportVisible(!isOpen);
    }

    private void OrganizeReportClose_Click(object sender, RoutedEventArgs e)
        => SetDockedReportVisible(false);

    private void OrganizeReportExpand_Click(object sender, RoutedEventArgs e)
    {
        if (_lastAgentReport == null) return;
        ShowAgentReport(_lastAgentReport);
    }

    private void ShowAgentReport(AgentReport report)
    {
        _lastAgentReport = report;
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
        PopulateReportPanel(ReportContent, report, isDocked: false);
    }

    private void PopulateReportPanel(StackPanel container, AgentReport report, bool isDocked)
    {
        container.Children.Clear();

        // 顶部摘要卡片：总括 + 模型与预计可回收空间
        var summaryCard = new Border
        {
            Background = ThemeService.Brush("PanelRight"),
            BorderBrush = ThemeService.Brush("Border"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(12, 10, 12, 10),
            Margin = new Thickness(0, 0, 0, 10),
        };
        var summaryStack = new StackPanel();
        summaryStack.Children.Add(AddText(report.Summary, 13, "Text", FontWeights.SemiBold, new Thickness(0, 0, 0, 4)));
        summaryStack.Children.Add(AddText(Loc.AiReportMeta(report.Model, report.AllItems.Count(), FileEntry.FormatSize(report.TotalReclaimableBytes)),
            11, "TextDim", FontWeights.Normal, new Thickness(0)));
        summaryCard.Child = summaryStack;
        container.Children.Add(summaryCard);

        var statusBlock = AddText("", 11, "TextMuted", FontWeights.Normal, new Thickness(0, 0, 0, 8));
        statusBlock.Visibility = Visibility.Collapsed;
        if (isDocked) _dockedReportStatusText = statusBlock;
        else _reportStatusText = statusBlock;
        container.Children.Add(statusBlock);

        if (!string.IsNullOrEmpty(report.Degraded))
        {
            var reason = report.Warnings.Count > 0 ? report.Warnings[0] : "degraded";
            container.Children.Add(AddText(Loc.AiReportDegraded(reason), 11, "SectionConfirmAccent", FontWeights.Normal, new Thickness(0, 0, 0, 8)));
        }

        if (!report.AllItems.Any())
        {
            container.Children.Add(AddText(Loc.AiReportEmpty, 12, "TextDim", FontWeights.Normal, new Thickness(0, 6, 0, 6)));
        }

        foreach (var group in report.Groups)
        {
            if (group.Items.Count == 0) continue;
            var header = new DockPanel { Margin = new Thickness(0, 8, 0, 6) };
            var title = new TextBlock
            {
                Text = group.Group,
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Foreground = ThemeService.Brush("Accent"),
            };
            var bytes = new TextBlock
            {
                Text = FileEntry.FormatSize(group.TotalBytes),
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Foreground = ThemeService.Brush("AccentDim"),
                HorizontalAlignment = HorizontalAlignment.Right,
            };
            DockPanel.SetDock(bytes, Dock.Right);
            DockPanel.SetDock(title, Dock.Left);
            header.Children.Add(bytes);
            header.Children.Add(title);
            container.Children.Add(header);

            foreach (var item in group.Items)
                container.Children.Add(BuildReportItem(item, closeModalOnJump: !isDocked));
        }

        container.Children.Add(AddText(Loc.AiReportFooter, 10.5, "TextMuted", FontWeights.Normal, new Thickness(0, 10, 0, 2)));
    }

    private Border BuildReportItem(ReportItem item, bool closeModalOnJump = true)
    {
        var borderBrushKey = item.Verdict switch
        {
            ReportVerdict.Recommend => "SectionSafeAccent",
            ReportVerdict.Consider => "SectionConfirmAccent",
            _ => "Border",
        };
        var surfaceKey = item.Verdict switch
        {
            ReportVerdict.Recommend => "SectionSafeSurface",
            ReportVerdict.Consider => "SectionConfirmSurface",
            _ => "Bg",
        };

        var card = new Border
        {
            BorderBrush = ThemeService.Brush("Border"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Background = ThemeService.Brush(surfaceKey),
            Padding = new Thickness(12, 10, 12, 10),
            Margin = new Thickness(0, 0, 0, 8),
        };

        var body = new StackPanel();

        // 第一行：结论徽章 + 标题 + 可回收大小
        var top = new DockPanel { Margin = new Thickness(0, 0, 0, 4) };
        var sizeText = new TextBlock
        {
            Text = FileEntry.FormatSize(item.ReclaimableBytes),
            FontSize = 12.5,
            FontWeight = FontWeights.SemiBold,
            Foreground = ThemeService.Brush(borderBrushKey == "Border" ? "TextDim" : borderBrushKey),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 0, 0),
        };
        DockPanel.SetDock(sizeText, Dock.Right);
        top.Children.Add(sizeText);

        var badge = MakeBadge(item.Verdict);
        badge.Margin = new Thickness(0, 0, 7, 0);
        DockPanel.SetDock(badge, Dock.Left);
        top.Children.Add(badge);

        var title = new TextBlock
        {
            Text = item.Title,
            FontSize = 12.5,
            FontWeight = FontWeights.SemiBold,
            Foreground = ThemeService.Brush(item.Verdict == ReportVerdict.Keep ? "TextDim" : "Text"),
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
            ToolTip = item.Title,
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
            ToolTip = item.RelPath,
            Margin = new Thickness(0, 0, 0, 5),
        };
        body.Children.Add(pathLine);

        // 清理理由
        if (!string.IsNullOrWhiteSpace(item.Why))
        {
            body.Children.Add(new TextBlock
            {
                Text = item.Why,
                FontSize = 11.5,
                Foreground = ThemeService.Brush("Text"),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 4),
            });
        }

        if (item.Evidence.Count > 0)
        {
            var labels = string.Join(" · ", item.Evidence.Select(x => x.Label));
            AddText(Loc.AiReportEvidence + labels, 10.5, "TextMuted", FontWeights.Normal,
                new Thickness(0, 0, 0, 2), body);
        }
        if (!string.IsNullOrWhiteSpace(item.Risk))
            AddText(Loc.AiReportRisk(item.Risk), 10.5, "SectionConfirmAccent", FontWeights.Normal,
                new Thickness(0, 0, 0, 4), body);

        // 唯一的动作：定位并高亮目标文件夹 —— 不勾选、不删除。
        var buttons = new DockPanel { Margin = new Thickness(0, 4, 0, 0) };
        var jumpBtn = new Button
        {
            Content = Loc.AiReportJump,
            Style = FindResource("GhostButton") as Style,
            Padding = new Thickness(12, 4, 12, 4),
            FontSize = 11.5,
            HorizontalAlignment = HorizontalAlignment.Right,
            ToolTip = Loc.AiReportJumpTip,
        };
        jumpBtn.Click += (_, _) => RevealReportFolder(item, closeModal: closeModalOnJump);
        
        // 增加交互式路径跳转支持
        jumpBtn.ToolTip = Loc.AiReportJumpTip + " (" + item.RelPath + ")";
        
        // 优化方案：如果该条目包含证据路径，将其显示为列表供点击跳转
        if (item.Evidence.Count > 0)
        {
            var evidencePanel = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
            // 使用已知 UI 字符串或默认文本
            evidencePanel.Children.Add(new TextBlock { Text = "证据文件:", FontSize = 10.5, FontWeight = FontWeights.Bold, Foreground = ThemeService.Brush("TextMuted") });
            
            foreach (var ev in item.Evidence)
            {
                var evBtn = new Button
                {
                    Content = ev.Label,
                    Style = FindResource("GhostButton") as Style,
                    FontSize = 11,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    ToolTip = ev.Target,
                    Margin = new Thickness(0, 2, 0, 0)
                };
                // 点击证据标签中的目标路径进行跳转
                evBtn.Click += (_, _) => {
                    RevealReportFolder(item, closeModal: closeModalOnJump);
                };
                evidencePanel.Children.Add(evBtn);
            }
            body.Children.Add(evidencePanel);
        }
        
        DockPanel.SetDock(jumpBtn, Dock.Right);
        buttons.Children.Add(jumpBtn);
        body.Children.Add(buttons);

        card.Child = body;
        return card;
    }

    private Border MakeBadge(ReportVerdict verdict)
    {
        var (text, key) = verdict switch
        {
            ReportVerdict.Recommend => (Loc.AiReportRecommend, "SectionSafeAccent"),
            ReportVerdict.Consider => (Loc.AiReportConsider, "SectionConfirmAccent"),
            ReportVerdict.Keep => (Loc.AiReportKeep, "TextMuted"),
            _ => (Loc.AiReportConsider, "TextMuted"),
        };
        return new Border
        {
            BorderBrush = ThemeService.Brush(key),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(6, 1, 6, 1),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock
            {
                Text = text,
                FontSize = 10,
                FontWeight = FontWeights.SemiBold,
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
        foreach (var block in new[] { _reportStatusText, _dockedReportStatusText })
        {
            if (block == null) continue;
            block.Text = text;
            block.Foreground = ThemeService.Brush(muted ? "TextMuted" : "Accent");
            block.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
        }
    }

    // ==================== 动作（只切页 + 定位，不勾选、不删除） ====================

    /// <summary>
    /// 定位并高亮「按文件夹删除」页里的目标文件夹行。
    /// 分屏模式下不关闭右侧报告面板；大窗弹层模式下关闭弹层并保持右侧分屏可用。
    /// </summary>
    private void RevealReportFolder(ReportItem item, bool closeModal = false)
    {
        var folder = ResolveReportFolder(item.FolderId);
        if (folder == null)
        {
            SetReportStatus(Loc.AiReportNotInTree, muted: true);
            SetOrganizeNote(Loc.AiReportNotInTree);
            return;
        }

        if (closeModal)
            HideOverlay();
        ShowRightTab(RightTab.Organize);
        var result = RevealOrganizeFolder(folder);
        string note = result.Kind switch
        {
            OrganizeRevealKind.Located when result.Node != null => Loc.AiReportJumped(result.Node.Name),
            OrganizeRevealKind.Partial => Loc.AiReportJumpPartial(result.Node?.Name ?? folder.Name),
            _ => Loc.AiReportNotInTree,
        };
        SetReportStatus(note, muted: false);
        SetOrganizeNote(note);
    }

    private FileEntry? ResolveReportFolder(string folderId)
    {
        if (_agentSession == null || string.IsNullOrWhiteSpace(folderId)) return null;
        return _agentSession.Index.TryGet(folderId, out var folder) ? folder : null;
    }
}
