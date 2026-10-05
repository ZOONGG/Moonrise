using System.Collections.Immutable;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace Moonrise.Compatibility;

public sealed record ControlledGenesisProcessIdentity(int ProcessId, DateTimeOffset StartTimeUtc,
    string Executable, string PlanId, string SessionId, string CommandProvenance);
public sealed record ControlledGenesisLaunch(Process Process, ControlledGenesisProcessIdentity Identity,
    string StdoutPath, string StderrPath, string PlanPath, string SessionDirectory) : IDisposable
{
    public string? OwnedResourceDirectory { get; init; }
    public string? ResourceParentDirectory { get; init; }
    public void Dispose() => Process.Dispose();
}

/// <summary>Spawns the exact frozen ControlledGenesis plan and owns only its returned process.</summary>
public sealed class ControlledGenesisExecutor
{
    private static readonly Regex Sensitive = new("token|session|password|authorization|credential|secret", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private readonly string _sessionRoot;

    public ControlledGenesisExecutor(string sessionRoot) => _sessionRoot = Path.GetFullPath(sessionRoot);

    public ImmutableArray<string> BuildArguments(LaunchPlan plan)
    {
        Validate(plan);
        var java = plan.Java.Runtime!;
        var args = ImmutableArray.CreateBuilder<string>();
        args.AddRange(plan.RequiredJvmArgs);
        args.AddRange(plan.MemoryRuntimeArgs);
        args.AddRange(plan.UserJvmArgs);
        foreach (var agent in plan.Agents)
        {
            var value = $"-javaagent:{agent.Artifact.CanonicalPath}";
            if (!string.IsNullOrWhiteSpace(agent.Options)) value += "=" + agent.Options;
            args.Add(value);
        }
        var lwjgl = plan.NativeArtifacts.First(a => a.Source.EndsWith("lwjgl64.dll", StringComparison.OrdinalIgnoreCase));
        if (!plan.RequiredJvmArgs.Any(a => a.StartsWith("-Djava.library.path=", StringComparison.Ordinal)))
            args.Add("-Djava.library.path=" + Path.GetDirectoryName(lwjgl.CanonicalPath));
        args.Add("-Dichor.logsFile=" + Path.Combine(plan.WorkingDirectory.Value!, ".ichor", "genesis.log"));
        args.Add("-cp");
        args.Add(string.Join(Path.PathSeparator, plan.ClassPath.Value.Select(a => a.CanonicalPath)));
        args.Add(plan.MainClass.Value!);
        args.Add("--version"); args.Add("1.8.9");
        args.Add("--classpathDir"); args.Add(plan.RuntimeFileDirectory ?? Path.GetDirectoryName(plan.ClassPath.Value.First(a => a.Source == "lunar.genesis").CanonicalPath)!);
        args.Add("--workingDirectory"); args.Add(plan.WorkingDirectory.Value!);
        args.Add("--gameDir"); args.Add(plan.GameDirectory.Value!);
        args.Add("--assetsDir"); args.Add(Path.Combine(plan.GameDirectory.Value!, "assets"));
        args.Add("--assetIndex"); args.Add("1.8");
        args.Add("--width"); args.Add("854");
        args.Add("--height"); args.Add("480");
        if (!plan.IchorClassPath.Value.IsEmpty)
        {
            args.Add("--ichorClassPath");
            args.Add(string.Join(',', plan.IchorClassPath.Value.Select(a => Path.GetFileName(a.CanonicalPath))));
        }
        if (!plan.IchorExternalFiles.Value.IsEmpty)
        {
            args.Add("--ichorExternalFiles");
            args.Add(string.Join(',', plan.IchorExternalFiles.Value.Select(a => Path.GetFileName(a.CanonicalPath))));
        }
        args.AddRange(plan.GenesisGameArgs.Value);
        return args.ToImmutable();
    }

    public ControlledGenesisLaunch Start(LaunchPlan plan, string sessionId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var arguments = BuildArguments(plan);
        if (string.IsNullOrWhiteSpace(sessionId) || !Regex.IsMatch(sessionId, "^[a-zA-Z0-9_-]{1,80}$"))
            throw new ArgumentException("Session ID contains unsupported characters.", nameof(sessionId));
        Directory.CreateDirectory(_sessionRoot);
        var directory = Path.Combine(_sessionRoot, "controlled-genesis-" + sessionId);
        if (Directory.Exists(directory)) throw new InvalidOperationException("The launch session directory already exists.");
        Directory.CreateDirectory(directory);
        if (!plan.ResourceRoots.IsEmpty)
        {
            if (Directory.Exists(plan.WorkingDirectory.Value!)) throw new InvalidOperationException("Resource working directory already exists.");
            ControlledGenesisResources.EnsureBounded(_sessionRoot, plan.WorkingDirectory.Value!);
            try { ControlledGenesisResources.Prepare(plan.ResourceRoots, plan.WorkingDirectory.Value!); }
            catch { ControlledGenesisResources.Cleanup(_sessionRoot, plan.WorkingDirectory.Value!); throw; }
        }
        Directory.CreateDirectory(Path.Combine(plan.WorkingDirectory.Value!, ".ichor"));
        var stdoutPath = Path.Combine(directory, "stdout.log");
        var stderrPath = Path.Combine(directory, "stderr.log");
        var planPath = Path.Combine(directory, "launch-plan.json");
        File.WriteAllText(planPath, PlanSerialization.Diagnostics(plan));
        var start = new ProcessStartInfo(plan.Java.Runtime!.Executable)
        {
            WorkingDirectory = plan.WorkingDirectory.Value!,
            UseShellExecute = false,
            CreateNoWindow = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var arg in arguments) start.ArgumentList.Add(arg);
        ApplyEnvironment(plan, start);
        File.WriteAllText(Path.Combine(directory, "command.txt"), string.Join(Environment.NewLine,
            new[] { Sanitize(plan.Java.Runtime.Executable) }.Concat(SanitizeArguments(arguments))));

        Process? process = null;
        try
        {
            process = new Process { StartInfo = start, EnableRaisingEvents = true };
            if (!process.Start()) throw new InvalidOperationException("ControlledGenesis JVM did not start.");
            var identity = new ControlledGenesisProcessIdentity(process.Id, process.StartTime.ToUniversalTime(),
                plan.Java.Runtime.Executable, plan.PlanId, sessionId, "Process.StartInfo.ArgumentList / LaunchPlan " + plan.PlanId);
            File.WriteAllText(Path.Combine(directory, "process.json"), System.Text.Json.JsonSerializer.Serialize(identity,
                new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
            _ = PumpAsync(process.StandardOutput, stdoutPath);
            _ = PumpAsync(process.StandardError, stderrPath);
            return new(process, identity, stdoutPath, stderrPath, planPath, directory)
            {
                OwnedResourceDirectory = plan.ResourceRoots.IsEmpty ? null : plan.WorkingDirectory.Value,
                ResourceParentDirectory = _sessionRoot
            };
        }
        catch
        {
            process?.Dispose();
            if (!plan.ResourceRoots.IsEmpty) ControlledGenesisResources.Cleanup(_sessionRoot, plan.WorkingDirectory.Value!);
            throw;
        }
    }

    public static bool IsOwnedProcess(Process process, ControlledGenesisProcessIdentity identity)
    {
        try { return process.Id == identity.ProcessId && process.StartTime.ToUniversalTime() == identity.StartTimeUtc &&
                     string.Equals(process.MainModule?.FileName, identity.Executable, StringComparison.OrdinalIgnoreCase); }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or System.Security.SecurityException) { return false; }
    }

    public static async Task<int?> WaitForExitAsync(ControlledGenesisLaunch launch, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(launch);
        try { await launch.Process.WaitForExitAsync(cancellationToken).ConfigureAwait(false); }
        catch (OperationCanceledException)
        {
            TerminateOwned(launch);
            await launch.Process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            if (launch.OwnedResourceDirectory != null)
                ControlledGenesisResources.Cleanup(launch.ResourceParentDirectory!, launch.OwnedResourceDirectory);
            throw;
        }
        var code = launch.Process.ExitCode;
        if (launch.OwnedResourceDirectory != null)
            ControlledGenesisResources.Cleanup(launch.ResourceParentDirectory!, launch.OwnedResourceDirectory);
        await File.WriteAllTextAsync(Path.Combine(launch.SessionDirectory, "exit.json"),
            System.Text.Json.JsonSerializer.Serialize(new { exitCode = code, exitedUtc = DateTimeOffset.UtcNow }), cancellationToken).ConfigureAwait(false);
        return code;
    }

    public static bool TerminateOwned(ControlledGenesisLaunch launch)
    {
        ArgumentNullException.ThrowIfNull(launch);
        if (!IsOwnedProcess(launch.Process, launch.Identity) || launch.Process.HasExited) return false;
        launch.Process.Kill(entireProcessTree: false);
        return true;
    }

    public static async Task<bool> WaitForUsableWindowAsync(ControlledGenesisLaunch launch, TimeSpan timeout,
        Func<IReadOnlyDictionary<int, Moonrise.Models.ProcessRecord>> snapshot, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(launch);
        ArgumentNullException.ThrowIfNull(snapshot);
        var deadline = DateTimeOffset.UtcNow + timeout;
        var visibleSamples = 0;
        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (launch.Process.HasExited) return false;
            var rows = snapshot();
            if (rows.TryGetValue(launch.Identity.ProcessId, out var process) &&
                process.StartTimeUtc == launch.Identity.StartTimeUtc &&
                process.IsMainWindowVisible && process.IsResponding &&
                !string.IsNullOrWhiteSpace(process.MainWindowTitle) &&
                (process.MainWindowClass.Equals("LWJGL", StringComparison.OrdinalIgnoreCase) ||
                 process.MainWindowClass.Equals("GLFW30", StringComparison.OrdinalIgnoreCase)))
            {
                if (++visibleSamples >= 6) return true;
            }
            else visibleSamples = 0;
            await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken).ConfigureAwait(false);
        }
        return false;
    }

