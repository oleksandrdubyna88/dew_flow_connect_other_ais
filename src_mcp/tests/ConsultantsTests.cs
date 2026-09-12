using CoaiMcp.Core.Consultation;
using CoaiMcp.Runners.Consultation;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Reviewers;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The other three consultants. Every flag was verified against the installed CLIs on 2026-09-12 —
/// claude 2.1.258, agy 1.2.2, codex 0.153.4 — before it was pinned here.
/// </summary>
public sealed class ConsultantsTests : IDisposable
{
    private const string Repo = "D:/rsd/some-checkout";
    private const string Answers = "D:/data/consultations/answers";

    private readonly string _data = Directory.CreateTempSubdirectory("coai-consultants-").FullName;

    public void Dispose()
    {
        try
        {
            Directory.Delete(_data, recursive: true);
        }
        catch (IOException) { }
    }

    private static ConsultantLaunch Launch(string handle = "", string model = "", string prompt = "help me") =>
        new(Repo, prompt, handle, Answers, new ReviewerSettings("v") { Model = model });

    private static ProcessResult Said(string stdout, string stderr = "", int exit = 0) =>
        new(exit, stdout, stderr, TimedOut: false);

    // ---------- claude ----------

    private static ClaudeConsultant Claude() => new(new ClaudeRuntime());

    [Fact]
    public void ClaudeIsReadOnly_AndCanSTILLReadTheTree()
    {
        var args = Claude().Build(Launch()).Request.Arguments;

        args.Should().ContainInOrder(["-p", "--output-format", "json"]);
        args.Should().ContainInOrder(["--permission-mode", "plan"]);
        args.Should().Contain("Edit").And.Contain("Write").And.Contain("NotebookEdit");
        // The consultant was asked here to READ the tree; denying Read as a confined REVIEWER does
        // would leave it judging the prompt alone, which is the thing it exists not to do.
        args.Should().NotContain("Read").And.NotContain("Grep").And.NotContain("Bash");
        args.Should().ContainInOrder(["--add-dir", Repo]);
    }

    [Fact]
    public void ClaudeResumesBySessionId()
    {
        Claude().Build(Launch()).Request.Arguments.Should().NotContain("--resume");
        Claude().Build(Launch(handle: "67289235-65f7-40b5-9532-e63515d90f30")).Request.Arguments
            .Should().ContainInOrder(["--resume", "67289235-65f7-40b5-9532-e63515d90f30"]);
    }

    [Fact]
    public void ClaudeReportsItsSessionIdWhetherOrNotItWasGivenOne()
    {
        // Measured: the envelope carries `session_id` even when `--session-id` was not passed, which
        // is why the id is READ rather than minted — a fresh GUID inside Build would make the launch
        // untestable.
        Claude().ReadHandle(Said("""{"session_id":"67289235-65f7-40b5-9532-e63515d90f30","result":"OK"}"""))
            .Should().Be("67289235-65f7-40b5-9532-e63515d90f30");
    }

    [Fact]
    public void ATruncatedClaudeEnvelopeIsNoHandle_NotACrash()
    {
        Claude().ReadHandle(Said("""{"session_id":"6728""")).Should().BeEmpty();
        Claude().ReadHandle(Said(string.Empty)).Should().BeEmpty();
        Claude().ReadHandle(Said("""{"session_id":"../../etc/passwd"}""")).Should().BeEmpty();
    }

    // ---------- antigravity ----------

    private static AntigravityConsultant Agy() => new(new AntigravityRuntime());

    [Fact]
    public void AgyStreamsItsTurnOnStdin_ReadOnly_WithTheCheckout()
    {
        var invocation = Agy().Build(Launch(prompt: "why is it red"));

        invocation.Request.Arguments.Should().Contain("--print=");
        invocation.Request.Arguments.Should().ContainInOrder(["--input-format", "stream-json"]);
        invocation.Request.Arguments.Should().ContainInOrder(["--mode", "plan"]);
        invocation.Request.Arguments.Should().ContainInOrder(["--add-dir", Repo]);
        invocation.Request.Arguments.Should().NotContain("--json-schema", "a consultation answers prose");
        invocation.Request.StdIn.Should().Contain("why is it red").And.StartWith("{");
    }

