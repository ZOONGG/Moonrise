using System.IO.Compression;
using Moonrise.Infrastructure;
using Moonrise.Models;
using Moonrise.Services;
using Xunit;

namespace Moonrise.Tests;

public sealed class LegacyHooksOnlySelectionTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "moonrise-hooks-" + Guid.NewGuid().ToString("N"));
    private readonly JarMetadataParser _parser = new();

    public LegacyHooksOnlySelectionTests() => Directory.CreateDirectory(_root);

    [Theory]
    [InlineData("{\"hooks\":[\"fixture.Hook\"]}", "net/weavemc/loader/api/Hook", true)]
    [InlineData("{\"hooks\":[\"fixture.Hook\"],\"entrypoints\":[],\"mixinConfigs\":[]}", "net/weavemc/loader/api/Hook", true)]
    [InlineData("{\"hooks\":[\"fixture.Absent\"]}", "net/weavemc/loader/api/Hook", false)]
    [InlineData("{\"hooks\":[null]}", "net/weavemc/loader/api/Hook", false)]
    [InlineData("{\"hooks\":[\"fixture.Hook\"],\"entrypoints\":[\"fixture.Main\"]}", "net/weavemc/loader/api/Hook", false)]
    [InlineData("{\"hooks\":[\"fixture.Hook\"],\"mixinConfigs\":[\"fixture.mixins.json\"]}", "net/weavemc/loader/api/Hook", false)]
    [InlineData("{\"hooks\":[\"fixture.Hook\"]}", "net/weavemc/loader/api/Hook net/weavemc/api/Hook", false)]
    [InlineData("{\"hooks\":[\"fixture.Hook\"]}", "net/weavemc/api/Hook", false)]
    public void DetectionUsesMetadataAndGeneration(string metadata, string api, bool expected)
        => Assert.Equal(expected, new WeaveModApiInspector().Inspect(Mod("renamed.jar", metadata, api)).IsLegacyHooksOnly);

    [Fact]
    public void ConfirmedLegacyPlanSelectsOriginalWithoutReadingMaintainedBuild()
    {
        var original = Hooks();
        var before = File.ReadAllBytes(original.FullPath);
        var rule = Rule(original);
        var validated = false;
        var service = new MaintainedCompatibilityBuildService(new AppPaths(_root), _parser, [rule], plan =>
        {
            Assert.Equal(WeaveRuntimeMode.Legacy, plan.WeaveMode);
            Assert.Equal(original.FullPath, Assert.Single(plan.ModPaths));
            Assert.Equal(new[] { LegacyWeaveDirectoryAdapterService.Id, "weave-loader-legacy" }, plan.Agents.Select(a => a.RuntimeId));
            validated = true;
        });
        var result = service.Resolve("1.8.9", [original]);
        Assert.True(validated);
        Assert.Same(original, Assert.Single(result.LaunchMods));
        Assert.Empty(result.AppliedBuilds);
        Assert.Null(result.LegacyPathBlocker);
        Assert.Equal(before, File.ReadAllBytes(original.FullPath));
        Assert.False(Directory.Exists(Path.Combine(_root, "adapters")));
    }

    [Fact]
    public void UnconfirmedContractPreservesMaintainedFallbackAndBlocker()
    {
        var original = Hooks();
        var build = Mod("current.jar", "{\"entryPoints\":[]}", "net/weavemc/api/ModInitializer");
        var rule = Rule(original) with { BuildSha256 = build.Sha256, BuildFileName = "maintained.jar" };
        var target = Path.Combine(_root, "adapters", "maintained-builds", rule.BuildFileName);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.Copy(build.FullPath, target);
        var result = new MaintainedCompatibilityBuildService(new AppPaths(_root), _parser, [rule],
            _ => throw new InvalidDataException("pinned runtime missing")).Resolve("1.8.9", [original]);
        Assert.Equal("pinned runtime missing", result.LegacyPathBlocker);
        Assert.Equal(build.Sha256, Assert.Single(result.LaunchMods).Sha256);
        Assert.Single(result.AppliedBuilds);
        Assert.True(File.Exists(target));
    }

    [Fact]
    public void CurrentAndMixedSelectionsCannotBypassMaintainedResolution()
    {
        var original = Hooks();
        var current = Mod("current.jar", "{\"entryPoints\":[]}", "net/weavemc/api/ModInitializer");
        var service = new MaintainedCompatibilityBuildService(new AppPaths(_root), _parser, [Rule(original)],
            _ => throw new Xunit.Sdk.XunitException("Mixed set must never reach legacy preflight"));
        Assert.Throws<FileNotFoundException>(() => service.Resolve("1.8.9", [original, current]));
        var mixed = Mod("mixed.jar", "{\"hooks\":[\"fixture.Hook\"]}", "net/weavemc/loader/api/Hook net/weavemc/api/Hook");
        Assert.Throws<FileNotFoundException>(() => new MaintainedCompatibilityBuildService(new AppPaths(_root), _parser,
            [Rule(mixed)], _ => throw new Xunit.Sdk.XunitException("Mixed ABI must not reach preflight")).Resolve("1.8.9", [mixed]));
    }

    [Fact]
    public void InitializerAndMixinPackageKeepsExactHashResolution()
    {
        var package = Mod("arbitrary.jar", "{\"entrypoints\":[\"fixture.Main\"],\"mixinConfigs\":[\"fixture.mixins.json\"],\"hooks\":[\"fixture.Hook\"]}", "net/weavemc/loader/api/Hook");
        Assert.Throws<FileNotFoundException>(() => new MaintainedCompatibilityBuildService(new AppPaths(_root), _parser,
            [Rule(package)], _ => throw new Xunit.Sdk.XunitException("Initializer package must retain its existing path"))
            .Resolve("1.8.9", [package]));
    }

    [Fact]
    public void ProductionBwhRuleStillRequiresItsExactMaintainedBuild()
    {
        var fixture = Mod("bwh-shape.jar", "{\"entrypoints\":[\"fixture.Main\"],\"mixinConfigs\":[\"fixture.mixins.json\"],\"hooks\":[\"fixture.Hook\"]}", "net/weavemc/loader/api/Hook");
        var original = new PackageInfo
        {
            FileName = fixture.FileName, DisplayName = fixture.DisplayName, Identifier = fixture.Identifier,
            FullPath = fixture.FullPath, Kind = PackageKind.WeaveMod,
            Sha256 = "C39F6F1C891825CD309F543D1EE112522CAC3660AC60D9E96A5A21F20756752D"
        };
        var error = Assert.Throws<FileNotFoundException>(() => new MaintainedCompatibilityBuildService(new AppPaths(_root),
            _parser, null, _ => throw new Xunit.Sdk.XunitException("BWH must never enter hooks-only preflight"))
            .Resolve("1.8.9", [original]));
        Assert.Contains("moonrise-local-bwh-c39f-weave-1.3.4", error.Message);
        Assert.Equal("6da5b2a67e05e07deae2779b4aa3d1b70e5028ded227c41487ff783f5d609a57.jar", Path.GetFileName(error.FileName));
    }

    [Fact]
    public void OriginalHashMismatchCannotBypassReplacement()
    {
        var original = Hooks();
        using (var archive = ZipFile.Open(original.FullPath, ZipArchiveMode.Update))
            archive.CreateEntry("changed.txt");
        Assert.Throws<InvalidDataException>(() => new MaintainedCompatibilityBuildService(new AppPaths(_root), _parser,
            [], _ => {}).Resolve("1.8.9", [original]));
    }

    private PackageInfo Hooks() => Mod("arbitrary.jar", "{\"hooks\":[\"fixture.Hook\"]}", "net/weavemc/loader/api/Hook");
    private static MaintainedCompatibilityBuildRule Rule(PackageInfo original) => new("fixture-maintained", original.Sha256,
        new string('B', 64), "absent.jar", "1.8.9", WeaveAgentService.Version);
    private PackageInfo Mod(string file, string metadata, string api)
    {
        var path = Path.Combine(_root, file);
        using (var archive = ZipFile.Open(path, ZipArchiveMode.Create))
        {
            using (var writer = new StreamWriter(archive.CreateEntry("weave.mod.json").Open())) writer.Write(metadata);
            using (var writer = new StreamWriter(archive.CreateEntry("fixture/Hook.class").Open())) writer.Write(api);
        }
        return _parser.ParseWeaveMod(path);
    }
    public void Dispose() => Directory.Delete(_root, true);
}
