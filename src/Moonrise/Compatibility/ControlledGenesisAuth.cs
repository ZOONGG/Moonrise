using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Moonrise.Compatibility;

public enum AuthReadinessState { Unknown, ProvisioningRequired, AuthBrokerUnavailable, BrokerAvailable, Ready, Unsupported }
public enum AuthEvidenceSource { None, InstallationMetadata, OfficialProcessTcpListener, GameScreenObservation, UnsupportedContract, SharedAccountStoreRequired }

// BrokerAvailable permits the game's official handshake; it does not assert login or ownership.
public sealed record AuthReadiness(AuthReadinessState State, AuthEvidenceSource EvidenceSource,
    DateTimeOffset CheckedAtUtc, int? BrokerPort = null)
{
    public string RequiredComponent => "Official Lunar Client GameIPC (Electron main process)";
    public bool CanAttemptGameHandshake => State == AuthReadinessState.BrokerAvailable;
    public string UserAction => State switch
    {
        AuthReadinessState.ProvisioningRequired => "Open Lunar Client and sign in to complete provisioning.",
        AuthReadinessState.AuthBrokerUnavailable => "Open Lunar Client to sign in, then re-check launch readiness.",
        AuthReadinessState.BrokerAvailable => "Official auth service is available. The game must confirm sign-in and ownership.",
        AuthReadinessState.Unsupported => "This Lunar version requires a shared official account store. Authenticated ControlledGenesis launch is unsupported with the isolated data contract.",
        _ => "ControlledGenesis auth readiness could not be verified."
    };
    public string Diagnostics() => JsonSerializer.Serialize(this, new JsonSerializerOptions
    {
        WriteIndented = true, Converters = { new JsonStringEnumConverter() }
    });
}

public sealed class AuthDependencyException(AuthReadiness readiness) : InvalidOperationException(readiness.UserAction)
{
    public AuthReadiness Readiness { get; } = readiness;
}

/// <summary>Observes only installation metadata and OS listener ownership. Never connects to auth IPC.</summary>
public static class ControlledGenesisAuth
{
    public const int FirstPort = 28190;
    public const int LastPort = 28219;

    public static AuthReadiness Evaluate(bool supported, bool? provisioned, int? verifiedBrokerPort)
    {
        var state = !supported ? AuthReadinessState.Unsupported : provisioned is null ? AuthReadinessState.Unknown :
            !provisioned.Value ? AuthReadinessState.ProvisioningRequired :
            verifiedBrokerPort is >= FirstPort and <= LastPort ? AuthReadinessState.BrokerAvailable : AuthReadinessState.AuthBrokerUnavailable;
        var source = state switch
        {
            AuthReadinessState.Unsupported => AuthEvidenceSource.UnsupportedContract,
            AuthReadinessState.BrokerAvailable => AuthEvidenceSource.OfficialProcessTcpListener,
            AuthReadinessState.ProvisioningRequired or AuthReadinessState.AuthBrokerUnavailable => AuthEvidenceSource.InstallationMetadata,
            _ => AuthEvidenceSource.None
        };
        return new(state, source, DateTimeOffset.UtcNow, state == AuthReadinessState.BrokerAvailable ? verifiedBrokerPort : null);
    }

    public static AuthReadiness Probe(LaunchPlan plan)
    {
        if (plan.ContractId != ControlledGenesisDiscovery.ContractId) return Evaluate(false, null, null);
        return ProbePlayableLaunch();
    }

    public static AuthReadiness ProbePlayableLaunch() => ForPlayableLaunch(ProbeBroker());

    // A live broker is insufficient: AddAccount returns only success/error and the game
    // reloads accounts.json from its own data root. No credential-free handoff was found.
    public static AuthReadiness ForPlayableLaunch(AuthReadiness broker) => broker.CanAttemptGameHandshake
        ? new(AuthReadinessState.Unsupported, AuthEvidenceSource.SharedAccountStoreRequired, broker.CheckedAtUtc, broker.BrokerPort)
        : broker;

    public static AuthReadiness ProbeBroker()
    {
        if (!OperatingSystem.IsWindows()) return Evaluate(false, null, null);
        if (!File.Exists(OfficialExecutable)) return Evaluate(true, false, null);
        try
        {
            var executable = OfficialExecutable;
            foreach (var row in Listeners())
            {
                if (row.Port is < FirstPort or > LastPort) continue;
                using var process = Process.GetProcessById(row.Pid);
                if (!process.HasExited && string.Equals(process.MainModule?.FileName, executable, StringComparison.OrdinalIgnoreCase))
                    return Evaluate(true, true, row.Port);
            }
            return Evaluate(true, true, null);
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException or ArgumentException)
        { return Evaluate(true, null, null); }
    }

    private static string OfficialExecutable => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Programs", "Lunar Client", "Lunar Client.exe");

    /// <summary>Explicit provisioning action only. No launch URI, account payload, window control or game dispatch.</summary>
    public static void OpenOfficialProvisioning()
    {
        if (!File.Exists(OfficialExecutable)) throw new AuthDependencyException(Evaluate(true, false, null));
        using var process = Process.Start(new ProcessStartInfo(OfficialExecutable) { UseShellExecute = true });
    }

    public static async Task<AuthReadiness> WaitForBrokerAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        if (timeout < TimeSpan.Zero || timeout > TimeSpan.FromMinutes(5)) throw new ArgumentOutOfRangeException(nameof(timeout));
        var timer = Stopwatch.StartNew();
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var readiness = ProbeBroker();
            if (readiness.CanAttemptGameHandshake || timer.Elapsed >= timeout) return readiness;
            await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken).ConfigureAwait(false);
        }
    }

    private static IEnumerable<(int Port, int Pid)> Listeners()
    {
        var size = 0;
        var result = GetExtendedTcpTable(IntPtr.Zero, ref size, true, 2, 3, 0); // IPv4 / OWNER_PID_LISTENER
        if (result != 122 || size < 4) throw new InvalidOperationException("Listener metadata unavailable.");
        var buffer = Marshal.AllocHGlobal(size);
        try
        {
            if (GetExtendedTcpTable(buffer, ref size, true, 2, 3, 0) != 0) throw new InvalidOperationException("Listener metadata unavailable.");
            var count = Marshal.ReadInt32(buffer);
            if (count < 0 || count > (size - 4) / 24) throw new InvalidOperationException("Invalid listener metadata.");
            for (var i = 0; i < count; i++)
            {
                var offset = 4 + i * 24;
                // MIB_TCPROW_OWNER_PID: state, localAddr, localPort, remoteAddr, remotePort, pid.
                if (Marshal.ReadInt32(buffer, offset + 4) != 0x0100007f) continue;
                var port = Marshal.ReadByte(buffer, offset + 8) * 256 + Marshal.ReadByte(buffer, offset + 9);
                yield return (port, Marshal.ReadInt32(buffer, offset + 20));
            }
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    [DllImport("iphlpapi.dll")]
    private static extern uint GetExtendedTcpTable(IntPtr table, ref int size, [MarshalAs(UnmanagedType.Bool)] bool order,
        int family, int tableClass, uint reserved);
}
