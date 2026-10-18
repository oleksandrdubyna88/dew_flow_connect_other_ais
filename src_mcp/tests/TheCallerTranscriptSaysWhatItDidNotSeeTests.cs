using System.Text.Json;
using CoaiMcp.Store;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// E3.S4 tests 19–35, 46, 49, 50 and 52–54, and 56: the reader says which of the four states it reached and why — in a closed
/// vocabulary — and a gap it could not see is a named reason, never a silent default.
/// </summary>
/// <remarks>
/// Every reason is asserted through <c>State.Render()</c>, the one text E5 will show. A test that cuts a file by bytes computes the cut
/// from the raw bytes, each line with its OWN terminator, after every mutation that changes them.
/// </remarks>
public sealed class TheCallerTranscriptSaysWhatItDidNotSeeTests
{
    private const string Prefix = "testsToDelete-coai-unseen-";
    private const string C02Agent = "303b2866f0b3022b2";
    private const string C05MetaOnly = "953030e2d167b42e0";
    private const string C07Depth1 = "207bc560f3fb5a452";
    private const string C07Depth2 = "2553a2f49d265a4e3";
    private const string C14Agent = "2edf3ddec8861a853";

    private static ObservedAgent AgentOf(ObservedSession session, string key) => session.Agent(key);

    private static string Reasons(ObservedSession session) => session.State.Render();

    private static ObservedSession Read(string name, TempDir dir, Action<CallerTranscriptFixture>? mutate = null)
    {
        var fixture = CallerTranscriptFixture.Materialise(name, dir);
        mutate?.Invoke(fixture);

        return fixture.Read();
    }

    // ---- observed, and observed zero -------------------------------------------------------------------------------------

    [Fact]
    public void An_observed_session_with_no_subagents_is_observed_zero()
    {
        using var dir = TempDir.For(Prefix);
        var zero = Read("model-switch-in-window", dir);
        using var other = TempDir.For(Prefix);
        var withAgent = Read("message-as-several-lines", other);

        zero.IsObservedZero.Should().BeTrue();
        zero.Agents.Should().ContainSingle().Which.Key.Should().Be("main");
        Reasons(zero).Should().Be("observed");
        withAgent.IsObservedZero.Should().BeFalse("one agent file means more than main");
    }

    [Fact]
    public void Every_agent_call_is_launched_refused_or_missing()
    {
        using var dir = TempDir.For(Prefix);

        var session = Read("missing-child-transcripts", dir);

        Reasons(session).Should().Be("partial: agent call without transcript, meta without transcript");
        session.UnobservedLaunches.Should().HaveCount(2).And.OnlyContain(u => u.Utc != null);
        session.UnobservedLaunches.Select(u => u.Reason).Should().BeEquivalentTo([ObservationReasons.AgentCallWithoutTranscript, ObservationReasons.MetaWithoutTranscript]);
        session.Agents.Where(a => a.Key != C05MetaOnly && a.Key != "main").Should().ContainSingle().Which.TranscriptPresent.Should().BeTrue();
    }

    [Fact]
    public void A_refused_agent_call_is_not_a_launch_and_not_partial()
    {
        using var dir = TempDir.For(Prefix);
        var fixture = CallerTranscriptFixture.Materialise("refused-agent-call", dir);

        var session = fixture.Read();

        session.State.Kind.Should().Be(ObservationKind.Observed);
        session.Agents.Should().HaveCount(1 + fixture.Case.Counts.AgentFiles);
        session.UnobservedLaunches.Should().BeEmpty();
    }

    [Fact]
    public void A_meta_without_its_transcript_is_listed_as_not_present()
    {
        using var dir = TempDir.For(Prefix);

        var agent = AgentOf(Read("missing-child-transcripts", dir), C05MetaOnly);

        agent.TranscriptPresent.Should().BeFalse();
        agent.LaunchedUtc.Should().BeNull();
        agent.CalledUtc.Should().NotBeNull();
    }

