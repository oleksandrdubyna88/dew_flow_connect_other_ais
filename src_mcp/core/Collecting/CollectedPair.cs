namespace CoaiMcp.Core.Collecting;

/// <summary>
/// The artefact: one method as it was broken, and as it was fixed, with nothing of ours left in it.
/// </summary>
/// <remarks>
/// <para><b>Only the two skeletons and the language ever leave the machine.</b> The symbol travels
/// with them here because the review page shows a person WHICH method they are looking at, and a
/// list of anonymous bodies is a list nobody can review. It is local metadata, and the boundary that
/// matters is the upload, not the table.</para>
/// <para>The skeletons are anonymous by construction — that is what the zero-knowledge property test
/// guarantees, over real files, per grammar — and a skeleton that reaches this type carrying a name
/// is a <see cref="CollectState.Failed"/>, never a skip.</para>
/// </remarks>
/// <param name="SymbolName">The method's own name, for a person reading the list. Never uploaded.</param>
public sealed record CollectedPair(
    string SymbolName,
    string Language,
    string SkeletonBefore,
    string SkeletonAfter);

/// <summary>A pair as the database holds it, with what a person has decided about it.</summary>
/// <remarks>
/// It carries the finding's own severity, category and title so the page can show a person what the
/// reviewers said this defect WAS — the skeletons say what the code did, and neither is legible
/// without the other. None of those three is anonymous, and none of them is ever uploaded.
/// </remarks>
/// <param name="Keep">-1 nobody has looked, 0 dropped, 1 kept.</param>
/// <param name="Comment">
/// What a person wrote about the pair, or empty. The ONE field here that crosses and that nobody
/// derived: it is public by the operator's decision, travels on <c>/ingest/commented</c>, and never
/// enters the pair's id. LAST and defaulted, so every construction written before it — the
/// privacy test builds one by name — still compiles and still means what it did.
/// </param>
public sealed record StoredPair(
    long FindingId,
    string SymbolName,
    string Language,
    string SkeletonBefore,
    string SkeletonAfter,
    int Keep,
    string Severity,
    string Category,
    string Title,
    string Comment = "");

/// <summary>What a person decided about one pair.</summary>
public readonly record struct KeepDecision(long FindingId, int Keep);

/// <summary>What a person decided about one pair, and what they wrote about it.</summary>
/// <remarks>
/// Its own type rather than a wider <see cref="KeepDecision"/>: `--pairs-keep` takes that one and
/// must keep meaning exactly what it did, because an old panel calls it with no comment to give.
/// </remarks>
/// <param name="Comment">Already normalised and trimmed; empty is "no comment".</param>
public readonly record struct CommentedDecision(long FindingId, int Keep, string Comment);

/// <summary>What the ingest server said about one pair, ready to be written down.</summary>
/// <remarks>
/// A batch's outcomes travel together so they can be written in ONE transaction: marking two hundred
/// pairs one statement at a time leaves half a batch recorded when the process is killed.
/// </remarks>
/// <param name="Why">
/// When refused, the server's reason. When taken, the server's sentence about the COMMENT if it did
/// not land — somebody else's was there first, or the pair was already promoted — and empty when it
/// did or when there was none.
/// </param>
/// <param name="Comment">
/// The comment exactly as it crossed, so the acknowledgement can tell whether the words a person
/// sees now are the words the server has: text edited while its batch was in the air did not go.
/// </param>
public readonly record struct SendOutcome(long FindingId, string Why, bool WasRefused, string Comment = "");

/// <summary>The vocabulary of <see cref="StoredPair.Keep"/>, spelled once.</summary>
/// <remarks>
/// Not a boolean, for the reason <c>collect_state</c> is not one: "nobody has looked at it" and
/// "somebody said no" are different answers, and a flag makes the first indistinguishable from the
/// second for ever. A review of two hundred pairs happens over days, and the undecided ones are
/// exactly what a person needs to find when they come back.
/// </remarks>
public static class Keep
{
    /// <summary>Nobody has looked.</summary>
    public const int Undecided = -1;

    /// <summary>Somebody looked and said no.</summary>
    public const int Dropped = 0;

    /// <summary>Somebody looked and said yes.</summary>
    public const int Kept = 1;

    /// <summary>Whether this is a value the column may hold.</summary>
    /// <remarks>
    /// The decisions arrive in a file written by another process, so they are input from outside and
    /// are validated as such — the coding-style rule's "never trust external data", applied to the one
    /// place in this story where a number crosses a boundary.
    /// </remarks>
    public static bool IsDecision(int keep) => keep is Undecided or Dropped or Kept;
}
