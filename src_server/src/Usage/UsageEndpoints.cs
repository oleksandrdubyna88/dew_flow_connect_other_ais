namespace CoaiServer;

/// <summary>What the team is spending, to the person who spent it and to an admin.</summary>
public static class UsageEndpoints
{
    /// <summary>The scope name that asks for everybody, and needs to be an admin.</summary>
    private const string CompanyScope = "company";

    public static void MapUsageEndpoints(this WebApplication app, UsageReader usage, CallerFilter gate)
    {
        app.MapGet("/api/usage", (HttpContext ctx, string? window, string? scope) =>
        {
            var caller = ctx.CallerOf();

            if (UsageWindow.Range(window, DateTimeOffset.UtcNow) is not { } range)
            {
                // Named, not defaulted. Falling back to "today" would answer a question nobody asked
                // and would look like the data was wrong rather than the request.
                return Refuse(
                    $"'{window}' is not a window. Use one of: {string.Join(", ", UsageWindow.Names)}",
                    StatusCodes.Status400BadRequest);
            }

            if (Scope(scope) is not { } company)
            {
                // `scope=compnay` used to answer 200 with the PERSONAL total. An admin reading that
                // as the company's would make a spending decision from one person's numbers.
                // (codex, code round.)
                return Refuse(
                    $"'{scope}' is not a scope. Use 'me' or '{CompanyScope}'.",
                    StatusCodes.Status400BadRequest);
            }

            if (company && !caller.IsAdmin)
            {
                // 403 rather than 404: the caller is authenticated and the route exists, so the honest
                // answer is that this is an admin view — and naming the setting is what lets somebody
                // ask the right person for it.
                return Refuse(
                    "scope=company is for admins. Ask an operator to add you to Coai:Admins.",
                    StatusCodes.Status403Forbidden);
            }

            var scan = usage.Read(range);

            return Results.Json(Describe(scan, range, caller, company), ServerJsonContext.Default.UsageDto);
        }).RequireCaller(gate);
    }

    /// <summary>
    /// The answer, which differs by scope in more than its size.
    /// </summary>
    /// <remarks>
    /// <c>unreadableLines</c> is reported ONLY to an admin. A single torn line from months ago would
    /// otherwise appear on every person's own page for ever, telling them their record is damaged —
    /// about something they cannot see, cannot fix and did not cause. An operator can act on it; a
    /// reviewer looking at their own week cannot. (Two reviewers, plan round.)
    /// </remarks>
    private static UsageDto Describe(UsageScan scan, UsageRange range, Caller caller, bool company) =>
        company
            ? new UsageDto(
                range.FromUtc,
                range.ToUtc,
                CompanyScope,
                UsageTotals.ByVendor(scan.Lines),
                UsageTotals.ByPerson(scan.Lines),
                scan.Unreadable)
            : new UsageDto(
                range.FromUtc,
                range.ToUtc,
                "me",
                UsageTotals.ByVendorFor(scan.Lines, caller.Email),
                [],
                null);

    /// <summary>True for company, false for me, null when it is neither.</summary>
    /// <remarks>
    /// Absent means "me" — that is a default, not a fallback. A VALUE that is neither is a typo, and
    /// answering it with somebody's personal total is how the wrong number ends up in a decision.
    /// </remarks>
    private static bool? Scope(string? scope) => scope switch
    {
        null or "" => false,
        _ when CompanyScope.Equals(scope, StringComparison.OrdinalIgnoreCase) => true,
        _ when "me".Equals(scope, StringComparison.OrdinalIgnoreCase) => false,
        _ => null,
    };

    private static IResult Refuse(string because, int status) =>
        Results.Json(new ErrorDto(because), ServerJsonContext.Default.ErrorDto, statusCode: status);
}
