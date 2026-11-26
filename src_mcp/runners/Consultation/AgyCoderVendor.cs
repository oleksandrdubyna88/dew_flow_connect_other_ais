using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Reviewers;
using CoaiMcp.Runners.Toolchain;

namespace CoaiMcp.Runners.Consultation;

/// <summary>
/// agy as a coder vendor (epoch 3 E3 s6; judgement addendum 3; SPEC C6): the measured flag set AG1 on every argv, the guard folder as the working directory, the unit that HIDES the main checkout and the coder root, the sign-in as a copy made for the job, and the checks of the turn's own stream.
/// It is launchable only when the table says so (<see cref="CoderBoundaryTable"/>): the verdict is <c>ship</c> and the row carries the mark <c>launchable</c>, which is OFF until s8 t6 turns it on after the must-changes are built and green. Nothing here compares the installed release with anything.
/// </summary>
internal sealed class AgyCoderVendor(HookHandler? handler = null) : ICoderVendor
{
    /// <summary>The vendor with this process as its hook handler: what the product launches.</summary>
    public static AgyCoderVendor Instance { get; } = new();

    /// <summary>The id of the vendor: a row of the table, the first word of a model of an agy route.</summary>
    public const string VendorId = "agy";

    /// <summary>The program that answers the hook of the guard.</summary>
    public HookHandler Handler => handler ?? HookHandler.OfThisProcess();

    /// <summary>For a test only: runs after the guard folder was written and read back, before the preflight — a RED that must see a row of the preflight refuse.</summary>
    internal Action<BoundaryJob>? AfterPrepare { get; init; }

    public string Id => VendorId;

    public string PromptId => "coder-agy";

    public string PromptResource => "CoaiMcp.coder-agy-prompt.md";

    /// <summary>The sign-in is a COPY made for the job: the access token and its expiry only, no refresh token at all (AG-R2); a fresh copy for every turn, refreshed by <c>agy models</c> in a unit of its own when the real file has no margin (A11, A12).</summary>
    public CoderSignInRecipe SignIn { get; } = new CoderSignInRecipe.CopyForJob(
        AgyCoderUnit.SignInRelative,
        ["token/access_token", "token/token_type", "token/expiry", "token/expires_in", "auth_method"],
        new Dictionary<string, string>(),
        "models",
        "AG-R2")
    {
        RealRelativePath = AgyCoderUnit.SignInRelative,
        ExpiryField = "token/expiry",
        ExpiryIsJwt = false,
        Margin = TimeSpan.FromMinutes(5),
        TokenFields = ["token/access_token"],
        RefreshArgv = ["models"],
        FreshForEveryTurn = true,
    };

    public IConsultantRuntime Runtime => new AntigravityConsultant(new AntigravityRuntime("agy"), VendorId);

    /// <summary>A token of agy lives about an hour (3599 s measured) and a launch needs <c>expiry &gt;= now + the unit's end + 5 minutes</c>, the unit's end being the deadline and a minute of backstop: 45 minutes leaves the margin and the minute with room to spare (judgement 14.4, G10).</summary>
    public TimeSpan? TurnDeadline => TimeSpan.FromMinutes(45);

    public string NotProvedByPreflight => AgyCoderPreflight.NotProved;

    public string FixClause => "you have no command tool, and you do not run them";

    public bool PointsAtInstructionBlock => true;

    /// <summary>Without the job's records no turn can be tied to the guard: the check fails closed.</summary>
    public CoderTurnVerdict CheckTurn(ProcessResult process, string expectedSession) => AgyTurnCheck.Check(process, expectedSession, records: null);

    public CoderTurnVerdict CheckTurn(ProcessResult process, string expectedSession, BoundaryJob job) =>
        AgyTurnCheck.Check(process, expectedSession, AgyGuardRecords.Read(AgyCoderUnit.RecordsFile(job)));

    public async Task<CoderCapability> ProbeAsync(IProcessLauncher launcher, string executable, string workingDirectory, CancellationToken ct) =>
        await AgyWriteCapability.ProbeAsync(launcher, executable, workingDirectory, ct) switch
        {
            AgyWriteCapability.Ready ready => new CoderCapability.Ready(ready.Version),
            AgyWriteCapability.Refused refused => new CoderCapability.Refused(refused.Said, refused.Cure, refused.CouldNotStart),
            _ => throw new InvalidOperationException("a write capability is ready or refused"),
        };

