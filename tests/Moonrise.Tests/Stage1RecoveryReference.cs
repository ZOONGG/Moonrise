using System.Security.Cryptography;
using Moonrise.Infrastructure;
using Moonrise.Models;

namespace Moonrise.Services;

// Test-only reference for the previous recovery milestone. Production runtime must not use it.
public sealed record RecoveryPackageDefinition(
    string Id,
    string SourcePath,
    string FileName,
    string Sha256,
    PackageKind Kind);

public sealed record RecoveryPackageSet(
    RecoveryPackageDefinition VeyraDefinition,
    RecoveryPackageDefinition CosmeticsDefinition,
    PackageInfo Veyra,
    PackageInfo Cosmetics);

public sealed class Stage1RecoveryPackageService
{
    private static readonly string TestAssetsRoot = ResolveTestAssetsRoot();

    public static readonly RecoveryPackageDefinition Veyra = new(
        "veyra",
        Path.Combine(TestAssetsRoot, "Veyra-0.1.1-moonrise.1.jar"),
        "Veyra-0.1.1-moonrise.1.jar",
        "1E0DBE11C602660A59CA1D0CACF790FE856E737E275821CE4E62B2F72A377D8D",
        PackageKind.WeaveMod);

    public static readonly RecoveryPackageDefinition Cosmetics = new(
        "moonrise-cosmetics",
        Path.Combine(TestAssetsRoot, "Moonrise-Cosmetics.jar"),
        "Moonrise-Cosmetics.jar",
        "FD8008D5B6F5A478AF56F70E33A397CEA5F50EB84731699BFC50DD66A511930C",
        PackageKind.JavaAgent);

    private readonly JarMetadataParser _parser;
    private readonly RecoveryPackageDefinition _veyra;
    private readonly RecoveryPackageDefinition _cosmetics;

    public Stage1RecoveryPackageService(
        JarMetadataParser parser,
        RecoveryPackageDefinition? veyra = null,
        RecoveryPackageDefinition? cosmetics = null)
    {
        _parser = parser;
        _veyra = veyra ?? Veyra;
        _cosmetics = cosmetics ?? Cosmetics;
    }

    public RecoveryPackageSet EnsureImported(AppPaths paths)
    {
        paths.EnsureUserDirectories();
        var veyraPath = ImportVerified(_veyra, paths.MoonriseOwnedModsDirectory);
        var cosmeticsPath = ImportVerified(_cosmetics, paths.MoonriseOwnedAgentsDirectory);
        var result = new RecoveryPackageSet(
            _veyra,
            _cosmetics,
            _parser.ParseWeaveMod(veyraPath),
            _parser.ParseJavaAgent(cosmeticsPath));
        VerifyUnchanged(result);
        return result;
    }

    public static void VerifyUnchanged(RecoveryPackageSet packages)
    {
        VerifyHash(packages.VeyraDefinition.SourcePath, packages.VeyraDefinition.Sha256);
        VerifyHash(packages.Veyra.FullPath, packages.VeyraDefinition.Sha256);
        VerifyHash(packages.CosmeticsDefinition.SourcePath, packages.CosmeticsDefinition.Sha256);
        VerifyHash(packages.Cosmetics.FullPath, packages.CosmeticsDefinition.Sha256);
    }

    public static (PackageInfo Veyra, PackageInfo Cosmetics) SelectEnabled(
        RecoveryPackageSet recovery,
        IEnumerable<PackageInfo> mods,
        IEnumerable<PackageInfo> agents) =>
        (AssertEnabled(mods, recovery.VeyraDefinition), AssertEnabled(agents, recovery.CosmeticsDefinition));

    private string ImportVerified(RecoveryPackageDefinition definition, string destinationDirectory)
    {
        VerifyHash(definition.SourcePath, definition.Sha256);
        Directory.CreateDirectory(destinationDirectory);
        var destination = Path.Combine(destinationDirectory, definition.FileName);
        if (!File.Exists(destination))
            File.Copy(definition.SourcePath, destination, overwrite: false);
        VerifyHash(destination, definition.Sha256);
        return destination;
    }

    private static PackageInfo AssertEnabled(
        IEnumerable<PackageInfo> packages,
        RecoveryPackageDefinition expected)
    {
        var match = AssertSingle(packages.Where(package =>
            package.Kind == expected.Kind &&
            string.Equals(package.FileName, expected.FileName, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(package.Sha256, expected.Sha256, StringComparison.OrdinalIgnoreCase)));
        if (!match.IsEnabled)
            throw new InvalidOperationException($"{expected.FileName} must be enabled for the Recovery Stage 1 test.");
        return match;
    }

    private static PackageInfo AssertSingle(IEnumerable<PackageInfo> packages)
    {
        var matches = packages.ToArray();
        if (matches.Length != 1)
            throw new InvalidDataException("Recovery Stage 1 expected exactly one matching package.");
        return matches[0];
    }

    private static void VerifyHash(string path, string expected)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException("Recovery test asset was not found.", path);
        if (!string.Equals(ComputeSha256(path), expected, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Recovery test asset hash does not match.");
    }

    internal static string ComputeSha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static string ResolveTestAssetsRoot()
    {
        var configured = Environment.GetEnvironmentVariable("MOONRISE_TEST_ASSETS");
        if (!string.IsNullOrWhiteSpace(configured))
            return Path.GetFullPath(configured);

        foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            var current = new DirectoryInfo(Path.GetFullPath(start));
            while (current is not null)
            {
                if (File.Exists(Path.Combine(current.FullName, "Moonrise.sln")))
                {
                    foreach (var candidate in new[]
                             {
                                 Path.Combine(current.Parent?.FullName ?? current.FullName, "Moonrise-TestAssets"),
                                 Path.Combine(current.Parent?.FullName ?? current.FullName, "private-assets")
                             })
                    {
                        if (Directory.Exists(candidate))
                            return candidate;
                    }
                }
                current = current.Parent;
            }
        }

        throw new DirectoryNotFoundException(
            "Set MOONRISE_TEST_ASSETS to the private Stage 1 test-assets directory.");
    }
}
