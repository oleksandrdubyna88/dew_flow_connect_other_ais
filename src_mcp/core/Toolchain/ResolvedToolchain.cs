namespace CoaiMcp.Core.Toolchain;

/// <summary>Where the checks of a repository came from.</summary>
public enum ToolchainSource
{
    /// <summary>The file at the base commit named every command of every project.</summary>
    File,

    /// <summary>The file at the base commit, and for a project that names no command the adapter's defaults of its own folder, read at the base.</summary>
    FileAndDefaults,

    /// <summary>No file at the base: the manifests of the base and of the head turned into commands.</summary>
    Detected,
}

/// <summary>
/// One project of a resolved toolchain: a project of the file whose commands the file states (<see cref="FileProject"/>), or one the detectors made (<see cref="Detected"/>). A project the head has and the base does not is <see cref="DefinedByHead"/>.
/// </summary>
/// <param name="Id">The path of the file that makes the project (the project path of the file for a file project).</param>
/// <param name="Origin"><c>file</c>, <c>defaults</c> (a project of the file with no command, given the defaults of its folder at the base) or <c>detected</c>.</param>
/// <param name="DefinedByHead">True when only the head has the project: its definition is the head's own, which the code gate reads.</param>
/// <param name="FileProject">The file's project, for <c>file</c>.</param>
/// <param name="Detected">What a detector made, for <c>defaults</c> and <c>detected</c>.</param>
public sealed record ResolvedProject(string Id, string Origin, bool DefinedByHead, ToolchainProject? FileProject, DetectedProject? Detected);

/// <summary>The checks of a repository, decided once for a round by <see cref="ToolchainResolve"/>.</summary>
/// <param name="Source">Where they came from.</param>
/// <param name="Projects">In the order they run: the file's order, else path order.</param>
/// <param name="File">The file at the base, when there was one (its skip list and timeouts apply).</param>
/// <param name="Notes">What the readers did not do (a submodule, a reference that leaves the repository, a setting that was not usable).</param>
/// <param name="Differences">What differs between the base and the head for a detected or defaulted project, one line each: the facts s3 t5 compares to freeze a check definition. A difference here is NOT yet a refusal.</param>
public sealed record ResolvedToolchain(
    ToolchainSource Source,
    IReadOnlyList<ResolvedProject> Projects,
    RepoToolchain? File,
    IReadOnlyList<string> Notes,
    IReadOnlyList<string> Differences)
{
    /// <summary>What the head changed in the definitions of the checks against the base (owner decision 2026-11-26: reported, never refused; the head's own definitions run).</summary>
    public DefinitionChanges Changes { get; init; } = DefinitionChanges.None;
}

/// <summary>
/// What the head did to the check definitions, as lines for the envelope and the job's record (owner decision 2026-11-26, which replaced the interim gate: nothing the head changes is refused; the head's own definitions run and every difference is reported).
/// </summary>
/// <param name="LostSteps">Steps (project and step) the base had and the head has not: a lost test step is the first thing a person looks at.</param>
/// <param name="ChangedDefinitions">The members of a project's definition that differ (a script, a test project of a solution, a runner, a props or targets file, a configuration file…), one line each, spelled.</param>
/// <param name="ChangedTestCommands">The subset of <paramref name="ChangedDefinitions"/> that is about running tests: a test script, a runner, a test project, a test fact.</param>
public sealed record DefinitionChanges(IReadOnlyList<string> LostSteps, IReadOnlyList<string> ChangedDefinitions, IReadOnlyList<string> ChangedTestCommands)
{
    public static DefinitionChanges None { get; } = new([], [], []);

    /// <summary>Whether anything was lost or changed.</summary>
    public bool Any => LostSteps.Count + ChangedDefinitions.Count > 0;
}

/// <summary>What resolving gave: checks, nothing to check, or a refusal. Neither of the last two is ever green, and no step is planned for either.</summary>
public abstract record ToolchainResolution
{
    /// <summary>The checks to run.</summary>
    public sealed record Resolved(ResolvedToolchain Toolchain) : ToolchainResolution;

    /// <summary><c>not_configured</c>: nothing was found to check.</summary>
    public sealed record NotConfigured(string Note) : ToolchainResolution;

    /// <summary><c>toolchain_refused</c>: the sentence names the path and the cure.</summary>
    public sealed record Refused(string Sentence) : ToolchainResolution;

    private ToolchainResolution() { }
}
