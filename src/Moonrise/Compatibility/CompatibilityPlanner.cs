using System.Collections.Immutable;
namespace Moonrise.Compatibility;

public static class CompatibilityPlanner
{
    public static LaunchPlan Build(PlanRequest request)
    {
        var issues = ImmutableArray.CreateBuilder<Issue>();
        var packages = request.Packages.Where(p => p.Enabled).OrderBy(p => p.Id, StringComparer.Ordinal).ToImmutableArray();
        var capabilities = BackendCapabilities.For(request.Backend);
        var minimumJava = request.JavaRequirement.Minimum;
        var packageGenerations = new HashSet<WeaveGeneration>();
        var mods = ImmutableArray.CreateBuilder<PackageInput>();
        foreach (var duplicate in packages.GroupBy(p => p.Id).Where(g => g.Count() > 1))
            issues.Add(new("package.duplicate", Severity.Error, "Enabled package IDs must be unique.", duplicate.Key));
        foreach (var package in packages)
        {
            var inspection = package.Artifact.Inspection;
            if (inspection == null) { issues.Add(new("package.uninspected", Severity.Error, "Enabled package has no inspection snapshot.", package.Id)); continue; }
            var type = package.Override is { Type: not PackageType.Auto } manual ? manual.Type : inspection.Requirements.Type;
            foreach (var issue in inspection.Issues)
                if (!(issue.Code == "package.ambiguous" && package.Override is { Type: not PackageType.Auto })) issues.Add(issue with { Affected = package.Id });
            if (type == PackageType.Unknown || type == PackageType.Auto)
                issues.Add(new("package.type.unknown", Severity.Error, "Package type requires an explicit supported interpretation.", package.Id));
            if (package.Override is { Type: not PackageType.Auto } && !inspection.Capabilities.Contains(type))
                issues.Add(new("package.override.unverified", Severity.Error, "Override lacks the required marker/evidence; validation remains mandatory.", package.Id));
            var requirement = inspection.Requirements;
            if (requirement.MinimumJava != null) minimumJava = Math.Max(minimumJava ?? 0, requirement.MinimumJava.Value);
            CheckRange(request.MinecraftVersion, requirement.MinecraftRange, "package.minecraft", package.Id, issues);
            foreach (var unknown in requirement.Unverified) issues.Add(new("package.requirement.unverified", Severity.Warning, "Unverified requirement: " + unknown, package.Id));
            if (type == PackageType.WeaveMod)
            {
                mods.Add(package);
                packageGenerations.Add(requirement.WeaveGeneration);
                if (requirement.WeaveGeneration == WeaveGeneration.Unknown)
                    issues.Add(new("weave.requirement.unknown", Severity.Error, "Weave generation cannot be proven; supply a verified contract before activation.", package.Id, inspection.Evidence));
                CheckRange(request.Weave.Version ?? "", requirement.WeaveVersionRange, "weave.version", package.Id, issues);
            }
            if (type is PackageType.FabricMod or PackageType.ForgeMod)
            {
                var needed = type == PackageType.FabricMod ? "fabric" : "forge";
                if (request.BaseLoader.Id != needed) issues.Add(new("loader.mismatch", Severity.Error, "Package requires base loader " + needed, package.Id));
                issues.Add(new("loader.external.mods.unverified", Severity.Error, "External mod activation is not verified by a Stage 1 backend contract.", package.Id));
            }
            foreach (var dependency in requirement.Dependencies.Where(d => d.Required))
            {
                string? version = dependency.Id switch
                {
                    "minecraft" => request.MinecraftVersion,
                    "fabricloader" when request.BaseLoader.Id == "fabric" => request.BaseLoader.Version,
                    "forge" when request.BaseLoader.Id == "forge" => request.BaseLoader.Version,
                    "java" => null,
                    _ => FindDependencyVersion(packages, dependency.Id)
                };
                if (dependency.Id == "java") continue; // handled as explicitly unverified metadata, never guessed
                if (version == null && !packages.Any(p => MatchesId(p, dependency.Id)))
                    issues.Add(new("dependency.missing", Severity.Error, "Missing required dependency: " + dependency.Id, package.Id));
                else CheckRange(version ?? "", dependency.Range, "dependency.version", package.Id, issues);
            }
        }
        if (request.Weave.Strategy != WeaveStrategy.Off)
            foreach (var requirement in request.Weave.Requirements)
            {
                CheckRange(request.MinecraftVersion, requirement.MinecraftRange, "weave.contract.minecraft", "weave", issues);
                CheckRange(request.Weave.Version ?? "", requirement.WeaveVersionRange, "weave.contract.version", "weave", issues);
                if (requirement.MinimumJava != null) minimumJava = Math.Max(minimumJava ?? 0, requirement.MinimumJava.Value);
                if (requirement.WeaveGeneration != WeaveGeneration.Unknown) packageGenerations.Add(requirement.WeaveGeneration);
            }
        var ordered = OrderAgents(request.Agents.Where(a => a.Enabled && !request.Packages.Any(p => !p.Enabled && (p.Id == a.Id || p.Artifact.CanonicalPath == a.Artifact.CanonicalPath))).ToImmutableArray(), issues);
        foreach (var agent in ordered)
        {
            if (agent.Artifact.Inspection is not { Requirements.StartupAgent: true } inspection)
                issues.Add(new("agent.premain.missing", Severity.Error, "Structured agent requires inspected Premain-Class evidence.", agent.Id));
            else
            {
                issues.AddRange(inspection.Issues.Where(i => i.Severity == Severity.Error && (i.Code != "package.ambiguous" || !packages.Any(p => p.Artifact == agent.Artifact && p.Override is { Type: PackageType.JavaAgent }))).Select(i => i with { Affected = agent.Id }));
                if (inspection.Requirements.MinimumJava != null) minimumJava = Math.Max(minimumJava ?? 0, inspection.Requirements.MinimumJava.Value);
                if (inspection.Metadata.ContainsKey("Boot-Class-Path")) issues.Add(new("agent.bootclasspath.unverified", Severity.Error, "Agent Boot-Class-Path requires a verified artifact dependency contract.", agent.Id));
            }
        }
        foreach (var package in packages.Where(p => (p.Override?.Type is null or PackageType.Auto ? p.Artifact.Inspection?.Requirements.Type : p.Override.Type) == PackageType.JavaAgent))
            if (ordered.Count(a => a.Artifact.Sha256 == package.Artifact.Sha256 && a.Artifact.CanonicalPath == package.Artifact.CanonicalPath) != 1)
                issues.Add(new("agent.package.slot", Severity.Error, "Enabled startup agent package requires exactly one structured agent slot.", package.Id));
        var weaveAgents = ordered.Where(a => a.Role == "WeaveLoader").ToArray();
        var expected = request.Weave.Strategy == WeaveStrategy.Off ? 0 : 1;
        if (request.Weave.Strategy == WeaveStrategy.Bundled && request.Weave.Artifact?.Source.StartsWith("managed", StringComparison.Ordinal) != true)
            issues.Add(new("weave.bundled.provenance", Severity.Error, "Bundled loader requires explicit managed artifact provenance."));
        if (weaveAgents.Length != expected || expected == 1 && request.Weave.Artifact == null)
            issues.Add(new("weave.loader.count", Severity.Error, "Weave Off requires zero loaders; active strategy requires exactly one explicit loader artifact."));
        if (expected == 1 && weaveAgents.Length == 1 && weaveAgents[0].Artifact != request.Weave.Artifact)
            issues.Add(new("weave.loader.artifact", Severity.Error, "Weave slot and selected loader artifact differ."));
        if (request.Weave.Strategy == WeaveStrategy.Off && mods.Count > 0)
            issues.Add(new("weave.disabled", Severity.Error, "Enabled Weave mods require an active loader."));
        if (request.Weave.Strategy != WeaveStrategy.Off && request.Weave.Generation == WeaveGeneration.Unknown)
            issues.Add(new("weave.loader.unknown", Severity.Error, "Selected loader generation requires verified evidence."));
        var known = packageGenerations.Where(g => g != WeaveGeneration.Unknown).ToArray();
        if (known.Length > 1 || known.Any(g => request.Weave.Strategy != WeaveStrategy.Off && g != request.Weave.Generation))
            issues.Add(new("weave.generation.conflict", Severity.Error, "Conflicting legacy/current Weave requirements."));
        if (expected == 1 && request.Weave.Artifact?.Inspection is { } loaderInspection &&
            loaderInspection.Requirements.WeaveGeneration != request.Weave.Generation &&
            !request.Weave.Evidence.Any(e => e.Source == "contract.weave.generation"))
            issues.Add(new("weave.loader.generation.unverified", Severity.Error, "Loader generation lacks matching inspection or verified contract evidence."));
        var javaDependencies = packages.SelectMany(p => p.Artifact.Inspection?.Requirements.Dependencies ?? [])
            .Where(d => d.Required && d.Id == "java").ToArray();
        var candidates = request.JavaCandidates.Where(c => javaDependencies.All(d =>
            VersionRanges.Matches(c.Major?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "", d.Range) != false)).ToImmutableArray();
        var java = JavaRuntimes.Resolve(request.JavaMode, request.CustomJava, candidates, request.JavaRequirement with { Minimum = minimumJava });
        issues.AddRange(java.Issues);
        foreach (var package in packages)
            foreach (var dependency in package.Artifact.Inspection?.Requirements.Dependencies.Where(d => d.Id == "java" && d.Required) ?? [])
                CheckRange(java.Runtime?.Major?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "", dependency.Range, "dependency.java", package.Id, issues);
        foreach (var package in packages.Where(p => p.Artifact.Inspection?.Requirements.BaseLoaderVersionRange != null))
            CheckRange(request.BaseLoader.Version ?? "", package.Artifact.Inspection!.Requirements.BaseLoaderVersionRange, "loader.version", package.Id, issues);
        if (!capabilities.Java)
        {
            if (request.JavaMode == JavaRuntimeMode.Custom) issues.Add(new("backend.java.unsupported", Severity.Error, "Custom Java is unsupported/unverified by OfficialLunar."));
            var delegated = new Issue("backend.java.delegated", Severity.Warning, "Final Java executable/version is delegated to OfficialLunar; inspected candidates do not prove its choice.");
            java = java with
            {
                State = ResolutionState.Delegated,
                Runtime = null,
                Compatibility = java.Compatibility == PreflightStatus.Invalid ? PreflightStatus.Invalid : PreflightStatus.ValidWithWarnings,
                Issues = java.Issues.Add(delegated)
            };
            issues.Add(delegated);
        }
        var args = JvmArguments.Validate(request.UserJvmArgumentsText, request.MinimumMemoryMb, request.MaximumMemoryMb, request.RequiredJvmArgs);
        issues.AddRange(args.Issues);
        if (!capabilities.JvmArgs && (!request.RequiredJvmArgs.IsEmpty || !args.Tokens.IsEmpty || request.MinimumMemoryMb > 0 || request.MaximumMemoryMb > 0))
            issues.Add(new("backend.jvm.unsupported", Severity.Error, "Managed JVM arguments/memory cannot be guaranteed by current OfficialLunar."));
        if (!capabilities.OrderedAgents && !ordered.IsEmpty)
            issues.Add(new("backend.agent.order.unverified", Severity.Warning, "OfficialLunar does not control final ordering relative to its own agents/environment."));
        var provenance = args.Provenance.ToBuilder();
        foreach (var env in request.Environment.OrderBy(e => e.Name, StringComparer.Ordinal))
        {
            if (string.IsNullOrWhiteSpace(env.Value)) continue;
            var inherited = JvmArguments.Validate(env.Value, request.MinimumMemoryMb, request.MaximumMemoryMb, request.RequiredJvmArgs, "environment." + env.Name);
            issues.AddRange(inherited.Issues); provenance.AddRange(inherited.Provenance);
            issues.Add(new("environment.inherited", Severity.Error, "Inherited JVM options can change the managed launch; explicit resolution is required.", env.Name));
        }
        foreach (var arg in request.RequiredJvmArgs) provenance.Add(new(arg, "contract.requiredJvmArgs", "required"));
        ImmutableArray<string> memory = [$"-Xms{request.MinimumMemoryMb}m", $"-Xmx{request.MaximumMemoryMb}m"];
        foreach (var arg in memory) provenance.Add(new(arg, "settings.memory", "managed"));
        var artifacts = packages.Select(p => p.Artifact).Concat(ordered.Select(a => a.Artifact))
            .Concat(Safe(request.NativeArtifacts)).Concat(Safe(request.ClassPath)).Concat(Safe(request.IchorClassPath)).Concat(Safe(request.IchorExternalFiles)).ToList();
        if (request.Weave.Strategy != WeaveStrategy.Off && request.Weave.Artifact != null) artifacts.Add(request.Weave.Artifact);
        // Freeze the selected Java executable and its release metadata as runtime artifacts.
        var runtime = capabilities.Java ? java.Runtime : null;
        if (runtime != null)
        {
            try
            {
                artifacts.Add(ArtifactSnapshots.Capture(runtime.Executable, "java.executable", false));
                var release = Path.Combine(runtime.RuntimeRoot, "release");
                if (File.Exists(release)) artifacts.Add(ArtifactSnapshots.Capture(release, "java.release", false));
                var actual = JavaRuntimes.Inspect(runtime.Executable);
                if (actual == null || actual.Version != runtime.Version || actual.Major != runtime.Major || actual.Vendor != runtime.Vendor || actual.Architecture != runtime.Architecture || actual.RuntimeRoot != runtime.RuntimeRoot)
                    issues.Add(new("java.metadata.changed", Severity.Error, "Java metadata changed after discovery; rebuild runtime snapshot.", runtime.Executable));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { issues.Add(new("java.snapshot", Severity.Error, "Java runtime could not be snapshotted.")); }
        }
        var fingerprints = artifacts.DistinctBy(a => (a.CanonicalPath, a.Sha256)).OrderBy(a => a.CanonicalPath, StringComparer.Ordinal).ThenBy(a => a.Sha256, StringComparer.Ordinal).ToImmutableArray();
        issues.AddRange(ArtifactSnapshots.Validate(fingerprints));
        PlanField<T> Field<T>(T value, bool supported, string name) => new(supported ? ResolutionState.Resolved : ResolutionState.Delegated, value, supported ? "contract." + name : "Delegated to OfficialLunar");
        PlanField<string> TextField(string? value, bool supported, string name)
        {
            if (supported && string.IsNullOrWhiteSpace(value)) issues.Add(new("contract.field.missing", Severity.Error, "Verified contract must supply " + name, name));
            return new(!supported ? ResolutionState.Delegated : value == null ? ResolutionState.Unknown : ResolutionState.Resolved, supported ? value : null, supported ? "contract." + name : "Delegated to OfficialLunar");
        }
        var working = TextField(request.WorkingDirectory, capabilities.ProcessOwnership, "workingDirectory");
        var game = TextField(request.GameDirectory, capabilities.GameArgs, "gameDirectory");
        var main = TextField(request.MainClass, capabilities.GameArgs, "mainClass");
        var asset = TextField(request.AssetIndex, capabilities.GameArgs, "assetIndex");
        if (capabilities.ClassPath && Safe(request.ClassPath).IsEmpty) issues.Add(new("contract.classpath.empty", Severity.Error, "Controlled contract requires a verified classpath."));
        var frozenIssues = issues.OrderBy(i => i.Code, StringComparer.Ordinal).ThenBy(i => i.Affected, StringComparer.Ordinal).ThenBy(i => i.Message, StringComparer.Ordinal).ToImmutableArray();
        var plan = new LaunchPlan(1, "", request.ContractId, PlanSerialization.SnapshotId(fingerprints), request.MinecraftVersion, request.ProfileId,
            request.BaseLoader, request.Backend, capabilities, java, working, game, Safe(request.NativeArtifacts), asset,
            Field(capabilities.ClassPath ? Safe(request.ClassPath) : [], capabilities.ClassPath, "classPath"), Field(capabilities.ClassPath ? Safe(request.IchorClassPath) : [], capabilities.ClassPath, "ichorClassPath"),
            Field(capabilities.ClassPath ? Safe(request.IchorExternalFiles) : [], capabilities.ClassPath, "ichorExternalFiles"), request.RequiredJvmArgs, memory, request.UserJvmArgumentsText,
            args.Tokens, ordered, request.Weave with { Requirements = request.Weave.Requirements.AddRange(mods.Select(p => p.Artifact.Inspection!.Requirements)) }, packages, mods.ToImmutable(),
            main, Field(capabilities.GameArgs ? Safe(request.GenesisGameArgs) : [], capabilities.GameArgs, "genesisGameArgs"), new("RequireExplicitResolutionBeforeExecution", request.Environment.OrderBy(e => e.Name, StringComparer.Ordinal).ToImmutableArray()),
            fingerprints, frozenIssues.Where(i => i.Severity == Severity.Error).ToImmutableArray(), frozenIssues.Where(i => i.Severity == Severity.Warning).ToImmutableArray(), provenance.ToImmutable());
        return plan with { PlanId = PlanSerialization.Hash(PlanSerialization.Canonical(plan)) };
    }
    private static ImmutableArray<T> Safe<T>(ImmutableArray<T> values) => values.IsDefault ? [] : values;
    private static bool MatchesId(PackageInput p, string id) => p.Id == id || p.Artifact.Inspection?.Metadata.Any(k =>
        (k.Key is "weave.id" or "weave.modId" or "fabric.id" || k.Key.EndsWith(".modId", StringComparison.Ordinal) || k.Key.EndsWith(".modid", StringComparison.Ordinal)) && k.Value.Trim('"') == id) == true;
    private static string? FindDependencyVersion(ImmutableArray<PackageInput> packages, string id) => packages.FirstOrDefault(p => MatchesId(p, id))?.Artifact.Inspection?.Metadata
        .FirstOrDefault(k => k.Key.EndsWith(".version", StringComparison.Ordinal)).Value?.Trim('"');
    private static void CheckRange(string version, string? range, string code, string affected, ImmutableArray<Issue>.Builder issues)
    {
        if (range == null) return;
        var compatible = VersionRanges.Matches(version, range);
        if (compatible != true) issues.Add(new(code + (compatible == false ? ".conflict" : ".unverified"), Severity.Error,
            compatible == false ? "Version " + version + " does not satisfy " + range : "Version range cannot be verified: " + range, affected,
            [new("requirement", range), new("contract", version)]));
    }
    private static ImmutableArray<Agent> OrderAgents(ImmutableArray<Agent> agents, ImmutableArray<Issue>.Builder issues)
    {
        if (agents.Select(a => a.Id).Distinct(StringComparer.Ordinal).Count() != agents.Length)
        { issues.Add(new("agent.id.duplicate", Severity.Error, "Agent IDs must be unique.")); return []; }
        foreach (var duplicate in agents.GroupBy(a => a.Artifact.Sha256).Where(g => g.Count() > 1))
            issues.Add(new("agent.artifact.duplicate", Severity.Error, "Same agent artifact is active more than once.", duplicate.First().Id));
        var edges = agents.ToDictionary(a => a.Id, _ => new HashSet<string>(StringComparer.Ordinal), StringComparer.Ordinal);
        void Edge(string from, string to)
        {
            if (!edges.ContainsKey(from) || !edges.ContainsKey(to)) { issues.Add(new("agent.order.missing", Severity.Error, "Ordering constraint references a missing/disabled agent.", from + " -> " + to)); return; }
            edges[from].Add(to);
        }
        foreach (var agent in agents)
        {
            foreach (var before in Safe(agent.Before)) Edge(agent.Id, before);
            foreach (var after in Safe(agent.After)) Edge(after, agent.Id);
        }
        var remaining = agents.ToList(); var result = ImmutableArray.CreateBuilder<Agent>();
        while (remaining.Count > 0)
        {
            var next = remaining.Where(a => !remaining.Any(other => edges[other.Id].Contains(a.Id))).OrderBy(a => a.Order).ThenBy(a => a.Id, StringComparer.Ordinal).FirstOrDefault();
            if (next == null) { issues.Add(new("agent.order.cycle", Severity.Error, "Agent ordering constraints contain a cycle.")); break; }
            result.Add(next); remaining.Remove(next);
        }
        return result.ToImmutable();
    }
}
