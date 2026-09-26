using System.Text.Json;
using CoaiMcp.Core.Rounds;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Server;
using FluentAssertions;
using Serilog.Core;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// A document is reviewed, end to end, through the public tool — and the summary comes back.
/// </summary>
/// <remarks>
/// <para><b>Asked for by the plan round, in these words: every other test can be green while
/// <c>review_document</c> returns an empty round.</b> The catalog tests prove the selection, the
/// parser tests prove the wire, the reader tests prove the refusals — and none of them touches the
/// wiring between them, which is where a stage that dispatches to nothing would live.</para>
/// <para>Scripted through the fake CLI's environment; nothing here touches a real model or the
/// network. The shape is <c>ACustomRoleReviewsAChangeTests</c>' — the test that exists for the same
/// reason one plan earlier.</para>
/// </remarks>
[Collection("fakecli-env")]
public sealed class ADocumentIsReviewedEndToEndTests : IAsyncLifetime
{
    private const string Purpose = """
        # What this document is for

        It is the requirements for a payments integration, written for the two engineers who will
        build it and the manager who signs it off. After reading it they must be able to start
        without asking anybody a question. It deliberately does not cover the contract with the
        provider or the pricing, both of which are settled elsewhere.
        """;

    private const string Document = """
        # Payments integration

        Payments must be fast. The team will migrate the existing records before launch, and the
        system will be reviewed afterwards.
        """;

    private const string WithNotes = """
        {"findings": [
          {"severity": "major", "category": "completeness", "file": null, "line": null,
           "title": "'fast' is not a number", "why": "nobody can tell whether the result meets it",
           "fix": "state a latency and a percentile"}
        ],
         "notes": "The document asks for a payments integration and settles almost nothing: one performance word with no number, a migration with no owner, and a review with no date."}
        """;

    private const string TheText = "You read one thing: whether a reader could act on this document.";

    private readonly ProcessLauncher _launcher = new();
    private string _repo = string.Empty;
    private string _data = string.Empty;

    private static string FakeCliExe => Path.Combine(
        AppContext.BaseDirectory, OperatingSystem.IsWindows() ? "FakeCli.exe" : "FakeCli");

    public async ValueTask InitializeAsync()
    {
        _repo = Directory.CreateTempSubdirectory("coai-doc-e2e-repo-").FullName;
        _data = Directory.CreateTempSubdirectory("coai-doc-e2e-data-").FullName;

        Directory.CreateDirectory(Path.Combine(_data, "prompts"));
        await File.WriteAllTextAsync(Path.Combine(_data, "prompts", "spec-general.md"), TheText);

        await Git("init", "-b", "main");
        await File.WriteAllTextAsync(Path.Combine(_repo, "app.cs"), "v1\n");
        await Git("add", ".");
        await Git("commit", "-m", "base");

        Environment.SetEnvironmentVariable("FAKECLI_MODE", "vendor");
    }

