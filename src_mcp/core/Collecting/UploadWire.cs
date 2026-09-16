using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace CoaiMcp.Core.Collecting;

/// <summary>
/// One pair, as it crosses the wire — three fields and no more.
/// </summary>
/// <remarks>
/// <para><b>ONE type, in the core both halves reference.</b> It was two: `CoaiMcp.Collecting` had
/// one and `CoaiBugs` had another, and two code-round reviewers said the same thing about it — a
/// field renamed on one side leaves the other compiling, both suites green, and the runtime dropping
/// whatever no longer lines up. The architecture test that pins the three fields could only ever
/// watch one of the two.</para>
/// <para><b>The fields are NULLABLE, whatever the initializers say.</b> A client that omits
/// <c>language</c> — or sends it as JSON <c>null</c> — produces a null here regardless of the
/// <c>= null</c> default, and the server then hands it to a validator that dereferences it, turning
/// malformed input into a 500. <see cref="Whole"/> is the one place that is settled, and the route
/// calls it before anything else reads a field. (Code round, codex.)</para>
/// <para><b>No id.</b> The server derives one with <see cref="PairId.Of"/> from exactly these three
/// fields, so nothing extra crosses and there is no second copy to disagree with the first.</para>
/// </remarks>
public sealed record UploadedPair(
    string? Language = null, string? SkeletonBefore = null, string? SkeletonAfter = null)
{
    /// <summary>The same three fields with every null read as the empty string.</summary>
    /// <remarks>
    /// A method rather than three computed properties: the architecture test asserts this type's
    /// PROPERTIES are exactly the three that may cross, and a computed property would widen that
    /// list for no reason anybody could see later.
    /// </remarks>
    public (string Language, string Before, string After) Whole() =>
        (Language ?? string.Empty, SkeletonBefore ?? string.Empty, SkeletonAfter ?? string.Empty);
}

/// <summary>A batch, as the client sends it.</summary>
/// <remarks>
/// <c>Items</c> is nullable and read through a pattern match at the boundary: a document with no
/// <c>items</c> is a malformed request, and normalising it to an empty list would make a misspelled
/// field look like a successful no-op. `coai-mcp`'s own `--pairs-keep` learned that during story 5.
/// </remarks>
public sealed record UploadRequest(IReadOnlyList<UploadedPair>? Items = null);

/// <summary>What one item came to.</summary>
public sealed record UploadResult(string EntryId = "", string Took = "", string Why = "");

/// <summary>What the batch came to, item by item.</summary>
public sealed record UploadAnswer(IReadOnlyList<UploadResult>? Items = null);

/// <summary>
/// The three words an item's fate is spelled with, and the only three.
/// </summary>
/// <remarks>
/// Constants in the shared core because the client used to treat every word it did not recognise as
/// a refusal — so a typo, a new outcome or a truncated field would permanently mark perfectly good
/// pairs as refused and record a defect in a normaliser that has none. A word this does not know is
/// now a contract mismatch, which stops the whole batch instead. (Code round, codex.)
/// </remarks>
public static class Took
{
    /// <summary>Stored in quarantine, waiting for a person.</summary>
    public const string Accepted = "accepted";

    /// <summary>Already held. A SUCCESS: the client may stop sending it.</summary>
    public const string Duplicate = "duplicate";

    /// <summary>The alphabet refused it. Something upstream leaked.</summary>
    public const string Refused = "refused";

    /// <summary>Whether this is a word both halves agree on.</summary>
    public static bool Known(string word) =>
        word is Accepted or Duplicate or Refused;
}

/// <summary>
/// The id of a pair: a pure function of what the pair IS.
/// </summary>
/// <remarks>
/// <para>Stable across machines and across runs, carries nothing, and makes two people who found the
/// same defect in the same library one row rather than two. It lives here rather than in the server
/// because the CLIENT needs it too — an acknowledgement is matched to a local pair by this id, and a
/// client that could not compute it would be back to trusting the answer's ORDER.</para>
/// <para><b>Length-prefixed, not delimiter-joined.</b> It hashed <c>$"{language}\0{before}\0{after}"</c>,
/// and a skeleton may legally contain a NUL: <c>before = "method_1() { }\0"</c> with one
/// <c>after</c> hashed identically to the plain <c>before</c> with a <c>"\0"</c>-prefixed
/// <c>after</c>, so the second, genuinely different pair came back <c>duplicate</c> and was never
/// stored. A length in front of each field cannot be forged from the field's own bytes. (Code round,
/// codex.)</para>
/// </remarks>
public static class PairId
{
    /// <summary>The id the server will derive for this payload, computed identically on both sides.</summary>
    public static string Of(string language, string before, string after)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Span<byte> length = stackalloc byte[sizeof(int)];
        foreach (var part in (string[])[language, before, after])
        {
            var bytes = Encoding.UTF8.GetBytes(part);
            BinaryPrimitives.WriteInt32BigEndian(length, bytes.Length);
            hash.AppendData(length);
            hash.AppendData(bytes);
        }

        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    /// <summary>The id of a pair as it stands on the wire.</summary>
    public static string Of(UploadedPair pair)
    {
        var (language, before, after) = pair.Whole();

        return Of(language, before, after);
    }
}
