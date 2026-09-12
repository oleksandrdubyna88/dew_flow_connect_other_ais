using System.Text.RegularExpressions;

namespace CoaiMcp.Core.Rounds;

/// <summary>One prompt as a person wrote it into the settings — a row of a role's prompt list.</summary>
/// <param name="Id">
/// Nullable because this is a WIRE shape: a hand-written <c>{"label": "x"}</c> binds null here
/// whatever the annotation says, and a non-nullable one would turn that into a crash at the first
/// comparison. An absent id fails the slug rule like any other unusable one.
/// </param>
/// <param name="Label">What the picker shows. Absent falls back to the id, which is a slug and reads well enough.</param>
/// <param name="Purpose">The picker's tooltip. Absent is a prompt with no explanation, which is allowed.</param>
public sealed record PromptEntry(string? Id, string? Label = null, string? Purpose = null);

/// <summary>
/// One row of <c>COAI_ROLES</c>, as a person wrote it.
/// </summary>
/// <remarks>
/// <para><b>Every field but the id is nullable, and that is the whole design.</b> A row whose id
/// names a built-in is an OVERRIDE, and an override must be able to say "leave that as it is" about
/// every field it does not mention. A non-nullable <c>Active</c> would read an omitted one as
/// <c>false</c> and quietly switch Architecture off for anybody who only wanted to add a prompt to
/// it — the same shape of defect as the Team server's nullable <c>kind</c>, and raised on this
/// plan's own review round.</para>
/// <para>For a row that names no built-in — a role of the person's own — the absent fields take
/// documented defaults instead: active, a programming task, named by its id.</para>
/// </remarks>
public sealed record RoleEntry(
    string? Id,
    string? Name = null,
    string? Stage = null,
    bool? ProgrammingTask = null,
    bool? Active = null,
    IReadOnlyList<PromptEntry>? Prompts = null);

/// <summary>
/// The shipped roles, plus what a person added, as one catalog — and a sentence for everything
/// refused on the way.
/// </summary>
/// <remarks>
/// <para>Its own file, beside <see cref="PromptDeal"/>, for the same reason: the interesting part is
/// a set of RULES, and rules belong somewhere they can be read and tested one at a time rather than
/// discovered inside a settings parser.</para>
/// <para><b>Nothing here throws.</b> Every refusal is a row in <see cref="RoleCatalog.Dropped"/>
/// that the panel shows and <c>providers</c> answers, because what arrives here is a person's
/// configuration and a configuration mistake must not stop the four roles that are still fine from
/// reviewing. That is the opposite of <see cref="RoleCatalog.FromSeed"/>, which throws: the seed is
/// shipped inside the binary, so a bad one is a bad build and there is nobody to tell.</para>
/// </remarks>
public static partial class RoleComposition
{
    /// <summary>
    /// How many roles of one bucket may be switched on at once.
    /// </summary>
    /// <remarks>
    /// The operator's number, and the cost it bounds is real: every active role is one more reviewer
    /// launched per vendor per round. Counted per BUCKET — stage × kind — because roles of the other
    /// kind never share a round, so a code project and a document project on one machine are not
    /// made to share five slots.
    /// </remarks>
    public const int MaxActivePerBucket = 5;

    /// <summary>A role id becomes <c>COAI_ROUNDS_&lt;ID&gt;</c>, so it is latin and starts with a letter.</summary>
    [GeneratedRegex("^[A-Za-z][A-Za-z0-9_-]*$")]
    private static partial Regex RoleId { get; }

    /// <summary>A prompt id is a file name under <c>&lt;dataDir&gt;/prompts/</c> — the slug shape every shipped id has.</summary>
    [GeneratedRegex("^[a-z0-9][a-z0-9-]*$")]
    private static partial Regex PromptId { get; }

    /// <summary>The shipped catalog with a person's rows composed onto it.</summary>
    /// <param name="entries">
    /// What <c>COAI_ROLES</c> parsed to. Empty — absent, <c>[]</c>, or JSON the parser refused — is
    /// the seed exactly as it ships, which is what a machine nobody has configured must run.
    /// </param>
    public static RoleCatalog Compose(IReadOnlyList<RoleEntry> entries)
    {
        var dropped = new List<string>();
        var promptIds = new HashSet<string>(
            RoleCatalog.Builtin.Roles.SelectMany(r => r.Prompts).Select(p => p.Id),
            StringComparer.OrdinalIgnoreCase);

        var overrides = ById(entries, dropped);
        var roles = new List<RoleDefinition>(
            RoleCatalog.Builtin.Roles.Select(r => Overridden(r, overrides.GetValueOrDefault(r.Id), promptIds, dropped)));

        roles.AddRange(Added(entries, roles, promptIds, dropped));

        return RoleCatalog.From(Capped(roles, dropped), dropped);
    }

    /// <summary>The rows that name a built-in, keyed by the seed's own spelling; later duplicates are dropped.</summary>
    private static Dictionary<string, RoleEntry> ById(IReadOnlyList<RoleEntry> entries, List<string> dropped)
    {
        var found = new Dictionary<string, RoleEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in entries)
        {
            if (RoleCatalog.Builtin.ById(entry.Id ?? string.Empty) is not { } builtIn)
            {
                continue;
            }

            if (!found.TryAdd(builtIn.Id, entry))
            {
                dropped.Add($"{entry.Id}: named more than once — the first row wins");
            }
        }

