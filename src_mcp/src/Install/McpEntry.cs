using System.Collections.Frozen;

namespace CoaiMcp.Install;

/// <summary>
/// ONE client's <c>coai</c> MCP entry, as the file holds it: the command, its arguments, and the environment it is started with.
/// </summary>
/// <param name="Command">The registered executable, exactly as written (a Windows path stays a Windows path).</param>
/// <param name="Args">The arguments after it.</param>
/// <param name="Env">The environment names and values. Values are NEVER printed — the output carries names only.</param>
/// <param name="RawJson">Claude's whole entry object as JSON text (every key it holds, so an update changes <c>command</c> only); empty for codex.</param>
internal sealed record McpEntry(string Command, IReadOnlyList<string> Args, IReadOnlyDictionary<string, string> Env, string RawJson = "")
{
    /// <summary>The names an entry of ours may have as a file name.</summary>
    private static readonly FrozenSet<string> OurNames = new[] { "coai-mcp", "coai-mcp.exe" }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>The last segment of <see cref="Command"/>, cut at EITHER separator: a Windows path is judged the same on a Linux host.</summary>
    internal string FileName => FileNameOf(Command);

    /// <summary>The entry is ours when its command's file name is <c>coai-mcp</c> / <c>coai-mcp.exe</c> — a wrapper (<c>wsl.exe</c>) is not.</summary>
    internal bool IsOurs => OurNames.Contains(FileName);

    /// <summary>This entry with another command and everything else kept: the update of the contract (env and args stay).</summary>
    internal McpEntry WithCommand(string command) => this with { Command = command, RawJson = string.Empty };

    /// <summary>Whether two entries start the same program the same way (the raw text is not compared: key order is not a difference).</summary>
    internal bool Same(McpEntry? other) =>
        other is not null
        && Command.Equals(other.Command, StringComparison.Ordinal)
        && Args.SequenceEqual(other.Args, StringComparer.Ordinal)
        && Env.Count == other.Env.Count
        && Env.All(pair => other.Env.TryGetValue(pair.Key, out var value) && value.Equals(pair.Value, StringComparison.Ordinal));

    /// <summary>A file name from a path of either spelling.</summary>
    internal static string FileNameOf(string path)
    {
        var trimmed = path.Trim();

        return trimmed[(trimmed.LastIndexOfAny(['\\', '/']) + 1)..];
    }
}

/// <summary>What a read of one client's file found — a closed pair, because a file that cannot be read is an expected answer, not an exception.</summary>
internal abstract record ConfigRead
{
    private ConfigRead()
    {
    }

    /// <summary>The file is whole (or absent, or empty): the <c>coai</c> entry if there is one, and the project scopes that hold their own.</summary>
    internal sealed record Found(McpEntry? Entry, IReadOnlyList<string> ShadowedBy) : ConfigRead;

    /// <summary>The file is not the shape the client writes — nothing is changed in it, and the reason says what was seen.</summary>
    internal sealed record Unreadable(string Reason) : ConfigRead;

    /// <summary>An absent file.</summary>
    internal static ConfigRead None { get; } = new Found(null, []);
}
