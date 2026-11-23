namespace CoaiMcp.Runners.Worktrees;

/// <summary>Why a coder worktree was not made or not removed. Each value has one sentence in <see cref="CoderWorktreeOutcome.Refused"/>.</summary>
public enum CoderWorktreeRefusal
{
    /// <summary>The job id is not the one strict shape; refused before any process starts.</summary>
    BadJobId,

    /// <summary>The base is not a full commit id; refused before any process starts.</summary>
    BadBaseSha,

    /// <summary>The repository path is not a git repository with a commit.</summary>
    NotARepository,

    /// <summary>The coder root lies inside the main checkout, so a coder could write into it.</summary>
    RootInsideCheckout,

    /// <summary>The coder root cannot be used, or another start holds the one creation lock too long.</summary>
    RootUnusable,

    /// <summary>The base commit is not in this repository.</summary>
    UnknownBase,

    /// <summary>The branch or the folder of this job id is already there.</summary>
    AlreadyExists,

    /// <summary>The repository already holds the most live coder worktrees; none is removed to make room.</summary>
    Full,

    /// <summary>The folder is not a coder worktree of this repository, so coai does not remove it.</summary>
    NotOurs,

    /// <summary>Git did not run, ran out of time, or said no.</summary>
    GitFailed,
}

/// <summary>What <see cref="CoderWorktree"/> answers: a closed set, so a refusal is a value and never an exception.</summary>
public abstract record CoderWorktreeOutcome
{
    /// <summary>The worktree is made: the branch <c>coder/&lt;jobId&gt;</c> at exactly the base commit, in the folder <c>coai-coder-&lt;jobId&gt;</c>.</summary>
    public sealed record Started(string Path, string Branch, string BaseSha) : CoderWorktreeOutcome;

    /// <summary>The worktree and its branch are gone (or were never there).</summary>
    public sealed record Removed(string Path, string Branch) : CoderWorktreeOutcome;

    /// <summary>Nothing was changed, and the sentence says why.</summary>
    public sealed record Refused(CoderWorktreeRefusal Reason, string Sentence) : CoderWorktreeOutcome;

    private CoderWorktreeOutcome() { }
}