    [Fact]
    public void An_agent_file_without_a_meta_is_a_launch_with_no_parent_and_partial()
    {
        using var dir = TempDir.For(Prefix);
        var fixture = CallerTranscriptFixture.Materialise("message-as-several-lines", dir);
        var before = fixture.Read();
        fixture.DeleteMeta(C02Agent);

        var session = fixture.Read();

        var agent = AgentOf(session, C02Agent);
        (agent.ParentKey, agent.RequestedAlias, agent.TranscriptPresent).Should().Be((string.Empty, string.Empty, true));
        session.Messages.Should().HaveCount(before.Messages.Count);
        Reasons(session).Should().Be("partial: agent call without transcript, agent without meta");
        session.UnobservedLaunches.Should().ContainSingle().Which.Reason.Should().Be(ObservationReasons.AgentCallWithoutTranscript, "the reader cannot know the file and the call are one agent");
    }

    [Fact]
    public void A_meta_without_model_is_counted_not_partial_and_the_model_comes_from_the_lines()
    {
        using var dir = TempDir.For(Prefix);
        var fixture = CallerTranscriptFixture.Materialise("meta-without-model", dir);

        var session = fixture.Read();

        var key = fixture.AgentKeys.Single();
        Reasons(session).Should().Be("observed");
        session.Diagnostics.MetasWithoutModel.Should().Be(1);
        AgentOf(session, key).RequestedAlias.Should().BeEmpty();
        var mine = session.Messages.Where(m => m.AgentKey == key).ToList();
        mine.Should().NotBeEmpty();
        mine.Should().OnlyContain(m => m.Model == "claude-sonnet-5-5" && m.Requested == "claude-sonnet-5-5");
    }

    [Fact]
    public void A_torn_middle_line_is_partial_and_the_rest_is_read()
    {
        using var dir = TempDir.For(Prefix);
        var fixture = CallerTranscriptFixture.Materialise("message-as-several-lines", dir);
        var before = fixture.Read();
        var lines = TranscriptTruth.Assistants(fixture).Where(l => l.FileKey == C02Agent).ToList();
        var secondOfItsId = lines.GroupBy(l => l.MessageId).Where(g => g.Count() > 1).Select(g => g.Last()).First(l => l.Line < lines[^1].Line);
        fixture.TruncateLine(TranscriptFile.Agent(C02Agent), secondOfItsId.Line);

        var session = fixture.Read();

        Reasons(session).Should().Be("partial: torn line");
        session.Diagnostics.TornTails.Should().Be(0);
        session.Messages.Select(m => m.MessageId).Should().BeEquivalentTo(before.Messages.Select(m => m.MessageId), "the torn line's id survives on its other line and every later id is read");
    }

    [Fact]
    public void A_torn_last_line_is_a_tail_not_partial()
    {
        using var dir = TempDir.For(Prefix);
        var fixture = CallerTranscriptFixture.Materialise("message-as-several-lines", dir);
        var before = fixture.Read();
        fixture.TruncateLastLine(TranscriptFile.Main);

        var session = fixture.Read();

        Reasons(session).Should().Be("observed");
        session.Diagnostics.TornTails.Should().Be(1);
        session.Messages.Should().HaveCount(before.Messages.Count);
    }

    [Fact]
    public void A_line_whose_agent_id_disagrees_with_its_file_counts_to_the_file_and_marks_the_transcript_inconsistent()
    {
        using var dir = TempDir.For(Prefix);
        var fixture = CallerTranscriptFixture.Materialise("message-as-several-lines", dir);
        var before = fixture.Read();
        fixture.SetAgentId(TranscriptFile.Agent(C02Agent), 1, "ffffffffffffffff0");

        var session = fixture.Read();

        Reasons(session).Should().Be("partial: inconsistent transcript");
        session.Messages.Where(m => m.AgentKey == C02Agent).Should().HaveCount(before.Messages.Count(m => m.AgentKey == C02Agent));
    }

    [Fact]
    public void A_parent_link_that_contradicts_the_meta_marks_the_transcript_inconsistent()
    {
        using var dir = TempDir.For(Prefix);
        var fixture = CallerTranscriptFixture.Materialise("nested-agents", dir);
        fixture.SetMetaKey(C07Depth2, "parentAgentId", "0123456789abcdef0");

        var session = fixture.Read();

        AgentOf(session, C07Depth2).ParentKey.Should().Be(C07Depth1, "the file that holds the launching call wins");
        Reasons(session).Should().Be("partial: inconsistent transcript");
    }

