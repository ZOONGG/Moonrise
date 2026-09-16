using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Moonrise.Models;

public sealed class PackageInfo : INotifyPropertyChanged
{
    private bool _isEnabled = true;
    private PackageKind _kind;

    public string PackageId { get; init; } = string.Empty;
    public required string FileName { get; init; }
    public string OriginalFileName { get; init; } = string.Empty;
    public required string DisplayName { get; init; }
    public required string Identifier { get; init; }
    public required string FullPath { get; init; }
    public required PackageKind Kind
    {
        get => _kind;
        set
        {
            if (_kind == value) return;
            _kind = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(TypeLabel));
            OnPropertyChanged(nameof(CanEnable));
        }
    }
    public string Version { get; init; } = "—";
    public string Entrypoint { get; init; } = "—";
    public long Size { get; init; }
    public string Sha256 { get; init; } = string.Empty;
    public string SourceType { get; init; } = "manual";
    public string? SourceUrl { get; init; }
    public string CompatibilityStatus { get; init; } = "untested";
    public DateTimeOffset ImportedAtUtc { get; init; } = DateTimeOffset.UtcNow;
    public string LastIntegrityCheckResult { get; set; } = "not-checked";
    public IReadOnlyList<string> CompatibleGameVersions { get; init; } = [];
    public IReadOnlyList<string> Conflicts { get; init; } = [];
    public IReadOnlyList<string> Requires { get; init; } = [];

    public bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            if (_isEnabled == value) return;
            _isEnabled = value;
            OnPropertyChanged();
        }
    }

    public bool CanEnable => Kind is PackageKind.WeaveMod or PackageKind.JavaAgent;
    public string TypeLabel => Kind switch
    {
        PackageKind.WeaveMod => "Weave mod",
        PackageKind.JavaAgent => "Java agent",
        PackageKind.Ambiguous => "Ambiguous",
        _ => "Unclassified"
    };

    public string SizeLabel => Size < 1024 * 1024
        ? $"{Size / 1024d:0.#} KB"
        : $"{Size / 1024d / 1024d:0.#} MB";

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
