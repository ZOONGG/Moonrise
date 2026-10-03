using System.Text.Json.Nodes;
using Moonrise.Tests.Fixtures;
using Xunit;

namespace Moonrise.Tests;

public sealed class LauncherProfileCompatibilityTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void SchemaEvolution_ReadsOnlyKnownEvidenceWithoutChangingDatabase(int schema)
    {
        using var files = new SyntheticLunarFiles();
        files.CreateProfiles(schema == 0 ? "" : ",loaders TEXT,loader_version TEXT,lunar_module TEXT" +
            (schema == 2 ? ",future_flag INTEGER,future_data BLOB" : ""));
        files.AddProfile();
        if (schema > 0) files.Sql("UPDATE profiles SET loaders='[\"ichor\",\"future-loader\"]',loader_version='synthetic-v1',lunar_module='unknown-module'");
        var before = File.ReadAllBytes(files.Database);
        var inventory = files.Files();
        var profile = Assert.Single(files.Profiles.GetProfiles());
        Assert.Equal("synthetic-profile-a", profile.Id);
        Assert.Equal("lunar", profile.Client);
        Assert.Equal("1.8.9", profile.GameVersion);
        Assert.Equal(schema == 0 ? [] : new[] { "ichor", "future-loader" }, profile.Loaders);
        Assert.Equal(schema == 0 ? null : "synthetic-v1", profile.LoaderVersion);
        Assert.Equal(schema == 0 ? null : "unknown-module", profile.LunarModule);
        Assert.DoesNotContain("forge", profile.DetailLabel, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("optifine", profile.DetailLabel, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(before, File.ReadAllBytes(files.Database));
        Assert.Equal(inventory, files.Files());
    }

    [Theory]
    [InlineData("id")]
    [InlineData("name")]
    [InlineData("type")]
    [InlineData("major_game_version")]
    [InlineData("game_version")]
    public void MissingRequiredColumn_FailsSelectionWithoutWrites(string missing)
    {
        using var files = new SyntheticLunarFiles();
        files.Sql("CREATE TABLE profiles(" + string.Join(",", SyntheticLunarFiles.RequiredColumns.Split(',').Where(c => c != missing + " TEXT")) + ")");
        File.WriteAllText(files.Settings, "{\"settings\":{\"gameProfile\":\"original\"}}");
        var database = File.ReadAllBytes(files.Database);
        var settings = File.ReadAllBytes(files.Settings);
        var inventory = files.Files();
        Assert.Empty(files.Profiles.GetProfiles());
        Assert.Throws<InvalidOperationException>(() => files.Profiles.BeginExactProfileSelection("lunar", "1.8.9"));
        Assert.Equal(database, File.ReadAllBytes(files.Database));
        Assert.Equal(settings, File.ReadAllBytes(files.Settings));
        Assert.Equal(inventory, files.Files());
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("zero-bytes")]
    [InlineData("no-table")]
    [InlineData("no-rows")]
    public void EmptyDatabase_IsReadOnlyAndEmpty(string kind)
    {
        using var files = new SyntheticLunarFiles();
        if (kind == "zero-bytes") File.WriteAllBytes(files.Database, []);
        if (kind == "no-table") files.Sql("PRAGMA user_version=1");
        if (kind == "no-rows") files.CreateProfiles();
        var before = File.Exists(files.Database) ? File.ReadAllBytes(files.Database) : null;
        var inventory = files.Files();
        Assert.Empty(files.Profiles.GetProfiles());
        Assert.Throws<InvalidOperationException>(() => files.Profiles.BeginExactProfileSelection("lunar", "1.8.9"));
        Assert.Equal(inventory, files.Files());
        if (before is not null) Assert.Equal(before, File.ReadAllBytes(files.Database));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("[")]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("42")]
    [InlineData("\"forge\"")]
    [InlineData("[]")]
    [InlineData("[null,42,true,{},[]]")]
    [InlineData("[\"unknown-loader\",null,42,\"\",\" \",{},\"ichor\"]")]
    public void LoaderJson_OnlyStringEvidenceIsPreserved(string? loaders)
    {
        using var files = new SyntheticLunarFiles();
        files.CreateProfiles(",loaders TEXT,loader_version TEXT,lunar_module TEXT");
        files.AddProfile();
        files.Sql("UPDATE profiles SET loaders=$loaders", ("$loaders", loaders));
        var before = File.ReadAllBytes(files.Database);
        var profile = Assert.Single(files.Profiles.GetProfiles());
        Assert.Equal(loaders?.Contains("unknown-loader") == true ? new[] { "unknown-loader", "ichor" } : [], profile.Loaders);
        Assert.Null(profile.LoaderVersion);
        Assert.Null(profile.LunarModule);
        Assert.Equal("lunar", profile.Client);
        Assert.DoesNotContain("forge", profile.DetailLabel, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("optifine", profile.DetailLabel, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(before, File.ReadAllBytes(files.Database));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void OptionalMetadata_NullAndEmptyAreNotInvented(string? value)
    {
        using var files = new SyntheticLunarFiles();
        files.CreateProfiles(",loaders TEXT,loader_version TEXT,lunar_module TEXT");
        files.AddProfile();
        files.Sql("UPDATE profiles SET loader_version=$value,lunar_module=$value", ("$value", value));
        var profile = Assert.Single(files.Profiles.GetProfiles());
        Assert.Equal(value, profile.LoaderVersion);
        Assert.Equal(value, profile.LunarModule);
        Assert.Empty(profile.Loaders);
    }

    [Fact]
    public void OtherClientProfiles_AreNotSelectedAsLunar()
    {
        using var files = new SyntheticLunarFiles();
        files.CreateProfiles(); files.AddProfile();
        files.Sql("UPDATE profiles SET type='vanilla'");
        Assert.Empty(files.Profiles.GetProfiles());
        Assert.Throws<InvalidOperationException>(() => files.Profiles.BeginExactProfileSelection("lunar", "1.8.9"));
        Assert.False(File.Exists(files.Settings));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"settings\":{\"nested\":{\"array\":[1,null,{\"value\":true}]}}}")]
    [InlineData("{\"settings\":{\"gameProfile\":null,\"nested\":{\"array\":[1,null]}}}")]
    [InlineData("{\"settings\":{\"gameProfile\":\"original\",\"nested\":{\"array\":[1,null]}},\"other\":{\"x\":1}}")]
    public void ExactSelection_RestoresOriginalValueAndPreservesUnrelatedData(string json)
    {
        using var files = new SyntheticLunarFiles();
        files.CreateProfiles();
        files.AddProfile("a"); files.AddProfile("b");
        File.WriteAllText(files.Settings, json);
        var before = JsonNode.Parse(json)!;
        var database = File.ReadAllBytes(files.Database);
        var service = files.Profiles;
        var selection = service.BeginExactProfileSelection("lunar", "1.8.9", "b");
        Assert.Equal("b", selection.Profile.Id);
        var selected = JsonNode.Parse(File.ReadAllText(files.Settings))!;
        var expected = before.DeepClone();
        expected["settings"] ??= new JsonObject();
        expected["settings"]!["gameProfile"] = "b";
        Assert.True(JsonNode.DeepEquals(expected, selected));
        selected["concurrent"] = new JsonObject { ["nested"] = new JsonArray(1, 2, 3) };
        File.WriteAllText(files.Settings, selected.ToJsonString());
        selection.Restore();
        selection.Dispose();
        before["concurrent"] = selected["concurrent"]!.DeepClone();
        // A previously absent settings object may remain empty after removing gameProfile.
        before["settings"] ??= new JsonObject();
        Assert.True(JsonNode.DeepEquals(before, JsonNode.Parse(File.ReadAllText(files.Settings))));
        var restored = File.ReadAllBytes(files.Settings);
        Assert.Throws<InvalidOperationException>(() => service.BeginExactProfileSelection("lunar", "1.8.9", "B"));
        Assert.Equal(restored, File.ReadAllBytes(files.Settings));
        Assert.Equal(database, File.ReadAllBytes(files.Database));
        Assert.Empty(Directory.GetFiles(files.Root, "*.tmp", SearchOption.AllDirectories));
    }

    [Theory]
    [InlineData("")]
    [InlineData("{")]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("42")]
    [InlineData("{\"settings\":[]}")]
    [InlineData("{\"settings\":\"preserve-me\"}")]
    [InlineData("{\"settings\":null}")]
    public void InvalidLauncherSettings_FailBeforeModification(string json)
    {
        using var files = new SyntheticLunarFiles();
        files.CreateProfiles(); files.AddProfile();
        File.WriteAllText(files.Settings, json);
        var before = File.ReadAllBytes(files.Settings);
        var inventory = files.Files();
        Assert.Throws<InvalidDataException>(() => files.Profiles.BeginExactProfileSelection("lunar", "1.8.9"));
        Assert.Equal(before, File.ReadAllBytes(files.Settings));
        Assert.Equal(inventory, files.Files());
    }

    [Fact]
    public void SiblingPrivateFiles_AreNeverNeededOrCopied()
    {
        using var files = new SyntheticLunarFiles();
        files.CreateProfiles(); files.AddProfile();
        File.WriteAllText(files.Settings, "{}");
        // Synthetic deny-sharing sentinels: reads/copies/writes fail on Windows.
        var sentinels = new List<FileStream>();
        try
        {
            foreach (var directory in new[] { files.Root, Path.GetDirectoryName(files.Database)!, Path.GetDirectoryName(files.Settings)! })
            foreach (var name in new[] { "accounts.json", "session.json", "tokens.json" })
                sentinels.Add(new FileStream(Path.Combine(directory, name), FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None));
            var inventory = files.Files();
            Assert.Single(files.Profiles.GetProfiles());
            using (files.Profiles.BeginExactProfileSelection("lunar", "1.8.9")) { }
            Assert.Equal(inventory, files.Files());
            Assert.All(sentinels, stream => Assert.Equal(0, stream.Length));
        }
        finally { foreach (var stream in sentinels) stream.Dispose(); }
    }
}
