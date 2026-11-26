using System.Text;
using CoaiMcp.Core.QuestionConsult;
using CoaiMcp.Runners.Files;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Reviewers;
using CoaiMcp.Runners.Toolchain;

namespace CoaiMcp.Runners.Consultation;

/// <summary>
/// codex as a coder vendor (epoch 3 E3 s5 t2; judgement section 13.4, <c>v2/spikes/RESULTS_codex_coder.md</c> section 2): the measured argv fresh and resumed, the permission profile in the job's <c>config.toml</c> before every turn, <c>CODEX_HOME</c> inside the job's coder folder, the unit that HIDES the main checkout
/// and the coder root, the minimal environment with the refresh override, and a sign-in that is a COPY with a placeholder in the place of the refresh token.
/// </summary>
/// <remarks>
/// <para><b>The release gates nothing.</b> The vendor asks the installed CLI what it declares (<see cref="CodexWriteCapability"/>) and nothing else; the measured release is history in the table.</para>
/// <para><b>No flag of the fresh argv is dropped on a resumed turn</b> (section 13.4): <see cref="Flags"/> is the one list both argvs are built from, and a test holds the two sets equal.</para>
/// </remarks>
internal sealed class CodexCoderVendor : ICoderVendor
{
    /// <summary>The one instance: the vendor holds no state.</summary>
    public static CodexCoderVendor Instance { get; } = new();

    /// <summary>The vendor's id.</summary>
    public const string VendorId = "codex";

    /// <summary>The reasoning effort of every coder turn: the coder has no setting of its own yet, and the measure used a low one for cost only (<c>todo</c>: an effort per coder model).</summary>
    public const string Effort = "medium";

    /// <summary>The address the refresh is sent to, a dead local one (id CX-R2): a copy that tries to refresh meets a refused connection, never the vendor.</summary>
    public const string RefreshOverrideUrl = "http://127.0.0.1:9/oauth/token";

    /// <summary>The name of the file a codex home must NOT hold (judgement 13.11, CX-C1 launch half): a third program of the folder runs commands as an "environment" it names.</summary>
    public const string EnvironmentsFile = "environments.toml";

    /// <summary>The 19 features CX1 disables (results section 2): the account's connectors, plugins, browsers, computer use, image tools, the agent tool, goals, the sleep tool, the tool suggestions, skills search, hooks and the in-app tools.</summary>
    public static IReadOnlyList<string> DisabledFeatures { get; } =
    [
        "apps", "plugins", "remote_plugin", "browser_use", "browser_use_external", "browser_use_full_cdp_access", "computer_use", "image_generation", "view_image", "multi_agent",
        "goals", "sleep_tool", "tool_suggest", "skill_search", "skill_mcp_dependency_install", "hooks", "in_app_browser", "in_app_chat", "in_app_local_automation",
    ];

    /// <summary>The flags that are never on a coder's argv (SPEC section C3): none is reachable, and a test tries each.</summary>
    public static IReadOnlyList<string> NeverFlags { get; } =
    [
        "-s", "--sandbox", "--dangerously-bypass-approvals-and-sandbox", "--dangerously-bypass-hook-trust", "--trust", "--add-dir", "--ephemeral", "--worktree", "--enable", "-i", "--image", "--oss", "-p", "--profile", "--full-auto", "--cd", "-C",
    ];

    private CodexCoderVendor()
    {
    }

    public string Id => VendorId;

    public string PromptId => "coder.codex";

    public string PromptResource => "CoaiMcp.coder-prompt-codex.md";

    public CoderSignInRecipe SignIn { get; } = new CoderSignInRecipe.CopyForJob(
        ".codex/auth.json",
        ["auth_mode", "tokens/id_token", "tokens/access_token", "tokens/account_id", "tokens/login_owner_id", "tokens/oauth_provenance", "last_refresh"],
        new Dictionary<string, string> { ["tokens/refresh_token"] = SignInCopy.Placeholder },
        string.Empty,
        "CX-R1")
    {
        ExpiryField = "tokens/access_token",
        ExpiryIsJwt = true,
        Margin = TimeSpan.FromMinutes(60),
        TokenFields = ["tokens/access_token", "tokens/id_token"],
        HomeVariable = "CODEX_HOME",
    };

    public IConsultantRuntime Runtime => new CodexConsultant(new CodexRuntime(), VendorId);

    public string FixClause => "you do not run them";

    public bool PointsAtInstructionBlock => true;

