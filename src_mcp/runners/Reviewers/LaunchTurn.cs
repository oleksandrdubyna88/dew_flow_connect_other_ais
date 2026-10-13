namespace CoaiMcp.Runners.Reviewers;

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

    private LaunchTurn(string refusal, IDisposable? held, string warning = "")
    {
        Refusal = refusal;
        _held = held;
        Warning = warning;
    }

    /// <summary>Start it; nothing is held. The default of every adapter that has no such check.</summary>
    public static LaunchTurn Free => new(string.Empty, null);

    /// <summary>Do not start it — the sentence names what was found and what cures it.</summary>
    public static LaunchTurn Refused(string reason) => new(reason, null);

    /// <summary>
    /// Start it, and give <paramref name="held"/> back once the launch has been read — with <paramref name="warning"/>, the
    /// sentence the person is owed about a launch that was ALLOWED (a CLI version nobody measured), carried to the outcome.
    /// </summary>
    public static LaunchTurn Holding(IDisposable held, string warning = "") => new(string.Empty, held, warning);

    /// <summary>Empty when the launch may start.</summary>
    public string Refusal { get; }

    /// <summary>What the person should know about a launch that may start; empty for nearly every launch.</summary>
    public string Warning { get; }

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
