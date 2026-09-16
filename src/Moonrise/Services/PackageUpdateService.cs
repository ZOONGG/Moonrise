using Moonrise.Models;

namespace Moonrise.Services;

public sealed class PackageUpdateService
{
    public bool IsUpdateAvailable(PackageManifest catalogPackage, PackageInfo installed)
    {
        if (!string.Equals(catalogPackage.Id, installed.Identifier, StringComparison.OrdinalIgnoreCase) || catalogPackage.LatestRelease is null)
            return false;
        return CompareVersions(catalogPackage.LatestRelease.Version, installed.Version) > 0;
    }

    private static int CompareVersions(string left, string right)
    {
        if (Version.TryParse(left.TrimStart('v'), out var leftVersion) && Version.TryParse(right.TrimStart('v'), out var rightVersion))
            return leftVersion.CompareTo(rightVersion);
        return string.Compare(left, right, StringComparison.OrdinalIgnoreCase);
    }
}
