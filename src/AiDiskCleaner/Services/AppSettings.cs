using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace AiDiskCleaner.Services;

public enum AppLang { Zh, En }

public sealed class AiProviderCfg
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string BaseUrl { get; set; } = "";
    public string Protocol { get; set; } = "completions";

    /// <summary>
    /// API Key 的**明文**，只在内存里活着。
    /// JsonIgnore 保证它永远不会被写进 settings.json ——
    /// 落盘一律走 <see cref="SecretStore"/>（DPAPI 加密的 secrets.dat）。
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string ApiKey { get; set; } = "";

    /// <summary>密钥是不是已经存在加密仓库里（界面用来显示「已保存 / 需重填」）。</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool HasStoredKey => !string.IsNullOrEmpty(ApiKey);

    public List<string> Models { get; set; } = new();
    [System.Text.Json.Serialization.JsonIgnore]
    public string EditLabel => Loc.AiEdit;
    [System.Text.Json.Serialization.JsonIgnore]
    public string DeleteLabel => Loc.AiDelProvider;
    [System.Text.Json.Serialization.JsonIgnore]
    public string CustomLabel => Loc.AiCustomTag;
}

public sealed class AppSettings
{
    public AppLang Lang { get; set; } = AppLang.Zh;
    public int UiRev { get; set; }
    public List<AiProviderCfg> AiProviders { get; set; } = new();
    public string AiActiveId { get; set; } = "";
    public string AiModel { get; set; } = "";
    public string AiExtraPrompt { get; set; } = "";
    public List<string> AiJury { get; set; } = new();
    public bool AiJuryOn { get; set; }
    // 走 Pi sidecar（pi-ai 处理中转协议 / 推理内容 / 工具循环）。
    // sidecar 起不来会自动退回内置的 OpenAI.NET 客户端。
    public bool AiUseSidecar { get; set; } = true;

    /// <summary>是否允许把完整本地路径发给外部 AI。默认关（只发脱敏路径）。</summary>
    public bool AiSendFullPaths { get; set; }

    public string AiName { get; set; } = "";
    public string AiBaseUrl { get; set; } = "";
    public string AiProtocol { get; set; } = "completions";
    public List<string> AiModels { get; set; } = new();

    /// <summary>旧字段：以前这里是明文密钥。现在只用于迁移，读写都不落盘。</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string AiApiKey { get; set; } = "";

    /// <summary>上一次 Load 时有没有从旧版明文 settings.json 里迁移过密钥。</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool KeysMigratedFromPlaintext { get; private set; }

    private static string Path => AppPaths.SettingsFile;

    public AiProviderCfg? CurrentProvider()
    {
        Migrate();
        if (AiProviders.Count == 0) return null;
        return AiProviders.FirstOrDefault(p => p.Id == AiActiveId) ?? AiProviders[0];
    }

    public void Migrate()
    {
        if (AiProviders.Count > 0) return;
        if (string.IsNullOrWhiteSpace(AiBaseUrl) && string.IsNullOrWhiteSpace(AiModel) && string.IsNullOrWhiteSpace(AiApiKey))
            return;
        var p = new AiProviderCfg
        {
            Id = "default",
            Name = string.IsNullOrWhiteSpace(AiName) ? "default" : AiName,
            BaseUrl = AiBaseUrl ?? "",
            Protocol = AiProtocol ?? "completions",
            ApiKey = AiApiKey ?? "",
            Models = (AiModels ?? new()).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
        };
        if (!string.IsNullOrWhiteSpace(AiModel) && !p.Models.Contains(AiModel, StringComparer.OrdinalIgnoreCase))
            p.Models.Insert(0, AiModel);
        AiProviders.Add(p);
        AiActiveId = p.Id;
    }

