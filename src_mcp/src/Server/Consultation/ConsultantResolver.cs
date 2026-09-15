using CoaiMcp.Runners.Consultation;
using CoaiMcp.Runners.Reviewers;

namespace CoaiMcp.Server;

/// <summary>
/// What a stored consultant entry MEANS: the vendor a consultation runs on, or the sentence it is
/// refused with — decided before anything is built or launched.
/// </summary>
/// <remarks>
/// A closed union rather than a nullable row beside a string, by the family's doctrine: a refusal here
/// is an expected answer with several causes, and each cause is a different sentence a person acts on.
/// </remarks>
public abstract record ResolvedConsultant
{
    /// <summary>A consultant this build can describe in full — every field the launch reads, in one row.</summary>
    public sealed record Definition(ProviderSettings Vendor) : ResolvedConsultant;

    /// <summary>An entry this build cannot place, and the sentence saying what to do about it.</summary>
    public sealed record Unavailable(string Why) : ResolvedConsultant;

    private ResolvedConsultant() { }
}

/// <summary>
/// The one resolution rule, server half — the C# twin of <c>resolveConsultant</c> in
/// <c>src_vs_code/src/consultSettings.ts</c>, which states the same rule for the panel.
/// </summary>
/// <remarks>
/// <para>A stored entry is a DEFINITION (<see cref="ConsultantChoice.Runtime"/> present) or a LEGACY
/// reference (absent — everything written before 2026-09-15, and the shipped pairs). A definition is
/// itself: a <see cref="ProviderSettings"/> built from the entry alone, with <c>settings.Providers</c>
/// never read. That is what makes a consultant's URL, model and CLI path independent of the reviewers
/// — the operator's ruling behind <c>PLAN_the_consultant_has_its_own_vendors</c>, and the move the chat
/// had already made for the same reason. A legacy reference resolves, identically on both halves, in
/// this order:</para>
/// <list type="number">
/// <item><b>(a)</b> a reviewer row with that id, matched case-insensitively — the way this server has
/// always looked the row up — <b>enabled OR disabled</b>: its runtime, its model unless the entry
/// names one, its endpoint, its CLI path. That is the meaning the entry always had, materialised.
/// Disabled counts, and that is the fix for the plan's opening symptom: a reviewer switched off is a
/// fact about REVIEWS, and it was taking the consultant down with it.</item>
/// <item><b>(b)</b> else an id that is itself a consulting runtime
/// (<see cref="ConsultantResolution.Consulting"/>): that runtime under its own name, the entry's model
/// or empty, no endpoint, no CLI path. The rule <see cref="RuntimeResolution.NameOf"/> already applies
/// to a row, applied to the entry — and what makes the shipped <c>codex → claude</c> reach a Claude
/// CLI on a machine with no <c>claude</c> reviewer row, with no write.</item>
/// <item><b>(c)</b> else UNAVAILABLE, refused BY NAME with the allowlist and the section that fixes it
/// — never substituted, because the person chose this one and a call billed to an account nobody
/// picked is the failure <see cref="ConsultantRouting"/>'s own doctrine forbids.</item>
/// </list>
/// <para><b>Materialising is not permitting.</b> Rule (a) hands back whatever runtime the row is on,
/// <c>remote</c> included; whether that runtime may hold a consultation is asked where one is run —
/// <see cref="ConsultantResolution.For"/>, whose refusal <see cref="ConsultantResolution.CannotConsult"/>
/// names it. A DEFINITION is different, and the difference is the security half of story B3: the
/// panel's picker excludes Team servers, but that excludes nothing from the WIRE — a hand-edited or
/// stale settings file can name <c>remote</c> outright, and a working tree must never be routed at a
/// Team server by a consultation. So a definition's runtime is checked against the allowlist HERE,
/// before any <see cref="ProviderSettings"/> exists for it, and the refusal names the caller kind, the
/// vendor, the runtime and the list — the adapter factory's sentence knows no caller. The match is
/// exact: <c>Codex</c> is not a runtime this build has, and on the wire the fail-closed reading of a
/// spelling nobody can launch is a refusal that names it, not a re-classification as legacy.</para>
/// <para><b>The id is kept in (a) and canonicalised in (b), and the asymmetry is the point</b> — the
/// TypeScript's own words. In (a) the id names a ROW a person created; it keys their vault entry and
/// the usage ledger, and a resolution that rewrote it would move a credential. In (b) the id names a
/// RUNTIME, and <c>Claude</c> and <c>claude</c> are the same one; the matched runtime's own name is the
/// id, so it keys the same vault entry and ledger line as every <c>claude</c> the rest of the panel
/// can produce.</para>
/// <para>Pure and static, so every arm is a unit test rather than a live consultation.</para>
/// </remarks>
public static class ConsultantResolver
{
    private const string Section = "the Consultant section of the ConnectOtherAIs panel (COAI_CONSULTANTS)";

