using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CoaiMcp.Install;

/// <summary>An MCP entry as the manifest keeps it. Values ARE kept (a restore needs them) — in a 0600 file under the state folder, never printed.</summary>
internal sealed record EntryRecord(string Command, string[] Args, Dictionary<string, string> Env, string Raw)
{
    /// <summary>The entry of a manifest, or none. A field the file omitted is null whatever the initializer says (csharp doctrine 4a), so every one is normalised here.</summary>
    internal static EntryRecord? Of(McpEntry? entry) =>
        entry is null ? null : new EntryRecord(entry.Command, [.. entry.Args], entry.Env.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal), entry.RawJson);

    internal McpEntry ToEntry() => new(Command ?? string.Empty, Args ?? [], (Env ?? []).ToDictionary(pair => pair.Key, pair => pair.Value ?? string.Empty, StringComparer.Ordinal), Raw ?? string.Empty);
}

/// <summary>One file's line in a manifest: what it was, what the install made it, how far the install got, and — when it did not end done — why (the redacted sentence of the report; a manifest of before 2.0 has none).</summary>
internal sealed record TargetRecord(string Cli, string Kind, string File, string Sha256Before, EntryRecord? Before, EntryRecord? After, string State, string Reason = "");

/// <summary>What one run planned and did, written before the first change and rewritten at every step — the reason a killed run can be finished.</summary>
internal sealed record ManifestRecord(string Id, string Binary, string StartedUtc, TargetRecord[] Targets);

/// <summary>The states of a manifest line. The first four are the contract; the rest say how a line ended when it did not simply verify (a line that did not end done also holds its reason).</summary>
internal static class ManifestStates
{
    internal const string Planned = "planned";
    internal const string Removed = "removed";
    internal const string Written = "written";
    internal const string Verified = "verified";

    /// <summary>A failed step was undone: the file is as it was.</summary>
    internal const string RolledBack = "rolledBack";

    /// <summary>A failed step could not be undone either.</summary>
    internal const string Failed = "failed";

    /// <summary>A later run finished what this one left half done.</summary>
    internal const string Superseded = "superseded";

    /// <summary>The target was refused before anything was run for it: the file is as it was, and the reason says why.</summary>
    internal const string Refused = "refused";
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = true, DefaultIgnoreCondition = JsonIgnoreCondition.Never)]
[JsonSerializable(typeof(ManifestRecord))]
internal sealed partial class InstallManifestJsonContext : JsonSerializerContext;

/// <summary>Reading and writing <c>manifest.json</c>. A manifest that cannot be read is skipped, never fatal: it is evidence, and a run goes on without it.</summary>
internal static class ManifestFile
{
    private static readonly InstallManifestJsonContext Context = new(new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    });

    internal const string Name = "manifest.json";

    internal static void Write(string dir, ManifestRecord manifest) =>
        InstallFiles.WritePrivateAtomically(Path.Combine(dir, Name), JsonSerializer.Serialize(manifest, Context.ManifestRecord));

    internal static ManifestRecord? Read(string dir)
    {
        try
        {
            var path = Path.Combine(dir, Name);

            return File.Exists(path) && JsonSerializer.Deserialize(File.ReadAllText(path), Context.ManifestRecord) is { } manifest
                ? manifest with { Targets = manifest.Targets ?? [] }
                : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }
}
