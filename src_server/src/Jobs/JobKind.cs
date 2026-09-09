using CoaiMcp.Runners.Reviewers;

namespace CoaiServer;

/// <summary>
/// What a job IS, and the rules that decide whether a submission said so coherently.
/// </summary>
/// <remarks>
/// <para>Pure, and its own file, because the interesting part is a TABLE — six rows of
/// kind-against-role — and a table belongs somewhere it can be tested row by row rather than
/// discovered inside an endpoint.</para>
/// <para><b>Why the field is nullable rather than defaulted to <c>review</c>.</b> That is the whole
/// design. It is the only way to tell <i>an old client that said nothing</i> from <i>a new client
/// that said review</i>, and the two must be treated differently: the first is every copy of the
/// extension and the shim already installed, and it has to keep working; the second is a client that
/// made a claim, and a claim can be checked. Defaulting would collapse them and refuse the field.
/// </para>
/// <para><b>A chat exists because the alternative was a role that cannot review.</b> A
/// <see cref="ReviewRole"/> carries a shipped prompt, a threshold and a round budget, and a test in
/// <c>src_mcp</c> walks every value of that enum asserting each one asks for an honest empty findings
/// list. So the server refuses <c>role: "Chat"</c> — correctly — and what it accepted instead was a
/// job with no role at all. That worked, and it was an ABSENCE being read as a statement: the day
/// somebody tightened the role check, every chat on every machine would have stopped working with a
/// message about roles. This field is that statement, said out loud.</para>
/// </remarks>
public enum JobKind
{
    /// <summary>A reviewer running a role against a diff. What this server was built for.</summary>
    Review,

    /// <summary>One person asking one question about one passage. No role, no findings, no verdict.</summary>
    Chat,
}

/// <summary>Reading and checking the <c>kind</c> a caller sent.</summary>
public static class JobKinds
{
    /// <summary>What a line written before this field existed is counted as.</summary>
    /// <remarks>
    /// Every job this server ran before the field was added was a review — there was nothing else to
    /// be. So an absent kind folds as <see cref="JobKind.Review"/> in the ledger and in every total,
    /// which is the truth about those lines rather than a convenience.
    /// </remarks>
    public const JobKind WhenNotSaid = JobKind.Review;

    /// <summary>The wire spelling of a kind. Lower case, because that is what every client sends.</summary>
    public static string Wire(JobKind kind) => kind.ToString().ToLowerInvariant();

    /// <summary>
    /// The kind a caller named, or null when they named nothing.
    /// </summary>
    /// <remarks>
    /// Trimmed and case-insensitive, exactly as the role check beside it already is: a client sending
    /// <c>"Chat"</c> or <c>"chat "</c> meant the same thing, and refusing it would be a rule about
    /// spelling wearing a contract's clothes. Whitespace alone is a client saying nothing — the same
    /// reading the usage window already gives an empty query parameter. (gemini, plan round.)
    /// </remarks>
    public static bool TryRead(string? said, out JobKind kind)
    {
        var found = Named(said);
        kind = found ?? WhenNotSaid;

        return found is not null || string.IsNullOrWhiteSpace(said);
    }

    /// <summary>The kind spelled by this name, or null when no kind is spelled that way.</summary>
    /// <remarks>
    /// The NAMES, matched explicitly. <c>Enum.TryParse</c> also accepts the underlying numbers, so
    /// <c>kind: "1"</c> would have arrived as a chat and <c>kind: "0"</c> as a review — values no
    /// contract mentions, from a caller who cannot have meant them, deciding what a spending row
    /// says. (codex, code round.)
    /// <para>Its own function because <see cref="TryRead"/> has an <c>out</c> parameter, which cannot
    /// be assigned from inside a lambda; returning a nullable instead is what lets the search be one
    /// expression rather than a loop. (SonarCloud S3267.)</para>
    /// </remarks>
    private static JobKind? Named(string? said) =>
        string.IsNullOrWhiteSpace(said)
            ? null
            : Enum.GetValues<JobKind>()
                .Cast<JobKind?>()
                .FirstOrDefault(k => Wire(k!.Value).Equals(said.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>Whether the caller said anything at all about what this job is.</summary>
    public static bool WasSaid(string? said) => !string.IsNullOrWhiteSpace(said);

    /// <summary>
    /// Why this kind and this role cannot both be true, or null when they can.
    /// </summary>
    /// <remarks>
    /// The whole table, in one place:
    /// <list type="table">
    ///   <item><term>nothing said</term><description>a review, role optional — an OLD client, and it
    ///     must keep working. This is the row the whole nullable design exists for.</description></item>
    ///   <item><term><c>review</c> + a role</term><description>accepted.</description></item>
    ///   <item><term><c>review</c> + no role</term><description>refused, naming the roles. A client
    ///     that says it is sending a review has made a claim, and a review is its role.</description></item>
    ///   <item><term><c>chat</c> + no role</term><description>accepted.</description></item>
    ///   <item><term><c>chat</c> + a role</term><description>refused. A chat carries no role, and
    ///     quietly dropping one is how a field comes to mean something else.</description></item>
    ///   <item><term>anything else</term><description>refused, naming both kinds — the same shape the
    ///     unknown-role refusal already has.</description></item>
    /// </list>
    /// </remarks>
    public static string? Refusal(string? saidKind, string? saidRole)
    {
        if (!TryRead(saidKind, out var kind))
        {
            return $"'{saidKind}' is not a kind of job. Allowed: "
                + string.Join(", ", Enum.GetValues<JobKind>().Select(Wire));
        }

        var hasRole = !string.IsNullOrWhiteSpace(saidRole);
        if (!WasSaid(saidKind))
        {
            // Said nothing: a client older than this field. Judged exactly as it was before it existed.
            return null;
        }

        return (kind, hasRole) switch
        {
            (JobKind.Chat, true) =>
                $"a chat carries no review role, and this one carries '{saidRole}'. Send kind "
                + $"'{Wire(JobKind.Chat)}' with no role, or drop the kind to send a review.",
            (JobKind.Review, false) =>
                $"a job sent as kind '{Wire(JobKind.Review)}' needs a role. Allowed: "
                + string.Join(", ", Enum.GetNames<ReviewRole>()),
            _ => null,
        };
    }
}
