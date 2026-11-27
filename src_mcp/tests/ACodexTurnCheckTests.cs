using System.Text.Json;
using CoaiMcp.Core.Toolchain;
using CoaiMcp.Runners.Consultation;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Toolchain;
using CoaiMcp.Runners.Worktrees;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// Epoch 3 E3 s5 t4 d3 (V26; ids CX-S1, CX-T4, CX-T5 of the judgement, section 13.11 for the exact rule): the check of a codex turn's OWN stream, played case by case with no unit and no process. The rule is closed: what it does not know ends the job, so each arm has a case that passes and cases that end it,
/// and a case for the stream that holds nothing wrong, so a check that ends everything is seen too.
/// </summary>
public sealed class ACodexTurnCheckTests
{
    private const string Thread = "01a217ad-59bd-7543-907c-6b343bd876d3";

    private const string Other = "01a217af-0f53-7310-a2e4-641468fecbb1";

    private static string Event(string type, string rest = "") => "{\"type\":\"" + type + "\"" + rest + "}";

    private static string Started(string thread = Thread) => Event("thread.started", $",\"thread_id\":\"{thread}\"");

    private static string Item(string kind, string item, string type = "item.completed") => Event(type, $",\"item\":{{\"id\":\"item_0\",\"type\":\"{kind}\"{item}}}");

    private static string Mcp(string server, string tool, string error = "null", string result = "null", string type = "item.completed") =>
        Event(type, $",\"item\":{{\"id\":\"item_1\",\"type\":\"mcp_tool_call\",\"server\":\"{server}\",\"tool\":\"{tool}\",\"arguments\":{{}},\"result\":{result},\"error\":{error}}}");

    private static string EmptyResources => "{\"content\":[{\"type\":\"text\",\"text\":\"{\\\"resources\\\":[]}\"}],\"structured_content\":null}";

    private static string EmptyTemplates => "{\"content\":[{\"type\":\"text\",\"text\":\"{\\\"resourceTemplates\\\":[]}\"}],\"structured_content\":null}";

    private sealed class World : IDisposable
    {
        private readonly TempDir _dir = TempDir.For("cxt-");

        public World()
        {
            Job = new BoundaryJob("job-x", new CoderTree(Path.Combine(_dir.Path, "wt", "coai-coder-job-x"), Path.Combine(_dir.Path, "main"), string.Empty), Path.Combine(_dir.Path, "jobs", "job-x"), NetworkChoice.None("t"), NetworkChoice.None("t"), 100);
            Directory.CreateDirectory(CodexCoderVendor.CodexHome(Job));
        }

        public BoundaryJob Job { get; }

        /// <summary>A rollout file of <paramref name="thread"/> with these lines, in the place the CLI makes it.</summary>
        public void Rollout(string thread, params string[] lines)
        {
            var folder = Path.Combine(CodexCoderVendor.CodexHome(Job), "sessions", "2026", "11", "26");
            Directory.CreateDirectory(folder);
            File.WriteAllLines(Path.Combine(folder, $"rollout-2026-11-26T10-00-00-{thread}.jsonl"), lines);
        }

        public static string Call(string name) => "{\"timestamp\":\"2026-11-26T10:00:00Z\",\"type\":\"response_item\",\"payload\":{\"type\":\"function_call\",\"name\":\"" + name + "\",\"arguments\":\"{}\"}}";

        public void Dispose() => _dir.Dispose();
    }

    private static ProcessResult Stream(params string[] lines) => new(0, string.Join('\n', lines) + "\n", string.Empty, false);

    private static CoderTurnVerdict Check(World world, ProcessResult process, string expected = "") => CodexTurnCheck.Check(process, expected, world.Job);

    private static string Sentence(CoderTurnVerdict verdict) => verdict switch { CoderTurnVerdict.Boundary b => b.Sentence, CoderTurnVerdict.Crash c => c.Sentence, _ => string.Empty };

    private static CoderTurnVerdict Fine(params string[] items)
    {
        using var world = new World();
        world.Rollout(Thread, World.Call("exec"));

        return Check(world, Stream([Started(), Event("turn.started"), .. items, Event("turn.completed")]));
    }

    // ---------- the items that pass ----------

