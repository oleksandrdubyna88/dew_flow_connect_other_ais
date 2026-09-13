using CoaiMcp.Core.Rounds;
using System.Text.RegularExpressions;

namespace CoaiMcp.Runners.Reviewers;

/// <summary>
/// How a Team server answered the question "which review roles will you run".
/// </summary>
/// <remarks>
/// <b>Three states, not two, and the third is the one that costs.</b> A server that ANSWERED and a
/// server that could not be REACHED both leave a client without a list, and treating them alike is
/// how a network blip becomes a silently smaller round. Naming the difference is what lets the
/// exclusion say something a person can act on. (codex, plan 3's plan round.)
/// </remarks>
public enum RemoteRolesSource
{
    /// <summary>Nobody has asked this server yet, so nothing is known about it.</summary>
    NotAsked,

    /// <summary>
    /// It was asked and did not answer — a timeout, a refusal, an unreadable body.
    /// </summary>
    /// <remarks>
    /// NOT the same as an old server that answered without the field. A failure has no opinion about
    /// roles, and reading one as "the five this product ships" would be inventing an answer nobody
    /// gave.
    /// </remarks>
    Unreachable,

    /// <summary>
    /// It answered, and said nothing about roles — a server older than this field.
    /// </summary>
    /// <remarks>
    /// Which means the five this product ships, exactly as before this plan existed. An EMPTY list
    /// lands here too: a server that HAS the field always accepts at least five, so <c>[]</c> can
    /// only be a bug, and reading it as "no roles at all" would silently empty every round.
    /// </remarks>
    Shipped,

    /// <summary>It answered with a list, and that list is the whole truth about it.</summary>
    Answered,
}

