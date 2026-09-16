namespace Moonrise.Models;

public sealed record DeveloperInspection(
    string FileName,
    PackageKind Kind,
    string Sha256,
    long FileSize,
    int? JavaClassVersion,
    string Manifest,
    string? WeaveMetadata,
    IReadOnlyList<string> EntryPoints,
    IReadOnlyList<string> MixinConfigs,
    IReadOnlyList<string> Hooks,
    IReadOnlyList<string> Classes);
