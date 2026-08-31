using System.Windows.Media;

namespace AiDiskCleaner.Models;

/// <summary>当前目录下按扩展名汇总的占用。</summary>
public sealed class ExtStat
{
    public string Extension { get; set; } = "";
    public string TypeName { get; set; } = "";
    public long Size { get; set; }
    public int Count { get; set; }
    public double Percent { get; set; }
    public string PercentText { get; set; } = "";
    public double PercentBarWidth => Math.Max(Size > 0 ? 2 : 0, Percent / 100.0 * 72);
    public Brush Color { get; set; } = Brushes.LimeGreen;
    public string SizeText => FileEntry.FormatSize(Size);
}
