using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CoaiMcp.Core.Notices;
using CoaiMcp.Core.Toolchain;
using CoaiMcp.Runners.Collecting;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Toolchain;
using CoaiMcp.Runners.Worktrees;

namespace CoaiMcp.Server.RoleJobs;

/// <summary>
/// What the instruction step of a coder job came to, in the words of the envelope (<c>instructions {declared, argv, exitCode, bytes, sha256, ms}</c>, SPEC C9 item 8): never a line of the output. A job whose base declares no step has
/// <see cref="NotDeclared"/>, so an orchestrator sees that a coder worked without the repository's own command.
/// </summary>
/// <param name="StderrTail">The last lines of standard error, only for a step that failed — the envelope of a failed job names them (C9 item 5). Empty otherwise.</param>
internal sealed record CoderInstructionsRecord(bool Declared, IReadOnlyList<string> Argv, int ExitCode, long Bytes, string Sha256, long Ms, IReadOnlyList<string> StderrTail)
{
    /// <summary>The record of a job whose base commit declares no step.</summary>
    public static CoderInstructionsRecord NotDeclared { get; } = new(false, [], 0, 0, string.Empty, 0, []);

    /// <summary>The <c>instructions</c> object of the envelope. The output is NOT in it: its size and its hash are.</summary>
    public void Write(Utf8JsonWriter json)
    {
        json.WriteStartObject("instructions");
        json.WriteBoolean("declared", Declared);
        json.WriteStartArray("argv");
        foreach (var item in Argv)
        {
            json.WriteStringValue(item);
        }

        json.WriteEndArray();
        json.WriteNumber("exitCode", ExitCode);
        json.WriteNumber("bytes", Bytes);
        json.WriteString("sha256", Sha256);
        json.WriteNumber("ms", Ms);
        if (StderrTail.Count > 0)
        {
            json.WriteStartArray("stderrTail");
            foreach (var line in StderrTail)
            {
                json.WriteStringValue(line);
            }

            json.WriteEndArray();
        }

        json.WriteEndObject();
    }
}

/// <summary>What the instruction step made of the job, before any model is launched.</summary>
internal abstract record CoderInstructionsOutcome
{
    private CoderInstructionsOutcome()
    {
    }

    /// <summary>The base commit declares no step: nothing was run.</summary>
    public sealed record None : CoderInstructionsOutcome;

    /// <summary>The step ran and its output is kept: <paramref name="Text"/> goes into the turn-0 prompt (s7 t2 d6), <paramref name="Record"/> into the envelope.</summary>
    public sealed record Ran(CoderInstructionsRecord Record, string Text, string BaseSha) : CoderInstructionsOutcome;

    /// <summary>The step is declared and could not run to a clean end — or the file that declares it cannot be read: the job ends with <paramref name="Stop"/> and no model is launched.</summary>
    public sealed record Failed(string Stop, string Reason, CoderInstructionsRecord Record) : CoderInstructionsOutcome;

    /// <summary>The job was stopped while the step ran.</summary>
    public sealed record Cancelled : CoderInstructionsOutcome;
}

/// <summary>
/// The instruction step of a coder job (epoch 3 E3 s7 t3; SPEC C9; judgement section 12.6): a command the repository declares in <c>.coai/toolchain.yaml</c> is run by coai, ONCE for the job and before any model is launched, in a CHECK unit of E2 at the base commit,
/// and its standard output is kept for the prompt.
/// </summary>
/// <remarks>
/// <para><b>Why coai runs it.</b> An instruction file of a repository can order a command before any edit (a rule resolver, a security check). A coder of coai runs no command of its own, so either coai runs it or nobody does; coai never finds the command by reading prose.</para>
/// <para><b>The unit.</b> <see cref="CheckBoundary"/> with the property list of a TEST step: no network, the job's own home, no credential, the worktree at the base commit. The tree and HEAD are compared before and after; a step that changed either ends the job.</para>
/// <para><b>It never half-runs.</b> A non-zero exit, a run past its time, more than <see cref="MostBytes"/>, bytes that are not UTF-8, a changed tree and a unit that cannot start each end the job <c>instructions-failed</c>, with nothing cut: no job starts with half an instruction.
/// A declared key the reader cannot read is <c>toolchain-refused</c>, as for every other key of the file. Nothing of the output goes to a log line or the envelope.</para>
/// <para><b>Once.</b> The output is kept under <c>instructions/</c> in the job folder and the same text serves every later attempt of the job (a failover starts a new session).</para>
/// </remarks>
internal sealed class CoderInstructions(IRoleRun run, GitHistory git, CoderOsLayer layer, CoderTree tree, string baseSha, CoderTaskSite site)
{
    /// <summary>The most the step may print: 64 KiB of UTF-8.</summary>
    public const int MostBytes = 64 * 1024;

