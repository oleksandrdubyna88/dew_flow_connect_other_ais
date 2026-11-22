namespace CoaiMcp.Install;

/// <summary>
/// The steps every MCP target shares: check the file can be changed, run the client's commands, read the file back, and put the old entry back when a step failed
/// (v2 E3.S6 t2). What differs per client — the commands, and whether an existing entry must be removed first — is the three members below.
/// </summary>
internal abstract class McpTarget : IInstallTarget
{
    public abstract Client Cli { get; }

    public string Kind => "mcp";

    public abstract string FileOf(InstallHost host);

    public abstract ConfigRead Read(string? text);

    public virtual McpEntry NewEntry(string binary) => new(binary, [], new Dictionary<string, string>());

    public virtual McpEntry WithCommand(McpEntry before, string binary) => before.WithCommand(binary);

    public virtual string Via(PlanAction action) => string.Empty;

    public bool FollowsBinary => true;

    /// <summary>Whether the client refuses to add over an existing entry (Claude), so an update is a remove and an add.</summary>
    protected abstract bool RemoveBeforeAdd { get; }

    protected abstract Task<VendorAnswer> AddAsync(McpEntry entry, ApplyContext context, CancellationToken ct);

    protected abstract Task<VendorAnswer> RemoveAsync(ApplyContext context, CancellationToken ct);

    public async Task<TargetOutcome> ApplyAsync(PlannedTarget plan, ApplyContext context, CancellationToken ct)
    {
        var failure = await StepsAsync(plan, context, plan.File, ct);
        if (failure.Length == 0)
        {
            context.Backup.SetState(Cli, Kind, ManifestStates.Verified);
            context.Faults.AfterState(Cli, Kind, ManifestStates.Verified);

            return TargetOutcome.Done;
        }

        return await UndoAsync(plan, context, failure, ct);
    }

    /// <summary>The steps; the empty string when the file reads back as wanted, else the sentence that says what failed.</summary>
    private async Task<string> StepsAsync(PlannedTarget plan, ApplyContext context, string file, CancellationToken ct)
    {
        if (plan.Before is not null && (plan.After is null || RemoveBeforeAdd))
        {
            if (await RemoveAsync(context, ct) is { Ok: false } removed)
            {
                return removed.Reason;
            }

            Step(context, ManifestStates.Removed);
        }

        if (plan.After is not null)
        {
            if (await AddAsync(plan.After, context, ct) is { Ok: false } added)
            {
                return added.Reason;
            }

            Step(context, ManifestStates.Written);
        }

        return Matches(ReadNow(file), plan.After) ? string.Empty : $"{file} does not hold the wanted entry after the command said it finished";
    }

    private void Step(ApplyContext context, string state)
    {
        context.Backup.SetState(Cli, Kind, state);
        context.Faults.AfterState(Cli, Kind, state);
    }

    /// <summary>Puts the entry that was on disk back, as far as the client allows, and says so.</summary>
    private async Task<TargetOutcome> UndoAsync(PlannedTarget plan, ApplyContext context, string failure, CancellationToken ct)
    {
        var now = ReadNow(plan.File);
        var restored = Matches(now, plan.Before);
        if (!restored)
        {
            restored = await PutBackAsync(plan, now, context, ct) && Matches(ReadNow(plan.File), plan.Before);
        }

        context.Backup.SetState(Cli, Kind, restored ? ManifestStates.RolledBack : ManifestStates.Failed);

        return new TargetOutcome(OutcomeKind.Failed, restored ? $"{failure}; the old entry was put back" : $"{failure}; the old entry could NOT be put back — backup {context.Backup.Id} holds it");
    }

    private async Task<bool> PutBackAsync(PlannedTarget plan, ConfigRead now, ApplyContext context, CancellationToken ct)
    {
        var present = now is ConfigRead.Found { Entry: not null };
        if (present && (plan.Before is null || RemoveBeforeAdd) && !(await RemoveAsync(context, ct)).Ok)
        {
            return false;
        }

        return plan.Before is null || (await AddAsync(plan.Before, context, ct)).Ok;
    }

    private static bool Matches(ConfigRead read, McpEntry? wanted) =>
        read is ConfigRead.Found found && (wanted is null ? found.Entry is null : wanted.Same(found.Entry));

    /// <summary>The file as it is now, read again: what the client really wrote, not what it said.</summary>
    private ConfigRead ReadNow(string file)
    {
        try
        {
            return Read(File.Exists(file) ? File.ReadAllText(file) : null);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return new ConfigRead.Unreadable(e.Message);
        }
    }
}
