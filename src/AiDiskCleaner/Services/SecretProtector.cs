using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using AiDiskCleaner.Native;

namespace AiDiskCleaner.Services;

/// <summary>
/// API Key 的加密与存储。
///
/// 硬规则：
/// - 明文密钥**只**存在于内存里（<see cref="AiProviderCfg.ApiKey"/> 标了 JsonIgnore，
///   永远不会写进 settings.json）；
/// - 落盘一律走 Windows DPAPI（当前用户范围），所以把文件拷到别的机器/别的账户都解不开；
/// - 解密失败只当作「没有密钥」，提示用户重新填，**绝不**退回明文存储；
/// - 任何日志/异常/诊断包里的密钥都由 <see cref="LogRedactor"/> 抹掉。
/// </summary>
public static class SecretProtector
{
    /// <summary>额外的熵，防止别的程序拿同一份 DPAPI blob 直接解。</summary>
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("DashaoHuo.ApiKey.v1");

    private static bool? _available;

    /// <summary>DPAPI 是否可用。不可用时界面会提示密钥只能用当前会话。</summary>
    public static bool Available
    {
        get
        {
            if (_available is { } cached) return cached;
            bool ok;
            try
            {
                var probe = Protect("probe");
                ok = probe != null && Unprotect(probe) == "probe";
            }
            catch { ok = false; }
            _available = ok;
            return ok;
        }
    }

    /// <summary>加密，返回 Base64。失败返回 null（调用方必须按「没存上」处理）。</summary>
    public static string? Protect(string plain)
    {
        if (string.IsNullOrEmpty(plain)) return "";
        IntPtr inPtr = IntPtr.Zero;
        IntPtr entropyPtr = IntPtr.Zero;
        try
        {
            byte[] data = Encoding.UTF8.GetBytes(plain);
            inPtr = Marshal.AllocHGlobal(data.Length);
            Marshal.Copy(data, 0, inPtr, data.Length);

            entropyPtr = Marshal.AllocHGlobal(Entropy.Length);
            Marshal.Copy(Entropy, 0, entropyPtr, Entropy.Length);

            var input = new CryptoNative.DATA_BLOB { cbData = data.Length, pbData = inPtr };
            // pOptionalEntropy 要的是 DATA_BLOB* —— 必须传结构体本身，
            // 传裸字节缓冲会让 DPAPI 把前 4 字节当长度读，直接调用失败。
            var entropy = new CryptoNative.DATA_BLOB { cbData = Entropy.Length, pbData = entropyPtr };

            bool ok = CryptoNative.CryptProtectData(
                ref input, "DashaoHuo API key", ref entropy, IntPtr.Zero, IntPtr.Zero,
                CryptoNative.CRYPTPROTECT_UI_FORBIDDEN, out var output);
            if (!ok)
            {
                AppLog.Write(new LogEntry(DateTime.UtcNow, LogLevel.Warn, "Secret", "", "protect",
                    "CryptProtectData failed, win32=" + Marshal.GetLastWin32Error()));
                return null;
            }
            try
            {
                if (output.cbData <= 0) return null;
                var buf = new byte[output.cbData];
                Marshal.Copy(output.pbData, buf, 0, output.cbData);
                return Convert.ToBase64String(buf);
            }
            finally
            {
                if (output.pbData != IntPtr.Zero) CryptoNative.LocalFree(output.pbData);
            }
        }
        catch
        {
            return null;
        }
        finally
        {
            if (inPtr != IntPtr.Zero) Marshal.FreeHGlobal(inPtr);
            if (entropyPtr != IntPtr.Zero) Marshal.FreeHGlobal(entropyPtr);
        }
    }

