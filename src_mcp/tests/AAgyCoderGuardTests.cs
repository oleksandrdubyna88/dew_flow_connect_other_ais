using System.Text.Json;
using System.Text.Json.Nodes;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Reviewers;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// Epoch 3 E3 s6 t2 (V15; judgement G2, G3, G4 as G15 amends it, G5): the write guard of the agy coder. The handler is closed by construction — an explicit decision for EVERY input — allows only a measured file tool inside the worktree of the job, and records each decision by the
/// conversation id, the step index and the tool name. The tests call the handler as a function over a real folder tree (links included), and the real hook command of <c>hooks.json</c> as a process, with a closed standard output among the inputs.
/// </summary>
/// <remarks>Every path is under a temp folder of the test; no real agy is started and no real home is read.</remarks>
[Collection("fakecli-env")]
public sealed class AAgyCoderGuardTests : IDisposable
{
    private const string Conversation = "11111111-2222-3333-4444-555555555555";

    private readonly TempDir _temp = TempDir.For("coai-agy-guard-");
    private readonly string _wt;
    private readonly string _outside;
    private readonly string _guard;
    private readonly string _records;
    private readonly string _agents;

    public AAgyCoderGuardTests()
    {
        _wt = Path.Combine(_temp.Path, "job", "worktree");
        _outside = Path.Combine(_temp.Path, "job", "outside");
        _guard = Path.Combine(_temp.Path, "job", "coder", "guard");
        _records = Path.Combine(_temp.Path, "job", "coder", "records.jsonl");
        foreach (var folder in new[] { _wt, Path.Combine(_wt, ".git"), Path.Combine(_wt, ".agents"), Path.Combine(_wt, ".codex"), Path.Combine(_wt, "src"), _outside, Path.GetDirectoryName(_records)! })
        {
            Directory.CreateDirectory(folder);
        }

        File.WriteAllText(Path.Combine(_wt, "src", "a.txt"), "a\n");
        File.WriteAllText(Path.Combine(_wt, ".git", "config"), "c\n");
        File.WriteAllText(Path.Combine(_outside, "secret.txt"), "s\n");
        _agents = Path.Combine(_guard, ".agents");
        AntigravityCoderGuard.Prepare(_guard, Handler, _wt, _records).Should().BeOfType<AntigravityReadOnly.Prepared.Ready>();
    }

    public void Dispose() => _temp.Dispose();

    private static HookHandler Handler => new(RoleJobWorld.FakeCli, []);

    private string Payload(string tool, string? path, string? name = null, int step = 3, string? cwd = null, string[]? workspaces = null, string? content = null)
    {
        var args = new JsonObject();
        if (path is not null)
        {
            args["Path"] = path;
        }

        if (content is not null)
        {
            args["Content"] = content;
        }

        return new JsonObject
        {
            ["conversationId"] = Conversation,
            ["cwd"] = cwd ?? _guard,
            ["hookEventName"] = "PreToolUse",
            ["stepIdx"] = step,
            ["toolCall"] = new JsonObject { ["args"] = args, ["id"] = "call_1", ["name"] = name ?? tool },
            ["workspacePaths"] = new JsonArray((workspaces ?? [_guard, _wt]).Select(item => (JsonNode)JsonValue.Create(item)!).ToArray()),
        }.ToJsonString();
    }

    private string Decide(string payload) => AntigravityCoderGuard.Decide(payload, _agents);

    private static string Verdict(string decision) => JsonDocument.Parse(decision).RootElement.GetProperty("decision").GetString()!;

    private string[] Records() => File.Exists(_records) ? File.ReadAllLines(_records) : [];

    // ---------- d1: what it allows and denies ----------

    [Theory]
    [InlineData("read_file")]
    [InlineData("write_file")]
    [InlineData("edit_file")]
    public void AMeasuredFileTool_InsideTheWorktree_IsAllowed(string tool)
    {
        Verdict(Decide(Payload(tool, Path.Combine(_wt, "src", "a.txt")))).Should().Be("allow");
        Verdict(Decide(Payload(tool, Path.Combine(_wt, "src", "new", "file.txt")))).Should().Be("allow", "a file that is not there yet is a place inside the worktree too");
    }

    public static TheoryData<string> DeniedCases() =>
    [
        "outside", "git-file", "git-folder", "agents", "codex", "file-link-out", "folder-link-out", "guard-folder", "guard-file", "sign-in", "link-to-git", "relative", "dotdot", "workspace-lacks-worktree", "cwd-inside-worktree",
        "run_command", "read_url_content", "search_web", "manage_task", "invoke_subagent", "unknown-tool", "no-path", "two-paths", "path-not-a-string", "view_file",
    ];