    private const int TailLines = 8;

    private const int TailCharacters = 300;

    private CoderInstructionsOutcome? _kept;

    /// <summary>Runs the step once; a second call answers what the first did, and runs nothing.</summary>
    public async Task<CoderInstructionsOutcome> RunAsync(CancellationToken ct) => _kept ??= await RunOnceAsync(ct);

    private async Task<CoderInstructionsOutcome> RunOnceAsync(CancellationToken ct)
    {
        run.Phase("instructions", 0);
        var read = await new ChecksAtBase(git, new CheckRunner(layer.Launcher, layer.ChecksBoundary())).ToolchainAtBaseAsync(tree.MainCheckout, baseSha, ct);
        if (read is ToolchainRead.Refused { InstructionFaults.Count: > 0 } refused)
        {
            return new CoderInstructionsOutcome.Failed("toolchain-refused", $"the 'instructions' key of {ToolchainTables.FilePath} at the base commit cannot be read, so no model was launched: {string.Join("; ", refused.InstructionFaults)}", new CoderInstructionsRecord(true, [], 0, 0, string.Empty, 0, []));
        }

        if (read is not ToolchainRead.Configured { Toolchain.Instructions: { Declared: true } step })
        {
            return new CoderInstructionsOutcome.None();
        }

        if (site.Files.FirstOrDefault(file => !IsPlainPath(file)) is { } odd)
        {
            return Fail(step.Argv, $"the task names the file '{Redaction.SafeText(odd, TailCharacters)}', which cannot be given to the step: a file is a relative path with no '..', no leading '-' and no control character", 0, 0, string.Empty, 0, []);
        }

        var argv = step.ArgvFor(site.Files);
        var before = await CoderChecks.StateAsync(git, tree, ct);
        if (before.Sentence.Length > 0)
        {
            return Fail(argv, before.Sentence, 0, 0, string.Empty, 0, []);
        }

        var admitted = await layer.ChecksBoundary().AdmitAsync(layer.Job, [layer.Job.NetworkOf(CheckStep.Test).Mode], ct);
        if (admitted is BoundaryAdmission.Refused refusal)
        {
            return Fail(argv, $"the unit the step runs in could not be started: {refusal.Sentence}", 0, 0, string.Empty, 0, []);
        }

        var started = run.Clock.GetTimestamp();
        BoundaryRun result;
        try
        {
            result = await layer.ChecksBoundary().RunAsync((BoundaryAdmission.Admitted)admitted, layer.Job, layer.Job.NetworkOf(CheckStep.Test), "i1", [CheckRunner.Executable(argv[0], tree.Root), .. argv.Skip(1)], new Dictionary<string, string?>(), TimeSpan.FromSeconds(step.TimeoutSeconds), 4 * MostBytes, ct);
        }
        catch (ArgumentException e)
        {
            return Fail(argv, $"the unit the step runs in could not be started: {e.Message}", 0, 0, string.Empty, 0, []);
        }

        if (ct.IsCancellationRequested)
        {
            return new CoderInstructionsOutcome.Cancelled();
        }

        var ms = (long)run.Clock.GetElapsedTime(started).TotalMilliseconds;
        var after = await CoderChecks.StateAsync(git, tree, ct);

        return Judge(argv, step.TimeoutSeconds, result, before, after, ms);
    }

