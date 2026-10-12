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

/// <summary>What a usage request asked: the window the answer's top is over, whose spending, and the windows besides.</summary>
/// <param name="Windows">The <c>?windows=</c> list, in the order asked; empty when none was asked (story 5.1).</param>
public sealed record UsageAsk(UsageRange Range, UsageScope Scope, IReadOnlyList<NamedRange> Windows);

/// <summary>A usage query, read: what it asked, or the refusal that names what it got wrong.</summary>
public abstract record UsageRequest
{
    private UsageRequest()
    {
    }

    public sealed record Asked(UsageAsk Ask) : UsageRequest;

    public sealed record Refused(IResult Answer) : UsageRequest;
}

/// <summary>What the team is spending, to the person who spent it and to an admin.</summary>
public static class UsageEndpoints
{
    /// <summary>The scope name that asks for everybody, and needs to be an admin.</summary>
    private const string CompanyScope = "company";

    public static void MapUsageEndpoints(this WebApplication app, UsageReader usage, CallerFilter gate)
    {
        app.MapGet("/api/usage", (HttpContext ctx, string? window, string? scope, string? windows) =>
        {
            var caller = ctx.CallerOf();
            var now = DateTimeOffset.UtcNow;

            return Request(window, scope, windows, now) switch
            {
                UsageRequest.Refused refused => refused.Answer,
                // The ONE admin decision, shared with /api/people — 403 rather than 404, naming the setting, for the
                // reasons AdminOnly gives. Only the company scope asks it: `me` is everyone's.
                UsageRequest.Asked { Ask.Scope: UsageScope.Company } when AdminOnly.RefusalFor(caller, $"scope={CompanyScope}") is { } refused => refused,
                UsageRequest.Asked asked => Results.Json(Answer(usage.Read, asked.Ask, caller, now), ServerJsonContext.Default.UsageDto),
                _ => throw new InvalidOperationException("a usage request is asked or refused"),
            };
        }).RequireCaller(gate);
    }

    /// <summary>What the query asked, or the 400 that names what it got wrong.</summary>
    private static UsageRequest Request(string? window, string? scope, string? windows, DateTimeOffset now)
    {
        // Named, not defaulted. Falling back to "today" would answer a question nobody asked and would look like the
        // data was wrong rather than the request.
        if (UsageWindow.Range(window, now) is not { } range)
        {
            return new UsageRequest.Refused(NotAWindow(window));
        }

        // The same refusal for one name of several: a list with one typo in it is a request fault, and answering the rest
        // would hand back fewer columns than were asked with no word why (story 5.1).
        var several = UsageWindow.Several(windows, now);
        if (several is WindowsAsked.Refused unknown)
        {
            return new UsageRequest.Refused(NotAWindow(unknown.Name));
        }

        // `scope=compnay` used to answer 200 with the PERSONAL total. An admin reading that as the company's would make a
        // spending decision from one person's numbers. (codex, code round.)
        return Scope(scope) is { } asked
            ? new UsageRequest.Asked(new UsageAsk(range, asked, ((WindowsAsked.Read)several).Windows))
            : new UsageRequest.Refused(Refuse($"'{scope}' is not a scope. Use 'me' or '{CompanyScope}'.", StatusCodes.Status400BadRequest));
    }

    private static IResult NotAWindow(string? name) =>
        Refuse($"'{name}' is not a window. Use one of: {string.Join(", ", UsageWindow.Names)}", StatusCodes.Status400BadRequest);

