using System.Runtime.InteropServices;

namespace AiDiskCleaner.Native;

/// <summary>
/// Windows DPAPI（CryptProtectData / CryptUnprotectData）的 P/Invoke。
/// 直接调 crypt32 而不是引 NuGet 包：少一个外部依赖，行为也更明确（当前用户范围）。
/// </summary>
internal static class CryptoNative
{
    /// <summary>禁止弹任何 UI（DPAPI 在服务/无交互场景下的标准用法）。</summary>
    internal const uint CRYPTPROTECT_UI_FORBIDDEN = 0x01;

    [StructLayout(LayoutKind.Sequential)]
    internal struct DATA_BLOB
    {
        public int cbData;
        public IntPtr pbData;
    }

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    internal static extern bool CryptProtectData(
        ref DATA_BLOB pDataIn,
        string? szDataDescr,
        ref DATA_BLOB pOptionalEntropy,
        IntPtr pvReserved,
        IntPtr pPromptStruct,
        uint dwFlags,
        out DATA_BLOB pDataOut);

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    internal static extern bool CryptUnprotectData(
        ref DATA_BLOB pDataIn,
        IntPtr ppszDataDescr,
        ref DATA_BLOB pOptionalEntropy,
        IntPtr pvReserved,
        IntPtr pPromptStruct,
        uint dwFlags,
        out DATA_BLOB pDataOut);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern IntPtr LocalFree(IntPtr hMem);
}
