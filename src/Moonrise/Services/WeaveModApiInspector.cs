using System.IO.Compression;
using System.Text.Json;
using Moonrise.Models;

namespace Moonrise.Services;

public enum WeaveApiGeneration
{
    Unknown,
    Legacy02,
    Current,
    Mixed
}

public sealed record WeaveModApiInspection(PackageInfo Package, WeaveApiGeneration Generation)
{
    public bool IsLegacyHooksOnly { get; init; }
}

public sealed record WeaveModSetInspection(IReadOnlyList<WeaveModApiInspection> Mods)
{
    public bool HasLegacy => Mods.Any(item => item.Generation is WeaveApiGeneration.Legacy02 or WeaveApiGeneration.Mixed);
    public bool HasCurrent => Mods.Any(item => item.Generation is WeaveApiGeneration.Current or WeaveApiGeneration.Mixed);
    public bool IsLegacyOnly => HasLegacy && !HasCurrent;
}

public sealed class WeaveModApiInspector
{
    private static readonly byte[] LegacyMarker = "net/weavemc/loader/api/"u8.ToArray();
    private static readonly byte[] CurrentMarker = "net/weavemc/api/"u8.ToArray();

    public bool IsLegacyHooksOnly(PackageInfo package)
    {
        ArgumentNullException.ThrowIfNull(package);
        using (var archive = ZipFile.OpenRead(package.FullPath))
            if (!IsHooksOnly(archive)) return false;
        return Inspect(package).IsLegacyHooksOnly;
    }

    public WeaveModSetInspection Inspect(IEnumerable<PackageInfo> mods)
    {
        ArgumentNullException.ThrowIfNull(mods);
        return new WeaveModSetInspection(mods.Select(Inspect).ToArray());
    }

    public WeaveModApiInspection Inspect(PackageInfo package)
    {
        ArgumentNullException.ThrowIfNull(package);
        var path = Path.GetFullPath(package.FullPath);
        using var archive = ZipFile.OpenRead(path);
        var legacy = false;
        var current = false;

        foreach (var entry in archive.Entries.Where(item =>
                     item.FullName.EndsWith(".class", StringComparison.OrdinalIgnoreCase)))
        {
            legacy |= Contains(entry, LegacyMarker);
            current |= Contains(entry, CurrentMarker);
            if (legacy && current) break;
        }

        if (!legacy && !current)
            InferFromMetadata(archive, ref legacy, ref current);

        var generation = (legacy, current) switch
        {
            (true, true) => WeaveApiGeneration.Mixed,
            (true, false) => WeaveApiGeneration.Legacy02,
            (false, true) => WeaveApiGeneration.Current,
            _ => WeaveApiGeneration.Unknown
        };
        if (generation == WeaveApiGeneration.Current)
        {
            var contract = Moonrise.Compatibility.PackageInspector.Inspect(path);
            var errors = contract.Issues.Where(issue => issue.Severity == Moonrise.Compatibility.Severity.Error).ToArray();
            if (errors.Length != 0)
                throw new InvalidDataException(string.Join("; ", errors.Select(issue => issue.Code + ": " + issue.Message)));
        }
        return new WeaveModApiInspection(package, generation)
        {
            IsLegacyHooksOnly = generation == WeaveApiGeneration.Legacy02 && IsHooksOnly(archive)
        };
    }

    private static bool IsHooksOnly(ZipArchive archive)
    {
        if (archive.GetEntry("weave.mod.json") is not { } metadata) return false;
        using var reader = new StreamReader(metadata.Open());
        using var document = JsonDocument.Parse(reader.ReadToEnd());
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("hooks", out var hooks) || hooks.ValueKind != JsonValueKind.Array ||
            hooks.GetArrayLength() == 0) return false;
        foreach (var key in new[] { "entrypoints", "entryPoints", "mixinConfigs", "mixins", "tweakers", "accessWideners" })
            if (root.TryGetProperty(key, out var declarations) &&
                (declarations.ValueKind != JsonValueKind.Array || declarations.GetArrayLength() != 0)) return false;
        foreach (var hook in hooks.EnumerateArray())
            if (hook.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(hook.GetString()) ||
                archive.GetEntry(hook.GetString()!.Replace('.', '/') + ".class") is null) return false;
        return true;
    }

    private static bool Contains(ZipArchiveEntry entry, byte[] marker)
    {
        if (entry.Length < marker.Length || entry.Length > int.MaxValue) return false;
        using var stream = entry.Open();
        using var memory = new MemoryStream(checked((int)entry.Length));
        stream.CopyTo(memory);
        return memory.GetBuffer().AsSpan(0, checked((int)memory.Length)).IndexOf(marker) >= 0;
    }

    private static void InferFromMetadata(ZipArchive archive, ref bool legacy, ref bool current)
    {
        var metadata = archive.Entries.FirstOrDefault(item =>
            string.Equals(item.FullName, "weave.mod.json", StringComparison.OrdinalIgnoreCase));
        if (metadata is null) return;

        using var reader = new StreamReader(metadata.Open());
        using var document = JsonDocument.Parse(reader.ReadToEnd());
        if (document.RootElement.ValueKind != JsonValueKind.Object) return;

        legacy = document.RootElement.TryGetProperty("entrypoints", out _);
        current = document.RootElement.TryGetProperty("entryPoints", out _) ||
                  document.RootElement.TryGetProperty("modId", out _);
    }
}