    [Fact]
    public void A_main_transcript_with_no_parseable_line_is_unobserved_transcript_empty()
    {
        using var dir = TempDir.For(Prefix);
        var fixture = CallerTranscriptFixture.Materialise("refused-agent-call", dir);
        Enumerable.Range(1, fixture.Lines(TranscriptFile.Main).Count).ToList().ForEach(n => fixture.TruncateLine(TranscriptFile.Main, n));

        var session = fixture.Read();

        Reasons(session).Should().Be("unobserved: transcript empty");
        session.Agents.Should().BeEmpty();
        session.Messages.Should().BeEmpty();
        session.FirstLineUtc.Should().BeNull();
        session.IsObservedZero.Should().BeFalse();
    }

    [Fact]
    public void An_unobservable_location_is_carried_through_and_nothing_is_opened()
    {
        using var dir = TempDir.For(Prefix);
        var fixture = CallerTranscriptFixture.Materialise("message-as-several-lines", dir);
        var stated = new CallerSessionId(string.Empty, string.Empty);

        var session = CallerTranscript.Read(new CallerTranscriptLocation.NotObservable(stated, ObservationReasons.StatedId), ReadBudget.Unbounded, null);

        fixture.MainFile.Should().NotBeEmpty("a transcript IS present in the home");
        Reasons(session).Should().Be("unobserved: stated id");
        session.Session.Should().Be(stated);
        session.FirstLineUtc.Should().BeNull();
        session.Diagnostics.FilesRead.Should().Be(0);
        (session.Agents.Count + session.Messages.Count + session.Writes.Count + session.UnobservedLaunches.Count).Should().Be(0);
    }

    // ---- the budget -------------------------------------------------------------------------------------------------------

    /// <summary>The first multi-line id of the agent file whose lines start after line 1 — the cut falls between its two lines.</summary>
    private static (long MaxBytes, RawAssistant FirstLine) CutInsideAMessage(CallerTranscriptFixture fixture, TranscriptFile agent)
    {
        var first = TranscriptTruth.Assistants(fixture).Where(l => l.FileKey == agent.Key).GroupBy(l => l.MessageId)
            .Where(g => g.Count() > 1).Select(g => g.First()).Last();
        var main = fixture.ByteLengths(TranscriptFile.Main).Sum(n => (long)n);

        return (main + fixture.ByteLengths(agent).Take(first.Line).Sum(n => (long)n), first);
    }

    private static int RenamedLineBefore(CallerTranscriptFixture fixture, TranscriptFile file, int line) =>
        Enumerable.Range(1, line - 1).First(n => HasRenamedString(fixture.Lines(file)[n - 1]));

    private static bool HasRenamedString(string line)
    {
        using var document = JsonDocument.Parse(line);

        return document.RootElement.EnumerateObject().Any(p => p.Name.Length > 1 && p.Name[0] == 'k' && p.Name[1..].All(char.IsAsciiDigit) && p.Value.ValueKind == JsonValueKind.String);
    }

    [Fact]
    public void The_read_stops_at_the_byte_budget_and_says_so()
    {
        using var dir = TempDir.For(Prefix);
        var fixture = CallerTranscriptFixture.Materialise("resumed-agent", dir);
        var agent = TranscriptFile.Agent(C14Agent);
        var cutLine = CutInsideAMessage(fixture, agent).FirstLine.Line;
        fixture.PutMultibyte(agent, RenamedLineBefore(fixture, agent, cutLine));
        var (maxBytes, first) = CutInsideAMessage(fixture, agent);
        var unbounded = fixture.Read();

        var session = fixture.Read(new ReadBudget(maxBytes, TimeSpan.MaxValue, TimeProvider.System));

        var seen = TranscriptTruth.Assistants(fixture).Where(l => l.FileKey == C14Agent && l.Line <= first.Line).Select(l => l.MessageId).Distinct().ToList();
        Reasons(session).Should().Be("partial: read budget");
        session.Messages.Where(m => m.AgentKey == C14Agent).Should().BeEmpty("every id of the stopped file is inside the guard, so each might still have a line after the cut");
        session.Diagnostics.MessagesDroppedAtBudget.Should().Be(seen.Count);
        session.Diagnostics.BytesInSlice.Should().Be(maxBytes, "bytes, not characters: the multibyte line sits before the cut");
        session.Messages.Where(m => m.AgentKey == "main").Should().BeEquivalentTo(unbounded.Messages.Where(m => m.AgentKey == "main"), "main was finished");
        session.Agents.Select(a => a.Key).Should().Equal(unbounded.Agents.Select(a => a.Key));
    }

