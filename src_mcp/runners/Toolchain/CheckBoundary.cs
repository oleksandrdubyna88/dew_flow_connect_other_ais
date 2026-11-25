using CoaiMcp.Core.Toolchain;
using CoaiMcp.Runners.Processes;

namespace CoaiMcp.Runners.Toolchain;

/// <summary>What <see cref="CheckBoundary.AdmitAsync"/> answers: a job that may run, with what each network state was seen to do, or the sentence that refuses it. Nothing was run in either case but the preflight.</summary>
public abstract record BoundaryAdmission
{
    /// <summary>Every property held. <see cref="Records"/> has one record for each network state the job's steps use, with the preflight rows.</summary>
    public sealed record Admitted(string JobId, IReadOnlyDictionary<CheckNetwork, BoundaryRecord> Records) : BoundaryAdmission;

    /// <summary>The boundary is not effective here, or could not be made. <see cref="Record"/> carries the rows that said no.</summary>
    public sealed record Refused(string Sentence, BoundaryRecord Record) : BoundaryAdmission;

    private BoundaryAdmission() { }
}

/// <summary>What one step did inside its unit: the launcher's result, whether the unit is gone, and what it ran inside.</summary>
/// <param name="Result">What the <c>systemd-run --wait --pipe</c> client gave back: the unit's exit code and its output, or the timeout / cancel flags.</param>
/// <param name="Unit">The unit's name.</param>
/// <param name="Record">The boundary, the properties and the network state of the step, with the preflight rows that were answered before the job.</param>
/// <param name="Gone">True when no process of the unit is left: it was not active at the end, or it was stopped and is not active now. False is a fact the caller must not hide.</param>
public sealed record BoundaryRun(ProcessResult Result, string Unit, bool Gone, BoundaryRecord Record);

/// <summary>
/// The OS boundary of a coder job's check commands (epoch 3 E2 s2 t4): every step runs inside a NAMED <c>systemd-run --user</c> unit with the property set of <c>v2/spikes/RESULTS_coder_write_boundary_judgement.md</c> § 2 — the main
/// checkout and the worktree's <c>.git</c> read-only, no bus, no WSL interop, a HOME and temp folders of the job's own, the caches read-only — and the job is REFUSED when a preflight cannot prove that this machine holds it.
/// </summary>
/// <remarks>
/// <para><b>One road.</b> There is no mode without the boundary: no user manager, native Windows, macOS, a path the unit cannot be given, a preflight row that says no — each is a refusal with a sentence, before any check ran
/// (decision 8; the plan gate's amendment). A later <c>cwd-only</c> mode needs an owner decision and must never say "bounded".</para>
/// <para><b>The launcher is the one launcher.</b> <c>systemd-run --wait --pipe</c> is a process like another: it starts through <see cref="IProcessLauncher"/>, with a deadline and the tree kill. Killing that client does NOT stop the unit — the tree belongs
/// to the user manager — so every step ends with the unit asked after, and stopped (<c>systemctl --user stop</c>) when it is still there: on a timeout, on a cancel, and on a client that died. <see cref="BoundaryRun.Gone"/> says whether it is gone.
/// <c>RuntimeMaxSec</c> is the backstop for a coai that dies first, set a minute past the step's own deadline.</para>
/// <para><b>Exit code and output pass through</b> (<c>--wait</c> gives the unit's exit code; <c>--pipe</c> its streams). A unit that cannot START is also exit 1 from the client, which a command can also exit with; the preflight, run with the same properties, is what
/// makes that case rare, and the client's own words (<c>Failed to …</c>) are in the step's log.</para>
/// </remarks>
public sealed class CheckBoundary(IProcessLauncher launcher, BoundaryHost host)
{
    private readonly UnitControl _units = new(launcher, host);

    private static readonly TimeSpan PreflightBudget = TimeSpan.FromSeconds(60);

    /// <summary>For a test only: rewrites the finished <c>systemd-run</c> argument list of every unit this boundary starts, so a RED can drop one property and see the preflight refuse. Production never sets it.</summary>
    internal Func<IReadOnlyList<string>, IReadOnlyList<string>>? Degrade { get; init; }

    /// <summary>The properties a step of this job runs with in this network state — the list the result records.</summary>
    public IReadOnlyList<string> PropertiesFor(BoundaryJob job, CheckNetwork network, TimeSpan runtimeMax) => BoundaryUnit.Properties(host, job, network, runtimeMax);

    /// <summary>
    /// The preflight, before the job: makes the job's folders, then runs the exact property set once for each network state the steps use and asks the OS every item of <see cref="BoundaryPreflight.Items"/>.
    /// A wrong answer anywhere refuses the job with a sentence; nothing but the preflight has run.
    /// </summary>
    public async Task<BoundaryAdmission> AdmitAsync(BoundaryJob job, IReadOnlyCollection<CheckNetwork> networks, CancellationToken ct = default)
    {
        var early = EarlyRefusal(job);
        if (early.Length > 0)
        {
            return Refuse(early, []);
        }

        var program = PrepareFolders(job);
        if (program.Sentence.Length > 0)
        {
            return Refuse(program.Sentence, []);
        }

        try
        {
            return await AskEveryNetworkAsync(job, networks, program.Copy, ct);
        }
        finally
        {
            BoundaryFolders.Tidy(job, program.Copy, job.TempFolder, job.VarTempFolder);
        }
    }

