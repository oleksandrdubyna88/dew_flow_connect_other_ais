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
}
