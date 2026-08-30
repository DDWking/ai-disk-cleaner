using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using AiDiskCleaner.Models;
using AiDiskCleaner.Native;
using Microsoft.Win32.SafeHandles;

namespace AiDiskCleaner.Services;

/// <summary>
/// 路线 A：直接读取 NTFS 的 MFT（主文件表），实现秒级扫盘，与 WizTree 同款技术。
/// 需要管理员权限；仅支持 NTFS。
/// </summary>
public sealed class MftScanService : IScanService
{
    private const int BlockSize = 16 * 1024 * 1024; // 每次顺序读 16MB
    private const ulong RootRecordNumber = 5;       // NTFS 根目录（$Root）的记录号

    public FileEntry Scan(string rootPath, IProgress<ScanProgress>? progress = null, CancellationToken ct = default)
    {
        string devicePath = @"\\.\" + rootPath.TrimEnd('\\'); // 例如 \\.\C:

        using SafeFileHandle handle = OpenVolume(devicePath);
        var vol = GetVolumeInfo(handle);

        long mftOffset = vol.MftStartLcn * vol.BytesPerCluster; // MFT 在卷上的字节偏移
        long mftLength = vol.MftValidDataLength;
        int recordSize = vol.BytesPerFileRecordSegment;

        // 记录号 → 条目；记录号 → 父目录记录号（MFT 顺序不保证父在前，先收集后建树）
        var entries = new Dictionary<ulong, FileEntry>();
        var parents = new Dictionary<ulong, ulong>();

        if (!NtfsNative.SetFilePointerEx(handle, mftOffset, out _, 0))
            throw new IOException($"定位 MFT 失败，错误码 {Marshal.GetLastWin32Error()}");

        byte[] buffer = new byte[BlockSize];
        ulong recordNumber = 0;
        long remaining = mftLength;

        while (remaining > 0)
        {
            ct.ThrowIfCancellationRequested();
            int toRead = (int)Math.Min(buffer.Length, remaining);
            if (!NtfsNative.ReadFile(handle, buffer, (uint)toRead, out uint bytesRead, IntPtr.Zero) || bytesRead == 0)
                break;

            int pos = 0;
            while (pos + recordSize <= bytesRead)
            {
                var entry = ParseRecord(buffer, pos, recordNumber, out ulong parentRef);
                if (entry != null)
                {
                    entries[recordNumber] = entry;
                    parents[recordNumber] = parentRef & 0xFFFFFFFFFFFFUL; // 取文件引用的低 48 位记录号
                }
                pos += recordSize;
                recordNumber++;
            }
            remaining -= bytesRead;

            if ((recordNumber & 0x1FFF) == 0)
                progress?.Report(new ScanProgress(entries.Count, "MFT 扫描中"));
        }

        // 建树：根目录 = 记录 5
        FileEntry root = entries.TryGetValue(RootRecordNumber, out var rootRec)
            ? rootRec
            : new FileEntry { Kind = EntryKind.Directory };
        root.Name = rootPath;
        root.FullPath = rootPath;
        root.Kind = EntryKind.Directory;

        foreach (var (rec, entry) in entries)
        {
            if (rec == RootRecordNumber) continue;
            ulong parentRec = parents.TryGetValue(rec, out var p) ? p : RootRecordNumber;
            if (parentRec != RootRecordNumber && entries.TryGetValue(parentRec, out var parent))
                parent.Children.Add(entry);
            else
                root.Children.Add(entry); // 父目录找不到（已删除/解析失败），挂到根
        }

        // 单次 DFS：填 FullPath + 自底向上累加目录大小（合并原来的两次遍历）
        Accumulate(root);
        return root;
    }

    private static FileEntry? ParseRecord(byte[] b, int offset, ulong recordNumber, out ulong parentRef)
    {
        parentRef = 0;
        if (offset + 4 > b.Length) return null;
        // 记录头魔数 "FILE"
        if (b[offset] != (byte)'F' || b[offset + 1] != (byte)'I' ||
            b[offset + 2] != (byte)'L' || b[offset + 3] != (byte)'E')
            return null;

        ushort flags = ReadUInt16(b, offset + 0x16);
        if ((flags & 0x01) == 0) return null; // 未使用的记录

        ushort attrOffset = ReadUInt16(b, offset + 0x14);
        uint usedSize = ReadUInt32(b, offset + 0x18);
        bool isDirectory = (flags & 0x02) != 0;

        string name = "";
        long size = 0;
        DateTime modified = DateTime.MinValue;

        int pos = offset + attrOffset;
        int end = offset + (int)Math.Min(usedSize, (uint)(b.Length - offset));

        // 遍历属性链
        while (pos + 16 <= end)
        {
            uint attrType = ReadUInt32(b, pos);
            if (attrType == 0xFFFFFFFF) break;
            uint attrLength = ReadUInt32(b, pos + 4);
            if (attrLength < 16) break;

            byte nonResident = b[pos + 8];

            // $FILE_NAME (0x30) 驻留属性：文件名、父目录引用、大小、时间戳
            if (attrType == 0x30 && nonResident == 0)
            {
                ushort valueOffset = ReadUInt16(b, pos + 0x14);
                int valuePos = pos + valueOffset;
                if (valuePos + 0x42 <= b.Length)
                {
                    parentRef = ReadUInt64(b, valuePos);
                    modified = FileTimeToDateTime(ReadInt64(b, valuePos + 16));
                    size = ReadInt64(b, valuePos + 0x30);
                    byte fnLen = b[valuePos + 0x40];
                    int nameBytes = fnLen * 2;
                    if (valuePos + 0x42 + nameBytes <= b.Length)
                        name = Encoding.Unicode.GetString(b, valuePos + 0x42, nameBytes);
                }
            }

            pos += (int)attrLength;
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

    /// <summary>一次 DFS：给每个节点填 FullPath，并自底向上累加目录大小。</summary>
    private static long Accumulate(FileEntry node)
    {
        long total = 0;
        foreach (var c in node.Children)
        {
            c.FullPath = node.FullPath + "\\" + c.Name;
            total += c.IsDirectory ? Accumulate(c) : c.Size;
        }
        node.Size = total;
        return total;
    }

    private static SafeFileHandle OpenVolume(string devicePath)
    {
        var handle = NtfsNative.CreateFile(devicePath, NtfsNative.GENERIC_READ,
            NtfsNative.FILE_SHARE_READ | NtfsNative.FILE_SHARE_WRITE,
            IntPtr.Zero, NtfsNative.OPEN_EXISTING, 0, IntPtr.Zero);
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
        => fileTime <= 0 ? DateTime.MinValue : DateTime.FromFileTimeUtc(fileTime).ToLocalTime();

    private static ushort ReadUInt16(byte[] b, int off) => (ushort)(b[off] | (b[off + 1] << 8));
    private static uint ReadUInt32(byte[] b, int off) => (uint)(b[off] | (b[off + 1] << 8) | (b[off + 2] << 16) | (b[off + 3] << 24));
    private static ulong ReadUInt64(byte[] b, int off) => BitConverter.ToUInt64(b, off);
    private static long ReadInt64(byte[] b, int off) => BitConverter.ToInt64(b, off);
}
