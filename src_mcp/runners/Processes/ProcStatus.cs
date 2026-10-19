using System.Globalization;

namespace CoaiMcp.Runners.Processes;

/// <summary>
/// Whose a process is, from the text of its own <c>/proc/&lt;pid&gt;/status</c>: the <c>Uid:</c> line carries the REAL, effective, saved and filesystem uids. Read from here — never from the owner of the
/// <c>/proc/&lt;pid&gt;</c> directory, which the kernel hands to root for a non-dumpable process of an ordinary account.
/// </summary>
public static class ProcStatus
{
    private const string UidLine = "Uid:";

    /// <summary>
    /// <see langword="true"/> when the process's real OR effective uid is <paramref name="uid"/> (a process that can open a <c>0700</c> folder as this account is this account's to judge); <see langword="false"/>
    /// when both are another's; <see langword="null"/> when the text holds no parseable <c>Uid:</c> line — nothing can be said, and the caller must not read that as "not ours".
    /// </summary>
    public static bool? RunsAs(string status, uint uid) =>
        status.Split('\n').FirstOrDefault(line => line.StartsWith(UidLine, StringComparison.Ordinal)) is { } line && Uids(line) is { Count: >= 2 } uids ? uids[0] == uid || uids[1] == uid : null;

    private static List<uint> Uids(string line)
    {
        var fields = line[UidLine.Length..].Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries).Take(2).ToList();
        var parsed = fields.Select(field => (Ok: uint.TryParse(field, NumberStyles.None, CultureInfo.InvariantCulture, out var value), Value: value)).ToList();

        return parsed.All(field => field.Ok) ? [.. parsed.Select(field => field.Value)] : [];
    }
}
