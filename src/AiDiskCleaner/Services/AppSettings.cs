using System.IO;
using System.Text.Json;

namespace AiDiskCleaner.Services;

public enum AppLang { Zh, En }

public sealed class AiProviderCfg
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string BaseUrl { get; set; } = "";
    public string Protocol { get; set; } = "completions";
    public string ApiKey { get; set; } = "";
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

    public string AiName { get; set; } = "";
    public string AiBaseUrl { get; set; } = "";
    public string AiProtocol { get; set; } = "completions";
    public List<string> AiModels { get; set; } = new();
    public string AiApiKey { get; set; } = "";

    private static string Path =>
        System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "DashaoHuo", "settings.json");

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
        try
        {
            var path = Path;
            if (File.Exists(path))
            {
                var loaded = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path)) ?? new AppSettings();
                if (loaded.UiRev < 2)
                {
                    loaded.UiRev = 2;
                    loaded.Save();
                }
                loaded.Migrate();
                return loaded;
            }
        }
        catch { }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            Migrate();
            var dir = System.IO.Path.GetDirectoryName(Path)!;
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }
}
