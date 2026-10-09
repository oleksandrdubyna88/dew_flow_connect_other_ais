using System.Text.Json;

namespace CoaiMcp.Core.Catalog;

/// <summary>The shared file as it is written: <c>shared/feature-availability.json</c>.</summary>
public sealed record FeatureAvailabilitySeed(
    IReadOnlyList<string>? Runtimes,
    FeatureListsSeed? Features,
    IReadOnlyList<EffortRowSeed>? Effort,
    IReadOnlyList<ThinkingRowSeed>? Thinking = null,
    IReadOnlyList<FastModeRowSeed>? FastMode = null);

/// <summary>Which runtimes serve the consultant and the chat.</summary>
public sealed record FeatureListsSeed(IReadOnlyList<string>? Consultant, IReadOnlyList<string>? Chat);

/// <summary>One runtime's efforts as written.</summary>
public sealed record EffortRowSeed(string? Runtime, string? Source, IReadOnlyList<string>? Levels, string? MeasuredWith, string? Note);

/// <summary>One runtime's legal efforts: the levels when <see cref="Source"/> is <c>list</c>, and why when it is not.</summary>
public sealed record EffortRow(string Runtime, string Source, IReadOnlyList<string> Levels, string Note);

public sealed record ThinkingRowSeed(string? Runtime, string? Source, string? Note);

/// <summary>Whether a runtime has a thinking switch (PLAN_one_model_catalog.md D12), and why when it has none.</summary>
public sealed record ThinkingRow(string Runtime, string Source, string Note);

public sealed record FastModeRowSeed(
    string? Runtime, string? Source, IReadOnlyList<string>? Models, string? MeasuredWith, string? Note,
    ReleaseRangeSeed? RefusesStandard = null);

/// <summary>A range of CLI releases as written: two <c>X.Y.Z</c> strings, both ends included.</summary>
public sealed record ReleaseRangeSeed(string? From, string? Through);

/// <summary>
/// A closed range of CLI releases, both ends included, compared as NUMBERS — <c>0.12.0</c> is below <c>0.110.0</c>,
/// which a string comparison would get backwards.
/// </summary>
public sealed record ReleaseRange(Version From, Version Through)
{
    /// <summary>The range no release is in — a row with no such range says nothing about any release.</summary>
    public static ReleaseRange None { get; } = new(new Version(int.MaxValue, 0, 0), new Version(0, 0, 0));

    public bool Contains(Version release) => release >= From && release <= Through;
}

/// <summary>
/// Whether a runtime has a fast tier (research/PLAN_fast_mode.md, decision 2): on <c>every-model</c>, on the listed
/// <c>models</c> only, or <c>none</c> — and, always, why.
/// </summary>
public sealed record FastModeRow(string Runtime, string Source, IReadOnlyList<string> Models, string Note)
{
    /// <summary>
    /// The releases that refuse to be told the STANDARD tier (research/PLAN_codex_tier_floor.md): codex 0.110.0–0.130.0
    /// take only <c>fast</c> or <c>flex</c> for <c>service_tier</c> and fail the whole launch on <c>default</c>
    /// (research/RESULTS_codex_service_tier_versions_2026-10-07.md). Data beside the measurement that found it, so the
    /// next measured release is a file edit; <see cref="ReleaseRange.None"/> on every row that names none.
    /// </summary>
    public ReleaseRange RefusesStandard { get; init; } = ReleaseRange.None;
}

/// <summary>
/// Which runtime serves which feature, and which efforts each accepts — read from the file the extension generates its
/// pickers from (research/PLAN_one_model_catalog.md D4), so the server and the panel cannot disagree about either.
/// </summary>
/// <remarks>
/// Embedded like <c>runtime-capabilities.json</c>, and REFUSED whole when it is not usable: a server that guessed a list
/// would run a consultant the panel never offered, or refuse one it did.
/// </remarks>
public sealed class FeatureAvailability
{
    internal const string SeedResource = "CoaiMcp.Core.feature-availability.json";

    private static readonly string[] Sources = ["list", "probe", "unmeasured", "none"];

    // Declared BEFORE Builtin: static fields initialise in textual order, and Builtin reads the file through these.
    private static readonly string[] ThinkingSources = ["probe", "unmeasured", "none"];

    private static readonly string[] FastSources = ["every-model", "models", "none"];

    /// <summary>One end of a <c>refusesStandard</c> range: three dot-separated numbers, nothing around them.</summary>
    private static readonly System.Text.RegularExpressions.Regex ReleaseShape = new("^[0-9]+\\.[0-9]+\\.[0-9]+$");

    public static FeatureAvailability Builtin { get; } = LoadBuiltin();

    public IReadOnlyList<string> Consultant { get; init; } = [];

    public IReadOnlyList<string> Chat { get; init; } = [];

    public IReadOnlyList<EffortRow> Effort { get; init; } = [];

    public IReadOnlyList<ThinkingRow> Thinking { get; init; } = [];