    [Fact]
    public void AStreamOfTheMeasuredItemTypes_AndTheTwoListToolsWithEmptyAnswers_PassesInTheStartedAndTheCompletedForm()
    {
        var verdict = Fine(
            Item("agent_message", ",\"text\":\"done\""),
            Item("command_execution", ",\"command\":\"ls\",\"aggregated_output\":\"\",\"exit_code\":0,\"status\":\"completed\""),
            Item("command_execution", ",\"command\":\"ls\",\"status\":\"in_progress\"", "item.started"),
            Item("file_change", ",\"changes\":[],\"status\":\"completed\""),
            Item("error", ",\"message\":\"Falling back from WebSockets to HTTPS transport.\""),
            Mcp("codex", "list_mcp_resources", result: EmptyResources),
            Mcp("codex", "list_mcp_resources", type: "item.started"),
            Mcp("codex", "list_mcp_resource_templates", result: EmptyTemplates));

        verdict.Should().BeOfType<CoderTurnVerdict.Fine>(Sentence(verdict));
    }

    // ---------- the items that end the job ----------

    public static TheoryData<string, string, string> Ending => new()
    {
        { "another server", Mcp("other", "list_mcp_resources", result: EmptyResources), "another server" },
        { "another tool", Mcp("codex", "read_mcp_resource", result: EmptyResources), "another tool" },
        { "a read_mcp_resource that succeeded", Mcp("codex", "read_mcp_resource", result: "{\"content\":[{\"type\":\"text\",\"text\":\"secret\"}]}"), "another tool" },
        { "an error", Mcp("codex", "list_mcp_resources", error: "{\"message\":\"unknown MCP server\"}"), "an error" },
        { "a list that is not empty", Mcp("codex", "list_mcp_resources", result: "{\"content\":[{\"type\":\"text\",\"text\":\"{\\\"resources\\\":[{\\\"uri\\\":\\\"x\\\"}]}\"}]}"), "not empty" },
        { "templates that are not empty", Mcp("codex", "list_mcp_resource_templates", result: "{\"content\":[{\"type\":\"text\",\"text\":\"{\\\"resourceTemplates\\\":[{\\\"n\\\":1}]}\"}]}"), "not empty" },
        { "two blocks", Mcp("codex", "list_mcp_resources", result: "{\"content\":[{\"type\":\"text\",\"text\":\"{\\\"resources\\\":[]}\"},{\"type\":\"text\",\"text\":\"x\"}]}"), "not empty" },
        { "an answer that is not JSON", Mcp("codex", "list_mcp_resources", result: "{\"content\":[{\"type\":\"text\",\"text\":\"nothing\"}]}"), "not empty" },
        { "an empty list with another member", Mcp("codex", "list_mcp_resources", result: "{\"content\":[{\"type\":\"text\",\"text\":\"{\\\"resources\\\":[],\\\"more\\\":1}\"}]}"), "not empty" },
        { "a web search, started", Item("web_search", ",\"query\":\"\"", "item.started"), "web_search" },
        { "a web search, completed", Item("web_search", ",\"query\":\"\",\"action\":{\"type\":\"other\"},\"results\":[]"), "web_search" },
        { "an agent call", Item("collab_tool_call", ",\"tool\":\"wait\",\"receiver_thread_ids\":[]"), "collab_tool_call" },
        { "an item type nobody measured", Item("reasoning", ",\"text\":\"thinking\""), "reasoning" },
        { "an item with no type", "{\"type\":\"item.completed\",\"item\":{\"id\":\"item_0\"}}", "''" },
        { "an item type that is a long text", Item(new string('x', 80), string.Empty), "(unreadable)" },
    };

    [Theory]
    [MemberData(nameof(Ending))]
    public void ABadItem_EndsTheJobAtTheBoundary_AndTheReasonNamesIt(string name, string line, string word)
    {
        var verdict = Fine(line);

        var wrong = verdict.Should().BeOfType<CoderTurnVerdict.Boundary>($"{name} ends the job").Subject;
        wrong.Sentence.Should().Contain(word);
        wrong.Sentence.Should().NotContain("secret", "a row of the verdict is never a word the program printed");
    }

    [Fact]
    public void AnItemTypeThatNobodyMeasured_IsNamedByItsType()
    {
        Sentence(Fine(Item("reasoning", string.Empty))).Should().Contain("'reasoning'");
    }

    [Fact]
    public void AStreamLineThatLooksLikeAnEventAndCannotBeRead_EndsTheJob_ButALogLineIsNothing()
    {
        using var world = new World();
        world.Rollout(Thread, World.Call("exec"));

        Check(world, Stream("a log line", Started(), "{\"type\":\"item.completed\",\"item\":{\"type\":\"agent_message\"")).Should().BeOfType<CoderTurnVerdict.Boundary>();
        Check(world, Stream("a log line that is not an event", Started(), Item("agent_message", string.Empty))).Should().BeOfType<CoderTurnVerdict.Fine>();
    }

