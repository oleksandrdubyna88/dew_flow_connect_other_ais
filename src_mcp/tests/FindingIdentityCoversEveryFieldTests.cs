using CoaiMcp.Core.Findings;
using CoaiMcp.Core.Security;
using FluentAssertions;
using System.Collections.Immutable;
using System.Reflection;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The drift guard for <see cref="FindingIdentity"/>: it lists fields by hand, so a field added to <see cref="Finding"/> would be ignored by it
/// without a sound — and two findings that differ only in that field would read as one. The test lists every public instance property of
/// <see cref="Finding"/> by reflection and fails with a sentence naming the one nobody decided about.
/// </summary>
public sealed class FindingIdentityCoversEveryFieldTests
{
    /// <summary>
    /// Every public instance property of <see cref="Finding"/>, and how <see cref="FindingIdentity.Same"/> treats it. A new property is added here
    /// WITH a decision (and, when it is compared, a mutation in <see cref="Mutations"/>).
    /// </summary>
    private static readonly Dictionary<string, string> Decided = new()
    {
        [nameof(Finding.Severity)] = "compared: value",
        [nameof(Finding.Category)] = "compared: value",
        [nameof(Finding.File)] = "compared: ordinal string",
        [nameof(Finding.Line)] = "compared: value",
        [nameof(Finding.Title)] = "compared: ordinal string",
        [nameof(Finding.Why)] = "compared: ordinal string",
        [nameof(Finding.Fix)] = "compared: ordinal string",
        [nameof(Finding.Providers)] = "compared: sequence, ordinal, null elements are values",
        [nameof(Finding.Role)] = "compared: ordinal string",
        [nameof(Finding.Reproduction)] = "compared: record value",
        [nameof(Finding.AttackEvidence)] = "compared: record value",
        [nameof(Finding.CapReason)] = "compared: ordinal string",
        [nameof(Finding.AlsoSeenBy)] = "compared: sequence of records, null elements are values",
        [nameof(Finding.IsGating)] = "derived from Severity, which is compared",
    };

    private static Finding Baseline() =>
        new(Severity.Blocking, Category.Completeness, "a.md", 3, "The rollback is missing", "nothing undoes the migration", "add the down step", ["codex"])
        {
            Role = "completeness",
            Reproduction = new("pre", "steps", "expected", "actual"),
            AttackEvidence = new("trigger", "mechanism", "consequence"),
            CapReason = "capped",
            AlsoSeenBy = [new("gemini", "prompt", Severity.Major, null, string.Empty)],
        };

    /// <summary>One change per compared property: the identity must see it.</summary>
    private static readonly Dictionary<string, Func<Finding, Finding>> Mutations = new()
    {
        [nameof(Finding.Severity)] = f => f with { Severity = Severity.Minor },
        [nameof(Finding.Category)] = f => f with { Category = Category.Clarity },
        [nameof(Finding.File)] = f => f with { File = "b.md" },
        [nameof(Finding.Line)] = f => f with { Line = 4 },
        [nameof(Finding.Title)] = f => f with { Title = "Another title" },
        [nameof(Finding.Why)] = f => f with { Why = "another reason" },
        [nameof(Finding.Fix)] = f => f with { Fix = "another fix" },
        [nameof(Finding.Providers)] = f => f with { Providers = ["gemini"] },
        [nameof(Finding.Role)] = f => f with { Role = "security" },
        [nameof(Finding.Reproduction)] = f => f with { Reproduction = new("pre", "other steps", "expected", "actual") },
        [nameof(Finding.AttackEvidence)] = f => f with { AttackEvidence = new("trigger", "other mechanism", "consequence") },
        [nameof(Finding.CapReason)] = f => f with { CapReason = "not capped" },
        [nameof(Finding.AlsoSeenBy)] = f => f with { AlsoSeenBy = [new("codex", "prompt", Severity.Major, null, string.Empty)] },
    };

    private static IEnumerable<string> FindingProperties() =>
        typeof(Finding).GetProperties(BindingFlags.Public | BindingFlags.Instance).Select(p => p.Name).Order(StringComparer.Ordinal);

    [Fact]
    public void EveryPublicPropertyOfFinding_HasADecisionHere()
    {
        var undecided = FindingProperties().Where(name => !Decided.ContainsKey(name)).ToList();
        var gone = Decided.Keys.Where(name => !FindingProperties().Contains(name)).ToList();

        undecided.Should().BeEmpty(
            "Finding gained {0}: decide whether FindingIdentity compares it (add it to Decided, and to Mutations when it is compared)",
            string.Join(", ", undecided));
        gone.Should().BeEmpty("Finding lost {0}: remove it from Decided and Mutations", string.Join(", ", gone));
    }

    [Fact]
    public void EveryComparedProperty_IsSeenByTheIdentity_WhenItChanges()
    {
        var compared = Decided.Where(d => d.Value.StartsWith("compared", StringComparison.Ordinal)).Select(d => d.Key).Order(StringComparer.Ordinal);
        Mutations.Keys.Order(StringComparer.Ordinal).Should().Equal(compared, "every compared property has exactly one mutation");

        var baseline = Baseline();
        FindingIdentity.Same(baseline, Baseline()).Should().BeTrue("two baselines built apart are the same finding");
        foreach (var (name, mutate) in Mutations)
        {
            FindingIdentity.Same(baseline, mutate(baseline)).Should().BeFalse(
                "a finding that differs only in {0} is another finding, and FindingIdentity says it is compared", name);
        }
    }
}
