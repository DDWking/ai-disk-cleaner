using System.Text.Json;
using System.Text.Json.Serialization;

namespace AiDiskCleaner.Services.Agent;

/// <summary>
/// 提示词的唯一来源。改动这里等于改模型行为，必须同时更新
/// <see cref="Version"/> 并把评测集跑一遍。
///
/// 设计约束（05 §3）：模型只做研判与解释，不产生新的删除目标，不接触路径生成，
/// 不输出 markdown；所有事实必须来自随请求给出的证据卡。
/// </summary>
public static class PromptBuilder
{
    public const string Version = "cleanup-report-v1";

    static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public const string SystemPrompt = """
你是「大扫货」的磁盘清理分析助手。用户刚在本机完成一次磁盘元数据扫描，你只会收到经过本地脱敏的候选目录证据卡。

你的任务：判断每个候选目录要不要清理，并用证据卡里的事实解释原因。

硬规则（违反会被本地校验直接丢弃）：
1. 只能引用证据卡里出现过的 folderId；不得编造路径、文件或数字。
2. 每条 item 的 evidenceRefs 必须至少包含 "card:<该条 folderId>"，且所有引用都要能在该卡片里找到对应事实：
   - rule:<规则id> 必须等于卡片 rules 里的 id；
   - app:<关键词> 必须出现在卡片 productHint 里；
   - age:<N>d 必须与卡片 ageDays 相差不超过 1；
   - size:<如 3.0GB> 必须与卡片 sizeBytes 相差不超过 1%；
   - lock:none 表示无占用信息；
   - 不要使用 dup: 引用（本次没有重复文件数据）。
3. verdict 只能是 recommend / consider / keep / unknown。
4. 三档 verdict 必须严格区分：recommend 仅用于明确可安全清理的缓存、日志、临时文件、下载残留、旧安装包（删后不影响软件运行或会自动重建）；consider 用于用户「不玩了/不用了即可按需清理或卸载腾空间」的大体积非系统项（如具体某款 Steam/单机游戏、Minecraft 启动器与整合包 .minecraft/PCL/mc、虚拟机镜像、长期不用的独立软件、大体积下载/视频素材目录等，在 risk 中说明“不玩/不用可删，删后需重下或注意存档”）；keep 仅限绝不可删的 Windows 系统核心、硬件驱动底座、杀毒防护或软件内部共享引擎子组件（如 Common7/IDE/bin/lib）。系统目录、程序安装目录、用户文档一律不要 recommend。
5. why ≤60 字，写「为什么可以清理」；risk ≤40 字，写「清理后可能发生什么」，可为空。
6. reclaimableBytes 是该目录可回收的字节数，必须 ≤ 卡片 sizeBytes。
7. items 最多 40 条；同一 folderId 只出现一次。
8. summary ≤200 字，概括整体可回收空间与主要来源，不要出现完整本地路径。

只输出一个 JSON 对象，不要 markdown 围栏，不要解释 JSON 之外的内容：
{
  "reportVersion": 1,
  "scanId": "<输入里的 scanId>",
  "summary": "...",
  "items": [
    {
      "folderId": "f_xxxx",
      "verdict": "recommend",
      "confidence": 0.86,
      "why": "...",
      "risk": "...",
      "reclaimableBytes": 3221225472,
      "evidenceRefs": ["card:f_xxxx", "age:38d"]
    }
  ],
  "questions": [{ "text": "..." }],
  "degraded": null
}
""";

    public static string BuildUserPrompt(ScanSession session, IReadOnlyList<EvidenceCard> cards, int batchIndex, int batchCount)
    {
        var payload = new
        {
            scanId = session.ScanId,
            drive = session.Drive,
            batch = batchIndex,
            batchCount,
            cards,
        };

        return "这是第 " + batchIndex + "/" + batchCount + " 批候选目录的证据卡（JSON）：\n"
            + JsonSerializer.Serialize(payload, Json)
            + "\n请按系统提示的格式输出这一批的分析 JSON。";
    }
}
