using System.Text.Json;

namespace CoaiMcp.Core.QuestionConsult;

/// <summary>
/// What a measurement said about one runtime × capability pair — the five words of
/// <c>shared/runtime-capabilities.json</c>.
/// </summary>
public enum CapabilityStanding
{
    /// <summary>The CLI's own enforced mechanism stops what the capability does not grant.</summary>
    Confined,

    /// <summary>The runtime can do the capability and cannot be held to it — a shell is always there (F2).</summary>
    Unconfined,

    /// <summary>Held only by a headless permission DEFAULT nobody configured; hand-checked, not instrument-confirmed (F7).</summary>
    DefaultDeny,

    /// <summary>The runtime cannot do the capability at all.</summary>
    Unsupported,

    /// <summary>No cell says either way.</summary>
    Unmeasured,
}

/// <summary>The standing words as the file spells them, and the parse that refuses the rest.</summary>
public static class CapabilityStandings
{
    public static string Spelled(this CapabilityStanding standing) => standing switch
    {
        CapabilityStanding.Confined => "confined",
        CapabilityStanding.Unconfined => "unconfined",
        CapabilityStanding.DefaultDeny => "default-deny",
        CapabilityStanding.Unsupported => "unsupported",
        CapabilityStanding.Unmeasured => "unmeasured",
        _ => throw new ArgumentOutOfRangeException(nameof(standing), standing, "a standing this build does not spell — map it here"),
    };

    /// <summary>The standing a file word names; throws for a word the table may not carry (a broken build).</summary>
    public static CapabilityStanding Parse(string word) =>
        Enum.GetValues<CapabilityStanding>().Cast<CapabilityStanding?>()
            .FirstOrDefault(s => string.Equals(s!.Value.Spelled(), word.Trim(), StringComparison.OrdinalIgnoreCase))
        ?? throw new ArgumentException(
            $"'{word}' is not a capability standing — the table spells: {string.Join(", ", Enum.GetValues<CapabilityStanding>().Select(s => s.Spelled()))}",
            nameof(word));
}

/// <summary>Which probe cells a row rests on: the CLI and its build, the day, where the record says it, and the cell ids.</summary>
/// <param name="ResultRef">The section of <c>research/RESULTS_question_consultant_capabilities.md</c> the row cites.</param>
/// <param name="Cells">Cell ids of the full run; empty only when <paramref name="Note"/> says why.</param>
/// <param name="Note">How the standing was read — by construction, hand-checked, not measured.</param>
public sealed record MeasuredWith(string Cli, string Version, string Date, string ResultRef, IReadOnlyList<string> Cells, string Note);

/// <summary>One row of the table: a runtime, a capability, what was measured, and where.</summary>
public sealed record RuntimeCapabilityRow(string Runtime, Capability Capability, CapabilityStanding Standing, MeasuredWith MeasuredWith);

/// <summary>The file's shape, for the source-generated deserialiser. Internal: the table is read, never written.</summary>
internal sealed record RuntimeCapabilitySeed(
    IReadOnlyList<string> Why, IReadOnlyList<string> Runtimes, IReadOnlyList<string> Capabilities, IReadOnlyList<RuntimeCapabilityRowSeed> Rows);

internal sealed record RuntimeCapabilityRowSeed(string Runtime, string Capability, string Standing, MeasuredWithSeed MeasuredWith);

internal sealed record MeasuredWithSeed(string Cli, string Version, string Date, string ResultRef, IReadOnlyList<string>? Cells, string? Note);

/// <summary>
/// The capability table, read once from this assembly's manifest resource — capabilities are DATA
/// (PLAN_question_consultant.md, D3), and every row cites the cell that measured it.
/// </summary>
/// <remarks>
/// <para>Embedded, never read from <c>shared/</c> at run time, for the reason <c>RoleCatalog</c> and
/// <c>CredentialWords</c> give: a published Native-AOT binary runs where no such directory exists,
/// and a confinement table that could go missing is a table that admits nothing — or, worse, a
/// fallback that admits everything. A broken table is a broken BUILD, refused whole at load.</para>
/// <para>The extension generates its copy from the same file; each half asserts its own loader
/// against the file, and both answer <c>shared/capability-matrix-vectors.json</c>.</para>
/// </remarks>
public sealed record RuntimeCapabilities
{
    /// <summary>The table's logical name inside this assembly — see the EmbeddedResource item in CoaiMcp.Core.csproj.</summary>
    internal const string SeedResource = "CoaiMcp.Core.runtime-capabilities.json";

    /// <summary>The runtimes the question consultant can launch, in the file's order — the one list a refusal spells.</summary>
    public static IReadOnlyList<string> Runtimes { get; } = ["claude", "codex", "antigravity", "local", "api"];

