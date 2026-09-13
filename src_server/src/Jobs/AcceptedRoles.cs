using System.Text.RegularExpressions;
using CoaiMcp.Core.Rounds;

namespace CoaiServer;

/// <summary>
/// Which review roles this server will run — the shipped five, whatever an operator added, and the
/// one place that decides.
/// </summary>
/// <remarks>
/// <para><b>Why it is a type rather than two checks.</b> <c>RoleCatalog.Builtin.Roles</c> was
/// enumerated in TWO places that answer the same question in their own words — the unknown-role
/// refusal in <c>ReviewEndpoints</c> and the missing-role refusal in <c>JobKinds</c>. A configured
/// list would have written the third. Plan 2 shipped with three independent counts of "which code
/// roles will run", and the one nobody updated promised four reviewers under a page drawing five
/// boxes; this is the same shape, refused before it was built.</para>
/// <para><b>Nothing here knows about HTTP.</b> Every rule is reachable from a test without a host:
/// the shape of an id, its length, what an operator may add, what "any" means, what a refusal says,
/// and which spelling is recorded.</para>
/// <para><b>It is the BOUNDARY, not a convenience.</b> The client applies the same rules so it never
/// offers an id this server would refuse, but a client is not a boundary — anything reaching the
/// endpoints is checked here. (local and codex, the plan round.)</para>
/// </remarks>
public sealed partial class AcceptedRoles
{
    /// <summary>
    /// The longest id this server will accept.
    /// </summary>
    /// <remarks>
    /// The same 48 the extension's <c>MAX_ROLE_ID_LENGTH</c> applies, so both halves refuse the same
    /// ids. <b>The shape rule bounds the alphabet and not the length</b>, so without this a 4 KB
    /// alphanumeric name would pass it and reach <c>JobRecord</c>, the idempotency fingerprint and an
    /// append-only ledger. Like the extension's, it is a POLICY bound rather than a probed platform
    /// number: POSIX sets no maximum on a variable name and Windows allows far more, so there is no
    /// single number to measure — this is one chosen comfortably under all of them. (codex, the plan
    /// round.)
    /// </remarks>
    public const int MaxIdLength = 48;

    /// <summary>
    /// A role id becomes the environment variable <c>COAI_ROUNDS_&lt;ID&gt;</c> — latin, starting with
    /// a letter, and no hyphen.
    /// </summary>
    /// <remarks>
    /// The same expression <see cref="RoleComposition"/> enforces on the client, for the same reason.
    /// <c>COAI_ROUNDS_MY-ROLE</c> is not a name a POSIX shell can export, so a role called
    /// <c>my-role</c> would take its budget from a settings file and never from the environment —
    /// working in one of the two places its settings can come from, which is worse than neither.
    /// <para><b>Anchored <c>\A…\z</c>, not <c>^…$</c>.</b> In .NET <c>$</c> matches at the end of the
    /// string OR immediately before a trailing newline, so <c>^[A-Za-z][A-Za-z0-9_]*$</c> ACCEPTS
    /// <c>"Requirements\n"</c> — an id carrying a line break into an environment variable name, a
    /// ledger key and a log line. Caught by this type's own test before it shipped; the client's copy
    /// of the expression has the same hole and is recorded in the plan's open tail.</para>
    /// </remarks>
    [GeneratedRegex(@"\A[A-Za-z][A-Za-z0-9_]*\z")]
    private static partial Regex RoleId { get; }

    /// <summary>
    /// Every accepted role, keyed by its own spelling under an ignore-case comparer.
    /// </summary>
    /// <remarks>
    /// The comparer does the folding, so <see cref="Knows"/> is a lookup with no allocation at all —
    /// it used to lower-case the id on every call, on the request path. (gemini, this story's code
    /// round.)
    /// </remarks>
    private readonly Dictionary<string, string> _spelling;

    /// <summary>Whether any well-formed id is accepted, whoever named it.</summary>
    public bool AllowAny { get; }

    /// <summary>What every refusal message lists, in the order a person configured them.</summary>
    public IReadOnlyList<string> Names { get; }

