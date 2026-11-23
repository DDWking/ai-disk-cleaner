using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace AiDiskCleaner.Native;

internal static class ShortcutNative
{
    public static string? ResolveTarget(string lnkPath)
    {
        if (string.IsNullOrWhiteSpace(lnkPath)) return null;
        IShellLinkW? link = null;
        IPersistFile? persist = null;
        try
        {
            link = (IShellLinkW)new ShellLink();
            persist = (IPersistFile)link;
            persist.Load(lnkPath, 0);
            var sb = new StringBuilder(260);
            link.GetPath(sb, sb.Capacity, IntPtr.Zero, 0);
            string target = sb.ToString();
            return string.IsNullOrWhiteSpace(target) ? null : target;
        }
        catch
        {
            return null;
        }
        finally
        {
            if (persist != null) Marshal.ReleaseComObject(persist);
            if (link != null) Marshal.ReleaseComObject(link);
        }
    }

    public static bool TryRewritePrefix(
        string lnkPath, string oldRoot, string newRoot, out Models.ShortcutUpdateRecord? before)
    {
        before = null;
        if (string.IsNullOrWhiteSpace(lnkPath) || string.IsNullOrWhiteSpace(oldRoot) || string.IsNullOrWhiteSpace(newRoot))
            return false;

        string oldPrefix = oldRoot.TrimEnd('\\') + "\\";
        string newPrefix = newRoot.TrimEnd('\\') + "\\";
        IShellLinkW? link = null;
        IPersistFile? persist = null;
        try
        {
            link = (IShellLinkW)new ShellLink();
            persist = (IPersistFile)link;
            persist.Load(lnkPath, 1 /* STGM_READWRITE */);

            var sbTarget = new StringBuilder(520);
            link.GetPath(sbTarget, sbTarget.Capacity, IntPtr.Zero, 0);
            string target = sbTarget.ToString();

            var sbWork = new StringBuilder(520);
            link.GetWorkingDirectory(sbWork, sbWork.Capacity);
            string work = sbWork.ToString();

            var sbIcon = new StringBuilder(520);
            link.GetIconLocation(sbIcon, sbIcon.Capacity, out int iconIndex);
            string icon = sbIcon.ToString();

            bool changed = false;
            string? nextTarget = ReplacePrefix(target, oldRoot, oldPrefix, newRoot, newPrefix, out bool targetChanged);
            string? nextWork = ReplacePrefix(work, oldRoot, oldPrefix, newRoot, newPrefix, out bool workChanged);
            string? nextIcon = ReplacePrefix(icon, oldRoot, oldPrefix, newRoot, newPrefix, out bool iconChanged);
            changed = targetChanged || workChanged || iconChanged;
            if (!changed) return false;

            before = new Models.ShortcutUpdateRecord
            {
                LnkPath = lnkPath,
                OldTargetPath = target,
                OldWorkingDirectory = work,
                OldIconPath = icon,
                OldIconIndex = iconIndex,
            };

            if (targetChanged && nextTarget != null) link.SetPath(nextTarget);
            if (workChanged && nextWork != null) link.SetWorkingDirectory(nextWork);
            if (iconChanged && nextIcon != null) link.SetIconLocation(nextIcon, iconIndex);
            persist.Save(lnkPath, true);
            return true;
        }
        catch
        {
            before = null;
            return false;
        }
        finally
        {
            if (persist != null) Marshal.ReleaseComObject(persist);
            if (link != null) Marshal.ReleaseComObject(link);
        }
    }

    public static bool TryRestore(Models.ShortcutUpdateRecord record)
    {
        if (record == null || string.IsNullOrWhiteSpace(record.LnkPath) || !System.IO.File.Exists(record.LnkPath))
            return false;

        IShellLinkW? link = null;
        IPersistFile? persist = null;
        try
        {
            link = (IShellLinkW)new ShellLink();
            persist = (IPersistFile)link;
            persist.Load(record.LnkPath, 1);
            if (!string.IsNullOrEmpty(record.OldTargetPath)) link.SetPath(record.OldTargetPath);
            if (!string.IsNullOrEmpty(record.OldWorkingDirectory)) link.SetWorkingDirectory(record.OldWorkingDirectory);
            if (!string.IsNullOrEmpty(record.OldIconPath)) link.SetIconLocation(record.OldIconPath, record.OldIconIndex);
            persist.Save(record.LnkPath, true);
            return true;
        }
        catch
        {
            return false;
        }
        finally
        {
            if (persist != null) Marshal.ReleaseComObject(persist);
            if (link != null) Marshal.ReleaseComObject(link);
        }
    }

