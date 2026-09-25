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

        var requests = ImmutableArray.CreateBuilder<SourceRequest>();
        var refusedRequests = ImmutableArray.CreateBuilder<RejectedEntry>();
        ReadRequests(raw.SourceRequests, requests, refusedRequests);

        return new ParseOutcome.Success(
            new NormalisedReview(findings.ToImmutable(), rejected.ToImmutable())
            {
                // Trimmed to empty rather than carried as whitespace: a reviewer that filled the
                // field with a space has said nothing, and a caller must not have to tell the two
                // apart. Absent, null and blank are one answer.
                Notes = raw.Notes?.Trim() ?? string.Empty,
                SourceRequests = requests.ToImmutable(),
                RejectedSourceRequests = refusedRequests.ToImmutable(),
            });
    }

    /// <summary>
    /// A feature reviewer's requests for code, each one read or refused BY NAME — never a crash.
    /// </summary>
    /// <remarks>
    /// Only the feature schema offers the field, so on every other stage this sees nothing and
    /// answers two empty lists. A path is checked here, lexically, with the same rule the collector
    /// uses for a stored path (<see cref="RepoPaths"/>): a request that could climb out of the
    /// repository is refused before anything could spend a process on it.
    /// </remarks>
    private static void ReadRequests(
        List<RawSourceRequest?>? raw,
        ImmutableArray<SourceRequest>.Builder requests,
        ImmutableArray<RejectedEntry>.Builder refused)
    {
        foreach (var (entry, index) in (raw ?? []).Select((r, i) => (r, i)))
        {
            switch (Request(entry))
            {
                case (SourceRequest request, null):
                    requests.Add(request);
                    break;
                case (null, string reason):
                    refused.Add(new RejectedEntry(index, reason));
                    break;
            }
        }
    }

    private static (SourceRequest?, string?) Request(RawSourceRequest? raw)
    {
        if (raw is null)
        {
            return (null, "not a request object");
        }

        var file = raw.File?.Trim() ?? string.Empty;
        if (RepoPaths.WhyNotRelative(file) is { Length: > 0 } why)
        {
            return (null, file.Length == 0 ? $"the request {why}" : $"'{file}' {why}; only files in the repository are served");
        }

        if (SpanProblem(raw.StartLine, raw.EndLine) is { } problem)
        {
            return (null, $"'{file}': {problem}");
        }

        return (new SourceRequest(
            file,
            raw.Symbol?.Trim() ?? string.Empty,
            raw.StartLine ?? 0,
            raw.EndLine ?? 0,
            raw.Why?.Trim() ?? string.Empty), null);
    }

    /// <summary>What is wrong with a requested span, or nothing. Lines are 1-based, the end inclusive.</summary>
    private static string? SpanProblem(int? start, int? end) => (start, end) switch
    {
        ( < 1, _) => $"startLine {start} is not a line (lines are 1-based)",
        (null, not null) => "an endLine needs a startLine",
        (_, < 1) => $"endLine {end} is not a line (lines are 1-based)",
        ({ } from, { } to) when to < from => $"endLine {to} is before startLine {from}, so the lines name no span",
        _ => null,
    };

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
        "clarity" => Category.Clarity,
        "completeness" => Category.Completeness,
        "consistency" => Category.Consistency,
        "feasibility" => Category.Feasibility,
        _ => null,
    };
}
