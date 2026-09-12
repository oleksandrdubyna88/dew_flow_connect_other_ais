using System.Text.Json;
using CoaiMcp.Core.Findings;

namespace CoaiMcp.Core.Rounds;

/// <summary>The two stages a role can belong to, spelled the way the seed and the settings spell them.</summary>
public static class RoleStages
{
    /// <summary>The plan gate — a document, no checkout, no diff.</summary>
    public const string Plan = "plan";

    /// <summary>The result gate — today a diff, from plan 4 also a document.</summary>
    public const string Result = "result";
}

/// <summary>
/// One review role: what it is called, which stage and kind of work it belongs to, whether it is
/// switched on, and the prompts it can be given — the first of which is its general one.
/// </summary>
/// <param name="Id">
/// A built-in's historical name (<c>Architecture</c>), or a custom role's latin slug. Permanent:
/// settings keys, session files and the rounds database are keyed by it.
/// </param>
/// <param name="Name">What a person sees. Free-form, any script.</param>
/// <param name="Stage"><see cref="RoleStages.Plan"/> or <see cref="RoleStages.Result"/>.</param>
/// <param name="ProgrammingTask">True: the result is a diff. False: a document — plan 4's round.</param>
/// <param name="Prompts">
/// General first. Never empty in a catalogued role — but the record does NOT enforce that, on
/// purpose: what a person writes into <c>COAI_ROLES</c> is validated into a dropped row with a
/// sentence (story A2), and a type that throws on its way in would turn that into an exception
/// nobody can report. Validation lives in the two construction paths — <see cref="RoleCatalog.FromSeed"/>
/// for the shipped seed, <c>Compose</c> for a person's roles.
/// </param>
/// <param name="Active">Whether the role takes part at all. Off is still in the catalog.</param>
/// <param name="BuiltIn">Came out of the seed: not renameable, not deletable, its prompts not removable.</param>
public sealed record RoleDefinition(
    string Id,
    string Name,
    string Stage,
    bool ProgrammingTask,
    IReadOnlyList<PromptChoice> Prompts,
    bool Active = true,
    bool BuiltIn = false)
{
    /// <summary>
    /// The prompt a round gets when nobody chose otherwise — the first one, by position.
    /// </summary>
    /// <remarks>
    /// POSITION is what decides, and <see cref="PromptChoice.Universal"/> is set from it rather
    /// than read: "exactly one default per role" used to be a flag a test had to check and is now
    /// true by construction. The flag survives only while the legacy <c>PromptCatalog</c> path does.
    /// </remarks>
    public PromptChoice General =>
        Prompts is { Count: > 0 }
            ? Prompts[0]
            : throw new InvalidOperationException(
                $"role '{Id}' was built with no prompts — every construction path validates first, so this is a defect in the caller");

    /// <summary>
    /// Which roles run together: a stage and a kind. Roles of the other kind never share a round,
    /// which is why the five-active limit counts per bucket.
    /// </summary>
    public string Bucket => $"{Stage}:{(ProgrammingTask ? "code" : "document")}";
}

/// <summary>The seed file's shape, for the source-generated deserialiser. Internal: the seed is read, never written.</summary>
internal sealed record RoleSeed(IReadOnlyList<SeedRole> Roles);

internal sealed record SeedRole(string Id, string Name, string Stage, bool ProgrammingTask, IReadOnlyList<SeedPrompt> Prompts);

internal sealed record SeedPrompt(string Id, string Label, string Purpose);

/// <summary>
/// Which roles exist and which prompt a given round of one gets.
/// </summary>
/// <remarks>
/// <para>The roles used to be an enum, five constants and a 25-row array — and a hand-typed mirror
/// of the same rows in the extension, held level by a test that regexed this file's predecessor as
/// text. The catalog is DATA now: <c>shared/builtin-roles.json</c> is embedded into this assembly
/// and loaded once as <see cref="Builtin"/>; the extension generates its copy from the same file
/// (story C1); each half's loader is asserted against the file by its own tests.</para>
/// <para>What did not change: a round's prompt is the person's explicit choice for that round, else
/// the role's general prompt — <see cref="ForRound"/> is the rule <c>PromptCatalog.ForRound</c> had,
/// word for word, and the panel's <c>selectedFor</c> is still a claim about it.</para>
/// </remarks>
public sealed class RoleCatalog
{
    public const string PlanRole = "PlanCritique";

