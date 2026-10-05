using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
namespace Moonrise.Compatibility;

public static class PlanSerialization
{
    private static readonly Regex SensitiveName = new("token|session|password|authorization|credential|secret", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    public static string Canonical(LaunchPlan plan) => Sanitize(JsonSerializer.SerializeToNode(plan with { PlanId = "" })!, Secrets(plan)).ToJsonString();
    public static string Diagnostics(LaunchPlan plan) => Sanitize(JsonSerializer.SerializeToNode(plan)!, Secrets(plan)).ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    public static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    public static string SnapshotId(ImmutableArray<ArtifactSnapshot> artifacts) => Hash(string.Join("\n", artifacts.Select(a => $"{a.CanonicalPath}\0{a.Size}\0{a.Sha256}")));
    private static HashSet<string> Secrets(LaunchPlan plan)
    {
        var secrets = new HashSet<string>(StringComparer.Ordinal);
        void Read(IEnumerable<string> arguments)
        {
            var awaitingValue = false;
            foreach (var argument in arguments)
            {
                if (awaitingValue && !argument.StartsWith('-')) { if (argument.Length > 0) secrets.Add(argument); continue; }
                awaitingValue = false;
                if (!SensitiveName.IsMatch(argument)) continue;
                var delimiter = argument.IndexOf('=');
                if (delimiter >= 0 && delimiter + 1 < argument.Length) secrets.Add(argument[(delimiter + 1)..]);
                else awaitingValue = true;
            }
        }
        Read(JvmArguments.Parse(plan.UserJvmArgumentsText).Tokens);
        Read(plan.RequiredJvmArgs);
        if (plan.GenesisGameArgs.State == ResolutionState.Resolved) Read(plan.GenesisGameArgs.Value);
        foreach (var option in plan.ChildEnvironmentPolicy.Inherited) Read(JvmArguments.Parse(option.Value).Tokens);
        foreach (var agent in plan.Agents) Read(JvmArguments.Parse(agent.Options ?? "").Tokens);
        return secrets;
    }
    private static JsonNode Sanitize(JsonNode node, HashSet<string> secrets)
    {
        if (node is JsonObject obj)
        {
            foreach (var key in obj.Select(p => p.Key).ToArray())
            {
                if (SensitiveName.IsMatch(key)) obj[key] = "[redacted]";
                else if (obj[key] != null) obj[key] = Sanitize(obj[key]!.DeepClone(), secrets);
            }
        }
        else if (node is JsonArray array)
        {
            var hideNext = false;
            for (var i = 0; i < array.Count; i++)
            {
                if (array[i] is JsonValue value && value.TryGetValue<string>(out var text))
                {
                    if (hideNext) { array[i] = "[redacted]"; hideNext = false; continue; }
                    hideNext = SensitiveName.IsMatch(text) && !text.Contains('=') && text.StartsWith('-');
                }
                if (array[i] != null) array[i] = Sanitize(array[i]!.DeepClone(), secrets);
            }
        }
        else if (node is JsonValue value && value.TryGetValue<string>(out var text))
        {
            // Metadata contains nested JSON strings; sanitize their keys as well.
            if (text.TrimStart().StartsWith('{') || text.TrimStart().StartsWith('['))
            {
                try { var nested = JsonNode.Parse(text); if (nested != null) return JsonValue.Create(Sanitize(nested, secrets).ToJsonString())!; }
                catch (JsonException) { }
            }
            return JsonValue.Create(SensitiveName.IsMatch(text) || secrets.Any(secret => text.Contains(secret, StringComparison.Ordinal)) ? "[redacted: sensitive value]" : text)!;
        }
        return node;
    }
}
