using System.Text.Json;
using CoaiMcp.Runners.Context;
using CoaiMcp.Store;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// E3.S4 of <c>todo/PLAN_a_round_records_who_wrote_the_code_and_how_much_E3.md</c>, tests 1–18, 44, 45, 47 and 55: every message, token,
/// launch and write is counted exactly once, and what was requested is kept apart from what was observed.
/// </summary>
/// <remarks>
/// Every expectation is read from the fixture's own lines (<see cref="TranscriptTruth"/>) or from its <c>rounds.json</c>; none is a typed
/// number, a real date or a clock reading. A fixture is a manifest NAME, materialised into a <c>testsToDelete-</c> folder that the test
/// removes. The mutation helpers of <see cref="CallerTranscriptFixture"/> are the only way a test alters a fixture.
/// </remarks>
public sealed class TheCallerTranscriptCountsEachThingOnceTests
{
    private const string Prefix = "testsToDelete-coai-counts-";
    private const string C02Agent = "303b2866f0b3022b2";
    private const string C07Depth1 = "207bc560f3fb5a452";
    private const string C07Depth2 = "2553a2f49d265a4e3";
    private const string C14Agent = "2edf3ddec8861a853";

    private static ObservedAgent AgentOf(ObservedSession session, string key) => session.Agent(key);

    private static IEnumerable<ObservedMessage> MessagesOf(ObservedSession session, string key) => session.Messages.Where(m => m.AgentKey == key);

    [Fact]
    public void A_message_written_as_several_lines_is_one_message_with_its_maximum_usage()
    {
        using var dir = TempDir.For(Prefix);
        var fixture = CallerTranscriptFixture.Materialise("message-as-several-lines", dir);

        var session = fixture.Read();

        var byId = TranscriptTruth.Assistants(fixture).GroupBy(l => l.MessageId).ToList();
        session.Messages.Should().HaveCount(fixture.Case.Counts.DistinctMessageIds);
        session.Messages.Select(m => m.MessageId).Should().BeEquivalentTo(byId.Select(g => g.Key));
        byId.Should().Contain(g => g.Count() > 1, "the case exists for a message written as several lines");
        foreach (var message in session.Messages)
        {
            message.Tokens.Should().Be(TranscriptTruth.MaxOf(byId.Single(g => g.Key == message.MessageId)), message.MessageId);
        }
    }

    [Fact]
    public void Summing_every_line_of_a_message_counts_its_output_again_and_that_is_the_point()
    {
        using var dir = TempDir.For(Prefix);
        var fixture = CallerTranscriptFixture.Materialise("message-as-several-lines", dir);

        var session = fixture.Read();

        var repeated = TranscriptTruth.Assistants(fixture).GroupBy(l => l.MessageId)
            .Where(g => g.Count() > 1 && g.Max(l => l.Tokens.Output) > 0).ToList();
        repeated.Should().NotBeEmpty("an id with several lines and a non-zero output is what this test is about");
        foreach (var lines in repeated)
        {
            var output = session.Message(lines.Key).Tokens.Output;
            output.Should().Be(lines.Max(l => l.Tokens.Output));
            output.Should().BeLessThan(lines.Sum(l => l.Tokens.Output), "summing the lines would count the same output again");
        }
    }

    [Fact]
    public void A_message_whose_output_grows_across_its_lines_is_counted_by_its_largest_line()
    {
        using var dir = TempDir.For(Prefix);
        var fixture = CallerTranscriptFixture.Materialise("message-as-several-lines", dir);
        fixture.GrowOutputAcross("8d01ee");

        var session = fixture.Read();

        var lines = TranscriptTruth.Assistants(fixture).Where(l => l.MessageId.EndsWith("8d01ee", StringComparison.Ordinal)).ToList();
        lines.Should().HaveCountGreaterThan(1);
        lines[^1].Tokens.Output.Should().BeGreaterThan(lines[0].Tokens.Output, "the mutation made the output grow");
        session.Message(lines[0].MessageId).Tokens.Output.Should().Be(lines[^1].Tokens.Output);
    }

