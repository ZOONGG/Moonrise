using Moonrise.Services;
using Xunit;

namespace Moonrise.Tests;

public sealed class LunarLaunchEnvironmentTests
{
    [Fact]
    public void Sanitize_RemovesElectronRunAsNodeWithoutChangingOtherVariables()
    {
        var environment = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["ELECTRON_RUN_AS_NODE"] = "1",
            ["PATH"] = "C:\\Windows"
        };

        LunarLaunchEnvironment.Sanitize(environment);

        Assert.False(environment.ContainsKey("ELECTRON_RUN_AS_NODE"));
        Assert.Equal("C:\\Windows", environment["PATH"]);
    }

    [Fact]
    public void OfficialLunarStart_DoesNotInheritElectronRunAsNode()
    {
        var startInfo = OfficialLunarStartInfoFactory.Create(
            Path.Combine("C:", "Program Files", "Lunar Client", "Lunar Client.exe"),
            background: true);

        Assert.False(startInfo.Environment.ContainsKey("ELECTRON_RUN_AS_NODE"));
    }
}
