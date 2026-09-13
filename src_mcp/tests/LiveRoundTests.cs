using System.Collections.Immutable;
using Xunit;
using CoaiMcp.Core.Findings;
using CoaiMcp.Core.Rounds;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Reviewers;
using CoaiMcp.Server;
using FluentAssertions;

namespace CoaiMcp.Tests;

/// <summary>
/// The round's live status: on disk before the first CLI starts, advancing while it runs, swept
/// when the process that owned it is gone. The defect these hold shut is the one the operator saw
/// — a ten-minute round that showed nothing at all until it ended.
/// </summary>
public sealed class LiveRoundTests : IDisposable
{
    /// <summary>Every temporary directory this class made, gone when it ends.</summary>
    public void Dispose()
    {
        _dir.Dispose();
    }
    private readonly TempDir _dir = TempDir.For("coai-live-");

    private static PersistedSession Session() =>
        new(new SessionState("s-live", "D:/repo", "feature/x", new PanelConfig()), []);

    private static ReviewerWork Work(string provider, string role) =>
        new(new ReviewerInvocation(provider, role, new ProcessRequest("cli", [], ".")));

    /// <summary>The same JSON with one property name removed, wherever it appears.</summary>
    private static void WriteWithout(
        System.Text.Json.JsonElement element,
        string drop,
        System.Text.Json.Utf8JsonWriter writer)
    {
        switch (element.ValueKind)
        {
            case System.Text.Json.JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in element.EnumerateObject().Where(p => p.Name != drop))
                {
                    writer.WritePropertyName(property.Name);
                    WriteWithout(property.Value, drop, writer);
                }

                writer.WriteEndObject();
                break;
            case System.Text.Json.JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in element.EnumerateArray())
                {
                    WriteWithout(item, drop, writer);
                }

