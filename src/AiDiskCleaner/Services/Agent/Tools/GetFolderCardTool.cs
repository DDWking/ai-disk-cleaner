using System.Text.Json;

namespace AiDiskCleaner.Services.Agent;

/// <summary>get_folder_card：单目录证据卡。L1 的大部分 token 都花在这个工具上。</summary>
public sealed class GetFolderCardTool : IAgentTool
{
    public string Name => "get_folder_card";

    public string Description => "取单个目录的证据卡";

    public ToolResult Execute(ToolContext context, JsonElement args)
    {
        ToolArgs.RejectUnknownKeys(args, "scanId", "folderId");
        context.RequireScanId(args);

        string folderId = ToolArgs.RequireString(args, "folderId");
        var folder = context.RequireFolder(folderId);

        // 保护路径连证据卡都不出：避免模型围绕系统目录编建议。
        if (ProtectedPaths.IsCleanupBlocked(folder))
        {
            throw new AgentToolException(ToolErrorCodes.OutOfScope, "该目录受保护，不参与清理分析");
        }

        var card = EvidenceCardBuilder.Build(context.Session, context.Index, folder);
        return ToolResult.Success(Name, context.Session.ScanId, card, truncated: card.Truncated);
    }
}
