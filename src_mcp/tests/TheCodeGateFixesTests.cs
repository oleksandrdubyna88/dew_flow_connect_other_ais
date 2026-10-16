using System.Reflection;
using CoaiMcp.Core;
using CoaiMcp.Core.Collecting;
using CoaiMcp.Core.Feature;
using CoaiMcp.Core.Rounds;
using CoaiMcp.Runners.Collecting;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Server;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// What the code gate of epic E1 asked for: one entry point that skips no guard, a preparation that overlaps its git calls and
/// answers within a deadline, a sentence for every state a plan can be dirty in, and the small shared helpers.
/// </summary>
public sealed class TheCodeGateFixesTests : IAsyncLifetime
{
    private readonly ProcessLauncher _launcher = new();
    private TempGitRepo _repo = null!;
    private string _sha = string.Empty;

    public async ValueTask InitializeAsync()
    {
        _repo = await TempGitRepo.InitAsync(_launcher, "coai-gate-fix-");
        await _repo.GitAsync("config", "core.autocrlf", "false");
        await _repo.WriteAsync("plan.md", "hello plan\n");
        await _repo.CommitAsync("a plan");
        _sha = await _repo.HeadAsync();
    }

    public async ValueTask DisposeAsync() => await _repo.DisposeAsync();

    [Fact]
    public void TheUnguardedRead_IsNotPartOfThePublicSurface()
    {
        // `ReadAsync` is the proof over an already-canonical path and applies no credential refusal — a future caller outside
        // the assembly could read `.env` through it. Only the guarded entry point is public.
        var methods = typeof(CommittedPlanReader).GetMethods(BindingFlags.Public | BindingFlags.Static).Select(m => m.Name);

        methods.Should().Contain("ReadGuardedAsync").And.NotContain("ReadAsync");
    }

    [Fact]
    public async Task AMixedStagedAndUnstagedEdit_GetsItsOwnSentence_NamingBothAndTheCure()
    {
        // `MM`: git add'd, then edited again. "has staged changes — commit them" was wrong: after the commit the working
        // edit is still there and the read fails again.
        await _repo.WriteAsync("plan.md", "edit one, staged\n");
        await _repo.GitAsync("add", "plan.md");
        await _repo.WriteAsync("plan.md", "edit two, not staged\n");

        var read = await CommittedPlanReader.ReadGuardedAsync(new GitHistory(_launcher), _repo.Path, _sha, "plan.md", TestContext.Current.CancellationToken);

        read.Should().BeOfType<PlanRead.Refused>().Which.Sentence
            .Should().Contain("staged").And.Contain("unstaged").And.Contain("git add");
    }

    [Fact]
    public async Task TheRootAndTheEntry_AreAskedAtTheSameTime()
    {
        // They depend on nothing but the repository; asking one after the other costs the sum of two git calls.
        var overlapping = new Overlapping(_launcher);

        var read = await CommittedPlanReader.ReadGuardedAsync(new GitHistory(overlapping), _repo.Path, _sha, "plan.md", TestContext.Current.CancellationToken);

        read.Should().BeOfType<PlanRead.Accepted>();
        overlapping.WaitedInVain.Should().BeFalse("the second call never started while the first was still running");
    }

    [Fact]
    public async Task APreparationThatGitCannotFinishInTime_IsRefusedBeforeAnyRound()
    {
        var stalling = new Stalling();
        var started = DateTime.UtcNow;

        var outcome = await PlanInputs.PrepareAsync(
            new PlanInput(string.Empty, "plan.md", string.Empty), _repo.Path, "main", (_, _) => Task.FromResult(_sha), stalling,
            TestContext.Current.CancellationToken, deadline: TimeSpan.FromMilliseconds(400));

        outcome.Should().BeOfType<PlanInputOutcome.Refused>().Which.Sentence
            .Should().Contain("git did not answer in time").And.Contain("the round was not started");
        (DateTime.UtcNow - started).Should().BeLessThan(TimeSpan.FromSeconds(15), "the deadline ended the wait, not the launcher's own budget");
    }

    [Fact]
    public void TheDeadlineHasANamedConstant()
    {
        PlanInputs.PreparationDeadline.Should().BeGreaterThan(TimeSpan.FromSeconds(30)).And.BeLessThan(TimeSpan.FromMinutes(2));
    }

    [Fact]
    public void TheShortIdIsOneHelper_TwelveCharacters_AndNeverLongerThanTheId()
    {
        GitIds.Short("0123456789abcdef0123456789abcdef01234567").Should().Be("0123456789ab");
        GitIds.Short("abc").Should().Be("abc");
    }

