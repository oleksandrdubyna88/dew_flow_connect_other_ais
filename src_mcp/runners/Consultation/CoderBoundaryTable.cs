using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace CoaiMcp.Runners.Consultation;

/// <summary>One property a verdict relies on, with the class that says where it is asked (judgement section 8): <c>pre</c>, <c>launch</c>, <c>turn</c> or <c>release</c>.</summary>
/// <param name="Id">The id the judgement file lists (<c>U2</c>, <c>CX-V1</c>, <c>AG-G3</c>).</param>
/// <param name="Class">The first class of the judgement's class cell.</param>
/// <param name="Also">The other classes of the cell (<c>pre, and launch</c>), in the order written; empty for most ids.</param>
public sealed record CoderProperty(string Id, string Class, IReadOnlyList<string> Also);

/// <summary>One measured release of a vendor's CLI: exact, and tied to the two headings and the program file that were measured.</summary>
/// <param name="Release">The release as the CLI's own <c>--version</c> names it.</param>
/// <param name="ResultsHeading">The heading in the vendor's results file under which the row measure of this release is.</param>
/// <param name="JudgementHeading">The heading in the judgement file under which the security judgement of this release is.</param>
/// <param name="Sha256">The sha256 of the measured program file, lower-case hex.</param>
/// <param name="Stream">The values of the CLI's own stream that a turn check compares for this release (name, value).</param>
public sealed record CoderRelease(string Release, string ResultsHeading, string JudgementHeading, string Sha256, IReadOnlyDictionary<string, string> Stream);

/// <summary>One vendor of the table: the verdict, the measured releases and the ids the verdict relies on.</summary>
/// <param name="Id">The vendor.</param>
/// <param name="Verdict"><c>ship</c> or <c>refuse</c>.</param>
/// <param name="Results">The vendor's results file, from the repository's top.</param>
/// <param name="Releases">Every measured release. Empty until the commit that follows a measure adds one.</param>
/// <param name="Properties">The ids the verdict relies on today.</param>
/// <param name="Pending">Ids the judgement lists that enter the table only with the addendum that follows their measure.</param>
public sealed record CoderVendorRow(string Id, string Verdict, string Results, IReadOnlyList<CoderRelease> Releases, IReadOnlyList<CoderProperty> Properties, IReadOnlyList<string> Pending)
{
    /// <summary>The verdict word that lets a vendor be launched.</summary>
    public const string Ship = "ship";

    /// <summary>The verdict word that never does.</summary>
    public const string Refuse = "refuse";

    /// <summary>Whether this vendor's coder may be launched: the verdict is <see cref="Ship"/> AND at least one release was measured. A verdict with no release is not a launch: "asked for" is not "held".</summary>
    public bool Ships => Verdict == Ship && Releases.Count > 0;
}

/// <summary>
/// <c>shared/coder-boundary-releases.json</c>, embedded: the ONE table of which vendors may be launched as a coder besides claude, and on which exact releases (epoch 3 E3 s7 t1; judgement S5; SPEC C2 item 1).
/// </summary>
/// <remarks>
/// <para><b>One place decides.</b> Nothing else in the product says that codex or agy is shipped or refused; <see cref="CoderVendorSet"/> reads this table and
/// <c>ACoderVendorSeamTests</c> scans the sources for a second place. It starts with the verdict <c>ship</c> and NO release for both vendors, so both are refused by data until the commit of s5 t6 or s6 t7 adds one.</para>
/// <para><b>It fails CLOSED.</b> A file that does not parse, a verdict that is neither word, a class that is not one of the four, a release with a blank field or a sha256 that is not 64 hex characters is a broken build: the first use throws, and the test that loads it is a red suite.</para>
/// <para>claude is not in it: claude keeps the floor of <c>ClaudeWriteCapability</c> and is always launchable.</para>
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
    public IReadOnlyList<string> ShippedVendors => [Claude, .. Vendors.Where(row => row.Ships).Select(row => row.Id)];

    /// <summary>
    /// Why a coder is not launched on <paramref name="vendor"/>, with what to do — empty when it may be. The sentence names the shipped vendors, and says which of the three things is true: the table has no row, the verdict is refuse, or no release was measured.
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
            { Verdict: CoderVendorRow.Ship } => $"the verdict for '{vendor}' is ship, but no release of its CLI was measured yet",
            _ => $"the verdict for '{vendor}' is refuse",
        };

        return $"the coder is not shipped for '{vendor}': {reason}; the coder vendors that are shipped are {string.Join(", ", ShippedVendors)} — set the coder's model to a model of one of them (COAI_ROLES_MODELS names the lists)";
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
        var releases = (row.Releases ?? []).Select((release, index) => Release(release ?? new ReleaseFile(), $"{id}.releases[{index}]")).ToList();
        Unique(releases.Select(release => release.Release), $"release of {id}");

        return new CoderVendorRow(id, verdict, Required(row.Results, $"{id}.results"), releases, [.. (row.Properties ?? []).Select((property, index) => Property(property, $"{id}.properties[{index}]"))], [.. row.Pending ?? []]);
    }

    private static CoderRelease Release(ReleaseFile row, string where)
    {
        var sha = Required(row.Sha256, $"{where}.sha256");

        return Sha256Hex().IsMatch(sha)
            ? new CoderRelease(Required(row.Release, $"{where}.release"), Required(row.ResultsHeading, $"{where}.resultsHeading"), Required(row.JudgementHeading, $"{where}.judgementHeading"), sha, new Dictionary<string, string>(row.Stream ?? new Dictionary<string, string>()))
            : throw Bad($"{where}.sha256 is not 64 lower-case hex characters");
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

    public IReadOnlyList<ReleaseFile?>? Releases { get; init; }

    public IReadOnlyList<PropertyFile?>? Properties { get; init; }

    public IReadOnlyList<string>? Pending { get; init; }
}

internal sealed record ReleaseFile
{
    public string? Release { get; init; }

    public string? ResultsHeading { get; init; }

    public string? JudgementHeading { get; init; }

    public string? Sha256 { get; init; }

    public IReadOnlyDictionary<string, string>? Stream { get; init; }
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
