namespace CoaiMcp.Install;

/// <summary>What the run is for: bring the entries to the binary, or take ours away.</summary>
internal enum Wanted
{
    Install,
    Remove,
}

/// <summary>What the planner needs besides the file texts — all of it injected, so a scenario is a unit test.</summary>
/// <param name="Wanted">Install or remove.</param>
/// <param name="Binary">The path to register.</param>
/// <param name="BinaryExplicit">Whether the person named it (<c>--binary</c>) — then it is registered over another live one; otherwise another live <c>coai-mcp</c> is left standing.</param>
/// <param name="IsWindows">Whether this side is Windows: a path with a character <c>cmd.exe</c> reads as an operator is refused there.</param>
/// <param name="Exists">Whether a file is on this side's disk — how a registered binary is told missing.</param>
internal sealed record PlanInputs(Wanted Wanted, string Binary, bool BinaryExplicit, bool IsWindows, Func<string, bool> Exists);

/// <summary>
/// The decisions of the installer — PURE: file texts and found CLIs and flags in, one <see cref="PlannedTarget"/> per target out (v2 E3.S6 t2; most of the scenarios are unit tests of this class).
/// </summary>
internal static class ClientPlan
{
    /// <summary>The characters <c>cmd.exe</c> reads as operators — a <c>.cmd</c> shim re-parses its arguments, so a path holding one is refused, not quoted.</summary>
    private static readonly char[] CmdOperators = ['&', '%', '^', '|', '<', '>', '\r', '\n'];

    /// <summary>The target of <paramref name="target"/> for an install or a removal, from what its file holds.</summary>
    internal static PlannedTarget Plan(IInstallTarget target, InstallHost host, ConfigRead read, PlanInputs inputs, McpEntry? interrupted)
    {
        var file = target.FileOf(host);
        if (read is not ConfigRead.Found found)
        {
            return PlannedTarget.Refused(target.Cli, target.Kind, file, $"{file} cannot be changed: {((ConfigRead.Unreadable)read).Reason}");
        }

        return inputs.Wanted == Wanted.Remove
            ? Removal(target, file, found)
            : Install(target, file, found, inputs, interrupted);
    }

    private static PlannedTarget Removal(IInstallTarget target, string file, ConfigRead.Found found) => found.Entry switch
    {
        null => PlannedTarget.Nothing(target.Cli, target.Kind, file, "no coai entry", found.ShadowedBy),
        { IsOurs: false } => PlannedTarget.Nothing(target.Cli, target.Kind, file, $"an entry named coai that starts '{found.Entry.FileName}' is not ours and is left alone", found.ShadowedBy),
        var ours => new PlannedTarget(target.Cli, target.Kind, file, PlanAction.Remove, Via(target.Cli, PlanAction.Remove), ours, null, found.ShadowedBy, "our entry is removed"),
    };

    private static PlannedTarget Install(IInstallTarget target, string file, ConfigRead.Found found, PlanInputs inputs, McpEntry? interrupted)
    {
        if (found.Entry is { IsOurs: false } foreign)
        {
            return PlannedTarget.Refused(target.Cli, target.Kind, file, $"an entry named coai already starts '{(foreign.FileName.Length > 0 ? foreign.FileName : "something that is not coai-mcp")}', not coai-mcp — it is not ours to replace", found.ShadowedBy);
        }

        if (found.Entry is { } ours && SamePath(ours.Command, inputs.Binary, inputs.IsWindows))
        {
            return PlannedTarget.Nothing(target.Cli, target.Kind, file, "already registered at this path", found.ShadowedBy);
        }

        if (inputs.IsWindows && inputs.Binary.IndexOfAny(CmdOperators) >= 0)
        {
            return PlannedTarget.Refused(target.Cli, target.Kind, file, $"the binary path holds a character cmd.exe reads as an operator (one of & % ^ | < >): {inputs.Binary}", found.ShadowedBy);
        }

        return found.Entry is null ? Added(target, file, found, inputs, interrupted) : Moved(target, file, found, found.Entry, inputs);
    }

    private static PlannedTarget Added(IInstallTarget target, string file, ConfigRead.Found found, PlanInputs inputs, McpEntry? interrupted)
    {
        var after = interrupted is null ? NewEntry(target.Cli, inputs.Binary) : WithCommand(target.Cli, interrupted, inputs.Binary);

        return new PlannedTarget(
            target.Cli, target.Kind, file, PlanAction.Add, Via(target.Cli, PlanAction.Add), null, after, found.ShadowedBy,
            interrupted is null ? "no coai entry yet" : "an update was interrupted after the old entry was removed; it is finished with the old environment");
    }

