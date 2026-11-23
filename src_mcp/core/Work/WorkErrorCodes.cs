namespace CoaiMcp.Core.Work;

/// <summary>
/// The error codes of the work-plan validator, written ONCE. The rules add errors by these names; <see cref="All"/> is the list the vectors file (<c>shared/work-plan-vectors.json</c>)
/// must cover with one failing and one passing example each, and the table of <c>shared/work-method.md</c> must list.
/// </summary>
public static class WorkErrorCodes
{
    public const string YamlTooLarge = "yaml-too-large";

    public const string YamlSyntax = "yaml-syntax";

    public const string YamlForbidden = "yaml-forbidden";

    public const string YamlDuplicateKey = "yaml-duplicate-key";

    public const string MissingFile = "missing-file";

    public const string WrongType = "wrong-type";

    public const string MissingKey = "missing-key";

    public const string UnknownKey = "unknown-key";

    public const string BadId = "bad-id";

    public const string BadValue = "bad-value";

    public const string FolderIdMismatch = "folder-id-mismatch";

    public const string ListFolderMismatch = "list-folder-mismatch";

    public const string TooManyEpics = "too-many-epics";

    public const string TooManyStories = "too-many-stories";

    public const string TooManyTasks = "too-many-tasks";

    public const string MvpCount = "mvp-count";

    public const string MvpOrder = "mvp-order";

    public const string StoryWithoutTasks = "story-without-tasks";

    public const string UnknownDependency = "unknown-dependency";

    public const string AmbiguousDependency = "ambiguous-dependency";

    public const string DependencyCycle = "dependency-cycle";

    public const string ParallelDependent = "parallel-dependent";

    public const string ParallelFiles = "parallel-files";

    public const string ParallelOverlap = "parallel-overlap";

    public const string EmptyDod = "empty-dod";

    public const string DuplicateDodId = "duplicate-dod-id";

    public const string SecurityContradiction = "security-contradiction";

    /// <summary>Every code, in the order the table of <c>shared/work-method.md</c> lists them.</summary>
    public static readonly IReadOnlyList<string> All =
    [
        YamlTooLarge,
        YamlSyntax,
        YamlForbidden,
        YamlDuplicateKey,
        MissingFile,
        WrongType,
        MissingKey,
        UnknownKey,
        BadId,
        BadValue,
        FolderIdMismatch,
        ListFolderMismatch,
        TooManyEpics,
        TooManyStories,
        TooManyTasks,
        MvpCount,
        MvpOrder,
        StoryWithoutTasks,
        UnknownDependency,
        AmbiguousDependency,
        DependencyCycle,
        ParallelDependent,
        ParallelFiles,
        ParallelOverlap,
        EmptyDod,
        DuplicateDodId,
        SecurityContradiction,
    ];
}
