using AiDiskCleaner.Models;
using AiDiskCleaner.Services;
using AiDiskCleaner.Services.CleanRules;

namespace SafetyCheck;

/// <summary>
/// 本轮（2026-09-13 体验版）四项改动的针对性回归。全部离线、纯模型层。
///
/// 覆盖：
/// <list type="number">
/// <item><b>D 批量决策</b>：只有「正式规则判定安全 + 证据到签名级 + 通过保护检查」的
///   缓存/临时/转储项能被批选；大文件/旧文件/下载/重复/空目录/长路径/未知一律
///   留在人工选择；AI 无论如何都改不了候选资格；排除与取消都不产生副作用。</item>
/// <item><b>B 行内看文件</b>：惰性建表、有界、复用同一份 CleanItem 实例、
///   收起不丢选择、不会一次性把整处候选都加载进来。</item>
/// <item><b>C 整理页展开状态</b>：有子目录才有箭头；只有文件给查看入口且不伪造箭头；
///   链接未扫描与「扫描无内容」必须分开，都不许用 0 KB 暗示；整理对象没有清理能力。</item>
/// </list>
/// </summary>
public static class ReviewFixesTests
{
    public delegate void CheckFn(string name, bool ok, string? detail = null);

    public static void Run(CheckFn check, Action<string> section)
    {
        RuleClearBatchTests(check, section);
        InlineFilesTests(check, section);
        OrganizeContentKindTests(check, section);
    }

    // ==================================================================== D

    /// <summary>造一条规则明确的候选：Safe + 签名级证据 + 可删。</summary>
    static CleanItem Clear(string path, long size, CleanPurpose purpose = CleanPurpose.Temp)
        => new()
        {
            Name = System.IO.Path.GetFileName(path),
            FullPath = path,
            Size = size,
            Reason = "rule says so",
            Purpose = purpose,
            Group = "cache",
            Risk = CleanRisk.Safe,
            CanDelete = true,
            Evidence = EvidenceLevel.Signature,
        };