    private static string? ReplacePrefix(
        string value, string oldRoot, string oldPrefix, string newRoot, string newPrefix, out bool changed)
    {
        changed = false;
        if (string.IsNullOrWhiteSpace(value)) return null;
        string clean = value.TrimEnd('\\');
        if (string.Equals(clean, oldRoot.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
        {
            changed = true;
            return newRoot.TrimEnd('\\');
        }
        if (value.StartsWith(oldPrefix, StringComparison.OrdinalIgnoreCase))
        {
            changed = true;
            return newPrefix + value[oldPrefix.Length..];
        }
        return null;
    }

    [ComImport]
    [Guid("00021401-0000-0000-C000-000000000046")]
    private class ShellLink { }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("000214F9-0000-0000-C000-000000000046")]
    private interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile, int cch, IntPtr pfd, int fFlags);
        void GetIDList(out IntPtr ppidl);
        void SetIDList(IntPtr pidl);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszName, int cch);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszDir, int cch);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszArgs, int cch);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
        void GetHotkey(out short pwHotkey);
        void SetHotkey(short wHotkey);
        void GetShowCmd(out int piShowCmd);
        void SetShowCmd(int iShowCmd);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszIconPath, int cch, out int piIcon);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, int dwReserved);
        void Resolve(IntPtr hwnd, int fFlags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
    }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("0000010b-0000-0000-C000-000000000046")]
    private interface IPersistFile
    {
        void GetClassID(out Guid pClassID);
        [PreserveSig] int IsDirty();
        void Load([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, uint dwMode);
        void Save([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, [MarshalAs(UnmanagedType.Bool)] bool fRemember);
        void SaveCompleted([MarshalAs(UnmanagedType.LPWStr)] string pszFileName);
        void GetCurFile([MarshalAs(UnmanagedType.LPWStr)] out string ppszFileName);
    }
}

/// <summary>
/// 纯 Win32 NTFS Directory Junction（目录联接 / IO_REPARSE_TAG_MOUNT_POINT）与进程占用预检底层辅助。
/// 无需管理员提权或开发者模式即可在 NTFS 卷创建原生透明联接，且 DeleteJunctionOnly 绝不穿透删除目标真实文件。
/// </summary>
internal static class JunctionNative
{
    private const uint IoReparseTagMountPoint = 0xA0000003;
    private const uint FsctlSetReparsePoint = 0x000900A4;
    private const uint FsctlGetReparsePoint = 0x000900A8;
    private const uint GenericWrite = 0x40000000;
    private const uint FileShareAll = 0x00000001 | 0x00000002 | 0x00000004;
    private const uint OpenExisting = 3;
    private const uint FileFlagBackupSemantics = 0x02000000;
    private const uint FileFlagOpenReparsePoint = 0x00200000;
    private const uint ProcessQueryLimitedInformation = 0x1000;

    public static bool IsNtfsVolume(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        try
        {
            string? root = Path.GetPathRoot(Path.GetFullPath(path));
            if (string.IsNullOrWhiteSpace(root)) return false;
            var di = new DriveInfo(root);
            return di.IsReady && string.Equals(di.DriveFormat, "NTFS", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    public static void CreateJunction(string junctionPath, string targetPath, bool setHidden = true)
    {
        if (string.IsNullOrWhiteSpace(junctionPath) || string.IsNullOrWhiteSpace(targetPath))
            throw new ArgumentException("软链路径或目标路径无效。");

        string fullJunction = Path.GetFullPath(junctionPath).TrimEnd('\\');
        string fullTarget = Path.GetFullPath(targetPath).TrimEnd('\\');

        if (!Directory.Exists(fullTarget))
            throw new DirectoryNotFoundException("软链目标目录不存在：" + fullTarget);
        if (Directory.Exists(fullJunction) || File.Exists(fullJunction))
            throw new IOException("软链创建位置已存在同名项：" + fullJunction);

        Directory.CreateDirectory(fullJunction);
        bool succeeded = false;
        try
        {
            string substituteName = @"\??\" + fullTarget;
            string printName = fullTarget;
            byte[] subBytes = Encoding.Unicode.GetBytes(substituteName);
            byte[] printBytes = Encoding.Unicode.GetBytes(printName);

            int reparseDataLen = 8 + subBytes.Length + 2 + printBytes.Length + 2;
            byte[] buffer = new byte[8 + reparseDataLen];

            BitConverter.GetBytes(IoReparseTagMountPoint).CopyTo(buffer, 0);
            BitConverter.GetBytes((ushort)reparseDataLen).CopyTo(buffer, 4);
            BitConverter.GetBytes((ushort)0).CopyTo(buffer, 6);
            BitConverter.GetBytes((ushort)0).CopyTo(buffer, 8);
            BitConverter.GetBytes((ushort)subBytes.Length).CopyTo(buffer, 10);
            BitConverter.GetBytes((ushort)(subBytes.Length + 2)).CopyTo(buffer, 12);
            BitConverter.GetBytes((ushort)printBytes.Length).CopyTo(buffer, 14);

            Buffer.BlockCopy(subBytes, 0, buffer, 16, subBytes.Length);
            Buffer.BlockCopy(printBytes, 0, buffer, 16 + subBytes.Length + 2, printBytes.Length);

            using var handle = CreateFileW(
                fullJunction,
                GenericWrite,
                FileShareAll,
                IntPtr.Zero,
                OpenExisting,
                FileFlagBackupSemantics | FileFlagOpenReparsePoint,
                IntPtr.Zero);

            if (handle.IsInvalid)
            {
                int err = Marshal.GetLastWin32Error();
                throw new IOException($"无法打开软链占位目录（Win32={err}）：{fullJunction}");
            }

            if (!DeviceIoControl(handle, FsctlSetReparsePoint, buffer, buffer.Length, IntPtr.Zero, 0, out _, IntPtr.Zero))
            {
                int err = Marshal.GetLastWin32Error();
                throw new IOException($"写入 NTFS 目录联接失败（Win32={err}）");
            }

            succeeded = true;
            if (setHidden)
            {
                try
                {
                    var attrs = File.GetAttributes(fullJunction);
                    File.SetAttributes(fullJunction, attrs | FileAttributes.Hidden);
                }
                catch { }
            }
        }
        finally
        {
            if (!succeeded)
            {
                try { if (Directory.Exists(fullJunction)) Directory.Delete(fullJunction, false); } catch { }
            }
        }
    }

    public static bool IsJunction(string? path, out string? targetPath)
    {
        targetPath = null;
        if (string.IsNullOrWhiteSpace(path)) return false;

        try
        {
            string full = Path.GetFullPath(path).TrimEnd('\\');
            if (!Directory.Exists(full)) return false;
            var attrs = File.GetAttributes(full);
            if ((attrs & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != (FileAttributes.Directory | FileAttributes.ReparsePoint))
                return false;

            using var handle = CreateFileW(
                full,
                0,
                FileShareAll,
                IntPtr.Zero,
                OpenExisting,
                FileFlagBackupSemantics | FileFlagOpenReparsePoint,
                IntPtr.Zero);

            if (handle.IsInvalid) return false;

            byte[] outBuf = new byte[16 * 1024];
            if (!DeviceIoControlOut(handle, FsctlGetReparsePoint, IntPtr.Zero, 0, outBuf, outBuf.Length, out int bytesReturned, IntPtr.Zero))
                return false;

            if (bytesReturned < 16) return false;
            uint tag = BitConverter.ToUInt32(outBuf, 0);
            if (tag != IoReparseTagMountPoint) return false;

            ushort subOffset = BitConverter.ToUInt16(outBuf, 8);
            ushort subLen = BitConverter.ToUInt16(outBuf, 10);
            if (16 + subOffset + subLen <= bytesReturned && subLen > 0)
            {
                string raw = Encoding.Unicode.GetString(outBuf, 16 + subOffset, subLen);
                if (raw.StartsWith(@"\??\", StringComparison.Ordinal))
                    raw = raw[4..];
                targetPath = raw.TrimEnd('\\');
            }
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>安全移除 NTFS 目录联接本身（严格校验确实是 Junction 才删，绝不删普通目录或穿透删目标内容）。</summary>
    public static void DeleteJunctionOnly(string junctionPath)
    {
        if (string.IsNullOrWhiteSpace(junctionPath)) return;
        string full = Path.GetFullPath(junctionPath).TrimEnd('\\');
        if (!IsJunction(full, out _))
            throw new InvalidOperationException("目标路径不是 NTFS 目录联接，已拒绝移除：" + full);

        try
        {
            var attrs = File.GetAttributes(full);
            if ((attrs & (FileAttributes.ReadOnly | FileAttributes.Hidden)) != 0)
                File.SetAttributes(full, attrs & ~(FileAttributes.ReadOnly | FileAttributes.Hidden));
        }
        catch { }

        Directory.Delete(full, recursive: false);
    }

    /// <summary>快速扫描当前是否有正在运行的进程其可执行文件位于指定目录内。</summary>
    public static List<string> FindRunningProcessesUnder(string folderPath)
    {
        var hits = new List<string>();
        if (!OperatingSystem.IsWindows() || string.IsNullOrWhiteSpace(folderPath)) return hits;

        string prefix = Path.GetFullPath(folderPath).TrimEnd('\\') + "\\";
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        Process[] procs;
        try { procs = Process.GetProcesses(); }
        catch { return hits; }

        var sb = new StringBuilder(1024);
        foreach (var p in procs)
        {
            try
            {
                IntPtr h = OpenProcess(ProcessQueryLimitedInformation, false, p.Id);
                if (h == IntPtr.Zero) continue;
                try
                {
                    int cap = sb.Capacity;
                    sb.Clear();
                    if (QueryFullProcessImageNameW(h, 0, sb, ref cap) && cap > 0)
                    {
                        string exePath = sb.ToString(0, cap);
                        if (exePath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                        {
                            string name = string.IsNullOrWhiteSpace(p.ProcessName)
                                ? Path.GetFileNameWithoutExtension(exePath)
                                : p.ProcessName;
                            if (seen.Add(name)) hits.Add(name);
                        }
                    }
                }
                finally
                {
                    CloseHandle(h);
                }
            }
            catch { }
            finally
            {
                try { p.Dispose(); } catch { }
            }
        }
        return hits;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(
        string lpFileName,
        uint dwDesiredAccess,
        uint dwShareMode,
        IntPtr lpSecurityAttributes,
        uint dwCreationDisposition,
        uint dwFlagsAndAttributes,
        IntPtr hTemplateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool DeviceIoControl(
        SafeFileHandle hDevice,
        uint dwIoControlCode,
        byte[] lpInBuffer,
        int nInBufferSize,
        IntPtr lpOutBuffer,
        int nOutBufferSize,
        out int lpBytesReturned,
        IntPtr lpOverlapped);

    [DllImport("kernel32.dll", EntryPoint = "DeviceIoControl", SetLastError = true)]
    private static extern bool DeviceIoControlOut(
        SafeFileHandle hDevice,
        uint dwIoControlCode,
        IntPtr lpInBuffer,
        int nInBufferSize,
        byte[] lpOutBuffer,
        int nOutBufferSize,
        out int lpBytesReturned,
        IntPtr lpOverlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, int dwProcessId);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool QueryFullProcessImageNameW(
        IntPtr hProcess,
        uint dwFlags,
        StringBuilder lpExeName,
        ref int lpdwSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);
}
