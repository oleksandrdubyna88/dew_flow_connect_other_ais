using System.Diagnostics;
using CoaiMcp.Runners.Processes;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// A request that ends its child on an answer ends the WHOLE tree and confirms it gone — the launcher's half of the
/// gauge reader's containment (story 6.1a of <c>todo/PLAN_every_round_is_counted.md</c>).
/// </summary>
/// <remarks>
/// <para>Against a REAL child, like <see cref="ProcessLauncherTests"/>: the fake CLI's <c>serve</c> verb answers the
/// request carrying <c>"id":2</c> and then lives for ever — with, when asked, a sleeping grandchild of its own — so a
/// launch that merely stopped waiting would leave two processes behind, and one that killed the root alone would leave
/// one. The pids come from the child itself, written to a file, and each is checked by pid AND start time.</para>
/// <para>In the <c>fakecli-env</c> collection because it launches the fake CLI by verb, which is only read while
/// <c>FAKECLI_MODE</c> is unset.</para>
/// </remarks>
[Collection("fakecli-env")]
public sealed class AReaderIsEndedWithItsTreeTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("coai-reader-tree-").FullName;

    /// <summary>A one-line answer, as a stdio server writes it — and the predicate that spots it.</summary>
    private const string Answer = """{"id":2,"result":{"ok":true}}""";

    private static bool Answered(string stdout) => stdout.Contains("\"id\":2", StringComparison.Ordinal);

    /// <summary>Long enough that a child living it out is unmistakably the wrong answer; the tests end in seconds.</summary>
    private static readonly TimeSpan Generous = TimeSpan.FromSeconds(30);

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
    public async Task AnAnsweredServer_IsEndedWithItsGrandchild_AndBothAreConfirmedGone()
    {
        Assert.SkipUnless(ProcessTree.Supported, "the tree discipline lists descendants on Linux and Windows only");
        var pids = Path.Combine(_dir, "pids");
        var recorder = new Recorder();
        var launcher = new ProcessLauncher(recorder);
        var watch = Stopwatch.StartNew();

        var result = await launcher.RunAsync(Serve(pids, afterEofMs: -1, grandchild: true) with { EndsWhen = Answered });

        result.Ended.Should().BeTrue("the predicate saw the answer, and that — not a deadline — is what ended the child");
        result.TimedOut.Should().BeFalse();
        result.StdOut.Should().Contain(Answer);
        result.Survivors.Should().BeEmpty("the root and its grandchild were both killed and confirmed gone");
        watch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(20), "a child that lives for ever must not set the pace");

        var (root, grandchild) = PidsIn(pids);
        Alive(root).Should().BeFalse("the root was ended");
        Alive(grandchild).Should().BeFalse("a grandchild that survived the root would still hold whatever the root held");
        recorder.Recorded.Should().Contain(root, "the root was recorded when it started");
        recorder.Recorded.Should().Contain(grandchild, "the grandchild was written down BEFORE the kill, so a crash mid-kill leaves it named");
    }

    [Fact]
    public async Task AServerThatNeverAnswers_IsEndedAtTheDeadline_WithItsTree()
    {
        Assert.SkipUnless(ProcessTree.Supported, "the tree discipline lists descendants on Linux and Windows only");
        var pids = Path.Combine(_dir, "pids");
        var launcher = new ProcessLauncher();

        var result = await launcher.RunAsync(
            Serve(pids, afterEofMs: -1, grandchild: true) with { EndsWhen = Answered, StdIn = "{\"id\":1}\n", Timeout = TimeSpan.FromSeconds(2) });

        result.TimedOut.Should().BeTrue("nothing carried id 2, so only the deadline could end it");
        result.Ended.Should().BeFalse();
        result.Survivors.Should().BeEmpty();
        var (root, grandchild) = PidsIn(pids);
        Alive(root).Should().BeFalse();
        Alive(grandchild).Should().BeFalse("the deadline takes the tree discipline too, for a request that ends its child itself");
    }

    [Fact]
    public async Task ARequestWithoutAPredicate_IsUnchanged_NoEndNoSurvivors()
    {
        var launcher = new ProcessLauncher();

        var result = await launcher.RunAsync(new ProcessRequest(FakeCliInvocations.Exe, ["emit", "hello"], AppContext.BaseDirectory));

        result.Ended.Should().BeFalse();
        result.Survivors.Should().BeEmpty();
        result.StdOut.Should().Be("hello");
    }

    [Fact]
    public async Task TheDescendantsOfALiveServer_AreListedWithTheirStartTimes()
    {
        Assert.SkipUnless(ProcessTree.Supported, "descendants are listed on Linux and Windows only");
        var pids = Path.Combine(_dir, "pids");
        var start = new ProcessStartInfo(FakeCliInvocations.Exe) { UseShellExecute = false, RedirectStandardInput = true };
        foreach (var arg in new[] { "serve", pids, "-1", "grandchild", Answer })
        {
            start.ArgumentList.Add(arg);
        }

        using var server = Process.Start(start)!;
        try
        {
            await WaitForAsync(() => File.Exists(pids) && File.ReadAllLines(pids).Length == 2, server);
            var (_, grandchild) = PidsIn(pids);

            var tree = ProcessTree.Descendants(server.Id);

            tree.Select(member => member.Pid).Should().Contain(grandchild);
            var listed = tree.Single(member => member.Pid == grandchild);
            OrphanSweep.Same(listed.StartedUtc, Process.GetProcessById(grandchild).StartTime.ToUniversalTime())
                .Should().BeTrue("the identity is the pid AND its start time, read the way every record here reads it");
            ProcessTree.IsAlive(listed).Should().BeTrue();
            ProcessTree.IsAlive(listed with { StartedUtc = listed.StartedUtc.AddHours(-1) })
                .Should().BeFalse("a start time that does not match is somebody else's process under a reused number");
        }
        finally
        {
            server.Kill(entireProcessTree: true);
        }
    }

    private static ProcessRequest Serve(string pidFile, int afterEofMs, bool grandchild) =>
        new(FakeCliInvocations.Exe, ["serve", pidFile, afterEofMs.ToString(), grandchild ? "grandchild" : "alone", Answer], AppContext.BaseDirectory)
        {
            StdIn = "{\"id\":1,\"method\":\"initialize\"}\n{\"id\":2,\"method\":\"read\"}\n",
            Timeout = Generous,
            TrackAs = "reader",
        };

    private static (int Root, int Grandchild) PidsIn(string file)
    {
        var lines = File.ReadAllLines(file);
        lines.Should().HaveCount(2, "the server wrote its own pid and its grandchild's");

        return (int.Parse(lines[0]), int.Parse(lines[1]));
    }

    private static bool Alive(int pid) => ProcessTracking.StartedAt(pid) is not null;

    private static async Task WaitForAsync(Func<bool> condition, Process child)
    {
        var until = DateTime.UtcNow.AddSeconds(30);
        while (!condition() && DateTime.UtcNow < until)
        {
            child.HasExited.Should().BeFalse("the server died before it had written its pids");
            await Task.Delay(50);
        }

        condition().Should().BeTrue("the server did not write its pids within 30 s");
    }

    /// <summary>A tracker that remembers every pid it was told about, and forgets nothing — what the marker on a slot does.</summary>
    private sealed class Recorder : IProcessTracker
    {
        public List<int> Recorded { get; } = [];

        public void Record(int pid, DateTime startedUtc, string label) => Recorded.Add(pid);

        public void Forget(int pid)
        {
        }
    }
}
