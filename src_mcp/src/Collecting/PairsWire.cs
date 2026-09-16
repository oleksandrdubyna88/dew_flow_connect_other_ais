using CoaiMcp.Core.Collecting;

namespace CoaiMcp.Collecting;

/// <summary>What `--pairs-json` answers.</summary>
/// <remarks>
/// A wrapper rather than a bare array, for the reason every other mode here uses one: a document can
/// gain a field without the reader changing shape, and a reader that expected an array would have to
/// be rewritten rather than extended. The panel's own parser treats an absent list as none.
/// </remarks>
public sealed record PairsAnswer(IReadOnlyList<StoredPair> Items);

/// <summary>One decision as the panel sends it.</summary>
/// <param name="Keep">-1 undecided, 0 dropped, 1 kept. Validated on arrival — it came from outside.</param>
public sealed record KeepAsk(long FindingId = 0, int Keep = Core.Collecting.Keep.Undecided);

/// <summary>A batch of decisions, file-in as `--findings-many` takes its keys.</summary>
/// <remarks>
/// A file rather than argv, and a BATCH rather than one call per decision: a review of two hundred
/// pairs is two hundred process launches otherwise, which is the shape `--findings-many` exists to
/// have ended.
/// </remarks>
public sealed record KeepRequest(IReadOnlyList<KeepAsk>? Items = null);

/// <summary>How many rows a batch actually decided.</summary>
/// <remarks>
/// The count, not a success flag: a decision naming a pair this database does not have changes
/// nothing, and a caller that sent fifty and hears "forty-nine" has learned something a boolean would
/// have hidden.
/// </remarks>
public sealed record KeepAnswer(int Decided);
