using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CoaiMcp.Store;

/// <summary>Where one half of a report came from. <c>SourceId</c> is OPAQUE — never the data directory's path (S4).</summary>
/// <param name="SourceId">Lower-case hex of the first 16 bytes of the SHA-256 of the canonical data-directory path.</param>
/// <param name="Platform"><c>windows</c> | <c>linux</c> | <c>macos</c> | <c>other</c>.</param>
/// <param name="ReadUtc">The instant the read ended at — the resolved <c>--until</c>; two halves read at different instants still add.</param>
/// <param name="EffortRecorded">Whether the store has step 22's <c>reviewers.effort</c>.</param>
/// <param name="Sessions">EVERY gate-session id the read touched, sorted ordinally (opaque 8-hex ids, no path, no text).</param>
public sealed record RoundAuthorsSource(string SourceId, string Platform, string ReadUtc, bool EffortRecorded, IReadOnlyList<string> Sessions);

/// <summary>
/// What <c>--round-authors</c> prints by default and what <c>--add</c> reads back: SUMS, counts, sets and the selection — no figure, no mark, no
/// header; those are render-time functions over the merged sums. <c>Format</c> is 1.
/// </summary>
/// <param name="EffortMarkerKnown">Every source's <c>EffortRecorded</c>, re-derived on a merge — false when any half is older than step 22.</param>
public sealed record RoundAuthorsAnswer(
    int Format,
    RoundAuthorsSelection Selection,
    IReadOnlyList<RoundAuthorsSource> Sources,
    IReadOnlyList<CellSums> Cells,
    IReadOnlyList<ExclusionCount> Exclusions,
    bool EffortMarkerKnown);

/// <summary>
/// Its OWN source-generated context rather than a line in <c>ServerJsonContext</c>: a report read back from a file must be STRICT — an unknown
/// property, a missing required one and a null where a value is required are refusals, and those switches are per context. Putting them on the
/// server's shared context would make every session file of an older build unreadable.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    RespectRequiredConstructorParameters = true,
    RespectNullableAnnotations = true)]
[JsonSerializable(typeof(RoundAuthorsAnswer))]
internal sealed partial class RoundAuthorsJson : JsonSerializerContext;

/// <summary>
/// The JSON of sums, and <c>--add</c> of two stores (E6.S4): the default output is the sums, additive, so a WSL run saved to a file plus the
/// Windows run print the combined report without opening a store across the drvfs boundary — and combining two halves equals reporting their union.
/// </summary>
/// <remarks>
/// <para><b>The contract.</b> One JSON document, camelCase, indented: <c>format</c> (1), <c>selection</c> (the flags, <c>untilUtc</c> empty when
/// <c>--until</c> was not given), <c>sources</c> (opaque id, platform, <c>readUtc</c>, <c>effortRecorded</c>, the session ids touched),
/// <c>cells</c> (<see cref="CellSums"/>), <c>exclusions</c> (<see cref="ExclusionCount"/>) and <c>effortMarkerKnown</c>. Every number is an integer
/// and no member is a figure. A cell carries no absolute path: the branch map's keys are an opaque repository id and a branch name.</para>
/// <para><b>Two halves add</b> only when they are one population: the same <c>format</c> and the same selection, field by field; and they share no
/// session — a store added twice, or a COPY of a store read under another path, would double every count, and a session id names one gate session
/// in one store. That last rule is ONE method (<see cref="Overlap"/>), so a decision to let two stores share a session changes one place.</para>
/// </remarks>
public static class RoundAuthorsMerge
{
    public const int Format = 1;

    /// <summary>The report of one store: read, decide who is in, count — as an answer of one source. <paramref name="readUtc"/> is the instant the read ends at when <c>--until</c> was not given.</summary>
    public static RoundAuthorsAnswer Report(string dataDir, RoundAuthorsSelection selection, string readUtc)
    {
        var until = selection.UntilUtc.Length == 0 ? readUtc : selection.UntilUtc;
        var facts = RoundAuthorsQuery.Read(dataDir, selection.Stage, selection.SinceUtc, until);
        var sums = RoundAuthorsCells.Count(RoundAuthorsPopulation.Of(facts, selection), selection);
        var source = SourceOf(dataDir, readUtc, facts);

        return new RoundAuthorsAnswer(Format, selection, [source], sums.Cells, sums.Exclusions, source.EffortRecorded);
    }

