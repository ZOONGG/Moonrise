using System.Reflection;
using System.Security.Cryptography;

namespace Moonrise.Services;

public sealed class NativeBridgeDeploymentService
{
    public const string ResourceName = "Moonrise.NativeBridge.dll";
    public const string ExpectedSha256 = "43759ACE29C640D650DABC9DA73E9EE35A4ACECCE55562F6477C43CF8F6FB660";

    private readonly Func<Stream> _openResource;
    private readonly string _expectedSha256;

    public NativeBridgeDeploymentService(
        Func<Stream>? openResource = null,
        string? expectedSha256 = null)
    {
        _openResource = openResource ?? OpenBundledResource;
        _expectedSha256 = expectedSha256 ?? ExpectedSha256;
    }

    public string Ensure(string targetPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetPath);
        // A previous bridge may still be mapped by another process. Windows
        // refuses to replace a mapped DLL, even when the user has FullControl.
        var fullPath = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(targetPath))!,
            _expectedSha256, Path.GetFileName(targetPath));
        if (File.Exists(fullPath) && HasExpectedHash(fullPath)) return fullPath;

        var directory = Path.GetDirectoryName(fullPath)
            ?? throw new InvalidOperationException("Unable to determine the native bridge directory.");
        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(
            directory,
            $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.extracting");
        var operation = "extract-resource";
        try
        {
            using (var resource = _openResource())
            using (var output = new FileStream(
                       temporaryPath,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None,
                       128 * 1024,
                       FileOptions.WriteThrough))
            {
                resource.CopyTo(output);
                output.Flush(flushToDisk: true);
            }

            if (!HasExpectedHash(temporaryPath))
                throw new InvalidDataException("The embedded native process bridge failed its SHA-256 check.");
            operation = "File.Move(overwrite: true)";
            File.Move(temporaryPath, fullPath, overwrite: true);
            operation = "verify-deployed-sha256";
            if (!HasExpectedHash(fullPath))
                throw new InvalidDataException("The deployed native process bridge failed its SHA-256 check.");
            return fullPath;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            exception.Data["fileOperation"] = operation;
            exception.Data["filePath"] = fullPath;
            exception.Data["temporaryPath"] = temporaryPath;
            throw;
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    private bool HasExpectedHash(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            var hash = Convert.ToHexString(SHA256.HashData(stream));
            return string.Equals(hash, _expectedSha256, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static Stream OpenBundledResource() =>
        typeof(NativeBridgeDeploymentService).Assembly.GetManifestResourceStream(ResourceName)
        ?? throw new InvalidDataException($"Embedded resource '{ResourceName}' is missing.");
}
