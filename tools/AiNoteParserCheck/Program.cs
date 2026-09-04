using AiDiskCleaner.Models;
using AiDiskCleaner.Services;

// AiNoteParser 的行为检查。跑法：
//   dotnet run --project tools/AiNoteParserCheck
// 退出码非 0 表示有断言失败。
//
// 核心契约只有一条：AI 只能往 AiNote 里写字，
// Risk / Selected / Reason 一律不许动 —— 风险归规则，AI 归说明。

int failed = 0;

void Check(string name, bool ok, string detail = "")
{
    Console.WriteLine((ok ? "  PASS  " : "  FAIL  ") + name + (ok || detail.Length == 0 ? "" : "  -> " + detail));
    if (!ok) failed++;
}

List<CleanItem> Batch() => new()
{
    new CleanItem { Name = "npm-cache", FullPath = @"C:\Users\me\AppData\Local\npm-cache", Size = 2_100_000_000, Risk = CleanRisk.Safe, Reason = "包管理器缓存" },
    new CleanItem { Name = "game", FullPath = @"D:\Steam\steamapps\common\game", Size = 40_000_000_000, Risk = CleanRisk.Keep, Reason = "大文件" },
    new CleanItem { Name = "dump", FullPath = @"C:\Windows\Minidump\090123-1.dmp", Risk = CleanRisk.Safe, Selected = true },
};

string NoteOf(List<CleanItem> b, string path) => b.First(x => x.FullPath == path).AiNote;

// 1. 标准两列回复
{
    var b = Batch();
    int n = AiNoteParser.Apply(b, "GOTO C:\\Users\\me\\AppData\\Local\\npm-cache\tnpm 的下载缓存，删掉后会重新拉取");
    Check("标准回复写入 AiNote", n == 1 && NoteOf(b, @"C:\Users\me\AppData\Local\npm-cache").StartsWith("npm 的下载缓存"), $"n={n}");
}

// 2. 模型把风险词塞进第二列
{
    var b = Batch();
    AiNoteParser.Apply(b, "GOTO C:\\Users\\me\\AppData\\Local\\npm-cache\tsafe\t包管理器缓存");
    Check("跳过风险词 safe", NoteOf(b, @"C:\Users\me\AppData\Local\npm-cache") == "包管理器缓存", NoteOf(b, @"C:\Users\me\AppData\Local\npm-cache"));
}

// 3. 模型照抄输入里的文件大小
{
    var b = Batch();
    AiNoteParser.Apply(b, "GOTO D:\\Steam\\steamapps\\common\\game\t40.0 GB\tSteam 游戏本体目录");
    Check("跳过大小列 40.0 GB", NoteOf(b, @"D:\Steam\steamapps\common\game") == "Steam 游戏本体目录", NoteOf(b, @"D:\Steam\steamapps\common\game"));
}

// 4. 风险词 + 大小同时出现
{
    var b = Batch();
    AiNoteParser.Apply(b, "GOTO D:\\Steam\\steamapps\\common\\game\tkeep\t40G\t游戏安装目录，含存档");
    Check("同时跳过 keep 和大小", NoteOf(b, @"D:\Steam\steamapps\common\game") == "游戏安装目录，含存档", NoteOf(b, @"D:\Steam\steamapps\common\game"));
}

// 5. 只有风险词、没有说明 -> 这条丢弃
{
    var b = Batch();
    int n = AiNoteParser.Apply(b, "GOTO D:\\Steam\\steamapps\\common\\game\tsafe");
    Check("无说明则丢弃", n == 0 && NoteOf(b, @"D:\Steam\steamapps\common\game") == "", $"n={n} note=[{NoteOf(b, @"D:\Steam\steamapps\common\game")}]");
}

// 6. 编造路径一律不认
{
    var b = Batch();
    int n = AiNoteParser.Apply(b, "GOTO C:\\不存在的目录\thaha");
    Check("清单外路径被忽略", n == 0);
}

