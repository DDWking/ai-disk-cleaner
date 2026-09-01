using System.ComponentModel;
using System.Runtime.CompilerServices;
using UninstallTools;

namespace AiDiskCleaner.Models;

public sealed class AppUninstallItem : INotifyPropertyChanged
{
    private bool _selected;
    private string _status = "";

    public bool Selected
    {
        get => _selected;
        set { if (_selected == value) return; _selected = value; OnPropertyChanged(); }
    }

    public string Status
    {
        get => _status;
        set { if (_status == value) return; _status = value; OnPropertyChanged(); }
    }

    public string Name { get; set; } = "";
    public string Publisher { get; set; } = "";
    public string Version { get; set; } = "";
    public long SizeBytes { get; set; }
    public string InstallLocation { get; set; } = "";
    public bool CanUninstall { get; set; }
    public bool IsProtected { get; set; }
    public ApplicationUninstallerEntry? Entry { get; set; }

    public string SizeText => SizeBytes <= 0 ? "—" : FileEntry.FormatSize(SizeBytes);

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
