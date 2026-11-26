using CoaiMcp.Core.QuestionConsult;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Reviewers;
using CoaiMcp.Runners.Toolchain;

namespace CoaiMcp.Runners.Consultation;

/// <summary>What a vendor's write-capability probe concluded: launchable on this release, or why not — with the cure and whether the CLI could not even be started.</summary>
internal abstract record CoderCapability
{
    private CoderCapability()
    {
    }

    /// <summary>Launchable. <paramref name="Version"/> is the release as the CLI's own <c>--version</c> printed it.</summary>
    public sealed record Ready(string Version) : CoderCapability;

    /// <summary>Not launchable: what is wrong in one sentence, the cure, and whether the CLI could not be started at all.</summary>
    public sealed record Refused(string Said, string Cure, bool CouldNotStart = false) : CoderCapability;
}

/// <summary>Where a vendor's CLI keeps its files inside the coder's unit: its home, the file its sign-in is read from, and the folders coai makes before the preflight.</summary>
/// <param name="Home">The <c>HOME</c> of the coder's process.</param>
/// <param name="SignInFile">The path inside <paramref name="Home"/> where the CLI looks for its sign-in.</param>
/// <param name="Folders">The folders coai makes for the unit, in the order they are made.</param>
internal sealed record CoderHomeLayout(string Home, string SignInFile, IReadOnlyList<string> Folders);

/// <summary>
/// How the sign-in of a vendor reaches its coder unit — as DATA, so the seam can state it before any code that acts on it exists (judgement S3, S6).
/// </summary>
internal abstract record CoderSignInRecipe
{
    private CoderSignInRecipe()
    {
    }

    /// <summary>The real file, bound read-only into the coder's home: what the claude coder has had since E2, and keeps.</summary>
    /// <param name="RelativePath">Where the file is under the real home and under the coder's home, <c>/</c>-separated.</param>
    public sealed record BindRealFile(string RelativePath) : CoderSignInRecipe;

    /// <summary>
    /// A COPY coai makes for the job (judgement S3): mode 0600 in a 0700 folder of the job's coder home, never a bind of the real file. <paramref name="Placeholders"/> says which field is replaced by a placeholder,
    /// <paramref name="RefreshStep"/> names a refresh with no model, and <paramref name="Gate"/> the id of the property asked before every launch. Acted on by s7 t6.
    /// </summary>
    public sealed record CopyForJob(string RelativePath, IReadOnlyList<string> KeptFields, IReadOnlyDictionary<string, string> Placeholders, string RefreshStep, string Gate) : CoderSignInRecipe;
}

/// <summary>
/// The seam of a coder's vendor (epoch 3 E3 s7 t1, judgement S6; SPEC C6): everything about a coder launch that differs between the CLIs, in one place — the capability probe, the fresh and the resumed argv, the property list of the unit, its working directory,
/// its environment allow-list, its home, its sign-in recipe, the real path of its program and the id of its coder prompt. The claude implementation is what <c>ClaudeConsultant</c>, <c>CoderUnit</c> and <c>CoderProgram</c> did before, moved here and
/// unchanged; a vendor is added by a type, never by a branch in the code that launches.
/// </summary>
/// <remarks>
/// A vendor does not decide whether it is launched: <see cref="CoderVendorSet"/> reads <see cref="CoderBoundaryTable"/> for that, and refuses a vendor with no prompt of its own (SPEC C7).
/// </remarks>
internal interface ICoderVendor
{
    /// <summary>The vendor's id: the first word of a <c>ModelRef</c>, a row of <see cref="CoderBoundaryTable"/> (claude is the exception).</summary>
    string Id { get; }

    /// <summary>The id of the vendor's coder prompt in <c>RolePrompts</c> (<c>coder</c> is claude's). EMPTY means the vendor has none, and then it cannot be launched as a coder: no vendor is given claude's tool names by default (SPEC C7).</summary>
    string PromptId { get; }

    /// <summary>How the sign-in reaches the coder's unit.</summary>
    CoderSignInRecipe SignIn { get; }

    /// <summary>Asks the installed CLI whether it can be given the write launch (<c>--version</c>, <c>--help</c>) — never launches the coder itself.</summary>
    Task<CoderCapability> ProbeAsync(IProcessLauncher launcher, string executable, string workingDirectory, CancellationToken ct);

