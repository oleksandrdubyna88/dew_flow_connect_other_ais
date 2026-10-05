using CoaiMcp.Core.Catalog;
using CoaiMcp.Core.Findings;
using CoaiMcp.Core.Rounds;
using CoaiMcp.Runners.Reviewers;
using CoaiMcp.Server;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// A Team server row sends its effort and its system prompt as contract 2's FIELDS, and what the server did not apply
/// reaches that reviewer's note (todo/PLAN_one_model_catalog.md, epic 2, story 5).
/// </summary>
/// <remarks>
/// <para>The system prompt travels in a file beside the prompt file — never argv, which is in every process listing — and
/// both files go when the turn does. A row with neither field launches, and sends, exactly what it did before.</para>
/// <para>"Sent" is not "applied": a contract-2 server names each field it dropped or lowered, and a contract-1 server
/// read none of them. Either way the person reads it on the reviewer, not in a log.</para>
/// </remarks>
public sealed class ATeamServerRowSendsContractTwoTests
{
    private const string Marker = "MARKER-c2 cite every line you rely on";

    private static ProviderSettings Row(string runtime, string effort = "", string systemPrompt = "") =>
        new("srv-claude") { Enabled = true, Runtime = runtime, Model = "opus", CliEffort = effort, SystemPrompt = systemPrompt };

    private static ReviewerInvocation Built(ReviewerSettings settings)
    {
        var dir = Directory.CreateTempSubdirectory("coai-c2-").FullName;

        return new RemoteRuntime("srv-claude", "https://coai.example", "claude")
            .Build(RoleCatalog.ArchitectureRole, "review this", dir, Path.Combine(dir, "schema.json"), dir, settings);
    }

    [Fact]
    public void ThisClientSpeaksContractTwo_AsTheExtensionDoes() =>
        // teamServerApi.ts CONTRACT_VERSION pins the same number in its own test; neither half reads the other's source.
        RemoteAsk.ContractVersion.Should().Be(2);

    [Fact]
    public void TheRowsEffortAndPrompt_AreSentAsFields_ThePromptInAFile_NeverArgv()
    {
        var invocation = Built(new ReviewerSettings("srv-claude") { Model = "opus", ReasoningEffort = "high", SystemPrompt = Marker });
        var argv = invocation.Request.Arguments;

        argv.Should().ContainInConsecutiveOrder("--effort", "high");
        var file = argv[argv.ToList().IndexOf("--system-prompt-file") + 1];
        File.ReadAllText(file).Should().Be(Marker);
        argv.Should().NotContain(a => a.Contains("MARKER-c2"), "argv is in every process listing");
        invocation.TempFiles.Should().Contain(file, "the system prompt's file goes when the turn does");
    }

    [Fact]
    public void ARowWithNeither_LaunchesAsBefore()
    {
        var argv = Built(new ReviewerSettings("srv-claude") { Model = "opus" }).Request.Arguments;

        argv.Should().NotContain("--effort").And.NotContain("--system-prompt-file");
        RemoteAsk.RequestBody("claude", "opus", "Architecture", "p", 60).Should().NotContain("effort").And.NotContain("systemPrompt");
    }

    [Fact]
    public void TheRoster_GivesARemoteRowItsOwnEffort_AndItsPromptAsTheField()
    {
        var remote = new RemoteRuntime("srv-claude", "https://coai.example", "claude");

        RosterBuilder.EffortFor(Row("remote", effort: "high"), localEffort: "none").Should().Be("high",
            "the Team server judges a remote row's effort per vendor — the local engines' setting is not it");
        RosterBuilder.FieldPromptFor(Row("remote", systemPrompt: Marker), remote).Should().Be(Marker);
        RosterBuilder.FieldPromptFor(Row("claude", systemPrompt: Marker), new ClaudeRuntime()).Should().BeEmpty(
            "a local row carries its prompt in the body, and a field too would send it twice");
    }

    [Fact]
    public void TheBody_CarriesBothFields_WhenSet()
    {
        var body = RemoteAsk.RequestBody("claude", "opus", "Architecture", "p", 60, "high", Marker);

        body.Should().Contain("\"effort\":\"high\"").And.Contain("\"systemPrompt\":\"MARKER-c2");
    }

    [Fact]
    public void AContractTwoServer_NamesWhatItDroppedAndLowered()
    {
        const string accepted = """
            {"id":"1","position":0,
             "dropped":[{"field":"systemPrompt","reason":"Coai:AcceptClientSystemPrompt is off"}],
             "clamped":[{"field":"effort","reason":"it runs at 'medium'"}]}
            """;

        var note = RemoteAsk.NotAppliedMessage(accepted, "2", "max", Marker);

        note.Should().Contain("did not apply systemPrompt: Coai:AcceptClientSystemPrompt is off")
            .And.Contain("lowered effort: it runs at 'medium'");
        RemoteAsk.NotAppliedMessage("""{"id":"1","position":0}""", "2", "high", Marker).Should().BeEmpty("it applied all of it");
    }

    [Theory]
    [InlineData("1")]
    [InlineData("")]
    public void AContractOneServer_AppliedNoneOfTheNewFields(string spoken)
    {
        RemoteAsk.NotAppliedMessage("""{"id":"1","position":0}""", spoken, "high", Marker)
            .Should().Contain("contract 1").And.Contain("effort").And.Contain("systemPrompt");
        RemoteAsk.NotAppliedMessage("""{"id":"1","position":0}""", spoken, string.Empty, string.Empty)
            .Should().BeEmpty("a request that sent neither lost nothing");
    }

    [Fact]
    public void TheNote_RidesTheUsageLine_IntoTheReviewersNote()
    {
        var line = RemoteAsk.UsageLine(11, 22, "the Team server did not apply effort: codex takes no effort");
        var runtime = new RemoteRuntime("srv-codex", "https://coai.example", "codex");
        var result = new Runners.Processes.ProcessResult(0, line, string.Empty, false);

        runtime.NotApplied(Built(new ReviewerSettings("srv-codex")), result).Should().Contain("codex takes no effort");
        runtime.ReadUsage(Built(new ReviewerSettings("srv-codex")), result).TokensIn.Should().Be(11, "the usage line still reads");
        RemoteAsk.UsageLine(1, 2).Should().NotContain("notApplied", "a line with nothing to say is the line it always was");

        var ok = new ReviewerOutcome.Ok(new NormalisedReview([], []), false) { NotApplied = "the Team server lowered effort" };
        TurnLoop.Note(ok).Should().Contain("the Team server lowered effort");
    }
}
