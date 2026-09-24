using Moonrise.Models;

namespace Moonrise.Services;

public sealed record Mnr3BridgeProjection(
    string PrimaryAgentPath,
    IReadOnlyList<string> AdditionalAgentPaths);

public static class Mnr3BridgeProjectionBuilder
{
    public static Mnr3BridgeProjection Build(LaunchPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        if (plan.Agents.Count == 0)
        {
            throw new InvalidOperationException(
                "MNR3 bridge projection requires at least one Java agent.");
        }

        var agentWithOptions = plan.Agents.FirstOrDefault(item =>
            !string.IsNullOrWhiteSpace(item.Options));
        if (agentWithOptions is not null)
        {
            throw new InvalidOperationException(
                $"MNR3 cannot represent Java-agent options for '{agentWithOptions.RuntimeId}'.");
        }

        if (plan.JvmArguments.Count > 0)
        {
            throw new InvalidOperationException(
                "MNR3 cannot represent explicit JVM arguments.");
        }

        if (plan.JvmProperties.Count > 0)
        {
            throw new InvalidOperationException(
                "MNR3 cannot represent explicit JVM properties.");
        }

        return new Mnr3BridgeProjection(
            plan.Agents[0].Path,
            plan.Agents.Skip(1).Select(item => item.Path).ToArray());
    }
}
