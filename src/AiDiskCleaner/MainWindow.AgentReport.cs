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
    private double _dockedReportWidth = 320;

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
            SaveActiveDriveToCache();
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
        PopulateReportPanel(ReportContent, report, isDocked: false, animate: false);
        SetDockedReportVisible(true);
        UpdateAiReportDockToggle();
    }

    /// <summary>在尚未生成报告时，在右侧抽屉渲染极简状态与单一动作入口。</summary>
    private void RefreshDockedReportPlaceholder()
    {
        if (OrganizeReportContent == null || _lastAgentReport != null) return;
        if (OrganizeReportHeaderTitle != null)
            OrganizeReportHeaderTitle.Text = Loc.AiReportTitle;
        if (OrganizeReportExpandBtn != null)
            OrganizeReportExpandBtn.Visibility = Visibility.Collapsed;

        OrganizeReportContent.Children.Clear();

        var stack = new StackPanel
        {
            Margin = new Thickness(8, 28, 8, 12),
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };

        if (_lastAgentError.Length > 0)
        {
            stack.Children.Add(new TextBlock
            {
                Text = _lastAgentError,
                FontSize = 11.5,
                Foreground = ThemeService.Brush("ToneRedAccent"),
                TextWrapping = TextWrapping.Wrap,
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(0, 0, 0, 10),
            });
        }

        var ctaStyle = TryFindResource("OrangePillButton") as Style ?? TryFindResource("PrimaryButton") as Style;

        if (_agentBusy)
        {
            stack.Children.Add(new TextBlock
            {
                Text = _agentProgressText.Length > 0 ? _agentProgressText : (Loc.IsEn ? "Analyzing..." : "分析中…"),
                FontSize = 11.5,
                FontWeight = FontWeights.SemiBold,
                Foreground = ThemeService.Brush("BrandAccent"),
                TextWrapping = TextWrapping.Wrap,
                TextAlignment = TextAlignment.Center,
            });
            stack.Children.Add(new ProgressBar
            {
                Height = 3,
                IsIndeterminate = true,
                Margin = new Thickness(16, 10, 16, 0),
            });
        }
        else if (_scanning)
        {
            stack.Children.Add(new TextBlock
            {
                Text = Loc.IsEn ? "Scanning..." : "扫描中…",
                FontSize = 11.5,
                Foreground = ThemeService.Brush("TextMuted"),
                TextAlignment = TextAlignment.Center,
            });
        }
        else if (_root == null)
        {
            stack.Children.Add(new TextBlock
            {
                Text = Loc.IsEn ? "Scan a drive first" : "请先扫描",
                FontSize = 11.5,
                Foreground = ThemeService.Brush("TextMuted"),
                TextAlignment = TextAlignment.Center,
            });
        }
        else if (!AiConfigured())
        {
            stack.Children.Add(new TextBlock
            {
                Text = Loc.IsEn ? "AI not configured" : "未配置 AI",
                FontSize = 11.5,
                Foreground = ThemeService.Brush("TextDim"),
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(0, 0, 0, 12),
            });
            var settingsBtn = new Button
            {
                Content = Loc.AiReportPlaceholderOpenSettings,
                Style = ctaStyle,
                Padding = new Thickness(18, 6, 18, 6),
                FontSize = 12,
                HorizontalAlignment = HorizontalAlignment.Center,
            };
            settingsBtn.Click += (_, _) => OpenOverlay(Loc.SettingsTitle, settings: true);
            stack.Children.Add(settingsBtn);
        }
        else
        {
            var runBtn = new Button
            {
                Content = Loc.AiReportPlaceholderAction,
                Style = ctaStyle,
                Padding = new Thickness(20, 7, 20, 7),
                FontSize = 12,
                HorizontalAlignment = HorizontalAlignment.Center,
            };
            runBtn.Click += AiReport_Click;
            stack.Children.Add(runBtn);
        }

        OrganizeReportContent.Children.Add(stack);
    }

    private void SetDockedReportVisible(bool visible)
    {
        if (OrganizeReportPanel == null || OrganizeReportCol == null) return;
        if (visible)
        {
            bool wasHidden = OrganizeReportPanel.Visibility != Visibility.Visible;
            OrganizeReportPanel.Visibility = Visibility.Visible;
            if (OrganizeReportSplitter != null) OrganizeReportSplitter.Visibility = Visibility.Visible;
            if (OrganizeReportSplitterCol != null) OrganizeReportSplitterCol.Width = new GridLength(8);
            OrganizeReportCol.MinWidth = 240;
            OrganizeReportCol.MaxWidth = 760;
            OrganizeReportCol.Width = new GridLength(_dockedReportWidth >= 240 ? _dockedReportWidth : 320);
            if (_lastAgentReport == null) RefreshDockedReportPlaceholder();
            if (wasHidden)
                AnimateSlideFadeIn(OrganizeReportPanel, offsetX: 24, offsetY: 0, durationMs: 230, fromOpacity: 0.0);
        }
        else
        {
            if (OrganizeReportCol.ActualWidth >= 240)
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
        AiReportToggleBtn.Content = isOpen
            ? (Loc.IsEn ? "Hide" : "收起")
            : (Loc.IsEn ? "Report" : "报告");
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

    private void PopulateReportPanel(StackPanel container, AgentReport report, bool isDocked, bool animate = true)
    {
        container.Children.Clear();
        bool runStagger = animate && CanAnimate;

        var allItems = report.AllItems.ToList();
        var recommendItems = allItems.Where(x => x.Verdict == ReportVerdict.Recommend).ToList();
        var considerItems = allItems.Where(x => x.Verdict == ReportVerdict.Consider).ToList();

        // 顶部双列 KPI 指标；模型元信息与自然语言总括收进悬停提示，保持面板极简
        var kpiGrid = new Grid { Margin = new Thickness(0, 0, 0, 10), Background = Brushes.Transparent };
        kpiGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        kpiGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
        kpiGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        string meta = Loc.AiReportMeta(report.Model, report.AllItems.Count(), FileEntry.FormatSize(report.TotalReclaimableBytes));
        kpiGrid.ToolTip = string.IsNullOrWhiteSpace(report.Summary)
            ? meta
            : meta + "\n" + report.Summary.Trim();

        var kpiGreen = MakeKpiTile("ToneGreenAccent",
            Loc.IsEn ? "Cleanable" : "可清理",
            recommendItems.Sum(x => x.SizeBytes > 0 ? x.SizeBytes : x.ReclaimableBytes),
            recommendItems.Count);
        Grid.SetColumn(kpiGreen, 0);
        kpiGrid.Children.Add(kpiGreen);

        var kpiYellow = MakeKpiTile("ToneYellowAccent",
            Loc.IsEn ? "Review" : "待复核",
            considerItems.Sum(x => x.SizeBytes > 0 ? x.SizeBytes : x.ReclaimableBytes),
            considerItems.Count);
        Grid.SetColumn(kpiYellow, 2);
        kpiGrid.Children.Add(kpiYellow);

        container.Children.Add(kpiGrid);
        if (runStagger)
            AnimateSlideFadeIn(kpiGrid, offsetY: 6, durationMs: 200, delayMs: 0, fromOpacity: 0.0);

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

        int cardIdx = 0;
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
            var header = new DockPanel { Margin = new Thickness(0, 8, 0, 5) };
            var leftHeader = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            leftHeader.Children.Add(new System.Windows.Shapes.Ellipse
            {
                Width = 6,
                Height = 6,
                Fill = ThemeService.Brush(groupAccent),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 6, 0),
            });
            leftHeader.Children.Add(new TextBlock
            {
                Text = $"{group.Group} · {group.Items.Count}",
                FontSize = 11.5,
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
                Foreground = ThemeService.Brush("TextMuted"),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
            };
            DockPanel.SetDock(bytes, Dock.Right);
            DockPanel.SetDock(leftHeader, Dock.Left);
            header.Children.Add(bytes);
            header.Children.Add(leftHeader);
            container.Children.Add(header);

            foreach (var item in group.Items)
            {
                var card = BuildReportItem(item, closeModalOnJump: !isDocked);
                container.Children.Add(card);
                if (runStagger && cardIdx < 10)
                {
                    AnimateSlideFadeIn(card, offsetY: 10, durationMs: 210, delayMs: (cardIdx + 1) * 24, fromOpacity: 0.0);
                    cardIdx++;
                }
            }
        }
    }

    private static Border MakeKpiTile(string accentKey, string label, long bytes, int count)
    {
        var stack = new StackPanel();
        var top = new DockPanel();
        var countText = new TextBlock
        {
            Text = count.ToString(),
            FontSize = 10.5,
            Foreground = ThemeService.Brush("TextMuted"),
            VerticalAlignment = VerticalAlignment.Center,
        };
        DockPanel.SetDock(countText, Dock.Right);

        var labelRow = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        labelRow.Children.Add(new System.Windows.Shapes.Ellipse
        {
            Width = 6,
            Height = 6,
            Fill = ThemeService.Brush(accentKey),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 5, 0),
        });
        labelRow.Children.Add(new TextBlock
        {
            Text = label,
            FontSize = 11,
            Foreground = ThemeService.Brush("TextDim"),
            VerticalAlignment = VerticalAlignment.Center,
        });
        DockPanel.SetDock(labelRow, Dock.Left);
        top.Children.Add(countText);
        top.Children.Add(labelRow);

        stack.Children.Add(top);
        stack.Children.Add(new TextBlock
        {
            Text = count > 0 ? FileEntry.FormatSize(bytes) : "0 B",
            FontSize = 16,
            FontWeight = FontWeights.Bold,
            Foreground = ThemeService.Brush(accentKey),
            Margin = new Thickness(0, 4, 0, 0),
        });
        return new Border
        {
            Background = ThemeService.Brush("CardSurface"),
            BorderBrush = ThemeService.Brush("Border"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(10, 8, 10, 8),
            Child = stack,
        };
    }

    private Border BuildReportItem(ReportItem item, bool closeModalOnJump = true)
    {
        var accentKey = item.Verdict switch
        {
            ReportVerdict.Recommend => "ToneGreenAccent",
            ReportVerdict.Consider => "ToneYellowAccent",
            ReportVerdict.Keep => "ToneRedAccent",
            _ => "Border",
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
            CornerRadius = new CornerRadius(8),
            Background = ThemeService.Brush("CardSurface"),
            Padding = closeModalOnJump ? new Thickness(12, 10, 12, 10) : new Thickness(10, 8, 10, 8),
            Margin = new Thickness(0, 0, 0, 6),
            Cursor = System.Windows.Input.Cursors.Hand,
            ToolTip = string.Join("\n", tipParts),
            RenderTransform = new TranslateTransform(0, 0),
        };
        card.MouseEnter += (_, _) =>
        {
            card.BorderBrush = ThemeService.Brush("BrandAccent");
            card.Background = ThemeService.Brush("HoverBg");
            AnimateHoverLift(card, lifted: true);
        };
        card.MouseLeave += (_, _) =>
        {
            card.BorderBrush = ThemeService.Brush("Border");
            card.Background = ThemeService.Brush("CardSurface");
            AnimateHoverLift(card, lifted: false);
        };
        card.MouseLeftButtonUp += (_, e) =>
        {
            if (e.Handled) return;
            e.Handled = true;
            RevealReportFolder(item, closeModal: closeModalOnJump);
        };

        var body = new StackPanel();
        bool showPathInModal = closeModalOnJump && !string.IsNullOrWhiteSpace(item.RelPath) && item.RelPath.Contains('\\');
        string detailLine = !string.IsNullOrWhiteSpace(item.Why) ? item.Why.Trim() : (item.Risk?.Trim() ?? "");
        bool hasSubline = showPathInModal || !string.IsNullOrWhiteSpace(detailLine);

        // 第 1 行：状态圆点 + 名称 + 右侧实际容量 & 跳转箭头
        var top = new Grid { Margin = hasSubline ? new Thickness(0, 0, 0, 2) : new Thickness(0) };
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var badge = MakeBadge(item.Verdict, compact: !closeModalOnJump);
        badge.Margin = new Thickness(0, 0, 7, 0);
        Grid.SetColumn(badge, 0);
        top.Children.Add(badge);

        var title = new TextBlock
        {
            Text = item.Title,
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Foreground = ThemeService.Brush(item.Verdict == ReportVerdict.Keep ? "TextDim" : "Text"),
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(title, 1);
        top.Children.Add(title);

        var sizeText = new TextBlock
        {
            Text = FileEntry.FormatSize(displayBytes) + "  ›",
            FontSize = 11.5,
            FontWeight = FontWeights.Bold,
            Foreground = ThemeService.Brush(accentKey == "Border" ? "TextDim" : accentKey),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 0, 0),
        };
        Grid.SetColumn(sizeText, 2);
        top.Children.Add(sizeText);

        body.Children.Add(top);

        // 大窗查看才展示完整等宽路径；右侧抽屉收进悬停提示保持极简
        if (showPathInModal)
        {
            body.Children.Add(new TextBlock
            {
                Text = item.RelPath,
                FontSize = 10.5,
                FontFamily = Application.Current?.TryFindResource("MonoFont") as FontFamily ?? new FontFamily("Consolas"),
                Foreground = ThemeService.Brush("TextMuted"),
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = !string.IsNullOrWhiteSpace(detailLine) ? new Thickness(0, 0, 0, 2) : new Thickness(0),
            });
        }

        // 第 2 行：仅保留单行简短说明（风险与证据完整详情放在 ToolTip）
        if (!string.IsNullOrWhiteSpace(detailLine))
        {
            body.Children.Add(new TextBlock
            {
                Text = detailLine,
                FontSize = 10.5,
                Foreground = ThemeService.Brush("TextMuted"),
                TextWrapping = TextWrapping.NoWrap,
                TextTrimming = TextTrimming.CharacterEllipsis,
            });
        }

        card.Child = body;
        return card;
    }

    private Border MakeBadge(ReportVerdict verdict, bool compact = false)
    {
        var (text, fgKey) = verdict switch
        {
            ReportVerdict.Recommend => (Loc.AiReportRecommend, "ToneGreenAccent"),
            ReportVerdict.Consider => (Loc.AiReportConsider, "ToneYellowAccent"),
            ReportVerdict.Keep => (Loc.AiReportKeep, "ToneRedAccent"),
            _ => (Loc.AiReportConsider, "TextMuted"),
        };
        var dot = new System.Windows.Shapes.Ellipse
        {
            Width = 6,
            Height = 6,
            Fill = ThemeService.Brush(fgKey),
            VerticalAlignment = VerticalAlignment.Center,
        };
        if (compact)
        {
            return new Border
            {
                VerticalAlignment = VerticalAlignment.Center,
                Child = dot,
            };
        }

        dot.Margin = new Thickness(0, 0, 4, 0);
        var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        row.Children.Add(dot);
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
