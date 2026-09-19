using System.IO;
using System.Text.Json;
using AiDiskCleaner.Models;

namespace AiDiskCleaner.Services;

/// <summary>
/// 识别结果落盘缓存（逐项 AI + 目录用途共用一份）。目标很具体：
/// **下次打开应用时，上次认出来的东西还在，而且不需要再发任何 AI 请求。**
///
/// 安全与稳健约束（都是硬约束，不是建议）：
/// <list type="bullet">
/// <item>**只存描述性文本**：身份（规范化路径）、用途 / 影响 / 依据（证据）/ 缺少的信息、
///       以及展示结论必需的类别 / 目录性质 / 来源 / 是否待确认。
///       **不存**模型原始回复、判定（verdict）、勾选状态、删除授权 —— 恢复出来的东西
///       永远不能变成「可删除 / 已选中」的能力。</item>
/// <item>**键是确定性的**：规范化路径 + 内容指纹 + 版本 + 配置签名（由调用方算好）。
///       绝不使用进程内随机的 <see cref="object.GetHashCode"/>，也不含扫描代次。</item>
/// <item>**离线**：load / store 都是纯文件操作，绝不触发任何出站请求。</item>
/// <item>**原子**：先写同目录临时文件，再原子替换；写失败只记日志。</item>
/// <item>**有界**：条数、单字段长度、读取文件大小都有上限，超出按时间淘汰最旧。</item>
/// <item>**损坏容忍**：文件不是合法 JSON / 结构不对 / 超限时当成空库，绝不让启动失败。</item>
/// <item>**无密钥**：写入前统一脱敏（密钥 / Token / 用户路径），落盘内容再自检一遍。</item>
/// </list>
///
/// 位置由 <see cref="AppPaths.ConfigDirectory"/> 决定（可用 DASHAOHUO_CONFIG_DIR 或
/// <see cref="AppPaths.OverrideDirectory"/> 隔离），因此不同配置/档案天然互不串扰；
/// 测试可以直接注入一个临时文件路径，绝不碰真实用户配置。
/// </summary>
public sealed class RecognitionStore : IPurposeRecognitionStore
{
    /// <summary>落盘结构版本。字段语义变了就 +1，旧文件按损坏处理（重新累积）。</summary>
    public const int SchemaVersion = 1;

    /// <summary>最多保留多少条（超出按写入时间淘汰最旧）。</summary>
    public const int MaxEntries = 400;

    /// <summary>单个文本字段的字符上限（防止一份记录撑大文件）。</summary>
    public const int MaxFieldChars = 512;

    /// <summary>读取文件大小上限；超过就当损坏，不解析。</summary>
    public const long MaxFileBytes = 4L * 1024 * 1024;

    /// <summary>配置目录下的文件名。</summary>
    public const string FileName = "recognition-cache.json";

    /// <summary>默认落盘位置：跟随当前配置目录（跨档案隔离）。</summary>
    public static string DefaultPath => System.IO.Path.Combine(AppPaths.ConfigDirectory, FileName);

    // ---------------- 落盘结构 ----------------

    sealed class Entry
    {
        /// <summary>
        /// 条目种类。现在**只剩 "purpose"** —— 逐项 AI 那半套删掉之后 "item" 不再写入。
        /// 字段本身保留：旧缓存里那些 "item" 条目会被自然跳过，不会当成用途读出来。
        /// </summary>
        public string Kind { get; set; } = "";
        public string Key { get; set; } = "";         // 确定性键（十六进制）
        public string Path { get; set; } = "";        // 规范化身份路径（诊断/排查用）
        public string Purpose { get; set; } = "";
        public string Basis { get; set; } = "";       // 「证据 / 依据」
        public string Category { get; set; } = "";
        public int FolderKind { get; set; }           // FolderKind（仅 purpose）
        public int Source { get; set; }               // PurposeSource（仅 purpose）
        public bool NeedsConfirm { get; set; }
        /// <summary>写入序号（确定性淘汰顺序；不依赖同一毫秒内的时间比较）。</summary>
        public long Seq { get; set; }
        public long StoredAtUnixMs { get; set; }
    }

