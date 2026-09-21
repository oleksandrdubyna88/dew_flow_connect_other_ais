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
