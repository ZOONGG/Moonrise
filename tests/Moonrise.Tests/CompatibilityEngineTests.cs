using System.Collections.Immutable;
using System.IO.Compression;
using System.Text;
using Moonrise.Compatibility;
using Xunit;
namespace Moonrise.Tests;

public sealed class CompatibilityEngineTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "MoonriseCompatibilityTests", Guid.NewGuid().ToString("N"));
    public CompatibilityEngineTests() => Directory.CreateDirectory(root);
    public void Dispose() => Directory.Delete(root, true);
    private string Jar(string name, params (string Name, byte[] Bytes)[] entries)
    {
        var path = Path.Combine(root, name + ".jar");
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var entry in entries) { using var stream = zip.CreateEntry(entry.Name).Open(); stream.Write(entry.Bytes); }
        return path;
    }
    private static (string, byte[]) Text(string name, string text) => (name, Encoding.UTF8.GetBytes(text));
    private static byte[] Class(string? reference = null, int major = 52, bool stringOnly = false)
    {
        using var stream = new MemoryStream();
        void U1(int n) => stream.WriteByte((byte)n);
        void U2(int n) { U1(n >> 8); U1(n); }
        U1(0xca); U1(0xfe); U1(0xba); U1(0xbe); U2(0); U2(major);
        U2(reference == null ? 1 : 3);
        if (reference != null)
        {
            var text = Encoding.UTF8.GetBytes(reference); U1(1); U2(text.Length); stream.Write(text);
            U1(stringOnly ? 8 : 7); U2(1);
        }
        U2(1); U2(0); U2(0); U2(0); U2(0); U2(0); U2(0);
        return stream.ToArray();
    }
    private ArtifactSnapshot AgentArtifact(string name = "agent", WeaveGeneration generation = WeaveGeneration.Unknown)
    {
        var reference = generation switch { WeaveGeneration.Legacy => "net/weavemc/loader/api/ModInitializer", WeaveGeneration.Current => "net/weavemc/api/ModInitializer", _ => null };
        return ArtifactSnapshots.Capture(Jar(name, Text("META-INF/MANIFEST.MF", "Manifest-Version: 1.0\nPremain-Class: sample.Agent\nCan-Retransform-Classes: true\n\n"), ("sample/Agent.class", Class(reference))), "synthetic");
    }
    private ArtifactSnapshot Mod(string name, string json, WeaveGeneration generation = WeaveGeneration.Unknown)
    {
        var reference = generation switch { WeaveGeneration.Legacy => "net/weavemc/loader/api/ModInitializer", WeaveGeneration.Current => "net/weavemc/api/ModInitializer", _ => null };
        return ArtifactSnapshots.Capture(Jar(name, Text("weave.mod.json", json), ("sample/Mod.class", Class(reference))), "synthetic");
    }
    private JavaCandidate Java(string version = "17.0.3", string architecture = "amd64")
    {
        var directory = Path.Combine(root, "runtime-" + version); Directory.CreateDirectory(Path.Combine(directory, "bin"));
        File.WriteAllBytes(Path.Combine(directory, "bin", "java.exe"), [1, 2, 3]);
        File.WriteAllText(Path.Combine(directory, "release"), $"JAVA_VERSION=\"{version}\"\nIMPLEMENTOR=\"Synthetic Vendor\"\nOS_ARCH=\"{architecture}\"\n");
        return JavaRuntimes.Inspect(directory)!;
    }
    private PlanRequest Request() => new("fixture-contract", "1.8.9", "profile", Backend.ControlledGenesis,
        new("vanilla", null, []), [], [], new(WeaveStrategy.Off, null, null, WeaveGeneration.Unknown, [], null),
        JavaRuntimeMode.Auto, null, [Java()], new(8, 17, "x64"), 512, 2048, "", [], [],
        root, root, "sample.Main", "1.8", [ArtifactSnapshots.Capture(AgentArtifact("classpath").CanonicalPath, "classpath", false)]);
    private static Agent Slot(string id, ArtifactSnapshot artifact, int order = 0, string role = "UserAgent", string? options = null,
        ImmutableArray<string> before = default, ImmutableArray<string> after = default) => new(id, artifact, role, true, order, options, before.IsDefault ? [] : before, after.IsDefault ? [] : after);
    private static void Error(LaunchPlan plan, string code) => Assert.Contains(plan.Errors, i => i.Code == code);

    [Fact] public void ValidPremainIsStartupAgent()
    {
        var inspection = AgentArtifact().Inspection!;
        Assert.Equal(PackageType.JavaAgent, inspection.Requirements.Type); Assert.True(inspection.Requirements.StartupAgent);
        Assert.Equal("true", inspection.Metadata["Can-Retransform-Classes"]); Assert.Empty(inspection.Issues.Where(i => i.Severity == Severity.Error));
    }
    [Fact] public void AgentClassOnlyIsNotStartup()
    {
        var result = PackageInspector.Inspect(Jar("attach", Text("META-INF/MANIFEST.MF", "Agent-Class: sample.Agent\n\n")));
        Assert.False(result.Requirements.StartupAgent); Assert.Equal(PackageType.Unknown, result.Requirements.Type);
    }
    [Fact] public void MissingPremainClassRejected()
    {
        var result = PackageInspector.Inspect(Jar("missing", Text("META-INF/MANIFEST.MF", "Premain-Class: sample.Missing\n\n")));
        Assert.Contains(result.Issues, i => i.Code == "agent.premain.target");
    }
    [Theory] [InlineData(WeaveGeneration.Legacy)] [InlineData(WeaveGeneration.Current)]
    public void GenerationUsesClassReferences(WeaveGeneration generation)
    {
        var result = Mod("mod", "{\"modId\":\"fixture\",\"compiledFor\":\"1.8.9\",\"entrypoints\":[\"sample.Mod\"],\"hooks\":[\"sample.Hook\"],\"mixins\":[\"mod.mixins.json\"]}", generation).Inspection!;
        Assert.Equal(generation, result.Requirements.WeaveGeneration); Assert.Equal("1.8.9", result.Requirements.MinecraftRange);
        Assert.Contains(result.Evidence, e => e.Source == "class.constant_pool"); Assert.Contains("weave.hooks", result.Metadata.Keys);
    }
    [Fact] public void ModIdDoesNotProveCurrentGeneration() => Assert.Equal(WeaveGeneration.Unknown, Mod("ambiguous", "{\"modId\":\"old\",\"compiledFor\":\"1.8.9\"}").Inspection!.Requirements.WeaveGeneration);
    [Fact] public void StringLiteralDoesNotProveGeneration()
    {
        var result = PackageInspector.Inspect(Jar("literal", Text("weave.mod.json", "{}"), ("sample/Mod.class", Class("net/weavemc/api/ModInitializer", stringOnly: true))));
        Assert.Equal(WeaveGeneration.Unknown, result.Requirements.WeaveGeneration);
    }
    [Fact] public void BothReferenceGenerationsAreAmbiguous()
    {
        var result = PackageInspector.Inspect(Jar("mixed", Text("weave.mod.json", "{}"), ("A.class", Class("net/weavemc/api/Foo")), ("B.class", Class("net/weavemc/loader/api/Foo"))));
        Assert.Equal(WeaveGeneration.Unknown, result.Requirements.WeaveGeneration); Assert.Contains(result.Issues, i => i.Code == "weave.references.mixed");
    }
    [Fact] public void ForgeTomlExtractsRequirements()
    {
        var result = PackageInspector.Inspect(Jar("forge", Text("META-INF/mods.toml", "modLoader=\"javafml\"\nloaderVersion=\"[36,)\"\n[[mods]]\nmodId=\"fixture\"\n[[dependencies.fixture]]\nmodId=\"minecraft\"\nmandatory=true\nversionRange=\"[1.16,1.17)\"\n")));
        Assert.Equal(PackageType.ForgeMod, result.Requirements.Type); Assert.Equal("[1.16,1.17)", result.Requirements.MinecraftRange);
        Assert.Contains(result.Requirements.Dependencies, d => d.Id == "minecraft"); Assert.Contains(result.Metadata.Values, v => v == "[36,)");
    }
    [Fact] public void LegacyForgeRecognized()
    {
        var result = PackageInspector.Inspect(Jar("forge", Text("mcmod.info", "[{\"modid\":\"fixture\",\"mcversion\":\"1.8.9\",\"requiredMods\":[\"dependency\"]}]")));
        Assert.Equal(PackageType.ForgeMod, result.Requirements.Type); Assert.Equal("1.8.9", result.Requirements.MinecraftRange); Assert.Single(result.Requirements.Dependencies);
    }
    [Fact] public void FabricExtractsMetadata()
    {
        var result = PackageInspector.Inspect(Jar("fabric", Text("fabric.mod.json", "{\"id\":\"fixture\",\"version\":\"1\",\"environment\":\"client\",\"depends\":{\"minecraft\":\"1.8.9\",\"fabricloader\":\">=0.15\"},\"jars\":[{\"file\":\"nested.jar\"}]}")));
        Assert.Equal(PackageType.FabricMod, result.Requirements.Type); Assert.Equal("1.8.9", result.Requirements.MinecraftRange);
        Assert.Contains(result.Requirements.Dependencies, d => d.Id == "fabricloader"); Assert.Contains("fabric.jars", result.Metadata.Keys);
    }
    [Fact] public void MultiMarkerRequiresOverride()
    {
        var result = PackageInspector.Inspect(Jar("multi", Text("weave.mod.json", "{}"), Text("fabric.mod.json", "{\"id\":\"test\"}")));
        Assert.Equal(PackageType.Unknown, result.Requirements.Type); Assert.Contains(result.Issues, i => i.Code == "package.ambiguous");
    }
    [Fact] public void InvalidJarReturnsIssue() { var path = Path.Combine(root, "bad.jar"); File.WriteAllText(path, "broken"); Assert.Contains(PackageInspector.Inspect(path).Issues, i => i.Code == "package.invalid"); }
    [Fact] public void EmptyJarReturnsIssue() => Assert.Contains(PackageInspector.Inspect(Jar("empty")).Issues, i => i.Code == "package.invalid");
    [Fact] public void UnknownJarIsExplicit() => Assert.Equal(PackageType.Unknown, PackageInspector.Inspect(Jar("unknown", Text("readme", "hello"))).Requirements.Type);
    [Fact] public void InspectionDoesNotMutateArtifact() { var artifact = AgentArtifact(); Assert.Empty(ArtifactSnapshots.Validate([artifact])); }
    [Fact] public void ManifestContinuationsAndOptionalAttributes()
    {
        var result = PackageInspector.Inspect(Jar("manifest", Text("META-INF/MANIFEST.MF", "premain-class: sample.\n Agent\nAgent-Class: sample.Attach\nCan-Redefine-Classes: true\nBoot-Class-Path: boot.jar\n\n"), ("sample/Agent.class", Class())));
        Assert.True(result.Requirements.StartupAgent); Assert.Equal("boot.jar", result.Metadata["Boot-Class-Path"]);
    }
    [Fact] public void OneAgentAndOptionsPreserved()
    {
        var artifact = AgentArtifact(); var request = Request() with { Packages = [new("one", artifact)], Agents = [Slot("one", artifact, options: "path with spaces=value")] };
        var plan = CompatibilityPlanner.Build(request); Assert.Empty(plan.Errors); Assert.Equal("path with spaces=value", Assert.Single(plan.Agents).Options);
    }
    [Fact] public void SeveralAgentsUseStableConstraints()
    {
        var first = AgentArtifact("first"); var second = AgentArtifact("second");
        // Different bytes are intentional; duplicate binaries must not become two startup agents.
        File.AppendAllText(second.CanonicalPath, "distinct"); second = ArtifactSnapshots.Capture(second.CanonicalPath, "synthetic");
        var plan = CompatibilityPlanner.Build(Request() with { Agents = [Slot("second", second, 0, after: ["first"]), Slot("first", first, 20)] });
        Assert.Empty(plan.Errors); Assert.Equal(new[] { "first", "second" }, plan.Agents.Select(a => a.Id));
    }
    [Fact] public void OrderingCycleRejected()
    {
        var a = AgentArtifact(); var plan = CompatibilityPlanner.Build(Request() with { Agents = [Slot("a", a, after: ["b"]), Slot("b", a, after: ["a"])] }); Error(plan, "agent.order.cycle");
    }
    [Theory] [InlineData(WeaveGeneration.Legacy)] [InlineData(WeaveGeneration.Current)]
    public void OneWeaveLoaderMatchesGeneration(WeaveGeneration generation)
    {
        var loader = AgentArtifact("loader", generation) with { Source = "managed.weave" }; var mod = Mod("mod", "{\"compiledFor\":\"1.8.9\"}", generation);
        var plan = CompatibilityPlanner.Build(Request() with { Packages = [new("mod", mod)], Agents = [Slot("loader", loader, role: "WeaveLoader")], Weave = new(WeaveStrategy.Bundled, loader, "1", generation, [], root) });
        Assert.Empty(plan.Errors); Assert.Single(plan.EnabledWeaveMods);
    }
    [Fact] public void MixedWeaveRequirementsRejected()
    {
        var legacy = Mod("legacy", "{}", WeaveGeneration.Legacy); var current = Mod("current", "{}", WeaveGeneration.Current);
        Error(CompatibilityPlanner.Build(Request() with { Packages = [new("legacy", legacy), new("current", current)] }), "weave.generation.conflict");
    }
    [Fact] public void DuplicateLoaderRejected()
    {
        var loader = AgentArtifact("loader", WeaveGeneration.Legacy);
        Error(CompatibilityPlanner.Build(Request() with { Agents = [Slot("a", loader, role: "WeaveLoader"), Slot("b", loader, role: "WeaveLoader")], Weave = new(WeaveStrategy.ExternalAgent, loader, "0.2.6", WeaveGeneration.Legacy, [], root) }), "weave.loader.count");
    }
    [Fact] public void WrongMinecraftVersionRejected() => Error(CompatibilityPlanner.Build(Request() with { Packages = [new("mod", Mod("mod", "{\"compiledFor\":\"1.12.2\"}"))] }), "package.minecraft.conflict");
    [Fact] public void MissingDependencyRejected() => Error(CompatibilityPlanner.Build(Request() with { Packages = [new("mod", Mod("mod", "{\"dependencies\":[\"missing\"]}"))] }), "dependency.missing");
    [Fact] public void DisabledPackageExcluded()
    {
        var plan = CompatibilityPlanner.Build(Request() with { Packages = [new("disabled", Mod("disabled", "{\"compiledFor\":\"1.12.2\"}"), false)] });
        Assert.Empty(plan.EnabledPackages); Assert.Empty(plan.Errors);
    }
    [Fact] public void UnknownGenerationBlocksActivation() => Error(CompatibilityPlanner.Build(Request() with { Packages = [new("mod", Mod("mod", "{\"modId\":\"legacy\"}"))] }), "weave.requirement.unknown");
    [Fact] public void ManualOverrideCannotDisableValidation()
    {
        var artifact = ArtifactSnapshots.Capture(Jar("missing", Text("META-INF/MANIFEST.MF", "Premain-Class: Missing\n\n")), "fixture");
        Error(CompatibilityPlanner.Build(Request() with { Packages = [new("missing", artifact, Override: new(PackageType.JavaAgent))] }), "agent.premain.target");
    }
    [Fact] public void AutoUsesMetadataAndRequirements()
    {
        var older = Java("8.0.1"); var newer = Java();
        var result = JavaRuntimes.Resolve(JavaRuntimeMode.Auto, null, [newer, older], new(8, 11, "x64"));
        Assert.Equal(8, result.Runtime!.Major); Assert.Equal("Synthetic Vendor", result.Runtime.Vendor); Assert.Equal(PreflightStatus.Valid, result.Compatibility);
    }
    [Fact] public void DiscoverFakeRuntimeDirectories() { Java(); Assert.Single(JavaRuntimes.Discover(root)); }
    [Fact] public void CustomExecutableResolves() { var java = Java(); var result = JavaRuntimes.Resolve(JavaRuntimeMode.Custom, java.Executable, [], new(17, 17, "x64")); Assert.Equal(java.Executable, result.Runtime!.Executable); }
    [Fact] public void CustomRootResolves() { var java = Java(); Assert.Equal(java.Executable, JavaRuntimes.Resolve(JavaRuntimeMode.Custom, java.RuntimeRoot, [], new()).Runtime!.Executable); }
    [Fact] public void MissingCustomDoesNotFallback() { var result = JavaRuntimes.Resolve(JavaRuntimeMode.Custom, Path.Combine(root, "absent"), [Java()], new()); Assert.Null(result.Runtime); Assert.Equal(PreflightStatus.Invalid, result.Compatibility); }
    [Fact] public void IncompatibleCustomRejected() { var java = Java("8.0.1"); Assert.Equal(PreflightStatus.Invalid, JavaRuntimes.Resolve(JavaRuntimeMode.Custom, java.Executable, [], new(17)).Compatibility); }
    [Fact] public void WrongArchitectureRejected() { var java = Java(architecture: "x86"); Assert.Equal(PreflightStatus.Invalid, JavaRuntimes.Resolve(JavaRuntimeMode.Custom, java.Executable, [], new(8, 17, "x64")).Compatibility); }
    [Fact] public void OfficialBackendHasDelegatedFields()
    {
        var plan = CompatibilityPlanner.Build(Request() with { Backend = Backend.OfficialLunar });
        Assert.Equal(ResolutionState.Delegated, plan.Java.State); Assert.Null(plan.Java.Runtime); Assert.Equal(ResolutionState.Delegated, plan.ClassPath.State); Assert.Equal(ResolutionState.Delegated, plan.GenesisGameArgs.State); Error(plan, "backend.jvm.unsupported");
    }
    [Fact] public void OfficialCustomJavaUnsupported() { var r = Request(); Error(CompatibilityPlanner.Build(r with { Backend = Backend.OfficialLunar, JavaMode = JavaRuntimeMode.Custom, CustomJava = r.JavaCandidates[0].Executable }), "backend.java.unsupported"); }
    [Theory]
    [InlineData("-Dfoo=bar", "-Dfoo=bar")]
    [InlineData("-Dpath=\"C:\\folder with spaces\\file\"", "-Dpath=C:\\folder with spaces\\file")]
    [InlineData("'-Dpath=a b'", "-Dpath=a b")]
    [InlineData("-Dliteral=$HOME", "-Dliteral=$HOME")]
    public void ArgumentsParseWithoutShell(string text, string expected) { var result = JvmArguments.Validate(text, 512, 2048, []); Assert.Equal(expected, Assert.Single(result.Tokens)); Assert.Empty(result.Issues); }
    [Fact] public void UnterminatedQuoteRejected() => Assert.Contains(JvmArguments.Parse("-Dfoo=\"bad").Issues, i => i.Code == "jvm.quotes");
    [Theory]
    [InlineData("-Xms512m", "jvm.memory.conflict")]
    [InlineData("-Xmx2048m", "jvm.memory.conflict")]
    [InlineData("-XX:InitialHeapSize=512m", "jvm.memory.conflict")]
    [InlineData("-XX:MaxHeapSize=2048m", "jvm.memory.conflict")]
    [InlineData("-XX:MaxRAMPercentage=75", "jvm.memory.conflict")]
    [InlineData("-cp foo", "jvm.classpath")]
    [InlineData("-classpath foo", "jvm.classpath")]
    [InlineData("--class-path=foo", "jvm.classpath")]
    [InlineData("-Djava.class.path=foo", "jvm.classpath")]
    [InlineData("-javaagent:agent.jar", "jvm.raw.agent")]
    [InlineData("-jar foo.jar", "jvm.entrypoint")]
    [InlineData("sample.Main", "jvm.entrypoint")]
    [InlineData("--module=sample/Main", "jvm.entrypoint")]
    [InlineData("@args.txt", "jvm.entrypoint")]
    [InlineData("-agentlib:jdwp", "jvm.native.module.unsupported")]
    [InlineData("-agentpath:C:\\agent.dll", "jvm.native.module.unsupported")]
    [InlineData("--module-path=foo", "jvm.native.module.unsupported")]
    [InlineData("-Xbootclasspath/a:foo", "jvm.native.module.unsupported")]
    public void ManagedOverridesRejected(string text, string code) => Assert.Contains(JvmArguments.Validate(text, 512, 2048, []).Issues, i => i.Code == code && i.Severity == Severity.Error);
    [Fact] public void MemoryConflictContainsBothSources() { var issue = Assert.Single(JvmArguments.Validate("-Xmx2g", 512, 2048, []).Issues); Assert.Equal(2, issue.Evidence.Length); }
    [Fact] public void MemoryMinMaxValidated() => Assert.Contains(JvmArguments.Validate("", 4096, 2048, []).Issues, i => i.Code == "memory.range");
    [Fact] public void ManagedPropertyConflict() => Assert.Contains(JvmArguments.Validate("-Dweave.dir=other", 512, 2048, ["-Dweave.dir=managed"]).Issues, i => i.Code == "jvm.property.conflict");
    [Fact] public void ExactManagedDuplicateResolved() { var result = JvmArguments.Validate("-Dweave.dir=managed", 512, 2048, ["-Dweave.dir=managed"]); Assert.Empty(result.Tokens); Assert.Contains(result.Issues, i => i.Code == "jvm.property.duplicate"); }
    [Theory] [InlineData("JAVA_TOOL_OPTIONS")] [InlineData("_JAVA_OPTIONS")] [InlineData("JDK_JAVA_OPTIONS")]
    public void InheritedEnvironmentConflicts(string name) { var plan = CompatibilityPlanner.Build(Request() with { Environment = [new(name, "-Xmx8g")] }); Error(plan, "environment.inherited"); Assert.Contains(plan.ArgumentProvenance, p => p.Source == "environment." + name); Assert.Single(plan.ChildEnvironmentPolicy.Inherited); }
    [Fact] public void StablePlanAndCanonicalIdentity()
    {
        var request = Request(); var first = CompatibilityPlanner.Build(request); var second = CompatibilityPlanner.Build(request);
        Assert.Equal(first.PlanId, second.PlanId); Assert.Equal(PlanSerialization.Canonical(first), PlanSerialization.Canonical(second)); Assert.Empty(first.Errors);
    }
    [Fact] public void ArtifactChangeInvalidatesFrozenPlan()
    {
        var request = Request(); var before = CompatibilityPlanner.Build(request); var artifact = request.ClassPath[0];
        File.AppendAllText(artifact.CanonicalPath, "change");
        Assert.Contains(ArtifactSnapshots.Validate(before.ArtifactFingerprints), i => i.Code == "artifact.changed");
        var after = CompatibilityPlanner.Build(request with { ClassPath = [ArtifactSnapshots.Capture(artifact.CanonicalPath, "classpath", false)] });
        Assert.NotEqual(before.PlanId, after.PlanId); Assert.NotEqual(before.RuntimeSnapshotId, after.RuntimeSnapshotId);
    }
    [Fact] public void DiagnosticsRedactSecretValues()
    {
        var plan = CompatibilityPlanner.Build(Request() with { UserJvmArgumentsText = "-DaccessToken=SENTINEL_A -Dpassword=SENTINEL_B", Environment = [new("JAVA_TOOL_OPTIONS", "-Dcredential=SENTINEL_C")], GenesisGameArgs = ["--accessToken", "SENTINEL_D"] });
        var diagnostics = PlanSerialization.Diagnostics(plan);
        foreach (var secret in new[] { "SENTINEL_A", "SENTINEL_B", "SENTINEL_C", "SENTINEL_D" }) Assert.DoesNotContain(secret, diagnostics);
        Assert.Contains("contract", diagnostics);
    }
    [Theory] [InlineData("1.8.9", "[1.8,1.9)", true)] [InlineData("1.9", "[1.8,1.9)", false)] [InlineData("17", ">=8 <=17", true)]
    public void VersionRangesAreChecked(string version, string range, bool expected) => Assert.Equal(expected, VersionRanges.Matches(version, range));
    [Fact] public void BenignClasspathPrefixPropertyAllowed() => Assert.Empty(JvmArguments.Validate("-Djava.class.pathology=foo", 512, 2048, []).Issues);
    [Fact] public void ConflictingUserPropertiesRejected() => Assert.Contains(JvmArguments.Validate("-Dfoo=one -Dfoo=two", 512, 2048, []).Issues, i => i.Code == "jvm.user.property.duplicate" && i.Severity == Severity.Error);
    [Fact] public void ConflictingCollectorsRejected() => Assert.Contains(JvmArguments.Validate("-XX:+UseSerialGC", 512, 2048, ["-XX:+UseG1GC"]).Issues, i => i.Code == "jvm.gc.conflict");
    [Fact] public void FabricJavaDependencyValidated()
    {
        var artifact = ArtifactSnapshots.Capture(Jar("fabric-java", Text("fabric.mod.json", "{\"id\":\"java-mod\",\"depends\":{\"java\":\">=21\"}}")), "fixture");
        Error(CompatibilityPlanner.Build(Request() with { Packages = [new("fabric", artifact)] }), "java.auto.unavailable");
    }
    [Fact] public void LoaderGenerationCannotBeGuessed()
    {
        var artifact = AgentArtifact();
        Error(CompatibilityPlanner.Build(Request() with { Agents = [Slot("loader", artifact, role: "WeaveLoader")], Weave = new(WeaveStrategy.ExternalAgent, artifact, "0.2.6", WeaveGeneration.Legacy, [], root) }), "weave.loader.generation.unverified");
    }
    [Fact] public void MissingOrderingTargetRejected()
    {
        var artifact = AgentArtifact(); Error(CompatibilityPlanner.Build(Request() with { Agents = [Slot("a", artifact, before: ["absent"])] }), "agent.order.missing");
    }
    [Fact] public void OffRejectsActiveLoader()
    {
        var artifact = AgentArtifact(); Error(CompatibilityPlanner.Build(Request() with { Agents = [Slot("loader", artifact, role: "WeaveLoader")] }), "weave.loader.count");
    }
    [Fact] public void MissingStartupMarkerRejected()
    {
        var artifact = ArtifactSnapshots.Capture(Jar("attach-only", Text("META-INF/MANIFEST.MF", "Agent-Class: sample.Agent\n\n")), "fixture");
        Error(CompatibilityPlanner.Build(Request() with { Agents = [Slot("a", artifact)] }), "agent.premain.missing");
    }
    [Fact] public void CanonicalIdentityOmitsSecretsButPreservesNonSessionGameArgs()
    {
        var request = Request();
        var first = CompatibilityPlanner.Build(request with { GenesisGameArgs = ["--version", "1.8.9", "--accessToken", "SECRET_ONE"] });
        var changedSecret = CompatibilityPlanner.Build(request with { GenesisGameArgs = ["--version", "1.8.9", "--accessToken", "SECRET_TWO"] });
        var changedVersion = CompatibilityPlanner.Build(request with { GenesisGameArgs = ["--version", "1.12.2", "--accessToken", "SECRET_ONE"] });
        Assert.Equal(first.PlanId, changedSecret.PlanId); Assert.NotEqual(first.PlanId, changedVersion.PlanId);
        Assert.DoesNotContain("SECRET_ONE", PlanSerialization.Canonical(first));
    }
    [Fact] public void DiagnosticsRedactQuotedSecretsWithSpaces()
    {
        var plan = CompatibilityPlanner.Build(Request() with { UserJvmArgumentsText = "-Dpassword=\"secret first second\"" });
        var output = PlanSerialization.Diagnostics(plan); Assert.DoesNotContain("secret first second", output); Assert.DoesNotContain("first second", output);
    }
    [Fact] public void JavaMetadataUnknownIsExplicit()
    {
        var java = Java(); File.Delete(Path.Combine(java.RuntimeRoot, "release"));
        var result = JavaRuntimes.Resolve(JavaRuntimeMode.Custom, java.Executable, [], new(17, null, "x64"));
        Assert.Contains(result.Issues, i => i.Code == "java.metadata.unknown" && i.Severity == Severity.Error);
    }
    [Fact] public void AgentOrderIndependentOfInputEnumeration()
    {
        var first = AgentArtifact("a"); var second = AgentArtifact("b", WeaveGeneration.Legacy); var request = Request();
        var a = Slot("a", first, 2); var b = Slot("b", second, 1);
        Assert.Equal(CompatibilityPlanner.Build(request with { Agents = [a, b] }).PlanId, CompatibilityPlanner.Build(request with { Agents = [b, a] }).PlanId);
    }
    [Fact] public void LoaderRequirementParticipatesInJavaResolution()
    {
        var artifact = AgentArtifact("loader", WeaveGeneration.Legacy) with { Source = "managed.weave" };
        var requirement = new PackageRequirements(PackageType.JavaAgent, null, 21, WeaveGeneration.Legacy, null, null, [], true, []);
        Error(CompatibilityPlanner.Build(Request() with { Agents = [Slot("loader", artifact, role: "WeaveLoader")], Weave = new(WeaveStrategy.Bundled, artifact, "1", WeaveGeneration.Legacy, [requirement], root) }), "java.auto.unavailable");
    }
    [Fact] public void DisabledPackageCannotLeakIntoAgentSlots()
    {
        var artifact = AgentArtifact(); var plan = CompatibilityPlanner.Build(Request() with { Packages = [new("disabled", artifact, false)], Agents = [Slot("disabled", artifact)] });
        Assert.Empty(plan.Agents); Assert.Empty(plan.EnabledPackages);
    }
    [Fact] public void BundledLoaderRequiresManagedProvenance()
    {
        var artifact = AgentArtifact("loader", WeaveGeneration.Legacy);
        Error(CompatibilityPlanner.Build(Request() with { Agents = [Slot("loader", artifact, role: "WeaveLoader")], Weave = new(WeaveStrategy.Bundled, artifact, "1", WeaveGeneration.Legacy, [], root) }), "weave.bundled.provenance");
    }
    [Fact] public void StandaloneAgentClassfileMinimumIsEnforced()
    {
        var artifact = ArtifactSnapshots.Capture(Jar("java21", Text("META-INF/MANIFEST.MF", "Premain-Class: sample.Agent\n\n"), ("sample/Agent.class", Class(major: 65))), "fixture");
        Error(CompatibilityPlanner.Build(Request() with { Agents = [Slot("agent", artifact)] }), "java.auto.unavailable");
    }
    [Fact] public void StaleJavaMetadataCannotEnterPlan()
    {
        var request = Request(); File.WriteAllText(Path.Combine(request.JavaCandidates[0].RuntimeRoot, "release"), "JAVA_VERSION=\"8.0.1\"\nOS_ARCH=\"amd64\"\n");
        Error(CompatibilityPlanner.Build(request), "java.metadata.changed");
    }
    [Fact] public void FrozenJavaReleaseMustValidate()
    {
        var request = Request(); var plan = CompatibilityPlanner.Build(request);
        File.WriteAllText(Path.Combine(request.JavaCandidates[0].RuntimeRoot, "release"), "JAVA_VERSION=\"21\"\n");
        Assert.Contains(ArtifactSnapshots.Validate(plan.ArtifactFingerprints), i => i.Code == "artifact.changed");
    }
    [Fact] public void AgentBootClasspathRequiresVerifiedContract()
    {
        var artifact = ArtifactSnapshots.Capture(Jar("boot-agent", Text("META-INF/MANIFEST.MF", "Premain-Class: sample.Agent\nBoot-Class-Path: hidden.jar\n\n"), ("sample/Agent.class", Class())), "fixture");
        Error(CompatibilityPlanner.Build(Request() with { Agents = [Slot("boot", artifact)] }), "agent.bootclasspath.unverified");
    }
    [Fact] public void MissingArtifactRequiresPlanRebuild()
    {
        var artifact = AgentArtifact(); File.Delete(artifact.CanonicalPath); Assert.Contains(ArtifactSnapshots.Validate([artifact]), i => i.Code == "artifact.missing");
    }
    [Fact] public void InvalidMetadataIsNormalPreflightIssue()
    {
        var result = PackageInspector.Inspect(Jar("broken-json", Text("weave.mod.json", "{bad"))); Assert.Contains(result.Issues, i => i.Code == "package.invalid");
    }
    [Fact] public void OversizedMetadataIsBounded()
    {
        var result = PackageInspector.Inspect(Jar("oversized", Text("weave.mod.json", new string(' ', 2 * 1024 * 1024 + 1)))); Assert.Contains(result.Issues, i => i.Code == "package.invalid");
    }
    [Fact] public void MalformedClassDoesNotInventGeneration()
    {
        var result = PackageInspector.Inspect(Jar("bad-class", Text("weave.mod.json", "{}"), ("Mod.class", [0xca, 0xfe])));
        Assert.Equal(WeaveGeneration.Unknown, result.Requirements.WeaveGeneration); Assert.Contains(result.Requirements.Unverified, s => s.Contains("malformed"));
    }
    [Fact] public void UnknownRangeCannotSilentlyPass() => Error(CompatibilityPlanner.Build(Request() with { Packages = [new("unknown-range", Mod("range", "{\"compiledFor\":\"maybe-compatible\"}"))] }), "package.minecraft.unverified");
    [Fact] public void PlanReportsThreePreflightStates()
    {
        var request = Request(); var valid = CompatibilityPlanner.Build(request); Assert.Equal(PreflightStatus.Valid, valid.Status);
        var warning = CompatibilityPlanner.Build(request with { UserJvmArgumentsText = "-XX:+UnverifiedOption" }); Assert.Equal(PreflightStatus.ValidWithWarnings, warning.Status);
        var invalid = CompatibilityPlanner.Build(request with { UserJvmArgumentsText = "-cp forbidden" }); Assert.Equal(PreflightStatus.Invalid, invalid.Status);
    }
    [Fact] public void AutoJavaConsidersDeclaredDependencyRange()
    {
        var artifact = ArtifactSnapshots.Capture(Jar("fabric-java-range", Text("fabric.mod.json", "{\"id\":\"java-mod\",\"depends\":{\"java\":\">=17\"}}")), "fixture");
        var request = Request(); var older = Java("8.0.1");
        var plan = CompatibilityPlanner.Build(request with { Packages = [new("fabric", artifact)], JavaCandidates = [older, request.JavaCandidates[0]] });
        Assert.Equal(17, plan.Java.Runtime!.Major); Assert.DoesNotContain(plan.Errors, i => i.Code.StartsWith("dependency.java", StringComparison.Ordinal));
    }
    [Fact] public void ConstantPoolDescriptorsProvideReferenceEvidence()
    {
        using var stream = new MemoryStream();
        void U1(int n) => stream.WriteByte((byte)n);
        void U2(int n) { U1(n >> 8); U1(n); }
        void Utf8(string value) { var bytes = Encoding.UTF8.GetBytes(value); U1(1); U2(bytes.Length); stream.Write(bytes); }
        U1(0xca); U1(0xfe); U1(0xba); U1(0xbe); U2(0); U2(52); U2(4);
        Utf8("method"); Utf8("(Lnet/weavemc/loader/api/Hook;)V"); U1(12); U2(1); U2(2);
        U2(1); U2(0); U2(0);
        var result = PackageInspector.Inspect(Jar("descriptor", Text("weave.mod.json", "{}"), ("Mod.class", stream.ToArray())));
        Assert.Equal(WeaveGeneration.Legacy, result.Requirements.WeaveGeneration);
    }
    [Fact] public void SplitSecretFlagsCannotLeakThroughIssueOrProvenance()
    {
        var plan = CompatibilityPlanner.Build(Request() with { UserJvmArgumentsText = "--password SENTINEL_SPLIT", Environment = [new("_JAVA_OPTIONS", "--authorization Bearer SENTINEL_AUTH")] });
        var output = PlanSerialization.Diagnostics(plan); Assert.DoesNotContain("SENTINEL_SPLIT", output); Assert.DoesNotContain("SENTINEL_AUTH", output);
    }
    [Fact] public void QuotedUncPathPreservesBackslashes()
    {
        var result = JvmArguments.Parse("-Dpath=\"\\\\server\\folder with spaces\\file\"");
        Assert.Equal("-Dpath=\\\\server\\folder with spaces\\file", Assert.Single(result.Tokens));
    }
    [Fact] public void QuotedTrailingBackslashUsesWindowsQuoteRules()
    {
        var result = JvmArguments.Parse("-Dpath=\"C:\\folder with spaces\\\\\"");
        Assert.Empty(result.Issues); Assert.Equal("-Dpath=C:\\folder with spaces\\", Assert.Single(result.Tokens));
    }
    [Theory]
    [InlineData("fabric.mod.json", "[]")]
    [InlineData("fabric.mod.json", "null")]
    [InlineData("fabric.mod.json", "{\"depends\":\"bad\"}")]
    [InlineData("mcmod.info", "\"bad\"")]
    public void InvalidMetadataShapeReturnsIssue(string name, string json)
    {
        Assert.Contains(PackageInspector.Inspect(Jar("shape", Text(name, json))).Issues, i => i.Code == "package.invalid");
    }
    [Fact] public void StandaloneCustomExecutableUsesItsMetadataDirectory()
    {
        var directory = Path.Combine(root, "standalone"); Directory.CreateDirectory(directory);
        var executable = Path.Combine(directory, "java.exe"); File.WriteAllBytes(executable, [1, 2, 3]);
        File.WriteAllText(Path.Combine(directory, "release"), "JAVA_VERSION=\"17.0.3\"\nOS_ARCH=\"amd64\"\n");
        var result = JavaRuntimes.Resolve(JavaRuntimeMode.Custom, executable, [], new(17, 17, "x64"));
        Assert.Equal(PreflightStatus.Valid, result.Compatibility); Assert.Equal(directory, result.Runtime!.RuntimeRoot);
    }
}
