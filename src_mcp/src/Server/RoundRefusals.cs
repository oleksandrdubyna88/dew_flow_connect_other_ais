using CoaiMcp.Core.Rounds;

namespace CoaiMcp.Server;

/// <summary>
/// What a round says when it will not run, and why — every sentence a person reads instead of a
/// review.
/// </summary>
/// <remarks>
/// <para>Lifted out of <c>PanelService</c>, which had grown past 2,500 lines. These belong together
/// and belong with nothing else: they read ONE thing — the catalog — and they produce prose. Nothing
/// here starts a process, touches a disk or holds session state, which is what made this the first
/// cut to take.</para>
/// <para>They are the product's voice at the moment it disappoints somebody, so they are written to
/// be acted on rather than merely to be true: each names the thing that is wrong, in the words the
/// person used for it, and then what to do about it. Several were rewritten by the gate for claiming
/// a cause the round did not actually know.</para>
/// </remarks>
/// <param name="Catalog">The roles this session has — the shipped ones plus whatever was configured.</param>
public sealed record RoundRefusals(RoleCatalog Catalog)
{
    /// <summary>Every code-review role is switched off.</summary>
    public string NoCodeRoles => NoRoles(Stage.CodeReview);

    /// <summary>
    /// The same sentence for any stage, naming that stage's own roles.
    /// </summary>
    /// <remarks>
    /// The plan stage needs it since its roster started coming from the catalog: the shipped plan
    /// role honours no <c>COAI_ENABLED_</c> key, but a <c>COAI_ROLES</c> row saying
    /// <c>active: false</c> switches it off like any other — and a person who does that with no plan
    /// role of their own would otherwise get a round with nothing in it, which the session counts as
    /// unresolved and never lets them retry.
    /// </remarks>
    public string NoRoles(Stage stage) =>
        $"Every {ReviewKindOf(stage)}-review role is switched off, so this "
        + "round would have no reviewers in it. "
        + $"Tick at least one of {Named(Tickable(stage))} "
        + "in the panel — or clear the matching COAI_ENABLED_<ROLE> variable — and ask again.";

    /// <summary>
    /// A round whose whole roster was filtered out — no vendor here can run any of it.
    /// </summary>
    /// <remarks>
    /// <para>This path returns before any summary is built, so whatever the round decided on the way
    /// here is said HERE or nowhere. It carried the roles it could not ASK — a prompt with no text —
    /// from the start; it did not carry the roles a vendor could not TAKE, and those are the ones
    /// that empty a round whose configuration is otherwise perfect: every vendor a Team server,
    /// every scheduled role one a person defined. They were then told to check their vendors, which
    /// are fine. (CodeRabbit, plan 3's pull request.)</para>
    /// <para>Both clauses name the ROLE first, because that is what somebody reading a round with
    /// nothing in it is trying to find.</para>
    /// <para><b>And the vendor advice is a CHECK, not a diagnosis.</b> The lead sentence used to
    /// assert it — "every configured vendor is either disabled, set not to review this stage, or
    /// missing its CLI or key" — which is one of the two causes and the wrong one exactly when a
    /// clause below is present: a healthy Team server, serving this stage, rejecting one role a
    /// person defined. It is last and conditional in wording now, so it stays actionable without
    /// claiming something the round does not know. (CodeRabbit, on the fix for its own earlier
    /// finding.)</para>
    /// </remarks>
    public string NoReviewer(
        Stage stage,
        IEnumerable<string> notAsked,
        IEnumerable<string> excluded) =>
        $"nothing could review the {stage} stage: no vendor here can run any of the roles this round "
        + "was going to ask. A round with no reviewer would pass the gate having reviewed nothing, so "
        + "it is refused."
        + Clause(" Before that, ", notAsked)
        + Clause(" And ", excluded)
        + " Otherwise every configured vendor is disabled, set not to review this stage, or missing "
        + "its CLI or key: tick a vendor's stage box in the panel, or enable one that can run.";

    /// <summary>A clause joining what a list holds, or nothing at all when it holds nothing.</summary>
    public static string Clause(string lead, IEnumerable<string> parts) =>
        parts.ToList() is { Count: > 0 } said ? $"{lead}{string.Join("; ", said)}." : string.Empty;

    /// <summary>Which KIND of review a stage runs, as one word inside a refusal.</summary>
    /// <remarks>
    /// Not <c>RoundSubject.StageName</c>, which renders a stage as a phrase for a person reading the
    /// rounds log ("code review"). Two names one letter apart for two different jobs is what the
    /// earlier spelling of this was called out for; this is the adjective in "every code-review role".
    /// Read off the stage's own row rather than a switch with a discard, so a stage added to the
    /// enum cannot be called "code" by omission (§9.4 of the feature-review plan).
    /// </remarks>
    public static string ReviewKindOf(Stage stage) => Stages.Of(stage).Kind;

    /// <summary>
    /// The roles a refusal may offer: the stage's OWN bucket, switched on or NOT.
    /// </summary>
    /// <remarks>
    /// Deliberately not the ACTIVE ones — in the only state this sentence is ever read, that can be
    /// the empty list, and "tick at least one of a role" is not an instruction. It offers only the
    /// stage's own bucket for the opposite reason: ticking a role of another kind cannot satisfy this
    /// round, so offering it would leave somebody exactly as blocked as before, having done what they
    /// were told. (codex, on the code round of the story that removed the enum — and the reason this
    /// reads the bucket rather than the stage since plan 4 gave a document role a round of its own to
    /// be ticked for.)
    /// </remarks>
    public IReadOnlyList<string> Tickable(Stage stage) =>
        [.. Catalog.Roles.Where(r => r.Bucket == PanelConfig.BucketFor(stage)).Select(r => r.Id)];

    /// <summary>Role ids as a person reads them: their display names, in an English list.</summary>
    public string Named(IReadOnlyList<string> roleIds)
    {
        var names = roleIds.Select(id => Catalog.ById(id)?.Name ?? id).ToList();

        return names.Count switch
        {
            0 => "a role",
            1 => names[0],
            _ => $"{string.Join(", ", names[..^1])} or {names[^1]}",
        };
    }
}
