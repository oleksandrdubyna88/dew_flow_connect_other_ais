using System.Security.Cryptography;
using System.Text;
using CoaiMcp.Core.Rounds;

namespace CoaiMcp.Server;

/// <summary>
/// The right to change one session's state, held by one process at a time, across every process.
/// </summary>
/// <remarks>
/// <para><b>Why the locator's own guard was not enough.</b> The addressable path takes a running
/// slot in <c>round_locators</c>, so two <c>run_round</c> calls cannot overlap. Nothing of the sort
/// happens on the legacy path: <c>review_plan</c> and <c>review_code</c> read the session, run a
/// fan-out for minutes, and write it back. So a legacy round and an addressable one — or two legacy
/// rounds — on the same repo+branch ran together, both computing the next ordinal from the same
/// file, both writing the trail, and the loser's round vanished from a document it thought it had
/// updated. A guard on one of the two paths is not a guard.</para>
/// <para><b>Why an OS lock and not a lease.</b> A time lease has to be longer than the longest
/// review or it expires under a live round, and no such number exists — a code round is however
/// long six vendor CLIs take. A file held open with <see cref="FileShare.None"/> is released by the
/// kernel when the process dies, whether it exited, crashed or was killed, so there is no expiry to
/// guess and no stale lock to break. The same shape <see cref="SessionTurn"/> and the engine lease
/// already use here.</para>
/// <para><b>Try once, then refuse.</b> Deliberately not <see cref="SessionTurn"/>'s retry loop.
/// That one guards a write of milliseconds and waiting is right; this one guards a round of
/// minutes, and a caller made to wait for it would sit on a blocked MCP call with no way to know
/// why. Refusing immediately, naming the session, is the honest answer — and it happens BEFORE any
/// reviewer starts.</para>
/// </remarks>
internal sealed class SessionClaim : IDisposable
{
    private readonly FileStream _held;

    private SessionClaim(FileStream held) => _held = held;

    /// <summary>The file whose handle IS the claim. One per session identity.</summary>
    public static string FileFor(string dataDir, string repoPath, string branch)
    {
        var key = SessionKey.For(repoPath, branch);
        var name = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(key)))[..16];

        return Path.Combine(dataDir, "claims", $"session-{name}.claim");
    }

    /// <summary>
    /// Takes the claim for this repo+branch, or answers null because somebody else holds it.
    /// </summary>
    /// <remarks>
    /// Keyed by the same session identity every other part of the server uses, so two callers who
    /// would land on one session file contend and two who would not never meet.
    /// </remarks>
    public static SessionClaim? TryTake(string dataDir, string repoPath, string branch)
    {
        try
        {
            var file = FileFor(dataDir, repoPath, branch);
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);

            // OpenOrCreate rather than Create: the file is a handle to hold, never content, and
            // truncating somebody else's held file is not something to attempt.
            return new SessionClaim(new FileStream(
                file,
                FileMode.OpenOrCreate,
                FileAccess.ReadWrite,
                FileShare.None));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>The sentence a caller that could not take it gets. One place, so it reads alike.</summary>
    public static string Busy(string branch) =>
        $"another stage call is already changing the session for '{branch}'. One at a time: the round "
        + "trail, the stage and the pending findings are one document, and two calls in flight over it "
        + "lose one of the two. Nothing was started — try again when the other call finishes, or read "
        + "it with status.";

    public void Dispose() => _held.Dispose();
}
