using System.Text.Json;

namespace CoaiMcp.Core.Api;

/// <summary>
/// The dialect table — <c>shared/api-dialects.json</c>, embedded in this assembly.
/// </summary>
/// <remarks>
/// <para><b>Embedded, never read from the repository at run time</b>, for the reason
/// <see cref="Notices.CredentialWords"/> gives: a published Native-AOT binary runs where no
/// <c>shared/</c> directory exists. The extension keeps its own list of the NAMES
/// (<c>apiDialects.ts</c>) and a test on each side is held to the one file.</para>
/// <para><b>It fails CLOSED.</b> A resource that is absent or unparseable is a broken build, and a
/// reviewer sent with a guessed body would spend a paid call to find out. The throw happens on first
/// use, which is a local review or an api review — a loud place.</para>
/// </remarks>
public static class ApiDialects
{
    internal const string Resource = "CoaiMcp.Core.api-dialects.json";

    /// <summary>The body the local reviewer sends — pinned byte for byte, the one row that MUST exist.</summary>
    public const string LocalName = "local";

    /// <summary>The generic hosted dialect, and the default for an <c>api</c> row that names none.</summary>
    public const string OpenAiName = "openai";

    private static readonly Lazy<IReadOnlyDictionary<string, ApiDialect>> Loaded = new(Embedded);

    /// <summary>Every dialect name this build knows, in the file's order.</summary>
    public static IReadOnlyCollection<string> Names => [.. Loaded.Value.Keys];

    /// <summary>One dialect by name, or null when this build has no such row — case-insensitive.</summary>
    public static ApiDialect? Named(string name) =>
        Loaded.Value.TryGetValue(name.Trim(), out var dialect) ? dialect : null;

    /// <summary>The local body's dialect. Cannot be missing: the build refuses to load without it.</summary>
    public static ApiDialect Local => Loaded.Value[LocalName];

    /// <summary>The generic hosted dialect. Cannot be missing either.</summary>
    public static ApiDialect OpenAi => Loaded.Value[OpenAiName];

    private static IReadOnlyDictionary<string, ApiDialect> Embedded()
    {
        using var stream = typeof(ApiDialects).Assembly.GetManifestResourceStream(Resource)
            ?? throw new InvalidOperationException(
                $"the embedded resource {Resource} is missing — this build cannot spell a completion request");
        using var document = JsonDocument.Parse(stream);
        if (!document.RootElement.TryGetProperty("dialects", out var rows) || rows.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException($"{Resource} carries no 'dialects' object");
        }

        var table = new Dictionary<string, ApiDialect>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in rows.EnumerateObject())
        {
            table[row.Name] = ApiDialect.From(row.Name, row.Value);
        }

        foreach (var required in (string[])[LocalName, OpenAiName])
        {
            if (!table.ContainsKey(required))
            {
                throw new InvalidOperationException($"{Resource} has no '{required}' dialect — the local body cannot be spelled");
            }
        }

        return table;
    }
}
