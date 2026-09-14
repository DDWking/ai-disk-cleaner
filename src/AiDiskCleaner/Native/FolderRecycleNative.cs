using System.IO;
using System.Runtime.InteropServices;

namespace AiDiskCleaner.Native;

/// <summary>
/// Shell 的 <c>IFileOperation</c> 互操作。**只用于「必须进回收站」的删除**：
/// <list type="bullet">
/// <item><c>FOFX_RECYCLEONDELETE</c>（Windows 8+）：删除时送入回收站而不是永久删除；</item>
/// <item><c>FOF_WANTNUKEWARNING</c>：万一真的走到「销毁而非回收」，也要弹警告，
/// 不允许静默销毁（它部分覆盖 <c>FOF_NOCONFIRMATION</c>）；</item>
/// <item><c>FOFX_EARLYFAILURE</c> + <c>FOF_NOERRORUI</c>：单项失败立即整体失败，不继续。</item>
/// </list>
/// 这里刻意**没有**任何永久删除 API（没有 DeleteFile / SHFileOperation 无 ALLOWUNDO 调用）。
/// 旧清理链路的 <see cref="ShellNative.SHFileOperation"/> 行为保持不变，本文件与之隔离。
/// </summary>
internal static class FolderRecycleNative
{
    // FOF flags（shellapi.h）
    public const uint FOF_NOCONFIRMATION = 0x0010;
    public const uint FOF_SILENT = 0x0004;
    public const uint FOF_NOERRORUI = 0x0400;
    public const uint FOF_WANTNUKEWARNING = 0x4000;
    // FOFX flags（shobjidl.h）
    public const uint FOFX_RECYCLEONDELETE = 0x00080000;
    public const uint FOFX_EARLYFAILURE = 0x00100000;

    public const uint RecycleRequiredFlags =
        FOF_NOCONFIRMATION | FOF_SILENT | FOF_NOERRORUI | FOF_WANTNUKEWARNING
        | FOFX_EARLYFAILURE | FOFX_RECYCLEONDELETE;

    private static readonly Guid ClsidFileOperation = new("3ad05575-8857-4850-9277-11b85bdb8e09");
    private static readonly Guid IidShellItem = new("43826d1e-e718-42ee-bc55-a1e261c37bfe");

    [StructLayout(LayoutKind.Sequential)]
    public struct SHQUERYRBINFO
    {
        public int cbSize;
        public long i64Size;
        public long i64NumItems;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHQueryRecycleBin(
        [MarshalAs(UnmanagedType.LPWStr)] string pszRootPath, ref SHQUERYRBINFO pSHQueryRBInfo);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    private static extern void SHCreateItemFromParsingName(
        [MarshalAs(UnmanagedType.LPWStr)] string pszPath,
        IntPtr pbc,
        ref Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out IShellItemNative ppv);

    /// <summary>卷根（"C:\"）是否支持回收站查询。S_OK 才算可用。</summary>
    public static bool TryQueryRecycleBin(string root, out string tech)
    {
        tech = "";
        try
        {
            var info = new SHQUERYRBINFO { cbSize = Marshal.SizeOf<SHQUERYRBINFO>() };
            int hr = SHQueryRecycleBin(root, ref info);
            if (hr != 0)
            {
                tech = "SHQueryRecycleBin 0x" + hr.ToString("X8");
                return false;
            }
            return true;
        }
        catch (Exception ex)
        {
            tech = ex.GetType().Name + ": " + ex.Message;
            return false;
        }
    }

    /// <summary>
    /// 用 IFileOperation 删除单个路径，并要求必须进回收站。返回 HRESULT（0 = 成功）。
    /// </summary>
    public static void RecycleRequired(string path)
    {
        IFileOperationNative? op = null;
        IShellItemNative? item = null;
        try
        {
            op = CreateFileOperation();
            int flagsHr = op.SetOperationFlags(RecycleRequiredFlags);
            if (flagsHr != 0)
                throw new IOException("SetOperationFlags failed 0x" + flagsHr.ToString("X8")) { HResult = flagsHr };
            op.SetOwnerWindow(IntPtr.Zero);

            item = CreateShellItem(path);
            int delHr = op.DeleteItem(item, IntPtr.Zero);
            if (delHr != 0)
                throw new IOException("DeleteItem failed 0x" + delHr.ToString("X8")) { HResult = delHr };

            int perfHr = op.PerformOperations();
            if (perfHr != 0)
                throw new IOException("IFileOperation failed 0x" + perfHr.ToString("X8")) { HResult = perfHr };

            int abortedHr = op.GetAnyOperationsAborted(out bool aborted);
            if (abortedHr != 0)
                throw new IOException("GetAnyOperationsAborted failed 0x" + abortedHr.ToString("X8")) { HResult = abortedHr };
            if (aborted)
                throw new OperationCanceledException("IFileOperation reported aborted")
                {
                    HResult = unchecked((int)0x800704C7), // ERROR_CANCELLED
                };
        }
        finally
        {
            Release(item);
            Release(op);
        }
    }

