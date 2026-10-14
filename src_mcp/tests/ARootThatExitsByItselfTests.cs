using CoaiMcp.Runners.Processes;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// A child the launcher ends itself may leave BY ITSELF — and the tree it started must not outlive it unnoticed (the
/// CodeRabbit finding on PR #790 against story 6.1a of <c>todo/PLAN_every_round_is_counted.md</c>).
/// </summary>
/// <remarks>
/// <para>Against a REAL child, like <see cref="AReaderIsEndedWithItsTreeTests"/>, whose helpers are shared: the fake CLI's
/// <c>serve-quit</c> verb never answers and exits 0 after a delay, with — as asked — a sleeping grandchild that holds the
/// launcher's pipes (<c>grandchild</c>), holds none (<c>grandchild-mute</c>), or does not exist (<c>alone</c>).</para>
/// <para>What is pinned: a descendant listed while the root ran is ended when the root exits (T1, T4); one nobody listed
/// but that still holds the pipes is raised as a partial tree, tracked only (T2, T5); and an exit with nothing behind it
/// is an ordinary result, with no grace wait and no exception (T3).</para>
/// </remarks>
[Collection("fakecli-env")]
public sealed class ARootThatExitsByItselfTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("coai-reader-quit-").FullName;

    /// <summary>An hour: the listing leg's first tick never comes, so nothing is seen before the root exits.</summary>
    private static readonly TimeSpan NoListing = TimeSpan.FromHours(1);

    /// <summary>The predicate that never matches: the child never answers, so only its own exit can end the wait.</summary>
    private static bool NeverAnswered(string stdout) => false;

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
            // A pid file a dying child still holds is the OS's to clean.
        }
    }

    [Fact]
    public async Task ARootThatExitsByItself_EndsTheGrandchildItSawWhileRunning()
    {
        Assert.SkipUnless(ProcessTree.Supported, "the tree discipline lists descendants on Linux and Windows only");
        var pids = Path.Combine(_dir, "pids");
        var recorder = new AReaderIsEndedWithItsTreeTests.Recorder();

        try
        {
            var result = await new ProcessLauncher(recorder).RunAsync(Quit(pids, "grandchild", 1500, tracked: true));

            var (root, grandchild) = (PidsIn(pids)[0], PidsIn(pids)[1]);
            result.Ended.Should().BeFalse("the root left by itself; nothing ended it");
            result.TimedOut.Should().BeFalse();
            result.Survivors.Should().BeEmpty("the grandchild was ended and confirmed gone");
            AReaderIsEndedWithItsTreeTests.Alive(grandchild).Should().BeFalse("a descendant listed while the root ran is ended on the root's exit");
            recorder.Recorded.Should().Contain(root).And.Contain(grandchild, "the grandchild was written down as it appeared");
        }
        finally
        {
            KillAll(pids);
        }
    }

    [Fact]
    public async Task ARootThatExitsByItself_WhileAnUnlistedProcessHoldsItsPipes_IsRaisedAsAPartialTree()
    {
        Assert.SkipUnless(ProcessTree.Supported, "the tree discipline lists descendants on Linux and Windows only");
        var pids = Path.Combine(_dir, "pids");
        var launcher = new ProcessLauncher(new AReaderIsEndedWithItsTreeTests.Recorder());
        var request = Quit(pids, "grandchild", 0, tracked: true) with { DrainGrace = TimeSpan.FromMilliseconds(500) };
        Exception? thrown = null;

        try
        {
            try
            {
                await launcher.RunAsync(request);
            }
            catch (Exception e)
            {
                thrown = e;
            }

            thrown.Should().BeOfType<ProcessTreeNotRecordedException>("something never listed held the pipes after the root left")
                .Which.Survivors.Should().BeEmpty();
            thrown!.InnerException.Should().BeOfType<ProcessTreeUnlistedException>("the reason is its own type, which the server logs by name");
        }
        finally
        {
            KillAll(pids);
        }
    }

    [Fact]
    public async Task ARootThatExitsByItself_WithClosedStreamsAndNothingSeen_IsAnOrdinaryNoAnswer()
    {
        Assert.SkipUnless(ProcessTree.Supported, "the tree discipline lists descendants on Linux and Windows only");
        var pids = Path.Combine(_dir, "pids");
        var recorder = new AReaderIsEndedWithItsTreeTests.Recorder();

        var result = await new ProcessLauncher(recorder).RunAsync(Quit(pids, "alone", 100, tracked: true));

        result.Ended.Should().BeFalse();
        result.TimedOut.Should().BeFalse();
        result.Survivors.Should().BeEmpty();
        result.Truncated.Should().BeFalse("the streams closed with the root, so no grace was waited");
        result.ExitCode.Should().Be(0);
        recorder.Recorded.Should().Contain(PidsIn(pids).Single(), "the root was recorded");
        recorder.Recorded.Should().OnlyContain(pid => pid == PidsIn(pids).Single(), "a root with no sleeper lists nothing else");
    }

    [Fact]
    public async Task AGrandchildHoldingNoPipe_IsStillEndedOnTheRootsExit_BecauseItWasListed()
    {
        Assert.SkipUnless(ProcessTree.Supported, "the tree discipline lists descendants on Linux and Windows only");
        var pids = Path.Combine(_dir, "pids");

        try
        {
            var result = await new ProcessLauncher(new AReaderIsEndedWithItsTreeTests.Recorder())
                .RunAsync(Quit(pids, "grandchild-mute", 1500, tracked: true));

            result.Truncated.Should().BeFalse("it held none of the pipes, so the streams closed with the root");
            result.Survivors.Should().BeEmpty();
            AReaderIsEndedWithItsTreeTests.Alive(PidsIn(pids)[1]).Should().BeFalse("it held no pipe, and was ended only because the listing leg saw it");
        }
        finally
        {
            KillAll(pids);
        }
    }

    [Fact]
    public async Task AnUntrackedRequest_EndsWhatItSawOnTheRootsExit()
    {
        Assert.SkipUnless(ProcessTree.Supported, "the tree discipline lists descendants on Linux and Windows only");
        var pids = Path.Combine(_dir, "pids");

        try
        {
            var result = await new ProcessLauncher().RunAsync(Quit(pids, "grandchild", 1500, tracked: false));

            result.Truncated.Should().BeFalse();
            AReaderIsEndedWithItsTreeTests.Alive(PidsIn(pids)[1]).Should().BeFalse("untracked still ends what the listing leg saw");
        }
        finally
        {
            KillAll(pids);
        }
    }

    [Fact]
    public async Task AnUntrackedRequest_WithAnUnlistedHolderOfThePipes_SaysSoByTruncatedAlone()
    {
        Assert.SkipUnless(ProcessTree.Supported, "the tree discipline lists descendants on Linux and Windows only");
        var pids = Path.Combine(_dir, "pids");
        var request = Quit(pids, "grandchild", 0, tracked: false) with { DrainGrace = TimeSpan.FromMilliseconds(500) };

        try
        {
            var result = await new ProcessLauncher().RunAsync(request);

            result.Truncated.Should().BeTrue("the stream was still open when the drain grace ran out; nothing is tracked, so nothing is raised");
        }
        finally
        {
            KillAll(pids);
        }
    }

    private static ProcessRequest Quit(string pidFile, string tree, int quitAfterMs, bool tracked) =>
        new(FakeCliInvocations.Exe, ["serve-quit", pidFile, tree, quitAfterMs.ToString()], AppContext.BaseDirectory)
        {
            Timeout = TimeSpan.FromSeconds(30),
            EndsWhen = NeverAnswered,
            TrackAs = tracked ? "reader" : string.Empty,
            // A first tick that never comes where the root must be gone before anything is listed; the product's own step elsewhere.
            ListingStep = quitAfterMs == 0 ? NoListing : ProcessTree.ListingStep,
        };

    /// <summary>The pids the child wrote: its own, then its grandchild's when it has one.</summary>
    private static int[] PidsIn(string file) => [.. File.ReadAllLines(file).Select(int.Parse)];

    /// <summary>
    /// The grandchild the child left, ended by its IDENTITY (pid and start time; never by image name) — only while it is still
    /// that process. The root is skipped: it left by contract, and its number may be a stranger's by now.
    /// </summary>
    private static void KillAll(string file) =>
        AReaderIsEndedWithItsTreeTests.IdentitiesIn(file, skipLines: 1).ToList().ForEach(AReaderIsEndedWithItsTreeTests.KillIfAlive);
}
