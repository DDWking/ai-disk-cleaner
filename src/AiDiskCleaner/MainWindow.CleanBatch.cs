using System.Windows;
using AiDiskCleaner.Models;
using AiDiskCleaner.Services;
using AiDiskCleaner.Services.CleanRules;

namespace AiDiskCleaner;

/// <summary>
/// 清理首页的**批量决策**（本轮核心）。
///
/// 只做一件事：把「正式规则已经判定为安全、证据到签名级、且通过保护检查」的
/// 缓存 / 临时 / 转储项一次性勾上，但**先给用户看清单并允许逐类排除**。
///
/// 刻意不做的事：
/// <list type="bullet">
/// <item>不新增第二套扫描、也不新增第三套规则引擎 —— 成员筛选只有
///   <see cref="CleanRuleEligibility"/> 一处判据，它读的是规则自己产出的字段；</item>
/// <item>不碰 AI —— 判据里没有 AI 结论这一项，AI 也永远改不了候选资格；</item>
/// <item>不自动执行 —— 勾完仍然必须走「清理已选项目 → 清理前检查 → 确认」；</item>
/// <item>不把大文件 / 旧文件 / 下载 / 压缩包 / 视频 / 个人资料 / 未知对象带走。</item>
/// </list>
/// </summary>
public partial class MainWindow
{
    /// <summary>当前打开的预览（确认时按它勾选；取消就丢掉，不做任何改动）。</summary>
    private CleanRuleSelectPreview? _ruleSelectPreview;

    /// <summary>「选择规则明确的清理项」：先构建预览，**这一步不勾任何东西**。</summary>
    private void RuleSelect_Click(object sender, RoutedEventArgs e)
    {
        if (_report == null) return;
        var preview = CleanRuleSelectPreview.Build(_layered);
        if (preview == null)
        {
            ShowAlert(Loc.RuleSelectTitle, Loc.RuleSelectNoEligible);
            return;
        }

        _ruleSelectPreview = preview;
        preview.Changed += UpdateRuleSelectSummary;
        RuleSelectGrid.ItemsSource = preview.Groups;
        UpdateRuleSelectSummary();

        // 确认 / 取消按钮的文案属于这个弹层，关掉时恢复默认
        ConfirmYesBtn.Content = Loc.RuleSelectConfirm;
        ConfirmNoBtn.Content = Loc.RuleSelectCancel;
        _confirmYes = ApplyRuleSelect;
        OpenOverlay(Loc.RuleSelectTitle, ruleSelect: true);
        AppLog.Info("Clean", $"op=rule-select-preview groups={preview.Groups.Count} "
            + $"files={preview.ActiveFiles} bytes={preview.ActiveBytes}");
    }

    /// <summary>预览里的排除勾选变化：只重算汇总，不重算成员、不碰选择。</summary>
    private void UpdateRuleSelectSummary()
    {
        var preview = _ruleSelectPreview;
        if (preview == null) return;
        RuleSelectSummary.Text = preview.SummaryText;
        RuleSelectExcluded.Text = preview.ExcludedNote;
        RuleSelectExcluded.Visibility = preview.ExcludedNote.Length > 0
            ? Visibility.Visible
            : Visibility.Collapsed;
        ConfirmYesBtn.IsEnabled = preview.ActiveFiles > 0;
    }

    /// <summary>
    /// 用户点了「确认勾选这些」才真正写选择。写入前对每一条再核一遍
    /// 清理资格与保护路径 —— 界面上显示的资格是扫描时算的，执行口径必须重新验。
    /// </summary>
    private void ApplyRuleSelect()
    {
        var preview = _ruleSelectPreview;
        _ruleSelectPreview = null;
        RuleSelectGrid.ItemsSource = null;
        if (preview == null) return;

        int applied = 0, recheckSkipped = 0;
        long bytes = 0;
        foreach (var group in preview.Active)
        {
            foreach (var item in group.Items)
            {
                if (!CleanRuleEligibility.IsRuleClear(item)) continue;
                if (!CleanRuleEligibility.IsStillDeletable(item)) { recheckSkipped++; continue; }
                if (item.Selected) continue;
                item.Selected = true;
                applied++;
                bytes += Math.Max(0, item.Size);
            }
        }

        RefreshAfterSelectionChange();
        if (applied > 0)
        {
            CleanSelectionNote.Text = Loc.RuleSelectApplied(applied, FileEntry.FormatSize(bytes));
            if (recheckSkipped > 0) CleanSelectionNote.Text += "  ·  " + Loc.RuleSelectRecheckSkipped(recheckSkipped);
        }
        else
        {
            CleanSelectionNote.Text = Loc.RuleSelectNothingNew;
        }
        AppLog.Info("Clean", $"op=rule-select-apply applied={applied} bytes={bytes} "
            + $"skippedByRecheck={recheckSkipped}");
    }

    /// <summary>关掉弹层时丢掉预览：取消不能留下任何副作用。</summary>
    private void DiscardRuleSelectPreview()
    {
        _ruleSelectPreview = null;
        if (RuleSelectGrid != null) RuleSelectGrid.ItemsSource = null;
    }
}
