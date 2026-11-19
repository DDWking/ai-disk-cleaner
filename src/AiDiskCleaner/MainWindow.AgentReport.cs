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

    /// <summary>最近一次 AI 分析失败时的简短说明（用于展示在右侧预留面板）。</summary>
    private string _lastAgentError = "";

    /// <summary>当前 AI 分析进度文本（用于展示在右侧预留面板）。</summary>
    private string _agentProgressText = "";

    /// <summary>右侧停靠报告面板的用户自定义宽度（折叠/展开间保持用户拖动宽度）。</summary>
    private double _dockedReportWidth = 430;

    // ==================== 入口 ====================

    /// <summary>
    /// 入口按钮在清理中心页头：有扫描结果才可点；没配置模型时先打开设置。
    /// </summary>
    private async void AiReport_Click(object sender, RoutedEventArgs e)
    {
        if (_agentBusy) return;
        if (_root == null)
        {
            SetOrganizeNote(Loc.AiReportNeedScan);
            RefreshDockedReportPlaceholder();
            return;
        }
        if (!AiConfigured())
        {
            SetOrganizeNote(Loc.AiReportNotConfigured);
            RefreshDockedReportPlaceholder();
            OpenOverlay(Loc.SettingsTitle, settings: true);
            return;
        }

        _agentBusy = true;
        _lastAgentError = "";
        _agentProgressText = "";
        UpdateAiReportButton();
        SetOrganizeNote(Loc.AiReportBusy);
        if (_lastAgentReport == null) RefreshDockedReportPlaceholder();
        var sw = Stopwatch.StartNew();
        try
        {
            var provider = App.Settings.DecisionProvider();
            var modelId = App.Settings.DecisionModelId();
            if (provider == null || string.IsNullOrWhiteSpace(modelId))
            {
                _lastAgentError = Loc.AiReportNotConfigured;
                SetOrganizeNote(_lastAgentError);
                return;
            }

            using var cts = new CancellationTokenSource();
            _agentCts = cts;
            var root = _root;
            var quality = _scanQuality;
            string driveRoot = Path.GetPathRoot(root.FullPath ?? "") ?? "";
            // 用扫描已用总量作为上限参考，避免在剩余空间紧张时把文件夹可清理体积按比例缩水到几百 MB。
            long sessionCapBytes = Math.Max(root.Size, FreeBytesOf(root));

            var model = ChatModelFactory.Create(provider.BaseUrl, modelId, provider.ApiKey);
            var service = new AgentReportService(model);
            var progress = new Progress<AgentReportProgress>(p =>
            {
                string step = !string.IsNullOrWhiteSpace(p.Message)
                    ? p.Message
                    : (p.Total > 0 ? Loc.AiReportProgress(Math.Min(p.Completed, p.Total), p.Total) : Loc.AiReportBusy);
                if (AiReportBtnText != null)
                {
                    AiReportBtnText.Text = p.Phase == "calling" && p.Total > 0
                        ? Loc.AiReportProgress(Math.Min(p.Completed + 1, p.Total), p.Total)
                        : Loc.AiReportBusy;
                }
                _agentProgressText = step;
                if (_lastAgentReport == null) RefreshDockedReportPlaceholder();
            });

            _agentProgressText = "正在建立目录索引…";
            if (_lastAgentReport == null) RefreshDockedReportPlaceholder();

            var (session, result) = await Task.Run(async () =>
            {
                var s = ScanSession.Create(driveRoot, root, quality, freeBytes: sessionCapBytes);
                var r = await service.AnalyzeAsync(s, progress, cts.Token);
                return (s, r);
            }, cts.Token);
            _agentSession = session;

            if (!result.Ok || result.Report == null)
            {
                _lastAgentError = Loc.AiReportFailed(result.Error ?? "");
                SetOrganizeNote(_lastAgentError);
                AppLog.Warn("AI", $"agent-report failed kind={result.ErrorKind} calls={result.Calls} ms={sw.ElapsedMilliseconds} err={result.Error}");
                return;
            }

            _lastAgentReport = result.Report;
            _lastAgentError = "";
            SetOrganizeNote(Loc.AiReportMeta(modelId, result.Report.AllItems.Count(),
                FileEntry.FormatSize(result.Report.TotalReclaimableBytes)));
            ApplyReportToOrganizeTree(result.Report);
            ShowDockedAgentReport(result.Report);
            AppLog.Info("AI", $"agent-report ok items={result.Report.AllItems.Count()} calls={result.Calls} in={result.Usage.InputTokens} out={result.Usage.OutputTokens} ms={sw.ElapsedMilliseconds}");
        }
        catch (OperationCanceledException)
        {
            SetOrganizeNote(Loc.AiReportNeedScan);
        }
        catch (Exception ex)
        {
            _lastAgentError = Loc.AiReportFailed(AppError.From(ex, "ai-report").UserMessage);
            SetOrganizeNote(_lastAgentError);
            AppLog.Record("AI", ex, "agent-report");
        }
        finally
        {
            _agentBusy = false;
            _agentCts = null;
            _agentProgressText = "";
            if (AiReportBtnText != null) AiReportBtnText.Text = Loc.AiReportBtn;
            UpdateAiReportButton();
            if (_lastAgentReport == null) RefreshDockedReportPlaceholder();
        }
    }

    /// <summary>按钮可用性只由「有没有扫描结果 + 是否在扫描/分析」决定，与清理列表无关。</summary>
    private void UpdateAiReportButton()
    {
        if (AiReportBtn == null) return;
        AiReportBtn.IsEnabled = _root != null && !_scanning && !_agentBusy;
        UpdateAiReportDockToggle();
    }

    /// <summary>重扫/关窗时取消正在跑的 AI 分析，清掉旧报告标色并回到预留引导状态。</summary>
    private void CancelAgentReport()
    {
        try { _agentCts?.Cancel(); } catch { /* 已经释放/取消过 */ }
        _lastAgentReport = null;
        _lastAgentError = "";
        _agentProgressText = "";
        _aiReportAnnotations.Clear();
        foreach (var node in _organizeAll) node.ClearAiAnnotation();
        RefreshOrganizeRows();
        RefreshDockedReportPlaceholder();
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

    /// <summary>将 AI 报告结果同步到左侧文件夹树的三色标记与结论摘要（绝不修改勾选状态）。</summary>
    private void ApplyReportToOrganizeTree(AgentReport report)
    {
        _aiReportAnnotations.Clear();
        foreach (var node in _organizeAll) node.ClearAiAnnotation();

        foreach (var item in report.AllItems)
        {
            var folder = ResolveReportFolder(item.FolderId);
            if (folder == null) continue;

            var (tone, badge) = item.Verdict switch
            {
                ReportVerdict.Recommend => (FolderColorTone.Green, Loc.AiReportRecommend),
                ReportVerdict.Consider => (FolderColorTone.Yellow, Loc.AiReportConsider),
                ReportVerdict.Keep => (FolderColorTone.Red, Loc.AiReportKeep),
                _ => (FolderColorTone.Yellow, Loc.AiReportConsider),
            };
            string brief = !string.IsNullOrWhiteSpace(item.Why) ? item.Why.Trim() : item.Title;
            string summary = brief;
            if (!string.IsNullOrWhiteSpace(item.Risk))
                summary += " · " + Loc.AiReportRisk(item.Risk);

            string norm = CleanListSnapshot.NormPath(folder.FullPath);
            if (norm.Length > 0)
                _aiReportAnnotations[norm] = (tone, badge, summary, brief);

            if (_organizeByDir.TryGetValue(folder, out var node))
                node.ApplyAiAnnotation(tone, badge, summary, brief);
        }

        RefreshOrganizeRows();
    }

    /// <summary>默认在清理中心右侧展开分屏报告，同时把内容同步到弹层容器。</summary>
    private void ShowDockedAgentReport(AgentReport report)
    {
        _lastAgentReport = report;
        if (OrganizeReportHeaderTitle != null)
            OrganizeReportHeaderTitle.Text = Loc.AiReportTitle;
        if (OrganizeReportExpandBtn != null)
        {
            OrganizeReportExpandBtn.Content = Loc.AiReportExpandModal;
            OrganizeReportExpandBtn.Visibility = Visibility.Visible;
        }
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

    /// <summary>在尚未生成报告时，在右侧常驻预留区渲染一张清爽的引导卡片。</summary>
    private void RefreshDockedReportPlaceholder()
    {
        if (OrganizeReportContent == null || _lastAgentReport != null) return;
        if (OrganizeReportHeaderTitle != null)
            OrganizeReportHeaderTitle.Text = Loc.AiReportTitle;
        if (OrganizeReportExpandBtn != null)
            OrganizeReportExpandBtn.Visibility = Visibility.Collapsed;

        OrganizeReportContent.Children.Clear();

        var card = MakePanelCard("PanelRight", _lastAgentError.Length > 0 ? "ToneRedAccent" : "Border");
        var stack = new StackPanel();

        stack.Children.Add(AddText(Loc.AiReportPlaceholderTitle, 13, "Accent", FontWeights.SemiBold, new Thickness(0, 0, 0, 4)));
        stack.Children.Add(AddText(Loc.AiReportFooter, 11, "TextDim", FontWeights.Normal, new Thickness(0, 0, 0, 10)));

        // 单行紧凑三色图例
        var legend = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
        legend.Children.Add(MakeCompactLegendChip("ToneGreenAccent", Loc.ColorFilterGreen));
        legend.Children.Add(MakeCompactLegendChip("ToneYellowAccent", Loc.ColorFilterYellow));
        legend.Children.Add(MakeCompactLegendChip("ToneRedAccent", Loc.ColorFilterRed));
        stack.Children.Add(legend);

        stack.Children.Add(new Border
        {
            Height = 1,
            Background = ThemeService.Brush("Border"),
            Margin = new Thickness(0, 0, 0, 10),
        });

        if (_lastAgentError.Length > 0)
            stack.Children.Add(AddText(_lastAgentError, 11.5, "ToneRedAccent", FontWeights.SemiBold, new Thickness(0, 0, 0, 8)));

        if (_agentBusy)
        {
            stack.Children.Add(AddText(_agentProgressText.Length > 0 ? _agentProgressText : Loc.AiReportPlaceholderBusy,
                11.5, "ToneGreenAccent", FontWeights.SemiBold, new Thickness(0, 0, 0, 8)));
            stack.Children.Add(new ProgressBar { Height = 5, IsIndeterminate = true });
        }
        else if (_scanning)
        {
            stack.Children.Add(AddText(Loc.AiReportPlaceholderScanning, 11.5, "TextDim", FontWeights.Normal, new Thickness(0)));
        }
        else if (_root == null)
        {
            stack.Children.Add(AddText(Loc.AiReportPlaceholderScanFirst, 11.5, "TextDim", FontWeights.Normal, new Thickness(0)));
        }
        else if (!AiConfigured())
        {
            stack.Children.Add(AddText(Loc.AiReportPlaceholderConfigFirst, 11.5, "TextDim", FontWeights.Normal, new Thickness(0, 0, 0, 10)));
            var settingsBtn = new Button
            {
                Content = Loc.AiReportPlaceholderOpenSettings,
                Style = FindResource("PrimaryButton") as Style,
                Padding = new Thickness(12, 6, 12, 6),
                FontSize = 12,
                HorizontalAlignment = HorizontalAlignment.Left,
            };
            settingsBtn.Click += (_, _) => OpenOverlay(Loc.SettingsTitle, settings: true);
            stack.Children.Add(settingsBtn);
        }
        else
        {
            stack.Children.Add(AddText(Loc.AiReportPlaceholderReady(_organizeAll.Count(x => x.Dir.IsDirectory)), 11.5, "Text", FontWeights.Normal, new Thickness(0, 0, 0, 10)));
            var runBtn = new Button
            {
                Content = Loc.AiReportPlaceholderAction,
                Style = FindResource("PrimaryButton") as Style,
                Padding = new Thickness(14, 6, 14, 6),
                FontSize = 12,
                HorizontalAlignment = HorizontalAlignment.Left,
            };
            runBtn.Click += AiReport_Click;
            stack.Children.Add(runBtn);
        }

        card.Child = stack;
        OrganizeReportContent.Children.Add(card);
    }

    private static StackPanel MakeCompactLegendChip(string dotKey, string label)
    {
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 10, 4),
        };
        row.Children.Add(new System.Windows.Shapes.Ellipse
        {
            Width = 7,
            Height = 7,
            Fill = ThemeService.Brush(dotKey),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 5, 0),
        });
        row.Children.Add(new TextBlock
        {
            Text = label,
            FontSize = 11,
            Foreground = ThemeService.Brush("TextDim"),
            VerticalAlignment = VerticalAlignment.Center,
        });
        return row;
    }

    private static Border MakePanelCard(string bgKey, string borderKey) => new()
    {
        Background = ThemeService.Brush(bgKey),
        BorderBrush = ThemeService.Brush(borderKey),
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(6),
        Padding = new Thickness(12, 10, 12, 10),
        Margin = new Thickness(0, 0, 0, 10),
    };

    private void SetDockedReportVisible(bool visible)
    {
        if (OrganizeReportPanel == null || OrganizeReportCol == null) return;
        if (visible)
        {
            OrganizeReportPanel.Visibility = Visibility.Visible;
            if (OrganizeReportSplitter != null) OrganizeReportSplitter.Visibility = Visibility.Visible;
            if (OrganizeReportSplitterCol != null) OrganizeReportSplitterCol.Width = new GridLength(6);
            OrganizeReportCol.MinWidth = 280;
            OrganizeReportCol.MaxWidth = 760;
            OrganizeReportCol.Width = new GridLength(_dockedReportWidth >= 280 ? _dockedReportWidth : 430);
            if (_lastAgentReport == null) RefreshDockedReportPlaceholder();
        }
        else
        {
            if (OrganizeReportCol.ActualWidth >= 280)
                _dockedReportWidth = OrganizeReportCol.ActualWidth;
            OrganizeReportPanel.Visibility = Visibility.Collapsed;
            if (OrganizeReportSplitter != null) OrganizeReportSplitter.Visibility = Visibility.Collapsed;
            if (OrganizeReportSplitterCol != null) OrganizeReportSplitterCol.Width = new GridLength(0);
            OrganizeReportCol.MinWidth = 0;
            OrganizeReportCol.Width = new GridLength(0);
        }
        UpdateAiReportDockToggle();
    }

    private void UpdateAiReportDockToggle()
    {
        if (AiReportToggleBtn == null) return;
        bool isOpen = OrganizeReportPanel?.Visibility == Visibility.Visible;
        AiReportToggleBtn.Visibility = Visibility.Visible;
        AiReportToggleBtn.Content = isOpen ? Loc.AiReportDockHide : Loc.AiReportDockShow;
    }

    private void AiReportToggle_Click(object sender, RoutedEventArgs e)
    {
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

        // 顶部摘要卡片：总括 + 三色分布胶囊（模型与统计细节进悬停，避免文字堆砌）
        var summaryCard = MakePanelCard("PanelRight", "Border");
        summaryCard.ToolTip = Loc.AiReportMeta(report.Model, report.AllItems.Count(), FileEntry.FormatSize(report.TotalReclaimableBytes));
        var summaryStack = new StackPanel();
        summaryStack.Children.Add(AddText(report.Summary, 12.5, "Text", FontWeights.SemiBold, new Thickness(0, 0, 0, 6)));

        var countsRow = new WrapPanel { Orientation = Orientation.Horizontal };
        var allItems = report.AllItems.ToList();
        AddSummaryPill(countsRow, "ToneGreenAccent", "ToneGreenBg", Loc.AiReportRecommend,
            allItems.Where(x => x.Verdict == ReportVerdict.Recommend).ToList());
        AddSummaryPill(countsRow, "ToneYellowAccent", "ToneYellowBg", Loc.AiReportConsider,
            allItems.Where(x => x.Verdict == ReportVerdict.Consider).ToList());
        AddSummaryPill(countsRow, "ToneRedAccent", "ToneRedBg", Loc.AiReportKeep,
            allItems.Where(x => x.Verdict == ReportVerdict.Keep).ToList());
        if (countsRow.Children.Count > 0) summaryStack.Children.Add(countsRow);

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
            container.Children.Add(AddText(Loc.AiReportDegraded(reason), 11, "ToneYellowAccent", FontWeights.Normal, new Thickness(0, 0, 0, 8)));
        }

        if (!report.AllItems.Any())
        {
            container.Children.Add(AddText(Loc.AiReportEmpty, 12, "TextDim", FontWeights.Normal, new Thickness(0, 6, 0, 6)));
        }

        foreach (var group in report.Groups)
        {
            if (group.Items.Count == 0) continue;
            var groupAccent = group.Items[0].Verdict switch
            {
                ReportVerdict.Recommend => "ToneGreenAccent",
                ReportVerdict.Consider => "ToneYellowAccent",
                ReportVerdict.Keep => "ToneRedAccent",
                _ => "Accent",
            };
            var header = new DockPanel { Margin = new Thickness(0, 10, 0, 6) };
            var leftHeader = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            leftHeader.Children.Add(new System.Windows.Shapes.Ellipse
            {
                Width = 8,
                Height = 8,
                Fill = ThemeService.Brush(groupAccent),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 6, 0),
            });
            leftHeader.Children.Add(new TextBlock
            {
                Text = $"{group.Group} ({group.Items.Count})",
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Foreground = ThemeService.Brush(groupAccent),
                VerticalAlignment = VerticalAlignment.Center,
            });
            long groupBytes = group.Items.Sum(x => x.SizeBytes > 0 ? x.SizeBytes : x.ReclaimableBytes);
            var bytes = new TextBlock
            {
                Text = FileEntry.FormatSize(groupBytes),
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Foreground = ThemeService.Brush("AccentDim"),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
            };
            DockPanel.SetDock(bytes, Dock.Right);
            DockPanel.SetDock(leftHeader, Dock.Left);
            header.Children.Add(bytes);
            header.Children.Add(leftHeader);
            container.Children.Add(header);

            foreach (var item in group.Items)
                container.Children.Add(BuildReportItem(item, closeModalOnJump: !isDocked));
        }

        container.Children.Add(AddText(Loc.AiReportFooter, 10.5, "TextMuted", FontWeights.Normal, new Thickness(0, 10, 0, 2)));
    }

    private static void AddSummaryPill(WrapPanel panel, string fgKey, string bgKey, string label, List<ReportItem> items)
    {
        if (items.Count == 0) return;
        long bytes = items.Sum(x => x.SizeBytes > 0 ? x.SizeBytes : x.ReclaimableBytes);
        var pill = new Border
        {
            Background = ThemeService.Brush(bgKey),
            BorderBrush = ThemeService.Brush(fgKey),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(8, 2, 8, 2),
            Margin = new Thickness(0, 0, 6, 4),
            Child = new TextBlock
            {
                Text = $"{label} {items.Count} · {FileEntry.FormatSize(bytes)}",
                FontSize = 10.5,
                FontWeight = FontWeights.SemiBold,
                Foreground = ThemeService.Brush(fgKey),
            },
        };
        panel.Children.Add(pill);
    }

    private Border BuildReportItem(ReportItem item, bool closeModalOnJump = true)
    {
        var (accentKey, surfaceKey) = item.Verdict switch
        {
            ReportVerdict.Recommend => ("ToneGreenAccent", "ToneGreenRowBg"),
            ReportVerdict.Consider => ("ToneYellowAccent", "ToneYellowRowBg"),
            ReportVerdict.Keep => ("ToneRedAccent", "ToneRedRowBg"),
            _ => ("Border", "Bg"),
        };

        long displayBytes = item.SizeBytes > 0 ? item.SizeBytes : item.ReclaimableBytes;

        var tipParts = new List<string>
        {
            $"{item.RelPath} · {FileEntry.FormatSize(displayBytes)}",
        };
        if (!string.IsNullOrWhiteSpace(item.Why)) tipParts.Add(item.Why);
        if (!string.IsNullOrWhiteSpace(item.Risk)) tipParts.Add(Loc.AiReportRisk(item.Risk));
        if (item.Evidence.Count > 0)
            tipParts.Add(Loc.AiReportEvidence + string.Join(" · ", item.Evidence.Select(x => x.Label)));
        tipParts.Add(Loc.AiReportJumpTip);

        var card = new Border
        {
            BorderBrush = ThemeService.Brush("Border"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Background = ThemeService.Brush(surfaceKey),
            Padding = new Thickness(8, 7, 10, 7),
            Margin = new Thickness(0, 0, 0, 6),
            Cursor = System.Windows.Input.Cursors.Hand,
            ToolTip = string.Join("\n", tipParts),
        };
        card.MouseEnter += (_, _) => card.BorderBrush = ThemeService.Brush(accentKey == "Border" ? "AccentDim" : accentKey);
        card.MouseLeave += (_, _) => card.BorderBrush = ThemeService.Brush("Border");
        card.MouseLeftButtonUp += (_, e) =>
        {
            if (e.Handled) return;
            e.Handled = true;
            RevealReportFolder(item, closeModal: closeModalOnJump);
        };

        var layout = new Grid();
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3.5) });
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var accentBar = new Border
        {
            Background = ThemeService.Brush(accentKey),
            CornerRadius = new CornerRadius(2),
            Margin = new Thickness(0, 1, 0, 1),
        };
        Grid.SetColumn(accentBar, 0);
        layout.Children.Add(accentBar);

        var body = new StackPanel { Margin = new Thickness(9, 0, 0, 0) };

        // 第 1 行：徽章 + 目录名 + 父路径线索（随面板拉宽展开）+ 右侧实际占用容量 & 跳转箭头
        var top = new Grid { Margin = new Thickness(0, 0, 0, 3) };
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var badge = MakeBadge(item.Verdict);
        badge.Margin = new Thickness(0, 0, 6, 0);
        Grid.SetColumn(badge, 0);
        top.Children.Add(badge);

        var titleGrid = new Grid { VerticalAlignment = VerticalAlignment.Center };
        titleGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        titleGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var title = new TextBlock
        {
            Text = item.Title,
            FontSize = 12.5,
            FontWeight = FontWeights.SemiBold,
            Foreground = ThemeService.Brush(item.Verdict == ReportVerdict.Keep ? "TextDim" : "Text"),
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(title, 0);
        titleGrid.Children.Add(title);

        if (!string.IsNullOrWhiteSpace(item.RelPath) && item.RelPath.Contains('\\'))
        {
            var relPathHint = new TextBlock
            {
                Text = "  " + item.RelPath,
                FontSize = 10.5,
                Foreground = ThemeService.Brush("TextMuted"),
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(relPathHint, 1);
            titleGrid.Children.Add(relPathHint);
        }

        Grid.SetColumn(titleGrid, 1);
        top.Children.Add(titleGrid);

        var sizeText = new TextBlock
        {
            Text = FileEntry.FormatSize(displayBytes) + "  ›",
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Foreground = ThemeService.Brush(accentKey == "Border" ? "TextDim" : accentKey),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 0, 0),
        };
        Grid.SetColumn(sizeText, 2);
        top.Children.Add(sizeText);

        body.Children.Add(top);

        // 第 2 行：单行结论 + 风险（不折行；拉宽面板即可在一行看到更多内容，完整内容仍在悬停）
        string detailLine = item.Why?.Trim() ?? "";
        if (!string.IsNullOrWhiteSpace(item.Risk))
            detailLine = string.IsNullOrEmpty(detailLine)
                ? Loc.AiReportRisk(item.Risk)
                : detailLine + " · " + Loc.AiReportRisk(item.Risk);

        if (!string.IsNullOrWhiteSpace(detailLine))
        {
            body.Children.Add(new TextBlock
            {
                Text = detailLine,
                FontSize = 11,
                Foreground = ThemeService.Brush("TextDim"),
                TextWrapping = TextWrapping.NoWrap,
                TextTrimming = TextTrimming.CharacterEllipsis,
            });
        }

        Grid.SetColumn(body, 1);
        layout.Children.Add(body);
        card.Child = layout;
        return card;
    }

    private Border MakeBadge(ReportVerdict verdict)
    {
        var (text, fgKey) = verdict switch
        {
            ReportVerdict.Recommend => (Loc.AiReportRecommend, "ToneGreenAccent"),
            ReportVerdict.Consider => (Loc.AiReportConsider, "ToneYellowAccent"),
            ReportVerdict.Keep => (Loc.AiReportKeep, "ToneRedAccent"),
            _ => (Loc.AiReportConsider, "TextMuted"),
        };
        var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        row.Children.Add(new System.Windows.Shapes.Ellipse
        {
            Width = 6,
            Height = 6,
            Fill = ThemeService.Brush(fgKey),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 4, 0),
        });
        row.Children.Add(new TextBlock
        {
            Text = text,
            FontSize = 10.5,
            FontWeight = FontWeights.SemiBold,
            Foreground = ThemeService.Brush(fgKey),
            VerticalAlignment = VerticalAlignment.Center,
        });
        return new Border
        {
            VerticalAlignment = VerticalAlignment.Center,
            Child = row,
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
