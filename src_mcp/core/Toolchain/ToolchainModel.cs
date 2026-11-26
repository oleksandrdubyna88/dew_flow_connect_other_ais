namespace CoaiMcp.Core.Toolchain;

/// <summary>
/// One command of a project as the toolchain file states it: an argv list, started WITHOUT a shell. <see cref="Stated"/> tells a command the file did not name (the
/// adapter's default applies, a later story) from one it named as <c>[]</c> (no such step) — absent is not empty.
/// </summary>
/// <param name="Stated">Whether the file names this command at all.</param>
/// <param name="Argv">The executable first, then its arguments, verbatim; <c>{jobs}</c> is replaced when the step runs. Empty when not stated or stated as <c>[]</c>.</param>
public sealed record ToolchainCommand(bool Stated, IReadOnlyList<string> Argv)
{
    /// <summary>The command a file did not name.</summary>
    public static ToolchainCommand NotStated { get; } = new(false, []);

    /// <summary>Whether there is something to start: stated, and not <c>[]</c>.</summary>
    public bool Runs => Stated && Argv.Count > 0;
}

/// <summary>One project of the file: where it is, which adapter knows it, and the two commands of the checks.</summary>
/// <param name="Path">The project folder, repository-relative with <c>/</c>; never absolute, never with <c>..</c>.</param>
/// <param name="Adapter">One of <see cref="ToolchainTables.Adapters"/>.</param>
/// <param name="Build">The build command.</param>
/// <param name="Test">The test command — for this repository the MTP executable, never <c>dotnet test</c>.</param>
/// <param name="MemoryMb">The memory the scheduler reserves for the project's heavy steps, or null when the file does not say.</param>
/// <remarks>A project of the file runs EXACTLY what it states (SPEC §A2). The names <c>restore</c>, <c>format</c>, <c>testFor</c> and <c>testFiles</c> are init properties so that every earlier construction of a project stays valid.</remarks>
public sealed record ToolchainProject(string Path, string Adapter, ToolchainCommand Build, ToolchainCommand Test, int? MemoryMb)
{
    /// <summary>The restore command (epoch 3 E4): runs before the build, on the host network. Not stated: the adapter's default, only for a project that states no command at all.</summary>
    public ToolchainCommand Restore { get; init; } = ToolchainCommand.NotStated;

    /// <summary>The formatter, run on the files a turn changed (<c>{file}</c> is replaced by one item per file). Not stated: the adapter's default, only for a project that states no command at all.</summary>
    public ToolchainCommand Format { get; init; } = ToolchainCommand.NotStated;

    /// <summary>The command that runs the tests of some files first (<c>{file}</c> or <c>{stem}</c>): the repository's own word for "run the tests of these files". There is no default. It needs <see cref="TestFiles"/>.</summary>
    public ToolchainCommand TestFor { get; init; } = ToolchainCommand.NotStated;

    /// <summary>The folder prefixes, inside the project, that hold test files; empty when the file names none.</summary>
    public IReadOnlyList<string> TestFiles { get; init; } = [];

    /// <summary>Whether the file states ANY command for the project: a project that states none gets the adapter's defaults of its own folder; one that states any is run exactly as written, and only its stated steps run.</summary>
    public bool StatesACommand => Build.Stated || Test.Stated || Restore.Stated || Format.Stated || TestFor.Stated;
}

/// <summary>How long one build step and one test step may run before the process tree is killed.</summary>
public sealed record ToolchainTimeouts(TimeSpan Build, TimeSpan Test)
{
    /// <summary>What a file that names no timeout gets.</summary>
    public static ToolchainTimeouts Default { get; } = new(ToolchainTables.DefaultBuildTimeout, ToolchainTables.DefaultTestTimeout);
}

/// <summary>A repository's toolchain file, read and checked. (Not named <c>Toolchain</c>: that is the namespace's own name, and a type of that name hides it from the runners' namespace of the same last segment.)</summary>
/// <param name="Projects">At least one, in the file's order — the order the checks run in.</param>
/// <param name="Skip">The optional steps the repository leaves out (each of <see cref="ToolchainTables.SkippableSteps"/>).</param>
/// <param name="Timeouts">The per-step limits.</param>
public sealed record RepoToolchain(IReadOnlyList<ToolchainProject> Projects, IReadOnlyList<string> Skip, ToolchainTimeouts Timeouts);

/// <summary>
/// What reading a toolchain file gave — a closed set: there is no file (checks are <c>not_configured</c>, which is never green), a file that is right, or a file that is refused with every
/// sentence that says why.
/// </summary>
public abstract record ToolchainRead
{
    private ToolchainRead()
    {
    }

    /// <summary>There is no toolchain file: not an error, and not an empty project list.</summary>
    public sealed record NotConfigured : ToolchainRead;

    /// <summary>The file is right.</summary>
    public sealed record Configured(RepoToolchain Toolchain) : ToolchainRead;

    /// <summary>The file is refused; each fault names the key it is about.</summary>
    public sealed record Refused(IReadOnlyList<string> Faults) : ToolchainRead
    {
        /// <summary>All the faults in one line, for a log or a notice.</summary>
        public string Sentence => string.Join("; ", Faults);
    }
}