                writer.WriteEndArray();
                break;
            default:
                element.WriteTo(writer);
                break;
        }
    }

    private static NormalisedReview Review(int findings) =>
        new(
            [.. Enumerable.Range(0, findings).Select(i =>
                new Finding(Severity.Major, Category.Security, "a.cs", i + 1, $"f{i}", "why", "fix", ["codex"]))],
            []);

    /// <summary>
    /// The round writes down WHICH MODEL each reviewer was launched with.
    /// </summary>
    /// <remarks>
    /// <para>The invocation has carried the model since the adapters were written; the round simply
    /// never wrote it down, so the log could say who reviewed and not with what. That is how a slow
    /// claude reviewer came to be investigated by reading a spending ledger instead of the log
    /// (`research/RESULTS_reviewer_input_sizes.md`).</para>
    /// <para>The field is trailing and defaulted, so every session file already on disk stays valid:
    /// an older round names no model, which is the truth about it rather than a gap.</para>
    /// </remarks>
    /// <summary>
    /// The round records WHO ASKED for it, and keeps it when the session is reopened by somebody else.
    /// </summary>
    /// <remarks>
    /// <para>Six findings across two vendors on this change's code round, and they were right. The
    /// declaration arrives on <c>open</c> and so lives on the SESSION — but a session is repo+branch
    /// and `open` is idempotent on that pair, so Codex opening a branch Claude reviewed yesterday
    /// replaces it. Rendering every row from the session's latest caller would relabel history:
    /// round 1 shown as asked by the client that merely reopened it.</para>
    /// <para>So the round takes a copy when it starts. The session's field stays what it says it is
    /// — who opened this session most recently — and the round says who asked for the round.</para>
    /// </remarks>
    [Fact]
    public void ARoundKeepsTheCallerItWasOpenedBy_EvenAfterSomebodyElseReopensTheSession()
    {
        var store = new SessionStore(_dir);
        var claude = Session() with { Caller = new CallerDeclaration("claude", "claude-code", "7.3.1", "claude-opus-5") };
        store.Save(claude);

        _ = new LiveRound(store, claude, [Work("codex", RoleCatalog.ArchitectureRole)]);
        var round = store.Load("D:/repo", "feature/x")!.Rounds.Single();

        round.Caller.Model.Should().Be("claude-opus-5");
        round.Caller.Client.Should().Be("claude-code");

        // Codex now opens the same repo and branch. The SESSION's caller becomes codex — that is
        // what the field means — and the round already written keeps its own.
        store.Save(store.Load("D:/repo", "feature/x")! with
        {
            Caller = new CallerDeclaration("codex", "codex", "0.9", "codex-astra"),
        });

        var reopened = store.Load("D:/repo", "feature/x")!;
        reopened.Caller.Model.Should().Be("codex-astra");
        reopened.Rounds.Single().Caller.Model.Should().Be(
            "claude-opus-5", "the round was asked for by claude, whoever opened the session afterwards");
    }

    [Fact]
    public void ARoundFromASessionThatNeverRecordedACaller_RecordsNone()
    {
        var store = new SessionStore(_dir);
        var session = Session();
        store.Save(session);

        _ = new LiveRound(store, session, [Work("codex", RoleCatalog.ArchitectureRole)]);

        store.Load("D:/repo", "feature/x")!.Rounds.Single().Caller.Model.Should().BeEmpty();
    }

    [Fact]
    public void EachReviewerRecordsTheModelItWasLaunchedWith()
    {
        var store = new SessionStore(_dir);
        var session = Session();
        store.Save(session);

        _ = new LiveRound(store, session, [
            Work("codex", RoleCatalog.ArchitectureRole) with
            {
                Invocation = Work("codex", RoleCatalog.ArchitectureRole).Invocation with { Model = "gpt-5-codex" },
            },
            Work("local", RoleCatalog.SecurityRole),
        ]);

        var states = store.Load("D:/repo", "feature/x")!.Rounds.Single().ReviewerStates;
        states.Single(s => s.Provider == "codex").Model.Should().Be("gpt-5-codex");
        states.Single(s => s.Provider == "local").Model.Should().BeEmpty(
            "a reviewer launched without a model names none rather than inventing one");
    }

    [Fact]
    public void ANullOrWhitespaceModel_IsRecordedAsNone()
    {
        // The two inputs the "launched without a model" test does NOT reach: it uses the invocation's
        // own default, which is already empty, so neither the coalesce nor the trim ever runs. A
        // reviewer on the code round asked for them by name.
        //
        // `null!` is the point rather than a shortcut: an invocation that came back through JSON with
        // a null model IS null at runtime whatever the annotation says, and that is the case the
        // coalesce exists for.
        var store = new SessionStore(_dir);
        var session = Session();
        store.Save(session);

        var codex = Work("codex", RoleCatalog.ArchitectureRole);
        var local = Work("local", RoleCatalog.SecurityRole);
        _ = new LiveRound(store, session, [
            codex with { Invocation = codex.Invocation with { Model = null! } },
            local with { Invocation = local.Invocation with { Model = "   " } },
        ]);

        store.Load("D:/repo", "feature/x")!.Rounds.Single().ReviewerStates
            .Should().OnlyContain(s => s.Model == string.Empty,
                "a model that is null or only whitespace is no model, and neither may throw");
    }

    [Fact]
    public void ASessionFileWrittenBeforeTheModelField_StillLoads()
    {
        // The promise a trailing default makes, kept by reading a file that predates it.
        var store = new SessionStore(_dir);
        var session = Session();
        store.Save(session);
        _ = new LiveRound(store, session, [Work("codex", RoleCatalog.ArchitectureRole)]);

        // Removed STRUCTURALLY, not by string surgery. The first draft replaced `"model": "",`
        // — and `Model` is the trailing property, so there is no comma after it, so the replacement
        // matched nothing and this test reloaded a CURRENT-format file while claiming to prove
        // something about an older one. Caught on the code round; it is the same vacuous shape as a
        // test that compares a function with itself.
        var path = Directory.EnumerateFiles(Path.Combine(_dir, "sessions"), "session-*.json").Single();
        var legacy = File.ReadAllText(path);
        legacy.Should().Contain("\"model\"", "the fixture must start from a file that HAS the field");

        using (var document = System.Text.Json.JsonDocument.Parse(legacy))
        {
            using var buffer = new MemoryStream();
            using (var writer = new System.Text.Json.Utf8JsonWriter(buffer))
            {
                WriteWithout(document.RootElement, "model", writer);
            }

            File.WriteAllBytes(path, buffer.ToArray());
        }

        File.ReadAllText(path).Should().NotContain("\"model\"", "the field really is gone now");
        store.Load("D:/repo", "feature/x")!.Rounds.Single().ReviewerStates
            .Single().Model.Should().BeEmpty();
    }

    [Fact]
    public void TheRoundIsOnDisk_BeforeAnyReviewerHasAnswered()
    {
        var store = new SessionStore(_dir);
        var session = Session();
        store.Save(session);

        _ = new LiveRound(store, session, [Work("codex", RoleCatalog.ArchitectureRole), Work("claude", RoleCatalog.ArchitectureRole)]);

        var round = store.Load("D:/repo", "feature/x")!.Rounds.Should().ContainSingle().Subject;
        round.Status.Should().Be(RoundRecord.Running);
        round.StartedUtc.Should().NotBe(default);
        round.RunnerPid.Should().Be(Environment.ProcessId);
        round.ReviewerStates.Should().HaveCount(2).And.OnlyContain(s => s.Status == ReviewerState.Queued);
    }

    [Fact]
    public void EachReviewerMoving_IsVisibleImmediately_QueuedThenRunningThenDone()
    {
        var store = new SessionStore(_dir);
        var session = Session();
        store.Save(session);
        var live = new LiveRound(store, session, [Work("codex", RoleCatalog.ArchitectureRole), Work("gemini", RoleCatalog.ArchitectureRole)]);

        live.Report(new ReviewerProgress("codex", RoleCatalog.ArchitectureRole, "running"));

        StateOf(store, "codex").Status.Should().Be(ReviewerState.Running);
        StateOf(store, "gemini").Status.Should().Be(ReviewerState.Queued, "one reviewer's progress is not another's");

        live.Report(new ReviewerProgress("codex", RoleCatalog.ArchitectureRole, "done",
            new ReviewerOutcome.Ok(Review(3), Repaired: false, new Usage(1000, 100, 0.02))));

        var done = StateOf(store, "codex");
        done.Status.Should().Be(ReviewerState.Done);
        done.Findings.Should().Be(3, "the count is what makes a finished reviewer worth reading");
    }

    /// <summary>
    /// Each reviewer's own duration, because the round's total cannot answer "which of the nine".
    /// </summary>
    /// <remarks>
    /// Measured 2026-09-03: a code round took 11m 2s across nine reviewers, and the two that spent
    /// 590 s each were indistinguishable in that number from the seven that took under a minute.
    /// The scheduler times every reviewer already — this is the number arriving instead of being
    /// dropped at the session boundary.
    /// </remarks>
    [Fact]
    public void AFinishedReviewer_KeepsItsOwnDuration()
    {
        var store = new SessionStore(_dir);
        var session = Session();
        store.Save(session);
        var live = new LiveRound(store, session, [Work("codex", RoleCatalog.ArchitectureRole)]);

        live.Report(new ReviewerProgress("codex", RoleCatalog.ArchitectureRole, "running"));
        StateOf(store, "codex").Seconds.Should().Be(0, "a running reviewer has no duration yet");

        live.Report(new ReviewerProgress(
            "codex",
            RoleCatalog.ArchitectureRole,
            "done",
            new ReviewerOutcome.Ok(Review(3), Repaired: false, new Usage(1000, 100, 0.02)),
            TimeSpan.FromSeconds(38.7)));

        StateOf(store, "codex").Seconds.Should().Be(38.7);
    }

    [Fact]
    public void ALaterReportWithoutADuration_DoesNotEraseTheOneRecorded()
    {
        // A "running" report carries no elapsed time, and taking it would zero the number of a
        // reviewer that had already finished — a retry, a repaint, or any later progress line.
        var store = new SessionStore(_dir);
        var session = Session();
        store.Save(session);
        var live = new LiveRound(store, session, [Work("gemini", RoleCatalog.ArchitectureRole)]);

        live.Report(new ReviewerProgress("gemini", RoleCatalog.ArchitectureRole, "done", null, TimeSpan.FromSeconds(12.5)));
        live.Report(new ReviewerProgress("gemini", RoleCatalog.ArchitectureRole, "running"));

        StateOf(store, "gemini").Seconds.Should().Be(12.5);
    }

    [Fact]
    public void AFailedReviewer_SaysWhy_WhileTheRoundIsStillOpen()
    {
        var store = new SessionStore(_dir);
        var session = Session();
        store.Save(session);
        var live = new LiveRound(store, session, [Work("gemini", RoleCatalog.PlanRole)]);

        live.Report(new ReviewerProgress("gemini", RoleCatalog.PlanRole, "failed", new ReviewerOutcome.TimedOut()));

        var state = StateOf(store, "gemini");
        state.Status.Should().Be(ReviewerState.Failed);
        state.Note.Should().Be("timeout");
    }

    [Fact]
    public void TheFinishedRecord_CarriesTheRoundsTokensAndMoney()
    {
        var store = new SessionStore(_dir);
        var session = Session();
        store.Save(session);
        var work = new[] { Work("codex", RoleCatalog.ArchitectureRole), Work("claude", RoleCatalog.ArchitectureRole) };
        var live = new LiveRound(store, session, work);

        var record = live.Finish("revise", 4, "all 2 reviewers answered",
        [
            (work[0].Invocation, new ReviewerOutcome.Ok(Review(2), false, new Usage(5300, 260, null))),
            (work[1].Invocation, new ReviewerOutcome.Ok(Review(2), false, new Usage(24064, 44, 0.0489))),
        ]);

        record.Status.Should().Be(RoundRecord.Done);
        record.TokensIn.Should().Be(29364);
        record.TokensOut.Should().Be(304);
        record.CostUsd.Should().BeApproximately(0.0489, 0.000001, "the priced vendor's spend is real money spent");
    }

    [Fact]
    public void ARoundAbandonedByADeadProcess_IsSwept_NeverLeftRunningForever()
    {
        var store = new SessionStore(_dir);
        var session = Session();
        store.Save(session);
        _ = new LiveRound(store, session, [Work("codex", RoleCatalog.ArchitectureRole)]);

        var swept = store.SweepOrphanedRounds(_ => false);

        swept.Should().Be(1);
        var round = store.Load("D:/repo", "feature/x")!.Rounds.Single();
        round.Status.Should().Be(RoundRecord.Interrupted);
        round.Verdict.Should().Be("interrupted");
    }

    [Fact]
    public void ALiveRoundOfAnotherRunningServer_IsLeftAlone()
    {
        // Two MCP clients can share this data directory; declaring the other one's round dead
        // would be worse than showing a stale one.
        var store = new SessionStore(_dir);
        var session = Session();
        store.Save(session);
        _ = new LiveRound(store, session, [Work("codex", RoleCatalog.ArchitectureRole)]);

        store.SweepOrphanedRounds(_ => true).Should().Be(0);
        store.Load("D:/repo", "feature/x")!.Rounds.Single().Status.Should().Be(RoundRecord.Running);
    }

    private static ReviewerState StateOf(SessionStore store, string provider) =>
        store.Load("D:/repo", "feature/x")!.Rounds.Last().ReviewerStates.Single(s => s.Provider == provider);
}