// 7. markdown 噪音（列表符号 + 反引号）
{
    var b = Batch();
    AiNoteParser.Apply(b, "- `C:\\Windows\\Minidump\\090123-1.dmp`\t系统崩溃转储文件");
    Check("容忍 - 和反引号", NoteOf(b, @"C:\Windows\Minidump\090123-1.dmp") == "系统崩溃转储文件", NoteOf(b, @"C:\Windows\Minidump\090123-1.dmp"));
}

// 8. CRLF + 前后空行 + 开场白
{
    var b = Batch();
    string text = "好的，下面是说明：\r\n\r\nGOTO C:\\Users\\me\\AppData\\Local\\npm-cache\t缓存\r\n\r\n";
    int n = AiNoteParser.Apply(b, text);
    Check("CRLF 与开场白不干扰", n == 1 && NoteOf(b, @"C:\Users\me\AppData\Local\npm-cache") == "缓存");
}

// 9. 路径大小写不敏感
{
    var b = Batch();
    AiNoteParser.Apply(b, "GOTO c:\\users\\ME\\appdata\\local\\NPM-CACHE\t大小写测试");
    Check("路径匹配不区分大小写", NoteOf(b, @"C:\Users\me\AppData\Local\npm-cache") == "大小写测试");
}

// 10. 超长说明被截断
{
    var b = Batch();
    AiNoteParser.Apply(b, "GOTO C:\\Users\\me\\AppData\\Local\\npm-cache\t" + new string('字', 400));
    string note = NoteOf(b, @"C:\Users\me\AppData\Local\npm-cache");
    Check("超长截断到 160 且以省略号结尾", note.Length == AiNoteParser.MaxNoteLength && note.EndsWith("…"), $"len={note.Length}");
}

// 11. 最关键：Risk / Selected / Reason 一个都不许变
{
    var b = Batch();
    var before = b.Select(x => (x.Risk, x.Selected, x.Reason)).ToList();
    AiNoteParser.Apply(b, string.Join("\n",
        "GOTO C:\\Users\\me\\AppData\\Local\\npm-cache\tsafe\t缓存",
        "GOTO D:\\Steam\\steamapps\\common\\game\tkeep\t游戏",
        "GOTO C:\\Windows\\Minidump\\090123-1.dmp\t可安全删除\t转储"));
    var after = b.Select(x => (x.Risk, x.Selected, x.Reason)).ToList();
    Check("AI 不改风险/勾选/原因", before.SequenceEqual(after),
        string.Join(" | ", after.Select((t, i) => $"{t.Risk}/{t.Selected}/{t.Reason}")));
}

// 12. 空回复 / 只有 GOTO / 省掉 GOTO
{
    var b = Batch();
    Check("空回复返回 0", AiNoteParser.Apply(b, "") == 0);
    Check("光秃 GOTO 返回 0", AiNoteParser.Apply(b, "GOTO C:\\Users\\me\\AppData\\Local\\npm-cache") == 0);
    Check("没有 GOTO 也不是路径的行返回 0", AiNoteParser.Apply(b, "SUMMARY\t整体很干净") == 0);
    // 省掉 GOTO 也认：路径必须命中清单，所以模型编不出东西
    Check("省掉 GOTO 的裸路径行也能写入",
        AiNoteParser.Apply(b, "C:\\Users\\me\\AppData\\Local\\npm-cache\t裸路径格式") == 1
        && NoteOf(b, @"C:\Users\me\AppData\Local\npm-cache") == "裸路径格式");
}

// 13. 大小判定不误伤正常说明（说明里带 G 也要当说明）
{
    Check("纯大小识别为噪音", AiNoteParser.LooksLikeNoise("2.1G") && AiNoteParser.LooksLikeNoise("1,024 MB"));
    Check("带内容的句子不被当成大小", !AiNoteParser.LooksLikeNoise("G 盘根目录的游戏"));
    Check("中文风险词识别为噪音", AiNoteParser.LooksLikeNoise("可安全删除") && AiNoteParser.LooksLikeNoise("别删"));
}

Console.WriteLine(failed == 0 ? "\n全部通过" : $"\n{failed} 项失败");
return failed == 0 ? 0 : 1;