    /// <summary>The arguments of turn 0 (after the executable), for <paramref name="model"/> in <paramref name="worktree"/>.</summary>
    IReadOnlyList<string> FreshArgv(string model, string worktree);

    /// <summary>The arguments of a turn that continues <paramref name="sessionId"/>.</summary>
    IReadOnlyList<string> ResumedArgv(string model, string worktree, string sessionId);

    /// <summary>The <c>-p</c> properties of the vendor's unit: what it hides, what it binds read-only, what it may write, its network. A vendor can HIDE the main checkout and the coder root where claude binds them read-only.</summary>
    IReadOnlyList<string> UnitProperties(BoundaryHost host, BoundaryJob job, CoderProgram program, TimeSpan runtimeMax);

    /// <summary>The folder the unit starts in: claude's is the worktree; a vendor whose guard lives beside it starts in its guard folder.</summary>
    string WorkingDirectory(BoundaryJob job);

    /// <summary>The environment allow-list as the unit sets it inside: exactly these names and no others.</summary>
    IReadOnlyList<KeyValuePair<string, string>> Environment(BoundaryHost host, BoundaryJob job);

    /// <summary>The home layout of the vendor's CLI.</summary>
    CoderHomeLayout Home(BoundaryJob job);

    /// <summary>The program the unit runs, as the unit must be given it: its real path, with a problem when it cannot be run inside the layer.</summary>
    CoderProgram Program(string executable, string path, BoundaryJob job);
}

/// <summary>
/// claude as a coder vendor: the measured flag set R11 and the unit of E2, byte for byte what they were before the seam existed — the existing coder tests hold them, and <c>ACoderVendorSeamTests</c> holds the argv and the property list against literals.
/// </summary>
internal sealed class ClaudeCoderVendor : ICoderVendor
{
    /// <summary>The one instance: the vendor holds no state.</summary>
    public static ClaudeCoderVendor Instance { get; } = new();

    /// <summary>The coder's tools: the read tools and the two that write — still no shell (R11), ONE argument, comma-joined, the form the probe measured.</summary>
    private static readonly string WriteTools = string.Join(',', ConfinementPlanner.ClaudeWriteTools);

    private ClaudeCoderVendor()
    {
    }

    public string Id => CoderBoundaryTable.Claude;

    public string PromptId => "coder";

    public CoderSignInRecipe SignIn { get; } = new CoderSignInRecipe.BindRealFile(".claude/.credentials.json");

    public async Task<CoderCapability> ProbeAsync(IProcessLauncher launcher, string executable, string workingDirectory, CancellationToken ct) =>
        await ClaudeWriteCapability.ProbeAsync(launcher, executable, workingDirectory, ct) switch
        {
            ClaudeWriteCapability.Ready ready => new CoderCapability.Ready(ready.Version),
            ClaudeWriteCapability.Refused refused => new CoderCapability.Refused(refused.Said, refused.Cure, refused.CouldNotStart),
            _ => throw new InvalidOperationException("a write capability is ready or refused"),
        };

    public IReadOnlyList<string> FreshArgv(string model, string worktree) => [.. Core(worktree), .. Model(model)];

    public IReadOnlyList<string> ResumedArgv(string model, string worktree, string sessionId) => [.. Core(worktree), "--resume", sessionId, .. Model(model)];

    public IReadOnlyList<string> UnitProperties(BoundaryHost host, BoundaryJob job, CoderProgram program, TimeSpan runtimeMax) =>
        CoderUnit.Properties(host, job, program, runtimeMax);

    public string WorkingDirectory(BoundaryJob job) => job.Tree.Root;

    public IReadOnlyList<KeyValuePair<string, string>> Environment(BoundaryHost host, BoundaryJob job) => CoderUnit.Environment(host, job);

    public CoderHomeLayout Home(BoundaryJob job) => new(job.CoderHomeFolder, job.CoderSignInFile, job.CoderFolders);

    public CoderProgram Program(string executable, string path, BoundaryJob job) => CoderProgram.Resolve(executable, path, job);

