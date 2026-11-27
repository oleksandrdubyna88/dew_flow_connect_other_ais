using System.Text.Json;
using CoaiMcp.Core.QuestionConsult;
using CoaiMcp.Core.Rounds;
using CoaiMcp.Runners.Consultation;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Reviewers;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// A claude launch in plan mode never carries the bare alias <c>haiku</c>: the CLI runs Sonnet for it, and the row's owner pays for Sonnet.
/// </summary>
/// <remarks>
/// <para><b>Measured 2026-11-27</b> (v2 paid proof): claude CLI 2.1.332 was started with <c>--permission-mode plan --model haiku</c>, and the
/// session's api-request <c>params.model</c> was <c>claude-sonnet-5-5</c>. In the CLI binary the function <c>npe</c> maps <c>haiku</c> to
/// <c>sonnet</c>, and <c>XMt</c> applies it when the permission mode is <c>plan</c>; its own help says so: "A session whose model setting is
/// 'opusplan', 'opusplan[1m]' or 'haiku' runs a different model in plan mode". A full id is not mapped. Every claude launch here is in plan mode,
/// so the alias is sent as the full id the repository lists for Haiku (<c>shared/model-display-vectors.json</c>).</para>
/// <para>These assert what is SENT, and what the CLI's own JSON result says it ANSWERED with — the second half is the check for the next alias
/// the CLI maps behind our back, which no list written today can name.</para>
/// </remarks>
public sealed class AClaudePlanLaunchNeverSendsTheHaikuAliasTests
{
    private const string Worktree = "D:/storage/coai-wt-s1-r1";
    private const string Repo = "D:/rsd/some-checkout";
    private const string FullHaiku = "claude-haiku-5-5";

    private static readonly string Scratch = Path.Combine(TempDir.Root, "coai-scratch-" + Guid.NewGuid().ToString("N"));
    private static readonly string AnswerFolder = Path.Combine(TempDir.Root, "coai-answers-" + Guid.NewGuid().ToString("N"));
    private static readonly string RowFolder = Path.Combine(TempDir.Root, "coai-question-" + Guid.NewGuid().ToString("N"));
    private static readonly string RowTree = Path.Combine(RowFolder, "tree");

    private static IReadOnlyList<string> Reviewer(string model) =>
        new ClaudeRuntime()
            .Build(RoleCatalog.ArchitectureRole, "review this", Worktree, "D:/storage/schema.json", "D:/storage/out", new ReviewerSettings("claude") { Model = model })
            .Request.Arguments;

    private static IReadOnlyList<string> Consultant(string model, ClaudeCapability cli) =>
        new ClaudeConsultant(new ClaudeRuntime())
            .Build(new ConsultantLaunch(Repo, "help me", string.Empty, AnswerFolder, new ReviewerSettings("claude") { Model = model, ClaudeCli = cli }))
            .Request.Arguments;

    private static IReadOnlyList<string> Question(string model) =>
        new ClaudeConsultant(new ClaudeRuntime())
            .Build(new ConsultantLaunch(Repo, "the question", string.Empty, AnswerFolder, new ReviewerSettings("claude") { Model = model, OwnedScratch = RowFolder })
            {
                Confinement = new LaunchConfinement.Planned((Confinement.Planned)ConfinementPlanner.Plan("claude", CapabilityGrant.None)),
                ScratchDir = Scratch,
                Workdir = RowTree,
            })
            .Request.Arguments;

    private static string ModelOf(IReadOnlyList<string> argv)
    {
        var at = argv.ToList().IndexOf("--model");

        return at >= 0 ? argv[at + 1] : string.Empty;
    }

    private static bool InPlanMode(IReadOnlyList<string> argv)
    {
        var at = argv.ToList().IndexOf("--permission-mode");

        return at >= 0 && argv[at + 1] == "plan";
    }

    /// <summary>Every launch road that is in plan mode, with the model the row asked for.</summary>
    public static TheoryData<string, string> Roads()
    {
        var roads = new TheoryData<string, string>();
        foreach (var model in (string[])["haiku", "Haiku", " haiku "])
        {
            roads.Add("reviewer", model);
            roads.Add("consultant, restricted", model);
            roads.Add("consultant, no restricted", model);
            roads.Add("question consultant", model);
        }

        return roads;
    }

