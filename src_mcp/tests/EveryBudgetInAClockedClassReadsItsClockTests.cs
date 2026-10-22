using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// In a class that owns a <see cref="TimeProvider"/>, every budget that decides an outcome reads THAT clock
/// (<c>todo/PLAN_the_suite_is_deterministic_and_leaves_no_temp_E4.md</c> §5.1, §8 S1 "The structural scan").
/// </summary>
/// <remarks>
/// <para>The mistake this guards is per class and invisible: a <c>Stopwatch</c> left beside an injected timer, or a <c>CancelAfter</c> on a
/// linked source that still uses the system timer, is the same race again while the test on the manual clock goes green. So over a NAMED
/// SET of product files the scan flags every <c>Stopwatch</c>, <c>.CancelAfter(</c>, <c>DateTime.UtcNow</c>, <c>Task.Delay(</c> and
/// <c>.WaitAsync(</c> with a timeout, <c>new CancellationTokenSource(&lt;args&gt;)</c>, <c>new PeriodicTimer(</c>, <c>new Timer(</c> and <c>Thread.Sleep(</c> whose argument list carries no clock (<c>TimeProvider.System</c> is the system timer and does not count as one; <c>new CancellationTokenSource()</c> arms nothing and is exempt). The argument list is read to its closing parenthesis on the
/// ONE joined line <see cref="ProductionSources"/> hands out, so a wrapped <c>WaitAsync(⏎ usageBound)</c> is not missed and a word in a
/// comment or a string is not a site.</para>
/// <para><b>Sanctioned sites</b> are an allow-list of <c>(file, snippet, reason)</c>; the companion fails when an allowed snippet is no
/// longer found exactly once — so a site that moves onto the clock must also leave the list, and a stale list is red, not silent.
/// <b>The set grows per story</b>, and that is each seam story's structural RED: S1 lands the scan with <c>BoundedScheduler</c> only;
/// S2 adds <c>AcpClient.cs</c> (red on its <c>CancelAfter</c> and its bare <c>WaitAsync</c> before the seam, green after, the drain grace
/// allowed); S3 adds <c>ConsultantPlaces.cs</c>; S4 adds <c>TurnLoop.cs</c> and <c>SourceResolver.cs</c>. It is NOT over
/// <c>ReviewSnapshots.cs</c>, <c>ProcessLauncher.cs</c> or <c>ReviewerExecutor.cs</c>: the plan names their sites as callees that do not
/// move, and a later story that seams one adds it to the set with its own red.</para>
/// </remarks>
public sealed class EveryBudgetInAClockedClassReadsItsClockTests
{
    private const string BoundedScheduler = "src_mcp/runners/Reviewers/BoundedScheduler.cs";
    private const string AcpClientFile = "src_mcp/runners/Acp/AcpClient.cs";
    private const string ConsultantPlaces = "src_mcp/src/Server/Consultation/ConsultantPlaces.cs";
    private const string TurnLoop = "src_mcp/runners/Reviewers/TurnLoop.cs";
    private const string SourceResolver = "src_mcp/runners/Feature/SourceResolver.cs";

    /// <summary>The product files judged. Each seam story adds its class here, red first, then green once the seam is in.</summary>
    private static readonly string[] Clocked = [BoundedScheduler, AcpClientFile, ConsultantPlaces, TurnLoop, SourceResolver];

    /// <summary>A product file whose constructor takes a <see cref="TimeProvider"/> and is NOT in <see cref="Clocked"/>, with the one-line reason (judge round G2).</summary>
    private sealed record NotScanned(string File, string Reason);

