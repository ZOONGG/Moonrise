namespace Moonrise.Models;

public sealed class LanguagePack
{
    public int SchemaVersion { get; init; } = 1;
    public required string Code { get; init; }
    public required string Name { get; init; }
    public Dictionary<string, string> Translations { get; init; } = new(StringComparer.Ordinal);
    public override string ToString() => Name;
}

public sealed class ThemePack
{
    public int SchemaVersion { get; init; } = 1;
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string WindowBackground { get; init; }
    public required string Panel { get; init; }
    public required string PanelSecondary { get; init; }
    public required string Border { get; init; }
    public required string Muted { get; init; }
    public required string Accent { get; init; }
    public string PrimaryText { get; init; } = "#F5F7FC";
    public override string ToString() => Name;
}
