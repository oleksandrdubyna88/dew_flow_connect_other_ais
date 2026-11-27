using CoaiMcp.Runners.Consultation;
using CoaiMcp.Runners.Files;
using CoaiMcp.Core.Toolchain;
using CoaiMcp.Runners.Processes;

namespace CoaiMcp.Runners.Toolchain;

/// <summary>
/// The OS layer of the claude CODER process itself (epoch 3 E2 s2 t5, option (a) of <c>v2/spikes/RESULTS_coder_write_boundary_judgement.md</c> § 4): the CLI runs inside a NAMED <c>systemd-run --user</c> unit of the same family as the checks'
/// (<see cref="CheckBoundary"/>) — the main checkout, the coder root and the worktree's <c>.git</c> read-only, the worktree the one writable source folder, no bus, no WSL interop, a home and temp folders of the coder's own — and the launch is
/// REFUSED when a preflight cannot prove that this machine holds it.
/// </summary>
/// <remarks>
/// <para><b>Why the flags are not enough.</b> R11 confines the CLI's own file tools with <c>--restricted</c>; that is the vendor's promise, and a fake CLI does not keep it (the judgement's must-change 9). The OS layer stops the write whoever makes it:
/// a CLI that is wrong about its own flags, a tool added in a later version, a child it starts.</para>
/// <para><b>What it is not.</b> It is not <c>ProtectHome=tmpfs</c> with a private network like the checks: the CLI needs its vendor and its sign-in (<see cref="CoderUnit"/> says what it gets and why). It stops WRITES; a read of a file the unit can see
/// is not stopped here (a read-only bind does not stop reads, R12 d).</para>
/// <para><b>One road, as for the checks.</b> No user manager, native Windows, macOS, a program the unit cannot be given, a preflight row that says no — each is a refusal with a sentence, before the coder is launched. The unit is stopped by name
/// (<see cref="UnitControl"/>) on a timeout, a cancel or a dead client, and <see cref="BoundaryRun.Gone"/> says whether it is gone.</para>
/// </remarks>
public sealed class CoderBoundary(IProcessLauncher launcher, BoundaryHost host)
{
    private static readonly TimeSpan PreflightBudget = TimeSpan.FromSeconds(60);

    /// <summary>The reason the host network is the coder's, written into every record: the CLI talks to its vendor.</summary>
    internal const string NetworkReason = "the claude CLI talks to its vendor, so the coder has the host network; the sign-in file is the one file of the real home it gets, read-only";

    private readonly UnitControl _units = new(launcher, host);

    /// <summary>The seam of the vendor this boundary bounds (epoch 3 E3 s7 t1): its unit's properties, working directory, environment, home and program. claude by default, which is what the boundary was before the seam.</summary>
    internal ICoderVendor Vendor { get; init; } = CoderVendors.Claude;

    /// <summary>The program of a launch as the unit must be given it: the vendor's own resolution, refused when its folder would show the person's home (pass 6, M17).</summary>
    private CoderProgram ProgramOf(string executable, BoundaryJob job)
    {
        var program = Vendor.Program(executable, host.Path, job);

        return program.Problem.Length == 0 && program.HomeProblem(host.ResolvedHome) is { Length: > 0 } wide ? program with { Problem = wide } : program;
    }

    /// <summary>For a test only: rewrites the finished <c>systemd-run</c> argument list of every unit this boundary starts, so a RED can drop one property and see the preflight refuse. Production never sets it.</summary>
    internal Func<IReadOnlyList<string>, IReadOnlyList<string>>? Degrade { get; init; }

    /// <summary>This boundary over the same machine for another vendor's coder (epoch 3 E3 s7 t2): one job may fail over from one vendor to another, and each launch is bounded by its own vendor's unit.</summary>
    internal CoderBoundary For(ICoderVendor vendor) => new(launcher, host) { Vendor = vendor, Degrade = Degrade };

    /// <summary>
    /// The boundary of the job's CHECKS on this machine: the same launcher, the same host, and — for a test — the same degrade, so a coder job's turns and its checks are bounded by one machine's facts and cannot disagree about it.
    /// </summary>
    public CheckBoundary ForChecks() => new(launcher, host) { Degrade = Degrade };