    /// <summary>The source of a read: an opaque id of the store, the platform, when the read ended, and the sessions it touched.</summary>
    public static RoundAuthorsSource SourceOf(string dataDir, string readUtc, IReadOnlyList<RoundFacts> facts) => new(
        SourceIdOf(dataDir),
        PlatformFamily(),
        readUtc,
        facts.SelectMany(round => round.Reviewers).All(reviewer => reviewer.EffortRecorded),
        [.. facts.Select(round => round.SessionId).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)]);

    /// <summary>Lower-case hex of the first 16 bytes of the SHA-256 of the canonical path: <c>GetFullPath</c>, no trailing separator, upper-cased where the file system folds case.</summary>
    public static string SourceIdOf(string dataDir)
    {
        var full = Path.GetFullPath(dataDir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(OperatingSystem.IsWindows() ? full.ToUpperInvariant() : full)))[..32].ToLowerInvariant();
    }

    private static string PlatformFamily() =>
        OperatingSystem.IsWindows() ? "windows" : OperatingSystem.IsLinux() ? "linux" : OperatingSystem.IsMacOS() ? "macos" : "other";

    /// <summary>The answer as the one JSON document.</summary>
    public static string Serialize(RoundAuthorsAnswer answer) => JsonSerializer.Serialize(answer, RoundAuthorsJson.Default.RoundAuthorsAnswer);

    /// <summary>A report read back from text. Unknown properties, a missing required one, a wrong type and a null are all refusals, never a guess.</summary>
    public static Outcome<RoundAuthorsAnswer> Parse(string text)
    {
        try
        {
            var parsed = JsonSerializer.Deserialize(text, RoundAuthorsJson.Default.RoundAuthorsAnswer);

            return Accepted(parsed);
        }
        catch (JsonException error)
        {
            return Refused("it is not a round-authors report: " + error.Message);
        }
    }

    private static Outcome<RoundAuthorsAnswer> Accepted(RoundAuthorsAnswer? parsed) =>
        parsed is null ? Refused("it holds no report")
        : Misshapen(parsed) is { Length: > 0 } why ? Refused("it is not a round-authors report: " + why)
        : Outcome<RoundAuthorsAnswer>.Of(parsed);

    /// <summary>
    /// The shapes this program writes and the table and a merge rely on, which the JSON types alone cannot say: an opaque source id of 32
    /// lower-case hex digits, and every branch and requested-to-answered name two words joined by ONE newline. A file that breaks one is refused
    /// (65), never rendered into a crash. Empty when the answer is well shaped.
    /// </summary>
    internal static string Misshapen(RoundAuthorsAnswer answer) =>
        answer.Sources.Any(source => !IsSourceId(source.SourceId)) ? "a source id is not 32 lower-case hex digits"
        : answer.Cells.SelectMany(cell => cell.Branches.Concat(cell.Requested)).Any(pair => pair.Name.Count(at => at == '\n') != 1)
            ? "a branch or requested-to-answered name is not two words joined by one newline"
        : string.Empty;

    private static bool IsSourceId(string id) => id.Length == 32 && id.All(char.IsAsciiHexDigitLower);

    /// <summary>Two halves of one population added: the merged answer, or the sentence that says why they are not one population.</summary>
    public static Outcome<RoundAuthorsAnswer> Add(RoundAuthorsAnswer mine, RoundAuthorsAnswer theirs)
    {
        var why = Incompatible(mine, theirs);

        return why.Length > 0 ? Refused(why) : Outcome<RoundAuthorsAnswer>.Of(Merged(mine, theirs));
    }

    private static string Incompatible(RoundAuthorsAnswer mine, RoundAuthorsAnswer theirs) =>
        mine.Format != theirs.Format
            ? $"the reports have different formats: {mine.Format} and {theirs.Format}"
            : Differs(mine.Selection, theirs.Selection) is { Length: > 0 } field
                ? field
                : Overlap(mine, theirs);

    /// <summary>The first selection field that differs, named with both values — two selections are two populations.</summary>
    private static string Differs(RoundAuthorsSelection mine, RoundAuthorsSelection theirs) =>
        Fields(mine).Zip(Fields(theirs), (left, right) => (left.Name, Mine: left.Value, Theirs: right.Value)).Where(pair => pair.Mine != pair.Theirs)
            .Select(pair => $"the reports were asked for different things: `{pair.Name}` is '{pair.Mine}' in one and '{pair.Theirs}' in the other")
            .FirstOrDefault() ?? string.Empty;

    private static (string Name, string Value)[] Fields(RoundAuthorsSelection selection) =>
    [
        ("stage", selection.Stage), ("since", selection.SinceUtc), ("until", selection.UntilUtc), ("split", selection.SplitUtc),
        ("cohort", selection.Cohort), ("kinds", string.Join(',', selection.Kinds)), ("with-before-open", selection.WithBeforeOpen.ToString()),
        ("without-elided", selection.WithoutElided.ToString()), ("with-uncovered", selection.WithUncovered.ToString()),
    ];

    /// <summary>
    /// THE one place the shared-session policy lives: a source present in both, or two sources that touched one session, would count a gate
    /// session twice (a copy of a store read under another path is a different <c>SourceId</c> with the same sessions). Empty when the halves are disjoint.
    /// </summary>
    internal static string Overlap(RoundAuthorsAnswer mine, RoundAuthorsAnswer theirs)
    {
        var sameSource = mine.Sources.Select(source => source.SourceId).Intersect(theirs.Sources.Select(source => source.SourceId), StringComparer.Ordinal).Any();
        var shared = mine.Sources.SelectMany(source => source.Sessions).Intersect(theirs.Sources.SelectMany(source => source.Sessions), StringComparer.Ordinal).Count();

        return sameSource ? "the same store is added twice; adding it would count each of its gate sessions twice"
            : shared > 0 ? $"the reports share {shared} gate session(s); adding them would count each twice"
            : string.Empty;
    }

    private static RoundAuthorsAnswer Merged(RoundAuthorsAnswer mine, RoundAuthorsAnswer theirs)
    {
        var sources = (IReadOnlyList<RoundAuthorsSource>)[.. mine.Sources, .. theirs.Sources];

        return mine with
        {
            Sources = sources,
            Cells = RoundAuthorsCells.Ordered(mine.Cells.Concat(theirs.Cells).GroupBy(cell => cell.Key).Select(group => group.Aggregate(RoundAuthorsCells.Add))),
            Exclusions = RoundAuthorsExclusions.Ordered(
                mine.Exclusions.Concat(theirs.Exclusions).GroupBy(count => (count.Cohort, count.Period, count.Reason)).Select(group => group.Aggregate(RoundAuthorsExclusions.Add))),
            EffortMarkerKnown = sources.All(source => source.EffortRecorded),
        };
    }

    private static Outcome<RoundAuthorsAnswer> Refused(string why) => Outcome<RoundAuthorsAnswer>.Refuse(why);
}