    /// <summary>The table, read once from this assembly's manifest resource. Depends on nothing on disk.</summary>
    public static RuntimeCapabilities Builtin { get; } = LoadBuiltin();

    /// <summary>Every row, in the file's order.</summary>
    public IReadOnlyList<RuntimeCapabilityRow> Rows { get; init; } = [];

    /// <summary>The row for one pair, or null for a runtime the table does not know.</summary>
    public RuntimeCapabilityRow? Of(string runtime, Capability capability) =>
        Rows.FirstOrDefault(r => string.Equals(r.Runtime, runtime, StringComparison.OrdinalIgnoreCase) && r.Capability == capability);

    private static RuntimeCapabilities LoadBuiltin()
    {
        using var stream = typeof(RuntimeCapabilities).Assembly.GetManifestResourceStream(SeedResource)
            ?? throw Broken("it is not embedded in this build — check the EmbeddedResource item in CoaiMcp.Core.csproj");
        var seed = JsonSerializer.Deserialize(stream, CoreJsonContext.Default.RuntimeCapabilitySeed)
            ?? throw Broken("it parsed to nothing");

        return FromSeed(seed);
    }

    /// <summary>
    /// The file as a table, refused whole if anything about it is unusable.
    /// </summary>
    /// <remarks>
    /// Every refusal is a broken build: the file is embedded and shipped by us. A pair missing, a
    /// pair twice, a runtime or capability outside the matrix, a standing this build does not spell,
    /// or a row that cites neither a cell nor a reason for having none — each throws naming the row,
    /// because a table that quietly decided a pair by a default arm is the thing D3 exists to end.
    /// </remarks>
    internal static RuntimeCapabilities FromSeed(RuntimeCapabilitySeed seed)
    {
        var rows = (seed.Rows ?? []).Select(Row).ToList();
        MustCoverTheMatrix(rows);

        return new RuntimeCapabilities { Rows = rows };
    }

    private static RuntimeCapabilityRow Row(RuntimeCapabilityRowSeed row)
    {
        if (!Runtimes.Contains(row.Runtime, StringComparer.Ordinal))
        {
            throw Broken($"row '{row.Runtime}' × '{row.Capability}' names a runtime outside the matrix ({string.Join(", ", Runtimes)})");
        }

        if (!QuestionConsult.Capabilities.TryParse(row.Capability ?? string.Empty, out var capability))
        {
            throw Broken($"row '{row.Runtime}' × '{row.Capability}' names a capability outside the matrix ({QuestionConsult.Capabilities.AllSpelled})");
        }

        return new RuntimeCapabilityRow(row.Runtime, capability, Standing(row), Measured(row));
    }

    private static CapabilityStanding Standing(RuntimeCapabilityRowSeed row)
    {
        try
        {
            return CapabilityStandings.Parse(row.Standing ?? string.Empty);
        }
        catch (ArgumentException e)
        {
            throw Broken($"row '{row.Runtime}' × '{row.Capability}': {e.Message}");
        }
    }

    private static MeasuredWith Measured(RuntimeCapabilityRowSeed row)
    {
        var measured = row.MeasuredWith ?? throw Broken($"row '{row.Runtime}' × '{row.Capability}' has no measuredWith");
        var cells = measured.Cells ?? [];
        var note = measured.Note ?? string.Empty;
        if (cells.Count == 0 && note.Length == 0)
        {
            throw Broken($"row '{row.Runtime}' × '{row.Capability}' cites no cell and gives no note saying why — a standing nobody measured is '{CapabilityStanding.Unmeasured.Spelled()}', said so");
        }

        return new MeasuredWith(measured.Cli ?? string.Empty, measured.Version ?? string.Empty, measured.Date ?? string.Empty, measured.ResultRef ?? string.Empty, cells, note);
    }

    private static void MustCoverTheMatrix(IReadOnlyList<RuntimeCapabilityRow> rows)
    {
        foreach (var runtime in Runtimes)
        {
            foreach (var capability in QuestionConsult.Capabilities.All)
            {
                var count = rows.Count(r => r.Runtime == runtime && r.Capability == capability);
                if (count != 1)
                {
                    throw Broken(count == 0
                        ? $"the pair '{runtime}' × '{capability.Spelled()}' has no row, so nothing would decide it"
                        : $"the pair '{runtime}' × '{capability.Spelled()}' appears twice, which is two answers");
                }
            }
        }
    }

    private static InvalidOperationException Broken(string what) =>
        new($"the runtime capability table ('{SeedResource}') is not usable: {what}. "
            + "It is embedded in this binary, so this is a broken build rather than anything a person configured.");
}
