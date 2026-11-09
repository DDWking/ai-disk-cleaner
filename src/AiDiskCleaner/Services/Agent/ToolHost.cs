using System.Text.Json;
using AiDiskCleaner.Models;

namespace AiDiskCleaner.Services.Agent;

/// <summary>
/// 工具宿主：模型/上层唯一的数据出口。
///
/// 职责只有四件：白名单、参数与 scanId 校验、预算记账、把异常转成错误码。
/// 它不持有 DeletionCoordinator，也不认识 File.Delete —— 这是安全的落点。
/// </summary>
public sealed class ToolHost
{
    public ScanSession Session { get; }

    public FolderIndex Index { get; }

    public ToolBudget Budget { get; }

    private readonly Dictionary<string, IAgentTool> _tools;

    public ToolHost(
        ScanSession session,
        FolderIndex? index = null,
        ToolBudget? budget = null,
        IEnumerable<IAgentTool>? tools = null)
    {
        Session = session ?? throw new ArgumentNullException(nameof(session));
        Index = index ?? session.Index;
        Budget = budget ?? new ToolBudget();

        var list = (tools ?? DefaultTools()).ToList();
        _tools = list
            .GroupBy(t => t.Name, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
    }

    /// <summary>首版只有 4 个只读工具。新增工具必须同时进离线检查的白名单断言。</summary>
    public static IReadOnlyList<IAgentTool> DefaultTools() => new IAgentTool[]
    {
        new GetOverviewTool(),
        new ListFoldersTool(),
        new GetFolderCardTool(),
        new QueryEntriesTool(),
    };

    public IReadOnlyList<string> ToolNames => _tools.Keys.OrderBy(x => x, StringComparer.Ordinal).ToList();

    public bool HasTool(string name) => _tools.ContainsKey(name);

    /// <summary>执行一次工具调用，永远返回可直接发给模型的 JSON 字符串。</summary>
    public string Invoke(string toolName, string? argsJson)
    {
        if (string.IsNullOrWhiteSpace(toolName) || !_tools.TryGetValue(toolName, out var tool))
        {
            return ToolResult.Failure(toolName ?? "", ToolErrorCodes.InvalidArgument, "未知工具").ToJson();
        }

        if (Budget.IsExhausted)
        {
            return ToolResult.Failure(toolName, ToolErrorCodes.BudgetExceeded, "本次分析预算已耗尽，请用已有证据收尾").ToJson();
        }

        Budget.ChargeCall();

        ToolResult result;
        try
        {
            using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(argsJson) ? "{}" : argsJson);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw new AgentToolException(ToolErrorCodes.InvalidArgument, "参数必须是 JSON 对象");
            }

            result = tool.Execute(new ToolContext(Session, Index, Budget), document.RootElement);
        }
        catch (AgentToolException ex)
        {
            result = ToolResult.Failure(toolName, ex.Code, ex.Message, ex.Retryable);
        }
        catch (JsonException)
        {
            result = ToolResult.Failure(toolName, ToolErrorCodes.InvalidArgument, "参数不是合法 JSON");
        }
        catch (Exception ex)
        {
            result = ToolResult.Failure(toolName, ToolErrorCodes.Internal, ex.Message, retryable: true);
        }

        if (!result.Ok)
        {
            return result.ToJson();
        }

        int tokens = ToolJson.ApproxTokens(ToolJson.Serialize(result.Data));
        Budget.ChargeTokens(tokens);
        return (result with { ApproxTokens = tokens }).ToJson();
    }
}
