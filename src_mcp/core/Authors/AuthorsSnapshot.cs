using System.Collections.Immutable;
using System.Text.RegularExpressions;

namespace CoaiMcp.Core.Authors;

/// <summary>
/// What a round records about who wrote the code it reviewed — the text-free snapshot kept on <c>RoundRecord.Authors</c> in the
/// session file (E5.S1 of <c>todo/PLAN_a_round_records_who_wrote_the_code_and_how_much_E5.md</c>). Ids, closed words, counts and
/// tokens; never a byte of what a transcript said.
/// </summary>
/// <remarks>
/// <para>The first twelve members are E4's <see cref="RoundAuthors"/>, as E4 prints them (<c>Session</c> is its
/// <c>CallerSessionId</c>); every member of that record except <c>WritesOutsideTheGateCheckout</c> — which carries checkout ROOTS, and a
/// root must never reach a file — is here by name, and a reflection fact fails when E4 adds one the snapshot lacks. The last five are
/// E5's own: where the id came from, the two versions that produced it, and the two write counts.</para>
/// <para><b>An unknown is <c>-1</c>, never <c>0</c>.</b> <see cref="ActivityTotals.Unknown"/> and every count nothing measured read
/// <c>-1</c>, so no round whose attribution did not run reads as a measured zero; <see cref="Unknown"/> is the one factory for a
/// snapshot E5 builds without E4. <see cref="Reader"/> and <see cref="Attribution"/> are <c>0</c> when that component did not run.</para>
/// <para><c>Rows</c>, <c>Reasons</c>, <c>Windows</c> and <c>PeerGates</c> are normalised to empty inside the snapshot; the snapshot
/// ITSELF is nullable on the round and is never normalised there — a null snapshot is <i>not recorded</i>.</para>
/// </remarks>
/// <param name="Session">The caller's session id as E3's ladder resolved it; empty when none.</param>
/// <param name="State"><c>observed</c> | <c>partial</c> | <c>unobserved</c> — the roll-up.</param>
/// <param name="IdSource"><c>registry</c> | <c>environment</c> | <c>stored</c> | <c>healed</c> — kept as the column keeps it, a string.</param>
/// <param name="Reader">E3's <c>ObservedSession.ReaderVersion</c>, or <c>0</c> when the reader produced no observation.</param>
/// <param name="Attribution">E4's <c>RoundAttribution.Version</c>, or <c>0</c> when <c>Assign</c> did not run.</param>
/// <param name="SubmoduleWrites">Writes into a registered submodule of the gate's checkout; <c>-1</c> = unknown.</param>
/// <param name="UncoveredWrites">Every write into another checkout, submodules included; <c>-1</c> = unknown.</param>
public sealed partial record AuthorsSnapshot(
    string Stage,
    int Number,
    string Session,
    string State,
    ImmutableArray<string> Reasons,
    ImmutableArray<AuthorRow> Rows,
    ActivityTotals Ambiguous,
    ActivityTotals Elsewhere,
    ActivityTotals ClaimedByPeer,
    int UnobservedLaunches,
    ImmutableArray<WindowAuthors> Windows,
    ImmutableArray<string> PeerGates,
    string IdSource,
    int Reader,
    int Attribution,
    int SubmoduleWrites,
    int UncoveredWrites)
{
    /// <summary>Normalised to empty, never default: a snapshot from a file that lacks the member reads an empty list, not a default array.</summary>
    public ImmutableArray<string> Reasons { get; init => field = Normal(value); } = Normal(Reasons);

    /// <inheritdoc cref="Reasons"/>
    public ImmutableArray<AuthorRow> Rows { get; init => field = Normal(value); } = Normal(Rows);

    /// <inheritdoc cref="Reasons"/>
    public ImmutableArray<WindowAuthors> Windows { get; init => field = Normal(value); } = Normal(Windows);

    /// <inheritdoc cref="Reasons"/>
    public ImmutableArray<string> PeerGates { get; init => field = Normal(value); } = Normal(PeerGates);

    private static ImmutableArray<T> Normal<T>(ImmutableArray<T> value) => value.IsDefault ? [] : value;

    /// <summary>The count of something nothing measured.</summary>
    public const int NotMeasured = -1;

    /// <summary>E5's own reason: E4 refused the input (<c>Assign</c> threw <see cref="ArgumentException"/>).</summary>
    public const string InvalidInput = "invalid input";

    /// <summary>E5's own reason: the reader or the attribution failed with anything else.</summary>
    public const string ReaderFailed = "reader failed";

    /// <summary>E4's result, as E4 prints it, with E5's five members.</summary>
    public static AuthorsSnapshot From(RoundAuthors authors, string idSource, int reader, int attribution, int submoduleWrites, int uncoveredWrites) =>
        new(authors.Stage, authors.Number, authors.CallerSessionId, authors.State, authors.Reasons, authors.Rows, authors.Ambiguous, authors.Elsewhere,
            authors.ClaimedByPeer, authors.UnobservedLaunches, authors.Windows, authors.PeerGates, idSource, reader, attribution, submoduleWrites, uncoveredWrites);

    /// <summary>
    /// The one constructor for a snapshot E5 builds WITHOUT E4. Every member no attribution measured reads its unknown value; pass
    /// <c>0</c> for <paramref name="reader"/> or <paramref name="attribution"/> when that component did not run.
    /// </summary>
    public static AuthorsSnapshot Unknown(
        string stage, int number, string session, string state, ImmutableArray<string> reasons, string idSource, int reader, int attribution) =>
        new(stage, number, session, state, reasons, [], ActivityTotals.Unknown, ActivityTotals.Unknown, ActivityTotals.Unknown, NotMeasured, [], [], idSource, reader, attribution, NotMeasured, NotMeasured);

    /// <summary>The column and card text: the state, then its reasons joined by <c>", "</c>.</summary>
    public string Render() => Reasons.Length == 0 ? State : State + ": " + string.Join(Joiner, Reasons);

    /// <summary>
    /// Whether a stored state is one E5 could have written — the WHOLE rule, written once: exactly <c>observed</c>, or <c>partial: </c> /
    /// <c>unobserved: </c> followed by one or more reasons joined by <c>", "</c>, pairwise distinct, each a lower-case phrase of letters,
    /// spaces and hyphens that starts and ends with a letter. A count (<c>×2</c>, any digit), a colon, a comma inside a reason, an empty
    /// reason and a repeated reason are refused: E4's reasons are distinct and count-free, so any of those is a state somebody else
    /// wrote, read as NOT RECORDED and never as a variant of one that was. The extension applies the same rule to the same vectors
    /// (<c>shared/authors-state-vectors.json</c>).
    /// </summary>
    public static bool IsWellFormed(string stored)
    {
        if (!StateRule().IsMatch(stored))
        {
            return false;
        }

        var at = stored.IndexOf(": ", StringComparison.Ordinal);
        var reasons = at < 0 ? [] : stored[(at + 2)..].Split(Joiner);

        return reasons.Distinct(StringComparer.Ordinal).Count() == reasons.Length;
    }

    private const string Joiner = ", ";

    /// <summary>One reason is <c>[a-z](?:[a-z -]*[a-z])?</c>; the end anchor is <c>\z</c>, so a trailing newline is refused too.</summary>
    [GeneratedRegex(@"^(?:observed|(?:partial|unobserved): [a-z](?:[a-z -]*[a-z])?(?:, [a-z](?:[a-z -]*[a-z])?)*)\z", RegexOptions.CultureInvariant)]
    private static partial Regex StateRule();
}
