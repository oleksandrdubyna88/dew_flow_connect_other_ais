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
/// <para>In the <c>fakecli-env</c> collection because it LAUNCHES the fake CLI, whose behaviour is
/// steered by process-wide environment variables that other classes set and clear. A verb this
/// class passes in argv is only read when <c>FAKECLI_MODE</c> is unset, so running beside a class
/// that sets it is a race with nothing holding it off — and the environment tests below print
/// nothing at all when they lose it.</para>
/// </remarks>
[Collection("fakecli-env")]
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

    /// <summary>Long enough that a child living it out is unmistakably the wrong answer.</summary>
    private const string LivesTenSeconds = "10000";

    /// <summary>The budget under test. Two orders of magnitude under the child's own lifetime.</summary>
    private static readonly TimeSpan ShortBudget = TimeSpan.FromMilliseconds(300);

    /// <summary>Long enough that nothing in these tests can reach it, so a failure is never the clock.</summary>
    private static readonly TimeSpan NoBudgetPressure = TimeSpan.FromMinutes(1);

    private const int Ceiling = 64 * 1024;

    /// <summary>Comfortably past <see cref="Ceiling"/>, so the drop is unambiguous.</summary>
    private const string SpewsPastTheCeiling = "400000";

    /// <summary>An exit code no runtime produces by accident, so seeing it back means the CHILD chose it.</summary>
    private const string RefusesWithCode = "7";

    /// <summary>The variable a confined launch is handed on the request itself, and must still see.</summary>
    private const string HandedOver = "COAI_HANDED_OVER";

    private static ProcessRequest Request(params string[] args) =>
        new(FakeCliInvocations.Exe, args, AppContext.BaseDirectory);

    [Fact]
    public async Task AConfinedChildDoesNotInheritTheParentsEnvironment()
    {
        // The allowlist, observed on a process rather than asserted on a dictionary: `env-names`
        // prints every variable NAME the child was actually started with. The canary stands for the
        // server's own configuration, which on the Team server is in every reviewer's environment
        // today; PATH and HOME stand for what a CLI cannot start without; the handed-over name is
        // the caller's own variable, applied last as before.
        using var canary = new Canary();
        var request = Request("env-names") with
        {
            InheritsEnvironment = false,
            Environment = HandedOverVariables(),
            Timeout = NoBudgetPressure,
        };

        var result = await new ProcessLauncher().RunAsync(request, TestContext.Current.CancellationToken);

        result.ExitCode.Should().Be(0,
            "a child must still be able to START on the allowlist alone; it said: {0}", result.StdErr);
        var names = Names(result.StdOut);
        names.Should().NotContain(canary.Name,
            "nothing the parent did not choose to hand over may cross into a process running somebody else's prompt");
        names.Should().Contain(name => IsThePlatformsSpellingOf(name, "PATH"),
            "the CLI is found through it and starts its own children through it");
        names.Should().Contain(HandedOver, "the request's own variables are applied on top, exactly as before");
        if (!OperatingSystem.IsWindows())
        {
            names.Should().Contain("HOME",
                "a Node runtime with no HOME fails in initialisation, before it reads a prompt (gemini, plan round)");
        }
    }

    [Fact]
    public async Task AnUnconfinedChildInheritsEverythingAsBefore()
    {
        // The positive companion, for the same reason the read-everything test below exists: a
        // launcher that handed a child nothing at all would pass the negative test above. And it is
        // the local coai-mcp's contract — the developer's own CLIs in the developer's own
        // environment, sign-ins and proxies included — which this change must not move.
        using var canary = new Canary();
        var request = Request("env-names") with { Timeout = NoBudgetPressure };

        var result = await new ProcessLauncher().RunAsync(request, TestContext.Current.CancellationToken);

        result.ExitCode.Should().Be(0);
        Names(result.StdOut).Should().Contain(canary.Name,
            "the default is the parent's whole environment, and nothing that already calls the launcher asked for less");
    }

    [Fact]
    public async Task AChildThatNeverReadsItsStdinIsStillKilledOnTime()
    {
        // `sleep` does not touch stdin, so the megabyte below fills the pipe and stays there. The
        // deadline has to be able to end a launch that is stuck in the WRITE, which is the half of
        // the operation the timeout used to start after.
        var request = Request("sleep", LivesTenSeconds) with
        {
            StdIn = new string('p', PastAnyPipeBuffer),
            Timeout = ShortBudget,
        };
        var clock = Stopwatch.StartNew();

        var result = await new ProcessLauncher().RunAsync(request, TestContext.Current.CancellationToken);

        clock.Elapsed.Should().BeLessThan(Promptly,
            "the launch was given 300 ms and the child ten seconds — whichever of the two runs out "
            + "first is the one that must decide");
        result.TimedOut.Should().BeTrue("the tree was killed, and the caller has to be able to tell");
        result.Cancelled.Should().BeFalse("nobody withdrew this one; its own budget ran out");
    }

    [Fact]
    public async Task AChildThatNeverReadsItsStdinIsStillStoppedByItsCaller()
    {
        // The second clock. A Team-server job carries a cancellation token that a cancel or the
        // deadline sweep fires, and it must reach a launch stuck in the write — otherwise the
        // account's lock is held past the job's own budget by a review nobody is waiting for.
        using var caller = new CancellationTokenSource(ShortBudget);
        var request = Request("sleep", LivesTenSeconds) with
        {
            StdIn = new string('p', PastAnyPipeBuffer),
            Timeout = TimeSpan.FromMinutes(5),
        };
        var clock = Stopwatch.StartNew();

        var result = await new ProcessLauncher().RunAsync(request, caller.Token);

        clock.Elapsed.Should().BeLessThan(Promptly);
        result.TimedOut.Should().BeTrue("the tree was killed, which is what this field has always said");
        result.Cancelled.Should().BeTrue(
            "and WHICH clock stopped it decides what happens next: a budget that ran out is a vendor "
            + "too slow for it, a caller that cancelled is a job somebody withdrew and must not be retried");
    }

    [Fact]
    public async Task AChildThatReadsEverythingStillGetsAllOfIt()
    {
        // The positive companion, and it is not decoration: the three tests above are all about a
        // launch ENDING early, and a launcher that delivered nothing at all would pass every one of
        // them. This is what fails if the write ever stops being awaited properly.
        var prompt = string.Concat(Enumerable.Range(0, PastAnyPipeBuffer / 16).Select(i => $"line {i,-9}\n"));
        var request = Request("echo-stdin") with { StdIn = prompt, Timeout = NoBudgetPressure };

        var result = await new ProcessLauncher().RunAsync(request, TestContext.Current.CancellationToken);

        result.TimedOut.Should().BeFalse();
        result.Truncated.Should().BeFalse();
        result.ExitCode.Should().Be(0);
        result.StdOut.Should().Be(prompt, "every byte the caller sent, and not one the launcher added");
    }

    [Fact]
    public async Task AChildThatExitsWhileWeAreStillWritingIsNotAFailure()
    {
        // A CLI that refuses before it reads — an expired sign-in, a bad flag — closes its end of
        // the pipe mid-write. That is the child's decision and its own words are what matter; the
        // write's broken pipe must not travel out of the launcher as an exception.
        var request = Request("stderr-exit", "not signed in", RefusesWithCode) with
        {
            StdIn = new string('p', PastAnyPipeBuffer),
            Timeout = NoBudgetPressure,
        };

        var result = await new ProcessLauncher().RunAsync(request, TestContext.Current.CancellationToken);

        result.ExitCode.Should().Be(int.Parse(RefusesWithCode));
        result.StdErr.Should().Contain("not signed in");
        result.TimedOut.Should().BeFalse();
    }

    [Fact]
    public async Task OutputPastTheCeilingIsTruncatedAndSaysSo()
    {
        // Not one newline in the whole 400 KB, which is the point. A line-based reader hands over
        // nothing until the stream closes, so a ceiling checked per line cannot fire before the
        // allocation it exists to prevent has already happened.
        var request = Request("spew", SpewsPastTheCeiling) with
        {
            MaxOutputChars = Ceiling,
            Timeout = NoBudgetPressure,
        };

        var result = await new ProcessLauncher().RunAsync(request, TestContext.Current.CancellationToken);

        result.ExitCode.Should().Be(0, "a runaway is still allowed to finish; it is its OUTPUT that is bounded");
        result.StdOut.Length.Should().BeLessThan(Ceiling + 200, "the ceiling, plus the sentence that names it");
        result.Truncated.Should().BeTrue(
            "a caller checking machine-readable output must be able to tell a cut answer from a bad one "
            + "without parsing the sentence");
        result.StdOut.Should().Contain("truncated",
            "a silently cut answer is one a parser fails on for a reason nobody can find");
        result.StdOut.Should().Contain("more were dropped",
            "the ceiling alone cannot say whether one character was lost or two hundred megabytes");
    }

    [Fact]
    public void ACeilingOfNothingIsRefusedRatherThanKeepingNothing()
    {
        // Zero and -1 are both ordinary spellings of "unlimited" elsewhere, and either would make
        // every launch answer with an empty stream — a discarded vendor answer that reads exactly
        // like a vendor that said nothing. (gemini, code round.)
        var refused = () => Request("emit", "x") with { MaxOutputChars = 0 };

        refused.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public async Task OrdinaryOutputIsNotTruncated()
    {
        // The companion the ceiling needs: a bound that fires on everything is indistinguishable
        // from a bound that fires on nothing, and only one of them is caught by the test above.
        var request = Request("emit", "a perfectly ordinary answer") with { Timeout = NoBudgetPressure };

        var result = await new ProcessLauncher().RunAsync(request, TestContext.Current.CancellationToken);

        result.StdOut.Should().Be("a perfectly ordinary answer");
        result.Truncated.Should().BeFalse();
    }

    /// <summary>What the confined launch is handed on the request itself.</summary>
    /// <remarks>
    /// The child is a .NET apphost, and on a machine where the runtime was installed by hand it is
    /// found through <c>DOTNET_ROOT</c> — the test's own plumbing rather than anything about the
    /// launcher, so it rides the request like any caller's variable and is not a name asserted on.
    /// </remarks>
    private static Dictionary<string, string?> HandedOverVariables()
    {
        var handed = new Dictionary<string, string?> { [HandedOver] = "yes" };
        if (Environment.GetEnvironmentVariable("DOTNET_ROOT") is { Length: > 0 } dotnetRoot)
        {
            handed["DOTNET_ROOT"] = dotnetRoot;
        }

        return handed;
    }

    /// <summary>One name per line is what <c>env-names</c> prints; Windows ends its lines in CR LF.</summary>
    private static List<string> Names(string stdout) =>
        stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(line => line.TrimEnd('\r')).ToList();

    /// <summary>
    /// Windows prints the variable as <c>Path</c> and resolves it case-insensitively; Linux prints
    /// <c>PATH</c> and would keep a <c>path</c> apart. The assertion follows the platform, so that a
    /// Linux <c>path</c> could never pass for the one the CLI is found through.
    /// </summary>
    private static bool IsThePlatformsSpellingOf(string name, string expected) =>
        string.Equals(name, expected,
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    /// <summary>
    /// A variable that exists in THIS process for one test and is gone when the test is — the suite
    /// shares a process, so a canary left behind is a canary the next launch inherits.
    /// </summary>
    private sealed class Canary : IDisposable
    {
        public string Name { get; } = $"COAI_CANARY_{Guid.NewGuid():N}";

        public Canary() => Environment.SetEnvironmentVariable(Name, "the parent's own configuration");

        public void Dispose() => Environment.SetEnvironmentVariable(Name, null);
    }
}