    /// <summary>解密。任何失败都返回 null —— 调用方据此走「安全降级」。</summary>
    public static string? Unprotect(string? base64)
    {
        if (string.IsNullOrEmpty(base64)) return "";
        IntPtr inPtr = IntPtr.Zero;
        IntPtr entropyPtr = IntPtr.Zero;
        try
        {
            byte[] data;
            try { data = Convert.FromBase64String(base64); }
            catch (FormatException) { return null; }

            inPtr = Marshal.AllocHGlobal(data.Length);
            Marshal.Copy(data, 0, inPtr, data.Length);
            entropyPtr = Marshal.AllocHGlobal(Entropy.Length);
            Marshal.Copy(Entropy, 0, entropyPtr, Entropy.Length);

            var input = new CryptoNative.DATA_BLOB { cbData = data.Length, pbData = inPtr };
            var entropy = new CryptoNative.DATA_BLOB { cbData = Entropy.Length, pbData = entropyPtr };
            bool ok = CryptoNative.CryptUnprotectData(
                ref input, IntPtr.Zero, ref entropy, IntPtr.Zero, IntPtr.Zero,
                CryptoNative.CRYPTPROTECT_UI_FORBIDDEN, out var output);
            if (!ok) return null;
            try
            {
                if (output.cbData <= 0) return "";
                var buf = new byte[output.cbData];
                Marshal.Copy(output.pbData, buf, 0, output.cbData);
                return Encoding.UTF8.GetString(buf);
            }
            finally
            {
                if (output.pbData != IntPtr.Zero) CryptoNative.LocalFree(output.pbData);
            }
        }
        catch
        {
            return null;
        }
        finally
        {
            if (inPtr != IntPtr.Zero) Marshal.FreeHGlobal(inPtr);
            if (entropyPtr != IntPtr.Zero) Marshal.FreeHGlobal(entropyPtr);
        }
    }
}

/// <summary>
/// 加密后的密钥仓库。单独一个文件，和 settings.json 分开，
/// 这样即使 settings.json 被同步/分享出去也不会带出密钥。
/// </summary>
public static class SecretStore
{
    private static readonly object Gate = new();

    public static string FilePath => AppPaths.SecretsFile;

    public static bool Exists
    {
        get { try { return File.Exists(FilePath); } catch { return false; } }
    }

    /// <summary>
    /// 读出 id → 明文密钥。任何一步失败都返回空表（安全降级），
    /// 调用方看到空表就等于「密钥需要重新填」。
    /// </summary>
    public static Dictionary<string, string> Load()
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            lock (Gate)
            {
                if (!File.Exists(FilePath)) return result;
                string raw = File.ReadAllText(FilePath, Encoding.UTF8);
                var payload = JsonSerializer.Deserialize<SecretPayload>(raw);
                if (payload?.Items == null) return result;

                foreach (var (id, cipher) in payload.Items)
                {
                    if (string.IsNullOrWhiteSpace(id) || string.IsNullOrEmpty(cipher)) continue;
                    string? plain = SecretProtector.Unprotect(cipher);
                    if (plain == null)
                    {
                        // 解不开（换了机器/换了账户/文件被改）：当没有，别当明文用。
                        AppLog.Warn("Secret", "one stored API key could not be decrypted and was dropped");
                        continue;
                    }
                    if (plain.Length > 0) result[id] = plain;
                }
            }
        }
        catch (Exception ex)
        {
            AppLog.Error("Secret", "secret store unreadable, dropping stored keys", ex);
            result.Clear();
        }
        return result;
    }

    /// <summary>加密写盘。空表就删文件（不留下空壳）。</summary>
    public static bool Save(IReadOnlyDictionary<string, string> secrets)
    {
        try
        {
            lock (Gate)
            {
                var items = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var (id, plain) in secrets)
                {
                    if (string.IsNullOrWhiteSpace(id) || string.IsNullOrEmpty(plain)) continue;
                    string? cipher = SecretProtector.Protect(plain);
                    if (cipher == null)
                    {
                        AppLog.Error("Secret", "DPAPI protect failed; API key was NOT persisted");
                        return false;
                    }
                    items[id] = cipher;
                }

                if (items.Count == 0)
                {
                    Delete();
                    return true;
                }

                string dir = Path.GetDirectoryName(FilePath)!;
                Directory.CreateDirectory(dir);
                string json = JsonSerializer.Serialize(new SecretPayload { Items = items });
                File.WriteAllText(FilePath, json, Encoding.UTF8);
                return true;
            }
        }
        catch (Exception ex)
        {
            AppLog.Error("Secret", "secret store write failed", ex);
            return false;
        }
    }

    /// <summary>清除全部已存密钥（设置页「清除 API Key」用）。</summary>
    public static bool Clear()
    {
        try
        {
            lock (Gate) { Delete(); }
            return true;
        }
        catch (Exception ex)
        {
            AppLog.Error("Secret", "secret store clear failed", ex);
            return false;
        }
    }

    private static void Delete()
    {
        if (File.Exists(FilePath)) File.Delete(FilePath);
    }

    private sealed class SecretPayload
    {
        public int Version { get; set; } = 1;
        public Dictionary<string, string> Items { get; set; } = new();
    }
}
