using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Moonrise.Models;

public enum ToastKind { Info, Success, Warning, Error }

public sealed class ToastMessage : INotifyPropertyChanged
{
    private bool _isClosing;
    public Guid Id { get; } = Guid.NewGuid();
    public required ToastKind Kind { get; init; }
    public required string Title { get; init; }
    public string Message { get; init; } = string.Empty;
    public string Glyph => Kind switch { ToastKind.Success => "✓", ToastKind.Warning => "!", ToastKind.Error => "×", _ => "i" };
    public string SemanticBrushKey => Kind switch { ToastKind.Success => "Success", ToastKind.Warning => "Warning", ToastKind.Error => "Danger", _ => "AccentCyan" };
    public bool IsClosing { get => _isClosing; set { if (_isClosing == value) return; _isClosing = value; OnPropertyChanged(); } }
    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