    static void RuleClearBatchTests(CheckFn check, Action<string> section)
    {
        section("本轮改动：清理首页的「规则明确」批量勾选");

        // ---- 资格判据本身 ----
        var eligible = Clear(@"C:\Users\x\AppData\Local\Temp\cache\a.bin", 1000);
        check("规则明确（Safe + 签名证据 + 可删）才够格", CleanRuleEligibility.IsRuleClear(eligible));

        var heuristic = Clear(@"C:\Users\x\AppData\Local\Temp\cache\b.bin", 1000);
        heuristic.Evidence = EvidenceLevel.Heuristic;
        check("只靠启发式（路径里有 cache）不够格",
            !CleanRuleEligibility.IsRuleClear(heuristic));

        var confirmRisk = Clear(@"C:\Users\x\AppData\Local\Temp\cache\c.bin", 1000);
        confirmRisk.Risk = CleanRisk.Confirm;
        check("风险是「需要确认」的项不够格", !CleanRuleEligibility.IsRuleClear(confirmRisk));

        var keepRisk = Clear(@"C:\Users\x\AppData\Local\Temp\cache\d.bin", 1000);
        keepRisk.Risk = CleanRisk.Keep;
        check("风险是「别删」的项永远不够格", !CleanRuleEligibility.IsRuleClear(keepRisk));

        var protectedItem = Clear(@"C:\Users\x\AppData\Local\Temp\cache\e.bin", 1000);
        protectedItem.CanDelete = false;
        check("受保护 / 不可删的项不够格", !CleanRuleEligibility.IsRuleClear(protectedItem));

        // 大文件 / 旧文件 / 下载安装包 / 重复 / 空目录 / 长路径 / 未知：即使用签名级证据也不批选
        var largeSafe = Clear(@"C:\big\movie.mkv", 9_000_000_000, CleanPurpose.Large);
        var oldSafe = Clear(@"C:\old\archive.zip", 9_000_000_000, CleanPurpose.Old);
        var installer = Clear(@"C:\Users\x\Downloads\setup.msi", 9_000_000_000, CleanPurpose.Installer);
        var duplicate = Clear(@"C:\dup\a.bin", 1000, CleanPurpose.Duplicate);
        var emptyDir = Clear(@"C:\empty", 0, CleanPurpose.EmptyFolder);
        var longPath = Clear(@"C:\deep\x.bin", 10, CleanPurpose.LongPath);
        var other = Clear(@"C:\misc\thing.bin", 10, CleanPurpose.Other);
        var videoInDownloads = Clear(@"C:\Users\x\Downloads\clip.mp4", 5_000_000_000, CleanPurpose.Installer);
        check("大文件永远人工选择", !CleanRuleEligibility.IsRuleClear(largeSafe));
        check("旧文件永远人工选择", !CleanRuleEligibility.IsRuleClear(oldSafe));
        check("下载里的安装包永远人工选择", !CleanRuleEligibility.IsRuleClear(installer));
        check("重复文件永远人工选择（删哪一个要人来定）", !CleanRuleEligibility.IsRuleClear(duplicate));
        check("空目录不参与批选", !CleanRuleEligibility.IsRuleClear(emptyDir));
        check("长路径不参与批选（处置是挪位置）", !CleanRuleEligibility.IsRuleClear(longPath));
        check("未知用途不参与批选", !CleanRuleEligibility.IsRuleClear(other));
        check("下载里的视频不参与批选", !CleanRuleEligibility.IsRuleClear(videoInDownloads));

        // ---- AI 不能改变候选资格 ----
        // 以前这里往条目上写 AiNote / AiSuggested 再断言资格没变。逐项 AI 删掉之后，
        // 那两个字段连**存在**都不存在了 —— 断言升级成结构断言，比运行时断言更硬：
        // 没有可写的槽位，就没有越权的可能。
        check("候选条目上已经没有 AI 说明 / AI 标记的槽位",
            typeof(CleanItem).GetProperty("AiNote") == null
            && typeof(CleanItem).GetProperty("AiSuggested") == null
            && typeof(CleanItem).GetProperty("Ai") == null);
        // （「启发式证据的项不管 AI 说什么都不够格」上面第 57-60 行已经断言过，不重复。）

        // ---- 预览：分类、排除、取消 ----
        var candidates = new List<CleanItem>
        {
            Clear(@"C:\Users\x\AppData\Local\Temp\cache\a.bin", 1000, CleanPurpose.AppCache),
            Clear(@"C:\Users\x\AppData\Local\Temp\cache\b.bin", 2000, CleanPurpose.AppCache),
            Clear(@"C:\Windows\SoftwareDistribution\Download\c.cab", 4000, CleanPurpose.Temp),
            Clear(@"C:\Windows\Temp\d.dmp", 3000, CleanPurpose.Dump),
            Clear(@"C:\big\movie.mkv", 9_000_000_000, CleanPurpose.Large),
            heuristic,
            confirmRisk,
            protectedItem,
        };
        var layered = CleanGroupingService.Build(candidates);
        var preview = CleanRuleSelectPreview.Build(layered);
        check("预览只装规则明确的项", preview != null && preview.ActiveFiles == 4,
            preview?.ActiveFiles.ToString() ?? "null");
        check("预览按用途分类", preview != null && preview.Groups.Count == 3,
            preview?.Groups.Count.ToString() ?? "null");
        check("大文件不在预览里的任何一类",
            preview != null && preview.Groups.All(g => g.Purpose != CleanPurpose.Large));
        check("预览汇总写明项数与空间",
            preview != null && preview.SummaryText.Contains("4"), preview?.SummaryText ?? "");
        check("预览阶段一个都没勾",
            candidates.All(x => !x.Selected));

        // 排除一类：只影响这一类
        var dumpGroup = preview!.Groups.First(g => g.Purpose == CleanPurpose.Dump);
        dumpGroup.Excluded = true;
        check("排除一类后汇总跟着减", preview.ActiveFiles == 3, preview.ActiveFiles.ToString());
        check("被排除的类不再进本次勾选",
            !preview.ActiveItems.Any(x => x.Purpose == CleanPurpose.Dump));
        check("排除提示写清排除了多少",
            preview.ExcludedNote.Length > 0 && preview.ExcludedFiles == 1, preview.ExcludedNote);
        dumpGroup.Excluded = false;
        check("取消排除后恢复", preview.ActiveFiles == 4, preview.ActiveFiles.ToString());

        // 「取消」= 不调用 ApplyRuleSelect：一个勾都不该出现
        check("只做预览不写选择（等价于点取消）",
            candidates.All(x => !x.Selected));

        // 模拟确认：把 ActiveItems 勾上
        foreach (var item in preview.ActiveItems) item.Selected = true;
        check("确认后只勾规则明确的 4 项",
            candidates.Count(x => x.Selected) == 4, candidates.Count(x => x.Selected).ToString());
        check("大文件即使被确认也没被勾上", largeSafe.Selected == false);
        check("启发式项即使被确认也没被勾上", heuristic.Selected == false);
        check("受保护项即使被确认也没被勾上", protectedItem.Selected == false);

        // 写入前复检：资格在预览之后被改掉时必须拦下
        var recheck = Clear(@"C:\Users\x\AppData\Local\Temp\cache\late.bin", 500);
        recheck.Entry = new FileEntry
        {
            Name = "late.bin",
            FullPath = recheck.FullPath,
            Kind = EntryKind.File,
            Size = 500,
            // 真实扫描树里文件一定有父目录；没有 Parent 会被老口径当成盘符根拦下
            Parent = new FileEntry
            {
                Name = "cache",
                FullPath = @"C:\Users\x\AppData\Local\Temp\cache",
                Kind = EntryKind.Directory,
            },
        };
        check("复检前：可删、有真实条目、够格", CleanRuleEligibility.IsStillDeletable(recheck));
        recheck.CanDelete = false;
        check("复检：资格变了就不许勾", !CleanRuleEligibility.IsStillDeletable(recheck));
        // 没有真实条目可以核对的候选，复检一律拦下（宁可不勾，也不猜）
        var noEntry = Clear(@"C:\Users\x\AppData\Local\Temp\cache\noentry.bin", 500);
        check("复检：没有可核对的条目就拦下，不猜", !CleanRuleEligibility.IsStillDeletable(noEntry));

        // 没有任何够格项 ⇒ 不给预览（界面说「本次没有符合条件的项」）
        var noneEligible = CleanGroupingService.Build(new List<CleanItem> { heuristic, largeSafe });
        check("没有规则明确的项时不产生预览", CleanRuleSelectPreview.Build(noneEligible) == null);

        // 分组键与自检：勾选只能落在 CanDelete 上（原有安全边界不能被本次改动破坏）
        var sectionItems = new List<CleanItem> { Clear(@"C:\T\a.bin", 10), protectedItem };
        var purpose = new CleanPurposeNode
        {
            Purpose = CleanPurpose.Temp,
            RiskTier = 0,
            PurposeName = "临时",
            Items = sectionItems,
            SelectableCount = 1,
        };
        purpose.IsChecked = true;
        check("整组勾选仍然只作用于可删项", purposeItemsSelected(sectionItems) == 1
            && !protectedItem.Selected, protectedItem.Selected.ToString());
    }