    /// <summary>The turn check of codex (s5 t4): the stream is read there. Until it is built the stream says nothing wrong.</summary>
    public CoderTurnVerdict CheckTurn(ProcessResult process, string expectedSession) => CodexTurnCheck.Check(process, expectedSession);

    public async Task<CoderCapability> ProbeAsync(IProcessLauncher launcher, string executable, string workingDirectory, CancellationToken ct) =>
        await CodexWriteCapability.ProbeAsync(launcher, executable, workingDirectory, ct) switch
        {
            CodexWriteCapability.Ready ready => new CoderCapability.Ready(ready.Version),
            CodexWriteCapability.Refused refused => new CoderCapability.Refused(refused.Said, refused.Cure, refused.CouldNotStart),
            _ => throw new InvalidOperationException("a write capability is ready or refused"),
        };

    /// <summary>
    /// Every flag of the argv, in order, for <paramref name="model"/>: the stream, the model and effort, the 19 disabled features, the approval policy, the environment policy, the web search, the agent switch (<c>-c agents.enabled=false</c>, section 13.4, not optional), then the output file
    /// when one is given. The prompt is on stdin (<c>-</c>); the worktree is the working folder of the unit, never a flag.
    /// </summary>
    public static IReadOnlyList<string> Flags(string model, CoderLaunchFiles files) =>
    [
        "--json",
        "-m", model,
        "-c", $"model_reasoning_effort={Effort}",
        .. DisabledFeatures.SelectMany(feature => new[] { "--disable", feature }),
        "-c", "approval_policy=\"never\"",
        "-c", "shell_environment_policy.inherit=\"core\"",
        "-c", "web_search=\"disabled\"",
        "-c", "agents.enabled=false",
        .. files.OutputFile.Length > 0 ? ["-o", files.OutputFile] : Array.Empty<string>(),
    ];

    public IReadOnlyList<string> FreshArgv(string model, string worktree) => FreshArgv(model, worktree, CoderLaunchFiles.None);

    public IReadOnlyList<string> FreshArgv(string model, string worktree, CoderLaunchFiles files) => ["exec", .. Flags(model, files), "-"];

    public IReadOnlyList<string> ResumedArgv(string model, string worktree, string sessionId) => ResumedArgv(model, worktree, sessionId, CoderLaunchFiles.None);

    public IReadOnlyList<string> ResumedArgv(string model, string worktree, string sessionId, CoderLaunchFiles files) => ["exec", "resume", .. Flags(model, files), sessionId, "-"];

    /// <summary>
    /// The unit as measured (<c>RESULTS_codex_coder.md</c> section 3, judgement X5, U2, U3, U6, U8): the main checkout and the coder root are HIDDEN (an empty tmpfs, not read-only), only <c>&lt;main&gt;/.git</c> and the worktree come back, the folder of the jobs is hidden, <c>/tmp</c> is a tmpfs (the shape under
    /// which <c>codex sandbox</c> starts), the vendor's folder of the job is the one writable bind, the program's folder is read-only. Never <c>RestrictNamespaces</c> (the vendor sandbox needs namespaces and refuses to run without them) and never <c>ReadWritePaths=/mnt</c>.
    /// </summary>
    public IReadOnlyList<string> UnitProperties(BoundaryHost host, BoundaryJob job, CoderProgram program, TimeSpan runtimeMax)
    {
        var tree = job.Tree;

        return
        [
            .. BoundaryUnit.Isolation,
            $"TemporaryFileSystem={tree.MainCheckout}",
            $"BindReadOnlyPaths={Path.Combine(tree.MainCheckout, ".git")}",
            $"TemporaryFileSystem={job.CoderRoot}",
            $"BindPaths={tree.Root}",
            $"BindReadOnlyPaths={tree.GitEntry}",
            .. BoundaryUnit.Mounts(job),
            $"TemporaryFileSystem={job.JobCover}:ro",
            $"BindPaths={job.CoderVendorFolder(VendorId)}",
            $"TemporaryFileSystem={BoundaryUnit.UnitTemp}",
            $"BindPaths={job.CoderVarTempOf(VendorId)}:{BoundaryUnit.UnitVarTemp}",
            .. BoundaryUnit.KeysHidden(job),
            .. BoundaryUnit.SignInHidden(host),
            .. program.BindFolder.Length > 0 ? [$"BindReadOnlyPaths={program.BindFolder}"] : Array.Empty<string>(),
            .. host.ResolverFile.Length > 0 ? [$"BindReadOnlyPaths={host.ResolverFile}"] : Array.Empty<string>(),
            .. BoundaryUnit.Hardening,
            "PrivateNetwork=no",
            .. BoundaryUnit.Limits(job, runtimeMax),
        ];
    }