    [Theory]
    [MemberData(nameof(DeniedCases))]
    public void EveryOtherInput_IsDenied_WithAnExplicitDecision(string which)
    {
        Link("file-out", Path.Combine(_outside, "secret.txt"), Path.Combine(_wt, "src", "out.txt"));
        Link("folder-out", _outside, Path.Combine(_wt, "src", "outdir"));
        Link("to-git", Path.Combine(_wt, ".git", "config"), Path.Combine(_wt, "src", "g.txt"));
        var signIn = Path.Combine(_temp.Path, "home", ".gemini", "antigravity-cli", "antigravity-oauth-token");
        Directory.CreateDirectory(Path.GetDirectoryName(signIn)!);
        File.WriteAllText(signIn, "{}");
        var inside = Path.Combine(_wt, "src", "a.txt");

        var payload = which switch
        {
            "outside" => Payload("write_file", Path.Combine(_outside, "secret.txt")),
            "git-file" => Payload("write_file", Path.Combine(_wt, ".git", "config")),
            "git-folder" => Payload("edit_file", Path.Combine(_wt, ".git")),
            "agents" => Payload("write_file", Path.Combine(_wt, ".agents", "hooks.json")),
            "codex" => Payload("write_file", Path.Combine(_wt, ".codex", "config.toml")),
            "file-link-out" => Payload("write_file", Path.Combine(_wt, "src", "out.txt")),
            "folder-link-out" => Payload("write_file", Path.Combine(_wt, "src", "outdir", "x.txt")),
            "guard-folder" => Payload("read_file", Path.Combine(_guard, ".agents", "hooks.json")),
            "guard-file" => Payload("write_file", Path.Combine(_agents, AntigravityCoderGuard.GuardFile)),
            "sign-in" => Payload("read_file", signIn),
            "link-to-git" => Payload("read_file", Path.Combine(_wt, "src", "g.txt")),
            "relative" => Payload("write_file", "src/a.txt"),
            "dotdot" => Payload("write_file", Path.Combine(_wt, "src", "..", "..", "outside", "secret.txt")),
            "workspace-lacks-worktree" => Payload("write_file", inside, workspaces: [_guard]),
            "cwd-inside-worktree" => Payload("write_file", inside, cwd: Path.Combine(_wt, "src")),
            "no-path" => Payload("write_file", null),
            "two-paths" => Payload("write_file", inside).Replace("\"Path\":", "\"Path\":\"" + Path.Combine(_outside, "secret.txt") + "\",\"path\":", StringComparison.Ordinal),
            "path-not-a-string" => Payload("write_file", inside).Replace(JsonSerializer.Serialize(inside), "42", StringComparison.Ordinal),
            "unknown-tool" => Payload("multi_edit_file", inside),
            var tool => Payload(tool, inside),
        };

        var decision = Decide(payload);

        Verdict(decision).Should().Be("deny", which);
        JsonDocument.Parse(decision).RootElement.GetProperty("reason").GetString().Should().StartWith("coai:").And.NotContain(_temp.Path, "the reason never quotes the model's path");
        Records().Should().ContainSingle().Which.Should().Contain("\"decision\":\"deny\"");
    }

    private static void Link(string what, string target, string at)
    {
        _ = what;
        Directory.CreateDirectory(Path.GetDirectoryName(at)!);
        if (!Path.Exists(at))
        {
            File.CreateSymbolicLink(at, target);
        }
    }

    [Fact]
    public void ALinkInsideTheWorktreeToAnotherPlaceInsideIt_IsAllowed()
    {
        File.CreateSymbolicLink(Path.Combine(_wt, "alias.txt"), Path.Combine(_wt, "src", "a.txt"));

        Verdict(Decide(Payload("write_file", Path.Combine(_wt, "alias.txt")))).Should().Be("allow");
    }

