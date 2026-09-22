using CoaiMcp.Core.Collecting;
using CoaiMcp.Store;

namespace CoaiMcp.Collecting;

/// <summary>What `--pairs-json` answers.</summary>
/// <remarks>
/// <para>A wrapper rather than a bare array, for the reason every other mode here uses one: a document
/// can gain a field without the reader changing shape, and a reader that expected an array would have
/// to be rewritten rather than extended. The panel's own parser treats an absent list as none.</para>
/// <para><b>It carries <see cref="ReviewPair"/>, the page's record — not <see cref="StoredPair"/>, the
/// send's.</b> Until story 2.1 of the review-page plan the two were one type, and the page showed a
/// method, two skeletons and nothing else. The page's row now also says where the defect was and what
/// the reviewers wrote; none of that may become reachable from <c>UploadRun.Wire</c>, which is why it
/// went into a second record rather than into the first. The first nine properties keep their names,
/// so an extension older than this field set reads exactly what it read before; the extension's
/// <c>pairOf</c> defaults each new field to empty, so a server older than this set is read by a newer
/// extension without complaint either.</para>
/// </remarks>
public sealed record PairsAnswer(IReadOnlyList<ReviewPair> Items);

/// <summary>One decision as the panel sends it.</summary>
/// <param name="Keep">-1 undecided, 0 dropped, 1 kept. Validated on arrival — it came from outside.</param>
public sealed record KeepAsk(long FindingId = 0, int Keep = Core.Collecting.Keep.Undecided);

/// <summary>A batch of decisions, file-in as `--findings-many` takes its keys.</summary>
/// <remarks>
/// A file rather than argv, and a BATCH rather than one call per decision: a review of two hundred
/// pairs is two hundred process launches otherwise, which is the shape `--findings-many` exists to
/// have ended.
/// </remarks>
public sealed record KeepRequest(IReadOnlyList<KeepAsk>? Items = null)
{
    /// <summary>The decisions, or nothing at all when the document did not carry the field.</summary>
    /// <remarks>
    /// Nullable ON PURPOSE and read through a pattern match at the one boundary: a client that omits
    /// `items` has sent a malformed request, and that must be told apart from one that explicitly sent
    /// an empty array. Normalising the absence to `[]` here would make a misspelled field look like a
    /// successful no-op — which it did, and a reviewer caught it. (Code round, codex.)
    /// </remarks>
    public IReadOnlyList<KeepAsk>? Items { get; init; } = Items;
}

/// <summary>One decision as the panel sends it to `--pairs-decide`: the keep AND the words.</summary>
/// <remarks>
/// Its own record, and its own MODE, rather than a comment added to <see cref="KeepAsk"/>: an older
/// binary handed a comment in `--pairs-keep`'s file would deserialise past it and answer
/// `{"decided": N}` — plausible and wrong, the comment dropped in silence. A new mode is the one
/// thing an old binary cannot pretend to understand; it exits 64, and the panel says so.
/// </remarks>
/// <param name="Comment">As typed. The mode normalises line endings, trims, and checks it.</param>
public sealed record DecideAsk(long FindingId = 0, int Keep = Core.Collecting.Keep.Undecided, string Comment = "");

/// <summary>A batch of decisions with their comments, file-in like <see cref="KeepRequest"/>.</summary>
/// <remarks>Nullable `items` for <see cref="KeepRequest.Items"/>'s reason: an absent list is a malformed request.</remarks>
public sealed record DecideRequest(IReadOnlyList<DecideAsk>? Items = null)
{
    /// <summary>The decisions, or nothing at all when the document did not carry the field.</summary>
    public IReadOnlyList<DecideAsk>? Items { get; init; } = Items;
}

/// <summary>How many rows a batch actually decided.</summary>
/// <remarks>
/// The count, not a success flag: a decision naming a pair this database does not have changes
/// nothing, and a caller that sent fifty and hears "forty-nine" has learned something a boolean would
/// have hidden.
/// </remarks>
public sealed record KeepAnswer(int Decided);
