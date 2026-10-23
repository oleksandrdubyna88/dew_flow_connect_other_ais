using System.Diagnostics;
using System.Text.RegularExpressions;
using CoaiMcp.Core.Notices;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The redactor finishes, on text nobody chose, and says so rather than letting it through.
/// </summary>
/// <remarks>
/// <para><b>Why this exists.</b> Three of the five patterns could not take
/// <c>RegexOptions.NonBacktracking</c> — two need an ASCII lookbehind, which that engine forbids, and
/// the third also sizes an automaton past its node ceiling. So for those three the bound on the
/// search is a bound somebody has to state, and the plan round was right that the prose said
/// "bounded repetition" while nothing checked it.</para>
/// <para>The cost had been MEASURED — 126 ms over fourteen adversarial inputs — and a measurement is
/// evidence about the inputs that were tried. These are the inputs that were not: the shapes that
/// make a backtracking engine retry, at a length a person can paste into a chat.</para>
/// </remarks>
public sealed class TheRedactorCannotBeMadeToHangTests
{
    /// <summary>
    /// What a value must not take, including the ceiling itself and a generous margin for CI.
    /// </summary>
    /// <remarks>
    /// Above the 2 s pattern ceiling rather than below it: the assertion is that the WHOLE call
    /// returns, either by finishing or by giving up, and a test that expected less than the ceiling
    /// would be asserting a performance figure instead of a guarantee.
    /// </remarks>
    private static readonly TimeSpan Ceiling = TimeSpan.FromSeconds(10);

    public static TheoryData<string, string> AdversarialShapes()
    {
        var data = new TheoryData<string, string>
        {
            // A near-miss for the bearer pattern: the word, the space, then a long run of the value
            // class that never terminates in a way the pattern can accept.
            { "bearer " + new string('a', 200_000), "a bearer value far past its own bound" },
            // The same, one character short of the minimum every time, repeated.
            { string.Concat(Enumerable.Repeat("bearer abcdefg ", 20_000)), "twenty thousand near-misses" },
            // A URL authority that never reaches its `@`.
            { "https://" + new string('u', 200_000), "an authority with no at-sign" },
            { "https://" + new string('u', 100_000) + ":" + new string('p', 100_000), "a colon and no at-sign" },
            // A vendor prefix followed by a run longer than its bound.
            { "sk-" + new string('k', 200_000), "a vendor key past its bound" },
            { string.Concat(Enumerable.Repeat("sk-abcdefg", 20_000)), "twenty thousand short vendor keys" },
            // A Moonshot id prefix with a run past its bound and no digit: the digit lookahead must stay bounded.
            { "org-" + new string('a', 200_000), "a Moonshot id with no digit, past its bound" },
            { string.Concat(Enumerable.Repeat("ak-abcdefgh", 20_000)), "twenty thousand digitless Moonshot ids" },
            // The two NonBacktracking patterns, for completeness: they cannot backtrack at all, and
            // a case that proves the suite would notice if one lost the flag.
            { "?" + new string('n', 60) + "=" + new string('v', 200_000), "a parameter value past its bound" },
            { new string('n', 60) + "=" + new string('v', 200_000), "a labelled value past its bound" },
            // Mixed, so no single pattern can be the only one doing work.
            { string.Concat(Enumerable.Repeat("bearer sk-https://u:p", 10_000)), "every shape, interleaved" },
        };

        return data;
    }

    [Theory]
    [MemberData(nameof(AdversarialShapes))]
    public void AnAdversarialValue_IsAnsweredRatherThanSearchedForEver(string value, string what)
    {
        var clock = Stopwatch.StartNew();

        var answer = Redaction.SafeText(value, Redaction.DetailLimit);

        clock.Stop();
        clock.Elapsed.Should().BeLessThan(Ceiling,
            "{0} made the redactor search for {1:0} ms. Every repetition in these patterns is "
            + "bounded and nothing nests, and three of them carry a match ceiling for exactly this "
            + "case — a value that could hang this loop would hang the server writing the notice.",
            what, clock.Elapsed.TotalMilliseconds);
        answer.Length.Should().BeLessThanOrEqualTo(Redaction.DetailLimit + 32,
            "whatever happened, the answer is cut to the limit");
    }

    /// <summary>The two real sizes: a security-lane file (<c>SecuritySignals.MaxFileCharacters</c>) and the ACP stderr bound.</summary>
    public static TheoryData<string, int> LineShapesAtTheRealSizes()
    {
        var data = new TheoryData<string, int>();
        foreach (var size in (int[])[262_144, 65_536])
        {
            foreach (var shape in LineShapes)
            {
                data.Add(shape, size);
            }
        }

        return data;
    }

