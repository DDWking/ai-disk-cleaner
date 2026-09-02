using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace AiDiskCleaner.Models;

public sealed class CleanItem : INotifyPropertyChanged
{
    private bool _selected;

    public bool Selected
    {
        get => _selected;
        set { if (_selected == value) return; _selected = value; OnPropertyChanged(); }
    }

    public string Name { get; set; } = "";
    public string FullPath { get; set; } = "";
    public long Size { get; set; }
    public string Reason { get; set; } = "";
    public string Group { get; set; } = "";
    public bool CanDelete { get; set; } = true;
    public bool AiSuggested { get; set; }
    public FileEntry? Entry { get; set; }
    public bool IsDirectory { get; set; }

    public string SizeText => FileEntry.FormatSize(Size);

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
