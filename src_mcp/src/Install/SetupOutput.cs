using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CoaiMcp.Install;

/// <summary>An entry as the output shows it: the command, the arguments, and the environment's NAMES — never a value.</summary>
internal sealed record EntryView(string Command, IReadOnlyList<string> Args, IReadOnlyList<string> EnvKeys)
{
    internal static EntryView? Of(McpEntry? entry) =>
        entry is null ? null : new EntryView(entry.Command, entry.Args, [.. entry.Env.Keys.Order(StringComparer.Ordinal)]);
}

internal sealed record TargetView(string Kind, string File, string Action, string Via, EntryView? Before, EntryView? After, IReadOnlyList<string> ShadowedBy, string Reason);

internal sealed record ClientView(string Cli, bool Found, string Path, string Version, string Result, string Reason, IReadOnlyList<TargetView> Targets);

internal sealed record BinaryView(string Path, string Version);

internal sealed record BackupView(string Id, string Dir);

/// <summary>The ONE object on stdout, the same shape for a dry run and a real one (the contract's <c>Output</c>).</summary>
internal sealed record SetupOutput(string Mode, bool DryRun, BinaryView Binary, string Host, BackupView? Backup, IReadOnlyList<ClientView> Clients, int ExitCode);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(SetupOutput))]
internal sealed partial class SetupOutputJsonContext : JsonSerializerContext;

internal static class SetupOutputJson
{
    private static readonly SetupOutputJsonContext Context = new(new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    });

    /// <summary>The object as one line. Source-generated (this binary is Native-AOT); a path with a non-ASCII letter or <c>&lt;</c> stays as written.</summary>
    internal static string Line(SetupOutput output) => JsonSerializer.Serialize(output, Context.SetupOutput);
}