    /// <summary>What <paramref name="choice"/> means for a NEW consultation by a <paramref name="callerKind"/> caller.</summary>
    public static ResolvedConsultant Resolve(ConsultantChoice choice, string callerKind, IReadOnlyList<ProviderSettings> rows) =>
        choice.IsDefinition ? Defined(choice, callerKind) : Legacy(choice, callerKind, rows);

    /// <summary>
    /// The vendor a RESUMED consultation runs on: the record's vendor, model and runtime, frozen; the
    /// endpoint and CLI path from whatever describes that vendor id today.
    /// </summary>
    /// <remarks>
    /// <para>The record freezes three things and not five, and the plan puts widening it out of scope:
    /// a base URL and a CLI path are facts about THIS machine's configuration rather than about the
    /// conversation, and a consultation opened before a CLI moved should follow it. So they come from a
    /// current DEFINITION with that vendor id under any caller kind — one id is one vault entry and one
    /// vendor, whichever caller it is configured for — else from the legacy path through the reviewer
    /// rows, else the resumed refusal: a consultation stays on the vendor it started with, so the cure
    /// is a new one.</para>
    /// <para><b>Borrowed only when today's description still runs the RECORD's runtime.</b> A CLI path
    /// belongs to a CLI: had the id <c>codex</c> been redefined onto <c>claude</c> since the
    /// consultation opened, borrowing its path would hand the codex adapter a Claude binary — the
    /// vendor-routing rule's exact failure, invisible in the output. That case is refused naming both
    /// runtimes rather than launched.</para>
    /// </remarks>
    public static ResolvedConsultant Resumed(
        ConsultationRecord record,
        IReadOnlyDictionary<string, ConsultantChoice> consultants,
        IReadOnlyList<ProviderSettings> rows) =>
        Today(record, consultants, rows) switch
        {
            ResolvedConsultant.Definition today when RuntimeResolution.NameOf(today.Vendor.Identity()) == record.Runtime =>
                new ResolvedConsultant.Definition(Frozen(record, today.Vendor)),
            ResolvedConsultant.Definition today => new ResolvedConsultant.Unavailable(MovedRuntime(record, today.Vendor)),
            _ => new ResolvedConsultant.Unavailable(NoLongerConfigured(record)),
        };

    /// <summary>What the record's vendor id means TODAY: a definition under any caller kind first, then the legacy path.</summary>
    /// <remarks>
    /// A definition found here is taken without the allowlist check <see cref="Resolve"/> applies,
    /// because what will RUN is the record's frozen runtime, not the definition's: a definition whose
    /// runtime differs — <c>remote</c> among them — is refused by the runtime comparison above, naming both.
    /// </remarks>
    private static ResolvedConsultant Today(
        ConsultationRecord record,
        IReadOnlyDictionary<string, ConsultantChoice> consultants,
        IReadOnlyList<ProviderSettings> rows) =>
        consultants.Values.FirstOrDefault(choice => choice.IsDefinition && SameId(choice.Vendor, record.Vendor)) is { } defined
            ? new ResolvedConsultant.Definition(AsProvider(defined))
            : Legacy(new ConsultantChoice(record.Vendor, record.Model), record.CallerKind, rows);

    /// <summary>
    /// A definition is itself — once its runtime is one a consultant may run on. The check comes
    /// BEFORE any row exists.
    /// </summary>
    /// <remarks>
    /// <para>Matched WITHOUT case, and the allowlist's own spelling is what the row then carries.
    /// Both halves have to recognise a runtime the same way or they disagree about the same file: the
    /// panel reads a stored runtime through a list of lower-case names, so a capital in
    /// <c>"Codex"</c> made it resolve a consultant that this server then refused. Canonicalising here
    /// also hands <c>RuntimeResolution.NameOf</c> and every adapter one spelling, whatever a person
    /// typed. (gemini and the local reviewer, independently, on B3's plan round.)</para>
    /// <para>A runtime this build has never heard of is still refused, and that residual difference is
    /// deliberate: the panel reads an unknown NAME as an older entry and resolves it by id, which is
    /// how an extension meets a runtime a NEWER one wrote, while this half fails closed — it is the
    /// half that would launch it.</para>
    /// </remarks>
    private static ResolvedConsultant Defined(ConsultantChoice choice, string callerKind) =>
        ConsultantResolution.Consulting.FirstOrDefault(one => SameId(one, choice.Runtime)) is { } runtime
            ? new ResolvedConsultant.Definition(AsProvider(choice with { Runtime = runtime }))
            : new ResolvedConsultant.Unavailable(ForeignRuntime(choice, callerKind));

