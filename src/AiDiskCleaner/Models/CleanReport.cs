namespace AiDiskCleaner.Models;

public sealed class CleanReport
{
    public List<CleanItem> Cleanable { get; } = new();
    public List<CleanItem> LargeFiles { get; } = new();
    public List<CleanItem> OldFiles { get; } = new();
    public List<CleanItem> Duplicates { get; } = new();
    public List<CleanItem> EmptyFolders { get; } = new();
    public List<CleanItem> BrokenShortcuts { get; } = new();
    public List<CleanItem> LongPaths { get; } = new();
    public List<CleanItem> Compare { get; } = new();
    public string CompareNote { get; set; } = "";
    public long CleanableBytes { get; set; }
    public int DupGroupCount { get; set; }
}
