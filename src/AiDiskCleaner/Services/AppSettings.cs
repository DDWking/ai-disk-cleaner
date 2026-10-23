using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using AiDiskCleaner.Models;

namespace AiDiskCleaner.Services;

public enum AppLang { Zh, En }

public sealed class AiProviderCfg
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string BaseUrl { get; set; } = "";

    /// <summary>Jev 接口形状：官方 Decisions API 或 TypeSafe System One API。</summary>
    public string Protocol { get; set; } = "decisions";

    // v3.0 删掉了聊天协议字段；现在保留的是 Jev 的两种官方结构化 API。旧配置中的 Protocol
    // 若是 completions 等聊天值，会在迁移时归一为 decisions。 </summary>
    // 聊天通道（completions / responses / anthropic）连同
    // 协议下拉一起下线，供应商只可能是判定端点。老配置里那个字段留在 json 里没关系，
    // 反序列化会忽略未知成员。

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

    /// <summary>
    /// 旧字段：聊天时代的「全局聊天模型」。v3.0 起整个软件只有判定一条通道，
    /// 模型统一由 <see cref="AiDecisionModel"/> 表达。保留它只是为了读得懂老配置
    /// （<see cref="Migrate"/> 会把它搬进判定模型），**新代码不要再用**。
    /// </summary>
    public string AiModel { get; set; } = "";

    /// <summary>是否允许把完整本地路径发给外部 AI。默认关（只发脱敏路径）。</summary>
    public bool AiSendFullPaths { get; set; }

    /// <summary>
    /// 判定通道用哪个供应商。留空时自动退到**第一个配了地址的供应商** ——
    /// v3.0 起所有供应商都是判定端点（协议概念随聊天通道一起删了）。
    /// </summary>
    public string AiDecisionProviderId { get; set; } = "";

    /// <summary>判定用的模型 id。留空时退到该供应商模型列表的第一个。</summary>
    public string AiDecisionModel { get; set; } = "";

    // 旧单供应商结构的迁移字段（老 settings.json 里就一个 name/baseUrl/protocol/models）。
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

    /// <summary>
    /// 判定通道用哪个供应商：显式指定优先，否则第一个配了地址的。
    /// （v3.0 起没有「协议」这个概念了 —— 聊天通道删掉之后，供应商只可能是判定端点。）
    /// </summary>
    public AiProviderCfg? DecisionProvider()
    {
        Migrate();
        if (AiProviders.Count == 0) return null;
        if (!string.IsNullOrWhiteSpace(AiDecisionProviderId))
        {
            var byId = AiProviders.FirstOrDefault(p => p.Id == AiDecisionProviderId);
            if (byId != null) return byId;
        }
        return AiProviders.FirstOrDefault(p => !string.IsNullOrWhiteSpace(p.BaseUrl))
               ?? AiProviders[0];
    }

    public string DecisionModelId()
    {
        if (!string.IsNullOrWhiteSpace(AiDecisionModel)) return AiDecisionModel.Trim();
        return DecisionProvider()?.Models.FirstOrDefault(m => !string.IsNullOrWhiteSpace(m))?.Trim() ?? "";
    }

    /// <summary>批量用途判定能不能用：地址和模型都得有。密钥可以为空（本地中转常见）。</summary>
    public bool DecisionConfigured()
    {
        var p = DecisionProvider();
        return p != null
               && !string.IsNullOrWhiteSpace(p.BaseUrl)
               && !string.IsNullOrWhiteSpace(DecisionModelId());
    }

    public void Migrate()
    {
        if (AiProviders.Count == 0)
        {
            bool hasLegacy = !string.IsNullOrWhiteSpace(AiBaseUrl)
                             || !string.IsNullOrWhiteSpace(AiModel)
                             || !string.IsNullOrWhiteSpace(AiApiKey);
            if (hasLegacy)
            {
                var p = new AiProviderCfg
                {
                    Id = "default",
                    Name = string.IsNullOrWhiteSpace(AiName) ? "default" : AiName,
                    BaseUrl = AiBaseUrl ?? "",
                    ApiKey = AiApiKey ?? "",
                    Models = (AiModels ?? new()).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                };
                if (!string.IsNullOrWhiteSpace(AiModel) && !p.Models.Contains(AiModel, StringComparer.OrdinalIgnoreCase))
                    p.Models.Insert(0, AiModel);
                AiProviders.Add(p);
                AiActiveId = p.Id;

                // v3.0：聊天通道删掉之后，老的「全局聊天模型」就是唯一的模型 ——
                // 搬进判定模型，否则老用户升级后会看到模型是空的，以为配置丢了。
                if (string.IsNullOrWhiteSpace(AiDecisionModel) && !string.IsNullOrWhiteSpace(AiModel))
                    AiDecisionModel = AiModel.Trim();
            }
        }

        NormalizeProtocols();
    }

    /// <summary>
    /// 把供应商上的 <see cref="AiProviderCfg.Protocol"/> 收敛到两种 Jev 接口形状。
    ///
    /// 旧配置里存的是聊天协议（completions / responses / anthropic）。那套已经下线，
    /// 但字段还留在 json 里 —— 如果继续拿它当门槛，老用户升级后会「看起来配好了却不能用」。
    /// 只认 systemone，其余一律当 decisions。
    /// </summary>
    private void NormalizeProtocols()
    {
        foreach (var p in AiProviders)
        {
            if (!string.Equals(p.Protocol, "systemone", StringComparison.OrdinalIgnoreCase))
                p.Protocol = "decisions";
        }
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