    // ---------- CX-S1: the thread ----------

    [Fact]
    public void AThreadThatIsNotTheHandles_IsACrash_AndTheHandlesOwnThreadPasses()
    {
        using var world = new World();

        Check(world, Stream(Started(Other), Item("agent_message", string.Empty)), Thread).Should().BeOfType<CoderTurnVerdict.Crash>();
        world.Rollout(Thread, World.Call("exec"));
        Check(world, Stream(Started(), Item("agent_message", string.Empty)), Thread).Should().BeOfType<CoderTurnVerdict.Fine>();
    }

    [Fact]
    public void AStreamThatNamesNoThread_IsACrash_OnTurnZeroToo()
    {
        using var world = new World();

        Check(world, Stream(Event("turn.started"), Item("agent_message", string.Empty))).Should().BeOfType<CoderTurnVerdict.Crash>();
    }

    // ---------- CX-T5: the rollout file ----------

    [Fact]
    public void NoRolloutFileOfTheThread_AFileThatCannotBeRead_AFileOfAnotherThread_AndACallNamedCollaboration_EachEndTheJob()
    {
        var stream = Stream(Started(), Item("agent_message", string.Empty));

        using (var none = new World())
        {
            Sentence(Check(none, stream)).Should().Contain("CX-T5");
            Check(none, stream).Should().BeOfType<CoderTurnVerdict.Boundary>("no such file ends the job: a format change refuses a job, it never passes one");
        }

        using (var unreadable = new World())
        {
            unreadable.Rollout(Thread, "this is not json");
            Check(unreadable, stream).Should().BeOfType<CoderTurnVerdict.Boundary>("a file that cannot be read ends the job");
        }

        using (var another = new World())
        {
            another.Rollout(Thread, World.Call("exec"));
            another.Rollout(Other, World.Call("exec"));
            Check(another, stream).Should().BeOfType<CoderTurnVerdict.Boundary>("a file of another thread is a child of the session");
        }

        using (var agent = new World())
        {
            agent.Rollout(Thread, World.Call("exec"), World.Call("collaboration.spawn_agent"));
            Check(agent, stream).Should().BeOfType<CoderTurnVerdict.Boundary>("a call named collaboration.* is an agent call, which the stream does not show");
        }

        using (var fine = new World())
        {
            fine.Rollout(Thread, World.Call("exec"), World.Call("exec"));
            Check(fine, stream).Should().BeOfType<CoderTurnVerdict.Fine>("a control: the rollout of the thread, with exec calls only");
        }
    }

    [Fact]
    public void WithNoJobToFindItsHome_TheRolloutCannotBeRead_AndThatEndsTheJob()
    {
        CodexTurnCheck.Check(Stream(Started(), Item("agent_message", string.Empty)), string.Empty, null).Should().BeOfType<CoderTurnVerdict.Boundary>();
    }

    [Fact]
    public void ARealRolloutLineIsNotRefusedForTheTextItCarries_OnlyForTheNameOfACall()
    {
        using var world = new World();
        world.Rollout(Thread, "{\"timestamp\":\"t\",\"type\":\"response_item\",\"payload\":{\"type\":\"message\",\"role\":\"assistant\",\"content\":[{\"type\":\"output_text\",\"text\":\"the tool collaboration.spawn_agent is off\"}]}}");

        Check(world, Stream(Started(), Item("agent_message", string.Empty))).Should().BeOfType<CoderTurnVerdict.Fine>("a message that quotes the name is not a call of it");
    }

    // ---------- the seam ----------

    [Fact]
    public void ClaudesCheck_StillSaysNothing_AndCodexsGoesThroughTheSeamWithTheJob()
    {
        using var world = new World();

        ((ICoderVendor)ClaudeCoderVendor.Instance).CheckTurn(Stream(Item("web_search", string.Empty)), string.Empty, world.Job).Should().BeOfType<CoderTurnVerdict.Fine>("claude's tools are the five of its flags");
        ((ICoderVendor)CodexCoderVendor.Instance).CheckTurn(Stream(Started(), Item("web_search", string.Empty)), string.Empty, world.Job).Should().BeOfType<CoderTurnVerdict.Boundary>();
    }

