using System.Runtime.CompilerServices;

namespace Moonrise.Services;

internal static class LunarLaunchEnvironment
{
    internal const string ElectronRunAsNode = "ELECTRON_RUN_AS_NODE";

    [ModuleInitializer]
    internal static void Initialize() =>
        Environment.SetEnvironmentVariable(ElectronRunAsNode, null);

    internal static void Sanitize<TValue>(IDictionary<string, TValue> environment)
    {
        ArgumentNullException.ThrowIfNull(environment);
        environment.Remove(ElectronRunAsNode);
    }
}
