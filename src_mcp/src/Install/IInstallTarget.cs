namespace CoaiMcp.Install;

/// <summary>How a target's work ended.</summary>
internal enum OutcomeKind
{
    /// <summary>The file holds what was wanted, read back.</summary>
    Done,

    /// <summary>Nothing was changed, and the reason is the OS's or the file's own.</summary>
    Refused,

    /// <summary>A step failed after the backup; the file was put back as far as it could be.</summary>
    Failed,
}

internal sealed record TargetOutcome(OutcomeKind Kind, string Reason)
{
    internal static TargetOutcome Done { get; } = new(OutcomeKind.Done, string.Empty);
}

/// <summary>
/// One thing the installer can change for a client — an MCP entry now, and the SessionStart hook of s7 later. A client has an ORDERED list of them, so a new
/// kind is a new element of <see cref="InstallTargets.For"/> and nothing else moves.
/// </summary>
internal interface IInstallTarget
{
    Client Cli { get; }

    /// <summary>The kind word of the output: <c>mcp</c>.</summary>
    string Kind { get; }

    /// <summary>The file this target lives in on this side.</summary>
    string FileOf(InstallHost host);

    /// <summary>What the file's text holds — pure; a missing file is <see langword="null"/>.</summary>
    ConfigRead Read(string? text);

    /// <summary>Carries out <paramref name="plan"/>: preflight, the client's own commands, a read-back, and an undo if a step failed.</summary>
    Task<TargetOutcome> ApplyAsync(PlannedTarget plan, ApplyContext context, CancellationToken ct);
}

/// <summary>What the steps of a target need: the host, the CLI's path, the command runner, and where progress is written down.</summary>
internal sealed record ApplyContext(InstallHost Host, string CliPath, VendorCommands Vendor, InstallBackup Backup, InstallFaults Faults);

/// <summary>
/// A seam for a test to END the process between two steps — the killed run of scenario 12. Production holds none: <see cref="None"/> does nothing.
/// </summary>
internal sealed record InstallFaults(Action<Client, string, string> AfterState)
{
    internal static InstallFaults None { get; } = new((_, _, _) => { });
}

/// <summary>The targets of each client, in the order they are applied.</summary>
internal static class InstallTargets
{
    internal static IReadOnlyList<IInstallTarget> For(Client client) => client switch
    {
        Client.Claude => [new ClaudeMcp()],
        Client.Codex => [new CodexMcp()],
        _ => [new AgyMcp()],
    };
}
