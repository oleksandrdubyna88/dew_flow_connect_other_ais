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
public sealed record RemoteRoles(
    IReadOnlyList<string> Names, bool AllowAny, RemoteRolesSource Source)
{
    /// <summary>Nothing is known about this server's roles.</summary>
    public static readonly RemoteRoles Unknown = new([], false, RemoteRolesSource.NotAsked);

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
        Source switch
        {
            RemoteRolesSource.Answered when AllowAny || Names.Contains(role, StringComparer.OrdinalIgnoreCase) => null,
            RemoteRolesSource.Answered =>
                $"'{shown}' is not one of the roles this Team server runs — it runs "
                + string.Join(", ", Names),
            _ when builtIn => null,
            RemoteRolesSource.Shipped =>
                $"'{shown}' is a role you added, and this Team server is older than the setting that "
                + "carries them — it runs the five this product ships",
            RemoteRolesSource.Unreachable =>
                $"'{shown}' is a role you added, and this Team server could not be asked whether it "
                + "runs one — so it was left out rather than sent and refused",
            _ =>
                $"'{shown}' is a role you added, and this Team server has not been asked yet whether "
                + "it runs one",
        };
}
