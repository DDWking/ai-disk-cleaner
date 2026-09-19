namespace AiDiskCleaner.Services.CleanRules;

/// <summary>
/// Jev 判出来的大类。
///
/// 刻意**比 <see cref="CleanPurpose"/> 更宽**：Jev 面对的是「本地规则认不出来的那批」，
/// 那批里什么都有——可能是缓存，也可能是用户自己的照片、系统文件、模型权重。
/// 只给它「可清理」的类别，等于逼它把一张全家福塞进「应用缓存」。
/// </summary>
public enum AiPurposeKind
{
    Temporary,
    BrowserCache,
    AppCache,
    DevCache,
    AppLog,
    Dump,
    Installer,
    /// <summary>模型 / 游戏素材这类大件：删了要重新下载，不是「会自动重建」。</summary>
    Model,

    // 下面四类原来合并成一个 keep 出口。合并的**代价**后来在真机上暴露了：
    // keep 成了垃圾桶，`$WinREAgent`（更新残留）、`$Extend`（NTFS 元数据）、
    // `Recovery`（恢复环境）、`msys64`（开发工具本体）全都显示成同一句话
    // 「你的数据或软件自己的数据」—— 说了等于没说，用户直接反馈「太笼统、没什么帮助」。
    //
    // 拆开之后实测（18 条众所周知的系统/厂商目录）：
    //   $WinREAgent  keep 0.79 → system 1.00   $Extend  keep 0.85 → system 1.00
    //   Recovery     keep 0.95 → system 1.00   eSupport keep 0.75 → system 0.89
    //   msys64       keep 0.84 → devtools 0.99 Downloads keep 0.95 → userdata 0.83
    // **选项说清楚了，模型反而更确定。** 代价是每项 token 357 → 446（+25%）。

    /// <summary>用户自己的东西：文档 / 照片 / 视频 / 音乐 / 游戏存档 / 聊天记录。</summary>
    UserData,
    /// <summary>Windows 或电脑厂商自己的系统目录：恢复环境、驱动备份、NTFS 元数据、更新残留。</summary>
    SystemOwned,
    /// <summary>某个软件自己的数据：它的设置 / 账号 / 插件 / 数据库。</summary>
    AppOwned,
    /// <summary>装好的开发工具本体（编译器、运行时、SDK）。它们不是缓存，删了要重装。</summary>
    DevTools,

    /// <summary>认不出来。什么都不做。</summary>
    Unknown,
}

/// <summary>
/// 用途判定的选项表、回写映射与置信度阈值。
///
/// 三条设计约束（都有实测依据，别随手改）：
///
/// <list type="number">
/// <item><b>只留一个「别删」出口。</b>把「用户数据 / 系统文件 / 开发项目」拆成三类时，
/// 边界立刻模糊：实测同一批路径里，用户视频被判成「项目」、Windows 更新缓存的置信度
/// 从中等的 0.43 变成「系统 0.84」（更容易误导）。合并成一个出口后，
/// 视频 / 程序本体 / 游戏存档全部正确落进 keep。</item>
///
/// <item><b>选项集越小越准。</b>实测每项 token：6 类 293、9 类 232、10 类 304、
/// 13 类 337——类别越多不光更贵，准确率还下降。</item>
///
/// <item><b>置信度阈值按「判错的代价」分档。</b>说「这是缓存」说错了会误导用户去删；
/// 说「别删」说错了只是少清一个东西。所以前者阈值高、后者低。</item>
/// </list>
///
/// <b>安全不变量：这一整套结果永远不改 Risk / CanDelete / Selected。</b>
/// 回写出去的用途一律压在 <see cref="Models.EvidenceLevel.Heuristic"/>，
/// 而批量勾选要求 <see cref="Models.EvidenceLevel.Signature"/>——
/// 也就是说 **AI 推测在结构上就够不到「界面替你打勾」那道闸**，不靠提示词约束。
/// </summary>
public static class AiPurposeCriteria
{
    /// <summary>
    /// 选项键的**规范列表**（顺序即展示顺序）。
    ///
    /// 键是稳定契约：它进请求体、也是以后做结果缓存时的缓存键 —— <b>不要改</b>。
    /// 文案（双语）在 <c>Loc.AiPurposeOptions</c> 里；键放这里是为了让离线回归
    /// 不用碰 Loc 就能断言「选项表是闭合的、正好这 13 个」。
    /// </summary>
    public static readonly string[] Keys =
    {
        "temp", "browsercache", "appcache", "devcache", "applog",
        "dump", "installer", "model",
        "userdata", "system", "appdata", "devtools",
        "unknown",
    };

    /// <summary>Jev 返回的键 → 大类。认不出的键一律当 <see cref="AiPurposeKind.Unknown"/>。</summary>
    public static AiPurposeKind Parse(string? key) => (key ?? "").Trim().ToLowerInvariant() switch
    {
        "temp" => AiPurposeKind.Temporary,
        "browsercache" => AiPurposeKind.BrowserCache,
        "appcache" => AiPurposeKind.AppCache,
        "devcache" => AiPurposeKind.DevCache,
        "applog" => AiPurposeKind.AppLog,
        "dump" => AiPurposeKind.Dump,
        "installer" => AiPurposeKind.Installer,
        "model" => AiPurposeKind.Model,
        "userdata" => AiPurposeKind.UserData,
        "system" => AiPurposeKind.SystemOwned,
        "appdata" => AiPurposeKind.AppOwned,
        "devtools" => AiPurposeKind.DevTools,
        _ => AiPurposeKind.Unknown,
    };

