using System.Text.Json;
using CoaiMcp.Core.Feature;
using CoaiMcp.Core.Rounds;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Server;
using FluentAssertions;
using Serilog.Core;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// Every refusal <c>review_feature</c> gives before it builds anything — each its own sentence, each
/// naming what to pass instead — on a real repository, through the public service entry (plan §4.5).
/// </summary>
/// <remarks>
/// No reviewer is configured to launch: a refusal starts none, and a test that needed one to fail would
/// be testing the wrong layer. The one positive (<see cref="AGoodCall_IsNotRefused"/>) is the check that
/// the fixture passes every guard, so the refusals below are refusals of the thing each one names.
/// </remarks>
public sealed class TheFeatureStageRefusesBeforeItBuildsTests : IAsyncLifetime
{
    private const string PlanPath = "todo/PLAN_x.md";

    private const string Epics = """
        [{"title": "One", "summary": "The first epic."}, {"title": "Two", "summary": "The second epic."},
         {"title": "Three", "summary": "The third epic."}]
        """;

    private const string Lessons = """
        {"pitfalls": ["The parser read a trailing comma as an empty entry until epic 2 fixed the tokenizer."],
         "blockers": ["none — nothing blocked; the one open question was the flag name and it was settled in review."],
         "findings": ["Epic 3 relies on the tokenizer fix from epic 2; a reviewer of the whole should check the seam."]}
        """;

    private readonly ProcessLauncher _launcher = new();
    private string _repo = string.Empty;
    private string _data = string.Empty;
    private string _base = string.Empty;
    private string _head = string.Empty;
    private string _links = string.Empty;

    public async ValueTask InitializeAsync()
    {
        _repo = Directory.CreateTempSubdirectory("coai-feature-refusals-").FullName;
        _data = Directory.CreateTempSubdirectory("coai-feature-refusals-data-").FullName;
        await Git("init", "-b", "main");
        Write(PlanPath, "# PLAN — x\n\nThree epics.\n");
        Write("src/A.cs", "public sealed class A { }\n");
        await Commit("base");
        _base = await Rev("HEAD");
        Write("src/A.cs", "public sealed class A { public int B() => 1; }\n");
        await Commit("head");
        _head = await Rev("HEAD");
    }

