using System.Security.Cryptography;
using System.Text;

namespace CoaiServer;

/// <summary>
/// The caller's own name for one attempt, so a lost answer can be asked for again.
/// </summary>
/// <remarks>
/// <para><b>The failure this exists for.</b> The POST reaches the server, the job is accepted, and
/// the response never comes back — a dropped connection, a proxy that gave up. The person presses
/// send again, and now there are two jobs for one question, each holding a slot on a shared account
/// where a slot is the scarcest thing there is. The client cannot tell the two cases apart, and it
/// should not have to.</para>
/// <para><b>Scoped to the CALLER, never global.</b> Two people are allowed to pick the same key —
/// they will, because a key is whatever a client feels like generating — and one of them must not be
/// handed the other's job. Every lookup is by <c>(email, key)</c>.</para>
/// <para><b>Bound to what was asked.</b> A key repeated with a different question is a bug in the
/// client, not a retry, and silently returning the first job would answer something nobody asked
/// while looking exactly like success. So the key carries a fingerprint of the request and a
/// mismatch is refused out loud. (codex and gemini, plan round, independently.)</para>
/// </remarks>
public static class Idempotency
{
    /// <summary>
    /// The shape a key may have.
    /// </summary>
    /// <remarks>
    /// A stranger's string that becomes part of a lookup and is echoed in a refusal. Bounded and
    /// restricted to what an identifier is made of — the same guard the catalog's vendor ids get, and
    /// the review ids on the client side. A GUID, which is what a client will send, fits easily.
    /// </remarks>
    public static bool IsUsable(string key) =>
        key.Length is > 0 and <= 128 && key.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.' or ':');

    /// <summary>Why this key cannot be used, or null.</summary>
    public static string? Refusal(string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            // Not sending one is allowed: it means "I accept that a retry may make a second job",
            // which is what every client did before this field existed.
            return null;
        }

        return IsUsable(key.Trim())
            ? null
            : "an idempotency key must be 1-128 characters of letters, digits, '-', '_', '.' or ':'";
    }

    /// <summary>What goes between two parts of a fingerprint, written as an escape on purpose.</summary>
    /// <remarks>
    /// A unit separator, and it is <c>\u001f</c> in SOURCE rather than the character itself: a control
    /// character pasted into a file is invisible in a diff, in a review, and in every editor that shows
    /// it as a space. The length prefix beside it is what actually makes a fingerprint unambiguous, so
    /// this only has to be something a reader can see is deliberate. (local, code round — it was
    /// declared below the method that used it.)
    /// </remarks>
    private const char Separator = '\u001f';

    /// <summary>
    /// What this request WAS, condensed, so a key reused for a different one can be told apart.
    /// </summary>
    /// <remarks>
    /// <para>A hash rather than the fields themselves, because the prompt is the whole transcript of a
    /// conversation and keeping a second copy of it per job to answer one equality question is a cost
    /// with no return. SHA-256 over the fields that decide what runs, WHO it runs for, and what it
    /// costs; the timeout is in it because two submissions differing only in their budget are two
    /// different asks of the vendor.</para>
    /// <para><b>The email is in it</b> so the value is caller-scoped by construction and not only by
    /// where it happens to be looked up. Two people are allowed to choose the same key, and a
    /// fingerprint that did not know whose it was would be one refactor away from matching across
    /// them. (gemini, code round.)</para>
    /// <para><b>Hashed incrementally, never assembled.</b> A prompt is a whole conversation and can be
    /// large; building one string out of it to hash would double that allocation on every submit, for a
    /// body the server has already paid to read once. Each part is fed to the hash as it stands.
    /// (local, code round.)</para>
    /// </remarks>
    public static string Fingerprint(
        string email, string vendor, string model, string role, string prompt, JobKind kind, int timeoutSeconds)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var part in new[]
                 { email, vendor, model, role, prompt, JobKinds.Wire(kind), timeoutSeconds.ToString() })
        {
            // Length-prefixed, so ("ab","c") and ("a","bc") are two fingerprints rather than one. A
            // prompt can contain ANY text — it is a conversation quoted from somewhere else — so no
            // separator is safe on its own; the length is what makes it unambiguous.
            hash.AppendData(Encoding.UTF8.GetBytes($"{part.Length}:"));
            hash.AppendData(Encoding.UTF8.GetBytes(part));
            hash.AppendData(Encoding.UTF8.GetBytes(Separator.ToString()));
        }

        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }
}
