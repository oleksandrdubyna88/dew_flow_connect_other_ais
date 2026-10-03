using CoaiMcp.Runners.Platform;
using System.Text.Json;
using System.Text.Json.Serialization;
using CoaiMcp.Runners.Reviewers;

namespace CoaiMcp.Runners.Consultation;

/// <summary>Where a consultant stands on one platform: what it can be kept from reading.</summary>
public enum LimitationStanding
{
    /// <summary>Nobody has measured it here, so nothing is claimed.</summary>
    Unmeasured = 0,

    /// <summary>It cannot read outside the repository.</summary>
    Confined,

    /// <summary>It can read outside the repository.</summary>
    Unconfined,

    /// <summary>The CLI refuses what it would have to ask a person about.</summary>
    DefaultDeny,
}

/// <summary>Why a limitation row says what it says — a measurement, or a sentence saying it is not one.</summary>
/// <remarks>
/// A closed pair, because requirement 12 of PLAN_the_consultant_works_on_every_vendor.md is that a limitation never
/// claims more than was measured: a row is either backed by cells somebody ran, or says in words that it was not
/// measured and why we believe it. The loader refuses a row that is neither.
/// </remarks>
public abstract record LimitationEvidence
{
    /// <summary>Measured: when, against which CLI, the cells, and the document that records them.</summary>
    public sealed record Measured(string Date, string CliVersion, string Cells, string Document) : LimitationEvidence;

    /// <summary>Not measured here; the sentence says so, and why the row is believed.</summary>
    public sealed record Sourced(string Sentence) : LimitationEvidence;

    private LimitationEvidence() { }
}

/// <summary>One row of <c>shared/consultant-limitations.json</c>.</summary>
/// <param name="Capability">
/// For claude, <c>restricted</c> or <c>no-restricted</c> (<see cref="ClaudeCapability.Qualifier"/>); empty for every
/// runtime whose standing does not depend on what its CLI accepts.
/// </param>
/// <param name="Text">One or two English sentences a person reads under the consultant's row.</param>
public sealed record ConsultantLimitation(
    string Runtime,
    HostKind Platform,
    string Capability,
    LimitationStanding Standing,
    string Text,
    LimitationEvidence Evidence)
{
    /// <summary>For antigravity: where agy reads <c>permissions.allow</c> on this side; empty otherwise.</summary>
    public string SettingsPath { get; init; } = string.Empty;

    /// <summary>
    /// For antigravity on linux/wsl only: a <c>permissions.allow</c> rule measured to work there, as JSON text for a
    /// person to paste — coai never writes agy's settings. Empty everywhere else.
    /// </summary>
    public string Snippet { get; init; } = string.Empty;

    /// <summary>
    /// The sentence that travels with <see cref="Snippet"/>: a prefix rule is not read-only. Present exactly when a
    /// snippet is (requirement 13) — the loader refuses a row with one and not the other.
    /// </summary>
    public string SnippetWarning { get; init; } = string.Empty;
}

/// <summary>
/// The consultant limitations — <c>shared/consultant-limitations.json</c>, embedded — and the one lookup every reader
/// goes through.
/// </summary>
/// <remarks>
/// <para><b>Embedded, never read from the repository at run time</b>, for the reason <c>ApiDialects</c> gives: a
/// published Native-AOT binary runs where no <c>shared/</c> directory exists. Read through a source-generated
/// <see cref="JsonSerializerContext"/>, so there is no reflection for the trimmer to break.</para>
/// <para><b>It fails CLOSED.</b> A row with no evidence, an unknown platform or standing, or a missing resource is a
/// broken build — a limitation shown without its evidence is the overclaim the file exists to prevent — and it
/// throws on first use. Its test loads it, so the throw is a red suite, not a surprise in the field.</para>
/// <para><b>No platform inherits another's row.</b> A host this file does not describe — a platform nobody has
/// run, <see cref="HostKind.Other"/> — is answered <see cref="LimitationStanding.Unmeasured"/>, never with the
/// nearest platform's text: "codex reads anywhere on Linux" is a claim about Linux.</para>
/// </remarks>
public static class ConsultantLimitations
{
    internal const string Resource = "CoaiMcp.Runners.consultant-limitations.json";

    private static readonly Lazy<IReadOnlyList<ConsultantLimitation>> Loaded = new(() => Parse(Embedded()));

