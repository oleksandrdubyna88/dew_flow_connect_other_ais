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
        ["review_document"] = "ADocumentIsReviewedEndToEndTests — a document role nobody compiled in "
            + "reads a real document through the public tool, and its summary comes back",
        ["review_feature"] = "AFeatureIsReviewedEndToEndTests — a real repository, the fake CLI as the feature "
            + "reviewer: the pack reaches its stdin, resolve(feature) closes it, again over a new head, the D17 and "
            + "no-reviewer skips, all-failed calling a person, a credential file withheld",
        ["resolve"] = "EndToEndTests + RoundAuditTests (a decision is recorded for every finding)",
        ["status"] = "McpContractTests + CallerSessionsTests",
        ["cons"] = "ConsultScenarioTests — two turns over the fake CLI as codex: a thread opened, "
            + "resumed by its own id, the cap closing it, the ledger and the record written",
        ["cons_close"] = "ConsultScenarioTests — a real consultation opened over the fake CLI and "
            + "then ENDED: the verdict on the record, a repeat that changes nothing, a conflicting "
            + "outcome refused, a cap-closed one lapsing and still taking a verdict",
        ["ask_human"] = "McpContractTests — a REAL server build over real stdio: v2 keeps the tool registered as an alias, and it answers at once "
            + "with exactly {status: ask_in_conversation, note: removed in v2…} whatever the arguments, writing no card",
        ["roles"] = "TheRolesToolTests — a REAL server over real stdio: the table for the client that called, a setting read from the environment; beneath it ARolesAnswerTests (the owner's Claude table, the ordered failover list, codex and agy empty, every override and its refusal) and TheRolesModeTests (--roles, the hook output, 65, no data directory)",
        ["start_role"] = "ARoleToolsOverStdioTests — a REAL server over real stdio starts a probe job, answers in two seconds or less, is KILLED with its process tree, and a NEW server reads the job succeeded (A1); beneath it ARoleToolsTests (the refusals, the cap A15, the environment file), ADetachedLauncherTests (the road order, the unit's memory properties A14) and ARoleRunnerTests (the runner's exit codes)",
        ["role_status"] = "ARoleToolsOverStdioTests (a new server's role_status after the starter was killed) and ARoleToolsTests (the shape, the wait, a dead runner closed as lost with one notice); beneath them ARoleJobLeaseTests (A4, A5)",
        ["role_cancel"] = "ARoleToolsTests — a live job, a dead runner, a job already over (A6), and a STALLED runner killed after the grace with the generation raised (E.3); beneath it ARoleRunnerTests (a stale runner publishes nothing, exit 75)",
        ["validate_work"] = "TheValidateWorkToolTests — a REAL server over real stdio: a plan in the working tree answered with the device's limits, an invalid one with its errors, every refused argument (absolute path, `..`, a link, no feature.yaml) as {error}; beneath it TheValidateWorkModeTests (the one-shot and the tool answer the same bytes, the exit codes, never 64) and AWorkPlanValidatorTests / AWorkPlanVectorsTests (the rules)",
        ["consask"] = "QuestionConsultScenarioTests — the fake CLI as a codex row, a REAL child: the planner's "
            + "argv reaches a process, its answer file is read back fenced, the record and the ledger line are on disk; "
            + "a web question carrying a path refused before any launch. Beneath it: QuestionFanOutTests (one row past "
            + "its deadline → partial; a blocked row never launched; a web row given the sanitised question alone; a none "
            + "api row the context and outline, a disk row the context and its roots; the invariant once, a breach "
            + "withholding disk advice alone; six rows in one tick) and QuestionConsultServiceTests (the argument "
            + "refusals, off, none_available, the eleventh question quota_spent, every answer fenced and separate)",
    };

    /// <summary>
    /// The flows no scenario drives, and why not. Every entry is a debt, not a decision.
    /// </summary>
    private static readonly Dictionary<string, string> NotCovered = new(StringComparer.Ordinal)
    {
        // Empty: `ask_human` was the one debt once, and v2 removed its wait — the alias is covered over the real wire.
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
        // A reason of "TODO" or an empty string is the silent gap this whole check exists to prevent. Spelled as
        // "no gap with a short reason" rather than OnlyContain, which refuses an EMPTY list — and the list has been
        // empty since S3 of the question consultant covered `ask_human`; a gap added later is still named here.
        NotCovered.Where(gap => gap.Value.Length <= 40).Should().BeEmpty("a declared gap says why it is one");
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