    private string EarlyRefusal(BoundaryJob job) => host.PlatformRefusal() is { Length: > 0 } platform ? platform : job.Problem() is { Length: > 0 } problem ? problem : job.MountsOnDiskProblem();

    private async Task<BoundaryAdmission> AskEveryNetworkAsync(BoundaryJob job, IReadOnlyCollection<CheckNetwork> networks, string program, CancellationToken ct)
    {
        var records = new Dictionary<CheckNetwork, BoundaryRecord>();
        foreach (var network in networks.Distinct())
        {
            var rows = await PreflightAsync(job, network, program, ct);
            var record = new BoundaryRecord(BoundaryRecord.UnitBoundary, PropertiesFor(job, network, PreflightBudget + UnitControl.Backstop), network == CheckNetwork.Private ? "none" : "host", string.Empty, rows);
            var bad = rows.FirstOrDefault(row => !row.Held);
            if (bad is not null)
            {
                return Refuse($"The check boundary did not hold on this machine, so the job was refused before any check ran: {bad.Item} — {bad.Detail}.", rows);
            }

            records[network] = record;
        }

        return new BoundaryAdmission.Admitted(job.JobId, records);
    }

    private static BoundaryAdmission.Refused Refuse(string sentence, IReadOnlyList<PreflightRow> rows) =>
        new(sentence, new BoundaryRecord(BoundaryRecord.UnitBoundary, [], "none", string.Empty, rows));

    private (string Sentence, string Copy) PrepareFolders(BoundaryJob job) => BoundaryFolders.Prepare(host, job, [.. job.Folders, job.CoderFolder], job.TempFolder, job.VarTempFolder);

    private async Task<IReadOnlyList<PreflightRow>> PreflightAsync(BoundaryJob job, CheckNetwork network, string program, CancellationToken ct)
    {
        var unit = BoundaryUnit.UnitName(job, network == CheckNetwork.Private ? "pre-none" : "pre-host");
        var command = BoundaryPreflight.Command(job, host, network, program);
        var started = await StartAsync(job, unit, network, PreflightBudget, new Dictionary<string, string?>(), command, ct);

        return BoundaryPreflight.Rows(started, started.Problem.Length == 0 && await _units.EnsureGoneAsync(unit), BoundaryPreflight.ItemsFor(job));
    }

    /// <summary>Runs one step inside its unit: the program with the argv, <c>env -i</c> and the allow-list set inside, the deadline and the cancel honoured, and the unit asked after and stopped if it is still there.</summary>
    /// <param name="admitted">The preflight's answer for THIS job. A step cannot be started without it: an admission of another job, or one that did not cover the step's network state, is refused with an exception — a programming error, never a data one.</param>
    /// <param name="job">The job.</param>
    /// <param name="network">The step's network state and the reason for it, recorded in the result.</param>
    /// <param name="part">The unit's part name — <c>s1</c>, <c>s2</c> — unique within the job.</param>
    /// <param name="command">The program and its argv. The program is an absolute path or a name found on the unit's PATH.</param>
    /// <param name="extra">The step's own environment switches, added to the allow-list.</param>
    /// <param name="timeout">The step's deadline. The unit's <c>RuntimeMaxSec</c> is a minute later.</param>
    /// <param name="maxOutputChars">The ceiling of each stream the launcher keeps.</param>
    /// <param name="ct">The caller's cancel.</param>
    /// <exception cref="InvalidOperationException">The admission is not for this job and network state.</exception>
    public async Task<BoundaryRun> RunAsync(BoundaryAdmission.Admitted admitted, BoundaryJob job, NetworkChoice network, string part, IReadOnlyList<string> command,
        IReadOnlyDictionary<string, string?> extra, TimeSpan timeout, int maxOutputChars, CancellationToken ct = default)
    {
        if (admitted.JobId != job.JobId || !admitted.Records.TryGetValue(network.Mode, out var seen))
        {
            throw new InvalidOperationException($"the preflight of job '{job.JobId}' did not admit a {network.Wire} network step; no step may start before it has");
        }

        var unit = BoundaryUnit.UnitName(job, part);
        var record = seen with { Network = network.Wire, NetworkReason = network.Reason, Properties = PropertiesFor(job, network.Mode, timeout + UnitControl.Backstop) };
        try
        {
            var started = await StartAsync(job, unit, network.Mode, timeout, extra, command, ct, maxOutputChars);

            var result = started.Problem.Length > 0 ? started.Result with { StdErr = started.Problem } : started.Result;

            return new BoundaryRun(result, unit, await _units.EnsureGoneAsync(unit), record);
        }
        catch
        {
            await _units.EnsureGoneAsync(unit);
            throw;
        }
    }

    private async Task<UnitStart> StartAsync(BoundaryJob job, string unit, CheckNetwork network, TimeSpan timeout, IReadOnlyDictionary<string, string?> extra,
        IReadOnlyList<string> command, CancellationToken ct, int maxOutputChars = 1024 * 1024)
    {
        var arguments = BoundaryUnit.Arguments(host, job, unit, network, timeout + UnitControl.Backstop, extra, command, Degrade);

        return await _units.StartAsync(new ProcessRequest(host.SystemdRun, arguments, job.Tree.Root)
        {
            Timeout = timeout,
            InheritsEnvironment = false,
            Passthrough = UnitControl.Client,
            MaxOutputChars = maxOutputChars,
        }, ct);
    }
}