    /// <summary>The properties a coder launch of this job runs with — the list the result records. Empty when the program cannot be given to a unit. <paramref name="withKey"/>: the launch carries a pool key, so no sign-in file is bound and the key folder is hidden.</summary>
    public IReadOnlyList<string> PropertiesFor(BoundaryJob job, string executable, TimeSpan runtimeMax, bool withKey = false)
    {
        var program = ProgramOf(executable, job);

        return program.Problem.Length > 0 ? [] : Vendor.UnitProperties(HostFor(withKey), job, program, runtimeMax);
    }

    /// <summary>
    /// The request with every explicit variable taken out of its environment and put before the prompt, one line each at the head of stdin: what the unit's own shell reads (<see cref="CoderUnit.Inner"/>). The environment of the <c>systemd-run</c> client is
    /// then empty of values: a process of the same user can read it.
    /// </summary>
    private static ProcessRequest WithSecretsOnStdin(ProcessRequest request, CoderUnit.Crossing crossing) =>
        request with
        {
            Environment = new Dictionary<string, string?>(),
            StdIn = crossing.StdinHead + request.StdIn,
        };

    /// <summary>
    /// The host as ONE launch sees it: a launch that carries a pool key (epoch 3 E2 s7) binds no sign-in file of the person's — the key is its credential — so its unit, its preflight and its record are made over a host with <see cref="BoundaryHost.BindSignIn"/> false.
    /// </summary>
    private BoundaryHost HostFor(bool withKey) => KeyedLaunch(withKey) ? host with { BindSignIn = false } : host;

    /// <summary>
    /// Whether the unit of this launch binds NONE of the person's claude sign-in (epoch 3 E3 s8 t1; SPEC E7, a unit holds only its own vendor's sign-in): a launch with a pool key, and EVERY launch of a vendor other than claude — the sign-in file the host names is claude's, and no unit of another vendor gets it whatever that vendor's own
    /// property list says. Such a unit hides the claude key pool, and its preflight proves both from inside (<see cref="BoundaryPreflight.SignInAbsent"/>, <see cref="BoundaryPreflight.KeysHidden"/>).
    /// </summary>
    private bool KeyedLaunch(bool withKey) => withKey || Vendor.Id != CoderBoundaryTable.Claude;

    /// <summary>Whether <paramref name="request"/> carries a pool key: the variable the claude CLI reads it from is in its explicit environment with a value. The one reading, for the admission and for the launch, so the two cannot disagree.</summary>
    public static bool CarriesKey(ProcessRequest request) =>
        request.Environment.TryGetValue(CoaiMcp.Runners.Consultation.ClaudeConsultant.KeyVariable, out var value) && !string.IsNullOrEmpty(value);

    /// <summary>
    /// The sign-in of the vendor, made for THIS launch when its recipe is a copy for the job (epoch 3 E3 s7 t6): a fresh copy for every turn when the recipe says so (agy), else one for the job; from the real file, or — when the recipe has a refresh step and the real file's access token has
    /// no margin — from the file a refresh unit rewrote in a scratch home, which is deleted before this returns; then the GATE of the recipe on the copy as it is (<c>expiry &gt;= now + the unit's end + the margin</c>), asked before EVERY launch. Empty when the launch may start, else the sentence — a
    /// launch that cannot have the margin is never started on less. A vendor whose sign-in is not a copy (claude's bind) has nothing to prepare.
    /// </summary>
    internal async Task<string> PrepareSignInAsync(BoundaryJob job, string executable, TimeSpan runtimeMax, SignInJobs states, DateTime nowUtc, CancellationToken ct)
    {
        if (Vendor.SignIn is not CoderSignInRecipe.CopyForJob recipe)
        {
            return string.Empty;
        }

        var real = Path.Combine(host.RealHome, recipe.RealRelativePath.Length > 0 ? recipe.RealRelativePath : recipe.RelativePath);
        var state = states.Of(Vendor.Id, () => new SignInJobState(Vendor.Id, recipe, Vendor.Home(job).SignInFile, real, Vendor.Home(job).Home));
        // The vendor's home is made by the preflight, which runs AFTER this; on the first launch it is not there yet, and the copy needs it. A home that IS there is looked at by the copy itself (a real folder at its own real path, M8).
        if (RealPath.Lstat(Vendor.Home(job).Home).Kind == EntryKind.Missing)
        {
            try
            {
                Directory.CreateDirectory(Vendor.Home(job).Home);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                return $"the coder's home for {Vendor.Id} could not be made ({e.GetType().Name}), so the launch was not started";
            }
        }

        // A refresh folder a killed runner left (it holds the FULL real file) goes at the next start, before anything else is asked (M10).
        SignInRefresh.Delete(job.RefreshFolder);
        state.Before ??= SignInCopy.Snapshot(real);
        if (!state.Before.Exists)
        {
            return $"the {Vendor.Id} sign-in file was not found where its CLI keeps it, so the coder has no sign-in to be given a copy of; start {Vendor.Id} once yourself to sign in, then start the coder again";
        }

        if (recipe.FreshForEveryTurn || !File.Exists(state.CopyFile))
        {
            var made = await MakeCopyAsync(job, executable, runtimeMax, recipe, state, nowUtc, ct);
            if (made.Length > 0)
            {
                return made;
            }
        }

        state.Remember();

        return SignInCopy.Gate(recipe, SignInCopy.ExpiryOfFile(recipe, state.CopyFile, noFollow: true), nowUtc, runtimeMax, Vendor.Id);
    }

