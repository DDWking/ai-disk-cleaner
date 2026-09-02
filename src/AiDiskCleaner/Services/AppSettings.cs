using System.IO;
using System.Text.Json;

namespace AiDiskCleaner.Services;

public enum AppLang { Zh, En }

public sealed class AppSettings
{
    public AppLang Lang { get; set; } = AppLang.Zh;
    public int UiRev { get; set; }
    public string AiProvider { get; set; } = nameof(AiKind.OpenAI);
    public string AiBaseUrl { get; set; } = "https://api.openai.com/v1";
    public string AiModel { get; set; } = "gpt-4o-mini";
    public string AiApiKey { get; set; } = "";

    private static string Path =>
        System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "DashaoHuo", "settings.json");

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
            var dir = System.IO.Path.GetDirectoryName(Path)!;
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }
}
