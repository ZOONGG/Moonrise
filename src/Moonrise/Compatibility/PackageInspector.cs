using System.Collections.Immutable;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
namespace Moonrise.Compatibility;

/// <summary>Read-only, bounded archive inspection. Never loads imported bytecode.</summary>
public static class PackageInspector
{
    private const int MaxEntry = 2 * 1024 * 1024;
    private const int MaxClasses = 4096;
    public static PackageInspection Inspect(string path)
    {
        var types = ImmutableArray.CreateBuilder<PackageType>();
        var metadata = ImmutableSortedDictionary.CreateBuilder<string, string>(StringComparer.Ordinal);
        var evidence = ImmutableArray.CreateBuilder<Evidence>();
        var issues = ImmutableArray.CreateBuilder<Issue>();
        var unknown = ImmutableArray.CreateBuilder<string>();
        var deps = ImmutableArray.CreateBuilder<Dependency>();
        string? mc = null, loader = null, weaveVersion = null, loaderVersion = null;
        int? java = null;
        var generation = WeaveGeneration.Unknown;
        void Warn(string code, string message) => issues.Add(new(code, Severity.Warning, message, path));
        string Read(ZipArchiveEntry entry)
        {
            if (entry.Length > MaxEntry) throw new InvalidDataException("Metadata/class entry exceeds inspection limit.");
            using var stream = entry.Open();
            using var reader = new StreamReader(stream, Encoding.UTF8);
            return reader.ReadToEnd();
        }
        try
        {
            using var archive = ZipFile.OpenRead(path);
            if (archive.Entries.Count > 20000) throw new InvalidDataException("Archive exceeds entry limit.");
            if (archive.Entries.Count == 0) throw new InvalidDataException("Empty archive.");
            if (archive.Entries.GroupBy(e => e.FullName, StringComparer.Ordinal).Any(g => g.Count() > 1))
                throw new InvalidDataException("Duplicate archive entry names.");
            var manifest = archive.GetEntry("META-INF/MANIFEST.MF");
            if (manifest != null)
            {
                // Continuations belong to the preceding attribute; only the main section is authoritative.
                var lines = Read(manifest).Replace("\r\n", "\n").Replace("\n ", "").Split('\n');
                var attrs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var line in lines)
                {
                    if (line.Length == 0) break;
                    var colon = line.IndexOf(':');
                    if (colon > 0) attrs[line[..colon]] = line[(colon + 1)..].Trim();
                }
                foreach (var key in new[] { "Premain-Class", "Agent-Class", "Can-Redefine-Classes", "Can-Retransform-Classes", "Boot-Class-Path", "Implementation-Version" })
                    if (attrs.TryGetValue(key, out var value)) metadata[key] = value;
                if (attrs.TryGetValue("Premain-Class", out var premain))
                {
                    types.Add(PackageType.JavaAgent);
                    evidence.Add(new("META-INF/MANIFEST.MF", "Premain-Class declaration"));
                    if (!Regex.IsMatch(premain, @"^[\w$]+(\.[\w$]+)*$", RegexOptions.CultureInvariant) || archive.GetEntry(premain.Replace('.', '/') + ".class") == null)
                        issues.Add(new("agent.premain.target", Severity.Error, "Declared Premain-Class is absent or invalid.", path));
                }
                else if (attrs.ContainsKey("Agent-Class"))
                    Warn("agent.attach.only", "Agent-Class alone does not support startup -javaagent.");
            }
            if (archive.GetEntry("weave.mod.json") is { } weave)
            {
                types.Add(PackageType.WeaveMod);
                evidence.Add(new("weave.mod.json", "Weave metadata marker"));
                using var doc = JsonDocument.Parse(Read(weave));
                var root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Weave metadata must be an object.");
                foreach (var key in new[] { "name", "modId", "id", "compiledFor", "namespace", "entrypoints", "hooks", "tweakers", "mixins", "dependencies" })
                    if (root.TryGetProperty(key, out var value)) metadata["weave." + key] = value.GetRawText();
                if (root.TryGetProperty("compiledFor", out var compiled)) mc = Scalar(compiled);
                if (root.TryGetProperty("dependencies", out var dependencies)) ReadDependencies(dependencies, deps);
            }
            if (archive.GetEntry("fabric.mod.json") is { } fabric)
            {
                types.Add(PackageType.FabricMod); loader = "fabric";
                evidence.Add(new("fabric.mod.json", "Fabric metadata marker"));
                using var doc = JsonDocument.Parse(Read(fabric));
                var root = doc.RootElement;
                foreach (var key in new[] { "id", "version", "environment", "jars", "depends" })
                    if (root.TryGetProperty(key, out var value)) metadata["fabric." + key] = value.GetRawText();
                if (root.TryGetProperty("depends", out var dependencies))
                {
                    ReadDependencies(dependencies, deps);
                    if (dependencies.TryGetProperty("minecraft", out var target)) mc = Scalar(target);
                    if (dependencies.TryGetProperty("fabricloader", out var loaderRequired)) loaderVersion = Scalar(loaderRequired);
                }
                if (root.TryGetProperty("jars", out _)) unknown.Add("fabric.nestedJars: declared, not inspected");
            }
            if (archive.GetEntry("META-INF/mods.toml") is { } forge)
            {
                types.Add(PackageType.ForgeMod); loader = "forge";
                evidence.Add(new("META-INF/mods.toml", "Forge metadata marker"));
                // Conservative TOML subset. Unsupported constructs remain explicitly unverified.
                var text = Read(forge);
                var section = "root"; var index = 0;
                var fields = new Dictionary<string, string>(StringComparer.Ordinal);
                void FinishDependency()
                {
                    if (section.StartsWith("dependencies.", StringComparison.Ordinal) && fields.TryGetValue("modId", out var id))
                    {
                        fields.TryGetValue("versionRange", out var range);
                        deps.Add(new(id, range, !fields.TryGetValue("mandatory", out var mandatory) || mandatory != "false"));
                        if (id == "minecraft") mc = range;
                    }
                    fields.Clear();
                }
                foreach (var raw in text.Replace("\r", "").Split('\n'))
                {
                    var line = raw.Trim();
                    if (line.Length == 0 || line.StartsWith('#')) continue;
                    if (line.StartsWith('[')) { FinishDependency(); section = line.Trim('[', ']'); index++; continue; }
                    var match = Regex.Match(line, "^([A-Za-z0-9_]+)\\s*=\\s*(?:\"([^\"]*)\"|'([^']*)'|(true|false))\\s*(?:#.*)?$");
                    if (!match.Success) { unknown.Add("forge.toml:" + section + ": unsupported syntax"); continue; }
                    var key = match.Groups[1].Value;
                    var value = match.Groups[2].Success ? match.Groups[2].Value : match.Groups[3].Success ? match.Groups[3].Value : match.Groups[4].Value;
                    if (section == "root" && key == "loaderVersion") loaderVersion = value;
                    fields[key] = value; metadata[$"forge.{index}.{section}.{key}"] = value;
                }
                FinishDependency();
            }
            if (archive.GetEntry("mcmod.info") is { } legacyForge)
            {
                if (!types.Contains(PackageType.ForgeMod)) types.Add(PackageType.ForgeMod);
                loader = "forge"; evidence.Add(new("mcmod.info", "Legacy Forge metadata marker"));
                using var doc = JsonDocument.Parse(Read(legacyForge));
                metadata["forge.mcmod.info"] = doc.RootElement.GetRawText();
                var mods = doc.RootElement.ValueKind == JsonValueKind.Array ? doc.RootElement :
                    doc.RootElement.TryGetProperty("modList", out var list) ? list : default;
                if (mods.ValueKind == JsonValueKind.Array)
                {
                    var i = 0;
                    foreach (var mod in mods.EnumerateArray())
                    {
                        foreach (var key in new[] { "modid", "version", "mcversion", "dependencies", "requiredMods" })
                            if (mod.TryGetProperty(key, out var value)) metadata[$"forge.legacy.{i}.{key}"] = value.GetRawText();
                        if (mod.TryGetProperty("mcversion", out var target)) mc = Scalar(target);
                        if (mod.TryGetProperty("requiredMods", out var required)) ReadDependencies(required, deps);
                        if (mod.TryGetProperty("dependencies", out var legacyDependencies)) ReadDependencies(legacyDependencies, deps);
                        i++;
                    }
                }
            }
            var legacy = false; var current = false; var count = 0; long total = 0;
            foreach (var entry in archive.Entries.Where(e => e.FullName.EndsWith(".class", StringComparison.Ordinal)).OrderBy(e => e.FullName, StringComparer.Ordinal))
            {
                if (++count > MaxClasses || (total += entry.Length) > 32 * 1024 * 1024)
                { unknown.Add("class references: inspection limit reached"); break; }
                if (entry.Length > MaxEntry) { unknown.Add("class references: oversized class"); continue; }
                using var stream = entry.Open();
                using var buffer = new MemoryStream(); stream.CopyTo(buffer);
                try
                {
                    var references = ClassReferences.Read(buffer.ToArray());
                    java = Math.Max(java ?? 0, references.Major - 44);
                    foreach (var reference in references.Classes)
                    {
                        if (reference.StartsWith("net/weavemc/loader/api/", StringComparison.Ordinal)) legacy = true;
                        if (reference.StartsWith("net/weavemc/api/", StringComparison.Ordinal)) current = true;
                    }
                }
                catch (InvalidDataException) { unknown.Add("class references: malformed class " + entry.FullName); }
            }
            if (legacy) evidence.Add(new("class.constant_pool", "Class reference net/weavemc/loader/api/"));
            if (current) evidence.Add(new("class.constant_pool", "Class reference net/weavemc/api/"));
            if (legacy != current) generation = legacy ? WeaveGeneration.Legacy : WeaveGeneration.Current;
            if (legacy && current) issues.Add(new("weave.references.mixed", Severity.Error, "Both Weave API generations are referenced.", path));
            if (types.Contains(PackageType.WeaveMod) && generation == WeaveGeneration.Unknown)
                Warn("weave.generation.unknown", "Weave generation is unverified; modId and compiledFor do not identify a loader version.");
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or JsonException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        { issues.Add(new("package.invalid", Severity.Error, "Package could not be inspected as a bounded valid JAR.", path)); }
        var detected = types.Distinct().OrderBy(t => t).ToImmutableArray();
        var type = detected.Length == 1 ? detected[0] : PackageType.Unknown;
        if (detected.Length > 1) issues.Add(new("package.ambiguous", Severity.Error, "Multiple ecosystem markers require an explicit interpretation.", path));
        if (detected.IsEmpty) Warn("package.unknown", "No supported startup/package marker was detected.");
        if (mc == null) unknown.Add("Minecraft target");
        if (java == null) unknown.Add("Java/classfile minimum");
        return new(detected, new(type, mc, java, generation, weaveVersion, loader, deps.ToImmutable(), detected.Contains(PackageType.JavaAgent), unknown.ToImmutable(), loaderVersion),
            metadata.ToImmutable(), evidence.ToImmutable(), issues.ToImmutable());
    }
    private static string? Scalar(JsonElement element) => element.ValueKind == JsonValueKind.String ? element.GetString() : element.GetRawText();
    private static void ReadDependencies(JsonElement element, ImmutableArray<Dependency>.Builder result)
    {
        if (element.ValueKind == JsonValueKind.Object)
            foreach (var property in element.EnumerateObject()) result.Add(new(property.Name, Scalar(property.Value)));
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var item in element.EnumerateArray())
                if (item.ValueKind == JsonValueKind.String) result.Add(new(item.GetString()!, null));
                else if (item.ValueKind == JsonValueKind.Object && (item.TryGetProperty("id", out var id) || item.TryGetProperty("modId", out id)))
                    result.Add(new(id.GetString()!, item.TryGetProperty("version", out var version) ? Scalar(version) : null));
    }
}

