using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using AiDiskCleaner.Models;

namespace AiDiskCleaner.Services.Agent;

/// <summary>工具错误码。模型看到的是这些字符串，不是异常。</summary>
public static class ToolErrorCodes
{
    public const string InvalidArgument = "INVALID_ARGUMENT";
    public const string ScanNotFound = "SCAN_NOT_FOUND";
    public const string FolderNotFound = "FOLDER_NOT_FOUND";
    public const string OutOfScope = "OUT_OF_SCOPE";
    public const string Disabled = "DISABLED";
    public const string BudgetExceeded = "BUDGET_EXCEEDED";
    public const string StaleSession = "STALE_SESSION";
    public const string Timeout = "TIMEOUT";
    public const string Internal = "INTERNAL";
}

public sealed record ToolError(string Code, string Message, bool Retryable);

/// <summary>
/// 统一返回信封。所有工具都只返回它，绝不抛异常给模型。
/// 成功：{ ok, tool, scanId, truncated, nextCursor?, approxTokens, data }
/// 失败：{ ok, tool, error: { code, message, retryable } }
/// </summary>
public sealed record ToolResult(
    bool Ok,
    string Tool,
    string? ScanId,
    bool? Truncated,
    string? NextCursor,
    int? ApproxTokens,
    object? Data,
    ToolError? Error)
{
    public static ToolResult Success(string tool, string scanId, object? data, bool truncated = false, string? nextCursor = null)
        => new(true, tool, scanId, truncated, nextCursor, null, data, null);

    public static ToolResult Failure(string tool, string code, string message, bool retryable = false)
        => new(false, tool, null, null, null, null, null, new ToolError(code, message, retryable));

    public string ToJson() => ToolJson.Serialize(this);
}

public static class ToolJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = false,
    };

    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);

    /// <summary>保守估算：UTF8 字节 / 3，向上取整（CJK 会低估，预算已留余量）。</summary>
    public static int ApproxTokens(string? json)
        => string.IsNullOrEmpty(json) ? 0 : (Encoding.UTF8.GetByteCount(json) + 2) / 3;
}

/// <summary>工具内部用它表达「可预期的坏输入」，由 ToolHost 转成错误码。</summary>
public sealed class AgentToolException : Exception
{
    public string Code { get; }
    public bool Retryable { get; }

    public AgentToolException(string code, string message, bool retryable = false) : base(message)
    {
        Code = code;
        Retryable = retryable;
    }
}

/// <summary>一次分析的预算账本。预算耗尽后只允许收尾。</summary>
public sealed class ToolBudget
{
    public int MaxCalls { get; init; } = 24;

    public int MaxInputTokens { get; init; } = 50_000;

    public int CallsUsed { get; private set; }

    public int TokensUsed { get; private set; }

    public bool IsExhausted => CallsUsed >= MaxCalls || TokensUsed >= MaxInputTokens;

    internal void ChargeCall() => CallsUsed++;

    internal void ChargeTokens(int tokens) => TokensUsed += Math.Max(0, tokens);
}

/// <summary>工具拿到的上下文：只读会话 + 索引 + 预算，没有文件系统句柄。</summary>
public sealed class ToolContext
{
    public ScanSession Session { get; }

    public FolderIndex Index { get; }

    public ToolBudget Budget { get; }

    public ToolContext(ScanSession session, FolderIndex index, ToolBudget budget)
    {
        Session = session;
        Index = index;
        Budget = budget;
    }

    /// <summary>scanId 必须存在且等于当前会话；否则 SCAN_NOT_FOUND。</summary>
    public void RequireScanId(JsonElement args)
    {
        string scanId = ToolArgs.RequireString(args, "scanId");
        if (!string.Equals(scanId, Session.ScanId, StringComparison.Ordinal))
        {
            throw new AgentToolException(ToolErrorCodes.ScanNotFound, "scanId 不存在或已过期");
        }
    }

    /// <summary>把 folderId 解析回目录；格式不对是 INVALID_ARGUMENT，格式对但不存在是 FOLDER_NOT_FOUND。</summary>
    public FileEntry RequireFolder(string? folderId)
    {
        if (!FolderIndex.LooksLikeFolderId(folderId))
        {
            throw new AgentToolException(ToolErrorCodes.InvalidArgument, "folderId 格式不合法（应形如 f_0123456789ab）");
        }

        if (!Index.TryGet(folderId, out var entry))
        {
            throw new AgentToolException(ToolErrorCodes.FolderNotFound, "folderId 不在当前 scanId 的索引中");
        }

        return entry;
    }
}