    public string WorkingDirectory(BoundaryJob job) => job.Tree.Root;

    /// <summary>The whole environment of the CLI (id U7, CX-R2): the allow-list, <c>CODEX_HOME</c> inside the job's coder folder and the refresh override with its dead local address — nothing else.</summary>
    public IReadOnlyList<KeyValuePair<string, string>> Environment(BoundaryHost host, BoundaryJob job) =>
    [
        new("PATH", BoundaryHost.SystemPath),
        new("HOME", job.CoderHomeOf(VendorId)),
        new("CODEX_HOME", CodexHome(job)),
        new("CODEX_REFRESH_TOKEN_URL_OVERRIDE", RefreshOverrideUrl),
        new("TMPDIR", BoundaryUnit.UnitTemp),
        new("LANG", "C.UTF-8"),
        new("TERM", "dumb"),
        new("NO_COLOR", "1"),
        new("GIT_TERMINAL_PROMPT", "0"),
    ];

    public CoderHomeLayout Home(BoundaryJob job) =>
        new(job.CoderHomeOf(VendorId), Path.Combine(CodexHome(job), "auth.json"), [job.CoderHomeOf(VendorId), CodexHome(job), job.CoderTempOf(VendorId), job.CoderVarTempOf(VendorId)]);

    public CoderProgram Program(string executable, string path, BoundaryJob job) => CoderProgram.Resolve(executable, path, job);

    /// <summary>The job's <c>CODEX_HOME</c>: <c>&lt;job&gt;/coder/codex/home/.codex</c>.</summary>
    public static string CodexHome(BoundaryJob job) => Path.Combine(job.CoderHomeOf(VendorId), ".codex");

    /// <summary>Where the report of a turn is written inside the unit (the vendor's own folder of the job): read on the host after the unit ended and copied to the file the launch named.</summary>
    public static string ReportFile(BoundaryJob job) => Path.Combine(job.CoderVendorFolder(VendorId), "report.txt");

    /// <summary>Where the output schema of the report is written for the CLI to read.</summary>
    public static string SchemaFile(BoundaryJob job) => Path.Combine(job.CoderVendorFolder(VendorId), "report.schema.json");

    /// <summary>The text of the job's <c>config.toml</c> (results section 2): the model and effort, the permission profile <c>coai</c> as the default — the platform minimum and the program's folder to read, the worktree to write; no network table.</summary>
    public static string ConfigText(string model, string programFolder) =>
        $"model = {Quote(model)}\nmodel_reasoning_effort = {Quote(Effort)}\ndefault_permissions = \"coai\"\n\n[permissions.coai.filesystem]\n\":minimal\" = \"read\"\n{Quote(programFolder)} = \"read\"\n\":project_roots\" = {{ \".\" = \"write\" }}\n";

    private static string Quote(string value) => "\"" + value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";

    /// <summary>
    /// Before EVERY launch, turn 0 and every fix turn (id CX-C1, judgement X4 and X8): the job's <c>CODEX_HOME</c> is a real folder with no <c>environments.toml</c> in it (one that is there refuses the launch, section 13.11), the <c>config.toml</c> is written and READ BACK byte for byte, the output schema
    /// is written, and the report file of the last turn is gone. The request that comes back names the report file and the schema inside the unit; the original names the host's file the answer is copied to (<see cref="CollectLaunch"/>).
    /// </summary>
    public CoderLaunchPrepared PrepareLaunch(BoundaryHost host, BoundaryJob job, ProcessRequest request)
    {
        var codexHome = CodexHome(job);
        if (SignInCopy.HomeProblem(job.CoderHomeOf(VendorId)) is { Length: > 0 } homeProblem)
        {
            return Refused(request, $"{homeProblem}; the codex coder was not launched");
        }

        if (RealPath.Lstat(codexHome).Kind != EntryKind.Directory)
        {
            return Refused(request, "the CODEX_HOME of the job is not a real folder, so the codex coder was not launched");
        }

        // The launch half of CX-C1: a file that makes the CLI run commands somewhere that is not the vendor sandbox. One look, every launch.
        if (RealPath.Lstat(Path.Combine(codexHome, EnvironmentsFile)).Kind != EntryKind.Missing)
        {
            return Refused(request, $"the job's CODEX_HOME holds a file named {EnvironmentsFile}, which would let the CLI run commands outside its sandbox; the codex coder was not launched");
        }

        var program = CoderProgram.Resolve(request.Executable, host.Path, job);
        if (program.Problem.Length > 0)
        {
            return Refused(request, $"the codex coder was not launched: {program.Problem}");
        }

        var folder = program.BindFolder.Length > 0 ? program.BindFolder : Path.GetDirectoryName(program.Real) ?? string.Empty;
        var wrote = WriteOwnFile(Path.Combine(codexHome, "config.toml"), ConfigText(ModelOf(request.Arguments), folder));
        if (wrote.Length > 0)
        {
            return Refused(request, wrote);
        }

        var schema = WriteOwnFile(SchemaFile(job), CodexReportSchema.Json);
        if (schema.Length > 0)
        {
            return Refused(request, schema);
        }

        DeleteIfThere(ReportFile(job));
        var arguments = request.Arguments.ToList();
        var output = arguments.IndexOf("-o");
        if (output >= 0 && output + 1 < arguments.Count)
        {
            arguments[output + 1] = ReportFile(job);
        }

        var stdin = arguments.LastIndexOf("-");
        arguments.InsertRange(stdin >= 0 ? stdin : arguments.Count, ["--output-schema", SchemaFile(job)]);

        return new CoderLaunchPrepared(request with { Arguments = arguments }, string.Empty);
    }