internal static class ClassReferences
{
    public static (int Major, ImmutableArray<string> Classes) Read(byte[] bytes)
    {
        var offset = 0;
        int U1() { if (offset >= bytes.Length) throw new InvalidDataException(); return bytes[offset++]; }
        int U2() => (U1() << 8) | U1();
        void Skip(int count) { if (count < 0 || offset + count > bytes.Length) throw new InvalidDataException(); offset += count; }
        if (U1() != 0xca || U1() != 0xfe || U1() != 0xba || U1() != 0xbe) throw new InvalidDataException();
        U2(); var major = U2(); if (major < 45) throw new InvalidDataException();
        var count = U2(); var utf8 = new Dictionary<int, string>(); var classes = new List<int>(); var descriptors = new List<int>();
        for (var index = 1; index < count; index++)
        {
            switch (U1())
            {
                case 1: var length = U2(); if (offset + length > bytes.Length) throw new InvalidDataException(); utf8[index] = Encoding.UTF8.GetString(bytes, offset, length); Skip(length); break;
                case 7: classes.Add(U2()); break;
                case 12: U2(); descriptors.Add(U2()); break;
                case 16: descriptors.Add(U2()); break;
                case 3: case 4: case 9: case 10: case 11: case 17: case 18: Skip(4); break;
                case 5: case 6: Skip(8); index++; break;
                case 8: case 19: case 20: Skip(2); break;
                case 15: Skip(3); break;
                default: throw new InvalidDataException();
            }
        }
        // A complete header must follow the pool; do not accept truncated pool-only files.
        Skip(6);
        var references = classes.Select(index => utf8.TryGetValue(index, out var value) ? value : throw new InvalidDataException()).ToList();
        foreach (var index in descriptors)
        {
            if (!utf8.TryGetValue(index, out var descriptor)) throw new InvalidDataException();
            references.AddRange(Regex.Matches(descriptor, @"L([^;]+);").Select(m => m.Groups[1].Value));
        }
        return (major, references.ToImmutableArray());
    }
}

