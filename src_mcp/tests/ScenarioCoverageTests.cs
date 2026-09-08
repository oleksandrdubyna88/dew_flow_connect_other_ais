using Xunit;
using FluentAssertions;

namespace CoaiMcp.Tests;

/// <summary>
/// Every flow this server offers is either exercised by a scenario or declared uncovered, with a reason.
/// </summary>
/// <remarks>
/// <para>Required by the family rule <c>common/scenario-tests.md</c> (point 4): the flow list is
/// DERIVED, never typed. A catalogue somebody maintains by hand goes stale in exactly the direction
/// that keeps it green — the tool gets added, the catalogue does not, and nothing says so.</para>
/// <para>So the list comes from the same place the server advertises: the tool registry. Add a tool
/// and this test fails until the tool is either covered by a scenario or written down here as a gap
/// with the reason it is one. An honest gap is a finding; a silent one is a lie the catalogue tells
/// for months.</para>
/// <para>The record a reader looks for is <c>research/module_tests.md</c>, which this test is the
/// enforcement half of.</para>
/// </remarks>
public sealed class ScenarioCoverageTests
{
    /// <summary>
    /// Which scenario drives each tool over a real transport or a real round.
    /// </summary>
    /// <remarks>
    /// The value is the suite, so a reader who wants to see a flow exercised has somewhere to go.
    /// `research/module_tests.md` carries the same table in prose, and this is what keeps the two
    /// from drifting.
    /// </remarks>
    private static readonly Dictionary<string, string> Covered = new(StringComparer.Ordinal)
    {
        ["providers"] = "McpContractTests — over real stdio, against a real coai-mcp process",
        ["open"] = "EndToEndTests — the whole story from a flawed plan to a verdict",
        ["review_plan"] = "EndToEndTests + LiveRoundTests",
        ["review_code"] = "EndToEndTests + StageGateTests (refused before a plan proceeds)",
        ["resolve"] = "EndToEndTests + RoundAuditTests (a decision is recorded for every finding)",
        ["status"] = "McpContractTests + CallerSessionsTests",
        ["reserve_round"] = "AnAddressableRoundSurvivesARestartTests — a locator before any reviewer runs",
        ["run_round"] = "AnAddressableRoundSurvivesARestartTests — one dispatch per locator, replayed on retry",
        ["round_status"] = "AnAddressableRoundSurvivesARestartTests — a lost answer recovered after a restart",
    };

    /// <summary>
    /// The flows no scenario drives, and why not. Every entry is a debt, not a decision.
    /// </summary>
    private static readonly Dictionary<string, string> NotCovered = new(StringComparer.Ordinal)
    {
        ["ask_human"] = "it BLOCKS until a person answers in the panel or on their phone; the wait is "
            + "the behaviour, and a scenario that answers it from a fake surface would exercise the "
            + "fake. Its pieces are covered by EscalationsTests and HumanDecisionTests.",
    };

    [Fact]
    public void EveryToolTheServerAdvertises_IsCoveredOrDeclaredUncovered()
    {
        var advertised = ToolNames();

        advertised.Should().NotBeEmpty("the registry is the source of truth and it answered nothing");
        advertised.Should().BeEquivalentTo(
            Covered.Keys.Concat(NotCovered.Keys),
            "a tool added without a scenario must be VISIBLE — cover it, or write it down here with "
            + "the reason it cannot be covered yet");
    }

    [Fact]
    public void NoFlowIsBothCoveredAndDeclaredUncovered()
    {
        Covered.Keys.Intersect(NotCovered.Keys, StringComparer.Ordinal).Should().BeEmpty(
            "one of the two entries is wrong, and a reader cannot tell which");
    }

    [Fact]
    public void EveryDeclaredGap_CarriesAReason()
    {
        // A reason of "TODO" or an empty string is the silent gap this whole check exists to prevent.
        NotCovered.Should().OnlyContain(gap => gap.Value.Length > 40);
    }

    /// <summary>
    /// The advertised names, from the registry rather than from a list in a test.
    /// </summary>
    /// <remarks>
    /// Read out of the source of <c>Tools.cs</c> because the registry needs a live
    /// <c>PanelServiceHost</c> to enumerate and this check is about NAMES, not about behaviour —
    /// building a host here would make a documentation check depend on a settings file. The file is
    /// the same source of truth either way, and <c>McpContractTests</c> already proves the running
    /// server advertises exactly these over the wire.
    /// </remarks>
    private static IReadOnlyList<string> ToolNames()
    {
        var source = File.ReadAllText(ToolsSourcePath());

        return [.. System.Text.RegularExpressions.Regex
            .Matches(source, "Name = \"(?<name>[a-z_]+)\"")
            .Select(match => match.Groups["name"].Value)];
    }

    private static string ToolsSourcePath()
    {
        var here = new DirectoryInfo(AppContext.BaseDirectory);
        while (here is not null && !File.Exists(Path.Combine(here.FullName, "src_mcp", "src", "Tools.cs")))
        {
            here = here.Parent;
        }

        here.Should().NotBeNull("Tools.cs was not found above the test binary");

        return Path.Combine(here!.FullName, "src_mcp", "src", "Tools.cs");
    }
}
