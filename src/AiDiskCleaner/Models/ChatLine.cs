using System.ComponentModel;
using AiDiskCleaner.Services;

namespace AiDiskCleaner.Models;

public sealed class ChatLine : INotifyPropertyChanged
{
    string _text = "";
    List<ChatPart> _parts = new();
    public string Who { get; set; } = "";
    public bool Log { get; set; }
    public event PropertyChangedEventHandler? PropertyChanged;
    public string Text
    {
        get => _text;
        set
        {
            if (_text == value) return;
            _text = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Text)));
        }
    }
    public List<ChatPart> Parts
    {
        get => _parts;
        set
        {
            _parts = value ?? new();
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Parts)));
        }
    }
}
