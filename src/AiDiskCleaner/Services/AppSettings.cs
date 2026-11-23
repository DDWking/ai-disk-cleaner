using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using AiDiskCleaner.Models;

namespace AiDiskCleaner.Services;

public enum AppLang { Zh, En }

[System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter))]
public enum AppTheme { Dark, Light }

/// <summary>
/// 供应商最近一次连接测试的结果。会落盘（settings.json）：
/// 顶栏那盏灯重启后仍能显示「上次测试通过」，直到用户改配置或再测失败。
/// </summary>
[System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter))]
public enum AiProviderHealth { Unknown, Ok, Failed }

public sealed class AiProviderCfg
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string BaseUrl { get; set; } = "";

    /// <summary>协议标记。v3.1 起只有 OpenAI 兼容聊天一种，统一写 "chat"。</summary>
    public string Protocol { get; set; } = "chat";

    // 旧配置里的 decisions / systemone / completions 等值会在 NormalizeProtocols()
    // 里统一归一为 "chat"；JsonIgnore？不用：留着旧值也不影响，只在内存里改。

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

    /// <summary>最近一次「测试连接」的结果；落盘，顶栏的 AI 灯靠它保持绿/黄。</summary>
    public AiProviderHealth Health { get; set; }

    /// <summary>最近一次测试失败的人话原因（不含密钥），供顶栏提示用。</summary>
    public string LastError { get; set; } = "";

    /// <summary>界面用：是不是 AI 分析当前使用的那家（由 LoadAiFields 重新计算）。</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsActive { get; set; }

    /// <summary>地址和模型都有才算「配置完整」；密钥可以为空（本地中转常见）。</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool Configured =>
        !string.IsNullOrWhiteSpace(BaseUrl)
        && Models.Any(m => !string.IsNullOrWhiteSpace(m));

    [System.Text.Json.Serialization.JsonIgnore]
    public string ModelLabel =>
        Models.FirstOrDefault(m => !string.IsNullOrWhiteSpace(m))?.Trim() ?? "";

    [System.Text.Json.Serialization.JsonIgnore]
    public string ActiveLabel => Loc.AiInUse;

    [System.Text.Json.Serialization.JsonIgnore]
    public string UseTip => Loc.AiUseThis;

    /// <summary>灰=未选择供应商；黄=配置不完整 / 还没测过；绿=测试通过；红=测试失败。</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string StatusKind =>
        Health == AiProviderHealth.Ok ? "ok"
        : Health == AiProviderHealth.Failed ? "fail"
        : "warn";

    [System.Text.Json.Serialization.JsonIgnore]
    public string StatusTip =>
        !Configured ? Loc.AiStatusIncomplete
        : Health == AiProviderHealth.Ok ? Loc.AiStatusReady
        : Health == AiProviderHealth.Failed
            ? Loc.AiStatusFailed + (LastError.Length > 0 ? " · " + LastError : "")
        : Loc.AiStatusUntested + (HasStoredKey ? "" : " · " + Loc.AiStatusNoKey);
}

public sealed class AppSettings
{
    public AppLang Lang { get; set; } = AppLang.Zh;
    public AppTheme Theme { get; set; } = AppTheme.Light;
    public int UiRev { get; set; }
    public List<AiProviderCfg> AiProviders { get; set; } = new();
    public string AiActiveId { get; set; } = "";

    /// <summary>
    /// 旧字段：聊天时代的「全局聊天模型」。现在模型统一由 <see cref="AiDecisionModel"/>
    /// 表达。保留它只是为了读得懂老配置（<see cref="Migrate"/> 会把它搬进判定模型），
    /// **新代码不要再用**。
    /// </summary>
    public string AiModel { get; set; } = "";

    /// <summary>是否允许把完整本地路径发给外部 AI。默认关（只发脱敏路径）。</summary>
    public bool AiSendFullPaths { get; set; }

    /// <summary>「整理归类」页用户自定义的目标大类规则；为空时按预设兜底。</summary>
    public List<ClassifyCategoryRule> ClassifyCategories { get; set; } = new();

    /// <summary>是否在盘符分析时也纳入「下载」目录里面的杂项子文件夹。</summary>
    public bool ClassifyIncludeDownloadsChildren { get; set; } = true;

