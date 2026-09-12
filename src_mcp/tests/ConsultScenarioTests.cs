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
/// This is the scenario <c>ScenarioCoverageTests</c> demands for the eighth tool. Nothing here
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

    private PanelService Service(int turns = 5, int callsPerSession = 10) => new(
        new PanelSettings
        {
            Providers = [new("codex") { ExecutablePath = FakeCliExe }],
            Rounds = PanelConfig.Uniform(3, 2, StagePolicy.Human),
            DataDir = _data,
            ReviewerTimeout = TimeSpan.FromSeconds(30),
            ConsultTurns = turns,
            ConsultCallsPerSession = callsPerSession,
        },
        VaultKeys.None("no vault in tests"),
        default,
        _launcher,
        Logger.None);

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
            sentence.Should().Contain("changed the working tree").And.Contain("the-consultant-wrote-this.txt");
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

    private async Task<string> Status()
    {
        var result = await _launcher.RunAsync(
            new ProcessRequest("git", ["status", "--porcelain=v1", "--untracked-files=all"], _repo), TestContext.Current.CancellationToken);

        return result.StdOut;
    }
}
