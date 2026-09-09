namespace CoaiMcp.Runners.Reviewers;

/// <summary>
/// How this shim tags the lines it writes to stderr, and what that tag lets a reader do.
/// </summary>
/// <remarks>
/// <para>The shim and the vendor CLI it wraps share one stderr, and everything that reads that
/// stream to explain a failure has to be able to tell whose line it is holding. The tag is the only
/// thing that answers it, and it was known in exactly one place — a private constant in the shim's
/// own <c>Program</c>, which the code that READS the stream cannot reach.</para>
/// <para>Two things needed it on 2026-09-08. A progress recogniser matching a bare substring
/// anywhere in a line will call <c>error: waiting for the local engine at …: connection refused</c>
/// progress and hide it; anchored to the tag, it cannot. And the reason a person reads is capped at
/// 160 characters because a VENDOR's line can be any length at all — a cap that also cut our own
/// queued-out verdict off at "One caller uses t.", losing every cure it names.</para>
/// </remarks>
public static class ShimNotes
{
    /// <summary>What this program calls itself, in its version line and in front of its notes.</summary>
    public const string AppName = "coai-mcp";

    /// <summary>What the shim puts in front of every line it writes to stderr.</summary>
    public const string Prefix = $"[{AppName}] ";

    /// <summary>Whether WE wrote this line, rather than the vendor sharing the stream.</summary>
    public static bool Ours(string line) => line.StartsWith(Prefix, StringComparison.Ordinal);

    /// <summary>
    /// The sentence without the tag, so a recogniser can be ANCHORED rather than a substring search.
    /// </summary>
    public static string Message(string line) => Ours(line) ? line[Prefix.Length..] : line;
}