    /// <summary>Every row, in the file's order.</summary>
    public static IReadOnlyList<ConsultantLimitation> All => Loaded.Value;

    /// <summary>
    /// The row for <paramref name="runtime"/> on <paramref name="host"/> whose capability is empty or
    /// <paramref name="capability"/> — or an <see cref="LimitationStanding.Unmeasured"/> row when the file describes none.
    /// </summary>
    /// <remarks>
    /// <para>The capability is part of the key, not a footnote (epic 3's plan round): a claude without
    /// <c>--restricted</c> gets the not-confined row on ANY platform, Windows included, instead of the platform's
    /// measured one — the 9 of 9 that row cites were measured WITH the flag.</para>
    /// <para>A plain qualifier word rather than a <see cref="ClaudeCapability"/> (epic 3's code round): the table is
    /// about runtimes, and a claude type in its key made every other runtime's caller invent a claude value. A
    /// caller holding a turn's RECORDED confinement (<c>ConsultationRecord.Confinement</c>) passes that word, so
    /// what is shown is what ran. An empty word matches only rows that need none — for claude, nothing, so a claude
    /// nobody could probe reads as unmeasured.</para>
    /// </remarks>
    public static ConsultantLimitation Lookup(HostKind host, string runtime, string capability = "") =>
        Lookup(All, host, runtime, capability);

    /// <summary>The claude row its installed CLI's capability selects.</summary>
    public static ConsultantLimitation Lookup(HostKind host, string runtime, ClaudeCapability claude) =>
        Lookup(All, host, runtime, claude.Qualifier);

    /// <summary><see cref="Lookup(HostKind, string, string)"/> over a given table — pure.</summary>
    public static ConsultantLimitation Lookup(IReadOnlyList<ConsultantLimitation> rows, HostKind host, string runtime, string capability) =>
        rows.FirstOrDefault(row => row.Runtime == runtime && row.Platform == host && AppliesTo(row, capability))
        ?? Unmeasured(host, runtime);

    /// <summary>The word the file writes a standing as.</summary>
    public static string Word(LimitationStanding standing) => standing switch
    {
        LimitationStanding.Confined => "confined",
        LimitationStanding.Unconfined => "unconfined",
        LimitationStanding.DefaultDeny => "default-deny",
        _ => "unmeasured",
    };

    /// <summary>The rows of a limitations document, every one validated — or a refusal naming the first bad row.</summary>
    internal static IReadOnlyList<ConsultantLimitation> Parse(string json)
    {
        var file = JsonSerializer.Deserialize(json, ConsultantLimitationsJson.Default.LimitationsFile)
            ?? throw new InvalidOperationException($"{Resource} is empty");

        return [.. (file.Rows ?? []).Select((row, at) => Row(row ?? new LimitationRow(), at))];
    }

    private static bool AppliesTo(ConsultantLimitation row, string capability) =>
        row.Capability.Length == 0 || row.Capability == capability;

    private static ConsultantLimitation Unmeasured(HostKind host, string runtime) => new(
        runtime,
        host,
        string.Empty,
        LimitationStanding.Unmeasured,
        "Not measured on this platform: nothing is claimed about what this consultant can read here.",
        new LimitationEvidence.Sourced(
            $"No row of shared/consultant-limitations.json describes '{runtime}' on {HostKinds.Word(host)}, and another platform's row is not evidence about this one."));

    private static ConsultantLimitation Row(LimitationRow row, int at)
    {
        var platform = PlatformOf(row.Platform, at);
        var (snippet, warning) = SnippetOf(row, platform, at);

        return new(Required(row.Runtime, "runtime", at), platform, CapabilityOf(row.Capability, at),
            StandingOf(row.Standing, at), Required(row.Text, "text", at), EvidenceOf(row, at))
        {
            SettingsPath = row.SettingsPath ?? string.Empty,
            Snippet = snippet,
            SnippetWarning = warning,
        };
    }