    [Fact]
    public void AgyResumesByConversationId()
    {
        Agy().Build(Launch()).Request.Arguments.Should().NotContain("--conversation");
        Agy().Build(Launch(handle: "005ad061-c4ed-42e9-a09b-f51ccac80a46")).Request.Arguments
            .Should().ContainInOrder(["--conversation", "005ad061-c4ed-42e9-a09b-f51ccac80a46"]);
    }

    [Fact]
    public void AgysConversationIdIsReadFromANESTEDEvent_AndFromTheLASTOneThatNamesIt()
    {
        // The id is on every step_update as well as the result, and a KILLED turn has no result at
        // all — which is exactly the turn whose handle has to survive.
        var stream = """
            {"event":"step_update","step_update":{"conversation_id":"005ad061-c4ed-42e9-a09b-f51ccac80a46","state":"DONE"}}
            {"event":"step_update","step_update":{"conversation_id":"005ad061-c4ed-42e9-a09b-f51ccac80a46","step_index":1}}
            """;

        Agy().ReadHandle(Said(stream, exit: -1)).Should().Be("005ad061-c4ed-42e9-a09b-f51ccac80a46");
    }

    [Fact]
    public void AgyProseBesideTheStreamIsNotAHandle()
    {
        Agy().ReadHandle(Said("loading conversation_id...\nnot json at all\n")).Should().BeEmpty();
    }

    // ---------- local ----------

    private LocalConsultant Local() => new(new LocalRuntime("local", LocalRuntime.DefaultEndpoint), "local", _data);

    [Fact]
    public void TheLocalEngineIsTheOneThatKeepsNoConversation()
    {
        Local().Memory.Should().BeOfType<ConsultantMemory.WeRemember>()
            .Which.CarryBudget.Should().Be(LocalConsultant.CarryBudget);

        Claude().Memory.Should().BeOfType<ConsultantMemory.VendorRemembers>();
        Agy().Memory.Should().BeOfType<ConsultantMemory.VendorRemembers>();
        new CodexConsultant(new CodexRuntime()).Memory.Should().BeOfType<ConsultantMemory.VendorRemembers>();
    }

    [Fact]
    public void TheLocalEngineIsBoundToTheANSWERSchema_NotTheFindingSchema()
    {
        var args = Local().Build(Launch()).Request.Arguments;

        var schema = args[Array.IndexOf([.. args], "--schema-file") + 1];
        File.ReadAllText(schema).Should().Contain("\"answer\"").And.NotContain("findings");
        args.Should().Contain("--ask-local");
    }

    [Fact]
    public void TheLocalEngineHasNoHandleToKeep_AndNoConversationToDrop()
    {
        Local().ReadHandle(Said("""{"tokensIn":10}""")).Should().BeEmpty();
        Local().DroppedTheConversation(Said(string.Empty, "anything at all")).Should().BeFalse();
    }

    // ---------- shared ----------

    [Fact]
    public void EVERYConsultantRefusesALineBreakInAnyArgument()
    {
        // The MODEL, because it is the one configured value every route puts in argv. (A repository
        // path reaches argv for the three CLIs and not for the local engine, whose shim takes a
        // prompt FILE and a working directory — so a path is not the shared case; a model is.)
        foreach (var consultant in Consultants())
        {
            var build = () => consultant.Build(new ConsultantLaunch(
                Repo, "p", string.Empty, Answers,
                new ReviewerSettings("v") { Model = "gpt-5.6\n--dangerously-bypass-approvals-and-sandbox" }));

            build.Should().Throw<ArgumentException>($"{consultant.Vendor} would lose everything after the first line");
        }
    }

    [Fact]
    public void TheThreeCLIsRefuseALineBreakInTheRepositoryPathToo()
    {
        foreach (var consultant in Consultants().Where(c => c.Vendor != "local"))
        {
            var build = () => consultant.Build(new ConsultantLaunch("D:/rsd/check\nout", "p", string.Empty, Answers, new ReviewerSettings("v")));

            build.Should().Throw<ArgumentException>(consultant.Vendor);
        }
    }

