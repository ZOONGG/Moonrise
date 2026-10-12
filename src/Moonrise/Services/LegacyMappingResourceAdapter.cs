using System.IO.Compression;
using Moonrise.Models;

namespace Moonrise.Services;

// OfficialLunar bridge only. Files belong to its existing launch session cleanup.
public static class LegacyMappingResourceAdapter
{
    public const string ResourcePath = "lunar/lunar_named_b5_1.8.9.xsrg";
    public const string Id = "moonrise-legacy-mapping-resource-1.1.0";
    public static LaunchPlan Prepare(LaunchPlan plan, string sessionDirectory)
    {
        if (plan.WeaveMode != WeaveRuntimeMode.Legacy) return plan;
        var weave = plan.Agents.Single(a => a.IsWeaveLoader);
        var runtime = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".lunarclient", "offline", "multiver");
        var genesis = Path.Combine(runtime, "genesis-0.1.0-SNAPSHOT-all.jar");
        var mapping = Path.Combine(runtime, "lunar-platform-mappings-v1_8.jar");
        ValidateRuntimeContract(weave.Path, runtime);
        var agent = Path.Combine(sessionDirectory, Id + ".jar");
        WriteBootstrapJar(agent);
        var resource = Path.Combine(sessionDirectory, "legacy-mapping-resource.jar");
        var agents = plan.Agents.ToList();
        agents.Insert(0, new LaunchAgent(agent, string.Join("|", genesis, mapping, resource), Id, LaunchAgentRole.CompatibilityAdapter));
        if (Environment.GetCommandLineArgs().Contains("--dev-jvm-startup-diagnostic", StringComparer.Ordinal))
        {
            var evidence = Environment.GetEnvironmentVariable("MOONRISE_DEV_STARTUP_CAPTURE");
            if (string.IsNullOrWhiteSpace(evidence) || !Path.IsPathFullyQualified(evidence) || !Directory.Exists(evidence))
                throw new InvalidOperationException("An existing absolute startup evidence directory is required.");
            var metadata = new
            {
                PreparedAtUtc = DateTimeOffset.UtcNow,
                LaunchId = Path.GetFileName(sessionDirectory),
                PremainClass = "dev.moonrise.mapping.MappingAgent",
                BootstrapSha256 = LocalPackageLibrary.ComputeSha256(agent),
                GenesisSha256 = LocalPackageLibrary.ComputeSha256(genesis),
                MappingSha256 = LocalPackageLibrary.ComputeSha256(mapping),
                BootstrapClassMajorVersion = 61,
                BootstrapOptionFields = 3,
                IsolatedParent = "platform",
                IsolatedDependencies = new[] { "bootstrap JAR", "pinned Genesis JAR" },
                AgentOrder = agents.Select(a => new { a.RuntimeId, Sha256 = LocalPackageLibrary.ComputeSha256(a.Path) }).ToArray()
            };
            using var stream = new FileStream(Path.Combine(evidence, $"bootstrap-{Guid.NewGuid():N}.json"), FileMode.CreateNew);
            System.Text.Json.JsonSerializer.Serialize(stream, metadata, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            stream.Flush(flushToDisk: true);
        }
        return plan with { Agents = agents };
    }
    // Read-only preflight; Prepare rechecks this immediately before launch.
    public static void ValidateRuntimeContract(string loaderPath, string? runtimeDirectory = null)
    {
        var runtime = runtimeDirectory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".lunarclient", "offline", "multiver");
        ValidateHash(loaderPath, WeaveAgentService.Legacy02.ExpectedSha256);
        ValidateHash(Path.Combine(runtime, "genesis-0.1.0-SNAPSHOT-all.jar"), "e3f34c4ad92f1c3bae83c17bbacb45952c156df359cb39d6c34d086b05e83c44");
        ValidateHash(Path.Combine(runtime, "lunar.jar"), "bb37980cdf62e4576adcd700268dc24359a7d7ca48090b9b698696df9d89932f");
        var mapping = Path.Combine(runtime, "lunar-platform-mappings-v1_8.jar");
        ValidateHash(mapping, "5d53fd9767c085475ad139fd7e667a28cb3c0fedaf526b495efc8fe515add521");
        using var archive = ZipFile.OpenRead(mapping);
        if (archive.Entries.Count(e => e.FullName == "lunar/lunar_named_b5_1.8.9.kin") != 1)
            throw new InvalidDataException("Legacy mapping contract: missing/duplicate Minecraft 1.8.9 KIN mapping.");
    }
    internal static void WriteBootstrapJar(string agent)
    {
        using (var output = new FileStream(agent, FileMode.CreateNew))
        using (var zip = new ZipArchive(output, ZipArchiveMode.Create))
        {
            using (var writer = new StreamWriter(zip.CreateEntry("META-INF/MANIFEST.MF").Open()))
                writer.Write("Manifest-Version: 1.0\r\nPremain-Class: dev.moonrise.mapping.MappingAgent\r\n\r\n");
            foreach (var item in LegacyMappingAdapterClasses.Bytes)
            {
                using var stream = zip.CreateEntry(item.Key).Open();
                stream.Write(Convert.FromBase64String(item.Value));
            }
        }
    }
    public static void ValidateHash(string path, string expected)
    {
        if (!File.Exists(path) || !LocalPackageLibrary.ComputeSha256(path).Equals(expected, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Legacy mapping contract unsupported or missing: {Path.GetFileName(path)}. Requires pinned Lunar 2.23.1 / Minecraft 1.8.9 / Weave 0.2.6.");
    }
}