    [Fact]
    public void The_read_stops_at_the_time_budget_and_says_so()
    {
        using var dir = TempDir.For(Prefix);
        var fixture = CallerTranscriptFixture.Materialise("resumed-agent", dir);
        var clock = new SteppingClock(new DateTimeOffset(fixture.LargestStamp(), TimeSpan.Zero), TimeSpan.FromSeconds(1));

        var session = fixture.Read(new ReadBudget(long.MaxValue, TimeSpan.FromSeconds(2), clock));

        Reasons(session).Should().Be("partial: read budget");
        var inReadOrder = fixture.ByteLengths(TranscriptFile.Main).Concat(fixture.ByteLengths(TranscriptFile.Agent(C14Agent)));
        session.Diagnostics.BytesInSlice.Should().Be(inReadOrder.Take(2).Sum(n => (long)n), "the clock was read once per line (main first, then the agent file) and the third line was past the budget");
    }

    [Fact]
    public void A_budget_hit_never_invents_a_missing_transcript()
    {
        using var dir = TempDir.For(Prefix);
        var refused = CallerTranscriptFixture.Materialise("refused-agent-call", dir);
        using var other = TempDir.For(Prefix);
        var nested = CallerTranscriptFixture.Materialise("nested-agents", other);

        var stoppedBetweenCallAndResult = refused.Read(new ReadBudget(refused.ByteLengths(TranscriptFile.Main)[0], TimeSpan.MaxValue, TimeProvider.System));
        var stoppedInMain = nested.Read(new ReadBudget(nested.ByteLengths(TranscriptFile.Main)[0], TimeSpan.MaxValue, TimeProvider.System));

        Reasons(stoppedBetweenCallAndResult).Should().Be("partial: read budget", "without the step-8a skip the call would read agent call without transcript");
        stoppedInMain.Agents.Where(a => a.Key != "main").Should().HaveCount(2).And.OnlyContain(a => a.TranscriptPresent && a.LaunchedUtc != null);
        Reasons(stoppedInMain).Should().Be("partial: read budget");
    }

    [Fact]
    public void A_sidechain_line_in_the_main_file_belongs_to_its_agent_id()
    {
        using var dir = TempDir.For(Prefix);
        var named = Read("message-as-several-lines", dir, f => f.SetSidechain(TranscriptFile.Main, 1, true, "aaaaaaaaaaaaaaaaa"));
        using var other = TempDir.For(Prefix);
        var nameless = Read("message-as-several-lines", other, f => f.SetSidechain(TranscriptFile.Main, 1, true));

        named.Messages.Should().Contain(m => m.AgentKey == "aaaaaaaaaaaaaaaaa");
        Reasons(nameless).Should().Be("partial: sidechain without agent id");
        nameless.Messages.Should().NotContain(m => m.AgentKey == "main");
    }

    [Fact]
    public void Rendering_a_state_never_pluralises()
    {
        var two = new ObservationState(ObservationKind.Partial, [new ObservationReason(ObservationReasons.AgentCallWithoutTranscript, 1), new ObservationReason(ObservationReasons.MetaWithoutTranscript, 2)]);

        two.Render().Should().Be("partial: agent call without transcript, meta without transcript ×2");
        ObservationState.Observed.Render().Should().Be("observed");
        ObservationState.Unobserved(ObservationReasons.TranscriptNotFound).Render().Should().Be("unobserved: transcript not found");
    }

    // ---- added after the plan gate and the judge ---------------------------------------------------------------------------

    [Theory]
    [InlineData("missing-child-transcripts", "2acc10")]
    [InlineData("two-repositories-one-session", "3dba83")]
    public void A_tool_result_that_answers_no_parsed_call_is_ignored(string name, string orphan)
    {
        using var dir = TempDir.For(Prefix);
        var fixture = CallerTranscriptFixture.Materialise(name, dir);
        var before = fixture.Read();
        before.Messages.Should().NotBeEmpty("a read of nothing would equal a read of nothing");
        fixture.DeleteResult(TranscriptFile.Main, orphan);

        var after = fixture.Read();

        Reasons(after).Should().Be(Reasons(before));
        after.Diagnostics.WriteCallsUnanswered.Should().Be(before.Diagnostics.WriteCallsUnanswered);
        after.Diagnostics.WriteCallsRefused.Should().Be(before.Diagnostics.WriteCallsRefused);
        after.State.Reasons.Should().BeEquivalentTo(before.State.Reasons);
    }

