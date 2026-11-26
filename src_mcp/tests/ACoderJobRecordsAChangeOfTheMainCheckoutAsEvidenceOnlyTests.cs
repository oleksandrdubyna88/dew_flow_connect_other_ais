using CoaiMcp.Runners.Consultation;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Toolchain;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// Epoch 3 E2 s2 t5, DoD d4 (<c>v2/architecture/server.md</c> § 16, decision 8): a change of the main checkout by ANOTHER process while a coder job runs does NOT fail the job — unlike the tree of a read-only role, where a change withholds the answer. It is recorded as
/// snapshot EVIDENCE: the paths and their verbs, in the outcome, for a person to read. The snapshots cannot say who wrote, and the coder cannot write the main checkout at all (the OS layer), so a difference is somebody else's.
/// </summary>
/// <remarks>
/// <para><b>The person is another process: the test itself.</b> The fake coder (<c>hold-in</c>) says it is working by writing <c>ready</c> in the worktree — the EVENT the test waits for, never a sleep — and only then does the "person" edit the main checkout, a tracked file, a new file and
/// the repository's own configuration, the one a read-only role fails hardest on.</para>
/// <para>The positive control is in the same test: the evidence DID see the edit (<see cref="CoderMainEvidence.Changed"/>), so "the job did not fail" is not the job having looked at nothing.</para>
/// <para>Real units on temp folders only; skipped, with the reason, without a user manager.</para>
/// </remarks>
public sealed class ACoderJobRecordsAChangeOfTheMainCheckoutAsEvidenceOnlyTests
{
    private static void NeedAUserManager() => Assert.SkipUnless(SystemdUser.Works(out var why), why);

    private static async Task WaitForAsync(string file)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(60);
        while (!File.Exists(file))
        {
            DateTime.UtcNow.Should().BeBefore(deadline, $"the fake coder never wrote {file}");
            await Task.Delay(25, TestContext.Current.CancellationToken);
        }
    }

    [Fact]
    public async Task APersonsEditOfTheMainCheckout_DuringACoderJob_DoesNotFailTheJob_ItIsEvidenceOnly()
    {
        NeedAUserManager();
        await using var bed = await CoderBed.CreateAsync();
        var running = bed.Turns().RunAsync(bed.Job, bed.Fake("hold-in", bed.Worktree, "3000"), "c1");
        await WaitForAsync(Path.Combine(bed.Worktree, "ready"));

        await File.WriteAllTextAsync(Path.Combine(bed.Main, "a.txt"), "a person edited this\n", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(bed.Main, "a-person-added-this.txt"), "new\n", TestContext.Current.CancellationToken);
        await File.AppendAllTextAsync(Path.Combine(bed.Main, ".git", "config"), "[user]\n\tname = a person\n", TestContext.Current.CancellationToken);
        var outcome = await running;

        var done = outcome.Should().BeOfType<CoderTurnOutcome.Finished>($"a person's edit is not the coder's failure: {outcome}").Subject;
        done.Green.Should().BeTrue();
        done.Evidence.Taken.Should().BeTrue();
        done.Evidence.Changed.Should().BeTrue("POSITIVE CONTROL: the snapshots did see the person's edit");
        done.Evidence.Changes.Select(change => change.Path).Should().Contain(["a.txt", "a-person-added-this.txt", "<git>/config"]);
        done.Evidence.Note.Should().Contain("evidence only").And.Contain("a.txt").And.Contain("cannot say who wrote");
        done.Run.Gone.Should().BeTrue();
    }

    [Fact]
    public async Task ACoderJobWithNoEditOfTheMainCheckout_RecordsNoChange_AndIsGreen()
    {
        NeedAUserManager();
        await using var bed = await CoderBed.CreateAsync();

        var outcome = await bed.Turns().RunAsync(bed.Job, bed.Fake("emit", "done"), "c1");

        var done = outcome.Should().BeOfType<CoderTurnOutcome.Finished>(outcome.ToString()).Subject;
        done.Evidence.Taken.Should().BeTrue();
        done.Evidence.Changed.Should().BeFalse();
        done.Evidence.Note.Should().Contain("looked the same");
    }

    [Fact]
    public async Task AFailingCoder_StillCarriesTheEvidence_AndAStoppedWriteIsNotAChangeOfTheMainCheckout()
    {
        NeedAUserManager();
        await using var bed = await CoderBed.CreateAsync();

        var outcome = await bed.Turns().RunAsync(bed.Job, bed.Fake("write-files", Path.Combine(bed.Main, "stopped-by-the-layer.txt")), "c1");

        var stopped = outcome.Should().BeOfType<CoderTurnOutcome.WriteStopped>(outcome.ToString()).Subject;
        stopped.Evidence.Changed.Should().BeFalse("the OS refused the write, so the main checkout did not change: evidence and layer agree");
        stopped.Evidence.Taken.Should().BeTrue();
    }

    [Fact]
    public async Task EvidenceThatCannotBeTaken_IsSaidSo_AndTheJobIsNotAffected()
    {
        NeedAUserManager();
        await using var bed = await CoderBed.CreateAsync();
        var gitFails = new WatchedLauncher(bed.Launcher, request => request.Executable == "git" ? new ProcessResult(128, string.Empty, "fatal: not a git repository", TimedOut: false) : null);

        var outcome = await bed.Turns(evidence: new FilesystemInvariant(gitFails)).RunAsync(bed.Job, bed.Fake("emit", "done"), "c1");

        var done = outcome.Should().BeOfType<CoderTurnOutcome.Finished>($"evidence is a diagnostic, never a verdict: {outcome}").Subject;
        done.Evidence.Taken.Should().BeFalse();
        done.Evidence.Note.Should().Contain("could not be snapshotted").And.Contain("the job is not affected");
    }
}
