using AiDiskCleaner.Models;

namespace AiDiskCleaner.Services;

public enum SigRisk { Safe, Cautious, Keep, Bloat }

/// <summary>
/// 一条路径签名。Name 是术语（pip / npm / WinSxS），只出现在悬停提示里；
/// Plain 是给普通人看的大白话，开头先说「删了会怎样」。
/// </summary>
public sealed record AppSig(
    string Name,
    string Category,
    SigRisk Risk,
    string[] Needles,
    string[]? Subs = null,
    string? Plain = null,
    string? Migrate = null);

public readonly record struct AppHit(AppSig Sig, string Needle, bool SubHit);

public static class AppSignatures
{
    static readonly string[] CacheSubs = { @"\cache", @"\cacheddata", @"\code cache", @"\gpucache", @"\dawncache" };
    static readonly string[] BrowserSubs = { @"\cache", @"\code cache", @"\gpucache", @"\service worker" };

    public static readonly AppSig[] All =
    {
        // Safe 那批只写「这是什么」——「删了会自动重建」已经写在分组标题上了，每行再喊一遍就是噪音。
        // Cautious / Keep 那批没有默认前提，后果该说就说。
        new("npm", "dev", SigRisk.Safe, N(@"\npm-cache", @"\appdata\roaming\npm"), Plain: "你装的开发工具留下的安装包备份", Migrate: "npm_config_cache"),
        new("pip", "dev", SigRisk.Safe, N(@"\pip\cache", @"\appdata\local\pip"), Plain: "你装的开发工具留下的安装包备份", Migrate: "PIP_CACHE_DIR"),
        new("Yarn", "dev", SigRisk.Safe, N(@"\yarn\cache"), Plain: "你装的开发工具留下的安装包备份"),
        new("pnpm", "dev", SigRisk.Safe, N(@"\pnpm\store", @"\pnpm\content-v2"), Plain: "你装的开发工具留下的安装包备份"),
        new("NuGet", "dev", SigRisk.Cautious, N(@"\.nuget\packages", @"\nuget\v3-cache"), Plain: "开发工具下载的组件备份，删了下次要用时自动重下"),
        new("Cargo", "dev", SigRisk.Safe, N(@"\.cargo\registry", @"\.cargo\git"), Plain: "开发工具下载的组件备份"),
        new("Maven", "dev", SigRisk.Cautious, N(@"\.m2\repository"), Plain: "开发工具下载的组件备份，删了下次构建时自动重下"),
        new("Gradle", "dev", SigRisk.Safe, N(@"\.gradle\caches"), Plain: "开发工具下载的组件备份"),
        new("Go modules", "dev", SigRisk.Safe, N(@"\go\pkg\mod"), Plain: "开发工具下载的组件备份"),
        new("conda", "dev", SigRisk.Safe, N(@"\miniconda3\pkgs", @"\anaconda3\pkgs", @"\conda\pkgs"), Plain: "你装的开发工具留下的安装包备份"),
        new("node_modules", "dev", SigRisk.Cautious, N(@"\node_modules"), Plain: "项目用的组件都堆在这儿，删了要重新装一遍，得联网"),
        new("Python venv", "dev", SigRisk.Cautious, N(@"\.venv", @"\venv\"), Plain: "项目独立的运行环境，删了要重新装一遍项目才跑得起来"),
        new("Android SDK", "dev", SigRisk.Keep, N(@"\android\sdk", @"\android-sdk"), Plain: "做安卓开发用的工具包，删了要重装好几个 G", Migrate: "ANDROID_HOME"),
        new("VS Code 缓存", "ide", SigRisk.Safe, N(@"\appdata\roaming\code"), CacheSubs, Plain: "编辑器留下的临时文件，下次打开会慢一点"),
        new("Cursor 缓存", "ide", SigRisk.Safe, N(@"\appdata\roaming\cursor", @"\appdata\local\cursor"), CacheSubs, Plain: "编辑器留下的临时文件，下次打开会慢一点"),
        new("JetBrains", "ide", SigRisk.Cautious, N(@"\jetbrains"), Plain: "开发工具旧版本留下的缓存"),
        new("Chrome 缓存", "browser", SigRisk.Safe, N(@"\google\chrome\user data"), BrowserSubs, Plain: "浏览器缓存，网页下次打开重新加载。收藏夹和密码不受影响"),
        new("Edge 缓存", "browser", SigRisk.Safe, N(@"\microsoft\edge\user data"), BrowserSubs, Plain: "浏览器缓存，网页下次打开重新加载。收藏夹和密码不受影响"),
        new("Firefox 缓存", "browser", SigRisk.Safe, N(@"\mozilla\firefox"), new[] { @"\cache2", @"\offlinecache" }, Plain: "浏览器缓存，网页下次打开重新加载。收藏夹和密码不受影响"),
        new("微信", "im", SigRisk.Cautious, N(@"\wechat files", @"\tencent\xwechat"), new[] { @"\cache", @"\log", @"\filestorage\video", @"\filestorage\image" }, "收到的图片视频要重新下载。文字聊天记录不受影响"),
        new("QQ", "im", SigRisk.Cautious, N(@"\tencent files", @"\tencent\qq"), Plain: "缓存和日志。文字聊天记录不受影响"),
        new("钉钉", "im", SigRisk.Cautious, N(@"\dingtalk", @"\dinglive"), new[] { @"\cache", @"\log" }, "缓存和日志。聊天记录不受影响"),
        new("飞书", "im", SigRisk.Safe, N(@"\larkshell"), CacheSubs, Plain: "聊天软件留下的临时文件。聊天记录不受影响"),
        new("企业微信", "im", SigRisk.Cautious, N(@"\wxwork"), Plain: "缓存和收到的文件。聊天的文字记录别动"),
        new("Discord", "im", SigRisk.Safe, N(@"\discord"), CacheSubs, Plain: "聊天软件留下的临时文件"),
        new("Teams", "im", SigRisk.Safe, N(@"\microsoft\teams"), CacheSubs, Plain: "聊天软件留下的临时文件"),
        new("Telegram", "im", SigRisk.Safe, N(@"\telegram desktop"), new[] { @"\cache" }, Plain: "聊天软件留下的临时文件"),
        new("WPS", "office", SigRisk.Cautious, N(@"\kingsoft\office6", @"\kingsoft\wps"), new[] { @"\cache", @"\log" }, "缓存和日志。你的文档本身不在里面"),
        new("Office 文件缓存", "office", SigRisk.Safe, N(@"\microsoft\office\officefilecache"), Plain: "Office 留下的临时文件。你的文档不在里面"),
        new("搜狗输入法", "ime", SigRisk.Cautious, N(@"\sogouinput", @"\sogoupy"), Plain: "输入法的缓存，别动你攒的词库"),
        new("火绒", "security", SigRisk.Keep, N(@"\huorong"), Plain: "杀毒软件自己的数据，删了防护会出问题"),
        new("Windows Defender", "security", SigRisk.Cautious, N(@"\windows defender"), Plain: "里面只有旧日志可以清，主程序别动"),
        new("360", "bloat", SigRisk.Bloat, N(@"\360\360safe", @"\360\360zip", @"\360se"), Plain: "这类软件一般用不上，可以在「卸载」里把它卸掉"),
        new("2345", "bloat", SigRisk.Bloat, N(@"\2345soft", @"\2345explorer"), Plain: "这类软件一般用不上，可以在「卸载」里把它卸掉"),
        new("驱动精灵", "bloat", SigRisk.Bloat, N(@"\mydrivers\drivergenius"), Plain: "这类软件一般用不上，可以在「卸载」里把它卸掉"),
        new("快压", "bloat", SigRisk.Bloat, N(@"\kzip", @"\kuaizip"), Plain: "这类软件一般用不上，可以在「卸载」里把它卸掉"),
        new("好压", "bloat", SigRisk.Bloat, N(@"\haozip"), Plain: "这类软件一般用不上，可以在「卸载」里把它卸掉"),
        new("鲁大师", "bloat", SigRisk.Bloat, N(@"\ludashi"), Plain: "这类软件一般用不上，可以在「卸载」里把它卸掉"),
        new("Docker", "vm", SigRisk.Cautious, N(@"\docker\wsl", @"\appdata\local\docker"), Plain: "里面的镜像和容器是真的，删了会丢。建议搬到别的盘", Migrate: "Docker Desktop 磁盘位置"),
        new("WSL", "vm", SigRisk.Keep, N(@"\lxss", @"\wsl\"), Plain: "Linux 子系统，你的文件都在里面，别删", Migrate: "wsl --export"),
        new("VMware", "vm", SigRisk.Keep, N(@"\virtual machines", @"\vmware"), Plain: "虚拟机里的系统和文件是真的，别删，建议搬到别的盘", Migrate: "挪到大盘"),
        new("VirtualBox", "vm", SigRisk.Keep, N(@"\virtualbox vms"), Plain: "虚拟机里的系统和文件是真的，别删，建议搬到别的盘"),
        new("Steam 下载缓存", "game", SigRisk.Safe, N(@"\steamapps\downloading"), Plain: "正在下载的游戏，删了进度要重来"),
        new("Steam 游戏库", "game", SigRisk.Keep, N(@"\steamapps\common", @"\steamlibrary"), Plain: "游戏本体，删了要重新下载几十个 G"),
        new("Epic", "game", SigRisk.Cautious, N(@"\epic games", @"\epic\epicgameslauncher"), Plain: "游戏本体相关，删了要重新下载，确认一下再动"),
        new("网易云缓存", "media", SigRisk.Safe, N(@"\netease\cloudmusic"), new[] { @"\cache", @"\webdata" }, Plain: "网易云音乐听歌留下的临时文件。你的歌单和下载的歌不受影响"),
        new("QQ 音乐缓存", "media", SigRisk.Safe, N(@"\qqmusic"), new[] { @"\cache" }, Plain: "QQ 音乐听歌留下的临时文件。你下载的歌不受影响"),
        new("Spotify 缓存", "media", SigRisk.Safe, N(@"\spotify\data"), Plain: "Spotify 听歌留下的临时文件"),
        new("百度网盘缓存", "cloud", SigRisk.Safe, N(@"\baidunetdisk"), CacheSubs, Plain: "网盘客户端留下的临时文件。你网盘里的文件不受影响"),
        new("OneDrive", "cloud", SigRisk.Cautious, N(@"\onedrive"), Plain: "别直接删，打开「按需同步」就能省下空间"),
        new("Ollama 模型", "ai", SigRisk.Cautious, N(@"\.ollama"), Plain: "本地 AI 用的模型文件，很大，删了要重新下载", Migrate: "OLLAMA_MODELS"),
        new("Trae 缓存", "ai", SigRisk.Safe, N(@"\trae"), CacheSubs, Plain: "编辑器留下的临时文件"),
        new("Qoder 缓存", "ai", SigRisk.Safe, N(@"\qoder"), CacheSubs, Plain: "编辑器留下的临时文件"),
        new("WinSxS", "system", SigRisk.Keep, N(@"\winsxs"), Plain: "Windows 自己的组件库，千万别删，删了系统会坏"),
        new("Windows 更新下载", "system", SigRisk.Safe, N(@"\windows\softwaredistribution\download"), Plain: "Windows 更新下载的临时文件。已经装好的更新不受影响"),
        new("NVIDIA 驱动缓存", "system", SigRisk.Safe, N(@"\programdata\nvidia corporation\downloader", @"\programdata\nvidia corporation\nv_cache"), Plain: "显卡驱动的安装包，下次装驱动会重新下"),
        new("软件安装包缓存", "system", SigRisk.Cautious, N(@"\programdata\package cache"), Plain: "软件的安装包，以后修复或卸载可能要重新下载"),
        new("传递优化", "system", SigRisk.Safe, N(@"\deliveryoptimization"), Plain: "Windows 更新用的中转文件"),
        new("Windows 错误报告", "system", SigRisk.Safe, N(@"\windows\wer"), Plain: "程序报错时留下的记录，基本没人看"),
        new("缩略图缓存", "system", SigRisk.Safe, N(@"\microsoft\windows\explorer"), Plain: "图片的缩略图，看图片时会重新生成"),
        new("用户临时目录", "system", SigRisk.Safe, N(@"\appdata\local\temp"), Plain: "各种程序运行时的临时文件，正在用的可能删不掉"),
        new("Windows 临时目录", "system", SigRisk.Safe, N(@"\windows\temp"), Plain: "各种程序运行时的临时文件，正在用的可能删不掉"),
        new("回收站", "system", SigRisk.Cautious, N(@"\$recycle.bin"), Plain: "你之前删掉的东西，清掉就真没了"),
        new("休眠文件", "system", SigRisk.Keep, N(@"\hiberfil.sys"), Plain: "和休眠功能绑在一起，关掉休眠才会释放这块空间"),
        new("页面文件", "system", SigRisk.Keep, N(@"\pagefile.sys"), Plain: "虚拟内存，别直接删，可以搬到别的盘"),
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

    /// <summary>悬停提示用的技术细节：命中哪条签名、风险词、能不能搬盘。术语都留在这里。</summary>
    public static string? Describe(string? path)
    {
        var hit = Match(path);
        if (hit == null) return null;
        var s = hit.Value.Sig;
        var bits = new List<string> { s.Name, RiskWord(s.Risk) };
        if (hit.Value.SubHit) bits.Add("cache");
        if (!string.IsNullOrEmpty(s.Migrate) && s.Risk is SigRisk.Cautious or SigRisk.Keep)
            bits.Add("可以搬到别的盘：" + s.Migrate);
        return string.Join(" · ", bits);
    }

    /// <summary>给普通人看的大白话说明（删了会怎样）。没命中签名就返回 null。</summary>
    public static string? PlainNote(string? path) => Match(path)?.Sig.Plain;

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
    /// 按路径识别用途分类、风险、一句大白话说明。认不出来返回 null，由调用方兜底。
    /// </summary>
    public static (string Key, string Name, CleanRisk Risk, string Plain)? Classify(string? path)
    {
        var hit = Match(path);
        if (hit == null) return null;
        var s = hit.Value.Sig;
        string key = (s.Category ?? "").Trim().ToLowerInvariant();
        var risk = ToCleanRisk(s.Risk);
        string plain = s.Plain ?? "";
        if (string.IsNullOrEmpty(plain) && hit.Value.SubHit) plain = Loc.NoteCache;
        return (key, CategoryName(key), risk, plain);
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