        return found;
    }

    /// <summary>
    /// A shipped role with the person's row applied: their switch when they set one, their extra
    /// prompts when they wrote any, and the seed's identity always.
    /// </summary>
    /// <remarks>
    /// Id, name, stage and kind are NEVER taken from the row. A built-in cannot be renamed — its id
    /// keys settings, session files and every row of the rounds database — and a name it could be
    /// given would only disagree with the one the help articles use.
    /// </remarks>
    private static RoleDefinition Overridden(
        RoleDefinition builtIn, RoleEntry? row, HashSet<string> promptIds, List<string> dropped) =>
        row is null
            ? builtIn
            : builtIn with
            {
                Active = row.Active ?? builtIn.Active,
                Prompts = [.. builtIn.Prompts, .. Extra(row, builtIn.Id, promptIds, dropped)],
            };

    /// <summary>The rows that name no built-in: a person's own roles, in the order they wrote them.</summary>
    private static IEnumerable<RoleDefinition> Added(
        IReadOnlyList<RoleEntry> entries, List<RoleDefinition> taken, HashSet<string> promptIds, List<string> dropped)
    {
        var ids = new HashSet<string>(taken.Select(r => r.Id), StringComparer.OrdinalIgnoreCase);
        foreach (var entry in entries)
        {
            var id = entry.Id ?? string.Empty;
            if (RoleCatalog.Builtin.ById(id) is not null)
            {
                continue;
            }

            if (Refusal(entry) is { } reason)
            {
                dropped.Add($"{id}: {reason}");
                continue;
            }

            if (!ids.Add(id))
            {
                dropped.Add($"{id}: another role already has that id — ids are matched without case, because each one becomes COAI_ROUNDS_{id.ToUpperInvariant()}");
                continue;
            }

            if (Extra(entry, id, promptIds, dropped) is not { Count: > 0 } prompts)
            {
                dropped.Add($"{id}: none of its prompts could be used, and a role's first prompt is its general one");
                ids.Remove(id);
                continue;
            }

            yield return new RoleDefinition(
                id,
                string.IsNullOrWhiteSpace(entry.Name) ? id : entry.Name,
                entry.Stage!,
                entry.ProgrammingTask ?? true,
                prompts,
                Active: entry.Active ?? true);
        }
    }

    /// <summary>Why this row cannot be a role of its own, or null when it can.</summary>
    private static string? Refusal(RoleEntry entry)
    {
        if (!RoleId.IsMatch(entry.Id ?? string.Empty))
        {
            return "that is not a usable id — it becomes an environment variable, so it is latin, starts with a letter, and holds only letters, digits, '-' and '_'";
        }

        if (entry.Stage is not (RoleStages.Plan or RoleStages.Result))
        {
            return $"'{entry.Stage}' is not a stage — it is '{RoleStages.Plan}' or '{RoleStages.Result}'";
        }

        return entry.Prompts is { Count: > 0 }
            ? null
            : "it has no prompts, and a role's first prompt is its general one";
    }

    /// <summary>
    /// The prompts a row adds, each refused by name if it cannot be taken.
    /// </summary>
    /// <remarks>
    /// Ids are global, not per role: <c>RolePrompts</c> keys text files by prompt id alone, so two
    /// roles naming <c>rules</c> would read one file and one of them would be given a question it
    /// never asked for. The slug rule is also what keeps an id from being a path — a row asking for
    /// <c>../../secrets</c> is refused here rather than at the moment somebody's file is read into
    /// a reviewer's prompt.
    /// </remarks>
    private static List<PromptChoice> Extra(
        RoleEntry row, string roleId, HashSet<string> promptIds, List<string> dropped)
    {
        var prompts = new List<PromptChoice>();
        foreach (var prompt in row.Prompts ?? [])
        {
            var id = prompt.Id ?? string.Empty;
            if (!PromptId.IsMatch(id))
            {
                dropped.Add($"{roleId}: the prompt id '{id}' is not a usable one — it names a file, so it is lower-case letters, digits and '-'");
                continue;
            }

            if (!promptIds.Add(id))
            {
                dropped.Add($"{roleId}: the prompt id '{id}' is already in use, and a prompt id names one text for the whole catalog");
                continue;
            }

            prompts.Add(new PromptChoice(
                id,
                roleId,
                string.IsNullOrWhiteSpace(prompt.Label) ? id : prompt.Label,
                prompt.Purpose ?? string.Empty,
                Universal: prompts.Count == 0 && !IsBuiltIn(roleId)));
        }

        return prompts;
    }

    private static bool IsBuiltIn(string roleId) => RoleCatalog.Builtin.ById(roleId) is not null;

    /// <summary>
    /// At most five active roles per bucket — and never at a built-in's expense.
    /// </summary>
    /// <remarks>
    /// Built-ins come first in catalog order, so the trim only ever reaches a person's own roles,
    /// in the order they wrote them. The consequence is worth saying out loud: with all four shipped
    /// result roles switched on, ONE custom result role can be active, and the way to make room is
    /// to untick a built-in rather than to discover a role quietly not running.
    /// </remarks>
    private static List<RoleDefinition> Capped(List<RoleDefinition> roles, List<string> dropped)
    {
        var active = new Dictionary<string, int>();
        var capped = new List<RoleDefinition>(roles.Count);
        foreach (var role in roles)
        {
            var count = active.GetValueOrDefault(role.Bucket);
            if (!role.Active)
            {
                capped.Add(role);
                continue;
            }

            if (count >= MaxActivePerBucket)
            {
                dropped.Add($"{role.Id}: it is over the limit of {MaxActivePerBucket} roles switched on at once for the {role.Stage} stage, so it is in the list and switched off");
                capped.Add(role with { Active = false });
                continue;
            }

            active[role.Bucket] = count + 1;
            capped.Add(role);
        }

        return capped;
    }
}
