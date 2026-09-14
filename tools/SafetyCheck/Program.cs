using System.IO;
using AiDiskCleaner;
using AiDiskCleaner.Models;
using AiDiskCleaner.Services;
using AiDiskCleaner.Services.CleanRules;

namespace SafetyCheck;

/// <summary>
/// 大扫货的安全/正确性回归检查。覆盖删除预检、逐项结果、取消、扫描质量统计、
/// 脱敏与错误分类。离线运行，不碰真实用户文件。
/// </summary>
public static class Program
{
    static int _pass, _fail;
    static readonly List<string> Failures = new();

    public static int Main()
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;

        DeletionPreflightTests();
        DeletionExecutorTests();
        CancellationTests();
        ProtectedPathTests();
        ScanQualityTests();
        RecursiveScanQualityTests();
        AiOutputConstraintTests();
        RedactionTests();
        PathRedactionTests();
        ApiKeyEncryptionTests();
        AiGatewayTests();
        StagedDuplicateTests();
        CoordinatorTests();
        WorkStateTests();
        CleanListSnapshotTests();
        LayeredCleanTests.Run(Check, Section);
        ReviewFixesTests.Run(Check, Section);
        ErrorClassificationTests();
        DiagnosticsTests();

        Console.WriteLine();
        Console.WriteLine($"PASS {_pass}   FAIL {_fail}");
        foreach (var f in Failures) Console.WriteLine("  - " + f);
        return _fail == 0 ? 0 : 1;
    }

    // ---------------------------------------------------------------- 删除预检

    static void DeletionPreflightTests()
    {
        Section("删除预检");

        // 1) 存在且未变化 → Ready
        var probe = new FakeProbe();
        probe.Set(@"C:\Temp\a.tmp", n => { n.Size = 100; });

        var pre = new DeletionPreflight(probe);
        var plan = pre.CreatePlan(new[]
        {
            new DeletionTarget { Path = @"C:\Temp\a.tmp", ExpectedSize = 100, SnapshotIsExact = true },
        });

        Check("预检通过项 Ready", plan.Preflight.Items.Count == 1
            && plan.Preflight.Items[0].Outcome == DeletionOutcome.Ready);
        Check("预检 ReadyCount", plan.Preflight.ReadyCount == 1);
        Check("预检放行", plan.HasAnythingToDo);

        // 2) 文件不存在 → NotFound
        var p2 = new FakeProbe();
        var plan2 = new DeletionPreflight(p2).CreatePlan(new[] { new DeletionTarget { Path = @"C:\Temp\gone.tmp" } });
        Check("不存在 → NotFound", plan2.Preflight.Items[0].Outcome == DeletionOutcome.NotFound);
        Check("不存在不计入可执行", plan2.ToExecute.Count == 0);
        Check("NotFound 计数", plan2.Preflight.MissingCount == 1);

        // 3) 路径身份变化 → PathChanged（同一路径换了另一个文件对象）
        var p3 = new FakeProbe();
        p3.Set(@"C:\Temp\swap.tmp", n => n.Identity = new FileIdentity(9, 999, 0));
        var plan3 = new DeletionPreflight(p3).CreatePlan(new[]
        {
            new DeletionTarget { Path = @"C:\Temp\swap.tmp", ExpectedIdentity = new FileIdentity(9, 111, 0) },
        });
        Check("身份不同 → PathChanged", plan3.Preflight.Items[0].Outcome == DeletionOutcome.PathChanged);
        Check("PathChanged 计数", plan3.Preflight.ChangedCount == 1);

        // 4) 大小变化 + 快照精确 → Modified（硬拦截）
        var p4 = new FakeProbe();
        p4.Set(@"C:\Temp\grow.bin", n => n.Size = 500);
        var plan4 = new DeletionPreflight(p4).CreatePlan(new[]
        {
            new DeletionTarget { Path = @"C:\Temp\grow.bin", ExpectedSize = 100, SnapshotIsExact = true },
        });
        Check("大小变化(精确) → Modified", plan4.Preflight.Items[0].Outcome == DeletionOutcome.Modified);
        Check("Modified 不可执行", plan4.ToExecute.Count == 0);

        // 5) 大小变化 + MFT 快照不精确 → 降级成「要确认」，不当硬拦截
        var p5 = new FakeProbe();
        p5.Set(@"C:\Temp\mft.bin", n => n.Size = 500);
        var plan5 = new DeletionPreflight(p5).CreatePlan(new[]
        {
            new DeletionTarget { Path = @"C:\Temp\mft.bin", ExpectedSize = 100, SnapshotIsExact = false },
        });
        Check("大小变化(MFT) → 不硬拦", plan5.Preflight.Items[0].Outcome == DeletionOutcome.Ready);
        Check("大小变化(MFT) → 需要确认", plan5.Preflight.Items[0].Guard == DeletionGuardLevel.NeedsConfirm);

        // 6) 修改时间变化（精确快照）→ Modified
        var p6 = new FakeProbe();
        p6.Set(@"C:\Temp\edit.txt", n => n.Modified = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc));
        var plan6 = new DeletionPreflight(p6).CreatePlan(new[]
        {
            new DeletionTarget
            {
                Path = @"C:\Temp\edit.txt",
                ExpectedModified = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                SnapshotIsExact = true,
            },
        });
        Check("修改时间变化 → Modified", plan6.Preflight.Items[0].Outcome == DeletionOutcome.Modified);

        // 7) 正在使用 → InUse
        var p7 = new FakeProbe();
        p7.Set(@"C:\Temp\lock.dat", n => n.InUse = true);
        var plan7 = new DeletionPreflight(p7).CreatePlan(new[] { new DeletionTarget { Path = @"C:\Temp\lock.dat" } });
        Check("占用 → InUse", plan7.Preflight.Items[0].Outcome == DeletionOutcome.InUse);
        Check("InUse 计数", plan7.Preflight.InUseCount == 1);

        // 8) 权限不足 → AccessDenied
        var p8 = new FakeProbe();
        p8.Set(@"C:\Temp\deny.dat", n => n.AccessDenied = true);
        var plan8 = new DeletionPreflight(p8).CreatePlan(new[] { new DeletionTarget { Path = @"C:\Temp\deny.dat" } });
        Check("无权限 → AccessDenied", plan8.Preflight.Items[0].Outcome == DeletionOutcome.AccessDenied);
        Check("AccessDenied 计数", plan8.Preflight.DeniedCount == 1);
    }

    static void ProtectedPathTests()
    {
        Section("受保护路径");

        var cases = new (string Path, PathGuard Expected, string Why)[]
        {
            (@"C:\", PathGuard.Blocked, "盘符根"),
            (@"C:\Windows", PathGuard.Blocked, "Windows 根"),
            (@"C:\Windows\System32", PathGuard.Blocked, "System32"),
            (@"C:\Windows\WinSxS\abc", PathGuard.Blocked, "WinSxS"),
            (@"C:\Windows\System32\drivers\etc\hosts", PathGuard.Blocked, "System32 深层"),
            (@"C:\pagefile.sys", PathGuard.Blocked, "pagefile"),
            (@"C:\hiberfil.sys", PathGuard.Blocked, "hiberfil"),
            (@"C:\$MFT", PathGuard.Blocked, "MFT 元数据"),
            (@"C:\Program Files\SomeApp", PathGuard.NeedsConfirm, "Program Files 一级"),
            (@"C:\Users\SomeUser", PathGuard.NeedsConfirm, "用户目录一级"),
            (@"C:\Temp\a.tmp", PathGuard.Allowed, "普通临时文件"),
            (@"C:\Users\SomeUser\AppData\Local\Temp\x.tmp", PathGuard.Allowed, "AppData 缓存"),
            (@"C:\Program Files\SomeApp\cache\x.dat", PathGuard.Allowed, "安装目录里的缓存"),
        };

        foreach (var c in cases)
        {
            var r = ProtectedPaths.Classify(c.Path);
            Check($"防护 {c.Why} ({c.Path})", r.Guard == c.Expected,
                $"期望 {c.Expected}，实际 {r.Guard}");
        }

        // 链接目录：不硬拦，但要确认；云占位文件放行
        var link = ProtectedPaths.Classify(@"C:\Temp\link", isReparsePoint: true, isSystem: false, isDirectory: true);
        Check("链接目录 → 需要确认", link.Guard == PathGuard.NeedsConfirm);
        var cloud = ProtectedPaths.Classify(@"C:\Temp\onedrive.docx", isReparsePoint: true, isSystem: false, isDirectory: false);
        Check("云占位文件 → 放行", cloud.Guard == PathGuard.Allowed);

        // 老口径不能变：清理列表的 CanDelete / 默认勾选依赖它
        var winChild = new FileEntry { Name = "Temp", FullPath = @"C:\Windows\Temp", Kind = EntryKind.Directory, Parent = new FileEntry() };
        Check("老口径保留 C:\\Windows\\Temp 受保护", ProtectedPaths.IsProtectedEntry(winChild));
        var normal = new FileEntry { Name = "x.tmp", FullPath = @"C:\Temp\x.tmp", Kind = EntryKind.File, Parent = new FileEntry() };
        Check("老口径不误伤普通文件", !ProtectedPaths.IsProtectedEntry(normal));
    }

    // ---------------------------------------------------------------- 父子去重 / 批量

    static void DeletionExecutorTests()
    {
        Section("父子去重与批量结果");

        // 父目录 + 子文件同时选中 → 子项判为冗余，不重复删
        var probe = new FakeProbe();
        probe.Set(@"C:\Temp\dir", n => { n.IsDirectory = true; n.Identity = new FileIdentity(1, 10, 0); });
        probe.Set(@"C:\Temp\dir\child.tmp", n => { n.Size = 10; n.Identity = new FileIdentity(1, 11, 0); });
        var plan = new DeletionPreflight(probe).CreatePlan(new[]
        {
            new DeletionTarget { Path = @"C:\Temp\dir", IsDirectory = true },
            new DeletionTarget { Path = @"C:\Temp\dir\child.tmp" },
        });
        var child = plan.Preflight.Items.First(x => x.Path.EndsWith("child.tmp", StringComparison.OrdinalIgnoreCase));
        Check("子项被父目录覆盖 → RedundantChild", child.Outcome == DeletionOutcome.RedundantChild);
        Check("父子去重后只删一项", plan.ToExecute.Count == 1);
        Check("RedundantCount", plan.Preflight.RedundantCount == 1);

        // 重复的同一路径只删一次
        var dup = new DeletionPreflight(probe).CreatePlan(new[]
        {
            new DeletionTarget { Path = @"C:\Temp\dir\child.tmp" },
            new DeletionTarget { Path = @"C:\Temp\dir\child.tmp" },
            new DeletionTarget { Path = @"C:\Temp\dir\CHILD.TMP" },
        });
        Check("重复路径去重", dup.ToExecute.Count == 1);

        // 批量部分失败：一项失败不能丢掉其他项的详细结果
        var probe2 = new FakeProbe();
        probe2.Set(@"C:\Temp\ok1.tmp", n => n.Size = 10);
        probe2.Set(@"C:\Temp\bad.tmp", n => n.Size = 20);
        probe2.Set(@"C:\Temp\ok2.tmp", n => n.Size = 30);
        probe2.ThrowOnDelete[FakeProbe.Norm(@"C:\Temp\bad.tmp")] =
            new UnauthorizedAccessException("denied");

        var plan2 = new DeletionPreflight(probe2).CreatePlan(new[]
        {
            new DeletionTarget { Path = @"C:\Temp\ok1.tmp" },
            new DeletionTarget { Path = @"C:\Temp\bad.tmp" },
            new DeletionTarget { Path = @"C:\Temp\ok2.tmp" },
        });
        var batch = new DeletionExecutor(probe2).Execute(plan2);
        Check("批量：总项数不丢", batch.Total == 3);
        Check("批量：成功 2 项", batch.Recycled == 2);
        Check("批量：失败 1 项", batch.Failed + batch.AccessDenied == 1);
        Check("批量：失败项有原因", batch.Results.Any(r => r.Outcome != DeletionOutcome.Recycled && r.Message.Length > 0));
        Check("批量：释放空间累计", batch.FreedBytes == 40);
        Check("批量：逐项结局都有记录", batch.Results.Count == 3);

        // 受保护项在执行阶段也要挡住
        var probe3 = new FakeProbe();
        probe3.Set(@"C:\Windows\System32\evil.dll", n => { });
        var plan3 = new DeletionPreflight(probe3).CreatePlan(new[] { new DeletionTarget { Path = @"C:\Windows\System32\evil.dll" } });
        var b3 = new DeletionExecutor(probe3).Execute(plan3);
        Check("受保护项不执行", b3.Recycled == 0 && probe3.DeletedPaths.Count == 0);

        // 预检之后文件被占用 → 执行阶段再次探测并拦下
        var probe4 = new FakeProbe();
        var node = probe4.Set(@"C:\Temp\lazy.tmp", n => n.Size = 5);
        var plan4 = new DeletionPreflight(probe4).CreatePlan(new[] { new DeletionTarget { Path = @"C:\Temp\lazy.tmp" } });
        node.InUse = true; // 预检通过之后才被占用
        var b4 = new DeletionExecutor(probe4).Execute(plan4);
        Check("执行前重新探测占用", b4.InUse == 1 && probe4.DeletedPaths.Count == 0);
        Check("执行阶段探针被调用两次以上", node.Probes >= 2);

        // 系统没真删掉 → Failed，不能谎报成功
        var probe5 = new FakeProbe();
        probe5.Set(@"C:\Temp\sticky.tmp", n => n.Size = 7);
        probe5.StickAround.Add(FakeProbe.Norm(@"C:\Temp\sticky.tmp"));
        var plan5 = new DeletionPreflight(probe5).CreatePlan(new[] { new DeletionTarget { Path = @"C:\Temp\sticky.tmp" } });
        var b5 = new DeletionExecutor(probe5).Execute(plan5);
        Check("没删掉就报失败", b5.Failed == 1 && b5.Recycled == 0);

        // 敏感位置未确认 → 记成「用户跳过」，不是失败
        var probe6 = new FakeProbe();
        probe6.Set(@"C:\Program Files\App", n => n.IsDirectory = true);
        var plan6 = new DeletionPreflight(probe6).CreatePlan(
            new[] { new DeletionTarget { Path = @"C:\Program Files\App", IsDirectory = true } },
            sensitiveConfirmed: false);
        var b6 = new DeletionExecutor(probe6).Execute(plan6);
        Check("敏感项未确认 → 跳过而非失败", b6.Skipped == 1 && b6.Failed == 0);
        Check("敏感项未被删除", probe6.DeletedPaths.Count == 0);
    }

    static void CancellationTests()
    {
        Section("取消");

        var probe = new FakeProbe();
        for (int i = 0; i < 6; i++) probe.Set($@"C:\Temp\c{i}.tmp", n => n.Size = 1);
        var targets = Enumerable.Range(0, 6)
            .Select(i => new DeletionTarget { Path = $@"C:\Temp\c{i}.tmp" })
            .ToList();
        var plan = new DeletionPreflight(probe).CreatePlan(targets);

        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var batch = new DeletionExecutor(probe).Execute(plan, null, cts.Token);

        Check("取消不抛异常", batch != null);
        Check("取消被标记", batch.Canceled);
        Check("取消后什么都没删", batch.Recycled == 0);

        // 取消也要保留已完成项的记录：允许删 1 项后再取消
        var probe2 = new FakeProbe();
        for (int i = 0; i < 4; i++) probe2.Set($@"C:\Temp\d{i}.tmp", n => n.Size = 1);
        var plan2 = new DeletionPreflight(probe2).CreatePlan(Enumerable.Range(0, 4)
            .Select(i => new DeletionTarget { Path = $@"C:\Temp\d{i}.tmp" }).ToList());
        using var cts2 = new CancellationTokenSource();
        // 注意用同步的 IProgress：Progress<T> 会 post 到线程池/UI 线程，
        // 回调在 Execute 返回之后才跑，取消就永远赶不上。真实界面里用户是在两次
        // 操作之间点的停止，同步回调才是这一行为的正确建模。
        int seen = 0;
        var progress = new SyncProgress<DeletionItemResult>(_ =>
        {
            if (++seen >= 2) cts2.Cancel();
        });
        var batch2 = new DeletionExecutor(probe2).Execute(plan2, progress, cts2.Token);
        Check("取消后仍有部分结果", batch2.Total is >= 1 and < 4, $"实际 {batch2.Total}");
        Check("取消状态为真", batch2.Canceled);

        // 预检也响应取消
        bool canceled = false;
        try
        {
            using var cts3 = new CancellationTokenSource();
            cts3.Cancel();
            var many = Enumerable.Range(0, 100).Select(i => new DeletionTarget { Path = $@"C:\Temp\x{i}.tmp" }).ToList();
            new DeletionPreflight(probe).CreatePlan(many, false, cts3.Token);
        }
        catch (OperationCanceledException) { canceled = true; }
        Check("预检响应取消", canceled);
    }

    // ---------------------------------------------------------------- 扫描质量

    static void ScanQualityTests()
    {
        Section("扫描质量模型");

        var q = new ScanQuality { Source = ScanSource.Recursive, FilesRead = 10, DirsRead = 3 };
        q.Complete = !q.HasSkips;
        Check("无跳过 → 完整", q.Complete && !q.HasSkips);

        q.SkippedDirs = 2;
        Check("有跳过 → 不完整", q.HasSkips);

        var other = new ScanQuality { SkippedDirs = 1, PermissionErrors = 3, PathErrors = 1, ReadErrors = 2, ReparsePoints = 4, FilesRead = 5, DirsRead = 6, HardLinks = 7, OrphanRecords = 8, UnparsedRecords = 9 };
        q.Merge(other);
        Check("Merge 累加跳过", q.SkippedDirs == 3);
        Check("Merge 累加权限错误", q.PermissionErrors == 3);
        Check("Merge 累加重解析点", q.ReparsePoints == 4);
        Check("Merge 累加孤儿记录", q.OrphanRecords == 8);
        Check("Merge 累加解析失败", q.UnparsedRecords == 9);
        Check("TotalErrors 汇总", q.TotalErrors == 6);

        var mft = new ScanQuality { Source = ScanSource.Mft, FilesRead = 100, DirsRead = 5, DurationMs = 1234 };
        Check("耗时换算", Math.Abs(mft.DurationSeconds - 1.234) < 0.001);
    }

    static void RecursiveScanQualityTests()
    {
        Section("递归扫描质量统计");

        string root = Path.Combine(Path.GetTempPath(), "dsh-scancheck-" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            Directory.CreateDirectory(root);
            Directory.CreateDirectory(Path.Combine(root, "sub"));
            Directory.CreateDirectory(Path.Combine(root, "sub", "deep"));
            File.WriteAllText(Path.Combine(root, "a.txt"), "hello");
            File.WriteAllText(Path.Combine(root, "sub", "b.txt"), "world!");
            File.WriteAllText(Path.Combine(root, "sub", "deep", "c.txt"), "!!");

            var svc = new RecursiveScanService();
            var entry = svc.Scan(root);
            var q = svc.LastQuality;

            Check("递归扫描产出质量报告", q != null);
            Check("来源标记为递归", q!.Source == ScanSource.Recursive);
            Check("统计到 3 个文件", q.FilesRead == 3, $"实际 {q.FilesRead}");
            Check("统计到 3 个目录", q.DirsRead == 3, $"实际 {q.DirsRead}");
            Check("完整扫描 Complete=true", q.Complete);
            Check("无权限错误", q.PermissionErrors == 0);
            Check("目录树大小正确", entry.Size == 5 + 6 + 2);
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { }
        }

        // 读不到的根目录：必须报成「跳过 + 路径错误 + 不完整」，不能静默吞掉
        var missing = new RecursiveScanService();
        missing.Scan(Path.Combine(Path.GetTempPath(), "dsh-does-not-exist-" + Guid.NewGuid().ToString("N")[..8]));
        var mq = missing.LastQuality;
        Check("不存在的根 → 有质量报告", mq != null);
        Check("不存在的根 → 跳过目录数 > 0", mq!.SkippedDirs > 0);
        Check("不存在的根 → 路径错误数 > 0", mq.PathErrors > 0);
        Check("不存在的根 → Complete=false", !mq.Complete);
        Check("不存在的根 → HasSkips", mq.HasSkips);
    }

    // ---------------------------------------------------------------- AI 输出约束

    static void AiOutputConstraintTests()
    {
        Section("AI 输出约束");

        var items = new List<CleanItem>
        {
            new() { Name = "a.tmp", FullPath = @"C:\Temp\a.tmp", Risk = CleanRisk.Safe, CanDelete = true, Size = 10 },
            new() { Name = "b.tmp", FullPath = @"C:\Temp\b.tmp", Risk = CleanRisk.Confirm, CanDelete = true, Size = 20, Selected = false },
        };

        // 格式错误的输出：解析不出来就保留本地结果，绝不抛异常
        int applied = AiNoteParser.Apply(items, "```\n这不是 GOTO 行\n随便写点什么\n```");
        Check("垃圾输出不写说明", applied == 0);
        Check("垃圾输出后规则原因保留", items[0].NoteText == items[0].Reason);
        Check("越权输出不改风险", items[0].Risk == CleanRisk.Safe && items[1].Risk == CleanRisk.Confirm);
        Check("越权输出不碰 CanDelete", items[0].CanDelete && items[1].CanDelete);
        Check("越权输出不勾选", items[0].Selected == false && items[1].Selected == false);

        // 风险词被塞进说明列 → 当噪音丢掉
        int a2 = AiNoteParser.Apply(items, "GOTO C:\\Temp\\a.tmp\tsafe");
        Check("纯风险词不算说明", a2 == 0);
        Check("风险档位未被覆盖", items[0].Risk == CleanRisk.Safe);

        // 正常输出 → 只写 AiNote
        int a3 = AiNoteParser.Apply(items, "GOTO C:\\Temp\\a.tmp\t这是程序崩溃时留下的记录文件");
        Check("正常输出写入说明", a3 == 1 && items[0].AiNote.Length > 0);
        Check("写入说明后风险不变", items[0].Risk == CleanRisk.Safe);
        Check("写入说明后勾选不变", !items[0].Selected);

        // 编造的路径（清单里没有）绝不接受
        int a4 = AiNoteParser.Apply(items, "GOTO C:\\Temp\\invented.tmp\t不存在的东西");
        Check("未知路径被忽略", a4 == 0);

        // 超长说明被截断
        var longItems = new List<CleanItem> { new() { Name = "c.tmp", FullPath = @"C:\Temp\c.tmp", Risk = CleanRisk.Safe } };
        AiNoteParser.Apply(longItems, "GOTO C:\\Temp\\c.tmp\t" + new string('长', 500));
        Check("说明长度受限", longItems[0].AiNote.Length <= AiNoteParser.MaxNoteLength);
    }

    // ---------------------------------------------------------------- 脱敏 / 错误分类

    static void RedactionTests()
    {
        Section("脱敏");

        Check("OpenAI key 被抹掉",
            LogRedactor.Scrub("key=sk-abcdefghijklmnopqrstuvwxyz012345").Contains(LogRedactor.Mask));
        Check("Anthropic key 被抹掉",
            LogRedactor.Scrub("x sk-ant-api03-abcdefghijklmnopqrstuvwxyz").Contains(LogRedactor.Mask));
        Check("Google key 被抹掉",
            LogRedactor.Scrub("AIzaSyA1234567890abcdefghijklmnopqrstu").Contains(LogRedactor.Mask));
        Check("Bearer token 被抹掉",
            LogRedactor.Scrub("Authorization: Bearer abcdefghijklmnop.qrstuvwx").Contains(LogRedactor.Mask));
        Check("JSON 里的 apiKey 被抹掉",
            LogRedactor.Scrub("{\"apiKey\":\"totallysecretvalue123\"}").Contains(LogRedactor.Mask));
        Check("查询串里的 key 被抹掉",
            LogRedactor.Scrub("https://x/y?api_key=abcdef123456&z=1").Contains(LogRedactor.Mask));
        Check("脱敏后不再像密钥",
            !LogRedactor.LooksSecret(LogRedactor.Scrub("Authorization: Bearer abcdefghijklmnop")));
        Check("普通文本不被误伤",
            LogRedactor.Scrub("扫描完成，共 12345 个文件") == "扫描完成，共 12345 个文件");

        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrWhiteSpace(home))
        {
            string p = Path.Combine(home, "AppData", "Local", "Temp", "x.tmp");
            string masked = LogRedactor.ScrubPath(p);
            Check("用户目录被替换成占位符", masked.StartsWith("<UserProfile>", StringComparison.Ordinal), masked);
            Check("脱敏后不含真实用户名",
                !masked.Contains(Environment.UserName, StringComparison.OrdinalIgnoreCase), masked);
        }
        else
        {
            Check("用户目录被替换成占位符(跳过：无用户目录)", true);
            Check("脱敏后不含真实用户名(跳过)", true);
        }

        Check("盘符路径结构保留", LogRedactor.ScrubPath(@"C:\Windows\Temp").StartsWith(@"C:\Windows", StringComparison.Ordinal));
        Check("ScrubAll 同时做两件事",
            LogRedactor.ScrubAll("sk-abcdefghijklmnopqrstuvwxyz0123").Contains(LogRedactor.Mask));
    }

    // ---------------------------------------------------------------- 路径脱敏 / API Key / 网关

    static void PathRedactionTests()
    {
        Section("路径脱敏（发给 AI 之前）");

        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrWhiteSpace(home))
        {
            string p = Path.Combine(home, "AppData", "Local", "Temp", "x.tmp");
            string red = PathRedactor.Redact(p);
            Check("当前用户目录 → <UserProfile>", red.StartsWith(PathRedactor.UserProfileToken, StringComparison.Ordinal), red);
            Check("脱敏后不含用户名", !red.Contains(Environment.UserName, StringComparison.OrdinalIgnoreCase), red);
            Check("脱敏结果自检通过", PathRedactor.IsRedacted(red));
            Check("原始路径自检不通过", !PathRedactor.IsRedacted(p));
        }
        else
        {
            Check("当前用户目录 → <UserProfile>(跳过)", true);
            Check("脱敏后不含用户名(跳过)", true);
            Check("脱敏结果自检通过(跳过)", true);
            Check("原始路径自检不通过(跳过)", true);
        }

        // 别的用户目录也要抹掉
        string other = PathRedactor.Redact(@"C:\Users\SomeOtherPerson\Documents\a.docx");
        Check("其他用户目录被替换", other.Contains(PathRedactor.UserToken, StringComparison.Ordinal), other);
        Check("其他用户名不再出现", !other.Contains("SomeOtherPerson", StringComparison.OrdinalIgnoreCase), other);

        // 系统路径保持结构（AI 需要靠它判断这是什么）
        string sys = PathRedactor.Redact(@"C:\Windows\Temp\x.log");
        Check("系统路径结构保留", sys == @"C:\Windows\Temp\x.log", sys);
        Check("盘符路径不被误改", PathRedactor.Redact(@"D:\Games\a.bin") == @"D:\Games\a.bin");

        // 默认策略：设置关着就不能把完整路径发出去
        App.Settings.AiSendFullPaths = false;
        string outbound = PathRedactor.Outbound(Path.Combine(home, "secret.txt"));
        Check("默认不发完整路径",
            !outbound.Contains(home, StringComparison.OrdinalIgnoreCase) || home.Length == 0, outbound);
        App.Settings.AiSendFullPaths = true;
        Check("用户明确允许后才发完整路径",
            PathRedactor.Outbound(@"C:\Temp\a.tmp") == @"C:\Temp\a.tmp");
        App.Settings.AiSendFullPaths = false;

        // 脱敏路径也要能映射回条目（AI 回的是脱敏路径）
        var items = new List<CleanItem>
        {
            new() { Name = "x.tmp", FullPath = Path.Combine(home, "AppData", "Local", "Temp", "x.tmp") },
        };
        string redKey = PathRedactor.Redact(items[0].FullPath);
        // 关键：发出去的是脱敏路径，所以映射回条目也必须用同一套规则。
        int n = AiNoteParser.Apply(items, $"GOTO {redKey}\t这是临时文件", PathRedactor.Redact);
        Check("脱敏路径能映射回条目", n == 1 && items[0].AiNote.Length > 0, $"applied={n}");

        // 反过来：不按脱敏规则映射时，模型编不出真实路径（它只见过脱敏路径）
        var strict = new List<CleanItem>
        {
            new() { Name = "x.tmp", FullPath = items[0].FullPath },
        };
        int n2 = AiNoteParser.Apply(strict, $"GOTO {redKey}\t这是临时文件");
        Check("未按脱敏规则映射时不误写", n2 == 0);
    }

    static void ApiKeyEncryptionTests()
    {
        Section("API Key 加密");

        const string secret = "sk-test-abcdefghijklmnopqrstuvwxyz0123456789";

        string? cipher = SecretProtector.Protect(secret);
        Check("DPAPI 可用", SecretProtector.Available);
        Check("能加密出密文", !string.IsNullOrEmpty(cipher));
        Check("密文不含明文", cipher != null && !cipher.Contains(secret, StringComparison.Ordinal));
        Check("解密还原", SecretProtector.Unprotect(cipher) == secret);
        Check("空值安全", SecretProtector.Unprotect("") == "");
        Check("垃圾密文安全降级为 null", SecretProtector.Unprotect("not-base64!!!") == null);
        Check("被篡改的密文不会解出东西", SecretProtector.Unprotect("AAAA") == null);

        // 密钥不进 settings.json
        var s = new AppSettings();
        s.AiProviders.Add(new AiProviderCfg { Id = "p1", Name = "n", BaseUrl = "https://x", ApiKey = secret });
        string json = System.Text.Json.JsonSerializer.Serialize(s);
        Check("settings.json 里没有明文密钥", !json.Contains(secret, StringComparison.Ordinal));
        Check("settings.json 里没有 ApiKey 字段", !json.Contains("ApiKey", StringComparison.Ordinal));
        Check("旧字段 AiApiKey 也不落盘", !json.Contains("AiApiKey", StringComparison.Ordinal));

        // 端到端：写盘 → 读回，明文只在加密仓库里
        string dir = Path.Combine(Path.GetTempPath(), "dsh-secretcheck-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        AppPaths.OverrideDirectory = dir;
        try
        {
            var settings = new AppSettings();
            settings.AiProviders.Add(new AiProviderCfg { Id = "p1", Name = "n", BaseUrl = "https://x", ApiKey = secret });
            settings.AiActiveId = "p1";
            settings.Save();

            Check("写盘后 settings.json 存在", File.Exists(Path.Combine(dir, "settings.json")));
            Check("写盘后 secrets.dat 存在", File.Exists(Path.Combine(dir, "secrets.dat")));

            string settingsText = File.ReadAllText(Path.Combine(dir, "settings.json"));
            string secretsText = File.ReadAllText(Path.Combine(dir, "secrets.dat"));
            Check("settings.json 无明文", !settingsText.Contains(secret, StringComparison.Ordinal));
            Check("secrets.dat 无明文", !secretsText.Contains(secret, StringComparison.Ordinal));
            Check("secrets.dat 里也没有能被日志扫描认出的密钥", !LogRedactor.LooksSecret(secretsText));

            var loaded = AppSettings.Load();
            Check("重新加载能取回密钥", loaded.CurrentProvider()?.ApiKey == secret,
                loaded.CurrentProvider()?.ApiKey ?? "(null)");
            Check("重新加载后 HasStoredKey", loaded.CurrentProvider()?.HasStoredKey == true);

            // 清除密钥
            loaded.ClearAllApiKeys();
            Check("清除后内存里没有密钥", loaded.CurrentProvider() is null || loaded.CurrentProvider()!.ApiKey.Length == 0);
            Check("清除后 secrets.dat 被删掉", !File.Exists(Path.Combine(dir, "secrets.dat")));
            string afterClear = File.ReadAllText(Path.Combine(dir, "settings.json"));
            Check("清除后 settings.json 仍无明文", !afterClear.Contains(secret, StringComparison.Ordinal));

            // 旧版明文迁移：手工写一份带明文 ApiKey 的 settings.json，Load 应该把它搬进加密仓库并抹掉明文
            string legacy = "{\"UiRev\":2,\"AiActiveId\":\"p1\",\"AiModel\":\"m\",\"AiProviders\":[" +
                            "{\"Id\":\"p1\",\"Name\":\"n\",\"BaseUrl\":\"https://x\",\"Protocol\":\"completions\",\"ApiKey\":\"" + secret + "\"}]}";
            File.WriteAllText(Path.Combine(dir, "settings.json"), legacy);
            var migrated = AppSettings.Load();
            Check("旧版明文密钥被读出", migrated.CurrentProvider()?.ApiKey == secret);
            Check("迁移后 secrets.dat 已生成", File.Exists(Path.Combine(dir, "secrets.dat")));
            string migratedText = File.ReadAllText(Path.Combine(dir, "settings.json"));
            Check("迁移后明文已从 settings.json 抹掉", !migratedText.Contains(secret, StringComparison.Ordinal));
        }
        finally
        {
            AppPaths.OverrideDirectory = null;
            try { Directory.Delete(dir, true); } catch { }
        }
    }

    static void AiGatewayTests()
    {
        Section("AI 通道统一入口");

        // 离线测试：整条换成假通道，默认不开 sidecar
        var fake = new FakeGatewayProvider { SidecarEnabled = false };
        AiGateway.Provider = fake;

        // 瞬时错误重试后成功
        FakeGateway flaky = null!;
        flaky = new FakeGateway("http", (_, _) =>
        {
            if (flaky.Calls < 3) throw new System.Net.Http.HttpRequestException("connection reset");
            return Task.FromResult(new AiReply { Text = "ok" });
        });
        fake.Direct = flaky;
        var ok = AiGateway.SendAsync(new AiRequest { Timeout = TimeSpan.FromSeconds(10) },
            null, CancellationToken.None).GetAwaiter().GetResult();
        Check("瞬时故障会重试并最终成功", ok.Text == "ok");
        Check("重试次数符合预期", flaky.Calls == 3, $"calls={flaky.Calls}");

        // 401 不该重试
        var denied = new FakeGateway("http", (_, _) =>
            throw new System.Net.Http.HttpRequestException("unauthorized",
                null, System.Net.HttpStatusCode.Unauthorized));
        fake.Direct = denied;
        try
        {
            AiGateway.SendAsync(new AiRequest { Timeout = TimeSpan.FromSeconds(10) },
                null, CancellationToken.None).GetAwaiter().GetResult();
            Check("401 会失败", false);
        }
        catch { Check("401 会失败", true); }
        Check("401 只尝试一次（重试没意义）", denied.Calls == 1, $"calls={denied.Calls}");

        // 用户取消要原样抛出，不被当成超时/故障
        var slow = new FakeGateway("http", async (_, ct) =>
        {
            await Task.Delay(5000, ct);
            return new AiReply { Text = "late" };
        });
        fake.Direct = slow;
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        bool canceledOk = false;
        try
        {
            AiGateway.SendAsync(new AiRequest { Timeout = TimeSpan.FromSeconds(30) }, null, cts.Token)
                .GetAwaiter().GetResult();
        }
        catch (OperationCanceledException) { canceledOk = true; }
        Check("用户取消原样抛出", canceledOk);

        // sidecar 挂了要能自动落到内置 HTTP
        var brokenSidecar = new FakeGateway("sidecar", (_, _) =>
            throw new InvalidOperationException("sidecar unavailable"));
        var healthyHttp = new FakeGateway("http", (_, _) => Task.FromResult(new AiReply { Text = "from-http" }));
        fake.SidecarEnabled = true;
        fake.Sidecar = brokenSidecar;
        fake.Direct = healthyHttp;
        var fell = AiGateway.SendAsync(new AiRequest { Timeout = TimeSpan.FromSeconds(10) },
            null, CancellationToken.None).GetAwaiter().GetResult();
        Check("sidecar 失败自动 fallback 到 HTTP", fell.Text == "from-http" && healthyHttp.Calls == 1);

        // sidecar 成功就不该碰 HTTP
        var goodSidecar = new FakeGateway("sidecar", (_, _) => Task.FromResult(new AiReply { Text = "from-sidecar" }));
        var untouchedHttp = new FakeGateway("http", (_, _) => Task.FromResult(new AiReply { Text = "nope" }));
        fake.Sidecar = goodSidecar;
        fake.Direct = untouchedHttp;
        var viaSidecar = AiGateway.SendAsync(new AiRequest { Timeout = TimeSpan.FromSeconds(10) },
            null, CancellationToken.None).GetAwaiter().GetResult();
        Check("sidecar 可用时优先走 sidecar",
            viaSidecar.Text == "from-sidecar" && untouchedHttp.Calls == 0);
        Check("状态记录的是 sidecar 通道", AiGateway.LastStatus.Channel == "sidecar");

        // 记录耗时
        Check("记录通道与耗时", AiGateway.LastStatus.Ok && AiGateway.LastStatus.ElapsedMs >= 0);

        // 限流：连续两次请求之间至少间隔 MinInterval
        var stamp = new List<DateTime>();
        var timed = new FakeGateway("http", (_, _) =>
        {
            stamp.Add(DateTime.UtcNow);
            return Task.FromResult(new AiReply { Text = "x" });
        });
        fake.SidecarEnabled = false;
        fake.Direct = timed;
        AiGateway.MinInterval = TimeSpan.FromMilliseconds(150);
        AiGateway.SendAsync(new AiRequest { Timeout = TimeSpan.FromSeconds(10) }, null, CancellationToken.None).GetAwaiter().GetResult();
        AiGateway.SendAsync(new AiRequest { Timeout = TimeSpan.FromSeconds(10) }, null, CancellationToken.None).GetAwaiter().GetResult();
        Check("两次请求之间有节流间隔",
            stamp.Count == 2 && (stamp[1] - stamp[0]).TotalMilliseconds >= 120,
            stamp.Count == 2 ? (stamp[1] - stamp[0]).TotalMilliseconds.ToString("0") + "ms" : "n/a");
        AiGateway.MinInterval = TimeSpan.FromMilliseconds(250);

        // 瞬时判定
        Check("连接失败算瞬时", AiGateway.IsTransient(new System.Net.Http.HttpRequestException("x")));
        Check("429 算瞬时", AiGateway.IsTransient(new System.Net.Http.HttpRequestException("x", null, (System.Net.HttpStatusCode)429)));
        Check("503 算瞬时", AiGateway.IsTransient(new System.Net.Http.HttpRequestException("x", null, System.Net.HttpStatusCode.ServiceUnavailable)));
        Check("400 不算瞬时", !AiGateway.IsTransient(new System.Net.Http.HttpRequestException("x", null, System.Net.HttpStatusCode.BadRequest)));
        Check("解析错误不算瞬时", !AiGateway.IsTransient(new System.Text.Json.JsonException("bad")));

        // 收尾：把假提供者摘掉，别影响后面的检查
        AiGateway.Provider = null;
    }

    // ---------------------------------------------------------------- 重复文件分阶段哈希

    static void StagedDuplicateTests()
    {
        Section("重复文件分阶段哈希");

        string root = Path.Combine(Path.GetTempPath(), "dsh-dupcheck-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(root);
        try
        {
            const int mb = 1024 * 1024;
            long size = 12 * mb; // > MinSize(8MB)

            // 同一份内容，两个路径（长度不同，短的应该被当作「留这个」）
            byte[] content = MakeContent(size, seed: 1);
            string a = Path.Combine(root, "aaa", "same.bin");
            string b = Path.Combine(root, "bbbbbbbbbb", "same.bin");
            Directory.CreateDirectory(Path.GetDirectoryName(a)!);
            Directory.CreateDirectory(Path.GetDirectoryName(b)!);
            File.WriteAllBytes(a, content);
            File.WriteAllBytes(b, content);

            // 同样大小但内容不同（首段就不同）→ 不能算重复
            byte[] other = MakeContent(size, seed: 2);
            string c = Path.Combine(root, "different.bin");
            File.WriteAllBytes(c, other);

            // 首段相同、尾段不同 → 第三阶段必须把它们分开
            byte[] headSameTailDiff = (byte[])content.Clone();
            headSameTailDiff[^1] ^= 0xFF;
            string d = Path.Combine(root, "head-same-tail-diff.bin");
            File.WriteAllBytes(d, headSameTailDiff);

            var files = new List<FileEntry>
            {
                Entry(a, size), Entry(b, size), Entry(c, size), Entry(d, size),
            };

            var run = DuplicateDetector.Find(files, null, CancellationToken.None);
            var groups = run.Groups;
            var stats = run.Stats;

            Check("找到 1 组重复", groups.Count == 1, $"groups={groups.Count}");
            Check("重复组恰好 2 个成员", groups.Count == 1 && groups[0].Members.Count == 2);
            Check("内容不同不算重复",
                groups.Count == 1 && groups[0].Members.All(x => !x.FullPath.EndsWith("different.bin", StringComparison.OrdinalIgnoreCase)));
            Check("首段相同尾段不同被分开",
                groups.Count == 1 && groups[0].Members.All(x => !x.FullPath.Contains("head-same-tail-diff", StringComparison.OrdinalIgnoreCase)));
            Check("保留路径最短的那个",
                groups.Count == 1 && groups[0].Members[0].FullPath == a, groups.Count == 1 ? groups[0].Members[0].FullPath : "n/a");
            Check("统计到 4 个候选", stats.Candidates == 4, $"cand={stats.Candidates}");
            Check("统计到 1 个大小分组", stats.SizeGroups == 1, $"sizeGroups={stats.SizeGroups}");
            Check("首段哈希算过", stats.HeadHashes >= 4, $"head={stats.HeadHashes}");
            Check("尾段哈希阶段有跑", stats.TailHashes > 0, $"tail={stats.TailHashes}");
            Check("完整哈希阶段有跑", stats.FullHashes > 0, $"full={stats.FullHashes}");
            Check("哈希字节数有统计", stats.BytesRead > 0);
            Check("完整跑完时 Complete=true", run.Complete, run.Note);

            // 小于阈值的文件不参与
            byte[] tiny = MakeContent(1024, seed: 3);
            string t1 = Path.Combine(root, "t1.bin");
            string t2 = Path.Combine(root, "t2.bin");
            File.WriteAllBytes(t1, tiny);
            File.WriteAllBytes(t2, tiny);
            var tinyRun = DuplicateDetector.Find(
                new List<FileEntry> { Entry(t1, 1024), Entry(t2, 1024) },
                null, CancellationToken.None);
            Check("小于 8MB 的不参与", tinyRun.Groups.Count == 0 && tinyRun.Stats.Candidates == 0);

            // 云占位文件（重解析点）不碰
            var cloud = Entry(a, size);
            cloud.IsReparsePoint = true;
            var cloudRun = DuplicateDetector.Find(
                new List<FileEntry> { cloud, Entry(b, size) },
                null, CancellationToken.None);
            Check("云占位文件被跳过", cloudRun.Groups.Count == 0 && cloudRun.Stats.SkippedCloud == 1);

            // 硬链接合并：两条路径报同一个 file id → 不是重复文件
            var hardLinked = new List<FileEntry> { Entry(a, size), Entry(b, size) };
            var sameId = new FileIdentity(1, 42, 0);
            var hlRun = DuplicateDetector.Find(hardLinked, _ => sameId, CancellationToken.None);
            Check("硬链接被合并（不算重复）", hlRun.Groups.Count == 0, $"groups={hlRun.Groups.Count}");
            Check("硬链接计数被记录", hlRun.Stats.SkippedHardLink == 1);

            // 不同 file id 时仍然是重复
            var idRun = DuplicateDetector.Find(hardLinked,
                p => p == a ? new FileIdentity(1, 1, 0) : new FileIdentity(1, 2, 0),
                CancellationToken.None);
            Check("不同 file id 仍判为重复", idRun.Groups.Count == 1);

            // 取消要能中断
            using (var cts = new CancellationTokenSource())
            {
                cts.Cancel();
                bool canceled = false;
                try { DuplicateDetector.Find(files, null, cts.Token); }
                catch (OperationCanceledException) { canceled = true; }
                Check("分阶段哈希响应取消", canceled);
            }

            // 网络盘识别
            Check("UNC 路径算网络盘", DuplicateDetector.IsOnNetworkDrive(@"\\server\share\a.bin"));
            Check("本地盘不算网络盘", !DuplicateDetector.IsOnNetworkDrive(Path.Combine(root, "a.bin")));

            // ---- 预算：按块扣减，撞上就明确报「没跑完」 ----
            var budgetRun = DuplicateDetector.Find(files, null, CancellationToken.None,
                progress: null, budgetBytes: 1);
            Check("预算耗尽时不再声称已核实",
                budgetRun.Groups.Count == 0, $"groups={budgetRun.Groups.Count}");
            Check("预算耗尽被明确标记", budgetRun.BudgetExhausted && !budgetRun.Complete);
            Check("预算耗尽有说明", budgetRun.Note.Length > 0, budgetRun.Note);
            Check("预算耗尽不会把没验证的算成重复",
                budgetRun.Stats.FullHashes == 0 && budgetRun.Stats.BytesRead <= 2);

            // ---- 进度：能收到阶段与字节数，且被节流（不会每个文件一条） ----
            var reports = new List<DuplicateProgress>();
            var syncProgress = new SyncProgress<DuplicateProgress>(reports.Add);
            DuplicateDetector.Find(files, null, CancellationToken.None, syncProgress);
            Check("进度有回报", reports.Count > 0, $"reports={reports.Count}");
            Check("进度带上阶段名", reports.Any(r => r.Phase.Length > 0));
            Check("进度带上已读字节", reports.Any(r => r.BytesRead > 0));
            Check("最后一条是收尾", reports[^1].Percent == 100, reports[^1].Phase);

            // ---- 读到一半变了（长度对不上）不算已验证重复 ----
            // 两个条目都声称 12MB，但其中一个文件实际只有 6MB（扫描之后被截断了）。
            // 首段哈希会相同，进入尾段阶段时短文件读不到那么远 → 必须记成「变了」，
            // 而且**不能**把它算成一条已验证的重复。
            string truncated = Path.Combine(root, "truncated.bin");
            File.WriteAllBytes(truncated, content.AsSpan(0, (int)(size / 2)).ToArray());
            var claimBig = new FileEntry { Name = "truncated.bin", FullPath = truncated, Size = size, Kind = EntryKind.File };
            var claimFull = new FileEntry { Name = "same2.bin", FullPath = b, Size = size, Kind = EntryKind.File };
            var shrinkRun = DuplicateDetector.Find(
                new List<FileEntry> { claimBig, claimFull }, null, CancellationToken.None);
            Check("长度对不上时不计入重复", shrinkRun.Groups.Count == 0, $"groups={shrinkRun.Groups.Count}");
            Check("长度对不上被单独计数", shrinkRun.Stats.SkippedChanged > 0, $"changed={shrinkRun.Stats.SkippedChanged}");

            // ---- 检测没跑完时，产出的条目不能被预先勾选 ----
            var incomplete = new DuplicateScanResult
            {
                Groups = new List<DuplicateGroup>
                {
                    new() { Size = size, Members = new List<FileEntry> {
                        Entry(Path.Combine(root, "keep.bin"), size),
                        Entry(Path.Combine(root, "dup.bin"), size) } },
                },
                Stats = new DuplicateStats(),
                Complete = false,
                BudgetExhausted = true,
            };
            var items = DuplicateScanService.BuildItems(incomplete);
            Check("未跑完时产出条目仍在（可见）", items.Count == 2);
            Check("未跑完时不预先勾选", items.All(x => !x.Selected));
            Check("未跑完时说明里标注了", items.All(x => x.Reason.Contains("incomplete", StringComparison.OrdinalIgnoreCase)));
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { }
        }
    }

    static byte[] MakeContent(long size, int seed)
    {
        var buf = new byte[size];
        var rnd = new Random(seed);
        rnd.NextBytes(buf);
        return buf;
    }

    static FileEntry Entry(string path, long size)
        => new() { Name = Path.GetFileName(path), FullPath = path, Size = size, Kind = EntryKind.File };

    // ---------------------------------------------------------------- 协调模块

    static void CoordinatorTests()
    {
        Section("协调模块（模块级测试）");

        // ---- ScanCoordinator：主扫描成功就不降级 ----
        var good = new FakeScan("mft", _ =>
        {
            var root = new FileEntry { Name = "C:\\", FullPath = "C:\\", Kind = EntryKind.Directory };
            return new ScanResult(root, new ScanQuality { Source = ScanSource.Mft, Complete = true, FilesRead = 5 });
        });
        var unusedFallback = new FakeScan("recursive", _ => throw new InvalidOperationException("不该被调用"));
        var coord = new ScanCoordinator(good, unusedFallback);
        var outcome = coord.RunAsync("C:\\", null, CancellationToken.None).GetAwaiter().GetResult();
        Check("主扫描成功时不降级", !outcome.UsedFallback && outcome.FallbackReason.Length == 0);
        Check("质量报告带出来", outcome.Quality?.Source == ScanSource.Mft && outcome.Quality.Complete);
        Check("降级服务没被调用", unusedFallback.Calls == 0);

        // ---- ScanCoordinator：主扫描失败自动降级，并回调通知界面 ----
        var broken = new FakeScan("mft", _ => throw new IOException("Access Denied (5)"));
        var fallback = new FakeScan("recursive", _ =>
        {
            var root = new FileEntry { Name = "C:\\", FullPath = "C:\\", Kind = EntryKind.Directory };
            return new ScanResult(root, new ScanQuality { Source = ScanSource.Recursive, SkippedDirs = 3 });
        });
        var coord2 = new ScanCoordinator(broken, fallback);
        string? notified = null;
        var outcome2 = coord2.RunAsync("C:\\", null, CancellationToken.None, r => notified = r).GetAwaiter().GetResult();
        Check("主扫描失败会降级", outcome2.UsedFallback);
        Check("降级原因带出来", outcome2.FallbackReason.Length > 0);
        Check("降级扫描被调用一次", fallback.Calls == 1);
        Check("降级前通知了界面", !string.IsNullOrEmpty(notified), notified ?? "(null)");
        Check("降级后质量报告来自递归", outcome2.Quality?.Source == ScanSource.Recursive);

        // ---- ScanCoordinator：取消原样抛出，不降级 ----
        var canceledPrimary = new FakeScan("mft", _ => throw new OperationCanceledException());
        var shouldNotRun = new FakeScan("recursive", _ => throw new InvalidOperationException("不该被调用"));
        var coord3 = new ScanCoordinator(canceledPrimary, shouldNotRun);
        bool canceled = false;
        try { coord3.RunAsync("C:\\", null, CancellationToken.None).GetAwaiter().GetResult(); }
        catch (OperationCanceledException) { canceled = true; }
        Check("取消不触发降级", canceled && shouldNotRun.Calls == 0);

        // ---- DeletionCoordinator：计划 → 确认 → 执行 → 汇总 ----
        var probe = new FakeProbe();
        probe.Set(@"C:\Temp\a.tmp", n => n.Size = 100);
        probe.Set(@"C:\Windows\System32\x.dll", n => { });
        var dele = new DeletionCoordinator(new DeletionPreflight(probe), new DeletionExecutor(probe));

        var plan = dele.Plan(new[]
        {
            new DeletionTarget { Path = @"C:\Temp\a.tmp" },
            new DeletionTarget { Path = @"C:\Windows\System32\x.dll" },
            new DeletionTarget { Path = @"C:\Temp\missing.tmp" },
        });
        Check("计划里一项都不丢", plan.Preflight.Items.Count == 3);
        Check("计划只放行可删项", plan.ToExecute.Count == 1);
        Check("计划说明能生成", DeletionCoordinator.PlanNote(plan).Length > 0);

        var batch = dele.Execute(plan, allowSensitive: true);
        Check("执行后成功 1 项", batch.Recycled == 1, $"recycled={batch.Recycled}");
        Check("受保护项被挡住", batch.Protected + batch.Skipped == 1);
        Check("不存在项单独记录", batch.NotFound == 1);
        Check("最近一次结果被保留", dele.LastBatch == batch);

        var lines = DeletionCoordinator.Summarize(batch);
        Check("汇总有内容", lines.Count > 0);
        Check("汇总首行是总数", lines[0].Contains($"total={batch.Total}", StringComparison.Ordinal), lines[0]);
        Check("汇总按原因分组列出", lines.Count >= 3, $"lines={lines.Count}");

        var gone = DeletionCoordinator.RecycledPaths(batch);
        Check("删除成功路径可回传界面", gone.Count == 1 && gone.Contains(@"C:\Temp\a.tmp", StringComparer.OrdinalIgnoreCase));

        // 带错误的计划：确认框要能说清「几项其实删不了」
        var plan2 = dele.Plan(new[] { new DeletionTarget { Path = @"C:\Windows\System32\x.dll" } });
        string note = DeletionCoordinator.PlanNote(plan2);
        Check("确认框说明含受保护计数", note.Contains("blocked=1", StringComparison.Ordinal), note);

        // ---- AiCoordinator：默认必须发脱敏路径 ----
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var items = new List<CleanItem>
        {
            new() { Name = "x.tmp", FullPath = Path.Combine(home, "AppData", "Local", "Temp", "x.tmp"), Size = 10, Risk = CleanRisk.Safe },
        };
        string redactedPrompt = AiCoordinator.BuildPrompt(items, sendFullPaths: false);
        Check("默认 prompt 不含完整路径",
            home.Length == 0 || !redactedPrompt.Contains(home, StringComparison.OrdinalIgnoreCase), redactedPrompt);
        Check("默认 prompt 含脱敏占位符",
            redactedPrompt.Contains(PathRedactor.UserProfileToken, StringComparison.Ordinal));

        string fullPrompt = AiCoordinator.BuildPrompt(items, sendFullPaths: true);
        Check("明确允许后才发完整路径", fullPrompt.Contains(items[0].FullPath, StringComparison.OrdinalIgnoreCase));

        // ---- AiCoordinator：AI 只写说明，不改风险/勾选 ----
        var aiItems = new List<CleanItem>
        {
            new() { Name = "a.tmp", FullPath = @"C:\Temp\a.tmp", Size = 10, Risk = CleanRisk.Safe, CanDelete = true },
        };
        AiClient.Handler = (req, onDelta, _) =>
        {
            // 模型照抄输入路径回一行说明
            string echoed = req.Turns.Count > 0 && req.Turns[0].Text.Contains("a.tmp", StringComparison.Ordinal)
                ? "GOTO C:\\Temp\\a.tmp\t这是临时文件"
                : "";
            onDelta?.Invoke(echoed);
            return Task.FromResult(new AiReply { Text = echoed });
        };
        var aico = new AiCoordinator();
        var explain = aico.ExplainAsync(aiItems, null, null, sendFullPaths: true, null, CancellationToken.None)
            .GetAwaiter().GetResult();
        Check("AI 说明写回条目", explain.Applied == 1 && aiItems[0].AiNote.Length > 0, $"applied={explain.Applied}");
        Check("AI 不改风险档", aiItems[0].Risk == CleanRisk.Safe);
        Check("AI 不改 CanDelete", aiItems[0].CanDelete);
        Check("AI 不勾选", !aiItems[0].Selected);
        AiClient.Handler = null;

        // 空批次不炸
        var empty = aico.ExplainAsync(new List<CleanItem>(), null, null, false, null, CancellationToken.None)
            .GetAwaiter().GetResult();
        Check("空批次安全返回", empty.Sent == 0 && empty.Applied == 0);
    }

    /// <summary>假的扫描服务：能成功、能抛、能计数。</summary>
    sealed class FakeScan : IScanService
    {
        private readonly Func<string, ScanResult> _impl;
        public FakeScan(string name, Func<string, ScanResult> impl) { Name = name; _impl = impl; }
        public string Name { get; }
        public int Calls { get; private set; }
        public ScanQuality? LastQuality { get; private set; }

        public FileEntry Scan(string rootPath, IProgress<ScanProgress>? progress = null, CancellationToken ct = default)
        {
            Calls++;
            ct.ThrowIfCancellationRequested();
            var result = _impl(rootPath);
            LastQuality = result.Quality;
            return result.Root;
        }
    }

    sealed record ScanResult(FileEntry Root, ScanQuality Quality);

    // ---------------------------------------------------------------- 工作状态 / 列表快照

    static void WorkStateTests()
    {
        Section("工作状态（停止按钮 / 取消链路）");

        var w = new WorkState();
        int changes = 0;
        w.Changed += (_, _) => changes++;

        Check("空闲时没有阶段", !w.Busy && !w.CanStop);
        Check("空闲时阶段为 Idle", w.Phase == WorkPhase.Idle);

        // ---- 回归点：扫描阶段结束、分析阶段仍在跑时，停止按钮必须还在 ----
        var scan = w.Begin(StageNames.Scan, WorkPhase.Scanning);
        Check("扫描中可停止", w.Busy && w.CanStop);
        Check("扫描中阶段为 Scanning", w.Phase == WorkPhase.Scanning);

        var analyze = w.Begin(StageNames.Analyze, WorkPhase.Analyzing);
        Check("扫描+分析都在跑", w.Stages.Count == 2);

        scan.Dispose(); // 扫描先结束，分析还在跑
        Check("扫描结束后仍然忙（分析未结束）", w.Busy);
        Check("扫描结束后停止按钮仍然可用（这就是以前丢掉的入口）", w.CanStop);
        Check("阶段切到 Analyzing", w.Phase == WorkPhase.Analyzing);

        // 重复检测接着跑
        var dup = w.Begin(StageNames.Duplicates, WorkPhase.Duplicates);
        Check("重复检测阶段可停止", w.CanStop);
        // 分析还在跑时，显示的应该是上游阶段（分析），而不是后加入的重复检测
        Check("同时有分析和重复检测时显示上游阶段", w.Phase == WorkPhase.Analyzing, w.Phase.ToString());

        dup.Dispose();
        analyze.Dispose();
        Check("全部结束后停止按钮消失", !w.Busy && !w.CanStop);
        Check("全部结束后回到 Idle", w.Phase == WorkPhase.Idle);

        // 单跑重复检测时阶段为 Duplicates
        var w3 = new WorkState();
        using (w3.Begin(StageNames.Duplicates, WorkPhase.Duplicates))
            Check("只有重复检测时阶段为 Duplicates", w3.Phase == WorkPhase.Duplicates);

        // ---- 取消：请求后不能再点，但阶段还在 ⇒ 按钮要留着显示「正在取消」 ----
        var s2 = w.Begin(StageNames.Scan, WorkPhase.Scanning);
        w.RequestCancel();
        Check("取消请求后仍标记为忙（阶段没退干净）", w.Busy);
        Check("取消请求后按钮不再可点", !w.CanStop);
        Check("取消请求被记录", w.CancelRequested && w.ShowPartialResult);
        Check("描述里含 canceling", w.Describe().StartsWith("canceling", StringComparison.Ordinal), w.Describe());
        s2.Dispose();
        Check("阶段退干净后不再忙", !w.Busy);
        Check("取消后仍标记部分结果", w.ShowPartialResult);

        w.ResetCancel();
        Check("新一轮开始会清掉取消标记", !w.CancelRequested && !w.ShowPartialResult);

        // ---- 异常安全：using 里抛异常也要注销阶段 ----
        var w2 = new WorkState();
        try
        {
            using var _ = w2.Begin(StageNames.Analyze, WorkPhase.Analyzing);
            throw new InvalidOperationException("boom");
        }
        catch (InvalidOperationException) { }
        Check("using 里抛异常后阶段被注销", !w2.Busy);

        // 事件有被触发过（界面据此刷新按钮）
        Check("阶段变化会通知界面", changes > 0, $"changes={changes}");

        // ---- 分段计时 ----
        var perf = new PerfTrace("test");
        perf.Measure("a", () => { Thread.Sleep(12); return 0; });
        perf.Mark("b", 5, 3);
        Check("计时记录了 a 段", perf.Get("a") >= 10, perf.Get("a").ToString("0.0"));
        Check("计时记录了 b 段", Math.Abs(perf.Get("b") - 5) < 0.001);
        Check("汇总含全部阶段", perf.Summary().Contains("a=", StringComparison.Ordinal)
            && perf.Summary().Contains("b=", StringComparison.Ordinal));
    }

    static void CleanListSnapshotTests()
    {
        Section("清理列表快照（去重 / 分类 / 单次排序）");

        var items = new List<CleanItem>
        {
            new() { Name = "a", FullPath = @"C:\x\a.tmp", Size = 100, Group = "Temp", Risk = CleanRisk.Safe },
            new() { Name = "b", FullPath = @"C:\x\b.tmp", Size = 300, Group = "Temp", Risk = CleanRisk.Confirm },
            new() { Name = "c", FullPath = @"C:\y\c.log", Size = 200, Group = "Log", Risk = CleanRisk.Safe },
            // 同一路径重复出现：必须去重
            new() { Name = "a-dup", FullPath = @"C:\x\A.TMP", Size = 100, Group = "Temp", Risk = CleanRisk.Safe },
            // 「别删」不进列表
            new() { Name = "keep", FullPath = @"C:\w\k.sys", Size = 999, Group = "System", Risk = CleanRisk.Keep },
            // 空路径不进列表
            new() { Name = "nopath", FullPath = "", Size = 50, Group = "Temp", Risk = CleanRisk.Safe },
        };

        var snap = CleanListSnapshot.Build(items, "All", "Other");

        Check("去重后 3 条", snap.Count == 3, $"count={snap.Count}");
        Check("排除「别删」", snap.All.All(x => x.Risk != CleanRisk.Keep));
        Check("排除空路径", snap.All.All(x => x.FullPath.Length > 0));
        Check("路径大小写不敏感去重", snap.All.Count(x => x.FullPath.Equals(@"C:\x\a.tmp", StringComparison.OrdinalIgnoreCase)) == 1);
        Check("总字节数正确", snap.Bytes == 600, snap.Bytes.ToString());

        // 排序：按 (风险组, 大小降序) —— 视图靠这个顺序，不再自己排序
        Check("先排安全组（风险组 0 在前）",
            snap.All[0].RiskGroupKey == 0 && snap.All[1].RiskGroupKey == 0 && snap.All[2].RiskGroupKey == 1,
            string.Join(",", snap.All.Select(x => x.RiskGroupKey)));
        Check("组内按大小降序",
            snap.All[0].Size == 200 && snap.All[1].Size == 100,
            string.Join(",", snap.All.Select(x => x.Size)));

        // 分类
        Check("两个分类 + 一个「全部」", snap.Categories.Count == 3, $"cats={snap.Categories.Count}");
        Check("第一个是「全部」", snap.Categories[0].Name == "All");
        Check("「全部」的总数等于全部候选", snap.Categories[0].Items.Count == 3);
        Check("分类按占用降序", snap.Categories[1].Name == "Temp" && snap.Categories[2].Name == "Log",
            string.Join(",", snap.Categories.Select(c => c.Name)));
        Check("分类条数正确", snap.Categories[1].Items.Count == 2);
        Check("占比合计约 100",
            Math.Abs(snap.Categories.Skip(1).Sum(c => c.Percent) - 100) < 0.01);

        // 只有一个分类时不加「全部」（老行为）
        var single = CleanListSnapshot.Build(new List<CleanItem>
        {
            new() { FullPath = @"C:\a", Size = 1, Group = "Temp", Risk = CleanRisk.Safe },
        }, "All", "Other");
        Check("单分类不加「全部」", single.Categories.Count == 1 && single.Categories[0].Name == "Temp");

        // 空 Group 落到「其他」
        var noGroup = CleanListSnapshot.Build(new List<CleanItem>
        {
            new() { FullPath = @"C:\a", Size = 1, Group = "", Risk = CleanRisk.Safe },
            new() { FullPath = @"C:\b", Size = 2, Group = "Temp", Risk = CleanRisk.Safe },
        }, "All", "Other");
        Check("空分类名落到其他", noGroup.Categories.Any(c => c.Name == "Other"));

        // 取消要能中断
        using (var cts = new CancellationTokenSource())
        {
            cts.Cancel();
            bool canceled = false;
            try { CleanListSnapshot.Build(items, "All", "Other", cts.Token); }
            catch (OperationCanceledException) { canceled = true; }
            Check("快照构建支持取消", canceled);
        }

        // 目录过滤 + 缓存用的前缀匹配
        var prefix = CleanListSnapshot.FolderPrefix(@"C:\x");
        var filtered = CleanListSnapshot.FilterByFolder(snap.All, prefix);
        Check("按目录过滤只留该目录下的", filtered.Count == 2, $"n={filtered.Count}");
        Check("过滤结果都在该目录下", filtered.All(x => x.FullPath.StartsWith(@"C:\x\", StringComparison.OrdinalIgnoreCase)));
        Check("空前缀不做过滤", CleanListSnapshot.FilterByFolder(snap.All, "").Count == 3);
        Check("盘符根前缀匹配子项", CleanListSnapshot.UnderPrefix(@"C:\x\a.tmp", @"C:\"));
        Check("前缀不会误匹配同名前缀目录",
            !CleanListSnapshot.UnderPrefix(@"C:\xyz\a.tmp", @"C:\x\"));

        // 大样本：30 万条也要能建出来，且总数不丢
        var big = new List<CleanItem>(300_000);
        for (int i = 0; i < 300_000; i++)
        {
            big.Add(new CleanItem
            {
                Name = "f" + i,
                FullPath = @"C:\big\" + i + ".tmp",
                Size = i % 4096,
                Group = i % 3 == 0 ? "Temp" : i % 3 == 1 ? "Log" : "Cache",
                Risk = i % 2 == 0 ? CleanRisk.Safe : CleanRisk.Confirm,
            });
        }
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var bigSnap = CleanListSnapshot.Build(big, "All", "Other");
        sw.Stop();
        Check("30 万条全部保留（不截断）", bigSnap.Count == 300_000, $"count={bigSnap.Count}");
        Check("30 万条能建出分类", bigSnap.Categories.Count >= 3);
        Check("30 万条构建在合理时间内", sw.ElapsedMilliseconds < 5000, $"{sw.ElapsedMilliseconds}ms");
        Console.WriteLine($"       (300k snapshot build: {sw.ElapsedMilliseconds} ms)");
    }

    static void ErrorClassificationTests()
    {
        Section("错误分类");

        Check("取消 → Canceled",
            AppError.Classify(new OperationCanceledException()) == AppErrorKind.Canceled);
        Check("取消不是「可恢复错误」", !AppError.From(new OperationCanceledException()).IsRecoverable);
        Check("权限 → Permission",
            AppError.Classify(new UnauthorizedAccessException()) == AppErrorKind.Permission);
        Check("路径过长 → Path",
            AppError.Classify(new PathTooLongException()) == AppErrorKind.Path);
        Check("文件不存在 → Path",
            AppError.Classify(new FileNotFoundException()) == AppErrorKind.Path);
        Check("网络 → Network",
            AppError.Classify(new System.Net.Http.HttpRequestException("boom")) == AppErrorKind.Network);
        Check("JSON 解析 → Parse",
            AppError.Classify(new System.Text.Json.JsonException("bad")) == AppErrorKind.Parse);
        Check("未知 → Unknown", AppError.Classify(new InvalidOperationException()) == AppErrorKind.Unknown);
        Check("未知异常给用户一句人话",
            AppError.From(new InvalidOperationException("内部错误细节")).UserMessage.Length > 0);

        // 异常里的密钥不能进日志
        var err = AppError.From(new InvalidOperationException("failed with sk-abcdefghijklmnopqrstuvwxyz012345"));
        Check("异常消息里的密钥被抹掉", !LogRedactor.LooksSecret(err.Technical));
        Check("异常技术细节带类型", err.Technical.Contains(nameof(InvalidOperationException)));
    }

    static void DiagnosticsTests()
    {
        Section("诊断包");

        string bundle = AppLog.BuildDiagnostics(2000);
        Check("诊断包非空", bundle.Length > 0);
        Check("诊断包不含密钥", !LogRedactor.LooksSecret(bundle));
        Check("诊断包带版本/环境段", bundle.Contains("diagnostics bundle"));

        // 写日志不能抛
        bool ok = true;
        try
        {
            AppLog.Info("SafetyCheck", "regression run");
            AppLog.Write(new LogEntry(DateTime.UtcNow, LogLevel.Error, "SafetyCheck", "op1", "stage1",
                "message with sk-abcdefghijklmnopqrstuvwxyz012345", 12.3, 7,
                nameof(InvalidOperationException), "用户可见错误"));
        }
        catch { ok = false; }
        Check("写日志不抛异常", ok);
    }

    // ---------------------------------------------------------------- helpers

    static void Section(string name) => Console.WriteLine("== " + name);

    /// <summary>同步回调的 IProgress，避免 Progress&lt;T&gt; 的线程池 post 让取消赶不上。</summary>
    sealed class SyncProgress<T> : IProgress<T>
    {
        private readonly Action<T> _handler;
        public SyncProgress(Action<T> handler) => _handler = handler;
        public void Report(T value) => _handler(value);
    }

    static void Check(string name, bool ok, string? detail = null)
    {
        if (ok) { _pass++; Console.WriteLine("  ok   " + name); }
        else
        {
            _fail++;
            string msg = name + (detail == null ? "" : "  [" + detail + "]");
            Failures.Add(msg);
            Console.WriteLine("  FAIL " + msg);
        }
    }
}