    // ---------- M16 of pass 6: a turn that ended badly is a CUT stream ----------

    private static ProcessResult Ended(int exit, bool timedOut, bool cancelled, params string[] lines) => new(exit, string.Join('\n', lines) + "\n", string.Empty, timedOut, cancelled);

    public static TheoryData<string> BadEnds => ["exit 1", "deadline", "cancel"];

    private static ProcessResult BadEnd(string how, params string[] lines) => how switch
    {
        "exit 1" => Ended(1, false, false, lines),
        "deadline" => Ended(0, true, false, lines),
        _ => Ended(0, false, true, lines),
    };

    [Theory]
    [MemberData(nameof(BadEnds))]
    public void M16_ABadItem_IsABoundaryOnABadEndToo_EvenInAStreamThatNamesNoThread_SoACrashNeverHidesIt(string how)
    {
        using var world = new World();

        CodexTurnCheck.Check(BadEnd(how, Event("turn.started"), Item("web_search", ",\"query\":\"\"")), string.Empty, world.Job).Should().BeOfType<CoderTurnVerdict.Boundary>("a violation the stream proves alone is a boundary whatever the end was");
        CodexTurnCheck.Check(BadEnd(how, Started(Other), Item("web_search", string.Empty)), Thread, world.Job).Should().BeOfType<CoderTurnVerdict.Boundary>("and not the crash of a thread that is not the handle's");
        CodexTurnCheck.Check(Stream(Event("turn.started"), Item("web_search", string.Empty)), string.Empty, world.Job).Should().BeOfType<CoderTurnVerdict.Boundary>("the same on a clean end: items first, then the thread");
    }

    [Theory]
    [MemberData(nameof(BadEnds))]
    public void M16_ARolloutOfAnotherThread_OrAnAgentCall_IsABoundaryOnABadEnd(string how)
    {
        var stream = BadEnd(how, Started(), Item("agent_message", string.Empty));
        using (var another = new World())
        {
            another.Rollout(Thread, World.Call("exec"));
            another.Rollout(Other, World.Call("exec"));
            CodexTurnCheck.Check(stream, string.Empty, another.Job).Should().BeOfType<CoderTurnVerdict.Boundary>("a second thread is a child of the session, proved by the files alone");
        }

        using (var agent = new World())
        {
            agent.Rollout(Thread, World.Call("exec"), World.Call("collaboration.spawn_agent"));
            CodexTurnCheck.Check(stream, string.Empty, agent.Job).Should().BeOfType<CoderTurnVerdict.Boundary>();
        }

        using (var agentWithNoThread = new World())
        {
            agentWithNoThread.Rollout(Thread, World.Call("collaboration.spawn_agent"));
            CodexTurnCheck.Check(BadEnd(how, Event("turn.started")), string.Empty, agentWithNoThread.Job).Should().BeOfType<CoderTurnVerdict.Boundary>("an agent call is proved by the file with no thread named at all");
        }
    }

    [Theory]
    [MemberData(nameof(BadEnds))]
    public void M16_WhatACutExplains_IsNotABoundary_NoThread_NoRollout_ACutLastLine_AUnreadableRollout_ATruncatedStream_SoTheLaunchsOwnStopIsRead(string how)
    {
        using var world = new World();
        var calm = new[] { Started(), Item("agent_message", string.Empty) };

        CodexTurnCheck.Check(BadEnd(how, Event("turn.started")), string.Empty, world.Job).Should().BeOfType<CoderTurnVerdict.Fine>("no thread on a bad end is the crash, the limit or the lost sign-in of the launch's own stop");
        CodexTurnCheck.Check(BadEnd(how, calm), string.Empty, world.Job).Should().BeOfType<CoderTurnVerdict.Fine>("no rollout of the thread on a bad end is not proved wrong");
        CodexTurnCheck.Check(BadEnd(how, [.. calm, "{\"type\":\"item.completed\",\"item\":{\"type\":\"agent_mes"]), string.Empty, world.Job).Should().BeOfType<CoderTurnVerdict.Fine>("a last line cut by the deadline");
        CodexTurnCheck.Check(BadEnd(how, calm) with { Truncated = true }, string.Empty, world.Job).Should().BeOfType<CoderTurnVerdict.Fine>("a stream cut at its ceiling");
        world.Rollout(Thread, "this is not json");
        CodexTurnCheck.Check(BadEnd(how, calm), string.Empty, world.Job).Should().BeOfType<CoderTurnVerdict.Fine>("a rollout cut in the middle of a line");
        CodexTurnCheck.Check(BadEnd(how, calm), string.Empty, null).Should().BeOfType<CoderTurnVerdict.Fine>("no job to read: not proved");
        // The controls on a clean end: every one of these ends the job.
        CodexTurnCheck.Check(Stream(Event("turn.started")), string.Empty, world.Job).Should().BeOfType<CoderTurnVerdict.Crash>();
        CodexTurnCheck.Check(Stream(calm), string.Empty, world.Job).Should().BeOfType<CoderTurnVerdict.Boundary>("a rollout that cannot be read ends a clean turn");
        CodexTurnCheck.Check(Stream(calm) with { Truncated = true }, string.Empty, world.Job).Should().BeOfType<CoderTurnVerdict.Boundary>("a clean turn whose stream was cut at its ceiling cannot be checked");
    }