    private static IReadOnlyList<string> Argv(string road, string model) => road switch
    {
        "reviewer" => Reviewer(model),
        "consultant, restricted" => Consultant(model, ClaudeCapability.WithRestricted),
        "consultant, no restricted" => Consultant(model, ClaudeCapability.NoRestricted),
        "question consultant" => Question(model),
        _ => throw new ArgumentOutOfRangeException(nameof(road), road, "an unknown launch road"),
    };

    [Theory]
    [MemberData(nameof(Roads))]
    public void ArgvNeverHoldsPlanModeAndTheBareHaikuAlias(string road, string model)
    {
        var argv = Argv(road, model);

        InPlanMode(argv).Should().BeTrue($"{road} is a plan-mode launch, which is why the alias matters");
        ModelOf(argv).Should().NotBe("haiku", $"{road}: the CLI maps the bare alias to Sonnet in plan mode");
        ModelOf(argv).Trim().ToLowerInvariant().Should().NotBe("haiku");
    }

    [Theory]
    [MemberData(nameof(Roads))]
    public void TheAliasIsSentAsTheFullIdTheRepositoryLists(string road, string model) =>
        ModelOf(Argv(road, model)).Should().Be(FullHaiku, road);

    /// <summary>
    /// The id is the NEWEST one the display vectors list for the haiku family — the repository's own list, not a memory of it. The bare alias
    /// runs claude-haiku-5-5 outside plan mode (measured 2026-11-27, CLI 2.1.332, a v2 coder job in acceptEdits mode), so the id sent in plan
    /// mode must be the same model. If a newer Haiku is ever listed there, this fails until the alias is pointed at it.
    /// </summary>
    [Fact]
    public void TheFullIdIsTheNewestHaikuTheRepositorysOwnModelListNames()
    {
        using var file = File.OpenRead(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "shared", "model-display-vectors.json")));
        using var parsed = JsonDocument.Parse(file);
        var haikus = parsed.RootElement.GetProperty("models").EnumerateArray()
            .Where(row => row.GetProperty("family").GetString() == "haiku")
            .Select(row => row.GetProperty("model").GetString()!)
            .ToList();

        haikus.Should().Contain(FullHaiku, "the id sent must be one the repository lists");
        haikus.OrderByDescending(Release).First().Should().Be(FullHaiku, "an older Haiku is a dearer model than the one the alias runs");
    }

    /// <summary>The (major, minor) of <c>claude-haiku-M-N[-date]</c>, so the newest sorts first.</summary>
    private static (int Major, int Minor) Release(string id)
    {
        var parts = id.Split('-');

        return (int.Parse(parts[2], System.Globalization.CultureInfo.InvariantCulture), int.Parse(parts[3], System.Globalization.CultureInfo.InvariantCulture));
    }

    /// <summary>Only the one alias is rewritten: another alias, a full id and no model at all go out as the row wrote them.</summary>
    [Theory]
    [InlineData("sonnet")]
    [InlineData("opus")]
    [InlineData("fable")]
    [InlineData("claude-haiku-4-5-20251001")]
    [InlineData("claude-sonnet-5-5")]
    [InlineData("opusplan")]
    public void AnythingElseIsSentAsWritten(string model)
    {
        ModelOf(Reviewer(model)).Should().Be(model);
        ModelOf(Consultant(model, ClaudeCapability.WithRestricted)).Should().Be(model);
        ModelOf(Question(model)).Should().Be(model);
    }

    [Fact]
    public void NoModelIsStillNoModelFlag()
    {
        Reviewer(string.Empty).Should().NotContain("--model");
        Consultant(string.Empty, ClaudeCapability.WithRestricted).Should().NotContain("--model");
        Question(string.Empty).Should().NotContain("--model");
    }

    // ---------- what the CLI answered with ----------

    private static string Envelope(string models) =>
        "{\"type\":\"result\",\"subtype\":\"success\",\"is_error\":false,\"result\":\"{}\",\"total_cost_usd\":0.05,"
        + "\"usage\":{\"input_tokens\":10,\"output_tokens\":44},\"modelUsage\":" + models + "}";

    private static string Flagged(string asked, string stdOut)
    {
        var runtime = new ClaudeRuntime();
        var invocation = runtime.Build(RoleCatalog.ArchitectureRole, "review this", Worktree, "D:/storage/schema.json", "D:/storage/out", new ReviewerSettings("claude") { Model = asked });

        return ((IReviewerRuntime)runtime).NotApplied(invocation, new ProcessResult(0, stdOut, string.Empty, TimedOut: false));
    }

    private static string Used(string id) => "{\"" + id + "\":{\"inputTokens\":532,\"outputTokens\":57,\"costUSD\":0.04}}";

    /// <summary>The 2026-11-27 proof: asked for haiku, the CLI's own result says it ran claude-sonnet-5-5.</summary>
    [Fact]
    public void AnAnswerOfAnotherFamily_IsFlaggedWithBothNames()
    {
        var note = Flagged("haiku", Envelope(Used("claude-sonnet-5-5")));

        note.Should().Contain("haiku").And.Contain("claude-sonnet-5-5").And.Contain("sonnet");
    }

    [Fact]
    public void AFullIdAskedAndAnotherFamilyAnswered_IsFlagged() =>
        Flagged(FullHaiku, Envelope(Used("claude-sonnet-5-5"))).Should().Contain("claude-sonnet-5-5");

    [Theory]
    [InlineData("haiku", "claude-haiku-4-5-20251001")]
    [InlineData("sonnet", "claude-sonnet-5-5")]
    [InlineData("opus", "claude-opus-5-5")]
    [InlineData("claude-opus-5-5", "claude-opus-5-5-20261001")]
    public void AnAnswerOfTheAskedFamily_IsNotFlagged(string asked, string answered) =>
        Flagged(asked, Envelope(Used(answered))).Should().BeEmpty();

    /// <summary>A run that used two models (a helper beside the main one) is not a mismatch while the asked family is among them.</summary>
    [Fact]
    public void TheAskedFamilyAmongSeveralAnswers_IsNotFlagged() =>
        Flagged("sonnet", Envelope("{\"claude-haiku-4-5-20251001\":{\"outputTokens\":3},\"claude-sonnet-5-5\":{\"outputTokens\":57}}")).Should().BeEmpty();

    /// <summary>Not a family this code can name — never a guess: an alias like opusplan, an empty ask, a non-claude id.</summary>
    [Theory]
    [InlineData("opusplan")]
    [InlineData("")]
    [InlineData("gpt-6-luna")]
    public void AnAskThatIsNoKnownFamily_IsNeverFlagged(string asked) =>
        Flagged(asked, Envelope(Used("claude-sonnet-5-5"))).Should().BeEmpty();

    /// <summary>An id that parses to no family (a gateway's own name) says nothing about the family: never a guess.</summary>
    [Theory]
    [InlineData("my-gateway-model")]
    [InlineData("anthropic.claude-sonnet-5-5-v1:0")]
    public void AnswersThatNameNoFamily_AreNeverFlagged(string answered) =>
        Flagged("haiku", Envelope(Used(answered))).Should().BeEmpty();

    /// <summary>The check reads what is there and says nothing of what is not — and never throws on the path of an answered run.</summary>
    [Theory]
    [InlineData("""{"type":"result","result":"no modelUsage at all"}""")]
    [InlineData("""{"type":"result","modelUsage":{}}""")]
    [InlineData("""{"type":"result","modelUsage":[1,2]}""")]
    [InlineData("""{"type":"result","modelUsage":"claude-sonnet-5-5"}""")]
    [InlineData("""["modelUsage"]""")]
    [InlineData("""{"type":"result","modelUsage":{"claude-sonn""")]
    [InlineData("not json at all")]
    [InlineData("")]
    public void AResultWithNoReadableModelUsage_IsNeverFlagged(string stdOut)
    {
        var reading = () => Flagged("haiku", stdOut);

        reading.Should().NotThrow();
        reading().Should().BeEmpty();
    }

    /// <summary>Through the executor, as a round meets it: the sentence reaches the launch the round reads.</summary>
    [Fact]
    public async Task TheExecutorCarriesTheFlagOnAnAnsweredLaunch()
    {
        var runtime = new ClaudeRuntime();
        var invocation = runtime.Build(RoleCatalog.ArchitectureRole, "review this", Worktree, "D:/storage/schema.json", "D:/storage/out", new ReviewerSettings("claude") { Model = "haiku" });
        var executor = new ReviewerExecutor(new Answers(new ProcessResult(0, Envelope(Used("claude-sonnet-5-5")), string.Empty, TimedOut: false)));

        var launch = await executor.LaunchAsync(invocation, TestContext.Current.CancellationToken);

        launch.Terminal.Should().BeNull("the run exited zero and answered");
        launch.NotApplied.Should().Contain("claude-sonnet-5-5");
    }

    private sealed class Answers(ProcessResult result) : IProcessLauncher
    {
        public Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken ct = default) => Task.FromResult(result);
    }
}
