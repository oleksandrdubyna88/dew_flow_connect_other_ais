using CoaiMcp.Core.Findings;
using CoaiMcp.Core.Notices;
using CoaiMcp.Core.Rounds;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Reviewers;
using CoaiMcp.Server;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// A reviewer that did not answer is written down once, and a sixth ending cannot arrive unnamed.
/// </summary>
/// <remarks>
/// <para><b>What was broken.</b> A reviewer that times out, is rate-limited, exits non-zero, never
/// starts or answers unparseably is described into <c>ReviewerState.Note</c> — which lives in the
/// session file, is rewritten on every tick and is swept. The page reads
/// <c>server-notices.jsonl</c>, the codes have existed since story 1.2, and nothing wrote them. A
/// person looking at a round with four findings had no way to learn that two reviewers never
/// answered.</para>
///
/// <para><b>The claim that a sixth ending "cannot appear silently" was false.</b> A C# switch over a
/// class hierarchy is not exhaustive-checked: adding a sealed subtype compiles, and the switch then
/// throws at runtime or folds it into "unknown" — losing the notice for exactly the ending nobody
/// thought about. `ReviewerExecutor`'s own docstring said otherwise and so did the plan. The map is
/// DATA now, and the test below compares its keys with what reflection finds.</para>
///
/// <para><b>What a throw here costs, precisely.</b> Not the round — the scheduler wraps its progress
/// callback in <c>catch (Exception)</c>, *"Reporting is not the work"*. It costs the REST of that
/// callback: <c>live.Report</c>, <c>audit.Moved</c> and <c>_ledger.Record</c> run in sequence at
/// <c>PanelService.cs:1249-1265</c>, so a throw in the first loses that reviewer's audit line and its
/// spending row. The split corrected the plan round on this, and the boundary claims it.</para>
/// </remarks>
public sealed class TheReviewerFailuresAreWrittenDownTests : IDisposable
{
    private readonly TempDir _dir = TempDir.For("coai-failures-");

    private readonly List<ServerNotice> _written = [];

    private readonly List<string> _said = [];

    public void Dispose() => _dir.Dispose();

    private Noticing Collecting => new(notice => { _written.Add(notice); return true; }, Watching());

    private Noticing Throwing =>
        new(_ => throw new InvalidOperationException("the writer exploded"), Watching());

    private Serilog.ILogger Watching() =>
        new Serilog.LoggerConfiguration().WriteTo.Sink(new Sink(_said)).CreateLogger();

    private SessionStore _store = null!;

    private LiveRound Round(Noticing noticing, params string[] providers)
    {
        _store = new SessionStore(_dir);
        var session = new PersistedSession(
            new SessionState("s-failures", "D:/repo", "feature/x", new PanelConfig()), []);
        _store.Save(session);

        return new LiveRound(_store, session, 1, [.. providers.Select(Work)], "", noticing);
    }

    /// <summary>What the session file says about one reviewer, after the round persisted it.</summary>
    private ReviewerState Persisted(string provider) =>
        _store.Load("D:/repo", "feature/x")!.Rounds.Single().ReviewerStates
            .Single(state => state.Provider == provider);

    private static ReviewerWork Work(string provider) =>
        new(new ReviewerInvocation(provider, RoleCatalog.ArchitectureRole, new ProcessRequest("cli", [], ".")));

    private static ReviewerProgress Ended(string provider, ReviewerOutcome outcome) =>
        new(provider, RoleCatalog.ArchitectureRole, "failed", outcome, TimeSpan.FromSeconds(1));

    /// <summary>
    /// One case per mapped ending, DERIVED from the map rather than repeated beside it.
    /// </summary>
    /// <remarks>
    /// codex, on the code round: a theory that lists the five types again is a theory that gives a
    /// sixth no row — the list and the code would have to be kept in step by somebody remembering.
    /// The cases come from <c>ReviewerNotices.ByType.Keys</c>, and <see cref="Instance"/> is the one
    /// thing a reflection walk cannot supply: constructor arguments. An ending whose type is mapped
    /// but which <see cref="Instance"/> cannot build fails LOUDLY here, which is the same demand in
    /// a different place.
    /// </remarks>
    public static TheoryData<ReviewerOutcome, string> EveryEnding
    {
        get
        {
            var cases = new TheoryData<ReviewerOutcome, string>();
            foreach (var (type, code) in ReviewerNotices.ByType)
            {
                cases.Add(Instance(type), code);
            }

            return cases;
        }
    }

