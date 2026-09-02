using AiDiskCleaner.Services;

namespace AiDiskCleaner.Models;

public sealed class ChatLine
{
    public string Who { get; set; } = "";
    public string Text { get; set; } = "";
    public bool Log { get; set; }
    public List<ChatPart> Parts { get; set; } = new();
}