    public IReadOnlyList<FastModeRow> FastMode { get; init; } = [];

    /// <summary>One runtime's fast tier — a runtime the file does not name has none.</summary>
    public FastModeRow FastModeOf(string runtime) =>
        FastMode.FirstOrDefault(row => string.Equals(row.Runtime, runtime, StringComparison.OrdinalIgnoreCase))
        ?? new FastModeRow(runtime, "none", [], $"'{runtime}' is not a runtime the feature-availability file names");

    /// <summary>
    /// Whether a ROW has a fast tier: its runtime and model by the file — and a codex row only on codex's own service, as
    /// <c>CodexRuntime.TierArgs</c> decides. The extension's <c>rowHasFastTier</c> asks the same.
    /// </summary>
    public bool RowHasFastTier(string runtime, string model, string baseUrl) =>
        HasFastTier(runtime, model) && !(string.Equals(runtime, "codex", StringComparison.OrdinalIgnoreCase) && baseUrl.Length > 0);

    /// <summary>
    /// Whether <paramref name="model"/> on <paramref name="runtime"/> has a fast tier — a listed model matched with its
    /// case and any <c>[1m]</c>-style suffix set aside; an empty model only on a runtime whose every model has one.
    /// </summary>
    public bool HasFastTier(string runtime, string model) => FastModeOf(runtime) switch
    {
        { Source: "every-model" } => true,
        { Source: "models" } row => row.Models.Contains(model.Split('[')[0].Trim().ToLowerInvariant(), StringComparer.Ordinal),
        _ => false,
    };

    /// <summary>One runtime's thinking switch — a runtime the file does not name has none.</summary>
    public ThinkingRow ThinkingOf(string runtime) =>
        Thinking.FirstOrDefault(row => string.Equals(row.Runtime, runtime, StringComparison.OrdinalIgnoreCase))
        ?? new ThinkingRow(runtime, "none", $"'{runtime}' is not a runtime the feature-availability file names");

    /// <summary>One runtime's efforts — a runtime the file does not name takes none.</summary>
    public EffortRow EffortOf(string runtime) =>
        Effort.FirstOrDefault(row => string.Equals(row.Runtime, runtime, StringComparison.OrdinalIgnoreCase))
        ?? new EffortRow(runtime, "none", [], $"'{runtime}' is not a runtime the feature-availability file names");

    private static FeatureAvailability LoadBuiltin()
    {
        using var stream = typeof(FeatureAvailability).Assembly.GetManifestResourceStream(SeedResource)
            ?? throw Broken("it is not embedded in this build — check the EmbeddedResource item in CoaiMcp.Core.csproj");
        var seed = JsonSerializer.Deserialize(stream, CoreJsonContext.Default.FeatureAvailabilitySeed)
            ?? throw Broken("it parsed to nothing");

        return FromSeed(seed);
    }

    /// <summary>A seed checked the way the extension's generator checks it, then read.</summary>
    public static FeatureAvailability FromSeed(FeatureAvailabilitySeed seed)
    {
        var runtimes = seed.Runtimes ?? [];

        return new FeatureAvailability
        {
            Consultant = Within(seed.Features?.Consultant, runtimes, "consultant"),
            Chat = Within(seed.Features?.Chat, runtimes, "chat"),
            Effort = [.. (seed.Effort ?? []).Select(row => Row(row, runtimes))],
            Thinking = ThinkingRows(seed.Thinking, runtimes),
            FastMode = FastModeRows(seed.FastMode, runtimes),
        };
    }

    /// <summary>A feature's runtimes: at least one, and every one a runtime the file names.</summary>
    private static IReadOnlyList<string> Within(IReadOnlyList<string>? list, IReadOnlyList<string> runtimes, string feature)
    {
        var named = list ?? [];
        if (named.Count == 0)
        {
            throw Broken($"features.{feature} names no runtime");
        }

        return named.FirstOrDefault(one => !runtimes.Contains(one, StringComparer.Ordinal)) is { } stranger
            ? throw Broken($"features.{feature} names '{stranger}', which is not one of the runtimes ({string.Join(", ", runtimes)})")
            : named;
    }

    private static EffortRow Row(EffortRowSeed row, IReadOnlyList<string> runtimes)
    {
        var runtime = row.Runtime ?? string.Empty;
        var source = row.Source ?? string.Empty;

        return runtimes.Contains(runtime, StringComparer.Ordinal) && Sources.Contains(source, StringComparer.Ordinal)
            ? new EffortRow(runtime, source, row.Levels ?? [], row.Note ?? string.Empty)
            : throw Broken($"effort row '{runtime}' has source '{source}'; the runtimes are {string.Join(", ", runtimes)} and the sources {string.Join(", ", Sources)}");
    }