    [Fact]
    public void The_same_message_id_in_two_files_is_one_message_and_the_first_file_wins()
    {
        using var dir = TempDir.For(Prefix);
        var fixture = CallerTranscriptFixture.Materialise("message-as-several-lines", dir);
        fixture.CopyLineInto(1, C02Agent);

        var session = fixture.Read();

        var copied = TranscriptTruth.Assistants(fixture).Where(l => l.FileKey == "main").Single();
        var message = session.Message(copied.MessageId);
        message.AgentKey.Should().Be("main", "main is read first");
        message.Tokens.Should().Be(TranscriptTruth.MaxOf(TranscriptTruth.Assistants(fixture).Where(l => l.MessageId == copied.MessageId)));
        session.State.Render().Should().Be("partial: inconsistent transcript");
    }

    [Fact]
    public void A_resumed_agent_is_one_launch_with_later_messages()
    {
        using var dir = TempDir.For(Prefix);
        var fixture = CallerTranscriptFixture.Materialise("resumed-agent", dir);

        var session = fixture.Read();

        session.Agents.Count(a => a.Key == C14Agent).Should().Be(1);
        AgentOf(session, C14Agent).LaunchedUtc.Should().Be(fixture.StampOf(TranscriptFile.Agent(C14Agent), 1));
        var round = fixture.Rounds.GateSessions.Single().Rounds.Single();
        var mine = MessagesOf(session, C14Agent).ToList();
        mine.Min(m => m.Utc).Should().BeBefore(round.StartedUtc);
        mine.Max(m => m.Utc).Should().BeAfter(round.CompletedUtc, "the messages span the gap the resume left in rounds.json");
    }

    [Fact]
    public void A_background_agent_running_across_a_round_start_is_one_launch()
    {
        using var dir = TempDir.For(Prefix);
        var fixture = CallerTranscriptFixture.Materialise("background-agent-across-round-start", dir);

        var session = fixture.Read();

        session.Agents.Where(a => a.Key != "main").Should().ContainSingle();
        var start = fixture.Rounds.GateSessions.Single().Rounds.Min(r => r.StartedUtc);
        session.Agents.Single(a => a.Key != "main").LaunchedUtc.Should().BeBefore(start);
        session.Messages.Should().Contain(m => m.Utc < start).And.Contain(m => m.Utc > start);
    }

    [Fact]
    public void Requested_and_observed_are_two_fields_and_mains_1m_suffix_shows_it()
    {
        using var dir = TempDir.For(Prefix);
        var fixture = CallerTranscriptFixture.Materialise("message-as-several-lines", dir);

        var main = fixture.Read().Message("afddac");

        main.Requested.Should().Be("claude-opus-5-5[1m]");
        main.Model.Should().Be("claude-opus-5-5");
        main.Model.Should().NotBe(main.Requested);
    }

    [Fact]
    public void A_subagents_requested_model_is_the_metas_alias()
    {
        using var dir = TempDir.For(Prefix);
        var fixture = CallerTranscriptFixture.Materialise("nested-agents", dir);

        var session = fixture.Read();

        MessagesOf(session, C07Depth1).Should().OnlyContain(m => m.Requested == "fable" && m.Model == "claude-fable-5-1");
        MessagesOf(session, C07Depth1).Should().NotBeEmpty();
        MessagesOf(session, C07Depth2).Should().OnlyContain(m => m.Requested == "claude-sonnet-5-5", "its meta names no model, so its lines' requestedModel is used");
        MessagesOf(session, C07Depth2).Should().NotBeEmpty();
        AgentOf(session, C07Depth1).AgentType.Should().Be("general-purpose");
        AgentOf(session, C07Depth1).RequestedAlias.Should().Be("fable");
    }

    [Fact]
    public void A_model_switch_inside_one_agent_yields_messages_of_both_models()
    {
        using var dir = TempDir.For(Prefix);
        var fixture = CallerTranscriptFixture.Materialise("model-switch-in-window", dir);

        var session = fixture.Read();

        session.Messages.Select(m => m.Model).Distinct().Should().HaveCountGreaterThan(1);
        session.Messages.Should().OnlyContain(m => m.Requested == string.Empty, "this Claude Code version writes no requestedModel");
        session.Messages.Select(m => m.AgentKey).Distinct().Should().Equal("main");
    }