    public ValueTask DisposeAsync()
    {
        // The link first, and never recursively, so no delete walks through it into the repository.
        if (_links.Length > 0)
        {
            DirectoryLink.Remove(Path.Combine(_links, "repo"));
        }

        foreach (var dir in ((string[])[_repo, _data, _links]).Where(d => d.Length > 0))
        {
            try
            {
                Directory.Delete(dir, recursive: true);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        return ValueTask.CompletedTask;
    }

    private void Write(string path, string text)
    {
        var full = Path.Combine(_repo, path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, text);
    }

    private async Task Commit(string message)
    {
        await Git("add", "-A");
        await Git("commit", "-m", message);
    }

    private async Task Git(params string[] args) => await Run(args);

    /// <summary>The repository, reached through a directory link made for this test.</summary>
    private async Task<string> LinkedRepoAsync()
    {
        _links = Directory.CreateTempSubdirectory("coai-feature-refusals-link-").FullName;
        var linked = Path.Combine(_links, "repo");
        await DirectoryLink.MakeAsync(_launcher, linked, _repo);

        return linked;
    }

    private async Task<string> Rev(string rev) => (await Run("rev-parse", rev)).Trim();

    private async Task<string> Run(params string[] args)
    {
        var result = await _launcher.RunAsync(new ProcessRequest(
            "git", ["-c", "user.email=t@t", "-c", "user.name=t", "-c", "commit.gpgsign=false", .. args], _repo));
        result.ExitCode.Should().Be(0, $"git {string.Join(' ', args)}: {result.StdErr}");

        return result.StdOut;
    }

    /// <summary>No vendor is ticked for features, so a call that passes every guard is a recorded skip, and nothing launches.</summary>
    private PanelService Service() =>
        new(
            new PanelSettings { Providers = [new("codex") { ExecutablePath = "no-such-cli" }], DataDir = _data },
            VaultKeys.None("no vault in tests"),
            default,
            _launcher,
            Logger.None, Noticing.None);

    private async Task<string> Refusal(
        string planPath = PlanPath, string baseRef = "", string epics = Epics, string lessons = Lessons, string repo = "")
    {
        var answer = JsonDocument.Parse(await Service().ReviewFeatureAsync(
            repo.Length > 0 ? repo : _repo, planPath, baseRef.Length > 0 ? baseRef : _base, epics, lessons)).RootElement;
        answer.TryGetProperty("error", out var error).Should().BeTrue($"a refusal was expected: {answer}");

        return error.GetString()!;
    }

    [Fact]
    public async Task AGoodCall_IsNotRefused()
    {
        var answer = JsonDocument.Parse(await Service().ReviewFeatureAsync(_repo, PlanPath, _base, Epics, Lessons)).RootElement;

        answer.TryGetProperty("error", out var error).Should().BeFalse($"the fixture passes every guard: {error}");
        answer.GetProperty("verdict").GetString().Should().Be("skipped", "nobody is ticked — the D1 skip, not a refusal");
    }

    [Theory]
    [InlineData("""{"pitfalls": [], "blockers": ["x"], "findings": ["y"]}""", "'pitfalls' is empty")]
    [InlineData("", "lessons was not given")]
    [InlineData("""{"pitfalls": ["none"], "blockers": ["x"], "findings": ["y"]}""", "without saying why")]
    public async Task ALessonsThatSaysNothing_IsRefused_WithTheFourQuestions(string lessons, string problem)
    {
        var sentence = await Refusal(lessons: lessons);

        sentence.Should().Contain(problem);
        foreach (var question in FeatureInputs.LessonQuestions)
        {
            sentence.Should().Contain(question, "every lessons refusal asks all four questions (§4.5)");
        }
    }

    [Fact]
    public async Task EpicsOutsideTheirBounds_AreRefused() =>
        (await Refusal(epics: "[]")).Should().Contain("review_feature needs `epics`").And.Contain("0 entries");

    [Fact]
    public async Task APlanOutsideTheRepository_IsRefused()
    {
        var outside = Path.Combine(Path.GetTempPath(), $"coai-not-in-repo-{Guid.NewGuid():N}.md");
        await File.WriteAllTextAsync(outside, "# PLAN — elsewhere\n");
        try
        {
            (await Refusal(planPath: outside)).Should().Contain("is not inside").And.Contain("identity");
        }
        finally
        {
            File.Delete(outside);
        }
    }

    [Fact]
    public async Task AMissingPlan_IsRefused_InTheToolsOwnWords() =>
        (await Refusal(planPath: "todo/PLAN_missing.md")).Should().Contain("there is no plan file").And.NotContain("documentPath");

    /// <summary>D15 reaches the plan too: a file named like a credential is refused by its NAME, before it is read — even when it exists and reads like a plan.</summary>
    [Fact]
    public async Task APlanNamedLikeACredentialFile_IsRefused_ByItsNameAlone()
    {
        Write(".env.production", "# PLAN — the deploy\n\nThree epics.\nDB_PASSWORD=hunter2\n");

        (await Refusal(planPath: ".env.production")).Should().Contain("looks like a credential file").And.Contain(".env*");
    }

    [Fact]
    public async Task ABaseThatIsHead_IsRefused() =>
        (await Refusal(baseRef: _head)).Should().Contain("is HEAD itself");

    [Fact]
    public async Task ABaseThatDoesNotResolve_IsRefused() =>
        (await Refusal(baseRef: "no-such-ref")).Should().Contain("does not resolve to a commit");

    [Fact]
    public async Task AnOptionLookingBase_IsRefusedBeforeGitSeesIt() =>
        (await Refusal(baseRef: "--output=x")).Should().Contain("starts with '-'");

    [Fact]
    public async Task ABaseThatIsNotAnAncestor_IsRefused()
    {
        await Git("checkout", "-q", "-b", "side", _base);
        Write("src/Side.cs", "public sealed class Side { }\n");
        await Commit("side work");
        var side = await Rev("HEAD");
        await Git("checkout", "-q", "main");

        (await Refusal(baseRef: side)).Should().Contain("is not an ancestor of HEAD");
    }

    [Fact]
    public async Task ARangeThatChangesNothingReviewable_IsRefused_NamingWhatDidChange()
    {
        Write("web/package-lock.json", "{}\n");
        await Commit("only a lock file");
        var head = await Rev("HEAD");
        Write("web/package-lock.json", "{\"lockfileVersion\": 3}\n");
        await Commit("the lock file again");

        (await Refusal(baseRef: head)).Should().Contain("nothing reviewable changed").And.Contain("web/package-lock.json");
    }

    [Fact]
    public async Task ASubdirectory_IsRefused_NamingTheTopLevel() =>
        (await Refusal(repo: Path.Combine(_repo, "src"))).Should().Contain("top level");

    /// <summary>
    /// A repository reached through a directory link IS its own top level — git answers the real path, the
    /// caller holds the linked one, and they are one directory.
    /// </summary>
    /// <remarks>
    /// Every macOS temp directory is this shape (<c>/var</c> is a link to <c>/private/var</c>), so the
    /// comparison that only normalised spelling refused every repository there: "'/var/folders/…' is
    /// inside a repository whose top level is '/private/var/folders/…'" — 23 tests on the macOS job. The
    /// link is made here, so it fails on every platform, not only on the one runner that showed it.
    /// </remarks>
    [Fact]
    public async Task ARepositoryReachedThroughALink_IsItsOwnTopLevel()
    {
        var linked = await LinkedRepoAsync();

        var answer = JsonDocument.Parse(await Service().ReviewFeatureAsync(linked, PlanPath, _base, Epics, Lessons)).RootElement;

        answer.TryGetProperty("error", out var error).Should().BeFalse($"a link to the top level is the top level: {error}");
        answer.GetProperty("verdict").GetString().Should().Be("skipped", "nobody is ticked — the D1 skip, not a refusal");
    }

    /// <summary>And through the link, the refusal that follows the top-level check is the one about the plan.</summary>
    [Fact]
    public async Task AMissingPlan_ThroughALink_IsRefusedAsMissing() =>
        (await Refusal(planPath: "todo/PLAN_missing.md", repo: await LinkedRepoAsync()))
            .Should().Contain("there is no plan file").And.NotContain("top level");

    [Fact]
    public async Task NamingADocumentAndAFeature_IsRefused()
    {
        var answer = JsonDocument.Parse(await Service().StatusAsync(_repo, "main", "docs/spec.md", string.Empty, feature: PlanPath)).RootElement;

        answer.GetProperty("error").GetString().Should().Contain("not both");
    }

    /// <summary>A different base on a plan's review is a different review — refused without again, the door named (§4.3).</summary>
    [Fact]
    public void ADifferentBase_WithoutAgain_IsRefusedNamingBoth()
    {
        var loaded = new PersistedSession(new SessionState("s1", _repo, SessionKey.FeatureBranch, new PanelConfig()) { Feature = PlanPath, Stage = Stage.FeatureReview }, [])
        {
            FeatureBase = new string('a', 40),
        };

        FeatureStage.WhyNotThisRound(loaded, _head, _base, again: false).Should().Contain(new string('a', 40)).And.Contain(_base);
        FeatureStage.WhyNotThisRound(loaded, _head, _base, again: true).Should().BeEmpty("again is the door to a fresh review against the new base");
    }

    /// <summary>D14 holds in every state: an open review whose last round read this head refuses <c>again</c> exactly as a finished one does.</summary>
    [Fact]
    public void AgainOverTheHeadTheLastRoundRead_IsRefused_WhetherOrNotTheReviewIsFinished()
    {
        var read = new RoundRecord(nameof(Stage.FeatureReview), 1, "revise", 1, "1 reviewer", DateTime.UtcNow) { Sha = _head };
        foreach (var stage in (Stage[])[Stage.Done, Stage.FeatureReview])
        {
            var loaded = new PersistedSession(new SessionState("s1", _repo, SessionKey.FeatureBranch, new PanelConfig()) { Feature = PlanPath, Stage = stage }, [read])
            {
                FeatureBase = _base,
            };

            FeatureStage.WhyNotThisRound(loaded, _head, _base, again: true).Should().Contain("the head has not moved", $"in stage {stage}, the same base and the same head review nothing new");
        }
    }
}
