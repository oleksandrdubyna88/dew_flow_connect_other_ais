using System.Text.Json;
using CoaiMcp.Core.QuestionConsult;
using CoaiMcp.Runners.Consultation;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Reviewers;
using CoaiMcp.Server;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// An agy launch cannot write inside its roots (todo/PLAN_agy_cannot_write_its_roots.md): every launch runs from a folder
/// coai owns, as an agent whose only tool is <c>view_file</c>, behind a hook that allows nothing else
/// (research/RESULTS_agy_write_block.md). What CI cannot prove — that agy honours them — is the live probe's job.
/// </summary>
public sealed class AntigravityReadOnlyTests : IDisposable
{
    private readonly string _base = Directory.CreateTempSubdirectory("coai-agyro-").FullName;

    private static HookHandler Handler => OperatingSystem.IsWindows()
        ? new HookHandler(@"C:\tools\coai mcp\coai-mcp.exe", [])
        : new HookHandler("/opt/coai mcp/coai-mcp", []);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_base, recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    private static string Payload(string name, string args = "{}") =>
        $$"""{"toolCall":{"name":"{{name}}","args":{{args}}},"stepIdx":3,"conversationId":"c"}""";

    private static string Decision(string payload) =>
        JsonDocument.Parse(AntigravityReadOnly.Decide(payload)).RootElement.GetProperty("decision").GetString()!;

    [Fact]
    public void OnlyViewFile_IsAllowed_AndEveryOtherTool_OrAPayloadNotUnderstood_IsDenied()
    {
        Decision(Payload("view_file")).Should().Be("allow");
        foreach (var name in (string[])["write_to_file", "replace_file_content", "run_command", "browser_click_element", "define_subagent", "View_File", ""])
        {
            Decision(Payload(name)).Should().Be("deny", name);
        }

        Decision("").Should().Be("deny", "an empty payload is not a read");
        Decision("not json").Should().Be("deny");
        Decision("""{"toolCall":{}}""").Should().Be("deny");
        // The ARGUMENTS can carry any text, the allowed tool's own name included — the decision reads the call's name.
        Decision(Payload("write_to_file", """{"Content":"\"name\":\"view_file\""}""")).Should().Be("deny");
        JsonDocument.Parse(AntigravityReadOnly.Decide(Payload("write_to_file"))).RootElement.GetProperty("reason").GetString()
            .Should().Be(AntigravityReadOnly.DenyReason);
    }

    [Fact]
    public void TheHookMode_ReadsThePayload_AndWritesTheDecision()
    {
        using var output = new StringWriter();

        AntigravityReadOnly.RunHook(new StringReader(Payload("run_command")), output).Should().Be(0);

        output.ToString().Should().Contain("\"deny\"");
    }

    [Fact]
    public void TheHookArgument_StartsTheHookMode() =>
        Program.Classify([AntigravityReadOnly.HookArgument]).Should().Be(Program.Startup.AgyHook);

    [Fact]
    public async Task TheBuiltBinary_AnswersTheHook_AsTheChildProcessAgyStarts()
    {
        // What agy runs for every tool call: coai-mcp with the hook argument, the payload on stdin.
        var dll = Path.Combine(AppContext.BaseDirectory, "coai-mcp.dll");
        var launcher = new ProcessLauncher();

        async Task<string> Answer(string payload) => (await launcher.RunAsync(
            new ProcessRequest("dotnet", [dll, AntigravityReadOnly.HookArgument], _base) { StdIn = payload, Timeout = TimeSpan.FromSeconds(60) },
            TestContext.Current.CancellationToken)).StdOut;

        JsonDocument.Parse(await Answer(Payload("write_to_file"))).RootElement.GetProperty("decision").GetString().Should().Be("deny");
        JsonDocument.Parse(await Answer(Payload("view_file"))).RootElement.GetProperty("decision").GetString().Should().Be("allow");
    }

    [Fact]
    public async Task TheHookCommand_AsAgyRunsIt_StartsTheHandler_EvenWhereTheCurrentDirectoryIsNotSearched()
    {
        // Live, 2026-10-09: Claude Code sets NoDefaultCurrentDirectoryInExePath=1, agy inherits it, and `cmd /c
        // coai-hook.cmd` then answered "not recognized" — on Windows the hook silently did nothing. The command is run
        // here exactly as agy runs it: by the platform's shell, from the hooks file's folder, with that variable set.
        var folder = ((AntigravityReadOnly.Prepared.Ready)AntigravityReadOnly.Prepare(
            _base, new HookHandler("dotnet", [Path.Combine(AppContext.BaseDirectory, "coai-mcp.dll")]))).Folder;
        var agents = Path.Combine(folder, ".agents");
        var command = JsonDocument.Parse(File.ReadAllText(Path.Combine(agents, "hooks.json"))).RootElement
            .GetProperty("coai-read-only").GetProperty("PreToolUse")[0].GetProperty("hooks")[0].GetProperty("command").GetString()!;
        var shell = OperatingSystem.IsWindows() ? new ProcessRequest("cmd", ["/c", command], agents) : new ProcessRequest("sh", ["-c", command], agents);

        var answer = await new ProcessLauncher().RunAsync(
            shell with
            {
                StdIn = Payload("write_to_file"),
                Environment = new Dictionary<string, string?> { ["NoDefaultCurrentDirectoryInExePath"] = "1" },
                Timeout = TimeSpan.FromSeconds(60),
            },
            TestContext.Current.CancellationToken);

        answer.StdOut.Should().Contain("\"decision\":\"deny\"", $"the hook must reach the handler: {answer.StdErr}");
    }