    [Theory]
    [InlineData("pasted", "", "", "")]
    [InlineData("", "", "", "pass the plan")]
    [InlineData("", "  ", "", "pass the plan")]
    [InlineData("pasted", "plan.md", "", "not both")]
    [InlineData("pasted", "plan.md", "1a2b", "not both")]
    [InlineData("", "", "0123456789012345678901234567890123456789", "means nothing without committedPlan")]
    [InlineData("pasted", "", "0123456789012345678901234567890123456789", "means nothing without committedPlan")]
    [InlineData("", "plan.md", "1a2b", "full 40-hex commit id")]
    public async Task TheInputRefusals_AreDecidedInOneFixedOrder(string text, string committed, string expected, string refusal)
    {
        // Both, then expectedCommit alone, then neither, then a malformed expectedCommit — the first that applies wins.
        var outcome = await PlanInputs.PrepareAsync(
            new PlanInput(text, committed, expected), _repo.Path, "main", (_, _) => Task.FromResult(_sha), _launcher, TestContext.Current.CancellationToken);

        if (refusal.Length == 0)
        {
            outcome.Should().BeOfType<PlanInputOutcome.Ready>();
        }
        else
        {
            outcome.Should().BeOfType<PlanInputOutcome.Refused>().Which.Sentence.Should().Contain(refusal);
        }
    }

    [Fact]
    public void ACanonicalRelativePath_IsTrimmed_AndUsesForwardSlashes()
    {
        CoaiMcp.Core.RepoPaths.Canonical("  todo\\PLAN_x.md \t").Should().Be("todo/PLAN_x.md");
        CoaiMcp.Core.RepoPaths.Canonical("a/b.md").Should().Be("a/b.md");
    }

    [Theory]
    [InlineData("key.pem.", "*.pem")]
    [InlineData("secrets/id_rsa. ", "id_rsa*")]
    [InlineData("config\\server.PEM...", "*.pem")]
    [InlineData(".env. .", ".env*")]
    public void ACredentialNameWithTrailingDotsOrSpaces_IsStillACredentialName(string path, string shape)
    {
        // Windows ignores trailing dots and spaces in a file name, so `key.pem.` IS `key.pem` there.
        CredentialFiles.WhichPattern(path).Should().Be(shape);
    }

    [Fact]
    public void AnOrdinaryNameWithATrailingDot_IsNotACredentialName()
    {
        CredentialFiles.LooksLikeOne("notes.md.").Should().BeFalse();
    }

    [Fact]
    public void ASuppliedTextId_IsUsedAsIs_NotComputedAgain()
    {
        PlanSection.Compose("a plan", string.Empty, "feedfeedfeedfeed").Should()
            .Contain("BEGIN OF PLAN feedfeedfeedfeed").And.EndWith("END OF PLAN feedfeedfeedfeed");
    }

    [Fact]
    public async Task APastedPlan_IsReadyWithTheIdOfItsText()
    {
        var outcome = await PlanInputs.PrepareAsync(
            new PlanInput("a pasted plan", string.Empty, string.Empty), _repo.Path, "main", (_, _) => Task.FromResult(_sha), _launcher,
            TestContext.Current.CancellationToken);

        outcome.Should().BeOfType<PlanInputOutcome.Ready>().Which.TextId.Should().Be(DocumentRules.ArtifactIdOf("a pasted plan"));
    }

    [Fact]
    public async Task ACommittedPlan_IsReadyWithTheIdTheReaderRecorded()
    {
        var outcome = await PlanInputs.PrepareAsync(
            new PlanInput(string.Empty, "plan.md", string.Empty), _repo.Path, "main", (_, _) => Task.FromResult(_sha), _launcher,
            TestContext.Current.CancellationToken);

        var ready = outcome.Should().BeOfType<PlanInputOutcome.Ready>().Subject;
        ready.TextId.Should().Be(DocumentRules.ArtifactIdOf("hello plan\n")).And.Be(ready.Source!.TextId);
    }

    /// <summary>Holds the first of the two independent reads until the other arrives — a sequential caller waits in vain.</summary>
    private sealed class Overlapping(IProcessLauncher real) : IProcessLauncher
    {
        private readonly TaskCompletionSource _both = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _arrived;

        public bool WaitedInVain { get; private set; }

        public async Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken ct = default)
        {
            if (request.Arguments.Contains("--show-toplevel") || request.Arguments.Contains("ls-tree"))
            {
                if (Interlocked.Increment(ref _arrived) == 2)
                {
                    _both.TrySetResult();
                }

                var first = await Task.WhenAny(_both.Task, Task.Delay(TimeSpan.FromSeconds(5), ct));
                WaitedInVain |= first != _both.Task;
            }

            return await real.RunAsync(request, ct);
        }
    }

    /// <summary>A git that never answers until it is cancelled — then answers as the real launcher does.</summary>
    private sealed class Stalling : IProcessLauncher
    {
        public async Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken ct = default)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(60), ct);
            }
            catch (OperationCanceledException)
            {
                return new ProcessResult(-1, string.Empty, string.Empty, TimedOut: false, Cancelled: true);
            }

            return new ProcessResult(0, string.Empty, string.Empty, TimedOut: false);
        }
    }
}
