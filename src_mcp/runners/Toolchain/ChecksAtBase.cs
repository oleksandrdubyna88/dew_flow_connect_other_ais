using CoaiMcp.Core.Toolchain;
using CoaiMcp.Core.Work;
using CoaiMcp.Runners.Collecting;

namespace CoaiMcp.Runners.Toolchain;

/// <summary>
/// The checks of a coder job: the toolchain is read from <c>.coai/toolchain.yaml</c> AT THE BASE COMMIT, never from the worktree, and run in the worktree. A coder that edits the file — to skip a test, to
/// build something else — changes nothing for its own job: the change is REPORTED, and it takes effect for the jobs after it, once it has been reviewed and merged (plan gate, epoch 3 E2).
/// </summary>
/// <remarks>
/// <para><b>Fails closed.</b> A base commit that is not in the repository, or a file git could not hand over, is <see cref="CheckState.ToolchainRefused"/> — never "no file", which would read the outage as a
/// repository with nothing to check, and never the worktree's file as a fallback.</para>
/// <para>The comparison with the worktree reads text, with line endings made equal, so a Windows checkout that turned LF into CRLF is not reported as a change the coder made.</para>
/// </remarks>
public sealed class ChecksAtBase(GitHistory git, CheckRunner runner)
{
    /// <summary>Runs the checks of the job whose worktree is <paramref name="request"/>'s tree, with the toolchain of <paramref name="baseSha"/>. <paramref name="tree"/> reads the worktree's files.</summary>
    public async Task<CheckResult> RunAsync(IWorkFolder tree, string baseSha, CheckRequest request, CancellationToken ct = default)
    {
        var atBase = await ReadAtBaseAsync(request.Tree, baseSha, ct);
        if (atBase.Trouble.Length > 0)
        {
            return new CheckResult(CheckState.ToolchainRefused, [], atBase.Trouble);
        }

        var change = Compare(atBase.File, tree.Read(ToolchainTables.FilePath, ToolchainTables.MaxBytes));
        var result = await runner.RunAsync(ToolchainFile.Read(atBase.File), request, ct);

        return result with { ToolchainChange = change, Note = result.Note + Reported(change, baseSha) };
    }

    private sealed record BaseFile(WorkFile File, string Trouble);

    private async Task<BaseFile> ReadAtBaseAsync(string worktree, string baseSha, CancellationToken ct)
    {
        var commit = await git.HasCommitAsync(worktree, baseSha, ct);
        if (!commit.Ran || !commit.Ok)
        {
            return Unreadable($"the base commit '{baseSha}' is not a commit of this repository");
        }

        var has = await git.HasPathAsync(worktree, baseSha, ToolchainTables.FilePath, ct);
        if (!has.Ran)
        {
            return Unreadable($"git did not answer whether {baseSha} has {ToolchainTables.FilePath}");
        }

        if (!has.Ok)
        {
            return new BaseFile(new WorkFile.Missing(), string.Empty);
        }

        var shown = await git.FileAtAsync(worktree, baseSha, ToolchainTables.FilePath, ct);

        return !shown.Ran || !shown.Ok
            ? Unreadable($"git could not read {ToolchainTables.FilePath} at {baseSha}")
            : new BaseFile(shown.Truncated ? new WorkFile.TooLarge(shown.Out.Length) : new WorkFile.Text(shown.Out), string.Empty);
    }

    private static BaseFile Unreadable(string trouble) => new(new WorkFile.Missing(), $"the toolchain at the base was not read, so nothing was run: {trouble}");

    /// <summary>What the worktree did to the file, comparing the text with line endings made equal.</summary>
    private static ToolchainChange Compare(WorkFile atBase, WorkFile inTree) => (atBase, inTree) switch
    {
        (WorkFile.Missing, WorkFile.Missing) => ToolchainChange.Unchanged,
        (WorkFile.Missing, _) => ToolchainChange.Added,
        (_, WorkFile.Missing) => ToolchainChange.Removed,
        (WorkFile.Text before, WorkFile.Text after) when Same(before.Content, after.Content) => ToolchainChange.Unchanged,
        _ => ToolchainChange.Changed,
    };

    private static bool Same(string before, string after) => before.ReplaceLineEndings("\n") == after.ReplaceLineEndings("\n");

    private static string Reported(ToolchainChange change, string baseSha) => change == ToolchainChange.Unchanged
        ? string.Empty
        : $" The worktree {Verb(change)} {ToolchainTables.FilePath}; this job ran the file at base {baseSha[..Math.Min(8, baseSha.Length)]} and the change did not run.";

    private static string Verb(ToolchainChange change) => change switch
    {
        ToolchainChange.Added => "added",
        ToolchainChange.Removed => "removed",
        _ => "changed",
    };
}
