using System.IO;
using System.Text.Json;

namespace AiDiskCleaner.Services;

public enum AppTheme { Terminal, Mono, Cyberpunk }
public enum AppLang { Zh, En }

public sealed class AppSettings
{
    public AppTheme Theme { get; set; } = AppTheme.Terminal;
    public AppLang Lang { get; set; } = AppLang.Zh;

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
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path)) ?? new AppSettings();
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