    [Fact]
    public void No_effort_on_a_line_is_unknown_not_high()
    {
        using var dir = TempDir.For(Prefix);
        var fixture = CallerTranscriptFixture.Materialise("message-as-several-lines", dir);
        var agent = TranscriptFile.Agent(C02Agent);
        var firsts = TranscriptTruth.Assistants(fixture).Where(l => l.FileKey == C02Agent).GroupBy(l => l.MessageId).Take(2).Select(g => g.First()).ToList();
        firsts.ForEach(first => fixture.SetEffort(agent, first.Line, null));

        var session = fixture.Read();

        session.State.Kind.Should().Be(ObservationKind.Observed, "an absent value never differs from a present one on the id's later lines");
        var stripped = firsts.Select(f => f.MessageId).ToList();
        session.Messages.Where(m => stripped.Contains(m.MessageId)).Should().OnlyContain(m => m.Effort == "unknown").And.HaveCount(2);
        session.Messages.Where(m => !stripped.Contains(m.MessageId)).Should().OnlyContain(m => m.Effort == "high");
    }

    [Fact]
    public void Effort_is_never_carried_from_the_previous_line()
    {
        using var dir = TempDir.For(Prefix);
        var fixture = CallerTranscriptFixture.Materialise("message-as-several-lines", dir);
        var agent = TranscriptFile.Agent(C02Agent);
        var ids = TranscriptTruth.Assistants(fixture).Where(l => l.FileKey == C02Agent).ToList();
        var second = ids.GroupBy(l => l.MessageId).ElementAt(1);
        second.ToList().ForEach(line => fixture.SetEffort(agent, line.Line, null));

        var session = fixture.Read();

        session.Message(second.Key).Effort.Should().Be("unknown", "the id before it says high and must not leak forward");
        session.Messages.Where(m => m.MessageId != second.Key).Should().OnlyContain(m => m.Effort == "high");
    }

    [Fact]
    public void A_nested_agent_names_its_parent_and_a_depth_one_agent_names_main()
    {
        using var dir = TempDir.For(Prefix);
        var fixture = CallerTranscriptFixture.Materialise("nested-agents", dir);

        var session = fixture.Read();

        var outer = AgentOf(session, C07Depth1);
        var inner = AgentOf(session, C07Depth2);
        outer.ParentKey.Should().Be("main");
        inner.ParentKey.Should().Be(C07Depth1);
        outer.CalledUtc.Should().NotBeNull();
        inner.CalledUtc.Should().NotBeNull();
        (outer.AgentType, inner.AgentType).Should().Be(("general-purpose", "custom-1"));
    }

    [Fact]
    public void A_write_call_is_one_write_at_its_messages_instant_in_its_checkout()
    {
        using var dir = TempDir.For(Prefix);
        var fixture = CallerTranscriptFixture.Materialise("write-into-submodule-and-scratchpad", dir);
        var main = TranscriptFile.Main;

        var writes = fixture.Read().Writes;

        writes.Should().HaveCount(2);
        writes[0].Target.Should().Be(new WriteTarget(WriteTargetKind.Checkout, RepositoryIdentity.Normalised(Path.Combine(fixture.Root, "repo-a", "p1000"))));
        writes[1].Target.Should().Be(new WriteTarget(WriteTargetKind.OutsideAnyCheckout, string.Empty));
        writes.Select(w => w.Utc).Should().OnlyContain(utc => utc == fixture.StampOf(main, 1), "both blocks belong to the message whose first line is L1, although L2 is a second later");
        writes.Select(w => w.AnsweredUtc).Should().Equal(fixture.StampOf(main, 3), fixture.StampOf(main, 4));
        writes.Select(w => w.Tool).Should().Equal("Edit", "Write");
    }

    [Fact]
    public void A_shell_command_is_never_a_write()
    {
        using var dir = TempDir.For(Prefix);
        var fixture = CallerTranscriptFixture.Materialise("writes-only-elsewhere", dir);

        var writes = fixture.Read().Writes;

        fixture.CallIds(TranscriptFile.Main, "Bash", "PowerShell").Should().NotBeEmpty("the fixture holds a shell call");
        writes.Should().ContainSingle().Which.Tool.Should().Be("Write");
        writes.Should().OnlyContain(w => WriteTools.Names.Contains(w.Tool));
    }