    private static IFileOperationNative CreateFileOperation()
    {
        var type = Type.GetTypeFromCLSID(ClsidFileOperation)
            ?? throw new PlatformNotSupportedException("IFileOperation CLSID is not registered");
        return (IFileOperationNative)Activator.CreateInstance(type)!;
    }

    private static IShellItemNative CreateShellItem(string path)
    {
        var iid = IidShellItem;
        SHCreateItemFromParsingName(path, IntPtr.Zero, ref iid, out var item);
        return item;
    }

    private static void Release(object? comObject)
    {
        if (comObject == null || !Marshal.IsComObject(comObject)) return;
        try { Marshal.ReleaseComObject(comObject); }
        catch { /* 释放失败不影响结果 */ }
    }

    [ComImport, Guid("947aab5f-0a5c-4c13-b4d6-4bf7836fc9f8"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IFileOperationNative
    {
        [PreserveSig] int Advise(IntPtr pfops, out uint pdwCookie);
        [PreserveSig] int Unadvise(uint dwCookie);
        [PreserveSig] int SetOperationFlags(uint dwOperationFlags);
        [PreserveSig] int SetProgressMessage([MarshalAs(UnmanagedType.LPWStr)] string pszMessage);
        [PreserveSig] int SetProgressDialog(IntPtr popd);
        [PreserveSig] int SetProperties(IntPtr pproparray);
        [PreserveSig] int SetOwnerWindow(IntPtr hwndOwner);
        [PreserveSig] int ApplyPropertiesToItem(IntPtr psiItem);
        [PreserveSig] int ApplyPropertiesToItems(IntPtr punkItems);
        [PreserveSig] int RenameItem(IntPtr psiItem, [MarshalAs(UnmanagedType.LPWStr)] string pszNewName, IntPtr pfopsItem);
        [PreserveSig] int RenameItems(IntPtr pUnkItems, [MarshalAs(UnmanagedType.LPWStr)] string pszNewName);
        [PreserveSig] int MoveItem(IntPtr psiItem, IntPtr psiDestinationFolder, [MarshalAs(UnmanagedType.LPWStr)] string pszNewName, IntPtr pfopsItem);
        [PreserveSig] int MoveItems(IntPtr punkItems, IntPtr psiDestinationFolder);
        [PreserveSig] int CopyItem(IntPtr psiItem, IntPtr psiDestinationFolder, [MarshalAs(UnmanagedType.LPWStr)] string pszCopyName, IntPtr pfopsItem);
        [PreserveSig] int CopyItems(IntPtr punkItems, IntPtr psiDestinationFolder);
        [PreserveSig] int DeleteItem(IShellItemNative psiItem, IntPtr pfopsItem);
        [PreserveSig] int DeleteItems(IntPtr punkItems);
        [PreserveSig] int NewItem(IntPtr psiDestinationFolder, uint dwFileAttributes,
            [MarshalAs(UnmanagedType.LPWStr)] string pszName,
            [MarshalAs(UnmanagedType.LPWStr)] string pszTemplateName, IntPtr pfopsItem);
        [PreserveSig] int PerformOperations();
        [PreserveSig] int GetAnyOperationsAborted([MarshalAs(UnmanagedType.Bool)] out bool pfAnyOperationsAborted);
    }

    [ComImport, Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItemNative
    {
        [PreserveSig] int BindToHandler(IntPtr pbc, ref Guid bhid, ref Guid riid, out IntPtr ppv);
        [PreserveSig] int GetParent(out IShellItemNative ppsi);
        [PreserveSig] int GetDisplayName(uint sigdnName, out IntPtr ppszName);
        [PreserveSig] int GetAttributes(uint sfgaoMask, out uint psfgaoAttribs);
        [PreserveSig] int Compare(IShellItemNative psi, uint hint, out int piOrder);
    }
}