    private static void Validate(LaunchPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (plan.Status == PreflightStatus.Invalid || !plan.Errors.IsEmpty)
            throw new InvalidOperationException("ControlledGenesis refuses an invalid launch plan.");
        if (plan.Backend != Backend.ControlledGenesis || plan.BackendCapabilities != BackendCapabilities.For(Backend.ControlledGenesis))
            throw new InvalidOperationException("Launch plan is not ControlledGenesis-capable.");
        if (plan.MinecraftVersion != "1.8.9" || plan.ContractId != ControlledGenesisDiscovery.ContractId || plan.BaseLoader.Id != "optifine")
            throw new NotSupportedException("Launch plan is outside the verified Lunar Genesis 1.8.9 OptiFine contract.");
        if (plan.Java.State != ResolutionState.Resolved || plan.Java.Runtime == null || plan.Java.Compatibility == PreflightStatus.Invalid)
            throw new InvalidOperationException("Launch plan does not contain a resolved compatible Java executable.");
        if (plan.ClassPath.State != ResolutionState.Resolved || plan.ClassPath.Value.IsDefaultOrEmpty ||
            plan.IchorClassPath.State != ResolutionState.Resolved || plan.IchorExternalFiles.State != ResolutionState.Resolved ||
            plan.GenesisGameArgs.State != ResolutionState.Resolved || plan.WorkingDirectory.State != ResolutionState.Resolved ||
            plan.GameDirectory.State != ResolutionState.Resolved || plan.AssetIndex.State != ResolutionState.Resolved ||
            plan.MainClass.Value != ControlledGenesisDiscovery.GenesisMain)
            throw new InvalidOperationException("Launch plan is missing a resolved ControlledGenesis runtime field.");
        if (plan.ClassPath.Value[0].Source != "lunar.genesis" ||
            !plan.ClassPath.Value.Skip(1).Take(plan.IchorClassPath.Value.Length).SequenceEqual(plan.IchorClassPath.Value) ||
            plan.ClassPath.Value.Skip(1 + plan.IchorClassPath.Value.Length).Any(a => a.Source != "lunar.profile-resource:language") ||
            plan.IchorClassPath.Value.IsEmpty || plan.IchorExternalFiles.Value.IsEmpty || plan.NativeArtifacts.IsEmpty)
            throw new InvalidOperationException("Launch plan does not separate Genesis, Ichor modules, external modules, and natives.");
        if (plan.ArtifactFingerprints.IsEmpty || plan.RuntimeSnapshotId.Length != 64)
            throw new InvalidOperationException("Launch plan does not contain a complete runtime artifact snapshot.");
        if (!plan.EnabledPackages.IsEmpty || !plan.EnabledWeaveMods.IsEmpty || plan.Weave.Strategy != WeaveStrategy.Off ||
            !plan.Agents.IsEmpty)
            throw new NotSupportedException("The Stage 2A clean baseline does not accept package, Weave, or agent injection.");
        if (!plan.ResourceRoots.IsEmpty)
        {
            if (plan.RuntimeFileDirectory == null) throw new InvalidOperationException("Resource plan is missing its frozen runtime file directory.");
            ControlledGenesisResources.EnsureBounded(plan.WorkingDirectory.Value!, plan.RuntimeFileDirectory);
            foreach (var root in plan.ResourceRoots)
            {
                ControlledGenesisResources.EnsureBounded(plan.WorkingDirectory.Value!, root.PreparedRoot);
                if (root.Files.Any(file => !plan.ArtifactFingerprints.Contains(file)))
                    throw new InvalidOperationException("Resource inputs are missing from the frozen runtime fingerprint.");
            }
        }
        var snapshotIssues = ArtifactSnapshots.Validate(plan.ArtifactFingerprints);
        if (!snapshotIssues.IsEmpty || PlanSerialization.Hash(PlanSerialization.Canonical(plan)) != plan.PlanId ||
            PlanSerialization.SnapshotId(plan.ArtifactFingerprints) != plan.RuntimeSnapshotId)
            throw new InvalidOperationException("Launch plan or runtime artifact snapshot changed after preflight.");
    }

