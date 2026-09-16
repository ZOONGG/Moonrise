using System.Diagnostics;

namespace Moonrise.Models;

public sealed record NativeBridgeLaunchResult(Process Process, bool BridgeReady);
