using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace CoaiMcp.Runners.Consultation;

/// <summary>One property a verdict relies on, with the class that says where it is asked (judgement section 8): <c>pre</c>, <c>launch</c>, <c>turn</c> or <c>release</c>.</summary>
/// <param name="Id">The id the judgement file lists (<c>U2</c>, <c>CX-V1</c>, <c>AG-G3</c>).</param>
/// <param name="Class">The first class of the judgement's class cell.</param>
/// <param name="Also">The other classes of the cell (<c>pre, and launch</c>), in the order written; empty for most ids.</param>
public sealed record CoderProperty(string Id, string Class, IReadOnlyList<string> Also);

/// <summary>One program file that was measured: its name and its sha256. History — nothing compares an installed program with it.</summary>
public sealed record CoderProgramRecord(string File, string Sha256);

/// <summary>
/// One MEASURE of a vendor's CLI, as history ("measured on X", the key <c>measured</c>): the release, the date, what the record is, the heading of the results under which the measure is, the heading of the judgement that rests on it, the later results headings and the program files with their
/// sha256. Information for a person: no code compares an installed release or hash with it (the owner's decision of 2026-11-26).
/// </summary>
/// <param name="Release">The release as the CLI's own <c>--version</c> named it.</param>
/// <param name="MeasuredOn">The date, or the dates, of the measure, in words.</param>
/// <param name="What">What the record is: history only.</param>
/// <param name="ResultsHeading">The heading in the vendor's results file under which the row measure is.</param>
/// <param name="JudgementHeading">The heading in the judgement file under which the security judgement of this measure is.</param>
/// <param name="LaterResultsHeadings">The headings of results added after that judgement (measures of rows it left open).</param>
/// <param name="Programs">The program files that were measured, each with its sha256, lower-case hex.</param>
public sealed record CoderMeasure(string Release, string MeasuredOn, string What, string ResultsHeading, string JudgementHeading, IReadOnlyList<string> LaterResultsHeadings, IReadOnlyList<CoderProgramRecord> Programs);

/// <summary>One vendor of the table: the verdict, the mark <c>launchable</c>, the history of its measures and the ids the verdict relies on.</summary>
/// <param name="Id">The vendor.</param>
/// <param name="Verdict"><c>ship</c> or <c>refuse</c>.</param>
/// <param name="Launchable">The owner's condition of 2026-11-26: the coder of the vendor may be launched only when the must-changes of its judgement are built and green. OFF for codex and agy; ONE commit turns it on (s5 t5 for codex, s8 t6 for agy).</param>
/// <param name="Results">The vendor's results file, from the repository's top.</param>
/// <param name="Measured">The history of the measures, oldest first. Information only: it makes nothing launchable.</param>
/// <param name="Properties">The ids the verdict relies on today.</param>
/// <param name="Pending">Ids the judgement lists that enter the table only with the addendum that follows their measure.</param>
public sealed record CoderVendorRow(string Id, string Verdict, bool Launchable, string Results, IReadOnlyList<CoderMeasure> Measured, IReadOnlyList<CoderProperty> Properties, IReadOnlyList<string> Pending)
{
    /// <summary>The verdict word that lets a vendor be launched.</summary>
    public const string Ship = "ship";

    /// <summary>The verdict word that never does.</summary>
    public const string Refuse = "refuse";

    /// <summary>
    /// Whether this vendor's coder may be launched: the verdict is <see cref="Ship"/> AND the mark <see cref="Launchable"/> is on (the owner's condition of 2026-11-26, SPEC C2 item 1). The history of measures and the CLI release that is installed are a RECORD and never a gate: a release that is
    /// not in the history, newer or older, runs, and the job records the installed release beside <see cref="LastMeasured"/>.
    /// </summary>
    public bool Ships => Verdict == Ship && Launchable;

    /// <summary>The release measured last (the last of the history), or null when none was measured: what a job's record names beside the release that is installed.</summary>
    public CoderMeasure? LastMeasured => Measured.Count > 0 ? Measured[^1] : null;
}

