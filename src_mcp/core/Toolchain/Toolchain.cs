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
public sealed record ToolchainProject(string Path, string Adapter, ToolchainCommand Build, ToolchainCommand Test, int? MemoryMb);

/// <summary>How long one build step and one test step may run before the process tree is killed.</summary>
public sealed record ToolchainTimeouts(TimeSpan Build, TimeSpan Test)
{
    /// <summary>What a file that names no timeout gets.</summary>
    public static ToolchainTimeouts Default { get; } = new(ToolchainTables.DefaultBuildTimeout, ToolchainTables.DefaultTestTimeout);
}

/// <summary>A repository's toolchain file, read and checked.</summary>
/// <param name="Projects">At least one, in the file's order — the order the checks run in.</param>
/// <param name="Skip">The optional steps the repository leaves out (each of <see cref="ToolchainTables.SkippableSteps"/>).</param>
/// <param name="Timeouts">The per-step limits.</param>
public sealed record Toolchain(IReadOnlyList<ToolchainProject> Projects, IReadOnlyList<string> Skip, ToolchainTimeouts Timeouts);

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
    public sealed record Configured(Toolchain Toolchain) : ToolchainRead;

    /// <summary>The file is refused; each fault names the key it is about.</summary>
    public sealed record Refused(IReadOnlyList<string> Faults) : ToolchainRead
    {
        /// <summary>All the faults in one line, for a log or a notice.</summary>
        public string Sentence => string.Join("; ", Faults);
    }
}
