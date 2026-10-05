using System.Collections.Immutable;
namespace Moonrise.Compatibility;

public enum PackageType { Auto, JavaAgent, WeaveMod, ForgeMod, FabricMod, Unknown }
public enum WeaveGeneration { Unknown, Legacy, Current }
public enum Backend { OfficialLunar, ControlledGenesis }
public enum JavaRuntimeMode { Auto, Custom }
public enum WeaveStrategy { Off, Bundled, ExternalAgent }
public enum Severity { Warning, Error }
public enum PreflightStatus { Valid, ValidWithWarnings, Invalid }
public enum ResolutionState { Resolved, Unknown, Delegated }
public sealed record Evidence(string Source, string Detail);
public sealed record Issue(string Code, Severity Severity, string Message, string? Affected = null, ImmutableArray<Evidence> Evidence = default)
{
    public ImmutableArray<Evidence> Evidence { get; init; } = Evidence.IsDefault ? [new("preflight", Code)] : Evidence;
}
public sealed record Dependency(string Id, string? Range, bool Required = true);
public sealed record PackageRequirements(PackageType Type, string? MinecraftRange, int? MinimumJava,
    WeaveGeneration WeaveGeneration, string? WeaveVersionRange, string? BaseLoader,
    ImmutableArray<Dependency> Dependencies, bool StartupAgent, ImmutableArray<string> Unverified, string? BaseLoaderVersionRange = null);
public sealed record UserOverride(PackageType Type = PackageType.Auto);
public sealed record PackageInspection(ImmutableArray<PackageType> Capabilities, PackageRequirements Requirements,
    ImmutableSortedDictionary<string, string> Metadata, ImmutableArray<Evidence> Evidence, ImmutableArray<Issue> Issues);
public sealed record ArtifactSnapshot(string CanonicalPath, long Size, string Sha256, string Source, PackageInspection? Inspection = null);
public sealed record PackageInput(string Id, ArtifactSnapshot Artifact, bool Enabled = true, UserOverride? Override = null);
public sealed record BackendCapabilities(bool Java, bool ClassPath, bool OrderedAgents, bool JvmArgs, bool GameArgs, bool ProcessOwnership)
{
    public static BackendCapabilities For(Backend backend) => backend == Backend.ControlledGenesis
        ? new(true, true, true, true, true, true) : new(false, false, false, false, false, false);
}
public sealed record PlanField<T>(ResolutionState State, T? Value, string Reason);
public sealed record BaseLoader(string Id, string? Version, ImmutableArray<Evidence> Evidence);
public sealed record JavaCandidate(string Executable, string RuntimeRoot, string? Version, string? Vendor, string? Architecture,
    int? Major, ImmutableArray<Evidence> Evidence);
public sealed record JavaRequirement(int? Minimum = null, int? Maximum = null, string? Architecture = null);
public sealed record JavaResolution(JavaRuntimeMode Mode, ResolutionState State, JavaCandidate? Runtime,
    PreflightStatus Compatibility, ImmutableArray<Issue> Issues);
public sealed record Agent(string Id, ArtifactSnapshot Artifact, string Role, bool Enabled, int Order, string? Options,
    ImmutableArray<string> Before, ImmutableArray<string> After);
public sealed record WeaveSelection(WeaveStrategy Strategy, ArtifactSnapshot? Artifact, string? Version,
    WeaveGeneration Generation, ImmutableArray<PackageRequirements> Requirements, string? ModDirectory, ImmutableArray<Evidence> Evidence = default)
{
    public ImmutableArray<Evidence> Evidence { get; init; } = Evidence.IsDefault ? [] : Evidence;
}
public sealed record ArgumentProvenance(string Argument, string Source, string Decision);
public sealed record EnvironmentOption(string Name, string Value);
public sealed record ChildEnvironmentPolicy(string Action, ImmutableArray<EnvironmentOption> Inherited);
public sealed record LaunchPlan(
    int SchemaVersion, string PlanId, string ContractId, string RuntimeSnapshotId, string MinecraftVersion, string LunarProfileId,
    BaseLoader BaseLoader, Backend Backend, BackendCapabilities BackendCapabilities, JavaResolution Java,
    PlanField<string> WorkingDirectory, PlanField<string> GameDirectory, ImmutableArray<ArtifactSnapshot> NativeArtifacts,
    PlanField<string> AssetIndex, PlanField<ImmutableArray<ArtifactSnapshot>> ClassPath,
    PlanField<ImmutableArray<ArtifactSnapshot>> IchorClassPath, PlanField<ImmutableArray<ArtifactSnapshot>> IchorExternalFiles,
    ImmutableArray<string> RequiredJvmArgs, ImmutableArray<string> MemoryRuntimeArgs, string UserJvmArgumentsText,
    ImmutableArray<string> UserJvmArgs, ImmutableArray<Agent> Agents, WeaveSelection Weave,
    ImmutableArray<PackageInput> EnabledPackages, ImmutableArray<PackageInput> EnabledWeaveMods,
    PlanField<string> MainClass, PlanField<ImmutableArray<string>> GenesisGameArgs, ChildEnvironmentPolicy ChildEnvironmentPolicy,
    ImmutableArray<ArtifactSnapshot> ArtifactFingerprints, ImmutableArray<Issue> Errors, ImmutableArray<Issue> Warnings,
    ImmutableArray<ArgumentProvenance> ArgumentProvenance)
{
    public PreflightStatus Status => !Errors.IsEmpty ? PreflightStatus.Invalid : !Warnings.IsEmpty ? PreflightStatus.ValidWithWarnings : PreflightStatus.Valid;
}
public sealed record PlanRequest(string ContractId, string MinecraftVersion, string ProfileId, Backend Backend,
    BaseLoader BaseLoader, ImmutableArray<PackageInput> Packages, ImmutableArray<Agent> Agents, WeaveSelection Weave,
    JavaRuntimeMode JavaMode, string? CustomJava, ImmutableArray<JavaCandidate> JavaCandidates, JavaRequirement JavaRequirement,
    int MinimumMemoryMb, int MaximumMemoryMb, string UserJvmArgumentsText,
    ImmutableArray<string> RequiredJvmArgs, ImmutableArray<EnvironmentOption> Environment,
    string? WorkingDirectory = null, string? GameDirectory = null, string? MainClass = null, string? AssetIndex = null,
    ImmutableArray<ArtifactSnapshot> ClassPath = default, ImmutableArray<ArtifactSnapshot> IchorClassPath = default,
    ImmutableArray<ArtifactSnapshot> IchorExternalFiles = default, ImmutableArray<ArtifactSnapshot> NativeArtifacts = default,
    ImmutableArray<string> GenesisGameArgs = default);