    public IReadOnlyList<string> FreshArgv(string model, string worktree) => [.. Core(model, worktree)];

    public IReadOnlyList<string> ResumedArgv(string model, string worktree, string sessionId) => [.. Core(model, worktree), "--conversation", sessionId];

    public IReadOnlyList<string> UnitProperties(BoundaryHost host, BoundaryJob job, CoderProgram program, TimeSpan runtimeMax) =>
        AgyCoderUnit.Properties(host, job, program, Handler, runtimeMax);

    public string WorkingDirectory(BoundaryJob job) => AgyCoderUnit.GuardFolder(job);

    public IReadOnlyList<KeyValuePair<string, string>> Environment(BoundaryHost host, BoundaryJob job) => AgyCoderUnit.Environment(job);

    public CoderHomeLayout Home(BoundaryJob job) => new(AgyCoderUnit.HomeFolder(job), AgyCoderUnit.SignInFile(job), AgyCoderUnit.Folders(job));

    public CoderProgram Program(string executable, string path, BoundaryJob job) => CoderProgram.Resolve(executable, path, job);

    /// <summary>The guard folder, written and read back before EVERY launch (G5, AG-G3); a refusal ends the launch. The request is launched as it is.</summary>
    public CoderLaunchPrepared PrepareLaunch(BoundaryHost host, BoundaryJob job, ProcessRequest request)
    {
        var prepared = AntigravityCoderGuard.Prepare(AgyCoderUnit.GuardFolder(job), Handler, job.Tree.Root, AgyCoderUnit.RecordsFile(job));
        if (prepared is AntigravityReadOnly.Prepared.Failed failed)
        {
            return new CoderLaunchPrepared(request, failed.Reason);
        }

        AfterPrepare?.Invoke(job);

        return new CoderLaunchPrepared(request, string.Empty);
    }

    /// <summary>The preflight of the agy coder: its own rows, in place of the claude coder's (U1 to U9, AG-B1, AG-R1, AG-R2, AG-B3, AG-G1 to AG-G3), asked inside the exact launch unit (<see cref="AgyCoderPreflight"/>).</summary>
    public CoderPreflightPlan? PreflightPlan(BoundaryHost host, BoundaryJob job, CoderProgram program, string windowsCopy, TimeSpan runtimeMax)
    {
        var other = AgyCoderPreflight.PlantOtherJob(job);

        return new CoderPreflightPlan(AgyCoderPreflight.Command(host, job, program, windowsCopy, other, Handler, runtimeMax, DateTime.UtcNow), AgyCoderPreflight.Items, () => AgyCoderPreflight.Tidy(job, other));
    }

    /// <summary>Right before the unit starts: the records of the earlier launches, and what the preflight's own probes of the guard wrote, are put away, so the records read after the turn are the turn's own (G15).</summary>
    public string BeforeStart(BoundaryJob job) => AgyGuardRecords.PutAway(AgyCoderUnit.RecordsFile(job));

    private static string[] Core(string model, string worktree) =>
    [
        "--print=",
        "--input-format", "stream-json",
        "--output-format", "stream-json",
        "--mode", AgyWriteCapability.AcceptEdits,
        "--agent", AntigravityCoderGuard.AgentName,
        .. model.Length > 0 ? (string[])["--model", model] : [],
        "--add-dir", worktree,
    ];
}

/// <summary>The guard's records file of a job: read for the turn check, put away before each launch.</summary>
internal static class AgyGuardRecords
{
    /// <summary>The text of the records, or empty when there is no file (no tool was asked), or null when it could not be read.</summary>
    public static string? Read(string file)
    {
        try
        {
            return Files.RealPath.Lstat(file).Kind switch
            {
                Files.EntryKind.Missing => string.Empty,
                Files.EntryKind.File => File.ReadAllText(file),
                _ => null,
            };
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Moves the records file to a name beside it (the earlier records stay for a person to read, under the job's folder); a sentence when that cannot be done.</summary>
    public static string PutAway(string file)
    {
        try
        {
            var kind = Files.RealPath.Lstat(file).Kind;
            if (kind == Files.EntryKind.Missing)
            {
                return string.Empty;
            }

            if (kind != Files.EntryKind.File)
            {
                File.Delete(file);

                return string.Empty;
            }

            File.Move(file, $"{file}.{DateTime.UtcNow.Ticks}.prev");

            return string.Empty;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return $"the records of the agy guard could not be put away before the launch ({e.GetType().Name}), so the launch was not started";
        }
    }
}