/// <summary>
/// What one Team server told this machine it will run.
/// </summary>
/// <param name="Names">
/// The roles it named. Empty unless <see cref="Source"/> is <see cref="RemoteRolesSource.Answered"/>.
/// </param>
/// <param name="AllowAny">Whether it said it will run any well-formed role, however it was named.</param>
public sealed partial record RemoteRoles(
    IReadOnlyList<string> Names, bool AllowAny, RemoteRolesSource Source)
{
    /// <summary>Nothing is known about this server's roles.</summary>
    public static readonly RemoteRoles Unknown = new([], false, RemoteRolesSource.NotAsked);

    /// <summary>A server's answer, read from the names and the flag its catalog carried.</summary>
    /// <remarks>
    /// <para><b>A nullable annotation is not a runtime check.</b> <c>{"roles":[null]}</c> deserialises
    /// to a list with a null in it, and counting that as an ANSWER made this half exclude every
    /// shipped role while the extension read the same response as "said nothing useful" — one server,
    /// two clients, two different rounds. Anything unusable is filtered, and a list with nothing left
    /// in it means the same as an absent one. (codex, story 4's second code round.)</para>
    /// <para>An empty list means <see cref="RemoteRolesSource.Shipped"/> for the reason the enum
    /// gives: a server that HAS the field always accepts at least five, so <c>[]</c> can only be a
    /// bug, and reading it as "no roles" would empty every round against that server.</para>
    /// </remarks>
    public static RemoteRoles From(IReadOnlyList<string?>? named, bool allowAny)
    {
        var usable = (named ?? [])
            .Select(name => name?.Trim() ?? string.Empty)
            .Where(IsRoleId)
            .ToList();

        return usable.Count > 0
            ? new RemoteRoles(usable, allowAny, RemoteRolesSource.Answered)
            : new RemoteRoles([], false, RemoteRolesSource.Shipped);
    }

    /// <summary>
    /// Whether a name a server sent could be a role id at all.
    /// </summary>
    /// <remarks>
    /// The same shape and the same 48 the rest of this product applies, because this is the one place
    /// an id arrives from OUTSIDE: a catalog is JSON from a box somebody else configured. Merely
    /// "not blank" was not enough — <c>[" "]</c> would make a catalog look ANSWERED, and an answered
    /// catalog is the whole truth about its server, so one space would report every shipped role as
    /// unsupported. (CodeRabbit, plan 3's pull request.)
    /// </remarks>
    private static bool IsRoleId(string name) =>
        name.Length is > 0 and <= MaxIdLength && RoleId.IsMatch(name);

    /// <summary>The bound the whole product applies, for the reason `COAI_ROUNDS_&lt;ID&gt;` gives.</summary>
    private const int MaxIdLength = 48;

    [GeneratedRegex(@"\A[A-Za-z][A-Za-z0-9_]*\z")]
    private static partial Regex RoleId { get; }

    /// <summary>The server was asked and did not answer — a timeout, a refusal, an unreadable body.</summary>
    public static readonly RemoteRoles Unreachable = new([], false, RemoteRolesSource.Unreachable);

    /// <summary>
    /// Whether this server will carry a role by this name — and if not, why not in one sentence.
    /// </summary>
    /// <remarks>
    /// <para><c>builtIn</c> is what the CLIENT knows: whether this is one of the five this product
    /// ships. It decides the answer for every server that has not told us otherwise, which is the
    /// behaviour that predates this plan and the behaviour an unreachable server must keep.</para>
    /// <para>Matched without case, because the server matches without case: a client that refused
    /// <c>requirements</c> against a catalog naming <c>Requirements</c> would never reach the
    /// canonicalisation that reconciles them. (gemini, plan 3's plan round.)</para>
    /// </remarks>
    /// <param name="role">The ID, which is what a server names its roles by and what is matched.</param>
    /// <param name="shown">
    /// The role's NAME as the person wrote it, which is what the sentence says. The two are different
    /// on purpose: a person who called a role "Requirements we wrote" should read that back, not the
    /// <c>Requirements</c> the wire uses. Caught by an existing test when this first used the id for
    /// both.
    /// </param>
    public string? WhyNot(string role, string shown, bool builtIn) =>
        Source == RemoteRolesSource.Answered ? Said(role, shown) : NotSaid(role, shown, builtIn);

    /// <summary>A server that told us its roles: the list decides, and it decides about the five too.</summary>
    private string? Said(string role, string shown) =>
        AllowAny || Names.Contains(role, StringComparer.OrdinalIgnoreCase)
            ? null
            : $"'{shown}' is not one of the roles this Team server runs — it runs {string.Join(", ", Names)}";

    /// <summary>
    /// The roles EVERY deployed Team server runs, whatever it has said.
    /// </summary>
    /// <remarks>
    /// <para><b>A literal list, and that is the point.</b> This was <c>builtIn</c> — a proxy for "one
    /// of the five", true for exactly as long as the product shipped five roles. Plan 4 put two
    /// DOCUMENT roles in the seed and the proxy silently started meaning seven, so a round would have
    /// sent a role no deployed server has ever heard of and been answered with a 400 naming the ones
    /// it does run: seconds, zero tokens, and a reviewer that was never going to answer. The Team
    /// server deploy is manual, so a tag does not put new roles on a box.</para>
    /// <para>A second proxy — <c>builtIn &amp;&amp; programmingTask</c> — was drafted and refused on
    /// the plan round, and the refusal is worth keeping: it fails UNSAFELY for the next built-in CODE
    /// role, which it would send. Any predicate over today's seed has to be re-derived every time the
    /// seed grows, and is wrong in between.</para>
    /// <para>This is not a predicate. It is a fact about the PAST — what this product shipped before
    /// <c>/api/catalog</c> ever named roles — so it is frozen by definition and no role added later
    /// can join it. A server that has not SAID its roles predates that field; therefore it runs
    /// exactly these.</para>
    /// </remarks>
    public static readonly System.Collections.Frozen.FrozenSet<string> BeforeTheCatalog =
        System.Collections.Frozen.FrozenSet.ToFrozenSet(
            [
                RoleCatalog.PlanRole,
                RoleCatalog.ConventionsRole,
                RoleCatalog.ArchitectureRole,
                RoleCatalog.SecurityRole,
                RoleCatalog.UxDxRole,
            ],
            StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Every other state. <see cref="BeforeTheCatalog"/> is carried by all of them; the SENTENCE
    /// differs, and so does WHOSE role it is.
    /// </summary>
    /// <remarks>
    /// <para>Three states and two kinds of role, because what a person can do about it differs on
    /// both axes. An old server wants updating, an unreachable one wants a look at the network, and
    /// one nobody has asked wants nothing yet — while a role THEY added and a role this PRODUCT added
    /// are different news entirely. Telling somebody that a shipped document role "is a role you
    /// added" would send them to look for a configuration mistake they never made. (gemini, the plan
    /// round.)</para>
    /// </remarks>
    private string? NotSaid(string role, string shown, bool builtIn)
    {
        // BOTH, not the name alone. A row a person wrote that happens to be called `Architecture`
        // is not the shipped Architecture, and the name on its own would have said it was — offering
        // somebody's own role to a server that has never seen it. (codex, the code round.)
        if (builtIn && BeforeTheCatalog.Contains(role))
        {
            return null;
        }

        return builtIn
            ? $"'{shown}' is a role this product added after this Team server was built, and that server {Because()}"
            : $"'{shown}' is a role you added, and this Team server {Because()}";
    }

    private string Because() =>
        Source switch
        {
            // Not a recital of the five. `Said` lists a server's roles when it REFUSES one, because
            // there the list is the answer; here the answer is "this box is too old", and the ids of
            // five roles somebody did not ask about are noise in front of it. (the code round, twice.)
            RemoteRolesSource.Shipped =>
                "is older than the setting that names roles, so it runs only what this product "
                + "shipped before that",
            RemoteRolesSource.Unreachable =>
                "could not be asked which roles it runs — so it was left out rather than sent and refused",
            _ => "has not been asked which roles it runs yet",
        };
}
