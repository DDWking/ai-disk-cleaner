using System.Text;
using AiDiskCleaner.Models;
using AiDiskCleaner.Services;

namespace MftDosFallbackCheck;

/// <summary>
/// MFT 解析里「纯 DOS 8.3 短名回退」的行为检查（离线，不碰真实卷、不碰用户文件）。
///
/// 被查疑点：MftScanService.ParseRecord 的 DOS 回退判据是**全局** nameLinks 的 Count，
/// 而不是「当前这条记录有没有 Win32 名」。Scan() 里 nameLinks 只 new 一次、整盘所有记录共用
/// （MftScanService.cs:128 创建，:154 传给每一条记录），所以只要前面出现过一条 Win32 名，
/// 之后所有「只有 DOS 短名」的记录都不会再登记进 nameLinks。
///
/// 本检查直接构造合法的 NTFS FILE 记录（含 USA fixup、$FILE_NAME / $DATA 属性）调用真实解析器，
/// 断言解析器的真实行为，而不是照抄一份逻辑来判断对错。
/// </summary>
public static class Program
{
    /// <summary>NTFS 记录大小，与卷上常见值一致（1024B = 2 个 512B 扇区）。</summary>
    const int RecordSize = 1024;

    static int _pass, _fail;
    static readonly List<string> Failures = new();

    public static int Main()
    {
        Console.OutputEncoding = Encoding.UTF8;

        Win32ThenDosOnlyTests();
        DosBeforeWin32Tests();
        SeveralDosOnlyRecordsTests();
        FirstRecordDosOnlyTests();
        DosOnlyDirectoryTests();
        SanityTests();

        Console.WriteLine();
        Console.WriteLine($"PASS {_pass}   FAIL {_fail}");
        foreach (var f in Failures) Console.WriteLine("  - " + f);
        return _fail == 0 ? 0 : 1;
    }

    // ------------------------------------------------------------------ 用例

    /// <summary>核心复现：先来一条 Win32 记录，再来纯 DOS 记录，共用同一条 nameLinks。</summary>
    static void Win32ThenDosOnlyTests()
    {
        Section("1) 先有 Win32 名，随后是纯 DOS 记录（复现点）");

        var links = new List<MftScanService.FileNameLink>();

        // 记录 101：同时有 Win32 名和 DOS 短名 —— 只该登记 Win32 名
        var (r1, p1) = Parse(BuildRecord(false, 1024, ("Report.txt", 1, 5), ("REPORT~1.TXT", 2, 5)), 101, links);
        Check("记录 101 解析出条目", r1 != null);
        Check("记录 101 条目名取 Win32 名", r1!.Name == "Report.txt", r1.Name);
        Check("记录 101 只登记 1 条名字", links.Count == 1, Dump(links));
        Check("记录 101 登记的是 Win32 名，DOS 让位",
            links.Count == 1 && links[0].Name == "Report.txt", Dump(links));

        // 记录 102：只有 DOS 短名 —— 必须照样登记（当前代码在这里丢）
        var (r2, p2) = Parse(BuildRecord(false, 1024, ("SOLOW~1.TXT", 2, 5)), 102, links);
        Check("记录 102 解析出条目", r2 != null);
        Check("记录 102 条目名取 DOS 短名", r2!.Name == "SOLOW~1.TXT", r2.Name);
        Check("记录 102 父引用取 DOS 名的父目录", p2 == 5, p2.ToString());
        Check("纯 DOS 记录也进 nameLinks（bug 点）",
            links.Any(l => l.Record == 102 && l.Name == "SOLOW~1.TXT"), Dump(links));
        Check("两条记录各自只留 1 条名字", links.Count == 2, Dump(links));
    }

    /// <summary>同一条记录里 DOS 属性排在 Win32 属性前面时，仍不能让 DOS 抢位。</summary>
    static void DosBeforeWin32Tests()
    {
        Section("2) 同记录 DOS 属性在前、Win32 属性在后");

        var links = new List<MftScanService.FileNameLink>();
        var (e, _) = Parse(BuildRecord(false, 2048, ("SETUP~1.EXE", 2, 5), ("setup.exe", 1, 5)), 201, links);
        Check("同记录只登记 1 条名字", links.Count == 1, Dump(links));
        Check("同记录登记的是 Win32 名", links.Count == 1 && links[0].Name == "setup.exe", Dump(links));
        Check("同记录条目名是 Win32 名", e!.Name == "setup.exe", e.Name);
    }