    private static void ApplyEnvironment(LaunchPlan plan, ProcessStartInfo start)
    {
        var policy = plan.ChildEnvironmentPolicy;
        if (policy.Action != "RequireExplicitResolutionBeforeExecution")
            throw new InvalidOperationException("Unsupported inherited environment policy.");
        var blocked = policy.Inherited.Select(o => o.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var key in start.Environment.Keys.ToArray())
            if (key.StartsWith("JAVA_TOOL_OPTIONS", StringComparison.OrdinalIgnoreCase) ||
                key.StartsWith("_JAVA_OPTIONS", StringComparison.OrdinalIgnoreCase) ||
                key.StartsWith("JDK_JAVA_OPTIONS", StringComparison.OrdinalIgnoreCase)) start.Environment.Remove(key);
        foreach (var key in blocked) start.Environment.Remove(key);
        foreach (var option in policy.Inherited)
            if (option.Value.Length > 0) start.Environment[option.Name] = option.Value;
    }

    private static async Task PumpAsync(StreamReader reader, string path)
    {
        try
        {
            await using var output = new StreamWriter(new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read,
                16 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan)) { AutoFlush = true };
            while (await reader.ReadLineAsync().ConfigureAwait(false) is { } line)
                await output.WriteLineAsync(RedactSecretValues(line)).ConfigureAwait(false);
            await output.FlushAsync().ConfigureAwait(false);
        }
        catch (IOException) { }
        catch (ObjectDisposedException) { }
    }

    private static string Sanitize(string argument) => Sensitive.IsMatch(argument)
        ? RedactSecretValues(argument)
        : argument;

    internal static ImmutableArray<string> SanitizeArguments(IEnumerable<string> arguments)
    {
        var sanitized = ImmutableArray.CreateBuilder<string>();
        var redactNext = false;
        foreach (var argument in arguments)
        {
            if (redactNext)
            {
                sanitized.Add("[redacted]");
                redactNext = false;
                continue;
            }
            sanitized.Add(Sanitize(argument));
            redactNext = Sensitive.IsMatch(argument) &&
                argument.Length > 0 && argument[0] == '-' &&
                !argument.Contains('=') && !argument.Contains(':');
        }
        return sanitized.ToImmutable();
    }

    private static string RedactSecretValues(string value) => Regex.Replace(value,
        @"(?i)((?:--?)?(?:\w*(?:token|session|password|authorization|credential|secret)\w*)(?:=|:|\s+))([^,;\s]+)",
        "$1[redacted]");
}