    /// <summary>The vendor's own preparation of the launch (<see cref="ICoderVendor.PrepareLaunch"/>): files it needs in its home, its tripwires, and the request to launch.</summary>
    internal CoderLaunchPrepared PrepareLaunch(BoundaryJob job, ProcessRequest request) => Vendor.PrepareLaunch(host, job, request);

    /// <summary>What the vendor's unit wrote that the host must read, brought to where the launch named it (<see cref="ICoderVendor.CollectLaunch"/>).</summary>
    internal void CollectLaunch(BoundaryJob job, ProcessRequest original, ProcessRequest launched) => Vendor.CollectLaunch(job, original, launched);

    private async Task<string> MakeCopyAsync(BoundaryJob job, string executable, TimeSpan runtimeMax, CoderSignInRecipe.CopyForJob recipe, SignInJobState state, DateTime nowUtc, CancellationToken ct)
    {
        var source = state.RealFile;
        var scratch = string.Empty;
        try
        {
            var realExpiry = SignInCopy.ExpiryOfFile(recipe, state.RealFile);
            if (recipe.RefreshArgv.Count > 0 && recipe.ExpiryField.Length > 0 && (realExpiry is null || realExpiry.Value - nowUtc < runtimeMax + recipe.Margin))
            {
                // The access token of the real file has no margin: ONE refresh in a unit of its own, on a scratch home that holds the full copy. It runs only now, and only when the margin is missing; a real file that has it is cut into the copy as it is.
                var program = Vendor.Program(executable, host.Path, job);
                if (program.Problem.Length > 0)
                {
                    return $"the sign-in of {Vendor.Id} needs a refresh and its program cannot be run in a unit: {program.Problem}";
                }

                // The full copy lives INSIDE the job folder, so every unit of the job has it under the cover and the retention removes it with the job (the fourth security verify of s7 t6, M10). Where the job folder is its own cover, the folder that holds the jobs is not hidden from
                // the refresh unit and the full copy would lie under no cover of its own: no refresh is run there.
                if (job.IsItsOwnCover)
                {
                    return $"the sign-in of {Vendor.Id} needs a refresh, and on this machine the folder of the jobs cannot be hidden from the refresh unit, so none is run and the launch was not started; start {Vendor.Id} once yourself (its own CLI refreshes your own file), then start the coder again";
                }

                scratch = job.RefreshFolder;
                state.Refreshes++;
                var unit = BoundaryUnit.UnitName(job, $"refresh{state.Refreshes}");
                var refreshed = await new SignInRefresh(launcher, host).RunAsync(job, state.RealFile, scratch, recipe.RealRelativePath.Length > 0 ? recipe.RealRelativePath : recipe.RelativePath, program, recipe.RefreshArgv, [new("PATH", BoundaryHost.SystemPath), new("HOME", scratch), new("LANG", "C.UTF-8"), new("TERM", "dumb"), new("NO_COLOR", "1")], unit, Degrade, ct, recipe.ExpiryIsJwt ? string.Empty : recipe.ExpiryField);
                if (refreshed is SignInRefreshResult.Failed failed)
                {
                    return failed.Sentence;
                }

                source = ((SignInRefreshResult.Refreshed)refreshed).File;
            }

            return SignInCopy.Make(recipe, source, state.CopyFile, Vendor.Home(job).Home) is SignInCopyResult.Refused refused ? refused.Sentence : string.Empty;
        }
        finally
        {
            if (scratch.Length > 0)
            {
                SignInRefresh.Delete(scratch);
            }
        }
    }