/// <summary>
/// <c>shared/coder-boundary-releases.json</c>, embedded: the ONE table of which vendors may be launched as a coder besides claude, and on which exact releases (epoch 3 E3 s7 t1; judgement S5; SPEC C2 item 1).
/// </summary>
/// <remarks>
/// <para><b>One place decides.</b> Nothing else in the product says that codex or agy is shipped or refused; <see cref="CoderVendorSet"/> reads this table and
/// <c>ACoderVendorSeamTests</c> scans the sources for a second place. It starts with the verdict <c>ship</c> and the mark <c>launchable</c> OFF for both vendors, so both are refused by data until the ONE commit that turns a mark on (s5 t5 for codex, s8 t6 for agy) after the must-changes of the judgement's addenda are built and green; the history of measures (the key <c>measured</c>) is information and never a condition.</para>
/// <para><b>It fails CLOSED.</b> A file that does not parse, a verdict that is neither word, a class that is not one of the four, a release with a blank field or a sha256 that is not 64 hex characters is a broken build: the first use throws, and the test that loads it is a red suite.</para>
/// <para>claude is not in it: claude is always launchable, whatever release is installed (<c>ClaudeWriteCapability</c> records it).</para>
/// </remarks>
public sealed partial record CoderBoundaryTable(string Judgement, IReadOnlyList<CoderProperty> Shared, IReadOnlyList<CoderVendorRow> Vendors)
{
    internal const string Resource = "CoaiMcp.Runners.coder-boundary-releases.json";

    /// <summary>The vendor that is always launchable and is not in the table.</summary>
    public const string Claude = "claude";

    private static readonly string[] Classes = ["pre", "launch", "turn", "release"];

    private static readonly Lazy<CoderBoundaryTable> Loaded = new(() => Parse(Embedded()));

    /// <summary>The table the build carries.</summary>
    public static CoderBoundaryTable Shipped => Loaded.Value;

    /// <summary>The row of <paramref name="vendor"/>, or null when the table has none.</summary>
    public CoderVendorRow? Row(string vendor) => Vendors.FirstOrDefault(row => row.Id == vendor);

    /// <summary>The vendors whose coder may be launched: claude, then every vendor of the table that ships.</summary>
    public IReadOnlyList<string> LaunchableVendors => [Claude, .. Vendors.Where(row => row.Ships).Select(row => row.Id)];

    /// <summary>
    /// Why a coder is not launched on <paramref name="vendor"/>, with what to do — empty when it may be. The sentence names the shipped vendors, and says which of the three things is true: the table has no row, the verdict is refuse, or the verdict is ship and the mark <c>launchable</c> is off. A release that was never measured, or an installed one that differs, is none of them.
    /// </summary>
    public string WhyNot(string vendor)
    {
        if (vendor == Claude || Row(vendor) is { Ships: true })
        {
            return string.Empty;
        }

        var reason = Row(vendor) switch
        {
            null => $"the table of coder boundaries has no row for '{vendor}'",
            { Verdict: CoderVendorRow.Ship } => $"the verdict for '{vendor}' is ship, but its coder is not launchable yet: the must-changes of its judgement are not built and green, and one commit turns the mark on",
            _ => $"the verdict for '{vendor}' is refuse",
        };

        return $"the coder is not launchable for '{vendor}': {reason}; the coder vendors that are launchable are {string.Join(", ", LaunchableVendors)} — set the coder's model to a model of one of them (COAI_ROLES_MODELS names the lists)";
    }

    /// <summary>The ids of every property the table relies on for <paramref name="vendor"/>: the shared ones, then the vendor's own.</summary>
    public IReadOnlyList<CoderProperty> PropertiesOf(string vendor) => [.. Shared, .. Row(vendor)?.Properties ?? []];

    /// <summary>The table of a document, every row validated — or an exception naming the first bad one.</summary>
    public static CoderBoundaryTable Parse(string json)
    {
        var file = JsonSerializer.Deserialize(json, CoderBoundaryTableJson.Default.TableFile)
            ?? throw new InvalidOperationException($"{Resource} is empty");
        if (file.Version != 1)
        {
            throw Bad($"has version {file.Version?.ToString() ?? "none"}; this build reads version 1");
        }

        var shared = (file.Shared ?? []).Select((row, at) => Property(row, $"shared[{at}]")).ToList();
        var vendors = (file.Vendors ?? []).Select((row, at) => Vendor(row ?? new VendorFile(), at)).ToList();
        Unique(vendors.Select(row => row.Id), "vendor");
        Unique(shared.Concat(vendors.SelectMany(row => row.Properties)).Select(property => property.Id), "property id");

        return new CoderBoundaryTable(Required(file.Judgement, "judgement"), shared, vendors);
    }

    private static CoderVendorRow Vendor(VendorFile row, int at)
    {
        var id = Required(row.Id, $"vendors[{at}].id");
        if (!VendorId().IsMatch(id) || id == Claude)
        {
            throw Bad($"vendors[{at}] names '{id}': a vendor id is lower-case letters, digits and '-', and claude is never in the table");
        }

        var verdict = row.Verdict is CoderVendorRow.Ship or CoderVendorRow.Refuse ? row.Verdict : throw Bad($"vendor '{id}' has the verdict '{row.Verdict}' — legal: ship, refuse");
        var measured = (row.Measured ?? []).Select((record, index) => Measure(record ?? new MeasureFile(), $"{id}.measured[{index}]")).ToList();
        Unique(measured.Select(record => record.Release), $"measured release of {id}");

        return new CoderVendorRow(id, verdict, row.Launchable ?? false, Required(row.Results, $"{id}.results"), measured, [.. (row.Properties ?? []).Select((property, index) => Property(property, $"{id}.properties[{index}]"))], [.. row.Pending ?? []]);
    }

