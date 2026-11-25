using CoaiMcp.Runners.Collecting;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Server.Keys;
using CoaiMcp.Server.RoleJobs;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// Epoch 3 E2 s7, the third security judgement: the search of a keyed turn's files for its key NEVER fails open. When git cannot make one of the searches it needs — list the changed files, show the staged changes, list or read the staged blobs — the turn is
/// NOT committed and the answer says the files could not be searched; it is neither a pass nor the exposure verdict (nothing was found). A REAL repository in a temp folder; only the process launcher is wrapped, so one named git command answers as a failure
/// (a non-zero exit, a timeout, or an answer cut at the output ceiling) and every other command is the real git.
/// </summary>
public sealed class ACommitSearchThatGitCannotMakeTests
{
    private static readonly string Token = KeyWorld.Token("search");

    private static ClaudeKeySecret Secret => new(KeyWorld.IdOf(Token), Token);

    /// <summary>How the injected git command fails.</summary>
    public enum Failure
    {
        ExitsNonZero,
        TimesOut,
        IsCutAtTheCeiling,
    }

    /// <summary>The commands of the search, each named by a token that is in its argv and in no other command of the commit (<c>diff --cached --text</c> is the only one with <c>--text</c>).</summary>
    public static TheoryData<string, Failure, string> Searches => new()
    {
        { "ls-files", Failure.ExitsNonZero, "the list of the changed files, before git add" },
        { "ls-files", Failure.TimesOut, "the list of the changed files, before git add" },
        { "ls-files", Failure.IsCutAtTheCeiling, "the list of the changed files, before git add" },
        { "--text", Failure.ExitsNonZero, "the staged changes as text" },
        { "--text", Failure.TimesOut, "the staged changes as text" },
    };

    private static Func<ProcessRequest, ProcessResult?> Fails(string token, Failure how) => request => request.Arguments.Contains(token, StringComparer.Ordinal)
        ? how switch
        {
            Failure.ExitsNonZero => new ProcessResult(128, string.Empty, "fatal: injected by the test", TimedOut: false),
            Failure.TimesOut => new ProcessResult(-1, string.Empty, string.Empty, TimedOut: true),
            _ => new ProcessResult(0, string.Empty, string.Empty, TimedOut: false, Truncated: true),
        }
        : null;

    private static async Task<string> HeadOf(BoundaryBed bed) => (await CoderCommits.HeadAsync(new GitHistory(bed.Launcher), bed.Tree, TestContext.Current.CancellationToken)).Head;

    [Theory]
    [MemberData(nameof(Searches))]
    public async Task AKeyedTurn_WhoseSearchGitCannotMake_IsNotCommitted_AndTheAnswerSaysItWasNotSearched(string command, Failure how, string what)
    {
        await using var bed = await BoundaryBed.CreateAsync();
        await ACoderFailoverTests.IdentifyAsync(bed);
        var before = await HeadOf(bed);
        await File.WriteAllTextAsync(Path.Combine(bed.Worktree, "work.txt"), "the coder's honest work\n", TestContext.Current.CancellationToken);
        var git = new GitHistory(new WatchedLauncher(bed.Launcher, Fails(command, how)));

        var made = await CoderCommits.CommitAsync(git, bed.Tree, "a keyed turn", TestContext.Current.CancellationToken, Secret);

        made.Failure.Should().NotBeNull($"{what} could not be made, so nothing may be committed");
        made.Failure!.Stop.Should().Be("coder-commit", "a search that could not be made is a failed commit, never the key-in-output verdict: nothing was found");
        made.Failure.Reason.Should().Contain("could not be searched");
        made.Head.Should().BeEmpty();
        (await HeadOf(bed)).Should().Be(before, "no commit was made");
    }

    [Fact]
    public async Task TheStagedBlobsThatCannotBeListedOrRead_AreAFailedCommitToo_WhenTheStagedDiffIsTooBigToReadAtOnce()
    {
        // The staged diff answers cut at the ceiling, so each staged blob is searched on its own (`diff --raw`, then `cat-file blob <id>`): either of those two failing is a search that was not made.
        foreach (var (command, how) in new[] { ("--raw", Failure.ExitsNonZero), ("--raw", Failure.IsCutAtTheCeiling), ("cat-file", Failure.ExitsNonZero), ("cat-file", Failure.TimesOut), ("cat-file", Failure.IsCutAtTheCeiling) })
        {
            await using var bed = await BoundaryBed.CreateAsync();
            await ACoderFailoverTests.IdentifyAsync(bed);
            var before = await HeadOf(bed);
            await File.WriteAllTextAsync(Path.Combine(bed.Worktree, "work.txt"), "the coder's honest work\n", TestContext.Current.CancellationToken);
            var injected = Fails(command, how);
            var git = new GitHistory(new WatchedLauncher(bed.Launcher, request => request.Arguments.Contains("--text", StringComparer.Ordinal)
                ? new ProcessResult(0, string.Empty, string.Empty, TimedOut: false, Truncated: true)
                : injected(request)));

            var made = await CoderCommits.CommitAsync(git, bed.Tree, "a keyed turn", TestContext.Current.CancellationToken, Secret);

            made.Failure.Should().NotBeNull($"{command} {how}: a blob that could not be searched must stop the commit");
            made.Failure!.Stop.Should().Be("coder-commit", $"{command} {how}");
            (await HeadOf(bed)).Should().Be(before, $"{command} {how}: no commit was made");
        }
    }

    [Fact]
    public async Task TheCompanion_WithNoInjectedFailure_TheSameTurnIsCommitted_AndTheSameKeyInItsFileIsTheExposureVerdict()
    {
        await using var bed = await BoundaryBed.CreateAsync();
        await ACoderFailoverTests.IdentifyAsync(bed);
        var git = new GitHistory(new WatchedLauncher(bed.Launcher));
        await File.WriteAllTextAsync(Path.Combine(bed.Worktree, "work.txt"), "the coder's honest work\n", TestContext.Current.CancellationToken);

        var clean = await CoderCommits.CommitAsync(git, bed.Tree, "a keyed turn", TestContext.Current.CancellationToken, Secret);

        clean.Failure.Should().BeNull("git can make every search, and the files hold no key: the commit is made");
        clean.Head.Should().NotBeEmpty();

        await File.WriteAllTextAsync(Path.Combine(bed.Worktree, "leak.txt"), Token + "\n", TestContext.Current.CancellationToken);
        var leaked = await CoderCommits.CommitAsync(git, bed.Tree, "a second turn", TestContext.Current.CancellationToken, Secret);

        leaked.Failure.Should().NotBeNull();
        leaked.Failure!.Stop.Should().Be("key-in-output", "the search WAS made and it found the key: that is the other verdict, and the test above is not it");
    }
}
