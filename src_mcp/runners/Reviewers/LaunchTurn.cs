namespace CoaiMcp.Runners.Reviewers;

/// <summary>Why an adapter refused a launch — the one distinction a consultation's failure kind depends on.</summary>
public enum RefusalCause
{
    /// <summary>coai's own launch check said no to a CLI that is installed (a home, a path, a model, an unreadable version): <c>launch-refused</c>.</summary>
    LaunchControl,

    /// <summary>There is no CLI to start (the OS says the file or its path is not there): <c>cli-not-found</c>, with the install cure.</summary>
    CliMissing,
}

/// <summary>
/// What an adapter answered when the executor asked to start one launch: go (holding whatever the launch needs until it
/// has been read), or a refusal with the sentence the reviewer's failure carries.
/// </summary>
/// <remarks>
/// <para><b>Why the executor asks at all</b> (D17 of <c>todo/PLAN_vibe_and_kimi_runtimes.md</c>). <c>Build</c> is pure, and
/// some decisions cannot be: Vibe's coai-owned home must be checked on the disk, against the INSTALLED Vibe's version,
/// immediately before every launch, and two turns on one home must not run at once. A check made when the work was built
/// would be a check of a moment that has passed by the time a repair or a retry starts. Every launch passes through
/// <c>ReviewerExecutor.LaunchAsync</c> — a review's, a repair's, a consultation's, a question row's — so the ONE launch
/// control lives there, and no path can add a flag another forgets (the code gate of 2026-10-11).</para>
/// <para>A class rather than a record: it owns what it holds (a turn on a home, a per-attempt export folder) and gives it
/// back exactly once, after the launch's answer, usage and reason have all been read.</para>
/// </remarks>
public sealed class LaunchTurn : IDisposable
{
    private readonly IDisposable? _held;
    private bool _given;

    private LaunchTurn(string refusal, IDisposable? held, string warning = "", string cliVersion = "", bool cliMissing = false)
    {
        Refusal = refusal;
        CliMissing = cliMissing;
        _held = held;
        Warning = warning;
        CliVersion = cliVersion;
    }

    /// <summary>Start it; nothing is held. The default of every adapter that has no such check.</summary>
    public static LaunchTurn Free => new(string.Empty, null);

    /// <summary>Do not start it — the sentence names what was found and what cures it.</summary>
    /// <param name="cause">REQUIRED, with no default: every caller states WHY (D5 of <c>research/PLAN_vibe_live_round_fixes.md</c>). A default
    /// would make an adapter's refusal read as coai's own launch check saying no to an installed CLI, whatever it really was.</param>
    public static LaunchTurn Refused(string reason, RefusalCause cause) => new(reason, null, cliMissing: cause == RefusalCause.CliMissing);

    /// <summary>Whether a refusal is a missing CLI rather than the launch control's own verdict.</summary>
    public bool CliMissing { get; }

    /// <summary>
    /// Start it, and give <paramref name="held"/> back once the launch has been read — with <paramref name="warning"/>, the
    /// sentence the person is owed about a launch that was ALLOWED (a CLI version nobody measured), carried to the outcome.
    /// </summary>
    /// <param name="cliVersion">The CLI's release, when the control ASKED it for this launch — what the ledger line names
    /// (<c>LaunchOptions.CliVersion</c>: a version already asked, never a spawn of its own).</param>
    public static LaunchTurn Holding(IDisposable held, string warning = "", string cliVersion = "") => new(string.Empty, held, warning, cliVersion);

    /// <summary>Start it, nothing held, with the sentence the person is owed about it — what <see cref="ReviewerInvocation.Note"/> carried; plain <see cref="Free"/> when it is empty.</summary>
    public static LaunchTurn Noting(string warning) => new(string.Empty, null, warning);

    /// <summary>Empty when the launch may start.</summary>
    public string Refusal { get; }

    /// <summary>What the person should know about a launch that may start; empty for nearly every launch.</summary>
    public string Warning { get; }

    /// <summary>The release the control asked before this launch, as the ledger names it; empty when it asked none.</summary>
    public string CliVersion { get; }

    public void Dispose()
    {
        if (_given)
        {
            return;
        }

        _given = true;
        _held?.Dispose();
    }
}

/// <summary>
/// How a path a reviewer CITED is turned back into the repository's own — for a launch that ran somewhere other than the
/// checkout (D6: a sanitised snapshot), whose findings name files under the snapshot's root.
/// </summary>
public interface ICitedPaths
{
    /// <summary>The repository-relative path an absolute cited path names, or empty when it names nothing this launch held.</summary>
    string MapBack(string absolutePath);
}

/// <summary>Citations as the reviewer wrote them — every launch that ran in the checkout itself, or in no tree at all.</summary>
public sealed class CitedAsWritten : ICitedPaths
{
    public static CitedAsWritten Instance { get; } = new();

    private CitedAsWritten()
    {
    }

    public string MapBack(string absolutePath) => string.Empty;
}