    private CoderInstructionsOutcome Judge(IReadOnlyList<string> argv, int seconds, BoundaryRun result, CoderTreeState before, CoderTreeState after, long ms)
    {
        var text = result.Result.StdOut;
        var bytes = Encoding.UTF8.GetByteCount(text);
        var tail = TailOf(result.Result.StdErr);
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
        var exit = result.Gone ? result.Result.ExitCode : -1;
        var why = Why(result, seconds, bytes, text, CoderChecks.Moved(before, after));
        if (why.Length > 0)
        {
            return Fail(argv, why, exit, bytes, hash, ms, tail);
        }

        var record = new CoderInstructionsRecord(true, argv, 0, bytes, hash, ms, []);
        Keep(text, record);
        run.Log.Information("role job {JobId}: the instruction step ran: exit 0, {Bytes} bytes, sha256 {Sha256}, {Ms} ms", run.Request.JobId, bytes, hash, ms);

        return new CoderInstructionsOutcome.Ran(record, text, baseSha);
    }

    /// <summary>The sentence that says why the step did not run to a clean end, or empty. In the order a person would look: the unit, the clock, the exit, the size, the bytes, the tree.</summary>
    private static string Why(BoundaryRun result, int seconds, int bytes, string text, string moved) => result.Result switch
    {
        _ when !result.Gone => "the unit the step ran in could not be stopped, so its result is not trusted",
        { ExitCode: StepRecord.NotStartedExitCode } => $"the unit the step runs in could not be started: {Redaction.SafeText(result.Result.StdErr, TailCharacters)}",
        { TimedOut: true } => $"the step ran past its {seconds} s and was stopped",
        { ExitCode: not 0 } => $"the step exited {result.Result.ExitCode}",
        { Truncated: true } => $"the step printed more than {MostBytes} bytes, and nothing is cut",
        _ when bytes > MostBytes => $"the step printed {bytes} bytes, more than {MostBytes}, and nothing is cut",
        _ when text.Contains('�', StringComparison.Ordinal) => "the step printed bytes that are not UTF-8",
        _ => moved,
    };

    private CoderInstructionsOutcome.Failed Fail(IReadOnlyList<string> argv, string why, int exit, long bytes, string hash, long ms, IReadOnlyList<string> tail) =>
        new(RoleStopWords.InstructionsFailed, $"the instruction step of this repository could not run, so no model was launched: {why}", new CoderInstructionsRecord(true, argv, exit, bytes, hash, ms, tail));

    private static IReadOnlyList<string> TailOf(string stderr) =>
    [
        .. stderr.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(line => line.Trim()).Where(line => line.Length > 0).TakeLast(TailLines)
            .Select(line => Redaction.SafeText(line, TailCharacters)),
    ];

    /// <summary>A file of the task, as the step is given it: relative, no <c>..</c>, no leading dash (it would read as an option), no control character.</summary>
    private static bool IsPlainPath(string file) =>
        file.Length > 0 && !file.StartsWith('-') && !Path.IsPathRooted(file) && !file.Split('/', '\\').Contains("..") && !file.Any(char.IsControl);

    /// <summary>The output and its record under <c>instructions/</c> in the job folder: kept for the job, and read by nobody but a person.</summary>
    private void Keep(string text, CoderInstructionsRecord record)
    {
        try
        {
            var folder = Path.Combine(run.Folder, "instructions");
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "stdout.txt"), text, new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(folder, "result.json"), $"{{\"exitCode\":0,\"bytes\":{record.Bytes.ToString(CultureInfo.InvariantCulture)},\"sha256\":\"{record.Sha256}\",\"baseSha\":\"{baseSha}\"}}\n");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            run.Log.Warning(e, "role job {JobId}: the output of the instruction step could not be kept", run.Request.JobId);
        }
    }
}
