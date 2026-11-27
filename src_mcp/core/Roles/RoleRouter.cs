using CoaiMcp.Core.Work;

namespace CoaiMcp.Core.Roles;

/// <summary>What a role job asks the router: the role, the model the caller named (empty for none) and the facts of the task it is for.</summary>
/// <param name="Role">The role's name in the table (<c>ba</c>, <c>coder</c>, <c>important</c>…).</param>
/// <param name="Model">The model the caller named, or empty.</param>
/// <param name="Kind">The task's <c>kind</c>, or empty when the job is not for a task file.</param>
/// <param name="Complexity">The task's complexity, 0 to 10, or <see cref="NoComplexity"/> when it is not known.</param>
/// <param name="Security">The task's <c>security: true</c> flag.</param>
internal sealed record RouteRequest(string Role, string Model, string Kind, int Complexity, bool Security)
{
    /// <summary>Complexity "not known": no band applies and the important rule does not fire on a number.</summary>
    public const int NoComplexity = -1;

    /// <summary>A request that names only a role — a job with no task facts and no model.</summary>
    public static RouteRequest For(string role) => new(role, string.Empty, string.Empty, NoComplexity, false);
}

/// <summary>The tables the router reads: each role's ordered list, and the complexity bands. Exactly the shape of the <c>roles</c> answer.</summary>
internal sealed record RouteTable(IReadOnlyList<(string Role, IReadOnlyList<ModelRef> Models)> Roles, IReadOnlyList<Band> Bands);

/// <summary>What the router decided: an ordered list with the reason for it, or a refusal with the cure. A closed pair — a caller cannot forget the refusal.</summary>
internal abstract record RouteResult
{
    private RouteResult()
    {
    }

    /// <summary>The models to try, in order, with no duplicates; <see cref="Why"/> names the rule that chose the list.</summary>
    public sealed record Routed(IReadOnlyList<ModelRef> Models, string Why) : RouteResult;

    /// <summary>No route: the sentence says why and names the setting that fixes it.</summary>
    public sealed record Refused(string Reason) : RouteResult;
}

/// <summary>
/// The router of a role job (spec A, "Router", epoch 3 E1 s3): which models a job may try, in which order — pure, so every rule is a vector (<c>shared/role-route-vectors.json</c>).
/// </summary>
/// <remarks>
/// <para><b>In order.</b> (1) A task that is IMPORTANT — <c>security: true</c>, kind <c>security</c> or complexity 10, decided by <see cref="ImportantRule"/>, the same function the validator lists such
/// tasks with — takes the <c>important</c> list, and an explicit model outside that list is refused. (2) An explicit model goes first, then the role's list. (3) A coder task takes the model of its
/// complexity band, then the coder list; every other role takes its list. (4) No duplicates; every entry must keep the vendor-routing rule; an empty list is a refusal naming <c>COAI_ROLES_MODELS</c>.</para>
/// <para><b>No IO and no clock.</b> The tables arrive as arguments; the answer leaves as a value. An explicit model is placed on a vendor from the lists first and from <see cref="VendorRouting.CliFor"/>
/// second, and a name neither knows is refused rather than guessed onto a CLI.</para>
/// </remarks>
internal static class RoleRouter
{
    /// <summary>The role whose list a task that is important takes.</summary>
    public const string ImportantRole = "important";

    /// <summary>The role whose tasks take the model of their complexity band.</summary>
    public const string CoderRole = "coder";

    /// <summary>The setting every refusal for a missing model names.</summary>
    public const string ModelsSetting = "COAI_ROLES_MODELS";

    /// <summary>The setting that holds a coder band's model — named when that model's name is refused.</summary>
    public const string BandsSetting = "COAI_ROLES_BANDS";

    /// <summary>What a refusal names for a model that only the caller wrote, in the call itself.</summary>
    public const string ExplicitModelKey = "the model argument of start_role";

    public static RouteResult Route(RouteRequest request, RouteTable table)
    {
        var important = ImportantRule.Reason(request.Kind, request.Complexity, request.Security);

        return (important.Length > 0 ? Important(request, table, important) : Ordinary(request, table)) switch
        {
            RouteResult.Routed chosen => Judged(chosen.Models, request, table, chosen.Why),
            var refused => refused,
        };
    }

    /// <summary>
    /// The list as it stands, or the refusal: an empty list names the setting; an entry that breaks the vendor-routing rule is named; and so is an entry whose NAME is not one a CLI is known to accept
    /// (<see cref="RoleModelNames"/>) — claude runs its default model, and bills it, for a name it does not know, so a typo must not reach a launch.
    /// </summary>
    private static RouteResult Judged(IReadOnlyList<ModelRef> ordered, RouteRequest request, RouteTable table, string why)
    {
        if (ordered.Count == 0)
        {
            return new RouteResult.Refused(
                $"no model is set for role '{request.Role}', so the job has none to run on; set {ModelsSetting} (a JSON object of role name to an ordered list of {{\"model\":\"…\",\"vendor\":\"…\"}})");
        }

        var wrong = ordered.Select(entry => VendorRouting.Refusal(entry.Model, entry.Vendor)).FirstOrDefault(refusal => refusal.Length > 0);

        if (wrong is { Length: > 0 })
        {
            return new RouteResult.Refused($"the route of role '{request.Role}' breaks the vendor-routing rule — {wrong}; fix it in {ModelsSetting}");
        }

        var unknown = ordered.Select(entry => RoleModelNames.Refusal(entry.Model, entry.Vendor, KeyOf(entry, request, table))).FirstOrDefault(refusal => refusal.Length > 0);

        return unknown is { Length: > 0 }
            ? new RouteResult.Refused($"the route of role '{request.Role}' has a model name no CLI is known to accept — {unknown}")
            : new RouteResult.Routed(ordered, why);
    }