    /// <summary>One of each ending, built — the arguments reflection cannot invent.</summary>
    private static readonly IReadOnlyDictionary<Type, Func<ReviewerOutcome>> Built =
        new Dictionary<Type, Func<ReviewerOutcome>>
        {
            [typeof(ReviewerOutcome.TimedOut)] = () => new ReviewerOutcome.TimedOut(),
            [typeof(ReviewerOutcome.RateLimited)] = () => new ReviewerOutcome.RateLimited("quota", 2),
            [typeof(ReviewerOutcome.NonZeroExit)] = () => new ReviewerOutcome.NonZeroExit(3, "boom"),
            [typeof(ReviewerOutcome.NotStarted)] = () => new ReviewerOutcome.NotStarted("no executable"),
            [typeof(ReviewerOutcome.Unparseable)] = () => new ReviewerOutcome.Unparseable("not json", Usage.None),
        };

    private static ReviewerOutcome Instance(Type ending) =>
        Built.TryGetValue(ending, out var build)
            ? build()
            : throw new NotSupportedException(
                $"{ending.Name} is mapped to a code but this suite cannot build one — add it here, "
                + "because an ending nothing can construct is an ending nothing tests");

    [Theory]
    [MemberData(nameof(EveryEnding))]
    public void EveryEnding_LeavesItsOwnCode(ReviewerOutcome outcome, string code)
    {
        Round(Collecting, "codex").Report(Ended("codex", outcome));

        var notice = _written.Should().ContainSingle().Subject;

        notice.Code.Should().Be(code, "one shared code would collapse five different failures");
        notice.Class.Should().Be("failure");
        notice.Subject.Should().Be($"codex/{RoleCatalog.ArchitectureRole}",
            "the extension keys repeats on (code, subject), so the reviewer IS the resource");
        notice.Title.Should().Be(ReviewerSummaryFactory.Describe(outcome),
            "the notice and the round's own summary must not drift into two sentences");
    }

    [Fact]
    public void AReviewerThatAnswered_WritesNothing()
    {
        Round(Collecting, "codex").Report(new ReviewerProgress(
            "codex", RoleCatalog.ArchitectureRole, "done",
            new ReviewerOutcome.Ok(Reviewed(), Repaired: false), TimeSpan.FromSeconds(1)));

        _written.Should().BeEmpty("a file full of successes is a file nobody reads");
    }

    [Fact]
    public void AReviewerStillRunning_WritesNothing()
    {
        Round(Collecting, "codex")
            .Report(new ReviewerProgress("codex", RoleCatalog.ArchitectureRole, "running"));

        _written.Should().BeEmpty("a progress line carries no outcome, and no outcome is no ending");
    }

    [Fact]
    public void TheSameReviewerReportedTwice_IsWrittenOnce()
    {
        var round = Round(Collecting, "codex");

        round.Report(Ended("codex", new ReviewerOutcome.TimedOut()));
        round.Report(Ended("codex", new ReviewerOutcome.TimedOut()));

        _written.Should().ContainSingle("`Report` is public and the suite already drives it twice "
            + "for one reviewer; two lines would double every count on the page");
    }

    [Fact]
    public void TwoReviewersThatFailed_AreTwoSubjects()
    {
        var round = Round(Collecting, "codex", "gemini");

        round.Report(Ended("codex", new ReviewerOutcome.TimedOut()));
        round.Report(Ended("gemini", new ReviewerOutcome.TimedOut()));

        _written.Select(notice => notice.Subject).Should()
            .Equal([$"codex/{RoleCatalog.ArchitectureRole}", $"gemini/{RoleCatalog.ArchitectureRole}"]);
    }