    [Fact]
    public void TheCoderCaseIsChosenByTheGuardsOwnFile_NeverByAFieldOfThePayload()
    {
        // A payload that CLAIMS to be the read-only case, or that names another worktree, changes nothing: the guard's file in the handler's folder decides.
        var claim = JsonNode.Parse(Payload("write_file", Path.Combine(_outside, "secret.txt")))!.AsObject();
        claim["coder"] = false;
        claim["readOnly"] = true;
        claim["guard"] = "none";
        claim["worktree"] = _outside;
        claim["workspacePaths"] = new JsonArray(_outside);

        Verdict(Decide(claim.ToJsonString())).Should().Be("deny");

        // Without the file the handler is the read-only handler of the consultants, byte for byte: a reader tool inside its roots is allowed and a write tool is denied.
        var recordsBefore = Records().Length;
        File.Delete(Path.Combine(_agents, AntigravityCoderGuard.GuardFile));
        var readOnly = AntigravityCoderGuard.RunHook(new StringReader(Payload("view_file", Path.Combine(_wt, "src", "a.txt"))), new StringWriter(), _agents);
        readOnly.Should().Be(0);
        var written = new StringWriter();
        AntigravityCoderGuard.RunHook(new StringReader(Payload("write_file", Path.Combine(_wt, "src", "a.txt"))), written, _agents);
        Verdict(written.ToString()).Should().Be("deny");
        Records().Should().HaveCount(recordsBefore, "the read-only handler makes no record");
    }

    [Theory]
    [InlineData("not json at all")]
    [InlineData("{")]
    [InlineData("{\"version\":1}")]
    [InlineData("{\"version\":1,\"worktree\":\"relative\",\"records\":\"x\"}")]
    public void AGuardFileThatCannotBeRead_DeniesEverything_AndIsNeverTheReadOnlyCase(string text)
    {
        File.WriteAllText(Path.Combine(_agents, AntigravityCoderGuard.GuardFile), text);

        Verdict(Decide(Payload("read_file", Path.Combine(_wt, "src", "a.txt")))).Should().Be("deny");
        Verdict(Decide(Payload("view_file", Path.Combine(_wt, "src", "a.txt")))).Should().Be("deny");
    }

    // ---------- d2: closed by construction ----------