    private AcceptedRoles(Dictionary<string, string> spelling, List<string> names, bool allowAny)
    {
        _spelling = spelling;
        // A SNAPSHOT, not the list itself: `Names` is about to be served on the catalog endpoint, and
        // a consumer that downcast it to `List<string>` and edited it would make this server advertise
        // a role `Knows` refuses. (codex, this story's code round.)
        Names = names.AsReadOnly();
        AllowAny = allowAny;
    }

    /// <summary>
    /// This server's accepted roles, from <c>Coai:ExtraRoles</c> and <c>Coai:AllowAnyRole</c>.
    /// </summary>
    /// <remarks>
    /// <para><b>Throws on a configured id that could never run</b>, which is why it is called from
    /// startup. An operator who writes <c>My-Role</c> learns at boot, in a message naming the entry
    /// and the rule — not on every request, where nobody is looking. A server whose configuration
    /// would be refused by its own endpoints is misconfigured, and a misconfigured server must not
    /// start quietly. It is the shape <c>Coai:AllowedDomains</c> and <c>Coai:SessionTtlDays</c>
    /// already have. (local, the plan round, Blocking.)</para>
    /// <para>An EMPTY list is not an error, and that is the one place the domain precedent is
    /// deliberately not followed: an empty domain list is a server anyone may use, while an empty
    /// extra-role list is a server running the five it always ran. The first is a hole; the second
    /// is the status quo.</para>
    /// </remarks>
    public static AcceptedRoles From(IEnumerable<string> extra, bool allowAny)
    {
        var spelling = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var names = new List<string>();
        foreach (var role in RoleCatalog.Builtin.Roles)
        {
            spelling[role.Id] = role.Id;
            names.Add(role.Id);
        }

        // The SHIPPED spelling wins, deliberately. An operator who writes `architecture` into
        // `Coai:ExtraRoles` has named a role this product already ships, and a built-in's identity is
        // fixed — its id keys settings, open sessions and every recorded round, which is the rule
        // plan 2 settled on the page. So the entry is accepted (it names a role this server runs) and
        // the catalog's spelling is what gets recorded. The same first-wins rule covers two entries
        // that differ only in case. Both are pinned by tests rather than left to be discovered.
        // (local and gemini, this story's code round, on the silence rather than the behaviour.)
        foreach (var id in extra)
        {
            Guard(id);
            if (spelling.TryAdd(id, id))
            {
                names.Add(id);
            }
        }

        return new AcceptedRoles(spelling, names, allowAny);
    }

    private static void Guard(string id)
    {
        if (WellFormed(id))
        {
            return;
        }

        throw new InvalidOperationException(
            $"Coai:ExtraRoles contains '{id}', which is not a role id. {ShapeRule} "
            + "Every request naming it would be refused, so this server will not start with it.");
    }

    /// <summary>The one sentence that explains the shape, wherever it has to be explained.</summary>
    private const string ShapeRule =
        "A role id is latin, starts with a letter, carries no hyphen, and is at most "
        + "48 characters — it becomes the environment variable COAI_ROUNDS_<ID>.";

    /// <summary>Whether this could be a role id at all, before asking whether it is one of ours.</summary>
    /// <remarks>
    /// Takes what a caller SENT and trims it — see <see cref="Said"/> for why the ends are forgiven
    /// and the middle is not.
    /// </remarks>
    public static bool WellFormed(string? id) =>
        Shaped(Said(id));

    private static bool Shaped(string id) =>
        id.Length > 0 && id.Length <= MaxIdLength && RoleId.IsMatch(id);

    /// <summary>
    /// What a caller meant by the role they sent: trimmed, and empty when they meant nothing.
    /// </summary>
    /// <remarks>
    /// <para>Every other field at this boundary is already read this way — <c>JobKinds.Named</c>
    /// trims the kind, and the endpoint trims the idempotency key. The role was the one that was not,
    /// so <c>" Architecture "</c> was refused by the SHAPE rule and told that a role id is latin and
    /// carries no hyphen, which is not what was wrong with it, while the same name without the spaces
    /// was accepted. Three inputs at one boundary, two of them forgiving. (gemini, story 2's second
    /// code round.)</para>
    /// <para><b>The ends only.</b> A space in the middle is an id that could never be an environment
    /// variable, and it stays refused — trimming is forgiveness for a stray keystroke, not a licence
    /// to rewrite what somebody sent.</para>
    /// </remarks>
    private static string Said(string? raw) => raw?.Trim() ?? string.Empty;

