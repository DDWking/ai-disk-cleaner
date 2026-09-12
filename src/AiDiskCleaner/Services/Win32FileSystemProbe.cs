using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using AiDiskCleaner.Models;
using AiDiskCleaner.Native;
using Microsoft.Win32.SafeHandles;

namespace AiDiskCleaner.Services;

/// <summary>
/// 走 Win32 的真实实现。所有原生调用都吞掉异常并降级成「读不到」，
/// 因为预检失败必须变成一条结果记录，而不是把整批删除炸掉。
/// </summary>
public sealed class Win32FileSystemProbe : IFileSystemProbe
{
    public static readonly Win32FileSystemProbe Instance = new();

    public FileProbeInfo Probe(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return FileProbeInfo.Missing("empty path");

        FileAttributes attrs;
        try
        {
            attrs = File.GetAttributes(path);
        }
        catch (FileNotFoundException) { return FileProbeInfo.Missing("not found"); }
        catch (DirectoryNotFoundException) { return FileProbeInfo.Missing("directory not found"); }
        catch (UnauthorizedAccessException ex) { return new FileProbeInfo { Exists = true, AccessDenied = true, Error = ex.Message }; }
        catch (Exception ex) { return FileProbeInfo.Missing(ex.Message); }

        bool isDir = (attrs & FileAttributes.Directory) != 0;
        bool reparse = (attrs & FileAttributes.ReparsePoint) != 0;
        bool system = (attrs & FileAttributes.System) != 0;
        bool hidden = (attrs & FileAttributes.Hidden) != 0;

        long size = 0;
        DateTime modified = DateTime.MinValue;
        try
        {
            if (isDir)
            {
                modified = Directory.GetLastWriteTimeUtc(path);
            }
            else
            {
                var fi = new FileInfo(path);
                size = fi.Length;
                modified = fi.LastWriteTimeUtc;
            }
        }
        catch (Exception ex)
        {
            return new FileProbeInfo
            {
                Exists = true, IsDirectory = isDir, IsReparsePoint = reparse,
                IsSystem = system, IsHidden = hidden, Error = ex.Message,
            };
        }

        long allocated = isDir ? -1 : TryGetAllocatedSize(path);

        // 独占打开：拿到身份 + 判断是否被占用。目录要 BACKUP_SEMANTICS 才能打开。
        var (identity, inUse, denied, err) = ProbeHandle(path, isDir);

        return new FileProbeInfo
        {
            Exists = true,
            IsDirectory = isDir,
            IsReparsePoint = reparse,
            IsSystem = system,
            IsHidden = hidden,
            Size = size,
            AllocatedSize = allocated,
            Modified = modified,
            Identity = identity,
            InUse = inUse,
            AccessDenied = denied,
            Error = err,
        };
    }

    /// <summary>
    /// 先用 share=0 打开：能开说明没人占用，顺便拿 file id；
    /// 打不开且报共享冲突说明正在被占用，再用宽松共享开一次拿身份。
    /// </summary>
    private static (FileIdentity Identity, bool InUse, bool Denied, string? Error) ProbeHandle(string path, bool isDirectory)
    {
        uint flags = FileProbeNative.FILE_FLAG_BACKUP_SEMANTICS
                     | FileProbeNative.FILE_FLAG_OPEN_REPARSE_POINT;

        using (var exclusive = FileProbeNative.CreateFile(
                   path, FileProbeNative.FILE_READ_ATTRIBUTES, 0, IntPtr.Zero,
                   FileProbeNative.OPEN_EXISTING, flags, IntPtr.Zero))
        {
            if (!exclusive.IsInvalid)
            {
                var id = ReadIdentity(exclusive);
                return (id, false, false, null);
            }

            int err = Marshal.GetLastWin32Error();
            bool denied = err == FileProbeNative.ERROR_ACCESS_DENIED || err == FileProbeNative.ERROR_PRIVILEGE_NOT_HELD;
            bool sharing = err == FileProbeNative.ERROR_SHARING_VIOLATION || err == FileProbeNative.ERROR_LOCK_VIOLATION;

            // 权限不足时信息也拿不到，直接如实返回
            if (denied && !isDirectory)
                return (default, false, true, new Win32Exception(err).Message);

            if (sharing)
            {
                using var shared = FileProbeNative.CreateFile(
                    path, FileProbeNative.FILE_READ_ATTRIBUTES,
                    FileProbeNative.FILE_SHARE_READ | FileProbeNative.FILE_SHARE_WRITE | FileProbeNative.FILE_SHARE_DELETE,
                    IntPtr.Zero, FileProbeNative.OPEN_EXISTING, flags, IntPtr.Zero);
                var id = shared.IsInvalid ? default : ReadIdentity(shared);
                return (id, true, false, null);
            }

            if (denied)
                return (default, false, true, new Win32Exception(err).Message);

            return (default, false, false, new Win32Exception(err).Message);
        }
    }

    private static FileIdentity ReadIdentity(SafeFileHandle handle)
    {
        if (!FileProbeNative.GetFileInformationByHandle(handle, out var info)) return default;
        ulong low = info.nFileIndexLow;
        ulong high = info.nFileIndexHigh;
        return new FileIdentity(info.dwVolumeSerialNumber, low, high);
    }

    private static long TryGetAllocatedSize(string path)
    {
        try
        {
            uint high = 0;
            uint low = FileProbeNative.GetCompressedFileSize(path, out high);
            if (low == FileProbeNative.INVALID_FILE_SIZE && Marshal.GetLastWin32Error() != 0) return -1;
            return ((long)high << 32) | low;
        }
        catch
        {
            return -1;
        }
    }

    /// <summary>
    /// 只要身份。共享模式全开、不做独占探测 —— 硬链接合并要问成千上万个路径，
    /// 走完整 <see cref="Probe"/> 会白付一遍占用检测和 GetCompressedFileSize 的钱。
    /// </summary>
    public FileIdentity? TryGetIdentity(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        try
        {
            using var handle = FileProbeNative.CreateFile(
                path, FileProbeNative.FILE_READ_ATTRIBUTES,
                FileProbeNative.FILE_SHARE_READ | FileProbeNative.FILE_SHARE_WRITE | FileProbeNative.FILE_SHARE_DELETE,
                IntPtr.Zero, FileProbeNative.OPEN_EXISTING,
                FileProbeNative.FILE_FLAG_BACKUP_SEMANTICS | FileProbeNative.FILE_FLAG_OPEN_REPARSE_POINT,
                IntPtr.Zero);
            if (handle.IsInvalid) return null;
            var id = ReadIdentity(handle);
            return id.IsKnown ? id : null;
        }
        catch
        {
            return null;
        }
    }

    public void SendToRecycle(IReadOnlyList<string> paths)
        => RecycleService.SendMany(paths);
}