    /// <summary>ONE scan of the ledger per request, however many ranges the answer is built from.</summary>
    /// <remarks>
    /// <para>The company answer carries a summary over the selected window AND a chart over its own thirty days
    /// (<see cref="UsageDaily"/>); either answer may carry several windows besides (story 5.1). The reader keeps only what
    /// is inside the range it is handed, so a chart built from a <c>today</c> read would show one bar — it is handed the
    /// union of every range the answer needs, and each part applies its own range to what came back.</para>
    /// <para>The union of disjoint ranges costs the lines between them, which is cheaper than a second parse of the whole
    /// file: the reader parses every line of the ledger whatever range it is handed (<see cref="UsageReader"/>).</para>
    /// </remarks>
    internal static UsageDto Answer(Func<UsageRange, UsageScan> read, UsageAsk ask, Caller caller, DateTimeOffset nowUtc)
    {
        if (ask.Scope == UsageScope.Me)
        {
            var mine = read(Spanning(ask, ask.Range, nowUtc));

            return Mine(mine, ask.Range, caller) with { Windows = MyWindows(mine, ask.Windows, caller), Gauges = UsageGauges.Of(mine, ask.Windows, nowUtc) };
        }

        var chart = UsageDaily.Range(nowUtc);
        var scan = read(Spanning(ask, ask.Range.Union(chart), nowUtc));

        return Company(scan, ask.Range, chart) with { Windows = CompanyWindows(scan, ask.Windows), Gauges = UsageGauges.Of(scan, ask.Windows, nowUtc) };
    }

    /// <summary>
    /// The range one read must cover: the answer's own, every window asked — and, when windows were asked, the gauge
    /// derivation's own reach back (story 6.2), so its figure never depends on which windows the client named.
    /// </summary>
    private static UsageRange Spanning(UsageAsk ask, UsageRange own, DateTimeOffset nowUtc) =>
        ask.Windows.Aggregate(
            ask.Windows.Count == 0 ? own : own.Union(UsageGauges.Range(nowUtc)),
            (span, window) => span.Union(window.Range));

    /// <summary>A person's windows — their own vendors in each, nobody else's, and no people list.</summary>
    private static IReadOnlyList<UsageWindowDto>? MyWindows(UsageScan scan, IReadOnlyList<NamedRange> windows, Caller caller) =>
        Each(windows, window => new UsageWindowDto(
            window.Name, window.Range.FromUtc, window.Range.ToUtc,
            UsageTotals.ByVendorFor(Inside(scan, window), caller.Email), []));

    /// <summary>An admin's windows — everyone's vendors, and each person's, in each.</summary>
    private static IReadOnlyList<UsageWindowDto>? CompanyWindows(UsageScan scan, IReadOnlyList<NamedRange> windows) =>
        Each(windows, window =>
        {
            var lines = Inside(scan, window);

            return new UsageWindowDto(
                window.Name, window.Range.FromUtc, window.Range.ToUtc, UsageTotals.ByVendor(lines), UsageTotals.ByPerson(lines));
        });

    /// <summary>One answer per window — or NONE, so a request that asked no windows carries no key (today's shape).</summary>
    private static IReadOnlyList<UsageWindowDto>? Each(IReadOnlyList<NamedRange> windows, Func<NamedRange, UsageWindowDto> answer) =>
        windows.Count == 0 ? null : [.. windows.Select(answer)];

    private static List<UsageLine> Inside(UsageScan scan, NamedRange window) =>
        [.. scan.Lines.Where(l => window.Range.Contains(l.AtUtc))];

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
    /// <remarks>
    /// The scan may reach further back than the window when several windows were asked, so the window is applied here, as
    /// <see cref="Company"/> always applied it.
    /// </remarks>
    private static UsageDto Mine(UsageScan scan, UsageRange range, Caller caller)
    {
        var window = scan.Lines.Where(l => range.Contains(l.AtUtc)).ToList();

        return new(
            range.FromUtc,
            range.ToUtc,
            "me",
            UsageTotals.ByVendorFor(window, caller.Email),
            [],
            null,
            // Scoped the same way the vendor rows above it are: a person's own conversations,
            // never the company's. The two lists must answer about the same lines or the small
            // block would contradict the table beside it.
            UsageTotals.ByKindFor(window, caller.Email));
    }

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