    [Fact]
    public void AWriterThatThrows_CostsNeitherTheStateNorTheRest()
    {
        // The consequence the split corrected: not the round — the scheduler catches its own progress
        // callback — but the REST of that callback, which is this reviewer's audit line and its
        // spending row. So `Report` must return, and the state it persists must still be there.
        var round = Round(Throwing, "codex");

        var reporting = () => round.Report(Ended("codex", new ReviewerOutcome.TimedOut()));

        reporting.Should().NotThrow();
        _said.Should().ContainSingle("and the loss is said out loud rather than swallowed");

        // The name of this test promises the STATE survives, and CodeRabbit was right that it only
        // checked the throw. The round's own record is what the page falls back to when the ledger
        // lost a line, so it is the half that must not go with it.
        var state = Persisted("codex");

        state.Status.Should().Be("failed");
        state.Note.Should().Be("timeout", "the reviewer's reason is still in the session file");
    }

    [Fact]
    public void AWriterThatRefuses_IsTriedAgainOnTheNextReport()
    {
        // codex, on the plan round: marking the key BEFORE the offer is accepted means a notice the
        // disk refused is suppressed for ever by a key nothing wrote.
        var refusals = 0;
        var flaky = new Noticing(
            notice => { refusals++; if (refusals == 1) { return false; } _written.Add(notice); return true; },
            Watching());
        var round = Round(flaky, "codex");

        round.Report(Ended("codex", new ReviewerOutcome.TimedOut()));
        round.Report(Ended("codex", new ReviewerOutcome.TimedOut()));

        _written.Should().ContainSingle("the second report wrote what the first could not");
        refusals.Should().Be(2, "and it was offered twice, not suppressed by a key nothing wrote");
    }

    [Fact]
    public void EveryEndingHasACode_AskedOfTheAssembly()
    {
        // The mechanism that replaces the false "a new subtype is a compile error". A sixth sealed
        // subtype of ReviewerOutcome is red HERE on the day it is added.
        var endings = typeof(ReviewerOutcome).Assembly.GetTypes()
            .Where(type => type.IsSealed && typeof(ReviewerOutcome).IsAssignableFrom(type))
            .ToList();

        endings.Should().HaveCountGreaterThan(3,
            "the reflection found almost nothing, so the comparison below means nothing");
        // StoodDown (issue #485) is the third kind: a DECISION not to launch, never a failure — it raises
        // no notice, and the round says it among the roles it chose not to ask.
        ReviewerNotices.ByType.Keys.Append(typeof(ReviewerOutcome.Ok)).Append(typeof(ReviewerOutcome.StoodDown))
            .Should().BeEquivalentTo(endings,
                "every ending either has a code, is the success, or is the decision not to run — one that is "
                + "none of these is an ending nothing writes down, which §8 of the plan exists to prevent");
        ReviewerNotices.ByType.Values.Should().OnlyContain(code => ServerNoticeCodes.All.Contains(code),
            "and a code outside the catalog cannot be grouped with its own repeats");
    }

    [Fact]
    public void ASecretInAReviewersStderr_DoesNotReachTheNotice()
    {
        Round(Collecting, "codex").Report(Ended("codex",
            new ReviewerOutcome.NonZeroExit(1, "auth failed: token=ghp_0123456789abcdefghij0123456789abcdef")));

        var line = ServerNoticeLine.Of(_written.Should().ContainSingle().Subject);

        line.Should().NotContain("ghp_0123456789abcdefghij0123456789abcdef",
            "the redactor covers every field, and this asserts the reviewer road reaches it");
        line.Should().Contain("exit 1", "and what is left still says what happened");
    }

    [Fact]
    public void AMegabyteOfStderr_DoesNotSitInTheQueue()
    {
        Round(Collecting, "codex").Report(Ended("codex",
            new ReviewerOutcome.NonZeroExit(1, new string('x', 1_000_000))));

        _written.Should().ContainSingle().Subject.Title!.Length
            .Should().BeLessThanOrEqualTo(Redaction.TitleLimit,
                "256 queued notices each holding a megabyte is 256 MB of process held because a "
                + "share stopped answering — story 2.2's rule, applied to this road");
    }

