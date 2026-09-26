using System.Globalization;
using CoaiMcp.Runners.Collecting;
using CoaiMcp.Runners.Processes;

namespace CoaiMcp.Runners.Git;

/// <summary>
/// What git says about one piece of work's range: when its base commit was made, and which commits
/// lie between the base and the head.
/// </summary>
/// <remarks>
/// <para>Two facts the feature review's history needs from git and nowhere else
/// (<c>todo/PLAN_feature_review.md</c> §4.8): <c>T0</c>, the committer time of the base, which opens
/// the window a round must fall in; and <c>S</c>, <c>rev-list base..head</c>, which a round's
/// <c>head_sha</c> must be in for rule (a) to tie it to the work.</para>
/// <para><b>Absent is not empty.</b> A range git could not read and a range with no commits are
/// different facts, so each half carries whether it is KNOWN and, when it is not, the sentence that
/// says why — the history then says that sentence instead of reading an outage as "nothing here".</para>
/// </remarks>
/// <param name="TimeKnown">Whether <paramref name="BaseTime"/> was read from git.</param>
/// <param name="BaseTime">The base commit's committer time, UTC. Meaningless when not known.</param>
/// <param name="CommitsKnown">Whether <paramref name="Commits"/> is the whole of <c>base..head</c>.</param>
/// <param name="Commits">Every commit in <c>base..head</c>, full ids, lowercase; empty when not known.</param>
/// <param name="Trouble">
/// Why a half is not known — empty when both are. Written for the reader of the history, not for a log.
/// </param>
public sealed record WorkRange(
    bool TimeKnown,
    DateTimeOffset BaseTime,
    bool CommitsKnown,
    IReadOnlySet<string> Commits,
    string Trouble)
{
    /// <summary>Nothing could be read; <paramref name="why"/> says what went wrong.</summary>
    public static WorkRange Unreadable(string why) =>
        new(false, DateTimeOffset.MinValue, false, new HashSet<string>(), why);
}

/// <summary>
/// Reads a <see cref="WorkRange"/> through the product's one process launcher, with a deadline.
/// </summary>
/// <remarks>
/// <para>Through <see cref="IProcessLauncher"/>, as every git call here is, so the timeout and the
/// whole-tree kill are the tested ones. Both ends must be FULL commit ids — the caller resolved them
/// (<c>rev-parse --verify</c>) before it asked, and a value that is not one is refused here without a
/// process, through <see cref="GitHistory.IsCommitish"/>, the one validator for a stored sha: a
/// revision beginning with <c>--</c> is an option to git, not a revision.</para>
/// <para><b>The commit list is bounded.</b> <c>--max-count</c> asks for one more than the cap, so a
/// range wider than the cap is recognised without reading all of it — and then the list is NOT
/// KNOWN, and says so, rather than a truncated list being mistaken for the whole range.</para>
/// </remarks>
public sealed class WorkRangeReader(IProcessLauncher launcher)
{
    private static readonly TimeSpan Budget = TimeSpan.FromSeconds(30);

    /// <summary>The base's time and the commits of <c>base..head</c>, each known or said not to be.</summary>
    public async Task<WorkRange> ReadAsync(
        string repoPath, string baseSha, string headSha, int maxCommits, CancellationToken ct = default)
    {
        if (!GitHistory.IsCommitish(baseSha) || !GitHistory.IsCommitish(headSha))
        {
            return WorkRange.Unreadable(
                $"the range {Shown(baseSha)}..{Shown(headSha)} is not two full commit ids, so git was not asked");
        }

        var time = await BaseTimeAsync(repoPath, baseSha, ct);
        var commits = await CommitsAsync(repoPath, baseSha, headSha, maxCommits, ct);

        return new WorkRange(
            time.Known,
            time.When,
            commits.Known,
            commits.Shas,
            string.Join(" ", new[] { time.Trouble, commits.Trouble }.Where(t => t.Length > 0)));
    }

    private async Task<(bool Known, DateTimeOffset When, string Trouble)> BaseTimeAsync(
        string repoPath, string baseSha, CancellationToken ct)
    {
        var answer = await RunAsync(repoPath, ["log", "-1", "--format=%ct", baseSha], ct);

        return answer.Ok && long.TryParse(answer.Out.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var seconds)
            ? (true, DateTimeOffset.FromUnixTimeSeconds(seconds), string.Empty)
            : (false, DateTimeOffset.MinValue, $"git could not say when the base commit {Shown(baseSha)} was made.");
    }

    private async Task<(bool Known, IReadOnlySet<string> Shas, string Trouble)> CommitsAsync(
        string repoPath, string baseSha, string headSha, int maxCommits, CancellationToken ct)
    {
        var answer = await RunAsync(
            repoPath, ["rev-list", $"--max-count={maxCommits + 1}", $"{baseSha}..{headSha}"], ct);
        if (!answer.Ok)
        {
            return (false, new HashSet<string>(), $"git could not list the commits of {Shown(baseSha)}..{Shown(headSha)}.");
        }

        var shas = answer.Lines.Select(l => l.ToLowerInvariant()).ToHashSet(StringComparer.Ordinal);

        return shas.Count > maxCommits
            ? (false, new HashSet<string>(), $"base..head holds more than {maxCommits} commits, so no round is tied to this work by its commit.")
            : (true, shas, string.Empty);
    }

    private async Task<GitAnswer> RunAsync(string repoPath, string[] args, CancellationToken ct)
    {
        var result = await launcher.RunAsync(new ProcessRequest("git", args, repoPath) { Timeout = Budget }, ct);

        // A timeout or a cancellation is us having learned nothing — never an empty range.
        return result.TimedOut || result.Cancelled
            ? GitAnswer.Broken
            : new GitAnswer(true, result.ExitCode == 0, result.StdOut);
    }

    /// <summary>A sha as a person reads it: the first twelve characters of a real one, the value otherwise.</summary>
    private static string Shown(string sha) => GitHistory.IsCommitish(sha)
        ? sha[..12]
        : $"'{(sha.Length > 40 ? sha[..40] + "…" : sha)}'";
}
