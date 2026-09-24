using System.Security.Cryptography;
using System.Text;
using CoaiMcp.Core.Rounds;

namespace CoaiMcp.Server;

/// <summary>
/// The right to change one session's state, held by one call at a time, across every process.
/// </summary>
/// <remarks>
/// <para>Ported from the unmerged <c>coai-ar1-addressable-rounds</c> branch (S4 of
/// research/PLAN_a_failed_round_can_be_retried.md), onto today's three-part session key. A review
/// round reads the session, runs a fan-out for minutes, and writes it back — so two rounds on one
/// session, from two windows or two agents, both computed the next round from the same file and the
/// loser's round vanished. Once a code session can be reopened with <c>again</c>, that is an ordinary
/// thing to try, not a rare one.</para>
/// <para><b>An OS lock, not a lease.</b> A time lease must outlive the longest review or it expires
/// under a live round, and no such number exists. A file held open with <see cref="FileShare.None"/>
/// is released by the kernel when the process dies — exited, crashed or killed — so there is no
/// expiry to guess and no stale lock to break; the shape <c>SessionTurn</c> already uses.</para>
/// <para><b>Try once, then refuse.</b> Not <c>SessionTurn</c>'s retry loop: that guards a write of
/// milliseconds, this a round of minutes, and a caller made to wait would sit on a blocked MCP call
/// with no way to know why. The refusal comes before any reviewer starts.</para>
/// </remarks>
internal sealed class SessionClaim : IDisposable
{
    private readonly FileStream _held;

    private SessionClaim(FileStream held) => _held = held;

    /// <summary>The file whose handle IS the claim — one per session identity, document included.</summary>
    public static string FileFor(string dataDir, string repoPath, string branch, string document = "")
    {
        var key = SessionKey.For(repoPath, branch, document);
        var name = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(key)))[..16];

        return Path.Combine(dataDir, "claims", $"session-{name}.claim");
    }

    /// <summary>
    /// Takes the claim for this session, or answers null because another call holds it — the one
    /// legitimate "nothing" here, and the caller refuses on it.
    /// </summary>
    public static SessionClaim? TryTake(string dataDir, string repoPath, string branch, string document = "")
    {
        try
        {
            var file = FileFor(dataDir, repoPath, branch, document);
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);

            // OpenOrCreate, not Create: the file is a handle to hold, never content, and truncating
            // somebody else's held file is not something to attempt.
            return new SessionClaim(new FileStream(file, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>The sentence a call that could not take it gets. One place, so it reads alike.</summary>
    public static string Busy(string branch) =>
        $"another call is already changing the gate session for '{branch}' — a round is running, or its "
        + "findings are being resolved. One at a time: the round trail, the stage and the pending findings "
        + "are one document, and two calls in flight over it lose one of the two. Nothing was started; try "
        + "again when the other call finishes, or read it with status.";

    public void Dispose() => _held.Dispose();
}