    /// <summary>Whether this server will run a role by this name.</summary>
    /// <remarks>
    /// Case-insensitive, and that is load-bearing rather than polite: a client that lower-cases its
    /// roles is a client, and the canonicalisation that reconciles two spellings never runs if
    /// membership refuses one of them first. (gemini, the plan round.)
    /// </remarks>
    public bool Knows(string? id) =>
        WellFormed(id) && (AllowAny || _spelling.ContainsKey(Said(id)));

    /// <summary>
    /// The spelling to RECORD for a role somebody named.
    /// </summary>
    /// <remarks>
    /// <para>A recorded role is a KEY — of a ledger row, of a usage total, of a line in a log — and a
    /// key's job is to be the same key. Passing a spelling through verbatim is how one role becomes
    /// two rows the day two clients disagree about it, in a ledger that is append-only.</para>
    /// <para>Three answers, in this order. A role this server KNOWS the spelling of takes that
    /// spelling — the catalog's for a built-in, the operator's for a configured one. Under
    /// <see cref="AllowAny"/> there is no configured spelling, so it is FOLDED: deterministic,
    /// needing no configuration, and the same on every server, which is what makes two clients
    /// agree. Anything else is handed back untouched, because it is about to be refused and a
    /// refusal must name the id that was actually sent.</para>
    /// <para>The first draft of this plan wrote the ledger split down as an acceptable price and put
    /// the question to the operator. Three reviewers refused that independently, and one of them
    /// named this answer.</para>
    /// </remarks>
    public string Canonical(string? raw)
    {
        // NOTHING is nothing, however it was spelled. A client omitting the field and one sending
        // three spaces mean the same thing, and returning "   " from here put whitespace into
        // `JobRecord.Role` and into the idempotency fingerprint — so the same review, sent by the
        // same person, was two jobs depending on which of the two ways they said nothing. Quoting
        // the input back is `Refusal`'s job, not this one's. (gemini, story 2's code round.)
        var said = Said(raw);
        if (said.Length == 0)
        {
            return string.Empty;
        }

        // Shape next, and it is not only tidiness: a malformed 4 KB id is about to be refused, and
        // folding it for a lookup that cannot match allocates a 4 KB copy per request on input
        // nobody here controls. (codex, story 1's code round.)
        if (!Shaped(said))
        {
            return said;
        }

        return _spelling.TryGetValue(said, out var known) ? known
            : AllowAny ? Fold(said)
            : said;
    }

    /// <summary>
    /// Why this role will not run here — or nothing, when it will.
    /// </summary>
    /// <remarks>
    /// <b>Two different wrongs, two different sentences.</b> An id that is not an id gets the rule it
    /// broke; a well-formed id this server does not know gets the list. Answering a shape failure
    /// with "Allowed: PlanCritique, Conventions, …" under <see cref="AllowAny"/> would tell an
    /// operator the server accepts five roles when it accepts any — the opposite of what is wrong
    /// with their request. (gemini and local, the plan round.)
    /// </remarks>
    public string? Refusal(string? raw)
    {
        // Missing is not malformed. "'' is not a role id" tells somebody who sent no role that the
        // empty string was rejected, which is true and useless. (gemini, this story's code round.)
        var said = Said(raw);
        if (said.Length == 0)
        {
            return $"a review needs a role. Accepted: {string.Join(", ", Names)}";
        }
        if (!Shaped(said))
        {
            return $"'{said}' is not a role id. {ShapeRule}";
        }

        return Knows(said) ? null : $"'{said}' is not a review role here. Accepted: {string.Join(", ", Names)}";
    }

    /// <summary>One casing, chosen once, so two spellings of one role meet.</summary>
    private static string Fold(string id) => id.ToLowerInvariant();
}
