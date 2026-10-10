namespace CoaiServer;

/// <summary>Who this request is, once, decided before the handler runs.</summary>
public sealed record Caller(string Email, string Name, bool IsAdmin);

/// <summary>
/// The one gate every authorised route passes through.
/// </summary>
/// <remarks>
/// <para>Story 2.1 called <see cref="Auth.RequireCaller"/> by hand in each handler and returned
/// <c>Results.Empty</c> when it answered null, relying on the status code it had already written.
/// Its code round asked for a filter and it was deferred to this story on the grounds that three
/// call sites did not pay for one. This story adds the catalog, and 2.3 adds four review routes, so
/// it does now — and the deferral was the point: the shape is adopted when there is enough of it to
/// judge, not on the first route.</para>
/// <para><b>What the filter fixes, beyond repetition.</b> The hand-written form had a real trap in
/// it: forgetting the check is a route that answers 200 to anybody, and nothing in the type system
/// says a handler needed it. Here a route is authorised by being registered with
/// <see cref="RouteHandlerBuilderExtensions.RequireCaller"/>, and the handler RECEIVES a
/// <see cref="Caller"/> — so a handler that wants to know who is calling cannot be written without
/// the gate that establishes it.</para>
/// <para>Authentication itself still happens in the pipeline, before the rate limiter, because the
/// limiter partitions on the verified email. This filter only AUTHORISES what that resolved.</para>
/// </remarks>
public sealed class CallerFilter(
    IReadOnlyCollection<string> allowedDomains,
    bool allowAnyDomain,
    IReadOnlyCollection<string> admins) : IEndpointFilter
{
    /// <summary>Where the resolved caller is put for the handler to read.</summary>
    public const string Key = "coai.caller";

    /// <summary>Whether this server serves <paramref name="email"/> at all — the allow-list, the ONE place it is applied.</summary>
    /// <remarks>
    /// The gate below calls it for every request, and the admin roster calls it for every session it
    /// lists: a session file outlives its domain's removal from <c>Coai:AllowedDomains</c>, and a roster
    /// that listed it would show an admin somebody every request refuses. One method, so the two cannot
    /// disagree — until the final code round on E1+E2 (2026-10-10) <see cref="Auth.RequireCaller"/> kept
    /// its own copy of this composition.
    /// </remarks>
    public bool Admits(string email) => allowAnyDomain || TokenIdentity.DomainAllowed(email, allowedDomains);

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var ctx = context.HttpContext;
        if (Auth.RequireCaller(ctx, Admits) is not { } who)
        {
            // RequireCaller has already set 401 or 403. Returning Results.Empty preserves it —
            // returning any other result here would overwrite the status the gate chose.
            return Results.Empty;
        }

        ctx.Items[Key] = new Caller(
            who.Email,
            who.Name,
            admins.Any(a => string.Equals(a, who.Email, StringComparison.OrdinalIgnoreCase)));

        return await next(context);
    }
}

/// <summary>
/// The one admin decision: who may see the company's people and the company's spending.
/// </summary>
/// <remarks>
/// <para>It was written twice — the inline check in <c>/api/people</c> and the one in the company
/// branch of <c>/api/usage</c>, identical to the letter — and the code round on E1+E2 (2026-10-10)
/// asked for one. Two copies of an authorisation decision are two places for the next change to miss,
/// and a miss here hands every colleague's email and spending to any caller.</para>
/// <para>Two shapes over the same decision, because the two routes differ: <c>/api/people</c> is
/// admin-only as a WHOLE and is registered with <see cref="RouteHandlerBuilderExtensions.RequireAdmin"/>,
/// so its handler runs for nobody else; <c>/api/usage</c> answers <c>me</c> to everyone and asks only
/// its company branch, so it calls <see cref="RefusalFor"/> from inside the handler. The predicate and
/// the sentence live here and nowhere else.</para>
/// </remarks>
public static class AdminOnly
{
    /// <summary>Null when <paramref name="caller"/> may proceed; otherwise the 403 that names the setting to ask for.</summary>
    /// <remarks>
    /// 403 rather than 404: the caller is authenticated and the route exists, so the honest answer is
    /// that this is an admin view — and naming <c>Coai:Admins</c> is what lets somebody ask the right
    /// person for it. <paramref name="what"/> is the route or the scope that was asked for, so the
    /// sentence says what was refused.
    /// </remarks>
    public static IResult? RefusalFor(Caller caller, string what) =>
        caller.IsAdmin
            ? null
            : Refusal.Json($"{what} is for admins. Ask an operator to add you to Coai:Admins.", StatusCodes.Status403Forbidden);
}

/// <summary>A whole route that is for admins only — <see cref="AdminOnly"/>'s decision, applied before the handler.</summary>
public sealed class AdminFilter(string what) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next) =>
        AdminOnly.RefusalFor(context.HttpContext.CallerOf(), what) ?? await next(context);
}

/// <summary>Registering a route as one that requires a company caller.</summary>
public static class RouteHandlerBuilderExtensions
{
    /// <summary>This route is for signed-in company callers, and its handler is given the caller.</summary>
    public static RouteHandlerBuilder RequireCaller(this RouteHandlerBuilder builder, CallerFilter filter) =>
        builder.AddEndpointFilter(filter);

    /// <summary>
    /// This route is for admins only: the caller is established, <see cref="AdminOnly"/> decides, and the
    /// handler runs for nobody else. <paramref name="what"/> names the route in the refusal.
    /// </summary>
    public static RouteHandlerBuilder RequireAdmin(this RouteHandlerBuilder builder, CallerFilter filter, string what) =>
        builder.RequireCaller(filter).AddEndpointFilter(new AdminFilter(what));

    /// <summary>
    /// The caller the filter established.
    /// </summary>
    /// <remarks>
    /// It throws when absent rather than returning null, because absent means the route was
    /// registered without <see cref="RequireCaller"/> — a wiring mistake that must fail loudly in a
    /// test, not degrade into an anonymous answer in production.
    /// </remarks>
    public static Caller CallerOf(this HttpContext ctx) =>
        ctx.Items[CallerFilter.Key] as Caller
        ?? throw new InvalidOperationException(
            $"{ctx.Request.Path} asked for its caller but is not registered with RequireCaller()");
}