    /// <summary>After the unit ended: the report file the CLI wrote inside the unit is copied to the file the launch named, where the executor reads it. No report file is no answer (the host file is not made).</summary>
    public void CollectLaunch(BoundaryJob job, ProcessRequest original, ProcessRequest launched)
    {
        var output = original.Arguments.ToList().IndexOf("-o");
        if (output < 0 || output + 1 >= original.Arguments.Count || RealPath.Lstat(ReportFile(job)).Kind != EntryKind.File)
        {
            return;
        }

        try
        {
            File.Copy(ReportFile(job), original.Arguments[output + 1], overwrite: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // The executor finds no answer and says so; the copy is the vendor's own words and a missing one is a turn without a report.
        }
    }

    private static CoderLaunchPrepared Refused(ProcessRequest request, string problem) => new(request, problem);

    /// <summary>The model of an argv: the value after <c>-m</c>.</summary>
    public static string ModelOf(IReadOnlyList<string> arguments)
    {
        var at = arguments.ToList().IndexOf("-m");

        return at >= 0 && at + 1 < arguments.Count ? arguments[at + 1] : string.Empty;
    }

    /// <summary>Writes a file of coai's own into the job's home (mode 0600, made new after a file or a link at its name is removed) and reads it back byte for byte; the sentence when it cannot be, never a path of a link's target.</summary>
    private static string WriteOwnFile(string path, string text)
    {
        try
        {
            DeleteIfThere(path);
            var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None };
            if (!OperatingSystem.IsWindows())
            {
                options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            }

            using (var stream = new FileStream(path, options))
            {
                stream.Write(Encoding.UTF8.GetBytes(text));
            }

            return File.ReadAllText(path) == text ? string.Empty : $"{Path.GetFileName(path)} of the job's CODEX_HOME did not read back as written, so the codex coder was not launched";
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return $"{Path.GetFileName(path)} of the job could not be written ({e.GetType().Name}), so the codex coder was not launched";
        }
    }

    private static void DeleteIfThere(string path)
    {
        if (RealPath.Lstat(path).Kind is EntryKind.File or EntryKind.Link)
        {
            File.Delete(path);
        }
    }
}

/// <summary>The files of one coder launch that the argv names: the output file the answer is read from (empty: none).</summary>
internal sealed record CoderLaunchFiles(string OutputFile)
{
    /// <summary>A launch that names no file.</summary>
    public static CoderLaunchFiles None { get; } = new(string.Empty);
}

/// <summary>What <see cref="ICoderVendor.PrepareLaunch"/> came to: the request to launch, or the sentence that says why the launch must not start.</summary>
internal sealed record CoderLaunchPrepared(ProcessRequest Request, string Problem);

/// <summary>The flat output schema of the codex coder's report (results section 2, V19): every member required, the members the status does not use written as <c>null</c>.</summary>
internal static class CodexReportSchema
{
    /// <summary>The embedded schema text (<c>shared/coder-report.codex.schema.json</c>).</summary>
    public static string Json => Loaded.Value;

    private static readonly Lazy<string> Loaded = new(() =>
    {
        using var stream = typeof(CodexReportSchema).Assembly.GetManifestResourceStream("CoaiMcp.Runners.coder-report.codex.schema.json")
            ?? throw new InvalidOperationException("the codex report schema is not embedded");
        using var reader = new StreamReader(stream, Encoding.UTF8);

        return reader.ReadToEnd();
    });
}