    /// <summary>连续多条纯 DOS 记录：每一条都要登记，不能只登记第一条。</summary>
    static void SeveralDosOnlyRecordsTests()
    {
        Section("3) 连续多条纯 DOS 记录");

        var links = new List<MftScanService.FileNameLink>();
        Parse(BuildRecord(false, 1, ("AAAA.txt", 1, 5)), 301, links);
        Parse(BuildRecord(false, 1, ("BBBB~1.TXT", 2, 5)), 302, links);
        Parse(BuildRecord(false, 1, ("CCCC~1.TXT", 2, 5)), 303, links);

        Check("Win32 记录仍以 Win32 名登记",
            links.Count(l => l.Record == 301) == 1 && links.Single(l => l.Record == 301).Name == "AAAA.txt", Dump(links));
        Check("第一条纯 DOS 记录登记", links.Any(l => l.Record == 302 && l.Name == "BBBB~1.TXT"), Dump(links));
        Check("第二条纯 DOS 记录同样登记", links.Any(l => l.Record == 303 && l.Name == "CCCC~1.TXT"), Dump(links));
        Check("一共 3 条名字", links.Count == 3, Dump(links));
    }

    /// <summary>回归保护：列表还空着时的纯 DOS 记录，旧行为本来就是对的，不能被改坏。</summary>
    static void FirstRecordDosOnlyTests()
    {
        Section("4) 列表为空时第一条就是纯 DOS 记录（旧行为保护）");

        var links = new List<MftScanService.FileNameLink>();
        var (e, p) = Parse(BuildRecord(false, 512, ("FIRST~1.TXT", 2, 5)), 401, links);
        Check("空列表时纯 DOS 记录登记", links.Count == 1 && links[0].Name == "FIRST~1.TXT", Dump(links));
        Check("条目名与父引用正确", e!.Name == "FIRST~1.TXT" && p == 5, e.Name + "/" + p);
    }

    /// <summary>目录记录同理：只有 DOS 短名的目录也不能被丢。</summary>
    static void DosOnlyDirectoryTests()
    {
        Section("5) 纯 DOS 短名的目录记录");

        var links = new List<MftScanService.FileNameLink>();
        Parse(BuildRecord(false, 1, ("Z.txt", 1, 5)), 501, links);
        var (d, p) = Parse(BuildRecord(true, 0, ("NEWDIR~1", 2, 5)), 502, links);

        Check("目录条目类型正确", d is { Kind: EntryKind.Directory }, d?.Kind.ToString() ?? "null");
        Check("目录条目名取 DOS 短名", d!.Name == "NEWDIR~1", d.Name);
        Check("纯 DOS 目录也进 nameLinks", links.Any(l => l.Record == 502 && l.Name == "NEWDIR~1"), Dump(links));
        Check("目录父引用正确", p == 5, p.ToString());
    }

    /// <summary>夹具自检：证明构造出来的记录确实被解析器读了（不是空记录走了兜底分支）。</summary>
    static void SanityTests()
    {
        Section("6) 夹具自检 / 记录边界");

        var links = new List<MftScanService.FileNameLink>();
        var (e, _) = Parse(BuildRecord(false, 4096, ("plain.dat", 1, 5)), 601, links);
        Check("Win32 记录登记且大小可读", e is { Size: 4096 }, e?.Size.ToString() ?? "null");
        Check("Win32 记录名字正确", e!.Name == "plain.dat", e.Name);

        // 未使用记录（flags 第 0 位 = 0）必须返回 null，nameLinks 不动
        var unused = BuildRecord(false, 1, ("ghost.txt", 1, 5));
        unused[0x16] = 0x00;
        var before = links.Count;
        var (u, _) = Parse(unused, 602, links);
        Check("未使用记录返回 null", u == null);
        Check("未使用记录不登记名字", links.Count == before, Dump(links));

        // 非 FILE 开头的记录必须返回 null
        var junk = BuildRecord(false, 1, ("junk.txt", 1, 5));
        junk[0] = (byte)'X';
        var (j, _) = Parse(junk, 603, links);
        Check("非 FILE 记录返回 null", j == null);
    }

    // ------------------------------------------------------------------ 解析调用

    static (FileEntry? Entry, ulong Parent) Parse(byte[] record, ulong recordNumber, List<MftScanService.FileNameLink> links)
    {
        var entry = MftScanService.ParseRecord(
            record, 0, RecordSize, recordNumber, out ulong parentRef, out ulong baseRef, links, out bool parseError);
        if (parseError) Check($"记录 {recordNumber} 解析未抛异常", false);
        return (entry, parentRef);
    }

    // ------------------------------------------------------------------ 构造 NTFS FILE 记录