    /// <summary>
    /// The thinking rows — exactly one per runtime, as the extension's generator requires (PR #687's review): a missing
    /// runtime would read as "none" and a duplicate as whichever came first. A seed with no list (from before D12) has none.
    /// </summary>
    private static IReadOnlyList<ThinkingRow> ThinkingRows(IReadOnlyList<ThinkingRowSeed>? seed, IReadOnlyList<string> runtimes)
    {
        if (seed is null)
        {
            return [];
        }
        IReadOnlyList<ThinkingRow> rows = [.. seed.Select(row => ThinkingRowOf(row, runtimes))];

        return runtimes.FirstOrDefault(runtime => rows.Count(row => row.Runtime == runtime) != 1) is { } off
            ? throw Broken($"runtime '{off}' has {rows.Count(row => row.Runtime == off)} thinking rows; every runtime has exactly one")
            : rows;
    }

    private static ThinkingRow ThinkingRowOf(ThinkingRowSeed row, IReadOnlyList<string> runtimes)
    {
        var runtime = row.Runtime ?? string.Empty;
        var source = row.Source ?? string.Empty;

        return runtimes.Contains(runtime, StringComparer.Ordinal) && ThinkingSources.Contains(source, StringComparer.Ordinal)
            ? new ThinkingRow(runtime, source, row.Note ?? string.Empty)
            : throw Broken($"thinking row '{runtime}' has source '{source}'; the runtimes are {string.Join(", ", runtimes)} and the sources {string.Join(", ", ThinkingSources)}");
    }

    /// <summary>The fast-mode rows — exactly one per runtime, as for thinking; a seed from before the block has none.</summary>
    private static IReadOnlyList<FastModeRow> FastModeRows(IReadOnlyList<FastModeRowSeed>? seed, IReadOnlyList<string> runtimes)
    {
        if (seed is null)
        {
            return [];
        }
        IReadOnlyList<FastModeRow> rows = [.. seed.Select(row => FastModeRowOf(row, runtimes))];

        return runtimes.FirstOrDefault(runtime => rows.Count(row => row.Runtime == runtime) != 1) is { } off
            ? throw Broken($"runtime '{off}' has {rows.Count(row => row.Runtime == off)} fast-mode rows; every runtime has exactly one")
            : rows;
    }

    private static FastModeRow FastModeRowOf(FastModeRowSeed row, IReadOnlyList<string> runtimes)
    {
        var runtime = row.Runtime ?? string.Empty;
        var source = row.Source ?? string.Empty;

        return runtimes.Contains(runtime, StringComparer.Ordinal) && FastSources.Contains(source, StringComparer.Ordinal)
            ? Listed(new FastModeRow(runtime, source, row.Models ?? [], row.Note ?? string.Empty) { RefusesStandard = RangeOf(runtime, row.RefusesStandard) })
            : throw Broken($"fast-mode row '{runtime}' has source '{source}'; the runtimes are {string.Join(", ", runtimes)} and the sources {string.Join(", ", FastSources)}");
    }

    /// <summary>
    /// A row's <c>refusesStandard</c> range, checked as the generator checks it (<c>FAST_RULES</c>): on codex only — the one
    /// runtime told a tier through <c>-c service_tier</c>, so a range anywhere else would be read by nothing — with both
    /// ends <c>X.Y.Z</c> and the first not after the second. A row that names none has <see cref="ReleaseRange.None"/>.
    /// </summary>
    private static ReleaseRange RangeOf(string runtime, ReleaseRangeSeed? seed) => seed switch
    {
        null => ReleaseRange.None,
        _ when runtime != "codex" => throw Broken($"fast-mode row '{runtime}' has a refusesStandard range, which only codex may carry"),
        _ => Ordered(runtime, new ReleaseRange(ReleaseOf(runtime, seed.From), ReleaseOf(runtime, seed.Through))),
    };

    private static ReleaseRange Ordered(string runtime, ReleaseRange range) =>
        range.From <= range.Through
            ? range
            : throw Broken($"fast-mode row '{runtime}' has a refusesStandard range that ends before it starts ({range.From} to {range.Through})");

    /// <summary>One end of a range: exactly three dot-separated numbers, as <c>codex --version</c> prints them.</summary>
    private static Version ReleaseOf(string runtime, string? text) =>
        text is not null && ReleaseShape.IsMatch(text)
            ? Version.Parse(text)
            : throw Broken($"fast-mode row '{runtime}' has a refusesStandard end '{text}', which is not a release (X.Y.Z)");

    /// <summary>
    /// The generator's rule (<c>FAST_RULES</c>, PR #693's review): a <c>models</c> source lists its models, and every
    /// other source lists none — an empty list would read as "no model has the tier", a list beside another source as
    /// nothing at all.
    /// </summary>
    private static FastModeRow Listed(FastModeRow row) =>
        (row.Source == "models") == (row.Models.Count > 0)
            ? row
            : throw Broken($"fast-mode row '{row.Runtime}' has source '{row.Source}' and {row.Models.Count} models: a 'models' source lists its models, and every other source lists none");

    private static InvalidOperationException Broken(string what) =>
        new($"the feature availability file ('{SeedResource}') is not usable: {what}. It ships with this build; a broken one is a build defect.");
}