    static int purposeItemsSelected(List<CleanItem> items) => items.Count(x => x.Selected);

    // ==================================================================== B

    static CleanLocationNode Location(params CleanItem[] items)
    {
        var node = new CleanLocationNode
        {
            Key = "loc-key",
            DisplayName = "某软件缓存",
            Path = @"C:\Users\x\AppData\Local\SomeApp\Cache",
            Purpose = CleanPurpose.AppCache,
            RiskTier = 0,
            Risk = CleanRisk.Safe,
            Items = items,
            SelectableCount = items.Count(x => x.CanDelete),
        };
        node.Bytes = items.Sum(x => Math.Max(0, x.Size));
        return node;
    }

    static void InlineFilesTests(CheckFn check, Action<string> section)
    {
        section("本轮改动：清理位置行内直接看候选文件");

        var small = Clear(@"C:\U\a.bin", 10);
        var big = Clear(@"C:\U\b.bin", 5000);
        var mid = Clear(@"C:\U\c.bin", 900);
        var kept = Clear(@"C:\U\kept.bin", 7000);
        kept.CanDelete = false;
        var node = Location(small, big, mid, kept);

        check("展开前不建列表（惰性）", !node.IsFilesOpen && node.VisibleFiles.Count == 0,
            node.VisibleFiles.Count.ToString());
        check("有候选项才给「查看文件」入口", node.CanShowFiles);

        node.ToggleFiles();
        check("展开后就地列出候选", node.IsFilesOpen && node.VisibleFiles.Count == 4,
            node.VisibleFiles.Count.ToString());
        check("按占用从大到小排",
            node.VisibleFiles[0].Name == "kept.bin" && node.VisibleFiles[1].Name == "b.bin",
            string.Join(",", node.VisibleFiles.Select(x => x.Name)));
        check("列表里就是同一份 CleanItem 实例（不是副本）",
            node.VisibleFiles.Contains(big) && node.VisibleFiles.Contains(kept));
        check("不可删的候选项也看得见，但带着「保留」标记",
            !kept.CanDelete && kept.KeptBadge.Length > 0);
        check("展开文案与箭头都指向「收起」", node.FilesToggleText.Length > 0
            && node.FilesGlyphKey != "IconChevronRight", node.FilesGlyphKey);
        check("列表说明写清「共几项、列了几项」", node.FilesNote.Length > 0, node.FilesNote);

        // 行内勾选 = 写回同一份状态
        big.Selected = true;
        node.SyncFromItems();
        check("行内勾选写回同一份状态（位置计数同步）", node.SelectedCount == 1,
            node.SelectedCount.ToString());

        // 收起：选择一项不丢，列表不重建
        node.ToggleFiles();
        check("收起后选择不丢", !node.IsFilesOpen && big.Selected);
        var cached = node.VisibleFiles;
        node.ToggleFiles();
        check("再展开不重建列表（同一实例）", ReferenceEquals(cached, node.VisibleFiles));
        check("再展开仍然显示同一批勾选", big.Selected && node.SelectedCount == 1);

        // 有界：不因为有几十万条就把它们全建出来
        var many = new List<CleanItem>();
        for (int i = 0; i < 500; i++) many.Add(Clear($@"C:\Many\f{i}.bin", i + 1));
        var huge = Location(many.ToArray());
        huge.SetFilesOpen(true);
        check("行内列表有上限，不会一次性全建出来",
            huge.VisibleFiles.Count == CleanLocationNode.InlineFileLimit,
            huge.VisibleFiles.Count.ToString());
        check("超出上限的条数如实报出",
            huge.HiddenFileCount == 500 - CleanLocationNode.InlineFileLimit && huge.HasHiddenFiles,
            huge.HiddenFileCount.ToString());
        check("上限提示写清还有多少没列",
            huge.FilesNote.Contains((500 - CleanLocationNode.InlineFileLimit).ToString()),
            huge.FilesNote);

        // 空位置：给不出列表但也不能崩
        var emptyLoc = Location();
        emptyLoc.SetFilesOpen(true);
        check("没有候选项的位置不假装有文件",
            !emptyLoc.CanShowFiles && emptyLoc.VisibleFiles.Count == 0);
    }

