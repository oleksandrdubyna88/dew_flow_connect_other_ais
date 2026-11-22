using CoaiMcp.Runners.Processes;

namespace CoaiMcp.Install;

/// <summary>One target as the run saw it: the plan, and how it ended (<c>planned</c> until it is applied).</summary>
internal sealed record TargetRun(IInstallTarget Target, PlannedTarget Plan, string Status, string Reason);

/// <summary>One client as the run saw it: what was found, and its targets.</summary>
internal sealed record ClientRun(FoundClient Found, IReadOnlyList<TargetRun> Targets);

/// <summary>
/// <c>coai-mcp --setup-clients</c> and <c>--install</c> (v2 E3.S6): find the CLIs on this side, write the <c>coai</c> MCP entry through each one's own command, and report
/// what was done as ONE JSON object — the same engine under two names (<c>--install --shared</c> is epoch 2).
/// </summary>
/// <remarks>
/// <para><b>It writes other tools' configuration, so it is conservative in a fixed order:</b> plan from the files as they are (no lock, no write); with a change to make, take the
/// lock without waiting, plan AGAIN under it (another run may have just done it), make the backup, and only then run a client's command — each one bounded, its progress written to the
/// manifest, and the old entry put back if it fails. A run with nothing to change touches nothing: no state folder, no lock, no backup.</para>
/// <para><b>Exit codes, never 64</b> (64 is "never heard of this mode"): 0 every found CLI in the wanted state; 65 bad arguments or an unknown backup id; 69 no CLI found; 73 the backup could not be
/// written, nothing changed; 74 a write failed after the backup; 75 another run holds the lock; 78 one or more CLIs refused, the others done. A dry run answers the code the real run would.</para>
/// <para><b>Never:</b> open a token file; print an environment value; touch another server's entry, a project scope, <c>.mcp.json</c>; write a PreToolUse or AskUserQuestion hook; call
/// <c>claude mcp get</c> or <c>list</c> (they start the server) — the files are READ.</para>
/// </remarks>
internal static class SetupClientsMode
{
    internal const int BadArguments = 65;
    internal const int NoClientFound = 69;
    internal const int BackupNotWritten = 73;
    internal const int WriteFailed = 74;
    internal const int RunInProgress = 75;
    internal const int Refused = 78;

    internal static async Task<int> RunAsync(string mode, IReadOnlyList<string> args, SetupDependencies deps, TextWriter output, Action<string> note, CancellationToken ct = default)
    {
        var parsed = SetupArgs.Parse(mode, args);
        if (parsed.Error.Length > 0)
        {
            note(parsed.Error);

            return BadArguments;
        }

        var host = InstallHost.From(deps.Env, deps.IsWindows);
        if (!host.HasHome)
        {
            note($"the home folder is not set ({(deps.IsWindows ? "USERPROFILE" : "HOME")}), so no client's file can be found");

            return BadArguments;
        }

        var run = new SetupRun(mode, parsed, host, deps, note);

        return await run.ExecuteAsync(output, ct);
    }
}
