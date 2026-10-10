namespace CoaiServer;

/// <summary>Whose spending a request is asking about.</summary>
/// <remarks>
/// A named type rather than a nullable bool. The tri-state version worked — false for me, true for
/// company, null for neither — and read as a puzzle at the call site, which is where the next person
/// adds a third scope. (Two reviewers, code round.)
/// </remarks>
public enum UsageScope
{
    Me,
    Company,
}

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
            var now = DateTimeOffset.UtcNow;

            if (UsageWindow.Range(window, now) is not { } range)
            {
                // Named, not defaulted. Falling back to "today" would answer a question nobody asked
                // and would look like the data was wrong rather than the request.
                return Refuse(
                    $"'{window}' is not a window. Use one of: {string.Join(", ", UsageWindow.Names)}",
                    StatusCodes.Status400BadRequest);
            }

            if (Scope(scope) is not { } asked)
            {
                // `scope=compnay` used to answer 200 with the PERSONAL total. An admin reading that
                // as the company's would make a spending decision from one person's numbers.
                // (codex, code round.)
                return Refuse(
                    $"'{scope}' is not a scope. Use 'me' or '{CompanyScope}'.",
                    StatusCodes.Status400BadRequest);
            }

            if (asked == UsageScope.Company && AdminOnly.RefusalFor(caller, $"scope={CompanyScope}") is { } refused)
            {
                // The ONE admin decision, shared with /api/people — 403 rather than 404, naming the
                // setting, for the reasons AdminOnly gives. Only this branch asks it: `me` is everyone's.
                return refused;
            }

            return Results.Json(Answer(usage, range, caller, asked, now), ServerJsonContext.Default.UsageDto);
        }).RequireCaller(gate);
    }

    /// <summary>ONE scan of the ledger per request, however many ranges the answer is built from.</summary>
    /// <remarks>
    /// The company answer carries a summary over the selected window AND a chart over its own thirty
    /// days (<see cref="UsageDaily"/>). The reader keeps only what is inside the range it is handed, so
    /// a chart built from a <c>today</c> read would show one bar — it is handed the union of the two,
    /// and each half applies its own range to what came back. The personal answer has no chart and
    /// reads its window alone.
    /// </remarks>
    private static UsageDto Answer(UsageReader usage, UsageRange range, Caller caller, UsageScope scope, DateTimeOffset nowUtc)
    {
        if (scope == UsageScope.Me)
        {
            return Mine(usage.Read(range), range, caller);
        }

        var chart = UsageDaily.Range(nowUtc);

        return Company(usage.Read(range.Union(chart)), range, chart);
    }

    /// <summary>
    /// The admin's answer: everyone, each person, the kinds — over the window — and the chart over its own days.
    /// </summary>
    /// <remarks>
    /// <c>unreadableLines</c> is reported ONLY to an admin. A single torn line from months ago would
    /// otherwise appear on every person's own page for ever, telling them their record is damaged —
    /// about something they cannot see, cannot fix and did not cause. An operator can act on it; a
    /// reviewer looking at their own week cannot. (Two reviewers, plan round.)
    /// </remarks>
    private static UsageDto Company(UsageScan scan, UsageRange range, UsageRange chart)
    {
        var window = scan.Lines.Where(l => range.Contains(l.AtUtc)).ToList();

        return new UsageDto(
            range.FromUtc,
            range.ToUtc,
            CompanyScope,
            UsageTotals.ByVendor(window),
            UsageTotals.ByPerson(window),
            scan.Unreadable,
            UsageTotals.ByKind(window),
            UsageDaily.Over(scan.Lines, chart));
    }

    /// <summary>A person's own answer: their vendors and their kinds, nobody else's, and no chart.</summary>
    private static UsageDto Mine(UsageScan scan, UsageRange range, Caller caller) =>
        new(
            range.FromUtc,
            range.ToUtc,
            "me",
            UsageTotals.ByVendorFor(scan.Lines, caller.Email),
            [],
            null,
            // Scoped the same way the vendor rows above it are: a person's own conversations,
            // never the company's. The two lists must answer about the same lines or the small
            // block would contradict the table beside it.
            UsageTotals.ByKindFor(scan.Lines, caller.Email));

    /// <summary>The scope asked for, or null when the value is neither of them.</summary>
    /// <remarks>
    /// Absent means <see cref="UsageScope.Me"/> — a default, not a fallback. A VALUE that is neither
    /// is a typo, and answering it with somebody's personal total is how the wrong number ends up in
    /// a decision somebody makes about money.
    /// </remarks>
    private static UsageScope? Scope(string? scope) => scope switch
    {
        null or "" => UsageScope.Me,
        _ when CompanyScope.Equals(scope, StringComparison.OrdinalIgnoreCase) => UsageScope.Company,
        _ when "me".Equals(scope, StringComparison.OrdinalIgnoreCase) => UsageScope.Me,
        _ => null,
    };

    private static IResult Refuse(string because, int status) => Refusal.Json(because, status);
}
