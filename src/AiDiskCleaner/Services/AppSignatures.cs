using AiDiskCleaner.Models;

namespace AiDiskCleaner.Services;

public enum SigRisk { Safe, Cautious, Keep, Bloat }

public sealed record AppSig(
    string Name,
    string Category,
    SigRisk Risk,
    string[] Needles,
    string[]? Subs = null,
    string? Note = null,
    string? Migrate = null);

public readonly record struct AppHit(AppSig Sig, string Needle, bool SubHit);

public static class AppSignatures
{
    static readonly string[] CacheSubs = { @"\cache", @"\cacheddata", @"\code cache", @"\gpucache", @"\dawncache" };
    static readonly string[] BrowserSubs = { @"\cache", @"\code cache", @"\gpucache", @"\service worker" };

    public static readonly AppSig[] All =
    {
        new("npm", "dev", SigRisk.Safe, N(@"\npm-cache", @"\appdata\roaming\npm"), Note: "开发缓存，可清", Migrate: "npm_config_cache"),
        new("pip", "dev", SigRisk.Safe, N(@"\pip\cache", @"\appdata\local\pip"), Note: "开发缓存，可清", Migrate: "PIP_CACHE_DIR"),
        new("Yarn", "dev", SigRisk.Safe, N(@"\yarn\cache"), Note: "开发缓存，可清"),
        new("pnpm", "dev", SigRisk.Safe, N(@"\pnpm\store", @"\pnpm\content-v2"), Note: "开发缓存，可清"),
        new("NuGet", "dev", SigRisk.Cautious, N(@"\.nuget\packages", @"\nuget\v3-cache"), Note: "清了下次会重新下"),
        new("Cargo", "dev", SigRisk.Safe, N(@"\.cargo\registry", @"\.cargo\git"), Note: "开发缓存，可清"),
        new("Maven", "dev", SigRisk.Cautious, N(@"\.m2\repository"), Note: "清了构建会重新下"),
        new("Gradle", "dev", SigRisk.Safe, N(@"\.gradle\caches"), Note: "开发缓存，可清"),
        new("Go modules", "dev", SigRisk.Safe, N(@"\go\pkg\mod"), Note: "开发缓存，可清"),
        new("conda", "dev", SigRisk.Safe, N(@"\miniconda3\pkgs", @"\anaconda3\pkgs", @"\conda\pkgs")),
        new("node_modules", "dev", SigRisk.Cautious, N(@"\node_modules"), Note: "依赖目录，确认项目不用再删"),
        new("Python venv", "dev", SigRisk.Cautious, N(@"\.venv", @"\venv\")),
        new("Android SDK", "dev", SigRisk.Keep, N(@"\android\sdk", @"\android-sdk"), Note: "SDK，别整夹删", Migrate: "ANDROID_HOME"),
        new("VS Code 缓存", "ide", SigRisk.Safe, N(@"\appdata\roaming\code"), CacheSubs),
        new("Cursor 缓存", "ide", SigRisk.Safe, N(@"\appdata\roaming\cursor", @"\appdata\local\cursor"), CacheSubs),
        new("JetBrains", "ide", SigRisk.Cautious, N(@"\jetbrains"), Note: "旧版本缓存可清"),
        new("Chrome 缓存", "browser", SigRisk.Safe, N(@"\google\chrome\user data"), BrowserSubs),
        new("Edge 缓存", "browser", SigRisk.Safe, N(@"\microsoft\edge\user data"), BrowserSubs),
        new("Firefox 缓存", "browser", SigRisk.Safe, N(@"\mozilla\firefox"), new[] { @"\cache2", @"\offlinecache" }),
        new("微信", "im", SigRisk.Cautious, N(@"\wechat files", @"\tencent\xwechat"), new[] { @"\cache", @"\log", @"\filestorage\video", @"\filestorage\image" }, "只清缓存/日志，别动聊天记录"),
        new("QQ", "im", SigRisk.Cautious, N(@"\tencent files", @"\tencent\qq"), Note: "只清缓存，别动聊天记录"),
        new("钉钉", "im", SigRisk.Cautious, N(@"\dingtalk", @"\dinglive"), new[] { @"\cache", @"\log" }, "只清 cache/log"),
        new("飞书", "im", SigRisk.Safe, N(@"\larkshell"), CacheSubs),
        new("企业微信", "im", SigRisk.Cautious, N(@"\wxwork"), Note: "别动聊天记录"),
        new("Discord", "im", SigRisk.Safe, N(@"\discord"), CacheSubs),
        new("Teams", "im", SigRisk.Safe, N(@"\microsoft\teams"), CacheSubs),
        new("Telegram", "im", SigRisk.Safe, N(@"\telegram desktop"), new[] { @"\cache" }),
        new("WPS", "office", SigRisk.Cautious, N(@"\kingsoft\office6", @"\kingsoft\wps"), new[] { @"\cache", @"\log" }),
        new("Office 文件缓存", "office", SigRisk.Safe, N(@"\microsoft\office\officefilecache")),
        new("搜狗输入法", "ime", SigRisk.Cautious, N(@"\sogouinput", @"\sogoupy"), Note: "别动词库"),
        new("火绒", "security", SigRisk.Keep, N(@"\huorong"), Note: "杀毒数据，别删"),
        new("Windows Defender", "security", SigRisk.Cautious, N(@"\windows defender"), Note: "只清旧日志"),
        new("360", "bloat", SigRisk.Bloat, N(@"\360\360safe", @"\360\360zip", @"\360se"), Note: "可考虑卸载"),
        new("2345", "bloat", SigRisk.Bloat, N(@"\2345soft", @"\2345explorer"), Note: "建议卸载"),
        new("驱动精灵", "bloat", SigRisk.Bloat, N(@"\mydrivers\drivergenius")),
        new("快压", "bloat", SigRisk.Bloat, N(@"\kzip", @"\kuaizip")),
        new("好压", "bloat", SigRisk.Bloat, N(@"\haozip")),
        new("鲁大师", "bloat", SigRisk.Bloat, N(@"\ludashi")),
        new("Docker", "vm", SigRisk.Cautious, N(@"\docker\wsl", @"\appdata\local\docker"), Note: "可 prune 或迁盘", Migrate: "Docker Desktop 磁盘位置"),
        new("WSL", "vm", SigRisk.Keep, N(@"\lxss", @"\wsl\"), Note: "vhdx 发行版，迁走别删", Migrate: "wsl --export"),
        new("VMware", "vm", SigRisk.Keep, N(@"\virtual machines", @"\vmware"), Migrate: "挪到大盘"),
        new("VirtualBox", "vm", SigRisk.Keep, N(@"\virtualbox vms")),
        new("Steam 下载缓存", "game", SigRisk.Safe, N(@"\steamapps\downloading")),
        new("Steam 游戏库", "game", SigRisk.Keep, N(@"\steamapps\common", @"\steamlibrary"), Note: "游戏本体，可在 Steam 里迁库"),
        new("Epic", "game", SigRisk.Cautious, N(@"\epic games", @"\epic\epicgameslauncher")),
        new("网易云缓存", "media", SigRisk.Safe, N(@"\netease\cloudmusic"), new[] { @"\cache", @"\webdata" }),
        new("QQ 音乐缓存", "media", SigRisk.Safe, N(@"\qqmusic"), new[] { @"\cache" }),
        new("Spotify 缓存", "media", SigRisk.Safe, N(@"\spotify\data")),
        new("百度网盘缓存", "cloud", SigRisk.Safe, N(@"\baidunetdisk"), CacheSubs),
        new("OneDrive", "cloud", SigRisk.Cautious, N(@"\onedrive"), Note: "开按需同步可减占用"),
        new("Ollama 模型", "ai", SigRisk.Cautious, N(@"\.ollama"), Note: "删了要重新下模型", Migrate: "OLLAMA_MODELS"),
        new("Trae 缓存", "ai", SigRisk.Safe, N(@"\trae"), CacheSubs),
        new("Qoder 缓存", "ai", SigRisk.Safe, N(@"\qoder"), CacheSubs),
        new("WinSxS", "system", SigRisk.Keep, N(@"\winsxs"), Note: "别删"),
        new("Windows 更新下载", "system", SigRisk.Safe, N(@"\windows\softwaredistribution\download")),
        new("传递优化", "system", SigRisk.Safe, N(@"\deliveryoptimization")),
        new("Windows 错误报告", "system", SigRisk.Safe, N(@"\windows\wer")),
        new("缩略图缓存", "system", SigRisk.Safe, N(@"\microsoft\windows\explorer")),
        new("用户临时目录", "system", SigRisk.Safe, N(@"\appdata\local\temp")),
        new("Windows 临时目录", "system", SigRisk.Safe, N(@"\windows\temp")),
        new("回收站", "system", SigRisk.Cautious, N(@"\$recycle.bin")),
        new("休眠文件", "system", SigRisk.Keep, N(@"\hiberfil.sys"), Note: "关休眠才释放"),
        new("页面文件", "system", SigRisk.Keep, N(@"\pagefile.sys"), Note: "可迁到别的盘，别直接删"),
    };

    static string[] N(params string[] x) => x;

    public static AppHit? Match(string? path)
    {
        string p = Norm(path);
        if (p.Length == 0) return null;
        AppHit? best = null;
        foreach (var sig in All)
        {
            foreach (var n in sig.Needles)
            {
                int i = p.IndexOf(n, StringComparison.Ordinal);
                if (i < 0) continue;
                bool sub = SubHit(p, i + n.Length, sig.Subs);
                if (best == null || n.Length > best.Value.Needle.Length || (n.Length == best.Value.Needle.Length && sub && !best.Value.SubHit))
                    best = new AppHit(sig, n, sub);
            }
        }
        return best;
    }

    public static string? Describe(string? path)
    {
        var hit = Match(path);
        if (hit == null) return null;
        var s = hit.Value.Sig;
        var bits = new List<string> { s.Name, RiskWord(s.Risk) };
        if (hit.Value.SubHit) bits.Add("cache");
        if (!string.IsNullOrEmpty(s.Note)) bits.Add(s.Note);
        if (!string.IsNullOrEmpty(s.Migrate) && s.Risk is SigRisk.Cautious or SigRisk.Keep)
            bits.Add("migrate: " + s.Migrate);
        return string.Join(" · ", bits);
    }

    public static bool IsSafeCache(string? path)
    {
        var hit = Match(path);
        if (hit == null) return false;
        var s = hit.Value.Sig;
        if (s.Risk != SigRisk.Safe) return false;
        return s.Subs is not { Length: > 0 } || hit.Value.SubHit;
    }

    /// <summary>用途分类的中文显示名。</summary>
    public static string CategoryName(string? key) => (key ?? "").Trim().ToLowerInvariant() switch
    {
        "system" => Loc.CatSystem,
        "browser" => Loc.CatBrowser,
        "dev" => Loc.CatDev,
        "im" => Loc.CatChat,
        "game" => Loc.CatGame,
        "media" => Loc.CatMedia,
        "cloud" => Loc.CatCloud,
        "vm" => Loc.CatVm,
        "ide" => Loc.CatIde,
        "ai" => Loc.CatAiTool,
        "office" => Loc.CatOffice,
        "security" => Loc.CatSecurity,
        "bloat" => Loc.CatBloat,
        "ime" => Loc.CatIme,
        _ => Loc.CatOther,
    };

    /// <summary>SigRisk（4 档）压成界面上的三档风险。</summary>
    public static CleanRisk ToCleanRisk(SigRisk r) => r switch
    {
        SigRisk.Safe => CleanRisk.Safe,
        SigRisk.Keep => CleanRisk.Keep,
        _ => CleanRisk.Confirm,   // Cautious / Bloat 都要看一眼
    };

    /// <summary>
    /// 按路径识别用途分类、风险、一句说明。认不出来返回 null，由调用方兜底。
    /// </summary>
    public static (string Key, string Name, CleanRisk Risk, string Note)? Classify(string? path)
    {
        var hit = Match(path);
        if (hit == null) return null;
        var s = hit.Value.Sig;
        string key = (s.Category ?? "").Trim().ToLowerInvariant();
        var risk = ToCleanRisk(s.Risk);
        string note = s.Note ?? "";
        if (string.IsNullOrEmpty(note) && hit.Value.SubHit) note = Loc.NoteCache;
        return (key, CategoryName(key), risk, note);
    }

    public static IEnumerable<(AppSig Sig, string Sample, long Size)> HitsIn(IEnumerable<Models.FileEntry> dirs)
    {
        var map = new Dictionary<string, (AppSig Sig, string Sample, long Size)>(StringComparer.OrdinalIgnoreCase);
        foreach (var d in dirs)
        {
            var hit = Match(d.FullPath);
            if (hit == null) continue;
            if (!map.TryGetValue(hit.Value.Sig.Name, out var cur) || d.Size > cur.Size)
                map[hit.Value.Sig.Name] = (hit.Value.Sig, d.FullPath ?? "", d.Size);
        }
        return map.Values.OrderByDescending(x => x.Size);
    }

    static bool SubHit(string path, int after, string[]? subs)
    {
        if (subs is not { Length: > 0 }) return false;
        if (after >= path.Length) return false;
        foreach (var s in subs)
            if (path.IndexOf(s, after, StringComparison.Ordinal) >= 0)
                return true;
        return false;
    }

    static string RiskWord(SigRisk r) => r switch
    {
        SigRisk.Safe => "safe cache",
        SigRisk.Cautious => "confirm",
        SigRisk.Keep => "keep",
        SigRisk.Bloat => "bloatware",
        _ => "",
    };

    static string Norm(string? path)
        => (path ?? "").Replace('/', '\\').TrimEnd('\\').ToLowerInvariant();
}
