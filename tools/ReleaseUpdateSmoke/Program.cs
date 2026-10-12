using Moonrise.Services;
using Velopack;
using Velopack.Sources;
using Velopack.Locators;

// Run only from current/ of a disposable, genuinely installed Velopack package.
// The default Windows locator reads its installed manifest; no mock locator is used.
var manager = new UpdateManager(new SimpleFileSource(new DirectoryInfo(args[0])),
    new UpdateOptions { ExplicitChannel = "beta", AllowVersionDowngrade = false }, new WindowsVelopackLocator(null, null));
if (!manager.IsInstalled || manager.IsPortable) throw new Exception("Real installation required.");
var service = new UpdateService(manager, new UpdateActivityGate(), () => false);
Console.WriteLine($"Installed: {manager.CurrentVersion}; pending: {service.PendingRestart?.Version}");
if (args[1] == "none") {
    if (await service.CheckAsync() is not null || service.PendingRestart is not null)
        throw new Exception("Unexpected update/pending package.");
    Console.WriteLine("PASS no newer update");
    return;
}
if (args[1] == "bad") {
    try {
        await service.CheckAndDownloadAsync(true);
    } catch (Exception e) {
        if (service.PendingRestart is not null) throw new Exception("Corrupt download staged.");
        Console.WriteLine($"PASS download rejected: {e.GetType().Name}");
        return;
    }
    throw new Exception("Expected download failure.");
}
var update = await service.CheckAndDownloadAsync(true, u => Console.WriteLine($"Found: {u.TargetFullRelease.Version}"));
if (update is null || service.PendingRestart?.Version.ToString() != "0.1.0-beta.2")
    throw new Exception("Expected beta.2 staged.");
Console.WriteLine("PASS real download and pending restart");
if (args[1] == "apply") {
    Console.WriteLine("Applying through Moonrise UpdateService and native Update.exe");
    if (!service.TryApplyAndRestart()) throw new Exception(service.RestartBlockReason);
}