    /// <summary>移动绿软/便携程序后，自动更新指向它的桌面或开始菜单快捷方式。</summary>
    public bool ClassifyUpdateShortcuts { get; set; } = true;

    /// <summary>用户标记「别再推荐整理」的目录全路径。</summary>
    public List<string> ClassifyIgnoredPaths { get; set; } = new();

    public List<ClassifyCategoryRule> EffectiveClassifyCategories()
    {
        if (ClassifyCategories is { Count: > 0 })
            return ClassifyCategories.Select(c => c.Clone()).ToList();
        return ClassifyCategoryRule.DefaultPresets();
    }

    /// <summary>
    /// 用哪个 AI 供应商。留空时自动退到**第一个配了地址的供应商**。
    /// 属性名是历史遗留（v3.0 判定时代），语义现在就是「聊天供应商」。
    /// </summary>
    public string AiDecisionProviderId { get; set; } = "";

    /// <summary>聊天模型 id。留空时退到该供应商模型列表的第一个。</summary>
    public string AiDecisionModel { get; set; } = "";

    // 旧单供应商结构的迁移字段（老 settings.json 里就一个 name/baseUrl/protocol/models）。
    public string AiName { get; set; } = "";
    public string AiBaseUrl { get; set; } = "";
    public string AiProtocol { get; set; } = "chat";
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
    /// AI 分析使用哪家供应商：显式选择优先；没选过就退到第一个填了地址的。
    /// 找不到（一个都没配地址）就返回 null，界面显示「未选择」，而不是偷偷用第一个。
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
        return AiProviders.FirstOrDefault(p => !string.IsNullOrWhiteSpace(p.BaseUrl));
    }

    /// <summary>切换「AI 分析使用」的供应商。这是唯一写入口，避免两个 active 字段各说各话。</summary>
    public void SetActiveProvider(string? id)
    {
        AiDecisionProviderId = id ?? "";
        AiActiveId = AiDecisionProviderId;   // 旧字段镜像，读老配置的人不会看到两个答案
    }

    /// <summary>
    /// 模型跟供应商走：读当前供应商的模型表；只有老配置里还有「全局模型」时才回退。
    /// </summary>
    public string DecisionModelId()
    {
        var p = DecisionProvider();
        var model = p?.Models.FirstOrDefault(m => !string.IsNullOrWhiteSpace(m))?.Trim();
        if (!string.IsNullOrEmpty(model)) return model;
        return AiDecisionModel.Trim();
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
        EnsureActiveProviderHasModel();
    }

    /// <summary>
    /// 把老配置里的「全局模型」补进当前供应商的模型表，让「模型跟供应商走」升级后不空。
    /// 只在当前供应商一条模型都没有时补，已经有模型的不动。
    /// </summary>
    private void EnsureActiveProviderHasModel()
    {
        // 这里不能调 DecisionProvider()：它会再触发 Migrate()，形成无限递归。
        AiProviderCfg? p = null;
        if (!string.IsNullOrWhiteSpace(AiDecisionProviderId))
            p = AiProviders.FirstOrDefault(x => x.Id == AiDecisionProviderId);
        p ??= AiProviders.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x.BaseUrl));
        if (p == null || p.Models.Any(m => !string.IsNullOrWhiteSpace(m))) return;
        string legacy = !string.IsNullOrWhiteSpace(AiDecisionModel) ? AiDecisionModel.Trim() : AiModel.Trim();
        if (legacy.Length > 0) p.Models.Add(legacy);
    }

    /// <summary>
    /// 把供应商上的 <see cref="AiProviderCfg.Protocol"/> 收敛到 <c>chat</c>。
    ///
    /// v3.0 存的是 Jev 的 decisions / systemone。那套接口已经下线，
    /// 字段还留在 json 里 —— 继续拿它当门槛，老用户升级后会「看起来配好了却不能用」。
    /// 全部归一到 OpenAI 兼容聊天接口。
    /// </summary>
    private void NormalizeProtocols()
    {
        foreach (var p in AiProviders)
        {
            if (!string.Equals(p.Protocol, AiConfigurationService.ChatProtocol, StringComparison.OrdinalIgnoreCase))
                p.Protocol = AiConfigurationService.ChatProtocol;
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

        if (result.UiRev < 3)
        {
            result.UiRev = 3;
            result.Theme = AppTheme.Light;
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
