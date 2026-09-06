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

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var ctx = context.HttpContext;
        if (Auth.RequireCaller(ctx, allowedDomains, allowAnyDomain) is not { } who)
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

/// <summary>Registering a route as one that requires a company caller.</summary>
public static class RouteHandlerBuilderExtensions
{
    /// <summary>This route is for signed-in company callers, and its handler is given the caller.</summary>
    public static RouteHandlerBuilder RequireCaller(this RouteHandlerBuilder builder, CallerFilter filter) =>
        builder.AddEndpointFilter(filter);

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
