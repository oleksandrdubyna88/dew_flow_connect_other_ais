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

    public static bool IsKnown(string stage) => stage is Plan or Result;
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
/// <param name="Prompts">Never empty; <c>[0]</c> is the general prompt. Enforced here, not by a caller.</param>
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
    /// <remarks>
    /// The throw lives on the record so that <c>General</c> can never be an
    /// <c>IndexOutOfRangeException</c> reached from configuration: the seed loader and the
    /// composition validate before they construct, and anything else that constructs one with no
    /// prompts is a programming error, which is what an exception is for. Raised on the plan
    /// round by two reviewers independently.
    /// </remarks>
    public IReadOnlyList<PromptChoice> Prompts { get; init; } =
        Prompts is { Count: > 0 }
            ? Prompts
            : throw new ArgumentException(
                $"role '{Id}' has no prompts — a role's first prompt is its general one, and there is none",
                nameof(Prompts));

    /// <summary>The prompt a round gets when nobody chose otherwise.</summary>
    public PromptChoice General => Prompts[0];

    /// <summary>
    /// Which roles run together: a stage and a kind. Roles of the other kind never share a round,
    /// which is why the five-active limit counts per bucket.
    /// </summary>
    public string Bucket => $"{Stage}:{(ProgrammingTask ? "code" : "document")}";
}

/// <summary>The seed file's shape, for the source-generated deserialiser. Internal: the seed is read, never written.</summary>
internal sealed record RoleSeed(List<SeedRole> Roles);

internal sealed record SeedRole(string Id, string Name, string Stage, bool ProgrammingTask, List<SeedPrompt> Prompts);

internal sealed record SeedPrompt(string Id, string Label, string Purpose);

/// <summary>
/// Which roles exist and which prompt a given round of one gets.
/// </summary>
/// <remarks>
/// <para>The roles used to be an enum, five constants and a 26-row array — and a hand-typed mirror
/// of the same rows in the extension, held level by a test that regexed this file's predecessor as
/// text. The catalog is DATA now: <c>shared/builtin-roles.json</c> is embedded into this assembly
/// and loaded once as <see cref="Builtin"/>; the extension generates its copy from the same file;
/// each half's loader is asserted against the file by its own tests. What a person adds through the
/// settings channel is composed onto the seed by <c>Compose</c>, whose rules are the reason a
/// custom role cannot rename a built-in, shadow a shipped prompt, or escape the prompts directory.</para>
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
    public IReadOnlyList<RoleDefinition> Roles { get; }

    /// <summary>What composition refused, each as "id: reason" — the panel shows these. Empty for the seed.</summary>
    public IReadOnlyList<string> Dropped { get; }

    internal RoleCatalog(IReadOnlyList<RoleDefinition> roles, IReadOnlyList<string> dropped)
    {
        Roles = roles;
        Dropped = dropped;
    }

    /// <summary>The role with this id, matched case-insensitively — what the enum parse used to allow.</summary>
    public RoleDefinition? ById(string id) =>
        Roles.FirstOrDefault(r => string.Equals(r.Id, id, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// The ids of the roles a stage runs, in catalog order.
    /// </summary>
    /// <remarks>
    /// Programming roles only, until plan 4 gives a document role a round to run in: a role stored
    /// with <c>programmingTask: false</c> is in the catalog and in no round, and it is not dropped —
    /// it is waiting.
    /// </remarks>
    public IReadOnlyList<string> RolesOf(string stage) =>
        [.. Roles.Where(r => r.Stage == stage && r.ProgrammingTask).Select(r => r.Id)];

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
            ?? throw new InvalidOperationException(
                $"the seed '{SeedResource}' is not embedded in this build — check the EmbeddedResource item in CoaiMcp.Core.csproj");
        var seed = JsonSerializer.Deserialize(stream, CoreJsonContext.Default.RoleSeed)
            ?? throw new InvalidOperationException($"the seed '{SeedResource}' parsed to nothing");

        return new RoleCatalog([.. seed.Roles.Select(FromSeed)], []);
    }

    /// <summary>A seed row as a role: every prompt shipped, the first one general, the role switched on.</summary>
    private static RoleDefinition FromSeed(SeedRole role) => new(
        role.Id,
        role.Name,
        role.Stage,
        role.ProgrammingTask,
        [.. role.Prompts.Select((p, i) => new PromptChoice(p.Id, role.Id, p.Label, p.Purpose, Universal: i == 0, BuiltIn: true))],
        Active: true,
        BuiltIn: true);
}