    public static AppSettings Load()
    {
        AppSettings result;
        try
        {
            var path = Path;
            if (File.Exists(path))
            {
                result = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path)) ?? new AppSettings();
                // 旧版把密钥明文写在 settings.json 的 ApiKey / AiApiKey 里：读出来搬到加密仓库，
                // 然后立刻重存一次，把明文从 json 里抹掉。
                if (MigratePlaintextKeys(path, result)) result.Save();
            }
            else
            {
                result = new AppSettings();
            }
        }
        catch (Exception ex)
        {
            // 设置文件坏了不能让程序起不来：记一笔，退回默认设置继续。
            AppLog.Error("Settings", "settings.json unreadable, falling back to defaults", ex);
            result = new AppSettings();
        }

        if (result.UiRev < 2)
        {
            result.UiRev = 2;
            result.Save();
        }
        result.Migrate();
        ApplyStoredSecrets(result);
        return result;
    }

    /// <summary>把加密仓库里的密钥灌进内存对象。</summary>
    private static void ApplyStoredSecrets(AppSettings settings)
    {
        Dictionary<string, string> secrets;
        try { secrets = SecretStore.Load(); }
        catch (Exception ex)
        {
            AppLog.Error("Settings", "secret store load failed", ex);
            return;
        }
        if (secrets.Count == 0) return;

        settings.Migrate();
        foreach (var p in settings.AiProviders)
        {
            if (string.IsNullOrEmpty(p.ApiKey) && secrets.TryGetValue(p.Id, out var key))
                p.ApiKey = key;
        }
        // 兼容只有一个默认供应商的老结构
        if (secrets.TryGetValue("default", out var def))
        {
            foreach (var p in settings.AiProviders.Where(x => x.Id == "default" && string.IsNullOrEmpty(x.ApiKey)))
                p.ApiKey = def;
        }
    }

    /// <summary>
    /// 从旧版 settings.json 里捡明文密钥。故意直接用 JsonNode 读原始 json，
    /// 因为 <see cref="AiProviderCfg.ApiKey"/> 现在标了 JsonIgnore，反序列化拿不到它。
    /// </summary>
    private static bool MigratePlaintextKeys(string path, AppSettings settings)
    {
        try
        {
            var root = JsonNode.Parse(File.ReadAllText(path)) as JsonObject;
            if (root == null) return false;

            bool found = false;
            if (root["AiProviders"] is JsonArray providers)
            {
                settings.AiProviders ??= new();
                for (int i = 0; i < providers.Count && i < settings.AiProviders.Count; i++)
                {
                    string? plain = providers[i]?["ApiKey"]?.GetValue<string>();
                    if (!string.IsNullOrWhiteSpace(plain) && string.IsNullOrEmpty(settings.AiProviders[i].ApiKey))
                    {
                        settings.AiProviders[i].ApiKey = plain;
                        found = true;
                    }
                }
            }

            string? legacy = root["AiApiKey"]?.GetValue<string>();
            if (!string.IsNullOrWhiteSpace(legacy) && string.IsNullOrEmpty(settings.AiApiKey))
            {
                settings.AiApiKey = legacy;
                found = true;
            }
            if (found) AppLog.Info("Settings", "migrating plaintext API key(s) into the encrypted store");
            return found;
        }
        catch (Exception ex)
        {
            AppLog.Error("Settings", "plaintext key migration failed", ex);
            return false;
        }
    }

    public void Save()
    {
        try
        {
            Migrate();

            // 1) 密钥：加密存到单独的 secrets.dat
            var secrets = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in AiProviders)
                if (!string.IsNullOrWhiteSpace(p.Id) && !string.IsNullOrEmpty(p.ApiKey))
                    secrets[p.Id] = p.ApiKey;
            if (secrets.Count > 0 && !SecretStore.Save(secrets))
                AppLog.Warn("Settings", "API key(s) could not be encrypted; they stay in memory only");

            // 2) 普通设置：照旧写 JSON。ApiKey / AiApiKey 都有 JsonIgnore，不会出现在文件里。
            var dir = System.IO.Path.GetDirectoryName(Path)!;
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex)
        {
            // 保存失败要让用户知道设置没生效，但不能打断界面。
            AppLog.Error("Settings", "settings.json save failed", ex);
        }
    }

    /// <summary>设置页「清除 API Key」：内存 + 加密仓库一起清掉。</summary>
    public void ClearAllApiKeys()
    {
        foreach (var p in AiProviders) p.ApiKey = "";
        AiApiKey = "";
        SecretStore.Clear();
        Save();
    }
}
