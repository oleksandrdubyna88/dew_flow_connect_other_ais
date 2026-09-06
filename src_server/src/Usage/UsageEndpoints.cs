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

            var company = CompanyScope.Equals(scope, StringComparison.OrdinalIgnoreCase);
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

    private static IResult Refuse(string because, int status) =>
        Results.Json(new ErrorDto(because), ServerJsonContext.Default.ErrorDto, statusCode: status);
}