    /// <summary>
    /// 这一类能不能落到 <see cref="CleanPurpose"/> 上。
    /// 「别删」那四类和 <see cref="AiPurposeKind.Unknown"/> 落不下去——
    /// 它们要么是「不该动」，要么是「没结论」，都不该被写成某个清理用途。
    /// </summary>
    public static bool TryToPurpose(AiPurposeKind kind, out CleanPurpose purpose)
    {
        switch (kind)
        {
            case AiPurposeKind.Temporary: purpose = CleanPurpose.Temp; return true;
            case AiPurposeKind.BrowserCache: purpose = CleanPurpose.BrowserCache; return true;
            case AiPurposeKind.AppCache: purpose = CleanPurpose.AppCache; return true;
            case AiPurposeKind.DevCache: purpose = CleanPurpose.DevCache; return true;
            case AiPurposeKind.AppLog: purpose = CleanPurpose.AppLog; return true;
            case AiPurposeKind.Dump: purpose = CleanPurpose.Dump; return true;
            case AiPurposeKind.Installer: purpose = CleanPurpose.Installer; return true;
            default: purpose = CleanPurpose.Other; return false;
        }
    }

    /// <summary>
    /// 采纳这条判定所需的最低置信度。
    ///
    /// **两侧刻意不对称 —— 拒绝猜「缓存」是保护，拒绝猜「别删」是危险。**
    ///
    /// 「别删」那四类（userdata / system / appdata / devtools）**不设门槛（0）**：
    /// 把一条拿不准的「别删」挡掉，界面就只剩「未识别」——
    /// 而「未识别」在用户眼里等于「AI 也不知道这是什么」，比一句「你的数据」危险得多。
    /// 实测证据：`AppData\Local\Programs` 猜 keep 0.39 被 0.55 挡掉 → 显示未识别 →
    /// 那可是**装软件的地方**。同一批里 `LocalLow` 被逼着猜成 temp 0.29、
    /// `Roaming\Code`（VS Code 的配置）被逼着猜成 devcache 0.27 —— 窄出口会把模型往
    /// 「猜缓存」上逼，方向正好反了。
    ///
    /// 清理类 **0.60**（原来是 0.75）—— 这个数字是**量出来的**，不是拍的：
    ///
    /// 拿 14 条「错标成缓存会很危险」的真实路径（文档 / 照片 / 游戏存档 / VS Code 设置 /
    /// 雷鸟邮箱 / qBittorrent 种子状态 / Notepad++ 备份 / 火狐书签备份 / Python 安装 /
    /// OneDrive 设置 / .ssh / 桌面 / 剪辑工程）加 14 条「模糊缓存」跑一遍，
    /// 逐阈值数两件事：危险组被误采纳几条、模糊组被救回几条。
    ///
    /// <code>
    /// 阈值   危险组误采纳   模糊组被采纳
    /// 0.75      0/14           8/14
    /// 0.65      0/14          11/14
    /// 0.60      0/14          12/14   ← 拐点
    /// 0.55      0/14          12/14
    /// 0.50      0/14          12/14
    /// </code>
    ///
    /// **危险组在任何阈值下都是 keep**（最低的 `Python311` 也有 0.55，其余 0.87~1.00），
    /// 所以放宽不会把用户数据错标成缓存；而 0.60 之后收益归零。
    /// 原来 0.75 的依据是「0.63~0.77 是认得但语焉不详」—— 数据说明
    /// **「语焉不详」的是措辞，不是答案**：`D3DSCache`(0.79)、`htmlcache`(0.60)、
    /// `OfficeFileCache`(0.68)、`Crashpad`(0.73)、`Service Worker`(0.72) 逐条看过，全是对的。
    /// 把它们扔掉才是真的损失。
    ///
    /// `Model` 留在 0.75：它不在上面这批样本里，而且那句「删了要重新下载」本身是**劝退**
    /// 方向，说错了不危险 —— 没有数据就先不动它。
    /// </summary>
    public static double ThresholdFor(AiPurposeKind kind) => kind switch
    {
        // 拿不准也照示：拒绝猜「别删」比拒绝猜「缓存」危险得多
        AiPurposeKind.UserData or AiPurposeKind.SystemOwned
            or AiPurposeKind.AppOwned or AiPurposeKind.DevTools => 0.0,
        AiPurposeKind.Temporary or AiPurposeKind.BrowserCache or AiPurposeKind.AppCache
            or AiPurposeKind.DevCache or AiPurposeKind.AppLog or AiPurposeKind.Dump
            or AiPurposeKind.Installer => 0.60,
        AiPurposeKind.Model => 0.75,
        _ => 1.0, // Unknown 本来就没结论
    };

    /// <summary>这条判定够不够格被采纳。</summary>
    public static bool IsAccepted(AiPurposeKind kind, double confidence)
        => kind != AiPurposeKind.Unknown && confidence >= ThresholdFor(kind);

    /// <summary>能不能显示成「AI 推测的用途」（这些只做展示，不回写清理用途）。</summary>
    public static bool IsInformational(AiPurposeKind kind)
        => kind is AiPurposeKind.UserData or AiPurposeKind.SystemOwned
           or AiPurposeKind.AppOwned or AiPurposeKind.DevTools or AiPurposeKind.Model;
}