public static class ArtifactSnapshots
{
    public static ArtifactSnapshot Capture(string path, string source, bool inspectPackage = true)
    {
        var canonical = Path.GetFullPath(path);
        canonical = new FileInfo(canonical).ResolveLinkTarget(true)?.FullName ?? canonical;
        using var stream = new FileStream(canonical, FileMode.Open, FileAccess.Read, FileShare.Read);
        var size = stream.Length; var hash = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        var inspection = inspectPackage ? PackageInspector.Inspect(canonical) : null;
        stream.Position = 0;
        if (stream.Length != size || Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant() != hash)
            throw new IOException("Artifact changed while snapshotting; rebuild snapshot.");
        return new(canonical, size, hash, source, inspection);
    }
    public static ImmutableArray<Issue> Validate(IEnumerable<ArtifactSnapshot> artifacts)
    {
        var issues = ImmutableArray.CreateBuilder<Issue>();
        foreach (var artifact in artifacts)
        {
            try
            {
                var actual = Capture(artifact.CanonicalPath, artifact.Source, false);
                if (actual.Size != artifact.Size || actual.Sha256 != artifact.Sha256)
                    issues.Add(new("artifact.changed", Severity.Error, "Artifact changed; rebuild the frozen plan.", artifact.CanonicalPath));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            { issues.Add(new("artifact.missing", Severity.Error, "Artifact is unavailable; rebuild the frozen plan.", artifact.CanonicalPath)); }
        }
        return issues.ToImmutable();
    }
}