    [Fact]
    public void A_requested_model_off_the_closed_shape_is_empty_and_counted()
    {
        using var dir = TempDir.For(Prefix);
        var fixture = CallerTranscriptFixture.Materialise("message-as-several-lines", dir);
        fixture.SetRequestedModel(TranscriptFile.Main, 1, "a sentence with spaces");

        var requested = fixture.Read();

        var main = requested.Message("afddac");
        main.Requested.Should().BeEmpty();
        requested.Diagnostics.ValuesOffShape.Should().Be(1);
        Reasons(requested).Should().Be("observed");

        fixture.SetModelOnLine(TranscriptFile.Main, 1, "a sentence with spaces");
        var both = fixture.Read();

        var modelless = both.Message("afddac");
        modelless.Model.Should().BeEmpty();
        modelless.Tokens.Should().NotBe(new TokenUsage(0, 0, 0, 0), "the message is still there with its tokens");
        both.Diagnostics.ValuesOffShape.Should().Be(2);
    }

    [Fact]
    public void An_old_message_survives_a_budget_stop_and_a_recent_one_does_not()
    {
        using var dir = TempDir.For(Prefix);
        var fixture = CallerTranscriptFixture.Materialise("resumed-agent", dir);
        var agent = TranscriptFile.Agent(C14Agent);
        var cut = CutInsideAMessage(fixture, agent).FirstLine.Line;
        var old = TranscriptTruth.Assistants(fixture).Where(l => l.FileKey == C14Agent && l.Line < cut).GroupBy(l => l.MessageId).First(g => g.Count() > 1).ToList();
        var fourHoursBefore = fixture.StampOf(agent, 1) - TimeSpan.FromHours(4);
        old.ForEach(line => fixture.SetStamp(agent, line.Line, fourHoursBefore));
        var (maxBytes, _) = CutInsideAMessage(fixture, agent);

        var session = fixture.Read(new ReadBudget(maxBytes, TimeSpan.MaxValue, TimeProvider.System));

        Reasons(session).Should().Be("partial: read budget");
        var survivors = session.Messages.Where(m => m.AgentKey == C14Agent).ToList();
        survivors.Should().ContainSingle().Which.MessageId.Should().Be(old[0].MessageId);
        survivors[0].Tokens.Should().Be(TranscriptTruth.MaxOf(TranscriptTruth.Assistants(fixture).Where(l => l.MessageId == old[0].MessageId)));
    }

    [Fact]
    public void A_middle_line_torn_before_its_stamp_is_partial_under_a_bound()
    {
        using var dir = TempDir.For(Prefix);
        var fixture = CallerTranscriptFixture.Materialise("message-as-several-lines", dir);
        var agent = TranscriptFile.Agent(C02Agent);
        var tornLine = TranscriptTruth.Assistants(fixture).Where(l => l.FileKey == C02Agent).Skip(2).First().Line;
        fixture.TruncateBeforeStamp(agent, tornLine);
        fixture.SetStamp(agent, fixture.Lines(agent).Count, fixture.LargestStamp() + TimeSpan.FromHours(4));
        var bound = fixture.StampOf(agent, fixture.Lines(agent).Count);

        var session = fixture.Read(bound);

        session.Diagnostics.LinesSkippedBeforeParse.Should().BeGreaterThan(0, "the bound is doing something");
        Reasons(session).Should().Be("partial: torn line", "the torn line has no stamp key, so the gate yields it and the parse refuses it");
    }

    [Fact]
    public void A_meta_that_does_not_parse_is_partial_torn_meta_and_its_file_still_counts()
    {
        using var dir = TempDir.For(Prefix);
        var fixture = CallerTranscriptFixture.Materialise("nested-agents", dir);
        var before = fixture.Read();
        fixture.CorruptMeta(C07Depth1);

        var session = fixture.Read();

        Reasons(session).Should().Be("partial: agent call without transcript, torn meta", "its meta is unreadable, so main's Agent call joins nothing; no 'agent without meta'");
        var outer = AgentOf(session, C07Depth1);
        (outer.ParentKey, outer.RequestedAlias).Should().Be((string.Empty, string.Empty));
        session.Messages.Where(m => m.AgentKey == C07Depth1).Should().HaveCount(before.Messages.Count(m => m.AgentKey == C07Depth1)).And.NotBeEmpty();
        AgentOf(session, C07Depth2).ParentKey.Should().Be(C07Depth1);
    }

