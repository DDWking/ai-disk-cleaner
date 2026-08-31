using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using AiDiskCleaner.Models;
using AiDiskCleaner.Native;
using Microsoft.Win32.SafeHandles;

namespace AiDiskCleaner.Services;

/// <summary>
/// 路线 A：直接读取 NTFS 的 MFT（主文件表），实现秒级扫盘，与 WizTree 同款技术。
/// 优先直接读 $MFT 文件（走文件系统缓存 + 预读，最快）；失败则回退读卷偏移。
/// 需要管理员权限；仅支持 NTFS。各阶段耗时实时写入 D:\ssssswiztree\scan-timing.log。
/// </summary>
public sealed class MftScanService : IScanService
{
    private const int BlockSize = 16 * 1024 * 1024; // 每次顺序读 16MB
    private const ulong RootRecordNumber = 5;       // NTFS 根目录（$Root）的记录号
    private const string LogPath = @"D:\ssssswiztree\scan-timing.log";

    public FileEntry Scan(string rootPath, IProgress<ScanProgress>? progress = null, CancellationToken ct = default)
    {
        var total = Stopwatch.StartNew();
        var log = new StringBuilder();
        void Flush(string msg)
        {
            log.AppendLine(msg);
            try { File.WriteAllText(LogPath, log.ToString()); } catch { }
        }

        string devicePath = @"\\.\" + rootPath.TrimEnd('\\');            // \\.\C:

        EnableBackupPrivilege();
        using var volHandle = OpenVolume(devicePath);
        var vol = GetVolumeInfo(volHandle);
        int recordSize = vol.BytesPerFileRecordSegment;
        int bytesPerCluster = vol.BytesPerCluster;
        long mftLength = vol.MftValidDataLength;
        long mftOffset = vol.MftStartLcn * (long)bytesPerCluster;
        int recordCount = (int)(mftLength / recordSize);
        Flush($"记录大小 {recordSize}B, 簇 {bytesPerCluster}B, MFT 长度 {mftLength / 1048576}MB, 记录数 {recordCount:N0}");

        // 先读记录 0（$MFT 自身），解析未命名 $DATA 的 data run。$MFT 几乎总是碎片化的，
        // 从起始簇连续读只会扫到第一段，文件数/总大小会对不上。
        // 先读开头 16 条记录：记录 0 是 $MFT 自身，扩展记录（ATTRIBUTE_LIST）常紧跟其后
        int headerBytes = recordSize * 16;
        var header = new byte[headerBytes];
        if (!NtfsNative.SetFilePointerEx(volHandle, mftOffset, out _, 0))
            throw new IOException($"定位 MFT 失败，错误码 {Marshal.GetLastWin32Error()}");
        if (!NtfsNative.ReadFile(volHandle, header, (uint)headerBytes, out uint firstRead, IntPtr.Zero) || firstRead < recordSize)
            throw new IOException("读取 $MFT 记录 0 失败");
        int headerRecords = (int)firstRead / recordSize;
        for (int i = 0; i < headerRecords; i++)
            ApplyUsaFixup(header, i * recordSize, recordSize);

        var runs = ExtractUnnamedDataRuns(header, 0, recordSize);
        var extraRecs = ExtractAttributeListRecordNumbers(header, 0, recordSize, volHandle, bytesPerCluster);
        foreach (var rec in extraRecs)
        {
            if (rec == 0) continue;
            if (rec < (ulong)headerRecords)
                runs.AddRange(ExtractUnnamedDataRuns(header, (int)rec * recordSize, recordSize));
            else
            {
                var extra = ReadNtfsFileRecord(volHandle, rec, recordSize);
                if (extra != null)
                    runs.AddRange(ExtractUnnamedDataRuns(extra, 0, recordSize));
            }
        }
        // 必须按 VCN（逻辑顺序）读，不能按磁盘 LCN 排序，否则记录号会全错。
        runs = MergeRuns(runs);

        // 内核的 retrieval pointers 才是完整碎片图。记录 0 里的 run 经常只有第一段。
        var (kernelRuns, kernelErr) = GetMftRetrievalPointers(volHandle, bytesPerCluster);
        if (kernelRuns.Count > 0)
        {
            long kBytes = 0;
            foreach (var r in kernelRuns) kBytes += r.Clusters * bytesPerCluster;
            Flush($"内核 retrieval pointers: {kernelRuns.Count} 段, 覆盖 {kBytes / 1048576}MB");
            if (kBytes >= mftLength || kernelRuns.Count > runs.Count)
                runs = kernelRuns;
        }
        else
        {
            Flush($"内核 retrieval pointers 失败，错误码 {kernelErr}");
        }

        if (runs.Count == 0)
        {
            long clusters = (mftLength + bytesPerCluster - 1) / bytesPerCluster;
            runs.Add((0, vol.MftStartLcn, clusters));
            Flush("读取方式: 单段回退（记录 0 无 data run）");
        }
        else
        {
            long runBytes = 0;
            foreach (var r in runs) runBytes += r.Clusters * bytesPerCluster;
            if (runBytes > mftLength)
            {
                mftLength = runBytes;
                recordCount = (int)(mftLength / recordSize);
            }
            Flush($"读取方式: $MFT {runs.Count} 段, 覆盖 {runBytes / 1048576}MB（逻辑 {mftLength / 1048576}MB），记录数 {recordCount:N0}");
        }

        var entries = new FileEntry?[Math.Max(recordCount, 1)];
        var parents = new ulong[entries.Length];
        var bases = new ulong[entries.Length];
        Array.Fill(parents, RootRecordNumber);

        byte[] buffer = new byte[BlockSize];
        ulong recordNumber = 0;
        int fileCount = 0;
        var readSw = new Stopwatch();
        var parseSw = new Stopwatch();

        void EnsureRecord(ulong rec)
        {
            if ((int)rec < entries.Length) return;
            int n = Math.Max(entries.Length * 2, (int)rec + 4096);
            Array.Resize(ref entries, n);
            int old = parents.Length;
            Array.Resize(ref parents, n);
            Array.Resize(ref bases, n);
            Array.Fill(parents, RootRecordNumber, old, n - old);
        }

        void ParseBlock(int bytesRead, int expected)
        {
            parseSw.Start();
            int pos = 0;
            while (pos + recordSize <= bytesRead)
            {
                EnsureRecord(recordNumber);
                var entry = ParseRecord(buffer, pos, recordSize, recordNumber, out ulong parentRef, out ulong baseRef);
                if (entry != null)
                {
                    entries[recordNumber] = entry;
                    parents[recordNumber] = parentRef & 0xFFFFFFFFFFFFUL;
                    bases[recordNumber] = baseRef & 0xFFFFFFFFFFFFUL;
                    fileCount++;
                }
                pos += recordSize;
                recordNumber++;
            }
            parseSw.Stop();
            int pct = expected > 0 ? (int)Math.Min(90, recordNumber * 90UL / (ulong)expected) : 1;
            progress?.Report(new ScanProgress(fileCount, "正在读取 MFT", Math.Max(1, pct)));
        }

        progress?.Report(new ScanProgress(0, "正在读取 MFT", 1));

        // 优先：OpenFileById($MFT) 顺序读，内核自己拼碎片，最完整。
        using var mftById = TryOpenMftById(volHandle);
        if (mftById != null)
        {
            Flush("读取方式: OpenFileById($MFT) 顺序读");
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                readSw.Start();
                bool ok = NtfsNative.ReadFile(mftById, buffer, (uint)buffer.Length, out uint bytesRead, IntPtr.Zero);
                readSw.Stop();
                if (!ok || bytesRead == 0) break;
                bytesRead -= bytesRead % (uint)recordSize;
                if (bytesRead == 0) break;
                ParseBlock((int)bytesRead, recordCount);
            }
        }
        else
        {
            foreach (var (_, lcn, clusters) in runs)
            {
                long remaining = clusters * (long)bytesPerCluster;
                long offset = lcn * (long)bytesPerCluster;
                if (!NtfsNative.SetFilePointerEx(volHandle, offset, out _, 0))
                    throw new IOException($"定位 MFT 碎片失败，错误码 {Marshal.GetLastWin32Error()}");

                while (remaining > 0)
                {
                    ct.ThrowIfCancellationRequested();
                    int toRead = (int)Math.Min(buffer.Length, remaining);
                    toRead -= toRead % recordSize;
                    if (toRead < recordSize) break;

                    readSw.Start();
                    bool ok = NtfsNative.ReadFile(volHandle, buffer, (uint)toRead, out uint bytesRead, IntPtr.Zero);
                    readSw.Stop();
                    if (!ok || bytesRead == 0) break;
                    bytesRead -= bytesRead % (uint)recordSize;
                    if (bytesRead == 0) break;

                    ParseBlock((int)bytesRead, recordCount);
                    remaining -= bytesRead;
                }
            }
        }
        recordCount = (int)recordNumber;
        Flush($"读盘 {readSw.ElapsedMilliseconds}ms, 解析 {parseSw.ElapsedMilliseconds}ms, 有效记录 {fileCount:N0}, 已读记录 {recordNumber:N0}/{recordCount:N0}");
        progress?.Report(new ScanProgress(fileCount, "正在建目录树", 92));