    /// <summary>
    /// The measured flag set R11 (<c>v2/spikes/RESULTS_coder_write_boundary.md</c>): <c>--restricted</c>, the five tools with no shell, <c>acceptEdits</c> with nobody to answer a prompt, no MCP server, the worktree as the only added directory.
    /// The model is the only option added: effort and fast mode were not measured with this set, and fast mode writes a settings file, so neither is sent.
    /// </summary>
    private static string[] Core(string worktree) =>
    [
        "-p",
        "--output-format", "json",
        ClaudeCapability.RestrictedFlag,
        "--tools", WriteTools,
        "--permission-mode", ClaudeWriteCapability.AcceptEdits,
        ClaudeWriteCapability.PermissionPromptsFlag, "none",
        NoMcpServers.ClaudeFlag,
        "--add-dir", worktree,
    ];

    private static string[] Model(string model) => model.Length > 0 ? ["--model", model] : [];
}

/// <summary>
/// The vendors a coder can be launched on, as ONE value: the table that says which of them ship (<see cref="CoderBoundaryTable"/>) and the implementations of the seam. The product's set is <see cref="CoderVendors.Shipped"/>;
/// a test injects its own table and its own vendor, and so proves the code for every combination of verdicts without a real CLI (judgement S5, E3 s7 t1 d5).
/// </summary>
/// <param name="Table">The one table of verdicts and measured releases.</param>
/// <param name="Implemented">The vendors that have a type of their own. claude is always one of them.</param>
internal sealed record CoderVendorSet(CoderBoundaryTable Table, IReadOnlyList<ICoderVendor> Implemented)
{
    /// <summary>The set with <paramref name="table"/> and the implementations given, claude added.</summary>
    public static CoderVendorSet Of(CoderBoundaryTable table, params ICoderVendor[] vendors) => new(table, [ClaudeCoderVendor.Instance, .. vendors]);

    /// <summary>The seam of <paramref name="vendor"/>, or why there is none: the table's sentence when it does not ship, or the sentence of a vendor that ships but has no type or no prompt.</summary>
    public CoderVendorResolution Resolve(string vendor)
    {
        if (Table.WhyNot(vendor) is { Length: > 0 } why)
        {
            return new CoderVendorResolution.Refused(why);
        }

        var found = Implemented.FirstOrDefault(candidate => candidate.Id == vendor);
        if (found is null)
        {
            return new CoderVendorResolution.Refused($"the coder has no launch built for '{vendor}' in this version; the coder vendors that are shipped are {string.Join(", ", Table.ShippedVendors)}");
        }

        return found.PromptId.Length == 0
            ? new CoderVendorResolution.Refused($"'{vendor}' has no coder prompt of its own, and a vendor is never given another vendor's prompt; the coder vendors that are shipped are {string.Join(", ", Table.ShippedVendors.Where(shipped => shipped != vendor))}")
            : new CoderVendorResolution.Launchable(found);
    }

    /// <summary>The vendors that may be launched now: those of <see cref="CoderBoundaryTable.ShippedVendors"/> that also resolve.</summary>
    public IReadOnlyList<string> Launchable => [.. Table.ShippedVendors.Where(vendor => Resolve(vendor) is CoderVendorResolution.Launchable)];
}

/// <summary>What <see cref="CoderVendorSet.Resolve"/> found.</summary>
internal abstract record CoderVendorResolution
{
    private CoderVendorResolution()
    {
    }

    /// <summary>The vendor may be launched as a coder.</summary>
    public sealed record Launchable(ICoderVendor Vendor) : CoderVendorResolution;

    /// <summary>It may not; <paramref name="Sentence"/> says why, names the shipped vendors and ends with what to do.</summary>
    public sealed record Refused(string Sentence) : CoderVendorResolution;
}

/// <summary>The product's coder vendors.</summary>
internal static class CoderVendors
{
    /// <summary>claude: always launchable, as it was before the seam.</summary>
    public static ICoderVendor Claude => ClaudeCoderVendor.Instance;

    /// <summary>The set the product launches from: the embedded table and the vendors that have a type. Today that is claude alone; s5 and s6 each add their vendor's type.</summary>
    public static CoderVendorSet Shipped => Loaded.Value;

    private static readonly Lazy<CoderVendorSet> Loaded = new(() => CoderVendorSet.Of(CoderBoundaryTable.Shipped));
}