/// <summary>模型可调用的一只手。名字 snake_case，一经发布不改名。</summary>
public interface IAgentTool
{
    string Name { get; }

    string Description { get; }

    ToolResult Execute(ToolContext context, JsonElement args);
}

/// <summary>参数读取：所有错误统一抛 INVALID_ARGUMENT，不读环境、不看当前时间。</summary>
public static class ToolArgs
{
    public static string RequireString(JsonElement args, string name)
    {
        if (!TryGet(args, name, out var value) || value.ValueKind != JsonValueKind.String)
        {
            throw new AgentToolException(ToolErrorCodes.InvalidArgument, $"缺少字符串参数 {name}");
        }

        string? text = value.GetString();
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new AgentToolException(ToolErrorCodes.InvalidArgument, $"参数 {name} 不能为空");
        }

        return text;
    }

    public static string? OptionalString(JsonElement args, string name)
    {
        if (!TryGet(args, name, out var value) || value.ValueKind == JsonValueKind.Null) return null;
        if (value.ValueKind != JsonValueKind.String)
        {
            throw new AgentToolException(ToolErrorCodes.InvalidArgument, $"参数 {name} 必须是字符串");
        }

        return value.GetString();
    }

    public static int? OptionalInt(JsonElement args, string name, int min, int max)
    {
        if (!TryGet(args, name, out var value) || value.ValueKind == JsonValueKind.Null) return null;
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out int number))
        {
            throw new AgentToolException(ToolErrorCodes.InvalidArgument, $"参数 {name} 必须是整数");
        }

        if (number < min || number > max)
        {
            throw new AgentToolException(ToolErrorCodes.InvalidArgument, $"参数 {name} 必须在 {min}..{max} 之间");
        }

        return number;
    }

    public static long? OptionalLong(JsonElement args, string name, long min, long max)
    {
        if (!TryGet(args, name, out var value) || value.ValueKind == JsonValueKind.Null) return null;
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt64(out long number))
        {
            throw new AgentToolException(ToolErrorCodes.InvalidArgument, $"参数 {name} 必须是整数");
        }

        if (number < min || number > max)
        {
            throw new AgentToolException(ToolErrorCodes.InvalidArgument, $"参数 {name} 必须在 {min}..{max} 之间");
        }

        return number;
    }

    public static bool? OptionalBool(JsonElement args, string name)
    {
        if (!TryGet(args, name, out var value) || value.ValueKind == JsonValueKind.Null) return null;
        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => throw new AgentToolException(ToolErrorCodes.InvalidArgument, $"参数 {name} 必须是布尔值"),
        };
    }

    public static JsonElement? OptionalObject(JsonElement args, string name)
    {
        if (!TryGet(args, name, out var value) || value.ValueKind == JsonValueKind.Null) return null;
        if (value.ValueKind != JsonValueKind.Object)
        {
            throw new AgentToolException(ToolErrorCodes.InvalidArgument, $"参数 {name} 必须是对象");
        }

        return value;
    }

    public static IReadOnlyList<string> OptionalStringArray(JsonElement args, string name, int maxItems)
    {
        if (!TryGet(args, name, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            return Array.Empty<string>();
        }

        if (value.ValueKind != JsonValueKind.Array)
        {
            throw new AgentToolException(ToolErrorCodes.InvalidArgument, $"参数 {name} 必须是数组");
        }

        var list = new List<string>();
        foreach (var item in value.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String)
            {
                throw new AgentToolException(ToolErrorCodes.InvalidArgument, $"参数 {name} 的每一项都必须是字符串");
            }

            list.Add(item.GetString() ?? "");
        }

        if (list.Count > maxItems)
        {
            throw new AgentToolException(ToolErrorCodes.InvalidArgument, $"参数 {name} 最多 {maxItems} 项");
        }

        return list;
    }

    /// <summary>自造参数一律拒绝：未知键直接 INVALID_ARGUMENT。</summary>
    public static void RejectUnknownKeys(JsonElement obj, params string[] allowed)
    {
        var allowedSet = new HashSet<string>(allowed, StringComparer.Ordinal);
        foreach (var property in obj.EnumerateObject())
        {
            if (!allowedSet.Contains(property.Name))
            {
                throw new AgentToolException(ToolErrorCodes.InvalidArgument, $"未知参数 {property.Name}");
            }
        }
    }

    private static bool TryGet(JsonElement args, string name, out JsonElement value)
    {
        if (args.ValueKind == JsonValueKind.Object && args.TryGetProperty(name, out value)) return true;
        value = default;
        return false;
    }
}
