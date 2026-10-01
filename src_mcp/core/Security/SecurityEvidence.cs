using System.Collections.Immutable;
using System.Text.Json;
using CoaiMcp.Core.Findings;
using CoaiMcp.Core.Gate;

namespace CoaiMcp.Core.Security;

/// <summary>Evidence supplied by a reviewer, never a command to execute.</summary>
public sealed record Reproduction(string Preconditions, string Steps, string Expected, string Actual)
{
    public const int MaxCharacters = 8000;
    public bool Complete => new[] { Preconditions, Steps, Expected, Actual }.All(s => !string.IsNullOrWhiteSpace(s));

    public static Reproduction? Read(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Object) return null;
        var parts = new[] { "preconditions", "steps", "expected", "actual" }.Select(key => Part(value, key)).ToArray();
        return parts.Sum(s => s.Length) <= MaxCharacters ? new(parts[0], parts[1], parts[2], parts[3]) : null;
    }

    private static string Part(JsonElement value, string key) =>
        value.TryGetProperty(key, out var part) && part.ValueKind == JsonValueKind.String ? part.GetString()!.Trim() : string.Empty;
}

public sealed record SecuritySighting(string Provider, string Prompt, Severity Severity, Reproduction? Reproduction, string CapReason)
{
    public AttackEvidence? AttackEvidence { get; init; }
}

/// <summary>Cap before merging; ordinary ownership never suppresses stronger reproduced evidence.</summary>
public static class SecurityEvidence
{
    public static Finding Attribute(Finding finding, string provider, string prompt)
    {
        var reason = finding.IsGating && finding.Reproduction is not { Complete: true }
            ? "Capped at minor: reproduction needs preconditions, steps, expected and actual (at most 8000 characters total)."
            : string.Empty;
        var severity = reason.Length > 0 ? Severity.Minor : finding.Severity;
        return finding with
        {
            Role = SecurityCatalog.Gate,
            Severity = severity,
            CapReason = reason,
            AlsoSeenBy = [new(provider, prompt, severity, finding.Reproduction, reason) { AttackEvidence = finding.AttackEvidence }],
        };
    }

    public static ImmutableArray<Finding> Merge(IEnumerable<Finding> findings) =>
        FindingDedup.Merge(findings.OrderBy(f => f.Role == SecurityCatalog.Gate));
}