    private static readonly string[] LineShapes =
    [
        "labels-that-decline", "one-endless-block", "blank-heavy-block", "huge-indentation", "label-value-pairs", "fifty-thousand-pairs", "no-line-break", "unclosed-triple-quote",
    ];

    /// <summary>
    /// A line-spanning shape of about <paramref name="size"/> characters — the adversarial inputs of the
    /// continuation pass (plan D7): every shape the cursor could be made to rescan.
    /// </summary>
    private static string LineShape(string shape, int size) => shape switch
    {
        "labels-that-decline" => Repeat("api_key:\n", size),
        "one-endless-block" => "api_key: |\n" + Repeat("  " + new string('x', 98) + "\n", size),
        "blank-heavy-block" => "api_key: |\n" + Repeat("  x\n\n", size),
        "huge-indentation" => Repeat(new string(' ', size / 4) + "api_key:\n" + new string(' ', (size / 4) + 1) + "v\n", size),
        "label-value-pairs" => Repeat("token:\n  v\n", size),
        "fifty-thousand-pairs" => string.Concat(Enumerable.Repeat("token:\n  v\n", 50_000)),
        "no-line-break" => new string('x', size),
        "unclosed-triple-quote" => "password = \"\"\"\n" + Repeat("FAKE-LINE\n", size),
        _ => throw new ArgumentOutOfRangeException(nameof(shape)),
    };

    private static string Repeat(string unit, int size)
    {
        var text = new System.Text.StringBuilder(size + unit.Length);
        while (text.Length < size)
        {
            text.Append(unit);
        }

        return text.ToString();
    }

    [Theory]
    [MemberData(nameof(LineShapesAtTheRealSizes))]
    public void ALineSpanningShape_IsAnsweredUnderTheCeiling_ThroughAllThreeEntryPoints(string shape, int size)
    {
        var value = LineShape(shape, size);
        var timings = new List<string>();

        foreach (var (entry, run) in (IEnumerable<(string, Func<string>)>)
        [
            ("SafeText", () => Redaction.SafeText(value, Redaction.DetailLimit)),
            ("SafeSource", () => Redaction.SafeSource(value)),
            ("FirstSecretClass", () => Redaction.FirstSecretClass(value)),
        ])
        {
            var clock = Stopwatch.StartNew();
            run();
            clock.Stop();
            timings.Add($"{entry} {clock.Elapsed.TotalMilliseconds:0} ms");
            clock.Elapsed.Should().BeLessThan(Ceiling,
                "{0} at {1} characters took {2:0} ms through {3} — the pass has one advancing cursor and no pattern, so what it visits it visits a bounded number of times",
                shape, value.Length, clock.Elapsed.TotalMilliseconds, entry);
        }

        TestContext.Current.SendDiagnosticMessage($"MEASURED {shape} @ {value.Length}: {string.Join(", ", timings)}");
    }

    [Fact]
    public void EveryPattern_IsNonBacktrackingOrSaysWhyNot()
    {
        // The mechanical half of the same guarantee: a later edit that adds a sixth pattern, or
        // takes the flag off one of the two that carry it, fails here rather than in production.
        Redaction.Patterns.Should().NotBeEmpty("a scan of no patterns would pass for ever");

        foreach (var pattern in Redaction.Patterns)
        {
            var nonBacktracking = pattern.Regex.Options.HasFlag(RegexOptions.NonBacktracking);

            (nonBacktracking || pattern.WhyNotNonBacktracking.Length > 0).Should().BeTrue(
                "{0} neither uses NonBacktracking nor says why it cannot", pattern.Name);

            if (!nonBacktracking)
            {
                // And a pattern that cannot have the engine must have the ceiling instead: a stated
                // reason is an explanation, not a bound.
                pattern.Regex.MatchTimeout.Should().NotBe(Regex.InfiniteMatchTimeout,
                    "{0} runs on the backtracking engine with no ceiling at all, so the reason it "
                    + "gives for not using NonBacktracking is an explanation rather than a guarantee",
                    pattern.Name);
            }
        }
    }

    [Fact]
    public void ATimeoutFailsCLOSED_SoTextNobodyCanVouchForIsNotWrittenDown()
    {
        // The branch the ceiling exists to reach, exercised directly rather than by hoping an
        // adversarial string finds it. The tempting shape is to return the value as it arrived when
        // the redactor gives up — and that is the one outcome that must never happen, because a
        // redactor that could not finish does not know whether the text is clean.
        Func<string> timedOut = () => throw new RegexMatchTimeoutException(
            "a value shaped to make the engine search", "a pattern", TimeSpan.Zero);

        Redaction.WhenRedactionTimesOut(timedOut).Should().Be("[redacted]",
            "a value whose redaction could not finish is not a value this server may write");
    }
}