    private static ResolvedConsultant Legacy(ConsultantChoice choice, string callerKind, IReadOnlyList<ProviderSettings> rows)
    {
        if (rows.FirstOrDefault(row => SameId(row.Provider, choice.Vendor)) is { } row)
        {
            return new ResolvedConsultant.Definition(FromRow(choice, row));
        }

        return ConsultantResolution.Consulting.FirstOrDefault(runtime => SameId(runtime, choice.Vendor)) is { } runtime
            ? new ResolvedConsultant.Definition(FromRuntime(choice, runtime))
            : new ResolvedConsultant.Unavailable(NothingNamed(choice, callerKind));
    }

    /// <summary>A definition as the row the launch reads — the entry's own fields and nothing borrowed.</summary>
    private static ProviderSettings AsProvider(ConsultantChoice choice) => new(choice.Vendor)
    {
        Runtime = choice.Runtime,
        Model = choice.Model,
        BaseUrl = choice.BaseUrl,
        ExecutablePath = choice.ExecutablePath,
        Enabled = true,
    };

    /// <summary>Rule (a): the row's runtime, endpoint and CLI path — its model only where the entry names none — under the entry's own id, enabled or not.</summary>
    private static ProviderSettings FromRow(ConsultantChoice choice, ProviderSettings row) => new(choice.Vendor)
    {
        Runtime = row.Runtime,
        Model = choice.Model.Length > 0 ? choice.Model : row.Model,
        BaseUrl = row.BaseUrl,
        ExecutablePath = row.ExecutablePath,
        Enabled = true,
    };

    /// <summary>Rule (b): the runtime the id names, under that runtime's own name, borrowing nothing.</summary>
    private static ProviderSettings FromRuntime(ConsultantChoice choice, string runtime) => new(runtime)
    {
        Runtime = runtime,
        Model = choice.Model,
        Enabled = true,
    };

    /// <summary>The record's three frozen facts over today's endpoint and CLI path.</summary>
    private static ProviderSettings Frozen(ConsultationRecord record, ProviderSettings today) => new(record.Vendor)
    {
        Runtime = record.Runtime,
        Model = record.Model,
        BaseUrl = today.BaseUrl,
        ExecutablePath = today.ExecutablePath,
        Enabled = true,
    };

    private static bool SameId(string one, string other) =>
        string.Equals(one, other, StringComparison.OrdinalIgnoreCase);

    private static string Allowlist => string.Join(", ", ConsultantResolution.Consulting);

    // ---------- the sentences ----------

    /// <summary>A definition on a runtime outside the allowlist: who, what, on which runtime, and what may run instead. Nothing was built for it.</summary>
    private static string ForeignRuntime(ConsultantChoice choice, string callerKind) =>
        $"the consultant for a '{callerKind}' caller is the vendor '{choice.Vendor}' on the runtime '{choice.Runtime}', "
        + $"which a consultant may not run on — consultants run on: {Allowlist}. Nothing was built or launched; "
        + $"pick one of those for this caller in {Section}";

    /// <summary>Rule (c): what the entry is, why nothing can run it, and the two ways to fix that.</summary>
    private static string NothingNamed(ConsultantChoice choice, string callerKind) =>
        $"the consultant for a '{callerKind}' caller is '{choice.Vendor}', which names no reviewer and is not a runtime "
        + $"this build can consult with ({Allowlist}) — choose another consultant for this caller in {Section}, "
        + "or add a reviewer under that name";

    /// <summary>A resumed consultation whose vendor nothing describes any more. The cure is a new one.</summary>
    private static string NoLongerConfigured(ConsultationRecord record) =>
        $"consultation {record.Id} was opened on the vendor '{record.Vendor}', which is no longer configured — "
        + "a consultation stays on the vendor it started with, so this one cannot go on; start a new consultation";

    /// <summary>A resumed consultation whose vendor id now runs on another CLI — refused rather than launched on the wrong binary.</summary>
    private static string MovedRuntime(ConsultationRecord record, ProviderSettings today) =>
        $"consultation {record.Id} was opened on the vendor '{record.Vendor}' running on '{record.Runtime}', "
        + $"which is now configured to run on '{RuntimeResolution.NameOf(today.Identity())}' — a consultation stays on the "
        + "runtime it started with, so this one cannot go on; start a new consultation";
}