    [Fact]
    public void AReasonNoQuoteBounds_IsCutAndSaysSo()
    {
        // Written after a plant proved the FIRST version of this assertion worthless. It hung off
        // `AMegabyteOfStderr`, where the tail goes through `BoundedScheduler.Quote` and comes back
        // already ellipsised at `ReasonLength` — so the assertion passed on Quote's mark and would
        // have passed with the shared cut removed. `not started: {Reason}` has no Quote in it, so
        // this is a sentence that really does reach `ServerNotice.Shortened`.
        Round(Collecting, "codex").Report(Ended("codex",
            new ReviewerOutcome.NotStarted(new string('r', 2_000))));

        var title = _written.Should().ContainSingle().Subject.Title!;

        title.Length.Should().BeLessThanOrEqualTo(Redaction.TitleLimit);
        title.Should().EndWith("…",
            "one helper cuts for both roads since story 2.3.3's second code round — this one used "
            + "to cut with no sign at all while the startup road marked its cuts");
    }

    [Fact]
    public void TheInstanceTheHostWasGiven_SurvivesASettingsRebuild()
    {
        // gemini, on the plan round, and it is the finding that would have made this whole story
        // write nowhere in the case people actually hit: `PanelServiceHost.Build()` runs AGAIN every
        // time the settings file moves, and a host that passed its Noticing once would hand the
        // rebuilt service nothing. Asserted on identity, because "a Noticing" is not the point — THE
        // one the host was given is.
        var mine = Collecting;
        var host = new PanelServiceHost(
            name => name == "COAI_DATA_DIR" ? _dir.Path : null,
            VaultKeys.None("no vault in this test"), default, new ProcessLauncher(),
            Serilog.Core.Logger.None, mine);

        Held(host.Current).Should().BeSameAs(mine, "the service the host built has the host's own");

        // Move the settings file, which is what makes `Current` rebuild.
        File.WriteAllText(Path.Combine(_dir.Path, SettingsFile.Name),
            """{"COAI_VENDORS": "[{\"id\":\"codex\",\"runtime\":\"codex\",\"model\":\"\",\"baseUrl\":\"\"}]"}""");

        var rebuilt = host.Current;

        Held(rebuilt).Should().BeSameAs(mine, "and so does the one it rebuilt when the file moved");
    }

    /// <summary>What a service is writing its notices through, asked of the field itself.</summary>
    /// <remarks>
    /// Reflection because the alternative is exposing the field for a test, and what is being checked
    /// is an IDENTITY — that the instance handed to the host is the instance the service holds — which
    /// no public surface should have to answer.
    /// </remarks>
    private static Noticing Held(PanelService service)
    {
        var field = typeof(PanelService).GetField(
            "_noticing",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        field.Should().NotBeNull(
            "a rename of PanelService's field would otherwise make this test throw a "
            + "NullReferenceException instead of saying what it could not find");

        return (Noticing)field!.GetValue(service)!;
    }

    [Fact]
    public void NoProductionCodeReachesForTheSharedWriterBehindTheHost()
    {
        // The ownership guarantee, as a census: the host is handed ONE composition and every service
        // it builds gets that one. A fourth file reaching for the static is a road that writes
        // somewhere else — or nowhere — while every injected test passes.
        ProductionSources.FilesMentioning("NoticeWriter.Shared").Keys.Should()
            .Equal(["src_mcp/src/Program.cs"],
                "ONE road: the writer is taken once in Program, composed into a Noticing there, and "
                + "handed down. Refusal reached for the static until this story — with reviewer "
                + "failures on the host's instance and refusals on the shared one, draining or "
                + "replacing either would have split the ledger the page reads (codex)");
        ProductionSources.FilesMentioning("Noticing.Through(").Keys.Should()
            .Equal(["src_mcp/src/Program.cs"], "one composition, in one place");
        ProductionSources.FilesMentioning("Noticing.None").Keys.Should()
            .Equal(["src_mcp/src/Program.cs"],
                "and the only production users of the silent one are the one-shot modes, composed there, which "
                + "answer on stdout and exit — a writer thread there is one nobody drains");
    }

    private static NormalisedReview Reviewed() => new([], []);

    private sealed class Sink(List<string> said) : Serilog.Core.ILogEventSink
    {
        public void Emit(Serilog.Events.LogEvent logEvent)
        {
            lock (said)
            {
                said.Add(logEvent.RenderMessage());
            }
        }
    }
}