    /// <summary>
    /// The conventions pass, a ROLE since 2026-09-08 rather than a prompt the other code roles
    /// could be given — listed first, because a broken written rule is the cheapest finding to act
    /// on and the least arguable.
    /// </summary>
    public const string ConventionsRole = "Conventions";
    public const string ArchitectureRole = "Architecture";
    public const string SecurityRole = "SecurityReliability";
    public const string UxDxRole = "UxDxPerformance";

    /// <summary>The one prompt that judges nothing but the project's own written rules.</summary>
    public const string ConventionsId = "conventions";

    /// <summary>The seed's logical name inside this assembly — see the EmbeddedResource item in CoaiMcp.Core.csproj.</summary>
    internal const string SeedResource = "CoaiMcp.Core.builtin-roles.json";

    /// <summary>The seed, read once from this assembly's manifest resource. Depends on nothing on disk.</summary>
    public static RoleCatalog Builtin { get; } = LoadBuiltin();

    /// <summary>Every role, in the order a round runs them and the panel draws them.</summary>
    public IReadOnlyList<RoleDefinition> Roles { get; init; } = [];

    /// <summary>What composition refused, each as "id: reason" — the panel shows these. Empty for the seed.</summary>
    public IReadOnlyList<string> Dropped { get; init; } = [];

    /// <summary>A catalog over roles that have ALREADY been validated — the seed's, or a composed one.</summary>
    internal static RoleCatalog From(IReadOnlyList<RoleDefinition> roles, IReadOnlyList<string>? dropped = null) =>
        new() { Roles = roles, Dropped = dropped ?? [] };