    [Fact]
    public void A_refused_or_unanswered_write_is_not_a_write()
    {
        using var dir = TempDir.For(Prefix);
        var fixture = CallerTranscriptFixture.Materialise("two-repositories-one-session", dir);
        var before = fixture.Read();
        var calls = fixture.CallIds(TranscriptFile.Main, WriteTools.Names.ToArray());
        fixture.SetResultError(TranscriptFile.Main, calls[0]);
        fixture.DeleteResult(TranscriptFile.Main, calls[1]);

        var after = fixture.Read();

        after.Writes.Should().HaveCount(before.Writes.Count - 2);
        (after.Diagnostics.WriteCallsRefused, after.Diagnostics.WriteCallsUnanswered).Should().Be((1, 1));
        after.State.Kind.Should().Be(ObservationKind.Observed);
    }

    [Fact]
    public void A_synthetic_line_is_no_message_and_no_tokens()
    {
        using var dir = TempDir.For(Prefix);
        var fixture = CallerTranscriptFixture.Materialise("message-as-several-lines", dir);
        var lines = TranscriptTruth.Assistants(fixture);
        var whole = lines.Select(l => l.MessageId).Distinct().Count();
        var lastOfTwo = lines.Where(l => l.FileKey == C02Agent).GroupBy(l => l.MessageId).First(g => g.Count() > 1 && g.Last().Tokens.Output > g.First().Tokens.Output).Last();
        fixture.SetModelOnLine(TranscriptFile.Agent(C02Agent), lastOfTwo.Line, "<synthetic>");

        var partlySynthetic = fixture.Read();
        fixture.SetModelOnLine(TranscriptFile.Main, 1, "<synthetic>");
        var mainSynthetic = fixture.Read();

        partlySynthetic.Diagnostics.SyntheticLines.Should().Be(1);
        partlySynthetic.Messages.Should().HaveCount(whole, "the id still has its other line");
        var remaining = TranscriptTruth.Assistants(fixture).Where(l => l.MessageId == lastOfTwo.MessageId).ToList();
        partlySynthetic.Message(lastOfTwo.MessageId).Tokens.Output.Should().BeLessThan(lastOfTwo.Tokens.Output, "the synthetic line's output is in no total");
        remaining.Should().HaveCount(1);
        mainSynthetic.Messages.Should().HaveCount(whole - 1);
        mainSynthetic.Diagnostics.SyntheticLines.Should().Be(2);
    }

    [Theory]
    [InlineData("nested-agents")]
    [InlineData("resumed-agent")]
    public void Reading_the_same_files_twice_gives_equal_sessions_in_a_stable_order(string name)
    {
        using var dir = TempDir.For(Prefix);
        var fixture = CallerTranscriptFixture.Materialise(name, dir);

        var first = fixture.Read();
        var second = fixture.Read();

        second.Should().BeEquivalentTo(first, options => options.WithStrictOrdering());
        first.Agents[0].Key.Should().Be("main");
        first.Agents.Skip(1).Select(a => a.LaunchedUtc ?? a.CalledUtc ?? DateTime.MaxValue).Should().BeInAscendingOrder();
        first.Messages.Select(m => (m.Utc, m.MessageId)).Should().BeInAscendingOrder();
    }

    [Fact]
    public void Rewriting_every_cwd_changes_nothing()
    {
        using var dir = TempDir.For(Prefix);
        var fixture = CallerTranscriptFixture.Materialise("resumed-agent", dir);
        var before = fixture.Read();
        before.Messages.Should().NotBeEmpty("a read of nothing would equal a read of nothing");
        fixture.RewriteEveryCwd("D:/fixture/repo-a");

        var after = fixture.Read();

        after.Should().BeEquivalentTo(before, options => options.WithStrictOrdering()
            .Excluding(s => s.Diagnostics.BytesScanned).Excluding(s => s.Diagnostics.BytesInSlice));
    }

    [Theory]
    [InlineData("background-agent-across-round-start")]
    [InlineData("missing-child-transcripts")]
    [InlineData("no-effort-lines")]
    public void A_synthetic_lines_id_never_reaches_Messages(string name)
    {
        using var dir = TempDir.For(Prefix);
        var fixture = CallerTranscriptFixture.Materialise(name, dir);

        var session = fixture.Read();

        session.Messages.Select(m => m.MessageId).Should().NotContain(FixtureManifest.Sentinel);
        session.Diagnostics.SyntheticLines.Should().Be(1);
        TranscriptFilesHolding(fixture, FixtureManifest.Sentinel).Should().BeTrue("the raw file DOES hold the sentinel as a message id, so the test could fail");
        session.Messages.Should().HaveCount(fixture.Case.Counts.DistinctMessageIds);
    }