    private static PlannedTarget Moved(IInstallTarget target, string file, ConfigRead.Found found, McpEntry registered, PlanInputs inputs)
    {
        if (!inputs.BinaryExplicit && inputs.Exists(registered.Command) && !InSameVersionsFolder(registered.Command, inputs.Binary))
        {
            return PlannedTarget.Nothing(
                target.Cli, target.Kind, file,
                $"another coai-mcp is registered and present ({registered.Command}); it is left standing — name this one with --binary to move it",
                [.. found.ShadowedBy, $"registered:{registered.Command}"]);
        }

        return new PlannedTarget(
            target.Cli, target.Kind, file, PlanAction.Update, Via(target.Cli, PlanAction.Update), registered, WithCommand(target.Cli, registered, inputs.Binary), found.ShadowedBy,
            "the registered binary is not this one; the command is replaced and its environment and arguments are kept");
    }

    /// <summary>The restore of <paramref name="target"/>: the file's entry goes back to <paramref name="before"/>, but only when it is still what the install wrote (<paramref name="after"/>).</summary>
    internal static PlannedTarget Restore(IInstallTarget target, InstallHost host, ConfigRead read, McpEntry? before, McpEntry? after)
    {
        var file = target.FileOf(host);
        if (read is not ConfigRead.Found found)
        {
            return PlannedTarget.Refused(target.Cli, target.Kind, file, $"{file} cannot be changed: {((ConfigRead.Unreadable)read).Reason}");
        }

        if (before is null ? found.Entry is null : before.Same(found.Entry))
        {
            return PlannedTarget.Nothing(target.Cli, target.Kind, file, "already as it was before the install", found.ShadowedBy);
        }

        return (after is null ? found.Entry is null : after.Same(found.Entry))
            ? new PlannedTarget(target.Cli, target.Kind, file, PlanAction.Restore, Via(target.Cli, PlanAction.Restore), found.Entry, before, found.ShadowedBy, "the entry goes back to what it was before the install")
            : PlannedTarget.Refused(target.Cli, target.Kind, file, $"{file} no longer holds what the install wrote, so it is not put back over a later change", found.ShadowedBy);
    }

    /// <summary>agy's one target: answered <c>unmeasured</c>, with the file it would be.</summary>
    internal static PlannedTarget Unmeasured(IInstallTarget target, InstallHost host) =>
        PlannedTarget.Nothing(target.Cli, target.Kind, target.FileOf(host), AgyMcp.Unmeasured);

    private static McpEntry NewEntry(Client cli, string binary) =>
        cli == Client.Claude ? ClaudeMcp.NewEntry(binary) : new McpEntry(binary, [], new Dictionary<string, string>());

    private static McpEntry WithCommand(Client cli, McpEntry before, string binary) =>
        cli == Client.Claude ? ClaudeMcp.Updated(before, binary) : before.WithCommand(binary);

    /// <summary>The vendor commands an action runs, for the output.</summary>
    internal static string Via(Client cli, PlanAction action) => (cli, action) switch
    {
        (Client.Claude, PlanAction.Add) => "claude mcp add-json",
        (Client.Claude, PlanAction.Update or PlanAction.Restore) => "claude mcp remove + claude mcp add-json",
        (Client.Claude, PlanAction.Remove) => "claude mcp remove",
        (Client.Codex, PlanAction.Add or PlanAction.Update or PlanAction.Restore) => "codex mcp add",
        (Client.Codex, PlanAction.Remove) => "codex mcp remove",
        _ => string.Empty,
    };

    private static bool SamePath(string registered, string binary, bool isWindows) =>
        registered.Equals(binary, isWindows ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    /// <summary>
    /// Both paths are <c>…/versions/&lt;v&gt;/coai-mcp</c> under the same folder — one binary's older and newer places, so the newer replaces the older
    /// (<c>versions/&lt;v&gt;</c> disappears after two updates, which is why the stable path is what is registered).
    /// </summary>
    internal static bool InSameVersionsFolder(string a, string b)
    {
        static string? Root(string path)
        {
            var parts = path.Replace('\\', '/').Split('/');

            return parts.Length >= 3 && parts[^3] == "versions" ? string.Join('/', parts[..^3]) : null;
        }

        return Root(a) is { } rootA && Root(b) is { } rootB && rootA.Equals(rootB, StringComparison.OrdinalIgnoreCase);
    }
}
