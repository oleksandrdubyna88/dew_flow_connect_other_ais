using System.Text.Json;
using CoaiMcp.Core.Rounds;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Server;
using FluentAssertions;
using Serilog.Core;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The whole `consult` flow over the fake CLI standing in for codex: a conversation opened, resumed
/// by the vendor's own id, capped, counted and recorded — with nothing in the repository touched.
/// </summary>
/// <remarks>
/// This is the scenario <c>ScenarioCoverageTests</c> demands for the ninth tool. Nothing here
/// reaches a real model or the network.
/// </remarks>
[Collection("fakecli-env")]
public sealed class ConsultScenarioTests : IAsyncLifetime
{
    private const string Advice = "Your parser counts the separator as a field. Check it: run the suite with a two-field input and print the count.";

    private readonly ProcessLauncher _launcher = new();
    private string _repo = string.Empty;
    private string _data = string.Empty;

    private static string FakeCliExe => Path.Combine(
        AppContext.BaseDirectory, OperatingSystem.IsWindows() ? "FakeCli.exe" : "FakeCli");

    public async ValueTask InitializeAsync()
    {
        _repo = Directory.CreateTempSubdirectory("coai-consult-repo-").FullName;
        _data = Directory.CreateTempSubdirectory("coai-consult-data-").FullName;
        await Git("init", "-b", "main");
        await Git("config", "user.email", "t@example.com");
        await Git("config", "user.name", "t");
        await File.WriteAllTextAsync(Path.Combine(_repo, "Parser.cs"), "int Count() => 3;\n");
        await Git("add", ".");
        await Git("commit", "-m", "base");
        // The uncommitted work the consultant is asked about — collected by the SERVER, never handed in.
        await File.WriteAllTextAsync(Path.Combine(_repo, "Parser.cs"), "int Count() => 3; // was 4\n");
        await File.WriteAllTextAsync(Path.Combine(_repo, "Scratch.cs"), "// a file nobody committed\n");

        Environment.SetEnvironmentVariable("FAKECLI_MODE", "vendor");
        Answer("0198f2c1-first", Advice);
    }

