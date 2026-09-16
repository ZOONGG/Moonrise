namespace Moonrise.Models;

public sealed record UpdateRelease(
    Version Version,
    string Tag,
    bool IsPrerelease,
    Uri ReleasePage,
    string ReleaseNotes,
    Uri SetupUrl,
    Uri ChecksumUrl,
    string SetupName);

public sealed record StagedUpdate(
    UpdateRelease Release,
    string Directory,
    string SetupPath,
    bool HasTrustedAuthenticodeSignature);
