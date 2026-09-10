using System.Diagnostics;
using CoaiMcp.Runners.Processes;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The launcher's own ceilings, against a REAL child process.
/// </summary>
/// <remarks>
/// <para>Every one of these is about what happens between this process and a pipe, so none of them
/// can be written with a fake: a stubbed launcher cannot block on a full buffer, and a stubbed
/// child cannot decline to read.</para>
/// <para>The child is the suite's own <c>FakeCli</c>, for the reason
/// <see cref="FakeCliInvocations"/> gives — one process rather than <c>dotnet FakeCli.dll</c>'s
/// two, so a killed timeout leaves nothing behind.</para>
/// </remarks>
public sealed class ProcessLauncherTests
{
    /// <summary>Bigger than any pipe buffer on any platform here, so the write MUST block.</summary>
    /// <remarks>
    /// Windows' anonymous pipes default to 4 KiB and Linux's to 64 KiB. A prompt smaller than the
    /// buffer is delivered whether or not the child ever reads it, and a test built on one would
    /// pass against the defect.
    /// </remarks>
    private const int PastAnyPipeBuffer = 1024 * 1024;

    /// <summary>
    /// How long the test is willing to wait for a launch that is supposed to give up in 300 ms.
    /// </summary>
    /// <remarks>
    /// Generous, because this runs on CI machines that are sometimes very slow, and still an order
    /// of magnitude under the ten seconds the child would otherwise live for — the assertion is
    /// "the deadline decided", not "the deadline decided within a millisecond".
    /// </remarks>
    private static readonly TimeSpan Promptly = TimeSpan.FromSeconds(5);

    private static ProcessRequest Request(params string[] args) =>
        new(FakeCliInvocations.Exe, args, AppContext.BaseDirectory);

    [Fact]
    public async Task AChildThatNeverReadsItsStdinIsStillKilledOnTime()
    {
        // `sleep` does not touch stdin, so the megabyte below fills the pipe and stays there. The
        // deadline has to be able to end a launch that is stuck in the WRITE, which is the half of
        // the operation the timeout used to start after.
        var request = Request("sleep", "10000") with
        {
            StdIn = new string('p', PastAnyPipeBuffer),
            Timeout = TimeSpan.FromMilliseconds(300),
        };
        var clock = Stopwatch.StartNew();

        var result = await new ProcessLauncher().RunAsync(request, TestContext.Current.CancellationToken);

        clock.Elapsed.Should().BeLessThan(Promptly,
            "the launch was given 300 ms and the child ten seconds — whichever of the two runs out "
            + "first is the one that must decide");
        result.TimedOut.Should().BeTrue("the tree was killed, and the caller has to be able to tell");
    }

    [Fact]
    public async Task AChildThatNeverReadsItsStdinIsStillStoppedByItsCaller()
    {
        // The second clock. A Team-server job carries a cancellation token that a cancel or the
        // deadline sweep fires, and it must reach a launch stuck in the write — otherwise the
        // account's lock is held past the job's own budget by a review nobody is waiting for.
        using var caller = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
        var request = Request("sleep", "10000") with
        {
            StdIn = new string('p', PastAnyPipeBuffer),
            Timeout = TimeSpan.FromMinutes(5),
        };
        var clock = Stopwatch.StartNew();

        var result = await new ProcessLauncher().RunAsync(request, caller.Token);

        clock.Elapsed.Should().BeLessThan(Promptly);
        result.TimedOut.Should().BeTrue("the tree was killed — this launcher says so the same way for both clocks");
    }

    [Fact]
    public async Task AChildThatReadsEverythingStillGetsAllOfIt()
    {
        // The positive companion, and it is not decoration: the three tests above are all about a
        // launch ENDING early, and a launcher that delivered nothing at all would pass every one of
        // them. This is what fails if the write ever stops being awaited properly.
        var prompt = string.Concat(Enumerable.Range(0, PastAnyPipeBuffer / 16).Select(i => $"line {i,-9}\n"));
        var request = Request("echo-stdin") with { StdIn = prompt, Timeout = TimeSpan.FromMinutes(1) };

        var result = await new ProcessLauncher().RunAsync(request, TestContext.Current.CancellationToken);

        result.TimedOut.Should().BeFalse();
        result.ExitCode.Should().Be(0);
        result.StdOut.Should().Be(prompt, "every byte the caller sent, and not one the launcher added");
    }

    [Fact]
    public async Task AChildThatExitsWhileWeAreStillWritingIsNotAFailure()
    {
        // A CLI that refuses before it reads — an expired sign-in, a bad flag — closes its end of
        // the pipe mid-write. That is the child's decision and its own words are what matter; the
        // write's broken pipe must not travel out of the launcher as an exception.
        var request = Request("stderr-exit", "not signed in", "7") with
        {
            StdIn = new string('p', PastAnyPipeBuffer),
            Timeout = TimeSpan.FromMinutes(1),
        };

        var result = await new ProcessLauncher().RunAsync(request, TestContext.Current.CancellationToken);

        result.ExitCode.Should().Be(7);
        result.StdErr.Should().Contain("not signed in");
        result.TimedOut.Should().BeFalse();
    }

    [Fact]
    public async Task OutputPastTheCeilingIsTruncatedAndSaysSo()
    {
        // Not one newline in the whole 400 KB, which is the point. A line-based reader hands over
        // nothing until the stream closes, so a ceiling checked per line cannot fire before the
        // allocation it exists to prevent has already happened.
        var request = Request("spew", "400000") with
        {
            MaxOutputChars = 64 * 1024,
            Timeout = TimeSpan.FromMinutes(1),
        };

        var result = await new ProcessLauncher().RunAsync(request, TestContext.Current.CancellationToken);

        result.ExitCode.Should().Be(0, "a runaway is still allowed to finish; it is its OUTPUT that is bounded");
        result.StdOut.Length.Should().BeLessThan(64 * 1024 + 200, "the ceiling, plus the sentence that names it");
        result.StdOut.Should().Contain("truncated",
            "a silently cut answer is one a parser fails on for a reason nobody can find");
    }

    [Fact]
    public async Task OrdinaryOutputIsNotTruncated()
    {
        // The companion the ceiling needs: a bound that fires on everything is indistinguishable
        // from a bound that fires on nothing, and only one of them is caught by the test above.
        var request = Request("emit", "a perfectly ordinary answer") with { Timeout = TimeSpan.FromMinutes(1) };

        var result = await new ProcessLauncher().RunAsync(request, TestContext.Current.CancellationToken);

        result.StdOut.Should().Be("a perfectly ordinary answer");
    }
}
