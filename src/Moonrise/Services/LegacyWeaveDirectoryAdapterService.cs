using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using Moonrise.Infrastructure;

namespace Moonrise.Services;

public sealed class LegacyWeaveDirectoryAdapterService(AppPaths paths)
{
    public const string Id = "moonrise-legacy-weave-directory-1.0.0";
    public const string PremainClass = "dev.moonrise.compat.LegacyWeaveDirectoryAgent";
    private const string AdapterFileName = Id + ".jar";
    private const string AgentClassPath = "dev/moonrise/compat/LegacyWeaveDirectoryAgent.class";
    private const string TransformerClassPath = "dev/moonrise/compat/LegacyWeaveDirectoryAgent$LegacyLoaderTransformer.class";

    // Built from tools/legacy-weave-directory-agent with javac --release 17.
    private const string AgentClassBase64 = "yv66vgAAAD0AdwoAAgADBwAEDAAFAAYBABBqYXZhL2xhbmcvT2JqZWN0AQAGPGluaXQ+AQADKClWCAAIAQAUd2VhdmUubW9kcy5kaXJlY3RvcnkKAAoACwcADAwADQAOAQAQamF2YS9sYW5nL1N5c3RlbQEAC2dldFByb3BlcnR5AQAmKExqYXZhL2xhbmcvU3RyaW5nOylMamF2YS9sYW5nL1N0cmluZzsKABAAEQcAEgwAEwAUAQAQamF2YS9sYW5nL1N0cmluZwEAB2lzQmxhbmsBAAMoKVoHABYBAB9qYXZhL2xhbmcvSWxsZWdhbFN0YXRlRXhjZXB0aW9uCAAYAQAzTW9vbnJpc2UgbGVnYWN5IFdlYXZlIGFkYXB0ZXIgaGFzIG5vIG1vZCBkaXJlY3RvcnkuCgAVABoMAAUAGwEAFShMamF2YS9sYW5nL1N0cmluZzspVgsAHQAeBwAfDAAgACEBABJqYXZhL25pby9maWxlL1BhdGgBAAJvZgEAOyhMamF2YS9sYW5nL1N0cmluZztbTGphdmEvbGFuZy9TdHJpbmc7KUxqYXZhL25pby9maWxlL1BhdGg7CwAdACMMACQAJQEADnRvQWJzb2x1dGVQYXRoAQAWKClMamF2YS9uaW8vZmlsZS9QYXRoOwsAHQAnDAAoACUBAAlub3JtYWxpemULAB0AKgwAKwAlAQAJZ2V0UGFyZW50CAAtAQAEbW9kcwoALwAwBwAxDAAyADMBAC1kZXYvbW9vbnJpc2UvY29tcGF0L0xlZ2FjeVdlYXZlRGlyZWN0b3J5QWdlbnQBAAhmaWxlTmFtZQEAKChMamF2YS9uaW8vZmlsZS9QYXRoOylMamF2YS9sYW5nL1N0cmluZzsKABAANQwANgA3AQAQZXF1YWxzSWdub3JlQ2FzZQEAFShMamF2YS9sYW5nL1N0cmluZzspWggAOQEABi53ZWF2ZQgAOwEAP01vb25yaXNlIGxlZ2FjeSBXZWF2ZSBhZGFwdGVyIHJlY2VpdmVkIGFuIHVuc2FmZSBtb2QgZGlyZWN0b3J5LggAPQEACXdlYXZlLmRpcgsAHQA/DABAAEEBAAh0b1N0cmluZwEAFCgpTGphdmEvbGFuZy9TdHJpbmc7CgAKAEMMAEQARQEAC3NldFByb3BlcnR5AQA4KExqYXZhL2xhbmcvU3RyaW5nO0xqYXZhL2xhbmcvU3RyaW5nOylMamF2YS9sYW5nL1N0cmluZzsHAEcBAEVkZXYvbW9vbnJpc2UvY29tcGF0L0xlZ2FjeVdlYXZlRGlyZWN0b3J5QWdlbnQkTGVnYWN5TG9hZGVyVHJhbnNmb3JtZXIKAEYAAwsASgBLBwBMDABNAE4BACRqYXZhL2xhbmcvaW5zdHJ1bWVudC9JbnN0cnVtZW50YXRpb24BAA5hZGRUcmFuc2Zvcm1lcgEALihMamF2YS9sYW5nL2luc3RydW1lbnQvQ2xhc3NGaWxlVHJhbnNmb3JtZXI7KVYLAB0AUAwAUQAlAQALZ2V0RmlsZU5hbWUIAFMBAAAIAFUBAAl1c2VyLmhvbWUJAFcAWAcAWQwAWgBbAQAhamF2YS9uaW8vY2hhcnNldC9TdGFuZGFyZENoYXJzZXRzAQAFVVRGXzgBABpMamF2YS9uaW8vY2hhcnNldC9DaGFyc2V0OwoAEABdDABeAF8BAAhnZXRCeXRlcwEAHihMamF2YS9uaW8vY2hhcnNldC9DaGFyc2V0OylbQgkALwBhDABiAGMBAA9TT1VSQ0VfUFJPUEVSVFkBAAJbQgkALwBlDABmAGMBAA9UQVJHRVRfUFJPUEVSVFkBABNMRUdBQ1lfTE9BREVSX0NMQVNTAQASTGphdmEvbGFuZy9TdHJpbmc7AQANQ29uc3RhbnRWYWx1ZQgAawEAHm5ldC93ZWF2ZW1jL2xvYWRlci9XZWF2ZUxvYWRlcgEABENvZGUBAA9MaW5lTnVtYmVyVGFibGUBAAdwcmVtYWluAQA7KExqYXZhL2xhbmcvU3RyaW5nO0xqYXZhL2xhbmcvaW5zdHJ1bWVudC9JbnN0cnVtZW50YXRpb247KVYBAA1TdGFja01hcFRhYmxlAQAIPGNsaW5pdD4BAApTb3VyY2VGaWxlAQAeTGVnYWN5V2VhdmVEaXJlY3RvcnlBZ2VudC5qYXZhAQALTmVzdE1lbWJlcnMBAAxJbm5lckNsYXNzZXMBABdMZWdhY3lMb2FkZXJUcmFuc2Zvcm1lcgAxAC8AAgAAAAMAGgBnAGgAAQBpAAAAAgBqABoAYgBjAAAAGgBmAGMAAAAEAAIABQAGAAEAbAAAACEAAQABAAAABSq3AAGxAAAAAQBtAAAACgACAAAAFwAEABgACQBuAG8AAQBsAAAA8gADAAYAAACLEge4AAlNLMYACiy2AA+ZAA27ABVZEhe3ABm/LAO9ABC4ABy5ACIBALkAJgEATi25ACkBADoEGQTHAAcBpwAKGQS5ACkBADoFGQXGABwSLC24AC62ADSZABASOBkEuAAutgA0mgANuwAVWRI6twAZvxI8GQW5AD4BALgAQlcruwBGWbcASLkASQIAsQAAAAIAbQAAADIADAAAABsABgAcABEAHQAbACAALgAhADYAIgBIACMAXQAkAGYAJQBwACgAfQApAIoAKgBwAAAAHQAG/AARBwAQCf0AIwcAHQcAHUYHAB38AB8HAB0JAAoAMgAzAAEAbAAAAEUAAQACAAAAFyq5AE8BAEwrxwAIElKnAAkruQA+AQCwAAAAAgBtAAAACgACAAAALQAHAC4AcAAAAAwAAvwAEAcAHUUHABAACABxAAYAAQBsAAAAMwACAAAAAAAXElSyAFa2AFyzAGASPLIAVrYAXLMAZLEAAAABAG0AAAAKAAIAAAAUAAsAFQADAHIAAAACAHMAdAAAAAQAAQBGAHUAAAAKAAEARgAvAHYAGg==";
    private const string TransformerClassBase64 = "yv66vgAAAD0ATgcAAgEALWRldi9tb29ucmlzZS9jb21wYXQvTGVnYWN5V2VhdmVEaXJlY3RvcnlBZ2VudAoABAAFBwAGDAAHAAgBABBqYXZhL2xhbmcvT2JqZWN0AQAGPGluaXQ+AQADKClWCAAKAQAebmV0L3dlYXZlbWMvbG9hZGVyL1dlYXZlTG9hZGVyCgAMAA0HAA4MAA8AEAEAEGphdmEvbGFuZy9TdHJpbmcBAAZlcXVhbHMBABUoTGphdmEvbGFuZy9PYmplY3Q7KVoKABIAEwcAFAwAFQAWAQACW0IBAAVjbG9uZQEAFCgpTGphdmEvbGFuZy9PYmplY3Q7CQABABgMABkAFAEAD1NPVVJDRV9QUk9QRVJUWQoAGwAcBwAdDAAeAB8BAEVkZXYvbW9vbnJpc2UvY29tcGF0L0xlZ2FjeVdlYXZlRGlyZWN0b3J5QWdlbnQkTGVnYWN5TG9hZGVyVHJhbnNmb3JtZXIBAAdtYXRjaGVzAQAIKFtCSVtCKVoJAAEAIQwAIgAUAQAPVEFSR0VUX1BST1BFUlRZCgAkACUHACYMACcAKAEAEGphdmEvbGFuZy9TeXN0ZW0BAAlhcnJheWNvcHkBACooTGphdmEvbGFuZy9PYmplY3Q7SUxqYXZhL2xhbmcvT2JqZWN0O0lJKVYHACoBAB9qYXZhL2xhbmcvSWxsZWdhbFN0YXRlRXhjZXB0aW9uEgAAACwMAC0ALgEAF21ha2VDb25jYXRXaXRoQ29uc3RhbnRzAQAVKEkpTGphdmEvbGFuZy9TdHJpbmc7CgApADAMAAcAMQEAFShMamF2YS9sYW5nL1N0cmluZzspVgcAMwEAKWphdmEvbGFuZy9pbnN0cnVtZW50L0NsYXNzRmlsZVRyYW5zZm9ybWVyAQAEQ29kZQEAD0xpbmVOdW1iZXJUYWJsZQEACXRyYW5zZm9ybQEAcihMamF2YS9sYW5nL01vZHVsZTtMamF2YS9sYW5nL0NsYXNzTG9hZGVyO0xqYXZhL2xhbmcvU3RyaW5nO0xqYXZhL2xhbmcvQ2xhc3M7TGphdmEvc2VjdXJpdHkvUHJvdGVjdGlvbkRvbWFpbjtbQilbQgEADVN0YWNrTWFwVGFibGUBAAlTaWduYXR1cmUBAHUoTGphdmEvbGFuZy9Nb2R1bGU7TGphdmEvbGFuZy9DbGFzc0xvYWRlcjtMamF2YS9sYW5nL1N0cmluZztMamF2YS9sYW5nL0NsYXNzPCo+O0xqYXZhL3NlY3VyaXR5L1Byb3RlY3Rpb25Eb21haW47W0IpW0IBAApTb3VyY2VGaWxlAQAeTGVnYWN5V2VhdmVEaXJlY3RvcnlBZ2VudC5qYXZhAQAITmVzdEhvc3QBABBCb290c3RyYXBNZXRob2RzCABAAQBTVW5zdXBwb3J0ZWQgV2VhdmUgTG9hZGVyIDAuMi54IGJ5dGVjb2RlOiBleHBlY3RlZCBvbmUgdXNlci5ob21lIHJlZmVyZW5jZSwgZm91bmQgAS4PBgBCCgBDAEQHAEUMAC0ARgEAJGphdmEvbGFuZy9pbnZva2UvU3RyaW5nQ29uY2F0RmFjdG9yeQEAmChMamF2YS9sYW5nL2ludm9rZS9NZXRob2RIYW5kbGVzJExvb2t1cDtMamF2YS9sYW5nL1N0cmluZztMamF2YS9sYW5nL2ludm9rZS9NZXRob2RUeXBlO0xqYXZhL2xhbmcvU3RyaW5nO1tMamF2YS9sYW5nL09iamVjdDspTGphdmEvbGFuZy9pbnZva2UvQ2FsbFNpdGU7AQAMSW5uZXJDbGFzc2VzAQAXTGVnYWN5TG9hZGVyVHJhbnNmb3JtZXIHAEoBACVqYXZhL2xhbmcvaW52b2tlL01ldGhvZEhhbmRsZXMkTG9va3VwBwBMAQAeamF2YS9sYW5nL2ludm9rZS9NZXRob2RIYW5kbGVzAQAGTG9va3VwADAAGwAEAAEAMgAAAAMAAgAHAAgAAQA0AAAAHQABAAEAAAAFKrcAA7EAAAABADUAAAAGAAEAAAAxAAEANgA3AAIANAAAAMcABQAKAAAAaBIJLbYAC5oABQGwGQa2ABHAABI6BwM2CAM2CRUJGQe+sgAXvmSjACsZBxUJsgAXuAAamgAGpwAVsgAgAxkHFQmyACC+uAAjhAgBhAkBp//OFQgEnwASuwApWRUIugArAAC3AC+/GQewAAAAAgA1AAAANgANAAAAOgAJADsACwA+ABUAPwAYAEAAKABBADUAQgA4AEQARwBFAEoAQABQAEcAVgBIAGUASwA4AAAAEQAGC/4ADwcAEgEBHBH6AAUUADkAAAACADoACgAeAB8AAQA0AAAAVQADAAQAAAAdAz4dLL6iABYqGx1gMywdM58ABQOshAMBp//qBKwAAAACADUAAAAWAAUAAABPAAgAUAATAFEAFQBPABsAVAA4AAAACgAD/AACARL6AAUABAA7AAAAAgA8AD0AAAACAAEAPgAAAAgAAQBBAAEAPwBHAAAAEgACABsAAQBIABoASQBLAE0AGQ==";

