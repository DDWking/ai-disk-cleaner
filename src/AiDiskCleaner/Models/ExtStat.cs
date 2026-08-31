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
    /// <summary>0–1，给占比条当比例。</summary>
    public double PercentShare => Math.Clamp(Percent / 100.0, 0, 1);
    public Brush Color { get; set; } = Brushes.LimeGreen;
    public string SizeText => FileEntry.FormatSize(Size);
}