    /// <summary>The role with this id, matched case-insensitively — what the enum parse used to allow.</summary>
    public RoleDefinition? ById(string id) =>
        Roles.FirstOrDefault(r => string.Equals(r.Id, id, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// The ids of the roles a stage runs, in catalog order: switched on, and of this stage.
    /// </summary>
    /// <remarks>
    /// <para><c>Active</c> is honoured here because it means "takes part at all" — a member that
    /// ignored it would schedule a reviewer the operator switched off. It is not the same switch as
    /// <c>RoleGate.Enabled</c>, which the panel writes per role as <c>COAI_ENABLED_*</c>: that one
    /// is a budget the stage reads, this one is whether the role is in the round's list at all.</para>
    /// <para>Programming roles only, until plan 4 gives a document role a round to run in: a role
    /// stored with <c>programmingTask: false</c> is in the catalog and in no round, and it is not
    /// dropped — it is waiting.</para>
    /// </remarks>
    public IReadOnlyList<string> RolesOf(string stage) =>
        [.. Roles.Where(r => r.Stage == stage && r.ProgrammingTask && r.Active).Select(r => r.Id)];

    /// <summary>A role's prompts, general first; nothing for a role this catalog does not know.</summary>
    public IEnumerable<PromptChoice> For(string roleId) => ById(roleId)?.Prompts ?? [];

    /// <summary>A role's general prompt — what a round uses when nobody has chosen otherwise.</summary>
    public PromptChoice UniversalFor(string roleId) =>
        ById(roleId)?.General
        ?? throw new ArgumentException($"no role called '{roleId}' in this catalog", nameof(roleId));

    /// <summary>The prompt with this id, from whichever role carries it, or null.</summary>
    public PromptChoice? PromptById(string promptId) =>
        Roles.SelectMany(r => r.Prompts).FirstOrDefault(p => p.Id == promptId);

    /// <summary>
    /// The prompt for one round: the person's explicit choice, or the role's general prompt.
    /// </summary>
    /// <param name="round">1-based. Round 0 or less is treated as the first round.</param>
    /// <param name="chosen">
    /// What the panel selected for THIS role, per round — index 0 is round 1. An entry that is
    /// blank or unknown falls through to the general prompt, so a stale setting can never leave a
    /// round with no prompt at all.
    /// </param>
    public PromptChoice ForRound(string roleId, int round, IReadOnlyList<string> chosen)
    {
        var index = Math.Max(round, 1) - 1;
        if (index < chosen.Count && For(roleId).FirstOrDefault(p => p.Id == chosen[index]) is { } picked)
        {
            return picked;
        }

        return UniversalFor(roleId);
    }

    private static RoleCatalog LoadBuiltin()
    {
        using var stream = typeof(RoleCatalog).Assembly.GetManifestResourceStream(SeedResource)
            ?? throw Broken("it is not embedded in this build — check the EmbeddedResource item in CoaiMcp.Core.csproj");
        var seed = JsonSerializer.Deserialize(stream, CoreJsonContext.Default.RoleSeed)
            ?? throw Broken("it parsed to nothing");

        return FromSeed(seed);
    }

    /// <summary>
    /// The seed as a catalog, refused whole if anything about it is unusable.
    /// </summary>
    /// <remarks>
    /// <para>Every refusal here is a broken BUILD: the seed is embedded in this assembly and shipped
    /// by us, so a bad one is a release that should never have been cut — the one class of failure
    /// doctrine 5 still lets throw. What a PERSON writes into <c>COAI_ROLES</c> never reaches this
    /// method: story A2's composition answers that with a dropped row and a sentence, because a
    /// configuration mistake must not stop a round that four other roles could still have run.</para>
    /// <para>Whole rather than row by row, for the same reason: a seed missing a role would quietly
    /// review less than it says, and there is nobody to tell — this code runs before any session
    /// exists.</para>
    /// </remarks>
    internal static RoleCatalog FromSeed(RoleSeed seed)
    {
        var roles = seed.Roles ?? [];
        if (roles.Count == 0)
        {
            throw Broken("it names no roles at all, so every round would open with no reviewers in it");
        }

        foreach (var role in roles)
        {
            MustBeWellFormed(role);
        }

        MustBeUnique([.. roles.Select(r => r.Id)], "role id");
        MustBeUnique([.. roles.SelectMany(r => r.Prompts).Select(p => p.Id)], "prompt id");

        return From([.. roles.Select(Definition)]);
    }

    /// <summary>One row of the seed, checked on its own.</summary>
    private static void MustBeWellFormed(SeedRole role)
    {
        if (role.Prompts is not { Count: > 0 })
        {
            throw Broken($"role '{role.Id}' has no prompts, and a role's first prompt is its general one");
        }

        if (role.Stage is not (RoleStages.Plan or RoleStages.Result))
        {
            throw Broken($"role '{role.Id}' has stage '{role.Stage}', which this build does not know — it would run in no round and say nothing");
        }
    }

    /// <summary>
    /// Ids collide case-insensitively or not at all.
    /// </summary>
    /// <remarks>
    /// <c>ById</c> matches case-insensitively, so two spellings of one role id make the round's role
    /// a coin toss; a prompt id is a file name under <c>&lt;dataDir&gt;/prompts/</c>, so two roles
    /// sharing one would read a single text. Both were raised on this story's own code round.
    /// </remarks>
    private static void MustBeUnique(IReadOnlyList<string> ids, string what)
    {
        if (ids.GroupBy(id => id, StringComparer.OrdinalIgnoreCase).FirstOrDefault(g => g.Count() > 1) is { } clash)
        {
            throw Broken($"the {what} '{clash.Key}' is used more than once ({string.Join(", ", clash)})");
        }
    }

    /// <summary>A validated seed row as a role: every prompt shipped, the first one general, the role switched on.</summary>
    private static RoleDefinition Definition(SeedRole role) => new(
        role.Id,
        role.Name,
        role.Stage,
        role.ProgrammingTask,
        [.. role.Prompts.Select((p, i) => new PromptChoice(p.Id, role.Id, p.Label, p.Purpose, Universal: i == 0, BuiltIn: true))],
        Active: true,
        BuiltIn: true);

    private static InvalidOperationException Broken(string what) =>
        new($"the built-in role seed ('{SeedResource}') is not usable: {what}. "
            + "It is embedded in this binary, so this is a broken build rather than anything a person configured.");
}