    [Fact]
    public void A_line_over_the_cap_is_a_lost_line_and_says_so()
    {
        using var dir = TempDir.For(Prefix);
        var fixture = CallerTranscriptFixture.Materialise("resumed-agent", dir);
        var agent = TranscriptFile.Agent(C14Agent);
        var before = fixture.Read();
        var single = TranscriptTruth.Assistants(fixture).Where(l => l.FileKey == C14Agent).GroupBy(l => l.MessageId).Where(g => g.Count() == 1).Select(g => g.Single()).Last();
        var cap = TranscriptTruth.FilesOf(fixture).SelectMany(fixture.ByteLengths).Max() + 16;
        fixture.PutLongLine(agent, single.Line, cap + 4096);

        var session = fixture.Read(new ReadBudget(long.MaxValue, TimeSpan.MaxValue, TimeProvider.System) { MaxLineBytes = cap });

        Reasons(session).Should().Be("partial: line too long");
        session.Diagnostics.LinesTooLong.Should().Be(1);
        session.Messages.Select(m => m.MessageId).Should().BeEquivalentTo(before.Messages.Select(m => m.MessageId).Where(id => id != single.MessageId));
    }

    [Fact]
    public void A_present_agent_file_with_no_stamped_line_reads_as_absent()
    {
        using var dir = TempDir.For(Prefix);
        var fixture = CallerTranscriptFixture.Materialise("message-as-several-lines", dir);
        var call = fixture.ToolUseIdEndingWith(TranscriptFile.Main, "5d66b6");
        fixture.EmptyAgentFile(C02Agent);

        var emptied = fixture.Read();

        Reasons(emptied).Should().Be("partial: meta without transcript");
        var agent = AgentOf(emptied, C02Agent);
        (agent.TranscriptPresent, agent.LaunchedUtc).Should().Be((false, (DateTime?)null));
        agent.CalledUtc.Should().Be(fixture.StampOf(TranscriptFile.Main, 1));
        emptied.UnobservedLaunches.Should().ContainSingle().Which.Should().Be(new UnobservedLaunch("main", call, fixture.StampOf(TranscriptFile.Main, 1), ObservationReasons.MetaWithoutTranscript));
        emptied.Diagnostics.AgentFilesEmpty.Should().Be(1);
        emptied.Messages.Should().ContainSingle().Which.AgentKey.Should().Be("main");

        fixture.DeleteMeta(C02Agent);
        var noMeta = fixture.Read();

        Reasons(noMeta).Should().Be("partial: agent call without transcript, agent without meta");
        AgentOf(noMeta, C02Agent).TranscriptPresent.Should().BeFalse();
        noMeta.UnobservedLaunches.Should().ContainSingle().Which.Reason.Should().Be(ObservationReasons.AgentCallWithoutTranscript);
    }

    [Fact]
    public void A_present_agent_is_always_launched_in_every_fixture()
    {
        FixtureManifest.Cases.Should().NotBeEmpty();
        foreach (var fixtureCase in FixtureManifest.Cases)
        {
            using var dir = TempDir.For(Prefix);
            var session = CallerTranscriptFixture.Materialise(fixtureCase.Name, dir).Read();

            session.Agents.All(a => !a.TranscriptPresent || a.Key == "main" || a.LaunchedUtc != null).Should().BeTrue(fixtureCase.Name + ": a present agent is always launched");
            session.Diagnostics.AgentFilesEmpty.Should().Be(0, fixtureCase.Name);
        }
    }

    // ---- added after the S4 review: a budget stop on an empty main, and a line with no stamp ------------------------------

