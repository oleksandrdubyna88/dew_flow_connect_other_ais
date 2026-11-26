namespace CoaiMcp.Core.Toolchain;

/// <summary>
/// One step a detector planned for a project: the default argv of <c>shared/toolchains.json</c> with <c>{unit}</c> filled by a path the detector chose, the network state and the folder it runs from. <c>{jobs}</c> and <c>{file}</c> are left in the argv: the runner fills them.
/// </summary>
/// <param name="Name">One of <see cref="ToolchainDefaults.StepNames"/>.</param>
/// <param name="Argv">The command, one item to an argument. No item is ever copied from a manifest: every item is the table's, or a path built from the paths of the commit.</param>
/// <param name="Network">The network state the table gives the step.</param>
/// <param name="WorkingFolder">The folder the step runs from, relative to the repository root; <c>.</c> for the root.</param>
/// <param name="Subject">What the step is about: the test project of a .NET test step; the project's own file (<see cref="DetectedProject.Id"/>) for every other step.</param>
/// <param name="Extensions">The extensions a formatter is given; empty for any other step.</param>
public sealed record DetectedStep(string Name, IReadOnlyList<string> Argv, DefaultNetwork Network, string WorkingFolder, string Subject, IReadOnlyList<string> Extensions);

/// <summary>
/// A project of a detected toolchain: a .NET unit (a solution and what it owns, or a project file nothing references) or a Node package. <see cref="Facts"/> are what the detector read, in a fixed order, so two detections of one commit are equal and a difference between two commits can be shown.
/// </summary>
/// <param name="Stack">The stack id of <c>toolchains.json</c>.</param>
/// <param name="Id">The path of the file that makes the project: the solution file, the project file or <c>package.json</c>, relative to the repository root.</param>
/// <param name="Folder">The project's folder, relative to the repository root; <c>.</c> for the root.</param>
/// <param name="Steps">The steps, in the order of a round (restore, format, build, typecheck, test); empty for an unchecked project.</param>
/// <param name="Facts">What was read: runner, test projects, lock file, scripts; by name, in a fixed order.</param>
/// <param name="Notes">What the reader did not do or could not resolve (a reference that leaves the repository, a condition that was not evaluated).</param>
/// <param name="TestsNone">True for a project that has no test step and says so: <c>tests: none</c>.</param>
public sealed record DetectedProject(
    string Stack,
    string Id,
    string Folder,
    IReadOnlyList<DetectedStep> Steps,
    IReadOnlyList<KeyValuePair<string, string>> Facts,
    IReadOnlyList<string> Notes,
    bool TestsNone)
{
    /// <summary>The project files of a .NET unit, as paths (never cut at a comma: a path may hold one); empty for any other stack.</summary>
    public IReadOnlyList<string> OwnedPaths { get; init; } = [];

    /// <summary>The test project files of a .NET unit, as paths, in step order.</summary>
    public IReadOnlyList<string> TestPaths { get; init; } = [];

    /// <summary>The value of a fact, or empty.</summary>
    public string Fact(string name) => Facts.FirstOrDefault(pair => pair.Key == name).Value ?? string.Empty;
}

/// <summary>A detector's answer: the projects of the manifests it was given, or the sentence that says why they cannot be turned into commands. There is no partial answer: one refused project refuses the repository.</summary>
public abstract record Detection
{
    /// <summary>The projects, in path order; empty when the stack has no manifest in the set.</summary>
    public sealed record Detected(IReadOnlyList<DetectedProject> Projects) : Detection;

    /// <summary>Refused. The sentence names the files and the cure: the commands in <c>.coai/toolchain.yaml</c>.</summary>
    public sealed record Refused(string Sentence) : Detection;

    private Detection() { }
}