    [Fact]
    public void A_message_whose_last_line_is_below_its_maximum_is_counted_by_its_maximum()
    {
        using var dir = TempDir.For(Prefix);
        var fixture = CallerTranscriptFixture.Materialise("message-last-line-below-max", dir);

        var session = fixture.Read();

        var below = TranscriptTruth.Assistants(fixture).GroupBy(l => l.MessageId).Single(g => g.Count() > 1 && g.Last().Tokens.Output < g.Max(l => l.Tokens.Output));
        var message = session.Message(below.Key);
        message.Tokens.Output.Should().Be(below.Max(l => l.Tokens.Output));
        message.Tokens.Output.Should().BeGreaterThan(below.Last().Tokens.Output, "the last line is below the maximum");
        message.Tokens.Input.Should().Be(below.First().Tokens.Input);
    }

    [Fact]
    public void A_repeated_tool_use_block_is_one_call()
    {
        using var dir = TempDir.For(Prefix);
        var nested = CallerTranscriptFixture.Materialise("nested-agents", dir);

        var session = nested.Read();

        session.Agents.Where(a => a.Key != "main").Should().HaveCount(2, "main L1 and L5 carry ONE Agent block id");
        session.UnobservedLaunches.Should().BeEmpty();
        session.State.Kind.Should().Be(ObservationKind.Observed);

        using var other = TempDir.For(Prefix);
        var resumed = CallerTranscriptFixture.Materialise("resumed-agent", other);
        var agent = TranscriptFile.Agent(C14Agent);
        var edit = resumed.CallIds(agent, "Edit").Single();
        var line = resumed.Lines(agent).Select((text, index) => (text, index)).Single(pair => pair.text.Contains(edit, StringComparison.Ordinal) && pair.text.Contains("\"tool_use\"", StringComparison.Ordinal));
        resumed.PrependLine(agent, line.text);

        var duplicated = resumed.Read();

        duplicated.Writes.Should().ContainSingle(w => w.AgentKey == C14Agent);
        duplicated.State.Kind.Should().Be(ObservationKind.Observed);
    }

    [Fact]
    public void A_write_carries_the_instant_of_its_answer()
    {
        using var dir = TempDir.For(Prefix);
        var switched = CallerTranscriptFixture.Materialise("model-switch-in-window", dir);
        var main = TranscriptFile.Main;

        var write = switched.Read().Writes.Should().ContainSingle().Subject;

        write.Utc.Should().Be(switched.StampOf(main, 2), "its message's earliest line, not the line of the block");
        write.AnsweredUtc.Should().Be(switched.StampOf(main, 5));
        write.AnsweredUtc.Should().NotBe(write.Utc);

        using var other = TempDir.For(Prefix);
        var resumed = CallerTranscriptFixture.Materialise("resumed-agent", other);
        var edit = resumed.Read().Writes.Where(w => w.AgentKey == C14Agent).Should().ContainSingle().Subject;
        edit.Utc.Should().Be(resumed.StampOf(TranscriptFile.Agent(C14Agent), 53));
        edit.AnsweredUtc.Should().Be(resumed.StampOf(TranscriptFile.Agent(C14Agent), 55));

        var oldLargest = switched.LargestStamp();
        var call = switched.CallIds(main, "Write").Single();
        switched.DeleteResult(main, call);
        switched.AnswerCall(main, call);
        var answeredLater = switched.Read().Writes.Should().ContainSingle().Subject;
        answeredLater.Utc.Should().Be(write.Utc);
        answeredLater.AnsweredUtc.Should().Be(oldLargest + TimeSpan.FromSeconds(1));
    }

    private static bool TranscriptFilesHolding(CallerTranscriptFixture fixture, string sentinel) =>
        TranscriptTruth.FilesOf(fixture).Any(file => fixture.Lines(file).Any(line => HoldsAsMessageId(line, sentinel)));

    private static bool HoldsAsMessageId(string line, string sentinel)
    {
        using var document = JsonDocument.Parse(line);

        return document.RootElement.TryGetProperty("message", out var message)
            && message.ValueKind == JsonValueKind.Object
            && message.TryGetProperty("id", out var id)
            && id.GetString() == sentinel;
    }
}