    [Fact]
    public void A_main_transcript_with_no_parseable_line_is_transcript_empty_even_after_a_budget_stop()
    {
        using var dir = TempDir.For(Prefix);
        var fixture = CallerTranscriptFixture.Materialise("message-as-several-lines", dir);
        Enumerable.Range(1, fixture.Lines(TranscriptFile.Main).Count).ToList().ForEach(n => fixture.TruncateLine(TranscriptFile.Main, n));

        var session = fixture.Read(new ReadBudget(1, TimeSpan.MaxValue, TimeProvider.System));

        Reasons(session).Should().Be("unobserved: transcript empty", "a main file with no stamped line has nothing to be partial about, stopped or not (S4-10, step 10)");
        session.Agents.Should().BeEmpty();
        session.Messages.Should().BeEmpty();
        session.FirstLineUtc.Should().BeNull();
    }

    private static int AnswerLineOf(CallerTranscriptFixture fixture, string callId)
    {
        var line = fixture.Lines(TranscriptFile.Main).ToList().FindIndex(text => text.Contains("\"tool_result\"", StringComparison.Ordinal) && text.Contains(callId, StringComparison.Ordinal)) + 1;
        line.Should().BeGreaterThan(0, "the fixture holds an answer for the call");

        return line;
    }

    private static string SingleWriteOf(CallerTranscriptFixture fixture) => fixture.CallIds(TranscriptFile.Main, "Write").Single();

    [Fact]
    public void An_answer_on_a_line_with_no_stamp_is_dropped_and_the_transcript_is_inconsistent()
    {
        using var dir = TempDir.For(Prefix);
        var fixture = CallerTranscriptFixture.Materialise("model-switch-in-window", dir);
        fixture.RemoveLineKey(TranscriptFile.Main, AnswerLineOf(fixture, SingleWriteOf(fixture)), "timestamp");

        var session = fixture.Read();

        session.Writes.Should().BeEmpty();
        (session.Diagnostics.WriteCallsUnanswered, session.Diagnostics.WriteCallsRefused).Should().Be((1, 0));
        Reasons(session).Should().Be("partial: inconsistent transcript");
    }

    [Fact]
    public void A_refused_answer_on_a_line_with_no_stamp_is_dropped_not_refused()
    {
        using var dir = TempDir.For(Prefix);
        var fixture = CallerTranscriptFixture.Materialise("model-switch-in-window", dir);
        var call = SingleWriteOf(fixture);
        fixture.SetResultError(TranscriptFile.Main, call);
        fixture.RemoveLineKey(TranscriptFile.Main, AnswerLineOf(fixture, call), "timestamp");

        var session = fixture.Read();

        session.Writes.Should().BeEmpty();
        (session.Diagnostics.WriteCallsUnanswered, session.Diagnostics.WriteCallsRefused).Should().Be((1, 0), "an unstamped result is dropped before anyone reads its error flag (S4-22)");
        Reasons(session).Should().Be("partial: inconsistent transcript");
    }

    private static RawAssistant LoneAssistantOf(CallerTranscriptFixture fixture) =>
        TranscriptTruth.Assistants(fixture).Where(l => l.FileKey == "main").GroupBy(l => l.MessageId).Where(g => g.Count() == 1).Select(g => g.Single()).Last();

    [Fact]
    public void An_assistant_line_with_no_stamp_is_inconsistent_and_its_message_is_not_counted_from_it()
    {
        using var dir = TempDir.For(Prefix);
        var fixture = CallerTranscriptFixture.Materialise("two-repositories-one-session", dir);
        var before = fixture.Read();
        var lone = LoneAssistantOf(fixture);
        fixture.RemoveLineKey(TranscriptFile.Main, lone.Line, "timestamp");

        var session = fixture.Read();

        before.Messages.Should().Contain(m => m.MessageId == lone.MessageId, "the message was counted before the stamp was removed");
        session.Messages.Should().NotContain(m => m.MessageId == lone.MessageId);
        Reasons(session).Should().Be("partial: inconsistent transcript");
    }

    [Fact]
    public void An_assistant_line_with_no_message_id_is_inconsistent()
    {
        using var dir = TempDir.For(Prefix);
        var fixture = CallerTranscriptFixture.Materialise("two-repositories-one-session", dir);
        var before = fixture.Read();
        var lone = LoneAssistantOf(fixture);
        fixture.RemoveLineKey(TranscriptFile.Main, lone.Line, "message.id");

        var session = fixture.Read();

        session.Messages.Should().HaveCount(before.Messages.Count - 1);
        session.Messages.Should().NotContain(m => m.MessageId == lone.MessageId);
        Reasons(session).Should().Be("partial: inconsistent transcript");
    }
}