    /// <summary>
    /// The snippet and its warning — both or neither, and only on linux/wsl, where a prefix rule was measured to work
    /// (requirement 13; research/RESULTS_agy_allow_rule.md: on Windows only exact command lines ran).
    /// </summary>
    private static (string Snippet, string Warning) SnippetOf(LimitationRow row, HostKind platform, int at) =>
        (row.Snippet ?? string.Empty, row.SnippetWarning ?? string.Empty) switch
        {
            ("", "") => (string.Empty, string.Empty),
            ({ Length: > 0 }, "") or ("", { Length: > 0 }) => throw Bad(at, "carries one of 'snippet' and 'snippetWarning' without the other"),
            var both when platform is HostKind.Linux or HostKind.Wsl => both,
            _ => throw Bad(at, $"offers a snippet on {HostKinds.Word(platform)} — a prefix rule was measured to work on linux and wsl only"),
        };

    private static string Required(string? value, string field, int at) =>
        string.IsNullOrWhiteSpace(value) ? throw Bad(at, $"has no '{field}'") : value;

    private static HostKind PlatformOf(string? word, int at) => word switch
    {
        "windows" => HostKind.Windows,
        "linux" => HostKind.Linux,
        "wsl" => HostKind.Wsl,
        "macos" => HostKind.MacOs,
        _ => throw Bad(at, $"names the platform '{word}' — legal: windows, linux, wsl, macos"),
    };

    private static string CapabilityOf(string? word, int at) => (word ?? string.Empty) switch
    {
        "" or "restricted" or "no-restricted" => word ?? string.Empty,
        _ => throw Bad(at, $"names the capability '{word}' — legal: restricted, no-restricted, or none"),
    };

    private static LimitationStanding StandingOf(string? word, int at) => word switch
    {
        "confined" => LimitationStanding.Confined,
        "unconfined" => LimitationStanding.Unconfined,
        "default-deny" => LimitationStanding.DefaultDeny,
        "unmeasured" => LimitationStanding.Unmeasured,
        _ => throw Bad(at, $"names the standing '{word}' — legal: confined, unconfined, default-deny, unmeasured"),
    };

    /// <summary>EXACTLY one of a complete measurement and a source sentence.</summary>
    private static LimitationEvidence EvidenceOf(LimitationRow row, int at) =>
        (Complete(row.Measured), string.IsNullOrWhiteSpace(row.Source)) switch
        {
            (true, true) => new LimitationEvidence.Measured(row.Measured!.Date!, row.Measured.CliVersion!, row.Measured.Cells!, row.Measured.Document!),
            (false, false) => new LimitationEvidence.Sourced(row.Source!),
            (true, false) => throw Bad(at, "carries both 'measured' and 'source' — a row is one or the other"),
            _ => throw Bad(at, "carries neither a complete 'measured' (date, cliVersion, cells, document) nor a 'source'"),
        };

    private static bool Complete(LimitationMeasured? measured) =>
        measured is not null
        && new[] { measured.Date, measured.CliVersion, measured.Cells, measured.Document }.All(value => !string.IsNullOrWhiteSpace(value));

    private static InvalidOperationException Bad(int at, string what) => new($"{Resource}: row {at} {what}");

    private static string Embedded()
    {
        using var stream = typeof(ConsultantLimitations).Assembly.GetManifestResourceStream(Resource)
            ?? throw new InvalidOperationException($"the embedded resource {Resource} is missing — this build cannot say what a consultant may read");
        using var reader = new StreamReader(stream);

        return reader.ReadToEnd();
    }
}

/// <summary>The file as JSON spells it. Every field nullable: an omitted one arrives null, whatever a default says (doctrine 4a).</summary>
internal sealed record LimitationsFile
{
    public IReadOnlyList<LimitationRow?>? Rows { get; init; }
}

internal sealed record LimitationRow
{
    public string? Runtime { get; init; }

    public string? Platform { get; init; }

    public string? Capability { get; init; }

    public string? Standing { get; init; }

    public string? Text { get; init; }

    public LimitationMeasured? Measured { get; init; }

    public string? Source { get; init; }

    public string? SettingsPath { get; init; }

    public string? Snippet { get; init; }

    public string? SnippetWarning { get; init; }
}

internal sealed record LimitationMeasured
{
    public string? Date { get; init; }

    public string? CliVersion { get; init; }

    public string? Cells { get; init; }

    public string? Document { get; init; }
}

/// <summary>The limitations file's own serializer: source-generated, so the Native-AOT binary reads it without reflection.</summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(LimitationsFile))]
internal sealed partial class ConsultantLimitationsJson : JsonSerializerContext;
