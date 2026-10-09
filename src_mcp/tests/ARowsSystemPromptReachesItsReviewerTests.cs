using CoaiMcp.Core.Catalog;
using CoaiMcp.Core.Rounds;
using CoaiMcp.Runners.Reviewers;
using CoaiMcp.Server;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// A catalog row's system prompt reaches that row's reviewer — inside the prompt body, after the product's own reviewer
/// instruction and before the finding contract, never on a command line (research/PLAN_one_model_catalog.md, epic 2, story
/// 2 as revised: "the system prompt's leak paths").
/// </summary>
/// <remarks>
/// <para>After the product's instruction, which it cannot replace: the output schema and the read-only rules come after
/// it again, so a person's "ignore the schema" is followed by the schema.</para>
/// <para>A Team server row is NOT given it in the body: there the operator decides whether a client's prompt is taken
/// at all (story 5's contract v2), and putting it in the body would go around that switch.</para>
/// </remarks>
public sealed class ARowsSystemPromptReachesItsReviewerTests
{
    private const string Marker = "MARKER-7f3 answer as terse as you can";

    private static PanelService Service(params ProviderSettings[] providers) =>
        new(new PanelSettings
        {
            DataDir = Path.Combine(Path.GetTempPath(), $"coai-sysprompt-{Guid.NewGuid():N}"),
            Providers = providers,
        },
            VaultKeys.None("no vault"), default, new Runners.Processes.ProcessLauncher(), Serilog.Core.Logger.None, Noticing.None);

    private static ProviderSettings Row(string id, string runtime, string systemPrompt) =>
        new(id) { Enabled = true, Runtime = runtime, Model = "m", BaseUrl = runtime == "local" ? "http://127.0.0.1:11434" : string.Empty, SystemPrompt = systemPrompt };

    private static IReadOnlyList<ReviewerWork> Work(PanelService service) =>
        service.Roster.BuildWork([RoleCatalog.ArchitectureRole], string.Empty, "ctx", round: 1, stage: Stage.PlanReview, readsCheckout: false, codexTiers: CodexTiers.None).Reviewers;

    /// <summary>What a launch hands its child: stdin, a prompt file's text, and the arguments.</summary>
    private static (string Body, string Argv) Sent(ReviewerInvocation invocation)
    {
        var args = invocation.Request.Arguments.ToList();
        var at = args.IndexOf("--prompt-file");
        var file = at >= 0 && at + 1 < args.Count && File.Exists(args[at + 1]) ? File.ReadAllText(args[at + 1]) : string.Empty;

        return (invocation.Request.StdIn + file, string.Join(' ', args));
    }

    [Fact]
    public void TheRowsPromptIsParsed_TrimmedAtItsEdges()
    {
        PanelSettings.ParseVendors("""[{"id":"codex","runtime":"codex","systemPrompt":"  Be terse.\nCite lines.  "}]""")[0]
            .SystemPrompt.Should().Be("Be terse.\nCite lines.");
    }

    [Theory]
    [InlineData("codex")]
    [InlineData("local")]
    public void TheRowsPromptIsInTheBody_AfterTheInstruction_BeforeTheContract_AndNeverInArgv(string runtime)
    {
        var (body, argv) = Sent(Work(Service(Row(runtime, runtime, Marker)))[0].Invocation);

        body.Should().Contain(Marker);
        body.IndexOf(Marker, StringComparison.Ordinal).Should().BeLessThan(body.IndexOf("## The finding contract", StringComparison.Ordinal),
            "the schema comes after the person's words, so they cannot replace it");
        argv.Should().NotContain("MARKER-7f3", "a command line is in process listings and shell history");
    }

    [Fact]
    public void ARowWithoutOne_GetsNoSection()
    {
        var (body, _) = Sent(Work(Service(Row("codex", "codex", string.Empty)))[0].Invocation);

        body.Should().NotContain(CoaiMcp.Core.Catalog.PersonInstruction.Heading);
    }

    [Fact]
    public void ATeamServerRow_IsNotGivenItInTheBody()
    {
        RosterBuilder.InstructionFor(Row("srv-codex", "remote", Marker), new RemoteRuntime("srv-codex", "https://coai.example", "codex"))
            .Should().BeEmpty("on a Team server the operator decides whether a client's prompt is taken (story 5)");
        RosterBuilder.InstructionFor(Row("codex", "codex", Marker), new CodexRuntime()).Should().Be(Marker);
    }

    [Fact]
    public void APromptOverTheLimit_LeavesTheRowOutOfTheRound_NamingTheLimit()
    {
        var service = Service(Row("codex", "codex", new string('x', CatalogLimits.MaxPromptBytes + 1)), Row("claude", "claude", "fine"));

        Work(service).Select(w => w.Invocation.Provider).Should().Equal("claude");
        service.ExcludedFrom(Stage.PlanReview).Should().ContainSingle()
            .Which.Should().Contain("codex").And.Contain($"{CatalogLimits.MaxPromptBytes + 1} bytes").And.Contain($"{CatalogLimits.MaxPromptBytes}");
    }

    [Fact]
    public void ThePromptAtTheLimit_IsTaken_CountedInUtf8Bytes()
    {
        // 4096 two-byte characters are 8192 bytes; one more is over, though it is far fewer characters than the limit.
        var atTheLimit = new string('é', CatalogLimits.MaxPromptBytes / 2);
        Work(Service(Row("codex", "codex", atTheLimit))).Should().ContainSingle();
        Work(Service(Row("codex", "codex", atTheLimit + "é"))).Should().BeEmpty();
    }

    [Fact]
    public void TheLimitIs8192Bytes_TheNumberTheExtensionRefusesPastToo()
    {
        // Pinned on both halves (catalogRow.test.ts pins MAX_PROMPT_BYTES): changing one alone is a red test on that side.
        CatalogLimits.MaxPromptBytes.Should().Be(8192);
    }

    [Fact]
    public void TheBinaryListsTheField()
    {
        FeaturesMode.Listed.Should().Contain("systemPrompt", "the extension writes the field only when this binary lists it");
    }
}