    [Theory]
    [InlineData("")]
    [InlineData("   \n")]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("{\"toolCall\":{\"name\":\"write_file\",\"args\":{}}}")]
    [InlineData("{\"toolCall\":{\"name\":\"\\uD800\"}}")]
    public void EveryInput_GetsAnExplicitAnswer_AndNoneIsExitZeroWithNoOutput(string input)
    {
        var output = new StringWriter();

        var exit = AntigravityCoderGuard.RunHook(new StringReader(input), output, _agents);

        exit.Should().Be(0);
        output.ToString().Should().NotBeNullOrWhiteSpace("exit 0 with no output is the one answer agy takes as allow");
        Verdict(output.ToString()).Should().Be("deny");
    }

    [Fact]
    public void AReadThatBlocksPastTheDeadline_IsADeny_InsideTheDeadline()
    {
        var started = DateTime.UtcNow;

        var decision = AntigravityCoderGuard.DecideWithin(new BlockedReader(), _agents, TimeSpan.FromMilliseconds(300));

        Verdict(decision).Should().Be("deny");
        (DateTime.UtcNow - started).Should().BeLessThan(TimeSpan.FromSeconds(3));
        AntigravityCoderGuard.Deadline.Should().BeLessThan(TimeSpan.FromSeconds(10), "under the hook's own 10 s timeout, so the handler denies and is not killed");
    }

    [Fact]
    public void AnExceptionWhileReading_IsADeny()
    {
        Verdict(AntigravityCoderGuard.DecideWithin(new ThrowingReader(), _agents, TimeSpan.FromSeconds(2))).Should().Be("deny");
    }

    [Fact]
    public void ADecisionThatCannotBeWritten_IsANonZeroExit()
    {
        AntigravityCoderGuard.RunHook(new StringReader(Payload("write_file", Path.Combine(_wt, "src", "a.txt"))), new ClosedWriter(), _agents).Should().NotBe(0);
    }

    [Fact]
    public void ARecordThatCannotBeWritten_TurnsAnAllowIntoADeny()
    {
        File.WriteAllText(Path.Combine(_agents, AntigravityCoderGuard.GuardFile), AntigravityCoderGuard.GuardFileText(_wt, Path.Combine(_temp.Path, "no-such-folder", "records.jsonl")));

        var decision = Decide(Payload("write_file", Path.Combine(_wt, "src", "a.txt")));

        Verdict(decision).Should().Be("deny");
        decision.Should().Contain(AntigravityCoderGuard.DenyRecordReason);
    }

    // ---------- d3: the record ----------

    [Fact]
    public void EachDecision_LeavesOneRecord_WithTheConversationTheStepTheToolAndTheDecision_AndNeverTheContent()
    {
        Decide(Payload("write_file", Path.Combine(_wt, "src", "a.txt"), step: 4, content: "TOP-SECRET-CONTENT"));
        Decide(Payload("run_command", Path.Combine(_wt, "src", "a.txt"), step: 6));

        var records = Records().Select(line => JsonDocument.Parse(line).RootElement).ToList();
        records.Should().HaveCount(2);
        records[0].GetProperty("conversationId").GetString().Should().Be(Conversation);
        (records[0].GetProperty("stepIdx").GetInt64(), records[0].GetProperty("tool").GetString(), records[0].GetProperty("decision").GetString()).Should().Be((4, "write_file", "allow"));
        (records[1].GetProperty("stepIdx").GetInt64(), records[1].GetProperty("tool").GetString(), records[1].GetProperty("decision").GetString()).Should().Be((6, "run_command", "deny"));
        File.ReadAllText(_records).Should().NotContain("TOP-SECRET-CONTENT").And.NotContain("a.txt", "the record holds no path either");
    }

    [Fact]
    public void TheRecordsFile_IsOutsideTheWorktree_AndOutsideTheGuardFolder_OrTheFolderIsRefused()
    {
        AntigravityCoderGuard.Prepare(_guard, Handler, _wt, Path.Combine(_wt, "records.jsonl")).Should().BeOfType<AntigravityReadOnly.Prepared.Failed>();
        AntigravityCoderGuard.Prepare(_guard, Handler, _wt, Path.Combine(_guard, "records.jsonl")).Should().BeOfType<AntigravityReadOnly.Prepared.Failed>();
        AntigravityCoderGuard.Prepare(_guard, Handler, _wt, _records).Should().BeOfType<AntigravityReadOnly.Prepared.Ready>();
    }

    // ---------- d4: the folder ----------

    [Fact]
    public void TheAgentFile_IsTheMeasuredBytes_AndTheHooksFileIsTheMeasuredShape()
    {
        File.ReadAllText(Path.Combine(_agents, "agents", "coai-coder.md")).Should().Be(
            "---\nname: coai-coder\ndescription: coai coder\ntools:\n  - view_file\n  - write_to_file\n  - replace_file_content\n  - multi_replace_file_content\nmainAgent: true\nsubagent: false\ncommandExecutionPolicy: \"off\"\n---\nDo what the person asks with the tools you have.\n");
        var hook = JsonDocument.Parse(File.ReadAllText(Path.Combine(_agents, "hooks.json"))).RootElement.EnumerateObject().Single().Value.GetProperty("PreToolUse").EnumerateArray().Single();
        hook.GetProperty("matcher").GetString().Should().Be("*");
        var command = hook.GetProperty("hooks").EnumerateArray().Single();
        (command.GetProperty("type").GetString(), command.GetProperty("timeout").GetInt32()).Should().Be(("command", 10));
        command.GetProperty("command").GetString().Should().Be(OperatingSystem.IsWindows() ? @".\coai-hook.cmd" : "sh ./coai-hook.sh");
        File.ReadAllText(Path.Combine(_agents, "coai-hook.sh")).Should().Be($"#!/bin/sh\nexec \"{RoleJobWorld.FakeCli}\" --agy-hook\n");
    }

    [Fact]
    public void BeforeEveryLaunch_TheFolderIsWrittenAndReadBack_WhateverWasChangedOrAddedInIt()
    {
        File.WriteAllText(Path.Combine(_agents, "hooks.json"), "{}");
        File.WriteAllText(Path.Combine(_agents, "agents", "coai-coder.md"), "changed");
        File.WriteAllText(Path.Combine(_guard, "stray.txt"), "x");
        File.WriteAllText(Path.Combine(_agents, "agents", "second.md"), "x");
        File.Delete(Path.Combine(_agents, "coai-hook.sh"));
        File.CreateSymbolicLink(Path.Combine(_agents, "coai-hook.sh"), Path.Combine(_outside, "secret.txt"));

        AntigravityCoderGuard.Prepare(_guard, Handler, _wt, _records).Should().BeOfType<AntigravityReadOnly.Prepared.Ready>();

        AntigravityCoderGuard.ReadBack(_guard, AntigravityCoderGuard.Files(Handler, _wt, _records)).Should().BeEmpty();
        Directory.EnumerateFileSystemEntries(_guard, "*", SearchOption.AllDirectories).Select(entry => Path.GetRelativePath(_guard, entry)).Order(StringComparer.Ordinal).Should().Equal(
            ".agents", Path.Combine(".agents", "agents"), Path.Combine(".agents", "agents", "coai-coder.md"), Path.Combine(".agents", "coai-guard.json"), Path.Combine(".agents", "coai-hook.cmd"), Path.Combine(".agents", "coai-hook.sh"), Path.Combine(".agents", "hooks.json"));
        File.ReadAllText(Path.Combine(_outside, "secret.txt")).Should().Be("s\n", "a link left where a file belongs is replaced, never written through");
        if (!OperatingSystem.IsWindows())
        {
            File.GetUnixFileMode(_guard).Should().Be(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            File.GetUnixFileMode(_agents).Should().Be(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    [Fact]
    public void TheReadOnlyAgentHookAndFolder_AreWhatTheyWere()
    {
        // The consultants' block is not touched by the coder's: its agent still lists the one reader, and its hook is the one the read-only tests hold.
        AntigravityReadOnly.AgentFile.Should().Contain("name: coai-reader").And.Contain("  - view_file\n").And.NotContain("write_to_file");
        AntigravityReadOnly.HooksFile(windows: false).Should().Contain("coai-read-only").And.NotContain("coai-coder-guard");
    }

    // ---------- the real hook command, as agy runs it ----------

    [Theory]
    [InlineData("", "deny")]
    [InlineData("text that is not json", "deny")]
    [InlineData("PAYLOAD-ALLOW", "allow")]
    [InlineData("PAYLOAD-NOPATH", "deny")]
    public async Task TheHooksJsonCommand_RunFromItsFolder_AnswersEveryInputExplicitly(string input, string expected)
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "the hook command is sh on Unix");
        var stdin = input switch
        {
            "PAYLOAD-ALLOW" => Payload("write_file", Path.Combine(_wt, "src", "a.txt")),
            "PAYLOAD-NOPATH" => Payload("write_file", null),
            _ => input,
        };

        var result = await new ProcessLauncher().RunAsync(new ProcessRequest("sh", ["./coai-hook.sh"], _agents) { StdIn = stdin, Timeout = TimeSpan.FromSeconds(30) }, TestContext.Current.CancellationToken);

        result.ExitCode.Should().Be(0);
        Verdict(result.StdOut).Should().Be(expected);
    }

    [Fact]
    public async Task TheHooksJsonCommand_WithAClosedStandardOutput_IsANonZeroExit()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "the hook command is sh on Unix");

        var result = await new ProcessLauncher().RunAsync(new ProcessRequest("sh", ["-c", "./coai-hook.sh >&-"], _agents) { StdIn = Payload("write_file", Path.Combine(_wt, "src", "a.txt")), Timeout = TimeSpan.FromSeconds(30) }, TestContext.Current.CancellationToken);

        result.ExitCode.Should().NotBe(0, "a decision that cannot be written must not look like exit 0 with no output");
    }

    [Theory]
    [InlineData("", "deny")]
    [InlineData("PAYLOAD-ALLOW", "allow")]
    public async Task TheProductBinary_IsTheHandler_AndNeedsNoNewMode(string input, string expected)
    {
        // d5: the handler stays --agy-hook of the product's own binary; no mode was added, so the list of one-shot modes in .agents/PROJECT.md is the same.
        var dll = Path.Combine(AppContext.BaseDirectory, "coai-mcp.dll");
        Assert.SkipUnless(File.Exists(dll), "the product binary is beside the tests only in a framework-dependent build");
        var stdin = input == "PAYLOAD-ALLOW" ? Payload("write_file", Path.Combine(_wt, "src", "a.txt")) : input;

        var result = await new ProcessLauncher().RunAsync(new ProcessRequest("dotnet", [dll, "--agy-hook"], _agents) { StdIn = stdin, Timeout = TimeSpan.FromSeconds(60) }, TestContext.Current.CancellationToken);

        result.ExitCode.Should().Be(0);
        Verdict(result.StdOut).Should().Be(expected);
    }

    private sealed class BlockedReader : TextReader
    {
        public override string ReadToEnd()
        {
            Thread.Sleep(TimeSpan.FromSeconds(20));

            return string.Empty;
        }
    }

    private sealed class ThrowingReader : TextReader
    {
        public override string ReadToEnd() => throw new IOException("the pipe broke");
    }

    private sealed class ClosedWriter : TextWriter
    {
        public override System.Text.Encoding Encoding => System.Text.Encoding.UTF8;

        public override void Write(string? value) => throw new IOException("closed");

        public override void Flush() => throw new IOException("closed");
    }
}