        // 扩展记录（Base != 0）上的 $DATA 归到主记录，自身不进目录树
        for (ulong rec = 0; rec < (ulong)recordCount; rec++)
        {
            var entry = entries[rec];
            if (entry == null) continue;
            ulong baseRec = bases[rec];
            if (baseRec == 0 || baseRec == rec) continue;
            if (baseRec < (ulong)recordCount && entries[baseRec] is { } owner && entry.Size > owner.Size)
                owner.Size = entry.Size;
            entries[rec] = null;
        }

        // 3. 建树：根目录 = 记录 5
        var buildSw = Stopwatch.StartNew();
        FileEntry root = entries[RootRecordNumber] ?? new FileEntry { Kind = EntryKind.Directory };
        root.Name = rootPath;
        root.FullPath = rootPath;
        root.Kind = EntryKind.Directory;

        for (ulong rec = 0; rec < (ulong)recordCount; rec++)
        {
            var entry = entries[rec];
            if (entry == null || rec == RootRecordNumber) continue;
            if (rec < 16) continue; // $MFT / $LogFile / $Bitmap 等系统元数据，不进用户目录树
            ulong parentRec = parents[rec];
            if (parentRec == rec) continue;
            if (parentRec == RootRecordNumber)
                root.Children.Add(entry);
            else if (parentRec < (ulong)recordCount && entries[parentRec] is { } parent)
                parent.Children.Add(entry);
            // 父记录已删除：丢掉，避免把孤儿文件堆到 C:\ 根上
        }
        buildSw.Stop();
        Flush($"建树 {buildSw.ElapsedMilliseconds}ms");
        progress?.Report(new ScanProgress(fileCount, "正在累计大小", 96));