    [Fact]
    public void EVERYConsultantRefusesAMalformedHandleBeforeItReachesAnArgv()
    {
        foreach (var consultant in Consultants())
        {
            foreach (var hostile in (string[])["a & calc", "a|b", "\"x\"", "../../etc/passwd"])
            {
                var build = () => consultant.Build(Launch(handle: hostile));

                build.Should().Throw<ArgumentException>($"{consultant.Vendor} + '{hostile}'");
            }
        }
    }

    [Fact]
    public void EVERYConsultantKeepsAMultiLinePromptOutOfArgv_AndCarriesTheRoleConsult()
    {
        foreach (var consultant in Consultants())
        {
            var invocation = consultant.Build(Launch(prompt: "line one\nline two"));

            invocation.Role.Should().Be("consult");
            invocation.Request.Arguments.Should().OnlyContain(a => !a.Contains('\n'), consultant.Vendor);
        }
    }

    [Fact]
    public void TheThreeCLIsCarryThePromptOnStdin_AndTheLocalEngineInAFile()
    {
        // Two shapes, both of which keep a multi-line prompt away from a Windows shim's argv: the
        // CLIs read stdin, the local shim is handed a `--prompt-file` it reads itself.
        foreach (var consultant in Consultants().Where(c => c.Vendor != "local"))
        {
            consultant.Build(Launch(prompt: "line one\nline two")).Request.StdIn
                .Should().Contain("line two", consultant.Vendor);
        }

        var local = Local().Build(Launch(prompt: "line one\nline two"));
        var promptFile = local.Request.Arguments[Array.IndexOf([.. local.Request.Arguments], "--prompt-file") + 1];
        File.ReadAllText(promptFile).Should().Contain("line two");
    }

    [Fact]
    public void EVERYConsultantRunsInTheCheckout()
    {
        foreach (var consultant in Consultants())
        {
            consultant.Build(Launch()).Request.WorkingDirectory.Should().Be(Repo, consultant.Vendor);
        }
    }

    private IConsultantRuntime[] Consultants() =>
        [new CodexConsultant(new CodexRuntime()), Claude(), Agy(), Local()];
}

/// <summary>Which vendor rows can consult now that the other three adapters exist.</summary>
public sealed class ConsultantResolutionAfterStoryTwoTests : IDisposable
{
    private readonly string _data = Directory.CreateTempSubdirectory("coai-resolve-").FullName;

    public void Dispose()
    {
        try
        {
            Directory.Delete(_data, recursive: true);
        }
        catch (IOException) { }
    }

    [Theory]
    [InlineData("codex", "codex", "")]
    [InlineData("claude", "claude", "")]
    [InlineData("antigravity", "antigravity", "")]
    [InlineData("my-claude", "claude", "")]
    public void EveryVendorCliConsults(string provider, string runtime, string baseUrl)
    {
        ConsultantResolution.For(new VendorIdentity(provider, runtime, baseUrl), _data).Should().NotBeNull();
    }

    [Fact]
    public void TheLocalEngineConsults_WhenItHasSomewhereToPutItsSchema()
    {
        var identity = new VendorIdentity("local", "local", "http://127.0.0.1:11434/v1");

        ConsultantResolution.For(identity, _data).Should().NotBeNull();
        ConsultantResolution.For(identity).Should().BeNull("with no data directory it has nowhere to write the answer schema");
    }

    [Theory]
    [InlineData("remsoftdev-codex", "remote", "https://coai.remsoft.dev")]
    [InlineData("deepseek", "codex", "https://api.deepseek.com")]
    public void ATeamServerRowAndACustomEndpointAreStillRefusedByName(string provider, string runtime, string baseUrl)
    {
        var identity = new VendorIdentity(provider, runtime, baseUrl);

        ConsultantResolution.For(identity, _data).Should().BeNull();
        ConsultantResolution.CannotConsult(identity).Should().Contain(provider).And.Contain("Consultant section");
    }

    [Fact]
    public void AClaudeRowNeverProducesAnAgyExecutable()
    {
        // vendor-routing.md, made structural: the adapter and the executable are chosen together.
        var consultant = ConsultantResolution.For(new VendorIdentity("claude", "claude", string.Empty), _data);

        consultant.Should().BeOfType<ClaudeConsultant>();
        consultant!.Build(new ConsultantLaunch("D:/repo", "p", string.Empty, "D:/out", new ReviewerSettings("claude")))
            .Request.Executable.Should().Be("claude");
    }
}
