using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace AiDiskCleaner.Native;

/// <summary>NTFS 相关的 P/Invoke 声明、常量与结构。</summary>
internal static class NtfsNative
{
    internal const uint GENERIC_READ = 0x80000000;
    internal const uint FILE_SHARE_READ = 0x00000001;
    internal const uint FILE_SHARE_WRITE = 0x00000002;
    internal const uint FILE_SHARE_DELETE = 0x00000004;
    internal const uint OPEN_EXISTING = 3;
    internal const uint FILE_FLAG_SEQUENTIAL_SCAN = 0x08000000; // 提示系统这是顺序扫描，启用激进预读
    internal const uint FSCTL_GET_NTFS_VOLUME_DATA = 0x00090064;

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    internal static extern SafeFileHandle CreateFile(
        string lpFileName, uint dwDesiredAccess, uint dwShareMode,
        IntPtr lpSecurityAttributes, uint dwCreationDisposition,
        uint dwFlagsAndAttributes, IntPtr hTemplateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern bool DeviceIoControl(
        SafeFileHandle hDevice, uint dwIoControlCode,
        IntPtr lpInBuffer, uint nInBufferSize,
        IntPtr lpOutBuffer, uint nOutBufferSize,
        out uint lpBytesReturned, IntPtr lpOverlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern bool SetFilePointerEx(
        SafeFileHandle hFile, long liDistanceToMove,
        out long lpNewFilePointer, uint dwMoveMethod);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern bool ReadFile(
        SafeFileHandle hFile, byte[] lpBuffer, uint nNumberOfBytesToRead,
        out uint lpNumberOfBytesRead, IntPtr lpOverlapped);

    /// <summary>FSCTL_GET_NTFS_VOLUME_DATA 返回的 NTFS_VOLUME_DATA_BUFFER。</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct NtfsVolumeData
    {
        public long VolumeSerialNumber;
        public long NumberSectors;
        public long TotalClusters;
        public long FreeClusters;
        public long TotalReserved;
        public int BytesPerSector;
        public int BytesPerCluster;
        public int BytesPerFileRecordSegment;   // MFT 每条记录大小，通常 1024
        public int ClustersPerFileRecordSegment;
        public long MftValidDataLength;         // MFT 有效数据长度（字节）
        public long MftStartLcn;                // MFT 起始簇号
        public long Mft2StartLcn;
        public long MftZoneStart;
        public long MftZoneEnd;
    }
}
