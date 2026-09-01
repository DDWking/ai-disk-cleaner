using System.ComponentModel;
using System.Runtime.CompilerServices;
using UninstallTools.Junk.Confidence;
using UninstallTools.Junk.Containers;

namespace AiDiskCleaner.Models;

public sealed class JunkItem : INotifyPropertyChanged
{
    private bool _selected;

    public bool Selected
    {
        get => _selected;
        set { if (_selected == value) return; _selected = value; OnPropertyChanged(); }
    }

    public string AppName { get; set; } = "";
    public string Category { get; set; } = "";
    public string Path { get; set; } = "";
    public string ConfidenceText { get; set; } = "";
    public int ConfidenceScore { get; set; }
    public bool Safe { get; set; }
    public IJunkResult? Result { get; set; }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
