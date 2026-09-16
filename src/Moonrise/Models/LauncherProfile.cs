namespace Moonrise.Models;

public sealed record LauncherProfile(
    string Id,
    string Name,
    string Client,
    string MajorVersion,
    string GameVersion);
