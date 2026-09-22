using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace CoaiMcp.Core.Collecting;

/// <summary>
/// One pair, as it crosses the wire — three fields the machine derived, and one a person typed.
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
/// <para><b>No id.</b> The server derives one with <see cref="PairId.Of"/> from exactly the three
/// DERIVED fields, so nothing extra crosses and there is no second copy to disagree with the first.
/// </para>
/// <para><b><see cref="Comment"/> is the fourth field, and it is the only one a person TYPED</b> —
/// added 2026-09-21 on the operator's decision that *a comment a person wrote is public; it goes
/// everywhere, including to the server, for storage and later processing.* The three it joins are
/// derived from somebody's repository and are anonymised before they leave; this one is not
/// anonymised, is not scanned, and is not scrubbed, and the page that offers the box says so beside
/// it. That is why widening the architecture test was safe, and the test's own docblock records the
/// reasoning.</para>
/// <para><b>It is NOT part of the id.</b> <see cref="PairId.Of(UploadedPair)"/> still deconstructs
/// three. If a comment entered the derivation, every entry already in quarantine and in the corpus
/// would change identity, the client's acknowledgement matching would break against every old row,
/// and two people who found the same defect would stop deduplicating. The consequence is accepted:
/// the same skeleton pair from two contributors is ONE row and the first comment is the one
/// stored — and the answer for the second SAYS so rather than reporting a silent success.</para>
/// <para><b>A null comment is OMITTED from the JSON</b> (<c>WhenWritingNull</c> on the client's
/// context), so a pair without one serialises byte-identically to the wire as it was before this
/// field existed. A fixture captured from that build asserts it, which is what lets a client that
/// nobody comments through keep talking to a server of any age.</para>
/// </remarks>
public sealed record UploadedPair(
    string? Language = null, string? SkeletonBefore = null, string? SkeletonAfter = null,
    string? Comment = null)
{
    /// <summary>The same four fields with every null read as the empty string.</summary>
    /// <remarks>
    /// A method rather than four computed properties: the architecture test asserts this type's
    /// PROPERTIES are exactly the four that may cross, and a computed property would widen that
    /// list for no reason anybody could see later.
    /// </remarks>
    public (string Language, string Before, string After, string Comment) Whole() =>
        (Language ?? string.Empty, SkeletonBefore ?? string.Empty, SkeletonAfter ?? string.Empty,
            Comment ?? string.Empty);
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

/// <summary>What the batch came to, item by item — and which contract answered.</summary>
/// <remarks>
/// <para><b><see cref="Contract"/> is a belt, not the mechanism.</b> What stops an old server
/// taking a commented pair is that it has no route to take it on: comments POST to
/// <c>/ingest/commented</c>, which a binary older than 2026-09-21 answers 404. A capability the
/// client merely ASKS about cannot work — the probe reaches one node and the POST reaches another
/// during a rollout, and by the time the client learns anything the comment is gone and a retry is
/// answered <c>duplicate</c>. (Plan round, codex.)</para>
/// <para>The number is still carried on every answer, and a batch that went to the commented route
/// and came back without <see cref="Contract"/> at <see cref="Collecting.Contract.Comments"/> marks
/// nothing — which catches a proxy answering 200 for a route it does not really have. Absent reads
/// as 0, and 1 is the three-field wire, never declared.</para>
/// </remarks>
public sealed record UploadAnswer(IReadOnlyList<UploadResult>? Items = null, int Contract = 0);

/// <summary>The wire's own version, as the server states it on every answer.</summary>
public static class Contract
{
    /// <summary>A server that accepts a comment, and has a route to accept it on.</summary>
    public const int Comments = 2;
}

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
    /// <remarks>
    /// <b>The comment is deliberately not read here</b>, and the discard is written out rather than
    /// hidden behind a shorter deconstruction so that the omission is visible to whoever reads this
    /// next. An id that moved when somebody typed would change the identity of every row already
    /// stored, break acknowledgement matching against all of them, and stop two people who found the
    /// same defect from deduplicating.
    /// </remarks>
    public static string Of(UploadedPair pair)
    {
        var (language, before, after, _) = pair.Whole();

        return Of(language, before, after);
    }
}