    public string Ensure()
    {
        var directory = Path.Combine(paths.AdaptersDirectory, "runtime");
        Directory.CreateDirectory(directory);
        var targetPath = Path.Combine(directory, AdapterFileName);
        var expectedBytes = BuildJar();
        var expectedHash = ComputeSha256(expectedBytes);

        if (File.Exists(targetPath) && string.Equals(
                LocalPackageLibrary.ComputeSha256(targetPath),
                expectedHash,
                StringComparison.OrdinalIgnoreCase))
        {
            return targetPath;
        }

        var temporaryPath = targetPath + $".writing-{Guid.NewGuid():N}";
        try
        {
            File.WriteAllBytes(temporaryPath, expectedBytes);
            if (!string.Equals(LocalPackageLibrary.ComputeSha256(temporaryPath), expectedHash, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The generated legacy Weave adapter failed its SHA-256 check.");
            File.Move(temporaryPath, targetPath, overwrite: true);
            return targetPath;
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    private static byte[] BuildJar()
    {
        using var output = new MemoryStream();
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            Add(archive, "META-INF/MANIFEST.MF", Encoding.UTF8.GetBytes(
                "Manifest-Version: 1.0\r\n" +
                $"Premain-Class: {PremainClass}\r\n" +
                "Implementation-Title: Moonrise Legacy Weave Directory Adapter\r\n" +
                "Implementation-Version: 1.0.0\r\n" +
                $"Moonrise-Adapter-Id: {Id}\r\n\r\n"));
            Add(archive, AgentClassPath, Convert.FromBase64String(AgentClassBase64));
            Add(archive, TransformerClassPath, Convert.FromBase64String(TransformerClassBase64));
        }
        return output.ToArray();
    }

    private static void Add(ZipArchive archive, string name, byte[] bytes)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.NoCompression);
        entry.LastWriteTime = new DateTimeOffset(2026, 8, 3, 0, 0, 0, TimeSpan.Zero);
        using var stream = entry.Open();
        stream.Write(bytes);
    }

    private static string ComputeSha256(byte[] bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes));
}