    /// <summary>
    /// The preflight, before the launch: makes the coder's folders, then runs the exact property set once and asks the OS every item of <see cref="BoundaryPreflight.CoderItems"/>. A wrong answer anywhere refuses the launch with a sentence;
    /// nothing but the preflight has run, and the coder has not been started.
    /// </summary>
    /// <param name="job">The job: its worktree, its main checkout, its folders.</param>
    /// <param name="executable">The program the launch will run — an absolute path, or a name on coai's PATH.</param>
    /// <param name="ct">The caller's cancel.</param>
    public async Task<BoundaryAdmission> AdmitAsync(BoundaryJob job, string executable, CancellationToken ct = default, bool withKey = false, TimeSpan? runtimeMax = null)
    {
        var launchHost = HostFor(withKey);
        var early = host.PlatformRefusal() is { Length: > 0 } platform ? platform : KeyedLaunch(withKey) && job.KeysFolderProblem() is { Length: > 0 } hidden ? hidden : job.CoderProblem() is { Length: > 0 } problem ? problem : job.MountsOnDiskProblem() is { Length: > 0 } mounts ? mounts : job.CoderTempOnDiskProblem(Vendor.Id);
        if (early.Length > 0)
        {
            return Refuse(early, []);
        }

        var program = ProgramOf(executable, job);
        if (program.Problem.Length > 0)
        {
            return Refuse($"The coder was not launched: {program.Problem}.", []);
        }

        var prepared = BoundaryFolders.Prepare(host, job, Vendor.Home(job).Folders, job.CoderTempOf(Vendor.Id), job.CoderVarTempOf(Vendor.Id));
        if (prepared.Sentence.Length > 0)
        {
            return Refuse(prepared.Sentence, []);
        }

        if (job.JobRootOnDiskProblem() is { Length: > 0 } linked)
        {
            BoundaryFolders.Tidy(job, prepared.Copy, job.CoderTempOf(Vendor.Id), job.CoderVarTempOf(Vendor.Id));

            return Refuse(linked, []);
        }

        try
        {
            return await AskAsync(launchHost, job, program, prepared.Copy, runtimeMax ?? PreflightBudget + UnitControl.Backstop, ct);
        }
        finally
        {
            BoundaryFolders.Tidy(job, prepared.Copy, job.CoderTempOf(Vendor.Id), job.CoderVarTempOf(Vendor.Id));
        }
    }

    private async Task<BoundaryAdmission> AskAsync(BoundaryHost launchHost, BoundaryJob job, CoderProgram program, string windowsCopy, TimeSpan runtimeMax, CancellationToken ct)
    {
        var rows = await PreflightAsync(launchHost, job, program, windowsCopy, runtimeMax, ct);
        var record = new BoundaryRecord(BoundaryRecord.UnitBoundary, Vendor.UnitProperties(launchHost, job, program, PreflightBudget + UnitControl.Backstop), "host", NetworkReason, rows);
        var bad = rows.FirstOrDefault(row => !row.Held);

        return bad is not null
            ? Refuse($"The coder boundary did not hold on this machine, so the job was refused before the coder was launched: {bad.Item} — {bad.Detail}.", rows)
            : new BoundaryAdmission.Admitted(job.JobId, new Dictionary<CheckNetwork, BoundaryRecord> { [CheckNetwork.Host] = record });
    }

    private static BoundaryAdmission.Refused Refuse(string sentence, IReadOnlyList<PreflightRow> rows) =>
        new(sentence, new BoundaryRecord(BoundaryRecord.UnitBoundary, [], "host", NetworkReason, rows));

    private async Task<IReadOnlyList<PreflightRow>> PreflightAsync(BoundaryHost launchHost, BoundaryJob job, CoderProgram program, string windowsCopy, TimeSpan runtimeMax, CancellationToken ct)
    {
        var unit = CoderUnit.UnitName(job, "pre");
        var signIn = launchHost.SignInFile.Length > 0 ? Vendor.Home(job).SignInFile : string.Empty;
        // A vendor with a preflight of its own (agy: its own rows, in place of the claude coder's) is asked inside the same unit: the same properties and the same working folder.
        var plan = Vendor.PreflightPlan(launchHost, job, program, windowsCopy, runtimeMax);
        try
        {
            IReadOnlyList<string> inner =
                ["/usr/bin/env", "-i", .. Vendor.Environment(host, job).Select(pair => $"{pair.Key}={pair.Value}"), .. plan?.Command ?? BoundaryPreflight.CoderCommand(job, launchHost, program, windowsCopy, signIn)];
            var budget = plan?.Budget ?? PreflightBudget;
            var arguments = BoundaryUnit.Wrap(unit, Vendor.WorkingDirectory(job), [], Vendor.UnitProperties(launchHost, job, program, budget + UnitControl.Backstop), inner, Degrade);
            var started = await _units.StartAsync(new ProcessRequest(host.SystemdRun, arguments, job.Tree.Root)
            {
                Timeout = budget,
                InheritsEnvironment = false,
                Passthrough = UnitControl.Client,
            }, ct);

            var gone = started.Problem.Length == 0 && await _units.EnsureGoneAsync(unit);

            return plan?.Rows is { } decide ? decide(started, gone) : BoundaryPreflight.Rows(started, gone, plan?.Items ?? BoundaryPreflight.CoderItemsFor(job));
        }
        finally
        {
            plan?.Tidy();
        }
    }

