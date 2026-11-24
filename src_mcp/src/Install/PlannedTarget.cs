namespace CoaiMcp.Install;

/// <summary>What the installer will do to one file — the closed set of the contract's <c>action</c> column.</summary>
internal enum PlanAction
{
    Add,
    Update,
    None,
    Remove,
    Restore,
    Refuse,
}

internal static class PlanActions
{
    internal static string Word(this PlanAction action) => action switch
    {
        PlanAction.Add => "add",
        PlanAction.Update => "update",
        PlanAction.Remove => "remove",
        PlanAction.Restore => "restore",
        PlanAction.Refuse => "refuse",
        _ => "none",
    };

    /// <summary>An action that changes a file (so it needs a backup and a vendor command).</summary>
    internal static bool Changes(this PlanAction action) => action is PlanAction.Add or PlanAction.Update or PlanAction.Remove or PlanAction.Restore;
}

/// <summary>
/// One target of one client, decided: the file, what happens to it, how, and the entry before and after. <c>Before</c> is what is on disk NOW, <c>After</c> what the file
/// will hold — whether the action is an install, a removal or a restore. Pure data: the planner makes it from file texts, the executor carries it out.
/// </summary>
/// <param name="Client">Which CLI's target this is.</param>
/// <param name="Kind">What the target is: <c>mcp</c> (s7 adds <c>hook</c>).</param>
/// <param name="File">The file, as this side spells it.</param>
/// <param name="Action">What happens.</param>
/// <param name="Via">The vendor command that does it, or empty.</param>
/// <param name="Before">The <c>coai</c> entry now, or none.</param>
/// <param name="After">The <c>coai</c> entry the file will hold, or none (a removal).</param>
/// <param name="ShadowedBy">Other places that hold a <c>coai</c> entry which wins over this one, or the registered binary that was left standing.</param>
/// <param name="Reason">Why, in one sentence a person can act on.</param>
internal sealed record PlannedTarget(
    Client Client,
    string Kind,
    string File,
    PlanAction Action,
    string Via,
    McpEntry? Before,
    McpEntry? After,
    IReadOnlyList<string> ShadowedBy,
    string Reason)
{
    internal static PlannedTarget Nothing(Client client, string kind, string file, string reason, IReadOnlyList<string>? shadowedBy = null) =>
        new(client, kind, file, PlanAction.None, string.Empty, null, null, shadowedBy ?? [], reason);

    internal static PlannedTarget Refused(Client client, string kind, string file, string reason, IReadOnlyList<string>? shadowedBy = null) =>
        new(client, kind, file, PlanAction.Refuse, string.Empty, null, null, shadowedBy ?? [], reason);
}
