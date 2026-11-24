namespace CoaiMcp.Install;

/// <summary>How a target's work ended.</summary>
internal enum OutcomeKind
{
    /// <summary>The file holds what was wanted, read back.</summary>
    Done,

    /// <summary>Nothing was changed, and the reason is the OS's, the file's or the client's own (a claude that cannot take the entry on any road that keeps a value off argv).</summary>
    Refused,

    /// <summary>A step failed after the backup; the file was put back as far as it could be.</summary>
    Failed,
}

internal sealed record TargetOutcome(OutcomeKind Kind, string Reason)
{
    internal static TargetOutcome Done { get; } = new(OutcomeKind.Done, string.Empty);
}

/// <summary>
/// One thing the installer can change for a client — an MCP entry, and (Claude) the SessionStart hook of s7. A client has an ORDERED list of them, so a new
/// kind is a new element of <see cref="InstallTargets.For"/> and nothing else moves.
/// </summary>
internal interface IInstallTarget
{
    Client Cli { get; }

    /// <summary>The kind word of the output: <c>mcp</c> or <c>hook</c>.</summary>
    string Kind { get; }

    /// <summary>The file this target lives in on this side.</summary>
    string FileOf(InstallHost host);

    /// <summary>A new entry for <paramref name="binary"/>: what the file will hold when none is there.</summary>
    McpEntry NewEntry(string binary);

    /// <summary><paramref name="before"/> with its command replaced and everything else kept — the update of the contract.</summary>
    McpEntry WithCommand(McpEntry before, string binary);

    /// <summary>The words for what <paramref name="action"/> does to this target, for the output (empty when nothing outside the file is run).</summary>
    string Via(PlanAction action);

    /// <summary>Whether what the target holds is a function of the binary registered (an MCP entry, the hook) — false for text that does not name the binary, which is the same on every run.</summary>
    bool FollowsBinary { get; }

    /// <summary>What the file's text holds — pure; a missing file is <see langword="null"/>.</summary>
    ConfigRead Read(string? text);

    /// <summary>Carries out <paramref name="plan"/>: preflight, the client's own commands, a read-back, and an undo if a step failed.</summary>
    Task<TargetOutcome> ApplyAsync(PlannedTarget plan, ApplyContext context, CancellationToken ct);
}

/// <summary>What the steps of a target need: the host, the CLI's path and the version it answered to <c>--version</c> (empty when it said nothing), the command runner, and where progress is written down.</summary>
internal sealed record ApplyContext(InstallHost Host, string CliPath, VendorCommands Vendor, InstallBackup Backup, InstallFaults Faults, string CliVersion = "");

/// <summary>
/// Seams for a test: to END the process between two steps (the killed run of scenario 12), and to change settings.json between the hook target's read and its write (scenario 10; called with the file and the attempt, from 1). Production holds none: <see cref="None"/> does nothing.
/// </summary>
internal sealed record InstallFaults(Action<Client, string, string> AfterState, Action<string, int>? AfterSettingsRead = null)
{
    internal static InstallFaults None { get; } = new((_, _, _) => { });
}

/// <summary>The targets of each client, in the order they are applied.</summary>
internal static class InstallTargets
{
    internal static IReadOnlyList<IInstallTarget> For(Client client) => client switch
    {
        Client.Claude => [new ClaudeMcp(), new ClaudeHook()],
        Client.Codex => [new CodexMcp(), new CodexInstructions()],
        _ => [new AgyMcp()],
    };
}