    /// <summary>
    /// Runs one launch of the coder inside its unit: the request's program and argv, the request's stdin (the prompt) and deadline, the environment set by <c>env -i</c> inside, and the unit asked after and stopped if it is still there.
    /// </summary>
    /// <param name="admitted">The preflight's answer for THIS job. A launch cannot start without it: an admission of another job is refused with an exception — a programming error, never a data one.</param>
    /// <param name="job">The job.</param>
    /// <param name="request">The launch as the consultant adapter built it. Only its program, argv, stdin, deadline and output ceiling are used; its working folder must be the job's worktree (the unit starts there), and its environment is replaced.</param>
    /// <param name="part">The unit's part name — <c>c1</c>, <c>c2</c> — unique within the job.</param>
    /// <param name="ct">The caller's cancel.</param>
    /// <exception cref="InvalidOperationException">The admission is not for this job, or the program cannot be given to the unit.</exception>
    /// <exception cref="ArgumentException">The part name is not one a unit can be named with.</exception>
    public async Task<BoundaryRun> RunAsync(BoundaryAdmission.Admitted admitted, BoundaryJob job, ProcessRequest request, string part, CancellationToken ct = default)
    {
        if (admitted.JobId != job.JobId || !admitted.Records.TryGetValue(CheckNetwork.Host, out var seen))
        {
            throw new InvalidOperationException($"the preflight of job '{job.JobId}' did not admit a coder launch; the coder may not start before it has");
        }

        ArgumentException.ThrowIfNullOrEmpty(part);
        if (!CoderUnit.IsPart(part))
        {
            throw new ArgumentException("the unit's part name is 1 to 16 characters of a-z, 0-9 and '-', starting with a letter or a digit", nameof(part));
        }

        var program = ProgramOf(request.Executable, job);
        if (program.Problem.Length > 0)
        {
            throw new InvalidOperationException($"the coder's program cannot be run in the unit: {program.Problem}");
        }

        // A secret is NOT a property of the unit. A `--setenv=NAME` is copied into the unit's Environment property and into a transient unit file of mode 0644 under /run/user for as long as the unit lives (measured, second judgement; the third judgement found a row's own
        // ANTHROPIC_API_KEY still going that way): so every explicit variable travels as a line at the head of the unit's stdin — the pool key first — which the unit's own shell reads and exports before the exec. Nothing but the pipe, and the child's own environment,
        // ever holds it, and the client is started with none.
        var keyed = CarriesKey(request);
        var crossing = CoderUnit.Cross(request, System.Environment.GetEnvironmentVariable);
        var sent = WithSecretsOnStdin(request, crossing);
        var unit = CoderUnit.UnitName(job, part);
        var launchHost = HostFor(keyed);
        var properties = Vendor.UnitProperties(launchHost, job, program, request.Timeout + UnitControl.Backstop);
        var record = seen with { Properties = properties };
        try
        {
            var arguments = CoderUnit.Wrapped(unit, Vendor.WorkingDirectory(job), properties, Vendor.Environment(launchHost, job), program, request.Arguments, crossing, Degrade);
            var started = await _units.StartAsync(sent with
            {
                Executable = host.SystemdRun,
                Arguments = arguments,
                WorkingDirectory = job.Tree.Root,
                InheritsEnvironment = false,
                Passthrough = CoderUnit.ClientNames(crossing.ByName),
            }, ct);
            var result = started.Problem.Length > 0 ? started.Result with { StdErr = started.Problem } : started.Result;

            return new BoundaryRun(result, unit, await _units.EnsureGoneAsync(unit), record);
        }
        catch
        {
            await _units.EnsureGoneAsync(unit);
            throw;
        }
    }
}