    /// <summary>The setting that holds an entry of the route: the call itself for a model only the caller wrote, the bands for a band's model, else the roles table.</summary>
    private static string KeyOf(ModelRef entry, RouteRequest request, RouteTable table)
    {
        var inRoles = table.Roles.SelectMany(role => role.Models).Contains(entry);
        if (!inRoles && request.Model.Length > 0 && string.Equals(entry.Model, request.Model, StringComparison.Ordinal))
        {
            return ExplicitModelKey;
        }

        return !inRoles && table.Bands.Any(band => band.Model == entry.Model && band.Vendor == entry.Vendor) ? BandsSetting : ModelsSetting;
    }

    /// <summary>The important list, the explicit model first when it is on it, and a refusal when it is not.</summary>
    private static RouteResult Important(RouteRequest request, RouteTable table, string reason)
    {
        var list = ListOf(table, ImportantRole);
        var why = $"important ({reason}): the {ImportantRole} list";
        if (request.Model.Length == 0)
        {
            return Chosen([], list, why);
        }

        return Named(list, request.Model) is { } named
            ? Chosen([named], list, why + $", {named.Model} first because the caller named it")
            : new RouteResult.Refused($"model '{request.Model}' is not in the {ImportantRole} list ({Names(list)}), and this task is important ({reason}): it runs only on that list; name one of them or set {ModelsSetting}{FamilyHint(request.Model, list)}");
    }

    /// <summary>
    /// A codex name of the exact shape whose FAMILY is on the list (epoch 3 E3 s2 t5, S4 item 6): lists are compared by the name as written, and never by what a family resolves to, so a caller who names <c>gpt-6.2-astra</c> against a list that holds <c>astra</c> is refused — and told to name the family or to change the list.
    /// </summary>
    private static string FamilyHint(string model, IReadOnlyList<ModelRef> list) =>
        CodexModelShape.TryParse(model, RoleModelNames.Current.CodexFamilies, out var family, out _) && list.Any(entry => entry.Vendor == "codex" && entry.Model == family)
            ? $"; the list holds the family '{family}' and lists are compared by the name as written, so name '{family}' or change the list in {ModelsSetting}"
            : string.Empty;

    /// <summary>An explicit model, else the coder's band, then the role's list.</summary>
    private static RouteResult Ordinary(RouteRequest request, RouteTable table)
    {
        var list = ListOf(table, request.Role);
        if (request.Model.Length > 0)
        {
            return PlacedModel(request, table) is { } placed
                ? Chosen([placed], list, $"the model the caller named, then the {request.Role} list")
                : new RouteResult.Refused($"model '{request.Model}' is in no list of the roles table and no vendor rule places it; fix {ExplicitModelKey}, or add the model to {ModelsSetting} with its vendor");
        }

        return request.Role == CoderRole && BandModel(table, request.Complexity) is { } band
            ? Chosen([band], list, $"the model of complexity {request.Complexity}'s band, then the {CoderRole} list")
            : Chosen([], list, $"the {request.Role} list");
    }

    /// <summary>The explicit model on its vendor: the role's own list first, then every other list and band, then the vendor-routing rule. Null when nothing places it.</summary>
    private static ModelRef? PlacedModel(RouteRequest request, RouteTable table)
    {
        var known = ListOf(table, request.Role)
            .Concat(ListOf(table, ImportantRole))
            .Concat(table.Roles.SelectMany(role => role.Models))
            .Concat(table.Bands.Select(band => new ModelRef(band.Model, band.Vendor)));

        if (Named(known, request.Model) is { } named)
        {
            return named;
        }

        return VendorRouting.CliFor(request.Model) is { Length: > 0 } cli ? new ModelRef(request.Model, cli) : null;
    }

    private static ModelRef? BandModel(RouteTable table, int complexity) =>
        complexity >= 0 && table.Bands.FirstOrDefault(band => complexity >= band.Min && complexity <= band.Max) is { } band ? new ModelRef(band.Model, band.Vendor) : null;

    private static ModelRef? Named(IEnumerable<ModelRef> models, string name) =>
        models.FirstOrDefault(model => string.Equals(model.Model, name, StringComparison.OrdinalIgnoreCase));

    private static IReadOnlyList<ModelRef> ListOf(RouteTable table, string role) =>
        table.Roles.FirstOrDefault(entry => entry.Role == role).Models ?? [];

    private static string Names(IReadOnlyList<ModelRef> list) => string.Join(", ", list.Select(model => model.Model));

    /// <summary>The ordered route of a rule: the head, then the tail, no duplicates, with the rule that chose it.</summary>
    private static RouteResult Chosen(IReadOnlyList<ModelRef> head, IReadOnlyList<ModelRef> tail, string why) => new RouteResult.Routed(Distinct([.. head, .. tail]), why);

    /// <summary>The first of each (model, vendor) — a model is one entry however many rules named it.</summary>
    private static IReadOnlyList<ModelRef> Distinct(IEnumerable<ModelRef> models)
    {
        var seen = new HashSet<(string, string)>();

        return [.. models.Where(model => seen.Add((model.Model.ToLowerInvariant(), model.Vendor.ToLowerInvariant())))];
    }
}