    /// <summary>
    /// 造一条合法的 NTFS FILE 记录：FILE 头 + USA + 若干 $FILE_NAME + 一条驻留 $DATA + 结束标记。
    /// 命名空间 1 = Win32，2 = DOS 8.3，与真实卷一致。
    /// </summary>
    static byte[] BuildRecord(bool isDirectory, uint dataSize, params (string Name, byte Ns, ulong Parent)[] fileNames)
    {
        var b = new byte[RecordSize];
        b[0] = (byte)'F'; b[1] = (byte)'I'; b[2] = (byte)'L'; b[3] = (byte)'E';
        WriteU16(b, 0x04, 0x30);   // update sequence array 偏移
        WriteU16(b, 0x06, 3);      // 1024B / 512B 扇区 + 1
        WriteU16(b, 0x14, 0x38);   // 第一个属性偏移
        WriteU16(b, 0x16, (ushort)(0x01 | (isDirectory ? 0x02 : 0))); // 使用中（+ 目录）
        WriteU32(b, 0x1C, RecordSize);

        int pos = 0x38;
        foreach (var (name, ns, parent) in fileNames) pos = WriteFileNameAttr(b, pos, name, ns, parent);
        if (!isDirectory) pos = WriteResidentDataAttr(b, pos, dataSize);

        WriteU32(b, pos, 0xFFFFFFFF);          // 属性结束标记
        WriteU32(b, 0x18, (uint)(pos + 4));    // usedSize
        return b;
    }

    static int WriteFileNameAttr(byte[] b, int pos, string name, byte ns, ulong parent)
    {
        int nameBytes = name.Length * 2;
        int valueLen = 0x42 + nameBytes;
        int attrLen = Align8(0x18 + valueLen); // 属性长度必须覆盖「值偏移 + 值长度」

        WriteU32(b, pos, 0x30);
        WriteU32(b, pos + 4, (uint)attrLen);
        b[pos + 8] = 0;                    // 驻留
        b[pos + 9] = 0;                    // 属性名长度
        WriteU16(b, pos + 0x0E, 1);        // 属性 id
        WriteU32(b, pos + 0x10, (uint)valueLen);
        WriteU16(b, pos + 0x14, 0x18);     // 值偏移

        int v = pos + 0x18;
        WriteU64(b, v, parent);                        // 父目录记录号
        WriteU64(b, v + 0x10, 132000000000000000);     // 修改时间（合法 FILETIME）
        b[v + 0x40] = (byte)name.Length;
        b[v + 0x41] = ns;
        Encoding.Unicode.GetBytes(name, 0, name.Length, b, v + 0x42);
        return pos + attrLen;
    }

    static int WriteResidentDataAttr(byte[] b, int pos, uint size)
    {
        int attrLen = Align8(0x18 + 4); // 属性长度必须覆盖「值偏移 + 值长度」
        WriteU32(b, pos, 0x80);
        WriteU32(b, pos + 4, (uint)attrLen);
        b[pos + 8] = 0;                    // 驻留
        b[pos + 9] = 0;                    // 未命名 $DATA（真实大小的判据）
        WriteU16(b, pos + 0x0E, 2);
        WriteU32(b, pos + 0x10, size);
        WriteU16(b, pos + 0x14, 0x18);
        return pos + attrLen;
    }

    static int Align8(int n) => (n + 7) & ~7;

    static void WriteU16(byte[] b, int off, int v) { b[off] = (byte)v; b[off + 1] = (byte)(v >> 8); }
    static void WriteU32(byte[] b, int off, uint v)
    {
        b[off] = (byte)v; b[off + 1] = (byte)(v >> 8); b[off + 2] = (byte)(v >> 16); b[off + 3] = (byte)(v >> 24);
    }
    static void WriteU64(byte[] b, int off, ulong v)
    {
        for (int i = 0; i < 8; i++) b[off + i] = (byte)(v >> (8 * i));
    }

    // ------------------------------------------------------------------ 断言

    static void Check(string name, bool ok, string detail = "")
    {
        if (ok) { _pass++; Console.WriteLine("  ok   " + name); return; }
        _fail++;
        string line = name + (detail.Length > 0 ? " [" + detail + "]" : "");
        Failures.Add(line);
        Console.WriteLine("  FAIL " + line);
    }

    static void Section(string s) { Console.WriteLine("== " + s); }

    static string Dump(List<MftScanService.FileNameLink> links)
        => links.Count == 0
            ? "(nameLinks 为空)"
            : string.Join(", ", links.Select(l => $"{l.Record}:{l.Name}"));
}
