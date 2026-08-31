using System.Collections.Immutable;
using System.Text.Json;

namespace CoaiMcp.Core.Findings;

/// <summary>What parsing one reviewer's answer produced — a closed union, never an exception.</summary>
public abstract record ParseOutcome
{
    /// <summary>Findings normalised; entries that could not become findings are named beside them.</summary>
    public sealed record Success(NormalisedReview Review) : ParseOutcome;

    /// <summary>The text was not the schema's JSON at all. One repair attempt, then a named failure.</summary>
    public sealed record Malformed(string Reason) : ParseOutcome;

    private ParseOutcome() { }
}

/// <summary>
/// One reviewer's JSON → one <see cref="NormalisedReview"/>, whichever vendor produced it.
/// </summary>
/// <remarks>
/// <para>An unknown severity or category is a <b>named rejection of that entry</b>, never a guess
/// and never a crash — a reviewer inventing <c>"critical"</c> must not take its four valid
/// findings down with it, and must not be silently promoted to anything either.</para>
/// <para>A finding with no file and no line survives as a repo-level finding: plan-stage remarks
/// have nothing to point at, and dropping them would silently un-gate the plan stage.</para>
/// </remarks>
public static class ReviewParser
{
    public static ParseOutcome Parse(string json, string provider)
    {
        RawReview? raw;
        try
        {
            raw = JsonSerializer.Deserialize(json, CoreJsonContext.Default.RawReview);
        }
        catch (JsonException e)
        {
            return new ParseOutcome.Malformed($"not valid JSON: {e.Message}");
        }

        if (raw?.Findings is null)
        {
            return new ParseOutcome.Malformed("valid JSON, but no \"findings\" array");
        }

        var findings = ImmutableArray.CreateBuilder<Finding>();
        var rejected = ImmutableArray.CreateBuilder<RejectedEntry>();
        foreach (var (entry, index) in raw.Findings.Select((f, i) => (f, i)))
        {
            switch (Normalise(entry, provider))
            {
                case (Finding finding, null):
                    findings.Add(finding);
                    break;
                case (null, string reason):
                    rejected.Add(new RejectedEntry(index, reason));
                    break;
            }
        }

        return new ParseOutcome.Success(new NormalisedReview(findings.ToImmutable(), rejected.ToImmutable()));
    }

    private static (Finding?, string?) Normalise(RawFinding raw, string provider)
    {
        if (string.IsNullOrWhiteSpace(raw.Title))
        {
            return (null, "no title — a finding that cannot be named cannot be acted on");
        }

        if (ParseSeverity(raw.Severity) is not { } severity)
        {
            return (null, $"unknown severity '{raw.Severity}'");
        }

        if (ParseCategory(raw.Category) is not { } category)
        {
            return (null, $"unknown category '{raw.Category}'");
        }

        return (new Finding(
            severity,
            category,
            raw.File ?? string.Empty,
            raw.Line ?? 0,
            raw.Title.Trim(),
            raw.Why?.Trim() ?? string.Empty,
            raw.Fix?.Trim() ?? string.Empty,
            [provider]), null);
    }

    internal static Severity? ParseSeverity(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "blocking" => Severity.Blocking,
        "major" => Severity.Major,
        "minor" => Severity.Minor,
        "nit" => Severity.Nit,
        _ => null,
    };

    internal static Category? ParseCategory(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "architecture" => Category.Architecture,
        "security" => Category.Security,
        "reliability" => Category.Reliability,
        "performance" => Category.Performance,
        "ux" => Category.Ux,
        "convention" => Category.Convention,
        _ => null,
    };
}