    public ValueTask DisposeAsync()
    {
        foreach (var name in (string[])["FAKECLI_MODE", "FAKECLI_STDOUT", "FAKECLI_OUTFILE_TEXT", "FAKECLI_EXIT", "FAKECLI_STDERR", "FAKECLI_SLEEP_MS"])
        {
            Environment.SetEnvironmentVariable(name, null);
        }

        foreach (var dir in (string[])[_repo, _data])
        {
            try
            {
                Directory.Delete(dir, recursive: true);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }

        return ValueTask.CompletedTask;
    }

    /// <summary>Script the stand-in: a thread id on its event stream, the advice in its `-o` file.</summary>
    private static void Answer(string threadId, string advice)
    {
        Environment.SetEnvironmentVariable("FAKECLI_STDOUT", $$"""{"type":"thread.started","thread_id":"{{threadId}}"}""" + "\n");
        Environment.SetEnvironmentVariable("FAKECLI_OUTFILE_TEXT", advice);
        Environment.SetEnvironmentVariable("FAKECLI_EXIT", null);
        Environment.SetEnvironmentVariable("FAKECLI_STDERR", null);
    }

    private async Task Git(params string[] args)
    {
        var result = await _launcher.RunAsync(new ProcessRequest("git", args, _repo), TestContext.Current.CancellationToken);
        result.ExitCode.Should().Be(0, string.Join(' ', args) + ": " + result.StdErr);
    }

    /// <param name="providers">The REVIEWER rows — by default the one codex row the legacy-shaped shipped map borrows from.</param>
    /// <param name="consultants">The caller map — by default the shipped one, four legacy references.</param>
    private PanelService Service(
        int turns = 5,
        int callsPerSession = 10,
        bool enabled = true,
        IReadOnlyList<ProviderSettings>? providers = null,
        IReadOnlyDictionary<string, ConsultantChoice>? consultants = null) => new(
        new PanelSettings
        {
            Providers = providers ?? [new("codex") { ExecutablePath = FakeCliExe }],
            Rounds = PanelConfig.Uniform(3, 2, StagePolicy.Human),
            DataDir = _data,
            ReviewerTimeout = TimeSpan.FromSeconds(30),
            ConsultTurns = turns,
            ConsultCallsPerSession = callsPerSession,
            ConsultEnabled = enabled,
            Consultants = consultants ?? ConsultantRouting.Shipped,
        },
        VaultKeys.None("no vault in tests"),
        default,
        _launcher,
        Logger.None);

    private static readonly string[] CallerVariables =
        ["COAI_CALLER_SESSION", "CLAUDE_CODE_SESSION_ID", "CODEX_SESSION_ID", "GEMINI_CLI_SESSION_ID"];

    /// <summary>
    /// Pins WHICH vendor is calling for one test, and puts the process environment back afterwards.
    /// </summary>
    /// <remarks>
    /// The service reads the caller kind from the process environment, and this suite runs under
    /// whatever launched it: a Claude Code session exports <c>CLAUDE_CODE_SESSION_ID</c> to every
    /// child, so a test that assumed the kind was <c>other</c> would pass in one terminal and fail in
    /// another. Every vendor variable is cleared and exactly one is set; the caller's id follows from
    /// the same variable, so a consultation opened and resumed inside one test has one owner.
    /// </remarks>
    private static IDisposable CallingAs(string variable)
    {
        var saved = CallerVariables.Select(name => (name, Environment.GetEnvironmentVariable(name))).ToList();
        foreach (var name in CallerVariables)
        {
            Environment.SetEnvironmentVariable(name, null);
        }

        Environment.SetEnvironmentVariable(variable, "consult-scenario");

        return new RestoredEnvironment(saved);
    }

    private sealed record RestoredEnvironment(IReadOnlyList<(string Name, string? Value)> Saved) : IDisposable
    {
        public void Dispose()
        {
            foreach (var (name, value) in Saved)
            {
                Environment.SetEnvironmentVariable(name, value);
            }
        }
    }

    private async Task<JsonElement> Consult(PanelService service, string problem, string id = "", string files = "[]") =>
        JsonDocument.Parse(await service.ConsultAsync(_repo, problem, files, id, TestContext.Current.CancellationToken)).RootElement;

    private static string Advise(JsonElement reply) => reply.GetProperty("advice").GetString()!;

    private static string Refusal(JsonElement reply) => reply.GetProperty("error").GetString()!;

    [Fact]
    public async Task AFirstTurn_AnswersFenced_AndOpensAConversation()
    {
        var reply = await Consult(Service(), "The parser returns 3 where 4 is expected, after two fix attempts.");

        reply.TryGetProperty("error", out _).Should().BeFalse(reply.ToString());
        reply.GetProperty("turnIndex").GetInt32().Should().Be(1);
        reply.GetProperty("maxTurns").GetInt32().Should().Be(5);
        reply.GetProperty("consultationId").GetString().Should().NotBeNullOrEmpty();

        var advice = Advise(reply);
        advice.Should().Contain("status=\"advisory_only\"");
        advice.Should().Contain(Advice);
        advice.Should().Contain("unverified external suggestion");
    }

    /// <summary>
    /// A REFUSED call spends nothing: the cap counts consultations, not attempts.
    /// </summary>
    /// <remarks>
    /// The count used to be taken at the top, before the lock and before every check below it — so an
    /// id belonging to another checkout, a vendor row somebody switched off, a missing runtime or a
    /// lock held by another consultation each cost the caller one of their calls while no consultant
    /// ran. `ConsultCallCounter` has no refund, so a caller could burn the whole cap on refusals and
    /// then be told they had used it up. (CodeRabbit, on the pull request.)
    /// </remarks>
    [Fact]
    public async Task ACallTheServerRefused_DoesNotComeOutOfTheCap()
    {
        var service = Service(callsPerSession: 2);

        // Two refusals, neither of which reaches a consultant: an id this server never wrote.
        Refusal(await Consult(service, "still stuck", id: new string('b', 32)))
            .Should().Contain("no consultation");
        Refusal(await Consult(service, "still stuck", id: new string('c', 32)))
            .Should().Contain("no consultation");

        // The budget is untouched, so both real consultations still run.
        var first = await Consult(service, "the parser returns 3 where 4 is expected");
        first.TryGetProperty("error", out _).Should().BeFalse(first.ToString());

        Answer("0198-second", Advice);
        var second = await Consult(service, "and the lock is taken after the read");
        second.TryGetProperty("error", out _).Should().BeFalse(second.ToString());

        // And the cap itself still holds on the third.
        Answer("0198-third", Advice);
        Refusal(await Consult(service, "a third thing entirely"))
            .Should().Contain("consult calls, the cap");
    }

    [Fact]
    public async Task TheConsultantIsHandedTheWorkingTreeTheSERVERCollected()
    {
        var recorded = Directory.CreateTempSubdirectory("coai-consult-argv-").FullName;
        Environment.SetEnvironmentVariable("FAKECLI_RECORD_DIR", recorded);
        try
        {
            await Consult(Service(), "why is the count wrong");

            var prompt = Directory.EnumerateFiles(recorded, "*.argv")
                .Select(File.ReadAllText)
                .Select(fields => fields.Split('\0')[^1])
                .Single();

            prompt.Should().Contain("// was 4", "the modified tracked file");
            prompt.Should().Contain("a file nobody committed", "the untracked file");
            prompt.Should().Contain("READ-ONLY checkout");
            prompt.Should().Contain("this is turn 1, 4 remain");
            prompt.TrimEnd().Should().EndWith("why is the count wrong", "the problem is last");
        }
        finally
        {
            Environment.SetEnvironmentVariable("FAKECLI_RECORD_DIR", null);
            Directory.Delete(recorded, recursive: true);
        }
    }

    [Fact]
    public async Task ASecondTurn_ResumesTheSameVendorConversation()
    {
        var service = Service();
        var first = await Consult(service, "the count is 3 and should be 4");
        var id = first.GetProperty("consultationId").GetString()!;

        var recorded = Directory.CreateTempSubdirectory("coai-consult-resume-").FullName;
        Environment.SetEnvironmentVariable("FAKECLI_RECORD_DIR", recorded);
        try
        {
            var second = await Consult(service, "I ran your check: with two fields it prints 3, at Parser.cs:1", id);

            second.TryGetProperty("error", out _).Should().BeFalse(second.ToString());
            second.GetProperty("turnIndex").GetInt32().Should().Be(2);
            second.GetProperty("consultationId").GetString().Should().Be(id);

            var argv = File.ReadAllText(Directory.EnumerateFiles(recorded, "*.argv").Single()).Split('\0');
            argv.Should().ContainInOrder("exec", "resume", "0198f2c1-first");
            argv[^1].Should().NotContain("diff --git", "a remembering vendor is not re-sent the tree");
            argv[^1].Should().Contain("has not moved");
            argv[^1].Should().Contain("What the caller verified since your last advice");
        }
        finally
        {
            Environment.SetEnvironmentVariable("FAKECLI_RECORD_DIR", null);
            Directory.Delete(recorded, recursive: true);
        }
    }

    [Fact]
    public async Task TheCapClosesTheConsultation_AndSaysSo()
    {
        var service = Service(turns: 2);
        var id = (await Consult(service, "turn one")).GetProperty("consultationId").GetString()!;
        (await Consult(service, "turn two", id)).GetProperty("turnIndex").GetInt32().Should().Be(2);

        var refused = await Consult(service, "turn three", id);

        Refusal(refused).Should().Contain("all 2 of its turns are used").And.Contain("COAI_CONSULT_TURNS");
    }

    [Fact]
    public async Task ARepeatedProblem_IsRefusedNamingTheTurn()
    {
        var service = Service();
        var id = (await Consult(service, "the count is 3 and should be 4")).GetProperty("consultationId").GetString()!;

        // Case and spacing are normalised away; the words themselves are the signal. A genuinely
        // different follow-up is not caught, which is the point of the rule rather than a gap in it.
        var refused = await Consult(service, "The  Count is 3   and should be 4\n", id);

        Refusal(refused).Should().Contain("repeats turn 1").And.Contain("VERIFIED");
    }

    [Fact]
    public async Task AnUnknownId_IsRefusedWithItsCure()
    {
        Refusal(await Consult(Service(), "hello", new string('a', 32))).Should().Contain("start a new consultation");
        Refusal(await Consult(Service(), "hello", "not-an-id")).Should().Contain("start a new consultation");
    }

    [Fact]
    public async Task TheCallCap_IsANamedRefusal()
    {
        var service = Service(callsPerSession: 1);
        await Consult(service, "the first and only call this session gets");

        var refused = await Consult(service, "one call too many");

        Refusal(refused).Should().Contain("COAI_CONSULT_CALLS_PER_SESSION").And.Contain("ask the person");
    }

    [Fact]
    public async Task AVendorFailure_IsASentence_NotAThrow()
    {
        Environment.SetEnvironmentVariable("FAKECLI_STDOUT", null);
        Environment.SetEnvironmentVariable("FAKECLI_OUTFILE_TEXT", null);
        Environment.SetEnvironmentVariable("FAKECLI_STDERR", "codex: something went wrong");
        Environment.SetEnvironmentVariable("FAKECLI_EXIT", "3");

        Refusal(await Consult(Service(), "why")).Should().Contain("did not answer").And.Contain("providers");
    }

    [Fact]
    public async Task AVendorThatExitsCleanlyAndSaysNothing_KeepsItsTranscript()
    {
        Environment.SetEnvironmentVariable("FAKECLI_OUTFILE_TEXT", null);
        Environment.SetEnvironmentVariable("FAKECLI_STDOUT", "{\"type\":\"thread.started\",\"thread_id\":\"0198-quiet\"}\n");

        var refused = await Consult(Service(), "why");

        Refusal(refused).Should().Contain("answered nothing").And.Contain("unparseable");
        Directory.EnumerateFiles(Path.Combine(_data, "unparseable")).Should().NotBeEmpty();
    }

    [Fact]
    public async Task AnInterruptedTurn_IsResumableAndIsNotCountedAgainstTheCap()
    {
        // The vendor accepted the turn — its thread id is on the stream — and the answer never
        // arrived. Paying for it twice is what the interrupted state exists to prevent.
        Environment.SetEnvironmentVariable("FAKECLI_OUTFILE_TEXT", null);
        Environment.SetEnvironmentVariable("FAKECLI_STDOUT", "{\"type\":\"thread.started\",\"thread_id\":\"0198-kept\"}\n");
        Environment.SetEnvironmentVariable("FAKECLI_STDERR", "killed");
        Environment.SetEnvironmentVariable("FAKECLI_EXIT", "137");
        var service = Service();

        var interrupted = await Consult(service, "the count is wrong");

        Refusal(interrupted).Should().Contain("was NOT counted").And.Contain("consultationId");
        var record = new ConsultationStore(_data).All().Single();
        record.Status.Should().Be(ConsultationStatuses.Interrupted);
        record.Handle.Should().Be("0198-kept");
        record.Turns.Should().BeEmpty("an interrupted turn is not a turn");
    }

    [Fact]
    public async Task AVendorThatDroppedTheThread_SaysSoRatherThanRetryingForever()
    {
        var service = Service();
        var id = (await Consult(service, "the count is wrong")).GetProperty("consultationId").GetString()!;
        Environment.SetEnvironmentVariable("FAKECLI_OUTFILE_TEXT", null);
        Environment.SetEnvironmentVariable("FAKECLI_STDOUT", null);
        Environment.SetEnvironmentVariable("FAKECLI_STDERR", "Error: thread/resume: no rollout found for thread id 0198f2c1-first");
        Environment.SetEnvironmentVariable("FAKECLI_EXIT", "1");

        var refused = await Consult(service, "I checked, and it prints 3", id);

        Refusal(refused).Should().Contain("no longer holds").And.Contain("start a new consultation");
    }

    [Fact]
    public async Task EveryTurnIsOneLedgerRow_OfKindConsult()
    {
        var service = Service();
        var id = (await Consult(service, "turn one")).GetProperty("consultationId").GetString()!;
        await Consult(service, "turn two, having checked", id);

        var rows = (await File.ReadAllLinesAsync(Path.Combine(_data, "usage.jsonl"), TestContext.Current.CancellationToken))
            .Select(line => JsonDocument.Parse(line).RootElement)
            .ToList();

        rows.Should().HaveCount(2);
        rows.Should().OnlyContain(r => r.GetProperty("kind").GetString() == "consult");
        rows.Should().OnlyContain(r => r.GetProperty("stage").GetString() == "Consultation");
        rows.Should().OnlyContain(r => r.GetProperty("role").GetString() == "consult");
    }

    [Fact]
    public async Task TheRepositoryIsUNTOUCHED()
    {
        var before = await Status();

        await Consult(Service(), "why is the count wrong");

        (await Status()).Should().Be(before);
    }

    [Fact]
    public async Task AConsultantThatWritesInTheTree_HasItsAdviceWithheld_AndTheFileIsLeftAlone()
    {
        // The whole reason the invariant exists: read-only is the VENDOR's promise, not ours. The
        // stand-in breaks it, answers perfectly well, and the answer is still withheld.
        var written = Path.Combine(_repo, "the-consultant-wrote-this.txt");
        Environment.SetEnvironmentVariable("FAKECLI_SIDE_EFFECT", written);
        try
        {
            var refused = await Consult(Service(), "why is the count wrong");

            var sentence = Refusal(refused);
            sentence.Should().Contain("the working tree changed").And.Contain("the-consultant-wrote-this.txt");
            sentence.Should().Contain("cannot say who wrote", "two snapshots cannot name a writer, and the person's own editor is in the same window");
            sentence.Should().Contain("nothing was touched");
            sentence.Should().NotContain(Advice, "advice from a run that broke the invariant is not handed over");
            File.Exists(written).Should().BeTrue("nothing is deleted — that file may be the person's");

            var record = new ConsultationStore(_data).All().Single();
            record.Status.Should().Be(ConsultationStatuses.Failed);
            record.Alert.Should().Contain("the-consultant-wrote-this.txt");
        }
        finally
        {
            Environment.SetEnvironmentVariable("FAKECLI_SIDE_EFFECT", null);
        }
    }

    [Fact]
    public async Task ACLIsProseThatHappensToBeJson_ReachesTheCallerWHOLE()
    {
        // The service must read each answer through its OWN adapter. Unwrapping every answer
        // centrally mangled exactly this: a consultant asked about a configuration file answers with
        // JSON that has an `answer` property, and the other fields were silently discarded. Only the
        // local route is schema-bound; codex is prose.
        const string prose = """{"answer":"rename the field","priority":1,"file":"appsettings.json"}""";
        Answer("0198f2c1-prose", prose);

        var advice = Advise(await Consult(Service(), "is this config right?"));

        advice.Should().Contain("\\\"priority\\\": 1".Replace("\\\"", "\"").Replace(": 1", ":1"));
        advice.Should().Contain("appsettings.json", "the whole answer reaches the caller, not one field of it");
    }

    [Fact]
    public async Task AProblemThatSaysNothing_IsRefusedBeforeAnythingIsLaunched()
    {
        Refusal(await Consult(Service(), "   ")).Should().Contain("problem statement is required");
    }

    [Fact]
    public async Task MalformedSuspectedFiles_AreRefusedByName()
    {
        Refusal(await Consult(Service(), "why", files: "src/A.cs")).Should().Contain("JSON array");
    }

    [Fact]
    public async Task APathOutsideAnyCheckout_IsRefusedInGitsWords()
    {
        var elsewhere = Directory.CreateTempSubdirectory("coai-not-a-repo-").FullName;
        try
        {
            var reply = JsonDocument.Parse(
                await Service().ConsultAsync(elsewhere, "why", "[]", string.Empty, TestContext.Current.CancellationToken)).RootElement;

            Refusal(reply).Should().Contain("not inside a git checkout");
        }
        finally
        {
            Directory.Delete(elsewhere, recursive: true);
        }
    }

    /// <summary>
    /// A resumed conversation can find the consultation it already opened.
    /// </summary>
    /// <remarks>
    /// <para>This is what <c>status</c> is for, pointed at the one thing a compacted conversation
    /// loses that costs money: the <c>consultationId</c> the first reply carried. Without it the next
    /// call opens a SECOND consultation — the working tree collected again, a model that has already
    /// answered asked from scratch, and the caller's own per-session budget spent twice.</para>
    /// <para>Keyed by the REPOSITORY rather than the session, because a consultation has no session:
    /// half the moments that start one happen before <c>open</c> exists.</para>
    /// </remarks>
    [Fact]
    public async Task Status_NamesAConsultationThisRepositoryStillHasOpen()
    {
        var service = Service();
        await service.OpenAsync(_repo, "main");
        var opened = await Consult(service, "The parser returns 3 where 4 is expected, after two fix attempts.");
        var id = opened.GetProperty("consultationId").GetString()!;

        var status = JsonDocument.Parse(await service.StatusAsync(_repo, "main")).RootElement;

        var listed = status.GetProperty("consultations").EnumerateArray().Single();
        listed.GetProperty("id").GetString().Should().Be(id);
        listed.GetProperty("vendor").GetString().Should().Be("codex");
        listed.GetProperty("status").GetString().Should().Be(ConsultationStatuses.Open);
        listed.GetProperty("turnsUsed").GetInt32().Should().Be(1);
        listed.GetProperty("maxTurns").GetInt32().Should().Be(5);
    }

    /// <summary>
    /// Another session's open consultation is not offered to this one.
    /// </summary>
    /// <remarks>
    /// <c>Existing</c> refuses a follow-up whose caller differs, so an id from somebody else's session
    /// is an id that comes back as a refusal — listing it would be handing out a dead end. (gemini,
    /// story 4's plan round.)
    /// </remarks>
    [Fact]
    public async Task Status_SaysNothingAboutAConsultationAnotherCallerOpened()
    {
        var service = Service();
        await service.OpenAsync(_repo, "main");
        await Consult(service, "The parser returns 3 where 4 is expected, after two fix attempts.");

        // The same repository, a different caller session — which is what a second agent window is.
        Environment.SetEnvironmentVariable("CLAUDE_CODE_SESSION_ID", "somebody-else");
        try
        {
            var status = JsonDocument.Parse(await Service().StatusAsync(_repo, "main")).RootElement;

            status.GetProperty("consultations").GetArrayLength().Should().Be(0);
        }
        finally
        {
            Environment.SetEnvironmentVariable("CLAUDE_CODE_SESSION_ID", null);
        }
    }

    [Fact]
    public async Task Status_SaysNothingAboutAConsultationThatIsOver()
    {
        var service = Service(turns: 1);
        await service.OpenAsync(_repo, "main");
        await Consult(service, "The parser returns 3 where 4 is expected, after two fix attempts.");

        // A cap of one means that turn closed it, so there is nothing to resume and nothing to say.
        var status = JsonDocument.Parse(await service.StatusAsync(_repo, "main")).RootElement;

        status.GetProperty("consultations").GetArrayLength().Should().Be(0);
    }

    /// <summary>
    /// Switched off is a SENTENCE, and it is the first thing the tool says.
    /// </summary>
    /// <remarks>
    /// The tool stays in the list when the feature is off — a caller that cannot see the tool cannot
    /// be told why it is not there — so the refusal has to name the setting and the panel section
    /// that writes it. Asserted with an empty problem statement as well, because the guard's PLACE is
    /// half of it: an off feature that first complains about an argument reads like a broken tool
    /// rather than a closed one.
    /// </remarks>
    [Fact]
    public async Task SwitchedOff_RefusesByNameAndLaunchesNothing()
    {
        var before = await Status();

        var refusal = Refusal(await Consult(Service(enabled: false), "The parser returns 3 where 4 is expected."));

        refusal.Should().Contain("switched off").And.Contain("COAI_CONSULT_ENABLED").And.Contain("Consultant section");
        Refusal(await Consult(Service(enabled: false), "   ")).Should().Contain("switched off",
            "an off feature answers for being off, not for the shape of a call nobody is going to make");
        // The tree is DIRTY here on purpose — the uncommitted change is what a consultation sends —
        // so the guarantee is that a refusal left it exactly as it was, not that it is clean.
        (await Status()).Should().Be(before, "a refusal touches nothing in the checkout");
    }

    // ---------- the consultant has its own vendors (PLAN_the_consultant_has_its_own_vendors, story B3) ----------

    /// <summary>
    /// A consultant that carries its own definition needs no reviewer row at all.
    /// </summary>
    /// <remarks>
    /// The whole point of the plan: until this story the server looked the consultant's id up in the
    /// REVIEWER catalogue and refused "not configured" when it was absent — so a reviewer removed took
    /// the consultant with it. A definition says which CLI answers and where it is; nothing about the
    /// reviewers is read.
    /// </remarks>
    [Fact]
    public async Task AConsultantDefinedWithoutAReviewerRow_StillResolves()
    {
        using var calling = CallingAs("CODEX_SESSION_ID");
        var service = Service(
            providers: [],
            consultants: new Dictionary<string, ConsultantChoice>
            {
                [CallerIdentity.Codex] = new("codex", Runtime: "codex", ExecutablePath: FakeCliExe),
            });

        var reply = await Consult(service, "The parser returns 3 where 4 is expected, after two fix attempts.");

        reply.TryGetProperty("error", out _).Should().BeFalse(reply.ToString());
        Advise(reply).Should().Contain(Advice);
        var record = new ConsultationStore(_data).All().Single();
        record.Vendor.Should().Be("codex");
        record.Runtime.Should().Be("codex");
    }

    /// <summary>
    /// A definition on a runtime the consultant may not run on is refused by name — caller kind,
    /// vendor, runtime and the allowlist — and nothing is built or launched for it.
    /// </summary>
    /// <remarks>
    /// The security half of the story. Excluding Team servers from the panel's picker does not
    /// exclude <c>remote</c> from the WIRE: a hand-edited or stale settings file can name it, and a
    /// working tree must never be routed at a Team server by a consultation. The refusal has to come
    /// from the guard on the definition — which knows the caller — and not from the adapter factory
    /// after a provider row was already built for it, whose sentence knows no caller.
    /// </remarks>
    [Fact]
    public async Task ADefinitionOnTheRemoteRuntime_IsRefusedByName_BeforeAnyProviderIsBuilt()
    {
        using var calling = CallingAs("CLAUDE_CODE_SESSION_ID");
        var before = await Status();
        var service = Service(
            providers: [],
            consultants: new Dictionary<string, ConsultantChoice>
            {
                [CallerIdentity.Claude] = new("remsoftdev-codex", Runtime: "remote", BaseUrl: "https://coai.example.test"),
            });

        var refusal = Refusal(await Consult(service, "The parser returns 3 where 4 is expected."));

        refusal.Should().Contain("'claude' caller", "the guard knows who is calling; the adapter factory's refusal does not")
            .And.Contain("remsoftdev-codex")
            .And.Contain("'remote'")
            .And.Contain("codex, claude, antigravity, local", "the allowlist is named so the cure is on screen")
            .And.Contain("Consultant section");
        new ConsultationStore(_data).All().Should().BeEmpty("nothing was built, so nothing was recorded");
        (await Status()).Should().Be(before, "a refusal touches nothing in the checkout");
    }

    /// <summary>
    /// A legacy reference to a reviewer row somebody switched OFF still consults.
    /// </summary>
    /// <remarks>
    /// A reviewer switched off is a fact about REVIEWS, and it was taking the consultant down with it —
    /// the opening symptom of the plan. Rule (a) of the one resolution rule materialises the row's
    /// runtime, model, endpoint and CLI path whether the row is enabled or not; whether the row may
    /// review is not the question being asked.
    /// </remarks>
    [Fact]
    public async Task ALegacyReferenceToASwitchedOffRow_ConsultsAnyway()
    {
        using var calling = CallingAs("CLAUDE_CODE_SESSION_ID");
        var service = Service(providers: [new("codex") { ExecutablePath = FakeCliExe, Enabled = false }]);

        var reply = await Consult(service, "The parser returns 3 where 4 is expected, after two fix attempts.");

        reply.TryGetProperty("error", out _).Should().BeFalse(reply.ToString());
        Advise(reply).Should().Contain(Advice);
    }

    /// <summary>
    /// A legacy reference — no runtime — still resolves through the reviewer rows, and takes the
    /// row's model where the entry names none.
    /// </summary>
    /// <remarks>
    /// The dual read the plan round required: a settings file written before definitions existed
    /// carries a bare reference and must not need a rewrite. This is rule (a) on an enabled row —
    /// the semantics the entry always had, materialised — and it is the guard that the change did
    /// not break what already worked.
    /// </remarks>
    [Fact]
    public async Task ALegacyReferenceWithNoRuntime_StillResolvesThroughTheReviewerRows()
    {
        using var calling = CallingAs("CLAUDE_CODE_SESSION_ID");
        var service = Service(providers: [new("codex") { ExecutablePath = FakeCliExe, Model = "gpt-5.6-luna" }]);

        var reply = await Consult(service, "The parser returns 3 where 4 is expected, after two fix attempts.");

        reply.TryGetProperty("error", out _).Should().BeFalse(reply.ToString());
        var record = new ConsultationStore(_data).All().Single();
        record.Vendor.Should().Be("codex");
        record.Model.Should().Be("gpt-5.6-luna", "rule (a) materialises the row's model where the entry names none");
        record.Runtime.Should().Be("codex");
    }

    /// <summary>
    /// A resumed consultation stays on the vendor, model and runtime frozen on its record — even
    /// after the panel moved this caller's consultant elsewhere and removed the reviewer row it
    /// opened on.
    /// </summary>
    /// <remarks>
    /// The record claims the three are frozen, and a settings change between turns must not hand a
    /// conversation opened on one vendor to another — or hand one vendor's handle to a different CLI.
    /// What the record does NOT hold is the endpoint and the CLI path, so those come from whatever
    /// describes that vendor id today: a current definition under ANY caller kind first, then the
    /// legacy path. Here the codex reviewer row is gone, Claude Code's consultant is now a definition
    /// on claude, and the only place <c>codex</c> is still described is Codex's own consultant.
    /// </remarks>
    [Fact]
    public async Task AResumedConsultation_StaysOnTheVendorItOpenedOn_WhenThePanelMoved()
    {
        using var calling = CallingAs("CLAUDE_CODE_SESSION_ID");
        var opened = await Consult(Service(), "the count is 3 and should be 4");
        var id = opened.GetProperty("consultationId").GetString()!;

        var moved = Service(
            providers: [],
            consultants: new Dictionary<string, ConsultantChoice>
            {
                [CallerIdentity.Claude] = new("claude", Runtime: "claude", ExecutablePath: FakeCliExe),
                [CallerIdentity.Codex] = new("codex", Runtime: "codex", ExecutablePath: FakeCliExe),
            });
        var recorded = Directory.CreateTempSubdirectory("coai-consult-moved-").FullName;
        Environment.SetEnvironmentVariable("FAKECLI_RECORD_DIR", recorded);
        try
        {
            var second = await Consult(moved, "I ran your check: with two fields it prints 3, at Parser.cs:1", id);

            second.TryGetProperty("error", out _).Should().BeFalse(second.ToString());
            second.GetProperty("turnIndex").GetInt32().Should().Be(2);
            var argv = File.ReadAllText(Directory.EnumerateFiles(recorded, "*.argv").Single()).Split('\0');
            argv.Should().ContainInOrder(["exec", "resume", "0198f2c1-first"], "the codex CLI's resume shape, not claude's --resume");
            var record = new ConsultationStore(_data).All().Single();
            record.Vendor.Should().Be("codex");
            record.Runtime.Should().Be("codex");
        }
        finally
        {
            Environment.SetEnvironmentVariable("FAKECLI_RECORD_DIR", null);
            Directory.Delete(recorded, recursive: true);
        }
    }

    private async Task<string> Status()
    {
        var result = await _launcher.RunAsync(
            new ProcessRequest("git", ["status", "--porcelain=v1", "--untracked-files=all"], _repo), TestContext.Current.CancellationToken);

        return result.StdOut;
    }
}