    public ValueTask DisposeAsync()
    {
        foreach (var name in (string[])["FAKECLI_MODE", "FAKECLI_STDOUT", "FAKECLI_OUTFILE_TEXT", "FAKECLI_EXIT", "FAKECLI_STDERR"])
        {
            Environment.SetEnvironmentVariable(name, null);
        }

        foreach (var dir in (string[])[_repo, _data])
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

    private static void Script(string answer)
    {
        Environment.SetEnvironmentVariable("FAKECLI_STDOUT", answer);
        Environment.SetEnvironmentVariable("FAKECLI_OUTFILE_TEXT", answer);
        Environment.SetEnvironmentVariable("FAKECLI_EXIT", "0");
        Environment.SetEnvironmentVariable("FAKECLI_STDERR", "");
    }

    private async Task Git(params string[] args)
    {
        var result = await _launcher.RunAsync(new ProcessRequest(
            "git", ["-c", "user.email=t@t", "-c", "user.name=t", "-c", "commit.gpgsign=false", .. args], _repo));
        result.ExitCode.Should().Be(0, $"git {string.Join(' ', args)}: {result.StdErr}");
    }

    /// <summary>One custom DOCUMENT role, with every shipped role switched off.</summary>
    /// <param name="escalationBudget">
    /// How long <c>ask_human</c> waits for a person. The shipped thirty minutes for every test that
    /// never asks one; a test that DOES ask sets a budget nobody is expected to meet.
    /// </param>
    private PanelService Service(TimeSpan? escalationBudget = null)
    {
        var catalog = RoleComposition.Compose([
            new RoleEntry(RoleCatalog.PlanRole, Active: false),
            new RoleEntry(RoleCatalog.ConventionsRole, Active: false),
            new RoleEntry(RoleCatalog.ArchitectureRole, Active: false),
            new RoleEntry(RoleCatalog.SecurityRole, Active: false),
            new RoleEntry(RoleCatalog.UxDxRole, Active: false),
            new RoleEntry(RoleCatalog.DocumentRole, Active: false),
            new RoleEntry(RoleCatalog.DocumentSummaryRole, Active: false),
            new RoleEntry("Spec", Name: "The specification", Stage: RoleStages.Result,
                ProgrammingTask: false,
                Prompts: [new PromptEntry("spec-general", "General", "Whether a reader could act on it.")]),
        ]);

        return new PanelService(
            new PanelSettings
            {
                Providers = [new("codex") { ExecutablePath = FakeCliExe }],
                Rounds = new PanelConfig(
                    catalog.Roles.ToDictionary(r => r.Id, _ => new RoleGate(1, 5)), StagePolicy.Human)
                {
                    Catalog = catalog,
                },
                DataDir = _data,
                ReviewerTimeout = TimeSpan.FromSeconds(30),
                EscalationBudget = escalationBudget ?? TimeSpan.FromMinutes(30),
            },
            VaultKeys.None("no vault in tests"),
            default,
            _launcher,
            Logger.None, Noticing.None);
    }

    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement;

    private string WriteDocument(string relative = "docs/spec.md", string text = Document)
    {
        var path = Path.Combine(_repo, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);

        return path;
    }

    /// <summary>
    /// The load-bearing test of this plan: a role nobody compiled in, marked NOT a programming task,
    /// reviews a document and its findings and its prose come back to the caller.
    /// </summary>
    [Fact]
    public async Task ADocumentRole_ReviewsADocument_AndItsSummaryComesBack()
    {
        var service = Service();
        await service.OpenAsync(_repo, "main");

        Script(WithNotes);
        var answer = Parse(await service.ReviewDocumentAsync(
            _repo, "main", Purpose, documentPath: WriteDocument()));

        answer.TryGetProperty("error", out var error).Should().BeFalse($"unexpected refusal: {error}");
        answer.GetProperty("reviewers").GetString().Should().Contain("1 reviewer",
            "every shipped role is off, so the round is the document role alone");
        answer.GetProperty("findings").EnumerateArray().Should().NotBeEmpty()
            .And.OnlyContain(f => f.GetProperty("role").GetString() == "Spec");
        answer.GetProperty("findings")[0].GetProperty("category").GetString().Should().Be("Completeness",
            "a document category survives the wire — it used to be dropped as unknown");

        // The summary. `notes: null` is a valid review, so nothing else on this page would notice a
        // round that answered findings and no prose. (codex, the plan round.)
        var notes = answer.GetProperty("notes").EnumerateArray().ToList();
        notes.Should().ContainSingle();
        notes[0].GetProperty("provider").GetString().Should().Be("codex");
        notes[0].GetProperty("role").GetString().Should().Be("Spec");
        notes[0].GetProperty("notes").GetString().Should().Contain("settles almost nothing");
    }

    /// <summary>
    /// A family mount carrying one rule of the document tier and one that is not.
    /// </summary>
    private void WriteFamilyRules()
    {
        File.WriteAllText(Path.Combine(_repo, ".gitmodules"),
            "[submodule \"conventions\"]\n path = .agents/conventions\n url = https://example.invalid/rules\n");
        var common = Directory.CreateDirectory(Path.Combine(_repo, ".agents", "conventions", "common")).FullName;
        File.WriteAllText(Path.Combine(common, "knowledge-base.md"), "research tracks the system as it is.");
        File.WriteAllText(Path.Combine(common, "dotnet-build.md"), "Build with the response file.");
    }

    /// <summary>
    /// A document round is judged against the rules that govern DOCUMENTS.
    /// </summary>
    /// <remarks>
    /// It said so itself until this shipped — "no diff and no rules at this stage" — so a reviewer
    /// asked whether a document belongs in <c>research/</c> had nothing to answer from but its own
    /// taste. Asserted on the RECORDED launch, not on a context handed to <c>BuildWork</c>: the
    /// question is whether the STAGE builds the rules in.
    /// </remarks>
    [Fact]
    public async Task ADocumentRound_IsGivenTheRulesItIsJudgedAgainst()
    {
        WriteFamilyRules();
        var record = Directory.CreateTempSubdirectory("coai-doc-rules-").FullName;
        Environment.SetEnvironmentVariable("FAKECLI_RECORD_DIR", record);
        try
        {
            var service = Service();
            await service.OpenAsync(_repo, "main");

            await service.ReviewDocumentAsync(_repo, "main", Purpose, documentPath: WriteDocument());

            var prompt = Directory.GetFiles(record, "*.argv")
                .Select(f => File.ReadAllText(f).Split('\0')[^1]).First();
            prompt.Should().Contain("The rules this project has written down");
            prompt.Should().Contain("research tracks the system as it is", "a rule of the document tier");
            prompt.Should().NotContain("Build with the response file",
                "a build recipe is not what a DOCUMENT is judged against");
        }
        finally
        {
            Environment.SetEnvironmentVariable("FAKECLI_RECORD_DIR", null);
            Directory.Delete(record, recursive: true);
        }
    }

    /// <summary>The reviewer is handed the document and the purpose — not one of the two.</summary>
    [Fact]
    public async Task TheReviewer_IsHandedThePurposeAndTheDocument()
    {
        var service = Service();
        await service.OpenAsync(_repo, "main");

        var work = service.Roster.BuildWork(
            ["Spec"], _repo,
            $"## What this document is for\n\n{Purpose}\n\n## The document under review — spec.md\n\n{Document}",
            // The stage this test is NAMED for. It rode `Stage.PlanReview` because that is what the
            // flag it replaced meant, so a vendor ticked for plans and not documents would have
            // produced reviewer work here while the round it stands for produced none.
            // (CodeRabbit, plan 5's pull request.)
            round: 1, stage: Stage.DocumentReview, readsCheckout: false);

        work.Reviewers.Should().ContainSingle();
        var prompt = work.Reviewers[0].Invocation.Request.StdIn;
        prompt.Should().Contain("payments integration", "the document itself");
        prompt.Should().Contain("without asking anybody a question", "the purpose it is judged against");
        prompt.Should().Contain(TheText, "the role's own prompt text");
    }

    /// <summary>A document review is its own session, and the branch's own is untouched by it.</summary>
    [Fact]
    public async Task ADocumentRound_LeavesTheBranchSessionAlone()
    {
        var service = Service();
        await service.OpenAsync(_repo, "main");

        Script(WithNotes);
        await service.ReviewDocumentAsync(_repo, "main", Purpose, documentPath: WriteDocument());

        var branch = Parse(await service.StatusAsync(_repo, "main"));
        branch.GetProperty("stage").GetString().Should().Be("PlanReview");
        branch.GetProperty("rounds").GetArrayLength().Should().Be(0,
            "the document's round belongs to the document's session");

        var document = Parse(await service.StatusAsync(_repo, "main", "docs/spec.md"));
        document.GetProperty("stage").GetString().Should().Be("DocumentReview");
        document.GetProperty("rounds").GetArrayLength().Should().Be(1);
    }

    /// <summary>
    /// The round is resolved on the document's own session, and the next round finds it — which is
    /// the loop the plan's first draft made impossible by keying on content.
    /// </summary>
    [Fact]
    public async Task AnEditedDocument_ContinuesTheSameReview()
    {
        var service = Service();
        await service.OpenAsync(_repo, "main");

        Script(WithNotes);
        var first = Parse(await service.ReviewDocumentAsync(
            _repo, "main", Purpose, documentPath: WriteDocument()));

        var decisions = JsonSerializer.Serialize(
            Enumerable.Range(0, first.GetProperty("findings").GetArrayLength())
                .Select(i => new { finding = i, action = "accept" }));
        var resolved = Parse(await service.ResolveAsync(_repo, "main", decisions, document: "docs/spec.md"));
        resolved.TryGetProperty("error", out var error).Should().BeFalse($"unexpected refusal: {error}");
        resolved.GetProperty("recordedDecisions").GetInt32().Should().Be(1);

        // The document is EDITED, which is what a person does between rounds.
        WriteDocument(text: $"{Document}\n\nLatency must be under 200 ms at the 95th percentile.\n");

        var status = Parse(await service.StatusAsync(_repo, "main", "docs/spec.md"));
        status.GetProperty("rounds").GetArrayLength().Should().Be(1,
            "round 2 is about to run in the session round 1 belongs to");
        status.GetProperty("awaitingResolve").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task ADocumentRound_IsRefusedWithNoSession()
    {
        var answer = Parse(await Service().ReviewDocumentAsync(
            _repo, "main", Purpose, documentPath: WriteDocument()));

        answer.GetProperty("error").GetString().Should().Contain("open first");
    }

    /// <summary>
    /// Every document role off refuses BEFORE anything is read or written — the guard both other
    /// stages have, because a round nobody is in is an unresolved round that sits open for ever.
    /// </summary>
    [Fact]
    public async Task EveryDocumentRoleOff_IsRefusedBeforeAnythingIsRead()
    {
        var catalog = RoleComposition.Compose([
            new RoleEntry(RoleCatalog.DocumentRole, Active: false),
            new RoleEntry(RoleCatalog.DocumentSummaryRole, Active: false),
        ]);
        var service = new PanelService(
            new PanelSettings
            {
                Providers = [new("codex") { ExecutablePath = FakeCliExe }],
                Rounds = new PanelConfig(
                    catalog.Roles.ToDictionary(r => r.Id, _ => new RoleGate(1, 5)), StagePolicy.Human)
                {
                    Catalog = catalog,
                },
                DataDir = _data,
            },
            VaultKeys.None("no vault in tests"),
            default,
            _launcher,
            Logger.None, Noticing.None);

        var answer = Parse(await service.ReviewDocumentAsync(
            _repo, "main", Purpose, documentPath: Path.Combine(_repo, "does-not-exist.md")));

        answer.GetProperty("error").GetString().Should().Contain("document-review role is switched off",
            "the roles are checked before the document is, so an unreadable path is not what is reported");
    }

    /// <summary>A purpose short enough to be a title is not a purpose.</summary>
    [Fact]
    public async Task ABarePurpose_IsRefused()
    {
        var service = Service();
        await service.OpenAsync(_repo, "main");

        var answer = Parse(await service.ReviewDocumentAsync(
            _repo, "main", "check the spec", documentPath: WriteDocument()));

        answer.GetProperty("error").GetString().Should().Contain("purposeText");
    }

    /// <summary>
    /// Resolving a document round without naming the document lands on the BRANCH's session — which
    /// is idle, correct, and has nothing to decide. The refusal has to say so, because the one
    /// sentence it used to give sent the caller to run another review: the single thing that would
    /// make it worse.
    /// </summary>
    [Fact]
    public async Task ResolvingWithoutTheDocument_SaysWhereTheRoundIs()
    {
        var service = Service();
        await service.OpenAsync(_repo, "main");
        Script(WithNotes);
        await service.ReviewDocumentAsync(_repo, "main", Purpose, documentPath: WriteDocument());

        var answer = Parse(await service.ResolveAsync(
            _repo, "main", """[{"finding": 0, "action": "accept"}]"""));

        answer.GetProperty("error").GetString().Should().Contain("document");
    }

    /// <summary>The snapshot is on disk, named by the content it holds.</summary>
    [Fact]
    public async Task TheDocumentSnapshot_IsKept()
    {
        var service = Service();
        await service.OpenAsync(_repo, "main");

        Script(WithNotes);
        await service.ReviewDocumentAsync(_repo, "main", Purpose, documentPath: WriteDocument());

        var kept = Path.Combine(_data, "documents", $"{DocumentRules.ArtifactIdOf(Document)}.txt");
        File.Exists(kept).Should().BeTrue("a person opens this to see what the reviewers were given");
        (await File.ReadAllTextAsync(kept)).Should().Be(Document);
    }

    /// <summary>
    /// Snapshots do not grow for ever — three reviewers said the same thing on the code round.
    /// </summary>
    [Fact]
    public void OldSnapshots_AreSweptOnTheNextWrite()
    {
        var store = new ArtifactStore(_data);
        store.Keep("aaaaaaaaaaaaaaaa", "old").Should().BeNull();

        var old = Path.Combine(_data, "documents", "aaaaaaaaaaaaaaaa.txt");
        File.SetLastWriteTimeUtc(old, DateTime.UtcNow - ArtifactStore.Keeps - TimeSpan.FromDays(1));
        // A scratch file a killed process left behind, which nothing else would ever remove.
        var abandoned = Path.Combine(_data, "documents", "bbbb.deadbeef.writing");
        File.WriteAllText(abandoned, "half a document");
        File.SetLastWriteTimeUtc(abandoned, DateTime.UtcNow - ArtifactStore.Keeps - TimeSpan.FromDays(1));

        store.Keep("cccccccccccccccc", "new").Should().BeNull();

        File.Exists(old).Should().BeFalse("it is past the retention window");
        File.Exists(abandoned).Should().BeFalse("and so is the scratch file");
        store.Has("cccccccccccccccc").Should().BeTrue("the one just written is kept");
    }

    /// <summary>A snapshot inside the window is kept, which is the half that matters.</summary>
    [Fact]
    public void ARecentSnapshot_Survives()
    {
        var store = new ArtifactStore(_data);
        store.Keep("dddddddddddddddd", "recent").Should().BeNull();

        store.Keep("eeeeeeeeeeeeeeee", "another").Should().BeNull();

        store.Has("dddddddddddddddd").Should().BeTrue();
    }

    // ---------- what the feature-review plan found (§9, 2026-09-25) ----------

    /// <summary>
    /// A document round nobody answered calls the person about the DOCUMENT gate — not about a gate
    /// called "done".
    /// </summary>
    /// <remarks>
    /// <c>RoundSubject.StageName</c> knew two stages and answered <c>done</c> for everything else, so
    /// the notice a document round left in the panel read "The done gate needs your decision". A
    /// person reading that has no idea which of their reviews stopped. (§9.1)
    /// </remarks>
    [Fact]
    public async Task ADocumentRoundNobodyAnswered_AsksThePersonAboutTheDocumentGate_NotAboutDone()
    {
        var service = Service();
        await service.OpenAsync(_repo, "main");

        Environment.SetEnvironmentVariable("FAKECLI_STDOUT", "");
        Environment.SetEnvironmentVariable("FAKECLI_OUTFILE_TEXT", "");
        Environment.SetEnvironmentVariable("FAKECLI_EXIT", "1");
        Environment.SetEnvironmentVariable("FAKECLI_STDERR", "429 Too Many Requests");
        var answer = Parse(await service.ReviewDocumentAsync(_repo, "main", Purpose, documentPath: WriteDocument()));
        answer.GetProperty("verdict").GetString().Should().Be("call_human", $"nothing was reviewed: {answer}");

        var notice = Directory.GetFiles(Path.Combine(_data, "escalations"), "*.json")
            .Single(f => !f.EndsWith(".answer.json", StringComparison.Ordinal));
        var question = JsonDocument.Parse(await File.ReadAllTextAsync(notice)).RootElement.GetProperty("question").GetString();

        question.Should().StartWith("The document review gate needs your decision",
            "the notice names the gate a person can find in their panel");
        question.Should().NotContain("done gate");
    }

    /// <summary>
    /// Resolving a document review that passed says the DOCUMENT stage is complete — never that the
    /// code stage is.
    /// </summary>
    /// <remarks>
    /// <c>Finish</c> answered "The code stage is complete. This session is done." for ANY session
    /// that reached <c>Done</c>, a document review included: a caller that had never run a code
    /// round was told one had finished. (§9.2)
    /// </remarks>
    [Fact]
    public async Task ResolvingADocumentReviewThatPassed_SaysTheDocumentStageIsComplete_NotTheCodeStage()
    {
        var service = Service();
        await service.OpenAsync(_repo, "main");
        Script("""{"findings": []}""");
        Parse(await service.ReviewDocumentAsync(_repo, "main", Purpose, documentPath: WriteDocument()))
            .GetProperty("verdict").GetString().Should().Be("proceed");

        var resolved = Parse(await service.ResolveAsync(_repo, "main", "[]", document: "docs/spec.md"));

        resolved.GetProperty("stage").GetString().Should().Be("Done");
        resolved.GetProperty("instruction").GetString().Should().Contain("document stage is complete")
            .And.NotContain("code stage", "no code round ran in this session, and the sentence must not say one did");
    }

    /// <summary>
    /// <c>ask_human</c> for a document review is filed under the DOCUMENT's session, with that
    /// session's open findings — so the person's answer reaches the review that asked.
    /// </summary>
    /// <remarks>
    /// It always loaded the branch session: for a document review it showed the wrong pending
    /// findings and filed the question under the branch session's id, so <c>Escalations.DecisionFor</c>
    /// — keyed by session id — never found the person's answer for the document session. (§9.3)
    /// </remarks>
    [Fact]
    public async Task AskHumanForADocumentReview_FilesTheQuestionUnderTheDocumentsSession_WithItsFindings()
    {
        var service = Service(escalationBudget: TimeSpan.FromMilliseconds(200));
        await service.OpenAsync(_repo, "main");
        Script(WithNotes);
        await service.ReviewDocumentAsync(_repo, "main", Purpose, documentPath: WriteDocument());
        var documentSession = Parse(await service.StatusAsync(_repo, "main", "docs/spec.md")).GetProperty("sessionId").GetString();
        var branchSession = Parse(await service.StatusAsync(_repo, "main")).GetProperty("sessionId").GetString();
        documentSession.Should().NotBe(branchSession, "the fixture holds two sessions, or the test proves nothing");

        var reply = Parse(await service.AskHumanAsync(_repo, "main", "Ship the spec as it stands?", document: "docs/spec.md"));

        reply.GetProperty("status").GetString().Should().Be("no_answer_yet", "nobody is at the keyboard in a test");
        var asked = Directory.GetFiles(Path.Combine(_data, "escalations"), "*.json")
            .Single(f => !f.EndsWith(".answer.json", StringComparison.Ordinal));
        var question = JsonDocument.Parse(await File.ReadAllTextAsync(asked)).RootElement;
        question.GetProperty("sessionId").GetString().Should().Be(documentSession,
            "the person's answer is looked up by session id, so a question filed under the branch's session is never answered");
        question.GetProperty("openFindings").GetArrayLength().Should().Be(1,
            "the document round's own gating finding rides with the question");
    }

    /// <summary>
    /// Asking about a document that was never reviewed — a path with no file behind it, or a name — is
    /// answered with the no-session sentence, never an exception.
    /// </summary>
    /// <remarks>
    /// Found by S2.2b (D12): <c>DocumentReader.FollowLink</c> asked the file system to resolve a link at
    /// a path that does not exist, and .NET answers that with <c>FileNotFoundException</c>, not with
    /// null — so <c>status</c>, <c>resolve</c> and <c>ask_human</c> threw for any <c>document</c> that was
    /// not a file on disk, and the caller got an SDK-level "An error occurred" instead of a sentence.
    /// </remarks>
    [Theory]
    [InlineData("docs/never-reviewed.md")]
    [InlineData("missing-dir/never-reviewed.md")]
    public async Task AskingAboutADocumentThatIsNotThere_IsASentence_NotAnException(string document)
    {
        var service = Service();
        await service.OpenAsync(_repo, "main");

        var status = Parse(await service.StatusAsync(_repo, "main", document));
        var resolve = Parse(await service.ResolveAsync(_repo, "main", "[]", document: document));

        status.GetProperty("error").GetString().Should().Contain("no review of");
        resolve.GetProperty("error").GetString().Should().Contain("no review of");
    }

    /// <summary>And with no document named, the question is the BRANCH session's, exactly as before.</summary>
    [Fact]
    public async Task AskHumanWithNoDocument_StillFilesUnderTheBranchsSession()
    {
        var service = Service(escalationBudget: TimeSpan.FromMilliseconds(200));
        await service.OpenAsync(_repo, "main");
        var branchSession = Parse(await service.StatusAsync(_repo, "main")).GetProperty("sessionId").GetString();

        var reply = Parse(await service.AskHumanAsync(_repo, "main", "Ship it?"));

        reply.GetProperty("status").GetString().Should().Be("no_answer_yet");
        var asked = Directory.GetFiles(Path.Combine(_data, "escalations"), "*.json")
            .Single(f => !f.EndsWith(".answer.json", StringComparison.Ordinal));
        JsonDocument.Parse(await File.ReadAllTextAsync(asked)).RootElement.GetProperty("sessionId").GetString()
            .Should().Be(branchSession);
    }
}
