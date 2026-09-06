namespace CoaiServer;

/// <summary>
/// The three routes a person's identity travels through: mint a session, spend it, give it back.
/// </summary>
public static class SessionEndpoints
{
    public static void MapSessionEndpoints(this WebApplication app, SessionStore sessions, CallerFilter gate)
    {
        // A session is minted from an IDENTITY PROVIDER's token and never from another session: a
        // token that could mint its own successor would never expire, which is the one property a
        // deadline exists to give it.
        app.MapPost("/api/session", (HttpContext ctx) =>
        {
            if (ctx.Items.ContainsKey(Auth.SessionToken))
            {
                return Refuse("a session token cannot mint another session — sign in with Microsoft again");
            }

            var caller = ctx.CallerOf();

            // Persisted BEFORE the token is returned: a caller holding a token this server never
            // stored has a credential that can only ever be refused, and no way to know it.
            var (token, record) = sessions.Issue(caller.Email, caller.Name, DateTimeOffset.UtcNow);

            return Results.Json(
                new SessionDto(token, record.ExpiresUtc, record.Email),
                ServerJsonContext.Default.SessionDto,
                statusCode: StatusCodes.Status201Created);
        }).RequireCaller(gate);

        // Only a session can be revoked, and saying so matters: answering 204 to somebody who
        // presented Microsoft's token would tell them a credential was withdrawn when nothing was —
        // a stateless token cannot be deleted by this server at all. (gemini, plan round.)
        app.MapDelete("/api/session", (HttpContext ctx) =>
        {
            if (ctx.Items[Auth.SessionToken] is not string token)
            {
                return Refuse(
                    "this endpoint revokes a session token, and you presented an identity provider's "
                    + "token — which this server cannot withdraw. Sign out in the editor instead.");
            }

            // 204 over a failed delete would tell a person their credential was withdrawn while a
            // stolen bearer went on working until it expired. The one lie a revoke must not tell.
            return sessions.Revoke(token)
                ? Results.NoContent()
                : Results.Json(
                    new ErrorDto(
                        "the session could not be withdrawn — its file could not be removed, so the "
                        + "token still works. The server log names the failure."),
                    ServerJsonContext.Default.ErrorDto,
                    statusCode: StatusCodes.Status500InternalServerError);
        }).RequireCaller(gate);

        app.MapGet("/api/whoami", (HttpContext ctx) =>
        {
            var caller = ctx.CallerOf();

            return Results.Json(
                new WhoAmIDto(caller.Email, caller.Name, caller.IsAdmin),
                ServerJsonContext.Default.WhoAmIDto);
        }).RequireCaller(gate);
    }

    /// <summary>
    /// A refusal in the shape every other answer here has.
    /// </summary>
    /// <remarks>
    /// <c>Results.BadRequest(string)</c> writes text/plain, and a client deserialising this API's
    /// <c>ErrorDto</c> meets a parse error where the sentence should be. (gemini, code round.)
    /// </remarks>
    private static IResult Refuse(string because) =>
        Results.Json(
            new ErrorDto(because),
            ServerJsonContext.Default.ErrorDto,
            statusCode: StatusCodes.Status400BadRequest);
}
