using Moonrise.Compatibility;
using Xunit;

namespace Moonrise.Tests;

public sealed class ControlledGenesisAuthTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "Moonrise Auth Tests", Guid.NewGuid().ToString("N"));
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }

    [Theory]
    [InlineData(true, null, null, AuthReadinessState.Unknown)]
    [InlineData(true, false, null, AuthReadinessState.ProvisioningRequired)]
    [InlineData(true, true, null, AuthReadinessState.AuthBrokerUnavailable)]
    [InlineData(true, true, 28190, AuthReadinessState.BrokerAvailable)]
    [InlineData(true, true, 28219, AuthReadinessState.BrokerAvailable)]
    [InlineData(true, true, 28220, AuthReadinessState.AuthBrokerUnavailable)]
    [InlineData(false, true, 28190, AuthReadinessState.Unsupported)]
    public void ObservationDoesNotClaimAuthenticatedReadiness(bool supported, bool? provisioned, int? port, AuthReadinessState state)
    {
        var result = ControlledGenesisAuth.Evaluate(supported, provisioned, port);
        Assert.Equal(state, result.State);
        Assert.NotEqual(AuthReadinessState.Ready, result.State);
        Assert.Equal(state == AuthReadinessState.BrokerAvailable, result.CanAttemptGameHandshake);
        Assert.DoesNotContain("accessToken", result.Diagnostics());
    }

    [Fact] public void LiveBrokerDoesNotProveCredentialFreePlayableContract()
    {
        var observed = ControlledGenesisAuth.Evaluate(true, true, 28190);
        var playable = ControlledGenesisAuth.ForPlayableLaunch(observed);
        Assert.Equal(AuthReadinessState.Unsupported, playable.State);
        Assert.Equal(AuthEvidenceSource.SharedAccountStoreRequired, playable.EvidenceSource);
        Assert.Equal(28190, playable.BrokerPort);
        Assert.False(playable.CanAttemptGameHandshake);
        Assert.Throws<AuthDependencyException>(() => ControlledGenesisExecutor.RequireAuthDependency(null!, playable));
    }

    [Fact] public void MissingDependencyBlocksBeforeAnyProcessOrSessionCreation()
    {
        var readiness = ControlledGenesisAuth.Evaluate(true, true, null);
        // The gate rejects independently of JVM creation and never falls back to OfficialLunar dispatch.
        Assert.Throws<AuthDependencyException>(() => ControlledGenesisExecutor.RequireAuthDependency(null!, readiness));
        Assert.False(Directory.Exists(_root));
    }

    [Theory]
    [InlineData("Authorization: Bearer fixture-value")]
    [InlineData("{\"refreshToken\": \"fixture-value\"}")]
    [InlineData("Cookie: login=fixture-value")]
    [InlineData("Account payload: fixture-value")]
    public void RuntimeDiagnosticsDiscardPotentialCredentialPayloads(string line)
    {
        Assert.DoesNotContain("fixture-value", ControlledGenesisExecutor.SanitizeDiagnosticLine(line));
        Assert.Equal("IPC connected", ControlledGenesisExecutor.SanitizeDiagnosticLine("IPC connected"));
    }
}
