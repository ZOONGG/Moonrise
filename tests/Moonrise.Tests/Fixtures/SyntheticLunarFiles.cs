using Microsoft.Data.Sqlite;
using Moonrise.Services;

namespace Moonrise.Tests.Fixtures;

// Generated public fixtures only. Both service paths are always supplied explicitly.
internal sealed class SyntheticLunarFiles : IDisposable
{
    public string Root { get; } = Path.Combine(Path.GetTempPath(), "moonrise-synthetic-" + Guid.NewGuid().ToString("N"));
    public string Database => Path.Combine(Root, "db", "profiles.db");
    public string Settings => Path.Combine(Root, "settings", "launcher.json");
    public string Log => Path.Combine(Root, "logs", "launcher", "main.log");
    public LauncherProfileService Profiles => new(Database, Settings);
    public const string RequiredColumns = "id TEXT,name TEXT,type TEXT,major_game_version TEXT,game_version TEXT";

    public SyntheticLunarFiles()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Database)!);
        Directory.CreateDirectory(Path.GetDirectoryName(Settings)!);
        Directory.CreateDirectory(Path.GetDirectoryName(Log)!);
        File.WriteAllText(Log, "");
    }

    public void Sql(string sql, params (string Name, object? Value)[] parameters)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = Database, Pooling = false
        }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        command.ExecuteNonQuery();
    }

    public void CreateProfiles(string optionalColumns = "") =>
        Sql($"CREATE TABLE profiles({RequiredColumns}{optionalColumns});");

    public void AddProfile(string id = "synthetic-profile-a") => Sql(
        "INSERT INTO profiles(id,name,type,major_game_version,game_version) VALUES ($id,'Duplicate name','lunar','1.8','1.8.9')", ("$id", id));

    public string[] Files() => Directory.GetFiles(Root, "*", SearchOption.AllDirectories)
        .Select(path => Path.GetRelativePath(Root, path)).OrderBy(path => path, StringComparer.Ordinal).ToArray();

    public void Dispose() => Directory.Delete(Root, recursive: true);
}