    // ---------- S43 to S45 ----------

    [Fact]
    public void S43_ACompletedListCallWithNoResult_AndAnItemEventWithNoItem_EndTheJob_AStartedOneWithNoResultDoesNot()
    {
        Fine(Mcp("codex", "list_mcp_resources", type: "item.started")).Should().BeOfType<CoderTurnVerdict.Fine>("a started call has no answer yet");
        Fine(Mcp("codex", "list_mcp_resources")).Should().BeOfType<CoderTurnVerdict.Boundary>("a completed one must carry one text block");
        Fine("{\"type\":\"item.completed\"}").Should().BeOfType<CoderTurnVerdict.Boundary>("an item event with no item cannot be told from a tool outside the list");
    }

    [Fact]
    public void S44_AFileThatIsNotARollout_ALinkAsTheSessionsFolder_AndTheOtherNamesOfAnAgentCall_EndTheJob()
    {
        var stream = Stream(Started(), Item("agent_message", string.Empty));
        using (var stray = new World())
        {
            stray.Rollout(Thread, World.Call("exec"));
            File.WriteAllText(Path.Combine(CodexCoderVendor.CodexHome(stray.Job), "sessions", "notes.txt"), "x\n");
            CodexTurnCheck.Check(stream, string.Empty, stray.Job).Should().BeOfType<CoderTurnVerdict.Boundary>("a file that is not a rollout is not what the CLI makes");
        }

        using (var linked = new World())
        {
            var target = Path.Combine(CodexCoderVendor.CodexHome(linked.Job), "elsewhere");
            Directory.CreateDirectory(target);
            File.CreateSymbolicLink(Path.Combine(CodexCoderVendor.CodexHome(linked.Job), "sessions"), target);
            CodexTurnCheck.Check(stream, string.Empty, linked.Job).Should().BeOfType<CoderTurnVerdict.Boundary>("a link is not the sessions folder; it is not followed");
        }

        foreach (var line in new[]
        {
            "{\"type\":\"response_item\",\"payload\":{\"type\":\"function_call\",\"namespace\":\"collaboration\",\"name\":\"spawn_agent\"}}",
            "{\"type\":\"response_item\",\"payload\":{\"type\":\"function_call\",\"name\":\"spawn_agent\",\"arguments\":\"{}\"}}",
        })
        {
            using var world = new World();
            world.Rollout(Thread, World.Call("exec"), line);
            CodexTurnCheck.Check(stream, string.Empty, world.Job).Should().BeOfType<CoderTurnVerdict.Boundary>(line);
        }
    }

    [Fact]
    public void M16_AFreshLaunch_ReadsTheRolloutsBeforeItDeletesThem_AnAgentCallKeepsTheFolderAndRefusesTheLaunch()
    {
        using var world = new World();
        world.Rollout(Other, World.Call("collaboration.spawn_agent"));
        var sessions = Path.Combine(CodexCoderVendor.CodexHome(world.Job), "sessions");

        CodexSessions.Clear(CodexCoderVendor.CodexHome(world.Job)).Should().Contain("agent call").And.Contain("was not launched");
        Directory.Exists(sessions).Should().BeTrue("the record of a child is never deleted unread");

        File.WriteAllText(Directory.EnumerateFiles(sessions, "*", SearchOption.AllDirectories).Single(), World.Call("exec") + "\n");
        CodexSessions.Clear(CodexCoderVendor.CodexHome(world.Job)).Should().BeEmpty();
        Directory.Exists(sessions).Should().BeFalse("a folder with no agent call is removed, as before");
    }
}