    /// <summary>
    /// The product files that take a clock in a constructor and are deliberately not scanned. Each reads its injected clock only by comparing
    /// instants (<c>GetUtcNow</c>, <c>GetTimestamp</c>) and carries none of the scan's shapes today; a later timer in one of them moves it to
    /// <see cref="Clocked"/>, red first. The companion fails when a file with a clock constructor is in neither list, and when an entry here no longer has one.
    /// </summary>
    private static readonly NotScanned[] Unscanned =
    [
        new("src_mcp/src/Server/Backfill/BackfillAttribution.cs", "forwards its clock to the peer lookup (AuthorPeers.Find) as a deadline instant; no timer, no wait — E5/E7's own tests pin it"),
        new("src_mcp/src/Server/Backfill/BackfillRun.cs", "only defaults and forwards the clock to BackfillAttribution; it arms nothing itself"),
        new("src_mcp/src/Server/Rounds/RoundAuthorsStep.cs", "reads its clock as instants (a deadline, an elapsed from GetTimestamp) for the round-authors step; no timer, no wait"),
        new("src_mcp/src/Server/SessionHolds.cs", "a wall STAMP only: a cached entry's age against the ten-minute recheck; it waits for nothing"),
        new("src_mcp/src/Store/ObservedSession.cs", "ReadBudget carries a clock for the transcript read's MaxTime as an instant comparison; the read arms no timer"),
        new("src_mcp/src/Store/RoundsDb.cs", "a wall STAMP for its rows' columns (the clock is what a test controls); its one DateTime.UtcNow is the session row's opened_utc, a stamp and not a budget"),
        new("src_mcp/src/Collecting/CollectRun.cs", "a wall STAMP only: the run id is the clock's instant; the run waits for nothing"),
        new("src_bugs/src/RateLimiter.cs", "a rate window compared as instants (GetUtcNow ticks per subject); no timer, no wait — its own tests move the clock"),
    ];

    /// <summary>A site that is allowed to stay, and why. <paramref name="Snippet"/> must occur in exactly one site's statement of <paramref name="File"/>.</summary>
    private sealed record Allowed(string File, string Snippet, string Reason);

    private static readonly Allowed[] Sanctioned =
    [
        new(BoundedScheduler, "Stopwatch.StartNew(); if (!await Took(", "how long the reviewer queued for a slot: REPORTED on the abandoned outcome, decides nothing the scheduler's clock does"),
        new(BoundedScheduler, "var queued = System.Diagnostics.Stopwatch.StartNew(); Report(", "how long the reviewer queued for a shared resource: reported, decides nothing"),
        new(BoundedScheduler, "Stopwatch.StartNew(); var loop = new TurnLoop(", "the conversation's elapsed as REPORTED; the loop's own cap reads the clock the scheduler hands it (S4)"),
        new(BoundedScheduler, "Stopwatch.StartNew(); var attempts = 1;", "DEFERRED (Q4): the 429 ladder's elapsed, judged against the ladder's budget — named, not moved, until a 429-ladder failure is kept (D5)"),
        new(BoundedScheduler, "await Task.Delay(wait, ct);", "DEFERRED (Q4): the 429 ladder's wait — named, not moved, until a 429-ladder failure is kept (D5)"),
        new(AcpClientFile, "reading.WaitAsync(AcpTransport.DrainGrace)", "TEARDOWN (E4 plan §5.1, the lead's Q1): the drain grace after the agent's tree was killed bounds a real pipe a survivor may hold; on a manual clock nobody advances it would hang, and it decides nothing the test races"),
    ];

    /// <summary>One shape of unclocked budget: what it is called, how it is found, and whether it has an argument list to inspect.</summary>
    private sealed record Rule(string Kind, Regex Pattern, bool HasArguments, bool TokenAloneIsFine, bool EmptyIsFine = false);

