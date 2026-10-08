using System.Text.Json;

namespace Moonrise.Services;

public sealed record LunarAccount(Guid Uuid, string Name, bool IsSelected)
{
    public string DisplayLabel => $"{(IsSelected ? "✓ " : "")}{Name} · {Uuid}";
}
public sealed record LunarAccountSnapshot(IReadOnlyList<LunarAccount> Accounts, string? Diagnostic);

/// <summary>Read-only projection of official Lunar UI fields. Never persists account data.</summary>
public sealed class LunarAccountProvider
{
    private readonly Func<bool> _installed;
    private readonly Func<string> _store;
    public LunarAccountSnapshot Snapshot { get; private set; } = new(Array.Empty<LunarAccount>(), null);
    public LunarAccountProvider(Func<bool>? installed = null, Func<string>? store = null)
    {
        _installed = installed ?? (() => new LauncherExecutableDetector().Detect().Count != 0);
        _store = store ?? (() => Path.Combine(ResolveRoot(), "settings", "game", "accounts.json"));
    }
    private static string ResolveRoot()
    {
        var custom = Environment.GetEnvironmentVariable("LUNAR_CLIENT_DIRECTORY");
        // Relative overrides depend on the launcher's working directory, which is unknown.
        if (!string.IsNullOrEmpty(custom))
        {
            if (!Path.IsPathFullyQualified(custom)) throw new InvalidOperationException();
            return custom;
        }
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".lunarclient");
    }
    public async Task<LunarAccountSnapshot> RefreshAsync(CancellationToken cancellationToken = default)
    {
        string? error = null;
        try
        {
            if (!_installed()) return Snapshot = new(Array.Empty<LunarAccount>(), "LUNAR_NOT_INSTALLED");
            var path = _store();
            for (var attempt = 0; attempt < 3; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    var before = new FileInfo(path);
                    var length = before.Length;
                    var modified = before.LastWriteTimeUtc;
                    await Task.Delay(150, cancellationToken);
                    var stable = new FileInfo(path);
                    if (length != stable.Length || modified != stable.LastWriteTimeUtc) throw new IOException();
                    // Deny concurrent write/delete while reading, including truncate/replacement writers.
                    using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                    if (stream.Length > 4 * 1024 * 1024) throw new JsonException();
                    using var json = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
                    var accounts = Project(json.RootElement);
                    return Snapshot = new(accounts, null);
                }
                catch (FileNotFoundException) { error = "ACCOUNT_STORE_MISSING"; }
                catch (DirectoryNotFoundException) { error = "ACCOUNT_STORE_MISSING"; }
                catch (UnauthorizedAccessException) { error = "ACCOUNT_STORE_UNREADABLE"; }
                catch (IOException) { error = "ACCOUNT_STORE_UNREADABLE"; }
                catch (JsonException) { error = "ACCOUNT_STORE_INVALID"; }
                catch (InvalidOperationException) { error = "ACCOUNT_STORE_INVALID"; }
                catch (KeyNotFoundException) { error = "ACCOUNT_STORE_INVALID"; }
                catch (FormatException) { error = "ACCOUNT_STORE_INVALID"; }
                if (attempt < 2) await Task.Delay(200, cancellationToken);
            }
        }
        catch (OperationCanceledException) { throw; }
        catch { error = "ACCOUNT_SYNC_FAILED"; }
        // Replacement can temporarily remove the source. Only a valid snapshot may clear accounts.
        return Snapshot = new(Snapshot.Accounts, error);
    }
    private static IReadOnlyList<LunarAccount> Project(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("accounts", out var map) || map.ValueKind != JsonValueKind.Object)
            throw new JsonException();
        string? active = null;
        if (root.TryGetProperty("activeAccountLocalId", out var selected)) active = selected.GetString();
        var result = new List<LunarAccount>();
        foreach (var entry in map.EnumerateObject())
        {
            var account = entry.Value;
            var local = account.GetProperty("localId").GetString();
            var profile = account.GetProperty("minecraftProfile");
            if (!Guid.TryParse(profile.GetProperty("id").GetString(), out var id)) throw new JsonException();
            var name = profile.GetProperty("name").GetString();
            if (string.IsNullOrWhiteSpace(local) || string.IsNullOrWhiteSpace(name) || name.Length > 16 || !name.All(c => char.IsAsciiLetterOrDigit(c) || c == '_')) throw new JsonException();
            result.Add(new(id, name, active != null && local == active));
        }
        return result.AsReadOnly();
    }
}
