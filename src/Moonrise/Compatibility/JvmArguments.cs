using System.Collections.Immutable;
using System.Text;
using System.Text.RegularExpressions;
namespace Moonrise.Compatibility;

public sealed record JvmArgumentsResult(ImmutableArray<string> Tokens, ImmutableArray<Issue> Issues, ImmutableArray<ArgumentProvenance> Provenance);
public static class JvmArguments
{
    /// <summary>Whitespace separates tokens outside quotes. Backslashes are literal except Windows-style runs immediately before a matching quote; no shell expansion.</summary>
    public static JvmArgumentsResult Parse(string text, string source = "user.jvm")
    {
        var tokens = ImmutableArray.CreateBuilder<string>(); var issues = ImmutableArray.CreateBuilder<Issue>();
        var token = new StringBuilder(); char quote = '\0'; var started = false;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (quote != '\0')
            {
                if (c == quote) quote = '\0';
                else if (c == '\\')
                {
                    var end = i;
                    while (end < text.Length && text[end] == '\\') end++;
                    var slashes = end - i;
                    if (end < text.Length && text[end] == quote)
                    {
                        token.Append('\\', slashes / 2);
                        if (slashes % 2 == 0) quote = '\0'; else token.Append(quote);
                        i = end;
                    }
                    else { token.Append('\\', slashes); i = end - 1; }
                }
                else token.Append(c);
            }
            else if (c is '\'' or '"') { quote = c; started = true; }
            else if (char.IsWhiteSpace(c)) { if (started) { tokens.Add(token.ToString()); token.Clear(); started = false; } }
            else { token.Append(c); started = true; }
        }
        if (quote != '\0') issues.Add(new("jvm.quotes", Severity.Error, "Unterminated quote in JVM arguments.", source));
        if (started) tokens.Add(token.ToString());
        return new(tokens.ToImmutable(), issues.ToImmutable(), tokens.Select(t => new ArgumentProvenance(t, source, "parsed without shell evaluation")).ToImmutableArray());
    }
    public static JvmArgumentsResult Validate(string text, int minimumMb, int maximumMb, ImmutableArray<string> required, string source = "user.jvm")
    {
        var parsed = Parse(text, source); var issues = parsed.Issues.ToBuilder(); var accepted = ImmutableArray.CreateBuilder<string>();
        var provenance = ImmutableArray.CreateBuilder<ArgumentProvenance>();
        var properties = required.Where(a => a.StartsWith("-D", StringComparison.Ordinal)).Select(Property)
            .GroupBy(p => p.Name, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.Last().Value, StringComparer.Ordinal);
        if (minimumMb <= 0 || maximumMb <= 0 || minimumMb > maximumMb)
            issues.Add(new("memory.range", Severity.Error, "Moonrise memory settings require 0 < min <= max.", "settings.memory"));
        foreach (var argument in parsed.Tokens)
        {
            string? code = null; string? message = null; var decision = "accepted";
            if (argument.StartsWith("-Xms", StringComparison.Ordinal) || argument.StartsWith("-Xmx", StringComparison.Ordinal) ||
                Regex.IsMatch(argument, @"^-XX:(InitialHeapSize|MaxHeapSize|MinHeapSize|InitialRAMPercentage|MinRAMPercentage|MaxRAMPercentage|MaxRAMFraction|MinRAMFraction|InitialRAMFraction|MaxRAM)=", RegexOptions.CultureInvariant))
            { code = "jvm.memory.conflict"; message = "Memory argument conflicts with Moonrise settings.memory; edit the managed memory settings."; }
            else if (argument is "-cp" or "-classpath" or "--class-path" || argument.StartsWith("--class-path=", StringComparison.Ordinal) || (argument == "-Djava.class.path" || argument.StartsWith("-Djava.class.path=", StringComparison.Ordinal)))
            { code = "jvm.classpath"; message = "Classpath belongs to LaunchPlan and cannot be overridden."; }
            else if (argument.StartsWith("-javaagent", StringComparison.Ordinal))
            { code = "jvm.raw.agent"; message = "Import the JAR as a structured Java Agent package with options/order."; }
            else if (argument.StartsWith('@') || argument is "-jar" or "-m" or "--module" || argument.StartsWith("--module=", StringComparison.Ordinal) || !argument.StartsWith('-'))
            { code = "jvm.entrypoint"; message = "Entrypoint replacement and hidden argfiles are unsupported."; }
            else if (argument.StartsWith("-agentlib", StringComparison.Ordinal) || argument.StartsWith("-agentpath", StringComparison.Ordinal) ||
                argument.StartsWith("-Xbootclasspath", StringComparison.Ordinal) || argument is "-p" or "--module-path" or "--upgrade-module-path" or "--patch-module" ||
                argument.StartsWith("--module-path=", StringComparison.Ordinal) || argument.StartsWith("--upgrade-module-path=", StringComparison.Ordinal) || argument.StartsWith("--patch-module=", StringComparison.Ordinal))
            { code = "jvm.native.module.unsupported"; message = "Native agents and boot/module path overrides require a verified structured contract."; }
            else if (argument.StartsWith("-D", StringComparison.Ordinal))
            {
                var property = Property(argument);
                if (properties.TryGetValue(property.Name, out var managed))
                {
                    if (managed != property.Value) { code = "jvm.property.conflict"; message = "Property conflicts with contract.requiredJvmArgs."; }
                    else { issues.Add(new("jvm.property.duplicate", Severity.Warning, "Exact managed property duplicate resolved to the contract value.", source,
                        [new(source, argument), new("contract.requiredJvmArgs", "-D" + property.Name + "=" + managed)])); decision = "managed duplicate removed"; }
                }
            }
            else if (!Regex.IsMatch(argument, @"^(-XX:[+-](UseG1GC|UseSerialGC|UseParallelGC)|-ea(?::.*)?|-da(?::.*)?)$"))
                issues.Add(new("jvm.option.unverified", Severity.Warning, "JVM option support is unverified for the selected runtime.", source, [new(source, argument)]));
            if (code != null) { issues.Add(new(code, Severity.Error, message!, source, [new(source, argument), new("settings/contract", code == "jvm.memory.conflict" ? $"memory min={minimumMb}MB max={maximumMb}MB" : "LaunchPlan owns this setting")])); decision = "rejected"; }
            if (decision == "accepted") accepted.Add(argument);
            provenance.Add(new(argument, source, decision));
        }
        var userProperties = accepted.Where(a => a.StartsWith("-D", StringComparison.Ordinal)).Select(Property).GroupBy(p => p.Name, StringComparer.Ordinal);
        foreach (var group in userProperties.Where(g => g.Count() > 1))
            issues.Add(new("jvm.user.property.duplicate", group.Select(p => p.Value).Distinct().Count() > 1 ? Severity.Error : Severity.Warning, "Repeated user property has multiple sources/values.", source, group.Select(p => new Evidence(source, "-D" + p.Name + "=" + p.Value)).ToImmutableArray()));
        var collectors = accepted.Concat(required).Where(a => Regex.IsMatch(a, @"^-XX:\+Use\w+GC$")).Distinct().ToArray();
        if (collectors.Length > 1) issues.Add(new("jvm.gc.conflict", Severity.Error, "Multiple garbage collectors selected.", source));
        return new(accepted.ToImmutable(), issues.ToImmutable(), provenance.ToImmutable());
    }
    private static (string Name, string Value) Property(string argument)
    {
        var equal = argument.IndexOf('='); return equal < 0 ? (argument[2..], "") : (argument[2..equal], argument[(equal + 1)..]);
    }
    public static ImmutableArray<EnvironmentOption> CaptureEnvironment() => new[] { "JAVA_TOOL_OPTIONS", "_JAVA_OPTIONS", "JDK_JAVA_OPTIONS", "IBM_JAVA_OPTIONS", "OPENJ9_JAVA_OPTIONS" }
        .Select(name => new EnvironmentOption(name, Environment.GetEnvironmentVariable(name) ?? "")).Where(e => e.Value.Length > 0).ToImmutableArray();
}