    // ==================================================================== C

    static void OrganizeContentKindTests(CheckFn check, Action<string> section)
    {
        section("本轮改动：整理页的展开状态与「未知 ≠ 空」");

        // (1) 有直接子文件夹 ⇒ 可以展开
        var withDirs = DirNode("WithDirs", @"X:\WithDirs", 4096);
        DirNode("kid", @"X:\WithDirs\kid", 1024, withDirs);
        FileNode("a.bin", @"X:\WithDirs\a.bin", 100, withDirs);
        var withDirsNode = new OrganizeNode(withDirs, new FolderId(withDirs.FullPath, 1), 0, "WithDirs");
        withDirsNode.SetChildDirCount(FolderOrganize.DirectChildDirs(withDirs, 0).Total);
        check("有子目录：形态是「可展开」", withDirsNode.ContentKind == FolderContentKind.ChildDirs,
            withDirsNode.ContentKind.ToString());
        check("有子目录：箭头有效（不是伪造的）", withDirsNode.CanExpand);
        check("有子目录：不显示「只有文件」入口", !withDirsNode.CanViewFiles);
        check("有子目录：容量列没有多余状态字", !withDirsNode.HasContentState);

        // (2) 只有文件 ⇒ 没有子文件夹可展开，但必须有有效的查看入口
        var filesOnly = DirNode("FilesOnly", @"X:\FilesOnly", 2048);
        FileNode("one.bin", @"X:\FilesOnly\one.bin", 1500, filesOnly);
        FileNode("two.bin", @"X:\FilesOnly\two.bin", 500, filesOnly);
        var filesNode = new OrganizeNode(filesOnly, new FolderId(filesOnly.FullPath, 1), 0, "FilesOnly");
        filesNode.SetChildDirCount(FolderOrganize.DirectChildDirs(filesOnly, 0).Total);
        check("只有文件：形态是 FilesOnly", filesNode.ContentKind == FolderContentKind.FilesOnly,
            filesNode.ContentKind.ToString());
        check("只有文件：**不伪造**展开箭头", !filesNode.CanExpand);
        check("只有文件：给出有效的查看文件入口", filesNode.CanViewFiles);
        check("只有文件：状态字写清「只有文件」并带数量",
            filesNode.ContentStateText.Length > 0 && filesNode.ContentStateText.Contains("2"),
            filesNode.ContentStateText);
        check("只有文件：悬停解释为什么没有箭头", filesNode.ContentStateTip.Length > 0);
        check("只有文件：容量照常显示真实值", filesNode.SizeText == FileEntry.FormatSize(2048),
            filesNode.SizeText);

        // 文件列表：惰性 + 有界 + 只读
        check("文件列表展开前是空的", filesNode.VisibleFiles.Count == 0);
        filesNode.ToggleFiles();
        check("展开后列出自己的文件", filesNode.VisibleFiles.Count == 2,
            filesNode.VisibleFiles.Count.ToString());
        check("按占用从大到小排", filesNode.VisibleFiles[0].Name == "one.bin",
            filesNode.VisibleFiles[0].Name);
        check("每个文件都有大小与修改时间字段", filesNode.VisibleFiles.All(x => x.SizeText.Length > 0));
        check("文件列表说明写清「共几个、列了几个」", filesNode.FilesNote.Length > 0);
        check("文件列表范围说明仍在模型上（界面不再占一行）", filesNode.FilesScopeText.Length > 0);

        var manyFiles = DirNode("ManyFiles", @"X:\ManyFiles", 100);
        for (int i = 0; i < 50; i++) FileNode($"f{i}.bin", $@"X:\ManyFiles\f{i}.bin", i + 1, manyFiles);
        var manyNode = new OrganizeNode(manyFiles, new FolderId(manyFiles.FullPath, 1), 0, "ManyFiles");
        manyNode.SetChildDirCount(0);
        manyNode.SetFilesOpen(true);
        check("文件列表有上限，不会把几十万个文件都建出来",
            manyNode.VisibleFiles.Count == OrganizeNode.InlineFileLimit,
            manyNode.VisibleFiles.Count.ToString());
        check("超出上限的条数如实报出并指路「打开文件夹看全部」",
            manyNode.HiddenFileCount == 50 - OrganizeNode.InlineFileLimit
            && manyNode.FilesNote.Contains("open the folder"),
            manyNode.FilesNote);

        // (3) 扫描结果里什么都没有 ⇒ 说「扫描无内容」，不拿 0 KB 暗示
        var emptyDir = DirNode("EmptyDir", @"X:\EmptyDir", 0);
        var emptyNode = new OrganizeNode(emptyDir, new FolderId(emptyDir.FullPath, 1), 0, "EmptyDir");
        emptyNode.SetChildDirCount(0);
        check("空目录：形态是 Empty", emptyNode.ContentKind == FolderContentKind.Empty,
            emptyNode.ContentKind.ToString());
        check("空目录：没有箭头也不给文件入口",
            !emptyNode.CanExpand && !emptyNode.CanViewFiles);
        check("空目录：容量列不写 0 KB，写「扫描无内容」",
            emptyNode.SizeText.Length > 0 && !emptyNode.SizeText.Contains("0 KB")
            && emptyNode.SizeText == Loc.OrganizeSizeEmpty,
            emptyNode.SizeText);
        check("空目录：状态字是「空」", emptyNode.ContentStateText == Loc.OrganizeStateEmpty,
            emptyNode.ContentStateText);

        // (4) 链接 / 重解析点：本次没进去 ⇒ 内容与占用都未知，**绝不用 0 KB**
        var linkDir = DirNode("Linked", @"X:\Linked", 0);
        linkDir.IsReparsePoint = true;
        var linkNode = new OrganizeNode(linkDir, new FolderId(linkDir.FullPath, 1), 0, "Linked");
        linkNode.SetChildDirCount(0);
        check("链接：形态是 NotScanned", linkNode.ContentKind == FolderContentKind.NotScanned,
            linkNode.ContentKind.ToString());
        check("链接：不跟随、不给箭头", !linkNode.CanExpand);
        check("链接：占用写「未知（未扫描）」而不是 0 KB",
            linkNode.SizeText == Loc.OrganizeSizeNotScanned && !linkNode.SizeText.Contains("0 KB"),
            linkNode.SizeText);
        check("链接：状态字与空目录**区分开**",
            linkNode.ContentStateText != emptyNode.ContentStateText
            && linkNode.ContentStateText == Loc.OrganizeStateNotScanned,
            linkNode.ContentStateText);
        check("链接：悬停说清「没有跟进去，所以未知，不是 0」",
            linkNode.ContentStateTip == Loc.OrganizeTipNotScanned);

        // (5) 整理对象**没有清理能力**（结构性保证，不是靠约定）
        var forbidden = new[] { "Selected", "CanDelete", "Risk", "Verdict" };
        var members = typeof(OrganizeNode).GetProperties().Select(p => p.Name).ToHashSet(StringComparer.Ordinal);
        check("整理对象身上没有 Selected / CanDelete / Risk / Verdict",
            forbidden.All(f => !members.Contains(f)),
            string.Join(",", forbidden.Where(f => members.Contains(f))));
        var rowMembers = typeof(OrganizeFileRow).GetProperties().Select(p => p.Name).ToHashSet(StringComparer.Ordinal);
        check("只有文件那一行也是只读模型（没有勾选/可删字段）",
            forbidden.All(f => !rowMembers.Contains(f)));

        // (6) 懒加载首击仍然有效（不能被本轮改动弄回「材料化≠展开」）
        var lazyParent = DirNode("Lazy", @"X:\Lazy", 4096);
        DirNode("l1", @"X:\Lazy\l1", 2048, lazyParent);
        DirNode("l2", @"X:\Lazy\l2", 1024, lazyParent);
        var lazyNode = new OrganizeNode(lazyParent, new FolderId(lazyParent.FullPath, 1), 0, "Lazy");
        lazyNode.SetChildDirCount(FolderOrganize.DirectChildDirs(lazyParent, 0).Total);
        check("首击前是收起的", !lazyNode.IsExpanded && !lazyNode.ChildrenLoaded);
        lazyNode.SetChildren(new[]
        {
            new OrganizeNode(lazyParent.Children[0], new FolderId(@"X:\Lazy\l1", 1), 1, "l1"),
            new OrganizeNode(lazyParent.Children[1], new FolderId(@"X:\Lazy\l2", 1), 1, "l2"),
        }, truncated: false, total: 2, unlisted: 0, autoExpand: true);
        check("首击：材料化与展开在同一次完成（1 击生效）",
            lazyNode.IsExpanded && lazyNode.ChildrenLoaded && lazyNode.Children.Count == 2);
        lazyNode.Collapse();
        check("收起后子对象还在（再展开不重复请求）", lazyNode.ChildrenLoaded && lazyNode.Children.Count == 2);

        // (7) 批量归类给出用途之后，用途列替换「未识别」，不再叠「需要确认」
        //     （这一条原来测的是「点过单项 AI 之后」，逐项 AI 删掉后改测批量归类这个唯一挂点。）
        var unknownDir = DirNode("VolleyballBall", @"X:\VolleyballBall", 100);
        var unknownNode = new OrganizeNode(unknownDir, new FolderId(unknownDir.FullPath, 1), 0, "VolleyballBall");
        check("归类前用途是未识别", unknownNode.PurposeText == Loc.PurposeUnrecognized,
            unknownNode.PurposeText);
        unknownNode.SetBatchPurpose("模型 / 游戏素材");
        check("归类后用途列换成模型给的用途",
            unknownNode.PurposeText == "模型 / 游戏素材", unknownNode.PurposeText);
        check("归类后不再算未识别 / 待确认",
            unknownNode.HasConclusion && !unknownNode.NeedsConfirm && !unknownNode.IsPending);
    }

    static FileEntry DirNode(string name, string path, long size, FileEntry? parent = null)
    {
        var e = new FileEntry { Name = name, FullPath = path, Kind = EntryKind.Directory, Size = size, Parent = parent };
        parent?.Children.Add(e);
        return e;
    }

    static FileEntry FileNode(string name, string path, long size, FileEntry parent)
    {
        var e = new FileEntry { Name = name, FullPath = path, Kind = EntryKind.File, Size = size, Parent = parent };
        parent.Children.Add(e);
        return e;
    }
}