    private static Regex Re(string pattern) => new(pattern, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2));

    private static readonly Rule[] Rules =
    [
        new("Stopwatch", Re(@"\bStopwatch\b"), HasArguments: false, TokenAloneIsFine: false),
        new("CancelAfter", Re(@"\.\s*CancelAfter\s*\("), HasArguments: true, TokenAloneIsFine: false),
        new("DateTime.UtcNow", Re(@"\bDateTime(?:Offset)?\s*\.\s*(?:UtcNow|Now)\b"), HasArguments: false, TokenAloneIsFine: false),
        new("Task.Delay", Re(@"\bTask\s*\.\s*Delay\s*\("), HasArguments: true, TokenAloneIsFine: false),
        new("WaitAsync", Re(@"\.\s*WaitAsync\s*\("), HasArguments: true, TokenAloneIsFine: true),
        // The seam's OWN shape (judge round M1): the timer source the clock replaced. `new CancellationTokenSource()` arms nothing and is exempt;
        // `new CancellationTokenSource(budget)` arms the system timer and is a site unless its arguments name a clock.
        new("new CancellationTokenSource(", Re(@"\bnew\s+CancellationTokenSource\s*\("), HasArguments: true, TokenAloneIsFine: false, EmptyIsFine: true),
        new("new PeriodicTimer(", Re(@"\bnew\s+PeriodicTimer\s*\("), HasArguments: true, TokenAloneIsFine: false),
        new("new Timer(", Re(@"\bnew\s+(?:System\s*\.\s*Threading\s*\.\s*)?Timer\s*\("), HasArguments: true, TokenAloneIsFine: false),
        new("Thread.Sleep", Re(@"\bThread\s*\.\s*Sleep\s*\("), HasArguments: true, TokenAloneIsFine: false),
    ];

    /// <summary>An argument list that names a clock: the field <c>_clock</c>, a parameter <c>clock</c> or a <c>TimeProvider</c>.</summary>
    private static readonly Regex NamesAClock = Re(@"\b(?:_clock|clock|TimeProvider\w*)\b");

    /// <summary><c>TimeProvider.System</c> IS the system timer: handing it to a budget is the race spelled with a clock's name, so it is removed before <see cref="NamesAClock"/> looks (judge round M1).</summary>
    private static readonly Regex TheSystemClock = Re(@"\bTimeProvider\s*\.\s*System\b");

    /// <summary><c>WaitAsync(ct)</c> bounds a wait by a token and arms no timer: not a budget.</summary>
    private static readonly Regex TokenAlone = Re(@"^\s*(?:[\w.]+\.)?(?:ct|token|cancellationToken|\w*Token|None)\s*$");

    /// <summary>A call the scan found: where, what shape, the call itself and the statement around it (to tell two alike sites apart).</summary>
    private sealed record Site(string File, string Kind, string Call, string Statement);

    private static IEnumerable<Site> SitesIn(string file, string code) =>
        Rules.SelectMany(rule => rule.Pattern.Matches(code).SelectMany(match => SiteOf(file, code, rule, match)));

    /// <summary>The site, or nothing at all (an empty sequence) when the call names a clock or is a token alone.</summary>
    private static IEnumerable<Site> SiteOf(string file, string code, Rule rule, Match match)
    {
        var end = rule.HasArguments ? ClosingParen(code, match.Index + match.Length - 1) : match.Index + match.Length - 1;
        var call = code[match.Index..(end + 1)];
        var arguments = rule.HasArguments ? code[(match.Index + match.Length)..end] : string.Empty;
        return IsClocked(rule, arguments) ? [] : [new Site(file, rule.Kind, call, StatementAround(code, match.Index, end))];
    }

    private static bool IsClocked(Rule rule, string arguments) =>
        rule.HasArguments && (EmptyAndFine(rule, arguments) || NamesAClockIn(arguments) || TokenAloneAndFine(rule, arguments));

    /// <summary>An empty argument list on a rule that exempts it (<c>new CancellationTokenSource()</c> arms nothing).</summary>
    private static bool EmptyAndFine(Rule rule, string arguments) => rule.EmptyIsFine && arguments.Trim().Length == 0;

    /// <summary>A clock named in the arguments — <c>TimeProvider.System</c> does not count: it is the system timer.</summary>
    private static bool NamesAClockIn(string arguments) => NamesAClock.IsMatch(TheSystemClock.Replace(arguments, string.Empty));

    /// <summary>A token alone on a rule that accepts it (<c>WaitAsync(ct)</c> arms no timer).</summary>
    private static bool TokenAloneAndFine(Rule rule, string arguments) => rule.TokenAloneIsFine && TokenAlone.IsMatch(arguments);

    private static int Depth(char c) => c switch { '(' => 1, ')' => -1, _ => 0 };

    /// <summary>The index of the parenthesis that closes the one at <paramref name="open"/>: an argument list is read across lines, not to the end of one.</summary>
    private static int ClosingParen(string code, int open)
    {
        var depth = 0;
        for (var at = open; at < code.Length; at++)
        {
            depth += Depth(code[at]);
            if (depth == 0)
            {
                return at;
            }
        }

        return code.Length - 1;
    }

    /// <summary>From the statement's start to its end plus the next sixty characters: enough to tell two identical lines apart by their neighbour.</summary>
    private static string StatementAround(string code, int at, int end)
    {
        var from = code.LastIndexOfAny([';', '{', '}'], Math.Max(at - 1, 0)) + 1;
        var stop = code.IndexOf(';', end);
        var to = Math.Min((stop < 0 ? code.Length : stop + 1) + 60, code.Length);
        return code[from..to].Trim();
    }

    // ---- the companion: the scanned set cannot drift (judge round G2) ----

    private static readonly Regex PrimaryConstructor = Re(@"\b(?:class|record|struct)\s+\w+(?:<[^<>]*>)?\s*\(");

    private static readonly Regex DeclaredType = Re(@"\b(?:class|record|struct)\s+(\w+)");

    private static readonly Regex ATimeProvider = Re(@"\bTimeProvider\b");

    /// <summary>The text between the parenthesis at <paramref name="open"/> and the one that closes it.</summary>
    private static string ParametersAt(string code, int open) => code[(open + 1)..ClosingParen(code, open)];

    private static IEnumerable<string> PrimaryParameterLists(string code) =>
        PrimaryConstructor.Matches(code).Select(match => ParametersAt(code, match.Index + match.Length - 1));

    private static IEnumerable<Match> ExplicitConstructorsOf(string code, string type) =>
        Re($@"\b(?:public|internal|protected|private)\s+{Regex.Escape(type)}\s*\(").Matches(code);

    private static IEnumerable<string> ExplicitParameterLists(string code) =>
        DeclaredType.Matches(code).Select(match => match.Groups[1].Value).Distinct()
            .SelectMany(type => ExplicitConstructorsOf(code, type))
            .Select(match => ParametersAt(code, match.Index + match.Length - 1));

    /// <summary>Whether a constructor of a type declared in this code — primary, positional record or explicit — takes a <see cref="TimeProvider"/>. A static method that does is not a constructor.</summary>
    private static bool TakesAClockInAConstructor(string code) =>
        PrimaryParameterLists(code).Concat(ExplicitParameterLists(code)).Any(parameters => ATimeProvider.IsMatch(parameters));

    private static List<string> ProductFilesWithAClockConstructor() =>
        [.. ProductionSources.Files().Where(file => file.EndsWith(".cs", StringComparison.Ordinal) && TakesAClockInAConstructor(ProductionSources.CodeOf(file)))];

    [Fact]
    public void EveryProductFileWhoseConstructorTakesAClock_IsScanned_OrNamedAsNotScannedWithItsReason()
    {
        string[] unclassified =
        [
            .. ProductFilesWithAClockConstructor()
                .Where(file => !Clocked.Contains(file) && !Unscanned.Any(entry => entry.File == file)),
        ];

        string.Join("; ", unclassified).Should().BeEmpty(
            "a class that owns a clock is judged by the scan or named here with a reason; a hand-kept set that a new clocked class can miss is how the race comes back");
    }

    [Fact]
    public void TheNotScannedList_IsNotStale_AndEveryEntryHasAReason()
    {
        var withAClock = ProductFilesWithAClockConstructor();

        string[] stale = [.. Unscanned.Where(entry => !withAClock.Contains(entry.File) || Clocked.Contains(entry.File)).Select(entry => entry.File)];

        string.Join("; ", stale).Should().BeEmpty("an entry whose file no longer takes a clock (or is now scanned) must leave the list");
        Unscanned.Should().OnlyContain(entry => entry.Reason.Length > 20, "every unscanned file says why");
        Clocked.Should().OnlyContain(file => withAClock.Contains(file), "the scanned set holds only files that take a clock — a file that lost its clock leaves it");
    }

    [Fact]
    public void TheCompanion_SeesAClockInEveryConstructorShape_AndNotInAStaticMethodAProseOrAComment()
    {
        TakesAClockInAConstructor(ProductionSources.Joined("class A(IFoo foo, TimeSpan t, TimeProvider? clock = null) { }")).Should().BeTrue("a primary constructor");
        TakesAClockInAConstructor(ProductionSources.Joined("record B(long Max, TimeProvider Clock) { }")).Should().BeTrue("a positional record");
        TakesAClockInAConstructor(ProductionSources.Joined("class C { public C(Func<int, int> f, TimeProvider? clock = null) { } }")).Should().BeTrue("an explicit constructor");
        TakesAClockInAConstructor(ProductionSources.Joined("class D { internal static int Run(TimeProvider clock) => 1; }")).Should().BeFalse("a static method is not a constructor");
        TakesAClockInAConstructor(ProductionSources.Joined("class E(int n) { /* TimeProvider clock */ string s = \"TimeProvider\"; }")).Should().BeFalse("prose is not a parameter");
    }

    private static List<Site> SitesOf(IEnumerable<string> files) =>
        [.. files.SelectMany(file => SitesIn(file, ProductionSources.CodeOf(file)))];

    private static bool IsAllowedAt(Site site, Allowed allowed) =>
        site.File == allowed.File && site.Statement.Contains(allowed.Snippet, StringComparison.Ordinal);

    private static List<string> Offenders(IReadOnlyCollection<Site> sites, IReadOnlyCollection<Allowed> allowed) =>
    [
        .. sites
            .Where(site => !allowed.Any(entry => IsAllowedAt(site, entry)))
            .Select(site => $"{site.File}: {site.Kind} `{site.Call}` in `{site.Statement}`"),
    ];

    [Fact]
    public void EveryBudgetInAClockedClass_ReadsItsClock_ExceptTheSitesNamedWithTheirReason()
    {
        string.Join("; ", Offenders(SitesOf(Clocked), Sanctioned)).Should().BeEmpty(
            "a Stopwatch, CancelAfter, DateTime.UtcNow, Task.Delay, WaitAsync(timeout), new CancellationTokenSource(budget), PeriodicTimer, Timer or Thread.Sleep that does not read the class's clock is the race the manual clock cannot see: "
            + "the test on the manual clock goes green while the system timer still decides");
    }

    [Fact]
    public void TheScan_StillFindsEveryNamedException_SoAStaleAllowListIsRedNotSilent()
    {
        var sites = SitesOf(Sanctioned.Select(entry => entry.File).Distinct());

        string[] stale =
        [
            .. Sanctioned
                .Where(entry => sites.Count(site => IsAllowedAt(site, entry)) != 1)
                .Select(entry => $"{entry.File}: `{entry.Snippet}` matches {sites.Count(site => IsAllowedAt(site, entry))} sites, not one"),
        ];

        string.Join("; ", stale).Should().BeEmpty("a site that moved onto the clock (or moved away) must leave the allow-list; a stale entry would excuse a future site silently");
        Sanctioned.Select(entry => entry.File).Distinct().Should().BeSubsetOf(Clocked, "an allowed site in a file the scan does not judge is dead weight");
        Sanctioned.Should().OnlyContain(entry => entry.Reason.Length > 20, "every sanctioned site says why");
    }

    [Fact]
    public void TheScan_FindsTheFiveSitesOfBoundedScheduler_ThatTheAllowListNames()
    {
        var sites = SitesOf([BoundedScheduler]);

        sites.Select(site => site.Kind).Should().BeEquivalentTo(
            ["Stopwatch", "Stopwatch", "Stopwatch", "Stopwatch", "Task.Delay"],
            "the scheduler has four Stopwatch reads and the ladder's Task.Delay, and nothing else the scan judges (slot.WaitAsync(ct) is a token alone)");
    }

    [Fact]
    public void AnUnclockedBudget_IsASite_InEveryShapeTheScanKnows_AcrossLines_AndAClockedOneIsNot()
    {
        var code = ProductionSources.Joined("""
            class C
            {
                async Task A(Task t, CancellationToken ct, TimeSpan usageBound)
                {
                    var w = Stopwatch.StartNew();
                    source.CancelAfter(budget);
                    var now = DateTime.UtcNow;
                    await Task.Delay(wait, ct);
                    await t.WaitAsync(
                        usageBound);
                    await t.WaitAsync(timeout: 3, ct);
                    using var a = new CancellationTokenSource(budget);
                    using var b = new CancellationTokenSource(TimeSpan.FromSeconds(1), TimeProvider.System);
                    await t.WaitAsync(bound, TimeProvider.System);
                    using var c = new PeriodicTimer(period);
                    using var d = new Timer(Tick, null, 0, 1000);
                    using var e = new System.Threading.Timer(Tick, null, 0, 1000);
                    Thread.Sleep(5);
                }
            }
            """);

        SitesIn("Synthetic.cs", code).Select(site => site.Kind).Should().BeEquivalentTo(
            ["Stopwatch", "CancelAfter", "DateTime.UtcNow", "Task.Delay", "WaitAsync", "WaitAsync",
             "new CancellationTokenSource(", "new CancellationTokenSource(", "WaitAsync", "new PeriodicTimer(", "new Timer(", "new Timer(", "Thread.Sleep"],
            "each unclocked shape is a site, and a WaitAsync whose argument list wraps onto the next line is still read to its closing parenthesis");
    }

    [Fact]
    public void AClockedBudget_AToken_AWordInACommentOrAString_AreNotSites()
    {
        var code = ProductionSources.Joined("""
            class C
            {
                async Task A(Task t, CancellationToken ct)
                {
                    using var timer = new CancellationTokenSource(budget, _clock);
                    await Task.Delay(wait, _clock, ct);
                    await t.WaitAsync(bound, _clock);
                    await t.WaitAsync(ct);
                    await t.WaitAsync(linked.Token);
                    var s = "Stopwatch.StartNew() and x.CancelAfter(1) and Task.Delay(1)"; // Stopwatch, DateTime.UtcNow
                    /* source.CancelAfter(budget); */
                    var stamp = _clock.GetUtcNow();
                    using var none = new CancellationTokenSource();
                    using var clocked = new CancellationTokenSource(budget, _clock);
                    using var tick = new PeriodicTimer(period, _clock);
                    var t2 = "new Timer(Tick) and Thread.Sleep(1) and new PeriodicTimer(1)"; // new CancellationTokenSource(5)
                }
            }
            """);

        SitesIn("Synthetic.cs", code).Should().BeEmpty("a budget that names the clock, a token alone and prose are not the race");
    }

    [Fact]
    public void ASiteTheAllowListDoesNotName_IsAnOffender_AndOneItDoesNameIsNot()
    {
        var sites = SitesIn("Synthetic.cs", ProductionSources.Joined("class C { void A() { var a = Stopwatch.StartNew(); Go(); var b = Stopwatch.StartNew(); Stop(); } }")).ToList();
        Allowed[] allowed = [new("Synthetic.cs", "Stopwatch.StartNew(); Go();", "the first read is reported only, which is a reason")];

        Offenders(sites, allowed).Should().ContainSingle("only the second Stopwatch is unlisted").Which.Should().Contain("Stop();");
    }
}