    sealed class Document
    {
        public int Version { get; set; } = SchemaVersion;
        public List<Entry> Entries { get; set; } = new();
    }

    static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };

    readonly string _path;
    readonly object _lock = new();
    /// <summary>写盘串行锁：识别是有限并发（两个工作线程），不能让两次保存抢同一个临时文件。</summary>
    readonly object _saveLock = new();
    readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    long _seq;

    public RecognitionStore(string? path = null)
    {
        _path = string.IsNullOrWhiteSpace(path) ? DefaultPath : path!;
        Load();
    }

    /// <summary>这份缓存实际用的文件路径（测试/诊断用）。</summary>
    public string Path => _path;

    /// <summary>当前内存中的有效条数。</summary>
    public int Count { get { lock (_lock) return _entries.Count; } }

    public int PurposeCount
    {
        get { lock (_lock) return _entries.Values.Count(e => e.Kind == "purpose"); }
    }

    static string MapKey(string kind, string key) => kind + "\u0001" + key;

    // ---------------- 目录用途 ----------------

    public bool TryGetPurpose(string key, FolderId currentId, out FolderPurposeResult result)
    {
        result = FolderPurposeResult.None(currentId);
        if (string.IsNullOrEmpty(key)) return false;

        Entry? e;
        lock (_lock) _entries.TryGetValue(MapKey("purpose", key), out e);
        if (e == null || e.Purpose.Length == 0) return false;

        var kind = Enum.IsDefined(typeof(FolderKind), e.FolderKind)
            ? (FolderKind)e.FolderKind : FolderKind.Unknown;
        var source = Enum.IsDefined(typeof(PurposeSource), e.Source)
            ? (PurposeSource)e.Source : PurposeSource.None;

        // 用**当前会话**的 FolderId 重新挂载：落盘键里没有扫描代次。
        result = new FolderPurposeResult(currentId, e.Purpose, e.Category, e.Basis,
            source, e.NeedsConfirm, kind);
        return true;
    }

    public void PutPurpose(string key, FolderPurposeResult result)
    {
        if (string.IsNullOrEmpty(key) || result == null || !result.HasConclusion) return;

        string purpose = Clean(result.PurposeName);
        if (purpose.Length == 0) return;

        Upsert(new Entry
        {
            Kind = "purpose",
            Key = Clip(key, 128),
            Path = Clean(result.Id.Path),   // 脱敏后的身份路径，仅供排查
            Purpose = purpose,
            Category = Clean(result.Category),
            Basis = Clean(result.Basis),
            FolderKind = (int)result.Kind,
            Source = (int)result.Source,
            NeedsConfirm = result.NeedsConfirm,
        });
    }

    // ---------------- 写路径 ----------------

    void Upsert(Entry e)
    {
        lock (_lock)
        {
            e.Seq = ++_seq;
            e.StoredAtUnixMs = NowMs();
            _entries[MapKey(e.Kind, e.Key)] = e;
            TrimLocked();
        }
        Save();
    }

    /// <summary>按写入序号淘汰最旧，保证条数有上限（同一批写入也是确定性的）。</summary>
    void TrimLocked()
    {
        if (_entries.Count <= MaxEntries) return;
        foreach (var k in _entries.OrderBy(kv => kv.Value.Seq)
                     .Take(_entries.Count - MaxEntries).Select(kv => kv.Key).ToList())
            _entries.Remove(k);
    }

    /// <summary>原子写：临时文件 + 替换。任何失败都只记日志，绝不影响识别主流程。</summary>
    void Save()
    {
        // 串行化：先把最新快照拍下来，再整份替换；并发写入不会互相抢临时文件。
        lock (_saveLock)
        {
            string tmp = _path + ".tmp";
            try
            {
                Document doc;
                lock (_lock)
                {
                    TrimLocked();
                    doc = new Document
                    {
                        Version = SchemaVersion,
                        Entries = _entries.Values
                            .OrderByDescending(x => x.Seq)
                            .ToList(),
                    };
                }

                string? dir = System.IO.Path.GetDirectoryName(_path);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

                File.WriteAllText(tmp, JsonSerializer.Serialize(doc, JsonOptions));
                File.Move(tmp, _path, overwrite: true);
            }
            catch (Exception ex)
            {
                AppLog.Record("Recognition", ex, "save recognition cache");
                TryDelete(tmp);
            }
        }
    }

    /// <summary>
    /// 读盘。**绝不因为一份坏缓存打断启动**：解析失败 / 结构不对 / 超限一律当空库，
    /// 下一次写入会自然覆盖掉坏文件。
    /// </summary>
    void Load()
    {
        try
        {
            if (!File.Exists(_path)) return;
            var fi = new FileInfo(_path);
            if (fi.Length <= 0 || fi.Length > MaxFileBytes)
            {
                AppLog.Info("Recognition", $"op=persist stage=load skip=size bytes={fi.Length}");
                return;
            }

            var doc = JsonSerializer.Deserialize<Document>(File.ReadAllText(_path), JsonOptions);
            if (doc == null || doc.Version != SchemaVersion || doc.Entries is not { Count: > 0 }) return;

            lock (_lock)
            {
                foreach (var e in doc.Entries)
                {
                    if (!IsValid(e)) continue;
                    _entries[MapKey(e.Kind, e.Key)] = e;
                    if (e.Seq > _seq) _seq = e.Seq;
                }
                TrimLocked();
            }
        }
        catch (Exception ex)
        {
            AppLog.Record("Recognition", ex, "load recognition cache");
            lock (_lock) _entries.Clear();
        }
    }

    /// <summary>逐条校验：手改过的坏记录只跳过这一条，不拖垮整个库。</summary>
    static bool IsValid(Entry? e)
    {
        if (e == null) return false;
        // 旧缓存里还可能有 "item" 记录（逐项 AI 那半套的遗留）：**当无效跳过**，
        // 不当作用途读出来，也不影响其余条目。
        if (e.Kind != "purpose") return false;
        if (string.IsNullOrWhiteSpace(e.Key) || e.Key.Length > 128) return false;
        if (TooLong(e.Purpose) || TooLong(e.Basis) || TooLong(e.Category) || TooLong(e.Path)) return false;
        return e.Purpose.Length > 0;   // 没结论的用途记录没有意义
    }

    static bool TooLong(string? s) => s != null && s.Length > MaxFieldChars;

    // ---------------- 文本与路径清理 ----------------

    /// <summary>
    /// 落盘前的统一脱敏：密钥 / Token / 密码 / 连接串 / 用户路径都换成占位符。
    /// 脱敏后仍像密钥的就丢弃，并限制长度。**绝不落原始模型回复**（调用方根本没传进来）。
    /// </summary>
    static string Clean(string? text)
    {
        string t = text ?? "";
        if (t.Length == 0) return "";
        try { t = SourceSnippetReader.Redact(LogRedactor.Scrub(t), out _); }
        catch { return ""; }
        t = t.Trim();
        if (t.Length == 0) return "";
        try { if (LogRedactor.LooksSecret(t)) return ""; } catch { /* 自检失败就当安全 */ }
        return Clip(t, MaxFieldChars);
    }

    static string Clip(string? s, int max)
    {
        string t = s ?? "";
        return t.Length <= max ? t : t[..max];
    }

    static long NowMs() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { /* 清理失败无所谓 */ }
    }

    /// <summary>测试/诊断：清空内存并删除落盘文件。</summary>
    public void Clear()
    {
        lock (_lock) _entries.Clear();
        TryDelete(_path);
    }
}
