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
/// <c>.WaitAsync(</c> with a timeout whose argument list carries no clock. The argument list is read to its closing parenthesis on the
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
    private const string TurnLoop = "src_mcp/runners/Reviewers/TurnLoop.cs";
    private const string SourceResolver = "src_mcp/runners/Feature/SourceResolver.cs";

    /// <summary>The product files judged. Each seam story adds its class here, red first, then green once the seam is in.</summary>
    private static readonly string[] Clocked = [BoundedScheduler, TurnLoop, SourceResolver];

    /// <summary>A site that is allowed to stay, and why. <paramref name="Snippet"/> must occur in exactly one site's statement of <paramref name="File"/>.</summary>
    private sealed record Allowed(string File, string Snippet, string Reason);

    private static readonly Allowed[] Sanctioned =
    [
        new(BoundedScheduler, "Stopwatch.StartNew(); if (!await Took(", "how long the reviewer queued for a slot: REPORTED on the abandoned outcome, decides nothing the scheduler's clock does"),
        new(BoundedScheduler, "var queued = System.Diagnostics.Stopwatch.StartNew(); Report(", "how long the reviewer queued for a shared resource: reported, decides nothing"),
        new(BoundedScheduler, "Stopwatch.StartNew(); var loop = new TurnLoop(", "the conversation's elapsed as REPORTED; the loop's own cap reads the clock the scheduler hands it (S4)"),
        new(BoundedScheduler, "Stopwatch.StartNew(); var attempts = 1;", "DEFERRED (Q4): the 429 ladder's elapsed, judged against the ladder's budget — named, not moved, until a 429-ladder failure is kept (D5)"),
        new(BoundedScheduler, "await Task.Delay(wait, ct);", "DEFERRED (Q4): the 429 ladder's wait — named, not moved, until a 429-ladder failure is kept (D5)"),
    ];

    /// <summary>One shape of unclocked budget: what it is called, how it is found, and whether it has an argument list to inspect.</summary>
    private sealed record Rule(string Kind, Regex Pattern, bool HasArguments, bool TokenAloneIsFine);

    private static Regex Re(string pattern) => new(pattern, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2));

    private static readonly Rule[] Rules =
    [
        new("Stopwatch", Re(@"\bStopwatch\b"), HasArguments: false, TokenAloneIsFine: false),
        new("CancelAfter", Re(@"\.\s*CancelAfter\s*\("), HasArguments: true, TokenAloneIsFine: false),
        new("DateTime.UtcNow", Re(@"\bDateTime(?:Offset)?\s*\.\s*(?:UtcNow|Now)\b"), HasArguments: false, TokenAloneIsFine: false),
        new("Task.Delay", Re(@"\bTask\s*\.\s*Delay\s*\("), HasArguments: true, TokenAloneIsFine: false),
        new("WaitAsync", Re(@"\.\s*WaitAsync\s*\("), HasArguments: true, TokenAloneIsFine: true),
    ];

    /// <summary>An argument list that names a clock: the field <c>_clock</c>, a parameter <c>clock</c> or a <c>TimeProvider</c>.</summary>
    private static readonly Regex NamesAClock = Re(@"\b(?:_clock|clock|TimeProvider\w*)\b");

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
        rule.HasArguments && (NamesAClock.IsMatch(arguments) || (rule.TokenAloneIsFine && TokenAlone.IsMatch(arguments)));

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
            "a Stopwatch, CancelAfter, DateTime.UtcNow, Task.Delay or WaitAsync(timeout) that does not read the class's clock is the race the manual clock cannot see: "
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
                }
            }
            """);

        SitesIn("Synthetic.cs", code).Select(site => site.Kind).Should().BeEquivalentTo(
            ["Stopwatch", "CancelAfter", "DateTime.UtcNow", "Task.Delay", "WaitAsync", "WaitAsync"],
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
