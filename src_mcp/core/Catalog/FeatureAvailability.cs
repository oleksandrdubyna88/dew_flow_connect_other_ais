using System.Text.Json;

namespace CoaiMcp.Core.Catalog;

/// <summary>The shared file as it is written: <c>shared/feature-availability.json</c>.</summary>
public sealed record FeatureAvailabilitySeed(
    IReadOnlyList<string>? Runtimes,
    FeatureListsSeed? Features,
    IReadOnlyList<EffortRowSeed>? Effort,
    IReadOnlyList<ThinkingRowSeed>? Thinking = null);

/// <summary>Which runtimes serve the consultant and the chat.</summary>
public sealed record FeatureListsSeed(IReadOnlyList<string>? Consultant, IReadOnlyList<string>? Chat);

/// <summary>One runtime's efforts as written.</summary>
public sealed record EffortRowSeed(string? Runtime, string? Source, IReadOnlyList<string>? Levels, string? MeasuredWith, string? Note);

/// <summary>One runtime's legal efforts: the levels when <see cref="Source"/> is <c>list</c>, and why when it is not.</summary>
public sealed record EffortRow(string Runtime, string Source, IReadOnlyList<string> Levels, string Note);

public sealed record ThinkingRowSeed(string? Runtime, string? Source, string? Note);

/// <summary>Whether a runtime has a thinking switch (PLAN_one_model_catalog.md D12), and why when it has none.</summary>
public sealed record ThinkingRow(string Runtime, string Source, string Note);

/// <summary>
/// Which runtime serves which feature, and which efforts each accepts — read from the file the extension generates its
/// pickers from (todo/PLAN_one_model_catalog.md D4), so the server and the panel cannot disagree about either.
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

    public static FeatureAvailability Builtin { get; } = LoadBuiltin();

    public IReadOnlyList<string> Consultant { get; init; } = [];

    public IReadOnlyList<string> Chat { get; init; } = [];

    public IReadOnlyList<EffortRow> Effort { get; init; } = [];

    public IReadOnlyList<ThinkingRow> Thinking { get; init; } = [];

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
            Thinking = [.. (seed.Thinking ?? []).Select(row => ThinkingRowOf(row, runtimes))],
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

    private static ThinkingRow ThinkingRowOf(ThinkingRowSeed row, IReadOnlyList<string> runtimes)
    {
        var runtime = row.Runtime ?? string.Empty;
        var source = row.Source ?? string.Empty;

        return runtimes.Contains(runtime, StringComparer.Ordinal) && ThinkingSources.Contains(source, StringComparer.Ordinal)
            ? new ThinkingRow(runtime, source, row.Note ?? string.Empty)
            : throw Broken($"thinking row '{runtime}' has source '{source}'; the runtimes are {string.Join(", ", runtimes)} and the sources {string.Join(", ", ThinkingSources)}");
    }

    private static InvalidOperationException Broken(string what) =>
        new($"the feature availability file ('{SeedResource}') is not usable: {what}. It ships with this build; a broken one is a build defect.");
}
