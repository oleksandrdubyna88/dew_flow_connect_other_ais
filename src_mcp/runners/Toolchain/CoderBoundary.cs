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

    /// <summary>For a test only: rewrites the finished <c>systemd-run</c> argument list of every unit this boundary starts, so a RED can drop one property and see the preflight refuse. Production never sets it.</summary>
    internal Func<IReadOnlyList<string>, IReadOnlyList<string>>? Degrade { get; init; }

    /// <summary>The properties a coder launch of this job runs with — the list the result records. Empty when the program cannot be given to a unit.</summary>
    public IReadOnlyList<string> PropertiesFor(BoundaryJob job, string executable, TimeSpan runtimeMax)
    {
        var program = CoderProgram.Resolve(executable, host.Path, job);

        return program.Problem.Length > 0 ? [] : CoderUnit.Properties(host, job, program, runtimeMax);
    }

    /// <summary>
    /// The preflight, before the launch: makes the coder's folders, then runs the exact property set once and asks the OS every item of <see cref="BoundaryPreflight.CoderItems"/>. A wrong answer anywhere refuses the launch with a sentence;
    /// nothing but the preflight has run, and the coder has not been started.
    /// </summary>
    /// <param name="job">The job: its worktree, its main checkout, its folders.</param>
    /// <param name="executable">The program the launch will run — an absolute path, or a name on coai's PATH.</param>
    /// <param name="ct">The caller's cancel.</param>
    public async Task<BoundaryAdmission> AdmitAsync(BoundaryJob job, string executable, CancellationToken ct = default)
    {
        var early = host.PlatformRefusal() is { Length: > 0 } platform ? platform : job.CoderProblem();
        if (early.Length > 0)
        {
            return Refuse(early, []);
        }

        var program = CoderProgram.Resolve(executable, host.Path, job);
        if (program.Problem.Length > 0)
        {
            return Refuse($"The coder was not launched: {program.Problem}.", []);
        }

        var prepared = BoundaryFolders.Prepare(host, job, job.CoderFolders, job.CoderTempFolder, job.CoderVarTempFolder);
        if (prepared.Sentence.Length > 0)
        {
            return Refuse(prepared.Sentence, []);
        }

        try
        {
            return await AskAsync(job, program, prepared.Copy, ct);
        }
        finally
        {
            BoundaryFolders.Tidy(job, prepared.Copy, job.CoderTempFolder, job.CoderVarTempFolder);
        }
    }

    private async Task<BoundaryAdmission> AskAsync(BoundaryJob job, CoderProgram program, string windowsCopy, CancellationToken ct)
    {
        var rows = await PreflightAsync(job, program, windowsCopy, ct);
        var record = new BoundaryRecord(BoundaryRecord.UnitBoundary, CoderUnit.Properties(host, job, program, PreflightBudget + UnitControl.Backstop), "host", NetworkReason, rows);
        var bad = rows.FirstOrDefault(row => !row.Held);

        return bad is not null
            ? Refuse($"The coder boundary did not hold on this machine, so the job was refused before the coder was launched: {bad.Item} — {bad.Detail}.", rows)
            : new BoundaryAdmission.Admitted(job.JobId, new Dictionary<CheckNetwork, BoundaryRecord> { [CheckNetwork.Host] = record });
    }

    private static BoundaryAdmission.Refused Refuse(string sentence, IReadOnlyList<PreflightRow> rows) =>
        new(sentence, new BoundaryRecord(BoundaryRecord.UnitBoundary, [], "host", NetworkReason, rows));

    private async Task<IReadOnlyList<PreflightRow>> PreflightAsync(BoundaryJob job, CoderProgram program, string windowsCopy, CancellationToken ct)
    {
        var unit = CoderUnit.UnitName(job, "pre");
        var signIn = host.SignInFile.Length > 0 ? job.CoderSignInFile : string.Empty;
        IReadOnlyList<string> inner =
            ["/usr/bin/env", "-i", .. CoderUnit.Environment(host, job).Select(pair => $"{pair.Key}={pair.Value}"), .. BoundaryPreflight.CoderCommand(job, host, program, windowsCopy, signIn)];
        var arguments = BoundaryUnit.Wrap(unit, job.Tree.Root, [], CoderUnit.Properties(host, job, program, PreflightBudget + UnitControl.Backstop), inner, Degrade);
        var started = await _units.StartAsync(new ProcessRequest(host.SystemdRun, arguments, job.Tree.Root)
        {
            Timeout = PreflightBudget,
            InheritsEnvironment = false,
            Passthrough = UnitControl.Client,
        }, ct);

        return BoundaryPreflight.Rows(started, started.Problem.Length == 0 && await _units.EnsureGoneAsync(unit), BoundaryPreflight.CoderItems);
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

        var program = CoderProgram.Resolve(request.Executable, host.Path, job);
        if (program.Problem.Length > 0)
        {
            throw new InvalidOperationException($"the coder's program cannot be run in the unit: {program.Problem}");
        }

        var names = CoderUnit.ByName(request, System.Environment.GetEnvironmentVariable);
        var unit = CoderUnit.UnitName(job, part);
        var record = seen with { Properties = CoderUnit.Properties(host, job, program, request.Timeout + UnitControl.Backstop) };
        try
        {
            var arguments = CoderUnit.Arguments(host, job, program, unit, request.Timeout + UnitControl.Backstop, request.Arguments, names, Degrade);
            var started = await _units.StartAsync(request with
            {
                Executable = host.SystemdRun,
                Arguments = arguments,
                WorkingDirectory = job.Tree.Root,
                InheritsEnvironment = false,
                Passthrough = CoderUnit.ClientNames(names),
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
