using System.Security.Cryptography;
using System.Text;

namespace CoaiMcp.Core.Rounds;

/// <summary>
/// Which round at this server, as this server itself names it — the addressable identity.
/// </summary>
/// <remarks>
/// <para><b>Why a repo+branch pair is not enough.</b> A session is keyed by repo+branch
/// (<see cref="SessionKey"/>) and every round of that pair shares it, so "what happened to the code
/// round for this branch?" is a question with more than one right answer. The trail distinguishes
/// rounds by <c>number</c>, an ordinal assigned when a round STARTS — which a caller cannot know
/// before it dispatches, cannot reserve, and cannot tell apart from the ordinal a second client
/// took in the same second.</para>
/// <para>So a caller that lost its answer had exactly three ways to find it again, and all three are
/// guesses: the last round, the round number it predicted, or the session's aggregate. A gate whose
/// recovery is a guess writes another round's verdict into its own record, which is worse than
/// having no recovery at all.</para>
/// <para><b>The provider is part of the name.</b> Session and round ids are unique only inside one
/// server's namespace; a caller that has been re-pointed at a different server must be able to see
/// that its locator is not from here, rather than be answered by a stranger.</para>
/// </remarks>
public sealed record RoundLocator(string ProviderId, string SessionId, string RoundId)
{
    /// <summary>
    /// This server's own name, in every locator it issues.
    /// </summary>
    /// <remarks>
    /// A constant rather than a machine or install id on purpose: it identifies the PRODUCT whose
    /// session and round ids these are. Two coai-mcp installs sharing a data directory share their
    /// sessions too, so they are one namespace and not two.
    /// </remarks>
    public const string Provider = "coai-mcp";

    /// <summary>Longest each part may be — bounded because these cross a protocol boundary.</summary>
    public const int MaxPart = 128;

    /// <summary>
    /// Is every part present, non-empty and within bounds?
    /// </summary>
    /// <remarks>
    /// Half a locator is worse than none: it looks answerable and names nothing. An empty part is
    /// the same failure wearing a value, and it is the one that survives a round-trip through JSON
    /// where a missing field arrives as <c>""</c>.
    /// </remarks>
    public bool IsComplete =>
        Ok(ProviderId) && Ok(SessionId) && Ok(RoundId);

    private static bool Ok(string part) =>
        !string.IsNullOrWhiteSpace(part) && part.Length <= MaxPart;

    /// <summary>Exact equality on all three parts — never on a subset.</summary>
    public bool Matches(RoundLocator other) =>
        ProviderId == other.ProviderId && SessionId == other.SessionId && RoundId == other.RoundId;

    public override string ToString() => $"{ProviderId}/{SessionId}/{RoundId}";
}

/// <summary>
/// What a round actually read, in terms both sides can recompute.
/// </summary>
/// <remarks>
/// <para>A verdict without this is a statement about unnamed code. The three git ids are what the
/// round was pinned to — the base it diffed from, the commit its worktree was checked out at, and
/// that commit's TREE, which is git's own content hash of every reviewed byte. A caller holding the
/// same checkout can compute all three itself, so the attestation is checkable rather than
/// trusted.</para>
/// <para><see cref="SubjectHash"/> folds them into one value with the repository's identity, so a
/// caller comparing one string is comparing all of it. It is a hash of ids, not of file contents:
/// it proves WHICH tree was read, and the tree id proves what that tree contained.</para>
/// </remarks>
public sealed record SubjectAttestation(
    string RepoIdentity,
    string BaseRef,
    string BaseSha,
    string HeadSha,
    string TreeSha)
{
    /// <summary>The canonical form's version tag — in the hashed bytes, so it can never drift.</summary>
    public const string Version = "coai/round-subject/v1";

    /// <summary>
    /// The canonical subject hash: SHA-256 over the version tag and the five fields, in order.
    /// </summary>
    /// <remarks>
    /// <para>Newline-separated with the version first, so a reader can reproduce it from this
    /// paragraph alone. The fields are lower-cased hex ids and a repository identity that is already
    /// normalised; nothing here is locale-dependent, and the separator cannot appear inside a
    /// field.</para>
    /// <para>It is computed, never accepted from a caller. A hash somebody else supplied would
    /// attest whatever they wanted it to.</para>
    /// </remarks>
    public string SubjectHash
    {
        get
        {
            var canonical = string.Join('\n', [Version, RepoIdentity, BaseRef, BaseSha, HeadSha, TreeSha]);

            return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
        }
    }

    /// <summary>
    /// Does this describe the same reviewed subject as <paramref name="other"/>?
    /// </summary>
    /// <remarks>
    /// Every field, not the hash alone — the hash is a convenience for a caller, and comparing only
    /// a derived value would make a bug in the derivation invisible to the check that exists to
    /// catch one.
    /// </remarks>
    public bool Matches(SubjectAttestation other) =>
        RepoIdentity == other.RepoIdentity
        && BaseRef == other.BaseRef
        && BaseSha == other.BaseSha
        && HeadSha == other.HeadSha
        && TreeSha == other.TreeSha;

    /// <summary>Every git id resolved and the repository named — an unpinned attestation is none.</summary>
    public bool IsComplete =>
        RepoIdentity.Length > 0 && BaseSha.Length > 0 && HeadSha.Length > 0 && TreeSha.Length > 0;
}

/// <summary>Where a reserved round has got to. The four a caller is allowed to act on.</summary>
public static class RoundLifecycle
{
    /// <summary>Reserved and not yet dispatched. Nothing external has run.</summary>
    public const string Reserved = "reserved";

    /// <summary>Dispatched; reviewers are working, or the process holding them died.</summary>
    public const string Running = "running";

    /// <summary>Finished, with its full answer stored.</summary>
    public const string Completed = "completed";

    /// <summary>Dispatched and provably ended without an answer. Not the same as unknown.</summary>
    public const string Failed = "failed";

    /// <summary>What a read-back reports when no such locator exists HERE.</summary>
    /// <remarks>
    /// Deliberately distinct from <see cref="Reserved"/>. "This server has never heard of that
    /// locator" and "this round was reserved and has not run" are different facts, and only the
    /// second is evidence that nothing was consumed.
    /// </remarks>
    public const string Unknown = "unknown";

    /// <summary>Reserved, but never dispatched — the read-back's name for it.</summary>
    public const string NotStarted = "not_started";

    public static string ReadBackFor(string state) => state switch
    {
        Reserved => NotStarted,
        Running => Running,
        Completed => Completed,
        Failed => Failed,
        _ => Unknown,
    };
}