        // 4. 单次 DFS：填 FullPath + 自底向上累加目录大小
        var accSw = Stopwatch.StartNew();
        Accumulate(root);
        accSw.Stop();
        Flush($"累加 {accSw.ElapsedMilliseconds}ms, 根目录大小 {root.Size}");
        progress?.Report(new ScanProgress(fileCount, "扫描完成", 100));

        total.Stop();
        Flush($"MFT 扫描总耗时 {total.ElapsedMilliseconds}ms ({total.Elapsed.TotalSeconds:0.00}s), 文件/目录数 {fileCount:N0}");

        return root;
    }

    private static FileEntry? ParseRecord(byte[] b, int offset, int recordSize, ulong recordNumber, out ulong parentRef, out ulong baseRef)
    {
        parentRef = 0;
        baseRef = 0;
        try
        {
            if (offset + 4 > b.Length || offset + recordSize > b.Length) return null;
            if (b[offset] != (byte)'F' || b[offset + 1] != (byte)'I' ||
                b[offset + 2] != (byte)'L' || b[offset + 3] != (byte)'E')
                return null;

            ApplyUsaFixup(b, offset, recordSize);

            ushort flags = ReadUInt16(b, offset + 0x16);
            if ((flags & 0x01) == 0) return null; // 未使用的记录
            baseRef = ReadUInt64(b, offset + 0x20) & 0xFFFFFFFFFFFFUL;

            ushort attrOffset = ReadUInt16(b, offset + 0x14);
            uint usedSize = ReadUInt32(b, offset + 0x18);
            bool isDirectory = (flags & 0x02) != 0;

            string name = "";
            long size = 0;
            DateTime modified = DateTime.MinValue;

            int pos = offset + attrOffset;
            int end = offset + (int)Math.Min(usedSize, (uint)recordSize);

            while (pos + 16 <= end)
            {
                uint attrType = ReadUInt32(b, pos);
                if (attrType == 0xFFFFFFFF) break;
                uint attrLength = ReadUInt32(b, pos + 4);
                if (attrLength < 16 || attrLength > 0x10000) break;
                int next = pos + (int)attrLength;
                if (next > end) break;

                byte nonResident = b[pos + 8];
                byte attrNameLen = b[pos + 9];

                // $FILE_NAME：只要名字、父目录、时间。大小不可靠，不在这里取。
                if (attrType == 0x30 && nonResident == 0)
                {
                    ushort valueOffset = ReadUInt16(b, pos + 0x14);
                    int valuePos = pos + valueOffset;
                    if (valuePos + 0x42 <= end)
                    {
                        byte fnLen = b[valuePos + 0x40];
                        byte nameSpace = b[valuePos + 0x41]; // 2 = DOS 短名，应让位给 Win32
                        int nameBytes = fnLen * 2;
                        if (valuePos + 0x42 + nameBytes <= end)
                        {
                            if (name.Length == 0 || nameSpace != 2)
                            {
                                parentRef = ReadUInt64(b, valuePos);
                                modified = FileTimeToDateTime(ReadInt64(b, valuePos + 16));
                                name = Encoding.Unicode.GetString(b, valuePos + 0x42, nameBytes);
                            }
                        }
                    }
                }

                // 未命名 $DATA 才是真实文件大小
                if (attrType == 0x80 && attrNameLen == 0 && !isDirectory)
                {
                    if (nonResident == 0)
                    {
                        size = ReadUInt32(b, pos + 0x10); // 驻留：值长度
                    }
                    else if (pos + 0x40 <= next)
                    {
                        // 非驻留：$DATA 布局 DataSize@0x30、InitializedSize@0x38、AllocatedSize@0x28
                        long dataSize = ReadInt64(b, pos + 0x30);
                        long inited = ReadInt64(b, pos + 0x38);
                        long alloc = ReadInt64(b, pos + 0x28);
                        size = dataSize;
                        if (size < 0) size = 0;
                        if (inited > 0 && (size == 0 || inited < size)) size = inited;
                        if (size == 0 && alloc > 0) size = alloc;
                    }
                    if (size < 0 || size > 32L * 1024 * 1024 * 1024 * 1024)
                        size = 0;
                }

                pos = next;
            }

            if (name.Length == 0)
                name = isDirectory ? $"<目录 {recordNumber}>" : $"<文件 {recordNumber}>";

            return new FileEntry
            {
                Name = name,
                Size = size,
                Modified = modified,
                Category = isDirectory ? "" : FileClassifier.Classify(name),
                Kind = isDirectory ? EntryKind.Directory : EntryKind.File,
            };
        }
        catch
        {
            parentRef = 0;
            baseRef = 0;
            return null;
        }
    }

    /// <summary>$ATTRIBUTE_LIST 里指向的其它记录号（$MFT 碎片 run 常拆到这些记录）。</summary>
    private static List<ulong> ExtractAttributeListRecordNumbers(byte[] b, int offset, int recordSize, SafeFileHandle vol, int bytesPerCluster)
    {
        var recs = new List<ulong>();
        ushort attrOffset = ReadUInt16(b, offset + 0x14);
        uint usedSize = ReadUInt32(b, offset + 0x18);
        int pos = offset + attrOffset;
        int end = offset + (int)Math.Min(usedSize, (uint)recordSize);
        while (pos + 16 <= end)
        {
            uint attrType = ReadUInt32(b, pos);
            if (attrType == 0xFFFFFFFF) break;
            uint attrLength = ReadUInt32(b, pos + 4);
            if (attrLength < 16 || attrLength > 0x10000) break;
            int next = pos + (int)attrLength;
            if (next > end) break;
            byte nonResident = b[pos + 8];
            if (attrType == 0x20)
            {
                byte[]? list = null;
                if (nonResident == 0)
                {
                    ushort valueOffset = ReadUInt16(b, pos + 0x14);
                    uint valueLen = ReadUInt32(b, pos + 0x10);
                    int vp = pos + valueOffset;
                    int len = (int)Math.Min(valueLen, (uint)Math.Max(0, next - vp));
                    if (len > 0)
                    {
                        list = new byte[len];
                        Buffer.BlockCopy(b, vp, list, 0, len);
                    }
                }
                else if (pos + 0x22 <= next)
                {
                    ushort mappingOff = ReadUInt16(b, pos + 0x20);
                    long dataSize = ReadInt64(b, pos + 0x30);
                    var listRuns = ParseRuns(b, pos + mappingOff, next);
                    list = ReadRuns(vol, listRuns, bytesPerCluster, dataSize);
                }
                if (list != null)
                    ParseAttributeListEntries(list, recs);
            }
            pos = next;
        }
        return recs;
    }

    private static void ParseAttributeListEntries(byte[] list, List<ulong> recs)
    {
        int p = 0;
        while (p + 26 <= list.Length)
        {
            ushort entryLen = ReadUInt16(list, p + 4);
            if (entryLen < 26 || p + entryLen > list.Length) break;
            uint type = ReadUInt32(list, p);
            if (type == 0x80)
            {
                ulong rec = ReadUInt64(list, p + 16) & 0xFFFFFFFFFFFFUL;
                if (rec != 0) recs.Add(rec);
            }
            p += entryLen;
        }
    }

    private static List<(long Vcn, long Lcn, long Clusters)> ParseRuns(byte[] b, int runPos, int end)
    {
        var runs = new List<(long, long, long)>();
        long lcn = 0;
        long vcn = 0;
        while (runPos < end)
        {
            byte header = b[runPos++];
            if (header == 0) break;
            int lenSize = header & 0x0F;
            int offSize = header >> 4;
            if (lenSize == 0 || runPos + lenSize + offSize > end) break;
            long clusters = ReadLeUnsigned(b, runPos, lenSize);
            runPos += lenSize;
            if (offSize == 0)
            {
                vcn += clusters; // 稀疏，跳过
                continue;
            }
            long delta = ReadLeSigned(b, runPos, offSize);
            lcn += delta;
            runPos += offSize;
            if (clusters > 0 && lcn >= 0)
                runs.Add((vcn, lcn, clusters));
            vcn += clusters;
        }
        return runs;
    }

    private static byte[]? ReadRuns(SafeFileHandle vol, List<(long Vcn, long Lcn, long Clusters)> runs, int bytesPerCluster, long dataSize)
    {
        if (runs.Count == 0 || dataSize <= 0 || dataSize > 16 * 1024 * 1024) return null;
        var buf = new byte[dataSize];
        int dest = 0;
        foreach (var (_, lcn, clusters) in runs)
        {
            if (dest >= buf.Length) break;
            int toRead = (int)Math.Min(clusters * (long)bytesPerCluster, buf.Length - dest);
            var chunk = new byte[toRead];
            if (!NtfsNative.SetFilePointerEx(vol, lcn * (long)bytesPerCluster, out _, 0)) return null;
            if (!NtfsNative.ReadFile(vol, chunk, (uint)toRead, out uint n, IntPtr.Zero) || n == 0) return null;
            Buffer.BlockCopy(chunk, 0, buf, dest, (int)n);
            dest += (int)n;
        }
        return dest > 0 ? buf : null;
    }

    private static byte[]? ReadNtfsFileRecord(SafeFileHandle vol, ulong recordNumber, int recordSize)
    {
        var input = new NtfsNative.NtfsFileRecordInput { FileReferenceNumber = (long)recordNumber };
        int inSize = Marshal.SizeOf<NtfsNative.NtfsFileRecordInput>();
        IntPtr inPtr = Marshal.AllocHGlobal(inSize);
        int outSize = recordSize + 64;
        IntPtr outPtr = Marshal.AllocHGlobal(outSize);
        try
        {
            Marshal.StructureToPtr(input, inPtr, false);
            bool ok = NtfsNative.DeviceIoControl(vol, NtfsNative.FSCTL_GET_NTFS_FILE_RECORD,
                inPtr, (uint)inSize, outPtr, (uint)outSize, out _, IntPtr.Zero);
            if (!ok) return null;
            int recLen = Marshal.ReadInt32(outPtr, 8);
            if (recLen <= 0) recLen = recordSize;
            var data = new byte[recordSize];
            int copy = Math.Min(recordSize, recLen);
            Marshal.Copy(IntPtr.Add(outPtr, 12), data, 0, copy);
            if (data[0] != (byte)'F') return null;
            ApplyUsaFixup(data, 0, recordSize);
            return data;
        }
        catch
        {
            return null;
        }
        finally
        {
            Marshal.FreeHGlobal(inPtr);
            Marshal.FreeHGlobal(outPtr);
        }
    }

    private static List<(long Vcn, long Lcn, long Clusters)> MergeRuns(List<(long Vcn, long Lcn, long Clusters)> runs)
    {
        var merged = new List<(long, long, long)>();
        foreach (var r in runs.OrderBy(x => x.Vcn))
        {
            if (r.Clusters <= 0) continue;
            if (merged.Count > 0)
            {
                var last = merged[^1];
                if (r.Vcn < last.Item1 + last.Item3) continue; // 重叠，保留先到的
                if (last.Item1 + last.Item3 == r.Vcn && last.Item2 + last.Item3 == r.Lcn)
                {
                    merged[^1] = (last.Item1, last.Item2, last.Item3 + r.Clusters);
                    continue;
                }
            }
            merged.Add(r);
        }
        return merged;
    }

    /// <summary>从 FILE 记录里取出未命名 $DATA 的 data run（VCN + LCN + 簇数）。稀疏 run 跳过。</summary>
    private static List<(long Vcn, long Lcn, long Clusters)> ExtractUnnamedDataRuns(byte[] b, int offset, int recordSize)
    {
        var runs = new List<(long, long, long)>();
        ushort attrOffset = ReadUInt16(b, offset + 0x14);
        uint usedSize = ReadUInt32(b, offset + 0x18);
        int pos = offset + attrOffset;
        int end = offset + (int)Math.Min(usedSize, (uint)recordSize);
        while (pos + 16 <= end)
        {
            uint attrType = ReadUInt32(b, pos);
            if (attrType == 0xFFFFFFFF) break;
            uint attrLength = ReadUInt32(b, pos + 4);
            if (attrLength < 16 || attrLength > 0x10000) break;
            int next = pos + (int)attrLength;
            if (next > end) break;

            byte nonResident = b[pos + 8];
            byte nameLen = b[pos + 9];
            if (attrType == 0x80 && nonResident != 0 && nameLen == 0 && pos + 0x22 <= next)
            {
                ushort mappingOff = ReadUInt16(b, pos + 0x20);
                long startVcn = pos + 0x18 <= next ? ReadInt64(b, pos + 0x10) : 0;
                var parsed = ParseRuns(b, pos + mappingOff, next);
                if (startVcn > 0)
                {
                    for (int i = 0; i < parsed.Count; i++)
                    {
                        var p = parsed[i];
                        parsed[i] = (p.Vcn + startVcn, p.Lcn, p.Clusters);
                    }
                }
                runs.AddRange(parsed);
                break;
            }
            pos = next;
        }
        return runs;
    }

    private static long ReadLeUnsigned(byte[] b, int off, int n)
    {
        ulong v = 0;
        for (int i = 0; i < n; i++) v |= (ulong)b[off + i] << (8 * i);
        return (long)v;
    }

    private static long ReadLeSigned(byte[] b, int off, int n)
    {
        ulong v = 0;
        for (int i = 0; i < n; i++) v |= (ulong)b[off + i] << (8 * i);
        if (n > 0 && (b[off + n - 1] & 0x80) != 0)
            v |= ~0UL << (8 * n);
        return unchecked((long)v);
    }

    /// <summary>把每个扇区末尾被 USN 覆盖的 2 字节还原，否则大小/名字会读到垃圾。</summary>
    private static void ApplyUsaFixup(byte[] b, int offset, int recordSize)
    {
        ushort usaOffset = ReadUInt16(b, offset + 4);
        ushort usaCount = ReadUInt16(b, offset + 6);
        if (usaCount < 2 || usaOffset + usaCount * 2 > recordSize) return;
        int sectors = usaCount - 1;
        for (int i = 1; i <= sectors; i++)
        {
            int dest = offset + i * 512 - 2;
            int src = offset + usaOffset + i * 2;
            if (dest + 1 >= offset + recordSize || src + 1 >= offset + recordSize) break;
            b[dest] = b[src];
            b[dest + 1] = b[src + 1];
        }
    }

    /// <summary>迭代式填 FullPath + 自底向上累加目录大小（显式栈，彻底避免深目录/损坏链导致的栈溢出）。</summary>
    private static void Accumulate(FileEntry root)
    {
        var visited = new HashSet<FileEntry>();
        var order = new List<FileEntry>();
        var stack = new Stack<FileEntry>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var node = stack.Pop();
            if (!visited.Add(node)) continue; // 环，跳过
            order.Add(node);
            foreach (var c in node.Children)
            {
                c.Parent = node;
                c.FullPath = node.FullPath.TrimEnd('\\') + "\\" + c.Name;
                if (c.IsDirectory && !visited.Contains(c))
                    stack.Push(c);
            }
        }
        for (int i = order.Count - 1; i >= 0; i--)
        {
            var node = order[i];
            long total = 0;
            int files = 0, folders = 0;
            foreach (var c in node.Children)
            {
                total += c.Size;
                if (c.IsDirectory)
                {
                    folders += 1 + c.FolderCount;
                    files += c.FileCount;
                }
                else files += 1;
            }
            node.Size = total;
            node.FileCount = files;
            node.FolderCount = folders;
        }
    }

    private static void EnableBackupPrivilege()
    {
        try
        {
            using var proc = Process.GetCurrentProcess();
            if (!NtfsNative.OpenProcessToken(proc.Handle,
                    NtfsNative.TOKEN_ADJUST_PRIVILEGES | NtfsNative.TOKEN_QUERY, out IntPtr tokenPtr))
                return;
            using (new SafeFileHandle(tokenPtr, true))
            {
                if (!NtfsNative.LookupPrivilegeValue(null, "SeBackupPrivilege", out var luid))
                    return;
                var tp = new NtfsNative.TokenPrivileges
                {
                    PrivilegeCount = 1,
                    Privileges = new NtfsNative.LuidAndAttributes
                    {
                        Luid = luid,
                        Attributes = NtfsNative.SE_PRIVILEGE_ENABLED,
                    },
                };
                NtfsNative.AdjustTokenPrivileges(tokenPtr, false, ref tp, 0, IntPtr.Zero, IntPtr.Zero);
            }
        }
        catch
        {
            // 权限启用失败不影响主流程，仍会回退读卷
        }
    }

    /// <summary>
    /// 用 OpenFileById(记录 0) + FSCTL_GET_RETRIEVAL_POINTERS 拿 $MFT 全部碎片。
    /// 这是内核维护的映射，比自己解析记录 0 的 data run 完整。
    /// </summary>
    private static SafeFileHandle? TryOpenMftById(SafeFileHandle vol)
    {
        var id = new NtfsNative.FileIdDescriptor { dwSize = 24, Type = NtfsNative.FileIdType, FileId = 0 };
        var h = NtfsNative.OpenFileById(vol, ref id,
            NtfsNative.GENERIC_READ,
            NtfsNative.FILE_SHARE_READ | NtfsNative.FILE_SHARE_WRITE | NtfsNative.FILE_SHARE_DELETE,
            IntPtr.Zero,
            NtfsNative.FILE_FLAG_BACKUP_SEMANTICS | NtfsNative.FILE_FLAG_SEQUENTIAL_SCAN);
        return h.IsInvalid ? null : h;
    }

    private static (List<(long Vcn, long Lcn, long Clusters)> Runs, int Error) GetMftRetrievalPointers(SafeFileHandle vol, int bytesPerCluster)
    {
        var runs = new List<(long, long, long)>();
        var id = new NtfsNative.FileIdDescriptor { dwSize = 24, Type = NtfsNative.FileIdType, FileId = 0 };
        var mft = NtfsNative.OpenFileById(vol, ref id,
            NtfsNative.GENERIC_READ,
            NtfsNative.FILE_SHARE_READ | NtfsNative.FILE_SHARE_WRITE | NtfsNative.FILE_SHARE_DELETE,
            IntPtr.Zero,
            NtfsNative.FILE_FLAG_BACKUP_SEMANTICS | NtfsNative.FILE_FLAG_SEQUENTIAL_SCAN);
        if (mft.IsInvalid)
        {
            id = new NtfsNative.FileIdDescriptor { dwSize = 24, Type = NtfsNative.FileIdType, FileId = 0 };
            mft = NtfsNative.OpenFileById(vol, ref id,
                NtfsNative.FILE_READ_ATTRIBUTES,
                NtfsNative.FILE_SHARE_READ | NtfsNative.FILE_SHARE_WRITE | NtfsNative.FILE_SHARE_DELETE,
                IntPtr.Zero,
                NtfsNative.FILE_FLAG_BACKUP_SEMANTICS);
        }
        if (mft.IsInvalid) return (runs, Marshal.GetLastWin32Error());
        using (mft)
        {
            long startingVcn = 0;
            var buf = new byte[64 * 1024];
            while (true)
            {
                bool ok = NtfsNative.DeviceIoControlBytes(mft, NtfsNative.FSCTL_GET_RETRIEVAL_POINTERS,
                    ref startingVcn, 8, buf, (uint)buf.Length, out uint returned, IntPtr.Zero);
                int err = Marshal.GetLastWin32Error();
                // 234 = ERROR_MORE_DATA；38 = ERROR_HANDLE_EOF
                if (!ok && err != 234 && err != 38) break;
                if (returned < 16) break;

                int extentCount = BitConverter.ToInt32(buf, 0);
                long prevVcn = BitConverter.ToInt64(buf, 8); // StartingVcn
                int pos = 16;
                for (int i = 0; i < extentCount && pos + 16 <= buf.Length; i++)
                {
                    long nextVcn = BitConverter.ToInt64(buf, pos);
                    long lcn = BitConverter.ToInt64(buf, pos + 8);
                    pos += 16;
                    long clusters = nextVcn - prevVcn;
                    if (clusters > 0 && lcn >= 0)
                        runs.Add((prevVcn, lcn, clusters));
                    prevVcn = nextVcn;
                }
                if (ok || err == 38 || extentCount == 0) break;
                startingVcn = prevVcn;
            }
        }
        return (MergeRuns(runs), 0);
    }

    private static SafeFileHandle OpenMftFile(string path)
    {
        var handle = NtfsNative.CreateFile(path, NtfsNative.GENERIC_READ,
            NtfsNative.FILE_SHARE_READ | NtfsNative.FILE_SHARE_WRITE | NtfsNative.FILE_SHARE_DELETE,
            IntPtr.Zero, NtfsNative.OPEN_EXISTING,
            NtfsNative.FILE_FLAG_SEQUENTIAL_SCAN | NtfsNative.FILE_FLAG_BACKUP_SEMANTICS, IntPtr.Zero);
        if (handle.IsInvalid)
            throw new IOException($"无法打开 {path}。错误码 {Marshal.GetLastWin32Error()}");
        return handle;
    }

    private static SafeFileHandle OpenVolume(string devicePath)
    {
        var handle = NtfsNative.CreateFile(devicePath, NtfsNative.GENERIC_READ,
            NtfsNative.FILE_SHARE_READ | NtfsNative.FILE_SHARE_WRITE,
            IntPtr.Zero, NtfsNative.OPEN_EXISTING, NtfsNative.FILE_FLAG_SEQUENTIAL_SCAN, IntPtr.Zero);
        if (handle.IsInvalid)
            throw new UnauthorizedAccessException($"无法打开卷 {devicePath}（需管理员权限）。错误码 {Marshal.GetLastWin32Error()}");
        return handle;
    }

    private static NtfsNative.NtfsVolumeData GetVolumeInfo(SafeFileHandle handle)
    {
        int size = Marshal.SizeOf<NtfsNative.NtfsVolumeData>();
        IntPtr ptr = Marshal.AllocHGlobal(size);
        try
        {
            bool ok = NtfsNative.DeviceIoControl(handle, NtfsNative.FSCTL_GET_NTFS_VOLUME_DATA,
                IntPtr.Zero, 0, ptr, (uint)size, out _, IntPtr.Zero);
            if (!ok)
                throw new IOException($"读取 NTFS 卷信息失败（可能非 NTFS）。错误码 {Marshal.GetLastWin32Error()}");
            return Marshal.PtrToStructure<NtfsNative.NtfsVolumeData>(ptr);
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }
    }

    private static DateTime FileTimeToDateTime(long fileTime)
    {
        if (fileTime <= 0) return DateTime.MinValue;
        try { return DateTime.FromFileTimeUtc(fileTime).ToLocalTime(); }
        catch { return DateTime.MinValue; }
    }

    private static ushort ReadUInt16(byte[] b, int off) => (ushort)(b[off] | (b[off + 1] << 8));
    private static uint ReadUInt32(byte[] b, int off) => (uint)(b[off] | (b[off + 1] << 8) | (b[off + 2] << 16) | (b[off + 3] << 24));
    private static ulong ReadUInt64(byte[] b, int off) => BitConverter.ToUInt64(b, off);
    private static long ReadInt64(byte[] b, int off) => BitConverter.ToInt64(b, off);
}
