using System.Collections.Immutable;
using System.IO.Compression;
using System.Text;
using Moonrise.Compatibility;
using Xunit;

namespace Moonrise.Tests;

public sealed class ControlledGenesisTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "Moonrise Controlled Genesis Tests", Guid.NewGuid().ToString("N"));
    public ControlledGenesisTests() => Directory.CreateDirectory(_root);
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }

    private LaunchPlan Plan(string userJvm = "-Dmoonrise.stage2.smoke=true", JavaRuntimeMode mode = JavaRuntimeMode.Custom, string? token = null, ImmutableArray<RuntimeResourceRoot> resources = default, string? runtimeView = null)
    {
        var runtime = Path.Combine(_root, "Lunar runtime");
        var native = Path.Combine(_root, "runtime files", "native");
        var genesis = Path.Combine(runtime, "genesis.jar");
        var ichor = Path.Combine(runtime, "ichor module.jar");
        var external = Path.Combine(runtime, "OptiFine_v1_8.jar");
        var asset = Path.Combine(runtime, "asset index.json");
        var executable = Path.Combine(runtime, "java.exe");
        Directory.CreateDirectory(native); Directory.CreateDirectory(runtime);
        foreach (var path in new[] { genesis, ichor, external, asset, executable, Path.Combine(native, "lwjgl64.dll") })
            File.WriteAllText(path, Path.GetFileName(path));
        File.WriteAllText(Path.Combine(runtime, "release"), "JAVA_VERSION=\"17.0.18\"\nIMPLEMENTOR=\"Fixture Java\"\nOS_ARCH=\"x86_64\"\n");
        var jre = JavaRuntimes.Inspect(executable)!;
        var classPath = ImmutableArray.Create(ArtifactSnapshots.Capture(genesis, "lunar.genesis", false));
        var ichorClassPath = ImmutableArray.Create(ArtifactSnapshots.Capture(ichor, "lunar.ichor-module:fixture", false));
        classPath = classPath.AddRange(ichorClassPath);
        var externalFiles = ImmutableArray.Create(ArtifactSnapshots.Capture(external, "lunar.profile-external:optifine", false));
        var natives = ImmutableArray.Create(ArtifactSnapshots.Capture(Path.Combine(native, "lwjgl64.dll"), "lunar.native:lwjgl64.dll", false));
        var game = Path.Combine(_root, "Minecraft game folder");
        var work = Path.Combine(_root, "Moonrise launch session");
        var args = ImmutableArray.Create("--gameDir", game, "--assetIndex", "1.8");
        if (token is not null) args = args.Add("--accessToken").Add(token);
        var request = new PlanRequest(ControlledGenesisDiscovery.ContractId, "1.8.9", "fixture-profile", Backend.ControlledGenesis,
            new BaseLoader("optifine", "HD_U_M6_pre2", [new("fixture", "structured profile module metadata")]),
            [], [], new(WeaveStrategy.Off, null, null, WeaveGeneration.Unknown, [], null),
            mode, mode == JavaRuntimeMode.Custom ? executable : null, [jre], new(17, 17, "x64"),
            1024, 4096, userJvm, ["-XX:+UseG1GC", "-Dlunar.dataDir=" + Path.Combine(work, "lunar-data")], [], work, game, ControlledGenesisDiscovery.GenesisMain,
            asset, classPath, ichorClassPath, externalFiles, natives, args)
            { ResourceRoots = resources.IsDefault ? [] : resources, RuntimeFileDirectory = runtimeView };
        return CompatibilityPlanner.Build(request);
    }

    [Fact] public void DiscoveryUsesVersionedContractAndSelectedOptiFineModule()
    {
        var lunar = Path.Combine(_root, "local lunar"); var offline = Path.Combine(lunar, "offline", "multiver");
        var game = Path.Combine(_root, "game with spaces"); var session = Path.Combine(_root, "Moonrise sessions");
        Directory.CreateDirectory(offline); Directory.CreateDirectory(Path.Combine(offline, "natives"));
        Directory.CreateDirectory(Path.Combine(game, "versions", "1.8.9"));
        Directory.CreateDirectory(Path.Combine(game, "assets", "indexes"));
        var genesis = Path.Combine(offline, "genesis-0.1.0-SNAPSHOT-all.jar");
        using (var zip = ZipFile.Open(genesis, ZipArchiveMode.Create))
        {
            Add(zip, "META-INF/MANIFEST.MF", "Manifest-Version: 1.0\nMain-Class: com.moonsworth.lunar.genesis.Genesis\nImplementation-Version: 9.10.1\n\n");
            Add(zip, "versions.conf", "v1_8: ${legacy} {\n exact-version: \"1.8.9\"\n modules: [\n {\n name: \"lunar\"\n modules: [\"lunar\", \":external:optifine\"]\n default: true\n }\n ]\n}\n");
        }
        var paths = new[] { "lunar.jar", "legacy-0.1.0-SNAPSHOT-all-nomappings.jar", "common-0.1.0-SNAPSHOT-all-nomappings.jar", "optifine-0.1.0-SNAPSHOT-all.jar", "lunar-platform-mappings-v1_8.jar", "OptiFine_v1_8.jar" };
        foreach (var path in paths) File.WriteAllText(Path.Combine(offline, path), path);
        File.WriteAllText(Path.Combine(offline, "lunar-lang.jar"), "language resource fixture");
        File.WriteAllText(Path.Combine(offline, "natives", "lwjgl64.dll"), "native");
        File.WriteAllText(Path.Combine(game, "versions", "1.8.9", "1.8.9.jar"), "minecraft");
        File.WriteAllText(Path.Combine(game, "assets", "indexes", "1.8.json"), "{}");
        var hash = ArtifactSnapshots.Capture(genesis, "fixture", false).Sha256;
        var result = ControlledGenesisDiscovery.Discover(lunar, game, Path.Combine(lunar, "jre"), session,
            "selected-id", "1.8.9", ["ichor"], hash);
        Assert.Equal("optifine", result.LoaderId);
        Assert.Equal("9.10.1", result.GenesisVersion);
        Assert.Equal("lunar.genesis", result.ClassPath[0].Source);
        Assert.Equal(result.IchorClassPath.ToArray(), result.ClassPath.Skip(1).Take(result.IchorClassPath.Length).ToArray());
        Assert.Equal("lunar.profile-resource:language", result.ClassPath.Last().Source);
        Assert.Contains(result.IchorClassPath, a => a.Source == "lunar.ichor-module:optifine");
        Assert.Contains(result.IchorExternalFiles, a => a.Source == "lunar.profile-external:optifine");
        Assert.Contains(result.Evidence, e => e.Source == "versions.conf:1.8.9.modules");
    }

    [Theory]
    [InlineData("1.20.1", "ichor")]
    [InlineData("1.8.9", "forge")]
    public void DiscoveryRejectsProfilesOutsideTheVerifiedContract(string version, string loader)
    {
        var exception = Assert.Throws<NotSupportedException>(() => ControlledGenesisDiscovery.Discover(
            _root, _root, _root, _root, "profile", version, [loader], new string('0', 64)));
        Assert.Contains("does not match", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact] public void ExecutorRejectsInvalidAndOfficialOnlyPlans()
    {
        var invalid = Plan() with { Errors = [new("fixture.invalid", Severity.Error, "invalid fixture")] };
        Assert.Throws<InvalidOperationException>(() => new ControlledGenesisExecutor(_root).BuildArguments(invalid));
        var official = Plan() with { Backend = Backend.OfficialLunar };
        Assert.Throws<InvalidOperationException>(() => new ControlledGenesisExecutor(_root).BuildArguments(official));
    }

    [Fact] public void ArgumentsAreOrderedAndKeepPathsWithSpacesAndResolvedCustomJava()
    {
        var plan = Plan(); var arguments = new ControlledGenesisExecutor(_root).BuildArguments(plan);
        Assert.Equal(plan.Java.Runtime!.Executable, Path.Combine(_root, "Lunar runtime", "java.exe"));
        Assert.Equal("-XX:+UseG1GC", arguments[0]);
        Assert.StartsWith("-Dlunar.dataDir=", arguments[1]);
        Assert.Equal(new[] { "-Xms1024m", "-Xmx4096m", "-Dmoonrise.stage2.smoke=true" }, arguments.Skip(2).Take(3));
        Assert.Contains(string.Join(Path.PathSeparator, plan.ClassPath.Value.Select(a => a.CanonicalPath)), arguments);
        Assert.Contains(Path.Combine(_root, "Moonrise launch session"), arguments);
        Assert.Contains("--ichorClassPath", arguments);
        Assert.Contains("--ichorExternalFiles", arguments);
        Assert.Equal("--version", arguments[arguments.IndexOf(ControlledGenesisDiscovery.GenesisMain) + 1]);
    }

    [Fact] public void AutoUsesVerifiedCompatibleCandidateAndNeverFallsBackFromCustom()
    {
        var customPlan = Plan();
        Assert.Equal(JavaRuntimeMode.Custom, customPlan.Java.Mode);
        Assert.Equal(Path.Combine(_root, "Lunar runtime", "java.exe"), customPlan.Java.Runtime!.Executable);
        var autoPlan = Plan(userJvm: "", mode: JavaRuntimeMode.Auto);
        Assert.Equal(JavaRuntimeMode.Auto, autoPlan.Java.Mode);
        Assert.Equal(17, autoPlan.Java.Runtime!.Major);
        var missingCustom = JavaRuntimes.Resolve(JavaRuntimeMode.Custom,
            Path.Combine(_root, "missing", "java.exe"), [], new(17, 17, "x64"));
        Assert.Null(missingCustom.Runtime);
        Assert.Contains(missingCustom.Issues, issue => issue.Code == "java.custom.missing");
        Assert.Equal(PreflightStatus.Invalid, missingCustom.Compatibility);
    }

    [Fact] public void ResourcePathsAndProvenanceAreFrozenIntoPlanAndSnapshot()
    {
        var source = Path.Combine(_root, "installed resources"); Directory.CreateDirectory(source);
        var file = Path.Combine(source, "index.html"); File.WriteAllText(file, "resource");
        var artifact = ArtifactSnapshots.Capture(file, "lunar.ui:index.html", false);
        ImmutableArray<RuntimeResourceRoot> roots = [new(source, Path.Combine(_root, "Moonrise launch session", "ui"), "WebOSR UI", true, "Moonrise-owned launch copy", "fixture archive hash", [artifact])];
        var view = Path.Combine(_root, "Moonrise launch session", "runtime");
        var first = Plan(resources: roots, runtimeView: view); var second = Plan(resources: roots, runtimeView: view);
        Assert.Equal(first.PlanId, second.PlanId);
        Assert.Equal(first.RuntimeSnapshotId, second.RuntimeSnapshotId);
        Assert.Contains(artifact, first.ArtifactFingerprints);
        Assert.Contains("fixture archive hash", PlanSerialization.Diagnostics(first));
        var arguments = new ControlledGenesisExecutor(_root).BuildArguments(first);
        Assert.Equal(view, arguments[arguments.IndexOf("--classpathDir") + 1]);
        var changed = first with { ResourceRoots = [roots[0] with { PreparedRoot = Path.Combine(_root, "other") }] };
        Assert.Throws<InvalidOperationException>(() => new ControlledGenesisExecutor(_root).BuildArguments(changed));
    }

    [Fact] public void DiagnosticsRedactSecretLikeGenesisArguments()
    {
        var plan = Plan(userJvm: "", token: "fixture-secret-token");
        Assert.DoesNotContain("fixture-secret-token", PlanSerialization.Diagnostics(plan));
        var args = ControlledGenesisExecutor.SanitizeArguments(new[] { "--accessToken", "fixture-secret-token", "-Dsecret=value" });
        Assert.DoesNotContain("fixture-secret-token", string.Join(' ', args));
        Assert.Contains("-Dsecret=[redacted]", args);
    }

    [Fact] public void CancellationIdentityCannotTerminateAnUnrelatedJavaProcess()
    {
        using var process = System.Diagnostics.Process.GetCurrentProcess();
        var identity = new ControlledGenesisProcessIdentity(process.Id, process.StartTime.ToUniversalTime(),
            Path.Combine(_root, "unrelated-java.exe"), "plan", "session", "fixture");
        var launch = new ControlledGenesisLaunch(process, identity, "", "", "", _root);
        Assert.False(ControlledGenesisExecutor.TerminateOwned(launch));
        Assert.False(process.HasExited);
    }

    private static void Add(ZipArchive archive, string path, string value)
    {
        using var writer = new StreamWriter(archive.CreateEntry(path).Open(), Encoding.UTF8);
        writer.Write(value);
    }
}