    private static CoderMeasure Measure(MeasureFile row, string where)
    {
        var programs = (row.Programs ?? []).Select((program, index) =>
        {
            var sha = Required(program?.Sha256, $"{where}.programs[{index}].sha256");

            return Sha256Hex().IsMatch(sha) ? new CoderProgramRecord(Required(program?.File, $"{where}.programs[{index}].file"), sha) : throw Bad($"{where}.programs[{index}].sha256 is not 64 lower-case hex characters");
        }).ToList();

        return new CoderMeasure(Required(row.Release, $"{where}.release"), row.MeasuredOn ?? string.Empty, row.What ?? string.Empty, Required(row.ResultsHeading, $"{where}.resultsHeading"), Required(row.JudgementHeading, $"{where}.judgementHeading"), [.. row.LaterResultsHeadings ?? []], programs);
    }

    private static CoderProperty Property(PropertyFile? row, string where)
    {
        var id = Required(row?.Id, $"{where}.id");
        var kind = Required(row?.Class, $"{where}.class");
        var also = row?.Also ?? [];
        if (!Classes.Contains(kind) || also.Any(word => !Classes.Contains(word)))
        {
            throw Bad($"{where} ('{id}') has a class that is not one of {string.Join(", ", Classes)}");
        }

        return new CoderProperty(id, kind, [.. also]);
    }

    private static void Unique(IEnumerable<string> values, string what)
    {
        if (values.GroupBy(value => value, StringComparer.Ordinal).FirstOrDefault(group => group.Count() > 1) is { } twice)
        {
            throw Bad($"names the {what} '{twice.Key}' twice");
        }
    }

    private static string Required(string? value, string field) =>
        string.IsNullOrWhiteSpace(value) ? throw Bad($"has no '{field}'") : value;

    private static InvalidOperationException Bad(string what) => new($"{Resource}: {what}");

    [GeneratedRegex("^[a-z][a-z0-9-]*$", RegexOptions.CultureInvariant)]
    private static partial Regex VendorId();

    [GeneratedRegex("^[0-9a-f]{64}$", RegexOptions.CultureInvariant)]
    private static partial Regex Sha256Hex();

    private static string Embedded()
    {
        using var stream = typeof(CoderBoundaryTable).Assembly.GetManifestResourceStream(Resource)
            ?? throw new InvalidOperationException($"the embedded resource {Resource} is missing — this build cannot say which vendors may be launched as a coder");
        using var reader = new StreamReader(stream);

        return reader.ReadToEnd();
    }
}

/// <summary>The file as JSON spells it. Every field nullable: an omitted one arrives null, whatever a default says (doctrine 4a).</summary>
internal sealed record TableFile
{
    public int? Version { get; init; }

    public string? Judgement { get; init; }

    public IReadOnlyList<PropertyFile?>? Shared { get; init; }

    public IReadOnlyList<VendorFile?>? Vendors { get; init; }
}

internal sealed record VendorFile
{
    public string? Id { get; init; }

    public string? Verdict { get; init; }

    public string? Results { get; init; }

    public bool? Launchable { get; init; }

    public IReadOnlyList<MeasureFile?>? Measured { get; init; }

    public IReadOnlyList<PropertyFile?>? Properties { get; init; }

    public IReadOnlyList<string>? Pending { get; init; }
}

internal sealed record MeasureFile
{
    public string? Release { get; init; }

    public string? MeasuredOn { get; init; }

    public string? What { get; init; }

    public string? ResultsHeading { get; init; }

    public string? JudgementHeading { get; init; }

    public IReadOnlyList<string>? LaterResultsHeadings { get; init; }

    public IReadOnlyList<ProgramFile?>? Programs { get; init; }
}

internal sealed record ProgramFile
{
    public string? File { get; init; }

    public string? Sha256 { get; init; }
}

internal sealed record PropertyFile
{
    public string? Id { get; init; }

    public string? Class { get; init; }

    public IReadOnlyList<string>? Also { get; init; }
}

/// <summary>The table's own serializer: source-generated, so the Native-AOT binary reads it without reflection.</summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(TableFile))]
internal sealed partial class CoderBoundaryTableJson : JsonSerializerContext;