    [Fact]
    public void TheFolder_IsThePersonsOwn_NeverTheSharedTemporaryFolder()
    {
        // Security review of 0a32211c: on Linux /tmp is shared by every user, so another account could plant the
        // folder — and its hook script, which agy runs as this user — before coai wrote it.
        AntigravityReadOnly.DefaultBase.Should().Be(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "coai-agy"));
        AntigravityReadOnly.DefaultBase.Should().NotStartWith(Path.GetTempPath());
        if (!OperatingSystem.IsWindows())
        {
            var folder = ((AntigravityReadOnly.Prepared.Ready)AntigravityReadOnly.Prepare(_base, Handler)).Folder;
            File.GetUnixFileMode(folder).Should().Be(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    [Fact]
    public void TheFolder_HoldsTheReaderAgent_AndTheHookThatStartsThisHandler()
    {
        var folder = ((AntigravityReadOnly.Prepared.Ready)AntigravityReadOnly.Prepare(_base, Handler)).Folder;

        var agent = File.ReadAllText(Path.Combine(folder, ".agents", "agents", "coai-reader.md"));
        agent.Should().Contain("name: coai-reader").And.Contain("tools:\n  - view_file\n").And.Contain("mainAgent: true");
        var hooks = JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, ".agents", "hooks.json"))).RootElement;
        var group = hooks.GetProperty("coai-read-only").GetProperty("PreToolUse")[0];
        group.GetProperty("matcher").GetString().Should().Be("*", "every tool call reaches the handler");
        group.GetProperty("hooks")[0].GetProperty("command").GetString().Should().Be(OperatingSystem.IsWindows() ? @".\coai-hook.cmd" : "sh ./coai-hook.sh");
        File.ReadAllText(Path.Combine(folder, ".agents", "coai-hook.cmd")).Should().Be($"@\"{Handler.Executable}\" --agy-hook\r\n");
        File.ReadAllText(Path.Combine(folder, ".agents", "coai-hook.sh")).Should().Be($"#!/bin/sh\nexec \"{Handler.Executable}\" --agy-hook\n");
    }

    [Fact]
    public void PreparingAgain_IsTheSameFolder_AndRewritesAFileThatChanged()
    {
        var first = ((AntigravityReadOnly.Prepared.Ready)AntigravityReadOnly.Prepare(_base, Handler)).Folder;
        var agent = Path.Combine(first, ".agents", "agents", "coai-reader.md");
        File.WriteAllText(agent, "---\nname: coai-reader\ntools:\n  - view_file\n  - write_to_file\n---\n");

        var second = ((AntigravityReadOnly.Prepared.Ready)AntigravityReadOnly.Prepare(_base, Handler)).Folder;

        second.Should().Be(first);
        File.ReadAllText(agent).Should().Be(AntigravityReadOnly.AgentFile, "a tampered agent is put back before any launch");
    }

    [Fact]
    public void AFolderThatCannotBeWritten_IsFailed_NeverAFolderWithoutItsBlock()
    {
        var blocked = Path.Combine(_base, "a-file");
        File.WriteAllText(blocked, "a file where the folder would go");

        var prepared = AntigravityReadOnly.Prepare(blocked, Handler);

        prepared.Should().BeOfType<AntigravityReadOnly.Prepared.Failed>()
            .Which.Reason.Should().StartWith("agy was not launched");
    }

    [Theory]
    [InlineData("C:\\a\"b\\coai-mcp.exe")]
    [InlineData("/opt/a\nb/coai-mcp")]
    [InlineData("/opt/$HOME/coai-mcp")]
    [InlineData("C:\\%TEMP%\\coai-mcp.exe")]
    public void AHandlerPathAScriptWouldMisread_IsFailed(string executable)
    {
        AntigravityReadOnly.Prepare(_base, new HookHandler(executable, [])).Should().BeOfType<AntigravityReadOnly.Prepared.Failed>();
    }

    [Fact]
    public void TwoHandlers_GetTwoFolders()
    {
        var one = ((AntigravityReadOnly.Prepared.Ready)AntigravityReadOnly.Prepare(_base, Handler)).Folder;
        var two = ((AntigravityReadOnly.Prepared.Ready)AntigravityReadOnly.Prepare(_base, Handler with { Prefix = ["coai-mcp.dll"] })).Folder;

        two.Should().NotBe(one);
    }

    // ---------- every launch builder (the plan round's finding 2) ----------

    private static readonly string Repo = Path.GetTempPath();

    private static ReviewerSettings Settings => new("antigravity") { Timeout = TimeSpan.FromMinutes(1) };

    private static void RunsReadOnly(ProcessRequest request, string what)
    {
        request.Arguments.Should().ContainInConsecutiveOrder(AntigravityReadOnly.AgentArguments, what);
        request.WorkingDirectory.Should().Be(AntigravityReadOnly.Home(), $"{what} runs from the folder holding its agent and hook");
        File.Exists(Path.Combine(request.WorkingDirectory, ".agents", "hooks.json")).Should().BeTrue(what);
    }

    [Fact]
    public void EveryAgyLaunch_RunsFromTheReadOnlyFolder_AsTheReaderAgent()
    {
        var consultant = new AntigravityConsultant(new AntigravityRuntime());

        RunsReadOnly(new AntigravityRuntime().Build("arch", "p", Repo, Path.Combine(Repo, "s.json"), Repo, Settings).Request, "a reviewer");
        var consult = consultant.Build(new ConsultantLaunch(Repo, "help", string.Empty, Repo, Settings));
        RunsReadOnly(consult.Request, "a consult");
        RunsReadOnly(AntigravityStream.Continue(consult, "5e1f2a3b-4c5d-4e6f-8a9b-0c1d2e3f4a5b", "go on").Request, "a consult's continuation");
        RunsReadOnly(consultant.Build(new ConsultantLaunch(Repo, "help", "5e1f2a3b-4c5d-4e6f-8a9b-0c1d2e3f4a5b", Repo, Settings)).Request, "a resumed consult");

        foreach (var capability in (Capability[])[Capability.Disk, Capability.None])
        {
            var plan = (Confinement.Planned)ConfinementPlanner.Plan("antigravity", capability == Capability.Disk
                ? new CapabilityGrant(Capability.Disk, [Repo])
                : CapabilityGrant.None);
            var launch = new ConsultantLaunch(Repo, "question", string.Empty, Repo, Settings)
            {
                Confinement = new LaunchConfinement.Planned(plan),
                ScratchDir = Repo,
            };

            RunsReadOnly(consultant.Build(launch).Request, $"a question row ({capability})");
        }
    }

    [Fact]
    public void AReviewerGivenACheckout_IsToldItsPath_ForItNoLongerRunsFromIt()
    {
        var worktree = Directory.CreateDirectory(Path.Combine(_base, "worktree")).FullName;
        File.WriteAllText(Path.Combine(worktree, "A.cs"), "class A { }\n");

        var request = new AntigravityRuntime().Build("arch", "Review the change.", worktree, Path.Combine(_base, "s.json"), _base, Settings).Request;

        request.Arguments.Should().ContainInConsecutiveOrder("--add-dir", worktree);
        var told = JsonDocument.Parse(request.StdIn).RootElement.GetProperty("message").GetProperty("content").GetString();
        told.Should().StartWith("Review the change.").And.Contain($"The checkout under review is at {worktree}");
        var empty = Directory.CreateDirectory(Path.Combine(_base, "empty")).FullName;
        JsonDocument.Parse(new AntigravityRuntime().Build("plan", "p", empty, Path.Combine(_base, "s.json"), _base, Settings).Request.StdIn)
            .RootElement.GetProperty("message").GetProperty("content").GetString().Should().Be("p", "a plan round has no checkout to name");
    }

    [Fact]
    public async Task AConsultWhoseFolderCannotBePrepared_IsRefusedBeforeTheLaunch_NeverLaunchedWithoutItsBlock()
    {
        var launch = new ConsultantLaunch(Repo, "help", string.Empty, Repo, Settings);
        var failing = new AntigravityConsultant(new AntigravityRuntime(), prepare: () => new AntigravityReadOnly.Prepared.Failed("agy was not launched: the disk is full"));

        var refused = await failing.PrepareAsync(launch, new ProcessLauncher(), TestContext.Current.CancellationToken);
        var ready = await new AntigravityConsultant(new AntigravityRuntime()).PrepareAsync(launch, new ProcessLauncher(), TestContext.Current.CancellationToken);

        refused.Should().BeOfType<ConsultantPreparation.Refused>()
            .Which.Failure.Should().BeOfType<ConsultFailure.VendorRefused>().Which.Said.Should().Contain("the disk is full");
        ready.Should().BeOfType<ConsultantPreparation.Ready>();
    }

    [Fact]
    public void AQuestionRowOnDisk_StillReachesItsRoot_ThroughAddDir()
    {
        var plan = (Confinement.Planned)ConfinementPlanner.Plan("antigravity", new CapabilityGrant(Capability.Disk, [Repo]));
        var launch = new ConsultantLaunch(Repo, "question", string.Empty, Repo, Settings) { Confinement = new LaunchConfinement.Planned(plan) };

        new AntigravityConsultant(new AntigravityRuntime()).Build(launch).Request.Arguments.Should().ContainInConsecutiveOrder("--add-dir", Repo);
    }
}
